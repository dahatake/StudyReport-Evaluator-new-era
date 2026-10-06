using System.Collections.Immutable;
using System.Globalization;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;

namespace StudyReportEvaluator.App.Workflow;

public interface IInputSnapshotBoundary
{
    InputSnapshot Capture(string inputPath);

    bool IsUnchanged(string inputPath, InputSnapshot expected);
}

public sealed class PhysicalInputSnapshotBoundary : IInputSnapshotBoundary
{
    private readonly InputSnapshotService service = new();

    public InputSnapshot Capture(string inputPath) => service.Capture(inputPath);

    public bool IsUnchanged(string inputPath, InputSnapshot expected) =>
        service.Recheck(inputPath, expected).IsMatch;

    public override string ToString() =>
        $"{nameof(PhysicalInputSnapshotBoundary)} {{ Content = <redacted> }}";
}

public sealed class QuantificationMappingValidationException : Exception
{
    public QuantificationMappingValidationException(
        IEnumerable<ColumnMappingValidationError> errors)
        : this(errors?.ToImmutableArray()
            ?? throw new ArgumentNullException(nameof(errors)))
    {
    }

    private QuantificationMappingValidationException(
        ImmutableArray<ColumnMappingValidationError> errors)
        : base($"The quantification mapping has {errors.Length.ToString(CultureInfo.InvariantCulture)} validation error(s).")
    {
        Errors = errors;
    }

    public ImmutableArray<ColumnMappingValidationError> Errors { get; }

