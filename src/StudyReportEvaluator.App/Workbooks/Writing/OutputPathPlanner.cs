using System.Globalization;

namespace StudyReportEvaluator.App.Workbooks.Writing;

public sealed class OutputPathReservation
{
    internal OutputPathReservation(string outputDirectory, string finalPath, string partialPath)
    {
        OutputDirectory = outputDirectory;
        FinalPath = finalPath;
        PartialPath = partialPath;
    }

    public string OutputDirectory { get; }

    public string FinalPath { get; }

    public string PartialPath { get; }

    public static OutputPathReservation Create(
        string outputDirectory,
        string finalPath,
        string partialPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(partialPath);
        return new OutputPathReservation(
            Path.GetFullPath(outputDirectory),
            Path.GetFullPath(finalPath),
            Path.GetFullPath(partialPath));
    }

    public override string ToString() =>
        $"{nameof(OutputPathReservation)} {{ Content = <redacted> }}";
}

public interface IOutputPathPlanner
{
    OutputPathReservation Reserve(
        string inputPath,
        DateTimeOffset localTime,
        string? outputDirectory = null);
}

public sealed class OutputPathPlanner : IOutputPathPlanner
{
    private const string DefaultDirectoryName = "result";
    private const string FileNamePrefix = "eval-";

    public OutputPathReservation Reserve(
        string inputPath,
        DateTimeOffset localTime,
        string? outputDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        if (outputDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        }

        string canonicalInputPath = Path.GetFullPath(inputPath);
        if (!string.Equals(
                Path.GetExtension(canonicalInputPath),
                ".xlsx",
                StringComparison.OrdinalIgnoreCase)
            || !File.Exists(canonicalInputPath))
        {
            throw new ArgumentException("The input path must identify an existing standard .xlsx workbook.", nameof(inputPath));
        }

        string inputDirectory = Path.GetDirectoryName(canonicalInputPath)
            ?? throw new ArgumentException("The input path has no parent directory.", nameof(inputPath));
        string targetDirectory = Path.GetFullPath(
            outputDirectory ?? Path.Combine(inputDirectory, DefaultDirectoryName));
        Directory.CreateDirectory(targetDirectory);

        string minute = localTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        for (int sequence = 1; sequence < int.MaxValue; sequence++)
        {
            string suffix = sequence == 1
                ? string.Empty
                : "-" + sequence.ToString("D2", CultureInfo.InvariantCulture);
            string stem = FileNamePrefix + minute + suffix;
            string finalPath = Path.Combine(targetDirectory, stem + ".xlsx");
            string partialPath = Path.Combine(targetDirectory, stem + ".partial.xlsx");
            if (PathIsUnused(finalPath)
                && PathIsUnused(partialPath)
                && !PathsEqual(canonicalInputPath, finalPath)
                && !PathsEqual(canonicalInputPath, partialPath))
            {
                return new OutputPathReservation(targetDirectory, finalPath, partialPath);
            }
        }

        throw new IOException("An unused output path suffix could not be allocated.");
    }

    public override string ToString() =>
        $"{nameof(OutputPathPlanner)} {{ Content = <redacted> }}";

    private static bool PathIsUnused(string path) =>
        !File.Exists(path) && !Directory.Exists(path);

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            first,
            second,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
