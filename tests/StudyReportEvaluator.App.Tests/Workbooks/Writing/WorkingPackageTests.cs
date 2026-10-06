using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Writing;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Writing;

public sealed class WorkingPackageTests
{
    [Fact]
    public void Create_makes_an_exact_target_local_copy_before_edit_and_dispose_removes_it()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        string targetDirectory = Path.Combine(input.Directory, "target");
        Directory.CreateDirectory(targetDirectory);
        string finalPath = Path.Combine(targetDirectory, "quantified.xlsx");
        byte[] inputBytes = File.ReadAllBytes(input.Path);
        InputSnapshotService snapshots = new();
        InputSnapshot inputBefore = snapshots.Capture(input.Path);
        string temporaryPath;

        {
            using WorkingPackage package = WorkingPackage.Create(input.Path, finalPath);
            temporaryPath = package.TemporaryPath;

            Assert.Equal(Path.GetFullPath(input.Path), package.InputPath);
            Assert.Equal(Path.GetFullPath(finalPath), package.RequestedFinalPath);
            Assert.Equal(Path.GetFullPath(targetDirectory), Path.GetDirectoryName(temporaryPath));
            Assert.NotEqual(Path.GetFullPath(input.Path), temporaryPath);
            Assert.Equal(inputBytes.LongLength, package.CopiedByteCount);
            Assert.Equal(inputBytes, File.ReadAllBytes(temporaryPath));
            Assert.False(File.Exists(finalPath));

            using (SpreadsheetDocument document = package.OpenForEditing())
            {
                Workbook workbook = document.WorkbookPart?.Workbook
                    ?? throw new InvalidDataException("Synthetic workbook root is missing.");
                workbook.CalculationProperties = new CalculationProperties
                {
                    ForceFullCalculation = true,
                };
                workbook.Save();
            }

            Assert.NotEqual(inputBytes, File.ReadAllBytes(temporaryPath));
            Assert.False(File.Exists(finalPath));
            Assert.Contains("<redacted>", package.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(input.Path, package.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(temporaryPath, package.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(finalPath, package.ToString(), StringComparison.Ordinal);
        }

        Assert.False(File.Exists(temporaryPath));
        Assert.True(snapshots.Recheck(input.Path, inputBefore).IsMatch);
        Assert.Equal(inputBytes, File.ReadAllBytes(input.Path));
        Assert.False(File.Exists(finalPath));
    }

    [Fact]
    public void Create_reads_a_read_only_input_without_changing_its_identity()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        string targetDirectory = Path.Combine(input.Directory, "target");
        Directory.CreateDirectory(targetDirectory);
        InputSnapshotService snapshots = new();
        InputSnapshot before = snapshots.Capture(input.Path);
        File.SetAttributes(input.Path, FileAttributes.ReadOnly);
        try
        {
            using WorkingPackage package = WorkingPackage.Create(
                input.Path,
                Path.Combine(targetDirectory, "readonly-copy.xlsx"));

            Assert.Equal(before.SizeBytes, package.CopiedByteCount);
            Assert.Equal(File.ReadAllBytes(input.Path), File.ReadAllBytes(package.TemporaryPath));
        }
        finally
        {
            File.SetAttributes(input.Path, FileAttributes.Normal);
        }

        Assert.True(snapshots.Recheck(input.Path, before).IsMatch);
    }

    [Fact]
    public void Existing_final_is_rejected_without_touching_it_or_leaving_a_temp_file()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        string targetDirectory = Path.Combine(input.Directory, "target");
        Directory.CreateDirectory(targetDirectory);
        string finalPath = Path.Combine(targetDirectory, "existing.xlsx");
        byte[] sentinel = "EXISTING-FINAL-SENTINEL"u8.ToArray();
        File.WriteAllBytes(finalPath, sentinel);
        InputSnapshotService snapshots = new();
        InputSnapshot inputBefore = snapshots.Capture(input.Path);

        IOException exception = Assert.Throws<IOException>(
            () => WorkingPackage.Create(input.Path, finalPath));

        Assert.Equal(sentinel, File.ReadAllBytes(finalPath));
        Assert.Empty(Directory.EnumerateFiles(targetDirectory, "*.working.xlsx"));
        Assert.True(snapshots.Recheck(input.Path, inputBefore).IsMatch);
        Assert.DoesNotContain(finalPath, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Concurrent_working_packages_receive_unique_names_and_clean_up_independently()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        string targetDirectory = Path.Combine(input.Directory, "target");
        Directory.CreateDirectory(targetDirectory);
        WorkingPackage first = WorkingPackage.Create(
            input.Path,
            Path.Combine(targetDirectory, "first.xlsx"));
        WorkingPackage second = WorkingPackage.Create(
            input.Path,
            Path.Combine(targetDirectory, "second.xlsx"));
        string firstPath = first.TemporaryPath;
        string secondPath = second.TemporaryPath;
        try
        {
            Assert.NotEqual(firstPath, secondPath);
            Assert.True(File.Exists(firstPath));
            Assert.True(File.Exists(secondPath));

            first.Dispose();

            Assert.False(File.Exists(firstPath));
            Assert.True(File.Exists(secondPath));
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }

        Assert.False(File.Exists(firstPath));
        Assert.False(File.Exists(secondPath));
    }

    [Fact]
    public void Input_and_final_must_be_distinct_standard_xlsx_paths()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        string nonXlsx = Path.Combine(input.Directory, "output.xlsm");

        Assert.Throws<ArgumentException>(() => WorkingPackage.Create(input.Path, input.Path));
        Assert.Throws<ArgumentException>(() => WorkingPackage.Create(input.Path, nonXlsx));
        Assert.Throws<ArgumentNullException>(() => WorkingPackage.Create(null!, input.Path));
        Assert.Throws<ArgumentException>(() => WorkingPackage.Create(input.Path, " "));
        Assert.Single(Directory.EnumerateFiles(input.Directory));
    }
}
