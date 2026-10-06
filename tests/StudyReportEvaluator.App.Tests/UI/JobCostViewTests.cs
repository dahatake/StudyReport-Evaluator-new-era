using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.Usage;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-043 (AC-044)
public sealed class JobCostViewTests
{
    [Fact]
    public void No_snapshot_shows_no_record_text_and_disables_file_commands()
    {
        using TemporaryJobs jobs = new();
        JobCostViewModel cost = new(jobs.Launcher, jobs.DirectoryPath);

        AssertNoRecord(cost);
        Assert.False(cost.IsExpanded);
        Assert.True(cost.AutoFollow);
        cost.OpenLogCommand.Execute(null);
        cost.OpenDirectoryCommand.Execute(null);
        Assert.Empty(jobs.Launcher.Paths);
        Assert.Empty(Directory.EnumerateFileSystemEntries(jobs.DirectoryPath));
    }

    [Fact]
    public void Same_job_older_or_equal_revision_is_ignored_without_notifications()
    {
        using TemporaryJobs jobs = new();
        JobCostViewModel cost = new(jobs.Launcher, jobs.DirectoryPath);
        JobCostSnapshot current = Snapshot(Guid.NewGuid(), 5);
        cost.Apply(current);
        List<string?> changed = [];
        int commandChanges = 0;
        cost.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        cost.OpenLogCommand.CanExecuteChanged += (_, _) => commandChanges++;
        cost.OpenDirectoryCommand.CanExecuteChanged += (_, _) => commandChanges++;

        cost.Apply(current with { Revision = 4, SummaryText = "stale", LogText = "stale" });
        cost.Apply(current with { SummaryText = "duplicate", IsFinished = true });

        Assert.Same(current, cost.Snapshot);
        Assert.Equal(current.SummaryText, cost.SummaryText);
        Assert.Empty(changed);
        Assert.Equal(0, commandChanges);

        JobCostSnapshot newer = current with { Revision = 6, SummaryText = "newer", IsFinished = true };
        cost.Apply(newer);
        Assert.Same(newer, cost.Snapshot);
        Assert.Contains(nameof(JobCostViewModel.Snapshot), changed);
        Assert.Contains(nameof(JobCostViewModel.SummaryText), changed);
        Assert.Equal(2, commandChanges);

        JobCostSnapshot nextJob = Snapshot(Guid.NewGuid(), 1);
        cost.Apply(nextJob);
        Assert.Same(nextJob, cost.Snapshot);
    }

    [Fact]
    public void Reset_clears_snapshot_open_status_and_expansion_and_accepts_revision_one()
    {
        using TemporaryJobs jobs = new();
        JobCostViewModel cost = new(jobs.Launcher, jobs.DirectoryPath);
        Guid jobId = Guid.NewGuid();
        JobCostSnapshot snapshot = Snapshot(jobId, 9) with
        {
            LogPath = Path.Combine(jobs.DirectoryPath, $"{jobId:N}.jsonl"),
        };
        cost.Apply(snapshot);
        cost.IsExpanded = true;
        cost.AutoFollow = false;
        Assert.True(cost.OpenLogCommand.CanExecute(null));
        cost.OpenLogCommand.Execute(null); // Generated path, but deliberately no file.
        Assert.NotEmpty(cost.OpenStatusText);
        List<string?> changed = [];
        cost.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        cost.Reset();

        AssertNoRecord(cost);
        Assert.Empty(cost.OpenStatusText);
        Assert.False(cost.IsExpanded);
        Assert.False(cost.AutoFollow); // Reset does not override the reader's preference.
        Assert.Contains(nameof(JobCostViewModel.Snapshot), changed);
        Assert.Contains(nameof(JobCostViewModel.IsExpanded), changed);
        Assert.Contains(nameof(JobCostViewModel.OpenStatusText), changed);
        JobCostSnapshot restarted = snapshot with { Revision = 1 };
        cost.Apply(restarted);
        Assert.Same(restarted, cost.Snapshot);
        Assert.Empty(jobs.Launcher.Paths);
    }

