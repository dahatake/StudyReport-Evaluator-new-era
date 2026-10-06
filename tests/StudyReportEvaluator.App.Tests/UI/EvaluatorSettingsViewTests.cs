using System.Windows.Input;
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
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-051 (AC-052)
public sealed class EvaluatorSettingsViewTests
{
    [AvaloniaFact]
    public void Parameterless_view_inherits_the_host_owner_without_creating_a_draft()
    {
        EvaluatorSettingsView view = new();
        Assert.Null(view.DataContext);
        Assert.Throws<InvalidOperationException>(() => view.ViewModel);
        Assert.Throws<ArgumentNullException>(() => new EvaluatorSettingsView(null!));
        QuantificationDesignViewModel owner = CreateDesign();
        QuantificationDefinition draft = owner.Draft;
        Window window = CreateWindow(view);
        window.DataContext = owner;
        try
        {
            window.Show();
            Render();

            Assert.Same(owner, view.DataContext);
            Assert.Same(owner, view.ViewModel);
            Assert.Same(draft, owner.Draft);
            AssertSelectors(view, owner);
            Assert.Equal("EvaluatorSettingsView", AutomationProperties.GetAutomationId(view));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Selected_target_edits_reach_only_its_nested_draft_and_survive_target_round_trips()
    {
        QuantificationDesignViewModel owner = CreateDesign();
        QuestionDesignItemViewModel firstQuestion = owner.Questions[0];
        EvaluatorDesignItemViewModel firstEvaluator = firstQuestion.SelectedEvaluator!;
        CriterionDesignItemViewModel firstCriterion = firstEvaluator.SelectedCriterion!;
        QuestionDesignItemViewModel question = owner.Questions[1];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[1];
        QuantificationDefinition before = owner.Draft;
        EvaluatorSettingsView view = new(owner);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            Select(view, "QuestionSelector", question);
            Select(view, "EvaluatorSelector", evaluator);
            Select(view, "CriterionSelector", criterion);
            AssertSelectors(view, owner);
            Assert.Same(evaluator, question.SelectedEvaluator);
            Assert.Same(criterion, evaluator.SelectedCriterion);

            Edit(view, $"DesignEvaluator-{evaluator.Id}-Name", "通常評価を編集");
            Edit(view, $"DesignEvaluator-{evaluator.Id}-Weight", "2.125");
            Edit(view, $"DesignEvaluator-{evaluator.Id}-Minimum", "-1.25");
            Edit(view, $"DesignEvaluator-{evaluator.Id}-Maximum", "5.125");
            SetChecked(view, $"DesignEvaluator-{evaluator.Id}-Enabled", false);
            Edit(view, criterion.NameAutomationId, "編集した知識ポイント");
            Edit(view, $"DesignCriterion-{criterion.Id}-Description", "説明・関係・適用\n二行目も保持 🧪");
            Edit(view, $"DesignCriterion-{criterion.Id}-Weight", "0.375");
            SetChecked(view, criterion.RangeAutomationId, true);
            Edit(view, $"DesignCriterion-{criterion.Id}-Minimum", "0.25");
            Edit(view, $"DesignCriterion-{criterion.Id}-Maximum", "3.75");
            SetChecked(view, $"DesignCriterion-{criterion.Id}-Enabled", false);

            QuestionDefinition oldQuestion = before.Questions[1];
            EvaluatorDefinition oldEvaluator = oldQuestion.Evaluators[1];
            CriterionDefinition expectedCriterion = oldEvaluator.Criteria[1] with
            {
                DisplayName = "編集した知識ポイント",
                Description = "説明・関係・適用\n二行目も保持 🧪",
                Weight = 0.375m,
                Range = new ScoreRange(0.25m, 3.75m),
                Enabled = false,
            };
            EvaluatorDefinition expectedEvaluator = oldEvaluator with
            {
                DisplayName = "通常評価を編集",
                Weight = 2.125m,
                Range = new ScoreRange(-1.25m, 5.125m),
                Enabled = false,
                Criteria = oldEvaluator.Criteria.SetItem(1, expectedCriterion),
            };
            QuantificationDefinition expected = before with
            {
                Questions = before.Questions.SetItem(1, oldQuestion with
                {
                    Evaluators = oldQuestion.Evaluators.SetItem(1, expectedEvaluator),
                }),
            };
            AssertCanonical(expected, owner.Draft);

            Select(view, "QuestionSelector", firstQuestion);
            Assert.Same(firstEvaluator, firstQuestion.SelectedEvaluator);
            Assert.Same(firstCriterion, firstEvaluator.SelectedCriterion);
            Select(view, "QuestionSelector", question);
            Assert.Same(evaluator, question.SelectedEvaluator);
            Assert.Same(criterion, evaluator.SelectedCriterion);
            Assert.Equal("通常評価を編集", ById<TextBox>(view, $"DesignEvaluator-{evaluator.Id}-Name").Text);
            Assert.Equal("編集した知識ポイント", ById<TextBox>(view, criterion.NameAutomationId).Text);
            AssertCanonical(expected, owner.Draft);

            SetChecked(view, criterion.RangeAutomationId, false);
            Assert.Null(owner.Draft.Questions[1].Evaluators[1].Criteria[1].Range);
            Assert.False(ById<TextBox>(view, $"DesignCriterion-{criterion.Id}-Minimum").IsEffectivelyEnabled);
            Assert.False(ById<TextBox>(view, $"DesignCriterion-{criterion.Id}-Maximum").IsEffectivelyEnabled);
            Edit(view, $"DesignEvaluator-{evaluator.Id}-Maximum", "6.5");
            Assert.Equal(new ScoreRange(-1.25m, 6.5m), criterion.EffectiveRange);
            Assert.Equal("6.5", ById<TextBox>(view, $"DesignCriterion-{criterion.Id}-Maximum").Text);
            Assert.Null(owner.Draft.Questions[1].Evaluators[1].Criteria[1].Range);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Evaluator_buttons_add_both_types_duplicate_reorder_and_delete_through_owner_commands()
    {
        QuantificationDesignViewModel owner = CreateDesign(evaluatorCount: 1, criterionCount: 1);
        QuantificationDefinition before = owner.Draft;
        QuestionDesignItemViewModel question = owner.Questions[1];
        EvaluatorDesignItemViewModel original = question.Evaluators[0];
        EvaluatorSettingsView view = new(owner);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            Select(view, "QuestionSelector", question);
            Assert.Same(question, owner.SelectedQuestion);
            Assert.False(ById<Button>(view, $"DesignEvaluator-{original.Id}-MoveUp").IsEffectivelyEnabled);
            Assert.False(ById<Button>(view, $"DesignEvaluator-{original.Id}-MoveDown").IsEffectivelyEnabled);
            AddEvaluator(view, question, custom: false);
            Assert.Equal(EvaluatorType.KnowledgeCoverage, question.Evaluators[^1].Type);
            AddEvaluator(view, question, custom: true);
            Assert.Equal(3, question.Evaluators.Count);
            EvaluatorDesignItemViewModel custom = question.Evaluators[^1];
            Assert.Equal(EvaluatorType.CustomPrompt, custom.Type);
            Assert.Same(original, question.SelectedEvaluator);
            Select(view, "EvaluatorSelector", custom);
            QuantificationDefinition afterAdd = owner.Draft;

            Button duplicate = ById<Button>(view, $"DesignEvaluator-{custom.Id}-Duplicate");
            Assert.Same(custom.DuplicateCommand, duplicate.Command);
            Activate(duplicate);
            EvaluatorDesignItemViewModel copy = question.Evaluators[^1];
            Assert.Equal(4, question.Evaluators.Count);
            Assert.NotEqual(custom.Id, copy.Id);
            Assert.NotEqual(custom.Criteria[0].Id, copy.Criteria[0].Id);
            Assert.Equal(custom.CustomPromptTemplate, copy.CustomPromptTemplate);
            QuantificationDefinition withCopy = owner.Draft;
            EvaluatorDefinition sourceDefinition = afterAdd.Questions[1].Evaluators[2];
            EvaluatorDefinition copyDefinition = withCopy.Questions[1].Evaluators[3];
            Assert.Equal(sourceDefinition, copyDefinition with
            {
                Id = sourceDefinition.Id,
                Criteria = sourceDefinition.Criteria,
            });
            Assert.Equal(sourceDefinition.Criteria[0], Assert.Single(copyDefinition.Criteria) with
            {
                Id = sourceDefinition.Criteria[0].Id,
            });
            AssertCanonical(afterAdd, withCopy with
            {
                Questions = withCopy.Questions.SetItem(1, withCopy.Questions[1].RemoveEvaluator(3)),
            });
            Select(view, "EvaluatorSelector", copy);
            CriterionDesignItemViewModel copiedCriterion = copy.SelectedCriterion!;
            Activate(ById<Button>(view, $"DesignEvaluator-{copy.Id}-MoveUp"));
            Assert.Same(copy, question.Evaluators[2]);
            Assert.Same(copy, question.SelectedEvaluator);
            Assert.Same(copiedCriterion, copy.SelectedCriterion);
            AssertSelectors(view, owner);
            AssertCanonical(withCopy with
            {
                Questions = withCopy.Questions.SetItem(1, withCopy.Questions[1].MoveEvaluator(3, 2)),
            }, owner.Draft);
            Activate(ById<Button>(view, $"DesignEvaluator-{copy.Id}-MoveDown"));
            Assert.Same(copy, question.Evaluators[3]);
            AssertSelectors(view, owner);
            AssertCanonical(withCopy, owner.Draft);
            Activate(ById<Button>(view, $"DesignEvaluator-{copy.Id}-Delete"));
            Assert.Equal(3, question.Evaluators.Count);
            Assert.DoesNotContain(question.Evaluators, item => item.Id == copy.Id);
            Assert.Same(custom, question.SelectedEvaluator);
            AssertSelectors(view, owner);
            AssertCanonical(afterAdd, owner.Draft);
            AssertCanonical(before, owner.Draft with
            {
                Questions = owner.Draft.Questions.SetItem(1, owner.Draft.Questions[1] with
                {
                    Evaluators = before.Questions[1].Evaluators,
                }),
            });
            AssertUniqueIds(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Criterion_buttons_preserve_ids_selection_and_neighbor_fallback()
    {
        QuantificationDesignViewModel owner = CreateDesign();
        QuantificationDefinition before = owner.Draft;
        QuestionDesignItemViewModel question = owner.Questions[1];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        CriterionDesignItemViewModel original = evaluator.Criteria[1];
        CriterionDesignItemViewModel next = evaluator.Criteria[2];
        EvaluatorSettingsView view = new(owner);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            Select(view, "QuestionSelector", question);
            Select(view, "EvaluatorSelector", evaluator);
            Select(view, "CriterionSelector", original);
            Button add = Required<Button>(view, "AddCriterionButton");
            Assert.Same(evaluator.AddCriterionCommand, add.Command);
            Activate(add);
            Assert.Equal(4, evaluator.Criteria.Count);
            Assert.Same(original, evaluator.SelectedCriterion);
            QuantificationDefinition afterAdd = owner.Draft;
            Button duplicate = ById<Button>(view, $"DesignCriterion-{original.Id}-Duplicate");
            Assert.Same(original.DuplicateCommand, duplicate.Command);
            Activate(duplicate);
            CriterionDesignItemViewModel copy = evaluator.Criteria[2];
            Assert.Equal(5, evaluator.Criteria.Count);
            Assert.NotEqual(original.Id, copy.Id);
            Assert.Equal(original.Description, copy.Description);
            QuantificationDefinition withCopy = owner.Draft;
            EvaluatorDefinition copiedEvaluator = withCopy.Questions[1].Evaluators[1];
            Assert.Equal(afterAdd.Questions[1].Evaluators[1].Criteria[1], copiedEvaluator.Criteria[2] with
            {
                Id = original.Id,
            });
            AssertCanonical(afterAdd, withCopy with
            {
                Questions = withCopy.Questions.SetItem(1, withCopy.Questions[1] with
                {
                    Evaluators = withCopy.Questions[1].Evaluators.SetItem(1, copiedEvaluator.RemoveCriterion(2)),
                }),
            });
            Select(view, "CriterionSelector", copy);
            Activate(ById<Button>(view, $"DesignCriterion-{copy.Id}-MoveUp"));
            Assert.Same(copy, evaluator.Criteria[1]);
            Assert.Same(copy, evaluator.SelectedCriterion);
            AssertSelectors(view, owner);
            AssertCanonical(withCopy with
            {
                Questions = withCopy.Questions.SetItem(1, withCopy.Questions[1] with
                {
                    Evaluators = withCopy.Questions[1].Evaluators.SetItem(1, copiedEvaluator.MoveCriterion(2, 1)),
                }),
            }, owner.Draft);
            Activate(ById<Button>(view, $"DesignCriterion-{copy.Id}-MoveDown"));
            Assert.Same(copy, evaluator.Criteria[2]);
            AssertSelectors(view, owner);
            AssertCanonical(withCopy, owner.Draft);
            Activate(ById<Button>(view, $"DesignCriterion-{copy.Id}-Delete"));
            Assert.DoesNotContain(evaluator.Criteria, item => item.Id == copy.Id);
            Assert.Same(next, evaluator.SelectedCriterion);
            AssertSelectors(view, owner);
            AssertCanonical(afterAdd, owner.Draft);
            Assert.Equal(evaluator.Criteria.Select(item => item.Id), owner.Draft.Questions[1].Evaluators[1].Criteria.Select(item => item.Id));
            AssertCanonical(before, owner.Draft with
            {
                Questions = owner.Draft.Questions.SetItem(1, owner.Draft.Questions[1] with
                {
                    Evaluators = owner.Draft.Questions[1].Evaluators.SetItem(1, owner.Draft.Questions[1].Evaluators[1] with
                    {
                        Criteria = before.Questions[1].Evaluators[1].Criteria,
                    }),
                }),
            });
            AssertUniqueIds(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Knowledge_is_read_only_and_Custom_retains_full_text_and_placeholder_validation()
    {
        QuantificationDesignViewModel owner = CreateDesign();
        EvaluatorDesignItemViewModel evaluator = owner.Questions[0].Evaluators[0];
        QuantificationDefinition before = owner.Draft;
        EvaluatorSettingsView view = new(owner);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            ShowPrompt(view);
            TextBox knowledge = ById<TextBox>(view, evaluator.KnowledgePromptPreviewAutomationId);
            Assert.True(IsShown(knowledge));
            Assert.True(knowledge.IsReadOnly);
            Assert.Equal(evaluator.PromptPreview, knowledge.Text);
            Assert.Contains("APP-OWNED STRUCTURED OUTPUT CONTRACT", knowledge.Text!, StringComparison.Ordinal);
            Assert.False(IsShown(ById<TextBox>(view, evaluator.PromptAutomationId)));
            Assert.False(IsShown(ById<TextBox>(view, evaluator.CustomPromptPreviewAutomationId)));
            string knowledgeText = knowledge.Text!;
            Assert.True(knowledge.Focus(NavigationMethod.Tab));
            window.KeyTextInput("書き換えない");
            Render();
            Assert.Equal(knowledgeText, knowledge.Text);
            Assert.Null(evaluator.CustomPromptTemplate);
            Assert.Same(before, owner.Draft);

            ShowBasic(view);
            ComboBox type = ById<ComboBox>(view, evaluator.TypeAutomationId);
            type.SetCurrentValue(ComboBox.SelectedItemProperty,
                evaluator.AvailableTypes.Single(item => item.Type == EvaluatorType.CustomPrompt));
            Render();
            Assert.True(evaluator.IsCustom);
            ShowPrompt(view);
            TextBox editor = ById<TextBox>(view, evaluator.PromptAutomationId);
            TextBox preview = ById<TextBox>(view, evaluator.CustomPromptPreviewAutomationId);
            Assert.False(editor.IsReadOnly);
            Assert.True(IsShown(editor));
            Assert.True(IsShown(preview));
            Assert.True(preview.IsReadOnly);
            Assert.False(IsShown(ById<TextBox>(view, evaluator.KnowledgePromptPreviewAutomationId)));
            string fullText = "先頭\r\nliteral {{brace}}\r\n" + new string('長', 12_000) + "\n{回答}\n{評価項目}\n末尾 🧪";
            editor.SetCurrentValue(TextBox.TextProperty, fullText);
            Render();
            Assert.Equal(fullText, evaluator.CustomPromptTemplate);
            Assert.Equal(fullText, owner.Draft.Questions[0].Evaluators[0].CustomPromptTemplate);
            Assert.Equal(fullText, editor.Text);
            Assert.Contains("literal {brace}", preview.Text!, StringComparison.Ordinal);
            Assert.Contains("【回答 preview】", preview.Text!, StringComparison.Ordinal);
            Assert.Contains("【評価項目 preview】", preview.Text!, StringComparison.Ordinal);
            Assert.Contains("末尾 🧪", preview.Text!, StringComparison.Ordinal);
            string previewText = preview.Text!;
            Assert.True(preview.Focus(NavigationMethod.Tab));
            window.KeyTextInput("プレビューを書き換えない");
            Render();
            Assert.Equal(previewText, preview.Text);
            Assert.Equal(fullText, editor.Text);
            Assert.Equal(fullText, evaluator.CustomPromptTemplate);

            Edit(view, evaluator.PromptAutomationId, "{評価項目}");
            Assert.Contains(owner.ValidationErrors, error => error.NodeId == evaluator.Id && error.Code == "ANSWER_PLACEHOLDER_REQUIRED");
            Edit(view, evaluator.PromptAutomationId, "{回答}");
            Assert.Contains(owner.ValidationErrors, error => error.NodeId == evaluator.Id && error.Code == "CRITERIA_PLACEHOLDER_REQUIRED");
            Assert.True(IsShown(ById<TextBlock>(view, $"DesignEvaluator-{evaluator.Id}-PromptValidation")));
            Assert.DoesNotContain("【回答 preview】", preview.Text ?? string.Empty, StringComparison.Ordinal);
            Edit(view, evaluator.PromptAutomationId, "{回答}\n{評価項目}\n{未対応}");
            Assert.Contains(owner.ValidationErrors, error => error.NodeId == evaluator.Id && error.Code == "UNKNOWN_PLACEHOLDER");
            Edit(view, evaluator.PromptAutomationId, fullText);
            Assert.DoesNotContain(owner.ValidationErrors, error => error.NodeId == evaluator.Id);

            ShowBasic(view);
            ById<ComboBox>(view, evaluator.TypeAutomationId).SetCurrentValue(ComboBox.SelectedItemProperty,
                evaluator.AvailableTypes.Single(item => item.Type == EvaluatorType.KnowledgeCoverage));
            Render();
            ShowPrompt(view);
            Assert.True(evaluator.IsKnowledge);
            Assert.Null(evaluator.CustomPromptTemplate);
            Assert.Equal(knowledgeText, ById<TextBox>(view, evaluator.KnowledgePromptPreviewAutomationId).Text);
            AssertCanonical(before, owner.Draft);
            AssertUniqueIds(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Missing_targets_show_messages_and_empty_children_can_be_added_again()
    {
        QuantificationDesignViewModel owner = new(CreateDesign().Draft with { Questions = [] });
        EvaluatorSettingsView view = new(owner);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            Assert.True(IsShown(Required<TextBlock>(view, "NoQuestionMessage")));
            Assert.Contains("設問を選択", Required<TextBlock>(view, "NoQuestionMessage").Text!, StringComparison.Ordinal);
            Assert.False(Required<Button>(view, "AddEvaluatorButton").IsEffectivelyEnabled);
            Assert.False(Required<Button>(view, "AddCriterionButton").IsEffectivelyEnabled);
            Assert.False(Required<TabControl>(view, "EditorTabs").IsVisible);

            // Question creation belongs to the host, not to this view.
            QuestionDesignItemViewModel question = owner.AddQuestion();
            Render();
            Assert.False(IsShown(Required<TextBlock>(view, "NoQuestionMessage")));
            EvaluatorDesignItemViewModel evaluator = question.Evaluators[0];
            CriterionDesignItemViewModel criterion = evaluator.Criteria[0];
            Activate(ById<Button>(view, $"DesignCriterion-{criterion.Id}-Delete"));
            Assert.Empty(evaluator.Criteria);
            Assert.True(IsShown(Required<TextBlock>(view, "NoCriterionMessage")));
            Assert.Contains("追加", Required<TextBlock>(view, "NoCriterionMessage").Text!, StringComparison.Ordinal);
            Assert.True(Required<Button>(view, "AddCriterionButton").IsEffectivelyEnabled);
            Activate(Required<Button>(view, "AddCriterionButton"));
            Assert.Same(Assert.Single(evaluator.Criteria), evaluator.SelectedCriterion);
            Assert.False(IsShown(Required<TextBlock>(view, "NoCriterionMessage")));

            question.SelectedEvaluator = null;
            Render();
            Assert.True(IsShown(Required<TextBlock>(view, "NoEvaluatorMessage")));
            Assert.Contains("選択するか", Required<TextBlock>(view, "NoEvaluatorMessage").Text!, StringComparison.Ordinal);
            Assert.False(Required<ComboBox>(view, "CriterionSelector").IsEffectivelyEnabled);
            Assert.False(Required<Button>(view, "AddCriterionButton").IsEffectivelyEnabled);
            Select(view, "EvaluatorSelector", evaluator);
            Activate(ById<Button>(view, $"DesignEvaluator-{evaluator.Id}-Delete"));
            Assert.Empty(question.Evaluators);
            Assert.True(IsShown(Required<TextBlock>(view, "NoEvaluatorMessage")));
            Assert.True(Required<Button>(view, "AddEvaluatorButton").IsEffectivelyEnabled);
            AddEvaluator(view, question, custom: true);
            Assert.Same(Assert.Single(question.Evaluators), question.SelectedEvaluator);
            AssertSelectors(view, owner);

            EvaluatorDesignItemViewModel selected = question.SelectedEvaluator!;
            owner.SelectedQuestion = null;
            Render();
            Assert.True(IsShown(Required<TextBlock>(view, "NoQuestionMessage")));
            Assert.Same(selected, question.SelectedEvaluator);
            Select(view, "QuestionSelector", question);
            Assert.Same(selected, question.SelectedEvaluator);
            AssertSelectors(view, owner);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Candidate_nulls_and_input_reorders_keep_owner_selection_and_compiled_bindings()
    {
        QuantificationDesignViewModel owner = CreateDesign();
        QuestionDesignItemViewModel question = owner.Questions[1];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[1];
        owner.SelectedQuestion = question;
        question.SelectedEvaluator = evaluator;
        evaluator.SelectedCriterion = criterion;
        EvaluatorSettingsView view = new(owner);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            SelectingItemsControl[] selectors = Selectors(view);
            BindingExpressionBase?[] bindings = selectors.Select(selector =>
                BindingOperations.GetBindingExpressionBase(selector, SelectingItemsControl.SelectedItemProperty)).ToArray();
            BindingExpressionBase?[] itemBindings = selectors.Select(selector =>
                BindingOperations.GetBindingExpressionBase(selector, ItemsControl.ItemsSourceProperty)).ToArray();
            Assert.All(bindings, binding => Assert.NotNull(binding));
            Assert.All(itemBindings, binding => Assert.NotNull(binding));
            List<(QuestionDesignItemViewModel? Question, EvaluatorDesignItemViewModel? Evaluator,
                CriterionDesignItemViewModel? Criterion)> observedSelections = [];
            foreach (SelectingItemsControl selector in selectors)
            {
                selector.SelectionChanged += (_, _) => observedSelections.Add(
                    (owner.SelectedQuestion, question.SelectedEvaluator, evaluator.SelectedCriterion));
            }

            QuantificationDefinition before = owner.Draft;
            foreach (SelectingItemsControl selector in selectors)
            {
                selector.SetCurrentValue(SelectingItemsControl.SelectedItemProperty, null);
            }

            Assert.Same(question, owner.SelectedQuestion);
            Assert.Same(evaluator, question.SelectedEvaluator);
            Assert.Same(criterion, evaluator.SelectedCriterion);
            Assert.Same(before, owner.Draft);
            QuestionDefinition nextQuestion = before.Questions[1];
            EvaluatorDefinition nextEvaluator = nextQuestion.Evaluators[1].MoveCriterion(1, 0);
            nextQuestion = (nextQuestion with
            {
                Evaluators = nextQuestion.Evaluators.SetItem(1, nextEvaluator),
            }).MoveEvaluator(1, 0);
            QuantificationDefinition incoming = (before with
            {
                Questions = before.Questions.SetItem(1, nextQuestion),
            }).MoveQuestion(1, 0);

            owner.SynchronizeFromInput(incoming, ["B", "A"]);
            Render();

            Assert.Same(question, owner.SelectedQuestion);
            Assert.Same(evaluator, question.SelectedEvaluator);
            Assert.Same(criterion, evaluator.SelectedCriterion);
            Assert.Same(question, owner.Questions[0]);
            Assert.Same(evaluator, question.Evaluators[0]);
            Assert.Same(criterion, evaluator.Criteria[0]);
            AssertSelectors(view, owner);
            AssertCanonical(incoming, owner.Draft);
            for (int index = 0; index < selectors.Length; index++)
            {
                Assert.Same(bindings[index], BindingOperations.GetBindingExpressionBase(selectors[index], SelectingItemsControl.SelectedItemProperty));
                Assert.Same(itemBindings[index], BindingOperations.GetBindingExpressionBase(selectors[index], ItemsControl.ItemsSourceProperty));
            }

            question.MoveDownCommand.Execute(null);
            Render();
            AssertSelectors(view, owner);
            AssertCanonical(incoming.MoveQuestion(0, 1), owner.Draft);
            Assert.NotEmpty(observedSelections);
            Assert.All(observedSelections, selection =>
            {
                Assert.Same(question, selection.Question);
                Assert.Same(evaluator, selection.Evaluator);
                Assert.Same(criterion, selection.Criterion);
            });
            evaluator.SelectedCriterion = null;
            Render();
            Assert.Null(Required<ComboBox>(view, "CriterionSelector").SelectedItem);
            Assert.True(IsShown(Required<TextBlock>(view, "NoCriterionMessage")));
            Select(view, "CriterionSelector", criterion);
            AssertSelectors(view, owner);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Input_sync_replaces_all_target_ids_then_clears_editors_without_retaining_retired_targets()
    {
        QuantificationDesignViewModel owner = CreateDesign();
        QuestionDesignItemViewModel retiredQuestion = owner.Questions[1];
        EvaluatorDesignItemViewModel retiredEvaluator = retiredQuestion.Evaluators[1];
        CriterionDesignItemViewModel retiredCriterion = retiredEvaluator.Criteria[1];
        EvaluatorSettingsView view = new(owner);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            Select(view, "QuestionSelector", retiredQuestion);
            Select(view, "EvaluatorSelector", retiredEvaluator);
            Select(view, "CriterionSelector", retiredCriterion);
            TextBox evaluatorEditor = ById<TextBox>(view, $"DesignEvaluator-{retiredEvaluator.Id}-Name");
            TextBox criterionEditor = ById<TextBox>(view, retiredCriterion.NameAutomationId);
            QuestionDefinition sourceQuestion = owner.Draft.Questions[1];
            EvaluatorDefinition sourceEvaluator = sourceQuestion.Evaluators[1];
            QuantificationDefinition incoming = owner.Draft with
            {
                Questions = [sourceQuestion with
                {
                    Id = "incoming-question",
                    DisplayName = "入替え後の設問",
                    Points = 40m,
                    Evaluators = [sourceEvaluator with
                    {
                        Id = "incoming-evaluator",
                        DisplayName = "入替え後の評価方法",
                        Criteria = [sourceEvaluator.Criteria[1] with
                        {
                            Id = "incoming-criterion",
                            DisplayName = "入替え後の評価項目",
                        }],
                    }],
                }],
            };

            owner.SynchronizeFromInput(incoming, owner.AvailableColumnNames);
            Render();

            QuestionDesignItemViewModel question = Assert.Single(owner.Questions);
            EvaluatorDesignItemViewModel evaluator = Assert.Single(question.Evaluators);
            CriterionDesignItemViewModel criterion = Assert.Single(evaluator.Criteria);
            Assert.NotSame(retiredQuestion, question);
            Assert.NotSame(retiredEvaluator, evaluator);
            Assert.NotSame(retiredCriterion, criterion);
            Assert.Same(question, owner.SelectedQuestion);
            Assert.Same(evaluator, question.SelectedEvaluator);
            Assert.Same(criterion, evaluator.SelectedCriterion);
            AssertSelectors(view, owner);
            Assert.Same(evaluatorEditor, ById<TextBox>(view, $"DesignEvaluator-{evaluator.Id}-Name"));
            Assert.Same(criterionEditor, ById<TextBox>(view, criterion.NameAutomationId));
            Assert.Equal("入替え後の評価方法", evaluatorEditor.Text);
            Assert.Equal("入替え後の評価項目", criterionEditor.Text);
            AssertCanonical(incoming, owner.Draft);
            AssertUniqueIds(view);
            ShowPrompt(view);
            Assert.Equal(evaluator.CustomPromptTemplate, ById<TextBox>(view, evaluator.PromptAutomationId).Text);
            Assert.Equal(evaluator.PromptPreview, ById<TextBox>(view, evaluator.CustomPromptPreviewAutomationId).Text);
            AssertUniqueIds(view);

            retiredQuestion.SelectedEvaluator = null;
            retiredEvaluator.SelectedCriterion = null;
            Render();
            AssertSelectors(view, owner);
            AssertCanonical(incoming, owner.Draft);

            QuantificationDefinition empty = incoming with { Questions = [] };
            owner.SynchronizeFromInput(empty, owner.AvailableColumnNames);
            Render();
            AssertSelectors(view, owner);
            Assert.True(IsShown(Required<TextBlock>(view, "NoQuestionMessage")));
            Assert.False(Required<TabControl>(view, "EditorTabs").IsVisible);
            Assert.False(Required<Button>(view, "AddEvaluatorButton").IsEffectivelyEnabled);
            Assert.False(Required<Button>(view, "AddCriterionButton").IsEffectivelyEnabled);
            AssertCanonical(empty, owner.Draft);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Data_context_replacement_detach_and_reattach_follow_only_the_current_owner()
    {
        QuantificationDesignViewModel original = CreateDesign();
        QuantificationDesignViewModel replacement = CreateDesign();
        EvaluatorSettingsView view = new(original);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            view.DataContext = replacement;
            Select(view, "QuestionSelector", replacement.Questions[1]);
            Select(view, "EvaluatorSelector", replacement.Questions[1].Evaluators[1]);
            Select(view, "CriterionSelector", replacement.Questions[1].Evaluators[1].Criteria[1]);
            QuantificationDefinition replacementDraft = replacement.Draft;
            original.Questions[0].MoveDownCommand.Execute(null);
            original.Questions[0].SelectedEvaluator = null;
            Render();
            Assert.Same(replacement, view.ViewModel);
            AssertSelectors(view, replacement);
            Assert.Same(replacementDraft, replacement.Draft);

            window.Content = null;
            replacement.SelectedQuestion = replacement.Questions[0];
            replacement.Questions[0].SelectedEvaluator = replacement.Questions[0].Evaluators[2];
            window.Content = view;
            Render();
            AssertSelectors(view, replacement);
            EvaluatorDesignItemViewModel selected = replacement.SelectedQuestion!.SelectedEvaluator!;
            selected.MoveUpCommand.Execute(null);
            Render();
            Assert.Same(selected, replacement.SelectedQuestion.SelectedEvaluator);
            AssertSelectors(view, replacement);
            Edit(view, $"DesignEvaluator-{selected.Id}-Name", "再接続後の編集");
            Assert.Equal("再接続後の編集", replacement.Draft.Questions[0].Evaluators[1].DisplayName);
            Assert.DoesNotContain(original.Draft.Questions.SelectMany(item => item.Evaluators), item => item.DisplayName == "再接続後の編集");
            view.DataContext = null;
            Render();
            Assert.Null(view.DataContext);
            Assert.True(IsShown(Required<TextBlock>(view, "NoQuestionMessage")));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Multiple_validation_errors_stay_bounded_and_keep_the_full_accessible_message()
    {
        QuantificationDesignViewModel owner = CreateDesign();
        QuestionDesignItemViewModel question = owner.Questions[0];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        question.SelectedEvaluator = evaluator;
        CriterionDesignItemViewModel criterion = evaluator.Criteria[0];
        evaluator.DisplayName = string.Empty;
        evaluator.Weight = 0m;
        evaluator.Minimum = evaluator.Maximum;
        evaluator.CustomPromptTemplate = "{回答}";
        criterion.DisplayName = string.Empty;
        criterion.Weight = 0m;
        criterion.HasCustomRange = true;
        Assert.False(owner.IsValid);
        Assert.Contains(Environment.NewLine, evaluator.ValidationText, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine, criterion.ValidationText, StringComparison.Ordinal);
        EvaluatorSettingsView view = new(owner);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            AssertInputContract(view);
            TextBlock evaluatorErrors = ById<TextBlock>(view, $"DesignEvaluator-{evaluator.Id}-Validation");
            TextBlock criterionErrors = ById<TextBlock>(view, $"DesignCriterion-{criterion.Id}-Validation");
            Assert.Equal(1, evaluatorErrors.MaxLines);
            Assert.Equal(1, criterionErrors.MaxLines);
            Assert.Equal(evaluator.ValidationText, AutomationProperties.GetName(evaluatorErrors));
            Assert.Equal(criterion.ValidationText, AutomationProperties.GetName(criterionErrors));
            Assert.Equal(evaluator.ValidationText, ToolTip.GetTip(evaluatorErrors));
            Assert.Equal(criterion.ValidationText, ToolTip.GetTip(criterionErrors));
            ShowPrompt(view);
            AssertInputContract(view);
            TextBlock promptErrors = ById<TextBlock>(view, $"DesignEvaluator-{evaluator.Id}-PromptValidation");
            Assert.Equal(1, promptErrors.MaxLines);
            Assert.Equal(evaluator.ValidationText, AutomationProperties.GetName(promptErrors));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(2d)]
    public void Finite_slot_large_context_keeps_one_editor_and_accessible_44_DIP_inputs(double scaling)
    {
        QuantificationDesignViewModel owner = CreateDesign(10, 5, 20, longContent: true);
        QuestionDesignItemViewModel question = owner.Questions[^1];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[^1];
        owner.SelectedQuestion = question;
        question.SelectedEvaluator = evaluator;
        evaluator.SelectedCriterion = criterion;
        criterion.HasCustomRange = true;
        QuantificationDefinition before = owner.Draft;
        EvaluatorSettingsView view = new(owner);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            window.SetRenderScaling(scaling);
            Render();
            Assert.Equal(1_000, owner.Questions.Sum(item => item.Evaluators.Sum(child => child.Criteria.Count)));
            Assert.True(owner.IsValid);
            Assert.InRange(view.Bounds.Width, 900d, 950d);
            Assert.InRange(view.Bounds.Height, 400d, 450d);
            Assert.InRange(view.GetVisualDescendants().OfType<TextBox>().Count(IsShown), 9, 12);
            Assert.Single(view.GetVisualDescendants().OfType<Border>(), border => AutomationProperties.GetAutomationId(border) == evaluator.CardAutomationId);
            Assert.Single(view.GetVisualDescendants().OfType<Border>(), border => AutomationProperties.GetAutomationId(border) == criterion.CardAutomationId);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBox>(), control =>
                AutomationProperties.GetAutomationId(control) == owner.Questions[0].Evaluators[0].Criteria[0].NameAutomationId);
            AssertUniqueIds(view);
            AssertInputContract(view);
            Assert.All(Selectors(view).OfType<ComboBox>(), selector => Assert.InRange(selector.MaxDropDownHeight, 44d, 220d));

            ListBox questions = Required<ListBox>(view, "QuestionSelector");
            Assert.True(questions.Focus(NavigationMethod.Tab));
            Press(window, Key.Tab);
            Assert.Same(Required<ComboBox>(view, "EvaluatorSelector"), window.FocusManager?.GetFocusedElement());
            TextBox name = ById<TextBox>(view, criterion.NameAutomationId);
            Assert.True(name.Focus(NavigationMethod.Tab));
            Assert.Equal(new Thickness(3d), name.BorderThickness);
            Press(window, Key.Tab);
            Assert.Same(ById<TextBox>(view, $"DesignCriterion-{criterion.Id}-Description"), window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(name, window.FocusManager?.GetFocusedElement());

            ShowPrompt(view);
            AssertInputContract(view);
            TextBox editor = ById<TextBox>(view, evaluator.PromptAutomationId);
            TextBox preview = ById<TextBox>(view, evaluator.CustomPromptPreviewAutomationId);
            Assert.Equal(evaluator.CustomPromptTemplate, editor.Text);
            Assert.True(editor.Focus(NavigationMethod.Tab));
            Assert.Equal(new Thickness(3d), editor.BorderThickness);
            Press(window, Key.Tab);
            Assert.Same(preview, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(editor, window.FocusManager?.GetFocusedElement());
            AssertLocalScroll(editor);
            AssertLocalScroll(preview);
            AssertUniqueIds(view);
            Assert.Same(before, owner.Draft);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Opening_selecting_and_preview_are_passive_and_expose_only_owner_CRUD_not_AI_commands()
    {
        QuantificationDesignViewModel owner = CreateDesign();
        QuantificationDefinition before = owner.Draft;
        EvaluatorSettingsView view = new(owner);
        Window window = CreateWindow(view);
        try
        {
            window.Show();
            Render();
            QuestionDesignItemViewModel question = owner.SelectedQuestion!;
            EvaluatorDesignItemViewModel evaluator = question.SelectedEvaluator!;
            CriterionDesignItemViewModel criterion = evaluator.SelectedCriterion!;
            ICommand[] allowed =
            [
                evaluator.AddCriterionCommand, evaluator.DuplicateCommand, evaluator.MoveUpCommand,
                evaluator.MoveDownCommand, evaluator.DeleteCommand,
                criterion.DuplicateCommand, criterion.MoveUpCommand, criterion.MoveDownCommand, criterion.DeleteCommand,
            ];
            Button[] actions = view.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Command is not null).ToArray();
            Assert.Equal(allowed.Length, actions.Length);
            Assert.All(actions, button => Assert.Contains(button.Command!, allowed));
            Activate(Required<Button>(view, "AddEvaluatorButton"));
            Flyout flyout = Assert.IsType<Flyout>(Required<Button>(view, "AddEvaluatorButton").Flyout);
            StackPanel additions = Assert.IsType<StackPanel>(flyout.Content);
            Assert.Same(question.AddKnowledgeEvaluatorCommand, ById<Button>(additions, $"DesignQuestion-{question.Id}-AddKnowledgeEvaluator").Command);
            Assert.Same(question.AddCustomEvaluatorCommand, ById<Button>(additions, $"DesignQuestion-{question.Id}-AddCustomEvaluator").Command);
            flyout.Hide();
            Render();

            foreach (QuestionDesignItemViewModel targetQuestion in owner.Questions)
            {
                Select(view, "QuestionSelector", targetQuestion);
                foreach (EvaluatorDesignItemViewModel targetEvaluator in targetQuestion.Evaluators)
                {
                    Select(view, "EvaluatorSelector", targetEvaluator);
                    ShowPrompt(view);
                    Assert.Equal(targetEvaluator.PromptPreview, ById<TextBox>(view, targetEvaluator.PromptPreviewAutomationId).Text);
                    ShowBasic(view);
                }
            }

            Assert.Same(before, owner.Draft);
            AssertCanonical(before, owner.Draft);
            // No execution/service composition is supplied: all action bindings above
            // are the existing synchronous draft commands, never an AI/run boundary.
            string[] excludedIds = ["StartQuantification", "CheckCopilotAuthentication", "ApplyImportedPrompt", "ValidateDesignDraft", "DesignQuestions", "NextStepButton", "PreviousStepButton"];
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(), control =>
                excludedIds.Contains(AutomationProperties.GetAutomationId(control) ?? string.Empty, StringComparer.Ordinal));
        }
        finally
        {
            window.Close();
        }
    }

    private static QuantificationDesignViewModel CreateDesign(
        int questionCount = 2,
        int evaluatorCount = 3,
        int criterionCount = 3,
        bool longContent = false)
    {
        QuantificationDefinition seed = new QuantificationDesignViewModel().Draft;
        QuestionDefinition question = seed.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        CriterionDefinition criterion = evaluator.Criteria[0];
        string longName = longContent ? new string('名', 256) : string.Empty;
        string description = longContent ? string.Join('\n', Enumerable.Repeat("説明・関係・適用を確認する長い文。", 200)) : "説明・関係・適用";
        string prompt = (longContent ? description + "\n" : string.Empty) + "literal {{brace}}\n{回答}\n{評価項目}";
        return new QuantificationDesignViewModel(seed with
        {
            Id = "definition-evaluator-settings",
            Questions = [.. Enumerable.Range(0, questionCount).Select(q => question with
            {
                Id = $"question-{q}",
                DisplayName = $"設問 {q} {longName}",
                QuestionText = $"変更しない設問本文 {q}",
                Points = q == 0 ? 40m : 0m,
                Evaluators = [.. Enumerable.Range(0, evaluatorCount).Select(e => evaluator with
                {
                    Id = $"evaluator-{q}-{e}",
                    DisplayName = $"評価方法 {e} {longName}",
                    Type = e == 1 ? EvaluatorType.CustomPrompt : EvaluatorType.KnowledgeCoverage,
                    BuiltInTemplateVersion = e == 1 ? null : BuiltInPromptTemplates.KnowledgeTemplateVersion,
                    CustomPromptTemplate = e == 1 ? prompt : null,
                    Criteria = [.. Enumerable.Range(0, criterionCount).Select(c => criterion with
                    {
                        Id = $"criterion-{q}-{e}-{c}",
                        DisplayName = $"評価項目 {c} {longName}",
                        Description = description,
                    })],
                })],
            })],
        }, ["A", "B"]);
    }

    private static Window CreateWindow(EvaluatorSettingsView view) => new()
    {
        Width = 950,
        Height = 450,
        Content = view,
    };

    private static T Required<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(root.FindControl<T>(name));

    private static T ById<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    private static SelectingItemsControl[] Selectors(Control view) =>
    [
        Required<ListBox>(view, "QuestionSelector"),
        Required<ComboBox>(view, "EvaluatorSelector"),
        Required<ComboBox>(view, "CriterionSelector"),
    ];

    private static void Select(Control view, string selectorName, object item)
    {
        Assert.IsAssignableFrom<SelectingItemsControl>(view.FindControl<Control>(selectorName)).SetCurrentValue(SelectingItemsControl.SelectedItemProperty, item);
        Render();
    }

    private static void AssertSelectors(Control view, QuantificationDesignViewModel owner)
    {
        SelectingItemsControl[] selectors = Selectors(view);
        Assert.Same(owner.Questions, selectors[0].ItemsSource);
        Assert.Same(owner.SelectedQuestion, selectors[0].SelectedItem);
        Assert.Same(owner.SelectedQuestion?.Evaluators, selectors[1].ItemsSource);
        Assert.Same(owner.SelectedQuestion?.SelectedEvaluator, selectors[1].SelectedItem);
        Assert.Same(owner.SelectedQuestion?.SelectedEvaluator?.Criteria, selectors[2].ItemsSource);
        Assert.Same(owner.SelectedQuestion?.SelectedEvaluator?.SelectedCriterion, selectors[2].SelectedItem);
    }

    private static void Edit(Control view, string id, string value)
    {
        TextBox editor = ById<TextBox>(view, id);
        Assert.NotNull(BindingOperations.GetBindingExpressionBase(editor, TextBox.TextProperty));
        Assert.True(editor.IsEffectivelyEnabled);
        editor.SetCurrentValue(TextBox.TextProperty, value);
        Render();
    }

    private static void SetChecked(Control view, string id, bool value)
    {
        ById<CheckBox>(view, id).SetCurrentValue(ToggleButton.IsCheckedProperty, value);
        Render();
    }

    private static void AddEvaluator(EvaluatorSettingsView view, QuestionDesignItemViewModel question, bool custom)
    {
        Button opener = Required<Button>(view, "AddEvaluatorButton");
        Activate(opener);
        Flyout flyout = Assert.IsType<Flyout>(opener.Flyout);
        StackPanel content = Assert.IsType<StackPanel>(flyout.Content);
        string suffix = custom ? "AddCustomEvaluator" : "AddKnowledgeEvaluator";
        Button action = ById<Button>(content, $"DesignQuestion-{question.Id}-{suffix}");
        Assert.Same(custom ? question.AddCustomEvaluatorCommand : question.AddKnowledgeEvaluatorCommand, action.Command);
        Assert.True(action.MinHeight >= 44d);
        Assert.Equal(14d, action.FontSize);
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(action)));
        Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(action)?.ToString()));
        Activate(action);
        flyout.Hide();
        Render();
    }

    private static void Activate(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        Assert.True(button.Focus(NavigationMethod.Tab));
        TopLevel topLevel = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(button));
        Press(topLevel, Key.Enter);
    }

    private static void ShowPrompt(Control view)
    {
        Required<TabControl>(view, "EditorTabs").SelectedIndex = 1;
        Render();
    }

    private static void ShowBasic(Control view)
    {
        Required<TabControl>(view, "EditorTabs").SelectedIndex = 0;
        Render();
    }

    private static bool IsShown(Control control) => control.IsVisible
        && control.GetVisualAncestors().All(ancestor => ancestor.IsVisible);

    private static void AssertInputContract(EvaluatorSettingsView view)
    {
        TemplatedControl[] controls = view.GetVisualDescendants().OfType<TemplatedControl>()
            .Where(control => control is TextBox or ComboBox or Button or CheckBox or TabItem)
            .Where(control => IsShown(control) && !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(control)))
            .ToArray();
        Assert.NotEmpty(controls);
        foreach (TemplatedControl control in controls)
        {
            Assert.True(control.MinHeight >= 44d, AutomationProperties.GetAutomationId(control));
            Assert.True(control.Bounds.Height >= 44d, AutomationProperties.GetAutomationId(control));
            Assert.True(control.Bounds.Width >= 44d, AutomationProperties.GetAutomationId(control));
            Assert.Equal(14d, control.FontSize);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(control)?.ToString()));
            Assert.True(control.TabIndex >= 0);
            Point origin = control.TranslatePoint(default, view)
                ?? throw new InvalidOperationException("The editor must be inside the finite view.");
            Assert.InRange(origin.X, -1d, view.Bounds.Width);
            Assert.InRange(origin.Y, -1d, view.Bounds.Height);
            Assert.True(origin.X + control.Bounds.Width <= view.Bounds.Width + 1d, AutomationProperties.GetAutomationId(control));
            Assert.True(origin.Y + control.Bounds.Height <= view.Bounds.Height + 1d, AutomationProperties.GetAutomationId(control));
        }
    }

    private static void AssertUniqueIds(Control view)
    {
        string[] ids = view.GetVisualDescendants().OfType<Control>()
            .Concat(view.GetLogicalDescendants().OfType<Control>())
            .Prepend(view).Distinct()
            .Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static void AssertLocalScroll(TextBox textBox)
    {
        ScrollViewer scroll = Assert.Single(textBox.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        Assert.True(scroll.Viewport.Height >= 44d);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        scroll.ScrollToEnd();
        Render();
        Assert.True(scroll.Offset.Y > 0d);
    }

    private static void AssertCanonical(QuantificationDefinition expected, QuantificationDefinition actual)
    {
        CanonicalDefinitionSerializer serializer = new();
        Assert.Equal(serializer.Serialize(expected), serializer.Serialize(actual));
        Assert.Equal(serializer.ComputeSha256(expected), serializer.ComputeSha256(actual));
    }

    private static void Press(TopLevel window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physicalKey = key == Key.Tab ? PhysicalKey.Tab : PhysicalKey.Enter;
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
        Render();
    }

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}