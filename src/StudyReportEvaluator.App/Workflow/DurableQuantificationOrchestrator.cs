using System.Collections.Immutable;
using System.Globalization;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Similarity;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.App.Workflow;

public static class CheckpointAdmissionStatusCodes
{
    public const string InputMismatch = "CHECKPOINT_INPUT_MISMATCH";
    public const string DefinitionMismatch = "CHECKPOINT_DEFINITION_MISMATCH";
    public const string ModelMismatch = "CHECKPOINT_MODEL_MISMATCH";
    public const string RuntimeMismatch = "CHECKPOINT_RUNTIME_MISMATCH";
}

public enum DurableEvaluationStage
{
    Preparing,
    GeneratingReferences,
    EvaluatingRows,
    SavingCheckpoint,
    FinalizingWorkbook,
    Completed,
    Cancelling,
}

public sealed record DurableEvaluationProgress
{
    public DurableEvaluationProgress(
        DurableEvaluationStage stage,
        int referenceCompleted,
        int referenceTotal,
        int rowCompleted,
        int rowTotal,
        int operationCompleted,
        int operationTotal,
        int inFlight,
        string statusCode,
        string? finalPath = null,
        string? partialPath = null)
    {
        if (referenceCompleted < 0
            || referenceCompleted > referenceTotal
            || referenceTotal < 0
            || rowCompleted < 0
            || rowCompleted > rowTotal
            || rowTotal < 0
            || operationCompleted < 0
            || operationCompleted > operationTotal
            || operationTotal < 0
            || inFlight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(operationCompleted));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(statusCode);
        Stage = stage;
        ReferenceCompleted = referenceCompleted;
        ReferenceTotal = referenceTotal;
        RowCompleted = rowCompleted;
        RowTotal = rowTotal;
        OperationCompleted = operationCompleted;
        OperationTotal = operationTotal;
        InFlight = inFlight;
        StatusCode = statusCode;
        FinalPath = finalPath;
        PartialPath = partialPath;
    }

    public DurableEvaluationStage Stage { get; }

    public int ReferenceCompleted { get; }

    public int ReferenceTotal { get; }

    public int RowCompleted { get; }

    public int RowTotal { get; }

    public int OperationCompleted { get; }

    public int OperationTotal { get; }

    public int InFlight { get; }

    public string StatusCode { get; }

    public string? FinalPath { get; init; }

    public string? PartialPath { get; init; }

    public override string ToString() =>
        $"{nameof(DurableEvaluationProgress)} {{ Stage = {Stage}, ReferenceCompleted = {ReferenceCompleted}, RowCompleted = {RowCompleted}, OperationCompleted = {OperationCompleted}, InFlight = {InFlight}, StatusCode = {StatusCode}, Content = <redacted> }}";
}

public sealed record DurableQuantificationRunRequest
{
    public required QuantificationRunRequest Run { get; init; }

    public required CheckpointRuntimeIdentity Runtime { get; init; }

    public string? OutputDirectory { get; init; }

    public string? ResumePartialPath { get; init; }

    public override string ToString() =>
        $"{nameof(DurableQuantificationRunRequest)} {{ IsResume = {ResumePartialPath is not null}, Content = <redacted> }}";
}

public sealed class DurableQuantificationOrchestrator
{
    private readonly IEvaluationRowSource rowSource;
    private readonly IReferenceAnswerOperationRunner referenceRunner;
    private readonly DurableEvaluationScheduler rowScheduler;
    private readonly IInputSnapshotBoundary inputSnapshots;
    private readonly ICheckpointStore checkpointStore;
    private readonly IOutputPathPlanner outputPathPlanner;
    private readonly IDurableRunFinalizer finalizer;
    private readonly IPartialCheckpointCleaner partialCleaner;
    private readonly TimeProvider timeProvider;
    private readonly EvaluationPlanBuilder planBuilder = new();
    private readonly ColumnMappingValidator mappingValidator = new();
    private readonly WorkbookExecutionPreflight workbookPreflight = new();
    private readonly SafeEvaluationPayloadBuilder payloadBuilder = new();
    private readonly AuxiliaryQuantificationResultValidator auxiliaryValidator = new();
    private readonly QuantificationResultValidator normalResultValidator = new();
    private readonly EvaluationRequestCapacityValidator requestCapacityValidator = new();
    private readonly SurfaceTextSimilarityCalculator similarityCalculator = new();

    public DurableQuantificationOrchestrator(
        IEvaluationRowSource rowSource,
        IEvaluationRunner normalRunner,
        IReferenceAnswerOperationRunner referenceRunner,
        ISpecialEvaluationOperationRunner specialRunner)
        : this(
            rowSource,
            normalRunner,
            referenceRunner,
            specialRunner,
            new PhysicalInputSnapshotBoundary(),
            new CheckpointStore(),
            new OutputPathPlanner(),
            new WorkbookDurableRunFinalizer(),
            new PhysicalPartialCheckpointCleaner(),
            TimeProvider.System)
    {
    }

