using GitHub.Copilot;

namespace StudyReportEvaluator.App.Copilot;

public enum CopilotAuthenticationStatus
{
    Available,
    AuthRequired,
    CliUnavailable,
    RuntimeFailed,
    Cancelled,
}

public enum CopilotRuntimeState
{
    Created,
    Starting,
    Ready,
    Stopping,
    Stopped,
    Faulted,
    Disposed,
}

public enum CopilotRuntimeAuthenticationState
{
    Authenticated,
    Unauthenticated,
}

public sealed class CopilotModelAvailability
{
    public CopilotModelAvailability(
        string id,
        int? maximumPromptTokens,
        int? maximumContextWindowTokens,
        IEnumerable<string>? supportedReasoningEfforts = null,
        string? defaultReasoningEffort = null,
        int? longContextPromptTokens = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 256
            || !string.Equals(id, id.Trim(), StringComparison.Ordinal)
            || id.Any(char.IsControl))
        {
            throw new ArgumentException("The model identity is invalid.", nameof(id));
        }

        Id = id;
        MaximumPromptTokens = PositiveOrNull(maximumPromptTokens);
        MaximumContextWindowTokens = PositiveOrNull(maximumContextWindowTokens);
        LongContextPromptTokens = longContextPromptTokens is > 0
            && (MaximumPromptTokens is null || longContextPromptTokens > MaximumPromptTokens)
                ? longContextPromptTokens : null;
        SupportedReasoningEfforts = Array.AsReadOnly((supportedReasoningEfforts ?? [])
            .Where(ReasoningEffortPolicy.IsSafeReasoningEffort)
            .Distinct(StringComparer.Ordinal)
            .ToArray());
        DefaultReasoningEffort = ReasoningEffortPolicy.IsSafeReasoningEffort(defaultReasoningEffort)
            ? defaultReasoningEffort
            : null;
    }

    public string Id { get; }

    public int? MaximumPromptTokens { get; }

    public int? MaximumContextWindowTokens { get; }

    public int? LongContextPromptTokens { get; }

    public IReadOnlyList<string> SupportedReasoningEfforts { get; }

    public string? DefaultReasoningEffort { get; }

    public bool SupportsReasoningEffort => SupportedReasoningEfforts.Count > 0;

    public int? EffectivePromptTokenLimit => MaximumPromptTokens is int prompt
        ? MaximumContextWindowTokens is int context
            ? Math.Min(prompt, context)
            : prompt
        : null;

    internal ModelInfo ToModelInfo() => new()
    {
        Id = Id,
        Capabilities = new ModelCapabilities
        {
            Supports = new ModelSupports { ReasoningEffort = SupportsReasoningEffort },
        },
        SupportedReasoningEfforts = [.. SupportedReasoningEfforts],
        DefaultReasoningEffort = DefaultReasoningEffort,
    };

    public override string ToString() =>
        $"{nameof(CopilotModelAvailability)} {{ Id = {Id}, PromptLimitKnown = {MaximumPromptTokens is not null}, ContextLimitKnown = {MaximumContextWindowTokens is not null}, Content = <redacted> }}";

    private static int? PositiveOrNull(int? value) => value is > 0 ? value : null;
}

public interface ICopilotAuthenticationRuntime : IAsyncDisposable
{
    CopilotRuntimeState State { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task PingAsync(CancellationToken cancellationToken);

    Task<CopilotRuntimeAuthenticationState> GetAuthenticationStatusAsync(CancellationToken cancellationToken);

    async Task<IReadOnlyList<CopilotModelAvailability>> ListModelsAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> modelIds = await ListModelIdsAsync(cancellationToken)
            .ConfigureAwait(false);
        return modelIds
            .Select(id => new CopilotModelAvailability(id, null, null))
            .ToArray();
    }

    Task<IReadOnlyList<string>> ListModelIdsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException("The runtime must provide model IDs or model capacity descriptors.");

    Task StopAsync(CancellationToken cancellationToken);
}

