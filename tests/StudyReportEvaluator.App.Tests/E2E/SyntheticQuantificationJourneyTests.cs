using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Validation;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.E2E;

public sealed class SyntheticQuantificationJourneyTests
{
    private const string ModelId = "synthetic-model-e01";

    [Fact]
    public async Task Fixed_seed_531_row_workbook_completes_the_local_atomic_quantification_journey()
    {
        using SyntheticWorkbook workbook = SyntheticWorkbookFactory.Create();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        InputSnapshotService snapshots = new();
        InputSnapshot inputBefore = snapshots.Capture(workbook.Path);
        string originalWorksheetXml = ReadWorksheetXml(
            workbook.Path,
            SyntheticWorkbookFactory.SourceSheetName);

        FileFormatClassificationResult classification = new FileFormatClassifier().Classify(workbook.Path);
        Assert.True(classification.IsAccepted);
        Assert.Equal(FileFormatClassification.StandardXlsx, classification.Classification);

        WorkbookMetadata metadata = new WorkbookMetadataReader().Read(workbook.Path);
        WorksheetMetadata original = Assert.Single(metadata.Worksheets);
        Assert.Equal(SyntheticWorkbookFactory.SourceSheetName, original.Name);
        Assert.Equal(SyntheticWorkbookFactory.Dimension, original.DimensionReference);
        Assert.Equal(531U, original.RowCount);
        Assert.Equal(12U, original.ColumnCount);

        ColumnMappingSuggestionResult suggestions = new ColumnMappingSuggester().Suggest(metadata);
        WorksheetMappingSuggestion suggestion = Assert.IsType<WorksheetMappingSuggestion>(
            suggestions.SuggestedWorksheet);
        Assert.Equal(["F", "G", "H", "I", "J", "K"], suggestion.InitialTargetColumns);
        Assert.Equal(["A", "B", "C", "D", "E", "L"], suggestion.InitiallyUnselectedColumns);
        ColumnMappingCandidate secondPrompt = Assert.Single(
            suggestion.Candidates,
            candidate => candidate.ColumnName == "J");
        Assert.True(secondPrompt.IsStudentPromptPrimaryCandidate);
        Assert.Equal(["K"], secondPrompt.SuggestedSupportingColumns);

        QuantificationDefinition definition = CreateDefinition();
        ColumnMappingValidationResult mappingValidation = new ColumnMappingValidator().Validate(
            metadata,
            definition);
        Assert.True(mappingValidation.IsValid);
        Assert.Equal(SyntheticWorkbookFactory.DataRowCount, mappingValidation.Mapping!.SelectedRowCount);

        SyntheticEvaluationRowSource rowSource = new(workbook);
        FakeCopilotTransport fakeCopilot = new(workbook);
        List<EvaluationProgress> progress = [];
        QuantificationOrchestrator orchestrator = new(rowSource, fakeCopilot);
        RunSummary summary = await orchestrator.RunAsync(
            new QuantificationRunRequest
            {
                DraftDefinition = definition,
                WorkbookMetadata = metadata,
                InputPath = workbook.Path,
                ModelId = ModelId,
                MaximumPromptTokens = 64_000,
                MaximumContextWindowTokens = 128_000,
                MaxConcurrency = 3,
            },
            progress.Add,
            cancellationToken);

        int expectedPlanCount = SyntheticWorkbookFactory.DataRowCount * 2;
        int expectedEmptyCount = workbook.CountBlankRows("F") + workbook.CountBlankRows("J");
        int expectedCallCount = expectedPlanCount - expectedEmptyCount;
        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        Assert.Equal(inputBefore, summary.InputSnapshot);
        Assert.True(summary.Snapshot.HasValidHash());
        Assert.Equal(expectedPlanCount, summary.PlannedEvaluationCount);
        Assert.Equal(expectedPlanCount, summary.CompletedEvaluationCount);
        Assert.Equal(expectedCallCount, summary.SucceededCount);
        Assert.Equal(expectedEmptyCount, summary.EmptyCount);
        Assert.Equal(0, summary.FailureCount);
        Assert.Equal(0, summary.CancelledCount);
        Assert.False(summary.IsPartial);
        Assert.Equal(expectedPlanCount + SyntheticWorkbookFactory.DataRowCount, rowSource.Reads.Length);
        Assert.Equal(expectedCallCount, fakeCopilot.Calls.Length);
        Assert.InRange(fakeCopilot.MaximumObservedConcurrency, 1, 3);
        Assert.Equal(
            [["F", "H", "J", "K"], ["F", "H"], ["J", "K"]],
            rowSource.Reads
                .Select(item => item.SelectedColumns)
                .Distinct(ImmutableArraySequenceComparer.Instance)
                .OrderBy(columns => columns[0], StringComparer.Ordinal)
                .Select(columns => columns.ToArray()));
        Assert.DoesNotContain(fakeCopilot.Calls, call =>
            call.QuestionId == "Q-DISABLED"
            || call.EvaluatorId == "E-KNOW-DISABLED"
            || call.CriterionIds.Contains("C-KNOW-DISABLED", StringComparer.Ordinal));
        Assert.All(fakeCopilot.Calls, call =>
            Assert.All(
                summary.Units
                    .Single(unit => unit.Item.SourceRowNumber == call.SourceRowNumber
                        && unit.Item.QuestionId == call.QuestionId
                        && unit.Item.EvaluatorId == call.EvaluatorId)
                    .AcceptedResult!
                    .Criteria,
                criterion => Assert.Contains(
                    SyntheticWorkbookFactory.RowMarker(call.SourceRowNumber),
                    criterion.Evidence,
                    StringComparison.Ordinal)));

        Assert.NotEmpty(progress);
        Assert.Equal(0, progress[0].Completed);
        Assert.All(progress, item =>
        {
            Assert.Equal(expectedPlanCount, item.Total);
            Assert.InRange(item.InFlight, 0, 3);
        });
        Assert.True(progress.Zip(progress.Skip(1), (left, right) => left.Completed <= right.Completed).All(value => value));
        EvaluationProgress completedProgress = progress[^1];
        Assert.Equal(EvaluationProgressStatus.Completed, completedProgress.Status);
        Assert.Equal(expectedPlanCount, completedProgress.Completed);
        Assert.Equal(0, completedProgress.InFlight);

        ImmutableArray<RunCriterionOverride> overrides =
        [
            new RunCriterionOverride
            {
                SourceRowNumber = 2,
                QuestionId = "Q-KNOW",
                EvaluatorId = "E-KNOW",
                CriterionId = "C-KNOW-INHERITED",
                Value = "8.5",
            },
            new RunCriterionOverride
            {
                SourceRowNumber = 3,
                QuestionId = "Q-PROMPT",
                EvaluatorId = "E-CUSTOM",
                CriterionId = "C-CUSTOM",
                Value = "1.5",
            },
        ];
        RunOverrideValidationResult overrideValidation = summary.ValidateOverrides(overrides);
        Assert.True(overrideValidation.IsValid);
        RunOutputPreparation preparation = summary.PrepareOutput(overrides);
        Assert.True(preparation.IsExportReady);
        Assert.Equal(SyntheticWorkbookFactory.DataRowCount, preparation.Rows.Length);

        string outputPath = Path.Combine(workbook.RootDirectory, "synthetic-e01-output.xlsx");
        Assert.False(File.Exists(outputPath));
        AppOwnedSheetNames sheetNames;
        ConfigCellAddressMap configCells;
        ResultsSheetWriteResult resultsWrite;
        string temporaryPath;
        using (WorkingPackage package = WorkingPackage.Create(workbook.Path, outputPath))
        {
            temporaryPath = package.TemporaryPath;
            using (SpreadsheetDocument document = package.OpenForEditing())
            {
                sheetNames = new AppOwnedSheetNameResolver().Resolve(document);
                configCells = new ConfigSheetWriter().Write(
                    document,
                    preparation.Snapshot,
                    sheetNames);
                new ReferenceAnswersSheetWriter().Write(
                    document,
                    preparation.Snapshot,
                    sheetNames,
                    preparation.Snapshot.Definition.Questions
                        .Where(question => question.Enabled)
                        .Select(question => new ReferenceAnswerSheetRow
                        {
                            QuestionId = question.Id,
                            ModelId = "auto",
                            StatusCode = ResultsStatusCodes.AiRuntimeFailed,
                            GeneratedAtUtc = summary.EndedAtUtc,
                        }));
                resultsWrite = new ResultsSheetWriter().Write(
                    document,
                    preparation.Snapshot,
                    sheetNames,
                    configCells,
                    preparation.Rows);
                new RunSheetWriter().Write(
                    document,
                    CreateRunMetadata(summary, sheetNames));
                new CalculationPropertiesWriter().Write(document);
            }

            Assert.Equal(530, resultsWrite.DataRowCount);
            Assert.Equal(57, resultsWrite.ColumnCount);
            Assert.Equal(10_600, resultsWrite.FormulaCells.Length);
            ImmutableArray<ExpectedFormulaCell> expectedFormulas = configCells.FormulaCells
                .Concat(resultsWrite.FormulaCells)
                .Select(cell => new ExpectedFormulaCell(cell.Definition, cell.CachedValue))
                .ToImmutableArray();
            OutputPackageValidationPlan validationPlan = OutputPackageValidationPlan.Capture(
                workbook.Path,
                sheetNames,
                expectedFormulas);
            AtomicOutputCommitResult commit = new AtomicOutputCommitter().Commit(
                package,
                outputPath,
                workbook.Path,
                summary.InputSnapshot,
                validationPlan,
                cancellationToken);
            Assert.True(commit.IsSuccess);
            Assert.Equal(AtomicOutputStatusCodes.Success, commit.Code);
            Assert.Equal(Path.GetFullPath(outputPath), commit.FinalPath);
            Assert.Empty(commit.ValidationErrors);
            Assert.False(File.Exists(temporaryPath));
            Assert.True(File.Exists(outputPath));
        }

        Assert.True(File.Exists(outputPath));
        Assert.False(File.Exists(temporaryPath));
        Assert.True(snapshots.Recheck(workbook.Path, inputBefore).IsMatch);
        Assert.NotEqual(Path.GetFullPath(workbook.Path), Path.GetFullPath(outputPath));
        AssertOutputWorkbook(
            outputPath,
            originalWorksheetXml,
            summary,
            sheetNames,
            expectedPlanCount,
            expectedEmptyCount,
            cancellationToken);
    }

