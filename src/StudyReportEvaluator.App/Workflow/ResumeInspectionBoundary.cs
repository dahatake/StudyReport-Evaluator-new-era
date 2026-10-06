using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;

namespace StudyReportEvaluator.App.Workflow;

/// <summary>Read-only operations; no reservation, save, authentication or AI dispatch.</summary>
public interface IResumeInspectionBoundary
{
    Task<CheckpointLoadResult> LoadAsync(string partialPath, CancellationToken cancellationToken);

    Task<InputSnapshot> CaptureInputAsync(string inputPath, CancellationToken cancellationToken);
}

public sealed class ResumeInspectionBoundary : IResumeInspectionBoundary
{
    public Task<CheckpointLoadResult> LoadAsync(string partialPath, CancellationToken cancellationToken) =>
        Task.Run(() => new CheckpointStore().Load(partialPath, cancellationToken), cancellationToken);

    public Task<InputSnapshot> CaptureInputAsync(string inputPath, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            InputSnapshot snapshot = new InputSnapshotService().Capture(inputPath);
            cancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }, cancellationToken);
}