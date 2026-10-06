using DocumentFormat.OpenXml.Packaging;
using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Tests.Workbooks.Validation;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Validation;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Formulas;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Writing;

// Requirements: FR-029 (AC-030)
public sealed class AtomicOutputCommitterFaultTests
{
    [Fact]
    public void Real_validator_commit_moves_the_valid_package_and_dispose_does_not_delete_final()
    {
        using OutputPackageValidatorTests.ValidationFixture fixture =
            OutputPackageValidatorTests.ValidationFixture.Create();
        byte[] expectedFinalBytes = File.ReadAllBytes(fixture.Package.TemporaryPath);
        string temporaryPath = fixture.Package.TemporaryPath;

        AtomicOutputCommitResult result = new AtomicOutputCommitter().Commit(
            fixture.Package,
            fixture.Package.RequestedFinalPath,
            fixture.Input.Path,
            fixture.InputIdentity,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AtomicOutputStatusCodes.Success, result.Code);
        Assert.Equal(Path.GetFullPath(fixture.Package.RequestedFinalPath), result.FinalPath);
        Assert.False(File.Exists(temporaryPath));
        Assert.Equal(expectedFinalBytes, File.ReadAllBytes(fixture.Package.RequestedFinalPath));
        fixture.Package.Dispose();
        Assert.True(File.Exists(fixture.Package.RequestedFinalPath));
        fixture.AssertInputUnchanged();
        AssertSafe(result, fixture.Input.Path, temporaryPath, fixture.Package.RequestedFinalPath);
    }

    [Fact]
    public void Validation_failure_returns_OUTPUT_INVALID_cleans_temp_and_creates_no_final()
    {
        using CommitFixture fixture = CommitFixture.Create();
        const string privateCanary = "PRIVATE-VALIDATION-CONTENT-CANARY";
        OutputPackageValidationError validationError = new(
            "FORMULA_SERIALIZATION_MISMATCH",
            new FormulaIdentity("Criterion", "C1", privateCanary, "Normalized"),
            "mismatch",
            "exact");
        DelegateValidator validator = new((_, _, _) => new OutputPackageValidationResult([validationError]));

        AtomicOutputCommitResult result = fixture.Commit(
            new AtomicOutputCommitter(validator, new TestFileOperations()));

        Assert.Equal(AtomicOutputStatusCodes.OutputInvalid, result.Code);
        Assert.Equal(validationError, Assert.Single(result.ValidationErrors));
        Assert.False(File.Exists(fixture.TemporaryPath));
        Assert.False(File.Exists(fixture.FinalPath));
        fixture.AssertInputUnchanged();
        AssertSafe(result, fixture.Input.Path, fixture.TemporaryPath, fixture.FinalPath, privateCanary);
    }

    [Fact]
    public void Input_hash_drift_with_same_size_and_restored_mtime_prevents_rename()
    {
        using CommitFixture fixture = CommitFixture.Create();
        DelegateValidator validator = new((_, _, _) =>
        {
            byte[] bytes = File.ReadAllBytes(fixture.Input.Path);
            bytes[^1] ^= 0x01;
            File.WriteAllBytes(fixture.Input.Path, bytes);
            File.SetLastWriteTimeUtc(fixture.Input.Path, fixture.InputIdentity.LastWriteTimeUtc.UtcDateTime);
            return OutputPackageValidationResult.Valid;
        });

        AtomicOutputCommitResult result = fixture.Commit(
            new AtomicOutputCommitter(validator, new TestFileOperations()));

        Assert.Equal(AtomicOutputStatusCodes.InputChanged, result.Code);
        Assert.False(File.Exists(fixture.TemporaryPath));
        Assert.False(File.Exists(fixture.FinalPath));
    }

