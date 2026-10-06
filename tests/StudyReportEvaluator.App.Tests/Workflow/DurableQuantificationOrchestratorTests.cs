using System.Collections.Immutable;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Tests.UI;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workflow;

// Requirements: FR-030 (AC-031), FR-032 (AC-033)
public sealed class DurableQuantificationOrchestratorTests
{
    private static readonly TimeSpan TestWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task New_run_is_reference_first_row_ordered_checkpointed_and_auto_finalized()
    {
        QuantificationDefinition definition = Definition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        List<string> events = [];
        ScriptedRowSource rows = new((request, _) =>
        {
            events.Add("row:" + request.SourceRowNumber);
            return Task.FromResult(Row(request, $"answer-{request.SourceRowNumber}", $"special-{request.SourceRowNumber}"));
        });
        ScriptedRunner normal = new((payload, _, _) =>
        {
            events.Add("normal:" + payload.PrimarySource.Value);
            return Task.FromResult(EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload, _ => 7m)));
        });
        RecordingReferenceRunner references = new((payload, _) =>
        {
            events.Add("reference:" + payload.QuestionId);
            return Task.FromResult(AuxiliaryOperationResult<ReferenceAnswerResult>.Succeeded(
                new ReferenceAnswerResult { QuestionId = payload.QuestionId, Answer = "reference answer" }));
        });
        RecordingSpecialRunner specials = SpecialSuccess(events);
        RecordingCheckpointStore checkpoint = new(events);
        RecordingPathPlanner paths = new();
        RecordingFinalizer finalizer = new((summary, envelope, _) =>
        {
            events.Add("finalize");
            Assert.Equal(2, summary.CompletedRows.Length);
            Assert.Single(summary.References);
            return DurableFinalizationResult.Succeeded(envelope.FinalPath);
        });
        RecordingCleaner cleaner = new(events);
        ScriptedInputSnapshots input = new(U01TestSupport.InputSnapshot(), unchanged: true, events);
        List<DurableEvaluationProgress> progress = [];

        RunSummary summary = await Orchestrator(
            rows,
            normal,
            references,
            specials,
            input,
            checkpoint,
            paths,
            finalizer,
            cleaner).RunAsync(
                Request(definition, metadata),
                progress.Add,
                TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        Assert.True(summary.IsDurable);
        Assert.False(summary.WasResumed);
        Assert.False(summary.IsPartial);
        Assert.Equal(paths.Reservation.FinalPath, summary.FinalPath);
        Assert.Equal(paths.Reservation.PartialPath, summary.PartialPath);
        Assert.Equal(7, summary.PlannedOperationCount);
        Assert.Equal(7, summary.CompletedOperationCount);
        Assert.Equal(0, summary.OperationFailureCount);
        Assert.Equal(2, summary.CompletedRows.Length);
        Assert.Equal([2, 3], summary.CompletedRows.Select(row => row.SourceRowNumber));
        Assert.Equal(1, checkpoint.CreateCount);
        Assert.Equal(3, checkpoint.UpdateCount);
        Assert.Equal([(1, 0), (1, 1), (1, 2)],
            checkpoint.Updates.Select(item => (item.References.Length, item.CompletedRows.Length)));
        Assert.Equal(1, references.CallCount);
        Assert.Equal(2, normal.Payloads.Count);
        Assert.Equal(2, specials.CallCount);
        Assert.Equal(1, finalizer.CallCount);
        Assert.Equal(1, cleaner.CallCount);
        Assert.True(events.IndexOf("reference:Q1") < events.FindIndex(item => item.StartsWith("row:", StringComparison.Ordinal)));
        Assert.Equal([2, 3, 2, 3], rows.Requests.Select(item => item.SourceRowNumber));
        Assert.Contains(progress, item => item.Stage == DurableEvaluationStage.GeneratingReferences);
        Assert.Contains(progress, item => item.Stage == DurableEvaluationStage.SavingCheckpoint);
        Assert.Contains(progress, item => item.Stage == DurableEvaluationStage.FinalizingWorkbook);
        Assert.Equal(DurableEvaluationStage.Completed, progress[^1].Stage);
        RunOutputPreparation output = summary.PrepareOutput();
        Assert.All(output.Rows, row =>
        {
            QuestionResultInput question = Assert.Single(row.Questions);
            Assert.Equal(0.8m, Assert.Single(question.SpecialResults).AiRaw);
            Assert.Equal(0.8571m, question.Similarity!.AiRaw);
        });
        ResultsOutputViewModel results = new(
            new RecordingOutputBoundary(),
            U04TestSupport.Context(summary));
        Assert.True(results.IsAutomaticOutput);
        Assert.Equal(summary.FinalPath, results.FinalPath);
        Assert.Equal(2, results.RowScores.Count);
        Assert.All(results.RowScores, row =>
        {
            Assert.Equal(8m, row.SpecialEarned);
            Assert.Equal(0.2m, row.SimilarityPenalty);
            Assert.Equal(97.2m, row.FinalRaw);
            Assert.Equal(97.2m, row.FinalScore);
            Assert.Contains("Question text Q1: 1.4", row.QuestionEarnedText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Row_pipeline_starts_later_rows_before_checkpointing_only_contiguous_prefix()
    {
        QuantificationDefinition definition = Definition(2, 4);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        List<string> events = [];
        ScriptedRowSource rows = new((request, _) =>
        {
            events.Add("row:" + request.SourceRowNumber);
            return Task.FromResult(Row(request, $"answer-{request.SourceRowNumber}", $"special-{request.SourceRowNumber}"));
        });
        TaskCompletionSource allowFirstNormal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedRunner normal = new(async (payload, _, token) =>
        {
            events.Add("normal:" + payload.PrimarySource.Value);
            if (payload.PrimarySource.Value == "answer-2")
            {
                await allowFirstNormal.Task.WaitAsync(token);
            }

            return EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload, _ => 7m));
        });
        RecordingCheckpointStore checkpoint = new(events);

        Task<RunSummary> run = Orchestrator(
            rows,
            normal,
            ReferenceSuccess(),
            SpecialSuccess(events),
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot(), unchanged: true, events),
            checkpoint,
            new RecordingPathPlanner(),
            new RecordingFinalizer(),
            new RecordingCleaner()).RunAsync(
                Request(definition, metadata) with
                {
                    Run = RunRequest(definition, metadata) with { MaxConcurrency = 2 },
                },
                cancellationToken: TestContext.Current.CancellationToken);

        await WaitUntilAsync(() => events.Contains("row:3"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("checkpoint:update:1:1", events);
        allowFirstNormal.SetResult();
        RunSummary summary = await run.WaitAsync(TestWait, TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        Assert.Equal([2, 3, 4], summary.CompletedRows.Select(row => row.SourceRowNumber));
        Assert.Equal([(1, 0), (1, 1), (1, 2), (1, 3)],
            checkpoint.Updates.Select(item => (item.References.Length, item.CompletedRows.Length)));
    }

    [Fact]
    public async Task Quota_exhausted_reference_is_not_checkpointed_so_resume_regenerates_it()
    {
        QuantificationDefinition definition = Definition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RecordingCheckpointStore checkpoint = new();
        RecordingReferenceRunner references = new((_, _) => Task.FromResult(
            AuxiliaryOperationResult<ReferenceAnswerResult>.Failed(ResultsStatusCodes.QuotaExhausted)));
        ScriptedRunner normal = NormalSuccess();

        RunSummary summary = await Orchestrator(
            Rows(),
            normal,
            references,
            SpecialSuccess(),
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot()),
            checkpoint,
            new RecordingPathPlanner(),
            new RecordingFinalizer(),
            new RecordingCleaner()).RunAsync(
                Request(definition, metadata),
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.Cancelled, summary.StatusCode);
        Assert.Empty(summary.References);
        Assert.Empty(summary.CompletedRows);
        Assert.Equal(1, references.CallCount);
        Assert.Empty(normal.Payloads);
        Assert.Equal(0, checkpoint.UpdateCount);
    }

    [Fact]
    public async Task Quota_exhausted_row_is_not_checkpointed_and_stops_later_rows()
    {
        QuantificationDefinition definition = Definition(2, 4);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RecordingCheckpointStore checkpoint = new();
        ScriptedRunner normal = new((payload, _, _) => Task.FromResult(
            payload.PrimarySource.Value == "answer-3"
                ? EvaluationRunnerResult.Failed(ResultsStatusCodes.QuotaExhausted)
                : EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload, _ => 7m))));

        RunSummary summary = await Orchestrator(
            Rows(),
            normal,
            ReferenceSuccess(),
            SpecialSuccess(),
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot()),
            checkpoint,
            new RecordingPathPlanner(),
            new RecordingFinalizer(),
            new RecordingCleaner()).RunAsync(
                Request(definition, metadata),
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.Cancelled, summary.StatusCode);
        Assert.Equal([2], summary.CompletedRows.Select(row => row.SourceRowNumber));
        Assert.DoesNotContain(
            checkpoint.Updates.SelectMany(item => item.CompletedRows),
            row => row.SourceRowNumber == 3);
        Assert.DoesNotContain(normal.Payloads, payload => payload.PrimarySource.Value == "answer-4");
    }

    [Fact]
    public async Task Resume_reuses_reference_and_completed_rows_then_runs_only_the_first_unfinished_row()
    {
        QuantificationDefinition definition = Definition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ScriptedInputSnapshots input = new(U01TestSupport.InputSnapshot());
        RecordingCheckpointStore checkpoint = new();
        RecordingPathPlanner paths = new();
        using CancellationTokenSource firstCancellation = new();
        checkpoint.UpdateCallback = envelope =>
        {
            if (envelope.CompletedRows.Length == 1)
            {
                firstCancellation.Cancel();
            }
        };
        RecordingReferenceRunner firstReferences = ReferenceSuccess();
        ScriptedRowSource firstRows = Rows();
        RunSummary first = await Orchestrator(
            firstRows,
            NormalSuccess(),
            firstReferences,
            SpecialSuccess(),
            input,
            checkpoint,
            paths,
            new RecordingFinalizer(),
            new RecordingCleaner()).RunAsync(
                Request(definition, metadata),
                cancellationToken: firstCancellation.Token);

        Assert.Equal(QuantificationRunStatusCodes.Cancelled, first.StatusCode);
        Assert.True(first.IsPartial);
        Assert.Single(first.CompletedRows);
        Assert.Equal(1, firstReferences.CallCount);
        Assert.Equal([2], firstRows.Requests.Select(item => item.SourceRowNumber));
        Assert.Equal(0, checkpoint.LoadCount);

        checkpoint.UpdateCallback = null;
        RecordingReferenceRunner resumedReferences = new((_, _) => throw new InvalidOperationException("reference must be reused"));
        ScriptedRowSource resumedRows = Rows();
        ScriptedRunner resumedNormal = NormalSuccess();
        RecordingFinalizer finalizer = new();
        RunSummary resumed = await Orchestrator(
            resumedRows,
            resumedNormal,
            resumedReferences,
            SpecialSuccess(),
            input,
            checkpoint,
            paths,
            finalizer,
            new RecordingCleaner()).RunAsync(
                Request(definition, metadata) with
                {
                    ResumePartialPath = checkpoint.Current!.PartialPath,
                },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.Success, resumed.StatusCode);
        Assert.True(resumed.WasResumed);
        Assert.Equal(0, resumedReferences.CallCount);
        Assert.Equal([2, 3, 2, 3], resumedRows.Requests.Select(item => item.SourceRowNumber));
        Assert.Single(resumedNormal.Payloads);
        Assert.Equal("answer-3", resumedNormal.Payloads[0].PrimarySource.Value);
        Assert.Equal([2, 3], resumed.CompletedRows.Select(row => row.SourceRowNumber));
        Assert.Equal(1, checkpoint.CreateCount);
        Assert.Equal(1, checkpoint.LoadCount);
        Assert.Equal(1, finalizer.CallCount);
    }

    [Theory]
    [InlineData("input", CheckpointAdmissionStatusCodes.InputMismatch)]
    [InlineData("definition", CheckpointAdmissionStatusCodes.DefinitionMismatch)]
    [InlineData("model", CheckpointAdmissionStatusCodes.ModelMismatch)]
    [InlineData("runtime", CheckpointAdmissionStatusCodes.RuntimeMismatch)]
    public async Task Resume_mismatch_matrix_rejects_before_AI_or_checkpoint_mutation(
        string mismatch,
        string expectedCode)
    {
        QuantificationDefinition original = Definition(2, 2);
        WorkbookMetadata originalMetadata = U01TestSupport.ValidateMapping(original).Metadata;
        InputSnapshot originalInput = U01TestSupport.InputSnapshot();
        DurableQuantificationRunRequest originalRequest = Request(original, originalMetadata);
        CheckpointEnvelope saved = Envelope(original, originalInput, originalRequest.Runtime);
        InputSnapshot currentInput = mismatch == "input"
            ? new InputSnapshot(new string('B', 64), originalInput.SizeBytes, originalInput.LastWriteTimeUtc)
            : originalInput;
        QuantificationDefinition requestedDefinition = mismatch == "definition"
            ? original with { Revision = "2" }
            : original;
        WorkbookMetadata requestedMetadata = U01TestSupport.ValidateMapping(requestedDefinition).Metadata;
        string model = mismatch == "model" ? "other-model" : "model-test";
        CheckpointRuntimeIdentity runtime = mismatch == "runtime"
            ? originalRequest.Runtime with { CliSha256 = new string('B', 64) }
            : originalRequest.Runtime;
        RecordingCheckpointStore checkpoint = new() { Current = saved };
        RecordingReferenceRunner references = new((_, _) => throw new InvalidOperationException("must not run"));
        ScriptedRunner normal = new((_, _, _) => throw new InvalidOperationException("must not run"));

        QuantificationRunException exception = await Assert.ThrowsAsync<QuantificationRunException>(() =>
            Orchestrator(
                new ScriptedRowSource((_, _) => throw new InvalidOperationException("must not read")),
                normal,
                references,
                new RecordingSpecialRunner((_, _, _) => throw new InvalidOperationException("must not run")),
                new ScriptedInputSnapshots(currentInput),
                checkpoint,
                new RecordingPathPlanner(),
                new RecordingFinalizer(),
                new RecordingCleaner()).RunAsync(
                    new DurableQuantificationRunRequest
                    {
                        Run = RunRequest(requestedDefinition, requestedMetadata) with { ModelId = model },
                        Runtime = runtime,
                        ResumePartialPath = saved.PartialPath,
                    },
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(0, checkpoint.CreateCount);
        Assert.Equal(0, checkpoint.UpdateCount);
        Assert.Empty(normal.Payloads);
        Assert.Equal(0, references.CallCount);
        Assert.Contains("<redacted>", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Real_store_and_finalizer_create_valid_four_sheet_output_delete_partial_and_preserve_input()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original",
            1,
            2,
            2,
            new X02Header(1, "Primary A"),
            new X02Header(2, "Special B"));
        QuantificationDefinition definition = Definition(2, 2);
        WorkbookMetadata metadata = new WorkbookMetadataReader().Read(workbook.Path);
        InputSnapshot original = new InputSnapshotService().Capture(workbook.Path);
        OpenXmlEvaluationRowSource rows = new(workbook.Path);
        RecordingReferenceRunner references = ReferenceSuccess();
        string outputDirectory = Path.Combine(workbook.Directory, "durable-result");
        Directory.CreateDirectory(outputDirectory);
        DurableQuantificationOrchestrator orchestrator = new(
            rows,
            NormalSuccess(),
            references,
            SpecialSuccess(),
            new PhysicalInputSnapshotBoundary(),
            new CheckpointStore(),
            new OutputPathPlanner(),
            new WorkbookDurableRunFinalizer(),
            new PhysicalPartialCheckpointCleaner(),
            new IncrementingTimeProvider());

        RunSummary summary = await orchestrator.RunAsync(
            Request(definition, metadata, workbook.Path) with { OutputDirectory = outputDirectory },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        string finalPath = Assert.IsType<string>(summary.FinalPath);
        string partialPath = Assert.IsType<string>(summary.PartialPath);
        Assert.True(File.Exists(finalPath));
        Assert.False(File.Exists(partialPath));
        Assert.False(summary.PartialCleanupFailed);
        Assert.True(new InputSnapshotService().Recheck(workbook.Path, original).IsMatch);
        using SpreadsheetDocument final = SpreadsheetDocument.Open(finalPath, false);
        Workbook workbookRoot = final.WorkbookPart?.Workbook
            ?? throw new InvalidDataException("Workbook root missing.");
        Sheet[] sheets = workbookRoot.Descendants<Sheet>().ToArray();
        Assert.Equal(5, sheets.Length);
        Assert.Contains(sheets, sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.ConfigBaseName);
        Assert.Contains(sheets, sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.ReferencesBaseName);
        Assert.Contains(sheets, sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.ResultsBaseName);
        Assert.Contains(sheets, sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.RunBaseName);
        Assert.DoesNotContain(sheets, sheet => sheet.Name?.Value == CheckpointStore.CheckpointSheetName);
        Worksheet referencesSheet = Worksheet(final, AppOwnedSheetNameResolver.ReferencesBaseName);
        Assert.Contains(referencesSheet.Descendants<Cell>(), cell => cell.InnerText == "reference answer");
        Worksheet results = Worksheet(final, AppOwnedSheetNameResolver.ResultsBaseName);
        Assert.Contains(results.Descendants<Cell>(), cell => cell.CellFormula is not null);
    }

    [Fact]
    public async Task Real_partial_reopens_across_orchestrator_instances_and_resumes_only_the_unfinished_row()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original",
            1,
            3,
            2,
            new X02Header(1, "Primary A"),
            new X02Header(2, "Special B"));
        PopulateTwoRows(workbook.Path);
        QuantificationDefinition definition = Definition(2, 3);
        WorkbookMetadata metadata = new WorkbookMetadataReader().Read(workbook.Path);
        string outputDirectory = Path.Combine(workbook.Directory, "resume-result");
        Directory.CreateDirectory(outputDirectory);
        using CancellationTokenSource cancellation = new();
        int firstNormalCalls = 0;
        ScriptedRunner firstNormal = new((payload, _, _) =>
        {
            if (Interlocked.Increment(ref firstNormalCalls) == 2)
            {
                cancellation.Cancel();
                return Task.FromResult(EvaluationRunnerResult.Failed(ResultsStatusCodes.Cancelled));
            }

            return Task.FromResult(EvaluationRunnerResult.Succeeded(
                U01TestSupport.ValidResult(payload, _ => 7m)));
        });
        RecordingReferenceRunner firstReferences = ReferenceSuccess();
        DurableQuantificationOrchestrator firstProcess = new(
            new OpenXmlEvaluationRowSource(workbook.Path),
            firstNormal,
            firstReferences,
            SpecialSuccess(),
            new PhysicalInputSnapshotBoundary(),
            new CheckpointStore(),
            new OutputPathPlanner(),
            new WorkbookDurableRunFinalizer(),
            new PhysicalPartialCheckpointCleaner(),
            new IncrementingTimeProvider());

        RunSummary interrupted = await firstProcess.RunAsync(
            Request(definition, metadata, workbook.Path) with { OutputDirectory = outputDirectory },
            cancellationToken: cancellation.Token);

        Assert.Equal(QuantificationRunStatusCodes.Cancelled, interrupted.StatusCode);
        string partialPath = Assert.IsType<string>(interrupted.PartialPath);
        Assert.True(File.Exists(partialPath));
        Assert.Null(interrupted.FinalPath);
        Assert.Single(interrupted.CompletedRows);
        Assert.Equal(1, firstReferences.CallCount);

        RecordingReferenceRunner resumedReferences = new((_, _) => throw new InvalidOperationException("saved reference must be reused"));
        ScriptedRunner resumedNormal = NormalSuccess();
        DurableQuantificationOrchestrator secondProcess = new(
            new OpenXmlEvaluationRowSource(workbook.Path),
            resumedNormal,
            resumedReferences,
            SpecialSuccess(),
            new PhysicalInputSnapshotBoundary(),
            new CheckpointStore(),
            new OutputPathPlanner(),
            new WorkbookDurableRunFinalizer(),
            new PhysicalPartialCheckpointCleaner(),
            new IncrementingTimeProvider());

        RunSummary resumed = await secondProcess.RunAsync(
            Request(definition, metadata, workbook.Path) with { ResumePartialPath = partialPath },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.Success, resumed.StatusCode);
        Assert.True(resumed.WasResumed);
        Assert.Equal(0, resumedReferences.CallCount);
        Assert.Single(resumedNormal.Payloads);
        Assert.Equal("answer-3", resumedNormal.Payloads[0].PrimarySource.Value);
        Assert.False(File.Exists(partialPath));
        Assert.True(File.Exists(resumed.FinalPath));
        Assert.Equal([2, 3], resumed.CompletedRows.Select(row => row.SourceRowNumber));
    }

    [Fact]
    public async Task Checkpoint_save_failure_rolls_back_unsaved_reference_and_prevents_rows_and_finalization()
    {
        QuantificationDefinition definition = Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RecordingCheckpointStore checkpoint = new()
        {
            UpdateResult = envelope => envelope.References.Length == 1
                ? CheckpointSaveResult.Failed(CheckpointStatusCodes.SaveFailed)
                : CheckpointSaveResult.Succeeded(createdNew: false),
        };
        ScriptedRowSource rows = Rows();
        RecordingFinalizer finalizer = new();

        RunSummary summary = await Orchestrator(
            rows,
            NormalSuccess(),
            ReferenceSuccess(),
            SpecialSuccess(),
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot()),
            checkpoint,
            new RecordingPathPlanner(),
            finalizer,
            new RecordingCleaner()).RunAsync(
                Request(definition, metadata),
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.CheckpointFailed, summary.StatusCode);
        Assert.Equal(CheckpointStatusCodes.SaveFailed, summary.FinalizationCode);
        Assert.Empty(summary.References);
        Assert.Empty(summary.CompletedRows);
        Assert.Empty(rows.Requests);
        Assert.Equal(0, finalizer.CallCount);
        Assert.Empty(checkpoint.Current!.References);
    }

    [Fact]
    public async Task Empty_special_and_similarity_project_zero_while_technical_failures_remain_blank()
    {
        QuantificationDefinition definition = Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ScriptedRowSource emptyRows = new((request, _) => Task.FromResult(Row(
            request,
            primary: "   ",
            special: "   ")));
        RunSummary empty = await Orchestrator(
            emptyRows,
            new ScriptedRunner((_, _, _) => throw new InvalidOperationException("must not run")),
            ReferenceSuccess(),
            new RecordingSpecialRunner((_, _, _) => throw new InvalidOperationException("must not run")),
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot()),
            new RecordingCheckpointStore(),
            new RecordingPathPlanner(),
            new RecordingFinalizer(),
            new RecordingCleaner()).RunAsync(
                Request(definition, metadata),
                cancellationToken: TestContext.Current.CancellationToken);
        QuestionResultInput emptyQuestion = Assert.Single(Assert.Single(empty.PrepareOutput().Rows).Questions);
        Assert.Equal(0m, Assert.Single(emptyQuestion.SpecialResults).AiRaw);
        Assert.Equal(0m, emptyQuestion.Similarity!.AiRaw);

        RunSummary failed = await Orchestrator(
            Rows(),
            NormalSuccess(),
            ReferenceSuccess(),
            new RecordingSpecialRunner((_, _, _) => Task.FromResult(
                AuxiliaryOperationResult<SpecialQuantificationResult>.Failed(ResultsStatusCodes.AiTimeout))),
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot()),
            new RecordingCheckpointStore(),
            new RecordingPathPlanner(),
            new RecordingFinalizer(),
            new RecordingCleaner()).RunAsync(
                Request(definition, metadata),
                cancellationToken: TestContext.Current.CancellationToken);
        QuestionResultInput failedQuestion = Assert.Single(Assert.Single(failed.PrepareOutput().Rows).Questions);
        Assert.Null(Assert.Single(failedQuestion.SpecialResults).AiRaw);
        Assert.Equal(ResultsStatusCodes.Success, failedQuestion.Similarity!.Status);
        Assert.Equal(0.8571m, failedQuestion.Similarity.AiRaw);
    }

    [Fact]
    public async Task Valid_final_remains_success_when_partial_cleanup_fails_and_invalid_final_keeps_partial()
    {
        QuantificationDefinition definition = Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RecordingCleaner failedCleaner = new() { Result = false };
        RunSummary cleanupWarning = await Orchestrator(
            Rows(),
            NormalSuccess(),
            ReferenceSuccess(),
            SpecialSuccess(),
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot()),
            new RecordingCheckpointStore(),
            new RecordingPathPlanner(),
            new RecordingFinalizer(),
            failedCleaner).RunAsync(
                Request(definition, metadata),
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.Success, cleanupWarning.StatusCode);
        Assert.NotNull(cleanupWarning.FinalPath);
        Assert.True(cleanupWarning.PartialCleanupFailed);
        Assert.Equal(1, failedCleaner.CallCount);

        RecordingCleaner untouchedCleaner = new();
        RunSummary invalidFinal = await Orchestrator(
            Rows(),
            NormalSuccess(),
            ReferenceSuccess(),
            SpecialSuccess(),
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot()),
            new RecordingCheckpointStore(),
            new RecordingPathPlanner(),
            new RecordingFinalizer((_, _, _) => DurableFinalizationResult.Failed(
                AtomicOutputStatusCodes.OutputInvalid)),
            untouchedCleaner).RunAsync(
                Request(definition, metadata),
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.OutputInvalid, invalidFinal.StatusCode);
        Assert.Null(invalidFinal.FinalPath);
        Assert.NotNull(invalidFinal.PartialPath);
        Assert.Equal(0, untouchedCleaner.CallCount);
    }

    [Fact]
    public async Task Resume_revalidates_saved_evidence_against_the_exact_source_row_before_skipping_it()
    {
        QuantificationDefinition definition = Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        DurableQuantificationRunRequest request = Request(definition, metadata);
        CheckpointEnvelope saved = Envelope(definition, U01TestSupport.InputSnapshot(), request.Runtime) with
        {
            References =
            [
                new CheckpointReference
                {
                    QuestionId = "Q1",
                    Answer = "reference answer",
                    StatusCode = ResultsStatusCodes.Success,
                    GeneratedAtUtc = new DateTimeOffset(2026, 9, 2, 5, 35, 1, TimeSpan.Zero),
                    AttemptCount = 1,
                },
            ],
            CompletedRows =
            [
                new CheckpointCompletedRow
                {
                    SourceRowNumber = 2,
                    NormalResults =
                    [
                        new CheckpointNormalResult
                        {
                            QuestionId = "Q1",
                            EvaluatorId = "E1",
                            StatusCode = ResultsStatusCodes.Success,
                            AttemptCount = 1,
                            Scorable = true,
                            ScorableKnown = true,
                            AcceptedResult = new QuantificationResult
                            {
                                EvaluatorId = "E1",
                                Criteria =
                                [
                                    new CriterionQuantificationResult
                                    {
                                        CriterionId = "C1",
                                        RawScore = 7m,
                                        Reason = "reason",
                                        Evidence = "FORGED-NOT-IN-SOURCE",
                                        EvidenceSource = EvidenceSourceKind.PrimaryAnswer,
                                        EvidenceSourceColumnId = "A",
                                    },
                                ],
                            },
                        },
                    ],
                    SpecialResults =
                    [
                        new CheckpointSpecialResult
                        {
                            QuestionId = "Q1",
                            SpecialEvaluationId = "S1",
                            StatusCode = ResultsStatusCodes.Success,
                            AttemptCount = 1,
                            AcceptedResult = new SpecialQuantificationResult
                            {
                                SpecialEvaluationId = "S1",
                                Score = 0.8m,
                                Reason = "reason",
                                Evidence = string.Empty,
                                EvidenceSource = EvidenceSourceKind.None,
                                EvidenceSourceColumnId = string.Empty,
                            },
                        },
                    ],
                    SimilarityResults =
                    [
                        new CheckpointSimilarityResult
                        {
                            QuestionId = "Q1",
                            StatusCode = ResultsStatusCodes.Success,
                            AttemptCount = 1,
                            AcceptedResult = new SimilarityQuantificationResult
                            {
                                QuestionId = "Q1",
                                Similarity = 0.25m,
                                Reason = "reason",
                            },
                        },
                    ],
                },
            ],
        };
        RecordingCheckpointStore checkpoint = new() { Current = saved };
        ScriptedRunner normal = new((_, _, _) => throw new InvalidOperationException("must not dispatch"));

        QuantificationRunException exception = await Assert.ThrowsAsync<QuantificationRunException>(() =>
            Orchestrator(
                Rows(),
                normal,
                new RecordingReferenceRunner((_, _) => throw new InvalidOperationException("must not dispatch")),
                new RecordingSpecialRunner((_, _, _) => throw new InvalidOperationException("must not dispatch")),
                new ScriptedInputSnapshots(U01TestSupport.InputSnapshot()),
                checkpoint,
                new RecordingPathPlanner(),
                new RecordingFinalizer(),
                new RecordingCleaner()).RunAsync(
                    request with { ResumePartialPath = saved.PartialPath },
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(CheckpointStatusCodes.Invalid, exception.Code);
        Assert.Empty(normal.Payloads);
        Assert.Equal(0, checkpoint.UpdateCount);
        Assert.DoesNotContain("FORGED-NOT-IN-SOURCE", exception.ToString(), StringComparison.Ordinal);
    }

    private static DurableQuantificationOrchestrator Orchestrator(
        IEvaluationRowSource rows,
        IEvaluationRunner normal,
        IReferenceAnswerOperationRunner references,
        ISpecialEvaluationOperationRunner specials,
        IInputSnapshotBoundary input,
        ICheckpointStore checkpoint,
        IOutputPathPlanner paths,
        IDurableRunFinalizer finalizer,
        IPartialCheckpointCleaner cleaner) =>
        new(
            rows,
            normal,
            references,
            specials,
            input,
            checkpoint,
            paths,
            finalizer,
            cleaner,
            new IncrementingTimeProvider());

    private static DurableQuantificationRunRequest Request(
        QuantificationDefinition definition,
        WorkbookMetadata metadata,
        string inputPath = "C:\\PRIVATE\\input.xlsx") =>
        new()
        {
            Run = RunRequest(definition, metadata) with { InputPath = inputPath },
            Runtime = Runtime(),
        };

    private static QuantificationRunRequest RunRequest(
        QuantificationDefinition definition,
        WorkbookMetadata metadata) =>
        new()
        {
            DraftDefinition = definition,
            WorkbookMetadata = metadata,
            InputPath = "C:\\PRIVATE\\input.xlsx",
            ModelId = "model-test",
            MaximumPromptTokens = 64_000,
            MaximumContextWindowTokens = 128_000,
            MaxConcurrency = 1,
        };

    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = new(TestWait);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        while (!predicate())
        {
            await Task.Delay(25, linked.Token).ConfigureAwait(false);
        }
    }

    private static CheckpointRuntimeIdentity Runtime() =>
        new()
        {
            ApplicationIdentity = "StudyReportEvaluator.App/4.0.0",
            CliVersion = "1.0.79",
            CliSha256 = new string('A', 64),
            SdkInformationalVersion = "1.0.11",
        };

    private static CheckpointEnvelope Envelope(
        QuantificationDefinition definition,
        InputSnapshot input,
        CheckpointRuntimeIdentity runtime)
    {
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);
        return new CheckpointEnvelope
        {
            InputPath = "C:\\PRIVATE\\input.xlsx",
            Input = input,
            DefinitionCanonicalJson = snapshot.CanonicalJson,
            DefinitionSha256 = snapshot.Sha256,
            NormalModelId = "model-test",
            ReferenceModelId = "model-test",
            Runtime = runtime,
            FinalPath = "C:\\PRIVATE\\result\\eval-20260902-1435.xlsx",
            PartialPath = "C:\\PRIVATE\\result\\eval-20260902-1435.partial.xlsx",
            StartedAtUtc = new DateTimeOffset(2026, 9, 2, 5, 35, 0, TimeSpan.Zero),
            SavedAtUtc = new DateTimeOffset(2026, 9, 2, 5, 35, 1, TimeSpan.Zero),
        };
    }

    private static QuantificationDefinition Definition(int firstRow, int lastRow)
    {
        SpecialEvaluationDefinition special = new()
        {
            Id = "S1",
            DisplayName = "Special",
            PrimarySourceColumn = "B",
            SupportingSourceColumns = ["A"],
            PromptTemplate = "Special {回答} {補助情報}",
        };
        QuantificationDefinition source = U01TestSupport.Definition(
            firstRow,
            lastRow,
            U01TestSupport.Question(
                "Q1",
                "A",
                ["B"],
                true,
                U01TestSupport.Evaluator("E1", "C1")));
        return source with
        {
            BasePoints = 88m,
            SpecialPoints = 10m,
            Questions = [source.Questions[0] with { SpecialEvaluations = [special] }],
        };
    }

    private static EvaluationRowData Row(EvaluationRowRequest request, string primary, string special) =>
        new(
            request.SourceRowNumber,
            new Dictionary<string, string?>
            {
                ["A"] = primary,
                ["B"] = special,
            });

    private static ScriptedRowSource Rows() =>
        new((request, _) => Task.FromResult(Row(
            request,
            $"answer-{request.SourceRowNumber}",
            $"special-{request.SourceRowNumber}")));

    private static ScriptedRunner NormalSuccess() =>
        new((payload, _, _) => Task.FromResult(
            EvaluationRunnerResult.Succeeded(
                U01TestSupport.ValidResult(payload, _ => 7m),
                tokenUsage: Usage())));

    private static RecordingReferenceRunner ReferenceSuccess() =>
        new((payload, _) => Task.FromResult(
            AuxiliaryOperationResult<ReferenceAnswerResult>.Succeeded(
                new ReferenceAnswerResult
                {
                    QuestionId = payload.QuestionId,
                    Answer = "reference answer",
                },
                tokenUsage: Usage())));

    private static RecordingSpecialRunner SpecialSuccess(List<string>? events = null) =>
        new((payload, _, _) =>
        {
            events?.Add("special:" + payload.PrimarySource.Value);
            return Task.FromResult(AuxiliaryOperationResult<SpecialQuantificationResult>.Succeeded(
                new SpecialQuantificationResult
                {
                    SpecialEvaluationId = payload.SpecialEvaluationId,
                    Score = 0.8m,
                    Reason = "special reason",
                    Evidence = string.Empty,
                    EvidenceSource = EvidenceSourceKind.None,
                    EvidenceSourceColumnId = string.Empty,
                },
                tokenUsage: Usage()));
        });
    private static EvaluationTokenUsage Usage() => new(true, 10, 2, 1, 3, 4);

    private static Worksheet Worksheet(SpreadsheetDocument document, string name)
    {
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("Workbook part missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("Workbook root missing.");
        Sheet sheet = workbook.Descendants<Sheet>().Single(item => item.Name?.Value == name);
        return ((WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!)).Worksheet
            ?? throw new InvalidDataException("Worksheet missing.");
    }

    private static void PopulateTwoRows(string path)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, true);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("Workbook part missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("Workbook root missing.");
        Sheet sheet = workbook.Descendants<Sheet>().Single();
        Worksheet worksheet = ((WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!)).Worksheet
            ?? throw new InvalidDataException("Worksheet missing.");
        SheetData data = worksheet.GetFirstChild<SheetData>()
            ?? throw new InvalidDataException("Sheet data missing.");
        Row row3 = data.Elements<Row>().Single(row => row.RowIndex?.Value == 3);
        Row row2 = new(
            InlineCell("A2", "answer-2"),
            InlineCell("B2", "special-2"))
        {
            RowIndex = 2,
        };
        data.InsertBefore(row2, row3);
        row3.RemoveAllChildren<Cell>();
        row3.Append(InlineCell("A3", "answer-3"), InlineCell("B3", "special-3"));
        worksheet.Save();
    }

    private static Cell InlineCell(string reference, string value) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(value)),
        };

    private sealed class RecordingReferenceRunner(
        Func<SafeReferenceAnswerPayload, CancellationToken, Task<AuxiliaryOperationResult<ReferenceAnswerResult>>> evaluate)
        : IReferenceAnswerOperationRunner
    {
        internal int CallCount { get; private set; }

        public Task<AuxiliaryOperationResult<ReferenceAnswerResult>> EvaluateAsync(
            SafeReferenceAnswerPayload payload,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return evaluate(payload, cancellationToken);
        }
    }

    private sealed class RecordingSpecialRunner(
        Func<SafeSpecialEvaluationPayload, string, CancellationToken, Task<AuxiliaryOperationResult<SpecialQuantificationResult>>> evaluate)
        : ISpecialEvaluationOperationRunner
    {
        internal int CallCount { get; private set; }

        public Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(
            SafeSpecialEvaluationPayload payload,
            string modelId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return evaluate(payload, modelId, cancellationToken);
        }
    }
    private sealed class RecordingCheckpointStore(IList<string>? events = null) : ICheckpointStore
    {
        internal CheckpointEnvelope? Current { get; set; }

        internal List<CheckpointEnvelope> Updates { get; } = [];

        internal Action<CheckpointEnvelope>? UpdateCallback { get; set; }

        internal Func<CheckpointEnvelope, CheckpointSaveResult>? UpdateResult { get; set; }

        internal int CreateCount { get; private set; }

        internal int UpdateCount { get; private set; }

        internal int LoadCount { get; private set; }

        public CheckpointSaveResult Create(CheckpointEnvelope envelope, CancellationToken cancellationToken = default)
        {
            CreateCount++;
            events?.Add("checkpoint:create");
            Current = envelope;
            return CheckpointSaveResult.Succeeded(createdNew: true);
        }

        public CheckpointSaveResult Update(CheckpointEnvelope envelope, CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            events?.Add($"checkpoint:update:{envelope.References.Length}:{envelope.CompletedRows.Length}");
            CheckpointSaveResult result = UpdateResult?.Invoke(envelope)
                ?? CheckpointSaveResult.Succeeded(createdNew: false);
            if (result.IsSuccess)
            {
                Current = envelope;
                Updates.Add(envelope);
                UpdateCallback?.Invoke(envelope);
            }

            return result;
        }

        public CheckpointLoadResult Load(string partialPath, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Current is null
                ? CheckpointLoadResult.Failed(CheckpointStatusCodes.Invalid)
                : CheckpointLoadResult.Succeeded(Current);
        }
    }

    private sealed class RecordingPathPlanner : IOutputPathPlanner
    {
        internal OutputPathReservation Reservation { get; } = OutputPathReservation.Create(
            "C:\\PRIVATE\\result",
            "C:\\PRIVATE\\result\\eval-20260902-1435.xlsx",
            "C:\\PRIVATE\\result\\eval-20260902-1435.partial.xlsx");

        public OutputPathReservation Reserve(string inputPath, DateTimeOffset localTime, string? outputDirectory = null) => Reservation;
    }

    private sealed class RecordingFinalizer : IDurableRunFinalizer
    {
        private readonly Func<RunSummary, CheckpointEnvelope, CancellationToken, DurableFinalizationResult>? finalize;

        internal RecordingFinalizer(
            Func<RunSummary, CheckpointEnvelope, CancellationToken, DurableFinalizationResult>? finalize = null)
        {
            this.finalize = finalize;
        }

        internal int CallCount { get; private set; }

        public DurableFinalizationResult Finalize(
            RunSummary summary,
            CheckpointEnvelope checkpoint,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return finalize?.Invoke(summary, checkpoint, cancellationToken)
                ?? DurableFinalizationResult.Succeeded(checkpoint.FinalPath);
        }
    }

    private sealed class RecordingCleaner(IList<string>? events = null) : IPartialCheckpointCleaner
    {
        internal bool Result { get; init; } = true;

        internal int CallCount { get; private set; }

        public bool TryDelete(string partialPath)
        {
            CallCount++;
            events?.Add("cleanup");
            return Result;
        }
    }

    private sealed class IncrementingTimeProvider : TimeProvider
    {
        private long ticks = new DateTimeOffset(2026, 9, 2, 5, 35, 0, TimeSpan.Zero).UtcTicks;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() =>
            new(Interlocked.Add(ref ticks, TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }
}
