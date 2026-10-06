using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-054 (AC-055)
public sealed class ImportedPromptSettingsViewTests
{
    [AvaloniaFact]
    public void Imported_list_preserves_basename_original_order_and_read_only_full_text_without_copy()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        ImportedPromptViewModel[] originals = viewModel.ImportedPrompts.ToArray();
        QuantificationDefinition before = viewModel.Draft;
        ImportedPromptSettingsView view = new() { DataContext = viewModel };
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            ListBox list = Required<ListBox>(view, "ImportedPromptList");
            TextBox preview = Required<TextBox>(view, "ImportedPromptPreview");
            Assert.Same(viewModel, view.DataContext);
            Assert.Same(viewModel, view.ViewModel);
            Assert.Same(viewModel.ImportedPrompts, list.ItemsSource);
            Assert.Equal(["02-reuse.txt", "01-reuse.txt", "01-reuse.txt"],
                list.Items.Cast<ImportedPromptViewModel>().Select(item => item.DisplayName));
            Assert.Equal("読込Prompt 3 件", Required<TextBlock>(view, "ImportedPromptCountText").Text);
            Assert.True(preview.IsReadOnly);
            Assert.True(preview.AcceptsReturn);

            foreach (ImportedPromptViewModel prompt in originals)
            {
                list.SetCurrentValue(ListBox.SelectedItemProperty, prompt);
                Render();
                Assert.Same(prompt, viewModel.SelectedImportedPrompt);
                Assert.Equal(prompt.Content, preview.Text);
                Assert.Same(before, viewModel.Draft);
            }