    public DurableQuantificationOrchestrator(
        IEvaluationRowSource rowSource,
        IEvaluationRunner normalRunner,
        IReferenceAnswerOperationRunner referenceRunner,
        ISpecialEvaluationOperationRunner specialRunner,
        IInputSnapshotBoundary inputSnapshots,
        ICheckpointStore checkpointStore,
        IOutputPathPlanner outputPathPlanner,
        IDurableRunFinalizer finalizer,
        IPartialCheckpointCleaner partialCleaner,
        TimeProvider? timeProvider = null)
    {
        this.rowSource = rowSource ?? throw new ArgumentNullException(nameof(rowSource));
        ArgumentNullException.ThrowIfNull(normalRunner);
        this.referenceRunner = referenceRunner ?? throw new ArgumentNullException(nameof(referenceRunner));
        ArgumentNullException.ThrowIfNull(specialRunner);
        this.inputSnapshots = inputSnapshots ?? throw new ArgumentNullException(nameof(inputSnapshots));
        this.checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
        this.outputPathPlanner = outputPathPlanner ?? throw new ArgumentNullException(nameof(outputPathPlanner));
        this.finalizer = finalizer ?? throw new ArgumentNullException(nameof(finalizer));
        this.partialCleaner = partialCleaner ?? throw new ArgumentNullException(nameof(partialCleaner));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        rowScheduler = new DurableEvaluationScheduler(
            rowSource,
            normalRunner,
            specialRunner);
    }

    public async Task<RunSummary> RunAsync(
        DurableQuantificationRunRequest request,
        Action<DurableEvaluationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        AdaptiveEvaluationConcurrencyLimiter? concurrencyLimiter = null,
        Action<LivePreviewUpdate>? livePreview = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Run);
        ArgumentNullException.ThrowIfNull(request.Runtime);
        QuantificationRunRequest run = request.Run;
        ArgumentNullException.ThrowIfNull(run.DraftDefinition);
        ArgumentNullException.ThrowIfNull(run.WorkbookMetadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.InputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.ModelId);
        EphemeralEvaluationRunner.ValidateReasoningEffort(run.ReasoningEffort);
        _ = ModelOptionPolicy.ToSdkContextTier(run.ContextTier);
        _ = new EvaluationSchedulerOptions(run.MaxConcurrency);

        Report(progress, DurableEvaluationStage.Preparing, 0, 0, 0, 0, 0, 0, 0, "PREPARING");
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(run.DraftDefinition);
        ColumnMappingValidationResult mappingValidation = mappingValidator.Validate(
            run.WorkbookMetadata,
            snapshot.Definition);
        if (!mappingValidation.IsValid || mappingValidation.Mapping is null)
        {
            throw new QuantificationMappingValidationException(mappingValidation.Errors);
        }

        WorkbookExecutionPreflightResult capacity = workbookPreflight.Validate(
            snapshot,
            run.WorkbookMetadata);
        if (!capacity.IsValid)
        {
            throw new QuantificationRunPreflightException(capacity.Errors);
        }

        EvaluationPlan plan = planBuilder.Build(snapshot, mappingValidation.Mapping);
        ValidateStaticCapacity(run, plan);
        InputSnapshot currentInput;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentInput = inputSnapshots.Capture(run.InputPath)
                ?? throw new InvalidOperationException("The input snapshot boundary returned no snapshot.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new QuantificationRunException("INPUT_SNAPSHOT_FAILED");
        }

        CheckpointEnvelope checkpoint;
        bool wasResumed;
        if (request.ResumePartialPath is null)
        {
            DateTimeOffset startedAtUtc = UtcNow(default);
            OutputPathReservation reservation = outputPathPlanner.Reserve(
                run.InputPath,
                timeProvider.GetLocalNow(),
                request.OutputDirectory);
            checkpoint = new CheckpointEnvelope
            {
                InputPath = Path.GetFullPath(run.InputPath),
                Input = currentInput,
                DefinitionCanonicalJson = snapshot.CanonicalJson,
                DefinitionSha256 = snapshot.Sha256,
                NormalModelId = run.ModelId,
                ReferenceModelId = run.ModelId,
                ReasoningEffort = run.ReasoningEffort,
                ContextTier = run.ContextTier,
                Runtime = request.Runtime,
                FinalPath = reservation.FinalPath,
                PartialPath = reservation.PartialPath,
                StartedAtUtc = startedAtUtc,
                SavedAtUtc = startedAtUtc,
            };
            cancellationToken.ThrowIfCancellationRequested();
            CheckpointSaveResult created = checkpointStore.Create(checkpoint, cancellationToken);
            if (!created.IsSuccess)
            {
                throw new QuantificationRunException(created.Code);
            }

            wasResumed = false;
        }
        else
        {
            CheckpointLoadResult loaded = checkpointStore.Load(
                request.ResumePartialPath,
                cancellationToken);
            if (!loaded.IsSuccess || loaded.Envelope is null)
            {
                throw new QuantificationRunException(loaded.Code);
            }

            checkpoint = loaded.Envelope;
            string? admissionError = ResumeAdmissionEvaluator.Evaluate(
                checkpoint,
                request.ResumePartialPath,
                snapshot,
                plan,
                currentInput,
                run.InputPath,
                run.ModelId,
                run.ReasoningEffort,
                request.Runtime,
                run.ContextTier).BlockingStatusCode;
            if (admissionError is not null)
            {
                throw new QuantificationRunException(admissionError);
            }

            if (!await ValidateCompletedRowsAsync(
                    snapshot,
                    plan,
                    checkpoint,
                    cancellationToken).ConfigureAwait(false))
            {
                throw new QuantificationRunException(CheckpointStatusCodes.Invalid);
            }

            wasResumed = true;
        }