    [Fact]
    public void Input_exact_mtime_drift_alone_prevents_rename_without_tolerance()
    {
        using CommitFixture fixture = CommitFixture.Create();
        DelegateValidator validator = new((_, _, _) =>
        {
            File.SetLastWriteTimeUtc(
                fixture.Input.Path,
                fixture.InputIdentity.LastWriteTimeUtc.UtcDateTime.AddSeconds(2));
            return OutputPackageValidationResult.Valid;
        });

        AtomicOutputCommitResult result = fixture.Commit(
            new AtomicOutputCommitter(validator, new TestFileOperations()));

        Assert.Equal(AtomicOutputStatusCodes.InputChanged, result.Code);
        Assert.False(File.Exists(fixture.TemporaryPath));
        Assert.False(File.Exists(fixture.FinalPath));
    }

    [Fact]
    public void Cancellation_before_commit_is_observed_before_validation_and_cleans_temp()
    {
        using CommitFixture fixture = CommitFixture.Create();
        DelegateValidator validator = new((_, _, _) => OutputPackageValidationResult.Valid);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        AtomicOutputCommitResult result = fixture.CommitWithCancellation(
            new AtomicOutputCommitter(validator, new TestFileOperations()),
            cancellation.Token);

        Assert.Equal(AtomicOutputStatusCodes.Cancelled, result.Code);
        Assert.Equal(0, validator.CallCount);
        Assert.False(File.Exists(fixture.TemporaryPath));
        Assert.False(File.Exists(fixture.FinalPath));
        fixture.AssertInputUnchanged();
    }

    [Fact]
    public void Cancellation_observed_after_validation_still_creates_no_final()
    {
        using CommitFixture fixture = CommitFixture.Create();
        using CancellationTokenSource cancellation = new();
        DelegateValidator validator = new((_, _, _) =>
        {
            cancellation.Cancel();
            return OutputPackageValidationResult.Valid;
        });

        AtomicOutputCommitResult result = fixture.CommitWithCancellation(
            new AtomicOutputCommitter(validator, new TestFileOperations()),
            cancellation.Token);

        Assert.Equal(AtomicOutputStatusCodes.Cancelled, result.Code);
        Assert.False(File.Exists(fixture.TemporaryPath));
        Assert.False(File.Exists(fixture.FinalPath));
        fixture.AssertInputUnchanged();
    }

    [Fact]
    public void Cancellation_during_critical_rename_is_deferred_until_valid_final_exists()
    {
        using CommitFixture fixture = CommitFixture.Create();
        using CancellationTokenSource cancellation = new();
        TestFileOperations operations = new();
        operations.MoveOverride = (source, destination) =>
        {
            cancellation.Cancel();
            operations.MovePhysical(source, destination);
        };

        AtomicOutputCommitResult result = fixture.CommitWithCancellation(
            new AtomicOutputCommitter(AlwaysValid(), operations),
            cancellation.Token);

        Assert.Equal(AtomicOutputStatusCodes.Success, result.Code);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(File.Exists(fixture.FinalPath));
        Assert.False(File.Exists(fixture.TemporaryPath));
        fixture.AssertInputUnchanged();
    }

    [Fact]
    public void Existing_target_is_never_overwritten_and_temp_is_removed()
    {
        using CommitFixture fixture = CommitFixture.Create();
        byte[] sentinel = "PRIVATE-EXISTING-TARGET-SENTINEL"u8.ToArray();
        File.WriteAllBytes(fixture.FinalPath, sentinel);

        AtomicOutputCommitResult result = fixture.Commit(
            new AtomicOutputCommitter(AlwaysValid(), new TestFileOperations()));

        Assert.Equal(AtomicOutputStatusCodes.TargetExists, result.Code);
        Assert.Equal(sentinel, File.ReadAllBytes(fixture.FinalPath));
        Assert.False(File.Exists(fixture.TemporaryPath));
        fixture.AssertInputUnchanged();
        AssertSafe(result, fixture.Input.Path, fixture.TemporaryPath, fixture.FinalPath, "PRIVATE-EXISTING-TARGET-SENTINEL");
    }

