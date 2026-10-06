using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workspace;
using Xunit;
using static StudyReportEvaluator.App.Tests.UI.ResponsiveLayoutTests;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: NFR-UX-008 (AC-084), NFR-UX-001 (AC-076), NFR-A11Y-001 (AC-078)
public sealed class WorkspaceReflowTests
{
    private static readonly string[] ResultsPanels =
        [WorkspacePanelIds.ResultsList, WorkspacePanelIds.ResultsDetail, WorkspacePanelIds.ResultsChart];

    [AvaloniaFact]
    public async Task Panels_reflow_at_1024_760_and_200_percent_without_horizontal_scroll_and_return_to_the_saved_layout()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        MainWindow window = fixture.Window;
        fixture.NavigateTo(WorkflowStep.Results);
        PanelWorkspace workspace = fixture.Workspace;
        WorkspaceLayout saved = workspace.Layout;

        // 1180 x 800: three columns, left to right.
        Assert.Equal(WorkspaceReflowMode.Full, workspace.Mode);
        double[] lefts = [.. ResultsPanels.Select(id => Left(workspace, id))];
        Assert.True(lefts[0] < lefts[1] && lefts[1] < lefts[2], string.Join(",", lefts));
        AssertNormalBody(window);

        // 1024 x 720: the third column becomes a tab of its neighbour.
        Resize(window, 1024, 720, 1d);
        Assert.Equal(WorkspaceReflowMode.Compact, workspace.Mode);
        Assert.Equal(2, workspace.EffectiveLayout?.Groups.Length);
        Assert.True(workspace.IsPresented(WorkspacePanelIds.ResultsList));
        AssertNormalBody(window);
        AssertEveryPanelReachable(window, workspace);
        AssertFitsHorizontally(window, "compact results");

        // 760 x 600 and 200 %: one column; every panel stays reachable without sideways scrolling.
        foreach ((double width, double height, double scale) in new[] { (760d, 600d, 1d), (1180d, 800d, 2d), (1024d, 720d, 2d) })
        {
            Resize(window, width, height, scale);
            Assert.Equal(WorkspaceReflowMode.Single, workspace.Mode);
            Assert.True(workspace.IsStacked);
            Assert.Equal(WorkspaceOrientation.Vertical, workspace.EffectiveLayout?.Orientation);
            Assert.All(ResultsPanels, id => Assert.True(workspace.IsPresented(id), id));
            double[] tops = [.. ResultsPanels.Select(id => Top(workspace, id))];
            Assert.True(tops[0] < tops[1] && tops[1] < tops[2]);
            AssertFitsHorizontally(window, $"single results {width}x{height}@{scale}");
            AssertNoHorizontalScroll(window);
            AssertEveryPanelReachable(window, workspace);
        }

        foreach (WorkflowStep step in new[] { WorkflowStep.Design, WorkflowStep.Execution })
        {
            fixture.NavigateTo(step);
            AssertFitsHorizontally(window, $"single {step}");
            AssertNoHorizontalScroll(window);
            AssertEveryPanelReachable(window, fixture.Workspace);
        }

