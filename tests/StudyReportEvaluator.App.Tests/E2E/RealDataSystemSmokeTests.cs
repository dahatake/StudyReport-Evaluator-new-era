using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using GitHub.Copilot;
using StudyReportEvaluator.App.Composition;
using StudyReportEvaluator.App.Launch;
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

public sealed partial class RealDataSystemSmokeTests
{
    private const string OptInEnvironmentVariable =
        "STUDY_REPORT_EVALUATOR_REALDATA_SYSTEM_SMOKE";
    private const string InputEnvironmentVariable =
        "STUDY_REPORT_EVALUATOR_REALDATA_INPUT";
    private const string EvidenceEnvironmentVariable =
        "STUDY_REPORT_EVALUATOR_REALDATA_EVIDENCE";
    private const string SourceCommitEnvironmentVariable =
        "STUDY_REPORT_EVALUATOR_SOURCE_COMMIT";
    private const string SourceStatusHashEnvironmentVariable =
        "STUDY_REPORT_EVALUATOR_SOURCE_STATUS_SHA256";
    private const string RunId = "SYSTEM-TEST-REALDATA-V4_5-20260906-D14";
    // Historical comparisons are diagnostic only (user-approved policy, 2026-09-16).
    private const long HistoricalSampleSizeBytes = 470_806;
    private const string HistoricalSampleSha256 =
        "73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA";
    private const string LocalModelId =
        "local-deterministic-midpoint-no-network-not-for-grading";
    // Compare the historical prefix without exposing current workbook content.
    private const string HistoricalSamplePrefixHex =
        "504B030414000600080000002100A90F"
        + "68387F01000002050000130008025B43"
        + "6F6E74656E745F54797065735D2E786D"
        + "6C20A2040228A0000200000000000000"
        + "00000000000000000000000000000000"
        + "00000000000000000000000000000000"
        + "00000000000000000000000000000000"
        + "00000000000000000000000000000000";

