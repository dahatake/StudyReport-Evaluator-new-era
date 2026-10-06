using GitHub.Copilot;
using StudyReportEvaluator.App.Logging;
using StudyReportEvaluator.App.Usage;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;

namespace StudyReportEvaluator.App.Copilot;

public sealed class ReferenceAnswerEvaluationRunner
{
    private readonly IEphemeralCopilotTransportFactory transportFactory;
    private readonly AuxiliaryEvaluationSchemaFactory schemaFactory = new();
    private readonly RetryAndCleanupCoordinator coordinator;
    private readonly EphemeralEvaluationRunnerOptions options;

    public ReferenceAnswerEvaluationRunner()
        : this(new SdkEphemeralCopilotTransportFactory(new CopilotClientFactory()))
    {
    }

    public ReferenceAnswerEvaluationRunner(
        IEphemeralCopilotTransportFactory transportFactory,
        EphemeralEvaluationRunnerOptions? options = null,
        SafeLogger? logger = null)
    {
        this.transportFactory = transportFactory
            ?? throw new ArgumentNullException(nameof(transportFactory));
        this.options = options ?? new EphemeralEvaluationRunnerOptions();
        coordinator = new RetryAndCleanupCoordinator(logger);
    }

    public Task<EphemeralEvaluationResult<ReferenceAnswerResult>> EvaluateAsync(
        SafeReferenceAnswerPayload payload,
        CancellationToken cancellationToken = default) =>
        EvaluateAsync(payload, "auto", null, cancellationToken);

    public Task<EphemeralEvaluationResult<ReferenceAnswerResult>> EvaluateAsync(
        SafeReferenceAnswerPayload payload,
        string modelId,
        string? reasoningEffort = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        EphemeralEvaluationRunner.ValidateModelId(modelId);
        EphemeralEvaluationRunner.ValidateReasoningEffort(reasoningEffort);
        return coordinator.ExecuteAuxiliaryAsync(
            _ => CreateAttempt(payload, modelId, reasoningEffort),
            options.AttemptTimeout,
            options.CleanupTimeout,
            options.MaxConcurrency,
            cancellationToken,
            options.ConcurrencyObserver,
            options.RetryDelayProvider);
    }

    private IEphemeralEvaluationAttempt<ReferenceAnswerResult> CreateAttempt(
        SafeReferenceAnswerPayload payload,
        string modelId,
        string? reasoningEffort)
    {
        SessionConfig config = schemaFactory.CreateReferenceSessionConfig(payload, out SubmitReferenceAnswerTool collector);
        config.ReasoningEffort = reasoningEffort;
        config.ContextTier = options.ContextTier;
        return new CopilotAuxiliaryAttempt<ReferenceAnswerResult>(
            transportFactory.Create()
                ?? throw new InvalidOperationException("The transport factory returned no transport."),
            config,
            payload.RenderedPrompt,
            modelId,
            $"ref-{Guid.NewGuid():N}",
            () => collector.TryGetAcceptedResult(out ReferenceAnswerResult? result) ? result : null);
    }
}

public sealed class SpecialEvaluationRunner
{
    private readonly IEphemeralCopilotTransportFactory transportFactory;
    private readonly AuxiliaryEvaluationSchemaFactory schemaFactory = new();
    private readonly RetryAndCleanupCoordinator coordinator;
    private readonly EphemeralEvaluationRunnerOptions options;

    public SpecialEvaluationRunner()
        : this(new SdkEphemeralCopilotTransportFactory(new CopilotClientFactory()))
    {
    }

    public SpecialEvaluationRunner(
        IEphemeralCopilotTransportFactory transportFactory,
        EphemeralEvaluationRunnerOptions? options = null,
        SafeLogger? logger = null)
    {
        this.transportFactory = transportFactory
            ?? throw new ArgumentNullException(nameof(transportFactory));
        this.options = options ?? new EphemeralEvaluationRunnerOptions();
        coordinator = new RetryAndCleanupCoordinator(logger);
    }

    public Task<EphemeralEvaluationResult<SpecialQuantificationResult>> EvaluateAsync(
        SafeSpecialEvaluationPayload payload,
        string modelId,
        CancellationToken cancellationToken = default) =>
        EvaluateAsync(payload, modelId, null, cancellationToken);

    public Task<EphemeralEvaluationResult<SpecialQuantificationResult>> EvaluateAsync(
        SafeSpecialEvaluationPayload payload,
        string modelId,
        string? reasoningEffort = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        EphemeralEvaluationRunner.ValidateModelId(modelId);
        EphemeralEvaluationRunner.ValidateReasoningEffort(reasoningEffort);
        return coordinator.ExecuteAuxiliaryAsync(
            _ => CreateAttempt(payload, modelId, reasoningEffort),
            options.AttemptTimeout,
            options.CleanupTimeout,
            options.MaxConcurrency,
            cancellationToken,
            options.ConcurrencyObserver,
            options.RetryDelayProvider);
    }