        int referenceTotal = snapshot.Definition.Questions.Count(question => question.Enabled);
        int rowTotal = plan.Mapping.SelectedRowCount;
        int perRowOperations = OperationsPerRow(plan);
        int operationTotal = checked(referenceTotal + (rowTotal * perRowOperations));
        string? terminalCode = null;
        Action<DurableEvaluationProgress>? boundProgress = progress is null
            ? null
            : value => progress(value with
            {
                FinalPath = checkpoint.FinalPath,
                PartialPath = checkpoint.PartialPath,
            });
        ImmutableArray<CheckpointReference>.Builder references = checkpoint.References.ToBuilder();
        ImmutableArray<CheckpointCompletedRow>.Builder completedRows = checkpoint.CompletedRows.ToBuilder();
        Action<LivePreviewRow>? rowPreview = null;
        if (livePreview is not null)
        {
            int firstDataRow = plan.Mapping.FirstDataRow;
            int lastDataRow = plan.Mapping.LastDataRow;
            int itemsPerRow = plan.Items.Count(item => item.SourceRowNumber == firstDataRow)
                + snapshot.Definition.Questions
                    .Where(question => question.Enabled)
                    .Sum(question => 1 + question.SpecialEvaluations.Count(special => special.Enabled));
            rowPreview = row => ReportPreview(
                livePreview,
                new LivePreviewUpdate
                {
                    FirstDataRow = firstDataRow,
                    LastDataRow = lastDataRow,
                    ItemsPerRow = itemsPerRow,
                    Row = row,
                });
            ReportPreview(
                livePreview,
                new LivePreviewUpdate
                {
                    FirstDataRow = firstDataRow,
                    LastDataRow = lastDataRow,
                    ItemsPerRow = itemsPerRow,
                });
            foreach (CheckpointCompletedRow restored in completedRows)
            {
                rowPreview(LivePreviewItems.Restored(snapshot.Definition, restored));
            }
        }

        for (int index = references.Count; index < referenceTotal; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            QuestionDefinition question = snapshot.Definition.Questions
                .Where(item => item.Enabled)
                .ElementAt(index);
            ReportCurrent(boundProgress, DurableEvaluationStage.GeneratingReferences, references.Count, referenceTotal, completedRows.Count, rowTotal, completedRows.Count * perRowOperations + references.Count, operationTotal, 1, "GENERATING_REFERENCE");
            CheckpointReference reference = await EvaluateReferenceAsync(
                snapshot,
                question,
                checkpoint.StartedAtUtc,
                run.ModelId,
                run.ReasoningEffort,
                run.MaximumPromptTokens,
                run.MaximumContextWindowTokens,
                cancellationToken).ConfigureAwait(false);
            // Quota exhaustion is not persisted so that a resume regenerates the reference.
            if (string.Equals(reference.StatusCode, ResultsStatusCodes.Cancelled, StringComparison.Ordinal)
                || string.Equals(reference.StatusCode, ResultsStatusCodes.QuotaExhausted, StringComparison.Ordinal))
            {
                break;
            }

            references.Add(reference);
            checkpoint = checkpoint with
            {
                References = references.ToImmutable(),
                SavedAtUtc = UtcNow(reference.GeneratedAtUtc),
            };
            ReportCurrent(boundProgress, DurableEvaluationStage.SavingCheckpoint, references.Count, referenceTotal, completedRows.Count, rowTotal, completedRows.Count * perRowOperations + references.Count, operationTotal, 0, "SAVING_CHECKPOINT");
            CheckpointSaveResult saved = checkpointStore.Update(checkpoint, CancellationToken.None);
            if (!saved.IsSuccess)
            {
                references.RemoveAt(references.Count - 1);
                checkpoint = checkpoint with { References = references.ToImmutable() };
                terminalCode = saved.Code;
                break;
            }
        }

