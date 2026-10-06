using System.Collections.Immutable;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using GitHub.Copilot;
using StudyReportEvaluator.App.Logging;
using StudyReportEvaluator.App.Usage;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;

namespace StudyReportEvaluator.App.Copilot;

public sealed class EphemeralEvaluationRunnerOptions
{
    // Owner decision "follow the SDK's 60 s": the AI response wait is the SDK SendAndWaitAsync default (60 s, the app passes
    // no timeout). This outer bound also covers start, auth and session create, so it must not preempt that wait. Re-check on SDK upgrade.
    public static TimeSpan DefaultAttemptTimeout { get; } = TimeSpan.FromSeconds(120);

    public static TimeSpan DefaultCleanupTimeout { get; } = TimeSpan.FromSeconds(15);

    public const int MinimumMaxConcurrency = 1;
    public const int DefaultMaxConcurrency = 8;
    public const int MaximumMaxConcurrency = 16;

    public EphemeralEvaluationRunnerOptions(
        TimeSpan? attemptTimeout = null,
        TimeSpan? cleanupTimeout = null,
        int maxConcurrency = DefaultMaxConcurrency,
        IEvaluationConcurrencyObserver? concurrencyObserver = null,
        IRetryDelayProvider? retryDelayProvider = null,
        string? contextTier = null)
    {
        AttemptTimeout = attemptTimeout ?? DefaultAttemptTimeout;
        CleanupTimeout = cleanupTimeout ?? DefaultCleanupTimeout;
        RetryAndCleanupCoordinator.ValidateFiniteTimeout(AttemptTimeout, nameof(attemptTimeout));
        RetryAndCleanupCoordinator.ValidateFiniteTimeout(CleanupTimeout, nameof(cleanupTimeout));

        if (maxConcurrency is < MinimumMaxConcurrency or > MaximumMaxConcurrency)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrency),
                "Evaluation concurrency must be between 1 and 16.");
        }

        MaxConcurrency = maxConcurrency;
        ConcurrencyObserver = concurrencyObserver;
        RetryDelayProvider = retryDelayProvider;
        ContextTier = ModelOptionPolicy.ToSdkContextTier(contextTier);
    }

    public TimeSpan AttemptTimeout { get; }

    public TimeSpan CleanupTimeout { get; }

    public int MaxConcurrency { get; }

    public IEvaluationConcurrencyObserver? ConcurrencyObserver { get; }

    internal IRetryDelayProvider? RetryDelayProvider { get; }

    public ContextTier? ContextTier { get; }
}

public interface IEphemeralCopilotTransportFactory
{
    IEphemeralCopilotTransport Create();
}

public interface IEphemeralCopilotTransport : IAsyncDisposable
{
    void SetUsageAttemptContext(int attemptNumber, Guid operationId) { }

    void CompleteUsageAttempt(UsageAttemptOutcome outcome) { }

    Task StartAsync(CancellationToken cancellationToken);

    Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken);

    Task<IEphemeralCopilotSession> CreateSessionAsync(
        SessionConfig config,
        CancellationToken cancellationToken);

    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public interface IEphemeralCopilotSession : IAsyncDisposable
{
    string SessionId { get; }

    Task SendAndWaitAsync(
        MessageOptions options,
        CancellationToken cancellationToken);

    Task<EvaluationTokenUsage> GetUsageAsync(CancellationToken cancellationToken) =>
        Task.FromResult(EvaluationTokenUsage.Unavailable);

    Task AbortAsync(CancellationToken cancellationToken);
}

public sealed class EphemeralEvaluationRunner
{
    private const string SessionIdPrefix = "a03-";

    private readonly IEphemeralCopilotTransportFactory _transportFactory;
    private readonly EvaluationSchemaFactory _schemaFactory;
    private readonly RetryAndCleanupCoordinator _coordinator;
    private readonly EphemeralEvaluationRunnerOptions _options;

    public EphemeralEvaluationRunner()
        : this(
            new SdkEphemeralCopilotTransportFactory(new CopilotClientFactory()),
            options: null,
            logger: null)
    {
    }

    public EphemeralEvaluationRunner(
        IEphemeralCopilotTransportFactory transportFactory,
        EphemeralEvaluationRunnerOptions? options = null,
        SafeLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(transportFactory);
        _transportFactory = transportFactory;
        _schemaFactory = new EvaluationSchemaFactory();
        _coordinator = new RetryAndCleanupCoordinator(logger);
        _options = options ?? new EphemeralEvaluationRunnerOptions();
    }

    public EphemeralEvaluationRunnerOptions Options => _options;

