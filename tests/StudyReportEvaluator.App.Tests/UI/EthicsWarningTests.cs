using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Resources;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-036 (AC-037)
public sealed class EthicsWarningTests
{
    private const string ExpectedWarning =
        "生成AIが行う評価には正確性が欠ける可能性があるため、必ず自分で責任をもって評点を行ってください。このツールや生成AIは評価結果に対しては一切の責任を負えません";

    [AvaloniaFact]
    public void Warning_persists_across_all_steps_and_settings_categories_without_blocking_keyboard_navigation()
    {
        RecordingAuthenticationBoundary authentication = new(new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.AuthRequired));
        RecordingRunBoundary runner = new((_, _, _) => throw new InvalidOperationException("No run was requested."));
        RecordingOutputBoundary output = new();
        MainWindow window = CreateWindow(authentication, runner, output);

        try
        {
            window.Show();
            Render();
            Border warning = Required<Border>(window, "EthicsWarningBanner");
            TextBlock message = Required<TextBlock>(window, "EthicsWarningMessage");
            MainWindowViewModel shell = window.ViewModel;
            WorkflowStep[] steps = Enum.GetValues<WorkflowStep>();
            SettingsCategory[] categories = Enum.GetValues<SettingsCategory>();
            Assert.Equal(4, steps.Length);
            Assert.Equal(4, shell.Steps.Length);
            Assert.Equal(5, categories.Length);
            Dictionary<SettingsCategory, (Control View, string[] Ids)> cachedCategories = [];
            foreach (WorkflowStep step in steps)
            {
                Assert.Equal(step, shell.CurrentStep);
                Control originalView = CurrentView(window);
                Assert.Equal(step switch
                {
                    WorkflowStep.Input => typeof(InputView),
                    WorkflowStep.Design => typeof(QuantificationDesignView),
                    WorkflowStep.Execution => typeof(ExecutionView),
                    _ => typeof(ResultsOutputView),
                }, originalView.GetType());
                AssertOnlyCurrentView(window);
                AssertWarning(window, warning, message);
                Assert.DoesNotContain(AllControls(originalView), control => control is EvaluatorSettingsView
                    or SpecialEvaluationSettingsView or ImportedPromptSettingsView);
                Button invoker = originalView switch
                {
                    QuantificationDesignView design => Required<Button>(design, "OpenEvaluatorSettingsButton"),
                    ExecutionView execution => Required<Button>(execution, "ChangeExecutionSettingsButton"),
                    _ => Required<Button>(window, "SettingsOpenButton"),
                };
                Activate(window, invoker);
                SettingsView settings = Assert.IsType<SettingsView>(CurrentView(window));
                Assert.Same(shell.Settings, settings.DataContext);
                Assert.Equal(step, shell.CurrentStep); // Settings is not a fifth workflow step.
                Assert.True(shell.IsSettingsOpen);
                Button[] categoryButtons = categories.Select(category => ById<Button>(settings, "SettingsCategory" + category)).ToArray();
                for (int index = 0; index < categories.Length; index++)
                {
                    Button categoryButton = categoryButtons[index];
                    Assert.Same(shell.Settings.SelectCategoryCommand, categoryButton.Command);
                    Assert.Equal(categories[index], Assert.IsType<SettingsCategory>(categoryButton.CommandParameter));
                    Assert.True(categoryButton.MinHeight >= 44d);
                    Assert.True(categoryButton.Bounds.Height >= 44d);
                    Assert.True(categoryButton.Bounds.Width >= 44d);
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(categoryButton)));
                    if (index == 0)
                    {
                        Activate(window, categoryButton);
                    }
                    else
                    {
                        Assert.True(categoryButtons[index - 1].Focus(NavigationMethod.Tab));
                        Press(window, Key.Tab);
                        Assert.Same(categoryButton, window.FocusManager?.GetFocusedElement());
                        Press(window, Key.Tab, RawInputModifiers.Shift);
                        Assert.Same(categoryButtons[index - 1], window.FocusManager?.GetFocusedElement());
                        Press(window, Key.Tab);
                        Press(window, Key.Enter);
                    }

                    SettingsCategory category = categories[index];
                    Assert.Equal(category, shell.Settings.SelectedCategory);
                    Assert.Equal(step, shell.CurrentStep);
                    Assert.Same(settings, CurrentView(window));
                    Control categoryView = Assert.IsAssignableFrom<Control>(Required<ContentControl>(settings, "CurrentSettingsContent").Content);
                    Assert.Equal(category switch
                    {
                        SettingsCategory.Common => typeof(TabControl),
                        SettingsCategory.Mapping => typeof(MappingSettingsView),
                        SettingsCategory.Evaluation => typeof(EvaluatorSettingsView),
                        SettingsCategory.Special => typeof(SpecialEvaluationSettingsView),
                        _ => typeof(ImportedPromptSettingsView),
                    }, categoryView.GetType());
                    Assert.Same(category == SettingsCategory.Common ? (object)shell.Settings
                        : category == SettingsCategory.Mapping ? shell.InputViewModel : shell.DesignViewModel, categoryView.DataContext);
                    string[] ids = UniqueIds(settings);
                    if (cachedCategories.TryGetValue(category, out var first))
                    {
                        Assert.Same(first.View, categoryView);
                        Assert.Equal(first.Ids, ids);
                    }
                    else
                    {
                        cachedCategories.Add(category, (categoryView, ids));
                    }

                    AssertOnlyCurrentView(window);
                    AssertWarning(window, warning, message);
                    Assert.True(Required<Button>(settings, "SettingsRequestClose").IsEffectivelyEnabled);
                    AssertPassive();
                }

