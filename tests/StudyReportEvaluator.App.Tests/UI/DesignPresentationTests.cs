using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

public sealed class DesignPresentationTests
{
    [AvaloniaFact]
    public void Questions_binding_preserves_real_selection_through_move_reorder_and_delete() =>
        AssertBoundStructuralEdits(useVisibleQuestions: false);

    [AvaloniaFact]
    public void VisibleQuestions_binding_preserves_real_selection_through_move_reorder_and_delete() =>
        AssertBoundStructuralEdits(useVisibleQuestions: true);

    [AvaloniaFact]
    public void Questions_binding_preserves_logical_selection_through_pages_and_resize() =>
        AssertBoundPagesAndResize(useVisibleQuestions: false);

    [AvaloniaFact]
    public void VisibleQuestions_binding_preserves_logical_selection_through_pages_and_resize() =>
        AssertBoundPagesAndResize(useVisibleQuestions: true);

    [AvaloniaFact]
    public void Questions_binding_preserves_incoming_primary_columns_and_child_selection() =>
        AssertBoundIncomingColumns(useVisibleQuestions: false);

    [AvaloniaFact]
    public void VisibleQuestions_binding_preserves_incoming_primary_columns_and_child_selection() =>
        AssertBoundIncomingColumns(useVisibleQuestions: true);

    [AvaloniaFact]
    public void Question_selection_refresh_uses_current_data_context_after_reattach()
    {
        QuantificationDesignViewModel original = CreateDesign();
        (QuantificationDesignView view, ListBox list, Window window) = CreateQuestionView(original, false);
        try
        {
            window.Show();
            AssertBoundSelection(original, list, false, original.Questions[0]);
            QuantificationDesignViewModel replacement = CreateDesign(5);
            view.DataContext = replacement;
            AssertBoundSelection(replacement, list, false, replacement.Questions[0]);
            QuestionDesignItemViewModel selected = replacement.Questions[1];
            replacement.SelectedQuestion = selected;
            QuantificationDefinition expected = replacement.Draft.MoveQuestion(1, 0);

            selected.MoveUpCommand.Execute(null);

            AssertBoundSelection(replacement, list, false, selected);
            AssertCanonical(expected, replacement.Draft);
            original.MoveQuestionDown(original.Questions[0].Id);
            AssertBoundSelection(replacement, list, false, selected);
            AssertCanonical(expected, replacement.Draft);

            window.Content = null;
            window.Content = view;
            Dispatcher.UIThread.RunJobs();
            selected.MoveDownCommand.Execute(null);

            AssertBoundSelection(replacement, list, false, selected);
            AssertCanonical(expected.MoveQuestion(0, 1), replacement.Draft);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Null_definition_keeps_the_existing_safe_default_and_single_page()
    {
        QuantificationDesignViewModel viewModel = new((QuantificationDefinition?)null);
        QuantificationDefinition draft = viewModel.Draft;
        QuestionDesignItemViewModel question = Assert.Single(viewModel.Questions);

        Assert.Equal(4, viewModel.PageSize);
        Assert.Same(question, viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "1–1 / 1 件", [question], false, false);
        viewModel.PreviousPageCommand.Execute(null);
        viewModel.NextPageCommand.Execute(null);
        viewModel.PageIndex = int.MaxValue;
        viewModel.PageIndex = int.MinValue;

        Assert.Same(draft, viewModel.Draft);
        Assert.True(viewModel.IsValid);
        Assert.Equal(60m, viewModel.BasePoints);
        Assert.Equal(0m, viewModel.SpecialPoints);
        Assert.Equal(0.1m, viewModel.SimilarityPenaltyWeight);
        Assert.Equal(100m, viewModel.AllocationTotal);
        Assert.Equal("配点合計 100 / 100 · 残り 0", viewModel.AllocationSummary);
        AssertPage(viewModel, 0, "1–1 / 1 件", [question], false, false);
    }

    [Fact]
    public void Empty_questions_keep_null_selection_and_disable_both_page_commands()
    {
        QuantificationDesignViewModel viewModel = CreateDesign(0);
        QuantificationDefinition draft = viewModel.Draft;

        viewModel.PageIndex = int.MaxValue;
        viewModel.PageSize = int.MaxValue;
        viewModel.PageIndex = int.MinValue;
        viewModel.SelectedQuestion = null;
        viewModel.PreviousPageCommand.Execute(null);
        viewModel.NextPageCommand.Execute(null);

        Assert.Empty(viewModel.Questions);
        Assert.Null(viewModel.SelectedQuestion);
        Assert.Same(draft, viewModel.Draft);
        Assert.False(viewModel.IsValid);
        AssertPage(viewModel, 0, "設問はありません（0 件）", [], false, false);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(33)]
    public void Every_question_is_reachable_without_copying_editors_or_changing_selection(int questionCount)
    {
        QuantificationDesignViewModel viewModel = CreateDesign(questionCount);
        QuantificationDefinition draft = viewModel.Draft;
        ReadOnlyObservableCollection<QuestionDesignItemViewModel> questions = viewModel.Questions;
        ReadOnlyObservableCollection<QuestionDesignItemViewModel> visible = viewModel.VisibleQuestions;
        QuestionDesignItemViewModel[] originals = questions.ToArray();
        List<QuestionDesignItemViewModel> visited = [];
        ICommand previous = viewModel.PreviousPageCommand;
        ICommand next = viewModel.NextPageCommand;
        int lastPage = (questionCount - 1) / 4;

        for (int page = 0; page <= lastPage; page++)
        {
            string summary = string.Format(CultureInfo.InvariantCulture,
                "{0:N0}–{1:N0} / {2:N0} 件", page * 4 + 1, Math.Min(page * 4 + 4, questionCount), questionCount);
            AssertPage(viewModel, page, summary, originals.Skip(page * 4).Take(4), page > 0, page < lastPage);
            Assert.Same(originals[0], viewModel.SelectedQuestion);
            visited.AddRange(visible);
            next.Execute(null);
        }

        Assert.Equal(originals.Length, visited.Count);
        for (int index = 0; index < originals.Length; index++)
        {
            Assert.Same(originals[index], visited[index]);
            Assert.Same(originals[index], questions[index]);
        }

        Assert.Same(questions, viewModel.Questions);
        Assert.Same(visible, viewModel.VisibleQuestions);
        Assert.Same(previous, viewModel.PreviousPageCommand);
        Assert.Same(next, viewModel.NextPageCommand);
        Assert.Same(draft, viewModel.Draft);
        AssertCanonical(draft, viewModel.Draft);
        IList<QuestionDesignItemViewModel> readOnly = visible;
        Assert.True(readOnly.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => readOnly.Add(originals[0]));
    }

    [Fact]
    public void Page_index_clamps_and_browsing_does_not_replace_the_selected_question()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel[] questions = viewModel.Questions.ToArray();
        viewModel.SelectedQuestion = questions[6];
        QuantificationDefinition draft = viewModel.Draft;

        viewModel.PageIndex = int.MaxValue;
        AssertPage(viewModel, 2, "9–9 / 9 件", [questions[8]], true, false);
        Assert.Same(questions[6], viewModel.SelectedQuestion);
        viewModel.NextPageCommand.Execute(null);
        Assert.Equal(2, viewModel.PageIndex);
        viewModel.PreviousPageCommand.Execute(null);
        AssertPage(viewModel, 1, "5–8 / 9 件", questions.Skip(4).Take(4), true, true);
        viewModel.PageIndex = int.MinValue;
        AssertPage(viewModel, 0, "1–4 / 9 件", questions.Take(4), false, true);
        Assert.Same(questions[6], viewModel.SelectedQuestion);

        // Selecting the same editor again brings its page back into view.
        viewModel.SelectedQuestion = questions[6];

        AssertPage(viewModel, 1, "5–8 / 9 件", questions.Skip(4).Take(4), true, true);
        Assert.Same(questions[6], viewModel.SelectedQuestion);
        Assert.Same(draft, viewModel.Draft);
    }

    [Fact]
    public void Selection_accepts_current_editors_and_null_but_not_foreign_references()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel last = viewModel.Questions[8];
        QuantificationDesignViewModel foreign = new(viewModel.Draft, viewModel.AvailableColumnNames);
        QuantificationDefinition draft = viewModel.Draft;

        viewModel.SelectedQuestion = last;
        AssertPage(viewModel, 2, "9–9 / 9 件", [last], true, false);
        Assert.Equal(last.Id, foreign.Questions[8].Id);
        Assert.NotSame(last, foreign.Questions[8]);
        viewModel.SelectedQuestion = foreign.Questions[8];
        Assert.Same(last, viewModel.SelectedQuestion);
        viewModel.SelectedQuestion = foreign.Questions[0];
        Assert.Same(last, viewModel.SelectedQuestion);
        Assert.Equal(2, viewModel.PageIndex);

        viewModel.SelectedQuestion = null;

        Assert.Null(viewModel.SelectedQuestion);
        Assert.False(viewModel.CanApplyImportedPrompt);
        AssertPage(viewModel, 2, "9–9 / 9 件", [last], true, false);
        Assert.Same(draft, viewModel.Draft);
    }