    [Fact]
    public async Task Fixed_seed_531_row_durable_new_and_resume_matches_uninterrupted_run()
    {
        using SyntheticWorkbook workbook = SyntheticWorkbookFactory.Create();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        InputSnapshotService snapshots = new();
        InputSnapshot inputBefore = snapshots.Capture(workbook.Path);
        string originalWorksheetXml = ReadWorksheetXml(
            workbook.Path,
            SyntheticWorkbookFactory.SourceSheetName);
        WorkbookMetadata metadata = new WorkbookMetadataReader().Read(workbook.Path);
        QuantificationDefinition definition = CreateDurableDefinition();
        Assert.True(new ColumnMappingValidator().Validate(metadata, definition).IsValid);

        string baselineDirectory = Path.Combine(workbook.RootDirectory, "durable-baseline");
        string resumeDirectory = Path.Combine(workbook.RootDirectory, "durable-resume");
        Directory.CreateDirectory(baselineDirectory);
        Directory.CreateDirectory(resumeDirectory);

        FakeCopilotTransport baselineNormal = new(workbook);
        DurableReferenceRunner baselineReferences = new();
        DurableSpecialRunner baselineSpecials = new(workbook);
        DurableCountingCheckpointStore baselineStore = new(new CheckpointStore());
        RunSummary baseline = await DurableOrchestrator(
            workbook,
            baselineNormal,
            baselineReferences,
            baselineSpecials,
            baselineStore).RunAsync(
                DurableRequest(definition, metadata, workbook.Path, baselineDirectory),
                cancellationToken: cancellationToken);

        int expectedEmptyCount = workbook.CountBlankRows("F") + workbook.CountBlankRows("J");
        int expectedNormalCalls = (SyntheticWorkbookFactory.DataRowCount * 2) - expectedEmptyCount;
        int expectedSpecialCalls = SyntheticWorkbookFactory.DataRowCount - workbook.CountBlankRows("J");
        AssertDurableSuccess(baseline, expectedEmptyCount, expectedResumed: false);
        Assert.Equal(expectedNormalCalls, baselineNormal.Calls.Length);
        Assert.Equal(2, baselineReferences.CallCount);
        Assert.Equal(expectedSpecialCalls, baselineSpecials.CallCount);
        Assert.Equal(1, baselineStore.CreateCount);
        Assert.Equal(532, baselineStore.UpdateCount);
        Assert.Equal(0, baselineStore.LoadCount);

        using CancellationTokenSource interruption = new();
        FakeCopilotTransport interruptedNormal = new(workbook);
        DurableReferenceRunner interruptedReferences = new();
        DurableSpecialRunner interruptedSpecials = new(workbook);
        bool interruptionRequested = false;
        DurableCountingCheckpointStore interruptedStore = new(
            new CheckpointStore(),
            envelope =>
            {
                if (!interruptionRequested && envelope.CompletedRows.Length >= 265)
                {
                    interruptionRequested = true;
                    interruption.Cancel();
                }
            });
        RunSummary interrupted = await DurableOrchestrator(
            workbook,
            interruptedNormal,
            interruptedReferences,
            interruptedSpecials,
            interruptedStore).RunAsync(
                DurableRequest(definition, metadata, workbook.Path, resumeDirectory),
                cancellationToken: interruption.Token);

        Assert.Equal(QuantificationRunStatusCodes.Cancelled, interrupted.StatusCode);
        Assert.True(interrupted.IsPartial);
        Assert.False(interrupted.WasResumed);
        Assert.Equal(2, interrupted.References.Length);
        Assert.InRange(interrupted.CompletedRows.Length, 265, SyntheticWorkbookFactory.DataRowCount - 1);
        Assert.Null(interrupted.FinalPath);
        string partialPath = Assert.IsType<string>(interrupted.PartialPath);
        Assert.True(File.Exists(partialPath));
        Assert.Equal(2, interruptedReferences.CallCount);
        Assert.Equal(1, interruptedStore.CreateCount);
        Assert.Equal(2 + interrupted.CompletedRows.Length, interruptedStore.UpdateCount);
        Assert.Equal(0, interruptedStore.LoadCount);

        FakeCopilotTransport resumedNormal = new(workbook);
        DurableReferenceRunner resumedReferences = new(throwOnCall: true);
        DurableSpecialRunner resumedSpecials = new(workbook);
        DurableCountingCheckpointStore resumedStore = new(new CheckpointStore());
        RunSummary resumed = await DurableOrchestrator(
            workbook,
            resumedNormal,
            resumedReferences,
            resumedSpecials,
            resumedStore).RunAsync(
                DurableRequest(definition, metadata, workbook.Path, resumeDirectory) with
                {
                    ResumePartialPath = partialPath,
                },
                cancellationToken: cancellationToken);

        AssertDurableSuccess(resumed, expectedEmptyCount, expectedResumed: true);
        Assert.Equal(expectedNormalCalls - interruptedNormal.Calls.Length, resumedNormal.Calls.Length);
        Assert.Equal(0, resumedReferences.CallCount);
        Assert.Equal(expectedSpecialCalls - interruptedSpecials.CallCount, resumedSpecials.CallCount);
        Assert.Equal(0, resumedStore.CreateCount);
        Assert.Equal(SyntheticWorkbookFactory.DataRowCount - interrupted.CompletedRows.Length, resumedStore.UpdateCount);
        Assert.Equal(1, resumedStore.LoadCount);
        Assert.False(File.Exists(partialPath));

        Assert.Equal(DurableResultHash(baseline), DurableResultHash(resumed));
        string baselineFinal = Assert.IsType<string>(baseline.FinalPath);
        string resumedFinal = Assert.IsType<string>(resumed.FinalPath);
        Assert.Equal(ResultFormulaHash(baselineFinal), ResultFormulaHash(resumedFinal));
        AssertDurableOutput(baselineFinal, originalWorksheetXml, cancellationToken);
        AssertDurableOutput(resumedFinal, originalWorksheetXml, cancellationToken);
        Assert.True(snapshots.Recheck(workbook.Path, inputBefore).IsMatch);
    }

