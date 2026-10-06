using System.Collections.Immutable;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Validation;

namespace StudyReportEvaluator.App.Workbooks.Writing;

public static class AtomicOutputStatusCodes
{
    public const string Success = "SUCCESS";
    public const string InputChanged = "INPUT_CHANGED";
    public const string OutputInvalid = "OUTPUT_INVALID";
    public const string TargetExists = "TARGET_EXISTS";
    public const string CommitFailed = "COMMIT_FAILED";
    public const string Cancelled = "CANCELLED";
    public const string CleanupFailed = "CLEANUP_FAILED";
}

public sealed class AtomicOutputCommitResult
{
    internal AtomicOutputCommitResult(
        string code,
        string? causeCode,
        string? finalPath,
        ImmutableArray<OutputPackageValidationError> validationErrors)
    {
        Code = code;
        CauseCode = causeCode;
        FinalPath = finalPath;
        ValidationErrors = validationErrors;
    }

    public string Code { get; }

    public string? CauseCode { get; }

    public string? FinalPath { get; }

    public ImmutableArray<OutputPackageValidationError> ValidationErrors { get; }

    public bool IsSuccess => string.Equals(Code, AtomicOutputStatusCodes.Success, StringComparison.Ordinal);

    public override string ToString() =>
        $"{nameof(AtomicOutputCommitResult)} {{ Code = {Code}, CauseCode = {CauseCode ?? "<none>"}, ValidationErrorCount = {ValidationErrors.Length}, Content = <redacted> }}";
}

public interface IAtomicOutputFileOperations
{
    bool Exists(string path);

    void FlushToDisk(string path);

    void MoveNoOverwrite(string sourcePath, string destinationPath);

    void Delete(string path);
}

public sealed class PhysicalAtomicOutputFileOperations : IAtomicOutputFileOperations
{
    public bool Exists(string path) => File.Exists(path);

    public void FlushToDisk(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.WriteThrough);
        stream.Flush(flushToDisk: true);
    }

    public void MoveNoOverwrite(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, overwrite: false);

    public void Delete(string path) => File.Delete(path);

    public override string ToString() =>
        $"{nameof(PhysicalAtomicOutputFileOperations)} {{ Content = <redacted> }}";
}

public sealed class AtomicOutputCommitter
{
    private readonly IOutputPackageValidator validator;
    private readonly IAtomicOutputFileOperations fileOperations;
    private readonly InputSnapshotService inputSnapshots;

    public AtomicOutputCommitter()
        : this(
            new OutputPackageValidator(),
            new PhysicalAtomicOutputFileOperations(),
            new InputSnapshotService())
    {
    }

    public AtomicOutputCommitter(
        IOutputPackageValidator validator,
        IAtomicOutputFileOperations fileOperations)
        : this(validator, fileOperations, new InputSnapshotService())
    {
    }

