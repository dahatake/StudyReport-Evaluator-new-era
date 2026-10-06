using System.Collections.Immutable;
using System.Net.Http;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Validation;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Formulas;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Validation;
using Xunit;

namespace StudyReportEvaluator.App.Tests.E2E;

// Requirements: FR-002 (AC-002), FR-007 (AC-008)
public sealed class HostileInputAndFailureTests
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(1);

    [Fact]
    public void Corrupt_zip_payload_is_rejected_without_content_disclosure()
    {
        using TemporaryWorkbook file = X01SyntheticWorkbookFactory.CreateRaw(
            ".xlsx",
            [0x50, 0x4B, 0x03, 0x04, 0x45, 0x30, 0x32]);

        FileFormatClassificationResult result = new FileFormatClassifier().Classify(file.Path);

        Assert.Equal(FileFormatClassification.CorruptPackage, result.Classification);
        AssertRedacted(result.ToString());
    }

    [Fact]
    public void Truncated_valid_zip_is_rejected_as_one_corruption_cause()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        byte[] bytes = File.ReadAllBytes(workbook.Path);
        File.WriteAllBytes(workbook.Path, bytes[..^22]);

        FileFormatClassificationResult result = new FileFormatClassifier().Classify(workbook.Path);

        Assert.Equal(FileFormatClassification.CorruptPackage, result.Classification);
        AssertRedacted(result.ToString());
    }

    [Fact]
    public void Legacy_CFB_workbook_extension_is_rejected_before_content_processing()
    {
        using TemporaryWorkbook file = X01SyntheticWorkbookFactory.CreateRaw(
            ".xls",
            CompoundFileBytes());

        FileFormatClassificationResult result = new FileFormatClassifier().Classify(file.Path);

        Assert.Equal(FileFormatClassification.LegacyBinaryWorkbook, result.Classification);
        AssertRedacted(result.ToString());
    }

    [Fact]
    public void Protected_CFB_container_masquerading_as_xlsx_is_rejected_fail_closed()
    {
        using TemporaryWorkbook file = X01SyntheticWorkbookFactory.CreateRaw(
            ".xlsx",
            CompoundFileBytes());

        FileFormatClassificationResult result = new FileFormatClassifier().Classify(file.Path);

        Assert.Equal(
            FileFormatClassification.EncryptedOrRightsProtected,
            result.Classification);
        AssertRedacted(result.ToString());
    }

    [Fact]
    public void Macro_enabled_package_masquerading_as_xlsx_is_rejected()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create(
            documentType: SpreadsheetDocumentType.MacroEnabledWorkbook);

        FileFormatClassificationResult result = new FileFormatClassifier().Classify(workbook.Path);

        Assert.Equal(FileFormatClassification.MacroEnabledWorkbook, result.Classification);
        AssertRedacted(result.ToString());
    }

    [Fact]
    public void Unsafe_high_expansion_ratio_zip_is_rejected_before_package_processing()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.CreateZipBombLike();

        FileFormatClassificationResult result = new FileFormatClassifier().Classify(workbook.Path);

        Assert.Equal(FileFormatClassification.UnsafePackage, result.Classification);
        AssertRedacted(result.ToString());
    }

    [Fact]
    public void Formula_markers_remain_literal_strings_without_formula_or_value_nodes()
    {
        string[] hostileValues =
        [
            "=E02-FORMULA-CANARY",
            "+E02-FORMULA-CANARY",
            "-E02-FORMULA-CANARY",
            "@E02-FORMULA-CANARY",
        ];
        UntrustedStringCellWriter writer = new();

        foreach (string hostileValue in hostileValues)
        {
            Cell cell = writer.CreateCell("A1", hostileValue);
            Assert.True(cell.DataType?.Value == CellValues.InlineString);
            Assert.Equal(hostileValue, cell.InlineString?.Text?.Text);
            Assert.Null(cell.CellFormula);
            Assert.Null(cell.CellValue);
            Assert.False(writer.ToString().Contains(hostileValue, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Invalid_AI_score_rejects_the_whole_result_without_echoing_bodies()
    {
        SafeEvaluationPayload payload = CreatePayload();
        QuantificationResult submitted = CreateResult(
            rawScore: 11m,
            evidence: "PRIMARY-EVIDENCE-CANARY");

        QuantificationResultValidationOutcome outcome =
            new QuantificationResultValidator().Validate(payload, submitted);

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.AcceptedResult);
        Assert.Equal(["RAW_SCORE_OUT_OF_RANGE"], outcome.Errors.Select(error => error.Code));
        AssertNoContent(
            string.Join('|', outcome.Errors),
            payload.PrimarySource.Value,
            submitted.Criteria[0].Reason,
            submitted.Criteria[0].Evidence);
    }

    [Fact]
    public void Evidence_not_in_the_claimed_same_row_source_is_rejected_without_echoing_content()
    {
        SafeEvaluationPayload payload = CreatePayload();
        QuantificationResult submitted = CreateResult(
            rawScore: 5m,
            evidence: "OTHER-ROW-EVIDENCE-CANARY");

        QuantificationResultValidationOutcome outcome =
            new QuantificationResultValidator().Validate(payload, submitted);

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.AcceptedResult);
        Assert.Equal(["EVIDENCE_NOT_EXACT_SUBSTRING"], outcome.Errors.Select(error => error.Code));
        AssertNoContent(
            string.Join('|', outcome.Errors),
            payload.PrimarySource.Value,
            submitted.Criteria[0].Reason,
            submitted.Criteria[0].Evidence);
    }

    [Fact]
    public async Task Invalid_AI_payload_retries_once_then_returns_AI_OUTPUT_INVALID_without_a_score()
    {
        List<ProbeAttempt> attempts = [];
        EphemeralEvaluationResult result = await new RetryAndCleanupCoordinator().ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                static _ => Task.FromException<QuantificationResult>(
                    new EvaluationSchemaException())),
            TimeSpan.FromSeconds(1),
            CleanupTimeout,
            maxConcurrency: 1,
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.AiOutputInvalid, result.Status);
        Assert.Equal("AI_OUTPUT_INVALID", result.StatusCode);
        Assert.Equal(RetryAndCleanupCoordinator.MaximumSchemaAttempts, result.AttemptCount);
        Assert.Null(result.AcceptedResult);
        Assert.All(attempts, attempt =>
            Assert.Equal(["execute", "dispose", "delete"], attempt.Operations));
        AssertRedacted(result.ToString());
    }

    [Fact]
    public async Task App_owned_timeout_returns_AI_TIMEOUT_and_cleans_each_attempt_without_hanging()
    {
        List<ProbeAttempt> attempts = [];
        TaskCompletionSource<QuantificationResult> never = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EphemeralEvaluationResult result = await new RetryAndCleanupCoordinator().ExecuteAsync(
            attemptNumber => AddAttempt(attempts, attemptNumber, _ => never.Task),
            TimeSpan.FromMilliseconds(20),
            CleanupTimeout,
            maxConcurrency: 1,
            TestContext.Current.CancellationToken).WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
        never.TrySetCanceled(TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.AiTimeout, result.Status);
        Assert.Equal("AI_TIMEOUT", result.StatusCode);
        Assert.Equal(RetryAndCleanupCoordinator.MaximumTransientAttempts, result.AttemptCount);
        Assert.Null(result.AcceptedResult);
        Assert.All(attempts, attempt =>
            Assert.Equal(["execute", "abort", "dispose", "delete"], attempt.Operations));
        AssertRedacted(result.ToString());
    }

    [Fact]
    public async Task Network_failure_returns_NETWORK_FAILED_without_transport_content()
    {
        const string networkCanary = "PRIVATE-NETWORK-CONTENT-CANARY";
        List<ProbeAttempt> attempts = [];

        EphemeralEvaluationResult result = await new RetryAndCleanupCoordinator().ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                _ => Task.FromException<QuantificationResult>(
                    new HttpRequestException(networkCanary))),
            TimeSpan.FromSeconds(1),
            CleanupTimeout,
            maxConcurrency: 1,
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.NetworkFailed, result.Status);
        Assert.Equal("NETWORK_FAILED", result.StatusCode);
        Assert.Equal(RetryAndCleanupCoordinator.MaximumTransientAttempts, result.AttemptCount);
        Assert.Null(result.AcceptedResult);
        AssertNoContent(result.ToString(), networkCanary);
        Assert.All(attempts, attempt =>
            Assert.Equal(["execute", "abort", "dispose", "delete"], attempt.Operations));
    }

    [Fact]
    public async Task Authentication_failure_returns_AUTH_REQUIRED_without_retry_or_session_content()
    {
        List<ProbeAttempt> attempts = [];

        EphemeralEvaluationResult result = await new RetryAndCleanupCoordinator().ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                static _ => Task.FromException<QuantificationResult>(
                    new EvaluationAuthenticationException())),
            TimeSpan.FromSeconds(1),
            CleanupTimeout,
            maxConcurrency: 1,
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.AuthRequired, result.Status);
        Assert.Equal("AUTH_REQUIRED", result.StatusCode);
        Assert.Equal(1, result.AttemptCount);
        Assert.Null(result.AcceptedResult);
        ProbeAttempt attempt = Assert.Single(attempts);
        Assert.Equal(["execute", "dispose", "delete"], attempt.Operations);
        AssertRedacted(result.ToString());
    }

    [Fact]
    public async Task Cancellation_before_start_returns_CANCELLED_without_creating_an_attempt()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        int factoryCalls = 0;

        EphemeralEvaluationResult result = await new RetryAndCleanupCoordinator().ExecuteAsync(
            attemptNumber =>
            {
                factoryCalls++;
                return new ProbeAttempt(
                    "unreachable-" + attemptNumber.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    static _ => Task.FromResult(ValidResult()));
            },
            TimeSpan.FromSeconds(1),
            CleanupTimeout,
            maxConcurrency: 1,
            cancellation.Token);

        Assert.Equal(EphemeralEvaluationStatus.Cancelled, result.Status);
        Assert.Equal("CANCELLED", result.StatusCode);
        Assert.Equal(0, result.AttemptCount);
        Assert.Equal(0, factoryCalls);
        Assert.Null(result.AcceptedResult);
        AssertRedacted(result.ToString());
    }

    [Fact]
    public async Task Cancellation_aborts_and_cleans_the_only_attempt_without_starting_another()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<ProbeAttempt> attempts = [];
        using CancellationTokenSource cancellation = new();
        Task<EphemeralEvaluationResult> execution =
            new RetryAndCleanupCoordinator().ExecuteAsync(
                attemptNumber => AddAttempt(
                    attempts,
                    attemptNumber,
                    async token =>
                    {
                        started.TrySetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                        return ValidResult();
                    }),
                TimeSpan.FromSeconds(1),
                CleanupTimeout,
                maxConcurrency: 1,
                cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        EphemeralEvaluationResult result = await execution.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.Cancelled, result.Status);
        Assert.Equal("CANCELLED", result.StatusCode);
        Assert.Equal(1, result.AttemptCount);
        Assert.Null(result.AcceptedResult);
        ProbeAttempt attempt = Assert.Single(attempts);
        Assert.Equal(["execute", "abort", "dispose", "delete"], attempt.Operations);
        AssertRedacted(result.ToString());
    }

    [Fact]
    public void Input_hash_change_with_same_size_and_exact_mtime_returns_INPUT_CHANGED_with_no_final()
    {
        using AtomicFixture fixture = AtomicFixture.Create();
        DelegateValidator validator = new((_, _, _) =>
        {
            MutateOneByteAndRestoreMtime(fixture.Input.Path, fixture.InputSnapshot);
            return OutputPackageValidationResult.Valid;
        });

        AtomicOutputCommitResult result = fixture.Commit(validator);
        InputSnapshot changed = new InputSnapshotService().Capture(fixture.Input.Path);

        Assert.Equal(AtomicOutputStatusCodes.InputChanged, result.Code);
        Assert.Equal(fixture.InputSnapshot.SizeBytes, changed.SizeBytes);
        Assert.Equal(
            fixture.InputSnapshot.LastWriteTimeUtc.UtcTicks,
            changed.LastWriteTimeUtc.UtcTicks);
        Assert.NotEqual(fixture.InputSnapshot.Sha256, changed.Sha256);
        Assert.False(File.Exists(fixture.TemporaryPath));
        Assert.False(File.Exists(fixture.FinalPath));
        AssertRedacted(result.ToString());
    }

    [Fact]
    public void Output_validation_failure_returns_OUTPUT_INVALID_and_removes_every_output_file()
    {
        using AtomicFixture fixture = AtomicFixture.Create();
        OutputPackageValidationError error = new(
            "SYNTHETIC_OUTPUT_INVALID",
            new FormulaIdentity("Workbook", "<workbook>", "<redacted>", "Package"),
            "invalid",
            "valid");
        DelegateValidator validator = new(
            (_, _, _) => new OutputPackageValidationResult([error]));

        AtomicOutputCommitResult result = fixture.Commit(validator);

        Assert.Equal(AtomicOutputStatusCodes.OutputInvalid, result.Code);
        Assert.Equal(error, Assert.Single(result.ValidationErrors));
        Assert.False(File.Exists(fixture.TemporaryPath));
        Assert.False(File.Exists(fixture.FinalPath));
        Assert.True(new InputSnapshotService()
            .Recheck(fixture.Input.Path, fixture.InputSnapshot)
            .IsMatch);
        AssertRedacted(result.ToString());
        AssertRedacted(error.ToString());
    }

    private static SafeEvaluationPayload CreatePayload() => new(
        "Q-E02",
        "E-E02",
        "<redacted-test-prompt>",
        new EvaluationSourceCell(
            EvaluationSourceKind.PrimaryAnswer,
            "G",
            "PRIMARY-EVIDENCE-CANARY IN PRIVATE SOURCE BODY"),
        [
            new EvaluationSourceCell(
                EvaluationSourceKind.SupportingColumn,
                "K",
                "SUPPORTING-EVIDENCE-CANARY IN PRIVATE SOURCE BODY"),
        ],
        [new ExpectedCriterion("C-E02", "Criterion", new ScoreRange(0m, 10m))]);

    private static QuantificationResult CreateResult(decimal rawScore, string evidence) => new()
    {
        EvaluatorId = "E-E02",
        Criteria =
        [
            new CriterionQuantificationResult
            {
                CriterionId = "C-E02",
                RawScore = rawScore,
                Reason = "PRIVATE-REASON-CANARY",
                Evidence = evidence,
                EvidenceSource = EvidenceSourceKind.PrimaryAnswer,
                EvidenceSourceColumnId = "G",
            },
        ],
    };

    private static QuantificationResult ValidResult() => CreateResult(
        rawScore: 5m,
        evidence: "PRIMARY-EVIDENCE-CANARY");

    private static ProbeAttempt AddAttempt(
        ICollection<ProbeAttempt> attempts,
        int attemptNumber,
        Func<CancellationToken, Task<QuantificationResult>> execute)
    {
        ProbeAttempt attempt = new(
            "e02-session-" + attemptNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            execute);
        attempts.Add(attempt);
        return attempt;
    }

    private static byte[] CompoundFileBytes() =>
    [
        0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1,
        0x00, 0x00, 0x00, 0x00,
    ];

    private static void MutateOneByteAndRestoreMtime(
        string inputPath,
        InputSnapshot expected)
    {
        using (FileStream stream = new(
                   inputPath,
                   FileMode.Open,
                   FileAccess.ReadWrite,
                   FileShare.None))
        {
            stream.Position = stream.Length - 1;
            int original = stream.ReadByte();
            if (original < 0)
            {
                throw new InvalidDataException("The synthetic input unexpectedly had no bytes.");
            }

            stream.Position--;
            stream.WriteByte((byte)(original ^ 0x01));
            stream.Flush(flushToDisk: true);
        }

        File.SetLastWriteTimeUtc(inputPath, expected.LastWriteTimeUtc.UtcDateTime);
    }

    private static void AssertRedacted(string representation)
    {
        Assert.Contains("<redacted>", representation, StringComparison.Ordinal);
        AssertNoContent(
            representation,
            "PRIMARY-EVIDENCE-CANARY",
            "SUPPORTING-EVIDENCE-CANARY",
            "PRIVATE-REASON-CANARY",
            "E02-FORMULA-CANARY");
    }

    private static void AssertNoContent(string representation, params string[] forbiddenValues)
    {
        foreach (string forbiddenValue in forbiddenValues)
        {
            Assert.False(representation.Contains(forbiddenValue, StringComparison.Ordinal));
        }
    }

    private sealed class ProbeAttempt(
        string sessionId,
        Func<CancellationToken, Task<QuantificationResult>> execute)
        : IEphemeralEvaluationAttempt
    {
        public string SessionId { get; } = sessionId;

        public List<string> Operations { get; } = [];

        public Task<QuantificationResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            Operations.Add("execute");
            return execute(cancellationToken);
        }

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            Operations.Add("abort");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task DisposeSessionAsync(CancellationToken cancellationToken)
        {
            Operations.Add("dispose");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task DeleteSessionAsync(CancellationToken cancellationToken)
        {
            Operations.Add("delete");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class DelegateValidator(
        Func<string, OutputPackageValidationPlan, CancellationToken, OutputPackageValidationResult> validate)
        : IOutputPackageValidator
    {
        public OutputPackageValidationResult Validate(
            string workingPackagePath,
            OutputPackageValidationPlan plan,
            CancellationToken cancellationToken = default) =>
            validate(workingPackagePath, plan, cancellationToken);
    }

    private sealed class AtomicFixture : IDisposable
    {
        private AtomicFixture(
            TemporaryWorkbook input,
            InputSnapshot inputSnapshot,
            WorkingPackage package,
            OutputPackageValidationPlan validationPlan)
        {
            Input = input;
            InputSnapshot = inputSnapshot;
            Package = package;
            ValidationPlan = validationPlan;
        }

        public TemporaryWorkbook Input { get; }

        public InputSnapshot InputSnapshot { get; }

        public WorkingPackage Package { get; }

        public OutputPackageValidationPlan ValidationPlan { get; }

        public string TemporaryPath => Package.TemporaryPath;

        public string FinalPath => Package.RequestedFinalPath;

        public static AtomicFixture Create()
        {
            TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
            WorkingPackage? package = null;
            try
            {
                InputSnapshot snapshot = new InputSnapshotService().Capture(input.Path);
                string targetDirectory = Path.Combine(input.Directory, "target");
                Directory.CreateDirectory(targetDirectory);
                package = WorkingPackage.Create(
                    input.Path,
                    Path.Combine(targetDirectory, "result.xlsx"));
                AppOwnedSheetNames names;
                using (SpreadsheetDocument document = SpreadsheetDocument.Open(input.Path, false))
                {
                    names = new AppOwnedSheetNameResolver().Resolve(document);
                }

                OutputPackageValidationPlan plan = OutputPackageValidationPlan.Capture(
                    input.Path,
                    names,
                    []);
                return new AtomicFixture(input, snapshot, package, plan);
            }
            catch
            {
                package?.Dispose();
                input.Dispose();
                throw;
            }
        }

        public AtomicOutputCommitResult Commit(IOutputPackageValidator validator) =>
            new AtomicOutputCommitter(
                validator,
                new PhysicalAtomicOutputFileOperations()).Commit(
                    Package,
                    FinalPath,
                    Input.Path,
                    InputSnapshot,
                    ValidationPlan,
                    TestContext.Current.CancellationToken);

        public void Dispose()
        {
            Package.Dispose();
            Input.Dispose();
        }
    }
}
