using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Validation;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.E2E;

// Requirements: FR-006 (AC-007), FR-074 (AC-098)
public sealed class SampleWorkbookStructuralTests
{
    // FR-006 AC-007: the real sample is the reference, but it is a private local file that is never
    // committed (Q-006). Without it this check is skipped explicitly; CI runs the same structural
    // assertions on the committed synthetic sample below (FR-074).
    [Fact]
    public async Task Repository_sample_is_opened_read_only_and_only_structural_metadata_drives_mapping()
    {
        E02RepositoryLayout.RequireWindowsX64();
        string? samplePath = E02RepositoryLayout.TryGetLocalSamplePath();
        if (samplePath is null)
        {
            Assert.Skip("sample/SampleReport.xlsx はローカルにだけ置く正本で、この環境にはない（FR-006、Q-006）。構造は FR-074 の合成見本で検査する。");
        }

        await AssertApprovedSampleStructureAsync(samplePath);
    }

    // FR-074 AC-098: the committed synthetic sample has the same approved structure and mapping.
    [Fact]
    public async Task Committed_synthetic_sample_has_the_approved_structure_and_mapping()
    {
        E02RepositoryLayout.RequireWindowsX64();
        string path = SyntheticSampleWorkbook.CommittedPath;
        if (string.Equals(
                Environment.GetEnvironmentVariable(SyntheticSampleWorkbook.GenerateEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            SyntheticSampleWorkbook.Write(path);
        }

        Assert.True(File.Exists(path), "The committed synthetic sample workbook is missing.");
        await AssertApprovedSampleStructureAsync(path);
    }

    // FR-074 AC-098: the generator reproduces the committed structure, so the fixture can be rebuilt.
    [Fact]
    public async Task Synthetic_sample_generator_reproduces_the_approved_structure()
    {
        E02RepositoryLayout.RequireWindowsX64();
        string directory = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-SyntheticSample-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "SyntheticSampleReport.xlsx");
        try
        {
            SyntheticSampleWorkbook.Write(path);
            await AssertApprovedSampleStructureAsync(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // FR-074 AC-098: the private local sample directory is never tracked.
    [Fact]
    public void Private_local_sample_directory_is_ignored()
    {
        string[] ignoreLines = File.ReadAllLines(Path.Combine(E02RepositoryLayout.FindRepositoryRoot(), ".gitignore"));
        Assert.Contains("/sample/", ignoreLines.Select(line => line.Trim()));
    }

    private static async Task AssertApprovedSampleStructureAsync(string samplePath)
    {
        InputSnapshotService snapshots = new();
        InputSnapshot before = snapshots.Capture(samplePath);

        // The local sample may be re-saved. Validate structure, not a historical
        // byte identity; the before/after snapshot checks below remain mandatory.
        Assert.True(before.SizeBytes > 0);

        using FileStream readOnlyLease = new(
            samplePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        Assert.True(readOnlyLease.CanRead);
        Assert.True(readOnlyLease.CanSeek);
        Assert.False(readOnlyLease.CanWrite);
        Assert.Equal(before.SizeBytes, readOnlyLease.Length);

        FileFormatClassificationResult classification = new FileFormatClassifier().Classify(samplePath);
        Assert.True(classification.IsAccepted);
        Assert.Equal(FileFormatClassification.StandardXlsx, classification.Classification);
        // Tables and document metadata can change the part count on a valid re-save.
        // Verify the reported inventory against the actual read-only package instead.
        using (ZipArchive archive = ZipFile.OpenRead(samplePath))
        {
            Assert.Equal(archive.Entries.Count, classification.PackagePartCount);
            Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
            Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
            Assert.NotNull(archive.GetEntry("xl/_rels/workbook.xml.rels"));
        }
        Assert.InRange(classification.RelationshipCount, 1, FileFormatClassifier.MaxTotalRelationships);

        WorkbookMetadata metadata = new WorkbookMetadataReader().Read(samplePath);
        Assert.Equal(classification.PackagePartCount, metadata.PackagePartCount);
        Assert.Equal(classification.RelationshipCount, metadata.RelationshipCount);
        WorksheetMetadata worksheet = Assert.Single(metadata.Worksheets);

        ColumnMappingSuggestionResult suggestions = new ColumnMappingSuggester().Suggest(metadata);
        WorksheetMappingSuggestion suggestion = Assert.IsType<WorksheetMappingSuggestion>(
            suggestions.SuggestedWorksheet);
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new
        {
            worksheet.DimensionReference,
            worksheet.RowCount,
            worksheet.ColumnCount,
            suggestion.HeaderRow,
            suggestion.FirstDataRow,
            suggestion.LastDataRow,
            suggestion.InitialTargetColumns,
            suggestion.InitiallyUnselectedColumns,
            Candidates = suggestion.Candidates.Select(candidate => new
            {
                candidate.ColumnName,
                Roles = candidate.Roles.ToString(),
                candidate.SuggestedSupportingColumns,
            }),
        }));
        AssertSheet(worksheet, "A1:J531", 10);
        InputSnapshot after = snapshots.Capture(samplePath);
        InputSnapshotComparison comparison = snapshots.Recheck(samplePath, before);
        Assert.Equal(before, after);
        Assert.True(comparison.Sha256Matches);
        Assert.True(comparison.SizeMatches);
        Assert.True(comparison.LastWriteTimeUtcMatches);
        Assert.True(comparison.IsMatch);

        Assert.Equal(worksheet.Name, suggestion.WorksheetName);
        Assert.Equal(1U, suggestion.HeaderRow);
        Assert.Equal(2U, suggestion.FirstDataRow);
        Assert.Equal(531U, suggestion.LastDataRow);
        Assert.Equal(530U, suggestion.SuggestedDataRowCount);
        Assert.Equal(["D", "E", "F", "G", "H", "I"], suggestion.InitialTargetColumns);
        Assert.Equal(["A", "B", "C", "J"], suggestion.InitiallyUnselectedColumns);

        IReadOnlyDictionary<string, ColumnMappingCandidate> candidates = suggestion.Candidates
            .ToDictionary(candidate => candidate.ColumnName, StringComparer.Ordinal);
        Assert.Equal(
            ["D", "E", "F", "G", "H", "I"],
            candidates.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(ColumnMappingCandidateRole.PrimaryAnswer, candidates["D"].Roles);
        Assert.Equal(
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.StudentPromptPrimary,
            candidates["E"].Roles);
        Assert.Equal(ColumnMappingCandidateRole.Supporting, candidates["F"].Roles);
        Assert.Equal(ColumnMappingCandidateRole.PrimaryAnswer, candidates["G"].Roles);
        Assert.Equal(
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.StudentPromptPrimary,
            candidates["H"].Roles);
        Assert.Equal(ColumnMappingCandidateRole.Supporting, candidates["I"].Roles);
        Assert.Equal(["F"], candidates["E"].SuggestedSupportingColumns);
        Assert.Equal(["I"], candidates["H"].SuggestedSupportingColumns);
        Assert.All(new[] { "D", "F", "G", "I" }, column =>
            Assert.Empty(candidates[column].SuggestedSupportingColumns));

        // Exercise the same default design used by the opt-in technical smoke,
        // without dispatching a run, launching the app, or invoking AI/Excel.
        InputViewModel input = new();
        await input.SetFilePathAsync(samplePath, TestContext.Current.CancellationToken);
        Assert.True(input.HasLoadedWorkbook);
        Assert.True(input.CanContinue);
        Assert.Empty(input.ValidationErrors);
        QuantificationDefinition definition = input.CreateDesignDefinition();
        Assert.Equal(60m, definition.BasePoints);
        Assert.Equal(0m, definition.SpecialPoints);
        Assert.Equal(0.1m, definition.SimilarityPenaltyWeight);
        Assert.Equal(1, definition.RoundingDigits);
        Assert.Equal(["D", "E", "G", "H"], definition.Questions.Select(question => question.PrimarySourceColumn));
        Assert.Equal([10m, 10m, 10m, 10m], definition.Questions.Select(question => question.Points));
        Assert.Equal(
            [EvaluatorType.KnowledgeCoverage, EvaluatorType.CustomPrompt,
                EvaluatorType.KnowledgeCoverage, EvaluatorType.CustomPrompt],
            definition.Questions.Select(question => Assert.Single(question.Evaluators).Type));
        Assert.Equal(["F"], definition.Questions[1].SupportingSourceColumns);
        Assert.Equal(["I"], definition.Questions[3].SupportingSourceColumns);
        Assert.Empty(definition.Questions.SelectMany(question => question.SpecialEvaluations));
        Assert.Equal(before, snapshots.Capture(samplePath));
    }

    private static void AssertSheet(
        WorksheetMetadata worksheet,
        string expectedDimension,
        uint expectedColumns)
    {
        Assert.False(string.IsNullOrWhiteSpace(worksheet.Name));
        Assert.Equal(expectedDimension, worksheet.DimensionReference);
        Assert.Equal(1U, worksheet.FirstRowIndex);
        Assert.Equal(531U, worksheet.LastRowIndex);
        Assert.Equal(531U, worksheet.RowCount);
        Assert.Equal(1U, worksheet.FirstColumnIndex);
        Assert.Equal(expectedColumns, worksheet.LastColumnIndex);
        Assert.Equal(expectedColumns, worksheet.ColumnCount);
    }
}

public sealed class WindowsX64PerformanceEvidenceTests
{
    private const int MeasurementCount = 3;
    private const decimal ThresholdSeconds = 30m;

    [Fact]
    public void Synthetic_531_row_read_write_validation_median_is_recorded_and_within_target()
    {
        E02RepositoryLayout.RequireWindowsX64();
        decimal[] measurements = new decimal[MeasurementCount];
        for (int index = 0; index < measurements.Length; index++)
        {
            measurements[index] = MeasureSyntheticReadWriteValidation();
        }

        decimal median = measurements.Order().ElementAt(MeasurementCount / 2);
        DateTimeOffset measuredAtUtc = DateTimeOffset.UtcNow;
        string artifactPath = Path.Combine(
            E02RepositoryLayout.FindRepositoryRoot(),
            "artifacts",
            "test",
            "performance-windows-x64.json");
        WritePerformanceEvidence(artifactPath, measuredAtUtc, measurements, median);
        AssertPerformanceEvidenceSchema(artifactPath, measurements, median);

        Assert.True(
            median <= ThresholdSeconds,
            $"The measured median {median} seconds exceeded the {ThresholdSeconds} second target.");
    }

    private static decimal MeasureSyntheticReadWriteValidation()
    {
        using SyntheticWorkbook workbook = SyntheticWorkbookFactory.Create();
        Stopwatch stopwatch = Stopwatch.StartNew();
        InputSnapshotService snapshots = new();
        InputSnapshot inputBefore = snapshots.Capture(workbook.Path);

        FileFormatClassificationResult classification = new FileFormatClassifier().Classify(workbook.Path);
        Assert.True(classification.IsAccepted);
        WorkbookMetadata metadata = new WorkbookMetadataReader().Read(workbook.Path);
        WorksheetMappingSuggestion mappingSuggestion = Assert.IsType<WorksheetMappingSuggestion>(
            new ColumnMappingSuggester().Suggest(metadata).SuggestedWorksheet);
        Assert.Contains(
            mappingSuggestion.Candidates,
            candidate => candidate.ColumnName == "F"
                && candidate.IsPrimaryCandidate);

        QuantificationDefinition definition = CreatePerformanceDefinition();
        ColumnMappingValidationResult mapping = new ColumnMappingValidator().Validate(metadata, definition);
        Assert.True(mapping.IsValid);
        Assert.Equal(SyntheticWorkbookFactory.DataRowCount, mapping.Mapping!.SelectedRowCount);
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);
        ImmutableArray<ResultsSheetRowInput> rows = CreatePerformanceRows();
        string outputPath = Path.Combine(workbook.RootDirectory, "performance-output.xlsx");

        using (WorkingPackage package = WorkingPackage.Create(workbook.Path, outputPath))
        {
            AppOwnedSheetNames sheetNames;
            ConfigCellAddressMap config;
            ResultsSheetWriteResult results;
            DateTimeOffset runStartedAtUtc = DateTimeOffset.UtcNow;
            using (SpreadsheetDocument document = package.OpenForEditing())
            {
                sheetNames = new AppOwnedSheetNameResolver().Resolve(document);
                config = new ConfigSheetWriter().Write(
                    document,
                    snapshot,
                    sheetNames);
                new ReferenceAnswersSheetWriter().Write(
                    document,
                    snapshot,
                    sheetNames,
                    [
                        new ReferenceAnswerSheetRow
                        {
                            QuestionId = "Q-PERFORMANCE",
                            ModelId = "auto",
                            StatusCode = ResultsStatusCodes.AiRuntimeFailed,
                            GeneratedAtUtc = runStartedAtUtc,
                        },
                    ]);
                results = new ResultsSheetWriter().Write(
                    document,
                    snapshot,
                    sheetNames,
                    config,
                    rows);
                new RunSheetWriter().Write(
                    document,
                    new RunSheetMetadata
                    {
                        InputIdentity = inputBefore,
                        DefinitionSha256 = snapshot.Sha256,
                        ApplicationIdentity = "StudyReportEvaluator.App/E-02-performance",
                        CopilotSdkIdentity = "GitHub.Copilot.SDK/not-invoked",
                        CopilotCliIdentity = "copilot/not-invoked",
                        ModelIdentity = "synthetic-no-ai-wait",
                        StartedAtUtc = runStartedAtUtc,
                        EndedAtUtc = DateTimeOffset.UtcNow,
                        PlannedEvaluationCount = SyntheticWorkbookFactory.DataRowCount,
                        CompletedEvaluationCount = SyntheticWorkbookFactory.DataRowCount,
                        ErrorCount = 0,
                        SheetNames = sheetNames,
                    });
                new CalculationPropertiesWriter().Write(document);
            }

            Assert.Equal(SyntheticWorkbookFactory.DataRowCount, results.DataRowCount);
            Assert.Equal(SyntheticWorkbookFactory.DataRowCount * 11, results.FormulaCells.Length);
            ImmutableArray<ExpectedFormulaCell> expectedFormulas = config.FormulaCells
                .Concat(results.FormulaCells)
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
                inputBefore,
                validationPlan,
                TestContext.Current.CancellationToken);
            Assert.True(commit.IsSuccess);
            Assert.True(File.Exists(outputPath));
            Assert.True(snapshots.Recheck(workbook.Path, inputBefore).IsMatch);
        }

        stopwatch.Stop();
        File.Delete(outputPath);
        Assert.False(File.Exists(outputPath));
        return decimal.Round(
            (decimal)stopwatch.Elapsed.TotalSeconds,
            6,
            MidpointRounding.AwayFromZero);
    }

    private static QuantificationDefinition CreatePerformanceDefinition() =>
        new()
        {
            Id = "DEF-E02-PERFORMANCE",
            Name = "E-02 synthetic performance definition",
            Revision = "1",
            SourceSheet = SyntheticWorkbookFactory.SourceSheetName,
            HeaderRow = SyntheticWorkbookFactory.HeaderRow,
            FirstDataRow = SyntheticWorkbookFactory.FirstDataRow,
            LastDataRow = SyntheticWorkbookFactory.LastDataRow,
            BasePoints = 99m,
            RoundingDigits = 2,
            Questions =
            [
                new QuestionDefinition
                {
                    Id = "Q-PERFORMANCE",
                    DisplayName = "Synthetic performance question",
                    QuestionText = "Synthetic performance question",
                    PrimarySourceColumn = "F",
                    SupportingSourceColumns = [],
                    Points = 1m,
                    Evaluators =
                    [
                        new EvaluatorDefinition
                        {
                            Id = "E-PERFORMANCE",
                            DisplayName = "Synthetic performance evaluator",
                            Type = EvaluatorType.CustomPrompt,
                            Weight = 1m,
                            Range = new ScoreRange(0m, 10m),
                            CustomPromptTemplate = "{回答}\n{評価項目}",
                            Criteria =
                            [
                                new CriterionDefinition
                                {
                                    Id = "C-PERFORMANCE",
                                    DisplayName = "Synthetic performance criterion",
                                    Description = "Synthetic performance criterion",
                                    Weight = 1m,
                                },
                            ],
                        },
                    ],
                },
            ],
        };

    private static ImmutableArray<ResultsSheetRowInput> CreatePerformanceRows() =>
        Enumerable.Range(
                SyntheticWorkbookFactory.FirstDataRow,
                SyntheticWorkbookFactory.DataRowCount)
            .Select(sourceRow => new ResultsSheetRowInput
            {
                SourceRowNumber = sourceRow,
                Questions =
                [
                    new QuestionResultInput
                    {
                        QuestionId = "Q-PERFORMANCE",
                        Scorable = true,
                        Evaluators =
                        [
                            new EvaluatorResultInput
                            {
                                EvaluatorId = "E-PERFORMANCE",
                                Status = ResultsStatusCodes.Success,
                                AiResult = new QuantificationResult
                                {
                                    EvaluatorId = "E-PERFORMANCE",
                                    Criteria =
                                    [
                                        new CriterionQuantificationResult
                                        {
                                            CriterionId = "C-PERFORMANCE",
                                            RawScore = (sourceRow - SyntheticWorkbookFactory.FirstDataRow) % 11,
                                            Reason = "synthetic",
                                            Evidence = string.Empty,
                                            EvidenceSource = EvidenceSourceKind.None,
                                            EvidenceSourceColumnId = string.Empty,
                                        },
                                    ],
                                },
                            },
                        ],
                    },
                ],
            })
            .ToImmutableArray();

    private static void WritePerformanceEvidence(
        string artifactPath,
        DateTimeOffset measuredAtUtc,
        IReadOnlyList<decimal> measurements,
        decimal median)
    {
        string? directory = Path.GetDirectoryName(artifactPath);
        if (directory is null)
        {
            throw new InvalidOperationException("The performance artifact directory is unavailable.");
        }

        Directory.CreateDirectory(directory);
        using FileStream stream = new(
            artifactPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            FileOptions.WriteThrough);
        using Utf8JsonWriter writer = new(
            stream,
            new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("schema_version", "1.0");
        writer.WriteString("task", "E-02");
        writer.WriteString("measurement_date_utc", measuredAtUtc.ToString("yyyy-MM-dd"));
        writer.WriteString("measured_at_utc", measuredAtUtc.ToString("O"));
        writer.WriteStartObject("environment");
        writer.WriteString("os_description", RuntimeInformation.OSDescription);
        writer.WriteString("os_architecture", RuntimeInformation.OSArchitecture.ToString());
        writer.WriteString("process_architecture", RuntimeInformation.ProcessArchitecture.ToString());
        writer.WriteNumber("logical_processor_count", Environment.ProcessorCount);
        writer.WriteString("dotnet_version", Environment.Version.ToString());
        writer.WriteString("framework_description", RuntimeInformation.FrameworkDescription);
        writer.WriteEndObject();
        writer.WriteStartObject("scenario");
        writer.WriteString("name", "synthetic-531-row-read-write-validation");
        writer.WriteNumber("worksheet_rows", SyntheticWorkbookFactory.LastDataRow);
        writer.WriteNumber("data_rows", SyntheticWorkbookFactory.DataRowCount);
        writer.WriteNumber("run_count", MeasurementCount);
        writer.WriteBoolean("fixture_generation_included", false);
        writer.WriteBoolean("ai_wait_included", false);
        writer.WriteString(
            "operation_scope",
            "classification,metadata,mapping,copy,write,reopen-validation,atomic-commit,input-recheck");
        writer.WriteEndObject();
        writer.WriteNumber("threshold_seconds", ThresholdSeconds);
        writer.WriteStartArray("measurements_seconds");
        foreach (decimal measurement in measurements)
        {
            writer.WriteNumberValue(measurement);
        }

        writer.WriteEndArray();
        writer.WriteNumber("median_seconds", median);
        writer.WriteString("result", median <= ThresholdSeconds ? "PASS" : "FAIL");
        writer.WriteBoolean("content_data_included", false);
        writer.WriteEndObject();
        writer.Flush();
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    private static void AssertPerformanceEvidenceSchema(
        string artifactPath,
        IReadOnlyList<decimal> measurements,
        decimal median)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(artifactPath));
        JsonElement root = document.RootElement;
        Assert.Equal(
            [
                "schema_version",
                "task",
                "measurement_date_utc",
                "measured_at_utc",
                "environment",
                "scenario",
                "threshold_seconds",
                "measurements_seconds",
                "median_seconds",
                "result",
                "content_data_included",
            ],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("1.0", root.GetProperty("schema_version").GetString());
        Assert.Equal("E-02", root.GetProperty("task").GetString());
        Assert.Equal(MeasurementCount, root.GetProperty("measurements_seconds").GetArrayLength());
        Assert.Equal(measurements, root.GetProperty("measurements_seconds")
            .EnumerateArray()
            .Select(value => value.GetDecimal()));
        Assert.Equal(median, root.GetProperty("median_seconds").GetDecimal());
        Assert.Equal(
            median <= ThresholdSeconds ? "PASS" : "FAIL",
            root.GetProperty("result").GetString());
        Assert.False(root.GetProperty("content_data_included").GetBoolean());
    }
}

internal static class E02RepositoryLayout
{
    internal static void RequireWindowsX64()
    {
        Assert.True(
            OperatingSystem.IsWindows(),
            "E-02 is a required Windows-only acceptance test for the initial target.");
        Assert.True(
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000),
            "E-02 requires Windows 11 or later for the initial target.");
        Assert.Equal(Architecture.X64, RuntimeInformation.OSArchitecture);
        Assert.Equal(Architecture.X64, RuntimeInformation.ProcessArchitecture);
    }

    internal static string? TryGetLocalSamplePath()
    {
        string path = Path.Combine(FindRepositoryRoot(), "sample", "SampleReport.xlsx");
        return File.Exists(path) ? path : null;
    }

    internal static string GetRequiredSamplePath()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "sample",
            "SampleReport.xlsx");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("The required repository sample workbook is missing.");
        }

        return path;
    }


    internal static string FindRepositoryRoot()
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

        throw new DirectoryNotFoundException(
            "The repository root containing StudyReportEvaluator.slnx was not found.");
    }
}
