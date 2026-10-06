using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workspace;
using Xunit;
using static StudyReportEvaluator.App.Tests.UI.PanelWorkspaceTests;
using static StudyReportEvaluator.App.Tests.UI.ResponsiveLayoutTests;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: NFR-UX-007 (AC-083), NFR-UX-006 (AC-082), NFR-A11Y-001 (AC-078)
public sealed class CommandPaletteTests
{
    [AvaloniaFact]
    public async Task Typing_default_offers_the_reset_command_and_enter_restores_the_default_layout_without_ai()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        fixture.NavigateTo(WorkflowStep.Results);
        MainWindow window = fixture.Window;
        PanelWorkspace workspace = fixture.Workspace;
        workspace.Execute(WorkspacePanelOperation.Hide, WorkspacePanelIds.ResultsChart);
        workspace.Execute(WorkspacePanelOperation.ToggleOrientation, WorkspacePanelIds.ResultsList);
        Render();
        Assert.True(fixture.Shell.Workspace.HasCustomLayout(WorkspaceScreens.Results));
        Control focused = Assert.IsAssignableFrom<Control>(PanelWorkspaceTestsFocus(window));

        PressKey(window, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.True(window.IsCommandPaletteOpen);
        TextBox search = Required<TextBox>(window, "CommandPaletteSearch");
        Assert.Same(search, window.FocusManager?.GetFocusedElement());
        Assert.True(search.Bounds.Height >= 44d);
        window.KeyTextInput("既定");
        Render();
        ListBox results = Required<ListBox>(window, "CommandPaletteResults");
        CommandPaletteEntry candidate = Assert.Single(results.Items.OfType<CommandPaletteEntry>());
        Assert.Equal("既定のレイアウトに戻す", candidate.Title);
        Assert.Same(candidate, results.SelectedItem);
        PressKey(window, Key.Enter);

        Assert.False(window.IsCommandPaletteOpen);
        Assert.False(fixture.Shell.Workspace.HasCustomLayout(WorkspaceScreens.Results));
        Assert.Equal(fixture.Shell.Workspace.GetDefaultLayout(WorkspaceScreens.Results), workspace.Layout);
        Assert.True(workspace.IsPresented(WorkspacePanelIds.ResultsChart));
        Assert.Same(focused, window.FocusManager?.GetFocusedElement());
        Assert.Equal(0, fixture.Authentication.CallCount);
        fixture.AssertPassive();
    }

    [AvaloniaFact]
    public async Task Command_search_lists_steps_settings_panels_and_status_check_but_never_evaluation_or_login()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        fixture.NavigateTo(WorkflowStep.Execution);
        await fixture.Shell.ExecutionViewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Render();
        Assert.Equal(1, fixture.Authentication.CallCount);
        MainWindow window = fixture.Window;

        PressKey(window, Key.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        CommandPaletteEntry[] entries = [.. window.CommandPaletteEntries];
        Assert.Equal(4, entries.Count(entry => entry.Id.StartsWith("Navigate.", StringComparison.Ordinal)));
        Assert.Equal(5, entries.Count(entry => entry.Id.StartsWith("Settings.", StringComparison.Ordinal)));
        Assert.Contains(entries, entry => entry.Title == "Copilot 状態を確認");
        Assert.Contains(entries, entry => entry.Title == "既定のレイアウトに戻す");
        Assert.Contains(entries, entry => entry.Title == "パネル「コスト・ログ」を隠す");
        Assert.Contains(entries, entry => entry.Title == "パネル「実行条件・出力先」を最大化");
        Assert.DoesNotContain(entries, entry => entry.Title.Contains("定量化を開始", StringComparison.Ordinal)
            || entry.Title.Contains("ログイン", StringComparison.Ordinal)
            || entry.Title.Contains("開始", StringComparison.Ordinal)
            || entry.Title.Contains("再開", StringComparison.Ordinal)
            || entry.Id.Contains("Start", StringComparison.OrdinalIgnoreCase)
            || entry.Id.Contains("Login", StringComparison.OrdinalIgnoreCase)
            || entry.Id.Contains("Resume", StringComparison.OrdinalIgnoreCase));
        window.KeyTextInput("定量化を開始");
        Render();
        Assert.Equal(0, Required<ListBox>(window, "CommandPaletteResults").ItemCount);
        PressKey(window, Key.Escape);
        Assert.False(window.IsCommandPaletteOpen);

        // Running every offered command except moving away still starts no evaluation and no login.
        foreach (CommandPaletteEntry entry in entries.Where(entry => entry.Id.StartsWith("Panel.", StringComparison.Ordinal)
            || entry.Id.StartsWith("Layout.", StringComparison.Ordinal) || entry.Id == "Copilot.CheckStatus"))
        {
            entry.Execute();
            Render();
        }

        for (int attempt = 0; attempt < 50 && fixture.Authentication.CallCount < 2; attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Render();
        }

        Assert.Equal(2, fixture.Authentication.CallCount);
        Assert.False(fixture.Shell.ExecutionViewModel.IsRunning);
        fixture.AssertPassive();
    }

    [AvaloniaFact]
    public async Task Alt_digits_move_to_steps_only_where_the_step_buttons_allow_it()
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync();
        MainWindow window = fixture.Window;
        Assert.Equal(WorkflowStep.Input, fixture.Shell.CurrentStep);

        PressKey(window, Key.D4, RawInputModifiers.Alt);
        Assert.Equal(WorkflowStep.Input, fixture.Shell.CurrentStep);
        PressKey(window, Key.D2, RawInputModifiers.Alt);
        Assert.Equal(WorkflowStep.Design, fixture.Shell.CurrentStep);
        PressKey(window, Key.D3, RawInputModifiers.Alt);
        Assert.Equal(WorkflowStep.Execution, fixture.Shell.CurrentStep);
        PressKey(window, Key.D1, RawInputModifiers.Alt);
        Assert.Equal(WorkflowStep.Input, fixture.Shell.CurrentStep);
        Assert.Equal("Alt+3", Avalonia.Automation.AutomationProperties.GetAcceleratorKey(Required<Button>(window, "ExecutionStepButton")));

        RunCommand(window, "4 結果・出力へ移動");
        Assert.Equal(WorkflowStep.Results, fixture.Shell.CurrentStep);
        RunCommand(window, "設定 通常評価");
        Assert.True(fixture.Shell.IsSettingsOpen);
        Assert.Equal(StudyReportEvaluator.App.ViewModels.SettingsCategory.Evaluation, fixture.Shell.Settings.SelectedCategory);
        fixture.AssertPassive();
    }

    private static object? PanelWorkspaceTestsFocus(MainWindow window)
    {
        Button reset = Required<Button>(window, "NextStepButton");
        if (!reset.IsEffectivelyVisible)
        {
            reset = Required<Button>(window, "PreviousStepButton");
        }

        Assert.True(reset.Focus(NavigationMethod.Tab));
        Render();
        return window.FocusManager?.GetFocusedElement();
    }
}