    internal AtomicOutputCommitter(
        IOutputPackageValidator validator,
        IAtomicOutputFileOperations fileOperations,
        InputSnapshotService inputSnapshots)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(fileOperations);
        ArgumentNullException.ThrowIfNull(inputSnapshots);
        this.validator = validator;
        this.fileOperations = fileOperations;
        this.inputSnapshots = inputSnapshots;
    }

    public AtomicOutputCommitResult Commit(
        WorkingPackage workingPackage,
        string requestedFinalPath,
        string originalInputPath,
        InputSnapshot originalInputSnapshot,
        OutputPackageValidationPlan validationPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workingPackage);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedFinalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalInputPath);
        ArgumentNullException.ThrowIfNull(originalInputSnapshot);
        ArgumentNullException.ThrowIfNull(validationPlan);

        string temporaryPath;
        string finalPath;
        string inputPath;
        try
        {
            temporaryPath = Path.GetFullPath(workingPackage.TemporaryPath);
            finalPath = Path.GetFullPath(requestedFinalPath);
            inputPath = Path.GetFullPath(originalInputPath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return Failure(AtomicOutputStatusCodes.CommitFailed);
        }

        if (!HasValidPathBinding(workingPackage, temporaryPath, finalPath, inputPath))
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.CommitFailed, temporaryPath);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.Cancelled, temporaryPath);
        }

        if (!TryExists(temporaryPath, out bool temporaryExists) || !temporaryExists)
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.CommitFailed, temporaryPath);
        }

        if (!TryExists(finalPath, out bool finalExists))
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.CommitFailed, temporaryPath);
        }

        if (finalExists)
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.TargetExists, temporaryPath);
        }

        try
        {
            fileOperations.FlushToDisk(temporaryPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.Cancelled, temporaryPath);
        }
        catch (Exception exception) when (IsOperationalFileException(exception))
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.CommitFailed, temporaryPath);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.Cancelled, temporaryPath);
        }

        OutputPackageValidationResult validation;
        try
        {
            validation = validator.Validate(temporaryPath, validationPlan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.Cancelled, temporaryPath);
        }
        catch (OperationCanceledException)
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.OutputInvalid, temporaryPath);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or OutputPackageValidationException)
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.OutputInvalid, temporaryPath);
        }

        if (!validation.IsValid)
        {
            return FailureWithCleanup(
                AtomicOutputStatusCodes.OutputInvalid,
                temporaryPath,
                validation.Errors);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.Cancelled, temporaryPath);
        }

        try
        {
            InputSnapshotComparison comparison = inputSnapshots.Recheck(inputPath, originalInputSnapshot);
            if (!comparison.IsMatch)
            {
                return FailureWithCleanup(AtomicOutputStatusCodes.InputChanged, temporaryPath);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException)
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.InputChanged, temporaryPath);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return FailureWithCleanup(AtomicOutputStatusCodes.Cancelled, temporaryPath);
        }

        // Critical section: from this point cancellation is deliberately deferred. File.Move with
        // overwrite:false is the single state transition from validated temp to completed final.
        try
        {
            fileOperations.MoveNoOverwrite(temporaryPath, finalPath);
            if (TryExists(finalPath, out bool finalAfterMove)
                && TryExists(temporaryPath, out bool tempAfterMove)
                && finalAfterMove
                && !tempAfterMove)
            {
                return Success(finalPath);
            }

            return FailureWithCleanup(AtomicOutputStatusCodes.CommitFailed, temporaryPath);
        }
        catch (Exception exception) when (exception is OperationCanceledException
            || IsOperationalFileException(exception))
        {
            if (TryExists(finalPath, out bool finalAfterFailure)
                && TryExists(temporaryPath, out bool tempAfterFailure))
            {
                if (finalAfterFailure && !tempAfterFailure)
                {
                    return Success(finalPath);
                }

                if (finalAfterFailure)
                {
                    return FailureWithCleanup(AtomicOutputStatusCodes.TargetExists, temporaryPath);
                }
            }

            return FailureWithCleanup(AtomicOutputStatusCodes.CommitFailed, temporaryPath);
        }
    }

    public override string ToString() =>
        $"{nameof(AtomicOutputCommitter)} {{ Content = <redacted> }}";

    private static bool HasValidPathBinding(
        WorkingPackage workingPackage,
        string temporaryPath,
        string finalPath,
        string inputPath)
    {
        string packageFinal = Path.GetFullPath(workingPackage.RequestedFinalPath);
        string packageInput = Path.GetFullPath(workingPackage.InputPath);
        string? temporaryDirectory = Path.GetDirectoryName(temporaryPath);
        string? finalDirectory = Path.GetDirectoryName(finalPath);
        return PathsEqual(packageFinal, finalPath)
            && PathsEqual(packageInput, inputPath)
            && !PathsEqual(inputPath, finalPath)
            && !PathsEqual(inputPath, temporaryPath)
            && !PathsEqual(temporaryPath, finalPath)
            && string.Equals(Path.GetExtension(finalPath), ".xlsx", StringComparison.OrdinalIgnoreCase)
            && temporaryDirectory is not null
            && finalDirectory is not null
            && PathsEqual(temporaryDirectory, finalDirectory)
            && PathsEqual(Path.GetPathRoot(temporaryPath) ?? string.Empty, Path.GetPathRoot(finalPath) ?? string.Empty);
    }

    private AtomicOutputCommitResult FailureWithCleanup(
        string code,
        string temporaryPath,
        ImmutableArray<OutputPackageValidationError> validationErrors = default)
    {
        ImmutableArray<OutputPackageValidationError> safeErrors = validationErrors.IsDefault
            ? []
            : validationErrors;
        try
        {
            if (fileOperations.Exists(temporaryPath))
            {
                fileOperations.Delete(temporaryPath);
                if (fileOperations.Exists(temporaryPath))
                {
                    return Failure(AtomicOutputStatusCodes.CleanupFailed, code, safeErrors);
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException
            || IsOperationalFileException(exception))
        {
            return Failure(AtomicOutputStatusCodes.CleanupFailed, code, safeErrors);
        }

        return Failure(code, validationErrors: safeErrors);
    }

    private bool TryExists(string path, out bool exists)
    {
        try
        {
            exists = fileOperations.Exists(path);
            return true;
        }
        catch (Exception exception) when (exception is OperationCanceledException
            || IsOperationalFileException(exception))
        {
            exists = false;
            return false;
        }
    }

    private static bool IsOperationalFileException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException;

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            first,
            second,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static AtomicOutputCommitResult Success(string finalPath) =>
        new(AtomicOutputStatusCodes.Success, null, finalPath, []);

    private static AtomicOutputCommitResult Failure(
        string code,
        string? causeCode = null,
        ImmutableArray<OutputPackageValidationError> validationErrors = default) =>
        new(
            code,
            causeCode,
            null,
            validationErrors.IsDefault ? [] : validationErrors);
}