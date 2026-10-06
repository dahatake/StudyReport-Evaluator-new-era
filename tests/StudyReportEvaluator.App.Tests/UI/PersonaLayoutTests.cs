using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Views.Charts;
using StudyReportEvaluator.App.Workspace;
using Xunit;
using static StudyReportEvaluator.App.Tests.UI.ResponsiveLayoutTests;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: NFR-UX-009 (AC-085), FR-065 (AC-087), NFR-UX-004 (AC-080), NFR-SEC-006
public sealed class PersonaLayoutTests
{
    [AvaloniaFact]
    public async Task First_run_P01_results_show_list_left_detail_centre_and_chart_tabs_right()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        Assert.Equal([Persona.Grader], fixture.Shell.Workspace.Personas);
        fixture.NavigateTo(WorkflowStep.Results);
        PanelWorkspace workspace = fixture.Workspace;
        Control view = fixture.CurrentView;

        Rect list = BoundsInWorkspace(workspace, WorkspacePanelIds.ResultsList);
        Rect detail = BoundsInWorkspace(workspace, WorkspacePanelIds.ResultsDetail);
        Rect chart = BoundsInWorkspace(workspace, WorkspacePanelIds.ResultsChart);
        Assert.True(list.Right <= detail.Left && detail.Right <= chart.Left, $"{list} {detail} {chart}");
        Assert.True(Math.Abs(list.Bottom - detail.Bottom) < 1d && Math.Abs(detail.Bottom - chart.Bottom) < 1d);
        Assert.True(detail.Width > list.Width && list.Width > chart.Width);
        Assert.Equal("図", Assert.IsType<TextBlock>(ById<TextBlock>(view, WorkspacePanelIds.ResultsChart + ".Title")).Text);
        Assert.True(Required<ListBox>(view, "RowScoreList").IsEffectivelyVisible);
        Assert.True(Required<ListBox>(view, "ResultsList").IsEffectivelyVisible);
        Assert.True(fixture.Shell.ResultsOutputViewModel.IsDetailVisible);
        // FR-066: with a result, the chart panel shows the result charts (the histogram first).
        Assert.True(ById<ContentControl>(view, "ResultsChartContent").IsEffectivelyVisible);
        Assert.True(ById<ListBox>(view, "ResultsHistogramChart").IsEffectivelyVisible);
        Assert.False(IsShown(view, "ResultsChartEmpty"));
        Assert.False(IsShown(view, "ResultsChartUnavailable"));
        // The panel hosts any visualization, and explains the alternative without one.
        fixture.Shell.ResultsOutputViewModel.Visualization = new TextBlock { Text = "chart" };
        Render();
        Assert.True(ById<ContentControl>(view, "ResultsChartContent").IsEffectivelyVisible);
        Assert.False(IsShown(view, "ResultsChartUnavailable"));
        fixture.Shell.ResultsOutputViewModel.Visualization = null;
        Render();
        Assert.True(ById<TextBlock>(view, "ResultsChartUnavailable").IsEffectivelyVisible);
        fixture.Shell.ResultsOutputViewModel.Visualization = fixture.Shell.ResultsOutputViewModel.Charts;
        Render();
        Assert.True(ById<ListBox>(view, "ResultsHistogramChart").IsEffectivelyVisible);
        fixture.AssertPassive();
    }

    [AvaloniaFact]
    public void Chart_panel_without_a_result_says_there_is_no_result_and_why()
    {
        using ResultsOutputViewModel results = new(new RecordingOutputBoundary());
        ResultsOutputView view = new(results);
        Window window = Host(view, 1180d, 800d);
        try
        {
            PanelWorkspace workspace = Required<PanelWorkspace>(view, "ResultsWorkspace");
            workspace.Execute(WorkspacePanelOperation.Maximize, WorkspacePanelIds.ResultsChart);
            Render();
            TextBlock empty = ById<TextBlock>(view, "ResultsChartEmpty");
            Assert.True(empty.IsEffectivelyVisible);
            Assert.Equal("結果がありません", empty.Text);
            Assert.Contains("定量化の実行が完了", ById<TextBlock>(view, "ResultsChartEmptyReason").Text, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task P02_and_P03_users_get_the_designer_default_on_design_and_the_engineer_default_on_execution()
    {
        WorkspaceLayoutService service = new();
        Assert.True(service.SetPersonaSelected(Persona.AssessmentDesigner, true));
        Assert.True(service.SetPersonaSelected(Persona.MaintenanceEngineer, true));
        Assert.True(service.SetPersonaSelected(Persona.Grader, false));
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync(service);

        fixture.NavigateTo(WorkflowStep.Design);
        PanelWorkspace design = fixture.Workspace;
        Assert.True(design.IsPresented(WorkspacePanelIds.DesignForm));
        Assert.True(design.IsPresented(WorkspacePanelIds.DesignAllocation));
        Rect form = BoundsInWorkspace(design, WorkspacePanelIds.DesignForm);
        Rect allocation = BoundsInWorkspace(design, WorkspacePanelIds.DesignAllocation);
        Assert.True(form.Right <= allocation.Left && form.Width > allocation.Width);
        Assert.Equal("配点構成", ById<TextBlock>(fixture.CurrentView, WorkspacePanelIds.DesignAllocation + ".Title").Text);
        // FR-067/FR-071: the allocation bar is shown whenever a definition exists.
        Assert.True(ById<AllocationBarPanel>(fixture.CurrentView, "DesignAllocationBar").IsEffectivelyVisible);
        Assert.True(ById<TextBlock>(fixture.CurrentView, "DesignAllocationDifference").IsEffectivelyVisible);
        AssertNormalBody(fixture.Window);

        fixture.NavigateTo(WorkflowStep.Execution);
        PanelWorkspace execution = fixture.Workspace;
        Assert.True(execution.IsPresented(WorkspacePanelIds.ExecutionProgress));
        Assert.True(execution.IsPresented(WorkspacePanelIds.ExecutionCost));
        Assert.True(BoundsInWorkspace(execution, WorkspacePanelIds.ExecutionProgress).Bottom
            <= BoundsInWorkspace(execution, WorkspacePanelIds.ExecutionCost).Top);

        fixture.NavigateTo(WorkflowStep.Results);
        Assert.Equal(WorkspaceOrientation.Vertical, fixture.Workspace.Layout.Orientation);
        Assert.True(fixture.Workspace.IsPresented(WorkspacePanelIds.ResultsCost));
        fixture.AssertPassive();
    }

    [AvaloniaFact]
    public async Task Settings_common_persona_check_boxes_keep_one_selected_and_never_mark_setting_txt_dirty()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        MainWindow window = fixture.Window;
        fixture.Shell.OpenSettings(SettingsCategory.Common);
        Render();
        SettingsView settings = Assert.IsType<SettingsView>(CurrentView(window));
        TabControl tabs = ById<TabControl>(settings, "SettingsCommonTabs");
        TabItem personaTab = ById<TabItem>(settings, "SettingsPersonaTab");
        tabs.SelectedItem = personaTab;
        Render();
        CheckBox p01 = ById<CheckBox>(settings, "SettingsPersonaP01");
        CheckBox p02 = ById<CheckBox>(settings, "SettingsPersonaP02");
        CheckBox p03 = ById<CheckBox>(settings, "SettingsPersonaP03");
        Assert.Equal(["P-01 採点担当教員", "P-02 評価設計担当", "P-03 保守エンジニア"], new[] { p01, p02, p03 }.Select(box => box.Content as string));
        Assert.True(p01.IsChecked);
        Assert.False(p02.IsChecked);
        Assert.False(p03.IsChecked);
        Assert.False(p01.IsEffectivelyEnabled);
        Assert.All(new[] { p01, p02, p03 }, box =>
        {
            Assert.True(box.Bounds.Height >= 44d);
            Assert.Equal(14d, box.FontSize);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(box)));
        });

        bool settingsDirty = fixture.Shell.Settings.HasUnsavedChanges;
        Assert.True(p02.Focus(NavigationMethod.Tab));
        Press(window, Key.Space);
        Assert.True(fixture.Shell.Workspace.IsSelected(Persona.AssessmentDesigner));
        Assert.True(p01.IsEffectivelyEnabled);
        Assert.True(p01.Focus(NavigationMethod.Tab));
        Press(window, Key.Space);
        Assert.Equal([Persona.AssessmentDesigner], fixture.Shell.Workspace.Personas);
        Assert.False(p02.IsEffectivelyEnabled);
        // Personas belong to layout.json: the explicit-save state of setting.txt does not change.
        Assert.Equal(settingsDirty, fixture.Shell.Settings.HasUnsavedChanges);
        Assert.Null(fixture.Shell.Settings.LastSaveTask);

        Activate(ById<Button>(settings, "SettingsRequestClose"));
        fixture.NavigateTo(WorkflowStep.Design);
        Assert.True(fixture.Workspace.IsPresented(WorkspacePanelIds.DesignAllocation));
        fixture.AssertPassive();
    }

    private static bool IsShown(Control root, string id) => root.GetVisualDescendants().OfType<Control>()
        .Any(control => control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == id);

    private static Rect BoundsInWorkspace(PanelWorkspace workspace, string id)
    {
        WorkspacePanel panel = Assert.IsType<WorkspacePanel>(workspace.FindPanel(id));
        Point origin = panel.TranslatePoint(default, workspace)!.Value;
        return new Rect(origin, panel.Bounds.Size);
    }
}