public interface ICopilotAuthenticationRuntimeFactory
{
    ValueTask<CopilotAuthenticationRuntimeCreation> CreateAsync(CancellationToken cancellationToken);
}

public sealed class CopilotAuthenticationRuntimeCreation
{
    private CopilotAuthenticationRuntimeCreation(
        CopilotClientCreationStatus status,
        ICopilotAuthenticationRuntime? runtime,
        CopilotRuntimeIdentity? identity)
    {
        Status = status;
        Runtime = runtime;
        Identity = identity;
    }

    public CopilotClientCreationStatus Status { get; }

    public ICopilotAuthenticationRuntime? Runtime { get; }

    public CopilotRuntimeIdentity? Identity { get; }

    public static CopilotAuthenticationRuntimeCreation Created(
        ICopilotAuthenticationRuntime runtime,
        CopilotRuntimeIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(identity);
        return new CopilotAuthenticationRuntimeCreation(CopilotClientCreationStatus.Created, runtime, identity);
    }

    public static CopilotAuthenticationRuntimeCreation Failed(CopilotClientCreationStatus status)
    {
        if (status == CopilotClientCreationStatus.Created)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new CopilotAuthenticationRuntimeCreation(status, null, null);
    }
}

public sealed class CopilotAuthenticationResult
{
    private CopilotAuthenticationResult(
        CopilotAuthenticationStatus status,
        IReadOnlyList<CopilotModelAvailability> availableModels,
        CopilotRuntimeIdentity? identity)
    {
        Status = status;
        AvailableModels = availableModels;
        AvailableModelIds = Array.AsReadOnly(availableModels.Select(model => model.Id).ToArray());
        Identity = identity;
    }

    public CopilotAuthenticationStatus Status { get; }

    public IReadOnlyList<string> AvailableModelIds { get; }

    public IReadOnlyList<CopilotModelAvailability> AvailableModels { get; }

    public CopilotRuntimeIdentity? Identity { get; }

    public override string ToString() =>
        $"{Status}; models={AvailableModelIds.Count}; runtime={(Identity is null ? "none" : Identity.ToString())}";

    internal static CopilotAuthenticationResult Available(
        IEnumerable<CopilotModelAvailability> models,
        CopilotRuntimeIdentity identity) =>
        new(CopilotAuthenticationStatus.Available, NormalizeModels(models), identity);

    internal static CopilotAuthenticationResult FromStatus(
        CopilotAuthenticationStatus status,
        CopilotRuntimeIdentity? identity = null)
    {
        if (status == CopilotAuthenticationStatus.Available)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new CopilotAuthenticationResult(
            status,
            Array.Empty<CopilotModelAvailability>(),
            identity);
    }

    private static IReadOnlyList<CopilotModelAvailability> NormalizeModels(
        IEnumerable<CopilotModelAvailability> models)
    {
        ArgumentNullException.ThrowIfNull(models);

        List<CopilotModelAvailability> normalized = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (CopilotModelAvailability? model in models)
        {
            if (model is null || !seen.Add(model.Id))
            {
                continue;
            }

            normalized.Add(model);
        }

        return normalized.AsReadOnly();
    }
}

public sealed class CopilotAuthenticationService
{
    private readonly ICopilotAuthenticationRuntimeFactory _runtimeFactory;
    private readonly TimeSpan _checkTimeout;

    public static TimeSpan DefaultCheckTimeout { get; } = TimeSpan.FromSeconds(15);

    public CopilotAuthenticationService()
        : this(new CopilotClientFactory(), DefaultCheckTimeout)
    {
    }

    public CopilotAuthenticationService(
        ICopilotClientFactory clientFactory,
        TimeSpan? checkTimeout = null)
        : this(new SdkCopilotAuthenticationRuntimeFactory(clientFactory), checkTimeout ?? DefaultCheckTimeout)
    {
    }

    public CopilotAuthenticationService(
        ICopilotAuthenticationRuntimeFactory runtimeFactory,
        TimeSpan? checkTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeFactory);
        TimeSpan timeout = checkTimeout ?? DefaultCheckTimeout;
        ValidateFiniteTimeout(timeout);