    [Theory]
    [InlineData(1, 6, "7–7 / 9 件")]
    [InlineData(3, 2, "7–9 / 9 件")]
    [InlineData(7, 0, "1–7 / 9 件")]
    [InlineData(9, 0, "1–9 / 9 件")]
    [InlineData(int.MaxValue, 0, "1–9 / 9 件")]
    public void Resize_follows_the_selected_id_and_keeps_child_selections_and_canonical_content(
        int pageSize,
        int expectedPageIndex,
        string expectedSummary)
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel question = viewModel.Questions[6];
        SelectChildren(question);
        EvaluatorDesignItemViewModel evaluator = question.SelectedEvaluator!;
        CriterionDesignItemViewModel criterion = evaluator.SelectedCriterion!;
        SpecialEvaluationDesignItemViewModel special = question.SelectedSpecialEvaluation!;
        viewModel.SelectedQuestion = question;
        viewModel.PageIndex = 0;
        QuantificationDefinition draft = viewModel.Draft;
        ((INotifyCollectionChanged)viewModel.VisibleQuestions).CollectionChanged +=
            (_, _) => viewModel.SelectedQuestion = null;

        viewModel.PageSize = pageSize;

        Assert.Equal(pageSize, viewModel.PageSize);
        AssertPage(viewModel, expectedPageIndex, expectedSummary,
            viewModel.Questions.Skip(expectedPageIndex * pageSize).Take(pageSize),
            expectedPageIndex > 0, pageSize == 1 || pageSize == 7);
        Assert.Same(question, viewModel.SelectedQuestion);
        Assert.Contains(viewModel.VisibleQuestions, item => ReferenceEquals(item, question));
        Assert.Same(evaluator, question.SelectedEvaluator);
        Assert.Same(criterion, evaluator.SelectedCriterion);
        Assert.Same(special, question.SelectedSpecialEvaluation);
        Assert.Same(draft, viewModel.Draft);
        AssertCanonical(draft, viewModel.Draft);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Nonpositive_page_size_is_rejected_without_any_change(int pageSize)
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        viewModel.SelectedQuestion = viewModel.Questions[6];
        QuantificationDefinition draft = viewModel.Draft;
        QuestionDesignItemViewModel? selected = viewModel.SelectedQuestion;
        QuestionDesignItemViewModel[] visible = viewModel.VisibleQuestions.ToArray();
        List<string?> changes = [];
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        Assert.Throws<ArgumentOutOfRangeException>(() => viewModel.PageSize = pageSize);