    public Task<EphemeralEvaluationResult> EvaluateAsync(
        SafeEvaluationPayload payload,
        string modelId,
        CancellationToken cancellationToken = default) =>
        EvaluateAsync(payload, modelId, reasoningEffort: null, cancellationToken);

    public Task<EphemeralEvaluationResult> EvaluateAsync(
        SafeEvaluationPayload payload,
        string modelId,
        string? reasoningEffort,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateModelId(modelId);
        ValidateReasoningEffort(reasoningEffort);

        return _coordinator.ExecuteAsync(
            _ => CreateAttempt(payload, modelId, reasoningEffort),
            _options.AttemptTimeout,
            _options.CleanupTimeout,
            _options.MaxConcurrency,
            cancellationToken,
            _options.ConcurrencyObserver,
            _options.RetryDelayProvider);
    }

    private IEphemeralEvaluationAttempt CreateAttempt(
        SafeEvaluationPayload payload,
        string modelId,
        string? reasoningEffort)
    {
        IEphemeralCopilotTransport transport = _transportFactory.Create()
            ?? throw new InvalidOperationException("The transport factory returned no transport.");
        return new CopilotEvaluationAttempt(
            transport,
            _schemaFactory,
            payload,
            modelId,
            reasoningEffort,
            CreateSessionId(),
            _options.ContextTier);
    }

    private static string CreateSessionId() =>
        $"{SessionIdPrefix}{Guid.NewGuid():N}";

    internal static void ValidateModelId(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        if (modelId.Length > 256
            || !string.Equals(modelId, modelId.Trim(), StringComparison.Ordinal)
            || modelId.Any(char.IsControl))
        {
            throw new ArgumentException("The model ID is invalid.", nameof(modelId));
        }
    }

    internal static void ValidateReasoningEffort(string? reasoningEffort)
    {
        if (reasoningEffort is not null
            && !ReasoningEffortPolicy.IsSafeReasoningEffort(reasoningEffort))
        {
            throw new ArgumentException("The reasoning effort value is invalid.", nameof(reasoningEffort));
        }
    }
}

internal sealed class CopilotEvaluationAttempt : IEphemeralEvaluationAttempt
{
    private readonly IEphemeralCopilotTransport _transport;
    private readonly SubmitQuantificationTool _collector;
    private readonly SessionConfig _sessionConfig;
    private readonly MessageOptions _messageOptions;
    private IEphemeralCopilotSession? _session;
    private string? _createdSessionId;
    private EvaluationTokenUsage _tokenUsage = EvaluationTokenUsage.Unavailable;

    internal CopilotEvaluationAttempt(
        IEphemeralCopilotTransport transport,
        EvaluationSchemaFactory schemaFactory,
        SafeEvaluationPayload payload,
        string modelId,
        string? reasoningEffort,
        string sessionId,
        ContextTier? contextTier = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(schemaFactory);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        _transport = transport;
        _sessionConfig = schemaFactory.CreateSessionConfig(payload, out _collector);
        _sessionConfig.SessionId = sessionId;
        _sessionConfig.Model = modelId;
        _sessionConfig.ReasoningEffort = reasoningEffort;
        _sessionConfig.ContextTier = contextTier;
        _messageOptions = new MessageOptions
        {
            Prompt = payload.RenderedPrompt,
            Attachments = [],
            DisplayPrompt = null,
            RequestHeaders = null,
        };
        SessionId = sessionId;
    }

    public string SessionId { get; }

    public EvaluationTokenUsage TokenUsage => Volatile.Read(ref _tokenUsage);

    public void SetUsageAttemptContext(int attemptNumber, Guid operationId) =>
        _transport.SetUsageAttemptContext(attemptNumber, operationId);

    public void CompleteUsageAttempt(UsageAttemptOutcome outcome) =>
        _transport.CompleteUsageAttempt(outcome);

