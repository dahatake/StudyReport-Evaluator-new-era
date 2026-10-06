using System.Collections.Specialized;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

public sealed class ExecutionSettingsTests
{
    private static readonly TimeSpan TestWait = TimeSpan.FromSeconds(10);

    [Fact]
    public void Output_override_survives_input_changes_and_restore_while_null_recomputes_result()
    {
        using SettingsHarness harness = new();
        ExecutionViewModel viewModel = harness.ViewModel;
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.Null(viewModel.OutputDirectoryOverride);
        Assert.Equal(string.Empty, viewModel.OutputDirectory);
        harness.Configure();
        Assert.Equal(DefaultOutput(harness.InputPath), viewModel.OutputDirectory);
        Assert.Null(viewModel.OutputDirectoryOverride);

        viewModel.OutputDirectory = harness.ExplicitOutput;
        harness.Configure(harness.OtherInputPath);
        Assert.Equal(harness.ExplicitOutput, viewModel.OutputDirectoryOverride);
        Assert.Equal(harness.ExplicitOutput, viewModel.OutputDirectory);

        // The parent Settings VM exports explicit values, not the computed OutputDirectory.
        ApplicationSettings saved = new()
        {
            PreferredModelId = viewModel.PreferredModelId,
            MaxConcurrency = viewModel.MaxConcurrency,
            OutputDirectoryOverride = viewModel.OutputDirectoryOverride,
        };
        using SettingsHarness restored = new();
        restored.ViewModel.ApplySettings(saved);
        Assert.False(restored.ViewModel.IsConfigured);
        Assert.Equal(harness.ExplicitOutput, restored.ViewModel.OutputDirectory);
        restored.Configure(restored.OtherInputPath);
        Assert.Equal(harness.ExplicitOutput, restored.ViewModel.OutputDirectoryOverride);
        Assert.Equal(harness.ExplicitOutput, restored.ViewModel.OutputDirectory);

        viewModel.OutputDirectory = " \t ";
        Assert.Null(viewModel.OutputDirectoryOverride);
        Assert.Equal(DefaultOutput(harness.OtherInputPath), viewModel.OutputDirectory);
        harness.Configure();
        Assert.Equal(DefaultOutput(harness.InputPath), viewModel.OutputDirectory);

        restored.ViewModel.ApplySettings(saved with { OutputDirectoryOverride = null });
        Assert.Null(restored.ViewModel.OutputDirectoryOverride);
        Assert.Equal(DefaultOutput(restored.OtherInputPath), restored.ViewModel.OutputDirectory);
        using SettingsHarness defaultRestored = new();
        defaultRestored.ViewModel.ApplySettings(saved with { OutputDirectoryOverride = null });
        Assert.Equal(string.Empty, defaultRestored.ViewModel.OutputDirectory);
        defaultRestored.Configure();
        Assert.Equal(DefaultOutput(defaultRestored.InputPath), defaultRestored.ViewModel.OutputDirectory);

        // Even an edit equal to the displayed default is an explicit output choice.
        string explicitDefault = viewModel.OutputDirectory;
        viewModel.OutputDirectory = explicitDefault;
        Assert.Equal(explicitDefault, viewModel.OutputDirectoryOverride);
        harness.Configure(harness.OtherInputPath);
        Assert.Equal(explicitDefault, viewModel.OutputDirectory);
        viewModel.OutputDirectoryOverride = null;
        Assert.Equal(DefaultOutput(harness.OtherInputPath), viewModel.OutputDirectory);

        Assert.Contains(nameof(ExecutionViewModel.OutputDirectoryOverride), changed);
        Assert.Contains(nameof(ExecutionViewModel.OutputDirectory), changed);
        foreach (SettingsHarness item in new[] { harness, restored, defaultRestored })
        {
            Assert.False(Directory.Exists(item.Root));
            Assert.Equal(0, item.Authentication.CallCount);
            AssertNoLoginOrRun(item);
        }
    }

    [Fact]
    public async Task Unavailable_output_override_blocks_start_without_fallback_or_directory_creation()
    {
        using SettingsHarness harness = new();
        string unavailable = Path.Combine(harness.Root, "existing-file");
        byte[] sentinel = [1, 2, 3, 4];
        Directory.CreateDirectory(harness.Root);
        try
        {
            await File.WriteAllBytesAsync(unavailable, sentinel, TestContext.Current.CancellationToken);
            ExecutionViewModel viewModel = harness.ViewModel;
            viewModel.ApplySettings(new ApplicationSettings
            {
                PreferredModelId = "model-b",
                OutputDirectoryOverride = unavailable,
            });
            harness.Configure();
            await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
            harness.Configure(harness.OtherInputPath);

            Assert.Equal(unavailable, viewModel.OutputDirectoryOverride);
            Assert.Equal(unavailable, viewModel.OutputDirectory);
            Assert.Contains(viewModel.TechnicalErrors, error => error.Code == "OUTPUT_DIRECTORY_INVALID");
            Assert.False(viewModel.CanStart);
            await viewModel.StartAsync(TestContext.Current.CancellationToken);

            Assert.False(Directory.Exists(unavailable));
            Assert.False(Directory.Exists(DefaultOutput(harness.InputPath)));
            Assert.False(Directory.Exists(DefaultOutput(harness.OtherInputPath)));
            Assert.Equal(sentinel, await File.ReadAllBytesAsync(unavailable, TestContext.Current.CancellationToken));
            AssertNoLoginOrRun(harness);
        }
        finally
        {
            Directory.Delete(harness.Root, recursive: true);
        }
    }

    [Theory]
    [InlineData("out")]
    [InlineData(@"..\out")]
    [InlineData("C:out")]
    public async Task Relative_explicit_output_blocks_new_run_without_fallback(string outputDirectory)
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        viewModel.OutputDirectoryOverride = outputDirectory;

        Assert.Equal(outputDirectory, viewModel.OutputDirectoryOverride);
        Assert.Equal(outputDirectory, viewModel.OutputDirectory);
        ExecutionTechnicalError error = Assert.Single(viewModel.TechnicalErrors);
        Assert.Equal("OUTPUT_DIRECTORY_INVALID", error.Code);
        Assert.Contains("絶対パス", error.Message, StringComparison.Ordinal);
        Assert.False(viewModel.CanStart);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        viewModel.StartCommand.Execute(null);
        await viewModel.StartAsync(TestContext.Current.CancellationToken);

