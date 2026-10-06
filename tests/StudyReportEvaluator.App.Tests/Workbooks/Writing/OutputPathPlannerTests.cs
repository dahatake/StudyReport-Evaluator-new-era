using DocumentFormat.OpenXml.Packaging;
using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Validation;
using StudyReportEvaluator.App.Workbooks.Writing;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Writing;

// Requirements: FR-027 (AC-028)
public sealed class OutputPathPlannerTests
{
    private static readonly DateTimeOffset LocalMinute =
        new(2026, 9, 2, 14, 35, 59, TimeSpan.FromHours(9));

    [Fact]
    public void Default_reservation_creates_result_directory_and_uses_local_minute_without_creating_files()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();

        OutputPathReservation reservation = new OutputPathPlanner().Reserve(input.Path, LocalMinute);

        string expectedDirectory = Path.Combine(input.Directory, "result");
        Assert.Equal(Path.GetFullPath(expectedDirectory), reservation.OutputDirectory);
        Assert.Equal(
            Path.Combine(expectedDirectory, "eval-20260902-1435.xlsx"),
            reservation.FinalPath);
        Assert.Equal(
            Path.Combine(expectedDirectory, "eval-20260902-1435.partial.xlsx"),
            reservation.PartialPath);
        Assert.True(Directory.Exists(expectedDirectory));
        Assert.False(File.Exists(reservation.FinalPath));
        Assert.False(File.Exists(reservation.PartialPath));
        Assert.Contains("<redacted>", reservation.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(input.Path, reservation.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Final_or_partial_collision_allocates_the_minimum_shared_suffix_and_treats_directories_as_occupied()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        OutputPathPlanner planner = new();
        OutputPathReservation first = planner.Reserve(input.Path, LocalMinute);
        File.WriteAllText(first.FinalPath, "occupied");

        OutputPathReservation second = planner.Reserve(input.Path, LocalMinute);

        Assert.EndsWith("eval-20260902-1435-02.xlsx", second.FinalPath, StringComparison.Ordinal);
        Assert.EndsWith("eval-20260902-1435-02.partial.xlsx", second.PartialPath, StringComparison.Ordinal);
        Directory.CreateDirectory(second.PartialPath);

        OutputPathReservation third = planner.Reserve(input.Path, LocalMinute);

        Assert.EndsWith("eval-20260902-1435-03.xlsx", third.FinalPath, StringComparison.Ordinal);
        Assert.EndsWith("eval-20260902-1435-03.partial.xlsx", third.PartialPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_creatable_directory_is_canonicalized_and_created()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        string requestedDirectory = Path.Combine(input.Directory, "nested", "custom-output");

        OutputPathReservation reservation = new OutputPathPlanner().Reserve(
            input.Path,
            LocalMinute,
            requestedDirectory);

        Assert.Equal(Path.GetFullPath(requestedDirectory), reservation.OutputDirectory);
        Assert.True(Directory.Exists(requestedDirectory));
        Assert.Equal(requestedDirectory, Path.GetDirectoryName(reservation.FinalPath));
        Assert.Equal(requestedDirectory, Path.GetDirectoryName(reservation.PartialPath));
    }

    [Fact]
    public void Invalid_input_or_output_directory_fails_before_a_reservation_is_returned()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        string occupiedByFile = Path.Combine(input.Directory, "not-a-directory");
        File.WriteAllText(occupiedByFile, "occupied");

        Assert.Throws<ArgumentException>(() => new OutputPathPlanner().Reserve(
            Path.Combine(input.Directory, "missing.xlsx"),
            LocalMinute));
        Assert.Throws<ArgumentException>(() => new OutputPathPlanner().Reserve(
            input.Path,
            LocalMinute,
            "   "));
        Assert.ThrowsAny<IOException>(() => new OutputPathPlanner().Reserve(
            input.Path,
            LocalMinute,
            occupiedByFile));
    }

    [Fact]
    public void A_target_created_after_reservation_is_not_overwritten_and_validation_is_not_started()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        InputSnapshot identity = new InputSnapshotService().Capture(input.Path);
        OutputPathReservation reservation = new OutputPathPlanner().Reserve(input.Path, LocalMinute);
        AppOwnedSheetNames names;
        using (SpreadsheetDocument document = SpreadsheetDocument.Open(input.Path, false))
        {
            names = new AppOwnedSheetNameResolver().Resolve(document);
        }

        OutputPackageValidationPlan plan = OutputPackageValidationPlan.Capture(input.Path, names, []);
        using WorkingPackage package = WorkingPackage.Create(input.Path, reservation.FinalPath);
        byte[] sentinel = "third-party-content"u8.ToArray();
        File.WriteAllBytes(reservation.FinalPath, sentinel);
        NeverCalledValidator validator = new();

        AtomicOutputCommitResult result = new AtomicOutputCommitter(
            validator,
            new PhysicalAtomicOutputFileOperations()).Commit(
                package,
                reservation.FinalPath,
                input.Path,
                identity,
                plan,
                TestContext.Current.CancellationToken);

        Assert.Equal(AtomicOutputStatusCodes.TargetExists, result.Code);
        Assert.Equal(0, validator.CallCount);
        Assert.Equal(sentinel, File.ReadAllBytes(reservation.FinalPath));
        Assert.False(File.Exists(package.TemporaryPath));
        Assert.True(new InputSnapshotService().Recheck(input.Path, identity).IsMatch);
    }

    private sealed class NeverCalledValidator : IOutputPackageValidator
    {
        public int CallCount { get; private set; }

        public OutputPackageValidationResult Validate(
            string workingPackagePath,
            OutputPackageValidationPlan plan,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Validation must not run after a target collision.");
        }
    }
}
