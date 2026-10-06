using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: NFR-A11Y-001 (AC-078)
public sealed class PrimaryJourneyAccessibilityTests
{
    [AvaloniaFact]
    public async Task Execution_actual_values_and_explicit_actions_are_keyboard_accessible_at_two_hundred_percent()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RecordingAuthenticationBoundary authentication = new(new ExecutionAuthenticationSnapshot(
            ExecutionAuthenticationState.Available,
            [U04TestSupport.Model("model-test"), U04TestSupport.Model("auto")], U04TestSupport.RuntimeIdentity()));
        RecordingRunBoundary runner = new((_, _, _) => throw new InvalidOperationException("No run was requested."));
        using ExecutionViewModel viewModel = new(authentication, runner);
        viewModel.Configure(definition, metadata, Path.Combine(Path.GetTempPath(), "synthetic-T25-input.xlsx"));
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        ExecutionView view = new(viewModel);
        ExecutionPreparationPanel prep = new() { DataContext = viewModel };
        Window window = new()
        {
            Width = 1080,
            Height = 760,
            Content = view,
        };
        Window prepWindow = new()
        {
            Width = 540,
            Height = 760,
            Content = prep,
        };

        try
        {
            window.Show();
            prepWindow.Show();
            window.SetRenderScaling(2d);
            prepWindow.SetRenderScaling(2d);
            Render();

            Button auth = Required<Button>(prep, "CheckAuthenticationButton");
            Button login = Required<Button>(prep, "StartCopilotLogin");
            Button changeSettings = Required<Button>(view, "ChangeExecutionSettingsButton");
            TextBox model = Required<TextBox>(view, "EffectiveModelTextBox");
            TextBlock concurrency = Required<TextBlock>(view, "ConcurrencySummary");
            CheckBox resumeMode = Required<CheckBox>(prep, "ResumeModeCheckBox");
            TextBox resumePath = Required<TextBox>(prep, "ResumePartialPathTextBox");
            TextBox outputDirectory = Required<TextBox>(view, "EffectiveOutputDirectoryTextBox");
            Button start = Required<Button>(view, "StartRunButton");
            Button cancel = Required<Button>(view, "CancelRunButton");
            ProgressBar progress = Required<ProgressBar>(view, "RunProgressBar");
            Border validation = Required<Border>(prep, "ExecutionValidationSummary");
            Assert.Null(view.FindControl<Border>("ExecutionValidationSummary"));

            Assert.Same(viewModel, view.DataContext);
            Assert.Equal(2d, window.RenderScaling);
            foreach ((Control owner, string name) in new (Control, string)[] { (prep, "ExecutionAuthenticationScroll"), (view, "ExecutionProgressScroll") })
            {
                ScrollViewer scroll = Required<ScrollViewer>(owner, name);
                Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
                Assert.True(double.IsFinite(scroll.Viewport.Height));
                Assert.True(scroll.Viewport.Height > 0d);
            }

            Assert.Equal("CheckCopilotAuthentication", AutomationProperties.GetAutomationId(auth));
            Assert.Equal("StartCopilotLogin", AutomationProperties.GetAutomationId(login));
            Assert.Equal("ExecutionChangeSettings", AutomationProperties.GetAutomationId(changeSettings));
            Assert.Equal("ExecutionEffectiveModel", AutomationProperties.GetAutomationId(model));
            Assert.Equal("ExecutionConcurrencySummary", AutomationProperties.GetAutomationId(concurrency));
            Assert.Equal("ExecutionResumeMode", AutomationProperties.GetAutomationId(resumeMode));
            Assert.Equal("ExecutionResumePartialPath", AutomationProperties.GetAutomationId(resumePath));
            Assert.Equal("ExecutionEffectiveOutputDirectory", AutomationProperties.GetAutomationId(outputDirectory));
            Assert.Equal("StartQuantification", AutomationProperties.GetAutomationId(start));
            Assert.Equal("CancelQuantification", AutomationProperties.GetAutomationId(cancel));
            Assert.Equal("ExecutionProgress", AutomationProperties.GetAutomationId(progress));
            Assert.Equal("ExecutionValidationSummary", AutomationProperties.GetAutomationId(validation));
            Assert.All(
                new Control[] { auth, login, changeSettings, model, resumeMode, resumePath, outputDirectory, start, cancel, progress },
                control => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control))));
            Assert.All(new Control[] { auth, login, changeSettings, model, resumeMode, outputDirectory, start, cancel },
                AssertKeyboardTarget);
            Assert.Same(viewModel.CheckAuthenticationCommand, auth.Command);
            Assert.Same(viewModel.LoginCommand, login.Command);
            Assert.Same(viewModel.StartCommand, start.Command);
            Assert.Same(viewModel.CancelCommand, cancel.Command);
            Assert.True(model.IsReadOnly);
            Assert.Equal(FormattableString.Invariant($"次回 model-test 並列{viewModel.MaxConcurrency} · effort: 未指定（model非対応またはauto） · Context Size: 64K (Default) · 希望: 未指定 · 上限: 64,000 tokens"), model.Text);
            Assert.Equal(FormattableString.Invariant($"次回並列\n{viewModel.MaxConcurrency} 件"), concurrency.Text);
            Assert.True(outputDirectory.IsReadOnly);
            Assert.Equal(viewModel.OutputDirectory, outputDirectory.Text);
            // These editable IDs now belong to Settings/Common, not read-only substitutes here.
            Assert.DoesNotContain(AllControls(view), control => AutomationProperties.GetAutomationId(control)
                is "ExecutionModel" or "ExecutionConcurrency" or "ExecutionOutputDirectory");
            Assert.True(start.IsEffectivelyEnabled);
            Assert.False(cancel.IsEffectivelyEnabled);
            Assert.False(resumePath.IsEffectivelyVisible);
            Assert.Null(view.FindControl<Border>("EthicsWarningBanner"));
            AssertUniqueAutomationIds(view);
            AssertUniqueAutomationIds(prep);

            Assert.True(start.Focus(NavigationMethod.Tab));
            Assert.Equal(new Thickness(3d), start.BorderThickness);
            Assert.True(auth.Focus(NavigationMethod.Tab));
            Press(prepWindow, Key.Tab);
            Assert.Same(login, prepWindow.FocusManager?.GetFocusedElement());
            Press(prepWindow, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(auth, prepWindow.FocusManager?.GetFocusedElement());
            Press(prepWindow, Key.Tab);
            Press(prepWindow, Key.Tab); // Disabled login cancellation is not a Tab stop.
            Assert.Same(resumeMode, prepWindow.FocusManager?.GetFocusedElement());
            Press(prepWindow, Key.Space);
            Assert.True(viewModel.IsResumeMode);
            Assert.True(resumePath.IsEffectivelyVisible);
            AssertKeyboardTarget(resumePath);
            Press(prepWindow, Key.Tab);
            Assert.Same(resumePath, prepWindow.FocusManager?.GetFocusedElement());
            // Same path-then-picker order as the input step (InputView FilePath 0 → PickFile 1).
            Press(prepWindow, Key.Tab);
            Assert.Same(Required<Button>(prep, "PickResumeCheckpointButton"), prepWindow.FocusManager?.GetFocusedElement());
            Press(prepWindow, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(resumePath, prepWindow.FocusManager?.GetFocusedElement());
            Press(prepWindow, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(resumeMode, prepWindow.FocusManager?.GetFocusedElement());
            Press(prepWindow, Key.Space);
            Assert.False(viewModel.IsResumeMode);
            Assert.True(start.IsEffectivelyEnabled);

            foreach (TextBox actualValue in new[] { model, outputDirectory })
            {
                string? before = actualValue.Text;
                Assert.True(actualValue.Focus(NavigationMethod.Tab));
                actualValue.SelectAll();
                window.KeyTextInput("この画面では変更しない");
                Render();
                Assert.Equal(before, actualValue.Text);
                Assert.Equal(new Thickness(3d), actualValue.BorderThickness);
            }

            Assert.Equal(1, authentication.CallCount);
            Assert.Equal(0, runner.CallCount);
            Assert.Null(viewModel.LastLoginTask);
        }
        finally
        {
            window.Close();
            prepWindow.Close();
        }
    }

    [AvaloniaFact]
    public async Task Results_list_and_single_detail_are_keyboard_operable_and_warning_gate_free_at_two_hundred_percent()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        RecordingOutputBoundary output = new();
        using ResultsOutputViewModel viewModel = new(output, U04TestSupport.Context(summary));
        ResultsOutputView view = new(viewModel);
        Window window = new()
        {
            Width = 1280,
            Height = 850,
            Content = view,
        };

        try
        {
            window.Show();
            window.SetRenderScaling(2d);
            Render();

            ScrollViewer scroll = Required<ScrollViewer>(view, "ResultsOutputScrollViewer");
            ListBox results = Required<ListBox>(view, "ResultsList");
            ListBox rowScores = Required<ListBox>(view, "RowScoreList");
            TextBox outputPath = Required<TextBox>(view, "OutputPathTextBox");
            Button export = Required<Button>(view, "ExportButton");
            Button cancel = Required<Button>(view, "CancelExportButton");
            TextBox rowNumber = Required<TextBox>(view, "GoToRowTextBox");
            ResultsCriterionViewModel item = Assert.Single(viewModel.Results);

            Assert.Same(viewModel, view.DataContext);
            Assert.Equal(2d, window.RenderScaling);
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
            Assert.Equal("ResultsReviewList", AutomationProperties.GetAutomationId(results));
            Assert.Equal("ResultsRowScores", AutomationProperties.GetAutomationId(rowScores));
            Assert.Equal("ResultsOutputPath", AutomationProperties.GetAutomationId(outputPath));
            Assert.Equal("ExportQuantifiedWorkbook", AutomationProperties.GetAutomationId(export));
            Assert.Equal("CancelWorkbookOutput", AutomationProperties.GetAutomationId(cancel));
            Assert.True(view.GetVisualDescendants().OfType<VirtualizingStackPanel>().Any());
            Assert.Same(viewModel.VisibleRowScores, rowScores.ItemsSource);
            Assert.Same(viewModel.SelectedRowCriteria, results.ItemsSource);
            Assert.True(rowScores.IsEffectivelyVisible);
            Assert.False(results.IsEffectivelyVisible);
            Assert.Empty(OverrideEditors(view));
            Assert.True(viewModel.CanExport);
            Assert.True(export.IsEffectivelyEnabled);
            Assert.False(cancel.IsEffectivelyEnabled);
            ListBoxItem selectedRow = SelectedContainer(rowScores);
            Assert.Same(viewModel.SelectedRow, selectedRow.DataContext);
            // Results permits list-level entry focus before its virtualized row is realized.
            Assert.True(rowScores.Focusable);
            IInputElement? initialFocus = window.FocusManager?.GetFocusedElement();
            Assert.True(ReferenceEquals(selectedRow, initialFocus) || ReferenceEquals(rowScores, initialFocus),
                "Results entry must focus the selected row or its explicitly focusable list.");
            Assert.True(selectedRow.IsEffectivelyVisible);
            Assert.True(selectedRow.IsEffectivelyEnabled);
            AssertKeyboardTarget(selectedRow);
            Control rowSummary = Assert.Single(selectedRow.GetVisualDescendants().OfType<Control>(),
                control => AutomationProperties.GetAutomationId(control) == "ResultsRow-2");
            Assert.Equal("元のExcel 2 行の採点結果", AutomationProperties.GetName(rowSummary));
            AssertKeyboardTarget(outputPath);
            AssertKeyboardTarget(export);
            Assert.Null(view.FindControl<Border>("EthicsWarningBanner"));
            AssertUniqueAutomationIds(view);

            // Tab exits the list; Shift+Tab must restore its selected row, not the entry fallback.
            Press(window, Key.Tab);
            Assert.Same(rowNumber, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(selectedRow, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Enter);
            Assert.True(viewModel.IsDetailVisible);
            Assert.False(rowScores.IsEffectivelyVisible);
            Assert.True(results.IsEffectivelyVisible);
            Assert.Same(SelectedContainer(results), window.FocusManager?.GetFocusedElement());
            Assert.Same(item, viewModel.SelectedCriterion);
            Assert.Same(item, Required<ContentControl>(view, "CriterionEditor").Content);
            TextBox overrideEditor = Assert.Single(OverrideEditors(view));
            Assert.Same(item, overrideEditor.DataContext);
            Assert.Equal("Result-2-Q1-E1-C1-Override", AutomationProperties.GetAutomationId(overrideEditor));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(overrideEditor)));
            AssertKeyboardTarget(overrideEditor);
            Assert.True(overrideEditor.Focus(NavigationMethod.Tab, KeyModifiers.None));
            Assert.Same(overrideEditor, window.FocusManager?.GetFocusedElement());
            Assert.Equal(new Thickness(3d), overrideEditor.BorderThickness);
            Press(window, Key.Tab);
            Assert.Same(rowNumber, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(overrideEditor, window.FocusManager?.GetFocusedElement());
            Assert.True(outputPath.Focus(NavigationMethod.Tab));
            Press(window, Key.Tab);
            Assert.Same(export, window.FocusManager?.GetFocusedElement());
            Assert.Equal(new Thickness(3d), export.BorderThickness);
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(outputPath, window.FocusManager?.GetFocusedElement());
            Activate(window, Required<Button>(view, "ShowListButton"));
            Assert.Same(SelectedContainer(rowScores), window.FocusManager?.GetFocusedElement());
            Assert.Same(viewModel.RowScores[0], rowScores.SelectedItem);
            Assert.Empty(OverrideEditors(view));
            AssertUniqueAutomationIds(view);
            Assert.True(viewModel.CanExport);
            Assert.Equal(0, output.ExportCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Five_hundred_thirty_rows_page_and_select_by_keyboard_with_one_override_editor_and_no_lost_results()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 531);
        QuestionDefinition question = definition.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        definition = definition with
        {
            Questions = [question with
            {
                Evaluators = [evaluator with
                {
                    Criteria = evaluator.Criteria.Add(evaluator.Criteria[0] with { Id = "C2", DisplayName = "Criterion C2" }),
                }],
            }],
        };
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        RecordingOutputBoundary output = new();
        using ResultsOutputViewModel viewModel = new(output, U04TestSupport.Context(summary));
        ResultsCriterionViewModel[] originalCriteria = viewModel.Results.ToArray();
        string runIdentity = viewModel.RunIdentityText;
        ResultsOutputView view = new(viewModel);
        Window window = new()
        {
            Width = 1280,
            Height = 850,
            Content = view,
        };

        try
        {
            window.Show();
            Render();
            ListBox rows = Required<ListBox>(view, "RowScoreList");
            ListBox criteria = Required<ListBox>(view, "ResultsList");
            Button nextPage = Required<Button>(view, "NextPageButton");
            Button previousPage = Required<Button>(view, "PreviousPageButton");
            Assert.Same(viewModel.VisibleRowScores, rows.ItemsSource);
            Assert.Same(viewModel.SelectedRowCriteria, criteria.ItemsSource);
            Assert.Equal(530, viewModel.RowScores.Count);
            Assert.Equal(1060, viewModel.Results.Count);
            AssertBoundedResults(view, viewModel);

            Activate(window, nextPage);
            Assert.Equal(1, viewModel.PageIndex);
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(previousPage, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(SelectedContainer(rows), window.FocusManager?.GetFocusedElement());
            Assert.True(viewModel.VisibleRowScores.Count >= 2);
            Press(window, Key.Down);
            Assert.Same(viewModel.VisibleRowScores[1], viewModel.SelectedRow);
            Assert.Same(SelectedContainer(rows), window.FocusManager?.GetFocusedElement());
            int selectedRowNumber = viewModel.SelectedRow!.SourceRowNumber;
            AssertBoundedResults(view, viewModel);
            Press(window, Key.Enter);
            Assert.True(viewModel.IsDetailVisible);
            Assert.Same(SelectedContainer(criteria), window.FocusManager?.GetFocusedElement());
            Press(window, Key.Down);
            ResultsCriterionViewModel selected = viewModel.SelectedRowCriteria[1];
            Assert.Same(selected, viewModel.SelectedCriterion);
            Assert.Same(selected, SelectedContainer(criteria).DataContext);
            Assert.Same(SelectedContainer(criteria), window.FocusManager?.GetFocusedElement());
            AssertBoundedResults(view, viewModel);
            TextBox editor = Assert.Single(OverrideEditors(view));
            string overrideId = FormattableString.Invariant($"Result-{selectedRowNumber}-Q1-E1-C2-Override");
            Assert.Equal(overrideId, AutomationProperties.GetAutomationId(editor));
            Assert.True(editor.Focus(NavigationMethod.Tab));
            editor.SelectAll();
            window.KeyTextInput("0");
            Render();
            Assert.Equal("0", selected.OverrideText);
            Assert.Equal(0m, selected.EffectiveRaw);
            Assert.Equal(5m, selected.AiRawScore);
            Assert.True(viewModel.HasUnsavedOverrides);
            Assert.True(viewModel.CanExport);
            Assert.Same(editor, Assert.Single(OverrideEditors(view)));
            Assert.Equal(overrideId, AutomationProperties.GetAutomationId(editor));
            Assert.Same(editor, window.FocusManager?.GetFocusedElement());

            Activate(window, Required<Button>(view, "ShowListButton"));
            Assert.Equal(selectedRowNumber, viewModel.SelectedRow?.SourceRowNumber);
            Assert.Same(selected, viewModel.SelectedCriterion);
            Assert.Same(SelectedContainer(rows), window.FocusManager?.GetFocusedElement());
            AssertBoundedResults(view, viewModel);
            Activate(window, nextPage);
            Assert.Equal(2, viewModel.PageIndex);
            AssertBoundedResults(view, viewModel);
            JumpToRow(window, view, selectedRowNumber);
            Activate(window, Required<Button>(view, "ShowDetailButton"));
            Press(window, Key.Down);
            Assert.Same(selected, viewModel.SelectedCriterion);
            Assert.Equal("0", Assert.Single(OverrideEditors(view)).Text);
            Assert.Equal(overrideId, AutomationProperties.GetAutomationId(Assert.Single(OverrideEditors(view))));
            AssertBoundedResults(view, viewModel);

            Activate(window, Required<Button>(view, "ShowListButton"));
            JumpToRow(window, view, 531);
            Assert.Equal((530 - 1) / viewModel.PageSize, viewModel.PageIndex);
            Assert.Same(viewModel.RowScores[^1], viewModel.SelectedRow);
            Assert.False(nextPage.IsEffectivelyEnabled);
            Assert.True(previousPage.IsEffectivelyEnabled);
            AssertBoundedResults(view, viewModel);
            Assert.Equal(530, viewModel.RowScores.Count);
            Assert.Equal(originalCriteria.Length, viewModel.Results.Count);
            for (int index = 0; index < originalCriteria.Length; index++)
            {
                Assert.Same(originalCriteria[index], viewModel.Results[index]);
            }

            Assert.Equal(runIdentity, viewModel.RunIdentityText);
            Assert.Equal("0", selected.OverrideText);
            Assert.All(summary.Units, unit => Assert.All(unit.AcceptedResult!.Criteria,
                criterion => Assert.Equal(5m, criterion.RawScore)));
            Assert.Equal(0, output.ExportCount);
        }
        finally
        {
            window.Close();
        }
    }

    private static T Required<T>(Control root, string name)
        where T : Control =>
        Assert.IsType<T>(root.FindControl<T>(name));

    private static IEnumerable<Control> AllControls(Control root) => root.GetVisualDescendants().OfType<Control>()
        .Concat(root.GetLogicalDescendants().OfType<Control>()).Prepend(root).Distinct();

    private static TextBox[] OverrideEditors(Control root) => AllControls(root).OfType<TextBox>()
        .Where(editor => (AutomationProperties.GetAutomationId(editor) ?? string.Empty).EndsWith("-Override", StringComparison.Ordinal))
        .ToArray();

    private static ListBoxItem SelectedContainer(ListBox list)
    {
        Assert.True(list.SelectedIndex >= 0);
        ListBoxItem selected = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(list.SelectedIndex));
        Assert.True(selected.IsSelected);
        return selected;
    }

    private static void AssertKeyboardTarget(Control control)
    {
        Assert.True(control.Focusable);
        Assert.True(control.IsTabStop);
        Assert.True(control.MinHeight >= 44d);
        Assert.True(control.Bounds.Height >= 44d);
        Assert.True(control.Bounds.Width >= 44d);
    }

    private static void AssertBoundedResults(ResultsOutputView view, ResultsOutputViewModel viewModel)
    {
        ListBox active = Required<ListBox>(view, viewModel.IsDetailVisible ? "ResultsList" : "RowScoreList");
        Assert.True(double.IsFinite(active.Bounds.Height));
        Assert.InRange(active.Bounds.Height, 44d, view.Bounds.Height);
        Assert.Contains(active.GetVisualDescendants(), visual => visual is VirtualizingStackPanel);
        ListBoxItem[] containers = active.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Assert.InRange(containers.Length, 1, viewModel.IsDetailVisible ? viewModel.SelectedRowCriteria.Count : viewModel.PageSize);
        Assert.All(containers, AssertKeyboardTarget);
        if (viewModel.IsDetailVisible)
        {
            TextBox editor = Assert.Single(OverrideEditors(view));
            Assert.True(editor.IsEffectivelyVisible);
            Assert.Same(viewModel.SelectedCriterion, editor.DataContext);
            Assert.Equal(viewModel.SelectedCriterion!.AutomationId + "-Override", AutomationProperties.GetAutomationId(editor));
        }
        else
        {
            Assert.Empty(OverrideEditors(view)); // Not even hidden per-row editors may be generated.
            Assert.InRange(viewModel.VisibleRowScores.Count, 1, viewModel.PageSize);
            ScrollViewer scroll = Assert.Single(active.GetVisualDescendants().OfType<ScrollViewer>(),
                item => ReferenceEquals(item.TemplatedParent, active));
            Assert.True(double.IsFinite(scroll.Viewport.Height));
            Assert.Equal(Math.Max(1, (int)Math.Floor(scroll.Viewport.Height / containers[0].Bounds.Height)), viewModel.PageSize);
            Assert.True(viewModel.PageSize < viewModel.RowScores.Count);
        }

        AssertUniqueAutomationIds(view);
    }

    private static void JumpToRow(Window window, ResultsOutputView view, int sourceRow)
    {
        TextBox rowNumber = Required<TextBox>(view, "GoToRowTextBox");
        Assert.True(rowNumber.Focus(NavigationMethod.Tab));
        rowNumber.SelectAll();
        window.KeyTextInput(sourceRow.ToString(CultureInfo.InvariantCulture));
        Render();
        Press(window, Key.Tab);
        Assert.Same(Required<Button>(view, "GoToRowButton"), window.FocusManager?.GetFocusedElement());
        Press(window, Key.Enter);
        Assert.Equal(sourceRow, view.ViewModel.SelectedRow?.SourceRowNumber);
    }

    private static void AssertUniqueAutomationIds(Control root)
    {
        string[] ids = AllControls(root)
            .Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static void Activate(Window window, Button button)
    {
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.True(button.Focus(NavigationMethod.Tab));
        Press(window, Key.Enter);
    }

    private static void Press(TopLevel window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physicalKey = key switch
        {
            Key.Tab => PhysicalKey.Tab,
            Key.Enter => PhysicalKey.Enter,
            Key.Space => PhysicalKey.Space,
            Key.Down => PhysicalKey.ArrowDown,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported test key."),
        };
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
        Render();
    }

    private static void Render()
    {
        // The view may schedule another layout after measuring its finite page capacity.
        for (int pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
    }
}