                Activate(window, Required<Button>(settings, "SettingsRequestClose"));
                Assert.False(shell.IsSettingsOpen);
                Assert.Equal(step, shell.CurrentStep);
                Assert.Same(originalView, CurrentView(window));
                Assert.Same(invoker, window.FocusManager?.GetFocusedElement());
                AssertWarning(window, warning, message);
                AssertOnlyCurrentView(window);

                // Restoration must also retain real forward/backward keyboard navigation.
                bool previousFirst = step is WorkflowStep.Input or WorkflowStep.Results;
                Control adjacent = step switch
                {
                    WorkflowStep.Input => Required<Button>(window, "DesignStepButton"),
                    WorkflowStep.Design => Required<Button>(originalView, "OpenSpecialSettingsButton"),
                    WorkflowStep.Execution => Required<CheckBox>(originalView, "ExecutionCostToggle"),
                    _ => Required<Button>(window, "ResultsStepButton"),
                };
                Press(window, Key.Tab, previousFirst ? RawInputModifiers.Shift : RawInputModifiers.None);
                Assert.Same(adjacent, window.FocusManager?.GetFocusedElement());
                Press(window, Key.Tab, previousFirst ? RawInputModifiers.None : RawInputModifiers.Shift);
                Assert.Same(invoker, window.FocusManager?.GetFocusedElement());
                if (step != WorkflowStep.Results)
                {
                    Assert.True(shell.NextCommand.CanExecute(null));
                    Activate(window, Required<Button>(window, "NextStepButton"));
                }

