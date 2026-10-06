using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.E2E;

public sealed class TenPersonSystemSmokeTests
{
    private const string FixtureRelativePath =
        "tests/fixtures/system-test/SystemTest-10Students.xlsx";
    private const string FixtureSha256 =
        "7F473879B3C43319D842881C5DFD1D01B2A60091A5B0C7B39B70B8D574D801C8";
    private const string ModelId = "local-deterministic-10-person-no-network";

    [Fact]
    public async Task Fixture_has_fixed_identity_structure_and_literal_formula_canaries()
    {
        string path = FixturePath();
        InputSnapshotService snapshots = new();
        InputSnapshot snapshot = snapshots.Capture(path);

        Assert.Equal(7_652, snapshot.SizeBytes);
        Assert.Equal(FixtureSha256, snapshot.Sha256);
        FileFormatClassificationResult classification = new FileFormatClassifier().Classify(path);
        Assert.True(classification.IsAccepted);
        Assert.Equal(FileFormatClassification.StandardXlsx, classification.Classification);

        InputViewModel input = new();
        await input.SetFilePathAsync(path, TestContext.Current.CancellationToken);
        Assert.True(input.HasLoadedWorkbook);
        Assert.True(input.CanContinue);
        Assert.Equal([1, 2], input.HeaderRowOptions);
        Assert.Equal(1, input.HeaderRow);
        Assert.Equal(2, input.FirstDataRow);
        Assert.Equal(11, input.LastDataRow);
        QuantificationDefinition definition = input.CreateDesignDefinition();
        Assert.Equal("SystemTestInput", definition.SourceSheet);
        Assert.Equal(1, definition.HeaderRow);
        Assert.Equal(2, definition.FirstDataRow);
        Assert.Equal(11, definition.LastDataRow);
        Assert.Equal(60m, definition.BasePoints);
        Assert.Equal(0m, definition.SpecialPoints);
        Assert.Equal(0.1m, definition.SimilarityPenaltyWeight);
        Assert.Equal(1, definition.RoundingDigits);
        Assert.Equal(5, definition.Questions.Length);
        Assert.Equal(
            ["F", "G", "H", "I", "J"],
            definition.Questions.Select(question => question.PrimarySourceColumn));
        Assert.Equal(["K"], definition.Questions[4].SupportingSourceColumns);
        Assert.Empty(definition.Questions.SelectMany(question => question.SpecialEvaluations));

        WorkbookMetadata metadata = new WorkbookMetadataReader().Read(path);
        WorksheetMetadata worksheetMetadata = Assert.Single(metadata.Worksheets);
        Assert.Equal("SystemTestInput", worksheetMetadata.Name);
        Assert.Equal("A1:L11", worksheetMetadata.DimensionReference);
        Assert.Equal(11U, worksheetMetadata.RowCount);
        Assert.Equal(12U, worksheetMetadata.ColumnCount);

        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, false);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The fixture workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("The fixture workbook root is missing.");
        ValidationErrorInfo[] validationErrors = new OpenXmlValidator()
            .Validate(document, TestContext.Current.CancellationToken)
            .ToArray();
        Assert.True(
            validationErrors.Length == 0,
            string.Join(
                ';',
                validationErrors.Select(error => string.Join(
                    '|',
                    error.Id ?? "UNKNOWN",
                    error.Part?.Uri.ToString() ?? "<none>",
                    error.Node?.LocalName ?? "<none>"))));
        Sheet sheet = Assert.Single(workbook.Descendants<Sheet>());
        Worksheet worksheet = ((WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!)).Worksheet
            ?? throw new InvalidDataException("The fixture worksheet is missing.");
        SharedStringTable? sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        Cell[] cells = worksheet.Descendants<Cell>().ToArray();