    private IEphemeralEvaluationAttempt<SpecialQuantificationResult> CreateAttempt(
        SafeSpecialEvaluationPayload payload,
        string modelId,
        string? reasoningEffort)
    {
        SessionConfig config = schemaFactory.CreateSpecialSessionConfig(payload, out SubmitSpecialQuantificationTool collector);
        config.ReasoningEffort = reasoningEffort;
        config.ContextTier = options.ContextTier;
        return new CopilotAuxiliaryAttempt<SpecialQuantificationResult>(
            transportFactory.Create()
                ?? throw new InvalidOperationException("The transport factory returned no transport."),
            config,
            payload.RenderedPrompt,
            modelId,
            $"special-{Guid.NewGuid():N}",
            () => collector.TryGetAcceptedResult(out SpecialQuantificationResult? result) ? result : null);
    }
}

internal sealed class CopilotAuxiliaryAttempt<TResult> : IEphemeralEvaluationAttempt<TResult>
    where TResult : class
{
    private readonly IEphemeralCopilotTransport transport;
    private readonly SessionConfig sessionConfig;
    private readonly MessageOptions messageOptions;
    private readonly Func<TResult?> acceptedResult;
    private IEphemeralCopilotSession? session;
    private string? createdSessionId;
    private EvaluationTokenUsage tokenUsage = EvaluationTokenUsage.Unavailable;

    internal CopilotAuxiliaryAttempt(
        IEphemeralCopilotTransport transport,
        SessionConfig sessionConfig,
        string prompt,
        string modelId,
        string sessionId,
        Func<TResult?> acceptedResult)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.sessionConfig = sessionConfig ?? throw new ArgumentNullException(nameof(sessionConfig));
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        EphemeralEvaluationRunner.ValidateModelId(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        this.acceptedResult = acceptedResult ?? throw new ArgumentNullException(nameof(acceptedResult));
        this.sessionConfig.SessionId = sessionId;
        this.sessionConfig.Model = modelId;
        messageOptions = new MessageOptions
        {
            Prompt = prompt,
            Attachments = [],
            DisplayPrompt = null,
            RequestHeaders = null,
        };
        SessionId = sessionId;
    }

    public string SessionId { get; }

    public EvaluationTokenUsage TokenUsage => Volatile.Read(ref tokenUsage);

    public void SetUsageAttemptContext(int attemptNumber, Guid operationId) =>
        transport.SetUsageAttemptContext(attemptNumber, operationId);

    public void CompleteUsageAttempt(UsageAttemptOutcome outcome) =>
        transport.CompleteUsageAttempt(outcome);

    public async Task<TResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await transport.StartAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await transport.IsAuthenticatedAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new EvaluationAuthenticationException();
        }

        cancellationToken.ThrowIfCancellationRequested();
        IEphemeralCopilotSession created = await transport
            .CreateSessionAsync(sessionConfig, cancellationToken)
            .ConfigureAwait(false);
        session = created ?? throw new EvaluationFatalException();
        createdSessionId = created.SessionId;
        if (!string.Equals(SessionId, created.SessionId, StringComparison.Ordinal))
        {
            throw new EvaluationFatalException();
        }

        await created.SendAndWaitAsync(messageOptions, cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(
                ref tokenUsage,
                await created.GetUsageAsync(cancellationToken).ConfigureAwait(false)
                ?? EvaluationTokenUsage.Unavailable);
        }
        catch
        {
            Volatile.Write(ref tokenUsage, EvaluationTokenUsage.Unavailable);
        }

        return acceptedResult() ?? throw new EvaluationSchemaException();
    }

    public Task AbortAsync(CancellationToken cancellationToken) =>
        Volatile.Read(ref session)?.AbortAsync(cancellationToken) ?? Task.CompletedTask;

    public async Task DisposeSessionAsync(CancellationToken cancellationToken)
    {
        IEphemeralCopilotSession? current = Volatile.Read(ref session);
        if (current is not null)
        {
            await current.DisposeAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteSessionAsync(CancellationToken cancellationToken)
    {
        bool succeeded = true;
        string? sessionId = Volatile.Read(ref createdSessionId);
        if (sessionId is not null)
        {
            try
            {
                await transport.DeleteSessionAsync(sessionId, cancellationToken)
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
            await transport.StopAsync(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            succeeded = false;
        }

        try
        {
            await transport.DisposeAsync().AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
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
