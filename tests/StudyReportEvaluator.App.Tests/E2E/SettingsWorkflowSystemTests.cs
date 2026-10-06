using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.Settings;
using StudyReportEvaluator.App.Tests.UI;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Validation;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.E2E;

/// <summary>
/// T27: local VM-to-filesystem journeys, not native UI or live Copilot tests.
/// Authentication/model/runtime identity, AI operation responses and time are synthetic.
/// The run adapter composes real readers, DurableQuantificationOrchestrator, checkpoint,
/// writers, validation and atomic commit; it never fabricates a RunSummary or file receipt.
/// Four data rows complement, rather than replace, the existing long synthetic E2E.
/// </summary>
// Requirements: FR-042 (AC-043)
public sealed class SettingsWorkflowSystemTests
{
    private const string SourceSheet = "Original";
    private const string ModelId = "model-test";
    private const string FirstAnswer = "=T27 answer 3";
    private const string LastAnswer = "T27 answer 6";
    private const string FirstSpecial = "-T27 special 3";
    private const string LastSpecial = "T27 special 6";
    private const string ReferenceAnswer = "+T27 saved reference";
    private const string UnselectedText = "UNSELECTED-T27-BODY";
    private static readonly string TestApplicationIdentity = QuantificationRunBoundary.ApplicationIdentity();
    private static readonly DateTimeOffset FixedUtc = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
    private static readonly CanonicalDefinitionSerializer Canonical = new();
    private static readonly CachedCopilotModel[] ExpectedCatalog =
        [new(ModelId, 64_000, 128_000), new("auto", null, null)];
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_output_preference_survives_new_instances_and_input_change_before_real_final_output(
        bool explicitOutput)
    {
        using X02TemporaryWorkbook original = CreateInput();
        using X02TemporaryWorkbook replacement = CreateInput();
        InputSnapshot originalBefore = new InputSnapshotService().Capture(original.Path);
        InputSnapshot replacementBefore = new InputSnapshotService().Capture(replacement.Path);
        string? chosenDirectory = explicitOutput ? Path.Combine(original.Directory, "explicit output") : null;
        string settingsPath = await SaveSettingsAsync(original, chosenDirectory);
        byte[] settingsBytes = await File.ReadAllBytesAsync(settingsPath, TestToken);
        using (JsonDocument json = JsonDocument.Parse(settingsBytes))
        {
            Assert.Equal(ApplicationSettings.CurrentSchemaVersion, json.RootElement.GetProperty("schemaVersion").GetInt32());
            JsonElement output = json.RootElement.GetProperty("outputDirectoryOverride");
            if (explicitOutput) Assert.Equal(chosenDirectory, output.GetString());
            else Assert.Equal(JsonValueKind.Null, output.ValueKind);
        }

        using LocalSession session = new(settingsPath);
        await InitializeAsync(session);
        Assert.Equal(chosenDirectory, session.Execution.OutputDirectoryOverride);
        await LoadAndAdmitAsync(session, original);
        GoToExecution(session);
        string firstDirectory = chosenDirectory ?? Path.Combine(original.Directory, "result");
        Assert.Equal(firstDirectory, session.Execution.OutputDirectory);
        Assert.False(Directory.Exists(firstDirectory));
        AssertPassive(session);

        session.Shell.NavigateCommand.Execute(WorkflowStep.Input);
        await LoadAndAdmitAsync(session, replacement);
        GoToExecution(session);
        string expectedDirectory = chosenDirectory ?? Path.Combine(replacement.Directory, "result");
        Assert.Equal(chosenDirectory, session.Execution.OutputDirectoryOverride);
        Assert.Equal(expectedDirectory, session.Execution.OutputDirectory);
        Assert.Equal(explicitOutput, firstDirectory == expectedDirectory);
        Assert.False(Directory.Exists(expectedDirectory));
        Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(settingsPath, TestToken));
        AssertPassive(session);