                AssertPassive();
            }

            Assert.Equal(5, cachedCategories.Count);

            void AssertPassive()
            {
                Assert.Equal(0, authentication.CallCount);
                Assert.Equal(0, runner.CallCount);
                Assert.Equal(0, output.ExportCount);
                Assert.Null(shell.ExecutionViewModel.LastLoginTask);
                Assert.Null(shell.Settings.LastLoadTask);
                Assert.Null(shell.Settings.LastSaveTask);
                Assert.Null(shell.Settings.LastApplySavedDefinitionTask);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Warning_has_no_acknowledgement_controls_or_focus_target()
    {
        MainWindow window = CreateWindow();

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Border warning = Required<Border>(window, "EthicsWarningBanner");
            Control[] descendants = [.. warning.GetVisualDescendants().OfType<Control>()];

            Assert.False(warning.Focusable);
            Assert.False(warning.IsTabStop);
            Assert.False(warning.IsHitTestVisible);
            Assert.DoesNotContain(descendants, control => control is Button);
            Assert.DoesNotContain(descendants, control => control is CheckBox);
            Assert.DoesNotContain(descendants, control => control is TextBox);
            Assert.DoesNotContain(
                descendants.OfType<InputElement>(),
                input => input.Focusable);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Warning_is_one_fixed_resource_and_has_no_state_or_decision_contract()
    {
        Assert.Equal(ExpectedWarning, EthicsWarningText.Message);

        FieldInfo messageField = Assert.Single(
            typeof(EthicsWarningText).GetFields(BindingFlags.Public | BindingFlags.Static));
        Assert.Equal(nameof(EthicsWarningText.Message), messageField.Name);
        Assert.True(messageField.IsLiteral);
        Assert.Empty(typeof(EthicsWarningText).GetProperties(BindingFlags.Public | BindingFlags.Static));

        string[] prohibitedStateTerms =
        [
            "Acknowledge",
            "Acknowledgement",
            "Consent",
            "Dismiss",
            "AcceptWarning",
            "WarningAccepted",
            "EthicsAccepted",
            "WarningRole",
            "WarningExpiry",
            "WarningExpiration",
            "WarningStatus",
        ];
        MemberInfo[] shellMembers = new[] { typeof(MainWindowViewModel), typeof(SettingsViewModel),
            typeof(ExecutionViewModel), typeof(ResultsOutputViewModel) }
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .ToArray();
        Assert.DoesNotContain(
            shellMembers,
            member => prohibitedStateTerms.Any(term =>
                member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));

        Type[] decisionTypes =
        [
            typeof(WorkflowNavigator),
            typeof(ColumnMappingValidator),
            typeof(QuantificationOrchestrator),
            typeof(RunSummary),
            typeof(AtomicOutputCommitter),
            typeof(QuantificationDefinition),
            typeof(QuantificationSnapshot),
        ];
        MethodInfo[] decisionMethods =
        [
            .. decisionTypes.SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)),
        ];
        Assert.NotEmpty(decisionMethods);
        Assert.DoesNotContain(
            decisionMethods.SelectMany(method => method.GetParameters()),
            parameter =>
                ContainsWarningTerm(parameter.Name)
                || ContainsWarningTerm(parameter.ParameterType.Name));
        Assert.DoesNotContain(
            decisionTypes.SelectMany(type => type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)),
            property =>
                ContainsWarningTerm(property.Name)
                || ContainsWarningTerm(property.PropertyType.Name));
    }

    [AvaloniaFact]
    public void Visible_execution_validation_is_visually_and_semantically_distinct_from_warning()
    {
        MainWindow window = CreateWindow();

        try
        {
            window.Show();
            Render();

            Border warning = Required<Border>(window, "EthicsWarningBanner");
            InputView input = Assert.IsType<InputView>(CurrentView(window));
            input.SetPreparationOpen(true);
            Render();
            ExecutionPreparationPanel execution = input.PreparationPanel;
            Border technical = Required<Border>(execution, "ExecutionValidationSummary");
            ExecutionTechnicalError error = Assert.Single(execution.ViewModel!.TechnicalErrors, item => item.Code == "AUTH_CHECK_REQUIRED");
            Required<ListBox>(execution, "TechnicalErrorsList").SelectedItem = error;
            Render();
            TextBox detail = Required<TextBox>(execution, "SelectedTechnicalErrorDetail");

            Assert.True(technical.IsEffectivelyVisible);
            Assert.True(detail.IsReadOnly);
            Assert.Equal("ExecutionValidationSummary", AutomationProperties.GetAutomationId(technical));
            Assert.Contains("ethics-warning", warning.Classes);
            Assert.DoesNotContain("ethics-warning", technical.Classes);
            Assert.NotEqual(warning.Background, technical.Background);
            Assert.NotEqual(warning.BorderBrush, technical.BorderBrush);
            Assert.Equal($"{error.Code}{Environment.NewLine}{error.TargetText}{Environment.NewLine}{error.Message}", detail.Text);
            Assert.DoesNotContain(ExpectedWarning, detail.Text, StringComparison.Ordinal);
            AssertWarning(window, warning, Required<TextBlock>(window, "EthicsWarningMessage"));
        }
        finally
        {
            window.Close();
        }
    }

    private static bool ContainsWarningTerm(string? value) =>
        value?.Contains("warning", StringComparison.OrdinalIgnoreCase) == true
        || value?.Contains("ethics", StringComparison.OrdinalIgnoreCase) == true
        || value?.Contains("acknowledge", StringComparison.OrdinalIgnoreCase) == true
        || value?.Contains("consent", StringComparison.OrdinalIgnoreCase) == true;

    private static T Required<T>(Control root, string name)
        where T : Control =>
        Assert.IsType<T>(root.FindControl<T>(name));

    private static MainWindow CreateWindow(
        RecordingAuthenticationBoundary? authentication = null,
        RecordingRunBoundary? runner = null,
        RecordingOutputBoundary? output = null) => new(new MainWindowViewModel(
            new WorkflowNavigator(), new InputViewModel(), new QuantificationDesignViewModel(),
            new ExecutionViewModel(authentication ?? new RecordingAuthenticationBoundary(
                new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.AuthRequired)),
                runner ?? new RecordingRunBoundary((_, _, _) => throw new InvalidOperationException("No run was requested."))),
            new ResultsOutputViewModel(output ?? new RecordingOutputBoundary())));

    private static Control CurrentView(MainWindow window) =>
        Assert.IsAssignableFrom<Control>(Required<ContentControl>(window, "CurrentStepContent").Content);

    private static T ById<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    private static IEnumerable<Control> AllControls(Control root) => root.GetVisualDescendants().OfType<Control>()
        .Concat(root.GetLogicalDescendants().OfType<Control>()).Prepend(root).Distinct();

    private static string[] UniqueIds(Control root)
    {
        string[] ids = AllControls(root).Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        return ids;
    }

    private static void AssertOnlyCurrentView(MainWindow window)
    {
        Control current = Assert.Single(AllControls(window), control => control is InputView
            or QuantificationDesignView or ExecutionView or ResultsOutputView or SettingsView);
        Assert.Same(CurrentView(window), current);
        Assert.Same(window.ViewModel.CurrentEditorViewModel, current.DataContext);
        UniqueIds(window); // Cached off-tree editors must not participate in this view's IDs.
    }

    private static void AssertWarning(MainWindow window, Border warning, TextBlock message)
    {
        Assert.Same(warning, ById<Border>(window, "EthicsWarningBanner"));
        Assert.Same(message, Required<TextBlock>(window, "EthicsWarningMessage"));
        Assert.Equal(ExpectedWarning, EthicsWarningText.Message);
        Assert.Equal(EthicsWarningText.Message, message.Text);
        Assert.True(warning.IsEffectivelyVisible);
        Assert.True(message.IsEffectivelyVisible);
        Assert.True(warning.Bounds.Height > 0d);
        Assert.False(warning.Focusable);
        Assert.False(warning.IsTabStop);
        Assert.False(warning.IsHitTestVisible);
        Assert.Contains(Required<Grid>(window, "ShellHeader"), warning.GetVisualAncestors());
        Assert.DoesNotContain(warning.GetVisualAncestors(), ancestor => ancestor is ScrollViewer);
        Assert.DoesNotContain(AllControls(warning), control => control is Button or CheckBox or TextBox || control.Focusable);
        Assert.DoesNotContain(AllControls(warning), control => ReferenceEquals(control, window.FocusManager?.GetFocusedElement()));
    }

    private static void Activate(Window window, Button button)
    {
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.True(button.Focus(NavigationMethod.Tab));
        Press(window, Key.Enter);
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physical = key switch
        {
            Key.Tab => PhysicalKey.Tab,
            Key.Enter => PhysicalKey.Enter,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        Render();
    }

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}