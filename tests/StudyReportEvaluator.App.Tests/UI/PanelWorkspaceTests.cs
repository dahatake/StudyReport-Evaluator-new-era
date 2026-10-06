using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workspace;
using StudyReportEvaluator.Core.Domain;
using Xunit;
using static StudyReportEvaluator.App.Tests.UI.ResponsiveLayoutTests;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: NFR-UX-003 (AC-079), NFR-UX-005 (AC-081), NFR-UX-006 (AC-082)
public sealed class PanelWorkspaceTests
{
    private static readonly string[] MenuLabels =
        ["前へ移動", "後ろへ移動", "上下に並べる", "広げる", "狭める", "タブにまとめる", "タブから出す", "隠す", "最大化", "元に戻す",
            "表示するパネル", "既定のレイアウトに戻す"];

    [AvaloniaFact]
    public async Task Results_detail_merged_into_the_chart_tabs_and_a_maximized_list_keep_contents_and_the_selected_row()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        fixture.NavigateTo(WorkflowStep.Results);
        ResultsOutputViewModel results = fixture.Shell.ResultsOutputViewModel;
        ResultsOutputView view = Assert.IsType<ResultsOutputView>(fixture.CurrentView);
        PanelWorkspace workspace = fixture.Workspace;
        ListBox rows = Required<ListBox>(view, "RowScoreList");
        Assert.Equal(WorkspaceReflowMode.Full, workspace.Mode);
        Assert.True(workspace.IsPresented(WorkspacePanelIds.ResultsList));
        Assert.True(workspace.IsPresented(WorkspacePanelIds.ResultsDetail));
        Assert.True(workspace.IsPresented(WorkspacePanelIds.ResultsChart));

        rows.SelectedItem = results.VisibleRowScores[2];
        Render();
        ResultsCriterionViewModel criterion = Assert.IsType<ResultsCriterionViewModel>(results.SelectedCriterion);
        TextBox editor = ById<TextBox>(view, criterion.AutomationId + "-Override");
        editor.SetCurrentValue(TextBox.TextProperty, "3");
        Render();
        // The override recalculates the row; capture the row as it is now shown.
        ResultsRowScoreViewModel selectedRow = Assert.IsType<ResultsRowScoreViewModel>(results.SelectedRow);
        Assert.Same(results.VisibleRowScores[2], selectedRow);
        object? rowSource = rows.ItemsSource;
        int page = results.PageIndex;

        Assert.Equal(MenuLabels, MenuItems(workspace, WorkspacePanelIds.ResultsDetail).Select(item => item.Header as string));
        InvokeMenu(workspace, WorkspacePanelIds.ResultsDetail, WorkspacePanelOperation.MergeIntoTabs);
        Assert.Equal([[WorkspacePanelIds.ResultsList], [WorkspacePanelIds.ResultsChart, WorkspacePanelIds.ResultsCost, WorkspacePanelIds.ResultsDetail]],
            workspace.Layout.Groups.Select(group => group.PanelIds.ToArray()));
        Assert.True(workspace.IsPresented(WorkspacePanelIds.ResultsDetail));
        Assert.False(workspace.IsPresented(WorkspacePanelIds.ResultsChart));
        TabStripItem chartTab = ById<TabStripItem>(view, WorkspacePanelIds.ResultsChart + ".Tab");
        Assert.True(chartTab.IsEffectivelyVisible);
        Assert.True(chartTab.Bounds.Height >= 44d && chartTab.Bounds.Width >= 44d);

        InvokeMenu(workspace, WorkspacePanelIds.ResultsList, WorkspacePanelOperation.Maximize);
        Assert.Equal(WorkspacePanelIds.ResultsList, workspace.Layout.MaximizedPanelId);
        Assert.True(rows.IsEffectivelyVisible);
        Assert.False(Required<ListBox>(view, "ResultsList").IsEffectivelyVisible);
        Assert.False(workspace.IsPresented(WorkspacePanelIds.ResultsDetail));