    private static QuantificationDefinition CreateDurableDefinition()
    {
        QuantificationDefinition source = CreateDefinition();
        SpecialEvaluationDefinition special = new()
        {
            Id = "S-PROMPT",
            DisplayName = "Synthetic prompt special",
            PrimarySourceColumn = "J",
            SupportingSourceColumns = ["K"],
            PromptTemplate = "{回答}\n{補助情報}",
        };
        return source with
        {
            Id = "DEF-E01-DURABLE-SEED-20260901",
            Name = "Synthetic E-01 durable fixed-seed definition",
            Revision = "durable-" + SyntheticWorkbookFactory.Seed.ToString(CultureInfo.InvariantCulture),
            BasePoints = 85m,
            SpecialPoints = 10m,
            Questions = source.Questions.Select(question =>
                question.Id == "Q-PROMPT"
                    ? question with { SpecialEvaluations = [special] }
                    : question).ToImmutableArray(),
        };
    }

    private static DurableQuantificationOrchestrator DurableOrchestrator(
        SyntheticWorkbook workbook,
        IEvaluationRunner normal,
        IReferenceAnswerOperationRunner references,
        ISpecialEvaluationOperationRunner specials,
        ICheckpointStore checkpoints) =>
        new(
            new SyntheticEvaluationRowSource(workbook),
            normal,
            references,
            specials,
            new PhysicalInputSnapshotBoundary(),
            checkpoints,
            new OutputPathPlanner(),
            new WorkbookDurableRunFinalizer(),
            new PhysicalPartialCheckpointCleaner());