    [Fact]
    public async Task Optional_real_workbook_default_values_complete_local_v4_system_test()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(OptInEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        string repositoryRoot = FindRepositoryRoot();
        string inputPath = RequiredAbsoluteFile(InputEnvironmentVariable);
        string canonicalSamplePath = Path.Combine(repositoryRoot, "sample", "SampleReport.xlsx");
        Assert.True(
            PathsEqual(inputPath, canonicalSamplePath),
            "The technical E2E input must be the canonical SampleReport.xlsx path.");
        string evidencePath = RequiredAbsolutePath(EvidenceEnvironmentVariable);
        AssertEvidencePathDoesNotOverwriteInput(evidencePath, inputPath);
        string evidenceDirectory = Path.GetDirectoryName(evidencePath)
            ?? throw new InvalidOperationException("The evidence path has no parent directory.");
        Directory.CreateDirectory(evidenceDirectory);

        DateTimeOffset measuredAtUtc = DateTimeOffset.UtcNow;
        string outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "StudyReportEvaluator-SystemTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        string? finalPath = null;
        string? recalculationPath = null;
        try
        {
            InputSnapshotService snapshots = new();
            InputSnapshot inputBefore = snapshots.Capture(inputPath);
            bool historicalPrefixMatch = MatchesHistoricalSamplePrefix(inputPath);

            FileFormatClassificationResult classification = new FileFormatClassifier().Classify(inputPath);
            Assert.True(classification.IsAccepted);
            Assert.Equal(FileFormatClassification.StandardXlsx, classification.Classification);

            InputViewModel input = new();
            await input.SetFilePathAsync(inputPath, TestContext.Current.CancellationToken);
            Assert.True(input.HasLoadedWorkbook);
            Assert.True(input.CanContinue);
            Assert.Empty(input.ValidationErrors);
            WorkbookMetadata metadata = Assert.IsType<WorkbookMetadata>(input.Metadata);
            WorksheetMetadata sourceWorksheet = Assert.Single(metadata.Worksheets);
            Assert.Equal("A1:J531", sourceWorksheet.DimensionReference);
            Assert.Equal(531U, sourceWorksheet.RowCount);
            Assert.Equal(10U, sourceWorksheet.ColumnCount);
            Assert.Equal(1, input.HeaderRow);
            Assert.Equal(2, input.FirstDataRow);
            Assert.Equal(531, input.LastDataRow);

            QuantificationDefinition definition = input.CreateDesignDefinition();
            Assert.Equal(60m, definition.BasePoints);
            Assert.Equal(0m, definition.SpecialPoints);
            Assert.Equal(0.1m, definition.SimilarityPenaltyWeight);
            Assert.Equal(1, definition.RoundingDigits);
            // Current sample profile measured through the production reader/suggester on 2026-09-16.
            Assert.Equal(4, definition.Questions.Length);
            Assert.Equal(["D", "E", "G", "H"],
                definition.Questions.Select(question => question.PrimarySourceColumn));
            Assert.Equal([10m, 10m, 10m, 10m],
                definition.Questions.Select(question => question.Points));
            Assert.Equal(
                [EvaluatorType.KnowledgeCoverage, EvaluatorType.CustomPrompt,
                    EvaluatorType.KnowledgeCoverage, EvaluatorType.CustomPrompt],
                definition.Questions.Select(question => Assert.Single(question.Evaluators).Type));
            Assert.Empty(definition.Questions.SelectMany(question => question.SpecialEvaluations));
            Assert.Empty(definition.Questions[0].SupportingSourceColumns);
            Assert.Equal(["F"], definition.Questions[1].SupportingSourceColumns);
            Assert.Empty(definition.Questions[2].SupportingSourceColumns);
            Assert.Equal(["I"], definition.Questions[3].SupportingSourceColumns);

            QuantificationDesignViewModel design = new(
                definition,
                input.AvailableColumnNames);
            Assert.True(design.IsValid);
            Assert.True(design.CanBuildSnapshot);
            Assert.True(design.IsAllocationValid);
            Assert.Equal(100m, design.AllocationTotal);
            Assert.Equal(0m, design.AllocationRemaining);

            LaunchProbeResult launchProbe = await ProbeLaunchAsync(
                repositoryRoot,
                inputPath,
                inputBefore,
                TestContext.Current.CancellationToken);
            Assert.True(snapshots.Recheck(inputPath, inputBefore).IsMatch);

            SharedConcurrencyTracker concurrency = new();
            LocalNormalRunner normal = new(concurrency);
            LocalReferenceRunner references = new(concurrency);
            NoCallSpecialRunner specials = new();
            CountingRowSource rowSource = new(new OpenXmlEvaluationRowSource(inputPath));
            TracingCheckpointFileOperations checkpointFiles = new();
            CountingCheckpointStore checkpoints = new(new CheckpointStore(checkpointFiles));
            ConcurrentQueue<DurableEvaluationProgress> progress = new();
            Stopwatch runWatch = Stopwatch.StartNew();
            CheckpointRuntimeIdentity runtime = RuntimeIdentity();
            DurableQuantificationOrchestrator orchestrator = new(
                rowSource,
                normal,
                references,
                specials,
                new PhysicalInputSnapshotBoundary(),
                checkpoints,
                new OutputPathPlanner(),
                new WorkbookDurableRunFinalizer(),
                new PhysicalPartialCheckpointCleaner());

            RunSummary summary = await orchestrator.RunAsync(
                new DurableQuantificationRunRequest
                {
                    Run = new QuantificationRunRequest
                    {
                        DraftDefinition = definition,
                        WorkbookMetadata = metadata,
                        InputPath = inputPath,
                        ModelId = LocalModelId,
                        MaximumPromptTokens = 64_000,
                        MaximumContextWindowTokens = 128_000,
                        MaxConcurrency = EvaluationSchedulerOptions.DefaultMaxConcurrency,
                    },
                    Runtime = runtime,
                    OutputDirectory = outputDirectory,
                },
                progress.Enqueue,
                TestContext.Current.CancellationToken);
            runWatch.Stop();

            Assert.True(
                string.Equals(
                    QuantificationRunStatusCodes.Success,
                    summary.StatusCode,
                    StringComparison.Ordinal),
                string.Join(
                    "; ",
                    $"status={summary.StatusCode}",
                    $"finalization={summary.FinalizationCode ?? "<none>"}",
                    $"references={summary.References.Length}",
                    $"rows={summary.CompletedRows.Length}",
                    $"checkpointCreates={checkpoints.CreateCount}",
                    $"checkpointUpdateAttempts={checkpoints.UpdateAttemptCount}",
                    $"checkpointUpdates={checkpoints.UpdateCount}",
                    $"lastCheckpointCode={checkpoints.LastSaveCode}",
                    $"checkpointFileFailures={checkpointFiles.FailureCount}",
                    $"lastFailedFileOperation={checkpointFiles.LastFailedOperation}",
                    $"lastFileExceptionType={checkpointFiles.LastExceptionType}"));
            Assert.True(summary.IsDurable);
            Assert.False(summary.WasResumed);
            Assert.False(summary.IsPartial);
            Assert.False(summary.PartialCleanupFailed);
            Assert.Equal(AtomicOutputStatusCodes.Success, summary.FinalizationCode);
            Assert.Equal(530, summary.CompletedRows.Length);
            Assert.Equal(4, summary.References.Length);
            Assert.Equal(2_120, summary.PlannedEvaluationCount);
            Assert.Equal(2_120, summary.CompletedEvaluationCount);
            Assert.Equal(0, summary.FailureCount);
            Assert.Equal(0, summary.CancelledCount);
            Assert.Equal(summary.PlannedOperationCount, summary.CompletedOperationCount);
            Assert.Equal(0, summary.OperationFailureCount);
            Assert.Equal(0, summary.OperationCancelledCount);
            Assert.Equal(4, references.CallCount);
            Assert.Equal(summary.SucceededCount, normal.CallCount);
            Assert.Equal(0, specials.CallCount);
            Assert.Equal(1_060, rowSource.ReadCount);
            Assert.Equal(1, checkpoints.CreateCount);
            // One update after each of 4 references and each of 530 completed rows.
            Assert.Equal(534, checkpoints.UpdateCount);
            Assert.Equal(0, checkpoints.LoadCount);
            Assert.Equal(1, concurrency.MaximumObserved);
            Assert.True(summary.SucceededCount + summary.EmptyCount == summary.PlannedEvaluationCount);
            Assert.True(snapshots.Recheck(inputPath, inputBefore).IsMatch);

            DurableEvaluationProgress[] progressItems = progress.ToArray();
            Assert.NotEmpty(progressItems);
            Assert.Contains(progressItems, item => item.Stage == DurableEvaluationStage.Preparing);
            Assert.Contains(progressItems, item => item.Stage == DurableEvaluationStage.GeneratingReferences);
            Assert.Contains(progressItems, item => item.Stage == DurableEvaluationStage.SavingCheckpoint);
            Assert.Contains(progressItems, item => item.Stage == DurableEvaluationStage.EvaluatingRows);
            Assert.Contains(progressItems, item => item.Stage == DurableEvaluationStage.FinalizingWorkbook);
            Assert.Equal(DurableEvaluationStage.Completed, progressItems[^1].Stage);
            Assert.Equal(0, progressItems[^1].InFlight);
            Assert.True(progressItems
                .Zip(progressItems.Skip(1), (left, right) =>
                    left.ReferenceCompleted <= right.ReferenceCompleted
                    && left.RowCompleted <= right.RowCompleted
                    && left.OperationCompleted <= right.OperationCompleted)
                .All(value => value));

            finalPath = Assert.IsType<string>(summary.FinalPath);
            string partialPath = Assert.IsType<string>(summary.PartialPath);
            Assert.True(File.Exists(finalPath));
            Assert.False(File.Exists(partialPath));
            OutputInspection output = InspectOutput(finalPath, sourceWorksheet.Name);
            Assert.Equal(5, output.WorksheetCount);
            Assert.Equal(4, output.AppOwnedSheetCount);
            Assert.False(output.CheckpointSheetPresent);
            Assert.Equal(2, output.ConfigFormulaCount);
            Assert.Equal(0, output.ReferenceFormulaCount);
            Assert.Equal(0, output.RunFormulaCount);
            Assert.Equal(530, output.ResultDataRowCount);
            Assert.True(output.ResultColumnCount > 0);
            Assert.True(output.ResultFormulaCount > 0);
            Assert.Equal(0, output.FormulaErrorCount);
            Assert.Equal(0, output.OpenXmlValidationErrorCount);
            Assert.Equal(530, output.NumericFinalScoreCount);
            Assert.Equal(0, output.BlankFinalScoreCount);
            Assert.True(output.CalculationModeAutomatic);
            Assert.True(output.FullCalculationOnLoad);
            Assert.Equal(0, Directory.EnumerateFiles(outputDirectory)
                .Count(path => !PathsEqual(path, finalPath)));

            string finalHashBeforeRecalculation = Sha256(finalPath);
            recalculationPath = Path.Combine(outputDirectory, "recalculation-copy.xlsx");
            File.Copy(finalPath, recalculationPath, overwrite: false);
            ExternalRecalculationResult external = RecalculateAndInspect(recalculationPath);
            Assert.Equal(finalHashBeforeRecalculation, Sha256(finalPath));

            InputSnapshot inputAfter = snapshots.Capture(inputPath);
            Assert.Equal(inputBefore, inputAfter);

            var evidence = new
            {
                schema_version = "1.0",
                run_id = RunId,
                measured_at_utc = measuredAtUtc,
                source = new
                {
                    commit = Environment.GetEnvironmentVariable(SourceCommitEnvironmentVariable) ?? "UNRECORDED",
                    worktree_status_sha256 = Environment.GetEnvironmentVariable(SourceStatusHashEnvironmentVariable) ?? "UNRECORDED",
                    test_driver = "tests/StudyReportEvaluator.App.Tests/E2E/RealDataSystemSmokeTests.cs",
                    requirements = "docs/requirements-definition.md v4.6",
                    system_test_prompt = "SystemTest-prompt.md v4.6",
                },
                environment = new
                {
                    os_description = RuntimeInformation.OSDescription,
                    os_architecture = RuntimeInformation.OSArchitecture.ToString(),
                    process_architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    framework = RuntimeInformation.FrameworkDescription,
                    dotnet_runtime_version = Environment.Version.ToString(),
                    logical_processor_count = Environment.ProcessorCount,
                    powershell_requirement = "PowerShell 7+ Core enforced by invocation",
                },
                input = new
                {
                    logical_name = "SampleReport.xlsx",
                    historical_sample_identity_gating = false,
                    sample_size_match = inputBefore.SizeBytes == HistoricalSampleSizeBytes,
                    sample_sha256_match = inputBefore.Sha256 == HistoricalSampleSha256,
                    sample_prefix_128_match = historicalPrefixMatch,
                    size_bytes = inputBefore.SizeBytes,
                    sha256 = inputBefore.Sha256,
                    last_write_time_utc = inputBefore.LastWriteTimeUtc,
                    classification = classification.Classification.ToString(),
                    package_part_count = metadata.PackagePartCount,
                    worksheet_count = metadata.Worksheets.Count,
                    source_sheet_name_sha256 = HashText(sourceWorksheet.Name),
                    dimension = sourceWorksheet.DimensionReference,
                    header_row = definition.HeaderRow,
                    first_data_row = definition.FirstDataRow,
                    last_data_row = definition.LastDataRow,
                    data_row_count = definition.LastDataRow - definition.FirstDataRow + 1,
                    unchanged_after_all_operations = inputBefore.Equals(inputAfter),
                    cell_content_in_evidence = false,
                    header_content_in_evidence = false,
                    worksheet_name_in_evidence = false,
                    private_path_in_evidence = false,
                    privacy_scan_enforced_before_write = true,
                },
                initial_values = new
                {
                    base_points = definition.BasePoints,
                    special_points = definition.SpecialPoints,
                    similarity_penalty_weight = definition.SimilarityPenaltyWeight,
                    rounding_digits = definition.RoundingDigits,
                    max_concurrency = EvaluationSchedulerOptions.DefaultMaxConcurrency,
                    question_count = definition.Questions.Length,
                    allocation_total = design.AllocationTotal,
                    allocation_remaining = design.AllocationRemaining,
                    questions = definition.Questions.Select((question, index) => new
                    {
                        ordinal = index + 1,
                        primary_source_column = question.PrimarySourceColumn,
                        supporting_source_columns = question.SupportingSourceColumns,
                        evaluator_type = Assert.Single(question.Evaluators).Type.ToString(),
                        points = question.Points,
                    }),
                },
                launch = launchProbe,
                local_ai_boundary = new
                {
                    live_ai_invoked = false,
                    network_invoked = false,
                    policy = "NOT_RUN_POLICY_REAL_DATA",
                    evaluation_semantics = "DETERMINISTIC_RANGE_MIDPOINT_TECHNICAL_TEST_ONLY_NOT_EDUCATIONAL_GRADING",
                    model_identity = LocalModelId,
                    reference_value_is_fixed_technical_text = true,
                    normal_value_is_effective_range_midpoint = true,
                    similarity_value = 0.25m,
                    fake_runner_source_access = "runner implementation uses IDs/ranges only; evidence writer scans input text absence",
                },
                durable_run = new
                {
                    status = summary.StatusCode,
                    finalization_code = summary.FinalizationCode,
                    elapsed_seconds = runWatch.Elapsed.TotalSeconds,
                    planned_normal_evaluations = summary.PlannedEvaluationCount,
                    completed_normal_evaluations = summary.CompletedEvaluationCount,
                    succeeded_normal_evaluations = summary.SucceededCount,
                    empty_normal_evaluations = summary.EmptyCount,
                    failed_normal_evaluations = summary.FailureCount,
                    cancelled_normal_evaluations = summary.CancelledCount,
                    planned_operations = summary.PlannedOperationCount,
                    completed_operations = summary.CompletedOperationCount,
                    operation_failures = summary.OperationFailureCount,
                    operation_cancelled = summary.OperationCancelledCount,
                    reference_runner_calls = references.CallCount,
                    normal_runner_calls = normal.CallCount,
                    special_runner_calls = specials.CallCount,
                    source_row_reads = rowSource.ReadCount,
                    checkpoint_creates = checkpoints.CreateCount,
                    checkpoint_updates = checkpoints.UpdateCount,
                    checkpoint_loads = checkpoints.LoadCount,
                    maximum_observed_concurrency = concurrency.MaximumObserved,
                    completed_rows = summary.CompletedRows.Length,
                    references = summary.References.Length,
                    stages = progressItems.Select(item => item.Stage.ToString()).Distinct().ToArray(),
                    progress_monotonic = true,
                    final_in_flight = progressItems[^1].InFlight,
                    partial_cleanup_failed = summary.PartialCleanupFailed,
                },
                output = new
                {
                    logical_name = Path.GetFileName(finalPath),
                    size_bytes = new FileInfo(finalPath).Length,
                    sha256 = finalHashBeforeRecalculation,
                    output,
                    source_output_unchanged_after_recalculation_copy = finalHashBeforeRecalculation == Sha256(finalPath),
                },
                external_spreadsheet_recalculation = external,
                cleanup = new
                {
                    output_and_copy_are_test_temporary_files = true,
                },
                result = "PASS_PRIMARY_APPLICATION_PATH",
            };

            WriteEvidence(evidencePath, evidence, inputPath);
        }
        finally
        {
            DeleteFileIfPresent(recalculationPath);
            DeleteFileIfPresent(finalPath);
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }

        Assert.False(Directory.Exists(outputDirectory));
        Assert.True(File.Exists(evidencePath));
    }

