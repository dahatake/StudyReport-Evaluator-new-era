using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using Xunit;
using static StudyReportEvaluator.App.Tests.UI.ResponsiveLayoutTests;

namespace StudyReportEvaluator.App.Tests.UI;

/// <summary>T26 extends T23's shell tests with populated pages, long content and capacity boundaries.</summary>
// Requirements: NFR-UX-001 (AC-076)
public sealed class CompactWorkflowLayoutTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(1024d, 720d)]
    [InlineData(1180d, 800d)]
    public async Task Long_content_in_four_steps_and_every_settings_category_keeps_normal_body_contained(double width, double height)
    {
        using WorkflowLayoutFixture fixture = await WorkflowLayoutFixture.CreateAsync(longContent: true);
        MainWindow window = fixture.Window;
        fixture.Shell.ResultsOutputViewModel.OutputPath = LongPath;
        window.Width = width;
        window.Height = height;
        window.Show();
        Render();
        Assert.Equal(new Size(width, height), window.ClientSize);
        Rect[] fixedRegions = AssertFixedRegions(window);

        Button settingsOpen = Required<Button>(window, "SettingsOpenButton");
        Assert.True(settingsOpen.Focus(NavigationMethod.Tab));
        Press(window, Key.Tab);
        Assert.Same(Required<TextBox>(CurrentView(window), "FilePathTextBox"), window.FocusManager?.GetFocusedElement());
        Press(window, Key.Tab, RawInputModifiers.Shift);
        Assert.Same(settingsOpen, window.FocusManager?.GetFocusedElement());

        foreach (WorkflowStep step in Enum.GetValues<WorkflowStep>())
        {
            Assert.Equal(step, fixture.Shell.CurrentStep);
            AssertNormalBody(window);
            TextBox? longField = CurrentView(window) switch
            {
                InputView input => Required<TextBox>(input, "QuestionTextEditor"),
                ExecutionView execution => Required<TextBox>(execution, "EffectiveOutputDirectoryTextBox"),
                ResultsOutputView results => Required<TextBox>(results, "OutputPathTextBox"),
                _ => null,
            };
            if (longField is not null)
            {
                AssertTextEndReachable(window, longField);
            }

            AssertNormalBody(window);
            Assert.Equal(fixedRegions, AssertFixedRegions(window));
            RecordLayout(output, $"long-{step}", window);
            if (step != WorkflowStep.Results)
            {
                Activate(Required<Button>(window, "NextStepButton"));
            }
        }

        Activate(settingsOpen, Key.Space);
        SettingsView settings = Assert.IsType<SettingsView>(CurrentView(window));
        foreach (SettingsCategory category in Enum.GetValues<SettingsCategory>())
        {
            if (fixture.Shell.Settings.SelectedCategory != category)
            {
                Activate(ById<Button>(settings, "SettingsCategory" + category));
            }

            Assert.Equal(category, fixture.Shell.Settings.SelectedCategory);
            Assert.Equal(WorkflowStep.Results, fixture.Shell.CurrentStep);
            Control categoryView = Assert.IsAssignableFrom<Control>(Required<ContentControl>(settings, "CurrentSettingsContent").Content);
            Assert.Contains(Assert.IsAssignableFrom<Control>(window.FocusManager?.GetFocusedElement()).GetVisualAncestors(),
                ancestor => ReferenceEquals(ancestor, categoryView));

            if (category is SettingsCategory.Mapping or SettingsCategory.Evaluation or SettingsCategory.Special)
            {
                string selectorId = category switch
                {
                    SettingsCategory.Mapping => "MappingSettingsQuestions",
                    SettingsCategory.Evaluation => "EvaluatorSettingsQuestions",
                    _ => "SpecialSettingsQuestions",
                };
                if (category == SettingsCategory.Evaluation)
                {
                    SelectLastTab(window, ById<ListBox>(settings, selectorId));
                }
                else
                {
                    SelectLastComboItem(window, ById<ComboBox>(settings, selectorId));
                }
            }
            else if (category == SettingsCategory.ImportedPrompts)
            {
                ListBox list = ById<ListBox>(settings, "ImportedPrompts");
                SelectLastItem(window, list);
                Assert.Same(fixture.Shell.DesignViewModel.ImportedPrompts[^1], fixture.Shell.DesignViewModel.SelectedImportedPrompt);
                AssertScrollEndReachable(ListScroll(list), Assert.IsType<ListBoxItem>(list.ContainerFromIndex(list.Items.Count - 1)),
                    requireOverflow: true);
            }

            // Cover Common edit/stored-definition/runtime and Basic/Prompt pages,
            // not just each category's default tab or its outer ScrollViewer flags.
            TabControl? tabs = SettingsEditorTabs(settings, category);
            int tabCount = tabs?.Items.Count ?? 1;
            for (int page = 0; page < tabCount; page++)
            {
                if (tabs is not null)
                {
                    tabs.SelectedIndex = page;
                    Render();
                }

                AssertNormalBody(window);
                TextBox[] longEditors = settings.GetVisualDescendants().OfType<TextBox>().Where(editor =>
                    editor.IsEffectivelyVisible && (editor.Text?.Contains(LongJapaneseName, StringComparison.Ordinal) == true
                    || editor.Text == Path.GetDirectoryName(LongPath))).ToArray();
                if ((page == 0 && category != SettingsCategory.Common) || (category == SettingsCategory.Common && page < 2))
                {
                    Assert.NotEmpty(longEditors);
                }

                foreach (TextBox editor in longEditors)
                {
                    AssertTextEndReachable(window, editor);
                }

                AssertNormalBody(window);
                AssertFullyInside(Required<Grid>(settings, "SettingsFooter"), settings);
                Assert.Equal(fixedRegions, AssertFixedRegions(window));
                RecordLayout(output, $"long-settings-{category}-tab-{page}", window);
            }
        }

        Activate(Required<Button>(settings, "SettingsRequestClose"));
        Assert.Same(settingsOpen, window.FocusManager?.GetFocusedElement());
        Assert.IsType<ResultsOutputView>(CurrentView(window));
        AssertNormalBody(window);
        fixture.AssertPassive();
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task Narrow_scaled_shell_reaches_last_operations_without_moving_warning_or_navigation(double scale)
    {
        using WorkflowLayoutFixture fixture = await WorkflowLayoutFixture.CreateAsync(longContent: true);
        MainWindow window = fixture.Window;
        // Test-only exception host, not a change to product minimum dimensions.
        window.MinWidth = 0;
        window.MinHeight = 0;
        window.Width = 760;
        window.Height = 600;
        window.Show();
        window.SetRenderScaling(scale);
        Render();
        Assert.Equal(new Size(760, 600), window.ClientSize);
        Assert.Equal(scale, window.RenderScaling);
        Rect[] fixedRegions = AssertFixedRegions(window);
        ScrollViewer body = Required<ScrollViewer>(window, "ShellScrollViewer");

        foreach (WorkflowStep step in Enum.GetValues<WorkflowStep>())
        {
            Control view = CurrentView(window);
            Assert.Equal(step, fixture.Shell.CurrentStep);
            string lastName = view switch
            {
                InputView => "InputValidationSummary",
                QuantificationDesignView => "ValidateDesignButton",
                ExecutionView => "CancelRunButton",
                _ => "ExportButton",
            };
            AssertScrollEndReachable(body, view.FindControl<Control>(lastName)!);
            AssertFitsHorizontally(window, $"narrow scaled {step}");
            Assert.Equal(fixedRegions, AssertFixedRegions(window));
            RecordLayout(output, $"exception-{step}", window);
            if (step != WorkflowStep.Results)
            {
                Activate(Required<Button>(window, "NextStepButton"));
            }
        }

        Button gear = Required<Button>(window, "SettingsOpenButton");
        Activate(gear, Key.Space);
        SettingsView settings = Assert.IsType<SettingsView>(CurrentView(window));
        Rect[] settingsFixedRegions = AssertSettingsFixedRegions(settings);
        Assert.True(body.Extent.Height > body.Viewport.Height, "The narrow shell must exercise actual body overflow.");
        foreach (SettingsCategory category in Enum.GetValues<SettingsCategory>())
        {
            Activate(ById<Button>(settings, "SettingsCategory" + category));
            Assert.Equal(category, fixture.Shell.Settings.SelectedCategory);
            TabControl? tabs = SettingsEditorTabs(settings, category);
            int tabCount = tabs?.Items.Count ?? 1;
            for (int page = 0; page < tabCount; page++)
            {
                if (tabs is not null)
                {
                    tabs.SelectedIndex = page;
                    Render();
                    Assert.Equal(page, tabs.SelectedIndex);
                }

                foreach (Control last in SettingsBodyTerminalControls(settings, category, page))
                {
                    AssertSettingsBodyTerminalReachable(settings, last);
                    Assert.Equal(settingsFixedRegions, AssertSettingsFixedRegions(settings));
                    Assert.Equal(fixedRegions, AssertFixedRegions(window));
                }

                AssertFitsHorizontally(window, $"narrow scaled settings {category} tab {page}");
                RecordLayout(output, $"exception-settings-{category}-tab-{page}", window);
            }

            // The footer remains a separate focus/return check, never a body terminal.
            Button back = Required<Button>(settings, "SettingsRequestClose");
            back.BringIntoView();
            Render();
            AssertFullyInsideAncestors(back);
            Assert.True(back.Focus(NavigationMethod.Tab));
            Press(window, Key.Tab);
            Control next = Assert.IsAssignableFrom<Control>(window.FocusManager?.GetFocusedElement());
            Assert.NotSame(back, next);
            AssertFullyInsideAncestors(next);
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(back, window.FocusManager?.GetFocusedElement());
            Assert.Equal(settingsFixedRegions, AssertSettingsFixedRegions(settings));
            Assert.Equal(fixedRegions, AssertFixedRegions(window));
        }

        Activate(Required<Button>(settings, "SettingsRequestClose"));
        Assert.Same(gear, window.FocusManager?.GetFocusedElement());
        fixture.AssertPassive();
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task Standalone_settings_at_760_by_600_keeps_all_categories_and_last_actions_reachable(double scale)
    {
        using WorkflowLayoutFixture fixture = await WorkflowLayoutFixture.CreateAsync(longContent: true);
        SettingsView view = new(fixture.Shell.Settings);
        Window window = Host(view, scale: scale);
        try
        {
            Rect[] fixedRegions = AssertSettingsFixedRegions(view);
            foreach (SettingsCategory category in Enum.GetValues<SettingsCategory>())
            {
                Activate(ById<Button>(view, "SettingsCategory" + category));
                Assert.Equal(category, fixture.Shell.Settings.SelectedCategory);
                TabControl? tabs = SettingsEditorTabs(view, category);
                int tabCount = tabs?.Items.Count ?? 1;
                for (int page = 0; page < tabCount; page++)
                {
                    if (tabs is not null)
                    {
                        tabs.SelectedIndex = page;
                        Render();
                        Assert.Equal(page, tabs.SelectedIndex);
                    }

                    foreach (Control last in SettingsBodyTerminalControls(view, category, page))
                    {
                        AssertSettingsBodyTerminalReachable(view, last);
                        Assert.Equal(fixedRegions, AssertSettingsFixedRegions(view));
                    }

                    AssertFitsHorizontally(view, $"standalone settings {category} tab {page}");
                    RecordLayout(output, $"standalone-settings-{category}-tab-{page}", window);
                }

                Assert.True(Required<Button>(view, "SettingsRequestClose").Focus(NavigationMethod.Tab));
                Press(window, Key.Tab);
                AssertFullyInsideAncestors(Assert.IsAssignableFrom<Control>(window.FocusManager?.GetFocusedElement()));
                Assert.Equal(fixedRegions, AssertSettingsFixedRegions(view));
            }

            fixture.AssertPassive();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Resizing_real_main_lists_grows_capacity_and_keeps_the_last_selected_item()
    {
        using WorkflowLayoutFixture fixture = await WorkflowLayoutFixture.CreateAsync();
        MainWindow window = fixture.Window;
        window.Width = 1024;
        window.Height = 720;
        window.Show();
        Render();

        foreach (WorkflowStep step in Enum.GetValues<WorkflowStep>())
        {
            Control view = CurrentView(window);
            if (step != WorkflowStep.Execution)
            {
                string nextName = step == WorkflowStep.Design ? "NextQuestionPageButton" : "NextPageButton";
                var first = AssertCurrentPage(view);
                GoToLastPage(view, nextName, first.Total);
                ListBox list = Required<ListBox>(view, step switch
                {
                    WorkflowStep.Input => "InputQuestionList",
                    WorkflowStep.Design => "QuestionEditorList",
                    _ => "RowScoreList",
                });
                SelectLastItem(window, list);
                object selected = Assert.IsAssignableFrom<object>(list.SelectedItem);
                var small = AssertCurrentPage(view);
                Assert.Equal((small.Total - 1) / small.Size, small.Index);
                RecordLayout(output, $"resize-small-{step}", window);

                window.Width = 1180;
                window.Height = 800;
                Render();
                Assert.Equal(new Size(1180, 800), window.ClientSize);
                var large = AssertCurrentPage(view);
                Assert.True(large.Size > small.Size, "More actual remaining height must increase page capacity.");
                Assert.Equal((large.Total - 1) / large.Size, large.Index);
                Assert.Same(selected, list.SelectedItem);
                AssertNormalBody(window);
                RecordLayout(output, $"resize-large-{step}", window);

                window.Width = 1024;
                window.Height = 720;
                Render();
                Assert.Equal(new Size(1024, 720), window.ClientSize);
                Assert.Equal(small, AssertCurrentPage(view));
                Assert.Same(selected, list.SelectedItem);
                AssertNormalBody(window);
            }

            if (step != WorkflowStep.Results)
            {
                Activate(Required<Button>(window, "NextStepButton"));
            }
        }

        fixture.AssertPassive();
    }

    [AvaloniaFact]
    public async Task Deleting_the_last_page_through_mapping_settings_repairs_main_pages_and_empty_state()
    {
        using WorkflowLayoutFixture fixture = await WorkflowLayoutFixture.CreateAsync();
        MainWindow window = fixture.Window;
        window.Width = 1024;
        window.Height = 720;
        window.Show();
        Render();
        InputViewModel input = fixture.Shell.InputViewModel;
        InputView view = Assert.IsType<InputView>(CurrentView(window));
        GoToLastPage(view, "NextPageButton", input.Questions.Count);
        SelectLastItem(window, Required<ListBox>(view, "InputQuestionList"));
        InputQuestionMappingViewModel neighbor = input.Questions[^2];
        Activate(Required<Button>(view, "MappingDetailsButton"));
        SettingsView settings = Assert.IsType<SettingsView>(CurrentView(window));
        MappingSettingsView mapping = Assert.Single(settings.GetVisualDescendants().OfType<MappingSettingsView>());
        Activate(Required<Button>(mapping, "DeleteQuestionButton"));
        Assert.Same(neighbor, input.SelectedQuestion);
        Activate(Required<Button>(settings, "SettingsRequestClose"));
        var repaired = AssertCurrentPage(view);
        Assert.Equal((input.Questions.Count - 1) / input.PageSize, repaired.Index);
        AssertNormalBody(window);
        RecordLayout(output, "last-page-delete", window);

        Activate(Required<Button>(view, "MappingDetailsButton"));
        // Each real Delete button rebinds to the previous surviving ID, including across page boundaries.
        int remaining = input.Questions.Count;
        while (remaining > 0)
        {
            string removedId = input.SelectedQuestion!.Id;
            Activate(Required<Button>(mapping, "DeleteQuestionButton"));
            Assert.Equal(--remaining, input.Questions.Count);
            Assert.DoesNotContain(input.Questions, question => question.Id == removedId);
            Assert.Same(remaining == 0 ? null : input.Questions[^1], input.SelectedQuestion);
        }

        AssertFullyInside(Required<TextBlock>(mapping, "NoQuestionMessage"), mapping);
        Activate(Required<Button>(settings, "SettingsRequestClose"));
        Assert.Equal((0, input.PageSize, 0), AssertCurrentPage(view));
        Assert.Equal("設問はありません（0 件）", Required<TextBlock>(view, "QuestionPageSummary").Text);
        Assert.False(Required<Button>(view, "PreviousPageButton").IsEffectivelyEnabled);
        Assert.False(Required<Button>(view, "NextPageButton").IsEffectivelyEnabled);
        AssertNormalBody(window);
        Activate(Required<Button>(window, "NextStepButton"));
        QuantificationDesignView design = Assert.IsType<QuantificationDesignView>(CurrentView(window));
        Assert.Equal((0, design.ViewModel.PageSize, 0), AssertCurrentPage(design));
        Assert.Null(design.ViewModel.SelectedQuestion);
        Assert.False(Required<Button>(design, "NextQuestionPageButton").IsEffectivelyEnabled);
        AssertNormalBody(window);
        RecordLayout(output, "empty-design", window);

        Activate(Required<Button>(window, "PreviousStepButton"));
        Activate(Required<Button>(view, "MappingDetailsButton"));
        Activate(Required<Button>(mapping, "AddQuestionButton"));
        Activate(Required<Button>(settings, "SettingsRequestClose"));
        Assert.Same(Assert.Single(input.Questions), input.SelectedQuestion);
        Assert.Equal((0, input.PageSize, 1), AssertCurrentPage(view));
        AssertNormalBody(window);
        fixture.AssertPassive();
    }

    [AvaloniaFact]
    public async Task Empty_results_replace_a_real_last_page_without_leaving_stale_controls()
    {
        using ResultsOutputViewModel populated = await CreateResultsAsync(100);
        using ResultsOutputViewModel empty = new(new RecordingOutputBoundary());
        ResultsOutputView view = new(populated);
        Window window = Host(view);
        try
        {
            Required<TextBox>(view, "GoToRowTextBox").Text = "101";
            Activate(Required<Button>(view, "GoToRowButton"));
            Assert.Equal(101, populated.SelectedRow?.SourceRowNumber);
            AssertCurrentPage(view);
            Activate(Required<Button>(view, "ShowDetailButton"));
            Assert.NotNull(Required<ContentControl>(view, "CriterionEditor").Content);
            view.DataContext = empty;
            Render();
            Assert.Equal((0, empty.PageSize, 0), AssertCurrentPage(view));
            Assert.Null(empty.SelectedRow);
            Assert.Equal("結果はありません（0 行）", ById<TextBlock>(view, "ResultsPageSummary").Text);
            foreach (string name in new[] { "PreviousPageButton", "NextPageButton", "GoToRowButton", "ShowDetailButton" })
            {
                Button button = Required<Button>(view, name);
                Assert.False(button.IsEffectivelyEnabled);
                AssertFullyInside(button, view);
            }

            Assert.Null(Required<ContentControl>(view, "CriterionEditor").Content);
            AssertFitsHorizontally(view, "empty results after last-page rebind");
            RecordLayout(output, "empty-results", window);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(13)]
    public void Twenty_thousand_item_page_math_resizes_clamps_after_deletion_and_empties_without_UI(int capacity)
    {
        // Same production seam as InputPresentationTests. No 20,000-item collection,
        // workbook, editor, UI tree or AI operation; these sizes are calculation inputs, not measured capacities.
        MethodInfo calculate = Assert.IsAssignableFrom<MethodInfo>(typeof(InputViewModel).GetMethod(
            "CalculateQuestionPage", BindingFlags.Static | BindingFlags.NonPublic));
        (int Index, int Start, int Count) Page(int count, int requested, int size) =>
            Assert.IsType<(int, int, int)>(calculate.Invoke(null, [count, requested, size]));
        const int total = 20_000;
        Assert.Equal((0, 0, capacity), Page(total, int.MinValue, capacity));
        var last = Page(total, int.MaxValue, capacity);
        Assert.Equal((total - 1) / capacity, last.Index);
        Assert.Equal((long)last.Index * capacity, last.Start);
        Assert.Equal(total, last.Start + last.Count);
        Assert.InRange(last.Count, 1, capacity);

        int largerCapacity = capacity + 2;
        var resized = Page(total, (total - 1) / largerCapacity, largerCapacity);
        Assert.Equal(total, resized.Start + resized.Count);
        Assert.Equal((total - 1) / largerCapacity, resized.Index);
        var removed = Page(total - 1, resized.Index, largerCapacity);
        Assert.Equal((total - 2) / largerCapacity, removed.Index);
        Assert.Equal(total - 1, removed.Start + removed.Count);
        Assert.InRange(removed.Count, 1, largerCapacity);
        Assert.Equal((0, 0, 0), Page(0, removed.Index, largerCapacity));
    }

    private static TabControl? SettingsEditorTabs(SettingsView view, SettingsCategory category)
    {
        (string? Id, string[] Pages) expected = category switch
        {
            SettingsCategory.Common => ("SettingsCommonTabs",
                ["SettingsCommonEditTab", "SettingsStoredDefinitionTab", "SettingsRuntimeTab", "SettingsPersonaTab"]),
            SettingsCategory.Evaluation => ("EvaluatorSettingsTabs",
                ["EvaluatorSettingsBasicTab", "EvaluatorSettingsPromptTab"]),
            SettingsCategory.Special => ("SpecialSettingsEditorTabs",
                ["SpecialSettingsItemTab", "SpecialSettingsPromptTab"]),
            SettingsCategory.Mapping or SettingsCategory.ImportedPrompts => (null, []),
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
        if (expected.Id is null)
        {
            Assert.Empty(view.GetVisualDescendants().OfType<TabControl>());
            return null;
        }

        TabControl tabs = ById<TabControl>(view, expected.Id);
        Assert.Equal(expected.Pages, tabs.Items.Cast<TabItem>().Select(AutomationProperties.GetAutomationId));
        return tabs;
    }

    private static Control[] SettingsBodyTerminalControls(SettingsView view, SettingsCategory category, int page)
    {
        // Shared by standalone and shell cases: final inputs/actions AND the trailing
        // body state, not SettingsRequestClose (which is outside SettingsBodyScroll).
        QuantificationDesignViewModel design = view.ViewModel.Design;
        string lastId = (category, page) switch
        {
            (SettingsCategory.Common, 0) => "DesignRoundingDigits",
            // The following ApplyStatus is intentionally empty until an explicit apply.
            (SettingsCategory.Common, 1) => "SettingsApplySavedDefinition",
            (SettingsCategory.Common, 2) => "SettingsPreferredModelId",
            (SettingsCategory.Common, 3) => "SettingsLayoutFilePath",
            (SettingsCategory.Mapping, 0) => "MappingSettingsErrorDetail",
            (SettingsCategory.Evaluation, 0) => $"DesignCriterion-{design.SelectedQuestion!.SelectedEvaluator!.SelectedCriterion!.Id}-Maximum",
            (SettingsCategory.Evaluation, 1) => design.SelectedQuestion!.SelectedEvaluator!.IsKnowledge
                ? design.SelectedQuestion.SelectedEvaluator.KnowledgePromptPreviewAutomationId
                : design.SelectedQuestion.SelectedEvaluator.CustomPromptPreviewAutomationId,
            (SettingsCategory.Special, 0) => $"{design.SelectedQuestion!.SelectedSpecialEvaluation!.CardAutomationId}-SupportingSelected",
            (SettingsCategory.Special, 1) => $"{design.SelectedQuestion!.SelectedSpecialEvaluation!.CardAutomationId}-PromptPreview",
            (SettingsCategory.ImportedPrompts, 0) => "ApplyImportedPrompt",
            _ => throw new ArgumentOutOfRangeException(nameof(page), $"No terminal is specified for {category} tab {page}."),
        };
        Control last = ById<Control>(view, lastId);
        switch (category, page)
        {
            case (SettingsCategory.Common, 1) when !string.IsNullOrEmpty(view.ViewModel.ApplyStatusText):
                return [last, ById<TextBlock>(view, "SettingsApplyStatus")];
            case (SettingsCategory.Evaluation, 0):
                // The range/weight summary is below the final numeric input. Anchor
                // to the selected criterion's named card, not arbitrary visible text.
                Border card = ById<Border>(view, design.SelectedQuestion!.SelectedEvaluator!.SelectedCriterion!.CardAutomationId);
                StackPanel summary = Assert.Single(Assert.IsType<Grid>(card.Child).Children.OfType<StackPanel>());
                return [last, summary];
            case (SettingsCategory.Evaluation, 1) when design.SelectedQuestion!.SelectedEvaluator!.IsCustom:
                TextBlock guidance = Assert.Single(Assert.IsType<Grid>(last.Parent).Children.OfType<TextBlock>(),
                    text => Grid.GetRow(text) == 2);
                return [last, guidance];
            case (SettingsCategory.Special, _):
                return [last, ById<Button>(view, "SpecialSettingsValidate")];
            case (SettingsCategory.ImportedPrompts, 0):
                return [last, ById<TextBlock>(view, "ImportedPromptApplyStatus")];
            default:
                return [last];
        }
    }

    private static void AssertSettingsBodyTerminalReachable(SettingsView view, Control last)
    {
        Control category = Assert.IsAssignableFrom<Control>(Required<ContentControl>(view, "CurrentSettingsContent").Content);
        Assert.Contains(category, last.GetVisualAncestors());
        Assert.Contains(Required<ScrollViewer>(view, "SettingsBodyScroll"), last.GetVisualAncestors());
        ScrollViewer scroll = last.GetVisualAncestors().OfType<ScrollViewer>().First();
        AssertScrollEndReachable(scroll, last);
        if (last is TextBlock state)
        {
            AssertImportantTextFullyVisible(state);
        }

        AssertFullyInsideAncestors(last);
    }

    private static (int Index, int Size, int Total) AssertCurrentPage(Control view)
    {
        switch (view)
        {
            case InputView input:
                ListBox inputList = Required<ListBox>(input, "InputQuestionList");
                Assert.Same(input.ViewModel.VisibleQuestions, inputList.ItemsSource);
                AssertPageRows(inputList, input.ViewModel.Questions, input.ViewModel.PageIndex, input.ViewModel.PageSize);
                return (input.ViewModel.PageIndex, input.ViewModel.PageSize, input.ViewModel.Questions.Count);
            case QuantificationDesignView design:
                ListBox designList = Required<ListBox>(design, "QuestionEditorList");
                Assert.Same(design.ViewModel.VisibleQuestions, designList.ItemsSource);
                AssertPageRows(designList, design.ViewModel.Questions, design.ViewModel.PageIndex, design.ViewModel.PageSize);
                return (design.ViewModel.PageIndex, design.ViewModel.PageSize, design.ViewModel.Questions.Count);
            case ResultsOutputView results:
                ListBox resultList = Required<ListBox>(results, "RowScoreList");
                Assert.Same(results.ViewModel.VisibleRowScores, resultList.ItemsSource);
                AssertPageRows(resultList, results.ViewModel.RowScores, results.ViewModel.PageIndex, results.ViewModel.PageSize);
                return (results.ViewModel.PageIndex, results.ViewModel.PageSize, results.ViewModel.RowScores.Count);
            default:
                throw new ArgumentException("A production paged view is required.", nameof(view));
        }
    }

    private static void SelectLastTab(Window window, ListBox selector)
    {
        Assert.NotEmpty(selector.Items);
        Assert.True(selector.Focus(NavigationMethod.Tab));
        selector.SelectedItem = selector.Items[selector.Items.Count - 1];
        Assert.Same(selector.Items[selector.Items.Count - 1], selector.SelectedItem);
        AssertFullyInside(selector, window);
    }

    private static void SelectLastComboItem(Window window, ComboBox selector)
    {
        Assert.NotEmpty(selector.Items);
        Assert.True(selector.Focus(NavigationMethod.Tab));
        Press(window, Key.F4);
        Assert.True(selector.IsDropDownOpen);
        Press(window, Key.End);
        Press(window, Key.Enter);
        Assert.False(selector.IsDropDownOpen);
        Assert.Same(selector.Items[selector.Items.Count - 1], selector.SelectedItem);
        AssertFullyInside(selector, window);
    }
}