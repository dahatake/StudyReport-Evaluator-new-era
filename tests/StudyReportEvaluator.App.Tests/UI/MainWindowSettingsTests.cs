using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

/// <summary>T23 shell contracts only; persistence and workflow integration belong to their own tests.</summary>
// Requirements: FR-039 (AC-040), FR-042 (AC-043)
public sealed class MainWindowSettingsTests
{
    private const string ExpectedWarning =
        "生成AIが行う評価には正確性が欠ける可能性があるため、必ず自分で責任をもって評点を行ってください。このツールや生成AIは評価結果に対しては一切の責任を負えません";
    private static readonly TimeSpan BoundaryWait = TimeSpan.FromSeconds(10);
    private static readonly string[] StepButtonNames =
        ["InputStepButton", "DesignStepButton", "ExecutionStepButton", "ResultsStepButton"];

    [AvaloniaTheory]
    [InlineData(1024d, 720d)]
    [InlineData(1180d, 800d)]
    public async Task Normal_shell_keeps_five_views_inside_fixed_regions_without_outer_scroll(double width, double height)
    {
        using ShellFixture fixture = await ShellFixture.CreateAsync();
        fixture.Results.Load(U04TestSupport.Context(await U04TestSupport.CreateSummaryAsync(
            fixture.Input.DefinitionDraft, fixture.Input.Metadata!), fixture.Workbook.Path));
        fixture.Window.Width = width;
        fixture.Window.Height = height;
        List<Control> contextChanges = [];
        Control initialView = CurrentView(fixture.Window);
        Assert.Same(fixture.Input, initialView.DataContext);
        // Watch before Show: a deferred template-root binding must not replace
        // the explicitly assigned editor when the control is first initialized.
        initialView.DataContextChanged += (_, _) => contextChanges.Add(initialView);
        fixture.Show();
        Assert.Same(initialView, CurrentView(fixture.Window));
        Assert.Empty(contextChanges);
        Assert.Equal(new Size(width, height), fixture.Window.ClientSize);
        Assert.Equal(1024d, fixture.Window.MinWidth);
        Assert.Equal(720d, fixture.Window.MinHeight);
        Assert.Equal(5, Required<ContentControl>(fixture.Window, "CurrentStepContent").DataTemplates.Count);
        Dictionary<UiObservableObject, Control> firstViews = new(ReferenceEqualityComparer.Instance);
        Button settingsOpen = Required<Button>(fixture.Window, "SettingsOpenButton");
        Assert.Equal("SettingsOpen", AutomationProperties.GetAutomationId(settingsOpen));
        Assert.Same(fixture.Shell.OpenSettingsCommand, settingsOpen.Command);
        Assert.NotNull(Assert.Single(settingsOpen.GetVisualDescendants().OfType<PathIcon>()).Data);
        Assert.Contains(settingsOpen.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "設定");

        foreach (WorkflowStep step in Enum.GetValues<WorkflowStep>())
        {
            if (step != WorkflowStep.Input)
            {
                fixture.Shell.NextCommand.Execute(null);
                Render();
            }

            Assert.Equal(step, fixture.Shell.CurrentStep);
            Control view = CurrentView(fixture.Window);
            Assert.Equal(step switch
            {
                WorkflowStep.Input => typeof(InputView),
                WorkflowStep.Design => typeof(QuantificationDesignView),
                WorkflowStep.Execution => typeof(ExecutionView),
                _ => typeof(ResultsOutputView),
            }, view.GetType());
            firstViews.Add(fixture.Shell.CurrentEditorViewModel!, view);
            if (!ReferenceEquals(view, initialView))
            {
                view.DataContextChanged += (_, _) => contextChanges.Add(view);
            }

            AssertNormalBody(fixture.Window);
            AssertOnlyCurrentEditor(fixture);
            AssertMainControlsInside(view);
            if (view is ResultsOutputView)
            {
                // AC-053: the loaded run's duration/credit heading is fully visible in the real shell.
                TextBlock metrics = ById<TextBlock>(view, "ResultsRunMetrics");
                Assert.Equal(fixture.Results.RunMetricsText, metrics.Text);
                ResponsiveLayoutTests.AssertImportantTextFullyVisible(metrics, Avalonia.Media.TextWrapping.NoWrap, expectEllipsis: true);
                Assert.DoesNotContain(metrics.TextLayout.TextLines, line => line.HasCollapsed);
            }

            Button previous = Required<Button>(fixture.Window, "PreviousStepButton");
            Button next = Required<Button>(fixture.Window, "NextStepButton");
            Assert.Equal(fixture.Shell.PreviousButtonText, previous.Content);
            Assert.Equal(fixture.Shell.PreviousButtonText, AutomationProperties.GetName(previous));
            Assert.Equal(step != WorkflowStep.Results, next.IsEffectivelyVisible);
            Assert.DoesNotContain(fixture.Window.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.IsEffectivelyVisible && text.Text == "最終ステップ");

            Activate(settingsOpen);
            SettingsView settings = Assert.IsType<SettingsView>(CurrentView(fixture.Window));
            if (firstViews.TryGetValue(fixture.Shell.Settings, out Control? firstSettings))
            {
                Assert.Same(firstSettings, settings);
            }
            else
            {
                firstViews.Add(fixture.Shell.Settings, settings);
                settings.DataContextChanged += (_, _) => contextChanges.Add(settings);
            }

            Assert.Equal(step, fixture.Shell.CurrentStep);
            Assert.False(previous.IsEffectivelyVisible);
            Assert.False(next.IsEffectivelyVisible);
            Assert.True(Required<TextBlock>(fixture.Window, "SettingsNavigationHint").IsEffectivelyVisible);
            foreach (SettingsCategory category in Enum.GetValues<SettingsCategory>())
            {
                Button categoryButton = ById<Button>(settings, "SettingsCategory" + category);
                Assert.Same(fixture.Shell.Settings.SelectCategoryCommand, categoryButton.Command);
                Activate(categoryButton);
                Assert.Equal(category, fixture.Shell.Settings.SelectedCategory);
                AssertNormalBody(fixture.Window);
                AssertOnlyCurrentEditor(fixture);
                ScrollViewer body = Required<ScrollViewer>(settings, "SettingsBodyScroll");
                Assert.True(body.Extent.Height <= body.Viewport.Height + 1d, category.ToString());
                Assert.True(body.Extent.Width <= body.Viewport.Width + 1d, category.ToString());
                AssertFullyInside(Required<Grid>(settings, "SettingsFooter"), settings);
            }

            Button close = Required<Button>(settings, "SettingsRequestClose");
            Assert.Same(fixture.Shell.Settings.RequestCloseCommand, close.Command);
            Activate(close);
            Assert.Same(settingsOpen, fixture.Window.FocusManager?.GetFocusedElement());
            Assert.Same(view, CurrentView(fixture.Window));
            AssertOnlyCurrentEditor(fixture);
        }

        Assert.Equal(5, firstViews.Count);
        foreach (WorkflowStep step in Enum.GetValues<WorkflowStep>().Reverse())
        {
            fixture.Shell.NavigateCommand.Execute(step);
            Render();
            Assert.Same(firstViews[fixture.Shell.CurrentEditorViewModel!], CurrentView(fixture.Window));
            AssertNormalBody(fixture.Window);
            AssertOnlyCurrentEditor(fixture);
        }

        // Detached cached roots retain their owner too, without a temporary
        // null/shell context that could silently reset pending text or commands.
        foreach ((UiObservableObject editor, Control view) in firstViews)
        {
            Assert.Same(editor, view.DataContext);
            Assert.Null(BindingOperations.GetBindingExpressionBase(view, StyledElement.DataContextProperty));
        }

        Assert.Empty(contextChanges);
        AssertPassive(fixture);
    }