        _runtimeFactory = runtimeFactory;
        _checkTimeout = timeout;
    }

    public async Task<CopilotAuthenticationResult> CheckAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return CopilotAuthenticationResult.FromStatus(CopilotAuthenticationStatus.Cancelled);
        }

        using CancellationTokenSource timeoutSource = new(_checkTimeout);
        using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        CancellationToken finiteToken = linkedSource.Token;

        CopilotAuthenticationRuntimeCreation creation;
        try
        {
            creation = await _runtimeFactory
                .CreateAsync(finiteToken)
                .AsTask()
                .WaitAsync(finiteToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (finiteToken.IsCancellationRequested)
        {
            return CopilotAuthenticationResult.FromStatus(
                cancellationToken.IsCancellationRequested
                    ? CopilotAuthenticationStatus.Cancelled
                    : CopilotAuthenticationStatus.RuntimeFailed);
        }
        catch
        {
            return CopilotAuthenticationResult.FromStatus(CopilotAuthenticationStatus.RuntimeFailed);
        }

        CopilotAuthenticationResult? terminalResult = MapCreationFailure(
            creation.Status,
            cancellationToken.IsCancellationRequested);
        if (terminalResult is not null)
        {
            return terminalResult;
        }

        ICopilotAuthenticationRuntime? runtime = creation.Runtime;
        CopilotRuntimeIdentity? identity = creation.Identity;
        if (runtime is null || identity is null)
        {
            if (runtime is not null)
            {
                await TryCleanupAsync(runtime).ConfigureAwait(false);
            }

            return CopilotAuthenticationResult.FromStatus(CopilotAuthenticationStatus.RuntimeFailed);
        }

        CopilotAuthenticationResult result;
        try
        {
            await runtime.StartAsync(finiteToken).WaitAsync(finiteToken).ConfigureAwait(false);
            if (runtime.State != CopilotRuntimeState.Ready)
            {
                result = CopilotAuthenticationResult.FromStatus(CopilotAuthenticationStatus.RuntimeFailed, identity);
            }
            else
            {
                await runtime.PingAsync(finiteToken).WaitAsync(finiteToken).ConfigureAwait(false);
                CopilotRuntimeAuthenticationState authenticationState = await runtime
                    .GetAuthenticationStatusAsync(finiteToken)
                    .WaitAsync(finiteToken)
                    .ConfigureAwait(false);

                result = authenticationState switch
                {
                    CopilotRuntimeAuthenticationState.Unauthenticated =>
                        CopilotAuthenticationResult.FromStatus(CopilotAuthenticationStatus.AuthRequired, identity),
                    CopilotRuntimeAuthenticationState.Authenticated =>
                        await GetAvailableResultAsync(runtime, identity, finiteToken).ConfigureAwait(false),
                    _ => CopilotAuthenticationResult.FromStatus(CopilotAuthenticationStatus.RuntimeFailed, identity),
                };
            }
        }
        catch (OperationCanceledException) when (finiteToken.IsCancellationRequested)
        {
            result = CopilotAuthenticationResult.FromStatus(
                cancellationToken.IsCancellationRequested
                    ? CopilotAuthenticationStatus.Cancelled
                    : CopilotAuthenticationStatus.RuntimeFailed,
                identity);
        }
        catch
        {
            result = CopilotAuthenticationResult.FromStatus(CopilotAuthenticationStatus.RuntimeFailed, identity);
        }

        bool cleanupSucceeded = await TryCleanupAsync(runtime).ConfigureAwait(false);
        if (!cleanupSucceeded && result.Status != CopilotAuthenticationStatus.Cancelled)
        {
            return CopilotAuthenticationResult.FromStatus(CopilotAuthenticationStatus.RuntimeFailed, identity);
        }

        return result;
    }

    private static CopilotAuthenticationResult? MapCreationFailure(
        CopilotClientCreationStatus status,
        bool callerCancelled) =>
        status switch
        {
            CopilotClientCreationStatus.Created => null,
            CopilotClientCreationStatus.CliUnavailable =>
                CopilotAuthenticationResult.FromStatus(CopilotAuthenticationStatus.CliUnavailable),
            CopilotClientCreationStatus.Cancelled =>
                CopilotAuthenticationResult.FromStatus(
                    callerCancelled
                        ? CopilotAuthenticationStatus.Cancelled
                        : CopilotAuthenticationStatus.RuntimeFailed),
            CopilotClientCreationStatus.RuntimeFailed =>
                CopilotAuthenticationResult.FromStatus(CopilotAuthenticationStatus.RuntimeFailed),
            _ => CopilotAuthenticationResult.FromStatus(CopilotAuthenticationStatus.RuntimeFailed),
        };

    private static async Task<CopilotAuthenticationResult> GetAvailableResultAsync(
        ICopilotAuthenticationRuntime runtime,
        CopilotRuntimeIdentity identity,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CopilotModelAvailability> models = await runtime
            .ListModelsAsync(cancellationToken)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        return models is null
            ? CopilotAuthenticationResult.FromStatus(CopilotAuthenticationStatus.RuntimeFailed, identity)
            : CopilotAuthenticationResult.Available(models, identity);
    }

    private async Task<bool> TryCleanupAsync(ICopilotAuthenticationRuntime runtime)
    {
        using CancellationTokenSource cleanupSource = new(_checkTimeout);
        CancellationToken cleanupToken = cleanupSource.Token;
        bool succeeded = true;

        try
        {
            await runtime
                .StopAsync(cleanupToken)
                .WaitAsync(cleanupToken)
                .ConfigureAwait(false);
        }
        catch
        {
            succeeded = false;
        }

        try
        {
            await runtime
                .DisposeAsync()
                .AsTask()
                .WaitAsync(cleanupToken)
                .ConfigureAwait(false);
        }
        catch
        {
            succeeded = false;
        }

        return succeeded;
    }

    private static void ValidateFiniteTimeout(TimeSpan timeout)
    {
        const double MaximumCancellationDelayMilliseconds = uint.MaxValue - 1d;
        if (timeout <= TimeSpan.Zero
            || timeout == Timeout.InfiniteTimeSpan
            || timeout.TotalMilliseconds > MaximumCancellationDelayMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The authentication check timeout must be positive and finite.");
        }
    }
}

