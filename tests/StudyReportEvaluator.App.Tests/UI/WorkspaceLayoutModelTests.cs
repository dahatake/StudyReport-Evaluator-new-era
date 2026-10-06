using StudyReportEvaluator.App.Workspace;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: NFR-UX-003 (AC-079), NFR-UX-006 (AC-082), NFR-UX-008 (AC-084), NFR-UX-009 (AC-085), FR-065 (AC-087)
public sealed class WorkspaceLayoutModelTests
{
    private const string List = WorkspacePanelIds.ResultsList;
    private const string Detail = WorkspacePanelIds.ResultsDetail;
    private const string Chart = WorkspacePanelIds.ResultsChart;
    private const string Cost = WorkspacePanelIds.ResultsCost;

    [Fact]
    public void Every_panel_operation_changes_only_the_arrangement_and_is_refused_when_unavailable()
    {
        WorkspaceLayout layout = WorkspaceDefaults.Resolve(WorkspaceScreens.Results, [Persona.Grader]);
        Assert.Equal([List, Detail, Chart, Cost], layout.PanelIds);
        Assert.Equal([Cost], layout.HiddenPanelIds);

        WorkspaceLayout moved = layout.Execute(WorkspacePanelOperation.MoveLater, List);
        Assert.Equal([[Detail], [List], [Chart, Cost]], moved.Groups.Select(group => group.PanelIds.ToArray()));
        Assert.Equal(layout.PanelIds.Order(), moved.PanelIds.Order());
        Assert.Equal(layout.Groups, moved.Execute(WorkspacePanelOperation.MoveEarlier, List).Groups);
        Assert.False(layout.CanExecute(WorkspacePanelOperation.MoveEarlier, List));
        Assert.Same(layout, layout.Execute(WorkspacePanelOperation.MoveEarlier, List));

        WorkspaceLayout vertical = layout.Execute(WorkspacePanelOperation.ToggleOrientation, List);
        Assert.Equal(WorkspaceOrientation.Vertical, vertical.Orientation);
        Assert.Equal(layout.Groups, vertical.Groups);

        double before = layout.Groups[0].Size;
        WorkspaceLayout grown = layout.Execute(WorkspacePanelOperation.Grow, List);
        Assert.True(grown.Groups[0].Size > before);
        Assert.True(grown.Execute(WorkspacePanelOperation.Shrink, List).Execute(WorkspacePanelOperation.Shrink, List).Groups[0].Size < before);

        WorkspaceLayout merged = layout.Execute(WorkspacePanelOperation.MergeIntoTabs, Detail);
        Assert.Equal([[List], [Chart, Cost, Detail]], merged.Groups.Select(group => group.PanelIds.ToArray()));
        Assert.True(merged.CanExecute(WorkspacePanelOperation.SeparateFromTabs, Detail));
        WorkspaceLayout separated = merged.Execute(WorkspacePanelOperation.SeparateFromTabs, Detail);
        Assert.Equal([[List], [Chart, Cost], [Detail]], separated.Groups.Select(group => group.PanelIds.ToArray()));
        Assert.False(layout.CanExecute(WorkspacePanelOperation.SeparateFromTabs, List));

        WorkspaceLayout hidden = layout.Execute(WorkspacePanelOperation.Hide, Chart);
        Assert.False(hidden.IsVisible(Chart));
        Assert.True(hidden.CanExecute(WorkspacePanelOperation.Show, Chart));
        Assert.True(hidden.Execute(WorkspacePanelOperation.Show, Chart).IsVisible(Chart));
        WorkspaceLayout onlyList = hidden.Execute(WorkspacePanelOperation.Hide, Detail);
        Assert.False(onlyList.CanExecute(WorkspacePanelOperation.Hide, List));
        Assert.Same(onlyList, onlyList.Execute(WorkspacePanelOperation.Hide, List));

        WorkspaceLayout maximized = layout.Execute(WorkspacePanelOperation.Maximize, List);
        Assert.Equal(List, maximized.MaximizedPanelId);
        Assert.False(maximized.CanExecute(WorkspacePanelOperation.MoveLater, List));
        Assert.True(maximized.CanExecute(WorkspacePanelOperation.Restore, List));
        Assert.Equal(layout, maximized.Execute(WorkspacePanelOperation.Restore, List));
        Assert.Null(maximized.Execute(WorkspacePanelOperation.Hide, List).MaximizedPanelId);
    }