    [Fact]
    public void Evidence_path_matching_input_path_is_rejected_before_smoke_writes()
    {
        string inputPath = Path.GetFullPath(Path.Combine("sample", "SampleReport.xlsx"));
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AssertEvidencePathDoesNotOverwriteInput(inputPath, inputPath.ToUpperInvariant()));
        Assert.Contains("must not be the input workbook path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evidence_path_may_share_input_directory_when_file_name_differs()
    {
        string inputPath = Path.GetFullPath(Path.Combine("sample", "SampleReport.xlsx"));
        string evidencePath = Path.Combine(
            Path.GetDirectoryName(inputPath)!,
            "real-data-evidence.json");
        AssertEvidencePathDoesNotOverwriteInput(evidencePath, inputPath);
    }

    private static CheckpointRuntimeIdentity RuntimeIdentity()
    {
        Assembly appAssembly = typeof(DurableQuantificationOrchestrator).Assembly;
        string applicationVersion = appAssembly.GetName().Version?.ToString()
            ?? "0.0.0.0";
        string sdkVersion = typeof(CopilotClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? "1.0.11";
        return new CheckpointRuntimeIdentity
        {
            ApplicationIdentity = appAssembly.GetName().Name + "/" + applicationVersion,
            CliVersion = "1.0.79-test-no-cli",
            CliSha256 = new string('0', 64),
            SdkInformationalVersion = sdkVersion,
        };
    }

    private static async Task<LaunchProbeResult> ProbeLaunchAsync(
        string repositoryRoot,
        string inputPath,
        InputSnapshot expectedInput,
        CancellationToken cancellationToken)
    {
        LaunchStartupState startup = LaunchStartupState.Create(
            ["--input", inputPath],
            repositoryRoot);
        Assert.True(startup.IsValid);
        using MainWindowViewModel shell = ServiceRegistration
            .FromStartup(startup)
            .CreateMainWindowViewModel();
        await shell.InputViewModel.LoadLaunchInputIfRequestedAsync(cancellationToken);
        Assert.True(shell.InputViewModel.HasLoadedWorkbook);
        Assert.True(shell.InputViewModel.CanContinue);
        Assert.Equal(4, shell.InputViewModel.Questions.Count);

        string resultDirectory = Path.Combine(
            Path.GetDirectoryName(inputPath)
                ?? throw new InvalidOperationException("The input has no parent directory."),
            "result");
        string resultStateBefore = DirectoryStateHash(resultDirectory);
        HashSet<int> copilotProcessesBefore = CopilotProcessIds();
        string executable = Path.Combine(
            repositoryRoot,
            "src",
            "StudyReportEvaluator.App",
            "bin",
            "Release",
            "net10.0",
            "StudyReportEvaluator.App.exe");
        Assert.True(File.Exists(executable), "The Release apphost must be built before the real-data system probe.");

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false,
                CreateNoWindow = false,
            },
        };
        process.StartInfo.ArgumentList.Add("--input");
        process.StartInfo.ArgumentList.Add(inputPath);
        Stopwatch watch = Stopwatch.StartNew();
        bool started = false;
        bool bodyFailed = false;
        bool controlledCleanup = false;
        bool exitedDuringProbe = false;
        string[] moduleNames = [];
        int newCopilotProcessCount = 0;
        try
        {
            started = process.Start();
            Assert.True(started);
            exitedDuringProbe = await WaitForExitAsync(
                process,
                TimeSpan.FromMilliseconds(2_000),
                cancellationToken);
            watch.Stop();
            Assert.False(exitedDuringProbe,
                exitedDuringProbe
                    ? $"The application exited during startup with code {process.ExitCode}."
                    : "The application must remain alive during startup.");
            process.Refresh();
            moduleNames = process.Modules
                .Cast<ProcessModule>()
                .Select(module => module.ModuleName)
                .ToArray();
            newCopilotProcessCount = CopilotProcessIds().Count(processId =>
                !copilotProcessesBefore.Contains(processId));
            Assert.DoesNotContain(moduleNames, IsOfficeRuntimeModule);
            Assert.Equal(0, newCopilotProcessCount);
        }
        catch
        {
            bodyFailed = true;
            throw;
        }
        finally
        {
            if (started)
            {
                try
                {
                    await EnsureStoppedAsync(process);
                    controlledCleanup = process.HasExited;
                }
                catch when (bodyFailed)
                {
                }
            }
        }

        string resultStateAfter = DirectoryStateHash(resultDirectory);
        Assert.Equal(resultStateBefore, resultStateAfter);
        Assert.True(new InputSnapshotService().Recheck(inputPath, expectedInput).IsMatch);
        return new LaunchProbeResult(
            ParserAccepted: true,
            PrefillLoaded: true,
            ProcessStarted: started,
            StartupProbeMilliseconds: watch.Elapsed.TotalMilliseconds,
            ExitedDuringProbe: exitedDuringProbe,
            ControlledCleanup: controlledCleanup,
            OfficeRuntimeModuleCount: moduleNames.Count(IsOfficeRuntimeModule),
            ResultDirectoryStateUnchanged: resultStateBefore == resultStateAfter,
            NewCopilotProcessCount: newCopilotProcessCount,
            PotentialAutomaticRunSignalObserved:
                newCopilotProcessCount > 0 || resultStateBefore != resultStateAfter);
    }