        if (terminalCode is null && references.Count == referenceTotal)
        {
            Dictionary<string, CheckpointReference> referencesByQuestion = references.ToDictionary(
                reference => reference.QuestionId,
                StringComparer.Ordinal);
            concurrencyLimiter ??= new AdaptiveEvaluationConcurrencyLimiter(run.MaxConcurrency);
            using CancellationTokenSource rowCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Dictionary<int, Task<DurableRowEvaluationResult>> inFlightRows = [];
            Dictionary<int, CheckpointCompletedRow> readyRows = [];
            Dictionary<int, int> rowInFlight = [];
            object rowInFlightGate = new();
            int nextLaunchRowIndex = completedRows.Count;
            int nextCheckpointRowIndex = completedRows.Count;
            // Bounds rows that are running or finished but not yet checkpointed, so a crash re-runs at most this many rows.
            int pipelineLimit = Math.Max(1, run.MaxConcurrency);

            while ((nextLaunchRowIndex < rowTotal || inFlightRows.Count > 0)
                && terminalCode is null)
            {
                while (nextLaunchRowIndex < rowTotal
                    && inFlightRows.Count + readyRows.Count < pipelineLimit
                    && !rowCancellation.IsCancellationRequested)
                {
                    int rowIndex = nextLaunchRowIndex++;
                    int sourceRow = checked(plan.Mapping.FirstDataRow + rowIndex);
                    ReportCurrent(boundProgress, DurableEvaluationStage.EvaluatingRows, references.Count, referenceTotal, completedRows.Count, rowTotal, completedRows.Count * perRowOperations + references.Count, operationTotal, SumInFlight(rowInFlight, rowInFlightGate), "EVALUATING_ROW");
                    Task<DurableRowEvaluationResult> task = rowScheduler.EvaluateRowAsync(
                        plan,
                        sourceRow,
                        referencesByQuestion,
                        run.ModelId,
                        run.ReasoningEffort,
                        run.MaxConcurrency,
                        run.MaximumPromptTokens,
                        run.MaximumContextWindowTokens,
                        inFlight =>
                        {
                            lock (rowInFlightGate)
                            {
                                rowInFlight[rowIndex] = inFlight;
                            }

                            ReportCurrent(
                                boundProgress,
                                rowCancellation.IsCancellationRequested
                                    ? DurableEvaluationStage.Cancelling
                                    : DurableEvaluationStage.EvaluatingRows,
                                references.Count,
                                referenceTotal,
                                completedRows.Count,
                                rowTotal,
                                completedRows.Count * perRowOperations + references.Count,
                                operationTotal,
                                SumInFlight(rowInFlight, rowInFlightGate),
                                rowCancellation.IsCancellationRequested ? "CANCELLING" : "EVALUATING_ROW");
                        },
                        rowCancellation.Token,
                        concurrencyLimiter,
                        rowPreview);
                    inFlightRows.Add(rowIndex, task);
                }

                if (inFlightRows.Count == 0)
                {
                    break;
                }

                Task<DurableRowEvaluationResult> completedTask = await Task
                    .WhenAny(inFlightRows.Values)
                    .ConfigureAwait(false);
                int completedRowIndex = inFlightRows.Single(pair => ReferenceEquals(pair.Value, completedTask)).Key;
                inFlightRows.Remove(completedRowIndex);
                lock (rowInFlightGate)
                {
                    rowInFlight.Remove(completedRowIndex);
                }

                DurableRowEvaluationResult row;
                try
                {
                    row = await completedTask.ConfigureAwait(false);
                }
                catch
                {
                    row = new DurableRowEvaluationResult(
                        checked(plan.Mapping.FirstDataRow + completedRowIndex),
                        null);
                }

                if (!row.IsComplete || row.CompletedRow is null)
                {
                    rowCancellation.Cancel();
                    break;
                }

                readyRows[completedRowIndex] = row.CompletedRow;
                while (readyRows.Remove(nextCheckpointRowIndex, out CheckpointCompletedRow? ready))
                {
                    // A row with quota exhaustion is not persisted, so a resume re-runs it.
                    if (HasQuotaExhausted(ready))
                    {
                        rowCancellation.Cancel();
                        break;
                    }

                    completedRows.Add(ready);
                    checkpoint = checkpoint with
                    {
                        CompletedRows = completedRows.ToImmutable(),
                        SavedAtUtc = UtcNow(checkpoint.SavedAtUtc),
                    };
                    ReportCurrent(boundProgress, DurableEvaluationStage.SavingCheckpoint, references.Count, referenceTotal, completedRows.Count, rowTotal, completedRows.Count * perRowOperations + references.Count, operationTotal, SumInFlight(rowInFlight, rowInFlightGate), "SAVING_CHECKPOINT");
                    CheckpointSaveResult saved = checkpointStore.Update(checkpoint, CancellationToken.None);
                    if (!saved.IsSuccess)
                    {
                        completedRows.RemoveAt(completedRows.Count - 1);
                        checkpoint = checkpoint with { CompletedRows = completedRows.ToImmutable() };
                        terminalCode = saved.Code;
                        rowCancellation.Cancel();
                        break;
                    }

                    nextCheckpointRowIndex++;
                }
            }

            if (rowCancellation.IsCancellationRequested && inFlightRows.Count > 0)
            {
                try { await Task.WhenAll(inFlightRows.Values).ConfigureAwait(false); }
                catch { /* In-flight rows are discarded unless checkpointed as a contiguous prefix. */ }
            }
        }

        ImmutableArray<CheckpointCompletedRow> completedRowsSnapshot = completedRows.ToImmutable();
        EvaluationScheduleResult schedule = BuildSchedule(plan, completedRowsSnapshot);
        string runStatus = terminalCode switch
        {
            CheckpointStatusCodes.InputChanged => QuantificationRunStatusCodes.InputChanged,
            not null => QuantificationRunStatusCodes.CheckpointFailed,
            _ when references.Count != referenceTotal || completedRows.Count != rowTotal => QuantificationRunStatusCodes.Cancelled,
            _ when !SafeInputRecheck(run.InputPath, currentInput) => QuantificationRunStatusCodes.InputChanged,
            _ => QuantificationRunStatusCodes.Success,
        };
        ImmutableArray<RunPeerSimilarityResult> peerSimilarities =
            string.Equals(runStatus, QuantificationRunStatusCodes.Success, StringComparison.Ordinal)
                ? await CalculatePeerSimilaritiesAsync(snapshot, plan, cancellationToken).ConfigureAwait(false)
                : [];
        DateTimeOffset endedAtUtc = UtcNow(checkpoint.StartedAtUtc);
        RunSummary summary = CreateSummary(
            schedule,
            currentInput,
            runStatus,
            checkpoint,
            references.ToImmutable(),
            completedRowsSnapshot,
            endedAtUtc,
            wasResumed,
            finalPath: null,
            finalizationCode: terminalCode,
            partialCleanupFailed: false,
            peerSimilarities);