        // Back to the normal size: the saved layout shows again; reflow saved nothing.
        Resize(window, 1180, 800, 1d);
        fixture.NavigateTo(WorkflowStep.Results);
        Assert.Equal(WorkspaceReflowMode.Full, fixture.Workspace.Mode);
        Assert.Equal(saved, fixture.Workspace.Layout);
        Assert.False(fixture.Shell.Workspace.HasCustomLayout(WorkspaceScreens.Results));
        Assert.Equal(3, fixture.Workspace.EffectiveLayout?.Groups.Length);
        AssertNormalBody(window);
        fixture.AssertPassive();
    }

    [AvaloniaFact]
    public async Task Os_text_scaling_counts_as_display_scaling_for_the_reflow()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        fixture.NavigateTo(WorkflowStep.Results);
        Assert.Equal(WorkspaceReflowMode.Full, fixture.Workspace.Mode);

        StudyReportEvaluator.App.Display.OsDisplaySettings.Apply(fixture.Window, new(
            Avalonia.Platform.PlatformThemeVariant.Light, Avalonia.Platform.ColorContrastPreference.NoPreference, null, 1.25d, true));
        Render();
        Assert.IsType<LayoutTransformControl>(fixture.Window.Content);
        Assert.Equal(1.25d, PanelWorkspace.GetContentScale(fixture.Workspace));
        Assert.Equal(WorkspaceReflowMode.Compact, fixture.Workspace.Mode);
        AssertFitsHorizontally(fixture.Window, "text scale 125%");

        StudyReportEvaluator.App.Display.OsDisplaySettings.Apply(fixture.Window, new(
            Avalonia.Platform.PlatformThemeVariant.Light, Avalonia.Platform.ColorContrastPreference.NoPreference, null, 2d, true));
        Render();
        Assert.Equal(WorkspaceReflowMode.Single, fixture.Workspace.Mode);
        AssertFitsHorizontally(fixture.Window, "text scale 200%");

        StudyReportEvaluator.App.Display.OsDisplaySettings.Apply(fixture.Window, new(
            Avalonia.Platform.PlatformThemeVariant.Light, Avalonia.Platform.ColorContrastPreference.NoPreference, null, 1d, true));
        Render();
        Assert.Equal(WorkspaceReflowMode.Full, fixture.Workspace.Mode);
        fixture.AssertPassive();
    }

    private static void Resize(Window window, double width, double height, double scale)
    {
        window.MinWidth = 0;
        window.MinHeight = 0;
        window.Width = width;
        window.Height = height;
        window.SetRenderScaling(scale);
        Render();
        Assert.Equal(new Size(width, height), window.ClientSize);
    }

    private static double Left(PanelWorkspace workspace, string id) =>
        Assert.IsType<WorkspacePanel>(workspace.FindPanel(id)).TranslatePoint(default, workspace)!.Value.X;

    private static double Top(PanelWorkspace workspace, string id) =>
        Assert.IsType<WorkspacePanel>(workspace.FindPanel(id)).TranslatePoint(default, workspace)!.Value.Y;

    private static void AssertNoHorizontalScroll(Window window)
    {
        foreach (ScrollViewer scroll in window.GetVisualDescendants().OfType<ScrollViewer>()
            .Where(scroll => scroll.IsEffectivelyVisible && scroll.TemplatedParent is not TextBox))
        {
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1d,
                $"{scroll.Name} extent {scroll.Extent.Width} exceeds {scroll.Viewport.Width}.");
        }
    }

    /// <summary>Each panel is shown, or one keyboard-selectable tab away; its menu is a 44 DIP target.</summary>
    private static void AssertEveryPanelReachable(Window window, PanelWorkspace workspace)
    {
        foreach (WorkspacePanel panel in workspace.Panels.Where(panel => workspace.Layout.IsVisible(panel.PanelId)))
        {
            if (!workspace.IsPresented(panel.PanelId))
            {
                TabStripItem tab = Assert.Single(window.GetVisualDescendants().OfType<TabStripItem>(),
                    item => Avalonia.Automation.AutomationProperties.GetAutomationId(item) == panel.PanelId + ".Tab");
                Assert.True(tab.IsEffectivelyVisible && tab.Bounds.Height >= 44d && tab.Bounds.Width >= 44d);
                TabStrip strip = Assert.IsType<TabStrip>(tab.GetVisualAncestors().OfType<TabStrip>().First());
                strip.SelectedItem = tab;
                Render();
            }

            Assert.True(workspace.IsPresented(panel.PanelId), panel.PanelId);
            Assert.True(panel.IsEffectivelyVisible && panel.Bounds.Width > 0d && panel.Bounds.Height > 0d, panel.PanelId);
            Button menu = Assert.IsType<Button>(workspace.GetMenuButton(panel.PanelId));
            Assert.True(menu.IsEffectivelyVisible && menu.Bounds.Width >= 44d && menu.Bounds.Height >= 44d, panel.PanelId);
        }
    }
}
