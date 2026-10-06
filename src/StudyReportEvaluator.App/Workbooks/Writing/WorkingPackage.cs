using DocumentFormat.OpenXml.Packaging;

namespace StudyReportEvaluator.App.Workbooks.Writing;

public sealed class WorkingPackage : IDisposable
{
    private const int CopyBufferSize = 128 * 1024;
    private const int MaximumNameAttempts = 64;

    private bool disposed;

    private WorkingPackage(
        string inputPath,
        string requestedFinalPath,
        string temporaryPath,
        long copiedByteCount)
    {
        InputPath = inputPath;
        RequestedFinalPath = requestedFinalPath;
        TemporaryPath = temporaryPath;
        CopiedByteCount = copiedByteCount;
    }

    public string InputPath { get; }

    public string RequestedFinalPath { get; }

    public string TemporaryPath { get; }

    public long CopiedByteCount { get; }

    public static WorkingPackage Create(string inputPath, string requestedFinalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedFinalPath);

        string canonicalInputPath = Path.GetFullPath(inputPath);
        string canonicalFinalPath = Path.GetFullPath(requestedFinalPath);
        ValidateWorkbookExtension(canonicalInputPath, nameof(inputPath));
        ValidateWorkbookExtension(canonicalFinalPath, nameof(requestedFinalPath));

        if (PathsAreEqual(canonicalInputPath, canonicalFinalPath))
        {
            throw new ArgumentException(
                "The requested final workbook must be different from the input workbook.",
                nameof(requestedFinalPath));
        }

        string targetDirectory = Path.GetDirectoryName(canonicalFinalPath)
            ?? throw new ArgumentException("The requested final path has no target directory.", nameof(requestedFinalPath));
        if (!Directory.Exists(targetDirectory))
        {
            throw new DirectoryNotFoundException("The target directory does not exist.");
        }

        if (File.Exists(canonicalFinalPath))
        {
            throw new IOException("The requested final workbook already exists.");
        }

        (string temporaryPath, long copiedByteCount) = CopyAndFlushToUniqueTarget(
            canonicalInputPath,
            targetDirectory);
        return new WorkingPackage(
            canonicalInputPath,
            canonicalFinalPath,
            temporaryPath,
            copiedByteCount);
    }

    public SpreadsheetDocument OpenForEditing()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return SpreadsheetDocument.Open(
            TemporaryPath,
            true,
            new OpenSettings { AutoSave = true });
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            if (File.Exists(TemporaryPath))
            {
                File.Delete(TemporaryPath);
            }

            disposed = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("The working workbook could not be cleaned up.");
        }
    }

    public override string ToString() =>
        $"{nameof(WorkingPackage)} {{ CopiedByteCount = {CopiedByteCount}, Content = <redacted> }}";

    private static (string TemporaryPath, long CopiedByteCount) CopyAndFlushToUniqueTarget(
        string inputPath,
        string targetDirectory)
    {
        using FileStream input = new(
            inputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.SequentialScan);
        long inputLength = input.Length;

        for (int attempt = 0; attempt < MaximumNameAttempts; attempt++)
        {
            string temporaryPath = CreateTemporaryPathCandidate(targetDirectory);
            FileStream output;
            try
            {
                output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    CopyBufferSize,
                    FileOptions.SequentialScan | FileOptions.WriteThrough);
            }
            catch (IOException) when (File.Exists(temporaryPath) || Directory.Exists(temporaryPath))
            {
                continue;
            }
            catch (UnauthorizedAccessException) when (Directory.Exists(temporaryPath))
            {
                continue;
            }

            try
            {
                long copiedByteCount;
                using (output)
                {
                    input.CopyTo(output, CopyBufferSize);
                    output.Flush(flushToDisk: true);
                    copiedByteCount = output.Length;
                    if (input.Length != inputLength || copiedByteCount != inputLength)
                    {
                        throw new IOException("The input workbook changed while the working copy was being created.");
                    }
                }

                return (temporaryPath, copiedByteCount);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        throw new IOException("A unique target-local working workbook name could not be allocated.");
    }

    private static string CreateTemporaryPathCandidate(string targetDirectory) =>
        Path.Combine(
            targetDirectory,
            $".study-report-evaluator-{Guid.NewGuid():N}.working.xlsx");

    private static void ValidateWorkbookExtension(string path, string parameterName)
    {
        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only standard .xlsx workbooks are supported.", parameterName);
        }
    }

    private static bool PathsAreEqual(string first, string second) =>
        string.Equals(
            first,
            second,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Creation is already failing. Cleanup is best effort and must not hide the original failure.
        }
    }
}