    [Fact]
    public async Task Results_keep_completed_run_cost_when_execution_changes_and_clear_it_for_legacy_run()
    {
        var definition = U04TestSupport.Definition(2, 2);
        var metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        JobCostSnapshot final = Snapshot(Guid.NewGuid(), 2) with { IsFinished = true };
        using ExecutionViewModel execution = U04TestSupport.ConfiguredExecutionViewModel(
            definition, metadata, new CostRunBoundary(summary, final));
        using ResultsOutputViewModel results = new(new NoIoOutputBoundary());
        await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.True(execution.CanStart);
        await execution.StartAsync(TestContext.Current.CancellationToken);
        ExecutionRunContext completed = Assert.IsType<ExecutionRunContext>(execution.LastRunContext);

        results.Load(completed);
        Assert.NotSame(execution.Cost, results.Cost);
        Assert.Same(final, completed.Cost);
        Assert.Same(final, results.Cost.Snapshot);
        Assert.True(results.Cost.Snapshot!.IsFinished);

        execution.Cost.Reset();
        execution.Cost.Apply(Snapshot(Guid.NewGuid(), 1));
        Assert.Same(final, completed.Cost);
        Assert.Same(final, results.Cost.Snapshot);
        Assert.Equal(final.SummaryText, results.Cost.SummaryText);
        Assert.Equal(final.DetailsText, results.Cost.DetailsText);
        Assert.Equal(final.LogText, results.Cost.LogText);

        results.Cost.IsExpanded = true;
        results.Load(U04TestSupport.Context(summary));
        AssertNoRecord(results.Cost);
        Assert.False(results.Cost.IsExpanded);
    }

