using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

/// <summary>AC-046: run-preparation parts live in the Input step; step states say "設定済み".</summary>
// Requirements: FR-047 (AC-048)
public sealed class PreparationOnInputTests
{
    private static readonly string[] PreparationIds =
    [
        "CopilotAuthenticationStatus", "CheckCopilotAuthentication", "StartCopilotLogin", "CancelCopilotLogin",
        "CopilotLoginStatus", "ExecutionResumeMode", "ExecutionResumePartialPath", "ExecutionPickResumeCheckpoint",
        "ExecutionValidationSummary", "ExecutionTechnicalErrors", "ExecutionTechnicalErrorDetail",
    ];

    private static MainWindow CreateWindow() => new(new MainWindowViewModel(
        new WorkflowNavigator(), new InputViewModel(), new QuantificationDesignViewModel(),
        new ExecutionViewModel(
            new RecordingAuthenticationBoundary(new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.AuthRequired)),
            new RecordingRunBoundary((_, _, _) => throw new InvalidOperationException("No run was requested."))),
        new ResultsOutputViewModel(new RecordingOutputBoundary())));

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static Control Current(MainWindow window) =>
        Assert.IsAssignableFrom<Control>(window.FindControl<ContentControl>("CurrentStepContent")!.Content);

    private static bool HasId(Control root, string id) => root.GetVisualDescendants().OfType<Control>()
        .Any(control => AutomationProperties.GetAutomationId(control) == id);

    [AvaloniaFact]
    public void Main_window_input_hosts_the_preparation_parts_and_execution_has_none()
    {
        MainWindow window = CreateWindow();
        try
        {
            window.Show();
            Render();
            InputView input = Assert.IsType<InputView>(Current(window));
            Button toggle = input.FindControl<Button>("PreparationToggleButton")!;
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.Equal("InputTogglePreparation", AutomationProperties.GetAutomationId(toggle));
            Assert.False(input.IsPreparationOpen);
            Assert.NotNull(input.FindControl<TextBox>("FilePathTextBox"));

            input.SetPreparationOpen(true);
            Render();
            Assert.Equal("入力に戻る", toggle.Content);
            Assert.Same(window.ViewModel.ExecutionViewModel, input.PreparationPanel.DataContext);
            Assert.False(input.FindControl<Control>("InputMappingHost")!.IsVisible);
            foreach (string id in PreparationIds)
            {
                Assert.True(HasId(input, id), id);
            }

            Assert.True(input.PreparationPanel.FindControl<Button>("StartCopilotLogin")!.IsEffectivelyEnabled);
            Assert.Same(window.ViewModel.ExecutionViewModel.LoginCommand,
                input.PreparationPanel.FindControl<Button>("StartCopilotLogin")!.Command);

            input.SetPreparationOpen(false);
            Render();
            Assert.True(input.FindControl<Control>("InputMappingHost")!.IsVisible);
            Assert.Equal("実行の準備を開く", toggle.Content);

            window.ViewModel.NextCommand.Execute(null);
            window.ViewModel.NextCommand.Execute(null);
            Render();
            ExecutionView execution = Assert.IsType<ExecutionView>(Current(window));
            foreach (string id in PreparationIds)
            {
                Assert.False(HasId(execution, id), id);
            }

            Assert.Empty(execution.GetVisualDescendants().OfType<ExecutionPreparationPanel>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Standalone_input_view_hides_the_preparation_parts()
    {
        InputView input = new(new InputViewModel());
        Window window = new() { Width = 950, Height = 450, Content = input };
        try
        {
            window.Show();
            Render();
            Assert.False(input.FindControl<Button>("PreparationToggleButton")!.IsVisible);
            Assert.False(input.IsPreparationOpen);
            Assert.Null(input.PreparationPanel.DataContext);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Step_states_say_configured_and_never_visited()
    {
        MainWindow window = CreateWindow();
        try
        {
            window.Show();
            Render();
            window.ViewModel.NextCommand.Execute(null);
            Render();
            Assert.Equal("設定済み", window.ViewModel.InputStep.StatusText);
            Assert.Contains("設定済み", window.ViewModel.InputStep.AccessibleName, StringComparison.Ordinal);
            Assert.Equal("現在・選択中", window.ViewModel.DesignStep.StatusText);
            Assert.Equal("未着手", window.ViewModel.ExecutionStep.StatusText);
            WorkflowStep[] steps = Enum.GetValues<WorkflowStep>();
            Assert.All(steps, step => Assert.DoesNotContain("訪問済み",
                window.FindControl<Button>(step + "StepButton") is { } b ? AutomationProperties.GetName(b) ?? "" : "",
                StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
        }
    }
}