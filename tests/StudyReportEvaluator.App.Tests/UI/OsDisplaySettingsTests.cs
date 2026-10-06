using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Display;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Views;
using Xunit;
using static StudyReportEvaluator.App.Tests.UI.ResponsiveLayoutTests;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: NFR-UX-010 (AC-086), NFR-A11Y-001 (AC-078)
public sealed class OsDisplaySettingsTests
{
    private static readonly SystemPalette HighContrastBlack = new(
        Window: Colors.Black, WindowText: Colors.Yellow, Highlight: Color.Parse("#1AEBFF"), HighlightText: Colors.Black,
        ButtonFace: Colors.Black, ButtonText: Colors.Yellow, GrayText: Color.Parse("#3FF23F"), HotLight: Color.Parse("#8080FF"));

    [AvaloniaFact]
    public async Task Windows_high_contrast_paints_text_borders_and_focus_with_system_colours_on_every_step()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        MainWindow window = fixture.Window;
        OsDisplaySettings.Apply(window, HighContrastState(HighContrastBlack));
        Render();

        foreach (WorkflowStep step in Enum.GetValues<WorkflowStep>())
        {
            fixture.NavigateTo(step);
            Assert.Equal(Colors.Yellow, Solid(Required<TextBlock>(window, "EthicsWarningMessage").Foreground));
            Assert.Equal(Colors.Black, Solid(Required<Border>(window, "EthicsWarningBanner").Background));
            Button previous = Required<Button>(window, "PreviousStepButton");
            Assert.Equal(Colors.Yellow, Solid(previous.Foreground));
            Assert.Equal(Colors.Yellow, Solid(previous.BorderBrush));
            TextBox[] boxes = [.. CurrentView(window).GetVisualDescendants().OfType<TextBox>().Where(box => box.IsEffectivelyVisible)];
            Assert.NotEmpty(boxes);
            Assert.All(boxes, box =>
            {
                Assert.Equal(Colors.Yellow, Solid(box.Foreground));
                Assert.Equal(Colors.Black, Solid(box.Background));
            });

            // Keyboard focus uses the OS highlight colour; state is also written as text, not colour alone.
            Button current = Required<Button>(window, step switch
            {
                WorkflowStep.Input => "InputStepButton",
                WorkflowStep.Design => "DesignStepButton",
                WorkflowStep.Execution => "ExecutionStepButton",
                _ => "ResultsStepButton",
            });
            Assert.True(current.Focus(NavigationMethod.Tab));
            Render();
            Assert.Equal(HighContrastBlack.Highlight, Solid(current.BorderBrush));
            Assert.Equal(new Thickness(3d), current.BorderThickness);
            Assert.Contains("現在・選択中", AutomationPropertiesName(current), StringComparison.Ordinal);
            Assert.Contains("現在・選択中", fixture.Shell.CurrentStepStatusText, StringComparison.Ordinal);
        }

        Assert.Contains(fixture.Shell.Steps, presentation => presentation.StatusText == "設定済み");
        OsDisplaySettings.Apply(window, new OsDisplayState(PlatformThemeVariant.Light, ColorContrastPreference.NoPreference, null, 1d, true));
        Render();
        Assert.NotEqual(Colors.Yellow, Solid(Required<TextBlock>(window, "EthicsWarningMessage").Foreground));
        Assert.DoesNotContain(OsDisplaySettings.HighContrastVariantKey, window.ActualThemeVariant.Key.ToString(), StringComparison.Ordinal);
        Assert.False(window.Resources.ContainsKey("AppTextBrush"));
        fixture.AssertPassive();
    }

    [AvaloniaFact]
    public void The_application_follows_the_os_theme_variant_and_has_no_theme_switch()
    {
        Assert.Equal(ThemeVariant.Default, Application.Current?.RequestedThemeVariant);
        MainWindow window = new();
        try
        {
            window.Show();
            Render();
            Assert.True(window.TryFindResource("AppTextBrush", ThemeVariant.Light, out object? light));
            Assert.True(window.TryFindResource("AppTextBrush", ThemeVariant.Dark, out object? dark));
            Assert.NotEqual(Solid(light as IBrush), Solid(dark as IBrush));
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Render();
            Assert.Equal(Solid(dark as IBrush), Solid(window.Foreground));
            window.RequestedThemeVariant = ThemeVariant.Light;
            Render();
            Assert.Equal(Solid(light as IBrush), Solid(window.Foreground));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), control =>
                (AutomationPropertiesName(control) ?? string.Empty).Contains("テーマ", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Reduced_motion_removes_transitions_and_turning_it_back_on_restores_them()
    {
        MainWindow window = new();
        try
        {
            window.Show();
            Render();
            Button next = Required<Button>(window, "NextStepButton");
            Avalonia.Animation.Transitions? before = next.Transitions;
            Assert.NotNull(before);
            OsDisplaySettings.Apply(window, new OsDisplayState(PlatformThemeVariant.Light, ColorContrastPreference.NoPreference, null, 1d, false));
            Render();
            Assert.Null(next.Transitions);
            OsDisplaySettings.Apply(window, new OsDisplayState(PlatformThemeVariant.Light, ColorContrastPreference.NoPreference, null, 1d, true));
            Render();
            Assert.NotNull(next.Transitions);
        }
        finally
        {
            window.Close();
        }
    }

    private static OsDisplayState HighContrastState(SystemPalette palette) =>
        new(PlatformThemeVariant.Dark, ColorContrastPreference.High, palette, 1d, true);

    private static Color? Solid(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    private static string? AutomationPropertiesName(Control control) => Avalonia.Automation.AutomationProperties.GetName(control);
}
