using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.App.Workflow;

public sealed class EvaluationRunnerResult
{
    public EvaluationRunnerResult(
        string statusCode,
        QuantificationResult? acceptedResult,
        int attemptCount = 0,
        EvaluationTokenUsage? tokenUsage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statusCode);
        if (!ResultsStatusCodes.IsDefined(statusCode)
            || string.Equals(statusCode, ResultsStatusCodes.Empty, StringComparison.Ordinal))
        {
            throw new ArgumentException("The evaluation runner status is invalid.", nameof(statusCode));
        }

        if (attemptCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptCount));
        }

        bool succeeded = string.Equals(statusCode, ResultsStatusCodes.Success, StringComparison.Ordinal);
        if (succeeded != (acceptedResult is not null))
        {
            throw new ArgumentException(
                "A successful runner result requires one accepted result and a failed result cannot carry one.",
                nameof(acceptedResult));
        }

        StatusCode = statusCode;
        AcceptedResult = acceptedResult;
        AttemptCount = attemptCount;
        TokenUsage = tokenUsage ?? EvaluationTokenUsage.Unavailable;
    }

    public string StatusCode { get; }

    public QuantificationResult? AcceptedResult { get; }

    public int AttemptCount { get; }

    public EvaluationTokenUsage TokenUsage { get; }

    public bool IsSuccess => string.Equals(
        StatusCode,
        ResultsStatusCodes.Success,
        StringComparison.Ordinal);

    public static EvaluationRunnerResult Succeeded(
        QuantificationResult acceptedResult,
        int attemptCount = 1,
        EvaluationTokenUsage? tokenUsage = null) =>
        new(ResultsStatusCodes.Success, acceptedResult, attemptCount, tokenUsage);

    public static EvaluationRunnerResult Failed(
        string statusCode,
        int attemptCount = 1,
        EvaluationTokenUsage? tokenUsage = null) =>
        new(statusCode, null, attemptCount, tokenUsage);

    public override string ToString() =>
        $"{nameof(EvaluationRunnerResult)} {{ StatusCode = {StatusCode}, AttemptCount = {AttemptCount.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public interface IEvaluationRunner
{
    Task<EvaluationRunnerResult> EvaluateAsync(
        SafeEvaluationPayload payload,
        string modelId,
        CancellationToken cancellationToken);
}

public sealed class EphemeralEvaluationRunnerAdapter : IEvaluationRunner
{
    private readonly EphemeralEvaluationRunner runner;
    private readonly string? reasoningEffort;

    public EphemeralEvaluationRunnerAdapter()
        : this(new EphemeralEvaluationRunner())
    {
    }

    public EphemeralEvaluationRunnerAdapter(EphemeralEvaluationRunner runner, string? reasoningEffort = null)
    {
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        EphemeralEvaluationRunner.ValidateReasoningEffort(reasoningEffort);
        this.reasoningEffort = reasoningEffort;
    }

    public async Task<EvaluationRunnerResult> EvaluateAsync(
        SafeEvaluationPayload payload,
        string modelId,
        CancellationToken cancellationToken)
    {
        EphemeralEvaluationResult result = await runner
            .EvaluateAsync(payload, modelId, reasoningEffort, cancellationToken)
            .ConfigureAwait(false);
        return new EvaluationRunnerResult(
            result.StatusCode,
            result.AcceptedResult,
            result.AttemptCount,
            result.TokenUsage);
    }

    public override string ToString() =>
        $"{nameof(EphemeralEvaluationRunnerAdapter)} {{ Content = <redacted> }}";
}

public sealed class EvaluationSchedulerOptions
{
    public const int MinimumMaxConcurrency = 1;
    public const int DefaultMaxConcurrency = 8;
    public const int MaximumMaxConcurrency = 16;

    public EvaluationSchedulerOptions(int maxConcurrency = DefaultMaxConcurrency)
    {
        if (maxConcurrency is < MinimumMaxConcurrency or > MaximumMaxConcurrency)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrency),
                "Evaluation concurrency must be between 1 and 16.");
        }

        MaxConcurrency = maxConcurrency;
    }

    public int MaxConcurrency { get; }
}