    public override string ToString() =>
        $"{nameof(QuantificationMappingValidationException)} {{ ErrorCount = {Errors.Length.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed class QuantificationRunException : Exception
{
    public QuantificationRunException(string code)
        : base("The quantification run could not be started.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }

    public override string ToString() =>
        $"{nameof(QuantificationRunException)} {{ Code = {Code}, Content = <redacted> }}";
}

public sealed class QuantificationRunPreflightException : Exception
{
    public QuantificationRunPreflightException(IEnumerable<ExecutionCapacityError> errors)
        : this(errors?.ToImmutableArray()
            ?? throw new ArgumentNullException(nameof(errors)))
    {
    }

    private QuantificationRunPreflightException(
        ImmutableArray<ExecutionCapacityError> errors)
        : base($"The quantification run has {errors.Length.ToString(CultureInfo.InvariantCulture)} capacity preflight error(s).")
    {
        if (errors.IsEmpty)
        {
            throw new ArgumentException("At least one capacity preflight error is required.", nameof(errors));
        }

        Errors = errors;
    }

    public ImmutableArray<ExecutionCapacityError> Errors { get; }

    public override string ToString() =>
        $"{nameof(QuantificationRunPreflightException)} {{ ErrorCount = {Errors.Length.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed record QuantificationRunRequest
{
    public required QuantificationDefinition DraftDefinition { get; init; }

    public required WorkbookMetadata WorkbookMetadata { get; init; }

    public required string InputPath { get; init; }

    public required string ModelId { get; init; }

    public string? ReasoningEffort { get; init; }

    public string? ContextTier { get; init; }

    /// null は SDK が当該 model の上限を公開していないことを表す。
    public int? MaximumPromptTokens { get; init; }

    public int? MaximumContextWindowTokens { get; init; }

    public int MaxConcurrency { get; init; } = EvaluationSchedulerOptions.DefaultMaxConcurrency;

    public CopilotRuntimeIdentity? RuntimeIdentity { get; init; }

    public string? OutputDirectory { get; init; }

    public string? ResumePartialPath { get; init; }

    public bool UseDurableWorkflow { get; init; }

    public override string ToString() =>
        $"{nameof(QuantificationRunRequest)} {{ MaxConcurrency = {MaxConcurrency.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed class QuantificationOrchestrator
{
    private readonly IEvaluationRowSource rowSource;
    private readonly IInputSnapshotBoundary inputSnapshots;
    private readonly EvaluationPlanBuilder planBuilder;
    private readonly ColumnMappingValidator mappingValidator;
    private readonly WorkbookExecutionPreflight workbookPreflight;
    private readonly SafeEvaluationPayloadBuilder payloadBuilder;
    private readonly EvaluationRequestCapacityValidator requestCapacityValidator;
    private readonly EvaluationScheduler scheduler;
    private readonly TimeProvider timeProvider;

    public QuantificationOrchestrator(
        IEvaluationRowSource rowSource,
        IEvaluationRunner runner)
        : this(
            rowSource,
            runner,
            new PhysicalInputSnapshotBoundary(),
            TimeProvider.System)
    {
    }

    public QuantificationOrchestrator(
        IEvaluationRowSource rowSource,
        IEvaluationRunner runner,
        IInputSnapshotBoundary inputSnapshots,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(rowSource);
        ArgumentNullException.ThrowIfNull(runner);
        this.rowSource = rowSource;
        this.inputSnapshots = inputSnapshots
            ?? throw new ArgumentNullException(nameof(inputSnapshots));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        planBuilder = new EvaluationPlanBuilder();
        mappingValidator = new ColumnMappingValidator();
        workbookPreflight = new WorkbookExecutionPreflight();
        payloadBuilder = new SafeEvaluationPayloadBuilder();
        requestCapacityValidator = new EvaluationRequestCapacityValidator();
        scheduler = new EvaluationScheduler(rowSource, runner);
    }

    public async Task<RunSummary> RunAsync(
        QuantificationRunRequest request,
        Action<EvaluationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.DraftDefinition);
        ArgumentNullException.ThrowIfNull(request.WorkbookMetadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelId);
        _ = new EvaluationSchedulerOptions(request.MaxConcurrency);

        DateTimeOffset startedAtUtc = timeProvider.GetUtcNow();

        // The draft is read exactly once. Every downstream contract receives this frozen instance.
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(request.DraftDefinition);
        ColumnMappingValidationResult mappingValidation = mappingValidator.Validate(
            request.WorkbookMetadata,
            snapshot.Definition);
        if (!mappingValidation.IsValid || mappingValidation.Mapping is null)
        {
            throw new QuantificationMappingValidationException(mappingValidation.Errors);
        }

        WorkbookExecutionPreflightResult capacity = workbookPreflight.Validate(
            snapshot,
            request.WorkbookMetadata);
        if (!capacity.IsValid)
        {
            throw new QuantificationRunPreflightException(capacity.Errors);
        }

        EvaluationPlan plan = planBuilder.Build(snapshot, mappingValidation.Mapping);
        ValidateStaticRequestCapacity(request, plan);

        InputSnapshot inputSnapshot;
        try
        {
            inputSnapshot = inputSnapshots.Capture(request.InputPath)
                ?? throw new InvalidOperationException("The input snapshot boundary returned no snapshot.");
        }
        catch (QuantificationRunException)
        {
            throw;
        }
        catch
        {
            throw new QuantificationRunException("INPUT_SNAPSHOT_FAILED");
        }

        await ValidateRequestCapacityAsync(
            plan,
            request.MaximumPromptTokens,
            request.MaximumContextWindowTokens,
            cancellationToken).ConfigureAwait(false);
        if (!SafeInputRecheck(request.InputPath, inputSnapshot))
        {
            throw new QuantificationRunPreflightException(
            [
                new ExecutionCapacityError(
                    "INPUT_CHANGED_BEFORE_DISPATCH",
                    "Definition",
                    snapshot.Definition.Id,
                    snapshot.Definition.Name,
                    "InputIdentity",
                    "changed",
                    "exact snapshot match"),
            ]);
        }

        EvaluationScheduleResult schedule = await scheduler
            .RunAsync(
                plan,
                request.ModelId,
                request.MaxConcurrency,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        bool inputUnchanged;
        try
        {
            inputUnchanged = inputSnapshots.IsUnchanged(
                request.InputPath,
                inputSnapshot);
        }
        catch
        {
            inputUnchanged = false;
        }

        string statusCode = !inputUnchanged
            ? QuantificationRunStatusCodes.InputChanged
            : schedule.IsCancelled
                ? QuantificationRunStatusCodes.Cancelled
                : QuantificationRunStatusCodes.Success;
        DateTimeOffset endedAtUtc = timeProvider.GetUtcNow();
        if (endedAtUtc < startedAtUtc)
        {
            endedAtUtc = startedAtUtc;
        }

        return new RunSummary(
            schedule,
            inputSnapshot,
            statusCode,
            startedAtUtc,
            endedAtUtc);
    }

    private static void ValidateStaticRequestCapacity(
        QuantificationRunRequest request,
        EvaluationPlan plan)
    {
        ImmutableArray<ExecutionCapacityError>.Builder errors =
            ImmutableArray.CreateBuilder<ExecutionCapacityError>();
        if (request.MaximumPromptTokens is int promptLimit && promptLimit <= 0)
        {
            errors.Add(new ExecutionCapacityError(
                "MODEL_PROMPT_LIMIT_UNAVAILABLE",
                "Definition",
                plan.Snapshot.Definition.Id,
                plan.Snapshot.Definition.Name,
                "MaximumPromptTokens",
                promptLimit.ToString(CultureInfo.InvariantCulture),
                "positive SDK model limit"));
        }

        if (request.MaximumContextWindowTokens is int contextLimit && contextLimit <= 0)
        {
            errors.Add(new ExecutionCapacityError(
                "MODEL_CONTEXT_LIMIT_UNAVAILABLE",
                "Definition",
                plan.Snapshot.Definition.Id,
                plan.Snapshot.Definition.Name,
                "MaximumContextWindowTokens",
                contextLimit.ToString(CultureInfo.InvariantCulture),
                "positive SDK model limit"));
        }

        long worstCaseAttempts;
        try
        {
            worstCaseAttempts = checked(
                (long)plan.TotalCount * RetryAndCleanupCoordinator.MaximumTransientAttempts);
        }
        catch (OverflowException)
        {
            worstCaseAttempts = long.MaxValue;
        }

        if (worstCaseAttempts > EvaluationRequestCapacityValidator.MaximumWorstCaseAttemptsPerRun)
        {
            errors.Add(new ExecutionCapacityError(
                "ATTEMPT_BUDGET_TOO_LARGE",
                "Definition",
                plan.Snapshot.Definition.Id,
                plan.Snapshot.Definition.Name,
                "WorstCaseAttempts",
                worstCaseAttempts.ToString(CultureInfo.InvariantCulture),
                EvaluationRequestCapacityValidator.MaximumWorstCaseAttemptsPerRun.ToString(CultureInfo.InvariantCulture)));
        }

        if (errors.Count > 0)
        {
            throw new QuantificationRunPreflightException(errors.ToImmutable());
        }
    }

    private async Task ValidateRequestCapacityAsync(
        EvaluationPlan plan,
        int? maximumPromptTokens,
        int? maximumContextWindowTokens,
        CancellationToken cancellationToken)
    {
        ImmutableArray<ExecutionCapacityError>.Builder errors =
            ImmutableArray.CreateBuilder<ExecutionCapacityError>();
        foreach (IGrouping<int, EvaluationPlanItem> rowItems in plan.Items.GroupBy(
                     item => item.SourceRowNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EvaluationPlanItem[] items = rowItems.ToArray();
            string[] selectedColumns = items
                .SelectMany(item => item.SelectedSourceColumns)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            EvaluationRowData row;
            try
            {
                EvaluationRowRequest rowRequest = new(
                    plan.Snapshot.Definition.SourceSheet,
                    rowItems.Key,
                    selectedColumns);
                Task<EvaluationRowData> readTask = rowSource.ReadAsync(
                    rowRequest,
                    cancellationToken)
                    ?? throw new InvalidOperationException("The row source returned no task.");
                row = await readTask.ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The row source returned no row.");
                if (row.SourceRowNumber != rowItems.Key)
                {
                    throw new InvalidDataException("The row source returned a different row.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                throw new QuantificationRunPreflightException(
                [
                    new ExecutionCapacityError(
                        "REQUEST_PREFLIGHT_ROW_READ_FAILED",
                        "Definition",
                        plan.Snapshot.Definition.Id,
                        plan.Snapshot.Definition.Name,
                        "SourceRowNumber",
                        rowItems.Key.ToString(CultureInfo.InvariantCulture),
                        "readable selected row"),
                ]);
            }

            foreach (EvaluationPlanItem item in items)
            {
                Dictionary<string, string?> selectedCells = new(StringComparer.OrdinalIgnoreCase);
                foreach (string column in item.SelectedSourceColumns)
                {
                    selectedCells[column] = row.Cells.TryGetValue(column, out string? value)
                        ? value
                        : string.Empty;
                }

                if (string.IsNullOrWhiteSpace(selectedCells[item.PrimarySourceColumn]))
                {
                    continue;
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
                    throw new QuantificationRunPreflightException(
                    [
                        new ExecutionCapacityError(
                            "REQUEST_PREFLIGHT_BUILD_FAILED",
                            "Evaluator",
                            item.EvaluatorId,
                            EvaluatorDisplayName(plan.Snapshot.Definition, item),
                            "RequestPayload",
                            "invalid",
                            "renderable closed payload"),
                    ]);
                }

                EvaluationRequestCapacityResult capacity = requestCapacityValidator.Validate(
                    payload,
                    maximumPromptTokens,
                    maximumContextWindowTokens);
                foreach (EvaluationRequestCapacityError error in capacity.Errors)
                {
                    errors.Add(new ExecutionCapacityError(
                        error.Code,
                        "Evaluator",
                        item.EvaluatorId,
                        EvaluatorDisplayName(plan.Snapshot.Definition, item),
                        error.Field,
                        error.ActualDimension.ToString(CultureInfo.InvariantCulture),
                        error.Limit.ToString(CultureInfo.InvariantCulture)));
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new QuantificationRunPreflightException(errors.ToImmutable());
        }
    }

    private bool SafeInputRecheck(string inputPath, InputSnapshot expected)
    {
        try
        {
            return inputSnapshots.IsUnchanged(inputPath, expected);
        }
        catch
        {
            return false;
        }
    }

    private static string EvaluatorDisplayName(
        QuantificationDefinition definition,
        EvaluationPlanItem item) =>
        definition.Questions
            .Single(question => string.Equals(question.Id, item.QuestionId, StringComparison.Ordinal))
            .Evaluators
            .Single(evaluator => string.Equals(evaluator.Id, item.EvaluatorId, StringComparison.Ordinal))
            .DisplayName;

    public override string ToString() =>
        $"{nameof(QuantificationOrchestrator)} {{ Content = <redacted> }}";
}
