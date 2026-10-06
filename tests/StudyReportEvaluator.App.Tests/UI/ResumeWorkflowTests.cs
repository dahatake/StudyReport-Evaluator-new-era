using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-033 (AC-034)
public sealed class ResumeWorkflowTests
{
    [Fact]
    public async Task Checkpoint_model_action_restores_effort_and_context_before_explicit_resume()
    {
        using Fixture fixture = new("high", "long-context");
        CopilotModelAvailability model = new("model-test", 272_000, 400_000,
            ["low", "high"], "low", 1_000_000);
        using ExecutionViewModel vm = new(new RecordingAuthenticationBoundary(new ExecutionAuthenticationSnapshot(
            ExecutionAuthenticationState.Available, [model], U04TestSupport.RuntimeIdentity())), fixture.Runner);
        vm.Configure(fixture.Definition, new WorkbookMetadataReader().Read(fixture.Workbook.Path), fixture.Workbook.Path);
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        vm.IsResumeMode = true;
        vm.ResumePartialPath = fixture.Checkpoint.PartialPath;
        await vm.PrepareResumeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CheckpointAdmissionStatusCodes.ModelMismatch, vm.ResumeReport?.BlockingStatusCode);
        Assert.Equal(0, fixture.Runner.CallCount);
        vm.ApplyCheckpointModelCommand.Execute(null);
        Assert.Equal("high", vm.SelectedModelReasoningEffort);
        Assert.Equal("long-context", vm.SelectedContextTier);
        await vm.PrepareResumeAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.CanStart);
        Assert.Equal(0, fixture.Runner.CallCount);
        await vm.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal("high", fixture.Runner.LastRequest?.ReasoningEffort);
        Assert.Equal("long-context", fixture.Runner.LastRequest?.ContextTier);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Shell_checkpoint_input_restore_preserves_selection_and_requires_matching_definition(
        bool hasInput, bool differentDefinition)
    {
        using Fixture fixture = new();
        InputViewModel input = new();
        if (hasInput)
        {
            string otherPath = Path.Combine(fixture.Workbook.Directory, "other.xlsx");
            File.Copy(fixture.Workbook.Path, otherPath);
            await input.SetFilePathAsync(otherPath, TestContext.Current.CancellationToken);
            Assert.True(await input.ApplySavedDefinitionAsync(
                differentDefinition ? fixture.Definition with { Revision = "different" } : fixture.Definition,
                TestContext.Current.CancellationToken));
        }

        using MainWindowViewModel shell = new(new WorkflowNavigator(), input,
            new QuantificationDesignViewModel(input.DefinitionDraft, input.AvailableColumnNames),
            fixture.CreateExecution(), new ResultsOutputViewModel(new RecordingOutputBoundary()));
        shell.NextCommand.Execute(null);
        shell.NextCommand.Execute(null);
        ExecutionViewModel vm = shell.ExecutionViewModel;
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        vm.IsResumeMode = true;
        vm.ResumePartialPath = fixture.Checkpoint.PartialPath;
        await vm.PrepareResumeAsync(TestContext.Current.CancellationToken);
        Assert.False(vm.CanStart);
        string? previousDefinition = hasInput
            ? QuantificationSnapshot.Create(input.DefinitionDraft).CanonicalJson : null;

        await vm.ApplyCheckpointInputAsync(TestContext.Current.CancellationToken);

        Assert.Equal(fixture.Workbook.Path, input.FilePath);
        Assert.True(vm.IsResumeMode);
        Assert.Equal(fixture.Checkpoint.PartialPath, vm.ResumePartialPath);
        Assert.Equal(0, fixture.Runner.CallCount);
        if (hasInput)
        {
            Assert.Equal(previousDefinition, QuantificationSnapshot.Create(input.DefinitionDraft).CanonicalJson);
            Assert.Equal(!differentDefinition, vm.CanStart);
        }
        else
        {
            Assert.False(vm.CanStart);
            Assert.True(await input.ApplySavedDefinitionAsync(fixture.Definition, TestContext.Current.CancellationToken));
            vm.IsResumeMode = true;
            vm.ResumePartialPath = fixture.Checkpoint.PartialPath;
            await vm.PrepareResumeAsync(TestContext.Current.CancellationToken);
            Assert.True(vm.CanStart);
        }
        Assert.Equal(0, fixture.Runner.CallCount);
    }

    [Fact]
    public async Task Real_checkpoint_preflight_requires_explicit_start_and_invalidates_after_model_change()
    {
        using Fixture fixture = new();
        using ExecutionViewModel vm = fixture.CreateExecution();
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        vm.IsResumeMode = true;
        vm.ResumePartialPath = fixture.Checkpoint.PartialPath;
        Assert.False(vm.CanStart);
        await vm.PrepareResumeAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.ResumeReport?.CanResume);
        Assert.True(vm.CanStart);
        Assert.Equal(0, fixture.Runner.CallCount);
        vm.SelectedModelId = "other-model";
        Assert.False(vm.CanStart);
        await vm.PrepareResumeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CheckpointAdmissionStatusCodes.ModelMismatch, vm.ResumeReport?.BlockingStatusCode);
        vm.ApplyCheckpointModelCommand.Execute(null);
        await vm.PrepareResumeAsync(TestContext.Current.CancellationToken);
        await vm.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(fixture.Checkpoint.PartialPath, fixture.Runner.LastRequest?.ResumePartialPath);
        Assert.Null(fixture.Runner.LastRequest?.OutputDirectory);
        Assert.Equal(1, fixture.Runner.CallCount);
    }

    [Fact]
    public async Task Interrupted_handoff_is_enabled_after_completion_and_only_prepares()
    {
        using Fixture fixture = new();
        using ExecutionViewModel vm = fixture.CreateExecution();
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        bool enabledNotification = false;
        vm.ResumeInterruptedRunCommand.CanExecuteChanged += (_, _) =>
            enabledNotification |= vm.ResumeInterruptedRunCommand.CanExecute(null);
        await vm.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.HasInterruptedRun);
        Assert.True(enabledNotification);
        TaskCompletionSource prepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnResumeStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(ExecutionViewModel.IsPreparingResume)
                && !vm.IsPreparingResume && vm.ResumeReport is not null)
                prepared.TrySetResult();
        }
        vm.PropertyChanged += OnResumeStateChanged;
        try
        {
            // Execute is fire-and-forget: keep checkpoint files alive until inspection finishes.
            vm.ResumeInterruptedRunCommand.Execute(null);
            await prepared.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            vm.PropertyChanged -= OnResumeStateChanged;
        }
        Assert.True(vm.ResumeReport?.CanResume);
        Assert.True(vm.IsResumeMode);
        Assert.Equal(fixture.Checkpoint.PartialPath, vm.ResumePartialPath);
        Assert.Equal(1, fixture.Runner.CallCount);
    }

    [Fact]
    public async Task Previous_run_progress_cannot_overwrite_new_run()
    {
        using Fixture fixture = new();
        List<Action<EvaluationProgress>?> callbacks = [];
        TaskCompletionSource<RunSummary> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRunBoundary runner = new((_, progress, _) =>
        {
            callbacks.Add(progress);
            return callbacks.Count == 1 ? Task.FromResult(fixture.Summary) : pending.Task;
        });
        using ExecutionViewModel vm = fixture.CreateExecution(runner);
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        await vm.StartAsync(TestContext.Current.CancellationToken);
        Task second = vm.StartAsync(TestContext.Current.CancellationToken);
        callbacks[1]!(new EvaluationProgress(10, 2, 1, EvaluationProgressStatus.Running));
        callbacks[0]!(new EvaluationProgress(99, 90, 1, EvaluationProgressStatus.Running));
        Assert.Equal(10, vm.ProgressTotal);
        Assert.Equal(2, vm.ProgressCompleted);
        pending.SetResult(fixture.Summary);
        await second;
    }

    [Fact]
    public async Task Drain_is_bounded_cancels_active_run_and_prevents_restart()
    {
        using Fixture fixture = new();
        ControlledRunBoundary runner = new(fixture.Summary);
        using ExecutionViewModel vm = fixture.CreateExecution(runner);
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.CanStart);
        Task running = vm.StartAsync(TestContext.Current.CancellationToken);
        await runner.Started.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await vm.StopAndDrainAsync(TimeSpan.Zero);
        Assert.True(runner.CancellationObserved.IsCompleted);
        Assert.False(vm.CanStart);
        Assert.False(vm.CanEditResume);
        Assert.False(running.IsCompleted);
        runner.Release();
        await running;
        Assert.False(vm.CanStart);
    }

    [Fact]
    public async Task First_input_load_works_and_failed_replacement_preserves_loaded_state()
    {
        using Fixture fixture = new();
        InputViewModel input = new();
        Assert.True(await input.TryLoadCheckpointInputAsync(fixture.Workbook.Path, TestContext.Current.CancellationToken));
        Assert.True(input.HasLoadedWorkbook);
        QuantificationDefinition draft = input.DefinitionDraft;
        var metadata = input.Metadata;
        var snapshot = input.Snapshot;
        Assert.False(await input.TryLoadCheckpointInputAsync(
            Path.Combine(fixture.Workbook.Directory, "missing.xlsx"), TestContext.Current.CancellationToken));
        Assert.Equal(fixture.Workbook.Path, input.FilePath);
        Assert.Same(draft, input.DefinitionDraft);
        Assert.Same(metadata, input.Metadata);
        Assert.Same(snapshot, input.Snapshot);
    }

    [Fact]
    public async Task An_interrupted_run_stays_on_the_execution_step_instead_of_announcing_results()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Responses", 1, 2, 1, new X02Header(1, "Report answer"));
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        RecordingRunBoundary runner = new((_, _, _) => Task.FromResult(DurableCancelledSummary(input)));
        WorkflowNavigator navigator = new();
        using MainWindowViewModel shell = new(
            navigator,
            input,
            new QuantificationDesignViewModel(input.DefinitionDraft, input.AvailableColumnNames),
            Execution(runner),
            new ResultsOutputViewModel(new RecordingOutputBoundary()));
        shell.NextCommand.Execute(null);
        shell.NextCommand.Execute(null);
        ExecutionViewModel execution = shell.ExecutionViewModel;
        await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WorkflowStep.Execution, navigator.CurrentStep);
        Assert.True(execution.CanStart);

        await execution.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WorkflowStep.Execution, navigator.CurrentStep);
        Assert.True(execution.HasInterruptedRun);
        Assert.Equal(1, runner.CallCount);
    }

    [AvaloniaFact]
    public async Task Picker_cancel_preserves_selection_and_never_starts_AI()
    {
        using Fixture fixture = new();
        using ExecutionViewModel vm = fixture.CreateExecution();
        vm.IsResumeMode = true;
        vm.ResumePartialPath = fixture.Checkpoint.PartialPath;
        ExecutionPreparationPanel view = new(new CancelPicker()) { DataContext = vm };
        Window window = new() { Content = view, Width = 1024, Height = 720 };
        window.Show();
        try
        {
            await view.PickResumeCheckpointAsync();
            Assert.Equal(fixture.Checkpoint.PartialPath, vm.ResumePartialPath);
            Assert.True(vm.IsResumeMode);
            Assert.Null(vm.ResumeReport);
            Assert.Equal(0, fixture.Runner.CallCount);
        }
        finally { window.Close(); }
    }

    private static RunSummary DurableCancelledSummary(InputViewModel input)
    {
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(input.DefinitionDraft);
        ValidatedColumnMapping mapping = new ColumnMappingValidator()
            .Validate(input.Metadata!, snapshot.Definition).Mapping!;
        return new RunSummary(
            new EvaluationScheduleResult(new EvaluationPlanBuilder().Build(snapshot, mapping), []),
            input.Snapshot!,
            QuantificationRunStatusCodes.Cancelled,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            isDurable: true,
            partialPath: Path.Combine(Path.GetTempPath(), "resume-workflow.partial.xlsx"));
    }

    private static ExecutionViewModel Execution(IQuantificationRunBoundary runner) => new(
        new RecordingAuthenticationBoundary(new ExecutionAuthenticationSnapshot(
            ExecutionAuthenticationState.Available,
            [U04TestSupport.Model("model-test"), U04TestSupport.AutoModel()],
            U04TestSupport.RuntimeIdentity())),
        runner);

    [Fact]
    public async Task Drain_with_an_unbounded_timeout_still_returns_within_the_finite_bound()
    {
        using Fixture fixture = new();
        ControlledRunBoundary runner = new(fixture.Summary);
        using ExecutionViewModel vm = fixture.CreateExecution(runner);
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Task running = vm.StartAsync(TestContext.Current.CancellationToken);
        await runner.Started.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        try
        {
            // The run ignores cancellation until released; close must not wait for it indefinitely.
            System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
            await vm.StopAndDrainAsync(Timeout.InfiniteTimeSpan)
                .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(15));
            Assert.False(running.IsCompleted);
        }
        finally
        {
            runner.Release();
            await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task Window_close_during_an_unresponsive_run_closes_after_the_ten_second_bound()
    {
        using Fixture fixture = new();
        InputViewModel input = new();
        await input.SetFilePathAsync(fixture.Workbook.Path, TestContext.Current.CancellationToken);
        ControlledRunBoundary runner = new(fixture.Summary);
        using MainWindowViewModel shell = new(new WorkflowNavigator(), input,
            new QuantificationDesignViewModel(input.DefinitionDraft, input.AvailableColumnNames),
            fixture.CreateExecution(runner), new ResultsOutputViewModel(new RecordingOutputBoundary()));
        shell.NextCommand.Execute(null);
        shell.NextCommand.Execute(null);
        MainWindow window = new(shell);
        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        Task? running = null;
        try
        {
            await shell.ExecutionViewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
            running = shell.ExecutionViewModel.StartAsync(TestContext.Current.CancellationToken);
            await runner.Started.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(15));
            Assert.False(running.IsCompleted);
        }
        finally
        {
            runner.Release();
            if (running is not null)
                await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Window_close_without_a_run_closes_immediately()
    {
        using Fixture fixture = new();
        InputViewModel input = new();
        await input.SetFilePathAsync(fixture.Workbook.Path, TestContext.Current.CancellationToken);
        using MainWindowViewModel shell = new(new WorkflowNavigator(), input,
            new QuantificationDesignViewModel(input.DefinitionDraft, input.AvailableColumnNames),
            fixture.CreateExecution(), new ResultsOutputViewModel(new RecordingOutputBoundary()));
        MainWindow window = new(shell);
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();

        window.Close();

        Assert.True(closed);
        Assert.Equal(0, fixture.Runner.CallCount);
    }

    [AvaloniaFact]
    public async Task Repeated_window_close_waits_for_run_completion()
    {
        using Fixture fixture = new();
        InputViewModel input = new();
        await input.SetFilePathAsync(fixture.Workbook.Path, TestContext.Current.CancellationToken);
        ControlledRunBoundary runner = new(fixture.Summary);
        using MainWindowViewModel shell = new(new WorkflowNavigator(), input,
            new QuantificationDesignViewModel(input.DefinitionDraft, input.AvailableColumnNames),
            fixture.CreateExecution(runner), new ResultsOutputViewModel(new RecordingOutputBoundary()));
        shell.NextCommand.Execute(null);
        shell.NextCommand.Execute(null);
        MainWindow window = new(shell);
        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        Task? running = null;
        try
        {
            await shell.ExecutionViewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
            Assert.True(shell.ExecutionViewModel.CanStart);
            running = shell.ExecutionViewModel.StartAsync(TestContext.Current.CancellationToken);
            await runner.Started.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            window.Close();
            window.Close();
            Assert.False(closed.Task.IsCompleted);
            Assert.True(runner.CancellationObserved.IsCompleted);
            runner.Release();
            await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            runner.Release();
            if (running is not null)
                await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            window.Close();
        }
    }

    private sealed class CancelPicker : IResumeCheckpointPicker
    {
        public Task<string?> PickAsync(TopLevel topLevel) => Task.FromResult<string?>(null);
    }

    private sealed class Fixture : IDisposable
    {
        public X02TemporaryWorkbook Workbook { get; } = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original", 1, 2, 3, new X02Header(1, "Report answer"), new X02Header(2, "Support"));
        public QuantificationDefinition Definition { get; } = U04TestSupport.Definition(2, 2);
        public CheckpointEnvelope Checkpoint { get; }
        public RunSummary Summary { get; }
        public RecordingRunBoundary Runner { get; }

        public Fixture(string? effort = null, string? tier = null)
        {
            QuantificationSnapshot snapshot = QuantificationSnapshot.Create(Definition);
            var runtime = U04TestSupport.RuntimeIdentity();
            InputSnapshot input = new InputSnapshotService().Capture(Workbook.Path);
            Checkpoint = new CheckpointEnvelope
            {
                ReasoningEffort = effort,
                ContextTier = tier,
                InputPath = Workbook.Path, Input = input,
                DefinitionCanonicalJson = snapshot.CanonicalJson, DefinitionSha256 = snapshot.Sha256,
                NormalModelId = "model-test",
                ReferenceModelId = "model-test",
                Runtime = new CheckpointRuntimeIdentity
                {
                    ApplicationIdentity = QuantificationRunBoundary.ApplicationIdentity(),
                    CliVersion = runtime.CliVersion, CliSha256 = runtime.CliSha256,
                    SdkInformationalVersion = runtime.SdkInformationalVersion,
                },
                FinalPath = Path.Combine(Workbook.Directory, "eval.xlsx"),
                PartialPath = Path.Combine(Workbook.Directory, "eval.partial.xlsx"),
                StartedAtUtc = DateTimeOffset.UnixEpoch, SavedAtUtc = DateTimeOffset.UnixEpoch,
            };
            Assert.True(new CheckpointStore().Create(Checkpoint).IsSuccess);
            EvaluationPlan plan = U01TestSupport.Plan(Definition);
            var units = plan.Items.Select(item => new EvaluationUnitResult(item,
                StudyReportEvaluator.App.Workbooks.Writing.ResultsStatusCodes.Cancelled,
                null, 0, false, false, StudyReportEvaluator.App.Copilot.EvaluationTokenUsage.Unavailable))
                .ToArray();
            Summary = new RunSummary(new EvaluationScheduleResult(plan, [.. units]),
                input, QuantificationRunStatusCodes.Cancelled, DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch, isDurable: true, partialPath: Checkpoint.PartialPath);
            Runner = new RecordingRunBoundary((_, _, _) => Task.FromResult(Summary));
        }

        public ExecutionViewModel CreateExecution(IQuantificationRunBoundary? runner = null)
        {
            ExecutionViewModel vm = new(new RecordingAuthenticationBoundary(new ExecutionAuthenticationSnapshot(
                ExecutionAuthenticationState.Available,
                [U04TestSupport.Model("model-test"), U04TestSupport.Model("other-model"), U04TestSupport.AutoModel()],
                U04TestSupport.RuntimeIdentity())), runner ?? Runner);
            vm.Configure(Definition, new StudyReportEvaluator.App.Workbooks.Reading.WorkbookMetadataReader().Read(Workbook.Path), Workbook.Path);
            return vm;
        }

        public void Dispose() => Workbook.Dispose();
    }
}