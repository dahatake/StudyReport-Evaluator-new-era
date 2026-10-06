using System.ComponentModel;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using StudyReportEvaluator.App.Composition;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.UI;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Composition;

public sealed class SettingsCompositionTests
{
    private const string PrivateCanary = "PRIVATE-T23-SETTINGS";
    private static readonly TimeSpan TestWait = TimeSpan.FromSeconds(10);
    private static readonly CanonicalDefinitionSerializer Canonical = new();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Legacy_registration_constructors_never_resolve_or_read_user_settings(int constructor)
    {
        ServiceRegistration services = constructor switch
        {
            0 => new ServiceRegistration(),
            1 => new ServiceRegistration(new WorkflowNavigator()),
            2 => new ServiceRegistration(new WorkflowNavigator(), LaunchStartupState.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(constructor)),
        };
        using CompositionHarness harness = new(services);

        AssertUnconfigured(harness.Shell.Settings);
        await services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);
        harness.Shell.OpenSettings();
        harness.Shell.CloseSettings();
        harness.Shell.NextCommand.Execute(null);

        AssertUnconfigured(harness.Shell.Settings);
        Assert.Equal(0, harness.Loader.CallCount);
        AssertNoRuntimeActivity(harness);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Legacy_main_window_view_model_constructors_remain_settings_IO_free(int constructor)
    {
        using MainWindowViewModel shell = constructor switch
        {
            0 => new MainWindowViewModel(new WorkflowNavigator()),
            1 => new MainWindowViewModel(new WorkflowNavigator(), new InputViewModel(), new QuantificationDesignViewModel()),
            2 => new MainWindowViewModel(new WorkflowNavigator(), new InputViewModel(), new QuantificationDesignViewModel(),
                new ExecutionViewModel(), new ResultsOutputViewModel()),
            _ => throw new ArgumentOutOfRangeException(nameof(constructor)),
        };

        AssertUnconfigured(shell.Settings);
        await shell.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        AssertUnconfigured(shell.Settings);
        AssertPassive(shell);
    }

