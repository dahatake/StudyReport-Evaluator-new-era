using System.Security;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workspace;

namespace StudyReportEvaluator.App.Composition;

public sealed class ServiceRegistration
{
    private readonly SettingsFileStore? settingsStore;
    private readonly LayoutFileStore? layoutStore;

    public ServiceRegistration()
        : this(new WorkflowNavigator(), LaunchStartupState.Empty)
    {
    }

    public ServiceRegistration(WorkflowNavigator workflowNavigator)
        : this(workflowNavigator, LaunchStartupState.Empty)
    {
    }

    public ServiceRegistration(
        WorkflowNavigator workflowNavigator,
        LaunchStartupState startup,
        SettingsFileStore? settingsStore = null,
        LayoutFileStore? layoutStore = null)
    {
        WorkflowNavigator = workflowNavigator
            ?? throw new ArgumentNullException(nameof(workflowNavigator));
        Startup = startup ?? throw new ArgumentNullException(nameof(startup));
        this.settingsStore = settingsStore;
        this.layoutStore = layoutStore;
    }

    public WorkflowNavigator WorkflowNavigator { get; }

    public LaunchStartupState Startup { get; }

    /// <summary>Production-only path resolution. Neither construction nor resolution reads settings.</summary>
    public static ServiceRegistration FromStartup(LaunchStartupState startup)
    {
        ArgumentNullException.ThrowIfNull(startup);
        string? localApplicationDataDirectory;
        try
        {
            localApplicationDataDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);
        }
        catch (Exception exception) when (IsSettingsPathFailure(exception))
        {
            localApplicationDataDirectory = null;
        }

        return FromStartup(startup, localApplicationDataDirectory);
    }

    /// <summary>Use an explicit local-data directory without changing process environment or launch options.</summary>
    public static ServiceRegistration FromStartup(
        LaunchStartupState startup,
        string? localApplicationDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(startup);
        SettingsFileStore? store = null;
        LayoutFileStore? layouts = null;
        if (!string.IsNullOrWhiteSpace(localApplicationDataDirectory)
            && Path.IsPathFullyQualified(localApplicationDataDirectory))
        {
            try
            {
                store = new SettingsFileStore(Path.Combine(
                    localApplicationDataDirectory, "StudyReportEvaluator", "setting.txt"));
            }
            catch (Exception exception) when (IsSettingsPathFailure(exception))
            {
                // The existing null-store status disables persistence and explains why.
                // Never repair the path, create directories, or fall back to cwd/EXE.
            }

            try
            {
                // NFR-UX-004: a separate file next to setting.txt; setting.txt itself is unchanged.
                layouts = new LayoutFileStore(Path.Combine(
                    localApplicationDataDirectory, "StudyReportEvaluator", LayoutFileStore.FileName));
            }
            catch (Exception exception) when (IsSettingsPathFailure(exception))
            {
                // Layouts then stay in memory for this run only.
            }
        }

        return new ServiceRegistration(new WorkflowNavigator(), startup, store, layouts);
    }

    public MainWindowViewModel CreateMainWindowViewModel() =>
        CreateMainWindowViewModel(new InputViewModel(), new ExecutionViewModel());

    /// <summary>Compose existing input/runtime boundaries; no input read or runtime action occurs here.</summary>
    public MainWindowViewModel CreateMainWindowViewModel(
        InputViewModel input,
        ExecutionViewModel execution)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(execution);
        if (Startup.Options?.InputPath is string inputPath)
        {
            input.ApplyLaunchInput(inputPath);
        }

        return new MainWindowViewModel(
            WorkflowNavigator,
            input,
            new QuantificationDesignViewModel(
                initialDefinition: null,
                availableColumnNames: null,
                importedPrompts: Startup.Prompts),
            execution,
            new ResultsOutputViewModel(),
            settingsStore,
            new WorkspaceLayoutService(layoutStore));
    }

    /// <summary>Awaitable, idempotent settings startup on the caller's UI context, never an AI/input startup.</summary>
    public Task InitializeAsync(
        MainWindowViewModel viewModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        // layout.json is small and read once, before the first workspace is shown.
        viewModel.Workspace.Load();
        Task initialization = viewModel.Settings.InitializeAsync(cancellationToken);
        _ = ObserveInitializationAsync(initialization);
        return initialization;
    }

    public MainWindow CreateMainWindow()
    {
        MainWindow window = new(CreateMainWindowViewModel());
        // App delegates here. Do not wait on the UI thread or defer this to Settings
        // navigation. MainWindow already owns parent disposal and read cancellation.
        _ = InitializeAsync(window.ViewModel);
        return window;
    }

    private static async Task ObserveInitializationAsync(Task initialization)
    {
        try
        {
            await initialization;
        }
        catch
        {
            // Settings reports IO failures. Even a throwing notification subscriber
            // must not leave an unobserved startup fault or expose private IO details.
            // The original task remains available to explicit callers for observation.
        }
    }

    private static bool IsSettingsPathFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or NotSupportedException;
}