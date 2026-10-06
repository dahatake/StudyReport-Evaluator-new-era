using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

public sealed class SettingsViewModelTests
{
    private const string PrivateCanary = "PRIVATE-T10-CONTENT";
    private static readonly CanonicalDefinitionSerializer Canonical = new();
    private static readonly TimeSpan TestWait = TimeSpan.FromSeconds(10);

    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact]
    public async Task Null_store_is_inert_and_preserves_editor_references_without_user_data_access()
    {
        using SettingsHarness harness = new(withStore: false);
        SettingsViewModel settings = harness.Settings;
        Assert.IsAssignableFrom<UiObservableObject>(settings);
        Assert.IsAssignableFrom<IDisposable>(settings);
        Assert.Same(harness.Input, settings.Input);
        Assert.Same(harness.Design, settings.Design);
        Assert.Same(harness.Execution, settings.Execution);
        Assert.Equal(string.Empty, settings.FilePath);
        Assert.True(settings.IsInitialized);
        Assert.False(settings.IsLoadPending);
        Assert.False(settings.CanSave);
        Assert.False(settings.CanEditDefinition);
        Assert.False(settings.CanApplySavedDefinition);
        Assert.False(settings.HasUnsavedChanges);
        Assert.Equal(SettingsCategory.Common, settings.SelectedCategory);
        Assert.Equal(new[] { SettingsCategory.Common, SettingsCategory.Mapping, SettingsCategory.Evaluation,
            SettingsCategory.Special, SettingsCategory.ImportedPrompts }, Enum.GetValues<SettingsCategory>());

        await settings.InitializeAsync(TestContext.Current.CancellationToken);
        await settings.LoadAsync(TestContext.Current.CancellationToken);
        await settings.SaveAsync(TestContext.Current.CancellationToken);
        settings.SaveCommand.Execute(null);
        Assert.False(await settings.ApplySavedDefinitionAsync(TestContext.Current.CancellationToken));
        settings.ApplySavedDefinitionCommand.Execute(null);
        foreach (SettingsCategory category in Enum.GetValues<SettingsCategory>())
        {
            Assert.True(settings.SelectCategoryCommand.CanExecute(category));
            settings.SelectCategoryCommand.Execute(category);
            Assert.Equal(category, settings.SelectedCategory);
        }

        Assert.False(settings.SelectCategoryCommand.CanExecute("Common"));
        Assert.False(settings.SelectCategoryCommand.CanExecute(null));
        Assert.False(settings.SelectCategoryCommand.CanExecute((SettingsCategory)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.SelectedCategory = (SettingsCategory)999);
        int closed = 0;
        settings.CloseRequested += (_, _) => closed++;
        settings.RequestCloseCommand.Execute(null);
        Assert.Equal(1, closed);
        Assert.False(settings.HasUnsavedChanges);
        Assert.Null(settings.StoredDefinition);
        Assert.Null(settings.LoadStatus);
        Assert.Null(settings.LastLoadTask);
        Assert.Null(settings.LastSaveTask);
        Assert.Equal(0, harness.Loader.CallCount);
        Assert.False(Directory.Exists(harness.Root));
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public void Constructor_requires_the_existing_editors()
    {
        using SettingsHarness harness = new(withStore: false);
        Assert.Equal("input", Assert.Throws<ArgumentNullException>(() =>
            new SettingsViewModel(null!, harness.Design, harness.Execution)).ParamName);
        Assert.Equal("design", Assert.Throws<ArgumentNullException>(() =>
            new SettingsViewModel(harness.Input, null!, harness.Execution)).ParamName);
        Assert.Equal("execution", Assert.Throws<ArgumentNullException>(() =>
            new SettingsViewModel(harness.Input, harness.Design, null!)).ParamName);
    }

    [Fact]
    public async Task Pending_load_blocks_save_and_missing_file_does_not_create_directories()
    {
        using SettingsHarness harness = new();
        SettingsViewModel settings = harness.Settings;
        List<string?> changed = [];
        settings.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        Assert.True(settings.IsLoadPending);
        Assert.False(settings.IsInitialized);
        Assert.False(settings.SaveCommand.CanExecute(null));
        Assert.Contains("読込待ち", settings.StatusText, StringComparison.Ordinal);
        settings.SaveCommand.Execute(null);
        await settings.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Null(settings.LastSaveTask);
        Assert.False(Directory.Exists(harness.Root));

        Task loading = settings.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Same(loading, settings.LastLoadTask);
        await loading;

        Assert.True(settings.IsInitialized);
        Assert.False(settings.IsLoadPending);
        Assert.False(settings.IsLoading);
        Assert.Equal(SettingsLoadStatus.Missing, settings.LoadStatus);
        Assert.True(settings.CanSave);
        Assert.True(settings.SaveCommand.CanExecute(null));
        Assert.False(settings.HasUnsavedChanges);
        Assert.False(settings.CanEditDefinition);
        Assert.Contains("Excel", settings.DefinitionAvailabilityText, StringComparison.Ordinal);
        Assert.Contains("まだありません", settings.StatusText, StringComparison.Ordinal);
        Assert.Equal(harness.SettingsPath, settings.FilePath);
        Assert.DoesNotContain(harness.Root, settings.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(SettingsViewModel.IsInitialized), changed);
        Assert.Contains(nameof(SettingsViewModel.CanSave), changed);
        Assert.False(Directory.Exists(harness.Root));
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Load_restores_preferences_and_baseline_without_applying_definition_authentication_or_output_creation()
    {
        using SettingsHarness harness = new();
        ApplicationSettings saved = SavedSettings(harness);
        await harness.SeedSettingsAsync(saved);
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        QuantificationDefinition inputBefore = harness.Input.DefinitionDraft;
        QuantificationDefinition designBefore = harness.Design.Draft;
        ImportedPromptViewModel[] prompts = harness.Design.ImportedPrompts.ToArray();

        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);

        SettingsViewModel settings = harness.Settings;
        Assert.Equal(SettingsLoadStatus.Loaded, settings.LoadStatus);
        Assert.Equal(saved.PreferredModelId, harness.Execution.PreferredModelId);
        Assert.Equal(saved.MaxConcurrency, harness.Execution.MaxConcurrency);
        Assert.Equal(saved.OutputDirectoryOverride, harness.Execution.OutputDirectoryOverride);
        Assert.Null(harness.Execution.SelectedModelId);
        Assert.False(harness.Execution.IsConfigured);
        Assert.Equal(ExecutionAuthenticationState.NotChecked, harness.Execution.AuthenticationState);
        Assert.Same(inputBefore, harness.Input.DefinitionDraft);
        Assert.Same(designBefore, harness.Design.Draft);
        Assert.NotSame(saved.Definition, settings.StoredDefinition);
        AssertDefinition(saved.Definition!, settings.StoredDefinition);
        Assert.Equal(prompts, harness.Design.ImportedPrompts);
        Assert.False(settings.CanApplySavedDefinition);
        Assert.False(settings.CanEditDefinition);
        Assert.False(settings.HasUnsavedChanges);
        Assert.Contains("保存済み", settings.StatusText, StringComparison.Ordinal);
        Assert.Contains("Original", settings.StoredDefinitionSummary, StringComparison.Ordinal);
        Assert.Contains("Excel", settings.ApplyAvailabilityText, StringComparison.Ordinal);
        Assert.False(Directory.Exists(harness.OutputPath));
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        Assert.Equal(0, harness.Loader.CallCount);
        AssertNoBackgroundActivity(harness);

        // Initialization is a one-time operation, not a navigation-triggered reload.
        harness.Execution.MaxConcurrency = 3;
        await settings.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, harness.Execution.MaxConcurrency);
        Assert.True(settings.HasUnsavedChanges);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
    }

    [Fact]
    public async Task Load_with_existing_input_preserves_both_editor_drafts_without_automatic_definition_application()
    {
        using SettingsHarness harness = new();
        await harness.SeedSettingsAsync(SavedSettings(harness));
        await harness.LoadInputAsync();
        harness.Design.DefinitionName = "読込前の定義編集";
        QuantificationDefinition input = harness.Input.DefinitionDraft;
        QuantificationDefinition design = harness.Design.Draft;
        var metadata = harness.Input.Metadata;
        int inputReads = harness.Loader.CallCount;

        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Same(input, harness.Input.DefinitionDraft);
        Assert.Same(design, harness.Design.Draft);
        Assert.Same(metadata, harness.Input.Metadata);
        Assert.Equal(inputReads, harness.Loader.CallCount);
        AssertDefinition(SavedDefinition(), harness.Settings.StoredDefinition);
        Assert.True(harness.Settings.HasUnsavedChanges);
        Assert.True(harness.Settings.CanApplySavedDefinition);
        Assert.Contains("mapping", harness.Settings.ApplyAvailabilityText, StringComparison.Ordinal);
        harness.Settings.SynchronizeDrafts();
        Assert.Equal(design.Name, harness.Input.DefinitionDraft.Name);
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Common_edits_during_loading_take_precedence_without_another_read_or_authentication_check()
    {
        using SettingsHarness harness = new();
        await harness.SeedSettingsAsync(SavedSettings(harness));
        QuantificationDefinition input = harness.Input.DefinitionDraft;
        QuantificationDefinition design = harness.Design.Draft;
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        bool edited = false;
        void EditDuringLoad(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(SettingsViewModel.IsLoading) && harness.Settings.IsLoading && !edited)
            {
                edited = true;
                harness.Execution.SelectedModelId = "explicit-during-load";
                harness.Execution.MaxConcurrency = 3;
                harness.Execution.OutputDirectoryOverride = Path.Combine(harness.Root, "explicit-during-load");
                Assert.False(harness.Settings.CanSave);
            }
        }

        harness.Settings.PropertyChanged += EditDuringLoad;
        try
        {
            await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            harness.Settings.PropertyChanged -= EditDuringLoad;
        }

        Assert.True(edited);
        Assert.Equal(SettingsLoadStatus.Loaded, harness.Settings.LoadStatus);
        Assert.Equal("explicit-during-load", harness.Execution.PreferredModelId);
        Assert.Equal(3, harness.Execution.MaxConcurrency);
        Assert.Equal(Path.Combine(harness.Root, "explicit-during-load"), harness.Execution.OutputDirectoryOverride);
        Assert.True(harness.Settings.HasUnsavedChanges);
        AssertDefinition(SavedDefinition(), harness.Settings.StoredDefinition);
        Assert.Same(input, harness.Input.DefinitionDraft);
        Assert.Same(design, harness.Design.Draft);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        Assert.False(Directory.Exists(harness.Execution.OutputDirectoryOverride));
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Cancelled_initial_read_keeps_save_pending_and_existing_file_until_explicit_retry()
    {
        using SettingsHarness harness = new();
        await harness.SeedSettingsAsync(SavedSettings(harness));
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Task reading = harness.Settings.InitializeAsync(cancellation.Token);
        await reading;

        Assert.True(reading.IsCompletedSuccessfully);
        Assert.True(harness.Settings.IsLoadPending);
        Assert.False(harness.Settings.IsLoading);
        Assert.False(harness.Settings.CanSave);
        Assert.Null(harness.Settings.StoredDefinition);
        Assert.Contains("取り消しました", harness.Settings.StatusText, StringComparison.Ordinal);
        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsLoadStatus.Loaded, harness.Settings.LoadStatus);
        Assert.True(harness.Settings.CanSave);
        Assert.False(harness.Settings.HasUnsavedChanges);
        AssertNoBackgroundActivity(harness);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("concurrency")]
    [InlineData("output")]
    [InlineData("all")]
    [InlineData("cleared")]
    public async Task Cancelled_initialization_keeps_only_explicit_preferences_across_retries(string editedPreference)
    {
        using SettingsHarness harness = new();
        ApplicationSettings saved = SavedSettings(harness);
        await harness.SeedSettingsAsync(saved);
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await harness.Settings.InitializeAsync(cancellation.Token);
        Assert.True(harness.Settings.IsLoadPending);

        bool editModel = editedPreference is "model" or "all" or "cleared";
        bool editConcurrency = editedPreference is "concurrency" or "all" or "cleared";
        bool editOutput = editedPreference is "output" or "all" or "cleared";
        bool clearPreferences = editedPreference == "cleared";
        string output = Path.Combine(harness.Root, "explicit-after-cancel");
        if (clearPreferences)
        {
            // Only a confirmed selection can be explicitly cleared; an already-null
            // effective selection is binding feedback under Execution's existing contract.
            await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
            // A successful explicit check now persists only the fetched catalog.
            ApplicationSettings catalogSaved = await harness.ReadSettingsAsync();
            Assert.Equal(saved.PreferredModelId, catalogSaved.PreferredModelId);
            Assert.Equal(saved.MaxConcurrency, catalogSaved.MaxConcurrency);
            AssertDefinition(saved.Definition!, catalogSaved.Definition);
            Assert.NotNull(catalogSaved.CachedModels);
            original = File.ReadAllBytes(harness.SettingsPath);
        }

        if (editModel) harness.Execution.SelectedModelId = "auto";
        if (editConcurrency) harness.Execution.MaxConcurrency = 3;
        if (editOutput) harness.Execution.OutputDirectoryOverride = output;
        if (clearPreferences)
        {
            harness.Execution.SelectedModelId = null;
            harness.Execution.OutputDirectoryOverride = null;
            Assert.Null(harness.Execution.PreferredModelId);
        }

        // An explicit Load while initialization is pending shares the same edit history.
        await harness.Settings.LoadAsync(cancellation.Token);
        Assert.True(harness.Settings.IsLoadPending);
        Assert.False(harness.Settings.CanSave);
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.Loaded, harness.Settings.LoadStatus);
        Assert.True(harness.Settings.IsInitialized);
        Assert.True(harness.Settings.CanSave);
        Assert.Equal(editModel ? clearPreferences ? null : "auto" : saved.PreferredModelId,
            harness.Execution.PreferredModelId);
        Assert.Equal(editConcurrency ? 3 : saved.MaxConcurrency, harness.Execution.MaxConcurrency);
        Assert.Equal(editOutput ? clearPreferences ? null : output : saved.OutputDirectoryOverride,
            harness.Execution.OutputDirectoryOverride);
        Assert.True(harness.Settings.HasUnsavedChanges);
        AssertDefinition(saved.Definition!, harness.Settings.StoredDefinition);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        Assert.False(Directory.Exists(output));
        Assert.False(Directory.Exists(harness.OutputPath));
        AssertNoBackgroundActivity(harness, authenticationChecks: clearPreferences ? 1 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_time_preference_reversions_win_until_the_next_explicit_reload(bool reload)
    {
        using SettingsHarness harness = new();
        if (reload)
        {
            await harness.SeedSettingsAsync(SavedSettings(harness));
            await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        }

        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        ApplicationSettings before = new()
        {
            PreferredModelId = harness.Execution.PreferredModelId,
            MaxConcurrency = harness.Execution.MaxConcurrency,
            OutputDirectoryOverride = harness.Execution.OutputDirectoryOverride,
        };
        ApplicationSettings incoming = SavedSettings(harness) with
        {
            PreferredModelId = "auto",
            MaxConcurrency = 3,
            OutputDirectoryOverride = Path.Combine(harness.Root, "newly-saved-output"),
        };
        await harness.SeedSettingsAsync(incoming);
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        List<string?> notifications = [];
        bool edited = false;
        void RecordPreference(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(ExecutionViewModel.PreferredModelId)
                or nameof(ExecutionViewModel.MaxConcurrency) or nameof(ExecutionViewModel.OutputDirectoryOverride))
            {
                notifications.Add(args.PropertyName);
            }
        }

        void RevertDuringRead(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(SettingsViewModel.IsLoading) || !harness.Settings.IsLoading || edited)
            {
                return;
            }

            edited = true;
            Assert.False(harness.Settings.CanSave);
            harness.Execution.SelectedModelId = "auto";
            harness.Execution.SelectedModelId = "auto";
            harness.Execution.SelectedModelId = before.PreferredModelId;
            harness.Execution.SelectedModelId = before.PreferredModelId;
            harness.Execution.MaxConcurrency = 3;
            harness.Execution.MaxConcurrency = 3;
            harness.Execution.MaxConcurrency = before.MaxConcurrency;
            harness.Execution.MaxConcurrency = before.MaxConcurrency;
            harness.Execution.OutputDirectoryOverride = Path.Combine(harness.Root, "temporary-output");
            harness.Execution.OutputDirectoryOverride = before.OutputDirectoryOverride;
            harness.Execution.OutputDirectoryOverride = before.OutputDirectoryOverride;
        }

        harness.Execution.PropertyChanged += RecordPreference;
        harness.Settings.PropertyChanged += RevertDuringRead;
        try
        {
            await (reload ? harness.Settings.LoadAsync(TestContext.Current.CancellationToken)
                : harness.Settings.InitializeAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            harness.Settings.PropertyChanged -= RevertDuringRead;
            harness.Execution.PropertyChanged -= RecordPreference;
        }

        Assert.True(edited);
        Assert.Equal(SettingsLoadStatus.Loaded, harness.Settings.LoadStatus);
        Assert.Equal(before.PreferredModelId, harness.Execution.PreferredModelId);
        Assert.Equal(before.MaxConcurrency, harness.Execution.MaxConcurrency);
        Assert.Equal(before.OutputDirectoryOverride, harness.Execution.OutputDirectoryOverride);
        Assert.True(harness.Settings.HasUnsavedChanges);
        // Reverting to the original value is observable, but unchanged writes and the
        // Settings restore must not add synthetic/duplicate preference notifications.
        Assert.Equal(new[]
        {
            nameof(ExecutionViewModel.PreferredModelId), nameof(ExecutionViewModel.PreferredModelId),
            nameof(ExecutionViewModel.MaxConcurrency), nameof(ExecutionViewModel.MaxConcurrency),
            nameof(ExecutionViewModel.OutputDirectoryOverride), nameof(ExecutionViewModel.OutputDirectoryOverride),
        }, notifications);

        // Completed initialization does not pin old edits onto future explicit reloads.
        await harness.Settings.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(incoming.PreferredModelId, harness.Execution.PreferredModelId);
        Assert.Equal(incoming.MaxConcurrency, harness.Execution.MaxConcurrency);
        Assert.Equal(incoming.OutputDirectoryOverride, harness.Execution.OutputDirectoryOverride);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        Assert.False(Directory.Exists(incoming.OutputDirectoryOverride));
        AssertNoBackgroundActivity(harness, authenticationChecks: 1);
    }

    [Fact]
    public async Task Cancelled_restore_excludes_loaded_preferences_and_releases_the_edit_guard()
    {
        using SettingsHarness harness = new();
        ApplicationSettings saved = SavedSettings(harness);
        await harness.SeedSettingsAsync(saved);
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        using CancellationTokenSource cancellation = new();
        bool interrupted = false;
        void InterruptRestore(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(ExecutionViewModel.OutputDirectoryOverride)
                && harness.Settings.IsLoading && !interrupted)
            {
                interrupted = true;
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
        }

        harness.Execution.PropertyChanged += InterruptRestore;
        try
        {
            await harness.Settings.InitializeAsync(cancellation.Token);
        }
        finally
        {
            harness.Execution.PropertyChanged -= InterruptRestore;
        }

        Assert.True(interrupted);
        Assert.True(harness.Settings.IsLoadPending);
        Assert.False(harness.Settings.CanSave);
        Assert.Equal(saved.PreferredModelId, harness.Execution.PreferredModelId);
        Assert.Equal(saved.MaxConcurrency, harness.Execution.MaxConcurrency);
        Assert.Equal(saved.OutputDirectoryOverride, harness.Execution.OutputDirectoryOverride);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));

        ApplicationSettings incoming = saved with
        {
            PreferredModelId = "auto",
            MaxConcurrency = 1,
            OutputDirectoryOverride = Path.Combine(harness.Root, "replacement-output"),
        };
        await harness.SeedSettingsAsync(incoming);
        byte[] replacement = File.ReadAllBytes(harness.SettingsPath);
        harness.Execution.MaxConcurrency = 3;
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.Loaded, harness.Settings.LoadStatus);
        Assert.True(harness.Settings.CanSave);
        Assert.Equal(incoming.PreferredModelId, harness.Execution.PreferredModelId);
        Assert.Equal(3, harness.Execution.MaxConcurrency);
        Assert.Equal(incoming.OutputDirectoryOverride, harness.Execution.OutputDirectoryOverride);
        Assert.True(harness.Settings.HasUnsavedChanges);
        Assert.Equal(replacement, File.ReadAllBytes(harness.SettingsPath));
        Assert.False(Directory.Exists(incoming.OutputDirectoryOverride));
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Navigation_sync_and_value_reversion_do_not_mark_persisted_settings_dirty()
    {
        using SettingsHarness harness = new();
        await harness.LoadInputAsync();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsSaveStatus.Saved, harness.Settings.SaveStatus);
        byte[] saved = File.ReadAllBytes(harness.SettingsPath);
        QuantificationDefinition input = harness.Input.DefinitionDraft;
        QuantificationDefinition design = harness.Design.Draft;
        QuestionDesignItemViewModel question = harness.Design.Questions[0];
        string questionText = question.QuestionText;
        foreach (SettingsCategory category in Enum.GetValues<SettingsCategory>())
        {
            harness.Settings.SelectCategoryCommand.Execute(category);
        }

        harness.Input.PageSize = 1;
        harness.Input.PageIndex = 100;
        harness.Input.SelectedQuestion = null;
        harness.Input.SelectedQuestion = harness.Input.Questions[0];
        harness.Design.PageSize = 1;
        harness.Design.PageIndex = 100;
        harness.Design.SelectedQuestion = null;
        harness.Design.SelectedQuestion = question;
        harness.Design.SelectedImportedPrompt = harness.Design.ImportedPrompts[1];
        harness.Design.SelectedPromptTarget = ImportedPromptTarget.SpecialEvaluation;
        harness.Settings.SynchronizeDrafts();
        harness.Settings.SynchronizeDrafts();
        harness.Settings.RequestCloseCommand.Execute(null);
        Assert.Same(input, harness.Input.DefinitionDraft);
        Assert.Same(design, harness.Design.Draft);
        Assert.False(harness.Settings.HasUnsavedChanges);

        question.QuestionText = "一時変更";
        Assert.True(harness.Settings.HasUnsavedChanges);
        question.QuestionText = questionText;
        harness.Execution.MaxConcurrency = 2;
        Assert.True(harness.Settings.HasUnsavedChanges);
        harness.Execution.MaxConcurrency = 8;
        harness.Settings.SynchronizeDrafts();
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Equal(saved, File.ReadAllBytes(harness.SettingsPath));
        AssertNoBackgroundActivity(harness);
    }

    [Theory]
    [InlineData("basePoints")]
    [InlineData("specialPoints")]
    [InlineData("similarityPenaltyWeight")]
    [InlineData("questionPoints")]
    [InlineData("evaluatorWeight")]
    [InlineData("evaluatorMinimum")]
    [InlineData("evaluatorMaximum")]
    [InlineData("criterionWeight")]
    [InlineData("criterionMinimum")]
    [InlineData("criterionMaximum")]
    public async Task Decimal_scale_reversion_is_clean_for_definition_values_without_changing_file_format(string field)
    {
        using SettingsHarness harness = new();
        await harness.LoadInputAsync();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        QuantificationDesignViewModel design = harness.Design;
        QuestionDesignItemViewModel question = design.Questions[0];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[0];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[0];
        criterion.HasCustomRange = true;
        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsSaveStatus.Saved, harness.Settings.SaveStatus);
        QuantificationDefinition saved = Assert.IsType<QuantificationDefinition>(harness.Settings.StoredDefinition);
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        (decimal OriginalValue, Action<decimal> Edit) numeric = field switch
        {
            "basePoints" => (design.BasePoints, value => design.BasePoints = value),
            "specialPoints" => (design.SpecialPoints, value => design.SpecialPoints = value),
            "similarityPenaltyWeight" => (design.SimilarityPenaltyWeight, value => design.SimilarityPenaltyWeight = value),
            "questionPoints" => (question.Points, value => question.Points = value),
            "evaluatorWeight" => (evaluator.Weight, value => evaluator.Weight = value),
            "evaluatorMinimum" => (evaluator.Minimum, value => evaluator.Minimum = value),
            "evaluatorMaximum" => (evaluator.Maximum, value => evaluator.Maximum = value),
            "criterionWeight" => (criterion.Weight, value => criterion.Weight = value),
            "criterionMinimum" => (criterion.Minimum, value => criterion.Minimum = value),
            "criterionMaximum" => (criterion.Maximum, value => criterion.Maximum = value),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        decimal reverted = numeric.OriginalValue == 0m ? new decimal(0, 0, 0, false, 1) : numeric.OriginalValue * 1.0m;
        Assert.NotEqual(decimal.GetBits(numeric.OriginalValue)[3], decimal.GetBits(reverted)[3]);
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        commaCulture.NumberFormat.NumberDecimalSeparator = ",";
        try
        {
            CultureInfo.CurrentCulture = commaCulture;
            numeric.Edit(numeric.OriginalValue + 1m);
            Assert.True(harness.Settings.HasUnsavedChanges);
            numeric.Edit(reverted);
            Assert.False(harness.Settings.HasUnsavedChanges);
            AssertDefinition(saved, design.Draft);
            // Default serialization still preserves scale: normalization is private to comparison.
            Assert.False(JsonSerializer.SerializeToUtf8Bytes(saved).AsSpan()
                .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(design.Draft)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }

        harness.Settings.SynchronizeDrafts();
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        Assert.Same(saved, harness.Settings.StoredDefinition);
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Nullable_definition_score_range_presence_remains_distinct_from_equal_effective_values()
    {
        using SettingsHarness harness = new();
        await harness.LoadInputAsync();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        CriterionDesignItemViewModel criterion = harness.Design.Questions[0].Evaluators[0].Criteria[0];
        criterion.HasCustomRange = false;
        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsSaveStatus.Saved, harness.Settings.SaveStatus);
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        ScoreRange effective = criterion.EffectiveRange;
        Assert.Null(harness.Design.Draft.Questions[0].Evaluators[0].Criteria[0].Range);

        criterion.HasCustomRange = true;
        Assert.Equal(effective, criterion.EffectiveRange);
        Assert.NotNull(harness.Design.Draft.Questions[0].Evaluators[0].Criteria[0].Range);
        Assert.True(harness.Settings.HasUnsavedChanges);
        criterion.HasCustomRange = false;
        Assert.Null(harness.Design.Draft.Questions[0].Evaluators[0].Criteria[0].Range);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Unserializable_definition_remains_dirty_without_replacing_the_persisted_baseline()
    {
        using SettingsHarness harness = new();
        await harness.LoadInputAsync();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        QuantificationDefinition saved = Assert.IsType<QuantificationDefinition>(harness.Settings.StoredDefinition);
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        harness.Design.DefinitionName = "temporary-comparison-edit";
        harness.Design.DefinitionName = saved.Name;
        Assert.False(harness.Settings.HasUnsavedChanges);
        QuantificationDefinition valid = harness.Design.Draft;
        QuantificationDefinition invalid = valid with { Questions = default };
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.SerializeToUtf8Bytes(
            new ApplicationSettings { Definition = invalid }));
        // Public editors normalize default arrays. Inject this malformed definition only
        // to exercise Settings' serialization-failure path, without a new production seam.
        FieldInfo? draftField = typeof(QuantificationDesignViewModel).GetField("draft",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(draftField);
        try
        {
            draftField.SetValue(harness.Design, invalid);
            harness.Execution.MaxConcurrency = 2;
            harness.Execution.MaxConcurrency = 8;
            Assert.True(harness.Settings.HasUnsavedChanges);
            Assert.Same(invalid, harness.Design.Draft);
            Assert.Same(saved, harness.Settings.StoredDefinition);
            Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        }
        finally
        {
            draftField.SetValue(harness.Design, valid);
        }

        harness.Execution.MaxConcurrency = 2;
        harness.Execution.MaxConcurrency = 8;
        Assert.False(harness.Settings.HasUnsavedChanges);
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Alternating_edit_boundaries_preserve_latest_values_and_existing_editor_instances()
    {
        using SettingsHarness harness = new();
        await harness.LoadInputAsync();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        SettingsViewModel settings = harness.Settings;
        InputQuestionMappingViewModel mapping = harness.Input.Questions[0];
        QuestionDesignItemViewModel question = harness.Design.Questions[0];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[0];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[0];
        mapping.QuestionText = "入力で編集した設問";
        settings.SelectedCategory = SettingsCategory.Evaluation;
        Assert.Equal(mapping.QuestionText, question.QuestionText);

        evaluator.CustomPromptTemplate = "設計で編集 {回答} {評価項目}";
        criterion.Description = "保持する観点";
        settings.SelectedCategory = SettingsCategory.Mapping;
        Assert.Equal(evaluator.CustomPromptTemplate, harness.Input.DefinitionDraft.Questions[0].Evaluators[0].CustomPromptTemplate);
        mapping.SetSupportingColumn("C", true);
        settings.SelectedCategory = SettingsCategory.Special;
        Assert.Equal(new[] { "B", "C" }, harness.Design.Draft.Questions[0].SupportingSourceColumns);
        Assert.Equal("保持する観点", criterion.Description);

        settings.SelectedCategory = SettingsCategory.Common;
        harness.Design.DefinitionName = "共通内の最新の定義名";
        harness.Design.Revision = "latest-revision";
        harness.Execution.MaxConcurrency = 3;
        harness.Execution.OutputDirectoryOverride = harness.OutputPath;
        bool closeObservedSynchronized = false;
        settings.CloseRequested += (_, _) => closeObservedSynchronized =
            harness.Input.DefinitionDraft.Name == harness.Design.DefinitionName
            && harness.Input.DefinitionDraft.Revision == harness.Design.Revision;
        settings.RequestCloseCommand.Execute(null);
        Assert.True(closeObservedSynchronized);
        settings.SynchronizeDrafts();
        await settings.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsSaveStatus.Saved, settings.SaveStatus);
        Assert.Equal("共通内の最新の定義名", settings.StoredDefinition?.Name);
        Assert.Equal("latest-revision", settings.StoredDefinition?.Revision);
        AssertDefinition(harness.Design.Draft, settings.StoredDefinition);
        Assert.Same(mapping, harness.Input.Questions[0]);
        Assert.Same(question, harness.Design.SelectedQuestion);
        Assert.Same(evaluator, question.SelectedEvaluator);
        Assert.Same(criterion, evaluator.SelectedCriterion);
        Assert.False(settings.HasUnsavedChanges);
        Assert.False(Directory.Exists(harness.OutputPath));
        AssertNoBackgroundActivity(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Common_preference_notifications_do_not_steal_the_latest_definition_editor(bool inputIsLatest)
    {
        using SettingsHarness harness = new();
        await harness.LoadInputAsync();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsCategory.Common, harness.Settings.SelectedCategory);
        if (inputIsLatest)
        {
            harness.Input.Questions[0].QuestionText = "最新の入力編集";
        }
        else
        {
            harness.Design.DefinitionName = "最新の定義 root 編集";
        }

        QuantificationDefinition latest = inputIsLatest ? harness.Input.DefinitionDraft : harness.Design.Draft;
        harness.Execution.MaxConcurrency = 2;
        harness.Execution.OutputDirectoryOverride = harness.OutputPath;
        harness.Execution.SelectedModelId = "model-test";

        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);

        AssertDefinition(latest, harness.Settings.StoredDefinition);
        AssertDefinition(latest, harness.Input.DefinitionDraft);
        AssertDefinition(latest, harness.Design.Draft);
        Assert.False(harness.Settings.HasUnsavedChanges);
        AssertNoBackgroundActivity(harness);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("allocation")]
    [InlineData("prompt")]
    [InlineData("concurrency")]
    [InlineData("output")]
    [InlineData("unicode")]
    public async Task Invalid_draft_save_keeps_old_file_and_stored_snapshot_and_reports_only_safe_status(string invalid)
    {
        using SettingsHarness harness = new();
        await harness.LoadInputAsync();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        QuantificationDefinition stored = Assert.IsType<QuantificationDefinition>(harness.Settings.StoredDefinition);
        switch (invalid)
        {
            case "name": harness.Design.DefinitionName = string.Empty; break;
            case "allocation": harness.Design.BasePoints += 0.0001m; break;
            case "prompt": harness.Design.Questions[0].Evaluators[0].CustomPromptTemplate = "{" + PrivateCanary + "}"; break;
            case "concurrency": harness.Execution.MaxConcurrency = 17; break;
            case "output": harness.Execution.OutputDirectoryOverride = "relative/" + PrivateCanary; break;
            case "unicode": harness.Design.DefinitionName = PrivateCanary + "\uD800"; break;
            default: throw new ArgumentOutOfRangeException(nameof(invalid));
        }

        Assert.True(harness.Settings.HasUnsavedChanges);
        Assert.True(harness.Settings.SaveCommand.CanExecute(null));
        harness.Settings.SaveCommand.Execute(null);
        Task saving = Assert.IsAssignableFrom<Task>(harness.Settings.LastSaveTask);
        await saving;

        Assert.True(saving.IsCompletedSuccessfully);
        Assert.False(harness.Settings.IsSaving);
        Assert.Equal(SettingsSaveStatus.InvalidSettings, harness.Settings.SaveStatus);
        Assert.True(harness.Settings.HasUnsavedChanges);
        Assert.Same(stored, harness.Settings.StoredDefinition);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        Assert.Equal(harness.SettingsPath, Assert.Single(Directory.GetFiles(harness.SettingsDirectory)));
        Assert.Contains("保存失敗", harness.Settings.StatusText, StringComparison.Ordinal);
        AssertSafeStatus(harness);
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task First_common_only_save_does_not_materialize_unloaded_input_or_default_design()
    {
        using SettingsHarness harness = new();
        QuantificationDefinition input = harness.Input.DefinitionDraft;
        QuantificationDefinition design = harness.Design.Draft;
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        harness.Execution.MaxConcurrency = 2;
        Assert.True(harness.Settings.HasUnsavedChanges);

        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);

        ApplicationSettings saved = await harness.ReadSettingsAsync();
        Assert.Equal(SettingsSaveStatus.Saved, harness.Settings.SaveStatus);
        Assert.Equal(2, saved.MaxConcurrency);
        Assert.Null(saved.Definition);
        Assert.Null(harness.Settings.StoredDefinition);
        Assert.Null(saved.OutputDirectoryOverride);
        Assert.Same(input, harness.Input.DefinitionDraft);
        Assert.Same(design, harness.Design.Draft);
        Assert.False(harness.Settings.CanEditDefinition);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Equal(0, harness.Loader.CallCount);
        AssertNoBackgroundActivity(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Common_only_save_without_loaded_input_keeps_saved_definition_not_placeholder_drafts(bool inputWasLoaded)
    {
        using SettingsHarness harness = new();
        ApplicationSettings saved = SavedSettings(harness);
        await harness.SeedSettingsAsync(saved);
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        if (inputWasLoaded)
        {
            await harness.LoadInputAsync();
            harness.Input.FilePath = string.Empty;
        }

        Assert.False(harness.Input.HasLoadedWorkbook);
        Assert.False(harness.Settings.CanEditDefinition);
        harness.Design.DefinitionName = "未読込状態で保存してはいけない placeholder";
        harness.Input.AddQuestion();
        Assert.False(harness.Settings.HasUnsavedChanges);
        harness.Execution.MaxConcurrency = 3;
        harness.Execution.OutputDirectoryOverride = null;

        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        ApplicationSettings actual = await harness.ReadSettingsAsync();

        Assert.Equal(SettingsSaveStatus.Saved, harness.Settings.SaveStatus);
        Assert.Equal(3, actual.MaxConcurrency);
        Assert.Null(actual.OutputDirectoryOverride);
        AssertDefinition(saved.Definition!, actual.Definition);
        AssertDefinition(saved.Definition!, harness.Settings.StoredDefinition);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.False(Directory.Exists(harness.OutputPath));
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Save_round_trips_latest_canonical_definition_without_implicit_output_or_runtime_navigation_prompt_state()
    {
        using SettingsHarness harness = new();
        await harness.LoadInputAsync();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        QuantificationDefinition latest = SavedDefinition();
        harness.Design.SynchronizeFromInput(latest, harness.Input.AvailableColumnNames);
        harness.Execution.Configure(latest, harness.Input.Metadata!, harness.InputPath);
        Assert.EndsWith("result", harness.Execution.OutputDirectory, StringComparison.Ordinal);
        Assert.Null(harness.Execution.OutputDirectoryOverride);
        harness.Design.SelectedImportedPrompt = harness.Design.ImportedPrompts[1];
        harness.Settings.SelectedCategory = SettingsCategory.ImportedPrompts;
        harness.Execution.SelectedModelId = "unconfirmed-preference";
        harness.Execution.MaxConcurrency = 3;

        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        ApplicationSettings restored = await harness.ReadSettingsAsync();

        AssertDefinition(latest, restored.Definition);
        Assert.Equal(Canonical.ComputeSha256(latest), Canonical.ComputeSha256(restored.Definition!));
        Assert.Equal("unconfirmed-preference", restored.PreferredModelId);
        Assert.Equal(3, restored.MaxConcurrency);
        Assert.Null(restored.OutputDirectoryOverride);
        Assert.Null(harness.Execution.SelectedModelId);
        Assert.False(harness.Settings.HasUnsavedChanges);
        byte[] bytes = File.ReadAllBytes(harness.SettingsPath);
        using JsonDocument json = JsonDocument.Parse(bytes);
        Assert.Equal(new[] { "definition", "maxConcurrency", "outputDirectoryOverride", "preferredModelId", "schemaVersion" },
            json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        string content = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain(Path.GetFileName(harness.InputPath), content, StringComparison.Ordinal);
        Assert.DoesNotContain("UNAPPLIED-T10", content, StringComparison.Ordinal);
        Assert.DoesNotContain("authenticationState", content, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedCategory", content, StringComparison.Ordinal);
        Assert.False(Directory.Exists(harness.Execution.OutputDirectory));
        AssertSafeStatus(harness);
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Authentication_failure_does_not_erase_loaded_model_preference_or_mark_navigation_dirty()
    {
        using SettingsHarness harness = new(authenticationState: ExecutionAuthenticationState.RuntimeFailed);
        ApplicationSettings saved = SavedSettings(harness);
        await harness.SeedSettingsAsync(saved);
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);

        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        harness.Execution.SelectedModelId = null;
        harness.Settings.SelectedCategory = SettingsCategory.Common;

        Assert.Equal(ExecutionAuthenticationState.RuntimeFailed, harness.Execution.AuthenticationState);
        Assert.Equal(saved.PreferredModelId, harness.Execution.PreferredModelId);
        Assert.Null(harness.Execution.SelectedModelId);
        Assert.False(harness.Settings.HasUnsavedChanges);
        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(saved.PreferredModelId, (await harness.ReadSettingsAsync()).PreferredModelId);
        AssertNoBackgroundActivity(harness, authenticationChecks: 1);
    }

    [Fact]
    public async Task Explicit_apply_updates_both_editors_on_success_and_preserves_instances_and_imported_prompts()
    {
        using SettingsHarness harness = new();
        ApplicationSettings saved = SavedSettings(harness);
        await harness.SeedSettingsAsync(saved);
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(harness.Settings.CanApplySavedDefinition);
        await harness.LoadInputAsync();
        Assert.True(harness.Settings.CanApplySavedDefinition);
        Assert.True(harness.Settings.HasUnsavedChanges);
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        InputQuestionMappingViewModel mapping = harness.Input.Questions[0];
        QuestionDesignItemViewModel question = harness.Design.Questions[0];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[0];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[0];
        ImportedPromptViewModel[] prompts = harness.Design.ImportedPrompts.ToArray();
        harness.Design.SelectedImportedPrompt = prompts[1];
        int previousCalls = harness.Loader.CallCount;

        harness.Settings.ApplySavedDefinitionCommand.Execute(null);
        Task<bool> applying = Assert.IsAssignableFrom<Task<bool>>(harness.Settings.LastApplySavedDefinitionTask);
        Assert.True(await applying);

        Assert.Equal(previousCalls + 1, harness.Loader.CallCount);
        AssertDefinition(saved.Definition!, harness.Input.DefinitionDraft);
        AssertDefinition(saved.Definition!, harness.Design.Draft);
        Assert.Same(mapping, harness.Input.Questions[0]);
        Assert.Same(question, harness.Design.SelectedQuestion);
        Assert.Same(evaluator, question.SelectedEvaluator);
        Assert.Same(criterion, evaluator.SelectedCriterion);
        Assert.Same(prompts[1], harness.Design.SelectedImportedPrompt);
        Assert.Equal(prompts, harness.Design.ImportedPrompts);
        Assert.Contains("適用しました", harness.Settings.ApplyStatusText, StringComparison.Ordinal);
        Assert.False(harness.Settings.IsApplying);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Failed_apply_preserves_both_divergent_drafts_metadata_and_settings_file()
    {
        using SettingsHarness harness = new();
        ApplicationSettings saved = SavedSettings(harness) with
        {
            Definition = SavedDefinition() with { SourceSheet = PrivateCanary },
        };
        await harness.SeedSettingsAsync(saved);
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await harness.LoadInputAsync();
        harness.Design.DefinitionName = "まだ Input と同期していない編集";
        QuantificationDefinition inputBefore = harness.Input.DefinitionDraft;
        QuantificationDefinition designBefore = harness.Design.Draft;
        var metadata = harness.Input.Metadata;
        var snapshot = harness.Input.Snapshot;
        byte[] original = File.ReadAllBytes(harness.SettingsPath);

        Assert.False(await harness.Settings.ApplySavedDefinitionAsync(TestContext.Current.CancellationToken));

        Assert.Same(inputBefore, harness.Input.DefinitionDraft);
        Assert.Same(designBefore, harness.Design.Draft);
        Assert.Same(metadata, harness.Input.Metadata);
        Assert.Same(snapshot, harness.Input.Snapshot);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        Assert.Contains("適用できませんでした", harness.Settings.ApplyStatusText, StringComparison.Ordinal);
        Assert.True(harness.Settings.CanApplySavedDefinition);
        AssertSafeStatus(harness);
        harness.Settings.SynchronizeDrafts();
        Assert.Equal(designBefore.Name, harness.Input.DefinitionDraft.Name);
        AssertNoBackgroundActivity(harness);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("input")]
    [InlineData("design")]
    public async Task Cancelled_or_superseded_apply_keeps_current_editors_and_releases_command_guards(string change)
    {
        using SettingsHarness harness = new();
        await harness.SeedSettingsAsync(SavedSettings(harness));
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await harness.LoadInputAsync();
        TaskCompletionSource<InputWorkbookLoadResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Loader.NextLoad = (_, _, _) => release.Task; // Deliberately ignores cancellation.
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<bool> applying = harness.Settings.ApplySavedDefinitionAsync(cancellation.Token);
        Assert.True(harness.Settings.IsApplying);
        Assert.False(harness.Settings.CanApplySavedDefinition);
        Assert.False(harness.Settings.CanSave);
        Assert.False(harness.Settings.CanEditDefinition);
        Assert.False(harness.Settings.RequestCloseCommand.CanExecute(null));
        Assert.False(harness.Settings.SelectCategoryCommand.CanExecute(SettingsCategory.Mapping));
        Assert.Same(applying, harness.Settings.ApplySavedDefinitionAsync(TestContext.Current.CancellationToken));
        if (change == "cancel")
        {
            cancellation.Cancel();
        }
        else if (change == "input")
        {
            harness.Input.Questions[0].QuestionText = "適用中の Input 編集";
        }
        else
        {
            harness.Design.DefinitionName = "適用中の Design 編集";
        }

        QuantificationDefinition input = harness.Input.DefinitionDraft;
        QuantificationDefinition design = harness.Design.Draft;
        try
        {
            release.TrySetResult(harness.Loader.Result!);
            Assert.False(await applying.WaitAsync(TestWait, TestContext.Current.CancellationToken));
        }
        finally
        {
            release.TrySetResult(harness.Loader.Result!);
            await applying.WaitAsync(TestWait, TestContext.Current.CancellationToken);
        }

        Assert.Same(input, harness.Input.DefinitionDraft);
        Assert.Same(design, harness.Design.Draft);
        Assert.False(harness.Settings.IsApplying);
        Assert.True(harness.Settings.CanApplySavedDefinition);
        Assert.True(harness.Settings.CanSave);
        Assert.True(harness.Settings.CanEditDefinition);
        AssertDefinition(SavedDefinition(), (await harness.ReadSettingsAsync()).Definition);
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Run_state_notifies_apply_guard_while_next_draft_save_keeps_active_request_unchanged()
    {
        using SettingsHarness harness = new();
        await harness.SeedSettingsAsync(SavedSettings(harness));
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await harness.LoadInputAsync();
        var summary = await U04TestSupport.CreateSummaryAsync(harness.Input.DefinitionDraft, harness.Input.Metadata!);
        TaskCompletionSource<RunSummary> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.RunnerHandler = (_, _, _) => release.Task;
        harness.Execution.Configure(harness.Input.DefinitionDraft, harness.Input.Metadata!, harness.InputPath);
        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.True(harness.Execution.CanStart);
        List<bool> availability = [];
        harness.Settings.ApplySavedDefinitionCommand.CanExecuteChanged += (_, _) =>
            availability.Add(harness.Settings.ApplySavedDefinitionCommand.CanExecute(null));
        int closeCount = 0;
        harness.Settings.CloseRequested += (_, _) => closeCount++;

        Task run = harness.Execution.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(harness.Execution.IsRunning);
            Assert.False(harness.Settings.CanApplySavedDefinition);
            Assert.False(harness.Settings.ApplySavedDefinitionCommand.CanExecute(null));
            Assert.Contains(false, availability);
            Assert.Contains("実行中", harness.Settings.ApplyAvailabilityText, StringComparison.Ordinal);
            int inputReads = harness.Loader.CallCount;
            harness.Settings.ApplySavedDefinitionCommand.Execute(null);
            Assert.False(await harness.Settings.ApplySavedDefinitionAsync(TestContext.Current.CancellationToken));
            Assert.Equal(inputReads, harness.Loader.CallCount);
            var request = harness.Runner.LastRequest!;
            string canonical = Canonical.Serialize(request.DraftDefinition);
            int concurrency = request.MaxConcurrency;
            harness.Design.DefinitionName = "次回用の編集";
            harness.Execution.MaxConcurrency = 3;
            Assert.True(harness.Settings.CanSave);
            await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
            Assert.Equal("次回用の編集", harness.Settings.StoredDefinition?.Name);
            Assert.True(harness.Execution.IsRunning);
            Assert.True(harness.Execution.CanCancel);
            Assert.Same(request, harness.Runner.LastRequest);
            Assert.Equal(canonical, Canonical.Serialize(request.DraftDefinition));
            Assert.Equal(concurrency, request.MaxConcurrency);
            Assert.Equal(1, harness.Runner.CallCount);
        }
        finally
        {
            release.TrySetResult(summary);
            await run.WaitAsync(TestWait, TestContext.Current.CancellationToken);
        }

        Assert.True(harness.Settings.CanApplySavedDefinition);
        Assert.True(availability[^1]);
        Assert.Equal(0, closeCount);
        Assert.Same(summary, harness.Execution.LastRunContext?.Summary);
        Assert.Equal(1, harness.Authentication.CallCount);
        Assert.Equal(1, harness.Runner.CallCount);
        Assert.Null(harness.Execution.LastLoginTask);
        Assert.False(Directory.Exists(harness.OutputPath));
    }

    [Theory]
    [InlineData("{PRIVATE-T10-CONTENT", SettingsLoadStatus.JsonInvalid)]
    [InlineData("{\"schemaVersion\":99,\"future\":\"PRIVATE-T10-CONTENT\"}", SettingsLoadStatus.UnsupportedVersion)]
    [InlineData("{\"schemaVersion\":1,\"unknown\":\"PRIVATE-T10-CONTENT\"}", SettingsLoadStatus.JsonInvalid)]
    public async Task Invalid_or_unknown_file_is_preserved_with_safe_status_until_explicit_save(string json, SettingsLoadStatus status)
    {
        using SettingsHarness harness = new();
        byte[] original = harness.WriteJson(json);
        QuantificationDefinition input = harness.Input.DefinitionDraft;
        QuantificationDefinition design = harness.Design.Draft;

        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, harness.Settings.LoadStatus);
        Assert.True(harness.Settings.IsInitialized);
        Assert.True(harness.Settings.CanSave);
        Assert.True(harness.Settings.HasUnsavedChanges);
        Assert.Null(harness.Settings.StoredDefinition);
        Assert.Same(input, harness.Input.DefinitionDraft);
        Assert.Same(design, harness.Design.Draft);
        Assert.Equal(8, harness.Execution.MaxConcurrency);
        Assert.Null(harness.Execution.PreferredModelId);
        Assert.Contains("元ファイルは変更していません", harness.Settings.StatusText, StringComparison.Ordinal);
        AssertSafeStatus(harness);
        harness.Settings.SelectedCategory = SettingsCategory.Mapping;
        harness.Settings.RequestCloseCommand.Execute(null);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));

        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new ApplicationSettings(), await harness.ReadSettingsAsync());
        Assert.Equal(SettingsSaveStatus.Saved, harness.Settings.SaveStatus);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Equal(0, harness.Loader.CallCount);
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Read_and_write_failure_keep_parent_file_and_redact_io_details_without_repair()
    {
        using SettingsHarness harness = new();
        Directory.CreateDirectory(harness.Root);
        byte[] sentinel = Encoding.UTF8.GetBytes(PrivateCanary);
        File.WriteAllBytes(harness.SettingsDirectory, sentinel);

        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.ReadFailed, harness.Settings.LoadStatus);
        Assert.True(harness.Settings.CanSave);
        Assert.True(harness.Settings.HasUnsavedChanges);
        Assert.Equal(sentinel, File.ReadAllBytes(harness.SettingsDirectory));
        Assert.False(Directory.Exists(harness.SettingsDirectory));
        AssertSafeStatus(harness);
        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsSaveStatus.WriteFailed, harness.Settings.SaveStatus);
        Assert.True(harness.Settings.HasUnsavedChanges);
        Assert.Equal(sentinel, File.ReadAllBytes(harness.SettingsDirectory));
        Assert.Equal(harness.SettingsDirectory, Assert.Single(Directory.GetFileSystemEntries(harness.Root)));
        AssertSafeStatus(harness);
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Failed_reload_retains_saved_snapshot_and_current_editors_without_rewriting_the_file()
    {
        using SettingsHarness harness = new();
        await harness.SeedSettingsAsync(SavedSettings(harness));
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await harness.LoadInputAsync();
        harness.Design.DefinitionName = "再読込前の未保存編集";
        harness.Execution.MaxConcurrency = 3;
        QuantificationDefinition? stored = harness.Settings.StoredDefinition;
        QuantificationDefinition input = harness.Input.DefinitionDraft;
        QuantificationDefinition design = harness.Design.Draft;
        byte[] broken = harness.WriteJson("{\"schemaVersion\":1,\"unknown\":\"" + PrivateCanary + "\"}");

        await harness.Settings.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.JsonInvalid, harness.Settings.LoadStatus);
        Assert.True(harness.Settings.HasUnsavedChanges);
        Assert.Same(stored, harness.Settings.StoredDefinition);
        Assert.Same(input, harness.Input.DefinitionDraft);
        Assert.Same(design, harness.Design.Draft);
        Assert.Equal(3, harness.Execution.MaxConcurrency);
        Assert.Equal("model-test", harness.Execution.PreferredModelId);
        Assert.Equal(broken, File.ReadAllBytes(harness.SettingsPath));
        AssertSafeStatus(harness);
        AssertNoBackgroundActivity(harness);
    }

    [Fact(SkipUnless = nameof(IsWindows), Skip = "Requires Windows exclusive file-sharing semantics.")]
    public async Task Failed_replace_keeps_saved_bytes_and_snapshot_and_explicit_retry_recovers()
    {
        using SettingsHarness harness = new();
        await harness.SeedSettingsAsync(SavedSettings(harness));
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        QuantificationDefinition? stored = harness.Settings.StoredDefinition;
        harness.Execution.MaxConcurrency = 3;
        using (FileStream held = new(harness.SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
            Assert.Equal(SettingsSaveStatus.WriteFailed, harness.Settings.SaveStatus);
            Assert.True(harness.Settings.HasUnsavedChanges);
            Assert.Same(stored, harness.Settings.StoredDefinition);
            Assert.False(harness.Settings.IsSaving);
        }

        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        Assert.Equal(harness.SettingsPath, Assert.Single(Directory.GetFiles(harness.SettingsDirectory)));
        AssertSafeStatus(harness);
        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SettingsSaveStatus.Saved, harness.Settings.SaveStatus);
        Assert.Equal(3, (await harness.ReadSettingsAsync()).MaxConcurrency);
        Assert.False(harness.Settings.HasUnsavedChanges);
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Save_freezes_values_before_notifications_blocks_duplicate_commands_and_keeps_new_edits_dirty()
    {
        using SettingsHarness harness = new();
        await harness.LoadInputAsync();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        harness.Design.DefinitionName = "保存開始時の定義";
        harness.Execution.MaxConcurrency = 2;
        QuantificationDefinition frozen = harness.Design.Draft;
        bool edited = false;
        void EditAfterFreeze(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(SettingsViewModel.IsSaving) || !harness.Settings.IsSaving || edited)
            {
                return;
            }

            edited = true;
            Assert.False(harness.Settings.CanSave);
            Assert.False(harness.Settings.SaveCommand.CanExecute(null));
            harness.Settings.SaveCommand.Execute(null);
            _ = harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
            harness.Design.DefinitionName = "保存開始後の未保存編集";
            harness.Execution.MaxConcurrency = 3;
        }

        // Deterministic reentrancy after freezing, independent of how fast real file IO completes.
        harness.Settings.PropertyChanged += EditAfterFreeze;
        try
        {
            Task saving = harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
            Assert.Same(saving, harness.Settings.LastSaveTask);
            await saving;
        }
        finally
        {
            harness.Settings.PropertyChanged -= EditAfterFreeze;
        }

        Assert.True(edited);
        Assert.Equal(SettingsSaveStatus.Saved, harness.Settings.SaveStatus);
        ApplicationSettings saved = await harness.ReadSettingsAsync();
        Assert.Equal(2, saved.MaxConcurrency);
        AssertDefinition(frozen, saved.Definition);
        AssertDefinition(frozen, harness.Settings.StoredDefinition);
        Assert.Equal("保存開始後の未保存編集", harness.Design.DefinitionName);
        Assert.Equal(3, harness.Execution.MaxConcurrency);
        Assert.True(harness.Settings.HasUnsavedChanges);
        Assert.Contains("未保存", harness.Settings.StatusText, StringComparison.Ordinal);
        Assert.True(harness.Settings.CanSave);
        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Equal("保存開始後の未保存編集", (await harness.ReadSettingsAsync()).Definition?.Name);
        AssertNoBackgroundActivity(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Edits_after_obtaining_save_task_remain_dirty_and_do_not_change_the_saved_value(bool inputEdit)
    {
        using SettingsHarness harness = new();
        await harness.LoadInputAsync();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        harness.Design.DefinitionName = "保存する値";
        QuantificationDefinition frozen = harness.Design.Draft;

        Task saving = harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        if (inputEdit)
        {
            harness.Input.Questions[0].QuestionText = "await 前の Input 編集";
        }
        else
        {
            harness.Design.DefinitionName = "await 前の Design 編集";
        }

        harness.Execution.MaxConcurrency = 3;
        await saving;

        Assert.Equal(SettingsSaveStatus.Saved, harness.Settings.SaveStatus);
        ApplicationSettings saved = await harness.ReadSettingsAsync();
        AssertDefinition(frozen, saved.Definition);
        Assert.Equal(8, saved.MaxConcurrency);
        Assert.True(harness.Settings.HasUnsavedChanges);
        harness.Settings.SynchronizeDrafts();
        Assert.Equal(inputEdit ? "await 前の Input 編集" : frozen.Questions[0].QuestionText,
            harness.Design.Questions[0].QuestionText);
        Assert.Equal(inputEdit ? frozen.Name : "await 前の Design 編集", harness.Input.DefinitionDraft.Name);
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Cancelled_save_preserves_file_and_returns_an_awaitable_safe_task_then_command_can_retry()
    {
        using SettingsHarness harness = new();
        await harness.SeedSettingsAsync(SavedSettings(harness));
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        harness.Execution.MaxConcurrency = 3;
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Task saving = harness.Settings.SaveAsync(cancellation.Token);
        await saving;

        Assert.True(saving.IsCompletedSuccessfully);
        Assert.False(harness.Settings.IsSaving);
        Assert.Null(harness.Settings.SaveStatus);
        Assert.True(harness.Settings.HasUnsavedChanges);
        Assert.Contains("取り消しました", harness.Settings.StatusText, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        harness.Settings.SaveCommand.Execute(null);
        await Assert.IsAssignableFrom<Task>(harness.Settings.LastSaveTask);
        Assert.Equal(SettingsSaveStatus.Saved, harness.Settings.SaveStatus);
        Assert.False(harness.Settings.HasUnsavedChanges);
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Dispose_detaches_notifications_disables_commands_and_leaves_editor_lifetimes_to_parent()
    {
        using SettingsHarness harness = new();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        int notifications = 0;
        int closeRequests = 0;
        harness.Settings.PropertyChanged += (_, _) => notifications++;
        harness.Settings.CloseRequested += (_, _) => closeRequests++;
        harness.Settings.Dispose();
        int afterDispose = notifications;

        harness.Input.AddQuestion();
        harness.Design.DefinitionName = "disposed Settings の外で編集";
        harness.Execution.MaxConcurrency = 3;
        harness.Settings.SelectedCategory = SettingsCategory.Mapping;
        harness.Settings.SynchronizeDrafts();
        harness.Settings.RequestCloseCommand.Execute(null);
        harness.Settings.SaveCommand.Execute(null);
        harness.Settings.ApplySavedDefinitionCommand.Execute(null);
        await harness.Settings.LoadAsync(TestContext.Current.CancellationToken);
        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        Assert.False(await harness.Settings.ApplySavedDefinitionAsync(TestContext.Current.CancellationToken));
        harness.Settings.Dispose();

        Assert.Equal(afterDispose, notifications);
        Assert.Equal(0, closeRequests);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.False(harness.Settings.CanSave);
        Assert.False(harness.Settings.CanEditDefinition);
        Assert.False(harness.Settings.CanApplySavedDefinition);
        Assert.False(harness.Settings.SelectCategoryCommand.CanExecute(SettingsCategory.Common));
        Assert.False(harness.Settings.RequestCloseCommand.CanExecute(null));
        Assert.Equal(SettingsCategory.Common, harness.Settings.SelectedCategory);
        Assert.True(harness.Execution.CanCheckAuthentication);
        Assert.False(Directory.Exists(harness.Root));
        AssertNoBackgroundActivity(harness);
    }

    [Fact]
    public async Task Dispose_during_saved_definition_read_cancels_application_without_changing_either_editor()
    {
        using SettingsHarness harness = new();
        await harness.SeedSettingsAsync(SavedSettings(harness));
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await harness.LoadInputAsync();
        QuantificationDefinition input = harness.Input.DefinitionDraft;
        QuantificationDefinition design = harness.Design.Draft;
        byte[] original = File.ReadAllBytes(harness.SettingsPath);
        TaskCompletionSource<InputWorkbookLoadResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Loader.NextLoad = (_, _, _) => release.Task;
        Task<bool> applying = harness.Settings.ApplySavedDefinitionAsync(TestContext.Current.CancellationToken);
        int notifications = 0;
        harness.Settings.PropertyChanged += (_, _) => notifications++;
        harness.Settings.Dispose();
        int afterDispose = notifications;
        release.TrySetResult(harness.Loader.Result!);

        Assert.False(await applying.WaitAsync(TestWait, TestContext.Current.CancellationToken));

        Assert.Same(input, harness.Input.DefinitionDraft);
        Assert.Same(design, harness.Design.Draft);
        Assert.Equal(afterDispose, notifications);
        Assert.False(harness.Input.IsBusy);
        Assert.Equal(original, File.ReadAllBytes(harness.SettingsPath));
        AssertNoBackgroundActivity(harness);
    }

    private static ApplicationSettings SavedSettings(SettingsHarness harness) => new()
    {
        PreferredModelId = "model-test",
        MaxConcurrency = 2,
        OutputDirectoryOverride = harness.OutputPath,
        Definition = SavedDefinition(),
    };

    private static QuantificationDefinition SavedDefinition()
    {
        QuantificationDefinition seed = U04TestSupport.Definition(2, 3);
        QuestionDefinition question = seed.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        return seed with
        {
            Id = "saved-T10-definition",
            Name = "保存した採点定義 日本語 🌸",
            Revision = "rev-3",
            BasePoints = 60.125m,
            SpecialPoints = 10.375m,
            SimilarityPenaltyWeight = 0.125m,
            RoundingDigits = 3,
            Questions =
            [
                question with
                {
                    QuestionText = "保存された見出し由来の設問文\r\n" + PrivateCanary,
                    Points = 29.5m,
                    Evaluators =
                    [
                        evaluator with
                        {
                            CustomPromptTemplate = PrivateCanary + "\r\n{設問} {回答} {補助情報}\n{評価項目} {最小点} {最大点} {{literal}}",
                            Weight = 1.375m,
                            Criteria = [evaluator.Criteria[0] with { Weight = 0.125m, Range = new ScoreRange(-0.25m, 9.75m) }],
                        },
                    ],
                    SpecialEvaluations =
                    [
                        new SpecialEvaluationDefinition
                        {
                            Id = "S1",
                            DisplayName = "保存した固有項目",
                            PrimarySourceColumn = "C",
                            SupportingSourceColumns = ["B", "A"],
                            PromptTemplate = "固有評価\r\n{回答} {補助情報}",
                        },
                    ],
                },
            ],
        };
    }

    private static void AssertDefinition(QuantificationDefinition expected, QuantificationDefinition? actual)
    {
        QuantificationDefinition value = Assert.IsType<QuantificationDefinition>(actual);
        Assert.Equal(Canonical.SerializeToUtf8Bytes(expected), Canonical.SerializeToUtf8Bytes(value));
        Assert.Equal(Canonical.ComputeSha256(expected), Canonical.ComputeSha256(value));
    }

    private static void AssertSafeStatus(SettingsHarness harness)
    {
        string exposed = string.Join("|", harness.Settings.StatusText, harness.Settings.ApplyStatusText,
            harness.Settings.DefinitionAvailabilityText, harness.Settings.ApplyAvailabilityText,
            harness.Settings.ToString());
        Assert.DoesNotContain(PrivateCanary, exposed, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Root, exposed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(IOException), exposed, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(UnauthorizedAccessException), exposed, StringComparison.Ordinal);
    }

    private static void AssertNoBackgroundActivity(SettingsHarness harness, int authenticationChecks = 0)
    {
        Assert.Equal(authenticationChecks, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Runner.CallCount);
        Assert.Null(harness.Execution.LastLoginTask);
        Assert.False(harness.Execution.IsLoggingIn);
        Assert.False(harness.Execution.IsRunning);
    }

    private sealed class SettingsHarness : IDisposable
    {
        public SettingsHarness(bool withStore = true, ExecutionAuthenticationState authenticationState = ExecutionAuthenticationState.Available)
        {
            Store = new SettingsFileStore(SettingsPath);
            Input = new InputViewModel(Loader);
            Design = new QuantificationDesignViewModel(null, null,
            [
                new ImportedPrompt { Path = Path.Combine(Root, "02.txt"), DisplayName = "02.txt", Content = "UNAPPLIED-T10-CUSTOM {回答} {評価項目}" },
                new ImportedPrompt { Path = Path.Combine(Root, "01.txt"), DisplayName = "01.txt", Content = "UNAPPLIED-T10-SPECIAL {回答}" },
            ]);
            Authentication = new RecordingAuthenticationBoundary(authenticationState == ExecutionAuthenticationState.Available
                ? new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.Available,
                    [U04TestSupport.Model("model-test"), U04TestSupport.Model("auto")], U04TestSupport.RuntimeIdentity())
                : new ExecutionAuthenticationSnapshot(authenticationState));
            Runner = new RecordingRunBoundary((request, progress, token) => RunnerHandler(request, progress, token));
            Execution = new ExecutionViewModel(Authentication, Runner);
            Settings = new SettingsViewModel(Input, Design, Execution, withStore ? Store : null);
        }

        // Every file operation is scoped to injected synthetic paths; never resolve user data.
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-T10-" + Guid.NewGuid().ToString("N"));
        public string SettingsDirectory => Path.Combine(Root, "合成 設定");
        public string SettingsPath => Path.Combine(SettingsDirectory, "setting.txt");
        public string OutputPath => Path.Combine(Root, "未作成の出力先");
        public string InputPath => Path.Combine(Root, "synthetic-T10-input.xlsx");
        public ScriptedInputLoader Loader { get; } = new();
        public InputViewModel Input { get; }
        public QuantificationDesignViewModel Design { get; }
        public RecordingAuthenticationBoundary Authentication { get; }
        public RecordingRunBoundary Runner { get; }
        public ExecutionViewModel Execution { get; }
        public SettingsFileStore Store { get; }
        public SettingsViewModel Settings { get; }
        public Func<QuantificationRunRequest, Action<EvaluationProgress>?, CancellationToken, Task<RunSummary>> RunnerHandler { get; set; } =
                (_, _, _) => throw new InvalidOperationException("No run was authorized by this test.");

        public async Task LoadInputAsync()
        {
            QuantificationDefinition definition = U04TestSupport.Definition(2, 3);
            var metadata = U01TestSupport.ValidateMapping(definition).Metadata;
            Loader.Result = new InputWorkbookLoadResult(U01TestSupport.InputSnapshot(), metadata,
                new ColumnMappingSuggester().Suggest(metadata));
            await Input.SetFilePathAsync(InputPath, TestContext.Current.CancellationToken);
            Assert.True(await Input.ApplySavedDefinitionAsync(definition, TestContext.Current.CancellationToken));
            Settings.SynchronizeDrafts();
            Assert.True(Input.HasLoadedWorkbook);
            Assert.True(Settings.CanEditDefinition);
        }

        public async Task SeedSettingsAsync(ApplicationSettings settings) =>
            Assert.Equal(SettingsSaveStatus.Saved, (await Store.SaveAsync(settings, TestContext.Current.CancellationToken)).Status);

        public async Task<ApplicationSettings> ReadSettingsAsync()
        {
            SettingsLoadResult loaded = await new SettingsFileStore(SettingsPath).LoadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
            return Assert.IsType<ApplicationSettings>(loaded.Settings);
        }

        public byte[] WriteJson(string json)
        {
            Directory.CreateDirectory(SettingsDirectory);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            File.WriteAllBytes(SettingsPath, bytes);
            return bytes;
        }

        public void Dispose()
        {
            Settings.Dispose();
            Execution.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    // Reuses the existing input boundary, not a new filesystem interface. Metadata comes
    // from U01's synthetic workbook; the input path and snapshots never read user files.
    private sealed class ScriptedInputLoader : IInputWorkbookLoader
    {
        public InputWorkbookLoadResult? Result { get; set; }
        public Func<string, uint, CancellationToken, Task<InputWorkbookLoadResult>>? NextLoad { get; set; }
        public int CallCount { get; private set; }

        public Task<InputWorkbookLoadResult> LoadAsync(string filePath, uint headerRow, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            var next = NextLoad;
            NextLoad = null;
            return next is not null ? next(filePath, headerRow, cancellationToken)
                : Task.FromResult(Result ?? throw new InvalidOperationException("No synthetic input was configured."));
        }
    }
}