    [AvaloniaTheory]
    [InlineData(WorkflowStep.Input, SettingsCategory.Mapping)]
    [InlineData(WorkflowStep.Design, SettingsCategory.Evaluation)]
    [InlineData(WorkflowStep.Execution, SettingsCategory.Common)]
    [InlineData(WorkflowStep.Results, SettingsCategory.Common)]
    public async Task Settings_close_restores_the_actual_invoker_from_each_workflow_view(
        WorkflowStep step, SettingsCategory expectedCategory)
    {
        using ShellFixture fixture = await ShellFixture.CreateAsync();
        fixture.Show();
        NavigateInitially(fixture, step);
        Control originalView = CurrentView(fixture.Window);
        Button invoker = originalView switch
        {
            InputView input => Required<Button>(input, "MappingDetailsButton"),
            QuantificationDesignView design => Required<Button>(design, "OpenEvaluatorSettingsButton"),
            ExecutionView execution => Required<Button>(execution, "ChangeExecutionSettingsButton"),
            _ => Required<Button>(fixture.Window, "SettingsOpenButton"),
        };

        Activate(invoker, Key.Space);
        SettingsView settings = Assert.IsType<SettingsView>(CurrentView(fixture.Window));
        Assert.True(fixture.Shell.IsSettingsOpen);
        Assert.Equal(expectedCategory, fixture.Shell.Settings.SelectedCategory);
        Assert.Equal(step, fixture.Shell.CurrentStep);
        AssertOnlyCurrentEditor(fixture);
        Assert.Same(ById<Control>(settings, expectedCategory switch
        {
            SettingsCategory.Mapping => "MappingSettingsQuestions",
            SettingsCategory.Evaluation => "EvaluatorSettingsQuestions",
            _ => "ExecutionModel",
        }), fixture.Window.FocusManager?.GetFocusedElement());

        // A category change and the fifth (already-open Settings) entry must not
        // replace the original invoking control with the gear or a category button.
        Activate(ById<Button>(settings, "SettingsCategorySpecial"));
        Activate(Required<Button>(fixture.Window, "SettingsOpenButton"));
        Assert.Same(settings, CurrentView(fixture.Window));
        Assert.Equal(SettingsCategory.Special, fixture.Shell.Settings.SelectedCategory);
        Activate(Required<Button>(settings, "SettingsRequestClose"));

        Assert.False(fixture.Shell.IsSettingsOpen);
        Assert.Equal(step, fixture.Shell.CurrentStep);
        Assert.Same(originalView, CurrentView(fixture.Window));
        Assert.Same(invoker, fixture.Window.FocusManager?.GetFocusedElement());
        Assert.True(invoker.IsEffectivelyVisible);
        AssertOnlyCurrentEditor(fixture);
        AssertPassive(fixture);
    }