        if (!string.Equals(runStatus, QuantificationRunStatusCodes.Success, StringComparison.Ordinal))
        {
            ReportCurrent(boundProgress, cancellationToken.IsCancellationRequested ? DurableEvaluationStage.Cancelling : DurableEvaluationStage.Completed, references.Count, referenceTotal, completedRows.Count, rowTotal, summary.CompletedOperationCount, operationTotal, 0, runStatus);
            return summary;
        }

        ReportCurrent(boundProgress, DurableEvaluationStage.FinalizingWorkbook, references.Count, referenceTotal, completedRows.Count, rowTotal, summary.CompletedOperationCount, operationTotal, 0, "FINALIZING_WORKBOOK");
        DurableFinalizationResult finalized = finalizer.Finalize(summary, checkpoint, cancellationToken);
        bool finalPathMatches = finalized.IsSuccess
            && finalized.FinalPath is not null
            && ResumeAdmissionEvaluator.PathsEqual(finalized.FinalPath, checkpoint.FinalPath);
        bool cleanupFailed = false;
        string finalStatus;
        string? finalPath;
        if (finalPathMatches)
        {
            try
            {
                cleanupFailed = !partialCleaner.TryDelete(checkpoint.PartialPath);
            }
            catch
            {
                cleanupFailed = true;
            }

            finalStatus = QuantificationRunStatusCodes.Success;
            finalPath = finalized.FinalPath;
        }
        else
        {
            finalStatus = string.Equals(finalized.Code, AtomicOutputStatusCodes.Cancelled, StringComparison.Ordinal)
                ? QuantificationRunStatusCodes.Cancelled
                : string.Equals(finalized.Code, AtomicOutputStatusCodes.InputChanged, StringComparison.Ordinal)
                    ? QuantificationRunStatusCodes.InputChanged
                    : QuantificationRunStatusCodes.OutputInvalid;
            finalPath = null;
        }