    [Fact]
    public async Task Stale_cost_notification_from_previous_run_cannot_replace_new_run_cost()
    {
        var definition = U04TestSupport.Definition(2, 2);
        var metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        JobCostSnapshot first = Snapshot(Guid.NewGuid(), 2) with
        {
            IsFinished = true,
            SummaryText = "first run",
        };
        JobCostSnapshot staleFirst = first with
        {
            Revision = 99,
            SummaryText = "stale previous run",
        };
        JobCostSnapshot second = Snapshot(Guid.NewGuid(), 1) with
        {
            SummaryText = "second run",
        };
        StaleCostRunBoundary boundary = new(summary, first, staleFirst, second);
        using ExecutionViewModel execution = U04TestSupport.ConfiguredExecutionViewModel(definition, metadata, boundary);
        await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        await execution.StartAsync(TestContext.Current.CancellationToken);
        Assert.Same(first, execution.Cost.Snapshot);

        await execution.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, boundary.CallCount);
        Assert.Same(second, execution.Cost.Snapshot);
        Assert.Equal("second run", execution.Cost.SummaryText);
        Assert.NotEqual(staleFirst.JobId, execution.Cost.Snapshot!.JobId);
    }

    [Fact]
    public async Task Cancelled_run_keeps_terminal_cost_for_discarded_row_snapshot()
    {
        var definition = U04TestSupport.Definition(2, 2);
        var metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary discarded = EmptyDurableSummary(definition, QuantificationRunStatusCodes.Cancelled);
        using TrackerTerminalCostBoundary boundary = new(
            discarded,
            "CANCELLED",
            new UsageMetrics(InputTokens: 29, TotalNanoAiu: 0.29m));
        using ExecutionViewModel execution = U04TestSupport.ConfiguredExecutionViewModel(definition, metadata, boundary);
        await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        await execution.StartAsync(TestContext.Current.CancellationToken);

        ExecutionRunContext context = Assert.IsType<ExecutionRunContext>(execution.LastRunContext);
        Assert.Empty(context.Summary.CompletedRows);
        JobCostSnapshot cost = Assert.IsType<JobCostSnapshot>(context.Cost);
        Assert.Same(cost, execution.Cost.Snapshot);
        Assert.Equal(29, cost.Metrics.InputTokens);
        Assert.Equal(0.29m, cost.Metrics.TotalNanoAiu);
        Assert.Contains("ジョブ終了（Cancelled）", cost.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Summaryless_boundary_failure_keeps_terminal_cost_in_view_model()
    {
        var definition = U04TestSupport.Definition(2, 2);
        var metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        using TrackerTerminalCostBoundary boundary = new(
            EmptyDurableSummary(definition, QuantificationRunStatusCodes.CheckpointFailed),
            "CHECKPOINT_SAVE_FAILED",
            new UsageMetrics(InputTokens: 31, OutputTokens: 4),
            throwCode: "CHECKPOINT_SAVE_FAILED");
        using ExecutionViewModel execution = U04TestSupport.ConfiguredExecutionViewModel(definition, metadata, boundary);
        await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        await execution.StartAsync(TestContext.Current.CancellationToken);

        Assert.Null(execution.LastRunContext);
        JobCostSnapshot cost = Assert.IsType<JobCostSnapshot>(execution.Cost.Snapshot);
        Assert.Equal(31, cost.Metrics.InputTokens);
        Assert.Equal(4, cost.Metrics.OutputTokens);
        Assert.Contains("ジョブ終了（Failed）", cost.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disposed_execution_view_model_ignores_late_cost_callback()
    {
        var definition = U04TestSupport.Definition(2, 2);
        var metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        HoldingCostRunBoundary boundary = new(summary);
        ExecutionViewModel execution = U04TestSupport.ConfiguredExecutionViewModel(definition, metadata, boundary);
        await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        QueuedSynchronizationContext context = new();
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        Task run;
        try
        {
            run = execution.StartAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        await boundary.Started.WaitAsync(TestContext.Current.CancellationToken);

        execution.Dispose();
        boundary.Publish(Snapshot(Guid.NewGuid(), 1) with { SummaryText = "late disposed update" });
        Assert.True(context.PendingCount > 0);
        context.Drain();
        boundary.Release();
        for (int i = 0; i < 30 && !run.IsCompleted; i++)
        {
            context.Drain();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        context.Drain();
        await run.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Null(execution.Cost.Snapshot);
        Assert.Equal(JobCostViewModel.NoDataText, execution.Cost.LogText);
    }

    [Fact]
    public void File_commands_reject_non_generated_and_traversal_paths_and_only_call_injected_launcher()
    {
        using TemporaryJobs jobs = new();
        JobCostViewModel cost = new(jobs.Launcher, jobs.DirectoryPath);
        Guid jobId = Guid.NewGuid();
        string generated = Path.Combine(jobs.DirectoryPath, $"{jobId:N}.jsonl");
        string arbitrary = Path.Combine(jobs.DirectoryPath, "not-a-job.jsonl");
        string outside = Path.Combine(jobs.Root, $"{jobId:N}.jsonl");
        Directory.CreateDirectory(Path.Combine(jobs.DirectoryPath, "nested"));
        foreach (string path in new[] { generated, arbitrary, outside })
        {
            File.WriteAllText(path, "synthetic log");
        }

        long revision = 0;
        foreach (string invalid in new[]
        {
            arbitrary,
            outside,
            Path.Combine(jobs.DirectoryPath, "nested", "..", $"{jobId:N}.jsonl"),
            Path.Combine(jobs.DirectoryPath, "..", $"{jobId:N}.jsonl"),
            $"{jobId:N}.jsonl",
            "https://example.invalid/log.jsonl",
        })
        {
            cost.Apply(Snapshot(jobId, ++revision) with { LogPath = invalid });
            Assert.False(cost.OpenLogCommand.CanExecute(null));
            Assert.False(cost.OpenDirectoryCommand.CanExecute(null));
            cost.OpenLogCommand.Execute(null);
            cost.OpenDirectoryCommand.Execute(null);
        }

        Assert.Empty(jobs.Launcher.Paths);
        foreach (string format in new[] { "N", "D" })
        {
            string owned = Path.Combine(jobs.DirectoryPath, jobId.ToString(format) + ".jsonl");
            File.WriteAllText(owned, "synthetic log");
            cost.Apply(Snapshot(jobId, ++revision) with { LogPath = owned });
            Assert.True(cost.OpenLogCommand.CanExecute(null));
            Assert.True(cost.OpenDirectoryCommand.CanExecute(null));
            int before = jobs.Launcher.Paths.Count;
            cost.OpenLogCommand.Execute(null);
            cost.OpenDirectoryCommand.Execute(null);
            Assert.Equal(new[] { owned, jobs.DirectoryPath }, jobs.Launcher.Paths.Skip(before));
            Assert.Equal("synthetic log", File.ReadAllText(owned));
        }

        Assert.Equal("synthetic log", File.ReadAllText(arbitrary));
        Assert.Equal("synthetic log", File.ReadAllText(outside));
    }

    [AvaloniaFact]
    public void Shared_view_preserves_paused_reader_detaches_and_binds_both_host_toggles()
    {
        using TemporaryJobs jobs = new();
        JobCostViewModel cost = new(jobs.Launcher, jobs.DirectoryPath);
        JobCostSnapshot first = Snapshot(Guid.NewGuid(), 1) with
        {
            LogText = string.Join('\n', Enumerable.Range(1, 100).Select(index => $"synthetic line {index}")),
        };
        cost.Apply(first);
        JobCostView view = new() { DataContext = cost, AutomationScope = "TestCost" };
        Window window = new() { Width = 950, Height = 450, Content = view };
        try
        {
            window.Show();
            Render();
            TextBox details = Required<TextBox>(view, "CostDetailsTextBox");
            Assert.True(details.IsReadOnly);
            Assert.Equal(first.DetailsText, details.Text);
            Required<TabControl>(view, "CostTabs").SelectedIndex = 1;
            Render();
            TextBox log = Required<TextBox>(view, "LogTextBox");
            ScrollViewer scroll = Required<ScrollViewer>(view, "LogScrollViewer");
            CheckBox follow = Required<CheckBox>(view, "AutoFollowCheckBox");
            Assert.True(log.IsReadOnly);
            Assert.Equal(first.LogText, log.Text);
            Assert.Equal("TestCost-LogTextBox", AutomationProperties.GetAutomationId(log));
            Assert.True(scroll.Viewport.Height > 0d);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(scroll.Bounds.Height < window.ClientSize.Height);

            // Publish while the log tab is visible so following occurs after layout.
            cost.Apply(first with { Revision = 2, LogText = first.LogText + "\nfollowed" });
            Render();
            Assert.InRange(Math.Abs(scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y), 0d, 1d);
            follow.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            Render();
            Assert.False(cost.AutoFollow);
            scroll.Offset = default;
            log.SelectionStart = 0;
            log.SelectionEnd = 9;
            Render();
            string? paused = log.Text;
            Vector offset = scroll.Offset;
            JobCostSnapshot latest = first with { Revision = 3, LogText = first.LogText + "\nlatest" };
            cost.Apply(latest);
            Render();
            Assert.Equal(paused, log.Text);
            Assert.Equal(0, log.SelectionStart);
            Assert.Equal(9, log.SelectionEnd);
            Assert.Equal(offset, scroll.Offset);

            follow.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Render();
            Assert.True(cost.AutoFollow);
            Assert.Equal(latest.LogText, log.Text);
            window.Content = null;
            Render();
            cost.Apply(latest with { Revision = 4, LogText = "updated while detached" });
            Render();
            Assert.Equal(latest.LogText, log.Text);
            window.Content = view;
            Render();
            Assert.Equal("updated while detached", log.Text);

            using ExecutionViewModel execution = new(
                new RecordingAuthenticationBoundary(new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.AuthRequired)),
                new RecordingRunBoundary((_, _, _) => throw new InvalidOperationException("must not run")));
            using ResultsOutputViewModel results = new(new NoIoOutputBoundary());
            AssertHostToggle(window, new ExecutionView(execution), execution.Cost,
                "ExecutionCost", "ExecutionProgressPanel");
            AssertHostToggle(window, new ResultsOutputView(results), results.Cost,
                "ResultsCost", "ResultsListPanel");
            Assert.Empty(jobs.Launcher.Paths);
            Assert.False(execution.IsRunning);
            Assert.Null(execution.LastLoginTask);
            Assert.False(results.IsExporting);
        }
        finally
        {
            window.Close();
        }
    }

    private static JobCostSnapshot Snapshot(Guid jobId, long revision) => new(
        jobId, revision, false, "synthetic summary", "synthetic details", "synthetic log", null, "保存先なし");

    private static RunSummary EmptyDurableSummary(StudyReportEvaluator.Core.Domain.QuantificationDefinition definition,
        string statusCode)
    {
        EvaluationPlan plan = U01TestSupport.Plan(definition);
        return new RunSummary(
            new EvaluationScheduleResult(plan, []),
            U01TestSupport.InputSnapshot(),
            statusCode,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            isDurable: true,
            partialPath: Path.Combine(Path.GetTempPath(), "job-cost-empty.partial.xlsx"));
    }

    private static void AssertNoRecord(JobCostViewModel cost)
    {
        Assert.Null(cost.Snapshot);
        Assert.Null(cost.LogPath);
        Assert.Equal("この実行のコスト記録なし", cost.SummaryText);
        Assert.Equal(JobCostViewModel.NoDataText, cost.DetailsText);
        Assert.Equal(JobCostViewModel.NoDataText, cost.LogText);
        Assert.Equal(JobCostViewModel.NoDataText, cost.LogStatusText);
        Assert.False(cost.OpenLogCommand.CanExecute(null));
        Assert.False(cost.OpenDirectoryCommand.CanExecute(null));
    }

    private static void AssertHostToggle(Window window, Control host, JobCostViewModel cost,
        string scope, string normalPanelName)
    {
        window.Content = host;
        Render();
        JobCostView shared = Required<JobCostView>(host, scope + "View");
        CheckBox toggle = Required<CheckBox>(host, scope + "Toggle");
        Grid normal = Required<Grid>(host, normalPanelName);
        Assert.Same(cost, shared.DataContext);
        Assert.Equal(scope, AutomationProperties.GetAutomationId(shared));
        Assert.False(shared.IsEffectivelyVisible);
        Assert.True(normal.IsEffectivelyVisible);
        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        Render();
        Assert.True(cost.IsExpanded);
        Assert.True(shared.IsEffectivelyVisible);
        Assert.False(normal.IsEffectivelyVisible);
        Assert.True(shared.Bounds.Width > 0d && shared.Bounds.Height > 0d);
        Assert.True(shared.Bounds.Height <= host.Bounds.Height);
        Assert.Same(cost.OpenLogCommand, Required<Button>(shared, "OpenLogButton").Command);
        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        Render();
        Assert.False(cost.IsExpanded);
        Assert.False(shared.IsEffectivelyVisible);
        Assert.True(normal.IsEffectivelyVisible);
        window.Content = null;
        Render();
    }

    private static T Required<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(root.FindControl<T>(name));

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class RecordingFileLauncher : IJobCostFileLauncher
    {
        public List<string> Paths { get; } = [];
        public void Open(string absolutePath) => Paths.Add(absolutePath);
    }

    private sealed class TemporaryJobs : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "JobCostViewTests-" + Guid.NewGuid().ToString("N"));
        public string DirectoryPath { get; }
        public RecordingFileLauncher Launcher { get; } = new();

        public TemporaryJobs()
        {
            DirectoryPath = Path.Combine(Root, "jobs");
            Directory.CreateDirectory(DirectoryPath);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class NoIoOutputBoundary : IResultsOutputBoundary
    {
        public ResultsOutputPathAssessment AssessPath(string inputPath, string outputPath) =>
            ResultsOutputPathAssessment.Valid;

        public Task<ResultsOutputResult> ExportAsync(ResultsOutputRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("must not export");
    }

    private sealed class CostRunBoundary(RunSummary summary, JobCostSnapshot final) : IQuantificationRunBoundary
    {
        public Task<RunSummary> RunAsync(QuantificationRunRequest request, Action<EvaluationProgress>? progress,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Cost callback is required.");

        public Task<RunSummary> RunAsync(QuantificationRunRequest request, Action<EvaluationProgress>? progress,
            Action<JobCostSnapshot>? costChanged, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            costChanged?.Invoke(final with { Revision = 1, IsFinished = false });
            costChanged?.Invoke(final);
            return Task.FromResult(summary);
        }
    }

    private sealed class StaleCostRunBoundary(
        RunSummary summary,
        JobCostSnapshot first,
        JobCostSnapshot staleFirst,
        JobCostSnapshot second) : IQuantificationRunBoundary
    {
        private Action<JobCostSnapshot>? firstCallback;

        public int CallCount { get; private set; }

        public Task<RunSummary> RunAsync(
            QuantificationRunRequest request,
            Action<EvaluationProgress>? progress,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Cost callback is required.");

        public Task<RunSummary> RunAsync(
            QuantificationRunRequest request,
            Action<EvaluationProgress>? progress,
            Action<JobCostSnapshot>? costChanged,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (CallCount == 1)
            {
                firstCallback = costChanged;
                costChanged?.Invoke(first);
            }
            else
            {
                costChanged?.Invoke(second);
                firstCallback?.Invoke(staleFirst);
            }

            return Task.FromResult(summary);
        }
    }

    private sealed class TrackerTerminalCostBoundary(
        RunSummary summary,
        string completionCode,
        UsageMetrics metrics,
        string? throwCode = null) : IQuantificationRunBoundary, IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(),
            "JobCostBoundaryTests-" + Guid.NewGuid().ToString("N"));

        public Task<RunSummary> RunAsync(
            QuantificationRunRequest request,
            Action<EvaluationProgress>? progress,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Cost callback is required.");

        public async Task<RunSummary> RunAsync(
            QuantificationRunRequest request,
            Action<EvaluationProgress>? progress,
            Action<JobCostSnapshot>? costChanged,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            await using var tracker = new JobUsageTracker(logDirectory: directory);
            Guid id = tracker.BeginAttempt(UsageOperation.Normal);
            tracker.ReplaceAttempt(new(id, UsageOperation.Normal, 1, metrics, UsageSource.Events,
                IsFinished: false, IsPartial: true, Status: UsageObservationStatus.EventObserved));
            await tracker.CompleteAsync(completionCode);
            costChanged?.Invoke(tracker.Snapshot);
            if (throwCode is not null)
            {
                throw new QuantificationRunException(throwCode);
            }

            return summary;
        }

        public void Dispose()
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }

    private sealed class HoldingCostRunBoundary(RunSummary summary) : IQuantificationRunBoundary
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action<JobCostSnapshot>? costChanged;

        public Task Started => started.Task;

        public Task<RunSummary> RunAsync(
            QuantificationRunRequest request,
            Action<EvaluationProgress>? progress,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Cost callback is required.");

        public async Task<RunSummary> RunAsync(
            QuantificationRunRequest request,
            Action<EvaluationProgress>? progress,
            Action<JobCostSnapshot>? costChanged,
            CancellationToken cancellationToken)
        {
            this.costChanged = costChanged;
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return summary;
        }

        public void Publish(JobCostSnapshot snapshot) => costChanged?.Invoke(snapshot);

        public void Release() => release.TrySetResult();
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> posts = new();

        public int PendingCount
        {
            get { lock (posts) { return posts.Count; } }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (posts) { posts.Enqueue((d, state)); }
        }

        public void Drain()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) next;
                lock (posts)
                {
                    if (posts.Count == 0) { return; }
                    next = posts.Dequeue();
                }

                next.Callback(next.State);
            }
        }
    }
}