    [AvaloniaFact]
    public async Task Cached_shell_views_keep_pending_numeric_text_and_binding_instances()
    {
        using ShellFixture fixture = await ShellFixture.CreateAsync();
        fixture.Show();
        InputView input = Assert.IsType<InputView>(CurrentView(fixture.Window));
        TextBox row = Required<TextBox>(input, "FirstDataRowTextBox");
        var originalRow = fixture.Input.FirstDataRow;
        BindingExpressionBase rowBinding = Assert.IsAssignableFrom<BindingExpressionBase>(
            BindingOperations.GetBindingExpressionBase(row, TextBox.TextProperty));
        Edit(row, "-");
        Assert.True(DataValidationErrors.GetHasErrors(row));
        Assert.Equal(originalRow, fixture.Input.FirstDataRow);

        fixture.Shell.NextCommand.Execute(null);
        Render();
        QuantificationDesignView design = Assert.IsType<QuantificationDesignView>(CurrentView(fixture.Window));
        TextBox points = Required<TextBox>(design, "BasePointsTextBox");
        decimal originalPoints = fixture.Design.BasePoints;
        BindingExpressionBase pointsBinding = Assert.IsAssignableFrom<BindingExpressionBase>(
            BindingOperations.GetBindingExpressionBase(points, TextBox.TextProperty));
        Edit(points, "未確定の配点");
        Assert.True(DataValidationErrors.GetHasErrors(points));
        Assert.Equal(originalPoints, fixture.Design.BasePoints);

        Activate(Required<Button>(fixture.Window, "SettingsOpenButton"));
        SettingsView settings = Assert.IsType<SettingsView>(CurrentView(fixture.Window));
        TextBox rounding = ById<TextBox>(settings, "DesignRoundingDigits");
        int originalRounding = fixture.Design.RoundingDigits;
        BindingExpressionBase roundingBinding = Assert.IsAssignableFrom<BindingExpressionBase>(
            BindingOperations.GetBindingExpressionBase(rounding, TextBox.TextProperty));
        Edit(rounding, "未確定の桁数");
        Assert.True(DataValidationErrors.GetHasErrors(rounding));
        Activate(ById<Button>(settings, "SettingsCategoryEvaluation"));
        Control evaluation = Assert.IsAssignableFrom<Control>(Required<ContentControl>(settings, "CurrentSettingsContent").Content);
        EvaluatorDesignItemViewModel evaluator = fixture.Design.SelectedQuestion!.SelectedEvaluator!;
        TextBox weight = ById<TextBox>(settings, $"DesignEvaluator-{evaluator.Id}-Weight");
        decimal originalWeight = evaluator.Weight;
        BindingExpressionBase weightBinding = Assert.IsAssignableFrom<BindingExpressionBase>(
            BindingOperations.GetBindingExpressionBase(weight, TextBox.TextProperty));
        Edit(weight, "-");
        Assert.True(DataValidationErrors.GetHasErrors(weight));
        Activate(Required<Button>(settings, "SettingsRequestClose"));

        Assert.Same(design, CurrentView(fixture.Window));
        AssertPending(points, "未確定の配点", pointsBinding);
        fixture.Shell.NavigateCommand.Execute(WorkflowStep.Input);
        Render();
        Assert.Same(input, CurrentView(fixture.Window));
        AssertPending(row, "-", rowBinding);
        fixture.Shell.NavigateCommand.Execute(WorkflowStep.Design);
        Render();
        Assert.Same(design, CurrentView(fixture.Window));
        AssertPending(points, "未確定の配点", pointsBinding);

        Activate(Required<Button>(fixture.Window, "SettingsOpenButton"));
        Assert.Same(settings, CurrentView(fixture.Window));
        Assert.Equal(SettingsCategory.Evaluation, fixture.Shell.Settings.SelectedCategory);
        Assert.Same(evaluation, Required<ContentControl>(settings, "CurrentSettingsContent").Content);
        AssertPending(weight, "-", weightBinding);
        Activate(ById<Button>(settings, "SettingsCategoryCommon"));
        Assert.Same(rounding, ById<TextBox>(settings, "DesignRoundingDigits"));
        AssertPending(rounding, "未確定の桁数", roundingBinding);
        Assert.Equal(originalRow, fixture.Input.FirstDataRow);
        Assert.Equal(originalPoints, fixture.Design.BasePoints);
        Assert.Equal(originalRounding, fixture.Design.RoundingDigits);
        Assert.Equal(originalWeight, evaluator.Weight);
        AssertOnlyCurrentEditor(fixture);
        AssertPassive(fixture);
    }