    private static DurableQuantificationRunRequest DurableRequest(
        QuantificationDefinition definition,
        WorkbookMetadata metadata,
        string inputPath,
        string outputDirectory) =>
        new()
        {
            Run = new QuantificationRunRequest
            {
                DraftDefinition = definition,
                WorkbookMetadata = metadata,
                InputPath = inputPath,
                ModelId = ModelId,
                MaximumPromptTokens = 64_000,
                MaximumContextWindowTokens = 128_000,
                MaxConcurrency = 3,
            },
            Runtime = new CheckpointRuntimeIdentity
            {
                ApplicationIdentity = "StudyReportEvaluator.App/E-01-durable-test",
                CliVersion = "1.0.82-test-no-cli",
                CliSha256 = new string('A', 64),
                SdkInformationalVersion = "1.0.11",
            },
            OutputDirectory = outputDirectory,
        };

    private static void AssertDurableSuccess(
        RunSummary summary,
        int expectedEmptyCount,
        bool expectedResumed)
    {
        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        Assert.True(summary.IsDurable);
        Assert.Equal(expectedResumed, summary.WasResumed);
        Assert.False(summary.IsPartial);
        Assert.False(summary.PartialCleanupFailed);
        Assert.Equal(AtomicOutputStatusCodes.Success, summary.FinalizationCode);
        Assert.Equal(1_060, summary.PlannedEvaluationCount);
        Assert.Equal(1_060, summary.CompletedEvaluationCount);
        Assert.Equal(1_060 - expectedEmptyCount, summary.SucceededCount);
        Assert.Equal(expectedEmptyCount, summary.EmptyCount);
        Assert.Equal(0, summary.FailureCount);
        Assert.Equal(0, summary.CancelledCount);
        Assert.Equal(2, summary.References.Length);
        Assert.Equal(530, summary.CompletedRows.Length);
        Assert.Equal(summary.PlannedOperationCount, summary.CompletedOperationCount);
        Assert.Equal(0, summary.OperationFailureCount);
        Assert.Equal(0, summary.OperationCancelledCount);
        Assert.True(File.Exists(summary.FinalPath));
        Assert.False(File.Exists(summary.PartialPath));
    }

