using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;

namespace StudyReportEvaluator.App.Workflow;

public sealed class AuxiliaryOperationResult<TResult>
    where TResult : class
{
    public AuxiliaryOperationResult(
        string statusCode,
        TResult? acceptedResult,
        int attemptCount = 0,
        EvaluationTokenUsage? tokenUsage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusCode);
        if (!ResultsStatusCodes.IsDefined(statusCode) || attemptCount < 0)
        {
            throw new ArgumentException("The auxiliary operation status is invalid.", nameof(statusCode));
        }

        bool succeeded = string.Equals(statusCode, ResultsStatusCodes.Success, StringComparison.Ordinal);
        if (succeeded != (acceptedResult is not null))
        {
            throw new ArgumentException(
                "A successful auxiliary operation requires one accepted result and a failed operation cannot carry one.",
                nameof(acceptedResult));
        }

        StatusCode = statusCode;
        AcceptedResult = acceptedResult;
        AttemptCount = attemptCount;
        TokenUsage = tokenUsage ?? EvaluationTokenUsage.Unavailable;
    }

    public string StatusCode { get; }

    public TResult? AcceptedResult { get; }

    public int AttemptCount { get; }

    public EvaluationTokenUsage TokenUsage { get; }

    public bool IsSuccess => string.Equals(StatusCode, ResultsStatusCodes.Success, StringComparison.Ordinal);

    public static AuxiliaryOperationResult<TResult> Succeeded(
        TResult acceptedResult,
        int attemptCount = 1,
        EvaluationTokenUsage? tokenUsage = null) =>
        new(ResultsStatusCodes.Success, acceptedResult, attemptCount, tokenUsage);

    public static AuxiliaryOperationResult<TResult> Failed(
        string statusCode,
        int attemptCount = 1,
        EvaluationTokenUsage? tokenUsage = null) =>
        new(statusCode, null, attemptCount, tokenUsage);

    public override string ToString() =>
        $"{nameof(AuxiliaryOperationResult<TResult>)} {{ StatusCode = {StatusCode}, AttemptCount = {AttemptCount}, Content = <redacted> }}";
}

public interface IReferenceAnswerOperationRunner
{
    Task<AuxiliaryOperationResult<ReferenceAnswerResult>> EvaluateAsync(
        SafeReferenceAnswerPayload payload,
        CancellationToken cancellationToken);

    Task<AuxiliaryOperationResult<ReferenceAnswerResult>> EvaluateAsync(
        SafeReferenceAnswerPayload payload,
        string modelId,
        string? reasoningEffort,
        CancellationToken cancellationToken) =>
        EvaluateAsync(payload, cancellationToken);
}

public interface ISpecialEvaluationOperationRunner
{
    Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(
        SafeSpecialEvaluationPayload payload,
        string modelId,
        CancellationToken cancellationToken);

    Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(
        SafeSpecialEvaluationPayload payload,
        string modelId,
        string? reasoningEffort,
        CancellationToken cancellationToken) =>
        EvaluateAsync(payload, modelId, cancellationToken);
}

public sealed class ReferenceAnswerOperationRunnerAdapter : IReferenceAnswerOperationRunner
{
    private readonly ReferenceAnswerEvaluationRunner runner;

    public ReferenceAnswerOperationRunnerAdapter(ReferenceAnswerEvaluationRunner runner)
    {
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<AuxiliaryOperationResult<ReferenceAnswerResult>> EvaluateAsync(
        SafeReferenceAnswerPayload payload,
        CancellationToken cancellationToken) =>
        await EvaluateAsync(payload, "auto", null, cancellationToken).ConfigureAwait(false);

    public async Task<AuxiliaryOperationResult<ReferenceAnswerResult>> EvaluateAsync(
        SafeReferenceAnswerPayload payload,
        string modelId,
        string? reasoningEffort,
        CancellationToken cancellationToken)
    {
        EphemeralEvaluationResult<ReferenceAnswerResult> result = await runner
            .EvaluateAsync(payload, modelId, reasoningEffort, cancellationToken)
            .ConfigureAwait(false);
        return new AuxiliaryOperationResult<ReferenceAnswerResult>(
            result.StatusCode,
            result.AcceptedResult,
            result.AttemptCount,
            result.TokenUsage);
    }
}

public sealed class SpecialEvaluationOperationRunnerAdapter : ISpecialEvaluationOperationRunner
{
    private readonly SpecialEvaluationRunner runner;

    public SpecialEvaluationOperationRunnerAdapter(SpecialEvaluationRunner runner)
    {
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(
        SafeSpecialEvaluationPayload payload,
        string modelId,
        CancellationToken cancellationToken) =>
        await EvaluateAsync(payload, modelId, null, cancellationToken).ConfigureAwait(false);

    public async Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(
        SafeSpecialEvaluationPayload payload,
        string modelId,
        string? reasoningEffort,
        CancellationToken cancellationToken)
    {
        EphemeralEvaluationResult<SpecialQuantificationResult> result = await runner
            .EvaluateAsync(payload, modelId, reasoningEffort, cancellationToken)
            .ConfigureAwait(false);
        return new AuxiliaryOperationResult<SpecialQuantificationResult>(
            result.StatusCode,
            result.AcceptedResult,
            result.AttemptCount,
            result.TokenUsage);
    }
}

internal static class DurableOperationConversions
{
    internal static CheckpointTokenUsage ToCheckpointUsage(EvaluationTokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return new CheckpointTokenUsage
        {
            IsAvailable = usage.IsAvailable,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            ReasoningTokens = usage.ReasoningTokens,
            CacheReadTokens = usage.CacheReadTokens,
            CacheWriteTokens = usage.CacheWriteTokens,
        };
    }

    internal static EvaluationTokenUsage ToEvaluationUsage(CheckpointTokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return new EvaluationTokenUsage(
            usage.IsAvailable,
            usage.InputTokens,
            usage.OutputTokens,
            usage.ReasoningTokens,
            usage.CacheReadTokens,
            usage.CacheWriteTokens);
    }

    internal static string ClassifyException(Exception exception) => exception switch
    {
        EvaluationAttemptException attempt => attempt.FailureKind switch
        {
            EvaluationAttemptFailureKind.SchemaInvalid => ResultsStatusCodes.AiOutputInvalid,
            EvaluationAttemptFailureKind.Network => ResultsStatusCodes.NetworkFailed,
            EvaluationAttemptFailureKind.Timeout => ResultsStatusCodes.AiTimeout,
            EvaluationAttemptFailureKind.RateLimited => ResultsStatusCodes.RateLimited,
            EvaluationAttemptFailureKind.QuotaExhausted => ResultsStatusCodes.QuotaExhausted,
            EvaluationAttemptFailureKind.Authentication => ResultsStatusCodes.AuthRequired,
            EvaluationAttemptFailureKind.Cancelled => ResultsStatusCodes.Cancelled,
            EvaluationAttemptFailureKind.Cleanup => ResultsStatusCodes.CleanupFailed,
            _ => ResultsStatusCodes.AiRuntimeFailed,
        },
        TimeoutException => ResultsStatusCodes.AiTimeout,
        HttpRequestException or SocketException or WebSocketException or IOException => ResultsStatusCodes.NetworkFailed,
        _ => ResultsStatusCodes.AiRuntimeFailed,
    };
}