internal sealed class SdkCopilotAuthenticationRuntimeFactory : ICopilotAuthenticationRuntimeFactory
{
    private readonly ICopilotClientFactory _clientFactory;

    internal SdkCopilotAuthenticationRuntimeFactory(ICopilotClientFactory clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = clientFactory;
    }

    public async ValueTask<CopilotAuthenticationRuntimeCreation> CreateAsync(CancellationToken cancellationToken)
    {
        CopilotClientCreationResult creation = await _clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        if (creation.Status != CopilotClientCreationStatus.Created)
        {
            return CopilotAuthenticationRuntimeCreation.Failed(creation.Status);
        }

        if (creation.Client is null || creation.Identity is null)
        {
            if (creation.Client is not null)
            {
                await creation.Client.DisposeAsync().ConfigureAwait(false);
            }

            return CopilotAuthenticationRuntimeCreation.Failed(CopilotClientCreationStatus.RuntimeFailed);
        }

        return CopilotAuthenticationRuntimeCreation.Created(
            new SdkCopilotAuthenticationRuntime(creation.Client),
            creation.Identity);
    }
}

internal sealed class SdkCopilotAuthenticationRuntime : ICopilotAuthenticationRuntime
{
    private readonly CopilotClient _client;
    private int _state = (int)CopilotRuntimeState.Created;

    internal SdkCopilotAuthenticationRuntime(CopilotClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public CopilotRuntimeState State => (CopilotRuntimeState)Volatile.Read(ref _state);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(
                ref _state,
                (int)CopilotRuntimeState.Starting,
                (int)CopilotRuntimeState.Created)
            != (int)CopilotRuntimeState.Created)
        {
            throw new InvalidOperationException("The Copilot runtime cannot be started from its current state.");
        }