    [Fact]
    public void Rename_fault_returns_COMMIT_FAILED_with_no_completed_final()
    {
        using CommitFixture fixture = CommitFixture.Create();
        TestFileOperations operations = new()
        {
            MoveOverride = (_, _) => throw new IOException("PRIVATE-RENAME-FAULT-CANARY"),
        };

        AtomicOutputCommitResult result = fixture.Commit(
            new AtomicOutputCommitter(AlwaysValid(), operations));

        Assert.Equal(AtomicOutputStatusCodes.CommitFailed, result.Code);
        Assert.False(File.Exists(fixture.TemporaryPath));
        Assert.False(File.Exists(fixture.FinalPath));
        fixture.AssertInputUnchanged();
        AssertSafe(result, fixture.Input.Path, fixture.TemporaryPath, fixture.FinalPath, "PRIVATE-RENAME-FAULT-CANARY");
    }

    [Fact]
    public void Process_interruption_seam_after_atomic_move_is_recognized_as_valid_final()
    {
        using CommitFixture fixture = CommitFixture.Create();
        TestFileOperations operations = new();
        operations.MoveOverride = (source, destination) =>
        {
            operations.MovePhysical(source, destination);
            throw new IOException("PRIVATE-AFTER-MOVE-INTERRUPTION-CANARY");
        };

        AtomicOutputCommitResult result = fixture.Commit(
            new AtomicOutputCommitter(AlwaysValid(), operations));

        Assert.Equal(AtomicOutputStatusCodes.Success, result.Code);
        Assert.True(File.Exists(fixture.FinalPath));
        Assert.False(File.Exists(fixture.TemporaryPath));
        fixture.AssertInputUnchanged();
        AssertSafe(result, "PRIVATE-AFTER-MOVE-INTERRUPTION-CANARY");
    }

    [Fact]
    public void Move_seam_returning_without_state_transition_cannot_false_green()
    {
        using CommitFixture fixture = CommitFixture.Create();
        TestFileOperations operations = new()
        {
            MoveOverride = (_, _) => { },
        };

        AtomicOutputCommitResult result = fixture.Commit(
            new AtomicOutputCommitter(AlwaysValid(), operations));

        Assert.Equal(AtomicOutputStatusCodes.CommitFailed, result.Code);
        Assert.False(File.Exists(fixture.TemporaryPath));
        Assert.False(File.Exists(fixture.FinalPath));
        fixture.AssertInputUnchanged();
    }

    [Theory]
    [InlineData("PRIVATE-DISK-FULL-CANARY", false)]
    [InlineData("PRIVATE-WRITE-DENIED-CANARY", true)]
    public void Flush_write_faults_return_COMMIT_FAILED_and_never_validate_or_rename(
        string privateCanary,
        bool unauthorized)
    {
        using CommitFixture fixture = CommitFixture.Create();
        DelegateValidator validator = AlwaysValid();
        TestFileOperations operations = new()
        {
            FlushOverride = _ =>
            {
                if (unauthorized)
                {
                    throw new UnauthorizedAccessException(privateCanary);
                }

                throw new IOException(privateCanary);
            },
        };

        AtomicOutputCommitResult result = fixture.Commit(
            new AtomicOutputCommitter(validator, operations));

        Assert.Equal(AtomicOutputStatusCodes.CommitFailed, result.Code);
        Assert.Equal(0, validator.CallCount);
        Assert.Equal(0, operations.MoveCount);
        Assert.False(File.Exists(fixture.TemporaryPath));
        Assert.False(File.Exists(fixture.FinalPath));
        fixture.AssertInputUnchanged();
        AssertSafe(result, privateCanary);
    }