        InvokeMenu(workspace, WorkspacePanelIds.ResultsList, WorkspacePanelOperation.Restore);
        Assert.Null(workspace.Layout.MaximizedPanelId);
        Assert.True(workspace.IsPresented(WorkspacePanelIds.ResultsList));
        Assert.True(workspace.IsPresented(WorkspacePanelIds.ResultsDetail));
        Assert.Same(selectedRow, results.SelectedRow);
        Assert.Same(selectedRow, rows.SelectedItem);
        Assert.Same(criterion, results.SelectedCriterion);
        Assert.Same(rowSource, rows.ItemsSource);
        Assert.Equal(page, results.PageIndex);
        // The detail shows the same criterion with the unsaved override value.
        TextBox restoredEditor = ById<TextBox>(view, criterion.AutomationId + "-Override");
        Assert.Same(criterion, restoredEditor.DataContext);
        Assert.Equal("3", restoredEditor.Text);
        Assert.Equal("3", criterion.OverrideText);
        Assert.True(restoredEditor.IsEffectivelyVisible);
        Assert.True(results.HasUnsavedOverrides);
        fixture.AssertPassive();
    }

    [AvaloniaFact]
    public async Task Every_panel_menu_is_a_named_44_dip_keyboard_target_with_a_unique_automation_id()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        foreach (WorkflowStep step in new[] { WorkflowStep.Design, WorkflowStep.Execution, WorkflowStep.Results })
        {
            fixture.NavigateTo(step);
            PanelWorkspace workspace = fixture.Workspace;
            Button[] menus = [.. workspace.Panels.Select(panel => workspace.GetMenuButton(panel.PanelId))
                .OfType<Button>().Where(button => button.IsEffectivelyVisible)];
            Assert.NotEmpty(menus);
            foreach (Button menu in menus)
            {
                string id = Assert.IsType<string>(AutomationProperties.GetAutomationId(menu));
                Assert.EndsWith(".Menu", id, StringComparison.Ordinal);
                Assert.Contains("パネルの操作", AutomationProperties.GetName(menu), StringComparison.Ordinal);
                Assert.True(menu.Bounds.Width >= 44d && menu.Bounds.Height >= 44d, id);
                Assert.True(menu.Focusable && menu.IsTabStop && menu.IsEffectivelyEnabled, id);
                Assert.Equal(14d, menu.FontSize);
                AssertFullyInside(menu, fixture.Window);
            }

            string[] ids = [.. fixture.Window.GetVisualDescendants().OfType<Control>()
                .Select(AutomationProperties.GetAutomationId).OfType<string>().Where(id => id.Length > 0)];
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        }

        fixture.AssertPassive();
    }

    [AvaloniaFact]
    public async Task Without_a_mouse_panels_are_moved_resized_tabbed_maximized_and_reset()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        fixture.NavigateTo(WorkflowStep.Results);
        MainWindow window = fixture.Window;
        PanelWorkspace workspace = fixture.Workspace;
        ListBox rows = Required<ListBox>(fixture.CurrentView, "RowScoreList");
        Control selectedRow = Assert.IsAssignableFrom<Control>(rows.ContainerFromIndex(Math.Max(0, rows.SelectedIndex)));
        Assert.True(selectedRow.Focus(NavigationMethod.Tab));

        // The panel menu precedes its content (the list, then its rows) in the Tab order.
        Button listMenu = Assert.IsType<Button>(workspace.GetMenuButton(WorkspacePanelIds.ResultsList));
        for (int presses = 0; presses < 2 && !ReferenceEquals(listMenu, window.FocusManager?.GetFocusedElement()); presses++)
        {
            Press(window, Key.Tab, RawInputModifiers.Shift);
        }

        Assert.Same(listMenu, window.FocusManager?.GetFocusedElement());
        Press(window, Key.Enter);
        Assert.True(listMenu.Flyout?.IsOpen);
        listMenu.Flyout!.Hide();
        Render();

        double listSize = workspace.Layout.Groups[0].Size;
        RunCommand(window, "結果一覧 後ろへ移動");
        Assert.Equal([WorkspacePanelIds.ResultsDetail], workspace.Layout.Groups[0].PanelIds);
        RunCommand(window, "結果一覧 前へ移動");
        Assert.Equal([WorkspacePanelIds.ResultsList], workspace.Layout.Groups[0].PanelIds);
        RunCommand(window, "結果一覧 広げる");
        Assert.True(workspace.Layout.Groups[0].Size > listSize);
        RunCommand(window, "詳細 タブにまとめる");
        Assert.Contains(WorkspacePanelIds.ResultsDetail, workspace.Layout.Groups[1].PanelIds);
        Assert.Equal(2, workspace.Layout.Groups.Length);
        RunCommand(window, "上下に並べる");
        Assert.Equal(WorkspaceOrientation.Vertical, workspace.Layout.Orientation);
        RunCommand(window, "結果一覧 最大化");
        Assert.Equal(WorkspacePanelIds.ResultsList, workspace.Layout.MaximizedPanelId);
        RunCommand(window, "最大化を元に戻す");
        Assert.Null(workspace.Layout.MaximizedPanelId);
        Assert.True(fixture.Shell.Workspace.HasCustomLayout(WorkspaceScreens.Results));

        RunCommand(window, "既定のレイアウトに戻す");
        Assert.False(fixture.Shell.Workspace.HasCustomLayout(WorkspaceScreens.Results));
        Assert.Equal(fixture.Shell.Workspace.GetDefaultLayout(WorkspaceScreens.Results), workspace.Layout);
        Assert.True(workspace.IsPresented(WorkspacePanelIds.ResultsList));
        fixture.AssertPassive();
    }

    [AvaloniaFact]
    public async Task Resetting_the_layout_keeps_unfinished_input_selection_and_page()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        fixture.Shell.Workspace.SetPersonaSelected(Persona.AssessmentDesigner, true);
        fixture.NavigateTo(WorkflowStep.Design);
        QuantificationDesignViewModel design = fixture.Shell.DesignViewModel;
        PanelWorkspace designWorkspace = fixture.Workspace;
        Assert.True(designWorkspace.IsPresented(WorkspacePanelIds.DesignAllocation));
        Button nextPage = Required<Button>(fixture.CurrentView, "NextQuestionPageButton");
        Activate(nextPage);
        int designPage = design.PageIndex;
        Assert.True(designPage > 0);
        QuestionDesignItemViewModel selectedQuestion = design.VisibleQuestions[^1];
        design.SelectedQuestion = selectedQuestion;
        TextBox basePoints = Required<TextBox>(fixture.CurrentView, "BasePointsTextBox");
        Assert.True(basePoints.Focus(NavigationMethod.Tab));
        fixture.Window.KeyTextInput("1");
        Render();
        string typing = Assert.IsType<string>(basePoints.Text);
        designWorkspace.Execute(WorkspacePanelOperation.MoveEarlier, WorkspacePanelIds.DesignAllocation);
        designWorkspace.Execute(WorkspacePanelOperation.Hide, WorkspacePanelIds.DesignAllocation);
        Render();

        RunCommand(fixture.Window, "既定のレイアウトに戻す");

        Assert.False(fixture.Shell.Workspace.HasCustomLayout(WorkspaceScreens.Design));
        Assert.True(designWorkspace.IsPresented(WorkspacePanelIds.DesignAllocation));
        Assert.Same(basePoints, Required<TextBox>(fixture.CurrentView, "BasePointsTextBox"));
        Assert.Equal(typing, basePoints.Text);
        Assert.Same(selectedQuestion, design.SelectedQuestion);
        Assert.Equal(designPage, design.PageIndex);

        fixture.NavigateTo(WorkflowStep.Results);
        ResultsOutputViewModel results = fixture.Shell.ResultsOutputViewModel;
        Activate(Required<Button>(fixture.CurrentView, "NextPageButton"));
        int resultsPage = results.PageIndex;
        Assert.True(resultsPage > 0);
        ResultsRowScoreViewModel row = results.VisibleRowScores[^1];
        Required<ListBox>(fixture.CurrentView, "RowScoreList").SelectedItem = row;
        Render();
        TextBox goToRow = Required<TextBox>(fixture.CurrentView, "GoToRowTextBox");
        goToRow.SetCurrentValue(TextBox.TextProperty, "12");
        fixture.Workspace.Execute(WorkspacePanelOperation.Maximize, WorkspacePanelIds.ResultsDetail);
        Render();
        RunCommand(fixture.Window, "既定のレイアウトに戻す");
        Assert.Same(row, results.SelectedRow);
        Assert.Equal(resultsPage, results.PageIndex);
        Assert.Equal("12", goToRow.Text);
        Assert.Null(fixture.Workspace.Layout.MaximizedPanelId);
        fixture.AssertPassive();
    }

    [AvaloniaFact]
    public async Task Changing_and_resetting_the_layout_does_not_disturb_a_running_run()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        ControlledRunBoundary runner = new(summary);
        using ExecutionViewModel execution = U04TestSupport.ConfiguredExecutionViewModel(definition, metadata, runner);
        await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        ExecutionView view = new(execution);
        Window window = new() { Width = 1180, Height = 800, Content = view };
        window.Show();
        Render();
        Task run = execution.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await runner.Started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Render();
            PanelWorkspace workspace = Required<PanelWorkspace>(view, "ExecutionWorkspace");
            Assert.True(execution.IsRunning);
            workspace.Execute(WorkspacePanelOperation.Maximize, WorkspacePanelIds.ExecutionCost);
            Render();
            Assert.True(workspace.IsPresented(WorkspacePanelIds.ExecutionCost));
            Assert.False(workspace.IsPresented(WorkspacePanelIds.ExecutionProgress));
            workspace.ResetLayout();
            Render();

            Assert.True(execution.IsRunning);
            Assert.Equal(1, runner.CallCount);
            Assert.True(workspace.IsPresented(WorkspacePanelIds.ExecutionProgress));
            Assert.True(Required<Button>(view, "CancelRunButton").IsEffectivelyEnabled);
        }
        finally
        {
            execution.Cancel();
            runner.Release();
            await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Dragging_a_splitter_resizes_the_neighbouring_panels_and_saves_their_relative_sizes()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        fixture.NavigateTo(WorkflowStep.Results);
        PanelWorkspace workspace = fixture.Workspace;
        GridSplitter splitter = Assert.Single(workspace.GetVisualDescendants().OfType<GridSplitter>(),
            candidate => AutomationProperties.GetAutomationId(candidate) == "Step.Results.Splitter.1");
        Assert.False(splitter.IsTabStop);
        double listWidth = Assert.IsType<WorkspacePanel>(workspace.FindPanel(WorkspacePanelIds.ResultsList)).Bounds.Width;
        double before = workspace.Layout.Groups[0].Size / workspace.Layout.Groups[1].Size;
        Point start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2d, splitter.Bounds.Height / 2d), fixture.Window)!.Value;

        fixture.Window.MouseDown(start, MouseButton.Left);
        fixture.Window.MouseMove(start + new Vector(60d, 0d));
        fixture.Window.MouseUp(start + new Vector(60d, 0d), MouseButton.Left);
        Render();

        Assert.True(fixture.Shell.Workspace.HasCustomLayout(WorkspaceScreens.Results));
        Assert.True(workspace.Layout.Groups[0].Size / workspace.Layout.Groups[1].Size > before);
        Assert.True(Assert.IsType<WorkspacePanel>(workspace.FindPanel(WorkspacePanelIds.ResultsList)).Bounds.Width > listWidth + 30d);
        fixture.AssertPassive();
    }

    internal static MenuItem[] MenuItems(PanelWorkspace workspace, string panelId)
    {
        Button menu = Assert.IsType<Button>(workspace.GetMenuButton(panelId));
        MenuFlyout flyout = Assert.IsType<MenuFlyout>(menu.Flyout);
        flyout.ShowAt(menu);
        Render();
        MenuItem[] items = [.. flyout.Items.OfType<MenuItem>()];
        flyout.Hide();
        Render();
        return items;
    }

    internal static void InvokeMenu(PanelWorkspace workspace, string panelId, WorkspacePanelOperation operation)
    {
        MenuItem item = Assert.Single(MenuItems(workspace, panelId),
            candidate => AutomationProperties.GetAutomationId(candidate) == $"{panelId}.Menu.{operation}");
        Assert.True(item.Command?.CanExecute(null), $"{panelId} {operation} must be available.");
        item.Command!.Execute(null);
        Render();
    }

    internal static void PressKey(TopLevel window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physical = key switch
        {
            Key.P => PhysicalKey.P,
            Key.D1 => PhysicalKey.Digit1,
            Key.D2 => PhysicalKey.Digit2,
            Key.D3 => PhysicalKey.Digit3,
            Key.D4 => PhysicalKey.Digit4,
            Key.Down => PhysicalKey.ArrowDown,
            Key.Up => PhysicalKey.ArrowUp,
            Key.Enter => PhysicalKey.Enter,
            Key.Escape => PhysicalKey.Escape,
            Key.Tab => PhysicalKey.Tab,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        Render();
    }

    /// <summary>Keyboard only: Ctrl+Shift+P, type the query, Enter.</summary>
    internal static void RunCommand(MainWindow window, string query)
    {
        PressKey(window, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.True(window.IsCommandPaletteOpen);
        TextBox search = Required<TextBox>(window, "CommandPaletteSearch");
        Assert.Same(search, window.FocusManager?.GetFocusedElement());
        window.KeyTextInput(query);
        Render();
        ListBox results = Required<ListBox>(window, "CommandPaletteResults");
        Assert.True(results.ItemCount > 0, $"No command matches '{query}'.");
        Press(window, Key.Enter);
        Assert.False(window.IsCommandPaletteOpen);
    }
}

/// <summary>A main window with a loaded workbook, a design and a 30-row fake result; no AI runtime.</summary>
internal sealed class WorkspaceUiFixture : IDisposable
{
    private WorkspaceUiFixture(X02TemporaryWorkbook workbook, MainWindowViewModel shell,
        RecordingAuthenticationBoundary authentication, RecordingRunBoundary runner)
    {
        Workbook = workbook;
        Shell = shell;
        Authentication = authentication;
        Runner = runner;
        Window = new MainWindow(shell);
    }

    internal X02TemporaryWorkbook Workbook { get; }

    internal MainWindowViewModel Shell { get; }

    internal MainWindow Window { get; }

    internal RecordingAuthenticationBoundary Authentication { get; }

    internal RecordingRunBoundary Runner { get; }

    internal Control CurrentView => ResponsiveLayoutTests.CurrentView(Window);

    internal PanelWorkspace Workspace => Assert.Single(CurrentView.GetVisualDescendants().OfType<PanelWorkspace>());

    internal static async Task<WorkspaceUiFixture> CreateAsync(
        WorkspaceLayoutService? workspace = null,
        double width = 1180d,
        double height = 800d,
        string primary = "synthetic answer",
        string questionName = "合成設問")
    {
        X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet("Original", 1, 101, 3,
            new X02Header(1, "Report answer"), new X02Header(2, "Rationale"), new X02Header(3, "Supporting"));
        try
        {
            InputViewModel input = new();
            await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
            Assert.True(await input.ApplySavedDefinitionAsync(ManyItemDefinition(), TestContext.Current.CancellationToken));
            QuantificationDesignViewModel design = new(input.DefinitionDraft, ["A", "B", "C"]);
            design.Questions[0].DisplayName = questionName;
            QuantificationDefinition resultDefinition = ManyItemDefinition(1, rowCount: 30);
            RunSummary summary = await U04TestSupport.CreateSummaryAsync(
                resultDefinition, U01TestSupport.ValidateMapping(resultDefinition).Metadata, primary);
            ResultsOutputViewModel results = new(new RecordingOutputBoundary(), U04TestSupport.Context(summary));
            RecordingAuthenticationBoundary authentication = new(new ExecutionAuthenticationSnapshot(
                ExecutionAuthenticationState.Available, [U04TestSupport.Model("model-test")], U04TestSupport.RuntimeIdentity()));
            RecordingRunBoundary runner = new((_, _, _) => throw new InvalidOperationException("Workspace tests never run AI."));
            ExecutionViewModel execution = new(authentication, runner);
            MainWindowViewModel shell = new(new WorkflowNavigator(), input, design, execution, results, null,
                workspace ?? new WorkspaceLayoutService());
            WorkspaceUiFixture fixture = new(workbook, shell, authentication, runner);
            fixture.Window.Width = width;
            fixture.Window.Height = height;
            fixture.Window.Show();
            Render();
            return fixture;
        }
        catch
        {
            workbook.Dispose();
            throw;
        }
    }

    internal void NavigateTo(WorkflowStep step)
    {
        while (Shell.CurrentStep != step)
        {
            ICommand command = Shell.CurrentStep < step ? Shell.NextCommand : Shell.PreviousCommand;
            Assert.True(command.CanExecute(null));
            command.Execute(null);
            Render();
        }
    }

    internal void AssertPassive()
    {
        Assert.Equal(0, Runner.CallCount);
        Assert.Null(Shell.ExecutionViewModel.LastLoginTask);
        Assert.Null(Shell.ExecutionViewModel.LastRunContext);
    }

    public void Dispose()
    {
        Window.Close();
        Shell.Dispose();
        Workbook.Dispose();
    }
}
