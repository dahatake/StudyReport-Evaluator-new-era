using System.Collections.Immutable;
using DocumentFormat.OpenXml.Packaging;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Validation;
using StudyReportEvaluator.App.Workbooks.Writing;

namespace StudyReportEvaluator.App.Workflow;

public sealed class DurableFinalizationResult
{
    internal DurableFinalizationResult(string code, string? finalPath)
    {
        Code = code;
        FinalPath = finalPath;
    }

    public string Code { get; }

    public string? FinalPath { get; }

    public bool IsSuccess => string.Equals(Code, AtomicOutputStatusCodes.Success, StringComparison.Ordinal);

    public static DurableFinalizationResult Succeeded(string finalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        return new DurableFinalizationResult(AtomicOutputStatusCodes.Success, finalPath);
    }

    public static DurableFinalizationResult Failed(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new DurableFinalizationResult(code, null);
    }

    public override string ToString() =>
        $"{nameof(DurableFinalizationResult)} {{ Code = {Code}, Content = <redacted> }}";
}

public interface IDurableRunFinalizer
{
    DurableFinalizationResult Finalize(
        RunSummary summary,
        CheckpointEnvelope checkpoint,
        CancellationToken cancellationToken);
}

public sealed class WorkbookDurableRunFinalizer : IDurableRunFinalizer
{
    private readonly AtomicOutputCommitter committer;

    public WorkbookDurableRunFinalizer()
        : this(new AtomicOutputCommitter())
    {
    }

    public WorkbookDurableRunFinalizer(AtomicOutputCommitter committer)
    {
        this.committer = committer ?? throw new ArgumentNullException(nameof(committer));
    }

    public DurableFinalizationResult Finalize(
        RunSummary summary,
        CheckpointEnvelope checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!summary.IsDurable
            || !string.Equals(summary.DefinitionSha256, checkpoint.DefinitionSha256, StringComparison.Ordinal)
            || !summary.InputSnapshot.Equals(checkpoint.Input)
            || !PathsEqual(summary.PartialPath, checkpoint.PartialPath)
            || summary.FinalPath is not null)
        {
            return Failure(AtomicOutputStatusCodes.OutputInvalid);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunOutputPreparation preparation = summary.PrepareOutput();
            if (!preparation.IsExportReady || preparation.IsPartial)
            {
                return Failure(AtomicOutputStatusCodes.OutputInvalid);
            }

            using WorkingPackage package = WorkingPackage.Create(
                checkpoint.InputPath,
                checkpoint.FinalPath);
            AppOwnedSheetNames sheetNames;
            ConfigCellAddressMap config;
            ResultsSheetWriteResult results;
            using (SpreadsheetDocument document = package.OpenForEditing())
            {
                sheetNames = new AppOwnedSheetNameResolver().Resolve(document);
                config = new ConfigSheetWriter().Write(
                    document,
                    summary.Snapshot,
                    sheetNames);
                new ReferenceAnswersSheetWriter().Write(
                    document,
                    summary.Snapshot,
                    sheetNames,
                    summary.References.Select(reference => new ReferenceAnswerSheetRow
                    {
                        QuestionId = reference.QuestionId,
                        ModelId = checkpoint.ReferenceModelId,
                        ReasoningEffort = checkpoint.ReasoningEffort,
                        Answer = reference.Answer,
                        StatusCode = reference.StatusCode,
                        GeneratedAtUtc = reference.GeneratedAtUtc,
                    }));
                results = new ResultsSheetWriter().Write(
                    document,
                    summary.Snapshot,
                    sheetNames,
                    config,
                    preparation.Rows);
                new RunSheetWriter().Write(document, CreateRunMetadata(summary, checkpoint, sheetNames));
                new CalculationPropertiesWriter().Write(document);
            }

            ImmutableArray<ExpectedFormulaCell> expectedFormulas = config.FormulaCells
                .Concat(results.FormulaCells)
                .Select(item => new ExpectedFormulaCell(item.Definition, item.CachedValue))
                .ToImmutableArray();
            OutputPackageValidationPlan validationPlan = OutputPackageValidationPlan.Capture(
                checkpoint.InputPath,
                sheetNames,
                expectedFormulas);
            AtomicOutputCommitResult commit = committer.Commit(
                package,
                checkpoint.FinalPath,
                checkpoint.InputPath,
                checkpoint.Input,
                validationPlan,
                cancellationToken);
            return new DurableFinalizationResult(commit.Code, commit.FinalPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(AtomicOutputStatusCodes.Cancelled);
        }
        catch
        {
            return Failure(AtomicOutputStatusCodes.OutputInvalid);
        }
    }

    public override string ToString() =>
        $"{nameof(WorkbookDurableRunFinalizer)} {{ Content = <redacted> }}";

    private static RunSheetMetadata CreateRunMetadata(
        RunSummary summary,
        CheckpointEnvelope checkpoint,
        AppOwnedSheetNames sheetNames)
    {
        var usage = summary.OperationTokenUsage;
        return new RunSheetMetadata
        {
            InputIdentity = summary.InputSnapshot,
            DefinitionSha256 = summary.DefinitionSha256,
            ApplicationIdentity = checkpoint.Runtime.ApplicationIdentity,
            CopilotSdkIdentity = "GitHub.Copilot.SDK/" + checkpoint.Runtime.SdkInformationalVersion,
            CopilotCliIdentity = "copilot/" + checkpoint.Runtime.CliVersion
                + ";sha256=" + checkpoint.Runtime.CliSha256,
            ModelIdentity = checkpoint.NormalModelId,
            ReasoningEffort = checkpoint.ReasoningEffort,
            ContextTier = checkpoint.ContextTier,
            StartedAtUtc = summary.StartedAtUtc,
            EndedAtUtc = summary.EndedAtUtc,
            PlannedEvaluationCount = summary.PlannedOperationCount,
            CompletedEvaluationCount = summary.CompletedOperationCount,
            ErrorCount = summary.OperationFailureCount,
            UsageObservedUnitCount = summary.OperationUsageObservedCount,
            InputTokenCount = usage.InputTokens,
            OutputTokenCount = usage.OutputTokens,
            ReasoningTokenCount = usage.ReasoningTokens,
            CacheReadTokenCount = usage.CacheReadTokens,
            CacheWriteTokenCount = usage.CacheWriteTokens,
            SheetNames = sheetNames,
        };
    }

    private static DurableFinalizationResult Failure(string code) => new(code, null);

    private static bool PathsEqual(string? first, string second) =>
        first is not null
        && string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

public interface IPartialCheckpointCleaner
{
    bool TryDelete(string partialPath);
}

public sealed class PhysicalPartialCheckpointCleaner : IPartialCheckpointCleaner
{
    public bool TryDelete(string partialPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partialPath);
        try
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }

            return !File.Exists(partialPath);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return false;
        }
    }

    public override string ToString() =>
        $"{nameof(PhysicalPartialCheckpointCleaner)} {{ Content = <redacted> }}";
}
