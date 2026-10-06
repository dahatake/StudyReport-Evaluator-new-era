using System.Collections.Immutable;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitHub.Copilot;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-016 (AC-017)
public sealed class ModelOptionsTests
{
    private static CopilotModelAvailability RichModel() =>
        new("model-a", 272_000, 400_000, ["high", "low", "medium", "high"], "medium", 1_000_000);

    [Fact]
    public async Task Model_specific_options_preserve_explicit_choices_and_do_not_make_network_calls()
    {
        MutableAuthentication auth = new();
        using ExecutionViewModel vm = Create(auth);
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["model-a", "model-b", "auto"], vm.AvailableModelIds);
        Assert.Equal(["低", "中 (Default)", "高"], vm.ReasoningEffortOptions.Select(option => option.DisplayName));
        Assert.Equal(["272K (Default)", "1M"], vm.ContextSizeOptions.Select(option => option.DisplayName));
        Assert.Equal("low", vm.SelectedModelReasoningEffort);
        Assert.Null(vm.SelectedContextTier);
        vm.SelectedReasoningEffortOption = vm.ReasoningEffortOptions.Last();
        vm.SelectedContextSizeOption = vm.ContextSizeOptions.Last();
        Assert.Equal("high", vm.SelectedModelReasoningEffort);
        Assert.Equal("long-context", vm.SelectedContextTier);
        Assert.Equal(1_000_000, vm.SelectedModelPromptTokenLimit);