    [Fact]
    public async Task Explicit_store_constructor_uses_only_the_injected_absolute_file_and_requires_initialization()
    {
        using SettingsDirectory directory = new();
        string path = Path.Combine(directory.Root, "explicit", "setting.txt");
        SettingsFileStore store = new(path);
        ServiceRegistration services = new(new WorkflowNavigator(), LaunchStartupState.Empty, store);
        using CompositionHarness harness = new(services);

        Assert.Equal(path, harness.Shell.Settings.FilePath);
        Assert.Null(harness.Shell.Settings.LastLoadTask);
        Assert.True(harness.Shell.Settings.IsLoadPending);
        Assert.False(harness.Shell.Settings.CanSave);
        Assert.False(Directory.Exists(directory.Root));
        Assert.Throws<ArgumentException>(() => new SettingsFileStore("setting.txt"));
        Assert.Throws<ArgumentNullException>(() => ServiceRegistration.FromStartup(null!, directory.LocalData));
        Assert.Throws<ArgumentNullException>(() => services.CreateMainWindowViewModel(null!, harness.Execution));
        Assert.Throws<ArgumentNullException>(() => services.CreateMainWindowViewModel(harness.Input, null!));
        Assert.Throws<ArgumentNullException>(() => { _ = services.InitializeAsync(null!, TestContext.Current.CancellationToken); });

        Task initialization = services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);
        Assert.Same(initialization, harness.Shell.Settings.LastLoadTask);
        await initialization.WaitAsync(TestWait, TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.Missing, harness.Shell.Settings.LoadStatus);
        Assert.False(Directory.Exists(directory.Root));
        AssertNoRuntimeActivity(harness);
    }

    [Fact]
    public void Production_factory_uses_the_OS_local_data_location_without_reading_it_during_VM_construction()
    {
        // Resolve only the BCL path. Never initialize or create a window with this
        // registration: all tests that perform settings IO inject a temporary path.
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty);
        using MainWindowViewModel shell = services.CreateMainWindowViewModel();
        string expected = string.IsNullOrWhiteSpace(localData) || !Path.IsPathFullyQualified(localData)
            ? string.Empty
            : Path.GetFullPath(Path.Combine(localData, "StudyReportEvaluator", "setting.txt"));

        Assert.Equal(expected, shell.Settings.FilePath);
        Assert.Null(shell.Settings.LastLoadTask);
        Assert.Null(shell.Settings.LoadStatus);
        Assert.False(shell.Settings.CanSave);
        AssertPassive(shell);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative-local-data")]
    [InlineData("C:relative-local-data")]
    public async Task Unavailable_or_relative_local_data_disables_persistence_without_cwd_or_EXE_fallback(string? localData)
    {
        ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty, localData);
        using CompositionHarness harness = new(services);

        await services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);

        AssertUnconfigured(harness.Shell.Settings);
        Assert.Contains("保存先が構成されていません", harness.Shell.Settings.StatusText, StringComparison.Ordinal);
        Assert.Equal(0, harness.Loader.CallCount);
        AssertNoRuntimeActivity(harness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_absolute_local_data_is_a_safe_unconfigured_state_not_a_GUI_startup_failure(bool invalidUnicode)
    {
        using SettingsDirectory directory = new();
        string invalidPath = directory.LocalData + (invalidUnicode ? "\uD800" : "\0");
        ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty, invalidPath);
        using CompositionHarness harness = new(services);

        await services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);

        AssertUnconfigured(harness.Shell.Settings);
        Assert.DoesNotContain(directory.Root, harness.Shell.Settings.StatusText, StringComparison.Ordinal);
        Assert.False(Directory.Exists(directory.Root));
        AssertNoRuntimeActivity(harness);
    }

    [Fact]
    public async Task Missing_settings_at_the_production_relative_location_does_not_create_any_file_or_directory()
    {
        using SettingsDirectory directory = new();
        ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty, directory.LocalData);
        using CompositionHarness harness = new(services);
        SettingsViewModel settings = harness.Shell.Settings;

        Assert.Equal(directory.SettingsPath, settings.FilePath);
        settings.SaveCommand.Execute(null);
        await settings.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Null(settings.LastSaveTask);
        Assert.False(Directory.Exists(directory.Root));

        await services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.Missing, settings.LoadStatus);
        Assert.True(settings.IsInitialized);
        Assert.True(settings.CanSave);
        Assert.False(settings.HasUnsavedChanges);
        Assert.Null(settings.StoredDefinition);
        Assert.Null(settings.LastSaveTask);
        Assert.False(Directory.Exists(directory.Root));
        Assert.Equal(0, harness.Loader.CallCount);
        AssertNoRuntimeActivity(harness);
    }

    [Fact]
    public async Task Valid_settings_restore_preferences_but_never_input_definition_or_imported_prompts()
    {
        using SettingsDirectory directory = new();
        ApplicationSettings saved = SavedSettings(directory);
        await directory.SeedAsync(saved);
        byte[] original = File.ReadAllBytes(directory.SettingsPath);
        string promptPath = directory.WritePrompt("unapplied.txt", "UNAPPLIED-T23 {回答} {評価項目}");
        LaunchStartupState startup = LaunchStartupState.Create(["--prompt", promptPath], directory.Root);
        ServiceRegistration services = ServiceRegistration.FromStartup(startup, directory.LocalData);
        using CompositionHarness harness = new(services);
        QuantificationDefinition input = harness.Input.DefinitionDraft;
        QuantificationDefinition design = harness.Shell.DesignViewModel.Draft;
        ImportedPromptViewModel prompt = Assert.Single(harness.Shell.DesignViewModel.ImportedPrompts);

        Task initialization = services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);
        Assert.Same(initialization, harness.Shell.Settings.LastLoadTask);
        await initialization.WaitAsync(TestWait, TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.Loaded, harness.Shell.Settings.LoadStatus);
        Assert.Equal(saved.PreferredModelId, harness.Execution.PreferredModelId);
        Assert.Equal(saved.MaxConcurrency, harness.Execution.MaxConcurrency);
        Assert.Equal(saved.OutputDirectoryOverride, harness.Execution.OutputDirectoryOverride);
        AssertDefinition(saved.Definition!, harness.Shell.Settings.StoredDefinition);
        Assert.Same(input, harness.Input.DefinitionDraft);
        Assert.Same(design, harness.Shell.DesignViewModel.Draft);
        Assert.Same(prompt, Assert.Single(harness.Shell.DesignViewModel.ImportedPrompts));
        Assert.Equal("unapplied.txt", prompt.DisplayName);
        Assert.Equal(string.Empty, harness.Input.FilePath);
        Assert.False(harness.Input.HasLoadedWorkbook);
        Assert.False(harness.Shell.Settings.CanApplySavedDefinition);
        Assert.Null(harness.Shell.Settings.LastApplySavedDefinitionTask);
        Assert.Null(harness.Shell.Settings.LastSaveTask);
        Assert.False(harness.Shell.Settings.HasUnsavedChanges);
        Assert.Equal(0, harness.Loader.CallCount);
        Assert.Equal(original, File.ReadAllBytes(directory.SettingsPath));
        Assert.False(Directory.Exists(directory.OutputPath));
        AssertNoRuntimeActivity(harness);
    }

    [Fact]
    public async Task Initialization_preserves_existing_input_metadata_and_divergent_editor_drafts()
    {
        using SettingsDirectory directory = new();
        await directory.SeedAsync(SavedSettings(directory));
        using X02TemporaryWorkbook workbook = CreateWorkbook();
        ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty, directory.LocalData);
        using CompositionHarness harness = new(services);
        harness.Loader.Result = await new InputWorkbookLoader().LoadAsync(workbook.Path, 1, TestContext.Current.CancellationToken);
        await harness.Input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        Assert.True(harness.Input.HasLoadedWorkbook);
        harness.Shell.DesignViewModel.DefinitionName = "既存の未保存設計を保持";
        QuantificationDefinition input = harness.Input.DefinitionDraft;
        QuantificationDefinition design = harness.Shell.DesignViewModel.Draft;
        var metadata = harness.Input.Metadata;
        var snapshot = harness.Input.Snapshot;

        await services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);

        Assert.Same(input, harness.Input.DefinitionDraft);
        Assert.Same(design, harness.Shell.DesignViewModel.Draft);
        Assert.Same(metadata, harness.Input.Metadata);
        Assert.Same(snapshot, harness.Input.Snapshot);
        Assert.Equal(workbook.Path, harness.Input.FilePath);
        Assert.Equal(1, harness.Loader.CallCount);
        AssertDefinition(SavedSettings(directory).Definition!, harness.Shell.Settings.StoredDefinition);
        Assert.True(harness.Shell.Settings.HasUnsavedChanges);
        Assert.True(harness.Shell.Settings.CanApplySavedDefinition);
        Assert.Null(harness.Shell.Settings.LastApplySavedDefinitionTask);
        AssertNoRuntimeActivity(harness);
    }

    [Fact]
    public async Task Settings_initialization_does_not_consume_the_existing_one_time_launch_input_request()
    {
        using SettingsDirectory directory = new();
        await directory.SeedAsync(SavedSettings(directory));
        using X02TemporaryWorkbook workbook = CreateWorkbook();
        LaunchStartupState startup = LaunchStartupState.Create(["--input", workbook.Path], directory.Root);
        ServiceRegistration services = ServiceRegistration.FromStartup(startup, directory.LocalData);
        using CompositionHarness harness = new(services);
        harness.Loader.Result = await new InputWorkbookLoader().LoadAsync(workbook.Path, 1, TestContext.Current.CancellationToken);

        Assert.Equal(workbook.Path, harness.Input.FilePath);
        Assert.False(harness.Input.HasLoadedWorkbook);
        await services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);
        Assert.Equal(0, harness.Loader.CallCount);
        Assert.False(harness.Input.HasLoadedWorkbook);

        // The Input view's existing Loaded handler, not composition/settings, owns this call.
        await harness.Input.LoadLaunchInputIfRequestedAsync(TestContext.Current.CancellationToken);
        await harness.Input.LoadLaunchInputIfRequestedAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Input.HasLoadedWorkbook);
        Assert.Equal(1, harness.Loader.CallCount);
        Assert.NotEqual(harness.Shell.Settings.StoredDefinition?.Id, harness.Input.DefinitionDraft.Id);
        Assert.Null(harness.Shell.Settings.LastApplySavedDefinitionTask);
        AssertNoRuntimeActivity(harness);
    }

    [Theory]
    [InlineData("{PRIVATE-T23-SETTINGS", SettingsLoadStatus.JsonInvalid)]
    [InlineData("{\"schemaVersion\":99}", SettingsLoadStatus.UnsupportedVersion)]
    [InlineData("{\"schemaVersion\":1,\"maxConcurrency\":17}", SettingsLoadStatus.JsonInvalid)]
    public async Task Invalid_settings_keep_the_original_file_and_offline_editors_with_safe_status(string json, SettingsLoadStatus expected)
    {
        using SettingsDirectory directory = new();
        byte[] original = directory.WriteJson(json);
        ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty, directory.LocalData);
        using CompositionHarness harness = new(services);
        QuantificationDefinition input = harness.Input.DefinitionDraft;
        QuantificationDefinition design = harness.Shell.DesignViewModel.Draft;

        await services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);
        harness.Shell.OpenSettings();
        harness.Shell.CloseSettings();

        Assert.Equal(expected, harness.Shell.Settings.LoadStatus);
        Assert.True(harness.Shell.Settings.CanSave);
        Assert.True(harness.Shell.Settings.HasUnsavedChanges);
        Assert.Contains("元ファイルは変更していません", harness.Shell.Settings.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateCanary, harness.Shell.Settings.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(directory.Root, harness.Shell.Settings.StatusText, StringComparison.Ordinal);
        Assert.Same(input, harness.Input.DefinitionDraft);
        Assert.Same(design, harness.Shell.DesignViewModel.Draft);
        Assert.Null(harness.Shell.Settings.StoredDefinition);
        Assert.Null(harness.Shell.Settings.LastSaveTask);
        Assert.Null(harness.Execution.PreferredModelId);
        Assert.Equal(8, harness.Execution.MaxConcurrency);
        Assert.Equal(original, File.ReadAllBytes(directory.SettingsPath));
        Assert.Equal(0, harness.Loader.CallCount);
        AssertNoRuntimeActivity(harness);
    }

    [Fact]
    public async Task Unreadable_settings_location_does_not_crash_repair_or_fall_back()
    {
        using SettingsDirectory directory = new();
        Directory.CreateDirectory(directory.LocalData);
        string blockedDirectory = Path.GetDirectoryName(directory.SettingsPath)!;
        File.WriteAllText(blockedDirectory, PrivateCanary);
        ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty, directory.LocalData);
        using CompositionHarness harness = new(services);

        await services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);

        Assert.Equal(directory.SettingsPath, harness.Shell.Settings.FilePath);
        Assert.Equal(SettingsLoadStatus.ReadFailed, harness.Shell.Settings.LoadStatus);
        Assert.Contains("アクセス権", harness.Shell.Settings.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(directory.Root, harness.Shell.Settings.StatusText, StringComparison.Ordinal);
        Assert.Equal(PrivateCanary, File.ReadAllText(blockedDirectory));
        Assert.Equal(blockedDirectory, Assert.Single(Directory.GetFileSystemEntries(directory.LocalData)));
        Assert.Null(harness.Shell.Settings.LastSaveTask);
        AssertNoRuntimeActivity(harness);
    }

    [Fact]
    public async Task Explicit_common_only_save_after_initialization_keeps_the_stored_definition_without_input()
    {
        using SettingsDirectory directory = new();
        ApplicationSettings saved = SavedSettings(directory);
        await directory.SeedAsync(saved);
        ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty, directory.LocalData);
        using CompositionHarness harness = new(services);
        await services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);
        harness.Shell.DesignViewModel.DefinitionName = "未読込のplaceholderを保存しない";
        harness.Execution.MaxConcurrency = 3;

        await harness.Shell.Settings.SaveAsync(TestContext.Current.CancellationToken);

        ApplicationSettings actual = Assert.IsType<ApplicationSettings>(
            (await new SettingsFileStore(directory.SettingsPath).LoadAsync(TestContext.Current.CancellationToken)).Settings);
        Assert.Equal(SettingsSaveStatus.Saved, harness.Shell.Settings.SaveStatus);
        Assert.Equal(3, actual.MaxConcurrency);
        AssertDefinition(saved.Definition!, actual.Definition);
        AssertDefinition(saved.Definition!, harness.Shell.Settings.StoredDefinition);
        Assert.Equal(string.Empty, harness.Input.FilePath);
        Assert.False(harness.Input.HasLoadedWorkbook);
        Assert.False(harness.Shell.Settings.HasUnsavedChanges);
        Assert.False(Directory.Exists(directory.OutputPath));
        Assert.Equal(0, harness.Loader.CallCount);
        AssertNoRuntimeActivity(harness);
    }

    [Theory]
    [InlineData("loaded")]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public async Task Repeated_initialization_and_navigation_read_once_and_do_not_overwrite_later_edits(string initialState)
    {
        using SettingsDirectory directory = new();
        if (initialState == "loaded") await directory.SeedAsync(SavedSettings(directory));
        if (initialState == "corrupt") directory.WriteJson("{" + PrivateCanary);
        ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty, directory.LocalData);
        using CompositionHarness harness = new(services);
        SettingsViewModel settings = harness.Shell.Settings;
        int reads = 0;
        bool saveWasBlocked = false;
        settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.IsLoading) && settings.IsLoading)
            {
                reads++;
                saveWasBlocked = !settings.CanSave && !settings.SaveCommand.CanExecute(null);
                settings.SaveCommand.Execute(null);
            }
        };

        Task initialization = services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);
        await Task.WhenAll(initialization, services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken))
            .WaitAsync(TestWait, TestContext.Current.CancellationToken);
        Assert.Same(initialization, settings.LastLoadTask);
        Assert.True(saveWasBlocked);
        Assert.Null(settings.LastSaveTask);
        SettingsLoadStatus? status = settings.LoadStatus;
        QuantificationDefinition? stored = settings.StoredDefinition;
        await directory.SeedAsync(SavedSettings(directory) with { MaxConcurrency = 2 });
        byte[] replacement = File.ReadAllBytes(directory.SettingsPath);
        harness.Execution.MaxConcurrency = 3;

        await services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);
        harness.Shell.OpenSettings();
        harness.Shell.Settings.RequestCloseCommand.Execute(null);
        harness.Shell.NextCommand.Execute(null);
        harness.Shell.PreviousCommand.Execute(null);
        await services.InitializeAsync(harness.Shell, TestContext.Current.CancellationToken);

        Assert.Equal(1, reads);
        Assert.Same(initialization, settings.LastLoadTask);
        Assert.Equal(status, settings.LoadStatus);
        Assert.Same(stored, settings.StoredDefinition);
        Assert.Equal(3, harness.Execution.MaxConcurrency);
        Assert.Equal(replacement, File.ReadAllBytes(directory.SettingsPath));
        Assert.Null(settings.LastSaveTask);
        AssertNoRuntimeActivity(harness);
    }

    [Fact]
    public async Task Transient_windows_have_independent_settings_state_and_initialization_even_with_one_registration()
    {
        using SettingsDirectory directory = new();
        await directory.SeedAsync(SavedSettings(directory));
        ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty, directory.LocalData);
        using CompositionHarness first = new(services);
        using CompositionHarness second = new(services);

        await services.InitializeAsync(first.Shell, TestContext.Current.CancellationToken);
        await directory.SeedAsync(SavedSettings(directory) with { MaxConcurrency = 3 });
        await services.InitializeAsync(second.Shell, TestContext.Current.CancellationToken);

        Assert.Same(first.Shell.Navigator, second.Shell.Navigator);
        Assert.NotSame(first.Shell.Settings, second.Shell.Settings);
        Assert.NotSame(first.Input, second.Input);
        Assert.NotSame(first.Execution, second.Execution);
        Assert.NotSame(first.Shell.Settings.LastLoadTask, second.Shell.Settings.LastLoadTask);
        Assert.Equal(2, first.Execution.MaxConcurrency);
        Assert.Equal(3, second.Execution.MaxConcurrency);
        first.Execution.MaxConcurrency = 1;
        Assert.True(first.Shell.Settings.HasUnsavedChanges);
        Assert.False(second.Shell.Settings.HasUnsavedChanges);
        first.Shell.Dispose();
        await services.InitializeAsync(second.Shell, TestContext.Current.CancellationToken);
        Assert.Equal(3, second.Execution.MaxConcurrency);
        Assert.True(second.Shell.Settings.CanSave);
        AssertNoRuntimeActivity(first);
        AssertNoRuntimeActivity(second);
    }

    [Fact]
    public async Task Parent_disposal_at_read_start_cancels_initialization_and_detaches_owned_subscriptions()
    {
        using SettingsDirectory directory = new();
        await directory.SeedAsync(SavedSettings(directory));
        byte[] original = File.ReadAllBytes(directory.SettingsPath);
        ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty, directory.LocalData);
        using CompositionHarness harness = new(services);
        MainWindowViewModel shell = harness.Shell;
        SettingsViewModel settings = shell.Settings;
        bool disposedDuringRead = false;
        void DisposeDuringRead(object? sender, PropertyChangedEventArgs args)
        {
            if (!disposedDuringRead && args.PropertyName == nameof(SettingsViewModel.IsLoading) && settings.IsLoading)
            {
                // Deterministic cancellation at the real read's initial notification;
                // no sleeps, fake filesystem/provider, or IO-speed assumptions.
                disposedDuringRead = true;
                shell.Dispose();
            }
        }

        settings.PropertyChanged += DisposeDuringRead;
        Task initialization;
        try
        {
            initialization = services.InitializeAsync(shell, TestContext.Current.CancellationToken);
            await initialization.WaitAsync(TestWait, TestContext.Current.CancellationToken);
        }
        finally
        {
            settings.PropertyChanged -= DisposeDuringRead;
        }

        Assert.True(disposedDuringRead);
        Assert.True(initialization.IsCompletedSuccessfully);
        Assert.False(settings.IsLoading);
        Assert.Null(settings.LoadStatus);
        Assert.Null(settings.StoredDefinition);
        Assert.False(settings.CanSave);
        Assert.False(harness.Execution.CanLogin);
        Assert.False(harness.Execution.CanCheckAuthentication);
        Assert.Equal(original, File.ReadAllBytes(directory.SettingsPath));
        AssertDetached(shell, shell.Navigator, typeof(WorkflowNavigator), nameof(WorkflowNavigator.CurrentStepChanged));
        AssertDetached(shell, harness.Input, typeof(UiObservableObject), nameof(UiObservableObject.PropertyChanged));
        AssertDetached(shell, shell.DesignViewModel, typeof(UiObservableObject), nameof(UiObservableObject.PropertyChanged));
        AssertDetached(shell, harness.Execution, typeof(UiObservableObject), nameof(UiObservableObject.PropertyChanged));
        AssertDetached(shell, harness.Execution, typeof(ExecutionViewModel), nameof(ExecutionViewModel.RunCompleted));
        AssertDetached(shell, settings, typeof(UiObservableObject), nameof(UiObservableObject.PropertyChanged));
        AssertDetached(shell, settings, typeof(SettingsViewModel), nameof(SettingsViewModel.CloseRequested));
        int notifications = 0;
        settings.PropertyChanged += (_, _) => notifications++;
        shell.PropertyChanged += (_, _) => notifications++;
        harness.Input.AddQuestion();
        shell.DesignViewModel.DefinitionName = "disposed-parent";
        harness.Execution.MaxConcurrency = 3;
        shell.Navigator.MoveNext();
        await services.InitializeAsync(shell, TestContext.Current.CancellationToken);
        Assert.Equal(0, notifications);
        Assert.Same(initialization, settings.LastLoadTask);
        using FileStream exclusive = new(directory.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Equal((long)original.Length, exclusive.Length);
        AssertNoRuntimeActivity(harness);
    }

    [AvaloniaFact]
    public void Default_test_App_and_window_factory_remain_unconfigured_without_user_settings_IO()
    {
        App app = Assert.IsType<App>(Application.Current);
        MainWindow window = app.CreateMainWindow();
        try
        {
            AssertUnconfigured(window.ViewModel.Settings);
            AssertPassive(window.ViewModel);
        }
        finally
        {
            window.Close();
            window.ViewModel.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task Production_window_with_empty_startup_reads_preferences_without_opening_settings_or_input()
    {
        using SettingsDirectory directory = new();
        ApplicationSettings saved = SavedSettings(directory);
        await directory.SeedAsync(saved);
        ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty, directory.LocalData);
        App app = new(services);
        MainWindow window = app.CreateMainWindow();
        Task? initialization = null;
        try
        {
            initialization = Assert.IsAssignableFrom<Task>(window.ViewModel.Settings.LastLoadTask);
            await initialization.WaitAsync(TestWait, TestContext.Current.CancellationToken);

            Assert.Equal(directory.SettingsPath, window.ViewModel.Settings.FilePath);
            Assert.Equal(SettingsLoadStatus.Loaded, window.ViewModel.Settings.LoadStatus);
            Assert.Equal(saved.PreferredModelId, window.ViewModel.ExecutionViewModel.PreferredModelId);
            Assert.Equal(saved.MaxConcurrency, window.ViewModel.ExecutionViewModel.MaxConcurrency);
            Assert.Equal(saved.OutputDirectoryOverride, window.ViewModel.ExecutionViewModel.OutputDirectoryOverride);
            AssertDefinition(saved.Definition!, window.ViewModel.Settings.StoredDefinition);
            Assert.Equal(string.Empty, window.ViewModel.InputViewModel.FilePath);
            Assert.False(window.ViewModel.InputViewModel.HasLoadedWorkbook);
            Assert.False(window.ViewModel.IsSettingsOpen);
            Assert.Equal(WorkflowStep.Input, window.ViewModel.CurrentStep);
            Assert.Null(window.ViewModel.Settings.LastApplySavedDefinitionTask);
            Assert.Null(window.ViewModel.Settings.LastSaveTask);
            AssertPassive(window.ViewModel);
        }
        finally
        {
            window.Close();
            window.ViewModel.Dispose();
            if (initialization is not null) await initialization.WaitAsync(TestWait);
        }
    }

    [AvaloniaFact]
    public async Task Production_App_window_starts_settings_without_navigation_and_preserves_Input_Loaded_ownership()
    {
        using SettingsDirectory directory = new();
        ApplicationSettings saved = SavedSettings(directory);
        await directory.SeedAsync(saved);
        using X02TemporaryWorkbook workbook = CreateWorkbook();
        string promptPath = directory.WritePrompt("startup.txt", "UNAPPLIED-T23 {回答} {評価項目}");
        LaunchStartupState startup = LaunchStartupState.Create(["--input", workbook.Path, "--prompt", promptPath], directory.Root);
        ServiceRegistration services = ServiceRegistration.FromStartup(startup, directory.LocalData);
        App app = new(services, startup.Error);
        MainWindow window = app.CreateMainWindow();
        MainWindowViewModel shell = window.ViewModel;
        Task? loading = null;
        TaskCompletionSource inputLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void InputChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (shell.InputViewModel.HasLoadedWorkbook && !shell.InputViewModel.IsBusy)
            {
                inputLoaded.TrySetResult();
            }
        }

        shell.InputViewModel.PropertyChanged += InputChanged;
        try
        {
            loading = Assert.IsAssignableFrom<Task>(shell.Settings.LastLoadTask);
            Assert.Equal(directory.SettingsPath, shell.Settings.FilePath);
            Assert.Same(services, app.Services);
            Assert.Same(shell, window.DataContext);
            Assert.Equal(workbook.Path, shell.InputViewModel.FilePath);
            Assert.False(shell.InputViewModel.HasLoadedWorkbook);
            Assert.False(shell.IsSettingsOpen);
            Assert.Equal(WorkflowStep.Input, shell.CurrentStep);

            window.Show();
            await Task.WhenAll(loading, inputLoaded.Task).WaitAsync(TestWait, TestContext.Current.CancellationToken);

            Assert.Equal(saved.PreferredModelId, shell.ExecutionViewModel.PreferredModelId);
            Assert.Equal(SettingsLoadStatus.Loaded, shell.Settings.LoadStatus);
            Assert.True(shell.InputViewModel.HasLoadedWorkbook);
            Assert.Equal(workbook.Path, shell.InputViewModel.FilePath);
            Assert.NotEqual(saved.Definition!.Id, shell.InputViewModel.DefinitionDraft.Id);
            Assert.Equal("startup.txt", Assert.Single(shell.DesignViewModel.ImportedPrompts).DisplayName);
            AssertDefinition(saved.Definition!, shell.Settings.StoredDefinition);
            Assert.Null(shell.Settings.LastApplySavedDefinitionTask);
            Assert.Null(shell.Settings.LastSaveTask);
            await services.InitializeAsync(shell, TestContext.Current.CancellationToken);
            Assert.Same(loading, shell.Settings.LastLoadTask);
            Assert.False(shell.IsSettingsOpen);
            Assert.Equal(WorkflowStep.Input, shell.CurrentStep);
            AssertPassive(shell);
        }
        finally
        {
            shell.InputViewModel.PropertyChanged -= InputChanged;
            window.Close();
            shell.Dispose();
            if (loading is not null) await loading.WaitAsync(TestWait);
        }
    }

    private static ApplicationSettings SavedSettings(SettingsDirectory directory) => new()
    {
        PreferredModelId = "unverified-t23-model",
        MaxConcurrency = 2,
        OutputDirectoryOverride = directory.OutputPath,
        Definition = U04TestSupport.Definition(2, 3) with { Id = "stored-T23", Name = PrivateCanary },
    };

    private static X02TemporaryWorkbook CreateWorkbook() => X02SyntheticWorkbookFactory.CreateSingleSheet(
        "Original", 1, 3, 2, new X02Header(1, "Answer"), new X02Header(2, "Rationale"));

    private static void AssertDefinition(QuantificationDefinition expected, QuantificationDefinition? actual) =>
        Assert.Equal(Canonical.SerializeToUtf8Bytes(expected), Canonical.SerializeToUtf8Bytes(Assert.IsType<QuantificationDefinition>(actual)));

    private static void AssertUnconfigured(SettingsViewModel settings)
    {
        Assert.Equal(string.Empty, settings.FilePath);
        Assert.Null(settings.LastLoadTask);
        Assert.Null(settings.LastSaveTask);
        Assert.Null(settings.LoadStatus);
        Assert.Null(settings.StoredDefinition);
        Assert.True(settings.IsInitialized);
        Assert.False(settings.CanSave);
    }

    private static void AssertPassive(MainWindowViewModel shell)
    {
        Assert.Equal(ExecutionAuthenticationState.NotChecked, shell.ExecutionViewModel.AuthenticationState);
        Assert.Null(shell.ExecutionViewModel.SelectedModelId);
        Assert.Null(shell.ExecutionViewModel.LastLoginTask);
        Assert.Null(shell.ExecutionViewModel.LastRunContext);
        Assert.False(shell.ExecutionViewModel.IsCheckingAuthentication);
        Assert.False(shell.ExecutionViewModel.IsLoggingIn);
        Assert.False(shell.ExecutionViewModel.IsRunning);
        Assert.False(shell.ExecutionViewModel.IsConfigured);
        Assert.False(shell.ResultsOutputViewModel.IsLoaded);
    }

    private static void AssertNoRuntimeActivity(CompositionHarness harness)
    {
        AssertPassive(harness.Shell);
        Assert.Equal(0, harness.Runtime.AuthenticationCalls);
        Assert.Equal(0, harness.Runtime.RunCalls);
        Assert.Equal(0, harness.Runtime.ResolveCalls);
        Assert.Equal(0, harness.Runtime.LoginProcessCalls);
    }

    private static void AssertDetached(MainWindowViewModel shell, object publisher, Type declaringType, string eventName)
    {
        FieldInfo field = declaringType.GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Expected event backing field for subscription inspection.");
        Delegate[] handlers = (field.GetValue(publisher) as Delegate)?.GetInvocationList() ?? [];
        Assert.DoesNotContain(handlers, handler => ReferenceEquals(handler.Target, shell) || ReferenceEquals(handler.Target, shell.Settings));
    }

    private sealed class SettingsDirectory : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-T23-" + Guid.NewGuid().ToString("N"));
        public string LocalData => Path.Combine(Root, "LocalData");
        public string SettingsPath => Path.Combine(LocalData, "StudyReportEvaluator", "setting.txt");
        public string OutputPath => Path.Combine(Root, "output-not-created");

        public async Task SeedAsync(ApplicationSettings settings) =>
            Assert.Equal(SettingsSaveStatus.Saved, (await new SettingsFileStore(SettingsPath)
                .SaveAsync(settings, TestContext.Current.CancellationToken)).Status);

        public byte[] WriteJson(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            File.WriteAllBytes(SettingsPath, bytes);
            return bytes;
        }

        public string WritePrompt(string name, string content)
        {
            Directory.CreateDirectory(Root);
            string path = Path.Combine(Root, name);
            File.WriteAllText(path, content, new UTF8Encoding(false, true));
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class CompositionHarness : IDisposable
    {
        public CompositionHarness(ServiceRegistration services)
        {
            Input = new InputViewModel(Loader);
            Execution = Runtime.CreateExecution();
            Shell = services.CreateMainWindowViewModel(Input, Execution);
        }

        public RecordingInputLoader Loader { get; } = new();
        public RuntimeGuard Runtime { get; } = new();
        public InputViewModel Input { get; }
        public ExecutionViewModel Execution { get; }
        public MainWindowViewModel Shell { get; }
        public void Dispose() => Shell.Dispose();
    }

    private sealed class RecordingInputLoader : IInputWorkbookLoader
    {
        public int CallCount { get; private set; }
        public InputWorkbookLoadResult? Result { get; set; }

        public Task<InputWorkbookLoadResult> LoadAsync(string filePath, uint headerRow, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(Result ?? throw new InputWorkbookLoadException("UNEXPECTED_T23_INPUT_READ"));
        }
    }

    private sealed class RuntimeGuard : IExecutionAuthenticationBoundary, IQuantificationRunBoundary, ICopilotCliPathResolver
    {
        public int AuthenticationCalls { get; private set; }
        public int RunCalls { get; private set; }
        public int ResolveCalls { get; private set; }
        public int LoginProcessCalls { get; private set; }

        public ExecutionViewModel CreateExecution() => new(this, this, new BundledCopilotLoginService(this, _ =>
        {
            LoginProcessCalls++;
            throw new InvalidOperationException("No login process was authorized by this test.");
        }));

        public Task<ExecutionAuthenticationSnapshot> CheckAsync(CancellationToken cancellationToken)
        {
            AuthenticationCalls++;
            return Task.FromResult(new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.AuthRequired));
        }

        public Task<RunSummary> RunAsync(QuantificationRunRequest request, Action<EvaluationProgress>? progress, CancellationToken cancellationToken)
        {
            RunCalls++;
            throw new InvalidOperationException("No run was authorized by this test.");
        }

        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken)
        {
            ResolveCalls++;
            throw new InvalidOperationException("No CLI resolution was authorized by this test.");
        }
    }
}