    private static string DurableResultHash(RunSummary summary)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            References = summary.References.Select(reference => new
            {
                reference.QuestionId,
                reference.Answer,
                reference.StatusCode,
                reference.AttemptCount,
                reference.TokenUsage,
            }),
            Rows = summary.CompletedRows,
        });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string ResultFormulaHash(string path)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, false);
        Worksheet results = GetWorksheet(document, AppOwnedSheetNameResolver.ResultsBaseName);
        string[] signature = results.Descendants<Cell>()
            .Where(cell => cell.CellFormula is not null)
            .Select(cell => string.Join(
                '|',
                cell.CellReference?.Value,
                cell.CellFormula?.Text,
                cell.DataType?.Value.ToString() ?? "<blank>",
                cell.CellValue?.Text ?? "<blank>"))
            .ToArray();
        Assert.NotEmpty(signature);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(signature)));
    }

    private static void AssertDurableOutput(
        string path,
        string originalWorksheetXml,
        CancellationToken cancellationToken)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, false);
        Assert.Empty(new OpenXmlValidator().Validate(document, cancellationToken));
        Workbook workbook = document.WorkbookPart?.Workbook
            ?? throw new InvalidDataException("The durable output workbook root is missing.");
        Sheet[] sheets = workbook.Descendants<Sheet>().ToArray();
        Assert.Equal(5, sheets.Length);
        Assert.Equal(SyntheticWorkbookFactory.SourceSheetName, sheets[0].Name?.Value);
        Assert.Equal(originalWorksheetXml, GetWorksheet(document, SyntheticWorkbookFactory.SourceSheetName).OuterXml);
        Assert.Contains(sheets, sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.ConfigBaseName);
        Assert.Contains(sheets, sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.ReferencesBaseName);
        Assert.Contains(sheets, sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.ResultsBaseName);
        Assert.Contains(sheets, sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.RunBaseName);
        Assert.DoesNotContain(sheets, sheet => string.Equals(
            sheet.Name?.Value,
            CheckpointStore.CheckpointSheetName,
            StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, GetWorksheet(document, AppOwnedSheetNameResolver.ConfigBaseName)
            .Descendants<CellFormula>().Count());
        Assert.Empty(GetWorksheet(document, AppOwnedSheetNameResolver.ReferencesBaseName)
            .Descendants<CellFormula>());
        Worksheet results = GetWorksheet(document, AppOwnedSheetNameResolver.ResultsBaseName);
        Assert.Equal(530, results.Descendants<Row>().Count(row => row.RowIndex?.Value >= 2));
        Assert.NotEmpty(results.Descendants<CellFormula>());
        Assert.Empty(GetWorksheet(document, AppOwnedSheetNameResolver.RunBaseName)
            .Descendants<CellFormula>());
    }

    private static QuantificationDefinition CreateDefinition() =>
        new()
        {
            Id = "DEF-E01-SEED-20260901",
            Name = "Synthetic E-01 fixed-seed definition",
            Revision = SyntheticWorkbookFactory.Seed.ToString(CultureInfo.InvariantCulture),
            SourceSheet = SyntheticWorkbookFactory.SourceSheetName,
            HeaderRow = SyntheticWorkbookFactory.HeaderRow,
            FirstDataRow = SyntheticWorkbookFactory.FirstDataRow,
            LastDataRow = SyntheticWorkbookFactory.LastDataRow,
            BasePoints = 95m,
            RoundingDigits = 2,
            Questions =
            [
                new QuestionDefinition
                {
                    Id = "Q-KNOW",
                    DisplayName = "Synthetic knowledge coverage",
                    QuestionText = "=SYNTHETIC KNOWLEDGE QUESTION",
                    PrimarySourceColumn = "F",
                    SupportingSourceColumns = ["H"],
                    Points = 3m,
                    Evaluators =
                    [
                        new EvaluatorDefinition
                        {
                            Id = "E-KNOW",
                            DisplayName = "Synthetic knowledge evaluator",
                            Type = EvaluatorType.KnowledgeCoverage,
                            Weight = 5m,
                            Range = new ScoreRange(0m, 10m),
                            BuiltInTemplateVersion = BuiltInPromptTemplates.KnowledgeTemplateVersion,
                            Criteria =
                            [
                                new CriterionDefinition
                                {
                                    Id = "C-KNOW-INHERITED",
                                    DisplayName = "Inherited knowledge range",
                                    Description = "@SYNTHETIC inherited knowledge point",
                                    Weight = 2m,
                                },
                                new CriterionDefinition
                                {
                                    Id = "C-KNOW-EXPLICIT",
                                    DisplayName = "Explicit knowledge range",
                                    Description = "-SYNTHETIC explicit knowledge point",
                                    Weight = 1m,
                                    Range = new ScoreRange(1m, 5m),
                                },
                                new CriterionDefinition
                                {
                                    Id = "C-KNOW-DISABLED",
                                    DisplayName = "Disabled knowledge criterion",
                                    Description = "SYNTHETIC disabled criterion",
                                    Weight = 13m,
                                    Range = new ScoreRange(0m, 100m),
                                    Enabled = false,
                                },
                            ],
                        },
                        new EvaluatorDefinition
                        {
                            Id = "E-KNOW-DISABLED",
                            DisplayName = "Disabled evaluator",
                            Type = EvaluatorType.KnowledgeCoverage,
                            Weight = 17m,
                            Range = new ScoreRange(0m, 1m),
                            BuiltInTemplateVersion = BuiltInPromptTemplates.KnowledgeTemplateVersion,
                            Enabled = false,
                            Criteria =
                            [
                                new CriterionDefinition
                                {
                                    Id = "C-IN-DISABLED-EVALUATOR",
                                    DisplayName = "Criterion in disabled evaluator",
                                    Description = "SYNTHETIC disabled evaluator criterion",
                                    Weight = 19m,
                                    Enabled = false,
                                },
                            ],
                        },
                    ],
                },
                new QuestionDefinition
                {
                    Id = "Q-PROMPT",
                    DisplayName = "Synthetic prompt quality",
                    QuestionText = "+SYNTHETIC PROMPT QUESTION",
                    PrimarySourceColumn = "J",
                    SupportingSourceColumns = ["K"],
                    Points = 2m,
                    Evaluators =
                    [
                        new EvaluatorDefinition
                        {
                            Id = "E-CUSTOM",
                            DisplayName = "Synthetic custom evaluator",
                            Type = EvaluatorType.CustomPrompt,
                            Weight = 7m,
                            Range = new ScoreRange(-2m, 2m),
                            CustomPromptTemplate = "{設問}\n{回答}\n{補助情報}\n{評価項目}\n{最小点}..{最大点}",
                            Criteria =
                            [
                                new CriterionDefinition
                                {
                                    Id = "C-CUSTOM",
                                    DisplayName = "Custom prompt criterion",
                                    Description = "+SYNTHETIC custom criterion",
                                    Weight = 4m,
                                },
                                new CriterionDefinition
                                {
                                    Id = "C-CUSTOM-DISABLED",
                                    DisplayName = "Disabled custom criterion",
                                    Description = "SYNTHETIC disabled custom criterion",
                                    Weight = 23m,
                                    Range = new ScoreRange(-10m, 10m),
                                    Enabled = false,
                                },
                            ],
                        },
                    ],
                },
                new QuestionDefinition
                {
                    Id = "Q-DISABLED",
                    DisplayName = "Disabled question",
                    QuestionText = "SYNTHETIC disabled question",
                    PrimarySourceColumn = "I",
                    SupportingSourceColumns = [],
                    Points = 29m,
                    Enabled = false,
                    Evaluators =
                    [
                        new EvaluatorDefinition
                        {
                            Id = "E-IN-DISABLED-QUESTION",
                            DisplayName = "Evaluator in disabled question",
                            Type = EvaluatorType.KnowledgeCoverage,
                            Weight = 31m,
                            Range = new ScoreRange(0m, 3m),
                            BuiltInTemplateVersion = BuiltInPromptTemplates.KnowledgeTemplateVersion,
                            Criteria =
                            [
                                new CriterionDefinition
                                {
                                    Id = "C-IN-DISABLED-QUESTION",
                                    DisplayName = "Criterion in disabled question",
                                    Description = "SYNTHETIC disabled question criterion",
                                    Weight = 37m,
                                },
                            ],
                        },
                    ],
                },
            ],
        };

    private static RunSheetMetadata CreateRunMetadata(
        RunSummary summary,
        AppOwnedSheetNames sheetNames) =>
        new()
        {
            InputIdentity = summary.InputSnapshot,
            DefinitionSha256 = summary.DefinitionSha256,
            ApplicationIdentity = "StudyReportEvaluator.App/E-01-test",
            CopilotSdkIdentity = "GitHub.Copilot.SDK/fake-no-network",
            CopilotCliIdentity = "copilot/not-started;sha256=" + new string('0', 64),
            ModelIdentity = ModelId,
            StartedAtUtc = summary.StartedAtUtc,
            EndedAtUtc = summary.EndedAtUtc,
            PlannedEvaluationCount = summary.PlannedEvaluationCount,
            CompletedEvaluationCount = summary.CompletedEvaluationCount,
            ErrorCount = summary.FailureCount,
            SheetNames = sheetNames,
        };

    private static void AssertOutputWorkbook(
        string outputPath,
        string originalWorksheetXml,
        RunSummary summary,
        AppOwnedSheetNames sheetNames,
        int expectedPlanCount,
        int expectedEmptyCount,
        CancellationToken cancellationToken)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(outputPath, false);
        Assert.Empty(new OpenXmlValidator().Validate(document, cancellationToken));
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The output workbook part is missing.");
        Sheet[] sheets = (workbookPart.Workbook
                ?? throw new InvalidDataException("The output workbook root is missing."))
            .Descendants<Sheet>()
            .ToArray();
        Assert.Equal(
            [SyntheticWorkbookFactory.SourceSheetName, .. sheetNames.AllSheetNames],
            sheets.Select(sheet => sheet.Name?.Value));
        Assert.Equal(
            originalWorksheetXml,
            GetWorksheet(document, SyntheticWorkbookFactory.SourceSheetName).OuterXml);

        Worksheet config = GetWorksheet(document, sheetNames.ConfigSheetName);
        Assert.Equal(2, config.Descendants<CellFormula>().Count());
        Row[] configRows = config.Descendants<Row>().ToArray();
        string reconstructedSnapshot = string.Concat(
            configRows
                .Where(row => Text(row, "A") == "CANONICAL_JSON")
                .OrderBy(row => int.Parse(Text(row, "X"), CultureInfo.InvariantCulture))
                .Select(row => Text(row, "Y")));
        Assert.Equal(summary.Snapshot.CanonicalJson, reconstructedSnapshot);
        Assert.Equal(summary.DefinitionSha256, Text(
            FindConfigRecord(configRows, "DEFINITION", summary.Snapshot.Definition.Id),
            "W"));
        Assert.Equal("0", Text(FindConfigRecord(configRows, "CRITERION", "C-KNOW-DISABLED"), "Q"));
        Assert.Equal("0", Text(FindConfigRecord(configRows, "EVALUATOR", "E-KNOW-DISABLED"), "Q"));
        Assert.Equal("0", Text(FindConfigRecord(configRows, "QUESTION", "Q-DISABLED"), "Q"));
        Assert.All(
            config.Descendants<Cell>().Where(IsTextualCell),
            cell =>
            {
                Assert.Equal(CellValues.InlineString, cell.DataType?.Value);
                Assert.Null(cell.CellFormula);
            });

        Worksheet results = GetWorksheet(document, sheetNames.ResultsSheetName);
        Dictionary<string, string> headers = ReadHeaders(results);
        Assert.Equal(57, headers.Count);
        Assert.Equal(531, results.Descendants<Row>().Count());
        Assert.Equal(10_600, results.Descendants<CellFormula>().Count());
        Assert.DoesNotContain(headers.Keys, header =>
            header.Contains("DISABLED", StringComparison.Ordinal));

        AssertNumericResult(results, headers, "Q-KNOW.E-KNOW.C-KNOW-INHERITED.Override", 2, 8.5m, formula: false);
        AssertNumericResult(results, headers, "Q-KNOW.E-KNOW.C-KNOW-INHERITED.Effective_Raw", 2, 8.5m, formula: true);
        AssertNumericResult(results, headers, "Q-KNOW.E-KNOW.C-KNOW-INHERITED.Normalized", 2, 85m, formula: true);
        AssertNumericResult(results, headers, "Q-KNOW.E-KNOW.Evaluator_Score", 2, 73.33m, formula: true);
        AssertNumericResult(results, headers, "Q-KNOW.Question_Normalized", 2, 73.33m, formula: true);
        AssertNumericResult(results, headers, "Q-PROMPT.E-CUSTOM.C-CUSTOM.Override", 3, 1.5m, formula: false);
        AssertNumericResult(results, headers, "Q-PROMPT.E-CUSTOM.C-CUSTOM.Effective_Raw", 3, 1.5m, formula: true);
        AssertNumericResult(results, headers, "Q-PROMPT.E-CUSTOM.C-CUSTOM.Normalized", 3, 87.5m, formula: true);
        AssertBlankFormula(CellByHeader(results, headers, ResultsSheetWriter.FinalScoreHeader, 2));
        AssertBlankFormula(CellByHeader(results, headers, ResultsSheetWriter.FinalScoreHeader, 3));

        for (int sourceRow = 2; sourceRow <= 5; sourceRow++)
        {
            char marker = SyntheticWorkbookFactory.FormulaMarkerForRow(sourceRow);
            Cell evidence = CellByHeader(
                results,
                headers,
                "Q-KNOW.E-KNOW.C-KNOW-INHERITED.Evidence",
                sourceRow);
            Cell reason = CellByHeader(
                results,
                headers,
                "Q-KNOW.E-KNOW.C-KNOW-INHERITED.Reason",
                sourceRow);
            Assert.Equal(
                string.Concat(marker, SyntheticWorkbookFactory.RowMarker(sourceRow)),
                evidence.InnerText);
            Assert.StartsWith(marker.ToString(), reason.InnerText, StringComparison.Ordinal);
            Assert.Equal(CellValues.InlineString, evidence.DataType?.Value);
            Assert.Equal(CellValues.InlineString, reason.DataType?.Value);
            Assert.Null(evidence.CellFormula);
            Assert.Null(reason.CellFormula);
        }

        const int blankRow = 17;
        Assert.Equal(
            "0",
            CellByHeader(
                results,
                headers,
                "Q-KNOW.E-KNOW.C-KNOW-INHERITED.Scorable",
                blankRow).InnerText);
        Assert.Equal(
            ResultsStatusCodes.Empty,
            CellByHeader(
                results,
                headers,
                "Q-KNOW.E-KNOW.C-KNOW-INHERITED.Status",
                blankRow).InnerText);
        AssertBlankLiteral(CellByHeader(
            results,
            headers,
            "Q-KNOW.E-KNOW.C-KNOW-INHERITED.AI_Raw",
            blankRow));
        AssertBlankFormula(CellByHeader(
            results,
            headers,
            "Q-KNOW.E-KNOW.C-KNOW-INHERITED.Effective_Raw",
            blankRow));
        AssertBlankFormula(CellByHeader(
            results,
            headers,
            "Q-KNOW.Question_Normalized",
            blankRow));
        AssertBlankFormula(CellByHeader(
            results,
            headers,
            "Q-PROMPT.Question_Normalized",
            blankRow));
        AssertNumericResult(
            results,
            headers,
            ResultsSheetWriter.FinalScoreHeader,
            blankRow,
            95m,
            formula: true);
        Assert.All(results.Descendants<Cell>().Where(cell => cell.CellFormula is not null), cell =>
        {
            Assert.False(cell.CellFormula!.Text?.StartsWith('=') == true);
            Assert.DoesNotContain("[", cell.CellFormula.Text ?? string.Empty, StringComparison.Ordinal);
        });

        Worksheet run = GetWorksheet(document, sheetNames.RunSheetName);
        Assert.Empty(run.Descendants<CellFormula>());
        Dictionary<string, Cell> runRecords = run.Descendants<Row>()
            .Skip(1)
            .ToDictionary(row => Text(row, "A"), row => CellInColumn(row, "B"), StringComparer.Ordinal);
        Assert.Equal(summary.InputSnapshot.Sha256, runRecords["InputSha256"].InnerText);
        Assert.Equal(summary.DefinitionSha256, runRecords["DefinitionSha256"].InnerText);
        Assert.Equal(ModelId, runRecords["ModelIdentity"].InnerText);
        Assert.Equal(expectedPlanCount.ToString(CultureInfo.InvariantCulture), runRecords["PlannedEvaluationCount"].InnerText);
        Assert.Equal(expectedPlanCount.ToString(CultureInfo.InvariantCulture), runRecords["CompletedEvaluationCount"].InnerText);
        Assert.Equal("0", runRecords["ErrorCount"].InnerText);
        Assert.Equal(sheetNames.ConfigSheetName, runRecords["ConfigSheetName"].InnerText);
        Assert.Equal(sheetNames.ReferencesSheetName, runRecords["ReferencesSheetName"].InnerText);
        Assert.Equal(sheetNames.ResultsSheetName, runRecords["ResultsSheetName"].InnerText);
        Assert.Equal(sheetNames.RunSheetName, runRecords["RunSheetName"].InnerText);
        Assert.StartsWith("DocumentFormat.OpenXml/3.5.1", runRecords["OpenXmlSdkIdentity"].InnerText, StringComparison.Ordinal);
        DateTimeOffset started = DateTimeOffset.Parse(
            runRecords["StartedAtUtc"].InnerText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        DateTimeOffset ended = DateTimeOffset.Parse(
            runRecords["EndedAtUtc"].InnerText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        Assert.Equal(TimeSpan.Zero, started.Offset);
        Assert.Equal(TimeSpan.Zero, ended.Offset);
        Assert.True(ended >= started);
        Assert.Equal(expectedEmptyCount, summary.EmptyCount);

        CalculationProperties properties = Assert.IsType<CalculationProperties>(
            workbookPart.Workbook.CalculationProperties);
        Assert.Equal(CalculateModeValues.Auto, properties.CalculationMode?.Value);
        Assert.True(properties.FullCalculationOnLoad?.Value == true);
        Assert.True(properties.ForceFullCalculation?.Value == true);
    }

    private static void AssertNumericResult(
        Worksheet results,
        IReadOnlyDictionary<string, string> headers,
        string header,
        int row,
        decimal expected,
        bool formula)
    {
        Cell cell = CellByHeader(results, headers, header, row);
        Assert.Equal(formula, cell.CellFormula is not null);
        Assert.Equal(CellValues.Number, cell.DataType?.Value);
        Assert.True(decimal.TryParse(
            cell.CellValue?.Text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out decimal actual));
        Assert.Equal(expected, actual);
    }

    private static void AssertBlankLiteral(Cell cell)
    {
        Assert.Null(cell.CellFormula);
        Assert.Null(cell.CellValue);
        Assert.Null(cell.DataType);
    }

    private static void AssertBlankFormula(Cell cell)
    {
        Assert.NotNull(cell.CellFormula);
        Assert.Null(cell.CellValue);
        Assert.Null(cell.DataType);
    }

    private static Row FindConfigRecord(
        IEnumerable<Row> rows,
        string recordType,
        string nodeId) =>
        rows.Single(row =>
            Text(row, "A") == recordType
            && Text(row, "C") == nodeId);

    private static Dictionary<string, string> ReadHeaders(Worksheet worksheet)
    {
        Row header = worksheet.Descendants<Row>()
            .Single(row => row.RowIndex?.Value == SyntheticWorkbookFactory.HeaderRow);
        return header.Elements<Cell>().ToDictionary(
            cell => cell.InnerText,
            cell => Column(cell.CellReference?.Value),
            StringComparer.Ordinal);
    }

    private static Cell CellByHeader(
        Worksheet worksheet,
        IReadOnlyDictionary<string, string> headers,
        string header,
        int row)
    {
        string reference = headers[header] + row.ToString(CultureInfo.InvariantCulture);
        return worksheet.Descendants<Cell>().Single(cell => string.Equals(
            cell.CellReference?.Value,
            reference,
            StringComparison.Ordinal));
    }

    private static string ReadWorksheetXml(string path, string sheetName)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, false);
        return GetWorksheet(document, sheetName).OuterXml;
    }

    private static Worksheet GetWorksheet(SpreadsheetDocument document, string sheetName)
    {
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The workbook part is missing.");
        Sheet sheet = (workbookPart.Workbook
                ?? throw new InvalidDataException("The workbook root is missing."))
            .Descendants<Sheet>()
            .Single(candidate => string.Equals(
                candidate.Name?.Value,
                sheetName,
                StringComparison.Ordinal));
        string relationshipId = sheet.Id?.Value
            ?? throw new InvalidDataException("A worksheet relationship is missing.");
        return (workbookPart.GetPartById(relationshipId) as WorksheetPart)?.Worksheet
            ?? throw new InvalidDataException("A worksheet part is missing.");
    }

    private static bool IsTextualCell(Cell cell)
    {
        CellValues? dataType = cell.DataType?.Value;
        return dataType == CellValues.InlineString
            || dataType == CellValues.String
            || dataType == CellValues.SharedString;
    }

    private static string Text(Row row, string column)
    {
        Cell? cell = row.Elements<Cell>().FirstOrDefault(candidate => string.Equals(
            Column(candidate.CellReference?.Value),
            column,
            StringComparison.Ordinal));
        return cell?.InnerText ?? string.Empty;
    }

    private static Cell CellInColumn(Row row, string column) =>
        row.Elements<Cell>().Single(candidate => string.Equals(
            Column(candidate.CellReference?.Value),
            column,
            StringComparison.Ordinal));

    private static string Column(string? reference) =>
        new((reference ?? string.Empty).TakeWhile(char.IsAsciiLetter).ToArray());

    private sealed class ImmutableArraySequenceComparer : IEqualityComparer<ImmutableArray<string>>
    {
        internal static ImmutableArraySequenceComparer Instance { get; } = new();

        public bool Equals(ImmutableArray<string> left, ImmutableArray<string> right) =>
            left.SequenceEqual(right, StringComparer.Ordinal);

        public int GetHashCode(ImmutableArray<string> value)
        {
            HashCode hash = new();
            foreach (string item in value)
            {
                hash.Add(item, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }

    private sealed class DurableReferenceRunner(bool throwOnCall = false)
        : IReferenceAnswerOperationRunner
    {
        private int calls;

        internal int CallCount => Volatile.Read(ref calls);

        public Task<AuxiliaryOperationResult<ReferenceAnswerResult>> EvaluateAsync(
            SafeReferenceAnswerPayload payload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref calls);
            if (throwOnCall)
            {
                throw new InvalidOperationException("Saved synthetic references must be reused.");
            }

            return Task.FromResult(AuxiliaryOperationResult<ReferenceAnswerResult>.Succeeded(
                new ReferenceAnswerResult
                {
                    QuestionId = payload.QuestionId,
                    Answer = "SYNTHETIC-DURABLE-REFERENCE-" + payload.QuestionId,
                }));
        }
    }

    private sealed class DurableSpecialRunner(SyntheticWorkbook workbook)
        : ISpecialEvaluationOperationRunner
    {
        private int calls;

        internal int CallCount => Volatile.Read(ref calls);

        public Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(
            SafeSpecialEvaluationPayload payload,
            string modelId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(ModelId, modelId);
            int sourceRow = workbook.FindUniqueSourceRow(
                payload.PrimarySource.SourceColumnId,
                payload.PrimarySource.Value);
            Assert.All(payload.Sources, source => Assert.Equal(
                workbook.GetCell(sourceRow, source.SourceColumnId),
                source.Value));
            Interlocked.Increment(ref calls);
            return Task.FromResult(AuxiliaryOperationResult<SpecialQuantificationResult>.Succeeded(
                new SpecialQuantificationResult
                {
                    SpecialEvaluationId = payload.SpecialEvaluationId,
                    Score = (sourceRow % 10) / 10m,
                    Reason = "SYNTHETIC-DURABLE-SPECIAL",
                    Evidence = string.Empty,
                    EvidenceSource = EvidenceSourceKind.None,
                    EvidenceSourceColumnId = string.Empty,
                }));
        }
    }
    private sealed class DurableCountingCheckpointStore(
        ICheckpointStore inner,
        Action<CheckpointEnvelope>? afterSuccessfulUpdate = null) : ICheckpointStore
    {
        private int creates;
        private int updates;
        private int loads;

        internal int CreateCount => Volatile.Read(ref creates);

        internal int UpdateCount => Volatile.Read(ref updates);

        internal int LoadCount => Volatile.Read(ref loads);

        public CheckpointSaveResult Create(
            CheckpointEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            CheckpointSaveResult result = inner.Create(envelope, cancellationToken);
            if (result.IsSuccess)
            {
                Interlocked.Increment(ref creates);
            }

            return result;
        }

        public CheckpointSaveResult Update(
            CheckpointEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            CheckpointSaveResult result = inner.Update(envelope, cancellationToken);
            if (result.IsSuccess)
            {
                Interlocked.Increment(ref updates);
                afterSuccessfulUpdate?.Invoke(envelope);
            }

            return result;
        }

        public CheckpointLoadResult Load(
            string partialPath,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref loads);
            return inner.Load(partialPath, cancellationToken);
        }
    }
}