    public async Task<QuantificationResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _transport.StartAsync(cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        if (!await _transport.IsAuthenticatedAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new EvaluationAuthenticationException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        IEphemeralCopilotSession session = await _transport
            .CreateSessionAsync(_sessionConfig, cancellationToken)
            .ConfigureAwait(false);
        _session = session
            ?? throw new EvaluationFatalException();
        _createdSessionId = session.SessionId;
        if (!string.Equals(SessionId, session.SessionId, StringComparison.Ordinal))
        {
            throw new EvaluationFatalException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        await session.SendAndWaitAsync(_messageOptions, cancellationToken).ConfigureAwait(false);
        EvaluationTokenUsage usage;
        try
        {
            usage = await session
                .GetUsageAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            usage = EvaluationTokenUsage.Unavailable;
        }

        Volatile.Write(ref _tokenUsage, usage ?? EvaluationTokenUsage.Unavailable);

        if (_collector.TryGetAcceptedResult(out QuantificationResult? acceptedResult))
        {
            return acceptedResult;
        }

        throw new EvaluationSchemaException();
    }

    public Task AbortAsync(CancellationToken cancellationToken)
    {
        IEphemeralCopilotSession? session = Volatile.Read(ref _session);
        return session is null
            ? Task.CompletedTask
            : session.AbortAsync(cancellationToken);
    }

    public async Task DisposeSessionAsync(CancellationToken cancellationToken)
    {
        IEphemeralCopilotSession? session = Volatile.Read(ref _session);
        if (session is not null)
        {
            await session.DisposeAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteSessionAsync(CancellationToken cancellationToken)
    {
        bool succeeded = true;
        string? createdSessionId = Volatile.Read(ref _createdSessionId);
        if (createdSessionId is not null)
        {
            try
            {
                await _transport
                    .DeleteSessionAsync(createdSessionId, cancellationToken)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                succeeded = false;
            }
        }

        try
        {
            await _transport
                .StopAsync(cancellationToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            succeeded = false;
        }

        try
        {
            await _transport
                .DisposeAsync()
                .AsTask()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            succeeded = false;
        }

        if (!succeeded)
        {
            throw new EvaluationCleanupException();
        }
    }
}

internal sealed class SdkEphemeralCopilotTransportFactory : IEphemeralCopilotTransportFactory
{
    private readonly ICopilotClientFactory _clientFactory;
    private readonly SharedCopilotClientPool? _sharedClientPool;
    private readonly JobUsageTracker? _usageTracker;
    private readonly UsageOperation _operation;

    internal SdkEphemeralCopilotTransportFactory(
        ICopilotClientFactory clientFactory,
        JobUsageTracker? usageTracker = null,
        UsageOperation operation = UsageOperation.Normal)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = clientFactory;
        _usageTracker = usageTracker;
        _operation = operation;
    }

    internal SdkEphemeralCopilotTransportFactory(
        SharedCopilotClientPool sharedClientPool,
        JobUsageTracker? usageTracker = null,
        UsageOperation operation = UsageOperation.Normal)
    {
        _sharedClientPool = sharedClientPool ?? throw new ArgumentNullException(nameof(sharedClientPool));
        _clientFactory = sharedClientPool.ClientFactory;
        _usageTracker = usageTracker;
        _operation = operation;
    }

    public IEphemeralCopilotTransport Create() =>
        new SdkEphemeralCopilotTransport(_clientFactory, _usageTracker, _operation, _sharedClientPool);
}

internal sealed class SharedCopilotClientPool : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<CopilotClient> _retired = [];
    private SharedCopilotClientState? _state;
    private bool _disposed;

    internal SharedCopilotClientPool(ICopilotClientFactory clientFactory)
    {
        ClientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    internal ICopilotClientFactory ClientFactory { get; }

    internal async Task<CopilotClient> GetStartedClientAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SharedCopilotClientState? state = Volatile.Read(ref _state);
        if (state is { Started: true })
        {
            return state.Client;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            state = _state;
            if (state is { Started: true })
            {
                return state.Client;
            }

            CopilotClientCreationResult creation = await ClientFactory
                .CreateAsync(cancellationToken)
                .ConfigureAwait(false);
            if (creation.Status == CopilotClientCreationStatus.Cancelled
                && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (creation.Status != CopilotClientCreationStatus.Created
                || creation.Client is null)
            {
                throw new EvaluationFatalException();
            }

            try
            {
                await creation.Client.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await DisposeClientAsync(creation.Client).ConfigureAwait(false);
                throw;
            }

            state = new SharedCopilotClientState(creation.Client) { Started = true };
            Volatile.Write(ref _state, state);
            return state.Client;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken)
    {
        SharedCopilotClientState state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (state.Authenticated is bool cachedAuthenticated)
        {
            return cachedAuthenticated;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (state.Authenticated is bool authenticated)
            {
                return authenticated;
            }

            GetAuthStatusResponse response = await state.Client
                .GetAuthStatusAsync(cancellationToken)
                .ConfigureAwait(false);
            state.Authenticated = response.IsAuthenticated;
            return response.IsAuthenticated;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Detaches a failed client so later attempts start a new CLI process. The old client is disposed only when the
    // run ends, because other in-flight attempts may still need it to abort and delete their sessions.
    internal async Task InvalidateAsync(CopilotClient failedClient)
    {
        ArgumentNullException.ThrowIfNull(failedClient);
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_state is { } state && ReferenceEquals(state.Client, failedClient))
            {
                Volatile.Write(ref _state, null);
                _retired.Add(failedClient);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        SharedCopilotClientState? state = Interlocked.Exchange(ref _state, null);
        if (state is not null)
        {
            await DisposeClientAsync(state.Client).ConfigureAwait(false);
        }

        foreach (CopilotClient retired in _retired)
        {
            await DisposeClientAsync(retired).ConfigureAwait(false);
        }

        _retired.Clear();
        _gate.Dispose();
    }

    private async Task<SharedCopilotClientState> GetStateAsync(CancellationToken cancellationToken)
    {
        CopilotClient client = await GetStartedClientAsync(cancellationToken).ConfigureAwait(false);
        SharedCopilotClientState? state = Volatile.Read(ref _state);
        return state is not null && ReferenceEquals(state.Client, client)
            ? state
            : throw new EvaluationNetworkException();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SharedCopilotClientPool));
        }
    }

    private static async ValueTask DisposeClientAsync(CopilotClient client)
    {
        try { await client.StopAsync().ConfigureAwait(false); }
        catch { /* Best effort invalidation. */ }
        try { await client.DisposeAsync().ConfigureAwait(false); }
        catch { /* Best effort invalidation. */ }
    }

    private sealed class SharedCopilotClientState(CopilotClient client)
    {
        internal CopilotClient Client { get; } = client;

        internal bool Started { get; init; }

        internal bool? Authenticated { get; set; }
    }
}

internal sealed class SdkEphemeralCopilotTransport : IEphemeralCopilotTransport
{
    private readonly ICopilotClientFactory _clientFactory;
    private readonly JobUsageTracker? _usageTracker;
    private readonly UsageOperation _operation;
    private readonly SharedCopilotClientPool? _sharedClientPool;
    private CopilotClient? _client;
    private SdkEphemeralCopilotSession? _usageSession;
    private int _usageAttemptNumber;
    private Guid _usageOperationId;

    internal SdkEphemeralCopilotTransport(
        ICopilotClientFactory clientFactory,
        JobUsageTracker? usageTracker = null,
        UsageOperation operation = UsageOperation.Normal,
        SharedCopilotClientPool? sharedClientPool = null)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = clientFactory;
        _usageTracker = usageTracker;
        _operation = operation;
        _sharedClientPool = sharedClientPool;
    }

    public void SetUsageAttemptContext(int attemptNumber, Guid operationId)
    {
        _usageAttemptNumber = attemptNumber;
        _usageOperationId = operationId;
    }

    public void CompleteUsageAttempt(UsageAttemptOutcome outcome) =>
        Volatile.Read(ref _usageSession)?.CompleteUsageAttempt(
            _usageAttemptNumber, _usageOperationId, outcome);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_sharedClientPool is not null)
        {
            _client = await TranslateAsync(
                () => _sharedClientPool.GetStartedClientAsync(cancellationToken)).ConfigureAwait(false);
            return;
        }

        CopilotClientCreationResult creation = await _clientFactory
            .CreateAsync(cancellationToken)
            .ConfigureAwait(false);
        if (creation.Status == CopilotClientCreationStatus.Cancelled
            && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (creation.Status != CopilotClientCreationStatus.Created
            || creation.Client is null)
        {
            throw new EvaluationFatalException();
        }

        _client = creation.Client;
        await TranslateAsync(
            () => creation.Client.StartAsync(cancellationToken)).ConfigureAwait(false);
    }

    public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken) =>
        TranslateAsync(async () =>
        {
            if (_sharedClientPool is not null)
            {
                return await _sharedClientPool
                    .IsAuthenticatedAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            CopilotClient client = GetClient();
            GetAuthStatusResponse response = await client
                .GetAuthStatusAsync(cancellationToken)
                .ConfigureAwait(false);
            return response.IsAuthenticated;
        });

    public Task<IEphemeralCopilotSession> CreateSessionAsync(
        SessionConfig config,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        return TranslateAsync<IEphemeralCopilotSession>(async () =>
        {
            CopilotClient client = GetClient();
            if (string.Equals(config.Model, "auto", StringComparison.Ordinal))
            {
                config.ReasoningEffort = null;
            }

            CopilotSession session = await client
                .CreateSessionAsync(config, cancellationToken)
                .ConfigureAwait(false);
            SdkEphemeralCopilotSession usageSession = new(
                session,
                _usageTracker,
                _operation,
                config.Model,
                config.ReasoningEffort,
                onProcessFailure: () => InvalidateSharedClientAsync(client));
            Volatile.Write(ref _usageSession, usageSession);
            return usageSession;
        }, invalidateSharedClient: true);
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        CopilotClient client = GetClient();
        return TranslateAsync(
            () => client.DeleteSessionAsync(sessionId, cancellationToken),
            invalidateSharedClient: true);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CopilotClient? client = Volatile.Read(ref _client);
        if (client is not null)
        {
            if (_sharedClientPool is null)
            {
                await client.StopAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        CopilotClient? client = Interlocked.Exchange(ref _client, null);
        if (client is not null && _sharedClientPool is null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private CopilotClient GetClient() =>
        Volatile.Read(ref _client)
        ?? throw new EvaluationFatalException();

    internal static string? ResolveReasoningEffort(
        IEnumerable<ModelInfo>? models,
        string? modelId,
        string? preferredEffort = null) =>
        ReasoningEffortPolicy.ResolveReasoningEffort(models, modelId, preferredEffort);

    private async Task TranslateAsync(Func<Task> operation, bool invalidateSharedClient = false)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw new EvaluationAttemptTimeoutException();
        }
        catch (Exception exception) when (IsNetworkException(exception))
        {
            if (invalidateSharedClient && exception is IOException)
            {
                await InvalidateSharedClientAsync().ConfigureAwait(false);
            }

            throw new EvaluationNetworkException();
        }
        catch (EvaluationAttemptException)
        {
            throw;
        }
        catch
        {
            throw new EvaluationFatalException();
        }
    }

    private async Task<T> TranslateAsync<T>(Func<Task<T>> operation, bool invalidateSharedClient = false)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw new EvaluationAttemptTimeoutException();
        }
        catch (Exception exception) when (IsNetworkException(exception))
        {
            if (invalidateSharedClient && exception is IOException)
            {
                await InvalidateSharedClientAsync().ConfigureAwait(false);
            }

            throw new EvaluationNetworkException();
        }
        catch (EvaluationAttemptException)
        {
            throw;
        }
        catch
        {
            throw new EvaluationFatalException();
        }
    }

    private static bool IsNetworkException(Exception exception) =>
        exception is IOException
            or HttpRequestException
            or SocketException
            or WebSocketException;

    private Task InvalidateSharedClientAsync(CopilotClient? failedClient = null)
    {
        CopilotClient? client = failedClient ?? Volatile.Read(ref _client);
        return _sharedClientPool is not null && client is not null
            ? _sharedClientPool.InvalidateAsync(client)
            : Task.CompletedTask;
    }
}

internal sealed class SdkEphemeralCopilotSession : IEphemeralCopilotSession
{
    private readonly CopilotSession _session;
    private readonly object _usageGate = new();
    private readonly JobUsageTracker? _usageTracker;
    private readonly UsageOperation _operation;
    private readonly IDisposable? _usageSubscription;
    private readonly IDisposable? _errorSubscription;
    private readonly Func<Task>? _onProcessFailure;
    private readonly HashSet<Guid> _eventIds = [];
    private readonly List<UsageMetrics> _eventMetrics = [];
    private readonly Dictionary<string, List<UsageMetrics>> _modelEvents = new(StringComparer.Ordinal);
    private readonly string? _requestedModelKey;
    private readonly bool? _requestedModelIsAuto;
    private readonly string? _requestedReasoningEffort;
    private ImmutableArray<ModelUsageSnapshot> _models = [];
    private bool _modelsTruncated;
    private Guid _attemptId;
    private long _usageRevision;
    private bool _sendStarted;
    private bool _sendPending;
    private bool _closed;
    private bool _finalCaptured;
    private bool _invalidUsage;
    private bool _partialUsage = true;
    private UsageSource _usageSource;
    private UsageObservationStatus _usageStatus;
    private UsageMetrics _observedUsage = new();
    private ImmutableDictionary<UsageMetric, MetricProvenance> _metricProvenance
        = ImmutableDictionary<UsageMetric, MetricProvenance>.Empty;
    private ModelCostComparison _modelCostComparison;
    private EvaluationTokenUsage _legacyUsage = EvaluationTokenUsage.Unavailable;
    private Task<EvaluationTokenUsage>? _captureTask;
    private Task? _abortTask;
    private Task? _disposeTask;
    private EvaluationAttemptException? _sessionErrorException;

    internal SdkEphemeralCopilotSession(
        CopilotSession session,
        JobUsageTracker? usageTracker = null,
        UsageOperation operation = UsageOperation.Normal,
        string? requestedModel = null,
        string? requestedReasoningEffort = null,
        Func<Task>? onProcessFailure = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _usageTracker = usageTracker;
        _operation = operation;
        _onProcessFailure = onProcessFailure;
        _requestedReasoningEffort = requestedReasoningEffort;
        _requestedModelKey = requestedModel is null ? null : ModelUsageSnapshot.CreateModelKey(requestedModel);
        _requestedModelIsAuto = requestedModel is null ? null : string.Equals(requestedModel, "auto", StringComparison.Ordinal);
        if (usageTracker is not null)
        {
            try { _usageSubscription = session.On<AssistantUsageEvent>(OnUsage); }
            catch { _usageStatus = UsageObservationStatus.EventsUnavailable; }
        }

        try { _errorSubscription = session.On<SessionErrorEvent>(OnSessionError); }
        catch { /* Classification falls back to the SDK exception message. */ }
    }

    public string SessionId => _session.SessionId;

    internal void CompleteUsageAttempt(int attemptNumber, Guid operationId, UsageAttemptOutcome outcome)
    {
        Guid attemptId;
        lock (_usageGate)
        {
            attemptId = _attemptId;
        }

        // Disposal seals numeric observations, not the evaluation outcome. No send means no usage attempt.
        // Do not hold the session gate while calling the independent job tracker.
        if (attemptId == Guid.Empty) { return; }
        try { _usageTracker?.RecordAttemptOutcome(attemptId, attemptNumber, operationId, outcome); }
        catch { /* Outcome logging must never replace the evaluation or cleanup result. */ }
    }

    public async Task SendAndWaitAsync(
        MessageOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        bool ownsSend = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_usageGate)
            {
                if (_sendStarted || _closed || _disposeTask is not null)
                {
                    throw new EvaluationFatalException();
                }

                _sendStarted = true;
                _sendPending = true;
                ownsSend = true;
                try { _attemptId = _usageTracker?.BeginAttempt(_operation) ?? Guid.Empty; }
                catch { /* Observation must not turn an AI send into a retry. */ }
                PublishUsage(finished: false);
            }

            _ = await _session
                .SendAndWaitAsync(
                    options,
                    timeout: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            throw new EvaluationAttemptTimeoutException();
        }
        catch (Exception exception) when (
            exception is IOException
                or HttpRequestException
                or SocketException
                or WebSocketException)
        {
            if (exception is IOException)
            {
                await NotifyProcessFailureAsync().ConfigureAwait(false);
            }

            throw new EvaluationNetworkException();
        }
        catch (InvalidOperationException exception) when (TryClassifySessionError(exception, out EvaluationAttemptException? sessionException))
        {
            throw sessionException;
        }
        catch (EvaluationAttemptException)
        {
            throw;
        }
        catch
        {
            throw new EvaluationFatalException();
        }
        finally
        {
            if (ownsSend)
            {
                lock (_usageGate) { _sendPending = false; }
            }
        }
    }

    // The CLI reports an upstream connect/timeout failure as a session error (errorType "query") whose
    // message carries reqwest's transport wording; HTTP status failures use different wording.
    internal static bool IsTransportSessionError(Exception exception) =>
        exception is InvalidOperationException
        && exception.Message.Contains("error sending request for url", StringComparison.Ordinal);

    private bool TryClassifySessionError(
        InvalidOperationException exception,
        out EvaluationAttemptException failure)
    {
        EvaluationAttemptException? observed = Volatile.Read(ref _sessionErrorException);
        if (observed is not null)
        {
            failure = observed;
            return true;
        }

        string message = exception.Message;
        if (ContainsAny(
                message,
                "rate_limit",
                "rate limited",
                "rate_limited",
                "user_model_rate_limited",
                "user_global_rate_limited",
                "integration_rate_limited"))
        {
            failure = new EvaluationRateLimitException();
            return true;
        }

        if (ContainsAny(
                message,
                "quota",
                "quota_exceeded",
                "session_quota_exceeded",
                "billing_not_configured"))
        {
            failure = new EvaluationQuotaExhaustedException();
            return true;
        }

        if (IsTransportSessionError(exception))
        {
            failure = new EvaluationNetworkException();
            return true;
        }

        failure = new EvaluationFatalException();
        return false;
    }

    public Task AbortAsync(CancellationToken cancellationToken)
    {
        lock (_usageGate)
        {
            return _abortTask ??= _session.AbortAsync(cancellationToken);
        }
    }

    private void OnSessionError(SessionErrorEvent error)
    {
        SessionErrorData? data = error.Data;
        if (data is null)
        {
            return;
        }

        string? errorType = data.ErrorType;
        string? errorCode = data.ErrorCode;
        if (string.Equals(errorType, "rate_limit", StringComparison.OrdinalIgnoreCase)
            || ContainsAny(
                errorCode,
                "user_weekly_rate_limited",
                "user_global_rate_limited",
                "rate_limited",
                "user_model_rate_limited",
                "integration_rate_limited"))
        {
            Volatile.Write(ref _sessionErrorException, new EvaluationRateLimitException());
            return;
        }

        if (string.Equals(errorType, "quota", StringComparison.OrdinalIgnoreCase)
            || ContainsAny(
                errorCode,
                "quota_exceeded",
                "session_quota_exceeded",
                "billing_not_configured"))
        {
            Volatile.Write(ref _sessionErrorException, new EvaluationQuotaExhaustedException());
        }
    }

    private static bool ContainsAny(string? value, params string[] needles) =>
        value is not null && needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private async Task NotifyProcessFailureAsync()
    {
        if (_onProcessFailure is null)
        {
            return;
        }

        try { await _onProcessFailure().ConfigureAwait(false); }
        catch { /* Recovery invalidation must not replace the original failure. */ }
    }

    public Task<EvaluationTokenUsage> GetUsageAsync(
        CancellationToken cancellationToken)
    {
        // Independent finite token: the run token is usually already cancelled on cleanup.
        lock (_usageGate)
        {
            if (_closed || !_sendStarted || _sendPending || _abortTask is { IsCompleted: false })
            {
                if (!_closed && _sendStarted)
                {
                    _usageStatus = _sendPending ? UsageObservationStatus.SendPending : UsageObservationStatus.AbortPending;
                    _partialUsage = true;
                    PublishUsage(finished: false);
                }

                return Task.FromResult(_legacyUsage);
            }

            return _captureTask ??= Task.Run(CaptureUsageAsync);
        }
    }

    private async Task<EvaluationTokenUsage> CaptureUsageAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        CancellationToken finiteToken = deadline.Token;
#pragma warning disable GHCP001 // Fixed SDK usage.getMetrics experimental result type.
        Task<GitHub.Copilot.Rpc.UsageGetMetricsResult>? rpc = null;
#pragma warning restore GHCP001
        try
        {
            rpc = Task.Run(() => _session.Rpc.Usage.GetMetricsAsync(finiteToken), finiteToken);
            var metrics = await rpc.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            // Keep the pre-existing Excel projection, including its established fallback.
            EvaluationTokenUsage total = EvaluationTokenUsage.Unavailable;
            if (metrics.ModelMetrics is not null)
            {
                foreach (var metric in metrics.ModelMetrics.Values)
                {
                    if (metric?.Usage is null)
                    {
                        continue;
                    }

                    total = total.Add(new EvaluationTokenUsage(
                        true,
                        NonNegative(metric.Usage.InputTokens),
                        NonNegative(metric.Usage.OutputTokens),
                        NonNegative(metric.Usage.ReasoningTokens ?? 0),
                        NonNegative(metric.Usage.CacheReadTokens),
                        NonNegative(metric.Usage.CacheWriteTokens)));
                }
            }

            EvaluationTokenUsage legacy = total.IsAvailable
                ? total
                : new EvaluationTokenUsage(
                    true,
                    NonNegative(metrics.LastCallInputTokens),
                    NonNegative(metrics.LastCallOutputTokens),
                    0,
                    0,
                    0);
            UsageMetrics final = SdkUsageAdapter.FromFinal(metrics,
                out UsageSource source, out bool partial, out bool invalid, out var finalProvenance);
            var finalModels = SdkUsageAdapter.ModelsFromFinal(metrics, out bool modelsTruncated);
            ModelCostComparison comparison = SdkUsageAdapter.CompareModelCosts(metrics);
            lock (_usageGate)
            {
                if (!_closed)
                {
                    // LastCall token fields are not a session total: prefer cumulative events.
                    UsageMetrics replacement = UsageProvenance.MergeFinal(final, _observedUsage,
                        finalProvenance, out _metricProvenance);
                    _modelCostComparison = comparison;
                    _partialUsage = partial || final != replacement;
                    _observedUsage = replacement;
                    if (finalModels is { } reportedModels)
                    {
                        _models = reportedModels;
                        _modelsTruncated = modelsTruncated;
                    }
                    // No map: retain partial event attribution, never infer it from requested model.
                    _usageSource = source;
                    _usageStatus = UsageObservationStatus.FinalObserved;
                    _invalidUsage |= invalid;
                    _legacyUsage = legacy;
                    _finalCaptured = true;
                    PublishUsage(finished: false);
                }

                return _legacyUsage;
            }
        }
        catch (Exception exception)
        {
            ObserveLateFault(rpc);
            lock (_usageGate)
            {
                if (!_closed)
                {
                    _usageStatus = exception is TimeoutException or OperationCanceledException
                        ? UsageObservationStatus.RpcTimedOut : UsageObservationStatus.RpcUnavailable;
                    _partialUsage = true;
                    PublishUsage(finished: false);
                }

                return _legacyUsage;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_usageGate)
        {
            return new ValueTask(_disposeTask ??= Task.Run(DisposeCoreAsync));
        }
    }

    private async Task DisposeCoreAsync()
    {
        // The coordinator aborts first on failure. GetUsageAsync refuses concurrent send/abort RPC.
        try { await GetUsageAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { /* Observation failure must not replace cleanup's result. */ }
        lock (_usageGate)
        {
            _closed = true;
            PublishUsage(finished: true);
            _eventIds.Clear();
            _eventMetrics.Clear();
            _modelEvents.Clear();
        }

        try { _errorSubscription?.Dispose(); }
        catch { /* Observation teardown is advisory. */ }
        try { _usageSubscription?.Dispose(); }
        catch { /* Observation teardown is advisory. */ }
        await _session.DisposeAsync().ConfigureAwait(false);
    }

    private void OnUsage(AssistantUsageEvent usageEvent)
    {
        try
        {
            lock (_usageGate)
            {
                if (_closed || _finalCaptured || !_sendStarted || usageEvent.Data is null) { return; }
                // Empty identifiers cannot establish uniqueness. Bound retained event metadata.
                if (_eventIds.Contains(usageEvent.Id)) { return; }
                if (usageEvent.Id == Guid.Empty || _eventIds.Count >= 4096)
                {
                    if (_usageStatus != UsageObservationStatus.EventsIncomplete)
                    {
                        _usageStatus = UsageObservationStatus.EventsIncomplete;
                        PublishUsage(finished: false);
                    }

                    return;
                }
                if (!_eventIds.Add(usageEvent.Id)) { return; }
                UsageMetrics value = SdkUsageAdapter.FromEvent(usageEvent.Data, out bool invalid);
                _eventMetrics.Add(value);
                _observedUsage = UsageMath.Sum(_eventMetrics, out bool overflow, out _);
                _metricProvenance = UsageProvenance.FromSource(_observedUsage, UsageSource.Events);
                string key = ModelUsageSnapshot.CreateModelKey(usageEvent.Data.Model);
                if (!_modelEvents.TryGetValue(key, out var modelValues)
                    && _modelEvents.Count < ModelUsageSnapshot.MaximumModels)
                {
                    modelValues = [];
                    _modelEvents.Add(key, modelValues);
                }
                if (modelValues is not null) { modelValues.Add(value); }
                else { _modelsTruncated = true; }
                // Shares the aggregate's dedup gate and 4096-event retention bound.
                _models = _modelEvents.Select(pair => new ModelUsageSnapshot(pair.Key,
                    UsageMath.Sum(pair.Value, out _, out _))).ToImmutableArray();
                _invalidUsage |= invalid || overflow;
                _usageSource = UsageSource.Events;
                if (_usageStatus != UsageObservationStatus.EventsIncomplete)
                {
                    _usageStatus = UsageObservationStatus.EventObserved;
                }
                PublishUsage(finished: false);
            }
        }
        catch
        {
            // Never propagate an observation callback exception into the SDK event dispatcher.
        }
    }

    private void PublishUsage(bool finished)
    {
        if (_usageTracker is null || _attemptId == Guid.Empty) { return; }
        try
        {
            _usageTracker.ReplaceAttempt(new AttemptUsageSnapshot(_attemptId, _operation,
                ++_usageRevision, _observedUsage, _usageSource, finished, _invalidUsage, _partialUsage, _usageStatus)
            {
                Models = _models,
                ModelsTruncated = _modelsTruncated,
                RequestedModelKey = _requestedModelKey,
                RequestedModelIsAuto = _requestedModelIsAuto,
                RequestedReasoningEffort = _requestedReasoningEffort,
                MetricProvenance = _metricProvenance,
                ModelCostComparison = _modelCostComparison,
            });
        }
        catch { /* Usage does not determine evaluation success. */ }
    }

    private static void ObserveLateFault(Task? task)
    {
        if (task is null) { return; }
        _ = task.ContinueWith(static completed => _ = completed.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static long NonNegative(long value) => Math.Max(0, value);
}