    [Fact]
    public void Locked_temp_surfaces_cleanup_failure_instead_of_hiding_it()
    {
        using CommitFixture fixture = CommitFixture.Create();
        AtomicOutputCommitResult result;
        using (FileStream locked = new(
            fixture.TemporaryPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        {
            result = fixture.Commit(
                new AtomicOutputCommitter(AlwaysValid(), new TestFileOperations()));
            Assert.True(File.Exists(fixture.TemporaryPath));
        }

        Assert.Equal(AtomicOutputStatusCodes.CleanupFailed, result.Code);
        Assert.Equal(AtomicOutputStatusCodes.CommitFailed, result.CauseCode);
        Assert.False(File.Exists(fixture.FinalPath));
        fixture.AssertInputUnchanged();
    }

    [Fact]
    public void Cleanup_fault_is_surfaced_with_the_original_failure_cause()
    {
        using CommitFixture fixture = CommitFixture.Create();
        TestFileOperations operations = new()
        {
            DeleteOverride = _ => throw new IOException("PRIVATE-CLEANUP-FAULT-CANARY"),
        };
        OutputPackageValidationError error = new(
            "SYNTHETIC_INVALID",
            new FormulaIdentity("Workbook", "<workbook>", "<redacted>", "Package"),
            "invalid",
            "valid");
        DelegateValidator validator = new((_, _, _) => new OutputPackageValidationResult([error]));

        AtomicOutputCommitResult result = fixture.Commit(
            new AtomicOutputCommitter(validator, operations));

        Assert.Equal(AtomicOutputStatusCodes.CleanupFailed, result.Code);
        Assert.Equal(AtomicOutputStatusCodes.OutputInvalid, result.CauseCode);
        Assert.Equal(error, Assert.Single(result.ValidationErrors));
        Assert.True(File.Exists(fixture.TemporaryPath));
        Assert.False(File.Exists(fixture.FinalPath));
        fixture.AssertInputUnchanged();
        AssertSafe(result, "PRIVATE-CLEANUP-FAULT-CANARY");
    }

    [Fact]
    public void Validator_cancellation_without_requested_cancellation_is_not_mapped_to_CANCELLED()
    {
        using CommitFixture fixture = CommitFixture.Create();
        DelegateValidator validator = new((_, _, _) => throw new OperationCanceledException());

        AtomicOutputCommitResult result = fixture.Commit(
            new AtomicOutputCommitter(validator, new TestFileOperations()));

        Assert.Equal(AtomicOutputStatusCodes.OutputInvalid, result.Code);
        Assert.False(File.Exists(fixture.FinalPath));
        Assert.False(File.Exists(fixture.TemporaryPath));
        fixture.AssertInputUnchanged();
    }

    [Fact]
    public void Requested_paths_must_match_the_WorkingPackage_and_remain_target_local()
    {
        using CommitFixture fixture = CommitFixture.Create();
        string differentDirectory = Path.Combine(fixture.Input.Directory, "different-target");
        Directory.CreateDirectory(differentDirectory);
        string mismatchedFinal = Path.Combine(differentDirectory, "other.xlsx");

        AtomicOutputCommitResult result = new AtomicOutputCommitter(
            AlwaysValid(),
            new TestFileOperations()).Commit(
                fixture.Package,
                mismatchedFinal,
                fixture.Input.Path,
                fixture.InputIdentity,
                fixture.Plan,
                TestContext.Current.CancellationToken);

        Assert.Equal(AtomicOutputStatusCodes.CommitFailed, result.Code);
        Assert.False(File.Exists(fixture.TemporaryPath));
        Assert.False(File.Exists(fixture.FinalPath));
        Assert.False(File.Exists(mismatchedFinal));
        fixture.AssertInputUnchanged();
        AssertSafe(result, fixture.Input.Path, fixture.TemporaryPath, fixture.FinalPath, mismatchedFinal);
    }

    private static DelegateValidator AlwaysValid() =>
        new((_, _, _) => OutputPackageValidationResult.Valid);

    private static void AssertSafe(AtomicOutputCommitResult result, params string[] forbidden)
    {
        string text = result.ToString();
        Assert.Contains("<redacted>", text, StringComparison.Ordinal);
        foreach (string value in forbidden)
        {
            Assert.DoesNotContain(value, text, StringComparison.Ordinal);
        }
    }

    private sealed class CommitFixture : IDisposable
    {
        private readonly InputSnapshotService snapshots = new();

        private CommitFixture(
            TemporaryWorkbook input,
            InputSnapshot inputIdentity,
            WorkingPackage package,
            OutputPackageValidationPlan plan)
        {
            Input = input;
            InputIdentity = inputIdentity;
            Package = package;
            Plan = plan;
        }

        public TemporaryWorkbook Input { get; }

        public InputSnapshot InputIdentity { get; }

        public WorkingPackage Package { get; }

        public OutputPackageValidationPlan Plan { get; }

        public string TemporaryPath => Package.TemporaryPath;

        public string FinalPath => Package.RequestedFinalPath;

        public static CommitFixture Create()
        {
            TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
            InputSnapshotService snapshots = new();
            InputSnapshot identity = snapshots.Capture(input.Path);
            AppOwnedSheetNames names;
            using (SpreadsheetDocument document = SpreadsheetDocument.Open(input.Path, false))
            {
                names = new AppOwnedSheetNameResolver().Resolve(document);
            }

            OutputPackageValidationPlan plan = OutputPackageValidationPlan.Capture(
                input.Path,
                names,
                []);
            string targetDirectory = Path.Combine(input.Directory, "target");
            Directory.CreateDirectory(targetDirectory);
            WorkingPackage package = WorkingPackage.Create(
                input.Path,
                Path.Combine(targetDirectory, "quantified.xlsx"));
            return new CommitFixture(input, identity, package, plan);
        }

        public AtomicOutputCommitResult Commit(AtomicOutputCommitter committer) =>
            committer.Commit(
                Package,
                FinalPath,
                Input.Path,
                InputIdentity,
                Plan,
                TestContext.Current.CancellationToken);

        public AtomicOutputCommitResult CommitWithCancellation(
            AtomicOutputCommitter committer,
            CancellationToken cancellationToken) =>
            committer.Commit(
                Package,
                FinalPath,
                Input.Path,
                InputIdentity,
                Plan,
                cancellationToken);

        public void AssertInputUnchanged() =>
            Assert.True(snapshots.Recheck(Input.Path, InputIdentity).IsMatch);

        public void Dispose()
        {
            Package.Dispose();
            Input.Dispose();
        }
    }

    private sealed class DelegateValidator(
        Func<string, OutputPackageValidationPlan, CancellationToken, OutputPackageValidationResult> handler)
        : IOutputPackageValidator
    {
        public int CallCount { get; private set; }

        public OutputPackageValidationResult Validate(
            string workingPackagePath,
            OutputPackageValidationPlan plan,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return handler(workingPackagePath, plan, cancellationToken);
        }
    }

    private sealed class TestFileOperations : IAtomicOutputFileOperations
    {
        private readonly PhysicalAtomicOutputFileOperations physical = new();

        public Func<string, bool>? ExistsOverride { get; init; }

        public Action<string>? FlushOverride { get; init; }

        public Action<string, string>? MoveOverride { get; set; }

        public Action<string>? DeleteOverride { get; init; }

        public int MoveCount { get; private set; }

        public bool Exists(string path) => ExistsOverride?.Invoke(path) ?? physical.Exists(path);

        public void FlushToDisk(string path)
        {
            if (FlushOverride is not null)
            {
                FlushOverride(path);
                return;
            }

            physical.FlushToDisk(path);
        }

        public void MoveNoOverwrite(string sourcePath, string destinationPath)
        {
            MoveCount++;
            if (MoveOverride is not null)
            {
                MoveOverride(sourcePath, destinationPath);
                return;
            }

            physical.MoveNoOverwrite(sourcePath, destinationPath);
        }

        public void Delete(string path)
        {
            if (DeleteOverride is not null)
            {
                DeleteOverride(path);
                return;
            }

            physical.Delete(path);
        }

        public void MovePhysical(string sourcePath, string destinationPath) =>
            physical.MoveNoOverwrite(sourcePath, destinationPath);
    }
}