    private static OutputInspection InspectOutput(string outputPath, string sourceSheetName)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(outputPath, false);
        OpenXmlValidator validator = new();
        int validationErrors = validator.Validate(document).Count();
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The output workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("The output workbook root is missing.");
        Sheet[] sheets = workbook.Descendants<Sheet>().ToArray();
        string[] appNames =
        [
            AppOwnedSheetNameResolver.ConfigBaseName,
            AppOwnedSheetNameResolver.ReferencesBaseName,
            AppOwnedSheetNameResolver.ResultsBaseName,
            AppOwnedSheetNameResolver.RunBaseName,
        ];
        Assert.Contains(sheets, sheet => sheet.Name?.Value == sourceSheetName);
        Assert.All(appNames, name => Assert.Contains(sheets, sheet => sheet.Name?.Value == name));

        Worksheet config = Worksheet(document, AppOwnedSheetNameResolver.ConfigBaseName);
        Worksheet references = Worksheet(document, AppOwnedSheetNameResolver.ReferencesBaseName);
        Worksheet results = Worksheet(document, AppOwnedSheetNameResolver.ResultsBaseName);
        Worksheet run = Worksheet(document, AppOwnedSheetNameResolver.RunBaseName);
        Row header = results.Descendants<Row>().Single(row => row.RowIndex?.Value == 1);
        Dictionary<string, string> columns = header.Elements<Cell>().ToDictionary(
            cell => cell.InnerText,
            cell => Column(cell.CellReference?.Value),
            StringComparer.Ordinal);
        string finalScoreColumn = columns[ResultsSheetWriter.FinalScoreHeader];
        Cell[] finalScores = results.Descendants<Row>()
            .Where(row => row.RowIndex?.Value >= 2)
            .Select(row => row.Elements<Cell>().Single(cell => string.Equals(
                Column(cell.CellReference?.Value),
                finalScoreColumn,
                StringComparison.Ordinal)))
            .ToArray();
        int formulaErrors = document
            .GetAllParts()
            .OfType<WorksheetPart>()
            .SelectMany(part => part.Worksheet?.Descendants<Cell>() ?? [])
            .Count(IsFormulaError);
        CalculationProperties? properties = workbook.CalculationProperties;