        Assert.DoesNotContain(cells, cell => cell.CellFormula is not null);
        Assert.Empty(workbook.DefinedNames?.Elements<DefinedName>() ?? []);
        ExternalWorkbookPart[] externalWorkbookParts = workbookPart.ExternalWorkbookParts.ToArray();
        // DdeLink is only valid beneath an ExternalWorkbookPart root.
        Assert.Empty(externalWorkbookParts);
        Cell[] canaries = cells
            .Where(cell => CellText(cell, sharedStrings).StartsWithAny('=', '+', '-', '@'))
            .ToArray();
        Assert.Equal(12, canaries.Length);
        Assert.All(canaries, cell =>
        {
            Assert.Equal(CellValues.InlineString, cell.DataType?.Value);
            Assert.Null(cell.CellFormula);
        });

        foreach (string column in new[] { "F", "G", "H", "I", "J" })
        {
            Assert.Single(
                Enumerable.Range(2, 10),
                row => string.IsNullOrWhiteSpace(CellText(
                    FindCell(worksheet, column, row),
                    sharedStrings)));
        }

        int jBlankRow = Assert.Single(
            Enumerable.Range(2, 10),
            row => string.IsNullOrWhiteSpace(CellText(
                FindCell(worksheet, "J", row),
                sharedStrings)));
        Assert.False(string.IsNullOrWhiteSpace(CellText(FindCell(worksheet, "K", jBlankRow), sharedStrings)));
        Assert.True(snapshots.Recheck(path, snapshot).IsMatch);
    }

    [Fact]
    public async Task Ten_person_uninterrupted_and_resumed_runs_have_identical_results_and_formula_cache()
    {
        string fixturePath = FixturePath();
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            "StudyReportEvaluator-TenPerson-" + Guid.NewGuid().ToString("N"));
        string baselineDirectory = Path.Combine(testRoot, "baseline");
        string resumeDirectory = Path.Combine(testRoot, "resume");
        Directory.CreateDirectory(baselineDirectory);
        Directory.CreateDirectory(resumeDirectory);
        InputSnapshotService snapshots = new();
        InputSnapshot inputBefore = snapshots.Capture(fixturePath);

        try
        {
            InputViewModel input = new();
            await input.SetFilePathAsync(fixturePath, TestContext.Current.CancellationToken);
            Assert.True(input.HasLoadedWorkbook);
            Assert.True(input.CanContinue);
            WorkbookMetadata metadata = Assert.IsType<WorkbookMetadata>(input.Metadata);
            QuantificationDefinition definition = input.CreateDesignDefinition();
            Assert.Equal(60m, definition.BasePoints);
            Assert.Equal(0m, definition.SpecialPoints);
            Assert.Equal(0.1m, definition.SimilarityPenaltyWeight);
            Assert.Equal(1, definition.RoundingDigits);
            Assert.Equal(5, definition.Questions.Length);
            Assert.Equal(["F", "G", "H", "I", "J"],
                definition.Questions.Select(question => question.PrimarySourceColumn));
            Assert.Equal([8m, 8m, 8m, 8m, 8m],
                definition.Questions.Select(question => question.Points));
            Assert.Equal(["K"], definition.Questions[4].SupportingSourceColumns);
            Assert.Empty(definition.Questions.SelectMany(question => question.SpecialEvaluations));

            DeterministicNormalRunner baselineNormal = new();
            DeterministicReferenceRunner baselineReferences = new();
            NoCallSpecialRunner baselineSpecials = new();
            CountingCheckpointStore baselineStore = new(new CheckpointStore());
            RunSummary baseline = await Orchestrator(
                fixturePath,
                baselineNormal,
                baselineReferences,
                baselineSpecials,
                baselineStore).RunAsync(
                    Request(definition, metadata, fixturePath, baselineDirectory),
                    cancellationToken: TestContext.Current.CancellationToken);

            AssertSuccessfulRun(baseline, expectedResumed: false);
            Assert.Equal(45, baselineNormal.CallCount);
            Assert.Equal(5, baselineReferences.CallCount);
            Assert.Equal(0, baselineSpecials.CallCount);
            Assert.Equal(1, baselineStore.CreateCount);
            Assert.Equal(15, baselineStore.UpdateCount);
            Assert.Equal(0, baselineStore.LoadCount);

            using CancellationTokenSource interruption = new();
            DeterministicNormalRunner firstNormal = new();
            DeterministicReferenceRunner firstReferences = new();
            NoCallSpecialRunner firstSpecials = new();
            CountingCheckpointStore firstStore = new(
                new CheckpointStore(),
                envelope =>
                {
                    if (envelope.CompletedRows.Length == 5)
                    {
                        interruption.Cancel();
                    }
                });
            RunSummary interrupted = await Orchestrator(
                fixturePath,
                firstNormal,
                firstReferences,
                firstSpecials,
                firstStore).RunAsync(
                    Request(definition, metadata, fixturePath, resumeDirectory),
                    cancellationToken: interruption.Token);

            Assert.Equal(QuantificationRunStatusCodes.Cancelled, interrupted.StatusCode);
            Assert.True(interrupted.IsPartial);
            Assert.False(interrupted.WasResumed);
            Assert.Null(interrupted.FinalPath);
            Assert.Equal(5, interrupted.References.Length);
            Assert.Equal(5, interrupted.CompletedRows.Length);
            string partialPath = Assert.IsType<string>(interrupted.PartialPath);
            Assert.True(File.Exists(partialPath));
            Assert.Equal(22, firstNormal.CallCount);
            Assert.Equal(5, firstReferences.CallCount);
            Assert.Equal(0, firstSpecials.CallCount);
            Assert.Equal(1, firstStore.CreateCount);
            Assert.Equal(10, firstStore.UpdateCount);

            DeterministicNormalRunner resumedNormal = new();
            DeterministicReferenceRunner resumedReferences = new(throwOnCall: true);
            NoCallSpecialRunner resumedSpecials = new();
            CountingCheckpointStore resumedStore = new(new CheckpointStore());
            RunSummary resumed = await Orchestrator(
                fixturePath,
                resumedNormal,
                resumedReferences,
                resumedSpecials,
                resumedStore).RunAsync(
                    Request(definition, metadata, fixturePath, resumeDirectory) with
                    {
                        ResumePartialPath = partialPath,
                    },
                    cancellationToken: TestContext.Current.CancellationToken);

            AssertSuccessfulRun(resumed, expectedResumed: true);
            Assert.Equal(23, resumedNormal.CallCount);
            Assert.Equal(0, resumedReferences.CallCount);
            Assert.Equal(0, resumedSpecials.CallCount);
            Assert.Equal(0, resumedStore.CreateCount);
            Assert.Equal(5, resumedStore.UpdateCount);
            Assert.Equal(1, resumedStore.LoadCount);
            Assert.False(File.Exists(partialPath));

            Assert.Equal(DurableResultSignature(baseline), DurableResultSignature(resumed));
            string baselineFinal = Assert.IsType<string>(baseline.FinalPath);
            string resumedFinal = Assert.IsType<string>(resumed.FinalPath);
            string[] baselineFormulas = ResultFormulaSignature(baselineFinal);
            string[] resumedFormulas = ResultFormulaSignature(resumedFinal);
            Assert.NotEmpty(baselineFormulas);
            Assert.Equal(baselineFormulas, resumedFormulas);
            AssertOutput(baselineFinal);
            AssertOutput(resumedFinal);
            Assert.True(snapshots.Recheck(fixturePath, inputBefore).IsMatch);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }

        Assert.False(Directory.Exists(testRoot));
        Assert.True(snapshots.Recheck(fixturePath, inputBefore).IsMatch);
    }

    private static DurableQuantificationOrchestrator Orchestrator(
        string inputPath,
        IEvaluationRunner normal,
        IReferenceAnswerOperationRunner references,
        ISpecialEvaluationOperationRunner specials,
        ICheckpointStore checkpoints) =>
        new(
            new OpenXmlEvaluationRowSource(inputPath),
            normal,
            references,
            specials,
            new PhysicalInputSnapshotBoundary(),
            checkpoints,
            new OutputPathPlanner(),
            new WorkbookDurableRunFinalizer(),
            new PhysicalPartialCheckpointCleaner());

    private static DurableQuantificationRunRequest Request(
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
                MaxConcurrency = 1,
            },
            Runtime = new CheckpointRuntimeIdentity
            {
                ApplicationIdentity = "StudyReportEvaluator.App/4.1.0",
                CliVersion = "1.0.82-test-no-cli",
                CliSha256 = new string('A', 64),
                SdkInformationalVersion = "1.0.11",
            },
            OutputDirectory = outputDirectory,
        };

    private static void AssertSuccessfulRun(RunSummary summary, bool expectedResumed)
    {
        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        Assert.True(summary.IsDurable);
        Assert.Equal(expectedResumed, summary.WasResumed);
        Assert.False(summary.IsPartial);
        Assert.False(summary.PartialCleanupFailed);
        Assert.Equal(AtomicOutputStatusCodes.Success, summary.FinalizationCode);
        Assert.Equal(50, summary.PlannedEvaluationCount);
        Assert.Equal(50, summary.CompletedEvaluationCount);
        Assert.Equal(45, summary.SucceededCount);
        Assert.Equal(5, summary.EmptyCount);
        Assert.Equal(0, summary.FailureCount);
        Assert.Equal(0, summary.CancelledCount);
        Assert.Equal(5, summary.References.Length);
        Assert.Equal(10, summary.CompletedRows.Length);
        Assert.True(File.Exists(summary.FinalPath));
        Assert.False(File.Exists(summary.PartialPath));
    }

    private static string DurableResultSignature(RunSummary summary)
    {
        List<string> values = [];
        foreach (CheckpointReference reference in summary.References.OrderBy(item => item.QuestionId, StringComparer.Ordinal))
        {
            values.Add($"R|{reference.QuestionId}|{reference.StatusCode}|{reference.Answer}");
        }

        foreach (CheckpointCompletedRow row in summary.CompletedRows.OrderBy(item => item.SourceRowNumber))
        {
            foreach (CheckpointNormalResult result in row.NormalResults
                         .OrderBy(item => item.QuestionId, StringComparer.Ordinal)
                         .ThenBy(item => item.EvaluatorId, StringComparer.Ordinal))
            {
                string raw = string.Join(
                    ',',
                    result.AcceptedResult?.Criteria
                        .OrderBy(item => item.CriterionId, StringComparer.Ordinal)
                        .Select(item => $"{item.CriterionId}:{item.RawScore.ToString(CultureInfo.InvariantCulture)}")
                    ?? []);
                values.Add($"N|{row.SourceRowNumber}|{result.QuestionId}|{result.EvaluatorId}|{result.StatusCode}|{result.Scorable}|{raw}");
            }

            foreach (CheckpointSpecialResult result in row.SpecialResults
                         .OrderBy(item => item.QuestionId, StringComparer.Ordinal)
                         .ThenBy(item => item.SpecialEvaluationId, StringComparer.Ordinal))
            {
                values.Add($"S|{row.SourceRowNumber}|{result.QuestionId}|{result.SpecialEvaluationId}|{result.StatusCode}|{result.AcceptedResult?.Score.ToString(CultureInfo.InvariantCulture)}");
            }

            foreach (CheckpointSimilarityResult result in row.SimilarityResults
                         .OrderBy(item => item.QuestionId, StringComparer.Ordinal))
            {
                values.Add($"L|{row.SourceRowNumber}|{result.QuestionId}|{result.StatusCode}|{result.AcceptedResult?.Similarity.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        return string.Join('\n', values);
    }

    private static string[] ResultFormulaSignature(string path)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, false);
        Worksheet results = Worksheet(document, AppOwnedSheetNameResolver.ResultsBaseName);
        return results.Descendants<Cell>()
            .Where(cell => cell.CellFormula is not null)
            .Select(cell => string.Join(
                '|',
                cell.CellReference?.Value,
                cell.CellFormula?.Text,
                cell.DataType?.Value.ToString() ?? "<blank>",
                cell.CellValue?.Text ?? "<blank>"))
            .ToArray();
    }

    private static void AssertOutput(string path)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, false);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        Workbook workbook = document.WorkbookPart?.Workbook
            ?? throw new InvalidDataException("The final workbook root is missing.");
        Assert.Equal(5, workbook.Descendants<Sheet>().Count());
        Assert.DoesNotContain(workbook.Descendants<Sheet>(), sheet => string.Equals(
            sheet.Name?.Value,
            CheckpointStore.CheckpointSheetName,
            StringComparison.OrdinalIgnoreCase));
        Worksheet results = Worksheet(document, AppOwnedSheetNameResolver.ResultsBaseName);
        Assert.Equal(10, results.Descendants<Row>().Count(row => row.RowIndex?.Value >= 2));
    }

    private static Worksheet Worksheet(SpreadsheetDocument document, string name)
    {
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("The workbook root is missing.");
        Sheet sheet = workbook.Descendants<Sheet>().Single(item => item.Name?.Value == name);
        return ((WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!)).Worksheet
            ?? throw new InvalidDataException("The worksheet is missing.");
    }

    private static Cell FindCell(Worksheet worksheet, string column, int row)
    {
        string reference = column + row.ToString(CultureInfo.InvariantCulture);
        return worksheet.Descendants<Cell>().Single(cell => string.Equals(
            cell.CellReference?.Value,
            reference,
            StringComparison.Ordinal));
    }

    private static string CellText(Cell cell, SharedStringTable? sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(cell.CellValue?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            return sharedStrings?.Elements<SharedStringItem>().ElementAtOrDefault(index)?.InnerText
                ?? string.Empty;
        }

        return cell.DataType?.Value == CellValues.InlineString
            ? cell.InlineString?.InnerText ?? string.Empty
            : cell.CellValue?.Text ?? string.Empty;
    }

    private static string FixturePath() =>
        Path.Combine(
            FindRepositoryRoot(),
            FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "StudyReportEvaluator.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }

    private sealed class DeterministicNormalRunner : IEvaluationRunner
    {
        private int calls;

        internal int CallCount => Volatile.Read(ref calls);

        public Task<EvaluationRunnerResult> EvaluateAsync(
            SafeEvaluationPayload payload,
            string modelId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(ModelId, modelId);
            Interlocked.Increment(ref calls);
            return Task.FromResult(EvaluationRunnerResult.Succeeded(
                new QuantificationResult
                {
                    EvaluatorId = payload.EvaluatorId,
                    Criteria = payload.ExpectedCriteria.Select(criterion =>
                        new CriterionQuantificationResult
                        {
                            CriterionId = criterion.CriterionId,
                            RawScore = criterion.Range.Minimum
                                + ((criterion.Range.Maximum - criterion.Range.Minimum) / 2m),
                            Reason = "SYNTHETIC_TECHNICAL_TEST",
                            Evidence = string.Empty,
                            EvidenceSource = EvidenceSourceKind.None,
                            EvidenceSourceColumnId = string.Empty,
                        }).ToImmutableArray(),
                }));
        }
    }

    private sealed class DeterministicReferenceRunner(bool throwOnCall = false)
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
                throw new InvalidOperationException("A saved synthetic reference must be reused.");
            }

            return Task.FromResult(AuxiliaryOperationResult<ReferenceAnswerResult>.Succeeded(
                new ReferenceAnswerResult
                {
                    QuestionId = payload.QuestionId,
                    Answer = "SYNTHETIC_REFERENCE_" + payload.QuestionId,
                }));
        }
    }

    private sealed class NoCallSpecialRunner : ISpecialEvaluationOperationRunner
    {
        private int calls;

        internal int CallCount => Volatile.Read(ref calls);

        public Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(
            SafeSpecialEvaluationPayload payload,
            string modelId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("A zero special budget must not dispatch a special operation.");
        }
    }
    private sealed class CountingCheckpointStore(
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

file static class TenPersonStringExtensions
{
    internal static bool StartsWithAny(this string value, params char[] candidates) =>
    value.Length > 0 && candidates.Contains(value[0]);
}