    [AvaloniaFact]
    public async Task Cached_results_keep_pending_row_text_across_settings_roundtrips()
    {
        using ShellFixture fixture = await ShellFixture.CreateAsync();
        fixture.Results.Load(U04TestSupport.Context(await U04TestSupport.CreateSummaryAsync(
            fixture.Input.DefinitionDraft, fixture.Input.Metadata!), fixture.Workbook.Path));
        fixture.Show();
        NavigateInitially(fixture, WorkflowStep.Results);
        ResultsOutputView view = Assert.IsType<ResultsOutputView>(CurrentView(fixture.Window));
        TextBox target = Required<TextBox>(view, "GoToRowTextBox");
        Button jump = Required<Button>(view, "GoToRowButton");
        ResultsCriterionViewModel criterion = fixture.Results.Results[0];
        int selectedRow = fixture.Results.SelectedRow!.SourceRowNumber;
        int destination = fixture.Results.RowScores[^1].SourceRowNumber;
        string validText = destination.ToString(CultureInfo.InvariantCulture);
        Assert.NotEqual(selectedRow, destination);
        target.Text = validText;
        Render();
        Assert.Equal(destination, fixture.Results.GoToRowNumber);
        Assert.True(jump.IsEffectivelyEnabled);

        target.Text = "-";
        Assert.Null(fixture.Results.GoToRowNumber);
        Assert.False(fixture.Results.GoToRowCommand.CanExecute(null));
        Render();
        Assert.False(jump.IsEffectivelyEnabled);
        for (int roundtrip = 0; roundtrip < 2; roundtrip++)
        {
            Activate(Required<Button>(fixture.Window, "SettingsOpenButton"));
            SettingsView settings = Assert.IsType<SettingsView>(CurrentView(fixture.Window));
            Assert.Null(TopLevel.GetTopLevel(view));
            if (roundtrip == 1)
            {
                ResultsRowScoreViewModel previousScore = fixture.Results.RowScores[0];
                criterion.OverrideText = criterion.Range.Minimum.ToString(CultureInfo.InvariantCulture);
                Assert.NotSame(previousScore, fixture.Results.RowScores[0]);
                Assert.Equal(criterion.Range.Minimum, criterion.EffectiveRaw);
            }

            Activate(Required<Button>(settings, "SettingsRequestClose"));

            Assert.Same(view, CurrentView(fixture.Window));
            Assert.Same(fixture.Results, view.ViewModel);
            Assert.Same(target, Required<TextBox>(view, "GoToRowTextBox"));
            Assert.Same(criterion, fixture.Results.Results[0]);
            Assert.Equal("-", target.Text);
            Assert.Null(fixture.Results.GoToRowNumber);
            Assert.False(fixture.Results.GoToRowCommand.CanExecute(null));
            Assert.False(jump.IsEffectivelyEnabled);
        }

        Assert.True(target.Focus(NavigationMethod.Tab));
        Press(fixture.Window, Key.Enter);
        Assert.Equal("-", target.Text);
        Assert.Equal(selectedRow, fixture.Results.SelectedRow?.SourceRowNumber);
        Assert.False(jump.IsEffectivelyEnabled);
        target.Text = validText;
        Render();
        Assert.Equal(destination, fixture.Results.GoToRowNumber);
        Assert.True(fixture.Results.GoToRowCommand.CanExecute(null));
        Assert.True(jump.IsEffectivelyEnabled);
        Activate(jump);
        Assert.Equal(destination, fixture.Results.SelectedRow?.SourceRowNumber);
        AssertPassive(fixture);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Cached_results_resynchronize_row_text_for_new_sources(bool replaceViewModel, bool whileSettingsOpen)
    {
        using ShellFixture fixture = await ShellFixture.CreateAsync();
        ExecutionRunContext context = U04TestSupport.Context(await U04TestSupport.CreateSummaryAsync(
            fixture.Input.DefinitionDraft, fixture.Input.Metadata!), fixture.Workbook.Path);
        fixture.Results.Load(context);
        fixture.Show();
        NavigateInitially(fixture, WorkflowStep.Results);
        ResultsOutputView view = Assert.IsType<ResultsOutputView>(CurrentView(fixture.Window));
        TextBox target = Required<TextBox>(view, "GoToRowTextBox");
        Button jump = Required<Button>(view, "GoToRowButton");
        ResultsCriterionViewModel previousResult = fixture.Results.Results[0];
        target.Text = "-";
        Render();
        Assert.Null(fixture.Results.GoToRowNumber);
        if (whileSettingsOpen)
        {
            Activate(Required<Button>(fixture.Window, "SettingsOpenButton"));
            Assert.IsType<SettingsView>(CurrentView(fixture.Window));
            Assert.Null(TopLevel.GetTopLevel(view));
        }

        using ResultsOutputViewModel? replacement = replaceViewModel ? new(fixture.Output, context) : null;
        ResultsOutputViewModel current = replacement ?? fixture.Results;
        if (replacement is not null)
        {
            view.DataContext = replacement;
        }
        else
        {
            // Even reloading the identical context resets presentation; display metadata is not a run token.
            current.Load(context);
        }

        if (whileSettingsOpen)
        {
            Activate(Required<Button>(CurrentView(fixture.Window), "SettingsRequestClose"));
        }

        Render();
        Assert.Same(view, CurrentView(fixture.Window));
        Assert.Same(current, view.ViewModel);
        Assert.Same(target, Required<TextBox>(view, "GoToRowTextBox"));
        Assert.NotSame(previousResult, current.Results[0]);
        Assert.Null(current.GoToRowNumber);
        Assert.Equal(string.Empty, target.Text);
        Assert.False(current.GoToRowCommand.CanExecute(null));
        Assert.False(jump.IsEffectivelyEnabled);

        int destination = current.RowScores[^1].SourceRowNumber;
        string validText = destination.ToString(CultureInfo.InvariantCulture);
        current.GoToRowNumber = destination;
        Render();
        Assert.Equal(validText, target.Text);
        Assert.True(jump.IsEffectivelyEnabled);
        target.Text = "-";
        Activate(Required<Button>(fixture.Window, "SettingsOpenButton"));
        current.GoToRowNumber = destination; // Legitimate source updates while detached must still win.
        Activate(Required<Button>(CurrentView(fixture.Window), "SettingsRequestClose"));
        Assert.Same(view, CurrentView(fixture.Window));
        Assert.Equal(validText, target.Text);
        Assert.Equal(destination, current.GoToRowNumber);
        Assert.True(jump.IsEffectivelyEnabled);
        AssertPassive(fixture);
    }

    [AvaloniaFact]
    public async Task Shell_tab_order_crosses_navigation_content_and_footer_in_both_directions()
    {
        using ShellFixture fixture = await ShellFixture.CreateAsync();
        fixture.Show();
        NavigateInitially(fixture, WorkflowStep.Results); // Visit, not run or success.
        fixture.Shell.NavigateCommand.Execute(WorkflowStep.Input);
        Render();
        MainWindow window = fixture.Window;
        Button input = Required<Button>(window, "InputStepButton");
        Assert.True(input.Focus(NavigationMethod.Tab));
        foreach (string name in StepButtonNames.Skip(1).Append("SettingsOpenButton"))
        {
            Press(window, Key.Tab);
            Assert.Same(Required<Button>(window, name), window.FocusManager?.GetFocusedElement());
        }

        Press(window, Key.Tab);
        Assert.Same(Required<TextBox>(CurrentView(window), "FilePathTextBox"), window.FocusManager?.GetFocusedElement());
        Press(window, Key.Tab, RawInputModifiers.Shift);
        Assert.Same(Required<Button>(window, "SettingsOpenButton"), window.FocusManager?.GetFocusedElement());
        Press(window, Key.Tab, RawInputModifiers.Shift);
        Assert.Same(Required<Button>(window, "ResultsStepButton"), window.FocusManager?.GetFocusedElement());

        Activate(Required<Button>(window, "DesignStepButton"));
        QuantificationDesignView design = Assert.IsType<QuantificationDesignView>(CurrentView(window));
        Assert.Same(Required<TextBox>(design, "BasePointsTextBox"), window.FocusManager?.GetFocusedElement());
        Button lastEditorAction = Required<Button>(design, "ValidateDesignButton");
        Assert.True(lastEditorAction.Focus(NavigationMethod.Tab));
        Press(window, Key.Tab);
        Assert.Same(Required<Button>(window, "PreviousStepButton"), window.FocusManager?.GetFocusedElement());
        Press(window, Key.Tab);
        Assert.Same(Required<Button>(window, "NextStepButton"), window.FocusManager?.GetFocusedElement());
        Press(window, Key.Tab, RawInputModifiers.Shift);
        Assert.Same(Required<Button>(window, "PreviousStepButton"), window.FocusManager?.GetFocusedElement());
        Press(window, Key.Tab, RawInputModifiers.Shift);
        Assert.Same(lastEditorAction, window.FocusManager?.GetFocusedElement());
        Assert.All(StepButtonNames.Select(name => Required<Button>(window, name)), button =>
            Assert.DoesNotContain("completed", button.Classes));
        AssertPassive(fixture);
    }

    [AvaloniaFact]
    public async Task Go_to_problem_focus_wins_over_queued_shell_initial_focus()
    {
        using ShellFixture fixture = await ShellFixture.CreateAsync();
        InputQuestionMappingViewModel question = fixture.Input.SelectedQuestion!;
        question.QuestionText = string.Empty;
        fixture.Show();
        Activate(Required<Button>(fixture.Window, "DesignStepButton"));
        QuantificationDesignView design = Assert.IsType<QuantificationDesignView>(CurrentView(fixture.Window));
        DesignValidationError error = Assert.Single(fixture.Design.ValidationErrors,
            item => item.NodeId == question.Id && item.Field == "QuestionText");
        Required<ComboBox>(design, "ValidationErrorSelector").SetCurrentValue(ComboBox.SelectedItemProperty, error);
        Render();

        Activate(Required<Button>(design, "GoToProblemButton"));
        Render(); // Drain late shell/default-focus work too.

        InputView input = Assert.IsType<InputView>(CurrentView(fixture.Window));
        Assert.Equal(WorkflowStep.Input, fixture.Shell.CurrentStep);
        Assert.Same(Required<TextBox>(input, "QuestionTextEditor"), fixture.Window.FocusManager?.GetFocusedElement());
        Assert.Same(question, fixture.Input.SelectedQuestion);
        AssertPassive(fixture);
    }

    [AvaloniaTheory]
    [InlineData(WorkflowStep.Input, false)]
    [InlineData(WorkflowStep.Design, false)]
    [InlineData(WorkflowStep.Execution, false)]
    [InlineData(WorkflowStep.Results, false)]
    [InlineData(WorkflowStep.Execution, true)]
    public async Task Active_run_progress_and_stop_are_fixed_in_all_five_views(WorkflowStep step, bool settingsOpen)
    {
        using ShellFixture fixture = await ShellFixture.CreateAsync();
        fixture.Window.Width = 1024;
        fixture.Window.Height = 720;
        fixture.Show();
        NavigateInitially(fixture, WorkflowStep.Execution);
        TaskCompletionSource<RunSummary> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<EvaluationProgress>? report = null;
        CancellationToken runToken = default;
        fixture.RunHandler = (_, progress, token) =>
        {
            report = progress;
            runToken = token;
            progress?.Invoke(new EvaluationProgress(7, 3, 1, EvaluationProgressStatus.Running,
                DurableEvaluationStage.EvaluatingRows));
            return release.Task; // Test controls completion separately from requesting stop.
        };
        await fixture.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Task run = fixture.Execution.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(fixture.Execution.IsRunning);
            Assert.False(run.IsCompleted);
            QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(fixture.Runner.LastRequest);
            fixture.Shell.NavigateCommand.Execute(step);
            if (settingsOpen)
            {
                fixture.Shell.OpenSettings();
            }

            Render();
            Assert.Equal(step, fixture.Shell.CurrentStep);
            Assert.Equal(settingsOpen, fixture.Shell.IsSettingsOpen);
            AssertOnlyCurrentEditor(fixture);
            Grid strip = Required<Grid>(fixture.Window, "ShellCurrentRun");
            Button stop = Required<Button>(fixture.Window, "ShellCancelRunButton");
            Button progressLink = Required<Button>(fixture.Window, "ShellOpenRunButton");
            Assert.True(strip.IsEffectivelyVisible);
            Assert.Same(fixture.Execution.CancelCommand, stop.Command);
            Assert.Equal("ShellCancelQuantification", AutomationProperties.GetAutomationId(stop));
            Assert.True(stop.IsEffectivelyEnabled);
            Assert.False(stop.IsCancel);
            Assert.False(stop.IsDefault);
            Assert.Same(fixture.Shell.NavigateCommand, progressLink.Command);
            Assert.Equal(WorkflowStep.Execution, progressLink.CommandParameter);
            Assert.Contains("今回run", Required<TextBlock>(fixture.Window, "ShellRunStage").Text!);

            Assert.NotNull(report);
            report(new EvaluationProgress(7, 4, 1, EvaluationProgressStatus.Running,
                DurableEvaluationStage.SavingCheckpoint));
            Render();
            Assert.Equal(fixture.Execution.ProgressText, Required<TextBlock>(fixture.Window, "ShellRunProgressText").Text);
            Assert.Equal(4d, Required<ProgressBar>(fixture.Window, "ShellRunProgressBar").Value);
            Assert.Equal(7d, Required<ProgressBar>(fixture.Window, "ShellRunProgressBar").Maximum);
            AssertNormalBody(fixture.Window);
            foreach (Button action in new[] { progressLink, stop })
            {
                AssertFixedButton(action, fixture.Window);
            }

            if (settingsOpen)
            {
                Control settings = CurrentView(fixture.Window);
                Activate(progressLink);
                Assert.False(fixture.Shell.IsSettingsOpen);
                Assert.IsType<ExecutionView>(CurrentView(fixture.Window));
                Assert.Same(request, fixture.Runner.LastRequest);
                Assert.Equal(1, fixture.Runner.CallCount);
                Assert.Equal(4, fixture.Execution.ProgressCompleted);
                Activate(Required<Button>(fixture.Window, "SettingsOpenButton"));
                Assert.Same(settings, CurrentView(fixture.Window));
            }

            Activate(stop, Key.Space);
            Assert.True(runToken.IsCancellationRequested);
            Assert.True(fixture.Execution.IsCancelling);
            Assert.True(strip.IsEffectivelyVisible);
            Assert.False(stop.IsEffectivelyEnabled);
            Assert.Same(request, fixture.Runner.LastRequest);
            AssertPassive(fixture, authenticationChecks: 1, runs: 1);
            release.TrySetCanceled(runToken);
            await run.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
            Render();
            Assert.False(strip.IsEffectivelyVisible);
            Assert.Equal(settingsOpen, fixture.Shell.IsSettingsOpen);
        }
        finally
        {
            release.TrySetCanceled();
            await run.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task Settings_return_falls_back_when_the_invoking_control_is_disabled()
    {
        using ShellFixture fixture = await ShellFixture.CreateAsync();
        fixture.Show();
        InputView input = Assert.IsType<InputView>(CurrentView(fixture.Window));
        Button invoker = Required<Button>(input, "MappingDetailsButton");
        Activate(invoker);
        SettingsView settings = Assert.IsType<SettingsView>(CurrentView(fixture.Window));
        invoker.IsEnabled = false;
        Activate(Required<Button>(settings, "SettingsRequestClose"));

        Assert.Same(input, CurrentView(fixture.Window));
        Assert.Same(Required<TextBox>(input, "FilePathTextBox"), fixture.Window.FocusManager?.GetFocusedElement());
        AssertPassive(fixture);
    }

    [AvaloniaFact]
    public async Task Rapid_navigation_and_close_cancel_pending_focus_and_unsubscribe()
    {
        using ShellFixture fixture = await ShellFixture.CreateAsync();
        fixture.Show();
        FieldInfo handlers = typeof(MainWindowViewModel).GetField(nameof(MainWindowViewModel.PropertyChanged),
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Contains(((Delegate)handlers.GetValue(fixture.Shell)!).GetInvocationList(),
            handler => ReferenceEquals(handler.Target, fixture.Window));

        fixture.Shell.NextCommand.Execute(null);
        fixture.Shell.OpenSettings();
        fixture.Shell.NavigateCommand.Execute(WorkflowStep.Input);
        Render();
        Assert.IsType<InputView>(CurrentView(fixture.Window));
        Assert.Same(Required<TextBox>(CurrentView(fixture.Window), "FilePathTextBox"), fixture.Window.FocusManager?.GetFocusedElement());
        AssertOnlyCurrentEditor(fixture);
        AssertPassive(fixture);

        fixture.Shell.NextCommand.Execute(null); // Leave an initial-focus callback queued.
        fixture.Window.Close();
        TextBox unrelatedTarget = new();
        Window unrelated = new() { Content = unrelatedTarget, Width = 300, Height = 100 };
        try
        {
            unrelated.Show();
            Assert.True(unrelatedTarget.Focus(NavigationMethod.Tab));
            Render();
            Assert.Same(unrelatedTarget, unrelated.FocusManager?.GetFocusedElement());
            Assert.Null(Required<ContentControl>(fixture.Window, "CurrentStepContent").Content);
            Assert.DoesNotContain((handlers.GetValue(fixture.Shell) as Delegate)?.GetInvocationList() ?? [],
                handler => ReferenceEquals(handler.Target, fixture.Window));
            Assert.False(fixture.Shell.OpenSettingsCommand.CanExecute(null));
        }
        finally
        {
            unrelated.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task Narrow_scaled_shell_scrolls_only_body_and_keeps_fixed_regions(double scale)
    {
        using ShellFixture fixture = await ShellFixture.CreateAsync();
        // Exception fixture only; the product's minimum dimensions remain unchanged.
        fixture.Window.MinWidth = 0;
        fixture.Window.MinHeight = 0;
        fixture.Window.Width = 760;
        fixture.Window.Height = 600;
        fixture.Show();
        fixture.Window.SetRenderScaling(scale);
        Render();
        Assert.Equal(scale, fixture.Window.RenderScaling);
        Assert.Equal(new Size(760, 600), fixture.Window.ClientSize);
        ScrollViewer body = Required<ScrollViewer>(fixture.Window, "ShellScrollViewer");
        Assert.True(body.Extent.Height > body.Viewport.Height);
        Assert.True(body.Extent.Width <= body.Viewport.Width + 1d);
        Point warningPosition = Required<Border>(fixture.Window, "EthicsWarningBanner").TranslatePoint(default, fixture.Window)!.Value;
        Point footerPosition = Required<Grid>(fixture.Window, "ShellFooter").TranslatePoint(default, fixture.Window)!.Value;
        body.ScrollToEnd();
        Render();
        Assert.True(body.Offset.Y > 0d);
        AssertFixedRegions(fixture.Window);
        Assert.Equal(warningPosition, Required<Border>(fixture.Window, "EthicsWarningBanner").TranslatePoint(default, fixture.Window)!.Value);
        Assert.Equal(footerPosition, Required<Grid>(fixture.Window, "ShellFooter").TranslatePoint(default, fixture.Window)!.Value);
        AssertFullyInside(Required<Border>(CurrentView(fixture.Window), "InputValidationSummary"), body);

        Activate(Required<Button>(fixture.Window, "SettingsOpenButton"));
        SettingsView settings = Assert.IsType<SettingsView>(CurrentView(fixture.Window));
        body.ScrollToEnd();
        Render();
        Assert.True(body.Offset.Y > 0d);
        Button back = Required<Button>(settings, "SettingsRequestClose");
        AssertFullyInside(back, body);
        AssertFixedRegions(fixture.Window);
        Activate(back);
        Assert.Same(Required<Button>(fixture.Window, "SettingsOpenButton"), fixture.Window.FocusManager?.GetFocusedElement());
        AssertPassive(fixture);
    }

    private static void AssertNormalBody(MainWindow window)
    {
        AssertFixedRegions(window);
        ScrollViewer body = Required<ScrollViewer>(window, "ShellScrollViewer");
        Assert.Equal(ScrollBarVisibility.Disabled, body.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Auto, body.VerticalScrollBarVisibility);
        Assert.True(body.Extent.Width <= body.Viewport.Width + 1d);
        Assert.True(body.Extent.Height <= body.Viewport.Height + 1d);
        Assert.Equal(default, body.Offset);
        ContentControl content = Required<ContentControl>(window, "CurrentStepContent");
        Assert.True(double.IsFinite(content.Bounds.Height));
        Assert.InRange(content.Bounds.Height, 450d, window.ClientSize.Height - 1d);
        AssertFullyInside(content, body);
        AssertFullyInside(CurrentView(window), content);
        body.ScrollToEnd();
        Render();
        Assert.Equal(default, body.Offset);
    }

    private static void AssertFixedRegions(MainWindow window)
    {
        Grid header = Required<Grid>(window, "ShellHeader");
        Grid footer = Required<Grid>(window, "ShellFooter");
        Border warning = Required<Border>(window, "EthicsWarningBanner");
        ScrollViewer body = Required<ScrollViewer>(window, "ShellScrollViewer");
        Assert.True(warning.IsEffectivelyVisible);
        Assert.Equal(ExpectedWarning, Required<TextBlock>(window, "EthicsWarningMessage").Text);
        Assert.False(warning.Focusable);
        Assert.False(warning.IsTabStop);
        Assert.False(warning.IsHitTestVisible);
        AssertFullyInside(Required<TextBlock>(window, "EthicsWarningMessage"), warning);
        foreach (Control fixedRegion in new Control[] { header, footer, warning })
        {
            AssertFullyInside(fixedRegion, window);
            Assert.DoesNotContain(fixedRegion.GetVisualAncestors(), ancestor => ancestor is ScrollViewer);
        }

        Point headerPosition = header.TranslatePoint(default, window)!.Value;
        Point bodyPosition = body.TranslatePoint(default, window)!.Value;
        Point footerPosition = footer.TranslatePoint(default, window)!.Value;
        Assert.True(headerPosition.Y + header.Bounds.Height <= bodyPosition.Y);
        Assert.True(bodyPosition.Y + body.Bounds.Height <= footerPosition.Y);
        Assert.True(footerPosition.Y + footer.Bounds.Height <= window.ClientSize.Height + 1d);
        foreach (string name in StepButtonNames.Concat(new[] { "SettingsOpenButton", "PreviousStepButton", "NextStepButton" }))
        {
            Button button = Required<Button>(window, name);
            if (button.IsEffectivelyVisible)
            {
                AssertFixedButton(button, window);
            }
        }
    }

    private static void AssertFixedButton(Button button, MainWindow window)
    {
        Assert.True(button.IsEffectivelyVisible, button.Name);
        Assert.True(button.MinWidth >= 44d, button.Name);
        Assert.True(button.MinHeight >= 44d, button.Name);
        Assert.True(button.Bounds.Width >= 44d, button.Name);
        Assert.True(button.Bounds.Height >= 44d, button.Name);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), button.Name);
        AssertFullyInside(button, window);
        Assert.DoesNotContain(button.GetVisualAncestors(), ancestor => ancestor is ScrollViewer);
    }

    private static void AssertMainControlsInside(Control view)
    {
        string[] names = view switch
        {
            InputView => ["FilePathTextBox", "PickFileButton", "LoadFileButton", "WorksheetComboBox", "HeaderRowComboBox",
                "FirstDataRowTextBox", "LastDataRowTextBox", "QuestionTextEditor", "InputValidationSummary"],
            QuantificationDesignView => ["BasePointsTextBox", "SelectedQuestionPointsTextBox", "FormulaGuide", "DesignValidationSummary"],
            ExecutionView => ["ExecutionSettingsSummary", "ExecutionActions", "EffectiveOutputDirectoryTextBox"],
            ResultsOutputView => ["RowScoreList", "GoToRowButton", "OutputPathTextBox", "ExportButton"],
            _ => throw new InvalidOperationException("A main workflow view is required."),
        };
        foreach (string name in names)
        {
            Control control = view.FindControl<Control>(name)!;
            Assert.NotNull(control);
            Assert.True(control.IsEffectivelyVisible, name);
            Assert.True(control.Bounds.Height > 0d, name);
            AssertFullyInside(control, view);
        }
    }

    private static void AssertOnlyCurrentEditor(ShellFixture fixture)
    {
        Control[] controls = fixture.Window.GetVisualDescendants().OfType<Control>()
            .Concat(fixture.Window.GetLogicalDescendants().OfType<Control>()).Distinct().ToArray();
        Control current = Assert.Single(controls, control => control is InputView or QuantificationDesignView
            or ExecutionView or ResultsOutputView or SettingsView);
        Assert.Same(CurrentView(fixture.Window), current);
        Assert.Same(fixture.Shell.CurrentEditorViewModel, current.DataContext);
        Assert.True(current.IsSet(StyledElement.DataContextProperty));
        Assert.Null(BindingOperations.GetBindingExpressionBase(current, StyledElement.DataContextProperty));
        string[] ids = controls.Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static void AssertFullyInside(Control control, Control container)
    {
        Point point = control.TranslatePoint(default, container)
            ?? throw new InvalidOperationException("Both controls must be attached to the same view.");
        string label = control.Name ?? AutomationProperties.GetAutomationId(control) ?? control.GetType().Name;
        Assert.True(point.X >= -1d && point.Y >= -1d, label);
        Assert.True(point.X + control.Bounds.Width <= container.Bounds.Width + 1d, label);
        Assert.True(point.Y + control.Bounds.Height <= container.Bounds.Height + 1d, label);
    }

    private static void AssertPending(TextBox editor, string expected, BindingExpressionBase binding)
    {
        Assert.Equal(expected, editor.Text);
        Assert.Same(binding, BindingOperations.GetBindingExpressionBase(editor, TextBox.TextProperty));
        Assert.True(DataValidationErrors.GetHasErrors(editor));
    }

    private static void AssertPassive(ShellFixture fixture, int authenticationChecks = 0, int runs = 0)
    {
        Assert.Equal(authenticationChecks, fixture.Authentication.CallCount);
        Assert.Equal(runs, fixture.Runner.CallCount);
        Assert.Null(fixture.Execution.LastLoginTask);
        Assert.Null(fixture.Shell.Settings.LastLoadTask);
        Assert.Null(fixture.Shell.Settings.LastSaveTask);
        Assert.Null(fixture.Shell.Settings.LastApplySavedDefinitionTask);
        Assert.Null(fixture.Output.LastRequest);
    }

    private static void NavigateInitially(ShellFixture fixture, WorkflowStep target)
    {
        while (fixture.Shell.CurrentStep != target)
        {
            Assert.True(fixture.Shell.NextCommand.CanExecute(null));
            fixture.Shell.NextCommand.Execute(null);
            Render();
        }
    }

    private static Control CurrentView(MainWindow window) =>
        Assert.IsAssignableFrom<Control>(Required<ContentControl>(window, "CurrentStepContent").Content);

    private static T Required<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(root.FindControl<T>(name));

    private static T ById<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    private static void Edit(TextBox editor, string text)
    {
        Assert.True(editor.IsEffectivelyEnabled);
        Assert.NotNull(BindingOperations.GetBindingExpressionBase(editor, TextBox.TextProperty));
        editor.SetCurrentValue(TextBox.TextProperty, text);
        Render();
    }

    private static void Activate(Button button, Key key = Key.Enter)
    {
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.True(button.Focus(NavigationMethod.Tab));
        Press(Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(button)), key);
    }

    private static void Press(TopLevel window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physical = key switch
        {
            Key.Tab => PhysicalKey.Tab,
            Key.Enter => PhysicalKey.Enter,
            Key.Space => PhysicalKey.Space,
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

    private sealed class ShellFixture : IDisposable
    {
        private ShellFixture(X02TemporaryWorkbook workbook, InputViewModel input)
        {
            Workbook = workbook;
            Input = input;
            Design = new QuantificationDesignViewModel(input.DefinitionDraft);
            Runner = new RecordingRunBoundary((request, progress, token) => RunHandler(request, progress, token));
            Execution = new ExecutionViewModel(Authentication, Runner);
            Results = new ResultsOutputViewModel(Output);
            Shell = new MainWindowViewModel(new WorkflowNavigator(), Input, Design, Execution, Results);
            Window = new MainWindow(Shell);
        }

        public X02TemporaryWorkbook Workbook { get; }
        public InputViewModel Input { get; }
        public QuantificationDesignViewModel Design { get; }
        public RecordingAuthenticationBoundary Authentication { get; } = new(
            new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.Available,
                [U04TestSupport.Model("model-test"), U04TestSupport.Model("auto")], U04TestSupport.RuntimeIdentity()));
        public RecordingRunBoundary Runner { get; }
        public RecordingOutputBoundary Output { get; } = new();
        public ExecutionViewModel Execution { get; }
        public ResultsOutputViewModel Results { get; }
        public MainWindowViewModel Shell { get; }
        public MainWindow Window { get; }
        public Func<QuantificationRunRequest, Action<EvaluationProgress>?, CancellationToken, Task<RunSummary>> RunHandler { get; set; } =
            (_, _, _) => throw new InvalidOperationException("No run was requested by this shell test.");

        public static async Task<ShellFixture> CreateAsync()
        {
            X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
                "Original", 1, 3, 2, new X02Header(1, "Answer"), new X02Header(2, "Rationale"));
            try
            {
                InputViewModel input = new();
                await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
                return new ShellFixture(workbook, input);
            }
            catch
            {
                workbook.Dispose();
                throw;
            }
        }

        public void Show()
        {
            Window.Show();
            Render();
        }

        public void Dispose()
        {
            Window.Close();
            Shell.Dispose();
            Workbook.Dispose();
        }
    }
}