        Assert.Equal(4, viewModel.PageSize);
        AssertPage(viewModel, 1, "5–8 / 9 件", visible, true, true);
        Assert.Same(selected, viewModel.SelectedQuestion);
        Assert.Same(draft, viewModel.Draft);
        Assert.Empty(changes);
    }

    [Fact]
    public void Explicit_null_selection_survives_resize_and_changed_input_sync()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        viewModel.SelectedQuestion = null;
        viewModel.PageIndex = 2;

        viewModel.PageSize = 5;
        AssertPage(viewModel, 1, "6–9 / 9 件", viewModel.Questions.Skip(5), true, false);
        Assert.Null(viewModel.SelectedQuestion);
        QuantificationDefinition incoming = CloneForInput(viewModel.Draft) with { Revision = "null-selection" };
        viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);
        Assert.Null(viewModel.SelectedQuestion);
        AssertPage(viewModel, 1, "6–9 / 9 件", viewModel.Questions.Skip(5), true, false);
        viewModel.PageSize = int.MaxValue;

        Assert.Null(viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "1–9 / 9 件", viewModel.Questions, false, false);
        AssertCanonical(incoming, viewModel.Draft);
    }

    [Fact]
    public void Collection_and_property_writebacks_cannot_clear_presentation_selection()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel first = viewModel.Questions[1];
        QuestionDesignItemViewModel last = viewModel.Questions[8];
        viewModel.SelectedQuestion = first;
        int writebacks = 0;
        void ClearSelection()
        {
            writebacks++;
            viewModel.SelectedQuestion = null;
        }

        ((INotifyCollectionChanged)viewModel.Questions).CollectionChanged += (_, _) => ClearSelection();
        ((INotifyCollectionChanged)viewModel.VisibleQuestions).CollectionChanged += (_, _) => ClearSelection();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(QuantificationDesignViewModel.SelectedQuestion))
            {
                ClearSelection();
            }
        };
        QuantificationDefinition draft = viewModel.Draft;

        viewModel.NextPageCommand.Execute(null);
        Assert.Same(first, viewModel.SelectedQuestion);
        viewModel.SelectedQuestion = last;
        Assert.Same(last, viewModel.SelectedQuestion);
        viewModel.PageSize = 3;
        Assert.Same(last, viewModel.SelectedQuestion);
        AssertCanonical(draft, viewModel.Draft);
        QuantificationDefinition reordered = draft.MoveQuestion(8, 7);
        last.MoveUpCommand.Execute(null);

        Assert.True(writebacks > 0);
        Assert.Same(last, viewModel.SelectedQuestion);
        AssertPage(viewModel, 2, "7–9 / 9 件", viewModel.Questions.Skip(6), true, false);
        AssertCanonical(reordered, viewModel.Draft);
        viewModel.SelectedQuestion = null;
        Assert.Null(viewModel.SelectedQuestion);
    }

    [Fact]
    public void Last_page_deletion_falls_back_to_previous_editor_then_empty_and_can_add_again()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel[] originals = viewModel.Questions.ToArray();
        viewModel.SelectedQuestion = originals[8];
        QuantificationDefinition expected = viewModel.Draft;

        originals[8].DeleteCommand.Execute(null);
        expected = expected.RemoveQuestion(8);
        Assert.Same(originals[7], viewModel.SelectedQuestion);
        AssertPage(viewModel, 1, "5–8 / 8 件", originals.Skip(4).Take(4), true, false);
        AssertCanonical(expected, viewModel.Draft);
        viewModel.SelectedQuestion = originals[8];
        Assert.Same(originals[7], viewModel.SelectedQuestion);

        for (int index = 7; index >= 0; index--)
        {
            originals[index].DeleteCommand.Execute(null);
            expected = expected.RemoveQuestion(index);
            AssertCanonical(expected, viewModel.Draft);
            Assert.Same(index > 0 ? originals[index - 1] : null, viewModel.SelectedQuestion);
            if (index > 0)
            {
                int expectedPage = (index - 1) / 4;
                string summary = string.Format(CultureInfo.InvariantCulture,
                    "{0:N0}–{1:N0} / {2:N0} 件", expectedPage * 4 + 1, index, index);
                AssertPage(viewModel, expectedPage, summary,
                    originals.Take(index).Skip(expectedPage * 4), expectedPage > 0, false);
            }
        }

        AssertPage(viewModel, 0, "設問はありません（0 件）", [], false, false);
        viewModel.AddQuestionCommand.Execute(null);
        QuestionDesignItemViewModel added = Assert.Single(viewModel.Questions);
        Assert.Same(added, viewModel.SelectedQuestion);
        Assert.Equal(0m, added.Points);
        AssertPage(viewModel, 0, "1–1 / 1 件", [added], false, false);
    }

    [Fact]
    public void Middle_deletion_prefers_the_next_neighbor_and_unselected_deletion_keeps_selection()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel[] originals = viewModel.Questions.ToArray();
        viewModel.SelectedQuestion = originals[3];
        QuantificationDefinition expected = viewModel.Draft.RemoveQuestion(3);

        originals[3].DeleteCommand.Execute(null);

        Assert.Same(originals[4], viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "1–4 / 8 件", [originals[0], originals[1], originals[2], originals[4]], false, true);
        AssertCanonical(expected, viewModel.Draft);
        expected = expected.RemoveQuestion(0);
        originals[0].DeleteCommand.Execute(null);
        Assert.Same(originals[4], viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "1–4 / 7 件", [originals[1], originals[2], originals[4], originals[5]], false, true);
        AssertCanonical(expected, viewModel.Draft);
    }

    [Fact]
    public void Reorder_across_a_page_boundary_follows_the_same_question_and_child_editors()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel[] originals = viewModel.Questions.ToArray();
        QuestionDesignItemViewModel question = originals[4];
        SelectChildren(question);
        EvaluatorDesignItemViewModel evaluator = question.SelectedEvaluator!;
        CriterionDesignItemViewModel criterion = evaluator.SelectedCriterion!;
        SpecialEvaluationDesignItemViewModel special = question.SelectedSpecialEvaluation!;
        viewModel.SelectedQuestion = question;
        QuantificationDefinition draft = viewModel.Draft;

        question.MoveUpCommand.Execute(null);

        AssertPage(viewModel, 0, "1–4 / 9 件", [originals[0], originals[1], originals[2], question], false, true);
        Assert.Same(question, viewModel.SelectedQuestion);
        Assert.Same(evaluator, question.SelectedEvaluator);
        Assert.Same(criterion, evaluator.SelectedCriterion);
        Assert.Same(special, question.SelectedSpecialEvaluation);
        AssertCanonical(draft.MoveQuestion(4, 3), viewModel.Draft);
        question.MoveDownCommand.Execute(null);
        AssertPage(viewModel, 1, "5–8 / 9 件", originals.Skip(4).Take(4), true, true);
        Assert.Same(question, viewModel.SelectedQuestion);
        AssertCanonical(draft, viewModel.Draft);
    }

    [Fact]
    public void Add_and_duplicate_preserve_selection_and_make_new_editors_reachable()
    {
        QuantificationDesignViewModel viewModel = CreateDesign(4);
        QuestionDesignItemViewModel[] originals = viewModel.Questions.ToArray();
        viewModel.SelectedQuestion = originals[3];
        QuantificationDefinition draft = viewModel.Draft;

        QuestionDesignItemViewModel added = viewModel.AddQuestion();
        Assert.Same(originals[3], viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "1–4 / 5 件", originals, false, true);
        QuestionDesignItemViewModel duplicate = viewModel.DuplicateQuestion(originals[0].Id);

        Assert.Same(originals[3], viewModel.SelectedQuestion);
        AssertPage(viewModel, 1, "5–6 / 6 件", [originals[3], added], true, false);
        Assert.NotEqual(originals[0].Id, duplicate.Id);
        Assert.NotEqual(originals[0].Evaluators[0].Id, duplicate.Evaluators[0].Id);
        Assert.NotEqual(originals[0].Evaluators[0].Criteria[0].Id, duplicate.Evaluators[0].Criteria[0].Id);
        Assert.NotEqual(originals[0].SpecialEvaluations[0].Id, duplicate.SpecialEvaluations[0].Id);
        string[] originalIds = originals.Select(item => item.Id).ToArray();
        AssertCanonical(draft, viewModel.Draft with
        {
            Questions = [.. viewModel.Draft.Questions.Where(item => originalIds.Contains(item.Id))],
        });
        viewModel.SelectedQuestion = duplicate;
        AssertPage(viewModel, 0, "1–4 / 6 件", [originals[0], duplicate, originals[1], originals[2]], false, true);
        viewModel.SelectedQuestion = added;
        AssertPage(viewModel, 1, "5–6 / 6 件", [originals[3], added], true, false);
    }

    [Fact]
    public void Incoming_changes_keep_selected_references_and_exact_definition_including_primary_columns()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel question = viewModel.Questions[6];
        SelectChildren(question);
        EvaluatorDesignItemViewModel evaluator = question.SelectedEvaluator!;
        CriterionDesignItemViewModel criterion = evaluator.SelectedCriterion!;
        SpecialEvaluationDesignItemViewModel special = question.SelectedSpecialEvaluation!;
        viewModel.SelectedQuestion = question;
        QuantificationDefinition incoming = CloneForInput(viewModel.Draft);
        QuestionDefinition nextQuestion = incoming.Questions[6];
        EvaluatorDefinition nextEvaluator = nextQuestion.Evaluators[1] with
        {
            Weight = 2.125m,
            Range = new ScoreRange(-1.25m, 5.125m),
            CustomPromptTemplate = "入力同期 {{literal}}\r\n{回答} {評価項目}",
            Enabled = false,
            Criteria = nextQuestion.Evaluators[1].Criteria.SetItem(1, nextQuestion.Evaluators[1].Criteria[1] with
            {
                Description = "更新した観点 🧪",
                Weight = 0.125m,
                Range = new ScoreRange(-0.5m, 3.5m),
                Enabled = false,
            }),
        };
        nextQuestion = nextQuestion with
        {
            QuestionText = "更新した設問文\n第二行",
            PrimarySourceColumn = "D",
            SupportingSourceColumns = ["C", "A"],
            Points = 0.123456789m,
            Evaluators = nextQuestion.Evaluators.SetItem(1, nextEvaluator),
            SpecialEvaluations = nextQuestion.SpecialEvaluations.SetItem(1, nextQuestion.SpecialEvaluations[1] with
            {
                PrimarySourceColumn = "D",
                SupportingSourceColumns = ["A", "C"],
                PromptTemplate = "入力からの固有評価 {回答}",
                Enabled = true,
            }),
        };
        incoming = incoming with
        {
            Revision = "incoming",
            SourceSheet = "入力更新",
            Questions = [.. incoming.Questions.Where(item => item.Id != nextQuestion.Id), nextQuestion],
        };
        ((INotifyCollectionChanged)viewModel.Questions).CollectionChanged += (_, _) => viewModel.SelectedQuestion = null;
        ((INotifyCollectionChanged)viewModel.VisibleQuestions).CollectionChanged += (_, _) => viewModel.SelectedQuestion = null;
        special.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SpecialEvaluationDesignItemViewModel.AvailableColumnNames))
            {
                special.PrimarySourceColumn = null!;
            }
        };

        viewModel.SynchronizeFromInput(incoming, ["D", "C", "A"]);

        AssertPage(viewModel, 2, "9–9 / 9 件", [question], true, false);
        Assert.Same(question, viewModel.SelectedQuestion);
        Assert.Same(evaluator, question.SelectedEvaluator);
        Assert.Same(criterion, evaluator.SelectedCriterion);
        Assert.Same(special, question.SelectedSpecialEvaluation);
        Assert.Equal("D", special.PrimarySourceColumn);
        Assert.Equal(["D", "C", "A"], special.AvailableColumnNames);
        Assert.Equal(["A", "C"], viewModel.Draft.Questions[8].SpecialEvaluations[1].SupportingSourceColumns);
        AssertCanonical(incoming, viewModel.Draft);
    }

    [Fact]
    public void Unchanged_column_only_and_value_only_sync_preserve_a_separately_browsed_page()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel selected = viewModel.Questions[1];
        viewModel.SelectedQuestion = selected;
        viewModel.PageIndex = 2;
        QuantificationDefinition draft = viewModel.Draft;
        List<string?> changes = [];
        int collectionChanges = 0;
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        ((INotifyCollectionChanged)viewModel.VisibleQuestions).CollectionChanged += (_, _) => collectionChanges++;

        viewModel.SynchronizeFromInput(CloneForInput(draft), viewModel.AvailableColumnNames);

        Assert.Empty(changes);
        Assert.Equal(0, collectionChanges);
        Assert.Same(draft, viewModel.Draft);
        Assert.Same(selected, viewModel.SelectedQuestion);
        AssertPage(viewModel, 2, "9–9 / 9 件", [viewModel.Questions[8]], true, false);
        viewModel.SynchronizeFromInput(CloneForInput(draft), ["A", "B", "C", "D", "E"]);
        Assert.Same(draft, viewModel.Draft);
        Assert.Equal(0, collectionChanges);
        Assert.Same(selected, viewModel.SelectedQuestion);
        AssertPage(viewModel, 2, "9–9 / 9 件", [viewModel.Questions[8]], true, false);
        QuantificationDefinition incoming = CloneForInput(draft) with { Name = "値だけ更新" };
        viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);
        Assert.Equal(0, collectionChanges);
        Assert.Same(selected, viewModel.SelectedQuestion);
        AssertPage(viewModel, 2, "9–9 / 9 件", [viewModel.Questions[8]], true, false);
        AssertCanonical(incoming, viewModel.Draft);
    }

    [Fact]
    public void Missing_selected_id_uses_previous_order_neighbors_even_when_incoming_order_changes()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel[] originals = viewModel.Questions.ToArray();
        viewModel.SelectedQuestion = originals[6];
        QuantificationDefinition incoming = viewModel.Draft with
        {
            Questions = [viewModel.Draft.Questions[8], viewModel.Draft.Questions[7], viewModel.Draft.Questions[1]],
        };

        viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);

        Assert.Same(originals[7], viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "1–3 / 3 件", [originals[8], originals[7], originals[1]], false, false);
        AssertCanonical(incoming, viewModel.Draft);
        viewModel.SelectedQuestion = originals[6];
        Assert.Same(originals[7], viewModel.SelectedQuestion);
    }

    [Fact]
    public void Replacing_all_ids_then_removing_all_questions_never_retains_a_stale_selection()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel[] originals = viewModel.Questions.ToArray();
        viewModel.SelectedQuestion = originals[8];
        QuantificationDefinition incoming = CreateDefinition(5);
        incoming = incoming with
        {
            Questions = [.. incoming.Questions.Select(question => question with { Id = "new-" + question.Id })],
        };

        viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);

        Assert.Same(viewModel.Questions[0], viewModel.SelectedQuestion);
        Assert.All(viewModel.Questions, item => Assert.DoesNotContain(item, originals));
        AssertPage(viewModel, 0, "1–4 / 5 件", viewModel.Questions.Take(4), false, true);
        AssertCanonical(incoming, viewModel.Draft);
        viewModel.SelectedQuestion = viewModel.Questions[4];
        incoming = incoming with { Questions = [] };
        viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);
        Assert.Null(viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "設問はありません（0 件）", [], false, false);
        AssertCanonical(incoming, viewModel.Draft);
    }

    [Fact]
    public void Disabled_questions_remain_visible_selectable_and_editable()
    {
        QuantificationDefinition definition = CreateDefinition();
        definition = definition with
        {
            Questions = [.. definition.Questions.Select(question => question with { Enabled = false })],
        };
        QuantificationDesignViewModel viewModel = new(definition);
        QuestionDesignItemViewModel selected = viewModel.Questions[8];

        viewModel.SelectedQuestion = selected;
        viewModel.PageSize = 2;

        Assert.False(viewModel.IsValid);
        Assert.Equal(9, viewModel.Questions.Count);
        AssertPage(viewModel, 4, "9–9 / 9 件", [selected], true, false);
        Assert.All(viewModel.Questions, item => Assert.False(item.Enabled));
        AssertCanonical(definition, viewModel.Draft);
        selected.Points = 40m;
        selected.Enabled = true;
        Assert.Same(selected, Assert.Single(viewModel.VisibleQuestions));
        Assert.Same(selected, viewModel.SelectedQuestion);
        Assert.True(viewModel.IsValid);
        Assert.Equal(100m, viewModel.AllocationTotal);
        Assert.All(viewModel.Questions.Take(8), item => Assert.False(item.Enabled));
    }

    [Fact]
    public void Invalid_draft_can_be_selected_and_corrected_without_presentation_coercing_values()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel question = viewModel.Questions[8];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        question.Points = -0.000001m;
        evaluator.CustomPromptTemplate = "編集中で placeholder が未入力";
        evaluator.Minimum = evaluator.Maximum;
        QuantificationDefinition invalidDraft = viewModel.Draft;

        viewModel.SelectedQuestion = question;
        question.SelectedEvaluator = evaluator;
        viewModel.PageSize = 3;
        viewModel.PageIndex = 0;
        viewModel.SelectedQuestion = question;

        Assert.False(viewModel.CanBuildSnapshot);
        Assert.True(question.HasErrors);
        Assert.True(evaluator.HasErrors);
        Assert.Same(question, viewModel.SelectedQuestion);
        AssertPage(viewModel, 2, "7–9 / 9 件", viewModel.Questions.Skip(6), true, false);
        Assert.Same(invalidDraft, viewModel.Draft);
        AssertCanonical(invalidDraft, viewModel.Draft);
        question.Points = 0m;
        evaluator.Minimum = 0m;
        evaluator.CustomPromptTemplate = BuiltInPromptTemplates.CustomPromptPreset;
        Assert.True(viewModel.IsValid);
        Assert.Same(question, viewModel.SelectedQuestion);
        Assert.Same(evaluator, question.SelectedEvaluator);
        Assert.Equal(100m, viewModel.AllocationTotal);
    }

    [Fact]
    public void Presentation_preserves_all_canonical_values_collections_and_imported_prompts()
    {
        QuantificationDefinition definition = CreateDefinition();
        definition = definition with
        {
            BasePoints = 55.123456789m,
            SpecialPoints = 4.876543211m,
            SimilarityPenaltyWeight = 0.123456789123456789m,
            Questions = definition.Questions.SetItem(1, definition.Questions[1] with { Enabled = false }),
        };
        ImportedPrompt[] sources =
        [
            new() { Path = "02-custom.txt", DisplayName = "02-custom.txt", Content = "読込 {{literal}}\r\n{回答} {評価項目}" },
            new() { Path = "01-special.txt", DisplayName = "01-special.txt", Content = "固有 🧪\n{回答}" },
        ];
        QuantificationDesignViewModel viewModel = new(definition, ["A", "B", "C", "D"], sources);
        QuantificationDefinition draft = viewModel.Draft;
        var questions = viewModel.Questions;
        var visible = viewModel.VisibleQuestions;
        var prompts = viewModel.ImportedPrompts;
        ImportedPromptViewModel[] originalPrompts = prompts.ToArray();
        var children = questions.Select(question =>
            (Question: question, Evaluators: question.Evaluators, Specials: question.SpecialEvaluations,
             Criteria: question.Evaluators.Select(evaluator => evaluator.Criteria).ToArray())).ToArray();
        foreach (QuestionDesignItemViewModel question in questions)
        {
            SelectChildren(question);
        }

        questions[0].Evaluators[0].SelectedCriterion = null;
        viewModel.SelectedImportedPrompt = originalPrompts[1];
        viewModel.SelectedPromptTarget = ImportedPromptTarget.SpecialEvaluation;

        foreach (int pageSize in new[] { 1, 3, 4, 7, int.MaxValue })
        {
            viewModel.PageSize = pageSize;
            foreach (QuestionDesignItemViewModel question in questions)
            {
                viewModel.SelectedQuestion = question;
                Assert.Contains(visible, item => ReferenceEquals(item, question));
            }

            viewModel.PageIndex = 0;
            viewModel.NextPageCommand.Execute(null);
            viewModel.PageIndex = int.MaxValue;
            viewModel.PreviousPageCommand.Execute(null);
            Assert.Same(draft, viewModel.Draft);
            AssertCanonical(definition, viewModel.Draft);
        }

        Assert.Same(questions, viewModel.Questions);
        Assert.Same(visible, viewModel.VisibleQuestions);
        foreach (var child in children)
        {
            Assert.Same(child.Evaluators, child.Question.Evaluators);
            Assert.Same(child.Specials, child.Question.SpecialEvaluations);
            Assert.Same(child.Evaluators[1], child.Question.SelectedEvaluator);
            Assert.Same(child.Specials[1], child.Question.SelectedSpecialEvaluation);
            for (int index = 0; index < child.Evaluators.Count; index++)
            {
                Assert.Same(child.Criteria[index], child.Evaluators[index].Criteria);
            }

            Assert.Same(child.Criteria[1][1], child.Evaluators[1].SelectedCriterion);
        }

        Assert.Null(questions[0].Evaluators[0].SelectedCriterion);
        Assert.Same(prompts, viewModel.ImportedPrompts);
        Assert.Same(originalPrompts[1], viewModel.SelectedImportedPrompt);
        Assert.Equal(ImportedPromptTarget.SpecialEvaluation, viewModel.SelectedPromptTarget);
        for (int index = 0; index < sources.Length; index++)
        {
            Assert.Same(originalPrompts[index], prompts[index]);
            Assert.Same(sources[index], viewModel.ImportedPromptSources[index]);
            Assert.Equal(sources[index].DisplayName, prompts[index].DisplayName);
            Assert.Equal(sources[index].Content, prompts[index].Content);
        }
    }

    [Theory]
    [InlineData("39.999999999999999999", "99.999999999999999999", "0.000000000000000001", false)]
    [InlineData("40", "100", "0", true)]
    [InlineData("40.000000000000000001", "100.000000000000000001", "-0.000000000000000001", false)]
    public void Allocation_summary_uses_exact_all_question_decimals_not_the_visible_page_or_rounding(
        string pointsText,
        string totalText,
        string remainingText,
        bool isValid)
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        viewModel.RoundingDigits = 0;
        viewModel.Questions[0].Points = decimal.Parse(pointsText, CultureInfo.InvariantCulture);
        QuantificationDefinition draft = viewModel.Draft;
        decimal total = decimal.Parse(totalText, CultureInfo.InvariantCulture);
        decimal remaining = decimal.Parse(remainingText, CultureInfo.InvariantCulture);

        viewModel.PageIndex = 2;

        AssertPage(viewModel, 2, "9–9 / 9 件", [viewModel.Questions[8]], true, false);
        Assert.Equal(0m, viewModel.VisibleQuestions[0].Points);
        Assert.Equal(total, viewModel.AllocationTotal);
        Assert.Equal(remaining, viewModel.AllocationRemaining);
        Assert.Equal(isValid, viewModel.IsAllocationValid);
        Assert.Equal($"配点合計 {total.ToString("G29", CultureInfo.InvariantCulture)} / 100 · 残り {remaining.ToString("G29", CultureInfo.InvariantCulture)}",
            viewModel.AllocationSummary);
        viewModel.PageSize = int.MaxValue;
        Assert.Equal(total, viewModel.AllocationTotal);
        Assert.Same(draft, viewModel.Draft);
        AssertCanonical(draft, viewModel.Draft);
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("overflow")]
    [InlineData("base")]
    [InlineData("special")]
    public void Uncalculable_allocation_remains_null_and_does_not_prevent_paging(string invalidValue)
    {
        QuantificationDefinition definition = CreateDefinition();
        definition = invalidValue switch
        {
            "negative" => definition with
            {
                Questions = definition.Questions.SetItem(8, definition.Questions[8] with { Points = -1m }),
            },
            "overflow" => definition with
            {
                Questions = definition.Questions.SetItem(0, definition.Questions[0] with { Points = decimal.MaxValue }),
            },
            "base" => definition with { BasePoints = 100.000001m },
            "special" => definition with { SpecialPoints = 100.000001m },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidValue)),
        };
        QuantificationDesignViewModel viewModel = new(definition);

        viewModel.SelectedQuestion = viewModel.Questions[8];
        viewModel.PageSize = 1;

        AssertPage(viewModel, 8, "9–9 / 9 件", [viewModel.Questions[8]], true, false);
        Assert.Null(viewModel.AllocationTotal);
        Assert.Null(viewModel.AllocationRemaining);
        Assert.False(viewModel.IsAllocationValid);
        Assert.Equal("配点合計を計算できません。", viewModel.AllocationSummary);
        AssertCanonical(definition, viewModel.Draft);
    }

    [Fact]
    public void Presentation_notifications_publish_consistent_ranges_buttons_and_selection()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel last = viewModel.Questions[8];
        List<string?> properties = [];
        List<(int Page, string Summary, bool Previous, bool Next, int VisibleCount)> states = [];
        void CaptureState() => states.Add((viewModel.PageIndex, viewModel.PageSummary,
            viewModel.PreviousPageCommand.CanExecute(null), viewModel.NextPageCommand.CanExecute(null),
            viewModel.VisibleQuestions.Count));
        viewModel.PropertyChanged += (_, args) =>
        {
            properties.Add(args.PropertyName);
            if (args.PropertyName is nameof(QuantificationDesignViewModel.PageIndex)
                or nameof(QuantificationDesignViewModel.PageSize)
                or nameof(QuantificationDesignViewModel.PageSummary))
            {
                CaptureState();
            }
        };
        int previousChanges = 0;
        int nextChanges = 0;
        viewModel.PreviousPageCommand.CanExecuteChanged += (_, _) => { previousChanges++; CaptureState(); };
        viewModel.NextPageCommand.CanExecuteChanged += (_, _) => { nextChanges++; CaptureState(); };

        viewModel.SelectedQuestion = last;

        Assert.NotEmpty(states);
        Assert.All(states, state => Assert.Equal((2, "9–9 / 9 件", true, false, 1), state));
        states.Clear();
        viewModel.PageSize = 5;
        Assert.NotEmpty(states);
        Assert.All(states, state => Assert.Equal((1, "6–9 / 9 件", true, false, 4), state));
        Assert.Contains(nameof(QuantificationDesignViewModel.PageIndex), properties);
        Assert.Contains(nameof(QuantificationDesignViewModel.PageSize), properties);
        Assert.Contains(nameof(QuantificationDesignViewModel.PageSummary), properties);
        Assert.Contains(nameof(QuantificationDesignViewModel.SelectedQuestion), properties);
        Assert.DoesNotContain(nameof(QuantificationDesignViewModel.Draft), properties);
        Assert.Equal(2, previousChanges);
        Assert.Equal(2, nextChanges);
        Assert.Same(last, viewModel.SelectedQuestion);
    }

    [Fact]
    public void Count_only_change_refreshes_summary_and_commands_even_when_visible_references_stay_the_same()
    {
        QuantificationDesignViewModel viewModel = CreateDesign(4);
        QuestionDesignItemViewModel[] visible = viewModel.VisibleQuestions.ToArray();
        List<string?> properties = [];
        int nextChanges = 0;
        int collectionChanges = 0;
        viewModel.PropertyChanged += (_, args) => properties.Add(args.PropertyName);
        viewModel.NextPageCommand.CanExecuteChanged += (_, _) => nextChanges++;
        ((INotifyCollectionChanged)viewModel.VisibleQuestions).CollectionChanged += (_, _) => collectionChanges++;

        viewModel.AddQuestion();

        AssertPage(viewModel, 0, "1–4 / 5 件", visible, false, true);
        Assert.Contains(nameof(QuantificationDesignViewModel.PageSummary), properties);
        Assert.Equal(0, collectionChanges);
        Assert.True(nextChanges > 0);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("resize")]
    [InlineData("sync")]
    public void Presentation_guard_is_released_after_notification_failure(string operation)
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        void FailOnSummary(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(QuantificationDesignViewModel.PageSummary))
            {
                throw new InvalidOperationException("Simulated presentation notification failure.");
            }
        }

        viewModel.PropertyChanged += FailOnSummary;
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                switch (operation)
                {
                    case "page":
                        viewModel.PageIndex = 1;
                        break;
                    case "resize":
                        viewModel.PageSize = 3;
                        break;
                    case "sync":
                        viewModel.SynchronizeFromInput(viewModel.Draft.MoveQuestion(0, 8), viewModel.AvailableColumnNames);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(operation));
                }
            });
        }
        finally
        {
            viewModel.PropertyChanged -= FailOnSummary;
        }

        viewModel.SelectedQuestion = null;
        Assert.Null(viewModel.SelectedQuestion);
        QuestionDesignItemViewModel last = viewModel.Questions[8];
        viewModel.SelectedQuestion = last;
        Assert.Same(last, viewModel.SelectedQuestion);
        Assert.Contains(viewModel.VisibleQuestions, item => ReferenceEquals(item, last));
        viewModel.PageSize = 4;
        AssertPage(viewModel, 2, "9–9 / 9 件", [last], true, false);
    }

    private static void AssertBoundStructuralEdits(bool useVisibleQuestions)
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel[] originals = viewModel.Questions.ToArray();
        QuestionDesignItemViewModel selected = originals[1];
        SelectChildren(selected);
        viewModel.SelectedQuestion = selected;
        QuantificationDefinition expected = viewModel.Draft;
        (_, ListBox list, Window window) = CreateQuestionView(viewModel, useVisibleQuestions);
        try
        {
            window.Show();
            AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
            List<QuestionDesignItemViewModel?> observedLogicalSelections = [];
            list.SelectionChanged += (_, _) => observedLogicalSelections.Add(viewModel.SelectedQuestion);

            // Real ObservableCollection.Move -> ListBox -> compiled TwoWay binding.
            // A simulated setter call cannot detect the stale target-value cache.
            viewModel.MoveQuestionUp(selected.Id);

            AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
            Assert.Same(selected, viewModel.Questions[0]);
            AssertCanonical(expected.MoveQuestion(1, 0), viewModel.Draft);
            Assert.NotEmpty(observedLogicalSelections);
            Assert.All(observedLogicalSelections, item => Assert.Same(selected, item));
            selected.MoveDownCommand.Execute(null);
            AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
            AssertCanonical(expected, viewModel.Draft);

            selected = originals[4];
            viewModel.SelectedQuestion = selected;
            AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
            selected.MoveUpCommand.Execute(null);
            Assert.Equal(0, viewModel.PageIndex);
            AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
            AssertCanonical(expected.MoveQuestion(4, 3), viewModel.Draft);
            selected.MoveDownCommand.Execute(null);
            Assert.Equal(1, viewModel.PageIndex);
            AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
            AssertCanonical(expected, viewModel.Draft);

            expected = CloneForInput(expected).MoveQuestion(4, 8);
            viewModel.SynchronizeFromInput(expected, viewModel.AvailableColumnNames);
            Assert.Equal(2, viewModel.PageIndex);
            AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
            AssertCanonical(expected, viewModel.Draft);

            // Last-page deletion chooses the previous survivor; middle deletion the next.
            selected.DeleteCommand.Execute(null);
            expected = expected.RemoveQuestion(8);
            AssertBoundSelection(viewModel, list, useVisibleQuestions, originals[8]);
            Assert.Equal(1, viewModel.PageIndex);
            AssertCanonical(expected, viewModel.Draft);
            viewModel.SelectedQuestion = originals[3];
            originals[3].DeleteCommand.Execute(null);
            expected = expected.RemoveQuestion(3);
            AssertBoundSelection(viewModel, list, useVisibleQuestions, originals[5]);
            AssertCanonical(expected, viewModel.Draft);
            originals[0].DeleteCommand.Execute(null);
            expected = expected.RemoveQuestion(0);
            AssertBoundSelection(viewModel, list, useVisibleQuestions, originals[5]);
            AssertCanonical(expected, viewModel.Draft);

            while (viewModel.Questions.Count > 0)
            {
                QuestionDesignItemViewModel last = viewModel.Questions[^1];
                QuestionDesignItemViewModel? neighbor = viewModel.Questions.Count > 1 ? viewModel.Questions[^2] : null;
                viewModel.SelectedQuestion = last;
                expected = expected.RemoveQuestion(viewModel.Questions.Count - 1);
                last.DeleteCommand.Execute(null);
                AssertBoundSelection(viewModel, list, useVisibleQuestions, neighbor);
                AssertCanonical(expected, viewModel.Draft);
            }

            Assert.Empty(list.Items);
            Assert.Equal(0, viewModel.PageIndex);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertBoundPagesAndResize(bool useVisibleQuestions)
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel selected = viewModel.Questions[6];
        SelectChildren(selected);
        viewModel.SelectedQuestion = selected;
        QuantificationDefinition draft = viewModel.Draft;
        (_, ListBox list, Window window) = CreateQuestionView(viewModel, useVisibleQuestions);
        try
        {
            window.Show();
            AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
            foreach (int page in new[] { 2, 0, 1 })
            {
                viewModel.PageIndex = page;
                AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
                Assert.Equal(page, viewModel.PageIndex);
            }

            viewModel.NextPageCommand.Execute(null);
            Assert.Equal(2, viewModel.PageIndex);
            AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
            viewModel.PreviousPageCommand.Execute(null);
            Assert.Equal(1, viewModel.PageIndex);
            AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
            viewModel.PageIndex = 0;
            viewModel.SelectedQuestion = selected;
            Assert.Equal(1, viewModel.PageIndex);
            AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);

            // T09 consumes the layout capacity; measuring the window belongs to T19.
            foreach (int capacity in new[] { 1, 3, 7, 9, 4 })
            {
                viewModel.PageIndex = 0;
                viewModel.PageSize = capacity;
                Assert.Equal(6 / capacity, viewModel.PageIndex);
                AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
                Assert.Same(selected.Evaluators[1], selected.SelectedEvaluator);
                Assert.Same(selected.Evaluators[1].Criteria[1], selected.SelectedEvaluator!.SelectedCriterion);
                Assert.Same(selected.SpecialEvaluations[1], selected.SelectedSpecialEvaluation);
                Assert.Same(draft, viewModel.Draft);
                AssertCanonical(draft, viewModel.Draft);
            }

            QuestionDesignItemViewModel userSelection = viewModel.VisibleQuestions[0];
            list.SetCurrentValue(ListBox.SelectedItemProperty, userSelection);
            AssertBoundSelection(viewModel, list, useVisibleQuestions, userSelection);
            list.SetCurrentValue(ListBox.SelectedItemProperty, null);
            AssertBoundSelection(viewModel, list, useVisibleQuestions, null);
            viewModel.NextPageCommand.Execute(null);
            viewModel.PageSize = 2;
            AssertBoundSelection(viewModel, list, useVisibleQuestions, null);
            Assert.Same(draft, viewModel.Draft);
            AssertCanonical(draft, viewModel.Draft);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertBoundIncomingColumns(bool useVisibleQuestions)
    {
        foreach (bool keepPreviousPrimaryCandidate in new[] { true, false })
        {
            QuantificationDesignViewModel viewModel = CreateDesign();
            QuestionDesignItemViewModel selected = viewModel.Questions[6];
            SelectChildren(selected);
            EvaluatorDesignItemViewModel evaluator = selected.SelectedEvaluator!;
            CriterionDesignItemViewModel criterion = evaluator.SelectedCriterion!;
            SpecialEvaluationDesignItemViewModel special = selected.SelectedSpecialEvaluation!;
            viewModel.SelectedQuestion = selected;
            (_, ListBox list, Window window) = CreateQuestionView(viewModel, useVisibleQuestions);
            // T19 no longer hosts special editors. Keep the real primary-column
            // binding/atomic-sync regression on its actual replacement surface.
            SpecialEvaluationSettingsView settings = new(viewModel);
            Window settingsWindow = new()
            {
                Width = 950, Height = 450, WindowDecorations = WindowDecorations.None, Content = settings,
            };
            try
            {
                window.Show();
                settingsWindow.Show();
                AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
                ComboBox primaryColumn = Assert.Single(settings.GetVisualDescendants().OfType<ComboBox>(),
                    control => AutomationProperties.GetAutomationId(control) == special.CardAutomationId + "-Primary");
                Assert.Same(special, primaryColumn.DataContext);
                Assert.NotNull(BindingOperations.GetBindingExpressionBase(primaryColumn, ComboBox.SelectedItemProperty));
                Assert.Equal("B", primaryColumn.SelectedItem);
                QuantificationDefinition incoming = CloneForInput(viewModel.Draft);
                QuestionDefinition nextQuestion = incoming.Questions[6];
                nextQuestion = nextQuestion with
                {
                    QuestionText = "入力同期の設問 🧪\r\n第二行",
                    PrimarySourceColumn = "D",
                    SupportingSourceColumns = ["C", "A"],
                    Points = 0.123456789m,
                    Evaluators = nextQuestion.Evaluators.SetItem(1, nextQuestion.Evaluators[1] with
                    {
                        CustomPromptTemplate = "入力同期 {{literal}}\r\n{回答} {評価項目}",
                        Range = new ScoreRange(-1.25m, 5.125m),
                        Enabled = false,
                    }),
                    SpecialEvaluations = nextQuestion.SpecialEvaluations.SetItem(1, nextQuestion.SpecialEvaluations[1] with
                    {
                        PrimarySourceColumn = "D",
                        SupportingSourceColumns = ["A", "C"],
                    }),
                };
                incoming = incoming with { Questions = incoming.Questions.SetItem(6, nextQuestion) };
                incoming = incoming.MoveQuestion(6, 8);
                CanonicalDefinitionSerializer serializer = new();
                string expectedJson = serializer.Serialize(incoming);
                string expectedHash = serializer.ComputeSha256(incoming);
                IReadOnlyList<string> columns = keepPreviousPrimaryCandidate ? ["D", "C", "A", "B"] : ["D", "C", "A"];

                viewModel.SynchronizeFromInput(incoming, columns);

                AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
                Assert.Equal(2, viewModel.PageIndex);
                Assert.Same(evaluator, selected.SelectedEvaluator);
                Assert.Same(criterion, evaluator.SelectedCriterion);
                Assert.Same(special, selected.SelectedSpecialEvaluation);
                Assert.Same(primaryColumn, Assert.Single(settings.GetVisualDescendants().OfType<ComboBox>(),
                    control => AutomationProperties.GetAutomationId(control) == special.CardAutomationId + "-Primary"));
                Assert.Equal("D", primaryColumn.SelectedItem);
                Assert.Equal("D", special.PrimarySourceColumn);
                Assert.Equal(columns, primaryColumn.ItemsSource!.Cast<string>());
                Assert.Equal(expectedJson, serializer.Serialize(viewModel.Draft));
                Assert.Equal(expectedHash, serializer.ComputeSha256(viewModel.Draft));
                Assert.Equal(expectedJson, serializer.Serialize(incoming));
                Assert.Equal(expectedHash, serializer.ComputeSha256(incoming));

                // The T05 primary-column guard must still release for an actual user edit.
                primaryColumn.SetCurrentValue(ComboBox.SelectedItemProperty, "C");
                AssertBoundSelection(viewModel, list, useVisibleQuestions, selected);
                Assert.Equal("C", primaryColumn.SelectedItem);
                nextQuestion = nextQuestion with
                {
                    SpecialEvaluations = nextQuestion.SpecialEvaluations.SetItem(1, nextQuestion.SpecialEvaluations[1] with
                    {
                        PrimarySourceColumn = "C",
                        SupportingSourceColumns = ["A"],
                    }),
                };
                AssertCanonical(incoming with { Questions = incoming.Questions.SetItem(8, nextQuestion) }, viewModel.Draft);
                Assert.Equal(expectedHash, serializer.ComputeSha256(incoming));
            }
            finally
            {
                settingsWindow.Close();
                window.Close();
            }
        }
    }

    private static (QuantificationDesignView View, ListBox List, Window Window) CreateQuestionView(
        QuantificationDesignViewModel viewModel,
        bool useVisibleQuestions)
    {
        // Leave the production XAML SelectedItem compiled binding intact in both cases.
        // Keep both ItemsSource patterns covered even after T19 switches the XAML.
        // Configure them before attaching the VM under test.
        QuantificationDesignView view = new();
        ListBox list = Assert.IsType<ListBox>(view.FindControl<ListBox>("QuestionEditorList"));
        BindingExpressionBase selectionBinding = Assert.IsAssignableFrom<BindingExpressionBase>(
            BindingOperations.GetBindingExpressionBase(list, ListBox.SelectedItemProperty));
        CompiledBinding itemsBinding = useVisibleQuestions
            ? CompiledBinding.Create<QuantificationDesignViewModel, ReadOnlyObservableCollection<QuestionDesignItemViewModel>>(
                model => model.VisibleQuestions, mode: BindingMode.OneWay)
            : CompiledBinding.Create<QuantificationDesignViewModel, ReadOnlyObservableCollection<QuestionDesignItemViewModel>>(
                model => model.Questions, mode: BindingMode.OneWay);
        list.Bind(ListBox.ItemsSourceProperty, itemsBinding);
        view.DataContext = viewModel;

        Assert.Same(selectionBinding, BindingOperations.GetBindingExpressionBase(list, ListBox.SelectedItemProperty));
        // This finite fixture fits four actual 48-DIP rows. T09 controls PageSize
        // explicitly below; T19 separately checks measured resize growth. Do not
        // depend on a production MaxHeight or disable its capacity calculation.
        return (view, list, new Window
        {
            Width = 1260, Height = 450, WindowDecorations = WindowDecorations.None, Content = view,
        });
    }

    private static void AssertBoundSelection(
        QuantificationDesignViewModel viewModel,
        ListBox list,
        bool useVisibleQuestions,
        QuestionDesignItemViewModel? logicalSelection)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.Same(logicalSelection, viewModel.SelectedQuestion);
        Assert.Same(useVisibleQuestions ? viewModel.VisibleQuestions : viewModel.Questions, list.ItemsSource);
        // Plan D11/6.2 retains the logical target independently of the browsed page.
        // Only the intersection of that target and ItemsSource can be highlighted.
        QuestionDesignItemViewModel? displayedSelection = logicalSelection is not null
            && (!useVisibleQuestions || viewModel.VisibleQuestions.Contains(logicalSelection))
                ? logicalSelection : null;
        Assert.Same(displayedSelection, list.SelectedItem);
        Assert.NotNull(BindingOperations.GetBindingExpressionBase(list, ListBox.SelectedItemProperty));
    }

    private static QuantificationDesignViewModel CreateDesign(int questionCount = 9) =>
        new(CreateDefinition(questionCount), ["A", "B", "C", "D"]);

    private static QuantificationDefinition CreateDefinition(int questionCount = 9)
    {
        QuantificationDefinition seed = new QuantificationDesignViewModel().Draft;
        QuestionDefinition question = seed.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        CriterionDefinition criterion = evaluator.Criteria[0];
        return seed with
        {
            Id = "definition-presentation",
            Name = "採点設計 🧪",
            Revision = "presentation-2",
            SourceSheet = "合成入力",
            HeaderRow = 2,
            FirstDataRow = 3,
            LastDataRow = 102,
            RoundingDigits = 6,
            Questions =
            [
                .. Enumerable.Range(1, questionCount).Select(questionIndex => question with
                {
                    Id = $"question-{questionIndex}",
                    DisplayName = $"設問 {questionIndex}",
                    QuestionText = $"合成設問 {questionIndex}\r\n第二行 🧪",
                    SupportingSourceColumns = ["C", "B"],
                    Points = questionIndex == 1 ? 40m : 0m,
                    Evaluators =
                    [
                        .. Enumerable.Range(1, 2).Select(evaluatorIndex => evaluator with
                        {
                            Id = $"evaluator-{questionIndex}-{evaluatorIndex}",
                            DisplayName = $"評価方法 {evaluatorIndex}",
                            Type = evaluatorIndex == 1 ? EvaluatorType.KnowledgeCoverage : EvaluatorType.CustomPrompt,
                            Weight = evaluatorIndex == 1 ? 1.25m : 2.125m,
                            Range = new ScoreRange(0.125m, 10.875m),
                            BuiltInTemplateVersion = evaluatorIndex == 1 ? BuiltInPromptTemplates.KnowledgeTemplateVersion : null,
                            CustomPromptTemplate = evaluatorIndex == 1 ? null : "合成 {{literal}}\r\n{回答}\n{評価項目}",
                            Criteria =
                            [
                                .. Enumerable.Range(1, 2).Select(criterionIndex => criterion with
                                {
                                    Id = $"criterion-{questionIndex}-{evaluatorIndex}-{criterionIndex}",
                                    DisplayName = $"観点 {criterionIndex}",
                                    Description = "説明\t関係・適用 🧪",
                                    Weight = criterionIndex == 1 ? 0.375m : 1.125m,
                                    Range = criterionIndex == 1 ? null : new ScoreRange(-1.25m, 3.5m),
                                    Enabled = criterionIndex == 1,
                                }),
                            ],
                        }),
                    ],
                    SpecialEvaluations =
                    [
                        .. Enumerable.Range(1, 2).Select(specialIndex => new SpecialEvaluationDefinition
                        {
                            Id = $"special-{questionIndex}-{specialIndex}",
                            DisplayName = $"固有 {specialIndex}",
                            PrimarySourceColumn = "B",
                            SupportingSourceColumns = ["C", "A"],
                            PromptTemplate = "合成の固有評価\n{回答}",
                            Enabled = specialIndex == 1,
                        }),
                    ],
                }),
            ],
        };
    }

    private static void SelectChildren(QuestionDesignItemViewModel question)
    {
        question.SelectedEvaluator = question.Evaluators[1];
        question.SelectedSpecialEvaluation = question.SpecialEvaluations[1];
        foreach (EvaluatorDesignItemViewModel evaluator in question.Evaluators)
        {
            evaluator.SelectedCriterion = evaluator.Criteria[1];
        }
    }

    private static QuantificationDefinition CloneForInput(QuantificationDefinition definition) => definition with
    {
        Questions = [.. definition.Questions.Select(question => question with
        {
            SupportingSourceColumns = [.. question.SupportingSourceColumns],
            Evaluators = [.. question.Evaluators.Select(evaluator => evaluator with
            {
                Criteria = [.. evaluator.Criteria.Select(criterion => criterion with { })],
            })],
            SpecialEvaluations = [.. question.SpecialEvaluations.Select(special => special with
            {
                SupportingSourceColumns = [.. special.SupportingSourceColumns],
            })],
        })],
    };

    private static void AssertCanonical(QuantificationDefinition expected, QuantificationDefinition actual)
    {
        CanonicalDefinitionSerializer serializer = new();
        Assert.Equal(serializer.Serialize(expected), serializer.Serialize(actual));
        Assert.Equal(serializer.ComputeSha256(expected), serializer.ComputeSha256(actual));
    }

    private static void AssertPage(
        QuantificationDesignViewModel viewModel,
        int expectedIndex,
        string expectedSummary,
        IEnumerable<QuestionDesignItemViewModel> expectedVisible,
        bool canPrevious,
        bool canNext)
    {
        Assert.Equal(expectedIndex, viewModel.PageIndex);
        Assert.Equal(expectedSummary, viewModel.PageSummary);
        Assert.Equal(canPrevious, viewModel.PreviousPageCommand.CanExecute(null));
        Assert.Equal(canNext, viewModel.NextPageCommand.CanExecute(null));
        QuestionDesignItemViewModel[] expected = expectedVisible.ToArray();
        Assert.Equal(expected.Length, viewModel.VisibleQuestions.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Same(expected[index], viewModel.VisibleQuestions[index]);
            Assert.Contains(viewModel.Questions, item => ReferenceEquals(item, expected[index]));
        }

        if (viewModel.SelectedQuestion is { } selected)
        {
            Assert.Contains(viewModel.Questions, item => ReferenceEquals(item, selected));
        }
    }
}