            Assert.Equal("別の原文\r\n{回答} {評価項目} 😀\n末尾", preview.Text);
            Assert.Equal(originals, viewModel.ImportedPrompts);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(),
                text => (text.Text ?? string.Empty).Contains("synthetic-private", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("empty", "読込Promptはありません")]
    [InlineData("prompt", "Promptを選択")]
    [InlineData("question", "設問を選択")]
    [InlineData("evaluator", "Custom評価方法を選択")]
    [InlineData("special", "固有評価を選択")]
    [InlineData("knowledge", "Knowledgeは読取専用")]
    [InlineData("root", "採点設計がありません")]
    public void Missing_selections_and_Knowledge_disable_apply_without_mutating_draft(string missing, string reason)
    {
        QuantificationDesignViewModel viewModel = missing == "empty" ? CreateDesign([]) : CreateDesign();
        QuestionDesignItemViewModel question = viewModel.Questions[0];
        ImportedPromptSettingsView view = new(viewModel);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            QuantificationDefinition before = viewModel.Draft;
            switch (missing)
            {
                case "prompt":
                    Required<ListBox>(view, "ImportedPromptList").SetCurrentValue(ListBox.SelectedItemProperty, null);
                    break;
                case "question":
                    Required<ComboBox>(view, "ImportedPromptQuestionSelector").SetCurrentValue(ComboBox.SelectedItemProperty, null);
                    break;
                case "evaluator":
                    EvaluatorSelector(view, question).SetCurrentValue(ComboBox.SelectedItemProperty, null);
                    break;
                case "special":
                    SelectTarget(view, ImportedPromptTarget.SpecialEvaluation);
                    SpecialSelector(view, question).SetCurrentValue(ComboBox.SelectedItemProperty, null);
                    break;
                case "knowledge":
                    EvaluatorSelector(view, question).SetCurrentValue(ComboBox.SelectedItemProperty, question.Evaluators[0]);
                    break;
                case "root":
                    view.DataContext = null;
                    break;
            }

            Render();
            Button apply = Required<Button>(view, "ApplyImportedPromptButton");
            Assert.False(apply.IsEffectivelyEnabled);
            Assert.Contains(reason, Required<TextBlock>(view, "ImportedPromptApplyStatus").Text ?? string.Empty,
                StringComparison.Ordinal);
            if (missing == "root")
            {
                Assert.Null(apply.Command);
            }
            else
            {
                Assert.Same(viewModel.ApplyImportedPromptCommand, apply.Command);
                Assert.False(viewModel.ApplyImportedPromptCommand.CanExecute(null));
                viewModel.ApplyImportedPromptCommand.Execute(null);
            }

            Assert.Same(before, viewModel.Draft);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Explicit_apply_copies_only_to_selected_custom_evaluator()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        ImportedPromptSettingsView view = new(viewModel);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            QuestionDesignItemViewModel question = viewModel.Questions[1];
            SelectQuestion(view, question);
            EvaluatorDesignItemViewModel evaluator = question.Evaluators[2];
            EvaluatorSelector(view, question).SetCurrentValue(ComboBox.SelectedItemProperty, evaluator);
            Required<ListBox>(view, "ImportedPromptList").SetCurrentValue(
                ListBox.SelectedItemProperty, viewModel.ImportedPrompts[2]);
            Render();
            QuantificationDefinition before = viewModel.Draft;
            QuantificationSnapshot snapshot = viewModel.BuildSnapshot();
            string content = viewModel.ImportedPrompts[2].Content;
            Assert.NotEqual(content, evaluator.CustomPromptTemplate);
            Assert.Equal(viewModel.SelectedPromptTargetSummary, Required<TextBlock>(view, "SelectedPromptTargetSummary").Text);
            Assert.Contains("未反映", Required<TextBlock>(view, "ImportedPromptApplyStatus").Text ?? string.Empty);

            Apply(window, view);

            QuestionDefinition expectedQuestion = before.Questions[1];
            AssertDefinition(before with
            {
                Questions = before.Questions.SetItem(1, expectedQuestion with
                {
                    Evaluators = expectedQuestion.Evaluators.SetItem(2, expectedQuestion.Evaluators[2] with
                    {
                        CustomPromptTemplate = content,
                    }),
                }),
            }, viewModel.Draft);
            Assert.Equal(content, evaluator.CustomPromptTemplate);
            Assert.NotEqual(content, snapshot.Definition.Questions[1].Evaluators[2].CustomPromptTemplate);
            Assert.True(snapshot.HasValidHash());
            Assert.Contains("一致", Required<TextBlock>(view, "ImportedPromptApplyStatus").Text ?? string.Empty);
            Assert.Contains("保存状態とは別", Required<TextBlock>(view, "ImportedPromptApplyStatus").Text ?? string.Empty);

            evaluator.CustomPromptTemplate = "適用後の編集 {回答} {評価項目}";
            Render();
            Assert.Contains("未反映", Required<TextBlock>(view, "ImportedPromptApplyStatus").Text ?? string.Empty);
            Assert.Equal(content, viewModel.ImportedPrompts[2].Content);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Explicit_apply_copies_only_to_selected_special_evaluation_without_changing_points()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        ImportedPromptSettingsView view = new(viewModel);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            QuestionDesignItemViewModel question = viewModel.Questions[1];
            SelectQuestion(view, question);
            SelectTarget(view, ImportedPromptTarget.SpecialEvaluation);
            SpecialEvaluationDesignItemViewModel special = question.SpecialEvaluations[0];
            SpecialSelector(view, question).SetCurrentValue(ComboBox.SelectedItemProperty, special);
            Render();
            QuantificationDefinition before = viewModel.Draft;
            string content = viewModel.ImportedPrompts[0].Content;
            Assert.NotEqual(content, special.PromptTemplate);
            Assert.Contains("固有評価", Required<TextBlock>(view, "SelectedPromptTargetSummary").Text ?? string.Empty);

            Apply(window, view);

            QuestionDefinition expectedQuestion = before.Questions[1];
            AssertDefinition(before with
            {
                Questions = before.Questions.SetItem(1, expectedQuestion with
                {
                    SpecialEvaluations = expectedQuestion.SpecialEvaluations.SetItem(0, expectedQuestion.SpecialEvaluations[0] with
                    {
                        PromptTemplate = content,
                    }),
                }),
            }, viewModel.Draft);
            Assert.Equal(content, special.PromptTemplate);
            Assert.Equal(0m, viewModel.SpecialPoints);
            Assert.Contains("一致", Required<TextBlock>(view, "ImportedPromptApplyStatus").Text ?? string.Empty);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Shared_prompt_is_reusable_across_questions_and_target_types_without_consuming_the_list()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        var prompts = viewModel.ImportedPrompts;
        ImportedPromptViewModel[] originals = prompts.ToArray();
        ImportedPrompt[] sources = viewModel.ImportedPromptSources.ToArray();
        ImportedPromptSettingsView view = new(viewModel);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            foreach (QuestionDesignItemViewModel question in viewModel.Questions)
            {
                SelectQuestion(view, question);
                SelectTarget(view, ImportedPromptTarget.CustomEvaluator);
                Apply(window, view);
                SelectTarget(view, ImportedPromptTarget.SpecialEvaluation);
                Apply(window, view);
            }

            foreach (QuestionDesignItemViewModel question in viewModel.Questions)
            {
                Assert.Equal(originals[0].Content, question.SelectedEvaluator!.CustomPromptTemplate);
                Assert.Equal(originals[0].Content, question.SelectedSpecialEvaluation!.PromptTemplate);
            }

            Assert.Same(prompts, viewModel.ImportedPrompts);
            Assert.Same(prompts, Required<ListBox>(view, "ImportedPromptList").ItemsSource);
            Assert.Same(originals[0], viewModel.SelectedImportedPrompt);
            for (int index = 0; index < originals.Length; index++)
            {
                Assert.Same(originals[index], prompts[index]);
                Assert.Same(sources[index], viewModel.ImportedPromptSources[index]);
                Assert.Equal(sources[index].Content, prompts[index].Content);
            }

            viewModel.Questions[0].SelectedEvaluator!.CustomPromptTemplate = "独立した編集 {回答} {評価項目}";
            Assert.Equal(originals[0].Content, viewModel.Questions[1].SelectedEvaluator!.CustomPromptTemplate);
            Assert.Equal(originals[0].Content, viewModel.Questions[0].SelectedSpecialEvaluation!.PromptTemplate);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Draft_updates_preserve_imported_prompt_and_target_selections_by_identity()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        viewModel.PageSize = 1;
        ImportedPromptSettingsView view = new(viewModel);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            QuestionDesignItemViewModel question = viewModel.Questions[1];
            EvaluatorDesignItemViewModel evaluator = question.Evaluators[2];
            SpecialEvaluationDesignItemViewModel special = question.SpecialEvaluations[1];
            ImportedPromptViewModel prompt = viewModel.ImportedPrompts[2];
            SelectQuestion(view, question);
            EvaluatorSelector(view, question).SetCurrentValue(ComboBox.SelectedItemProperty, evaluator);
            SelectTarget(view, ImportedPromptTarget.SpecialEvaluation);
            Required<ListBox>(view, "ImportedPromptList").SetCurrentValue(ListBox.SelectedItemProperty, prompt);
            Render();
            QuantificationDefinition incoming = viewModel.Draft;
            QuestionDefinition updated = incoming.Questions[1] with
            {
                DisplayName = "入力で改名した設問",
                Evaluators = [.. incoming.Questions[1].Evaluators.Reverse()],
                SpecialEvaluations = [.. incoming.Questions[1].SpecialEvaluations.Reverse()],
            };
            incoming = incoming with { Revision = "updated", Questions = [updated, incoming.Questions[0]] };

            viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);
            Render();