        RunSummary completed = CreateSummary(
            schedule,
            currentInput,
            finalStatus,
            checkpoint,
            references.ToImmutable(),
            completedRowsSnapshot,
            endedAtUtc,
            wasResumed,
            finalPath,
            finalized.Code,
            cleanupFailed,
            peerSimilarities);
        ReportCurrent(boundProgress, DurableEvaluationStage.Completed, references.Count, referenceTotal, completedRows.Count, rowTotal, completed.CompletedOperationCount, operationTotal, 0, finalStatus);
        return completed;
    }

    public override string ToString() =>
        $"{nameof(DurableQuantificationOrchestrator)} {{ Content = <redacted> }}";

    private async Task<CheckpointReference> EvaluateReferenceAsync(
        QuantificationSnapshot snapshot,
        QuestionDefinition question,
        DateTimeOffset startedAtUtc,
        string modelId,
        string? reasoningEffort,
        int? maximumPromptTokens,
        int? maximumContextWindowTokens,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ReferenceFailure(question.Id, ResultsStatusCodes.Cancelled, startedAtUtc);
        }

        SafeReferenceAnswerPayload payload;
        try
        {
            payload = payloadBuilder.BuildReferenceAnswer(snapshot, question.Id);
        }
        catch
        {
            return ReferenceFailure(question.Id, ResultsStatusCodes.AiRuntimeFailed, UtcNow(startedAtUtc));
        }

        try
        {
            if (!requestCapacityValidator.Validate(
                    payload,
                    maximumPromptTokens,
                    maximumContextWindowTokens).IsValid)
            {
                return ReferenceFailure(
                    question.Id,
                    ResultsStatusCodes.AiOutputInvalid,
                    UtcNow(startedAtUtc));
            }

            AuxiliaryOperationResult<ReferenceAnswerResult> result = await referenceRunner
                .EvaluateAsync(payload, modelId, reasoningEffort, cancellationToken)
                .ConfigureAwait(false);
            DateTimeOffset generatedAtUtc = UtcNow(startedAtUtc);
            if (!result.IsSuccess)
            {
                return ReferenceFailure(
                    question.Id,
                    result.StatusCode,
                    generatedAtUtc,
                    result.AttemptCount,
                    result.TokenUsage);
            }

            AuxiliaryResultValidationOutcome<ReferenceAnswerResult> validation =
                auxiliaryValidator.ValidateReference(payload, result.AcceptedResult!);
            return validation.IsValid
                ? new CheckpointReference
                {
                    QuestionId = question.Id,
                    Answer = validation.AcceptedResult!.Answer,
                    StatusCode = ResultsStatusCodes.Success,
                    GeneratedAtUtc = generatedAtUtc,
                    AttemptCount = result.AttemptCount,
                    TokenUsage = DurableOperationConversions.ToCheckpointUsage(result.TokenUsage),
                }
                : ReferenceFailure(
                    question.Id,
                    ResultsStatusCodes.AiOutputInvalid,
                    generatedAtUtc,
                    result.AttemptCount,
                    result.TokenUsage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ReferenceFailure(question.Id, ResultsStatusCodes.Cancelled, UtcNow(startedAtUtc));
        }
        catch (Exception exception)
        {
            return ReferenceFailure(
                question.Id,
                DurableOperationConversions.ClassifyException(exception),
                UtcNow(startedAtUtc));
        }
    }

    private static CheckpointReference ReferenceFailure(
        string questionId,
        string statusCode,
        DateTimeOffset generatedAtUtc,
        int attemptCount = 0,
        EvaluationTokenUsage? usage = null) =>
        new()
        {
            QuestionId = questionId,
            StatusCode = statusCode,
            GeneratedAtUtc = generatedAtUtc,
            AttemptCount = attemptCount,
            TokenUsage = DurableOperationConversions.ToCheckpointUsage(
                usage ?? EvaluationTokenUsage.Unavailable),
        };

    private async Task<bool> ValidateCompletedRowsAsync(
        QuantificationSnapshot snapshot,
        EvaluationPlan plan,
        CheckpointEnvelope checkpoint,
        CancellationToken cancellationToken)
    {
        Dictionary<string, CheckpointReference> references = checkpoint.References.ToDictionary(
            reference => reference.QuestionId,
            StringComparer.Ordinal);
        foreach (CheckpointReference reference in checkpoint.References)
        {
            if (reference.Answer is not null)
            {
                SafeReferenceAnswerPayload payload;
                try
                {
                    payload = payloadBuilder.BuildReferenceAnswer(snapshot, reference.QuestionId);
                }
                catch
                {
                    return false;
                }

                if (!auxiliaryValidator.ValidateReference(
                        payload,
                        new ReferenceAnswerResult
                        {
                            QuestionId = reference.QuestionId,
                            Answer = reference.Answer,
                        }).IsValid)
                {
                    return false;
                }
            }
        }

        foreach (CheckpointCompletedRow completed in checkpoint.CompletedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] columns = plan.Items
                .Where(item => item.SourceRowNumber == completed.SourceRowNumber)
                .SelectMany(item => item.SelectedSourceColumns)
                .Concat(snapshot.Definition.Questions.Where(question => question.Enabled)
                    .Select(question => question.PrimarySourceColumn))
                .Concat(snapshot.Definition.Questions.Where(question => question.Enabled)
                    .SelectMany(question => question.SpecialEvaluations
                        .Where(special => special.Enabled)
                        .SelectMany(special => special.SupportingSourceColumns.Prepend(
                            special.PrimarySourceColumn))))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            EvaluationRowData row;
            try
            {
                row = await rowSource.ReadAsync(
                    new EvaluationRowRequest(
                        snapshot.Definition.SourceSheet,
                        completed.SourceRowNumber,
                        columns),
                    cancellationToken).ConfigureAwait(false);
                if (row.SourceRowNumber != completed.SourceRowNumber)
                {
                    return false;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return false;
            }

            foreach (CheckpointNormalResult saved in completed.NormalResults)
            {
                QuestionDefinition question = snapshot.Definition.Questions.Single(item =>
                    item.Enabled && string.Equals(item.Id, saved.QuestionId, StringComparison.Ordinal));
                bool scorable = !string.IsNullOrWhiteSpace(ReadCell(row, question.PrimarySourceColumn));
                if (string.Equals(saved.StatusCode, ResultsStatusCodes.Empty, StringComparison.Ordinal))
                {
                    if (scorable || saved.Scorable || !saved.ScorableKnown)
                    {
                        return false;
                    }

                    continue;
                }

                if (saved.AcceptedResult is not null)
                {
                    if (!scorable || !saved.Scorable || !saved.ScorableKnown)
                    {
                        return false;
                    }

                    SafeEvaluationPayload payload;
                    try
                    {
                        payload = payloadBuilder.Build(
                            snapshot,
                            saved.QuestionId,
                            saved.EvaluatorId,
                            SelectCells(row, question.SupportingSourceColumns.Prepend(
                                question.PrimarySourceColumn)));
                    }
                    catch
                    {
                        return false;
                    }

                    if (!normalResultValidator.Validate(payload, saved.AcceptedResult).IsValid)
                    {
                        return false;
                    }
                }
            }

            foreach (CheckpointSpecialResult saved in completed.SpecialResults)
            {
                QuestionDefinition question = snapshot.Definition.Questions.Single(item =>
                    item.Enabled && string.Equals(item.Id, saved.QuestionId, StringComparison.Ordinal));
                SpecialEvaluationDefinition special = question.SpecialEvaluations.Single(item =>
                    item.Enabled && string.Equals(item.Id, saved.SpecialEvaluationId, StringComparison.Ordinal));
                bool hasPrimary = !string.IsNullOrWhiteSpace(ReadCell(row, special.PrimarySourceColumn));
                if (string.Equals(saved.StatusCode, ResultsStatusCodes.Empty, StringComparison.Ordinal))
                {
                    if (hasPrimary || snapshot.Definition.SpecialPoints == 0m)
                    {
                        return false;
                    }

                    continue;
                }

                if (saved.AcceptedResult is not null)
                {
                    if (!hasPrimary || snapshot.Definition.SpecialPoints == 0m)
                    {
                        return false;
                    }

                    SafeSpecialEvaluationPayload payload;
                    try
                    {
                        payload = payloadBuilder.BuildSpecialEvaluation(
                            snapshot,
                            question.Id,
                            special.Id,
                            SelectCells(row, special.SupportingSourceColumns.Prepend(
                                special.PrimarySourceColumn)));
                    }
                    catch
                    {
                        return false;
                    }

                    if (!auxiliaryValidator.ValidateSpecial(payload, saved.AcceptedResult).IsValid)
                    {
                        return false;
                    }
                }
            }

            foreach (CheckpointSimilarityResult saved in completed.SimilarityResults)
            {
                QuestionDefinition question = snapshot.Definition.Questions.Single(item =>
                    item.Enabled && string.Equals(item.Id, saved.QuestionId, StringComparison.Ordinal));
                string studentAnswer = ReadCell(row, question.PrimarySourceColumn);
                CheckpointReference reference = references[question.Id];
                if (string.Equals(saved.StatusCode, ResultsStatusCodes.Empty, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(studentAnswer))
                    {
                        return false;
                    }

                    continue;
                }

                if (!string.Equals(reference.StatusCode, ResultsStatusCodes.Success, StringComparison.Ordinal))
                {
                    if (!string.Equals(saved.StatusCode, reference.StatusCode, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    continue;
                }

                if (saved.AcceptedResult is not null)
                {
                    SurfaceSimilarityResult expected = similarityCalculator.Calculate(
                        studentAnswer,
                        reference.Answer);
                    if (!string.Equals(saved.AcceptedResult.QuestionId, question.Id, StringComparison.Ordinal)
                        || saved.AcceptedResult.Similarity != expected.Score
                        || !string.Equals(saved.AcceptedResult.Reason, expected.ToReason(), StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static EvaluationScheduleResult BuildSchedule(
        EvaluationPlan plan,
        ImmutableArray<CheckpointCompletedRow> completedRows)
    {
        Dictionary<(int Row, string QuestionId, string EvaluatorId), CheckpointNormalResult> results =
            completedRows.SelectMany(row => row.NormalResults.Select(result => (
                    Key: (row.SourceRowNumber, result.QuestionId, result.EvaluatorId),
                    Result: result)))
                .ToDictionary(item => item.Key, item => item.Result);
        ImmutableArray<EvaluationUnitResult> units = plan.Items.Select(item =>
        {
            if (results.TryGetValue(
                    (item.SourceRowNumber, item.QuestionId, item.EvaluatorId),
                    out CheckpointNormalResult? result))
            {
                return new EvaluationUnitResult(
                    item,
                    result.StatusCode,
                    result.AcceptedResult,
                    result.AttemptCount,
                    result.Scorable,
                    result.ScorableKnown,
                    DurableOperationConversions.ToEvaluationUsage(result.TokenUsage));
            }

            return new EvaluationUnitResult(
                item,
                ResultsStatusCodes.Cancelled,
                null,
                attemptCount: 0,
                scorable: false,
                scorableKnown: false,
                EvaluationTokenUsage.Unavailable);
        }).ToImmutableArray();
        return new EvaluationScheduleResult(plan, units);
    }

    private static RunSummary CreateSummary(
        EvaluationScheduleResult schedule,
        InputSnapshot input,
        string statusCode,
        CheckpointEnvelope checkpoint,
        ImmutableArray<CheckpointReference> references,
        ImmutableArray<CheckpointCompletedRow> completedRows,
        DateTimeOffset endedAtUtc,
        bool wasResumed,
        string? finalPath,
        string? finalizationCode,
        bool partialCleanupFailed,
        ImmutableArray<RunPeerSimilarityResult> peerSimilarities) =>
        new(
            schedule,
            input,
            statusCode,
            checkpoint.StartedAtUtc,
            endedAtUtc,
            isDurable: true,
            references,
            completedRows,
            finalPath,
            checkpoint.PartialPath,
            wasResumed,
            finalizationCode,
            partialCleanupFailed,
            peerSimilarities);

    private async Task<ImmutableArray<RunPeerSimilarityResult>> CalculatePeerSimilaritiesAsync(
        QuantificationSnapshot snapshot,
        EvaluationPlan plan,
        CancellationToken cancellationToken)
    {
        ImmutableArray<QuestionDefinition> questions = snapshot.Definition.Questions
            .Where(question => question.Enabled)
            .ToImmutableArray();
        string[] selectedColumns = questions
            .Select(question => question.PrimarySourceColumn)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ImmutableArray<SimilarityPeerInput>.Builder answers =
            ImmutableArray.CreateBuilder<SimilarityPeerInput>(checked(plan.Mapping.SelectedRowCount * questions.Length));
        try
        {
            for (int sourceRow = plan.Mapping.FirstDataRow; sourceRow <= plan.Mapping.LastDataRow; sourceRow++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EvaluationRowData row = await rowSource.ReadAsync(
                    new EvaluationRowRequest(
                        snapshot.Definition.SourceSheet,
                        sourceRow,
                        selectedColumns),
                    cancellationToken).ConfigureAwait(false);
                if (row.SourceRowNumber != sourceRow)
                {
                    return [];
                }

                foreach (QuestionDefinition question in questions)
                {
                    answers.Add(new SimilarityPeerInput(
                        sourceRow,
                        question.Id,
                        ReadCell(row, question.PrimarySourceColumn)));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return [];
        }

        return similarityCalculator.CalculatePeerMaximums(answers).Results
            .Select(result => new RunPeerSimilarityResult
            {
                SourceRowNumber = result.SourceRowNumber,
                QuestionId = result.QuestionId,
                PeerMax = result.PeerMax,
                PeerRow = result.PeerRow,
            })
            .ToImmutableArray();
    }

    private static void ValidateStaticCapacity(
        QuantificationRunRequest request,
        EvaluationPlan plan)
    {
        ImmutableArray<ExecutionCapacityError>.Builder errors =
            ImmutableArray.CreateBuilder<ExecutionCapacityError>();
        if (request.MaximumPromptTokens is int promptLimit && promptLimit <= 0)
        {
            errors.Add(CapacityError(plan, "MODEL_PROMPT_LIMIT_UNAVAILABLE", "MaximumPromptTokens", promptLimit, "positive SDK model limit"));
        }

        if (request.MaximumContextWindowTokens is int contextLimit && contextLimit <= 0)
        {
            errors.Add(CapacityError(plan, "MODEL_CONTEXT_LIMIT_UNAVAILABLE", "MaximumContextWindowTokens", contextLimit, "positive SDK model limit"));
        }

        long worstCaseAttempts;
        try
        {
            int referenceCount = plan.Snapshot.Definition.Questions.Count(question => question.Enabled);
            long operationCount = checked(referenceCount + ((long)plan.Mapping.SelectedRowCount * LlmOperationsPerRow(plan)));
            worstCaseAttempts = checked(operationCount * RetryAndCleanupCoordinator.MaximumTransientAttempts);
        }
        catch (OverflowException)
        {
            worstCaseAttempts = long.MaxValue;
        }

        if (worstCaseAttempts > EvaluationRequestCapacityValidator.MaximumWorstCaseAttemptsPerRun)
        {
            errors.Add(CapacityError(
                plan,
                "ATTEMPT_BUDGET_TOO_LARGE",
                "WorstCaseAttempts",
                worstCaseAttempts,
                EvaluationRequestCapacityValidator.MaximumWorstCaseAttemptsPerRun.ToString(CultureInfo.InvariantCulture)));
        }

        if (errors.Count > 0)
        {
            throw new QuantificationRunPreflightException(errors.ToImmutable());
        }
    }

    private static ExecutionCapacityError CapacityError(
        EvaluationPlan plan,
        string code,
        string field,
        long actual,
        string limit) =>
        new(
            code,
            "Definition",
            plan.Snapshot.Definition.Id,
            plan.Snapshot.Definition.Name,
            field,
            actual.ToString(CultureInfo.InvariantCulture),
            limit);

    private static int OperationsPerRow(EvaluationPlan plan) => checked(
        plan.Items.Count(item => item.SourceRowNumber == plan.Mapping.FirstDataRow)
        + plan.Snapshot.Definition.Questions.Where(question => question.Enabled)
            .Sum(question => question.SpecialEvaluations.Count(special => special.Enabled))
        + plan.Snapshot.Definition.Questions.Count(question => question.Enabled));

    private static int LlmOperationsPerRow(EvaluationPlan plan) => checked(
        plan.Items.Count(item => item.SourceRowNumber == plan.Mapping.FirstDataRow)
        + plan.Snapshot.Definition.Questions.Where(question => question.Enabled)
            .Sum(question => question.SpecialEvaluations.Count(special => special.Enabled)));

    private static int SumInFlight(Dictionary<int, int> rowInFlight, object gate)
    {
        lock (gate)
        {
            return rowInFlight.Values.Sum();
        }
    }

    private static bool HasQuotaExhausted(CheckpointCompletedRow row) =>
        row.NormalResults.Any(result => string.Equals(result.StatusCode, ResultsStatusCodes.QuotaExhausted, StringComparison.Ordinal))
        || row.SpecialResults.Any(result => string.Equals(result.StatusCode, ResultsStatusCodes.QuotaExhausted, StringComparison.Ordinal));

    private static Dictionary<string, string?> SelectCells(
        EvaluationRowData row,
        IEnumerable<string> columns) =>
        columns.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(
            column => column,
            column => row.Cells.TryGetValue(column, out string? value) ? value : string.Empty,
            StringComparer.OrdinalIgnoreCase);

    private static string ReadCell(EvaluationRowData row, string column) =>
        row.Cells.TryGetValue(column, out string? value) ? value ?? string.Empty : string.Empty;

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

    private DateTimeOffset UtcNow(DateTimeOffset minimum)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return now < minimum ? minimum : now;
    }

    private static void ReportCurrent(
        Action<DurableEvaluationProgress>? progress,
        DurableEvaluationStage stage,
        int referenceCompleted,
        int referenceTotal,
        int rowCompleted,
        int rowTotal,
        int operationCompleted,
        int operationTotal,
        int inFlight,
        string statusCode) =>
        Report(
            progress,
            stage,
            referenceCompleted,
            referenceTotal,
            rowCompleted,
            rowTotal,
            operationCompleted,
            operationTotal,
            inFlight,
            statusCode);

    private static void ReportPreview(Action<LivePreviewUpdate> livePreview, LivePreviewUpdate update)
    {
        try
        {
            livePreview(update);
        }
        catch
        {
            // Presentation observers cannot alter durable state.
        }
    }

    private static void Report(
        Action<DurableEvaluationProgress>? progress,
        DurableEvaluationStage stage,
        int referenceCompleted,
        int referenceTotal,
        int rowCompleted,
        int rowTotal,
        int operationCompleted,
        int operationTotal,
        int inFlight,
        string statusCode)
    {
        if (progress is null)
        {
            return;
        }

        try
        {
            progress(new DurableEvaluationProgress(
                stage,
                referenceCompleted,
                referenceTotal,
                rowCompleted,
                rowTotal,
                operationCompleted,
                operationTotal,
                inFlight,
                statusCode));
        }
        catch
        {
            // Progress callbacks are observers and cannot alter durable state.
        }
    }
}