public enum EvaluationProgressStatus
{
    Running,
    Cancelling,
    Completed,
    Cancelled,
}

public sealed record EvaluationProgress
{
    public EvaluationProgress(
        int total,
        int completed,
        int inFlight,
        EvaluationProgressStatus status,
        DurableEvaluationStage? stage = null,
        int referenceCompleted = 0,
        int referenceTotal = 0,
        int rowCompleted = 0,
        int rowTotal = 0,
        string? detailStatusCode = null,
        string? finalPath = null,
        string? partialPath = null)
    {
        if (total < 0
            || completed < 0
            || completed > total
            || inFlight < 0
            || completed + inFlight > total
            || referenceCompleted < 0
            || referenceCompleted > referenceTotal
            || referenceTotal < 0
            || rowCompleted < 0
            || rowCompleted > rowTotal
            || rowTotal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completed));
        }

        Total = total;
        Completed = completed;
        InFlight = inFlight;
        Status = status;
        Stage = stage;
        ReferenceCompleted = referenceCompleted;
        ReferenceTotal = referenceTotal;
        RowCompleted = rowCompleted;
        RowTotal = rowTotal;
        DetailStatusCode = detailStatusCode;
        FinalPath = finalPath;
        PartialPath = partialPath;
    }

    public int Total { get; }

    public int Completed { get; }

    public int InFlight { get; }

    public EvaluationProgressStatus Status { get; }

    public DurableEvaluationStage? Stage { get; }

    public int ReferenceCompleted { get; }

    public int ReferenceTotal { get; }

    public int RowCompleted { get; }

    public int RowTotal { get; }

    public string? DetailStatusCode { get; }

    public string? FinalPath { get; }

    public string? PartialPath { get; }

    public string StatusCode => Status switch
    {
        EvaluationProgressStatus.Running => "RUNNING",
        EvaluationProgressStatus.Cancelling => "CANCELLING",
        EvaluationProgressStatus.Completed => "COMPLETED",
        EvaluationProgressStatus.Cancelled => "CANCELLED",
        _ => "CANCELLED",
    };
}

public sealed class EvaluationUnitResult
{
    internal EvaluationUnitResult(
        EvaluationPlanItem item,
        string statusCode,
        QuantificationResult? acceptedResult,
        int attemptCount,
        bool scorable,
        bool scorableKnown,
        EvaluationTokenUsage tokenUsage)
    {
        Item = item;
        StatusCode = statusCode;
        AcceptedResult = acceptedResult;
        AttemptCount = attemptCount;
        Scorable = scorable;
        ScorableKnown = scorableKnown;
        TokenUsage = tokenUsage ?? throw new ArgumentNullException(nameof(tokenUsage));
    }

    public EvaluationPlanItem Item { get; }

    public string StatusCode { get; }

    public QuantificationResult? AcceptedResult { get; }

    public int AttemptCount { get; }

    public bool Scorable { get; }

    public bool ScorableKnown { get; }

    public EvaluationTokenUsage TokenUsage { get; }

    public bool HasCompletedPayload =>
        string.Equals(StatusCode, ResultsStatusCodes.Success, StringComparison.Ordinal)
        && AcceptedResult is not null;