        vm.SelectedModelId = "model-b";
        Assert.Empty(vm.ReasoningEffortOptions);
        Assert.False(vm.CanSelectReasoningEffort);
        Assert.Null(vm.SelectedModelReasoningEffort);
        Assert.Equal("SDK未公開 (Default)", Assert.Single(vm.ContextSizeOptions).DisplayName);
        vm.SelectedModelId = "auto";
        Assert.Empty(vm.ReasoningEffortOptions);
        Assert.Single(vm.ContextSizeOptions);
        vm.SelectedModelId = "model-a";
        Assert.Equal("high", vm.SelectedModelReasoningEffort);
        Assert.Equal("1M", vm.SelectedContextSizeOption?.DisplayName);
        Assert.Equal(1, auth.Count);
        Assert.Single(vm.ModelPreferences!.Value);
        Assert.Throws<ArgumentException>(() =>
            vm.SelectedReasoningEffortOption = new("unsupported", "unsupported"));
        Assert.Throws<ArgumentException>(() => vm.SelectedContextSizeOption = new("invented", 42));
    }

    [Fact]
    public async Task Changed_capabilities_block_invalid_preferences_and_explicit_reselection_recovers()
    {
        MutableAuthentication auth = new();
        using ExecutionViewModel vm = Create(auth);
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        vm.SelectedReasoningEffortOption = vm.ReasoningEffortOptions.Last();
        vm.SelectedContextSizeOption = vm.ContextSizeOptions.Last();
        Assert.True(vm.CanStart);
        auth.Models = [new("model-a", 272_000, 400_000, ["low"])];
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Null(vm.SelectedReasoningEffortOption);
        Assert.Null(vm.SelectedContextSizeOption);
        Assert.Equal(["MODEL_REASONING_EFFORT_UNAVAILABLE", "MODEL_CONTEXT_TIER_UNAVAILABLE"],
            vm.TechnicalErrors.Select(error => error.Code));
        Assert.False(vm.CanStart);
        Assert.Equal("high", vm.ModelPreferences!.Value[0].ReasoningEffort);
        Assert.Equal("long-context", vm.ModelPreferences.Value[0].ContextTier);
        vm.SelectedReasoningEffortOption = vm.ReasoningEffortOptions.Single();
        vm.SelectedContextSizeOption = vm.ContextSizeOptions.Single();
        Assert.True(vm.CanStart);
        Assert.Equal("low", vm.SelectedModelReasoningEffort);
        Assert.Null(vm.SelectedContextTier);
        auth.State = ExecutionAuthenticationState.RuntimeFailed;
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Null(vm.SelectedModelId);
        Assert.Empty(vm.ReasoningEffortOptions);
        Assert.Empty(vm.ContextSizeOptions);
        Assert.Single(vm.AvailableModelIds);
        auth.State = ExecutionAuthenticationState.Available;
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.CanStart);
        auth.Models = [];
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Empty(vm.AvailableModelIds);
        Assert.False(vm.CanStart);
    }

    [Fact]
    public async Task Removed_reasoning_support_can_be_recovered_by_an_explicit_reset_without_network()
    {
        MutableAuthentication auth = new();
        using ExecutionViewModel vm = Create(auth);
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        vm.SelectedReasoningEffortOption = vm.ReasoningEffortOptions.Last();
        vm.SelectedContextSizeOption = vm.ContextSizeOptions.Last();
        auth.Models = [new("model-a", null, null)];
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.False(vm.CanSelectReasoningEffort);
        Assert.False(vm.CanStart);
        Assert.True(vm.ResetModelOptionsCommand.CanExecute(null));
        vm.ResetModelOptionsCommand.Execute(null);
        Assert.Null(vm.SelectedModelReasoningEffort);
        Assert.Null(vm.SelectedContextTier);
        Assert.True(vm.CanStart);
        Assert.False(vm.ResetModelOptionsCommand.CanExecute(null));
        Assert.Equal(2, auth.Count);
        Assert.Empty(vm.ModelPreferences!.Value);
    }

    [Fact]
    public async Task Run_snapshot_captures_selected_tier_capacity_and_is_immutable_during_edits()
    {
        MutableAuthentication auth = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingRunBoundary run = new(async (_, _, _) =>
        {
            await release.Task;
            throw new QuantificationRunException("RUN_FAILED");
        });
        using ExecutionViewModel vm = Create(auth, run);
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        vm.SelectedReasoningEffortOption = vm.ReasoningEffortOptions.Last();
        vm.SelectedContextSizeOption = vm.ContextSizeOptions.Last();
        Task running = vm.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(vm.IsRunning);
            QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(run.LastRequest);
            Assert.Equal("high", request.ReasoningEffort);
            Assert.Equal("long-context", request.ContextTier);
            Assert.Equal(1_000_000, request.MaximumPromptTokens);
            Assert.Null(request.MaximumContextWindowTokens);
            var capacity = new EvaluationRequestCapacityValidator().Validate(
                new StudyReportEvaluator.Core.Prompting.SafeReferenceAnswerPayload("Q1", "synthetic prompt"),
                request.MaximumPromptTokens, request.MaximumContextWindowTokens);
            Assert.Equal(800_000, capacity.ModelContextBudget);
            vm.SelectedReasoningEffortOption = vm.ReasoningEffortOptions.First();
            vm.SelectedContextSizeOption = vm.ContextSizeOptions.First();
            Assert.Equal("high", request.ReasoningEffort);
            Assert.Equal("long-context", request.ContextTier);
            Assert.Equal("high", vm.CurrentRunReasoningEffortText);
            Assert.Equal("long-context", vm.CurrentRunContextTierText);
            Assert.Null(vm.SelectedContextTier);
            Assert.Equal(272_000, vm.SelectedModelPromptTokenLimit);
        }
        finally
        {
            release.TrySetResult();
            await running;
        }
    }

    [Theory]
    [InlineData(1_000_000, "1M (Default)")]
    [InlineData(272_000, "272K (Default)")]
    [InlineData(1, "1 (Default)")]
    [InlineData(int.MaxValue, "2147483647 (Default)")]
    [InlineData(null, "SDK未公開 (Default)")]
    public void Context_labels_and_reasoning_order_are_metadata_driven(int? tokens, string label)
    {
        Assert.Equal(label, new ContextSizeOption("default", tokens).DisplayName);
        CopilotModelAvailability model = new("new-model", tokens, null,
            ["future-b", "max", "none", "minimal", "xhigh", "medium", "low", "future-a", "high", "none"]);
        Assert.Equal(["none", "minimal", "low", "medium", "high", "xhigh", "max", "future-a", "future-b"],
            ModelOptionPolicy.ReasoningOptions(model).Select(option => option.Value));
        Assert.Single(ModelOptionPolicy.ContextOptions(new("small", 10, 20, longContextPromptTokens: 10)));
        Assert.Single(ModelOptionPolicy.ContextOptions(new("auto", null, null, longContextPromptTokens: 1_000_000)));
        Assert.Throws<ArgumentException>(() => new EphemeralEvaluationRunnerOptions(contextTier: "invented"));
    }

    [AvaloniaFact]
    public async Task Settings_controls_bind_real_options_and_offer_accessible_44_DIP_targets()
    {
        MutableAuthentication auth = new();
        using ExecutionViewModel vm = Create(auth);
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        using SettingsViewModel settings = new(new InputViewModel(), new QuantificationDesignViewModel(), vm);
        SettingsView view = new(settings);
        Window window = new() { Width = 950, Height = 450, Content = view };
        try
        {
            window.Show();
            window.SetRenderScaling(2d);
            Dispatcher.UIThread.RunJobs();
            ComboBox Find(string id) => view.GetVisualDescendants().OfType<ComboBox>()
                .Single(control => AutomationProperties.GetAutomationId(control) == id);
            ComboBox effort = Find("ExecutionReasoningEffort");
            ComboBox context = Find("ExecutionContextSize");
            Assert.Same(vm.ReasoningEffortOptions, effort.ItemsSource);
            Assert.Same(vm.ContextSizeOptions, context.ItemsSource);
            foreach (ComboBox control in new[] { effort, context })
            {
                Assert.True(control.Bounds.Height >= 44);
                Assert.True(control.Bounds.Width >= 44);
                Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(control)));
                Assert.NotNull(ToolTip.GetTip(control));
                Assert.True(control.Focus());
            }
            effort.Focus();
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(context, window.FocusManager?.GetFocusedElement());
            effort.SetCurrentValue(ComboBox.SelectedItemProperty, vm.ReasoningEffortOptions.Last());
            context.SetCurrentValue(ComboBox.SelectedItemProperty, vm.ContextSizeOptions.Last());
            Assert.Equal("high", vm.SelectedModelReasoningEffort);
            Assert.Equal("long-context", vm.SelectedContextTier);
            Button reset = view.GetVisualDescendants().OfType<Button>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "ExecutionResetModelOptions");
            Assert.Same(vm.ResetModelOptionsCommand, reset.Command);
            Assert.True(reset.IsEffectivelyEnabled);
            reset.Command!.Execute(reset.CommandParameter);
            Assert.Equal("low", vm.SelectedModelReasoningEffort);
            Assert.Null(vm.SelectedContextTier);
            Assert.Equal(1, auth.Count);
        }
        finally { window.Close(); }
    }

    private static ExecutionViewModel Create(MutableAuthentication auth, RecordingRunBoundary? run = null)
    {
        ExecutionViewModel vm = new(auth, run ?? new RecordingRunBoundary((_, _, _) =>
            throw new InvalidOperationException("No run expected.")));
        var definition = U04TestSupport.Definition(2, 2);
        vm.Configure(definition, U01TestSupport.ValidateMapping(definition).Metadata,
            Path.Combine(Path.GetTempPath(), "synthetic-model-options.xlsx"));
        return vm;
    }

    [Fact]
    public async Task Explicit_save_and_reload_restore_options_but_catalog_autosave_does_not_save_drafts()
    {
        using SettingsFixture fixture = new();
        MutableAuthentication auth = new();
        using ExecutionViewModel vm = Create(auth);
        using SettingsViewModel settings = new(new InputViewModel(), new QuantificationDesignViewModel(), vm, fixture.Store);
        await settings.InitializeAsync(TestContext.Current.CancellationToken);
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        vm.SelectedModelId = "model-a";
        vm.SelectedReasoningEffortOption = vm.ReasoningEffortOptions.Last();
        vm.SelectedContextSizeOption = vm.ContextSizeOptions.Last();
        Assert.True(settings.HasUnsavedChanges);
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        var cacheOnly = (await fixture.Store.LoadAsync(TestContext.Current.CancellationToken)).Settings!;
        Assert.NotNull(cacheOnly.CachedModels);
        Assert.Null(cacheOnly.ModelPreferences);
        Assert.Null(cacheOnly.PreferredModelId);
        await settings.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsSaveStatus.Saved, settings.SaveStatus);
        var saved = (await fixture.Store.LoadAsync(TestContext.Current.CancellationToken)).Settings!;
        Assert.Equal(vm.ModelPreferences!.Value, saved.ModelPreferences!.Value);
        using ExecutionViewModel restored = Create(new MutableAuthentication());
        restored.ApplySettings(saved);
        Assert.Empty(restored.ReasoningEffortOptions);
        await restored.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.Equal("high", restored.SelectedModelReasoningEffort);
        Assert.Equal("long-context", restored.SelectedContextTier);
        Assert.False(settings.HasUnsavedChanges);

        byte[] before = File.ReadAllBytes(fixture.Path);
        File.SetAttributes(fixture.Path, FileAttributes.ReadOnly);
        try
        {
            vm.SelectedReasoningEffortOption = vm.ReasoningEffortOptions.First();
            await settings.SaveAsync(TestContext.Current.CancellationToken);
            Assert.Equal(SettingsSaveStatus.WriteFailed, settings.SaveStatus);
            Assert.Equal(before, File.ReadAllBytes(fixture.Path));
            Assert.Equal("low", vm.SelectedModelReasoningEffort);
            Assert.True(settings.HasUnsavedChanges);
        }
        finally { File.SetAttributes(fixture.Path, FileAttributes.Normal); }
    }

    [Theory]
    [InlineData("""[{"modelId":"a","reasoningEffort":"high","contextTier":"invalid"}]""")]
    [InlineData("""[{"modelId":"a","reasoningEffort":"","contextTier":"default"}]""")]
    [InlineData("""[{"modelId":"a ","reasoningEffort":null,"contextTier":"default"}]""")]
    [InlineData("""[{"modelId":"a","contextTier":"default"}]""")]
    [InlineData("""[{"modelId":"a","reasoningEffort":null,"contextTier":"default","extra":true}]""")]
    [InlineData("""[{"modelId":"a","reasoningEffort":null,"contextTier":"default"},{"modelId":"a","reasoningEffort":null,"contextTier":"default"}]""")]
    [InlineData("[null]")]
    [InlineData("{}")]
    public async Task Malformed_preferences_are_rejected_without_modifying_the_file(string json)
    {
        using SettingsFixture fixture = new();
        Directory.CreateDirectory(fixture.Root);
        byte[] original = System.Text.Encoding.UTF8.GetBytes("""{"schemaVersion":1,"modelPreferences":""" + json + "}");
        await File.WriteAllBytesAsync(fixture.Path, original, TestContext.Current.CancellationToken);
        var result = await fixture.Store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsLoadStatus.JsonInvalid, result.Status);
        Assert.Null(result.Settings);
        Assert.Equal(original, File.ReadAllBytes(fixture.Path));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public async Task Preference_count_boundary_and_old_format_are_explicit(int count, bool valid)
    {
        using SettingsFixture fixture = new();
        Assert.True((await fixture.Store.SaveAsync(new(), TestContext.Current.CancellationToken)).IsSuccess);
        byte[] baseline = File.ReadAllBytes(fixture.Path);
        Assert.Null((await fixture.Store.LoadAsync(TestContext.Current.CancellationToken)).Settings!.ModelPreferences);
        var result = await fixture.Store.SaveAsync(new()
        {
            ModelPreferences = [.. Enumerable.Range(0, count).Select(index =>
                new ModelSelectionPreference($"model-{index}", null, "default"))],
        }, TestContext.Current.CancellationToken);
        Assert.Equal(valid, result.IsSuccess);
        if (valid)
            Assert.Equal(count, (await fixture.Store.LoadAsync(TestContext.Current.CancellationToken)).Settings!.ModelPreferences!.Value.Length);
        else
        {
            Assert.Equal(SettingsSaveStatus.InvalidSettings, result.Status);
            Assert.Equal(baseline, File.ReadAllBytes(fixture.Path));
        }
    }

    [Fact]
    public async Task Explicit_option_edit_during_load_wins_over_saved_options()
    {
        using SettingsFixture fixture = new();
        await fixture.Store.SaveAsync(new()
        {
            PreferredModelId = "model-a",
            ModelPreferences = [new("model-a", "medium", "default")],
        }, TestContext.Current.CancellationToken);
        byte[] original = File.ReadAllBytes(fixture.Path);
        MutableAuthentication auth = new();
        using ExecutionViewModel vm = Create(auth);
        await vm.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        using SettingsViewModel settings = new(new InputViewModel(), new QuantificationDesignViewModel(), vm, fixture.Store);
        settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.IsLoading) && settings.IsLoading)
            {
                vm.SelectedReasoningEffortOption = vm.ReasoningEffortOptions.Last();
                vm.SelectedContextSizeOption = vm.ContextSizeOptions.Last();
            }
        };
        await settings.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal("high", vm.SelectedModelReasoningEffort);
        Assert.Equal("long-context", vm.SelectedContextTier);
        Assert.True(settings.HasUnsavedChanges);
        Assert.Equal(original, File.ReadAllBytes(fixture.Path));
        Assert.Equal(1, auth.Count);
    }

    private sealed class SettingsFixture : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(RepositoryRoot(),
            "artifacts", "test", "model-options", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root, "setting.txt");
        public SettingsFileStore Store => new(Path);
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        private static string RepositoryRoot()
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(System.IO.Path.Combine(directory.FullName, "StudyReportEvaluator.slnx")))
                    return directory.FullName;
            }
            throw new InvalidOperationException("The test repository root is unavailable.");
        }
    }

    private sealed class MutableAuthentication : IExecutionAuthenticationBoundary
    {
        public IReadOnlyList<CopilotModelAvailability> Models { get; set; } =
            [RichModel(), new("model-b", null, null), new("auto", null, null)];
        public ExecutionAuthenticationState State { get; set; } = ExecutionAuthenticationState.Available;
        public int Count { get; private set; }
        public Task<ExecutionAuthenticationSnapshot> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            return Task.FromResult(new ExecutionAuthenticationSnapshot(State,
                State == ExecutionAuthenticationState.Available ? Models : [], U04TestSupport.RuntimeIdentity()));
        }
    }
}