        return new OutputInspection(
            WorksheetCount: sheets.Length,
            AppOwnedSheetCount: sheets.Count(sheet => appNames.Contains(
                sheet.Name?.Value,
                StringComparer.Ordinal)),
            CheckpointSheetPresent: sheets.Any(sheet => string.Equals(
                sheet.Name?.Value,
                CheckpointStore.CheckpointSheetName,
                StringComparison.OrdinalIgnoreCase)),
            ConfigFormulaCount: config.Descendants<CellFormula>().Count(),
            ReferenceFormulaCount: references.Descendants<CellFormula>().Count(),
            ResultFormulaCount: results.Descendants<CellFormula>().Count(),
            RunFormulaCount: run.Descendants<CellFormula>().Count(),
            ResultDataRowCount: results.Descendants<Row>().Count(row => row.RowIndex?.Value >= 2),
            ResultColumnCount: header.Elements<Cell>().Count(),
            FormulaErrorCount: formulaErrors,
            OpenXmlValidationErrorCount: validationErrors,
            NumericFinalScoreCount: finalScores.Count(cell =>
                cell.CellFormula is not null
                && cell.DataType?.Value == CellValues.Number
                && decimal.TryParse(cell.CellValue?.Text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _)),
            BlankFinalScoreCount: finalScores.Count(cell =>
                cell.CellFormula is not null && cell.CellValue is null),
            CalculationModeAutomatic: properties?.CalculationMode?.Value == CalculateModeValues.Auto,
            FullCalculationOnLoad: properties?.FullCalculationOnLoad?.Value == true);
    }

    private static ExternalRecalculationResult RecalculateAndInspect(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ExternalRecalculationResult(
                "NOT_RUN_EXTERNAL_PREREQUISITE",
                "supported_spreadsheet_unavailable",
                null,
                0,
                0,
                0,
                false,
                0,
                [],
                true);
        }

        Type? excelType = Type.GetTypeFromProgID("Excel.Application", throwOnError: false);
        if (excelType is null)
        {
            return new ExternalRecalculationResult(
                "SKIPPED_NOT_INSTALLED",
                "supported_spreadsheet_unavailable",
                null,
                0,
                0,
                0,
                false,
                0,
                [],
                true);
        }

        ExcelAutomationResult automation = RecalculateWithExcel(excelType, path);
        if (!automation.Succeeded)
        {
            return new ExternalRecalculationResult(
                "FAILED_ADVISORY",
                "external_recalculation_failed",
                automation.Version,
                0,
                0,
                0,
                false,
                0,
                [],
                automation.ProcessCleanupConfirmed);
        }

        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, false);
        Cell[] formulaCells = document.GetAllParts()
            .OfType<WorksheetPart>()
            .SelectMany(part => part.Worksheet?.Descendants<Cell>() ?? [])
            .Where(cell => cell.CellFormula is not null)
            .ToArray();
        int formulaErrors = formulaCells.Count(IsFormulaError);
        ValidationErrorInfo[] validationErrors = new OpenXmlValidator()
            .Validate(document)
            .Select(error => new ValidationErrorInfo(
                error.Id ?? "UNKNOWN",
                error.ErrorType.ToString(),
                error.Node?.LocalName ?? "<none>",
                error.Part?.Uri.ToString() ?? "<none>"))
            .ToArray();
        ValidationErrorCategory[] categories = validationErrors
            .GroupBy(error => new
            {
                error.Id,
                error.ErrorType,
                error.NodeLocalName,
                error.PartUri,
            })
            .Select(group => new ValidationErrorCategory(
                group.Key.Id,
                group.Key.ErrorType,
                group.Key.NodeLocalName,
                group.Key.PartUri,
                group.Count()))
            .OrderBy(category => category.Id, StringComparer.Ordinal)
            .ThenBy(category => category.PartUri, StringComparer.Ordinal)
            .ToArray();
        CalculationChainPart? chain = document.WorkbookPart?.CalculationChainPart;
        int chainCells = chain?.CalculationChain?.Descendants<CalculationCell>().Count() ?? 0;
        string status = formulaErrors == 0 && validationErrors.Length == 0
            ? "PASS"
            : "FAILED_ADVISORY";
        string rationale = formulaErrors > 0
            ? "formula_error_detected"
            : validationErrors.Length > 0
                ? "openxml_validation_error_after_external_save"
                : "external_recalculation_completed";
        return new ExternalRecalculationResult(
            status,
            rationale,
            automation.Version,
            formulaCells.Length,
            formulaErrors,
            validationErrors.Length,
            chain is not null,
            chainCells,
            categories,
            automation.ProcessCleanupConfirmed);
    }

    [SupportedOSPlatform("windows")]
    private static ExcelAutomationResult RecalculateWithExcel(Type excelType, string workbookPath)
    {
        using ManualResetEventSlim completed = new(false);
        bool succeeded = false;
        string? version = null;
        int processId = 0;
        Thread thread = new(() =>
        {
            object? applicationObject = null;
            object? workbooksObject = null;
            object? workbookObject = null;
            try
            {
                applicationObject = Activator.CreateInstance(excelType)
                    ?? throw new InvalidOperationException("Excel automation could not start.");
                dynamic application = applicationObject;
                application.Visible = false;
                application.DisplayAlerts = false;
                application.EnableEvents = false;
                application.AskToUpdateLinks = false;
                application.AutomationSecurity = 3;
                version = Convert.ToString(application.Version, CultureInfo.InvariantCulture);
                nint windowHandle = (nint)(int)application.Hwnd;
                _ = GetWindowThreadProcessId(windowHandle, out uint createdProcessId);
                processId = checked((int)createdProcessId);
                workbooksObject = application.Workbooks;
                dynamic workbooks = workbooksObject;
                workbookObject = workbooks.Open(
                    workbookPath,
                    UpdateLinks: 0,
                    ReadOnly: false,
                    IgnoreReadOnlyRecommended: true,
                    AddToMru: false,
                    Local: true,
                    CorruptLoad: 0);
                dynamic workbook = workbookObject;
                application.CalculateFullRebuild();
                workbook.Save();
                workbook.Close(SaveChanges: false);
                workbookObject = null;
                application.Quit();
                succeeded = true;
            }
            catch
            {
                succeeded = false;
            }
            finally
            {
                ReleaseComObject(workbookObject);
                ReleaseComObject(workbooksObject);
                if (applicationObject is not null)
                {
                    try
                    {
                        ((dynamic)applicationObject).Quit();
                    }
                    catch
                    {
                    }
                }

                ReleaseComObject(applicationObject);
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "StudyReportEvaluator-RealData-Excel-Advisory",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (completed.Wait(TimeSpan.FromSeconds(120)))
        {
            thread.Join();
            return new ExcelAutomationResult(
                succeeded,
                version,
                ProcessHasExited(processId));
        }

        bool processCleanupConfirmed = processId <= 0;
        if (processId > 0)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                processCleanupConfirmed = process.WaitForExit(5_000);
            }
            catch
            {
                processCleanupConfirmed = ProcessHasExited(processId);
            }
        }

        if (processCleanupConfirmed && completed.Wait(TimeSpan.FromSeconds(5)))
        {
            thread.Join(TimeSpan.FromSeconds(5));
        }

        return new ExcelAutomationResult(false, version, processCleanupConfirmed);
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

    private static bool IsFormulaError(Cell cell)
    {
        if (cell.DataType?.Value == CellValues.Error)
        {
            return true;
        }

        string value = cell.CellValue?.Text ?? string.Empty;
        return value is "#REF!" or "#DIV/0!" or "#VALUE!" or "#N/A" or "#NAME?"
            or "#NUM!" or "#NULL!";
    }

    private static string Column(string? reference) =>
        new((reference ?? string.Empty).TakeWhile(char.IsAsciiLetter).ToArray());

    private static bool MatchesHistoricalSamplePrefix(string path)
    {
        byte[] expected = Convert.FromHexString(HistoricalSamplePrefixHex);
        byte[] actual = new byte[expected.Length];
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < expected.Length)
        {
            return false;
        }

        stream.ReadExactly(actual);
        return actual.SequenceEqual(expected);
    }

    private static string RequiredAbsoluteFile(string variable)
    {
        string path = RequiredAbsolutePath(variable);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The opted-in real-data input is unavailable.");
        }

        return path;
    }

    private static string RequiredAbsolutePath(string variable)
    {
        string value = Environment.GetEnvironmentVariable(variable)
            ?? throw new InvalidOperationException($"Environment variable {variable} is required for the opted-in smoke.");
        if (!Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException($"Environment variable {variable} must contain an absolute path.");
        }

        return Path.GetFullPath(value);
    }

    private static void AssertEvidencePathDoesNotOverwriteInput(string evidencePath, string inputPath)
    {
        if (PathsEqual(evidencePath, inputPath))
        {
            throw new InvalidOperationException(
                "The evidence path must not be the input workbook path.");
        }
    }

    private static void WriteEvidence(string path, object evidence, string inputPath)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            evidence,
            new JsonSerializerOptions { WriteIndented = true });
        AssertEvidenceContainsNoInputContent(bytes, inputPath);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       16_384,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            DeleteFileIfPresent(temporary);
        }
    }

    private static string DirectoryStateHash(string path)
    {
        if (!Directory.Exists(path))
        {
            return "ABSENT";
        }

        string state = string.Join(
            "\n",
            Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)
                .Select(entry => new FileInfo(entry))
                .OrderBy(info => info.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(info => string.Join(
                    "|",
                    Path.GetRelativePath(path, info.FullName),
                    info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "directory",
                    info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))));
        return HashText(state);
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void AssertEvidenceContainsNoInputContent(
        byte[] evidence,
        string inputPath)
    {
        string json = Encoding.UTF8.GetString(evidence);
        string fullInputPath = Path.GetFullPath(inputPath);
        string? inputDirectory = Path.GetDirectoryName(fullInputPath);
        if (json.Contains(fullInputPath, StringComparison.OrdinalIgnoreCase)
            || (inputDirectory is not null
                && json.Contains(inputDirectory, StringComparison.OrdinalIgnoreCase))
            || (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is string userProfile
                && !string.IsNullOrWhiteSpace(userProfile)
                && json.Contains(userProfile, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Evidence contains a private path.");
        }

        foreach (string value in ReadSensitiveWorkbookText(inputPath))
        {
            if (json.Contains(value, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Evidence contains workbook text content.");
            }
        }
    }

    private static IEnumerable<string> ReadSensitiveWorkbookText(string inputPath)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(inputPath, false);
        SharedStringTable? sharedStrings = document.WorkbookPart?
            .SharedStringTablePart?
            .SharedStringTable;
        HashSet<string> values = new(StringComparer.Ordinal);
        foreach (Cell cell in document.GetAllParts()
                     .OfType<WorksheetPart>()
                     .SelectMany(part => part.Worksheet?.Descendants<Cell>() ?? []))
        {
            string value = CellText(cell, sharedStrings).Trim();
            if (value.Length >= 16
                && value.Any(char.IsLetter)
                && !decimal.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string CellText(Cell cell, SharedStringTable? sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(
                cell.CellValue?.Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int sharedStringIndex)
            && sharedStrings is not null)
        {
            return sharedStrings.Elements<SharedStringItem>()
                .ElementAtOrDefault(sharedStringIndex)?
                .InnerText ?? string.Empty;
        }

        return cell.DataType?.Value == CellValues.InlineString
            ? cell.InlineString?.InnerText ?? string.Empty
            : cell.CellValue?.Text ?? string.Empty;
    }

    private static string Sha256(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static HashSet<int> CopilotProcessIds()
    {
        HashSet<int> processIds = [];
        foreach (string processName in new[] { "copilot", "copilot-runtime" })
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    processIds.Add(process.Id);
                }
            }
        }

        return processIds;
    }

    private static bool ProcessHasExited(int processId)
    {
        if (processId <= 0)
        {
            return true;
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.HasExited || process.WaitForExit(5_000);
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task EnsureStoppedAsync(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            if (process.CloseMainWindow()
                && await WaitForExitAsync(process, TimeSpan.FromSeconds(3), CancellationToken.None))
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            Assert.True(await WaitForExitAsync(
                process,
                TimeSpan.FromSeconds(5),
                CancellationToken.None));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool IsOfficeRuntimeModule(string moduleName)
    {
        string name = Path.GetFileNameWithoutExtension(moduleName);
        return name.Equals("excel", StringComparison.OrdinalIgnoreCase)
            || name.Equals("soffice", StringComparison.OrdinalIgnoreCase)
            || name.Contains("libreoffice", StringComparison.OrdinalIgnoreCase)
            || name.Contains("office.interop", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Microsoft.Office", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteFileIfPresent(string? path)
    {
        if (path is not null && File.Exists(path))
        {
            File.Delete(path);
        }
    }

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

    [SupportedOSPlatform("windows")]
    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

#pragma warning disable SYSLIB1054 // Test-only COM process containment requires one stable Win32 lookup.
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
#pragma warning restore SYSLIB1054

    private sealed class LocalNormalRunner(SharedConcurrencyTracker concurrency) : IEvaluationRunner
    {
        private int calls;

        internal int CallCount => Volatile.Read(ref calls);

        public async Task<EvaluationRunnerResult> EvaluateAsync(
            SafeEvaluationPayload payload,
            string modelId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using IDisposable scope = concurrency.Enter();
            await Task.Yield();
            Interlocked.Increment(ref calls);
            Assert.Equal(LocalModelId, modelId);
            return EvaluationRunnerResult.Succeeded(
                new QuantificationResult
                {
                    EvaluatorId = payload.EvaluatorId,
                    Criteria = payload.ExpectedCriteria.Select(criterion =>
                        new CriterionQuantificationResult
                        {
                            CriterionId = criterion.CriterionId,
                            RawScore = criterion.Range.Minimum
                                + ((criterion.Range.Maximum - criterion.Range.Minimum) / 2m),
                            Reason = "LOCAL_TECHNICAL_TEST_NOT_FOR_GRADING",
                            Evidence = string.Empty,
                            EvidenceSource = EvidenceSourceKind.None,
                            EvidenceSourceColumnId = string.Empty,
                        }).ToImmutableArray(),
                });
        }
    }

    private sealed class LocalReferenceRunner(SharedConcurrencyTracker concurrency)
        : IReferenceAnswerOperationRunner
    {
        private int calls;

        internal int CallCount => Volatile.Read(ref calls);

        public async Task<AuxiliaryOperationResult<ReferenceAnswerResult>> EvaluateAsync(
            SafeReferenceAnswerPayload payload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using IDisposable scope = concurrency.Enter();
            await Task.Yield();
            Interlocked.Increment(ref calls);
            return AuxiliaryOperationResult<ReferenceAnswerResult>.Succeeded(
                new ReferenceAnswerResult
                {
                    QuestionId = payload.QuestionId,
                    Answer = "LOCAL_TECHNICAL_REFERENCE_NOT_FOR_GRADING",
                });
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
            throw new InvalidOperationException("The default zero special budget must not dispatch a special operation.");
        }
    }
    private sealed class SharedConcurrencyTracker
    {
        private int inFlight;
        private int maximumObserved;

        internal int MaximumObserved => Volatile.Read(ref maximumObserved);

        internal IDisposable Enter()
        {
            int current = Interlocked.Increment(ref inFlight);
            int maximum;
            do
            {
                maximum = Volatile.Read(ref maximumObserved);
                if (current <= maximum)
                {
                    break;
                }
            }
            while (Interlocked.CompareExchange(ref maximumObserved, current, maximum) != maximum);

            return new ExitScope(this);
        }

        private sealed class ExitScope(SharedConcurrencyTracker owner) : IDisposable
        {
            private int disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                {
                    Interlocked.Decrement(ref owner.inFlight);
                }
            }
        }
    }

    private sealed class CountingRowSource(IEvaluationRowSource inner) : IEvaluationRowSource
    {
        private int reads;

        internal int ReadCount => Volatile.Read(ref reads);

        public async Task<EvaluationRowData> ReadAsync(
            EvaluationRowRequest request,
            CancellationToken cancellationToken)
        {
            EvaluationRowData row = await inner.ReadAsync(request, cancellationToken);
            Interlocked.Increment(ref reads);
            return row;
        }
    }

    private sealed class CountingCheckpointStore(ICheckpointStore inner) : ICheckpointStore
    {
        private int creates;
        private int updateAttempts;
        private int updates;
        private int loads;
        private string lastSaveCode = "NOT_CALLED";

        internal int CreateCount => Volatile.Read(ref creates);

        internal int UpdateAttemptCount => Volatile.Read(ref updateAttempts);

        internal int UpdateCount => Volatile.Read(ref updates);

        internal int LoadCount => Volatile.Read(ref loads);

        internal string LastSaveCode => Volatile.Read(ref lastSaveCode);

        public CheckpointSaveResult Create(
            CheckpointEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            CheckpointSaveResult result = inner.Create(envelope, cancellationToken);
            Volatile.Write(ref lastSaveCode, result.Code);
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
            Interlocked.Increment(ref updateAttempts);
            CheckpointSaveResult result = inner.Update(envelope, cancellationToken);
            Volatile.Write(ref lastSaveCode, result.Code);
            if (result.IsSuccess)
            {
                Interlocked.Increment(ref updates);
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

    private sealed class TracingCheckpointFileOperations : ICheckpointFileOperations
    {
        private readonly PhysicalCheckpointFileOperations inner = new();
        private int failureCount;
        private string lastFailedOperation = "NONE";
        private string lastExceptionType = "NONE";

        internal int FailureCount => Volatile.Read(ref failureCount);

        internal string LastFailedOperation => Volatile.Read(ref lastFailedOperation);

        internal string LastExceptionType => Volatile.Read(ref lastExceptionType);

        public bool Exists(string path) => Invoke("Exists", () => inner.Exists(path));

        public void FlushToDisk(string path) =>
            Invoke("FlushToDisk", () => inner.FlushToDisk(path));

        public void MoveNoOverwrite(string sourcePath, string destinationPath) =>
            Invoke("MoveNoOverwrite", () => inner.MoveNoOverwrite(sourcePath, destinationPath));

        public void Replace(string sourcePath, string destinationPath) =>
            Invoke("Replace", () => inner.Replace(sourcePath, destinationPath));

        public void Delete(string path) => Invoke("Delete", () => inner.Delete(path));

        private T Invoke<T>(string operation, Func<T> action)
        {
            try
            {
                return action();
            }
            catch (Exception exception)
            {
                RecordFailure(operation, exception);
                throw;
            }
        }

        private void Invoke(string operation, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                RecordFailure(operation, exception);
                throw;
            }
        }

        private void RecordFailure(string operation, Exception exception)
        {
            Volatile.Write(ref lastFailedOperation, operation);
            Volatile.Write(ref lastExceptionType, exception.GetType().Name);
            Interlocked.Increment(ref failureCount);
        }
    }

    private sealed record LaunchProbeResult(
        bool ParserAccepted,
        bool PrefillLoaded,
        bool ProcessStarted,
        double StartupProbeMilliseconds,
        bool ExitedDuringProbe,
        bool ControlledCleanup,
        int OfficeRuntimeModuleCount,
        bool ResultDirectoryStateUnchanged,
        int NewCopilotProcessCount,
        bool PotentialAutomaticRunSignalObserved);

    private sealed record OutputInspection(
        int WorksheetCount,
        int AppOwnedSheetCount,
        bool CheckpointSheetPresent,
        int ConfigFormulaCount,
        int ReferenceFormulaCount,
        int ResultFormulaCount,
        int RunFormulaCount,
        int ResultDataRowCount,
        int ResultColumnCount,
        int FormulaErrorCount,
        int OpenXmlValidationErrorCount,
        int NumericFinalScoreCount,
        int BlankFinalScoreCount,
        bool CalculationModeAutomatic,
        bool FullCalculationOnLoad);

    private sealed record ExcelAutomationResult(
        bool Succeeded,
        string? Version,
        bool ProcessCleanupConfirmed);

    private sealed record ExternalRecalculationResult(
        string Status,
        string Rationale,
        string? SpreadsheetVersion,
        int FormulaCount,
        int FormulaErrorCount,
        int OpenXmlValidationErrorCount,
        bool CalculationChainPresent,
        int CalculationChainCellCount,
        IReadOnlyList<ValidationErrorCategory> ValidationErrorCategories,
        bool ProcessCleanupConfirmed);

    private sealed record ValidationErrorInfo(
        string Id,
        string ErrorType,
        string NodeLocalName,
        string PartUri);

    private sealed record ValidationErrorCategory(
        string Id,
        string ErrorType,
        string NodeLocalName,
        string PartUri,
        int Count);
}