    [Fact]
    public void Reflow_tabs_the_third_column_below_1180_and_stacks_everything_in_the_single_mode_without_changing_the_layout()
    {
        WorkspaceLayout layout = WorkspaceDefaults.Resolve(WorkspaceScreens.Results, [Persona.Grader]);

        WorkspaceEffectiveLayout full = layout.Reflow(WorkspaceReflowMode.Full);
        Assert.Equal(WorkspaceOrientation.Horizontal, full.Orientation);
        Assert.Equal([[List], [Detail], [Chart]], full.Groups.Select(group => group.PanelIds.ToArray()));

        WorkspaceEffectiveLayout compact = layout.Reflow(WorkspaceReflowMode.Compact);
        Assert.Equal([[List], [Detail, Chart]], compact.Groups.Select(group => group.PanelIds.ToArray()));
        Assert.Equal([0, 1], compact.Groups.Select(group => group.SourceGroupIndex));

        WorkspaceEffectiveLayout single = layout.Reflow(WorkspaceReflowMode.Single);
        Assert.Equal(WorkspaceOrientation.Vertical, single.Orientation);
        Assert.Equal([List, Detail, Chart], single.Groups.SelectMany(group => group.PanelIds));

        Assert.Equal(WorkspaceDefaults.Resolve(WorkspaceScreens.Results, [Persona.Grader]), layout);
        WorkspaceLayout vertical = layout.Execute(WorkspacePanelOperation.ToggleOrientation, List);
        Assert.Equal(3, vertical.Reflow(WorkspaceReflowMode.Compact).Groups.Length);
    }

    [Fact]
    public void Persona_defaults_follow_section_5_1_and_the_first_selected_persona_with_a_default_wins()
    {
        WorkspaceLayout grader = WorkspaceDefaults.Resolve(WorkspaceScreens.Results, [Persona.Grader]);
        Assert.Equal(WorkspaceOrientation.Horizontal, grader.Orientation);
        Assert.Equal([[List], [Detail], [Chart, Cost]], grader.Groups.Select(group => group.PanelIds.ToArray()));

        Persona[] designerAndEngineer = [Persona.MaintenanceEngineer, Persona.AssessmentDesigner];
        WorkspaceLayout design = WorkspaceDefaults.Resolve(WorkspaceScreens.Design, designerAndEngineer);
        Assert.Equal([[WorkspacePanelIds.DesignForm], [WorkspacePanelIds.DesignAllocation]],
            design.Groups.Select(group => group.PanelIds.ToArray()));
        Assert.Empty(design.HiddenPanelIds);
        WorkspaceLayout execution = WorkspaceDefaults.Resolve(WorkspaceScreens.Execution, designerAndEngineer);
        Assert.Equal(WorkspaceOrientation.Vertical, execution.Orientation);
        Assert.Equal([WorkspacePanelIds.ExecutionCost], execution.Groups[^1].PanelIds);

        // P-01 has no design or execution default; the generic (pre-workspace) arrangement is used.
        Assert.Equal(WorkspaceDefaults.Generic(WorkspaceScreens.Design).Normalize(WorkspaceDefaults.PanelsFor(WorkspaceScreens.Design)),
            WorkspaceDefaults.Resolve(WorkspaceScreens.Design, [Persona.Grader]));
        Assert.Equal([WorkspacePanelIds.DesignAllocation],
            WorkspaceDefaults.Resolve(WorkspaceScreens.Design, [Persona.Grader]).HiddenPanelIds);
        // P-01 precedes P-03 on the results screen.
        Assert.Equal(grader, WorkspaceDefaults.Resolve(WorkspaceScreens.Results, [Persona.MaintenanceEngineer, Persona.Grader]));
        Assert.Equal(WorkspaceOrientation.Vertical,
            WorkspaceDefaults.Resolve(WorkspaceScreens.Results, [Persona.MaintenanceEngineer]).Orientation);
    }

    [Fact]
    public void Normalization_keeps_known_panels_once_and_always_leaves_one_visible()
    {
        WorkspaceLayout odd = new(WorkspaceOrientation.Horizontal,
            [new WorkspaceGroup([List, "Unknown.Panel", List], 3d), new WorkspaceGroup([Detail], 99d)],
            [List, Detail], "Unknown.Panel");
        WorkspaceLayout normalized = odd.Normalize(WorkspaceDefaults.PanelsFor(WorkspaceScreens.Results));

        Assert.Equal([List, Detail, Chart, Cost], normalized.PanelIds);
        Assert.Null(normalized.MaximizedPanelId);
        Assert.Equal(WorkspaceGroup.MaximumSize, normalized.Groups[1].Size);
        Assert.NotEmpty(normalized.VisiblePanelIds);
    }

    [Fact]
    public void Command_search_filters_by_every_term_case_insensitively()
    {
        CommandPaletteEntry reset = new("Layout.Reset", "既定のレイアウトに戻す", "4 結果", () => { });
        CommandPaletteEntry check = new("Copilot.CheckStatus", "Copilot 状態を確認", "Copilot", () => { });

        Assert.Equal([reset], CommandPaletteFilter.Filter([reset, check], "既定"));
        Assert.Equal([check], CommandPaletteFilter.Filter([reset, check], "copilot 状態"));
        Assert.Equal([reset, check], CommandPaletteFilter.Filter([reset, check], "  "));
        Assert.Empty(CommandPaletteFilter.Filter([reset, check], "定量化を開始"));
    }
}
