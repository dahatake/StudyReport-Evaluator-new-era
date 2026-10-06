using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using StudyReportEvaluator.App.Composition;
using StudyReportEvaluator.App.Display;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Views;

namespace StudyReportEvaluator.App;

public sealed partial class App : Application
{
    public const string AutomaticLoginEnvironmentVariable = "STUDY_REPORT_EVALUATOR_AUTO_COPILOT_LOGIN";

    public App()
        : this(new ServiceRegistration(), null)
    {
    }

    public App(ServiceRegistration services)
        : this(services, null)
    {
    }

    public App(
        ServiceRegistration services,
        LaunchOptionsException? startupError)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        StartupError = startupError;
    }

    public ServiceRegistration Services { get; }

    public LaunchOptionsException? StartupError { get; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public MainWindow CreateMainWindow() => Services.CreateMainWindow();

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (StartupError is null)
            {
                MainWindow window = CreateMainWindow();
                // NFR-UX-010: OS high contrast, text size and animation preferences (no in-app switch).
                OsDisplaySettings.Attach(window);
                AttachStartupAuthentication(
                    window,
                    IsAutomaticLoginEnabled(Environment.GetEnvironmentVariable(AutomaticLoginEnvironmentVariable)));
                desktop.MainWindow = window;
            }
            else
            {
                desktop.MainWindow = new StartupErrorWindow(StartupError);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Only "0" or "false" (case-insensitive) opts out of the automatic login start.</summary>
    public static bool IsAutomaticLoginEnabled(string? environmentValue) =>
        !("0".Equals(environmentValue?.Trim(), StringComparison.Ordinal)
            || "false".Equals(environmentValue?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Runs the one-time startup authentication after the window is shown.</summary>
    public static void AttachStartupAuthentication(MainWindow window, bool allowAutomaticLogin)
    {
        ArgumentNullException.ThrowIfNull(window);

        void HandleOpened(object? sender, EventArgs e)
        {
            window.Opened -= HandleOpened;
            _ = window.ViewModel.ExecutionViewModel.RunStartupAuthenticationAsync(allowAutomaticLogin);
        }

        window.Opened += HandleOpened;
    }
}