            AssertDefinition(incoming, viewModel.Draft);
            Assert.Same(question, viewModel.SelectedQuestion);
            Assert.Same(question, Required<ComboBox>(view, "ImportedPromptQuestionSelector").SelectedItem);
            Assert.Same(evaluator, question.SelectedEvaluator);
            Assert.Same(evaluator, EvaluatorSelector(view, question).SelectedItem);
            Assert.Same(special, question.SelectedSpecialEvaluation);
            Assert.Same(special, SpecialSelector(view, question).SelectedItem);
            Assert.Same(prompt, viewModel.SelectedImportedPrompt);
            Assert.Same(prompt, Required<ListBox>(view, "ImportedPromptList").SelectedItem);
            Assert.Equal(prompt.Content, Required<TextBox>(view, "ImportedPromptPreview").Text);
            Assert.Equal(ImportedPromptTarget.SpecialEvaluation, viewModel.SelectedPromptTarget);
            Assert.Equal(viewModel.SelectedPromptTargetSummary, Required<TextBlock>(view, "SelectedPromptTargetSummary").Text);

            EvaluatorDesignItemViewModel nextEvaluator = question.Evaluators[1];
            SpecialEvaluationDesignItemViewModel nextSpecial = question.SpecialEvaluations[1];
            updated = updated with
            {
                Evaluators = updated.Evaluators.RemoveAt(0),
                SpecialEvaluations = updated.SpecialEvaluations.RemoveAt(0),
            };
            incoming = incoming with { Questions = incoming.Questions.SetItem(0, updated) };
            viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);
            Render();

            AssertDefinition(incoming, viewModel.Draft);
            Assert.Same(nextEvaluator, EvaluatorSelector(view, question).SelectedItem);
            Assert.Same(nextSpecial, SpecialSelector(view, question).SelectedItem);
            Assert.Same(prompt, viewModel.SelectedImportedPrompt);
            Assert.True(viewModel.CanApplyImportedPrompt);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Automation_ids_remain_unique_and_use_stable_question_ids_not_display_names()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        foreach (QuestionDesignItemViewModel question in viewModel.Questions)
        {
            question.DisplayName = "同じ設問名";
        }

        ImportedPromptSettingsView view = new(viewModel);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            foreach (QuestionDesignItemViewModel question in viewModel.Questions)
            {
                SelectQuestion(view, question);
                string[] ids = view.GetVisualDescendants().OfType<Control>().Prepend(view)
                    .Select(AutomationProperties.GetAutomationId)
                    .Where(id => !string.IsNullOrEmpty(id)).Cast<string>().ToArray();
                Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
                Assert.Contains("ImportedPromptSettingsView", ids);
                Assert.Contains("ImportedPrompts", ids);
                Assert.Contains("ImportedPromptPreview", ids);
                Assert.Contains("ImportedPromptTarget", ids);
                Assert.Contains("ApplyImportedPrompt", ids);
                Assert.Contains("SelectedPromptTargetSummary", ids);
                Assert.Contains($"{question.CardAutomationId}-ImportedPromptEvaluator", ids);
                Assert.Contains($"{question.CardAutomationId}-ImportedPromptSpecialEvaluation", ids);
                question.DisplayName = "改名後も同じ識別子";
                Render();
                Assert.NotNull(EvaluatorSelector(view, question));
                Assert.NotNull(SpecialSelector(view, question));
                Assert.DoesNotContain(ids, id => id.Contains("同じ設問名", StringComparison.Ordinal));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Compact_layout_keeps_long_prompt_scroll_local_and_exposes_accessible_keyboard_targets()
    {
        string content = string.Join('\n', Enumerable.Repeat("長い本文の確認 {回答} {評価項目}", 500)) + "\n本文の末尾";
        QuantificationDesignViewModel viewModel = CreateDesign(
            [new ImportedPrompt { Path = "long.txt", DisplayName = "long.txt", Content = content }]);
        ImportedPromptSettingsView view = new(viewModel);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            Assert.IsType<Grid>(view.Content);
            QuestionDesignItemViewModel question = viewModel.Questions[0];
            TextBox preview = Required<TextBox>(view, "ImportedPromptPreview");
            Control[] targets =
            [
                Required<ListBox>(view, "ImportedPromptList"),
                preview,
                Required<ComboBox>(view, "ImportedPromptQuestionSelector"),
                Required<ComboBox>(view, "ImportedPromptTargetSelector"),
                EvaluatorSelector(view, question),
                SpecialSelector(view, question),
                Required<Button>(view, "ApplyImportedPromptButton"),
            ];
            for (int index = 0; index < targets.Length; index++)
            {
                Control target = targets[index];
                Assert.Equal(index, target.TabIndex);
                Assert.True(target.Bounds.Height >= 44d);
                Assert.True(target.Bounds.Width >= 44d);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(target)));
                Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(target)?.ToString()));
                Point origin = target.TranslatePoint(default, view)!.Value;
                Assert.InRange(origin.X, 0d, view.Bounds.Width - target.Bounds.Width + 1d);
                Assert.InRange(origin.Y, 0d, view.Bounds.Height - target.Bounds.Height + 1d);
            }

            Assert.Equal(14d, preview.FontSize);
            Assert.Equal(content, preview.Text);
            ScrollViewer scroll = Assert.Single(preview.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            scroll.Offset = new Vector(0d, scroll.Extent.Height - scroll.Viewport.Height);
            Render();
            Assert.True(scroll.Offset.Y > 0d);
            Assert.Equal(content, preview.Text);
            Assert.Contains(view.GetVisualDescendants(), control => control is VirtualizingStackPanel);
            Assert.DoesNotContain(preview.GetVisualAncestors(), ancestor => ancestor is ScrollViewer);

            Assert.True(preview.Focus(NavigationMethod.Tab));
            Press(window, Key.Tab);
            Assert.Same(targets[2], window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Rebinding_uses_only_the_current_root_design_context()
    {
        QuantificationDesignViewModel previous = CreateDesign();
        QuantificationDesignViewModel current = CreateDesign([]);
        QuantificationDefinition before = previous.Draft;
        ImportedPromptSettingsView view = new(previous);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            view.DataContext = current;
            Render();
            Assert.Same(current, view.ViewModel);
            Assert.Same(current.ImportedPrompts, Required<ListBox>(view, "ImportedPromptList").ItemsSource);
            Assert.Same(current.ApplyImportedPromptCommand, Required<Button>(view, "ApplyImportedPromptButton").Command);
            Assert.False(Required<Button>(view, "ApplyImportedPromptButton").IsEffectivelyEnabled);
            Assert.Equal("読込Prompt 0 件", Required<TextBlock>(view, "ImportedPromptCountText").Text);
            string? status = Required<TextBlock>(view, "ImportedPromptApplyStatus").Text;

            previous.SelectedImportedPrompt = previous.ImportedPrompts[1];
            previous.SelectedQuestion = previous.Questions[1];
            Render();

            Assert.Equal(status, Required<TextBlock>(view, "ImportedPromptApplyStatus").Text);
            Assert.Same(current.Questions[0], Required<ComboBox>(view, "ImportedPromptQuestionSelector").SelectedItem);
            Assert.Same(before, previous.Draft);
        }
        finally
        {
            window.Close();
        }
    }

    private static QuantificationDesignViewModel CreateDesign(IReadOnlyList<ImportedPrompt>? prompts = null)
    {
        QuantificationDefinition seed = new QuantificationDesignViewModel().Draft;
        QuestionDefinition question = seed.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        CriterionDefinition criterion = evaluator.Criteria[0];
        QuantificationDesignViewModel viewModel = new(seed with
        {
            Id = "definition-import",
            Questions = [.. Enumerable.Range(1, 2).Select(questionIndex => question with
            {
                Id = $"question-import-{questionIndex}",
                DisplayName = $"設問 {questionIndex}",
                Points = 20m,
                Evaluators = [.. Enumerable.Range(0, 3).Select(evaluatorIndex => evaluator with
                {
                    Id = $"evaluator-import-{questionIndex}-{evaluatorIndex}",
                    DisplayName = $"評価方法 {evaluatorIndex}",
                    Type = evaluatorIndex == 0 ? EvaluatorType.KnowledgeCoverage : EvaluatorType.CustomPrompt,
                    BuiltInTemplateVersion = evaluatorIndex == 0 ? evaluator.BuiltInTemplateVersion : null,
                    CustomPromptTemplate = evaluatorIndex == 0 ? null : $"元の通常Prompt {questionIndex}-{evaluatorIndex} {{回答}} {{評価項目}}",
                    Criteria = [criterion with { Id = $"criterion-import-{questionIndex}-{evaluatorIndex}" }],
                })],
                SpecialEvaluations = [.. Enumerable.Range(1, 2).Select(specialIndex => new SpecialEvaluationDefinition
                {
                    Id = $"special-import-{questionIndex}-{specialIndex}",
                    DisplayName = $"固有評価 {specialIndex}",
                    PrimarySourceColumn = "B",
                    SupportingSourceColumns = ["C"],
                    PromptTemplate = $"元の固有Prompt {questionIndex}-{specialIndex} {{回答}}",
                    Enabled = true,
                })],
            })],
        }, ["A", "B", "C"], prompts ??
        [
            new ImportedPrompt { Path = "synthetic-private/02-reuse.txt", DisplayName = "02-reuse.txt", Content = "共通の原文\n{回答} {評価項目}" },
            new ImportedPrompt { Path = "synthetic-private/01-reuse.txt", DisplayName = "01-reuse.txt", Content = "次の原文 {回答} {評価項目}" },
            new ImportedPrompt { Path = "synthetic-private/other/01-reuse.txt", DisplayName = "01-reuse.txt", Content = "別の原文\r\n{回答} {評価項目} 😀\n末尾" },
        ]);
        foreach (QuestionDesignItemViewModel item in viewModel.Questions)
        {
            item.SelectedEvaluator = item.Evaluators[1];
            item.SelectedSpecialEvaluation = item.SpecialEvaluations[1];
        }

        return viewModel;
    }

    private static Window CreateWindow(ImportedPromptSettingsView view) =>
        new() { Width = 950, Height = 450, Content = view };

    private static void SelectQuestion(ImportedPromptSettingsView view, QuestionDesignItemViewModel question)
    {
        Required<ComboBox>(view, "ImportedPromptQuestionSelector").SetCurrentValue(ComboBox.SelectedItemProperty, question);
        Render();
        Assert.Same(question, view.ViewModel.SelectedQuestion);
    }

    private static void SelectTarget(ImportedPromptSettingsView view, ImportedPromptTarget target)
    {
        Required<ComboBox>(view, "ImportedPromptTargetSelector").SetCurrentValue(ComboBox.SelectedItemProperty,
            view.ViewModel.AvailablePromptTargets.Single(choice => choice.Target == target));
        Render();
        Assert.Equal(target, view.ViewModel.SelectedPromptTarget);
    }

    private static ComboBox EvaluatorSelector(ImportedPromptSettingsView view, QuestionDesignItemViewModel question) =>
        Assert.Single(view.GetVisualDescendants().OfType<ComboBox>(), control =>
            AutomationProperties.GetAutomationId(control) == $"{question.CardAutomationId}-ImportedPromptEvaluator");

    private static ComboBox SpecialSelector(ImportedPromptSettingsView view, QuestionDesignItemViewModel question) =>
        Assert.Single(view.GetVisualDescendants().OfType<ComboBox>(), control =>
            AutomationProperties.GetAutomationId(control) == $"{question.CardAutomationId}-ImportedPromptSpecialEvaluation");

    private static void Apply(Window window, ImportedPromptSettingsView view)
    {
        Button button = Required<Button>(view, "ApplyImportedPromptButton");
        Assert.Same(view.ViewModel.ApplyImportedPromptCommand, button.Command);
        Assert.True(button.IsEffectivelyEnabled);
        Assert.True(button.Focus(NavigationMethod.Tab));
        Press(window, Key.Enter);
        Render();
    }

    private static void AssertDefinition(QuantificationDefinition expected, QuantificationDefinition actual)
    {
        CanonicalDefinitionSerializer serializer = new();
        Assert.Equal(serializer.Serialize(expected), serializer.Serialize(actual));
    }

    private static T Required<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(root.FindControl<T>(name));

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(TopLevel window, Key key)
    {
        PhysicalKey physicalKey = key == Key.Tab ? PhysicalKey.Tab : PhysicalKey.Enter;
        window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
        Dispatcher.UIThread.RunJobs();
    }
}