        settingsBytes = await AuthenticateAsync(session, settingsBytes);
        RunSummary summary = await RunAsync(session);
        string finalPath = AssertSuccessfulFinal(session, replacement, summary);
        QuantificationRunRequest dispatched = Assert.Single(session.Boundary.Requests);
        Assert.Equal(replacement.Path, dispatched.InputPath);
        Assert.Same(session.Input.Metadata, dispatched.WorkbookMetadata);
        Assert.Equal(expectedDirectory, dispatched.OutputDirectory);
        Assert.Null(dispatched.ResumePartialPath);
        Assert.Equal(expectedDirectory, Path.GetDirectoryName(finalPath));
        Assert.False(summary.WasResumed);
        AssertAiCalls(session.Ai, [FirstAnswer, "0", LastAnswer], [FirstSpecial, "0", LastSpecial], referenceCalls: 1);
        Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(settingsPath, TestToken));
        AssertUnchanged(original.Path, originalBefore);
        AssertUnchanged(replacement.Path, replacementBefore);
    }

    [Fact]
    public async Task Real_final_and_preview_distinguish_missing_answer_numeric_zero_and_technical_failure()
    {
        using X02TemporaryWorkbook workbook = CreateInput();
        InputSnapshot before = new InputSnapshotService().Capture(workbook.Path);
        string settingsPath = await SaveSettingsAsync(workbook);
        using LocalSession session = await OpenAdmittedAsync(settingsPath, workbook, new LocalAi { FailLastRow = true });
        await PrepareExecutionAsync(session);
        RunSummary summary = await RunAsync(session);
        string finalPath = AssertSuccessfulFinal(session, workbook, summary, operationFailures: 2);
        Assert.Equal(2, summary.SucceededCount);
        Assert.Equal(1, summary.EmptyCount);
        Assert.Equal(1, summary.FailureCount);
        AssertAiCalls(session.Ai, [FirstAnswer, "0", LastAnswer], [FirstSpecial, "0", LastSpecial], referenceCalls: 1);

        // The existing durable Workflow oracle is 88 + (2 * .7) + (10 * .8) - (2 * .25 * .2) = 97.3.
        Assert.Equal(97.3m, Score(session, 3).FinalScore);
        Assert.Equal(8m, Score(session, 3).SpecialEarned);
        Assert.Equal(0.1m, Score(session, 3).SimilarityPenalty);
        ResultsCriterionViewModel blank = Criterion(session, 4);
        ResultsCriterionViewModel zero = Criterion(session, 5);
        ResultsCriterionViewModel failed = Criterion(session, 6);
        Assert.Equal(ResultsRowStatus.Empty, Score(session, 4).Status);
        Assert.Equal(ResultsStatusCodes.Empty, blank.StatusCode);
        Assert.False(blank.CanOverride);
        Assert.Null(blank.AiRawScore);
        Assert.Null(blank.EffectiveRaw);
        blank.OverrideText = "0";
        Assert.Equal(string.Empty, blank.OverrideText);
        Assert.Equal(88m, Score(session, 4).FinalScore);
        Assert.Equal(0m, Score(session, 4).SpecialEarned);
        Assert.Equal(0m, Score(session, 4).SimilarityPenalty);
        Assert.Equal(ResultsRowStatus.Success, Score(session, 5).Status);
        Assert.Equal(ResultsStatusCodes.Success, zero.StatusCode);
        Assert.True(zero.CanOverride);
        Assert.Equal(0m, zero.AiRawScore);
        Assert.Equal(0m, zero.EffectiveRaw);
        Assert.Equal(0m, zero.NormalizedScore);
        Assert.Equal(88m, Score(session, 5).FinalScore);
        Assert.Equal(ResultsRowStatus.TechnicalError, Score(session, 6).Status);
        Assert.Equal(ResultsStatusCodes.AiTimeout, failed.StatusCode);
        Assert.Null(failed.AiRawScore);
        Assert.Null(failed.EffectiveRaw);
        Assert.Null(Score(session, 6).SpecialEarned);
        Assert.Equal(0.1m, Score(session, 6).SimilarityPenalty);
        Assert.Null(Score(session, 6).FinalRaw);
        Assert.Null(Score(session, 6).FinalScore);

        using (SpreadsheetDocument document = SpreadsheetDocument.Open(finalPath, false))
        {
            Worksheet results = Worksheet(document, AppOwnedSheetNameResolver.ResultsBaseName);
            AssertNumber(ResultCell(results, "Q1.Answer_Present", 4), 0m, formula: false);
            AssertNumber(ResultCell(results, "Q1.Answer_Present", 5), 1m, formula: false);
            AssertNumber(ResultCell(results, "Q1.S1.Special_AI_Raw", 4), 0m, formula: false);
            AssertNumber(ResultCell(results, "Q1.Similarity_AI_Raw", 4), 0m, formula: false);
            AssertNumber(ResultCell(results, "Q1.S1.Special_AI_Raw", 6), null, formula: false);
            AssertNumber(ResultCell(results, "Q1.Similarity_AI_Raw", 6), 0.3m, formula: false);
            Assert.Equal(ResultsStatusCodes.Success, ResultCell(results, "Q1.Similarity_Status", 6).InnerText);
        }

        AssertUnchanged(workbook.Path, before);
    }

    [Fact]
    public async Task Override_exports_a_separate_real_workbook_using_the_run_snapshot_not_next_settings()
    {
        using X02TemporaryWorkbook workbook = CreateInput();
        InputSnapshot before = new InputSnapshotService().Capture(workbook.Path);
        string settingsPath = await SaveSettingsAsync(workbook);
        byte[] settingsBytes = await File.ReadAllBytesAsync(settingsPath, TestToken);
        using LocalSession session = await OpenAdmittedAsync(settingsPath, workbook);
        settingsBytes = await PrepareExecutionAsync(session, settingsBytes);
        RunSummary summary = await RunAsync(session);
        string originalFinal = AssertSuccessfulFinal(session, workbook, summary);
        InputSnapshot finalBefore = new InputSnapshotService().Capture(originalFinal);
        Assert.True(session.Results.OutputTargetExists);
        Assert.False(session.Results.CanExport); // An automatic final cannot be overwritten.

        session.Shell.OpenSettings(SettingsCategory.Evaluation);
        session.Design.DefinitionName = "T27 next draft, not the previous result";
        session.Design.Questions[0].Evaluators[0].Maximum = 20m;
        session.Shell.CloseSettings();
        ResultsCriterionViewModel criterion = Criterion(session, 3);
        Assert.Equal(10m, criterion.Range.Maximum);
        Assert.Equal(7m, criterion.AiRawScore);
        string revisedPath = Path.Combine(Path.GetDirectoryName(originalFinal)!, "revised.xlsx");
        session.Results.OutputPath = revisedPath;
        criterion.OverrideText = "11"; // Fits the next draft, but not the frozen run range.
        Assert.True(session.Results.HasOverrideErrors);
        Assert.False(session.Results.CanExport);
        await session.Results.ExportAsync(TestToken);
        Assert.False(File.Exists(revisedPath));
        Assert.False(session.Results.HasSuccessfulExport);

        criterion.OverrideText = "8.5";
        Assert.False(session.Results.HasOverrideErrors);
        Assert.True(session.Results.CanExport);
        Assert.Equal(85m, criterion.NormalizedScore);
        Assert.Equal(97.6m, Score(session, 3).FinalScore);
        await session.Results.ExportAsync(TestToken);
        Assert.Equal(ResultsOutputStatusCodes.Success, session.Results.LastExportCode);
        Assert.Equal(revisedPath, session.Results.LastSuccessfulExportPath);
        Assert.Equal(originalFinal, session.Results.FinalPath);
        Assert.False(session.Results.HasUnsavedOverrides);
        Assert.True(session.Results.OutputTargetExists);
        Assert.False(session.Results.CanExport);
        RunCriterionOverride[] overrides =
        [
            new() { SourceRowNumber = 3, QuestionId = "Q1", EvaluatorId = "E1", CriterionId = "C1", Value = "8.5" },
        ];
        AssertFinalWorkbook(workbook, summary, session.Results, revisedPath, overrides);
        using (SpreadsheetDocument document = SpreadsheetDocument.Open(revisedPath, false))
        {
            Worksheet results = Worksheet(document, AppOwnedSheetNameResolver.ResultsBaseName);
            AssertNumber(ResultCell(results, "Q1.E1.C1.AI_Raw", 3), 7m, formula: false);
            AssertNumber(ResultCell(results, "Q1.E1.C1.Override", 3), 8.5m, formula: false);
            AssertNumber(ResultCell(results, "Q1.E1.C1.Effective_Raw", 3), 8.5m, formula: true);
        }

        AssertDefinition(Definition(), summary.Snapshot.Definition);
        Assert.True(session.Shell.Settings.HasUnsavedChanges);
        Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(settingsPath, TestToken));
        Assert.Single(session.Boundary.Requests);
        AssertAiCalls(session.Ai, [FirstAnswer, "0", LastAnswer], [FirstSpecial, "0", LastSpecial], referenceCalls: 1);
        AssertUnchanged(workbook.Path, before);
        AssertUnchanged(originalFinal, finalBefore);
    }

    [Fact]
    public async Task Cancel_saves_a_real_partial_and_new_instances_resume_without_repeating_completed_AI_or_reference()
    {
        using X02TemporaryWorkbook workbook = CreateInput();
        InputSnapshot before = new InputSnapshotService().Capture(workbook.Path);
        string settingsPath = await SaveSettingsAsync(workbook);
        byte[] settingsBytes = await File.ReadAllBytesAsync(settingsPath, TestToken);
        RunSummary baseline;
        using (LocalSession uninterrupted = await OpenAdmittedAsync(settingsPath, workbook))
        {
            settingsBytes = await PrepareExecutionAsync(uninterrupted, settingsBytes);
            baseline = await RunAsync(uninterrupted);
            AssertSuccessfulFinal(uninterrupted, workbook, baseline);
            Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(settingsPath, TestToken));
        }

        CheckpointEnvelope saved;
        using (LocalSession interrupted = await OpenAdmittedAsync(settingsPath, workbook, expectStartupAuthentication: true))
        {
            // The baseline run cached the catalog. This new instance must check auth once
            // at startup without dispatching AI; PrepareExecution performs one explicit recheck.
            AssertPassive(interrupted, authenticationChecks: 1);
            Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(settingsPath, TestToken));
            bool stopped = false;
            interrupted.Ai.BeforeNormal = (payload, token) =>
            {
                if (payload.PrimarySource.Value == "0")
                {
                    interrupted.Execution.Cancel();
                    stopped = token.IsCancellationRequested;
                }
            };
            await PrepareExecutionAsync(interrupted);
            RunSummary partial = await RunAsync(interrupted);
            Assert.True(stopped);
            Assert.Equal(QuantificationRunStatusCodes.Cancelled, partial.StatusCode);
            Assert.True(partial.IsDurable);
            Assert.True(partial.IsPartial);
            Assert.False(partial.WasResumed);
            Assert.Null(partial.FinalPath);
            Assert.Null(partial.FinalizationCode);
            Assert.Equal([3], partial.CompletedRows.Select(row => row.SourceRowNumber));
            Assert.Equal(4, partial.CompletedOperationCount);
            Assert.Equal(9, partial.OperationCancelledCount);
            Assert.Equal([FirstAnswer, "0"], interrupted.Ai.NormalCalls.Select(call => call.Payload.PrimarySource.Value));
            Assert.Single(interrupted.Ai.SpecialCalls);
            Assert.Single(interrupted.Ai.ReferenceCalls);
            Assert.Equal(ResultsRowStatus.Unprocessed, Score(interrupted, 5).Status);
            Assert.Equal(ResultsRowStatus.Unprocessed, Score(interrupted, 6).Status);
            Assert.Null(Score(interrupted, 5).FinalScore);
            Assert.Null(Score(interrupted, 6).FinalScore);
            Assert.False(Criterion(interrupted, 5).CanOverride);
            Assert.True(interrupted.Results.IsPartial);
            Assert.Equal(string.Empty, interrupted.Results.FinalPath);
            string partialPath = Assert.IsType<string>(partial.PartialPath);
            Assert.True(File.Exists(partialPath));
            Assert.Equal(partialPath, interrupted.Results.PartialPath);
            CheckpointLoadResult loaded = new CheckpointStore().Load(partialPath, TestToken);
            Assert.True(loaded.IsSuccess, loaded.Code);
            Assert.False(string.IsNullOrWhiteSpace(loaded.PayloadSha256));
            saved = Assert.IsType<CheckpointEnvelope>(loaded.Envelope);
            Assert.Equal(CheckpointEnvelope.CurrentSchemaVersion, saved.SchemaVersion);
            Assert.Equal(before, saved.Input);
            Assert.Equal(workbook.Path, saved.InputPath);
            Assert.Equal(partial.DefinitionSha256, saved.DefinitionSha256);
            Assert.Equal(partial.Snapshot.CanonicalJson, saved.DefinitionCanonicalJson);
            using (JsonDocument definitionJson = JsonDocument.Parse(saved.DefinitionCanonicalJson))
            {
                Assert.Equal(CanonicalDefinitionSerializer.SchemaVersion,
                    definitionJson.RootElement.GetProperty("schemaVersion").GetString());
            }

            Assert.Equal(RuntimeFor(U04TestSupport.RuntimeIdentity()), saved.Runtime);
            Assert.Equal(ModelId, saved.NormalModelId);
            Assert.Equal(ModelId, saved.ReferenceModelId);
            Assert.Equal(FixedUtc, saved.StartedAtUtc);
            Assert.Equal(JsonSerializer.Serialize(partial.References), JsonSerializer.Serialize(saved.References));
            Assert.Equal(JsonSerializer.Serialize(partial.CompletedRows), JsonSerializer.Serialize(saved.CompletedRows));
            Assert.False(File.Exists(saved.FinalPath));
            using SpreadsheetDocument document = SpreadsheetDocument.Open(partialPath, false);
            Assert.Empty(new OpenXmlValidator().Validate(document, TestToken));
            Assert.Equal([SourceSheet, CheckpointStore.CheckpointSheetName], document.WorkbookPart!.Workbook!
                .Descendants<Sheet>().Select(sheet => sheet.Name?.Value));
            Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(settingsPath, TestToken));
        }

        // New store, input reader, VMs and orchestrator. Completed rows are still read to
        // revalidate evidence; only their AI operations (and the saved reference) are skipped.
        using LocalSession resumed = await OpenAdmittedAsync(settingsPath, workbook,
            new LocalAi { RejectReferenceCalls = true }, expectStartupAuthentication: true);
        AssertPassive(resumed, authenticationChecks: 1);
        Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(settingsPath, TestToken));
        await PrepareExecutionAsync(resumed);
        resumed.Execution.IsResumeMode = true;
        resumed.Execution.ResumePartialPath = saved.PartialPath;
        await resumed.Execution.PrepareResumeAsync(TestToken);
        Assert.True(resumed.Execution.ResumeReport?.CanResume);
        Assert.True(resumed.Execution.CanStart, resumed.Execution.ValidationSummary);
        Assert.Empty(resumed.Boundary.Requests);
        RunSummary completed = await RunAsync(resumed);
        string finalPath = AssertSuccessfulFinal(resumed, workbook, completed);
        Assert.True(completed.WasResumed);
        Assert.Equal(saved.FinalPath, finalPath);
        Assert.Equal(saved.StartedAtUtc, completed.StartedAtUtc);
        Assert.Equal(saved.PartialPath, completed.PartialPath);
        Assert.Equal(saved.Runtime, resumed.Boundary.Runtime);
        QuantificationRunRequest request = Assert.Single(resumed.Boundary.Requests);
        Assert.Null(request.OutputDirectory);
        Assert.Equal(saved.PartialPath, request.ResumePartialPath);
        AssertAiCalls(resumed.Ai, ["0", LastAnswer], ["0", LastSpecial], referenceCalls: 0);
        Assert.Equal(JsonSerializer.Serialize(saved.References), JsonSerializer.Serialize(completed.References));
        Assert.Equal(
            JsonSerializer.Serialize(saved.CompletedRows),
            JsonSerializer.Serialize(completed.CompletedRows.Take(saved.CompletedRows.Length)));
        Assert.Equal(JsonSerializer.Serialize(baseline.CompletedRows), JsonSerializer.Serialize(completed.CompletedRows));
        Assert.Equal(7, completed.OperationUsageObservedCount);
        Assert.Equal(70L, completed.OperationTokenUsage.InputTokens);
        Assert.Equal(JsonSerializer.Serialize(baseline.OperationTokenUsage), JsonSerializer.Serialize(completed.OperationTokenUsage));
        Assert.Equal(ReadWorksheetXml(baseline.FinalPath!, AppOwnedSheetNameResolver.ResultsBaseName),
            ReadWorksheetXml(finalPath, AppOwnedSheetNameResolver.ResultsBaseName));
        Assert.False(File.Exists(saved.PartialPath));
        Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(settingsPath, TestToken));
        AssertUnchanged(workbook.Path, before);
    }

    [Fact]
    public async Task Saved_definition_admission_rejects_missing_sheet_without_mutation_then_accepts_the_matching_input()
    {
        using X02TemporaryWorkbook workbook = CreateInput();
        using X02TemporaryWorkbook incompatible = CreateInput("Different source");
        InputSnapshot before = new InputSnapshotService().Capture(workbook.Path);
        InputSnapshot incompatibleBefore = new InputSnapshotService().Capture(incompatible.Path);
        string settingsPath = await SaveSettingsAsync(workbook);
        byte[] settingsBytes = await File.ReadAllBytesAsync(settingsPath, TestToken);
        using LocalSession session = new(settingsPath);
        await InitializeAsync(session);
        await session.Input.SetFilePathAsync(incompatible.Path, TestToken);
        session.Shell.OpenSettings(SettingsCategory.Common);
        session.Design.DefinitionName = "Uncommitted local design";
        QuantificationDefinition inputDraft = session.Input.DefinitionDraft;
        QuantificationDefinition designDraft = session.Design.Draft;
        var metadata = session.Input.Metadata;
        var snapshot = session.Input.Snapshot;
        Assert.True(session.Shell.Settings.CanApplySavedDefinition);

        Assert.False(await session.Shell.Settings.ApplySavedDefinitionAsync(TestToken));

        Assert.Equal("SOURCE_SHEET_NOT_FOUND", session.Input.SavedDefinitionApplicationError?.Code);
        Assert.Same(inputDraft, session.Input.DefinitionDraft);
        Assert.Same(designDraft, session.Design.Draft);
        Assert.Same(metadata, session.Input.Metadata);
        Assert.Same(snapshot, session.Input.Snapshot);
        AssertDefinition(Definition(), session.Shell.Settings.StoredDefinition!);
        Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(settingsPath, TestToken));
        Assert.False(session.Shell.Settings.IsApplying);
        Assert.False(session.Input.IsBusy);
        AssertPassive(session);
        Assert.False(Directory.Exists(Path.Combine(incompatible.Directory, "result")));

        await LoadAndAdmitAsync(session, workbook);
        Assert.Null(session.Input.SavedDefinitionApplicationError);
        settingsBytes = await PrepareExecutionAsync(session, settingsBytes);
        AssertSuccessfulFinal(session, workbook, await RunAsync(session));
        AssertUnchanged(workbook.Path, before);
        AssertUnchanged(incompatible.Path, incompatibleBefore);
        Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(settingsPath, TestToken));
    }

    [Fact]
    public async Task Real_atomic_final_validation_rejects_a_corrupted_cached_score_and_does_not_publish_it()
    {
        using X02TemporaryWorkbook workbook = CreateInput();
        InputSnapshot before = new InputSnapshotService().Capture(workbook.Path);
        string settingsPath = await SaveSettingsAsync(workbook);
        using LocalSession session = await OpenAdmittedAsync(settingsPath, workbook);
        await PrepareExecutionAsync(session);
        RunSummary summary = await RunAsync(session);
        string finalPath = AssertSuccessfulFinal(session, workbook, summary);
        InputSnapshot finalBefore = new InputSnapshotService().Capture(finalPath);
        OutputPackageValidationPlan plan = ExpectedOutputPlan(workbook.Path, summary);
        string rejectedPath = Path.Combine(Path.GetDirectoryName(finalPath)!, "must-not-be-published.xlsx");
        using (WorkingPackage candidate = WorkingPackage.Create(workbook.Path, rejectedPath))
        {
            // Copy only into this invocation's owned working file. The original input binding
            // is retained, and the candidate initially contains the actual successful final.
            File.Copy(finalPath, candidate.TemporaryPath, overwrite: true);
            Assert.True(new OutputPackageValidator().Validate(candidate.TemporaryPath, plan, TestToken).IsValid);
            using (SpreadsheetDocument document = candidate.OpenForEditing())
            {
                Worksheet results = Worksheet(document, plan.SheetNames.ResultsSheetName);
                Cell score = ResultCell(results, ResultsSheetWriter.FinalScoreHeader, 3);
                AssertNumber(score, 97.3m, formula: true);
                score.CellValue = new CellValue("0");
                results.Save();
            }

            AtomicOutputCommitResult rejected = new AtomicOutputCommitter().Commit(
                candidate, rejectedPath, workbook.Path, before, plan, TestToken);
            Assert.False(rejected.IsSuccess);
            Assert.Equal(AtomicOutputStatusCodes.OutputInvalid, rejected.Code);
            Assert.Null(rejected.FinalPath);
            OutputPackageValidationError error = Assert.Single(rejected.ValidationErrors);
            Assert.Equal("FORMULA_CACHE_MISMATCH", error.Code);
            Assert.Equal(ResultsSheetWriter.FinalScoreHeader, error.Identity.Field);
            Assert.False(File.Exists(candidate.TemporaryPath));
        }

        Assert.False(File.Exists(rejectedPath));
        AssertUnchanged(finalPath, finalBefore);
        AssertUnchanged(workbook.Path, before);
        Assert.Single(session.Boundary.Requests);
        AssertAiCalls(session.Ai, [FirstAnswer, "0", LastAnswer], [FirstSpecial, "0", LastSpecial], referenceCalls: 1);
    }

    private static QuantificationDefinition Definition()
    {
        QuantificationDefinition seed = U04TestSupport.Definition(3, 6);
        return seed with
        {
            Id = "DEF-T27",
            Name = "保存した定義 T27",
            Revision = "t27",
            HeaderRow = 2,
            BasePoints = 88m,
            SpecialPoints = 10m,
            SimilarityPenaltyWeight = 0.2m,
            RoundingDigits = 1,
            Questions =
            [
                seed.Questions[0] with
                {
                    QuestionText = "=保存した設問文（header の再生成で置換しない）",
                    Evaluators =
                    [
                        seed.Questions[0].Evaluators[0] with
                        {
                            CustomPromptTemplate = "T27 {設問}\r\n{回答}\n{補助情報}\n{評価項目}\n{最小点}..{最大点}",
                        },
                    ],
                    SpecialEvaluations =
                    [
                        new SpecialEvaluationDefinition
                        {
                            Id = "S1",
                            DisplayName = "保存した固有評価",
                            PrimarySourceColumn = "B",
                            SupportingSourceColumns = ["A"],
                            PromptTemplate = "固有評価 {回答}\n{補助情報}",
                        },
                    ],
                },
            ],
        };
    }

    private static async Task<string> SaveSettingsAsync(X02TemporaryWorkbook workbook, string? outputDirectory = null)
    {
        string path = Path.Combine(workbook.Directory, "preferences", "setting.txt");
        using LocalSession author = new(path);
        Assert.False(File.Exists(path));
        await author.Shell.Settings.InitializeAsync(TestToken);
        Assert.Equal(SettingsLoadStatus.Missing, author.Shell.Settings.LoadStatus);
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
        author.Input.HeaderRow = 2;
        await author.Input.SetFilePathAsync(workbook.Path, TestToken);
        Assert.True(author.Input.HasLoadedWorkbook);
        author.Shell.OpenSettings(SettingsCategory.Evaluation);
        // Set the synthetic graph through the existing design editing boundary, then let
        // Settings.SaveAsync synchronize the latest draft and perform real persistence.
        author.Design.SynchronizeFromInput(Definition(), author.Input.AvailableColumnNames);
        author.Execution.SelectedModelId = ModelId;
        author.Execution.MaxConcurrency = 2;
        author.Execution.OutputDirectoryOverride = outputDirectory;
        Assert.True(author.Design.IsAllocationValid);
        Assert.Equal(100m, author.Design.AllocationTotal);
        Assert.True(author.Shell.Settings.HasUnsavedChanges);
        await author.Shell.Settings.SaveAsync(TestToken);
        Assert.Equal(SettingsSaveStatus.Saved, author.Shell.Settings.SaveStatus);
        Assert.False(author.Shell.Settings.HasUnsavedChanges);
        AssertDefinition(Definition(), author.Input.DefinitionDraft);
        AssertDefinition(Definition(), author.Design.Draft);
        SettingsLoadResult reloaded = await new SettingsFileStore(path).LoadAsync(TestToken);
        Assert.Equal(SettingsLoadStatus.Loaded, reloaded.Status);
        ApplicationSettings saved = Assert.IsType<ApplicationSettings>(reloaded.Settings);
        Assert.Equal(ApplicationSettings.CurrentSchemaVersion, saved.SchemaVersion);
        AssertDefinition(Definition(), Assert.IsType<QuantificationDefinition>(saved.Definition));
        Assert.Equal(ModelId, saved.PreferredModelId);
        Assert.Equal(2, saved.MaxConcurrency);
        Assert.Equal(outputDirectory, saved.OutputDirectoryOverride);
        Assert.Equal(path, Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!)));
        Assert.False(Directory.Exists(outputDirectory ?? Path.Combine(workbook.Directory, "result")));
        AssertPassive(author);
        return path;
    }

    private static async Task InitializeAsync(LocalSession session, bool expectStartupAuthentication = false)
    {
        byte[] settingsBytes = await File.ReadAllBytesAsync(session.Shell.Settings.FilePath, TestToken);
        using (JsonDocument json = JsonDocument.Parse(settingsBytes))
        {
            Assert.Equal(expectStartupAuthentication,
                json.RootElement.TryGetProperty("cachedModels", out JsonElement catalog)
                    && catalog.ValueKind != JsonValueKind.Null);
        }

        session.StartupAuthenticationChecks = expectStartupAuthentication ? 1 : 0;
        AssertPassive(session);
        QuantificationDefinition input = session.Input.DefinitionDraft;
        QuantificationDefinition design = session.Design.Draft;
        Assert.False(session.Shell.Settings.IsInitialized);
        Assert.False(session.Shell.Settings.CanApplySavedDefinition);
        await session.Shell.Settings.InitializeAsync(TestToken);
        Assert.Equal(SettingsLoadStatus.Loaded, session.Shell.Settings.LoadStatus);
        Assert.Same(input, session.Input.DefinitionDraft);
        Assert.Same(design, session.Design.Draft);
        Assert.False(session.Input.HasLoadedWorkbook);
        Assert.False(session.Execution.IsConfigured);
        Assert.Equal(1, session.Input.HeaderRow);
        AssertDefinition(Definition(), session.Shell.Settings.StoredDefinition!);
        Assert.Equal(ModelId, session.Execution.PreferredModelId);
        Assert.Equal(2, session.Execution.MaxConcurrency);
        Assert.Equal(expectStartupAuthentication ? ModelId : null, session.Execution.SelectedModelId);
        Assert.Equal(expectStartupAuthentication ? ExecutionAuthenticationState.Available : ExecutionAuthenticationState.NotChecked,
            session.Execution.AuthenticationState);
        if (expectStartupAuthentication)
        {
            ModelCatalogPersistenceAssert.OnlyCatalogChanged(settingsBytes,
                await File.ReadAllBytesAsync(session.Shell.Settings.FilePath, TestToken), ExpectedCatalog);
            Assert.Equal(ExpectedCatalog.Select(model => model.Id), session.Execution.AvailableModelIds);
        }
        else
        {
            Assert.Null(session.Execution.CachedModels);
            Assert.Empty(session.Execution.AvailableModelIds);
        }

        Assert.False(session.Execution.CanStart);
        Assert.False(session.Execution.IsRunning);
        Assert.Null(session.Execution.LastRunContext);
        Assert.Null(session.Shell.Settings.LastSaveTask);
        Assert.False(session.Shell.Settings.HasUnsavedChanges);
        Assert.False(await session.Shell.Settings.ApplySavedDefinitionAsync(TestToken));
        // Startup may refresh metadata, never log in, dispatch a run, or call any AI operation.
        AssertPassive(session, authenticationChecks: session.StartupAuthenticationChecks);
        await session.Shell.Settings.InitializeAsync(TestToken);
        AssertPassive(session, authenticationChecks: session.StartupAuthenticationChecks);
        Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(session.Shell.Settings.FilePath, TestToken));
    }

    private static async Task LoadAndAdmitAsync(LocalSession session, X02TemporaryWorkbook workbook)
    {
        byte[] settingsBytes = await File.ReadAllBytesAsync(session.Shell.Settings.FilePath, TestToken);
        uint requestedHeader = checked((uint)session.Input.HeaderRow);
        await session.Input.SetFilePathAsync(workbook.Path, TestToken);
        Assert.True(session.Input.HasLoadedWorkbook);
        Assert.Equal(requestedHeader, session.Input.Metadata!.HeaderRowNumber);
        Assert.NotEqual(Definition().Id, session.Input.DefinitionDraft.Id);
        AssertDefinition(Definition(), session.Shell.Settings.StoredDefinition!);
        var previousMetadata = session.Input.Metadata;
        session.Shell.OpenSettings(SettingsCategory.Common);
        Assert.True(session.Shell.Settings.CanApplySavedDefinition);
        AssertPassive(session, authenticationChecks: session.StartupAuthenticationChecks);
        Assert.True(await session.Shell.Settings.ApplySavedDefinitionAsync(TestToken), session.Shell.Settings.ApplyStatusText);
        Assert.NotSame(previousMetadata, session.Input.Metadata);
        Assert.Equal(2U, session.Input.Metadata!.HeaderRowNumber);
        Assert.Equal(2, session.Input.HeaderRow);
        Assert.Equal(3, session.Input.FirstDataRow);
        Assert.Equal(6, session.Input.LastDataRow);
        Assert.False(session.Input.IsUsingSuggestedMapping);
        Assert.True(session.Input.CanContinue, session.Input.ValidationSummary);
        AssertDefinition(Definition(), session.Input.DefinitionDraft);
        AssertDefinition(Definition(), session.Design.Draft);
        Assert.False(session.Shell.Settings.HasUnsavedChanges);
        session.Shell.CloseSettings();
        AssertPassive(session, authenticationChecks: session.StartupAuthenticationChecks);
        Assert.Equal(settingsBytes, await File.ReadAllBytesAsync(session.Shell.Settings.FilePath, TestToken));
    }

    private static async Task<LocalSession> OpenAdmittedAsync(
        string settingsPath, X02TemporaryWorkbook workbook, LocalAi? ai = null, bool expectStartupAuthentication = false)
    {
        LocalSession session = new(settingsPath, ai);
        try
        {
            await InitializeAsync(session, expectStartupAuthentication);
            await LoadAndAdmitAsync(session, workbook);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private static void GoToExecution(LocalSession session)
    {
        session.Shell.CloseSettings();
        while (session.Shell.CurrentStep != WorkflowStep.Execution)
        {
            Assert.True(session.Shell.NextCommand.CanExecute(null));
            session.Shell.NextCommand.Execute(null);
        }

        Assert.True(session.Execution.IsConfigured);
        Assert.InRange(session.Execution.WorstCaseAttemptCount, 1L,
            (long)EvaluationRequestCapacityValidator.MaximumWorstCaseAttemptsPerRun);
    }

    private static async Task<byte[]> PrepareExecutionAsync(LocalSession session, byte[]? savedBytes = null)
    {
        GoToExecution(session);
        return await AuthenticateAsync(session, savedBytes);
    }

    private static async Task<byte[]> AuthenticateAsync(LocalSession session, byte[]? savedBytes = null)
    {
        byte[] before = await File.ReadAllBytesAsync(session.Shell.Settings.FilePath, TestToken);
        if (savedBytes is not null) Assert.Equal(savedBytes, before);
        AssertPassive(session, authenticationChecks: session.StartupAuthenticationChecks);
        await session.Execution.CheckAuthenticationAsync(TestToken);
        Assert.Equal(session.StartupAuthenticationChecks + 1, session.Authentication.CallCount);
        Assert.Equal(ModelId, session.Execution.SelectedModelId);
        Assert.Empty(session.Execution.TechnicalErrors);
        Assert.True(session.Execution.CanStart, session.Execution.ValidationSummary);
        Assert.Empty(session.Boundary.Requests);
        Assert.Empty(session.Ai.ReferenceCalls);
        Assert.Empty(session.Ai.NormalCalls);
        Assert.Equal(0, session.Login.CallCount);
        AssertPassive(session, authenticationChecks: session.StartupAuthenticationChecks + 1);
        return ModelCatalogPersistenceAssert.OnlyCatalogChanged(before,
            await File.ReadAllBytesAsync(session.Shell.Settings.FilePath, TestToken), ExpectedCatalog);
    }

    private static async Task<RunSummary> RunAsync(LocalSession session)
    {
        Assert.True(session.Execution.CanStart, session.Execution.ValidationSummary);
        await session.Execution.StartAsync(TestToken);
        Assert.False(session.Execution.IsRunning);
        Assert.False(session.Execution.CanCancel);
        ExecutionRunContext context = Assert.IsType<ExecutionRunContext>(session.Execution.LastRunContext);
        // Completion is established by this run's context and real artifacts, not next-start
        // eligibility: successful resume deletes its partial, so that old path is no longer valid.
        Assert.Equal(context.Summary.StatusCode == QuantificationRunStatusCodes.Success ? WorkflowStep.Results : WorkflowStep.Execution, session.Shell.CurrentStep);
        Assert.True(session.Results.IsLoaded);
        Assert.True(session.Results.IsAutomaticOutput);
        QuantificationRunRequest request = Assert.Single(session.Boundary.Requests);
        Assert.True(request.UseDurableWorkflow);
        Assert.Equal(ModelId, request.ModelId);
        Assert.Equal(2, request.MaxConcurrency);
        AssertDefinition(Definition(), request.DraftDefinition);
        Assert.Equal(session.StartupAuthenticationChecks + 1, session.Authentication.CallCount);
        Assert.Equal(0, session.Login.CallCount);
        Assert.Null(session.Execution.LastLoginTask);
        return context.Summary;
    }

    private static string AssertSuccessfulFinal(
        LocalSession session, X02TemporaryWorkbook workbook, RunSummary summary, int operationFailures = 0)
    {
        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        Assert.Equal(AtomicOutputStatusCodes.Success, summary.FinalizationCode);
        Assert.True(summary.IsDurable);
        Assert.False(summary.IsPartial);
        Assert.False(summary.PartialCleanupFailed);
        Assert.Equal(4, summary.PlannedEvaluationCount);
        Assert.Equal(4, summary.CompletedEvaluationCount);
        Assert.Equal(13, summary.PlannedOperationCount);
        Assert.Equal(13, summary.CompletedOperationCount);
        Assert.Equal(operationFailures, summary.OperationFailureCount);
        Assert.Equal(0, summary.OperationCancelledCount);
        Assert.Equal([3, 4, 5, 6], summary.CompletedRows.Select(row => row.SourceRowNumber));
        Assert.True(summary.Snapshot.HasValidHash());
        AssertDefinition(Definition(), summary.Snapshot.Definition);
        Assert.Equal(session.Input.Snapshot, summary.InputSnapshot);
        Assert.Equal(FixedUtc, summary.StartedAtUtc);
        Assert.Equal(FixedUtc, summary.EndedAtUtc);
        CheckpointReference reference = Assert.Single(summary.References);
        Assert.Equal(ReferenceAnswer, reference.Answer);
        Assert.Equal(ResultsStatusCodes.Success, reference.StatusCode);
        Assert.Equal(1, reference.AttemptCount);
        Assert.All(summary.Units, unit => Assert.InRange(unit.AttemptCount, 0, RetryAndCleanupCoordinator.MaximumTransientAttempts));
        string finalPath = Assert.IsType<string>(summary.FinalPath);
        Assert.NotEqual(workbook.Path, finalPath);
        Assert.True(File.Exists(finalPath));
        Assert.False(File.Exists(summary.PartialPath));
        Assert.Equal(finalPath, session.Results.FinalPath);
        Assert.Contains(session.Boundary.Progress, item => item.Stage == DurableEvaluationStage.SavingCheckpoint);
        Assert.Contains(session.Boundary.Progress, item => item.Stage == DurableEvaluationStage.FinalizingWorkbook);
        AssertFinalWorkbook(workbook, summary, session.Results, finalPath);
        return finalPath;
    }

    private static OutputPackageValidationPlan ExpectedOutputPlan(
        string inputPath, RunSummary summary, IEnumerable<RunCriterionOverride>? overrides = null)
    {
        RunOutputPreparation preparation = summary.PrepareOutput(overrides);
        Assert.True(preparation.IsExportReady);
        // Build the expected closed-AST formulas/caches with the production writers from
        // the input and frozen results, NOT by reading formulas back from the output under test.
        using MemoryStream memory = new(); // Expandable: Open XML adds parts to this copy.
        memory.Write(File.ReadAllBytes(inputPath));
        memory.Position = 0;
        using SpreadsheetDocument document = SpreadsheetDocument.Open(memory, true);
        AppOwnedSheetNames names = new AppOwnedSheetNameResolver().Resolve(document);
        ConfigCellAddressMap config = new ConfigSheetWriter().Write(document, summary.Snapshot, names);
        ResultsSheetWriteResult results = new ResultsSheetWriter().Write(
            document, summary.Snapshot, names, config, preparation.Rows);
        Assert.Equal(4, results.DataRowCount);
        Assert.Equal(33, results.ColumnCount);
        Assert.Equal(48, results.FormulaCells.Length);
        return OutputPackageValidationPlan.Capture(inputPath, names, config.FormulaCells.Concat(results.FormulaCells)
            .Select(cell => new ExpectedFormulaCell(cell.Definition, cell.CachedValue)));
    }

    private static void AssertFinalWorkbook(
        X02TemporaryWorkbook input, RunSummary summary, ResultsOutputViewModel preview, string path,
        IEnumerable<RunCriterionOverride>? overrides = null)
    {
        OutputPackageValidationPlan plan = ExpectedOutputPlan(input.Path, summary, overrides);
        Assert.Equal(50, plan.ExpectedFormulaCells.Length);
        OutputPackageValidationResult validation = new OutputPackageValidator().Validate(path, plan, TestToken);
        Assert.True(validation.IsValid, string.Join(',', validation.Errors.Select(error => error.Code)));
        Assert.Empty(validation.Errors);
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, false);
        Assert.Empty(new OpenXmlValidator().Validate(document, TestToken));
        Workbook workbook = document.WorkbookPart!.Workbook!;
        Assert.Equal([SourceSheet, .. plan.SheetNames.AllSheetNames], workbook.Descendants<Sheet>().Select(sheet => sheet.Name?.Value));
        Assert.DoesNotContain(workbook.Descendants<Sheet>(), sheet => sheet.Name?.Value == CheckpointStore.CheckpointSheetName);
        Assert.Equal(ReadWorksheetXml(input.Path, SourceSheet), Worksheet(document, SourceSheet).OuterXml);
        Worksheet config = Worksheet(document, plan.SheetNames.ConfigSheetName);
        Assert.Equal(2, config.Descendants<CellFormula>().Count());
        string canonical = string.Concat(config.Descendants<Row>().Where(row => Text(row, "A") == "CANONICAL_JSON")
            .OrderBy(row => int.Parse(Text(row, "X"), CultureInfo.InvariantCulture)).Select(row => Text(row, "Y")));
        Assert.Equal(summary.Snapshot.CanonicalJson, canonical);
        Row definition = Assert.Single(config.Descendants<Row>(), row => Text(row, "A") == "DEFINITION");
        Assert.Equal(summary.Snapshot.Definition.Id, Text(definition, "C"));
        Assert.Equal(summary.DefinitionSha256, Text(definition, "W"));
        Worksheet references = Worksheet(document, plan.SheetNames.ReferencesSheetName);
        Assert.Empty(references.Descendants<CellFormula>());
        Cell answer = Assert.Single(references.Descendants<Cell>(), cell => cell.InnerText == ReferenceAnswer);
        Assert.Equal(CellValues.InlineString, answer.DataType?.Value);
        Assert.Null(answer.CellFormula);
        Worksheet run = Worksheet(document, plan.SheetNames.RunSheetName);
        Assert.Empty(run.Descendants<CellFormula>());
        Dictionary<string, string> records = run.Descendants<Row>().Skip(1)
            .ToDictionary(row => Text(row, "A"), row => Text(row, "B"), StringComparer.Ordinal);
        Assert.Equal(summary.InputSnapshot.Sha256, records["InputSha256"]);
        Assert.Equal(summary.DefinitionSha256, records["DefinitionSha256"]);
        Assert.Equal(ModelId, records["ModelIdentity"]);
        Assert.Equal("13", records["PlannedEvaluationCount"]);
        Assert.Equal("13", records["CompletedEvaluationCount"]);
        Assert.Equal(summary.OperationFailureCount.ToString(CultureInfo.InvariantCulture), records["ErrorCount"]);
        CalculationProperties properties = Assert.IsType<CalculationProperties>(workbook.CalculationProperties);
        Assert.Equal(CalculateModeValues.Auto, properties.CalculationMode?.Value);
        Assert.True(properties.FullCalculationOnLoad?.Value);
        Assert.True(properties.ForceFullCalculation?.Value);

        Worksheet results = Worksheet(document, plan.SheetNames.ResultsSheetName);
        Assert.Equal(5, results.Descendants<Row>().Count());
        Assert.Equal(48, results.Descendants<CellFormula>().Count());
        Assert.Equal(4, preview.Results.Count);
        Assert.Equal(4, preview.RowScores.Count);
        foreach (ResultsCriterionViewModel criterion in preview.Results)
        {
            int row = criterion.SourceRowNumber;
            string prefix = $"{criterion.QuestionId}.{criterion.EvaluatorId}.{criterion.CriterionId}.";
            AssertNumber(ResultCell(results, prefix + ResultsSheetWriter.AiRawSuffix, row), criterion.AiRawScore, formula: false);
            AssertNumber(ResultCell(results, prefix + ResultsSheetWriter.EffectiveRawSuffix, row), criterion.EffectiveRaw, formula: true);
            AssertNumber(ResultCell(results, prefix + ResultsSheetWriter.NormalizedSuffix, row), criterion.NormalizedScore, formula: true);
            AssertNumber(ResultCell(results, "Q1.E1.Evaluator_Score", row), criterion.EvaluatorScore, formula: true);
            AssertNumber(ResultCell(results, "Q1.Question_Normalized", row), criterion.QuestionScore, formula: true);
        }

        foreach (ResultsRowScoreViewModel row in preview.RowScores)
        {
            AssertNumber(ResultCell(results, ResultsSheetWriter.SpecialEarnedHeader, row.SourceRowNumber), row.SpecialEarned, formula: true);
            AssertNumber(ResultCell(results, "Q1.Similarity_Penalty", row.SourceRowNumber), row.SimilarityPenalty, formula: true);
            AssertNumber(ResultCell(results, ResultsSheetWriter.FinalRawHeader, row.SourceRowNumber), row.FinalRaw, formula: true);
            AssertNumber(ResultCell(results, ResultsSheetWriter.FinalScoreHeader, row.SourceRowNumber), row.FinalScore, formula: true);
        }

        Cell evidence = ResultCell(results, "Q1.E1.C1.Evidence", 3);
        Assert.Equal(FirstAnswer, evidence.InnerText);
        Assert.Equal(CellValues.InlineString, evidence.DataType?.Value);
        Assert.Null(evidence.CellFormula);
    }

    private static void AssertNumber(Cell cell, decimal? expected, bool formula)
    {
        Assert.Equal(formula, cell.CellFormula is not null);
        if (expected is null)
        {
            Assert.Null(cell.CellValue);
            Assert.Null(cell.DataType);
        }
        else
        {
            Assert.Equal(CellValues.Number, cell.DataType?.Value);
            Assert.True(decimal.TryParse(cell.CellValue?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal actual));
            Assert.Equal(expected.Value, actual);
        }
    }

    private static void AssertDefinition(QuantificationDefinition expected, QuantificationDefinition actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Questions.Select(question => question.Id), actual.Questions.Select(question => question.Id));
        Assert.Equal(expected.Questions.SelectMany(question => question.Evaluators).Select(evaluator => evaluator.Id),
            actual.Questions.SelectMany(question => question.Evaluators).Select(evaluator => evaluator.Id));
        Assert.Equal(expected.Questions.SelectMany(question => question.Evaluators).SelectMany(evaluator => evaluator.Criteria).Select(criterion => criterion.Id),
            actual.Questions.SelectMany(question => question.Evaluators).SelectMany(evaluator => evaluator.Criteria).Select(criterion => criterion.Id));
        Assert.Equal(expected.Questions.SelectMany(question => question.SpecialEvaluations).Select(special => special.Id),
            actual.Questions.SelectMany(question => question.SpecialEvaluations).Select(special => special.Id));
        Assert.Equal(Canonical.SerializeToUtf8Bytes(expected), Canonical.SerializeToUtf8Bytes(actual));
        Assert.Equal(Canonical.ComputeSha256(expected), Canonical.ComputeSha256(actual));
    }

    private static void AssertUnchanged(string path, InputSnapshot expected)
    {
        InputSnapshot actual = new InputSnapshotService().Capture(path);
        Assert.Equal(expected.Sha256, actual.Sha256);
        Assert.Equal(expected.SizeBytes, actual.SizeBytes);
        Assert.Equal(expected.LastWriteTimeUtc, actual.LastWriteTimeUtc);
    }

    private static void AssertPassive(LocalSession session, int authenticationChecks = 0)
    {
        Assert.Equal(authenticationChecks, session.Authentication.CallCount);
        Assert.Equal(0, session.Login.CallCount);
        Assert.Null(session.Execution.LastLoginTask);
        Assert.Empty(session.Boundary.Requests);
        Assert.Empty(session.Ai.ReferenceCalls);
        Assert.Empty(session.Ai.NormalCalls);
        Assert.Empty(session.Ai.SpecialCalls);
    }

    private static void AssertAiCalls(LocalAi ai, string[] normalAnswers, string[] specialAnswers, int referenceCalls)
    {
        Assert.Equal(referenceCalls, ai.ReferenceCalls.Count);
        Assert.Equal(normalAnswers, ai.NormalCalls.Select(call => call.Payload.PrimarySource.Value));
        Assert.Equal(specialAnswers, ai.SpecialCalls.Select(call => call.Payload.PrimarySource.Value));
        Assert.Equal(specialAnswers, ai.NormalCalls.Select(call => Assert.Single(call.Payload.SupportingSources).Value));
        Assert.Equal(normalAnswers, ai.SpecialCalls.Select(call => Assert.Single(call.Payload.SupportingSources).Value));
        Assert.All(ai.ReferenceCalls, payload => Assert.Equal("Q1", payload.QuestionId));
        Assert.All(ai.NormalCalls, call =>
        {
            Assert.Equal(ModelId, call.ModelId);
            Assert.Equal("Q1", call.Payload.QuestionId);
            Assert.Equal("E1", call.Payload.EvaluatorId);
            Assert.Equal("C1", Assert.Single(call.Payload.ExpectedCriteria).CriterionId);
            Assert.Equal(["A", "B"], call.Payload.Sources.Select(source => source.SourceColumnId));
            Assert.DoesNotContain(UnselectedText, call.Payload.RenderedPrompt, StringComparison.Ordinal);
        });
        Assert.All(ai.SpecialCalls, call =>
        {
            Assert.Equal(ModelId, call.ModelId);
            Assert.Equal("Q1", call.Payload.QuestionId);
            Assert.Equal("S1", call.Payload.SpecialEvaluationId);
            Assert.Equal(["B", "A"], call.Payload.Sources.Select(source => source.SourceColumnId));
            Assert.DoesNotContain(UnselectedText, call.Payload.RenderedPrompt, StringComparison.Ordinal);
        });
    }

    private static ResultsCriterionViewModel Criterion(LocalSession session, int row) =>
        Assert.Single(session.Results.Results, item => item.SourceRowNumber == row);

    private static ResultsRowScoreViewModel Score(LocalSession session, int row) =>
        Assert.Single(session.Results.RowScores, item => item.SourceRowNumber == row);

    private static Cell ResultCell(Worksheet worksheet, string header, int row)
    {
        Row headers = worksheet.Descendants<Row>().Single(item => item.RowIndex?.Value == 2);
        Cell heading = Assert.Single(headers.Elements<Cell>(), cell => cell.InnerText == header);
        string reference = Column(heading.CellReference?.Value) + row.ToString(CultureInfo.InvariantCulture);
        return Assert.Single(worksheet.Descendants<Cell>(), cell => cell.CellReference?.Value == reference);
    }

    private static Worksheet Worksheet(SpreadsheetDocument document, string name)
    {
        WorkbookPart part = document.WorkbookPart ?? throw new InvalidDataException("Workbook part missing.");
        Sheet sheet = part.Workbook!.Descendants<Sheet>().Single(item => item.Name?.Value == name);
        return ((WorksheetPart)part.GetPartById(sheet.Id!.Value!)).Worksheet
            ?? throw new InvalidDataException("Worksheet missing.");
    }

    private static string ReadWorksheetXml(string path, string name)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, false);
        return Worksheet(document, name).OuterXml;
    }

    private static string Column(string? reference) => new((reference ?? string.Empty).TakeWhile(char.IsAsciiLetter).ToArray());

    private static string Text(Row row, string column) => row.Elements<Cell>()
        .SingleOrDefault(cell => Column(cell.CellReference?.Value) == column)?.InnerText ?? string.Empty;

    private static X02TemporaryWorkbook CreateInput(string sheetName = SourceSheet)
    {
        X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            sheetName, 2, 6, 3, new X02Header(1, "Report answer"), new X02Header(2, "Special answer"), new X02Header(3, "Unselected"));
        try
        {
            // Populate only this new synthetic fixture, before any input snapshot is taken.
            using SpreadsheetDocument document = SpreadsheetDocument.Open(workbook.Path, true);
            Worksheet worksheet = Worksheet(document, sheetName);
            SheetData data = worksheet.GetFirstChild<SheetData>()!;
            Row header = (Row)data.Elements<Row>().Single(row => row.RowIndex?.Value == 2).CloneNode(true);
            data.RemoveAllChildren<Row>();
            data.Append(new Row(Inline("A1", "Synthetic export metadata")) { RowIndex = 1 }, header);
            data.Append(new Row(Inline("A3", FirstAnswer), Inline("B3", FirstSpecial), Inline("C3", UnselectedText)) { RowIndex = 3 });
            data.Append(new Row(Inline("B4", "   "), Inline("C4", UnselectedText)) { RowIndex = 4 }); // A4 is genuinely absent.
            data.Append(new Row(Number("A5", "0"), Number("B5", "0"), Inline("C5", UnselectedText)) { RowIndex = 5 });
            data.Append(new Row(Inline("A6", LastAnswer), Inline("B6", LastSpecial), Inline("C6", UnselectedText)) { RowIndex = 6 });
            worksheet.Save();
            return workbook;
        }
        catch
        {
            workbook.Dispose();
            throw;
        }
    }

    private static Cell Inline(string reference, string value) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value)),
    };

    private static Cell Number(string reference, string value) => new()
    {
        CellReference = reference,
        DataType = CellValues.Number,
        CellValue = new CellValue(value),
    };

    private static CheckpointRuntimeIdentity RuntimeFor(CopilotRuntimeIdentity identity) => new()
    {
        ApplicationIdentity = TestApplicationIdentity,
        CliVersion = identity.CliVersion,
        CliSha256 = identity.CliSha256,
        SdkInformationalVersion = identity.SdkInformationalVersion,
    };

    private sealed class LocalSession : IDisposable
    {
        internal LocalSession(string settingsPath, LocalAi? ai = null)
        {
            Ai = ai ?? new LocalAi();
            Input = new InputViewModel(new InputWorkbookLoader());
            Design = new QuantificationDesignViewModel(Input.DefinitionDraft, Input.AvailableColumnNames);
            Authentication = new RecordingAuthenticationBoundary(new ExecutionAuthenticationSnapshot(
                ExecutionAuthenticationState.Available,
                [U04TestSupport.Model(ModelId), U04TestSupport.Model("auto")], U04TestSupport.RuntimeIdentity()));
            Boundary = new LocalDurableRunBoundary(Ai);
            Execution = new ExecutionViewModel(Authentication, Boundary, new BundledCopilotLoginService(
                Login, _ => throw new InvalidOperationException("T27 must never start a CLI process.")));
            Results = new ResultsOutputViewModel(new ResultsOutputBoundary());
            Shell = new MainWindowViewModel(new WorkflowNavigator(), Input, Design, Execution, Results,
                new SettingsFileStore(settingsPath));
        }

        internal LocalAi Ai { get; }
        internal InputViewModel Input { get; }
        internal QuantificationDesignViewModel Design { get; }
        internal RecordingAuthenticationBoundary Authentication { get; }
        internal DeniedLoginResolver Login { get; } = new();
        internal LocalDurableRunBoundary Boundary { get; }
        internal ExecutionViewModel Execution { get; }
        internal ResultsOutputViewModel Results { get; }
        internal MainWindowViewModel Shell { get; }
        internal int StartupAuthenticationChecks { get; set; }
        public void Dispose() => Shell.Dispose();
    }

    private sealed class DeniedLoginResolver : ICopilotCliPathResolver
    {
        internal int CallCount { get; private set; }

        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult<string?>(null);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => FixedUtc;
    }

    // QuantificationRunBoundary constructs live SDK runners internally. This test-only
    // composition adapter changes those runners, not the durable/file implementation.
    private sealed class LocalDurableRunBoundary(LocalAi ai) : IQuantificationRunBoundary
    {
        internal List<QuantificationRunRequest> Requests { get; } = [];
        internal ConcurrentQueue<DurableEvaluationProgress> Progress { get; } = new();
        internal CheckpointRuntimeIdentity? Runtime { get; private set; }

        public Task<RunSummary> RunAsync(
            QuantificationRunRequest request, Action<EvaluationProgress>? progress, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            CheckpointRuntimeIdentity runtime = RuntimeFor(request.RuntimeIdentity
                ?? throw new InvalidOperationException("A synthetic authenticated runtime is required."));
            Runtime = runtime;
            DurableQuantificationOrchestrator orchestrator = new(
                new OpenXmlEvaluationRowSource(request.InputPath), ai, ai, ai,
                new PhysicalInputSnapshotBoundary(), new CheckpointStore(), new OutputPathPlanner(),
                new WorkbookDurableRunFinalizer(), new PhysicalPartialCheckpointCleaner(), new FixedTimeProvider());
            return orchestrator.RunAsync(new DurableQuantificationRunRequest
            {
                Run = request,
                Runtime = runtime,
                OutputDirectory = request.OutputDirectory,
                ResumePartialPath = request.ResumePartialPath,
            }, value =>
            {
                Progress.Enqueue(value);
                EvaluationProgressStatus status = value.Stage switch
                {
                    DurableEvaluationStage.Cancelling => EvaluationProgressStatus.Cancelling,
                    DurableEvaluationStage.Completed when value.StatusCode == QuantificationRunStatusCodes.Success => EvaluationProgressStatus.Completed,
                    DurableEvaluationStage.Completed => EvaluationProgressStatus.Cancelled,
                    _ => EvaluationProgressStatus.Running,
                };
                progress?.Invoke(new EvaluationProgress(value.OperationTotal, value.OperationCompleted, value.InFlight,
                    status, value.Stage, value.ReferenceCompleted, value.ReferenceTotal, value.RowCompleted, value.RowTotal,
                    value.StatusCode, value.FinalPath, value.PartialPath));
            }, cancellationToken);
        }
    }

    private sealed class LocalAi : IEvaluationRunner, IReferenceAnswerOperationRunner,
        ISpecialEvaluationOperationRunner
    {
        internal ConcurrentQueue<(SafeEvaluationPayload Payload, string ModelId)> NormalCalls { get; } = new();
        internal ConcurrentQueue<(SafeSpecialEvaluationPayload Payload, string ModelId)> SpecialCalls { get; } = new();
        internal ConcurrentQueue<SafeReferenceAnswerPayload> ReferenceCalls { get; } = new();
        internal Action<SafeEvaluationPayload, CancellationToken>? BeforeNormal { get; set; }
        internal bool FailLastRow { get; init; }
        internal bool RejectReferenceCalls { get; init; }

        private static EvaluationTokenUsage Usage() => new(true, 10, 2, 1, 3, 4);

        public Task<EvaluationRunnerResult> EvaluateAsync(
            SafeEvaluationPayload payload, string modelId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NormalCalls.Enqueue((payload, modelId));
            BeforeNormal?.Invoke(payload, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (FailLastRow && payload.PrimarySource.Value == LastAnswer)
                return Task.FromResult(EvaluationRunnerResult.Failed(ResultsStatusCodes.AiTimeout));
            QuantificationResult result = U01TestSupport.ValidResult(payload, _ => payload.PrimarySource.Value == "0" ? 0m : 7m);
            return Task.FromResult(EvaluationRunnerResult.Succeeded(result with
            {
                Criteria = result.Criteria.Select(criterion => criterion with
                {
                    Evidence = payload.PrimarySource.Value,
                    EvidenceSource = EvidenceSourceKind.PrimaryAnswer,
                    EvidenceSourceColumnId = payload.PrimarySource.SourceColumnId,
                }).ToImmutableArray(),
            }, tokenUsage: Usage()));
        }

        public Task<AuxiliaryOperationResult<ReferenceAnswerResult>> EvaluateAsync(
            SafeReferenceAnswerPayload payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReferenceCalls.Enqueue(payload);
            if (RejectReferenceCalls) throw new InvalidOperationException("The real checkpoint must supply the reference.");
            return Task.FromResult(AuxiliaryOperationResult<ReferenceAnswerResult>.Succeeded(new ReferenceAnswerResult
            {
                QuestionId = payload.QuestionId,
                Answer = ReferenceAnswer,
            }, tokenUsage: Usage()));
        }

        public Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(
            SafeSpecialEvaluationPayload payload, string modelId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpecialCalls.Enqueue((payload, modelId));
            if (FailLastRow && payload.PrimarySource.Value == LastSpecial)
                return Task.FromResult(AuxiliaryOperationResult<SpecialQuantificationResult>.Failed(ResultsStatusCodes.AiTimeout));
            return Task.FromResult(AuxiliaryOperationResult<SpecialQuantificationResult>.Succeeded(new SpecialQuantificationResult
            {
                SpecialEvaluationId = payload.SpecialEvaluationId,
                Score = payload.PrimarySource.Value == "0" ? 0m : 0.8m,
                Reason = "T27 special reason",
                Evidence = payload.PrimarySource.Value,
                EvidenceSource = EvidenceSourceKind.PrimaryAnswer,
                EvidenceSourceColumnId = payload.PrimarySource.SourceColumnId,
            }, tokenUsage: Usage()));
        }

    }
}