        try
        {
            await _client.StartAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _state, (int)CopilotRuntimeState.Ready);
        }
        catch
        {
            Volatile.Write(ref _state, (int)CopilotRuntimeState.Faulted);
            throw;
        }
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        EnsureReady();
        _ = await _client.PingAsync(message: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CopilotRuntimeAuthenticationState> GetAuthenticationStatusAsync(
        CancellationToken cancellationToken)
    {
        EnsureReady();
        GetAuthStatusResponse response = await _client
            .GetAuthStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        return response.IsAuthenticated
            ? CopilotRuntimeAuthenticationState.Authenticated
            : CopilotRuntimeAuthenticationState.Unauthenticated;
    }

    public async Task<IReadOnlyList<CopilotModelAvailability>> ListModelsAsync(
        CancellationToken cancellationToken)
    {
        EnsureReady();
        IList<ModelInfo> models = await _client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
        return MapAvailableModels(models);
    }

    // Every enumerated model is selectable except malformed IDs and models the account's policy disables.
    // One unusable entry must not fail the whole enumeration.
    internal static IReadOnlyList<CopilotModelAvailability> MapAvailableModels(IEnumerable<ModelInfo>? models)
    {
        List<CopilotModelAvailability> available = [];
        foreach (ModelInfo? model in models ?? [])
        {
            if (model?.Id is not { } id
                || !IsUsableModelId(id)
                || string.Equals(model.Policy?.State, "disabled", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ModelLimits? limits = model.Capabilities?.Limits;
            available.Add(new CopilotModelAvailability(
                id,
                DefaultPromptBudget(model) ?? limits?.MaxPromptTokens,
                limits?.MaxContextWindowTokens,
                model.Capabilities?.Supports?.ReasoningEffort == true ? model.SupportedReasoningEfforts : null,
                model.DefaultReasoningEffort,
                LongPromptBudget(model)));
        }

        return available.AsReadOnly();
    }

    // The pinned SDK exposes tier budgets through experimental billing metadata.
#pragma warning disable GHCP001
    private static int? DefaultPromptBudget(ModelInfo model) =>
        TokenBudget(model.Billing?.TokenPrices?.MaxPromptTokens ?? model.Billing?.TokenPrices?.ContextMax);

    private static int? LongPromptBudget(ModelInfo model) =>
        TokenBudget(model.Billing?.TokenPrices?.LongContext?.MaxPromptTokens
            ?? model.Billing?.TokenPrices?.LongContext?.ContextMax);
#pragma warning restore GHCP001

    private static int? TokenBudget(long? value) => value is > 0 and <= int.MaxValue ? (int)value : null;

    private static bool IsUsableModelId(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length <= 256
        && string.Equals(id, id.Trim(), StringComparison.Ordinal)
        && !id.Any(char.IsControl);

    public async Task<IReadOnlyList<string>> ListModelIdsAsync(
        CancellationToken cancellationToken) =>
        (await ListModelsAsync(cancellationToken).ConfigureAwait(false))
            .Select(model => model.Id)
            .ToArray();

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CopilotRuntimeState current = State;
        if (current is CopilotRuntimeState.Disposed or CopilotRuntimeState.Stopped)
        {
            return;
        }

        if (current == CopilotRuntimeState.Created)
        {
            Volatile.Write(ref _state, (int)CopilotRuntimeState.Stopped);
            return;
        }

        Volatile.Write(ref _state, (int)CopilotRuntimeState.Stopping);
        try
        {
            await _client.StopAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _state, (int)CopilotRuntimeState.Stopped);
        }
        catch
        {
            Volatile.Write(ref _state, (int)CopilotRuntimeState.Faulted);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if ((CopilotRuntimeState)Interlocked.Exchange(ref _state, (int)CopilotRuntimeState.Disposed)
            == CopilotRuntimeState.Disposed)
        {
            return;
        }

        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private void EnsureReady()
    {
        ThrowIfDisposed();
        if (State != CopilotRuntimeState.Ready)
        {
            throw new InvalidOperationException("The Copilot runtime is not ready.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(State == CopilotRuntimeState.Disposed, this);
    }
}