    public override string ToString() =>
        $"{nameof(EvaluationUnitResult)} {{ SequenceNumber = {Item.SequenceNumber.ToString(CultureInfo.InvariantCulture)}, StatusCode = {StatusCode}, AttemptCount = {AttemptCount.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed class EvaluationScheduleResult
{
    internal EvaluationScheduleResult(
        EvaluationPlan plan,
        ImmutableArray<EvaluationUnitResult> units)
    {
        Plan = plan;
        Units = units;
    }

    public EvaluationPlan Plan { get; }

    public ImmutableArray<EvaluationUnitResult> Units { get; }

    public bool IsCancelled => Units.Any(unit => string.Equals(
        unit.StatusCode,
        ResultsStatusCodes.Cancelled,
        StringComparison.Ordinal));

    public string StatusCode => IsCancelled
        ? ResultsStatusCodes.Cancelled
        : ResultsStatusCodes.Success;

    public int CompletedCount => Units.Count(unit => !string.Equals(
        unit.StatusCode,
        ResultsStatusCodes.Cancelled,
        StringComparison.Ordinal));

    public override string ToString() =>
        $"{nameof(EvaluationScheduleResult)} {{ StatusCode = {StatusCode}, TotalCount = {Units.Length.ToString(CultureInfo.InvariantCulture)}, CompletedCount = {CompletedCount.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed class EvaluationScheduler
{
    private readonly IEvaluationRowSource rowSource;
    private readonly IEvaluationRunner runner;
    private readonly SafeEvaluationPayloadBuilder payloadBuilder;
    private readonly QuantificationResultValidator resultValidator;
    private readonly EvaluationRequestCapacityValidator requestCapacityValidator;

    public EvaluationScheduler(
        IEvaluationRowSource rowSource,
        IEvaluationRunner runner)
    {
        this.rowSource = rowSource ?? throw new ArgumentNullException(nameof(rowSource));
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        payloadBuilder = new SafeEvaluationPayloadBuilder();
        resultValidator = new QuantificationResultValidator();
        requestCapacityValidator = new EvaluationRequestCapacityValidator();
    }

    /// <summary>
    /// Executes a deterministic plan with bounded concurrency. Progress callbacks are observers;
    /// their exceptions are deliberately ignored so UI observer failures cannot corrupt a run.
    /// </summary>
    public async Task<EvaluationScheduleResult> RunAsync(
        EvaluationPlan plan,
        string modelId,
        int maxConcurrency = EvaluationSchedulerOptions.DefaultMaxConcurrency,
        Action<EvaluationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        EvaluationSchedulerOptions options = new(maxConcurrency);

        int total = plan.Items.Length;
        EvaluationUnitResult?[] results = new EvaluationUnitResult?[total];
        Dictionary<int, Task<EvaluationUnitResult>> inFlight = [];
        int nextIndex = 0;
        int completed = 0;
        ReportSafely(
            progress,
            new EvaluationProgress(
                total,
                completed,
                inFlight.Count,
                cancellationToken.IsCancellationRequested
                    ? EvaluationProgressStatus.Cancelling
                    : EvaluationProgressStatus.Running));

        while (nextIndex < total || inFlight.Count > 0)
        {
            while (nextIndex < total
                && inFlight.Count < options.MaxConcurrency
                && !cancellationToken.IsCancellationRequested)
            {
                int itemIndex = nextIndex++;
                Task<EvaluationUnitResult> task = ExecuteItemAsync(
                    plan,
                    plan.Items[itemIndex],
                    modelId,
                    cancellationToken);
                inFlight.Add(itemIndex, task);
                ReportSafely(
                    progress,
                    new EvaluationProgress(
                        total,
                        completed,
                        inFlight.Count,
                        EvaluationProgressStatus.Running));
            }

            if (inFlight.Count == 0)
            {
                break;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                ReportSafely(
                    progress,
                    new EvaluationProgress(
                        total,
                        completed,
                        inFlight.Count,
                        EvaluationProgressStatus.Cancelling));
            }

            KeyValuePair<int, Task<EvaluationUnitResult>>[] ready = inFlight
                .Where(entry => entry.Value.IsCompleted)
                .OrderBy(entry => entry.Key)
                .ToArray();
            if (ready.Length == 0)
            {
                _ = await Task.WhenAny(inFlight.Values).ConfigureAwait(false);
                ready = inFlight
                    .Where(entry => entry.Value.IsCompleted)
                    .OrderBy(entry => entry.Key)
                    .ToArray();
            }

            foreach ((int itemIndex, Task<EvaluationUnitResult> task) in ready)
            {
                EvaluationUnitResult result;
                try
                {
                    result = await task.ConfigureAwait(false);
                }
                catch
                {
                    result = RuntimeFailure(plan.Items[itemIndex]);
                }

                results[itemIndex] = result;
                inFlight.Remove(itemIndex);
                completed++;
                ReportSafely(
                    progress,
                    new EvaluationProgress(
                        total,
                        completed,
                        inFlight.Count,
                        cancellationToken.IsCancellationRequested
                            ? EvaluationProgressStatus.Cancelling
                            : EvaluationProgressStatus.Running));
            }
        }

        while (nextIndex < total)
        {
            EvaluationPlanItem item = plan.Items[nextIndex];
            results[nextIndex] = Cancelled(item, scorable: false, scorableKnown: false);
            nextIndex++;
            completed++;
        }

        ImmutableArray<EvaluationUnitResult> ordered = results
            .Select((result, index) => result ?? RuntimeFailure(plan.Items[index]))
            .ToImmutableArray();
        EvaluationScheduleResult scheduleResult = new(plan, ordered);
        ReportSafely(
            progress,
            new EvaluationProgress(
                total,
                total,
                0,
                scheduleResult.IsCancelled
                    ? EvaluationProgressStatus.Cancelled
                    : EvaluationProgressStatus.Completed));
        return scheduleResult;
    }

    private async Task<EvaluationUnitResult> ExecuteItemAsync(
        EvaluationPlan plan,
        EvaluationPlanItem item,
        string modelId,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(item, scorable: false, scorableKnown: false);
        }

        EvaluationRowData row;
        try
        {
            EvaluationRowRequest request = new(
                plan.Snapshot.Definition.SourceSheet,
                item.SourceRowNumber,
                item.SelectedSourceColumns);
            Task<EvaluationRowData> readTask = rowSource.ReadAsync(request, cancellationToken)
                ?? throw new InvalidOperationException("The row source returned no task.");
            row = await readTask.ConfigureAwait(false)
                ?? throw new InvalidOperationException("The row source returned no row.");
            if (row.SourceRowNumber != item.SourceRowNumber)
            {
                return RuntimeFailure(item);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(item, scorable: false, scorableKnown: false);
        }
        catch
        {
            return RuntimeFailure(item);
        }

        return await ExecuteItemAsync(
            plan,
            item,
            row,
            modelId,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<EvaluationUnitResult> ExecuteItemAsync(
        EvaluationPlan plan,
        EvaluationPlanItem item,
        EvaluationRowData row,
        string modelId,
        CancellationToken cancellationToken,
        int? maximumPromptTokens = null,
        int? maximumContextWindowTokens = null,
        Action<SafeEvaluationPayload>? payloadBuilt = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        if (row.SourceRowNumber != item.SourceRowNumber)
        {
            return RuntimeFailure(item);
        }

        Dictionary<string, string?> selectedCells = new(StringComparer.OrdinalIgnoreCase);
        foreach (string column in item.SelectedSourceColumns)
        {
            selectedCells[column] = row.Cells.TryGetValue(column, out string? value)
                ? value
                : string.Empty;
        }

        bool scorable = !string.IsNullOrWhiteSpace(selectedCells[item.PrimarySourceColumn]);
        if (!scorable)
        {
            return new EvaluationUnitResult(
                item,
                ResultsStatusCodes.Empty,
                null,
                attemptCount: 0,
                scorable: false,
                scorableKnown: true,
                EvaluationTokenUsage.Unavailable);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(item, scorable: true, scorableKnown: true);
        }

        SafeEvaluationPayload payload;
        try
        {
            payload = payloadBuilder.Build(
                plan.Snapshot,
                item.QuestionId,
                item.EvaluatorId,
                selectedCells);
        }
        catch
        {
            return RuntimeFailure(item, scorable: true, scorableKnown: true);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(item, scorable: true, scorableKnown: true);
        }

        payloadBuilt?.Invoke(payload);

        // The model-relative budget is skipped inside the validator when the SDK publishes no
        // limit, but the model-independent app-owned ceiling always applies.
        if (!requestCapacityValidator.Validate(payload, maximumPromptTokens, maximumContextWindowTokens).IsValid)
        {
            return Failure(item, ResultsStatusCodes.AiOutputInvalid, scorable: true);
        }

        EvaluationRunnerResult runnerResult;
        try
        {
            Task<EvaluationRunnerResult> evaluationTask = runner
                .EvaluateAsync(payload, modelId, cancellationToken)
                ?? throw new InvalidOperationException("The evaluation runner returned no task.");
            runnerResult = await evaluationTask.ConfigureAwait(false)
                ?? throw new InvalidOperationException("The evaluation runner returned no result.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(item, scorable: true, scorableKnown: true);
        }
        catch (EvaluationAttemptException exception)
        {
            return FailureFromAttemptException(item, exception, scorable: true);
        }
        catch (TimeoutException)
        {
            return Failure(item, ResultsStatusCodes.AiTimeout, scorable: true);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or SocketException
            or WebSocketException
            or IOException)
        {
            return Failure(item, ResultsStatusCodes.NetworkFailed, scorable: true);
        }
        catch
        {
            return RuntimeFailure(item, scorable: true, scorableKnown: true);
        }

        if (!runnerResult.IsSuccess)
        {
            return new EvaluationUnitResult(
                item,
                runnerResult.StatusCode,
                null,
                runnerResult.AttemptCount,
                scorable: true,
                scorableKnown: true,
                runnerResult.TokenUsage);
        }

        try
        {
            QuantificationResultValidationOutcome validation = resultValidator.Validate(
                payload,
                runnerResult.AcceptedResult!);
            if (!validation.IsValid)
            {
                return new EvaluationUnitResult(
                    item,
                    ResultsStatusCodes.AiOutputInvalid,
                    null,
                    runnerResult.AttemptCount,
                    scorable: true,
                    scorableKnown: true,
                    runnerResult.TokenUsage);
            }

            return new EvaluationUnitResult(
                item,
                ResultsStatusCodes.Success,
                validation.AcceptedResult,
                runnerResult.AttemptCount,
                scorable: true,
                scorableKnown: true,
                runnerResult.TokenUsage);
        }
        catch
        {
            return RuntimeFailure(
                item,
                scorable: true,
                scorableKnown: true,
                runnerResult.TokenUsage);
        }
    }

    private static EvaluationUnitResult FailureFromAttemptException(
        EvaluationPlanItem item,
        EvaluationAttemptException exception,
        bool scorable) =>
        Failure(
            item,
            exception.FailureKind switch
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
            scorable);

    private static EvaluationUnitResult Failure(
        EvaluationPlanItem item,
        string statusCode,
        bool scorable) =>
        new(
            item,
            statusCode,
            null,
            attemptCount: 0,
            scorable,
            scorableKnown: true,
            EvaluationTokenUsage.Unavailable);

    private static EvaluationUnitResult RuntimeFailure(
        EvaluationPlanItem item,
        bool scorable = false,
        bool scorableKnown = false,
        EvaluationTokenUsage? tokenUsage = null) =>
        new(
            item,
            ResultsStatusCodes.AiRuntimeFailed,
            null,
            attemptCount: 0,
            scorable,
            scorableKnown,
            tokenUsage ?? EvaluationTokenUsage.Unavailable);

    private static EvaluationUnitResult Cancelled(
        EvaluationPlanItem item,
        bool scorable,
        bool scorableKnown) =>
        new(
            item,
            ResultsStatusCodes.Cancelled,
            null,
            attemptCount: 0,
            scorable,
            scorableKnown,
            EvaluationTokenUsage.Unavailable);

    private static void ReportSafely(
        Action<EvaluationProgress>? progress,
        EvaluationProgress value)
    {
        if (progress is null)
        {
            return;
        }

        try
        {
            progress(value);
        }
        catch
        {
            // Progress is an observer only. A UI callback failure must not alter run data.
        }
    }
}
