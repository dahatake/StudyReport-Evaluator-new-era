using Avalonia;
using StudyReportEvaluator.App.Composition;
using StudyReportEvaluator.App.Launch;

namespace StudyReportEvaluator.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        LaunchStartupState startup = LaunchStartupState.Create(args);
        return BuildAvaloniaApp(startup).StartWithClassicDesktopLifetime([]);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        BuildAvaloniaApp(LaunchStartupState.Empty);

    public static AppBuilder BuildAvaloniaApp(LaunchStartupState startup)
    {
        ArgumentNullException.ThrowIfNull(startup);
        return AppBuilder.Configure(() => new App(
            ServiceRegistration.FromStartup(startup),
                startup.Error))
            .UsePlatformDetect()
            .LogToTrace();
    }
}