        // Execution and persistence must reject the same explicit relative value.
        SettingsFileStore store = new(Path.Combine(harness.Root, "setting.txt"));
        SettingsSaveResult saved = await store.SaveAsync(new ApplicationSettings
        {
            OutputDirectoryOverride = outputDirectory,
        }, TestContext.Current.CancellationToken);
        Assert.Equal(SettingsSaveStatus.InvalidSettings, saved.Status);
        Assert.False(viewModel.HasCurrentRun);
        Assert.Null(harness.Runner.LastRequest);
        Assert.False(Directory.Exists(harness.Root));
        Assert.Equal(1, harness.Authentication.CallCount);
        AssertNoLoginOrRun(harness);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("absolute")]
    public async Task Fully_qualified_and_empty_output_choices_reach_the_new_run_without_fallback(string? choice)
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        string? explicitOutput = choice == "absolute" ? harness.ExplicitOutput : choice;
        string expectedOutput = choice == "absolute" ? harness.ExplicitOutput : DefaultOutput(harness.InputPath);
        viewModel.OutputDirectoryOverride = explicitOutput;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(harness.Definition, harness.Metadata);
        harness.Run = (_, _, _) => Task.FromResult(summary);
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.CanStart);
        Assert.Empty(viewModel.TechnicalErrors);
        Assert.Equal(choice == "absolute" ? harness.ExplicitOutput : null, viewModel.OutputDirectoryOverride);
        Assert.Equal(expectedOutput, viewModel.OutputDirectory);
        await viewModel.StartAsync(TestContext.Current.CancellationToken);

        QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
        Assert.Equal(expectedOutput, request.OutputDirectory);
        Assert.Equal(harness.InputPath, request.InputPath);
        Assert.Null(request.ResumePartialPath);
        Assert.Equal("前回run 新規出力先: " + expectedOutput, viewModel.CurrentRunOutputSummary);
        Assert.Equal(1, harness.Runner.CallCount);
        Assert.Equal(1, harness.Authentication.CallCount);
        Assert.Equal(0, harness.FactoryCallCount);
        Assert.Null(viewModel.LastLoginTask);
        Assert.False(Directory.Exists(harness.Root));
    }

    [Fact]
    public async Task Resume_ignores_relative_new_run_output_and_never_falls_back_from_a_missing_checkpoint()
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(harness.Definition, harness.Metadata);
        harness.Run = (_, _, _) => Task.FromResult(summary);
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        viewModel.OutputDirectoryOverride = "out";
        Assert.False(viewModel.CanStart);
        viewModel.IsResumeMode = true;
        await viewModel.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, harness.Runner.CallCount);
        Assert.Contains(viewModel.TechnicalErrors, error => error.Code == "RESUME_PARTIAL_REQUIRED");
        Assert.DoesNotContain(viewModel.TechnicalErrors, error => error.Code == "OUTPUT_DIRECTORY_INVALID");

        Directory.CreateDirectory(harness.Root);
        string checkpoint = Path.Combine(harness.Root, "chosen.partial.xlsx");
        try
        {
            // Only the existing VM file/path gate is exercised; checkpoint admission belongs to the boundary.
            await File.WriteAllBytesAsync(checkpoint, [1], TestContext.Current.CancellationToken);
            harness.ResumeInspection.Admit(harness, checkpoint);
            viewModel.ResumePartialPath = checkpoint;
            await viewModel.PrepareResumeAsync(TestContext.Current.CancellationToken);
            Assert.True(viewModel.ResumeReport?.CanResume);
            Assert.True(viewModel.CanStart);
            await viewModel.StartAsync(TestContext.Current.CancellationToken);
            QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
            Assert.Equal(checkpoint, request.ResumePartialPath);
            Assert.Null(request.OutputDirectory);
            Assert.Equal("out", viewModel.OutputDirectoryOverride);
            Assert.Equal("前回run 再開: " + checkpoint, viewModel.CurrentRunOutputSummary);

            viewModel.ResumePartialPath = Path.Combine(harness.Root, "missing.partial.xlsx");
            Assert.False(viewModel.CanStart);
            await viewModel.StartAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, harness.Runner.CallCount);
            Assert.Same(request, harness.Runner.LastRequest);
            Assert.Contains(viewModel.TechnicalErrors, error => error.Code == "RESUME_PARTIAL_REQUIRED");
            viewModel.IsResumeMode = false;
            Assert.False(viewModel.CanStart);
            Assert.Contains(viewModel.TechnicalErrors, error => error.Code == "OUTPUT_DIRECTORY_INVALID");
            await viewModel.StartAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, harness.Runner.CallCount);
            Assert.False(Directory.Exists(DefaultOutput(harness.InputPath)));
            Assert.Equal(1, harness.Authentication.CallCount);
            Assert.Equal(0, harness.FactoryCallCount);
            Assert.Null(viewModel.LastLoginTask);
        }
        finally
        {
            Directory.Delete(harness.Root, recursive: true);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Apply_settings_restores_only_common_values_without_authentication_login_or_run(int concurrency)
    {
        using SettingsHarness harness = new();
        ExecutionViewModel viewModel = harness.ViewModel;
        string runtimeText = viewModel.RuntimeIdentityText;
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        Assert.Contains("入力と定量化設計を完了", viewModel.ValidationSummary, StringComparison.Ordinal);
        viewModel.ApplySettings(new ApplicationSettings
        {
            PreferredModelId = "model-b",
            MaxConcurrency = concurrency,
            OutputDirectoryOverride = harness.ExplicitOutput,
            Definition = harness.Definition with { LastDataRow = 100 },
        });

        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.Null(viewModel.SelectedModelId);
        Assert.Equal(concurrency, viewModel.MaxConcurrency);
        Assert.Equal(Enumerable.Range(1, 16).ToArray(), viewModel.ConcurrencyOptions);
        Assert.Equal(harness.ExplicitOutput, viewModel.OutputDirectoryOverride);
        Assert.False(viewModel.IsConfigured);
        Assert.Equal(0, viewModel.PlannedEvaluationCount);
        Assert.Equal(ExecutionAuthenticationState.NotChecked, viewModel.AuthenticationState);
        Assert.False(viewModel.IsAuthenticationAvailable);
        Assert.Equal(runtimeText, viewModel.RuntimeIdentityText);
        Assert.Empty(viewModel.AvailableModelIds);
        Assert.Contains(nameof(ExecutionViewModel.PreferredModelId), changed);
        Assert.Contains(nameof(ExecutionViewModel.OutputDirectoryOverride), changed);
        Assert.Null(typeof(ExecutionViewModel).GetProperty("Settings"));
        await viewModel.StartAsync(TestContext.Current.CancellationToken);
        Assert.False(viewModel.HasCurrentRun);
        Assert.Null(viewModel.CurrentRunModelId);
        Assert.Null(viewModel.CurrentRunMaxConcurrency);
        Assert.Equal(string.Empty, viewModel.CurrentRunOutputSummary);

        harness.Configure();
        Assert.Equal(5, viewModel.PlannedEvaluationCount);
        Assert.Equal(concurrency, viewModel.MaxConcurrency);
        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.False(viewModel.CanStart);
        Assert.Contains("技術的な問題", viewModel.ValidationSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("開始できます", viewModel.ValidationSummary, StringComparison.Ordinal);
        await viewModel.StartAsync(TestContext.Current.CancellationToken);
        Assert.False(viewModel.HasCurrentRun);
        Assert.False(Directory.Exists(harness.Root));
        Assert.Equal(0, harness.Authentication.CallCount);
        AssertNoLoginOrRun(harness);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(int.MaxValue)]
    public async Task Restored_concurrency_outside_one_to_sixteen_is_blocked_without_clamping(int concurrency)
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        viewModel.ApplySettings(new ApplicationSettings { PreferredModelId = "model-b", MaxConcurrency = concurrency });
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(concurrency, viewModel.MaxConcurrency);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.Contains(viewModel.TechnicalErrors, error => error.Code == "CONCURRENCY_OUT_OF_RANGE");
        Assert.False(viewModel.CanStart);
        await viewModel.StartAsync(TestContext.Current.CancellationToken);
        AssertNoLoginOrRun(harness);
    }

    [Theory]
    [InlineData("model-b", "model-b")]
    [InlineData("MODEL-B", null)]
    [InlineData("model-missing", null)]
    public async Task Preferred_model_requires_an_exact_confirmed_match_and_never_falls_back(
        string preference,
        string? expectedSelection)
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        viewModel.ApplySettings(new ApplicationSettings { PreferredModelId = preference });
        Assert.Equal(preference, viewModel.PreferredModelId);
        Assert.Null(viewModel.SelectedModelId);
        Assert.False(viewModel.IsAuthenticationAvailable);
        await viewModel.StartAsync(TestContext.Current.CancellationToken);

        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(preference, viewModel.PreferredModelId);
        Assert.Equal(expectedSelection, viewModel.SelectedModelId);
        Assert.Equal(expectedSelection is not null, viewModel.CanStart);
        Assert.Equal(new[] { "model-a", "model-b", "auto" }, viewModel.AvailableModelIds);
        if (expectedSelection is null)
        {
            Assert.Contains(viewModel.TechnicalErrors, error => error.Code == "MODEL_SELECTION_REQUIRED");
            await viewModel.StartAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, harness.Authentication.CallCount);
        AssertNoLoginOrRun(harness);
    }

    [Fact]
    public async Task Missing_auto_does_not_block_start_for_confirmed_selected_model()
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        viewModel.ApplySettings(new ApplicationSettings { PreferredModelId = "model-b" });
        harness.Authentication.Snapshot = Available("model-a", "model-b");
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(harness.Definition, harness.Metadata);
        harness.Run = (_, _, _) => Task.FromResult(summary);

        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsAuthenticationAvailable);
        Assert.Equal(new[] { "model-a", "model-b" }, viewModel.AvailableModelIds);
        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.Empty(viewModel.TechnicalErrors);
        Assert.False(viewModel.HasTechnicalErrors);
        Assert.True(viewModel.IsTechnicallyValid);
        Assert.True(viewModel.CanStart);
        Assert.True(viewModel.StartCommand.CanExecute(null));

        await viewModel.StartAsync(TestContext.Current.CancellationToken);

        QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
        Assert.Equal("model-b", request.ModelId);
        Assert.Same(summary, viewModel.LastRunContext?.Summary);
        Assert.Equal(1, harness.Authentication.CallCount);
        Assert.Equal(1, harness.Runner.CallCount);
        Assert.Null(viewModel.LastLoginTask);
    }

    [Fact]
    public async Task Auto_only_recheck_keeps_the_preferred_normal_model_unselected()
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        viewModel.ApplySettings(new ApplicationSettings { PreferredModelId = "model-b" });
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.True(viewModel.CanStart);
        harness.Authentication.Snapshot = Available("auto");

        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsAuthenticationAvailable);
        Assert.Equal("auto", Assert.Single(viewModel.AvailableModelIds));
        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.Null(viewModel.SelectedModelId);
        Assert.Equal("MODEL_SELECTION_REQUIRED", Assert.Single(viewModel.TechnicalErrors).Code);
        Assert.False(viewModel.CanStart);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        viewModel.StartCommand.Execute(null);
        await viewModel.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, harness.Authentication.CallCount);
        Assert.Null(harness.Runner.LastRequest);
        AssertNoLoginOrRun(harness);
    }

    [Fact]
    public async Task Auto_model_is_not_a_fallback_for_a_missing_preferred_normal_model()
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        viewModel.ApplySettings(new ApplicationSettings { PreferredModelId = "model-b" });
        harness.Authentication.Snapshot = Available("auto");

        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.Equal("auto", Assert.Single(viewModel.AvailableModelIds));
        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.Null(viewModel.SelectedModelId);
        Assert.False(viewModel.CanStart);
        Assert.Contains(viewModel.TechnicalErrors, error => error.Code == "MODEL_SELECTION_REQUIRED");
        await viewModel.StartAsync(TestContext.Current.CancellationToken);
        AssertNoLoginOrRun(harness);
    }

    [Theory]
    [InlineData(ExecutionAuthenticationState.AuthRequired)]
    [InlineData(ExecutionAuthenticationState.CliUnavailable)]
    [InlineData(ExecutionAuthenticationState.RuntimeFailed)]
    [InlineData(ExecutionAuthenticationState.Cancelled)]
    public async Task Authentication_refresh_and_failure_keep_preference_despite_binding_null_feedback(
        ExecutionAuthenticationState failure)
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        viewModel.ApplySettings(new ApplicationSettings { PreferredModelId = "model-b" });
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        ((INotifyCollectionChanged)viewModel.AvailableModelIds).CollectionChanged +=
            (_, _) => viewModel.SelectedModelId = null;
        TaskCompletionSource<ExecutionAuthenticationSnapshot> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Authentication.CheckOverride = token => completion.Task.WaitAsync(token);

        Task checking = viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(viewModel.IsCheckingAuthentication);
            Assert.Equal(ExecutionAuthenticationState.Checking, viewModel.AuthenticationState);
            Assert.Equal("model-b", viewModel.PreferredModelId);
            Assert.Null(viewModel.SelectedModelId);
            Assert.Equal(new[] { "model-a", "model-b", "auto" }, viewModel.AvailableModelIds);
            Assert.False(viewModel.CanStart);
        }
        finally
        {
            completion.TrySetResult(new ExecutionAuthenticationSnapshot(failure));
            await checking.WaitAsync(TestWait, TestContext.Current.CancellationToken);
        }

        Assert.Equal(failure, viewModel.AuthenticationState);
        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.Null(viewModel.SelectedModelId);
        Assert.False(viewModel.CanStart);
        // An already-empty two-way selection must not erase a preference during reattachment.
        viewModel.SelectedModelId = null;
        Assert.Equal("model-b", viewModel.PreferredModelId);
        harness.Authentication.CheckOverride = null;
        harness.Authentication.Snapshot = Available("model-a", "auto");
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Null(viewModel.SelectedModelId);
        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.False(viewModel.CanStart);

        harness.Authentication.Snapshot = Available("model-a", "model-b", "auto");
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.True(viewModel.CanStart);
        Assert.Equal(4, harness.Authentication.CallCount);
        AssertNoLoginOrRun(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authentication_exception_or_cancellation_keeps_preference_for_explicit_recheck(bool cancel)
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        viewModel.ApplySettings(new ApplicationSettings { PreferredModelId = "model-b" });
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<ExecutionAuthenticationSnapshot> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Authentication.CheckOverride = token => cancel
            ? pending.Task.WaitAsync(token)
            : Task.FromException<ExecutionAuthenticationSnapshot>(new InvalidOperationException("PRIVATE-T06-AUTH-CANARY"));

        Task checking = viewModel.CheckAuthenticationAsync(cancellation.Token);
        if (cancel)
        {
            cancellation.Cancel();
        }

        await checking.WaitAsync(TestWait, TestContext.Current.CancellationToken);
        Assert.Equal(cancel ? ExecutionAuthenticationState.Cancelled : ExecutionAuthenticationState.RuntimeFailed,
            viewModel.AuthenticationState);
        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.Null(viewModel.SelectedModelId);
        Assert.False(viewModel.CanStart);
        Assert.DoesNotContain("PRIVATE-T06-AUTH-CANARY", viewModel.AuthenticationStatusText, StringComparison.Ordinal);
        Assert.All(viewModel.TechnicalErrors, error =>
            Assert.DoesNotContain("PRIVATE-T06-AUTH-CANARY", error.Message, StringComparison.Ordinal));

        harness.Authentication.CheckOverride = null;
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.True(viewModel.CanStart);
        AssertNoLoginOrRun(harness);
    }

    [Fact]
    public async Task Initial_model_selection_is_session_only_and_does_not_fall_back_on_recheck()
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        viewModel.ApplySettings(new ApplicationSettings());
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Equal("model-a", viewModel.SelectedModelId);
        Assert.Null(viewModel.PreferredModelId);

        harness.Authentication.Snapshot = Available("model-b", "model-a", "auto");
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Equal("model-a", viewModel.SelectedModelId);
        harness.Authentication.Snapshot = Available("model-b", "auto");
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Null(viewModel.SelectedModelId);
        Assert.Null(viewModel.PreferredModelId);
        Assert.False(viewModel.CanStart);

        harness.Authentication.Snapshot = new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.AuthRequired);
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        harness.Authentication.Snapshot = Available("model-b", "model-a", "auto");
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Equal("model-a", viewModel.SelectedModelId);
        Assert.Null(viewModel.PreferredModelId);
        Assert.True(viewModel.CanStart);
        AssertNoLoginOrRun(harness);
    }

    [Fact]
    public async Task Explicit_model_selection_updates_preference_and_explicit_clearing_disables_initial_fallback()
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Null(viewModel.PreferredModelId);

        viewModel.SelectedModelId = "model-a";
        Assert.Equal("model-a", viewModel.PreferredModelId);
        viewModel.SelectedModelId = "model-b";
        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        viewModel.SelectedModelId = null;
        Assert.Null(viewModel.PreferredModelId);
        Assert.Null(viewModel.SelectedModelId);
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Null(viewModel.SelectedModelId);
        Assert.False(viewModel.CanStart);

        viewModel.SelectedModelId = "model-missing";
        Assert.Equal("model-missing", viewModel.PreferredModelId);
        Assert.Null(viewModel.SelectedModelId);
        viewModel.ApplySettings(new ApplicationSettings { PreferredModelId = null });
        Assert.Null(viewModel.PreferredModelId);
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Null(viewModel.SelectedModelId);
        Assert.False(viewModel.CanStart);
        AssertNoLoginOrRun(harness);
    }

    [Fact]
    public async Task Settings_applied_during_authentication_use_the_latest_preference_without_another_check()
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        viewModel.ApplySettings(new ApplicationSettings { PreferredModelId = "model-a" });
        TaskCompletionSource<ExecutionAuthenticationSnapshot> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Authentication.CheckOverride = token => completion.Task.WaitAsync(token);

        Task checking = viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        try
        {
            viewModel.ApplySettings(new ApplicationSettings
            {
                PreferredModelId = "model-b",
                MaxConcurrency = 3,
                OutputDirectoryOverride = harness.ExplicitOutput,
            });
            Assert.Equal("model-b", viewModel.PreferredModelId);
            Assert.Null(viewModel.SelectedModelId);
            Assert.False(viewModel.IsAuthenticationAvailable);
            Assert.False(viewModel.CanStart);
        }
        finally
        {
            completion.TrySetResult(Available("model-a", "model-b", "auto"));
            await checking.WaitAsync(TestWait, TestContext.Current.CancellationToken);
        }

        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.Equal(3, viewModel.MaxConcurrency);
        Assert.Equal(harness.ExplicitOutput, viewModel.OutputDirectory);
        Assert.Equal(1, harness.Authentication.CallCount);
        Assert.True(viewModel.CanStart);
        AssertNoLoginOrRun(harness);
    }

    [Fact]
    public async Task Login_preserves_visible_catalog_and_preference_then_automatically_rechecks()
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        viewModel.ApplySettings(new ApplicationSettings { PreferredModelId = "model-b" });
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        ((INotifyCollectionChanged)viewModel.AvailableModelIds).CollectionChanged +=
            (_, _) => viewModel.SelectedModelId = null;

        Task login = viewModel.LoginAsync(TestContext.Current.CancellationToken);
        try
        {
            await harness.Process.Started.WaitAsync(TestWait, TestContext.Current.CancellationToken);
            Assert.True(viewModel.IsLoggingIn);
            Assert.Equal(ExecutionAuthenticationState.NotChecked, viewModel.AuthenticationState);
            Assert.Equal("model-b", viewModel.PreferredModelId);
            Assert.Null(viewModel.SelectedModelId);
            Assert.Equal(new[] { "model-a", "model-b", "auto" }, viewModel.AvailableModelIds);
            Assert.False(viewModel.CanStart);
            Assert.False(viewModel.CanCheckAuthentication);
            Assert.True(viewModel.CanCancelLogin);
            Assert.Equal(1, harness.Authentication.CallCount);
            Assert.Equal(0, harness.Runner.CallCount);
        }
        finally
        {
            harness.Process.Complete(0);
            await login.WaitAsync(TestWait, TestContext.Current.CancellationToken);
        }

        Assert.False(viewModel.IsLoggingIn);
        Assert.True(viewModel.IsAuthenticationAvailable);
        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.Equal(2, harness.Authentication.CallCount);
        Assert.Equal(1, harness.Resolver.CallCount);
        Assert.Equal(1, harness.FactoryCallCount);
        Assert.Equal(1, harness.Process.StartCount);
        Assert.Equal(1, harness.Process.DisposeCount);
        Assert.Empty(harness.Process.KillTreeArguments);

        harness.Authentication.Snapshot = Available("model-a", "auto");
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Null(viewModel.SelectedModelId);
        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.False(viewModel.CanStart);
        harness.Authentication.Snapshot = Available("model-a", "model-b", "auto");
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.True(viewModel.CanStart);
        Assert.Equal(4, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Runner.CallCount);
    }

    [Fact]
    public async Task Restored_model_without_sdk_prompt_limit_starts_without_model_relative_preflight()
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        harness.Authentication.Snapshot = new ExecutionAuthenticationSnapshot(
            ExecutionAuthenticationState.Available,
            [new CopilotModelAvailability("model-b", null, 0), U04TestSupport.Model("auto")],
            U04TestSupport.RuntimeIdentity());
        viewModel.ApplySettings(new ApplicationSettings { PreferredModelId = "model-b" });
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.Null(viewModel.SelectedModelPromptTokenLimit);
        Assert.Equal("SDK未公開・事前検証なし", viewModel.SelectedModelLimitText);
        Assert.DoesNotContain(viewModel.TechnicalErrors, error => error.Code == "MODEL_PROMPT_LIMIT_UNAVAILABLE");
        Assert.True(viewModel.CanStart);
    }

    [Fact]
    public async Task Equivalent_configuration_preserves_last_run_progress_resume_and_explicit_output()
    {
        using SettingsHarness harness = new();
        ExecutionViewModel viewModel = harness.ViewModel;
        viewModel.ApplySettings(new ApplicationSettings
        {
            PreferredModelId = "model-b",
            MaxConcurrency = 2,
            OutputDirectoryOverride = harness.ExplicitOutput,
        });
        RunSummary summary = await CompleteRunAsync(harness);
        ExecutionRunContext context = Assert.IsType<ExecutionRunContext>(viewModel.LastRunContext);
        viewModel.ResumePartialPath = Path.Combine(harness.Root, "chosen.partial.xlsx");
        viewModel.IsResumeMode = true;
        string resumePath = viewModel.ResumePartialPath;
        object?[] presentation = RunPresentation(viewModel);
        QuantificationDefinition equalDraft = CloneDefinition(harness.Definition);
        Assert.NotSame(harness.Definition, equalDraft);
        Assert.False(harness.Definition.Questions.Equals(equalDraft.Questions));
        CanonicalDefinitionSerializer serializer = new();
        Assert.Equal(serializer.Serialize(harness.Definition), serializer.Serialize(equalDraft));
        string equivalentPath = Path.Combine(Path.GetDirectoryName(harness.InputPath)!, ".", Path.GetFileName(harness.InputPath));

        viewModel.Configure(equalDraft, harness.Metadata, equivalentPath);

        Assert.Same(context, viewModel.LastRunContext);
        Assert.Same(summary, viewModel.LastRunContext?.Summary);
        Assert.Equal(presentation, RunPresentation(viewModel));
        Assert.True(viewModel.IsResumeMode);
        Assert.Equal(resumePath, viewModel.ResumePartialPath);
        Assert.Equal(string.Empty, viewModel.ResumeResetReason);
        Assert.Equal(harness.ExplicitOutput, viewModel.OutputDirectoryOverride);
        Assert.Equal(harness.ExplicitOutput, viewModel.OutputDirectory);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.Equal(2, viewModel.MaxConcurrency);
        Assert.Equal(1, harness.Authentication.CallCount);
        Assert.Equal(1, harness.Runner.CallCount);
        Assert.Equal(0, harness.FactoryCallCount);
    }

    [Theory]
    [InlineData(ConfigurationChange.InputPath)]
    [InlineData(ConfigurationChange.MetadataIdentity)]
    [InlineData(ConfigurationChange.Criterion)]
    [InlineData(ConfigurationChange.Prompt)]
    public async Task Changed_configuration_resets_execution_and_resume_but_preserves_output_and_previous_results(
        ConfigurationChange change)
    {
        using SettingsHarness harness = new();
        ExecutionViewModel viewModel = harness.ViewModel;
        RecordingOutputBoundary output = new();
        using ResultsOutputViewModel results = new(output);
        viewModel.RunCompleted += (_, args) => results.Load(args.Context);
        viewModel.ApplySettings(new ApplicationSettings
        {
            PreferredModelId = "model-b",
            MaxConcurrency = 2,
            OutputDirectoryOverride = harness.ExplicitOutput,
        });
        RunSummary previousSummary = await CompleteRunAsync(harness);
        ResultsCriterionViewModel previousEditor = results.Results[0];
        previousEditor.OverrideText = "8";
        string previousCanonical = previousSummary.Snapshot.CanonicalJson;
        viewModel.ResumePartialPath = Path.Combine(harness.Root, "PRIVATE-T06-RESUME-CANARY.partial.xlsx");
        viewModel.IsResumeMode = true;
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        QuantificationDefinition next = change switch
        {
            ConfigurationChange.Criterion => WithChangedCriterion(harness.Definition),
            ConfigurationChange.Prompt => WithChangedPrompt(harness.Definition),
            _ => CloneDefinition(harness.Definition),
        };
        WorkbookMetadata metadata = change == ConfigurationChange.MetadataIdentity
            ? U01TestSupport.ValidateMapping(harness.Definition).Metadata
            : harness.Metadata;
        string input = change == ConfigurationChange.InputPath ? harness.OtherInputPath : harness.InputPath;
        if (change == ConfigurationChange.MetadataIdentity)
        {
            Assert.NotSame(harness.Metadata, metadata);
            Assert.Equal(harness.Metadata.HeaderRowNumber, metadata.HeaderRowNumber);
        }

        viewModel.Configure(next, metadata, input);

        Assert.Null(viewModel.LastRunContext);
        Assert.Equal(0, viewModel.ProgressTotal);
        Assert.Equal(0, viewModel.ProgressCompleted);
        Assert.Equal(0, viewModel.ProgressInFlight);
        Assert.Null(viewModel.ProgressStage);
        Assert.Equal(0, viewModel.ReferenceCompleted);
        Assert.Equal(0, viewModel.RowCompleted);
        Assert.Equal(string.Empty, viewModel.ReservedFinalPath);
        Assert.Equal(string.Empty, viewModel.PartialPath);
        Assert.False(viewModel.IsResumeMode);
        Assert.Equal(string.Empty, viewModel.ResumePartialPath);
        Assert.Contains("再開指定を解除", viewModel.ResumeResetReason, StringComparison.Ordinal);
        Assert.Contains(viewModel.ResumeResetReason, viewModel.OutputModeText, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Root, viewModel.ResumeResetReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE-T06", viewModel.ResumeResetReason, StringComparison.Ordinal);
        Assert.Contains(nameof(ExecutionViewModel.ResumeResetReason), changed);
        Assert.Equal(harness.ExplicitOutput, viewModel.OutputDirectoryOverride);
        Assert.Equal(harness.ExplicitOutput, viewModel.OutputDirectory);
        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.Equal(2, viewModel.MaxConcurrency);
        Assert.True(viewModel.CanStart);
        Assert.True(viewModel.HasCurrentRun);
        Assert.Equal("model-b", viewModel.CurrentRunModelId);
        Assert.Equal(2, viewModel.CurrentRunMaxConcurrency);
        Assert.Equal("前回run 新規出力先: " + harness.ExplicitOutput, viewModel.CurrentRunOutputSummary);
        Assert.Same(previousEditor, results.Results[0]);
        Assert.Equal("8", previousEditor.OverrideText);
        Assert.Equal(5m, previousEditor.AiRawScore);
        Assert.Equal(previousCanonical, previousSummary.Snapshot.CanonicalJson);
        Assert.Equal(0, output.ExportCount);
        Assert.Equal(1, harness.Runner.CallCount);
        Assert.Equal(1, harness.Authentication.CallCount);
        Assert.Equal(0, harness.FactoryCallCount);

        viewModel.IsResumeMode = true;
        Assert.Equal(string.Empty, viewModel.ResumeResetReason);
        Assert.False(viewModel.CanStart);
        Assert.Contains(viewModel.TechnicalErrors, error => error.Code == "RESUME_PARTIAL_REQUIRED");
    }

    [Fact]
    public async Task Current_run_metadata_freezes_before_start_observers_edit_settings_and_updates_only_on_start()
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        viewModel.ApplySettings(new ApplicationSettings
        {
            PreferredModelId = "model-a", MaxConcurrency = 1, OutputDirectoryOverride = harness.ExplicitOutput,
        });
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(harness.Definition, harness.Metadata);
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.False(viewModel.HasCurrentRun);
        Assert.True(viewModel.CanStart);
        TaskCompletionSource<RunSummary> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Run = (_, _, _) => completion.Task;
        string nextOutput = Path.Combine(harness.Root, "next-output");
        bool edited = false;
        (bool HasRun, string? Model, int? Concurrency, string Output) observed = default;
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, args) =>
        {
            changed.Add(args.PropertyName);
            if (!edited && args.PropertyName == nameof(ExecutionViewModel.IsRunning) && viewModel.IsRunning)
            {
                edited = true;
                observed = (viewModel.HasCurrentRun, viewModel.CurrentRunModelId,
                    viewModel.CurrentRunMaxConcurrency, viewModel.CurrentRunOutputSummary);
                // Re-enter settings synchronously, before StartAsync reaches its boundary/await.
                viewModel.ApplySettings(new ApplicationSettings
                {
                    PreferredModelId = "model-b", MaxConcurrency = 3, OutputDirectoryOverride = nextOutput,
                });
            }
        };
        Task run = viewModel.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.False(run.IsCompleted);
            Assert.True(edited);
            Assert.True(observed.HasRun);
            Assert.Equal("model-a", observed.Model);
            Assert.Equal(1, observed.Concurrency);
            Assert.Equal("今回run 新規出力先: " + harness.ExplicitOutput, observed.Output);
            QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
            Assert.Equal(request.ModelId, viewModel.CurrentRunModelId);
            Assert.Equal(request.MaxConcurrency, viewModel.CurrentRunMaxConcurrency);
            Assert.Equal(harness.ExplicitOutput, request.OutputDirectory);
            Assert.Equal("model-a", request.ModelId);
            Assert.Equal(1, request.MaxConcurrency);
            Assert.Equal("model-b", viewModel.SelectedModelId);
            Assert.Equal(3, viewModel.MaxConcurrency);
            Assert.Equal(nextOutput, viewModel.OutputDirectory);
            Assert.False(viewModel.CanStart);
            Assert.Contains("実行中", viewModel.ValidationSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("開始できます", viewModel.ValidationSummary, StringComparison.Ordinal);
            foreach (string name in new[] { nameof(ExecutionViewModel.HasCurrentRun), nameof(ExecutionViewModel.CurrentRunModelId),
                nameof(ExecutionViewModel.CurrentRunMaxConcurrency), nameof(ExecutionViewModel.CurrentRunOutputSummary) })
            {
                Assert.Contains(name, changed);
                var property = typeof(ExecutionViewModel).GetProperty(name);
                Assert.NotNull(property);
                Assert.False(property.CanWrite);
            }
        }
        finally
        {
            completion.TrySetResult(summary);
            await run.WaitAsync(TestWait, TestContext.Current.CancellationToken);
        }

        Assert.True(viewModel.HasCurrentRun);
        Assert.Equal("model-a", viewModel.CurrentRunModelId);
        Assert.Equal(1, viewModel.CurrentRunMaxConcurrency);
        Assert.Equal("前回run 新規出力先: " + harness.ExplicitOutput, viewModel.CurrentRunOutputSummary);
        Assert.Contains("開始できます", viewModel.ValidationSummary, StringComparison.Ordinal);
        Assert.True(viewModel.CanStart);
        viewModel.MaxConcurrency = 17;
        await viewModel.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, harness.Runner.CallCount);
        Assert.Equal("model-a", viewModel.CurrentRunModelId);
        Assert.Equal(1, viewModel.CurrentRunMaxConcurrency);
        Assert.Contains("技術的な問題", viewModel.ValidationSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("開始できます", viewModel.ValidationSummary, StringComparison.Ordinal);

        viewModel.MaxConcurrency = 3;
        harness.Run = (_, _, _) => Task.FromResult(summary);
        await viewModel.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, harness.Runner.CallCount);
        Assert.Equal("model-b", viewModel.CurrentRunModelId);
        Assert.Equal(3, viewModel.CurrentRunMaxConcurrency);
        Assert.Equal("前回run 新規出力先: " + nextOutput, viewModel.CurrentRunOutputSummary);
        foreach (string text in new[] { viewModel.ToString(), harness.Runner.LastRequest!.ToString(), viewModel.LastRunContext!.ToString() })
        {
            foreach (string value in new[] { "model-a", "model-b", harness.InputPath, harness.ExplicitOutput, nextOutput, summary.DefinitionSha256 })
            {
                Assert.DoesNotContain(value, text, StringComparison.Ordinal);
            }
        }
        Assert.False(Directory.Exists(harness.Root));
        Assert.Equal(1, harness.Authentication.CallCount);
        Assert.Equal(0, harness.FactoryCallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Running_settings_edits_preserve_immutable_request_progress_and_stop(bool resume)
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        RunSummary partial = await U04TestSupport.CreateSummaryAsync(harness.Definition, harness.Metadata, cancelAfterFirst: true);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<RunSummary> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int cancellations = 0;
        int completions = 0;
        harness.Run = async (_, progress, token) =>
        {
            using CancellationTokenRegistration registration = token.Register(() =>
            {
                Interlocked.Increment(ref cancellations);
                cancelled.TrySetResult();
            });
            progress?.Invoke(RunningProgress(harness));
            started.TrySetResult();
            return await release.Task;
        };
        viewModel.RunCompleted += (_, _) => completions++;
        string requestedPartial = Path.Combine(harness.Root, "chosen.partial.xlsx");
        string runOutput = resume ? "再開: " + requestedPartial : "新規出力先: " + harness.ExplicitOutput;
        try
        {
            if (resume)
            {
                Directory.CreateDirectory(harness.Root);
                await File.WriteAllBytesAsync(requestedPartial, [1], TestContext.Current.CancellationToken);
                harness.ResumeInspection.Admit(harness, requestedPartial);
            }

            viewModel.ApplySettings(new ApplicationSettings
            {
                PreferredModelId = "model-a",
                MaxConcurrency = 1,
                OutputDirectoryOverride = harness.ExplicitOutput,
            });
            viewModel.IsResumeMode = resume;
            viewModel.ResumePartialPath = resume ? requestedPartial : string.Empty;
            await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
            if (resume)
            {
                await viewModel.PrepareResumeAsync(TestContext.Current.CancellationToken);
                Assert.True(viewModel.ResumeReport?.CanResume);
            }
            Assert.True(viewModel.CanStart);
            Task run = viewModel.StartAsync(TestContext.Current.CancellationToken);
            try
            {
                await started.Task.WaitAsync(TestWait, TestContext.Current.CancellationToken);
                QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
                CanonicalDefinitionSerializer serializer = new();
                string canonical = serializer.Serialize(request.DraftDefinition);
                object?[] presentation = RunPresentation(viewModel);
                string runtimeText = viewModel.RuntimeIdentityText;
                Assert.NotSame(harness.Definition, request.DraftDefinition);
                Assert.Same(harness.Metadata, request.WorkbookMetadata);
                Assert.Equal(resume ? null : harness.ExplicitOutput, request.OutputDirectory);
                Assert.Equal(resume ? requestedPartial : null, request.ResumePartialPath);
                Assert.True(viewModel.HasCurrentRun);
                Assert.Equal("model-a", viewModel.CurrentRunModelId);
                Assert.Equal(1, viewModel.CurrentRunMaxConcurrency);
                Assert.Equal("今回run " + runOutput, viewModel.CurrentRunOutputSummary);

                viewModel.ApplySettings(new ApplicationSettings
                {
                    PreferredModelId = "model-b",
                    MaxConcurrency = 3,
                    OutputDirectoryOverride = Path.Combine(harness.Root, "next-output"),
                    Definition = WithChangedPrompt(harness.Definition),
                });
                viewModel.IsResumeMode = !resume;
                viewModel.ResumePartialPath = Path.Combine(harness.Root, "next.partial.xlsx");

                Assert.Equal("model-b", viewModel.PreferredModelId);
                Assert.Equal("model-b", viewModel.SelectedModelId);
                Assert.Equal(3, viewModel.MaxConcurrency);
                Assert.Equal("model-a", viewModel.CurrentRunModelId);
                Assert.Equal(1, viewModel.CurrentRunMaxConcurrency);
                Assert.Equal("今回run " + runOutput, viewModel.CurrentRunOutputSummary);
                Assert.Equal(runtimeText, viewModel.RuntimeIdentityText);
                Assert.Equal(presentation, RunPresentation(viewModel));
                Assert.True(viewModel.IsRunning);
                Assert.True(viewModel.CanCancel);
                Assert.True(viewModel.CancelCommand.CanExecute(null));
                Assert.False(viewModel.CanStart);
                Assert.False(viewModel.CanLogin);
                Assert.False(viewModel.CanCheckAuthentication);
                Assert.Throws<InvalidOperationException>(() =>
                    viewModel.Configure(CloneDefinition(harness.Definition), harness.Metadata, harness.InputPath));
                Assert.Throws<InvalidOperationException>(() =>
                    viewModel.Configure(WithChangedPrompt(harness.Definition), harness.Metadata, harness.OtherInputPath));
                Assert.Same(request, harness.Runner.LastRequest);
                Assert.Equal(canonical, serializer.Serialize(request.DraftDefinition));
                Assert.Equal(harness.InputPath, request.InputPath);
                Assert.Equal("model-a", request.ModelId);
                Assert.Equal(1, request.MaxConcurrency);
                Assert.Equal(64_000, request.MaximumPromptTokens);
                Assert.Equal(128_000, request.MaximumContextWindowTokens);
                Assert.True(request.UseDurableWorkflow);
                Assert.Equal(resume ? null : harness.ExplicitOutput, request.OutputDirectory);
                Assert.Equal(resume ? requestedPartial : null, request.ResumePartialPath);

                viewModel.CancelCommand.Execute(null);
                await cancelled.Task.WaitAsync(TestWait, TestContext.Current.CancellationToken);
                viewModel.MaxConcurrency = 1;
                viewModel.OutputDirectory = string.Empty;
                viewModel.CancelCommand.Execute(null);
                Assert.Equal(1, Volatile.Read(ref cancellations));
                Assert.True(viewModel.IsCancelling);
                Assert.False(viewModel.CanCancel);
                Assert.False(viewModel.CanStart);
                Assert.Contains("取消処理中", viewModel.ValidationSummary, StringComparison.Ordinal);
                Assert.DoesNotContain("開始できます", viewModel.ValidationSummary, StringComparison.Ordinal);
                Assert.Equal("model-a", viewModel.CurrentRunModelId);
                Assert.Equal(1, viewModel.CurrentRunMaxConcurrency);
                Assert.Equal("今回run " + runOutput, viewModel.CurrentRunOutputSummary);
                Assert.Equal(canonical, serializer.Serialize(request.DraftDefinition));
                Assert.Equal(1, request.MaxConcurrency);
                Assert.Equal(resume ? null : harness.ExplicitOutput, request.OutputDirectory);
            }
            finally
            {
                release.TrySetResult(partial);
                await run.WaitAsync(TestWait, TestContext.Current.CancellationToken);
            }

            Assert.False(viewModel.IsRunning);
            Assert.False(viewModel.IsCancelling);
            Assert.True(viewModel.HasCurrentRun);
            Assert.Equal("model-a", viewModel.CurrentRunModelId);
            Assert.Equal(1, viewModel.CurrentRunMaxConcurrency);
            Assert.Equal("前回run " + runOutput, viewModel.CurrentRunOutputSummary);
            Assert.Same(partial, viewModel.LastRunContext?.Summary);
            Assert.Equal("model-a", viewModel.LastRunContext?.ModelId);
            Assert.Equal(harness.InputPath, viewModel.LastRunContext?.InputPath);
            Assert.Same(harness.Runner.LastRequest?.RuntimeIdentity, viewModel.LastRunContext?.RuntimeIdentity);
            Assert.Equal("model-b", viewModel.PreferredModelId);
            Assert.Equal(1, viewModel.MaxConcurrency);
            Assert.Null(viewModel.OutputDirectoryOverride);
            Assert.Equal(1, completions);
            Assert.Equal(1, harness.Runner.CallCount);
            Assert.Equal(1, harness.Authentication.CallCount);
            Assert.Equal(0, harness.FactoryCallCount);
            Assert.False(Directory.Exists(harness.ExplicitOutput));
            Assert.False(Directory.Exists(Path.Combine(harness.Root, "next-output")));
        }
        finally
        {
            if (Directory.Exists(harness.Root))
            {
                Directory.Delete(harness.Root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Existing_run_preflight_error_survives_equivalent_settings_but_fresh_login_check_clears_it()
    {
        using SettingsHarness harness = new();
        harness.Configure();
        ExecutionViewModel viewModel = harness.ViewModel;
        ApplicationSettings settings = new() { PreferredModelId = "model-b", MaxConcurrency = 2 };
        viewModel.ApplySettings(settings);
        harness.Run = (_, _, _) => throw new QuantificationRunPreflightException(
        [
            new ExecutionCapacityError("MODEL_CONTEXT_LIMIT_UNAVAILABLE", "Definition", "DEF-U01",
                "Synthetic definition", "MaximumContextWindowTokens", "0", "positive SDK model limit"),
        ]);
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        await viewModel.StartAsync(TestContext.Current.CancellationToken);
        ExecutionTechnicalError previous = Assert.Single(viewModel.TechnicalErrors,
            error => error.Code == "MODEL_CONTEXT_LIMIT_UNAVAILABLE");

        viewModel.Configure(CloneDefinition(harness.Definition), harness.Metadata, harness.InputPath);
        viewModel.ApplySettings(settings);
        Assert.False(viewModel.CanStart);
        Assert.Equal(previous.Message, Assert.Single(viewModel.TechnicalErrors,
            error => error.Code == previous.Code).Message);
        ((INotifyCollectionChanged)viewModel.AvailableModelIds).CollectionChanged +=
            (_, _) => viewModel.SelectedModelId = null;
        harness.Process.Complete(0);
        await viewModel.LoginAsync(TestContext.Current.CancellationToken).WaitAsync(TestWait, TestContext.Current.CancellationToken);

        Assert.Equal("model-b", viewModel.PreferredModelId);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.True(viewModel.CanStart);
        Assert.DoesNotContain(viewModel.TechnicalErrors, error => error.Code == previous.Code);
        Assert.Equal(1, harness.Runner.CallCount);
        Assert.Equal(2, harness.Authentication.CallCount);
        Assert.Equal(1, harness.FactoryCallCount);
    }

    private static string DefaultOutput(string inputPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputPath))!, "result");

    private static ExecutionAuthenticationSnapshot Available(params string[] modelIds) =>
        new(ExecutionAuthenticationState.Available, modelIds.Select(U04TestSupport.Model), U04TestSupport.RuntimeIdentity());

    private static void AssertNoLoginOrRun(SettingsHarness harness)
    {
        Assert.Null(harness.ViewModel.LastLoginTask);
        Assert.Equal(0, harness.Resolver.CallCount);
        Assert.Equal(0, harness.FactoryCallCount);
        Assert.Equal(0, harness.Process.StartCount);
        Assert.Equal(0, harness.Runner.CallCount);
    }

    private static QuantificationDefinition CloneDefinition(QuantificationDefinition definition) => definition with
    {
        BasePoints = definition.BasePoints * 1.00m,
        Questions = [.. definition.Questions.Select(question => question with
        {
            SupportingSourceColumns = [.. question.SupportingSourceColumns],
            Evaluators = [.. question.Evaluators.Select(evaluator => evaluator with
            {
                Criteria = [.. evaluator.Criteria.Select(criterion => criterion with { })],
            })],
            SpecialEvaluations = [.. question.SpecialEvaluations.Select(special => special with
            {
                SupportingSourceColumns = [.. special.SupportingSourceColumns],
            })],
        })],
    };

    private static QuantificationDefinition WithChangedCriterion(QuantificationDefinition definition)
    {
        QuestionDefinition question = definition.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        return definition with
        {
            Questions = definition.Questions.SetItem(0, question with
            {
                Evaluators = question.Evaluators.SetItem(0, evaluator with
                {
                    Criteria = evaluator.Criteria.SetItem(0, evaluator.Criteria[0] with
                    {
                        Description = "PRIVATE-T06-CRITERION-CANARY",
                    }),
                }),
            }),
        };
    }

    private static QuantificationDefinition WithChangedPrompt(QuantificationDefinition definition)
    {
        QuestionDefinition question = definition.Questions[0];
        return definition with
        {
            Questions = definition.Questions.SetItem(0, question with
            {
                Evaluators = question.Evaluators.SetItem(0, question.Evaluators[0] with
                {
                    CustomPromptTemplate = "PRIVATE-T06-PROMPT-CANARY {回答} {評価項目}",
                }),
            }),
        };
    }

    private static EvaluationProgress RunningProgress(SettingsHarness harness) =>
        new(5, 3, 1, EvaluationProgressStatus.Running, DurableEvaluationStage.EvaluatingRows,
            referenceCompleted: 1, referenceTotal: 1, rowCompleted: 1, rowTotal: 2,
            finalPath: Path.Combine(harness.Root, "current.xlsx"),
            partialPath: Path.Combine(harness.Root, "current.partial.xlsx"));

    private static object?[] RunPresentation(ExecutionViewModel viewModel) =>
    [
        viewModel.ProgressTotal, viewModel.ProgressCompleted, viewModel.ProgressInFlight,
        viewModel.ProgressPercent, viewModel.ProgressText, viewModel.ProgressStage,
        viewModel.ReferenceCompleted, viewModel.ReferenceTotal, viewModel.RowCompleted, viewModel.RowTotal,
        viewModel.ReservedFinalPath, viewModel.PartialPath, viewModel.StageText,
        viewModel.DurableProgressText, viewModel.OutputIdentityText, viewModel.RunStatusText,
        viewModel.HasCurrentRun, viewModel.CurrentRunModelId, viewModel.CurrentRunMaxConcurrency,
        viewModel.CurrentRunOutputSummary,
    ];

    private static async Task<RunSummary> CompleteRunAsync(SettingsHarness harness)
    {
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(harness.Definition, harness.Metadata);
        harness.Run = (_, progress, _) =>
        {
            progress?.Invoke(RunningProgress(harness));
            return Task.FromResult(summary);
        };
        harness.Configure();
        await harness.ViewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.True(harness.ViewModel.CanStart);
        await harness.ViewModel.StartAsync(TestContext.Current.CancellationToken);
        Assert.Same(summary, harness.ViewModel.LastRunContext?.Summary);
        return summary;
    }

    public enum ConfigurationChange { InputPath, MetadataIdentity, Criterion, Prompt }

    private sealed class SettingsHarness : IDisposable
    {
        public SettingsHarness()
        {
            Metadata = U01TestSupport.ValidateMapping(Definition).Metadata;
            Runner = new RecordingRunBoundary((request, progress, token) => Run(request, progress, token));
            BundledCopilotLoginService login = new(Resolver, _ =>
            {
                FactoryCallCount++;
                return Process;
            });
            ViewModel = new ExecutionViewModel(Authentication, Runner, login, ResumeInspection);
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-T06-" + Guid.NewGuid().ToString("N"));
        public string InputPath => Path.Combine(Root, "input-a", "synthetic.xlsx");
        public string OtherInputPath => Path.Combine(Root, "input-b", "synthetic.xlsx");
        public string ExplicitOutput => Path.Combine(Root, "explicit-output");
        public QuantificationDefinition Definition { get; } = U04TestSupport.Definition(2, 3);
        public WorkbookMetadata Metadata { get; }
        public MutableAuthenticationBoundary Authentication { get; } = new();
        public AdmittingResumeInspectionBoundary ResumeInspection { get; } = new();
        public RecordingRunBoundary Runner { get; }
        public LoginResolver Resolver { get; } = new();
        public LoginProcess Process { get; } = new();
        public int FactoryCallCount { get; private set; }
        public ExecutionViewModel ViewModel { get; }
        public Func<QuantificationRunRequest, Action<EvaluationProgress>?, CancellationToken, Task<RunSummary>> Run { get; set; } =
            (_, _, _) => throw new InvalidOperationException("The test did not authorize a run.");

        public void Configure(string? inputPath = null) => ViewModel.Configure(Definition, Metadata, inputPath ?? InputPath);

        public void Dispose() => ViewModel.Dispose();
    }

    private sealed class AdmittingResumeInspectionBoundary : IResumeInspectionBoundary
    {
        private readonly InputSnapshot input = new(new string('A', 64), 123, DateTimeOffset.UnixEpoch);
        private CheckpointEnvelope? checkpoint;

        public void Admit(SettingsHarness harness, string partialPath)
        {
            QuantificationSnapshot snapshot = QuantificationSnapshot.Create(harness.Definition);
            CopilotRuntimeIdentity runtime = U04TestSupport.RuntimeIdentity();
            checkpoint = new CheckpointEnvelope
            {
                InputPath = harness.InputPath,
                Input = input,
                DefinitionCanonicalJson = snapshot.CanonicalJson,
                DefinitionSha256 = snapshot.Sha256,
                NormalModelId = harness.ViewModel.SelectedModelId ?? "model-a",
                ReferenceModelId = harness.ViewModel.SelectedModelId ?? "model-a",
                Runtime = new CheckpointRuntimeIdentity
                {
                    ApplicationIdentity = QuantificationRunBoundary.ApplicationIdentity(),
                    CliVersion = runtime.CliVersion,
                    CliSha256 = runtime.CliSha256,
                    SdkInformationalVersion = runtime.SdkInformationalVersion,
                },
                FinalPath = Path.Combine(harness.Root, "admitted.xlsx"),
                PartialPath = partialPath,
                StartedAtUtc = DateTimeOffset.UnixEpoch,
                SavedAtUtc = DateTimeOffset.UnixEpoch,
            };
        }

        public Task<CheckpointLoadResult> LoadAsync(string partialPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return checkpoint is { } envelope && string.Equals(envelope.PartialPath, partialPath, StringComparison.Ordinal)
                ? Task.FromResult(CheckpointLoadResult.Succeeded(envelope))
                : Task.FromResult(CheckpointLoadResult.Failed(CheckpointStatusCodes.Invalid));
        }

        public Task<InputSnapshot> CaptureInputAsync(string inputPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(input);
        }
    }

    private sealed class MutableAuthenticationBoundary : IExecutionAuthenticationBoundary
    {
        public ExecutionAuthenticationSnapshot Snapshot { get; set; } = Available("model-a", "model-b", "auto");
        public Func<CancellationToken, Task<ExecutionAuthenticationSnapshot>>? CheckOverride { get; set; }
        public int CallCount { get; private set; }

        public Task<ExecutionAuthenticationSnapshot> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return CheckOverride?.Invoke(cancellationToken) ?? Task.FromResult(Snapshot);
        }
    }

    private sealed class LoginResolver : ICopilotCliPathResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult<string?>(Path.Combine(Path.GetTempPath(), "synthetic-t06-cli", "copilot.exe"));
        }
    }

    private sealed class LoginProcess : ICopilotLoginProcess
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;
        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool HasExited => exit.Task.IsCompletedSuccessfully;
        public int ExitCode { get; private set; }
        public List<bool> KillTreeArguments { get; } = [];

        public bool Start()
        {
            StartCount++;
            started.TrySetResult();
            return true;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => exit.Task;

        public void Kill(bool entireProcessTree)
        {
            KillTreeArguments.Add(entireProcessTree);
            Complete(-1);
        }

        public bool WaitForExit(int milliseconds) => HasExited;

        public void Complete(int exitCode)
        {
            ExitCode = exitCode;
            exit.TrySetResult();
        }

        public void Dispose() => DisposeCount++;
    }
}