using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
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

// Requirements: FR-013 (AC-014)
public sealed class DesignStateTests
{
    [Fact]
    public void Unchanged_sync_preserves_instances_selections_and_notifications()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        SelectSecondItems(viewModel);
        QuantificationDefinition draft = viewModel.Draft;
        QuantificationDefinition incoming = CloneForInput(draft);
        var questions = viewModel.Questions;
        IReadOnlyList<string> columns = viewModel.AvailableColumnNames;
        UiObservableObject[] items = EnumerateEditorItems(viewModel).ToArray();
        List<string?> propertyChanges = [];
        int collectionChanges = 0;
        int commandChanges = 0;
        foreach (UiObservableObject item in items)
        {
            item.PropertyChanged += (_, args) => propertyChanges.Add(args.PropertyName);
        }

        ((INotifyCollectionChanged)questions).CollectionChanged += (_, _) => collectionChanges++;
        foreach (QuestionDesignItemViewModel question in questions)
        {
            question.Evaluators.CollectionChanged += (_, _) => collectionChanges++;
            question.SpecialEvaluations.CollectionChanged += (_, _) => collectionChanges++;
            foreach (EvaluatorDesignItemViewModel evaluator in question.Evaluators)
            {
                evaluator.Criteria.CollectionChanged += (_, _) => collectionChanges++;
            }

            foreach (SpecialEvaluationDesignItemViewModel special in question.SpecialEvaluations)
            {
                special.SupportingColumns.CollectionChanged += (_, _) => collectionChanges++;
            }
        }

        viewModel.ApplyImportedPromptCommand.CanExecuteChanged += (_, _) => commandChanges++;
        Assert.NotSame(draft, incoming);
        Assert.NotSame(draft.Questions[1], incoming.Questions[1]);
        Assert.NotSame(draft.Questions[1].Evaluators[1], incoming.Questions[1].Evaluators[1]);
        Assert.NotSame(draft.Questions[1].Evaluators[1].Criteria[1], incoming.Questions[1].Evaluators[1].Criteria[1]);
        Assert.NotSame(draft.Questions[1].SpecialEvaluations[1], incoming.Questions[1].SpecialEvaluations[1]);

        viewModel.SynchronizeFromInput(draft, columns);
        viewModel.SynchronizeFromInput(incoming, ["A", "B", "C", "A", "", " "]);

        Assert.Same(draft, viewModel.Draft);
        Assert.Same(questions, viewModel.Questions);
        Assert.Same(columns, viewModel.AvailableColumnNames);
        UiObservableObject[] after = EnumerateEditorItems(viewModel).ToArray();
        Assert.Equal(items.Length, after.Length);
        for (int index = 0; index < items.Length; index++)
        {
            Assert.Same(items[index], after[index]);
        }

        Assert.Same(questions[1], viewModel.SelectedQuestion);
        foreach (QuestionDesignItemViewModel question in questions)
        {
            Assert.Same(question.Evaluators[1], question.SelectedEvaluator);
            Assert.Same(question.SpecialEvaluations[1], question.SelectedSpecialEvaluation);
            foreach (EvaluatorDesignItemViewModel evaluator in question.Evaluators)
            {
                Assert.Same(evaluator.Criteria[1], evaluator.SelectedCriterion);
            }
        }

        Assert.Empty(propertyChanges);
        Assert.Equal(0, collectionChanges);
        Assert.Equal(0, commandChanges);
        Assert.Equal(60m, viewModel.BasePoints);
        Assert.Equal(0m, viewModel.SpecialPoints);
        Assert.Equal(0.1m, viewModel.SimilarityPenaltyWeight);
        Assert.Equal(100m, viewModel.AllocationTotal);
    }

    [Fact]
    public void Changed_sync_updates_definition_and_preserves_selected_ids()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        SelectSecondItems(viewModel);
        QuestionDesignItemViewModel question = viewModel.Questions[1];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[1];
        SpecialEvaluationDesignItemViewModel special = question.SpecialEvaluations[1];
        var questions = viewModel.Questions;
        var evaluators = question.Evaluators;
        var criteria = evaluator.Criteria;
        var specials = question.SpecialEvaluations;
        QuantificationSnapshot snapshot = viewModel.BuildSnapshot();
        QuantificationDefinition incoming = CloneForInput(viewModel.Draft);
        QuestionDefinition nextQuestion = incoming.Questions[1];
        EvaluatorDefinition nextEvaluator = nextQuestion.Evaluators[1];
        CriterionDefinition nextCriterion = nextEvaluator.Criteria[1] with
        {
            Description = "変更した観点",
            Weight = 3m,
            Range = new ScoreRange(1m, 4m),
            Enabled = false,
        };
        nextEvaluator = nextEvaluator with
        {
            DisplayName = "変更したCustom",
            Weight = 2m,
            Range = new ScoreRange(0m, 5m),
            CustomPromptTemplate = "変更したtemplate {回答} {評価項目}",
            Criteria = [nextCriterion, nextEvaluator.Criteria[2], nextEvaluator.Criteria[0]],
            Enabled = false,
        };
        SpecialEvaluationDefinition nextSpecial = nextQuestion.SpecialEvaluations[1] with
        {
            DisplayName = "変更した固有項目",
            PrimarySourceColumn = "C",
            SupportingSourceColumns = ["D"],
            PromptTemplate = "変更した固有template {回答}",
            Enabled = false,
        };
        nextQuestion = nextQuestion with
        {
            DisplayName = "入力で変更した設問",
            QuestionText = "新しい見出しの設問文",
            PrimarySourceColumn = "C",
            SupportingSourceColumns = ["B"],
            Evaluators = [nextQuestion.Evaluators[2], nextQuestion.Evaluators[0], nextEvaluator],
            SpecialEvaluations = [nextSpecial, nextQuestion.SpecialEvaluations[2], nextQuestion.SpecialEvaluations[0]],
        };
        incoming = incoming with
        {
            Name = "別の入力の設計",
            Revision = "2",
            SourceSheet = "Updated",
            HeaderRow = 2,
            FirstDataRow = 3,
            LastDataRow = 100,
            BasePoints = 55m,
            SpecialPoints = 5m,
            SimilarityPenaltyWeight = 0.25m,
            RoundingDigits = 2,
            Questions = [nextQuestion, incoming.Questions[2], incoming.Questions[0]],
        };

        viewModel.SynchronizeFromInput(incoming, ["C", "A", "D", "B"]);

        Assert.Same(questions, viewModel.Questions);
        Assert.Same(evaluators, question.Evaluators);
        Assert.Same(criteria, evaluator.Criteria);
        Assert.Same(specials, question.SpecialEvaluations);
        AssertSelection(viewModel, question, evaluator, criterion, special);
        Assert.Same(question, questions[0]);
        Assert.Same(evaluator, evaluators[2]);
        Assert.Same(criterion, criteria[0]);
        Assert.Same(special, specials[0]);
        Assert.Equal("新しい見出しの設問文", question.QuestionText);
        Assert.Equal("C", question.PrimarySourceColumn);
        Assert.Equal(new ScoreRange(1m, 4m), criterion.EffectiveRange);
        Assert.False(evaluator.Enabled);
        Assert.False(criterion.Enabled);
        Assert.False(special.Enabled);
        Assert.True(special.SupportingColumns.Single(item => item.ColumnName == "D").IsSelected);
        Assert.False(special.SupportingColumns.Single(item => item.ColumnName == "C").CanSelect);
        Assert.Equal(["C", "A", "D", "B"], special.AvailableColumnNames);
        Assert.NotSame(incoming, viewModel.Draft);
        AssertDefinitionContent(incoming, viewModel.Draft);
        Assert.Equal("1", snapshot.Definition.Revision);
        Assert.Equal(60m, snapshot.Definition.BasePoints);
        Assert.True(snapshot.HasValidHash());
        Assert.True(viewModel.BuildSnapshot().HasValidHash());
    }

    [Theory]
    [InlineData("question")]
    [InlineData("question-columns")]
    [InlineData("question-enabled")]
    [InlineData("evaluator")]
    [InlineData("criterion")]
    [InlineData("special")]
    [InlineData("special-columns")]
    [InlineData("question-order")]
    [InlineData("evaluator-order")]
    [InlineData("criterion-order")]
    [InlineData("special-order")]
    public void Nested_changes_are_not_treated_as_unchanged(string change)
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        SelectSecondItems(viewModel);
        QuestionDesignItemViewModel question = viewModel.Questions[1];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[1];
        SpecialEvaluationDesignItemViewModel special = question.SpecialEvaluations[1];
        QuantificationDefinition previous = viewModel.Draft;
        QuantificationDefinition incoming = CloneForInput(previous);
        QuestionDefinition nextQuestion = incoming.Questions[1];
        EvaluatorDefinition nextEvaluator = nextQuestion.Evaluators[1];
        nextQuestion = change switch
        {
            "question" => nextQuestion with { QuestionText = "入力からの変更" },
            "question-columns" => nextQuestion with { SupportingSourceColumns = ["C", "B"] },
            "question-enabled" => nextQuestion with { Enabled = false },
            "evaluator" => nextQuestion with
            {
                Evaluators = nextQuestion.Evaluators.SetItem(1, nextEvaluator with { Weight = 2m }),
            },
            "criterion" => nextQuestion with
            {
                Evaluators = nextQuestion.Evaluators.SetItem(1, nextEvaluator with
                {
                    Criteria = nextEvaluator.Criteria.SetItem(1, nextEvaluator.Criteria[1] with
                    {
                        Range = new ScoreRange(1m, 8m),
                    }),
                }),
            },
            "special" => nextQuestion with
            {
                SpecialEvaluations = nextQuestion.SpecialEvaluations.SetItem(1, nextQuestion.SpecialEvaluations[1] with
                {
                    PromptTemplate = "入力で変更した固有評価 {回答}",
                }),
            },
            "special-columns" => nextQuestion with
            {
                SpecialEvaluations = nextQuestion.SpecialEvaluations.SetItem(1, nextQuestion.SpecialEvaluations[1] with
                {
                    SupportingSourceColumns = ["A", "C"],
                }),
            },
            "question-order" => nextQuestion,
            "evaluator-order" => nextQuestion.MoveEvaluator(1, 0),
            "criterion-order" => nextQuestion with
            {
                Evaluators = nextQuestion.Evaluators.SetItem(1, nextEvaluator.MoveCriterion(1, 0)),
            },
            "special-order" => nextQuestion.MoveSpecialEvaluation(1, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        incoming = incoming with { Questions = incoming.Questions.SetItem(1, nextQuestion) };
        if (change == "question-order")
        {
            incoming = incoming.MoveQuestion(1, 0);
        }

        viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);

        Assert.NotSame(previous, viewModel.Draft);
        AssertDefinitionContent(incoming, viewModel.Draft);
        AssertSelection(viewModel, question, evaluator, criterion, special);
    }

    [Fact]
    public void Column_only_sync_preserves_draft_and_selection()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        SelectSecondItems(viewModel);
        QuantificationDefinition draft = viewModel.Draft;
        QuestionDesignItemViewModel question = viewModel.Questions[1];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[1];
        SpecialEvaluationDesignItemViewModel special = question.SpecialEvaluations[1];
        SpecialSupportingColumnSelectionViewModel columnB = special.SupportingColumns[1];
        SpecialSupportingColumnSelectionViewModel columnC = special.SupportingColumns[2];
        List<string?> changedProperties = [];
        List<string?> specialProperties = [];
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        special.PropertyChanged += (_, args) => specialProperties.Add(args.PropertyName);
        List<string> columns = ["C", "D", "B", "B", "", " "];

        viewModel.SynchronizeFromInput(CloneForInput(draft), columns);
        columns.Add("Z");

        Assert.Same(draft, viewModel.Draft);
        AssertSelection(viewModel, question, evaluator, criterion, special);
        Assert.Equal(["C", "D", "B"], viewModel.AvailableColumnNames);
        Assert.Equal(["C", "D", "B"], special.SupportingColumns.Select(item => item.ColumnName));
        Assert.Same(columnC, special.SupportingColumns[0]);
        Assert.Same(columnB, special.SupportingColumns[2]);
        Assert.True(columnC.IsSelected);
        Assert.False(columnB.CanSelect);
        Assert.False(special.SupportingColumns[1].IsSelected);
        Assert.Contains(nameof(QuantificationDesignViewModel.AvailableColumnNames), changedProperties);
        Assert.DoesNotContain(nameof(QuantificationDesignViewModel.Draft), changedProperties);
        Assert.Contains(nameof(SpecialEvaluationDesignItemViewModel.AvailableColumnNames), specialProperties);

        special.SupportingColumns[1].IsSelected = true;

        Assert.Equal(["C", "D"], viewModel.Draft.Questions[1].SpecialEvaluations[1].SupportingSourceColumns);
        AssertSelection(viewModel, question, evaluator, criterion, special);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Special_primary_combobox_sync_preserves_incoming_hash_and_user_edits(bool keepPreviousPrimaryCandidate)
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        SelectSecondItems(viewModel);
        QuestionDesignItemViewModel question = viewModel.Questions[1];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[1];
        SpecialEvaluationDesignItemViewModel special = question.SpecialEvaluations[1];
        SpecialEvaluationSettingsView view = new(viewModel);
        Window window = new() { Width = 1260, Height = 900, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ComboBox primaryColumn = Assert.Single(
                view.GetVisualDescendants().OfType<ComboBox>(),
                control => ReferenceEquals(control.DataContext, special)
                    && AutomationProperties.GetAutomationId(control) == $"{special.CardAutomationId}-Primary");
            Assert.Equal("B", primaryColumn.SelectedItem);
            Assert.Equal(["A", "B", "C"], primaryColumn.ItemsSource!.Cast<string>());
            QuantificationDefinition incoming = CloneForInput(viewModel.Draft);
            QuestionDefinition nextQuestion = incoming.Questions[1];
            nextQuestion = nextQuestion with
            {
                SpecialEvaluations = nextQuestion.SpecialEvaluations.SetItem(1, nextQuestion.SpecialEvaluations[1] with
                {
                    PrimarySourceColumn = "D",
                    SupportingSourceColumns = ["A", "C"],
                }),
            };
            incoming = incoming with { Questions = incoming.Questions.SetItem(1, nextQuestion) };
            IReadOnlyList<string> columns = keepPreviousPrimaryCandidate
                ? ["A", "B", "C", "D"]
                : ["A", "C", "D"];
            CanonicalDefinitionSerializer serializer = new();
            string expectedHash = serializer.ComputeSha256(incoming);

            viewModel.SynchronizeFromInput(incoming, columns);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(expectedHash, serializer.ComputeSha256(viewModel.Draft));
            Assert.Equal("D", viewModel.Draft.Questions[1].SpecialEvaluations[1].PrimarySourceColumn);
            Assert.Equal("D", special.PrimarySourceColumn);
            Assert.Equal("D", primaryColumn.SelectedItem);
            Assert.Equal(columns, primaryColumn.ItemsSource!.Cast<string>());
            Assert.Same(question, viewModel.Questions[1]);
            Assert.Same(special, question.SpecialEvaluations[1]);
            AssertSelection(viewModel, question, evaluator, criterion, special);

            primaryColumn.SetCurrentValue(ComboBox.SelectedItemProperty, "C");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("C", special.PrimarySourceColumn);
            Assert.Equal("C", viewModel.Draft.Questions[1].SpecialEvaluations[1].PrimarySourceColumn);
            Assert.Equal(["A"], viewModel.Draft.Questions[1].SpecialEvaluations[1].SupportingSourceColumns);
            AssertSelection(viewModel, question, evaluator, criterion, special);

            primaryColumn.SetCurrentValue(ComboBox.SelectedItemProperty, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(string.Empty, special.PrimarySourceColumn);
            Assert.Equal(string.Empty, viewModel.Draft.Questions[1].SpecialEvaluations[1].PrimarySourceColumn);
            Assert.False(viewModel.IsValid);
            Assert.Contains(viewModel.ValidationErrors, error =>
                error.NodeId == special.Id && error.Field == nameof(SpecialEvaluationDesignItemViewModel.PrimarySourceColumn));

            special.PrimarySourceColumn = "not-a-column";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("not-a-column", special.PrimarySourceColumn);
            Assert.Equal("not-a-column", viewModel.Draft.Questions[1].SpecialEvaluations[1].PrimarySourceColumn);
            Assert.Null(primaryColumn.SelectedItem);

            primaryColumn.SetCurrentValue(ComboBox.SelectedItemProperty, "A");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("A", special.PrimarySourceColumn);
            Assert.Equal("A", viewModel.Draft.Questions[1].SpecialEvaluations[1].PrimarySourceColumn);
            Assert.Empty(viewModel.Draft.Questions[1].SpecialEvaluations[1].SupportingSourceColumns);
            AssertSelection(viewModel, question, evaluator, criterion, special);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("B")]
    [InlineData("C")]
    [InlineData("not-a-column")]
    public void Column_sync_ignores_all_primary_writebacks(string? writeback)
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        SpecialEvaluationDesignItemViewModel special = viewModel.Questions[1].SpecialEvaluations[1];
        QuantificationDefinition draft = viewModel.Draft;
        int writebacks = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(QuantificationDesignViewModel.AvailableColumnNames))
            {
                writebacks++;
                special.PrimarySourceColumn = writeback!;
            }
        };

        viewModel.SynchronizeFromInput(CloneForInput(draft), ["A", "B", "C", "D"]);

        Assert.Equal(1, writebacks);
        Assert.Same(draft, viewModel.Draft);
        Assert.Equal("B", special.PrimarySourceColumn);
        Assert.Equal(["C"], viewModel.Draft.Questions[1].SpecialEvaluations[1].SupportingSourceColumns);

        special.PrimarySourceColumn = "C";

        Assert.Equal("C", special.PrimarySourceColumn);
        Assert.Equal("C", viewModel.Draft.Questions[1].SpecialEvaluations[1].PrimarySourceColumn);
        Assert.Empty(viewModel.Draft.Questions[1].SpecialEvaluations[1].SupportingSourceColumns);
    }

    [Theory]
    [InlineData(nameof(SpecialEvaluationDesignItemViewModel.AvailableColumnNames))]
    [InlineData(nameof(SpecialEvaluationDesignItemViewModel.PrimarySourceColumn))]
    public void Column_sync_releases_primary_writeback_guard_after_notification_failure(string propertyName)
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        SpecialEvaluationDesignItemViewModel special = viewModel.Questions[1].SpecialEvaluations[1];
        QuantificationDefinition draft = viewModel.Draft;
        void FailOnSelection(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == propertyName)
            {
                throw new InvalidOperationException("Simulated selection notification failure.");
            }
        }

        special.PropertyChanged += FailOnSelection;
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                viewModel.SynchronizeFromInput(draft, ["A", "B", "C", "D"]));
        }
        finally
        {
            special.PropertyChanged -= FailOnSelection;
        }

        Assert.Same(draft, viewModel.Draft);
        special.PrimarySourceColumn = null!;

        Assert.Equal(string.Empty, special.PrimarySourceColumn);
        Assert.Equal(string.Empty, viewModel.Draft.Questions[1].SpecialEvaluations[1].PrimarySourceColumn);
        special.PrimarySourceColumn = "C";
        Assert.Equal("C", special.PrimarySourceColumn);
        Assert.Empty(viewModel.Draft.Questions[1].SpecialEvaluations[1].SupportingSourceColumns);
    }

    [AvaloniaFact]
    public void Bound_evaluator_selection_survives_input_reorder_and_deletion()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        SelectSecondItems(viewModel);
        QuestionDesignItemViewModel question = viewModel.Questions[1];
        EvaluatorDesignItemViewModel[] evaluators = question.Evaluators.ToArray();
        EvaluatorSettingsView view = new(viewModel);
        Window window = new() { Width = 1260, Height = 900, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ComboBox evaluatorSelector = Assert.IsType<ComboBox>(view.FindControl<ComboBox>("EvaluatorSelector"));
            Assert.Same(question.Evaluators, evaluatorSelector.ItemsSource);
            Assert.Same(evaluators[1], evaluatorSelector.SelectedItem);
            QuantificationDefinition incoming = CloneForInput(viewModel.Draft);
            QuestionDefinition nextQuestion = incoming.Questions[1];
            nextQuestion = nextQuestion with
            {
                Evaluators = [nextQuestion.Evaluators[1], nextQuestion.Evaluators[2], nextQuestion.Evaluators[0]],
            };
            incoming = incoming with { Questions = incoming.Questions.SetItem(1, nextQuestion) };
            CanonicalDefinitionSerializer serializer = new();
            string expectedHash = serializer.ComputeSha256(incoming);

            viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(expectedHash, serializer.ComputeSha256(viewModel.Draft));
            Assert.Same(question, viewModel.SelectedQuestion);
            Assert.Same(evaluators[1], question.Evaluators[0]);
            Assert.Same(evaluators[1], question.SelectedEvaluator);
            Assert.Same(evaluators[1], evaluatorSelector.SelectedItem);

            nextQuestion = nextQuestion with
            {
                Evaluators = [nextQuestion.Evaluators[2], nextQuestion.Evaluators[1]],
            };
            incoming = incoming with { Questions = incoming.Questions.SetItem(1, nextQuestion) };
            expectedHash = serializer.ComputeSha256(incoming);

            viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(expectedHash, serializer.ComputeSha256(viewModel.Draft));
            Assert.Same(question, viewModel.SelectedQuestion);
            Assert.Same(evaluators[2], question.Evaluators[1]);
            Assert.Same(evaluators[2], question.SelectedEvaluator);
            Assert.Same(evaluators[2], evaluatorSelector.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Delete_commands_fall_back_to_neighbors_at_every_level()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        SelectSecondItems(viewModel);
        QuestionDesignItemViewModel[] questions = viewModel.Questions.ToArray();
        QuestionDesignItemViewModel question = questions[1];
        EvaluatorDesignItemViewModel[] evaluators = question.Evaluators.ToArray();
        EvaluatorDesignItemViewModel evaluator = evaluators[1];
        CriterionDesignItemViewModel[] criteria = evaluator.Criteria.ToArray();
        SpecialEvaluationDesignItemViewModel[] specials = question.SpecialEvaluations.ToArray();

        criteria[1].DeleteCommand.Execute(null);
        Assert.Same(criteria[2], evaluator.SelectedCriterion);
        criteria[2].DeleteCommand.Execute(null);
        Assert.Same(criteria[0], evaluator.SelectedCriterion);
        criteria[0].DeleteCommand.Execute(null);
        Assert.Null(evaluator.SelectedCriterion);
        Assert.Empty(evaluator.Criteria);
        evaluator.AddCriterionCommand.Execute(null);
        Assert.Same(Assert.Single(evaluator.Criteria), evaluator.SelectedCriterion);

        specials[1].DeleteCommand.Execute(null);
        Assert.Same(specials[2], question.SelectedSpecialEvaluation);
        specials[2].DeleteCommand.Execute(null);
        Assert.Same(specials[0], question.SelectedSpecialEvaluation);
        specials[0].DeleteCommand.Execute(null);
        Assert.Null(question.SelectedSpecialEvaluation);
        Assert.Empty(question.SpecialEvaluations);
        question.AddSpecialEvaluationCommand.Execute(null);
        Assert.Same(Assert.Single(question.SpecialEvaluations), question.SelectedSpecialEvaluation);

        evaluators[1].DeleteCommand.Execute(null);
        Assert.Same(evaluators[2], question.SelectedEvaluator);
        evaluators[2].DeleteCommand.Execute(null);
        Assert.Same(evaluators[0], question.SelectedEvaluator);
        evaluators[0].DeleteCommand.Execute(null);
        Assert.Null(question.SelectedEvaluator);
        Assert.Empty(question.Evaluators);
        question.AddCustomEvaluatorCommand.Execute(null);
        Assert.Same(Assert.Single(question.Evaluators), question.SelectedEvaluator);

        questions[1].DeleteCommand.Execute(null);
        Assert.Same(questions[2], viewModel.SelectedQuestion);
        questions[2].DeleteCommand.Execute(null);
        Assert.Same(questions[0], viewModel.SelectedQuestion);
        questions[0].DeleteCommand.Execute(null);
        Assert.Null(viewModel.SelectedQuestion);
        Assert.Empty(viewModel.Questions);
        viewModel.AddQuestionCommand.Execute(null);
        Assert.Same(Assert.Single(viewModel.Questions), viewModel.SelectedQuestion);
    }

    [Fact]
    public void Sync_deletions_restore_neighbor_ids_after_reordering()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        SelectSecondItems(viewModel);
        QuestionDesignItemViewModel nextQuestion = viewModel.Questions[2];
        EvaluatorDesignItemViewModel nextEvaluator = nextQuestion.Evaluators[2];
        CriterionDesignItemViewModel nextCriterion = nextEvaluator.Criteria[2];
        SpecialEvaluationDesignItemViewModel nextSpecial = nextQuestion.SpecialEvaluations[2];
        QuantificationDefinition incoming = CloneForInput(viewModel.Draft);
        QuestionDefinition question = incoming.Questions[2];
        EvaluatorDefinition evaluator = question.Evaluators[2];
        question = question with
        {
            Evaluators =
            [
                evaluator with { Criteria = [evaluator.Criteria[2], evaluator.Criteria[0]] },
                question.Evaluators[0],
            ],
            SpecialEvaluations = [question.SpecialEvaluations[2], question.SpecialEvaluations[0]],
        };
        incoming = incoming with { Questions = [question, incoming.Questions[0]] };

        viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);

        AssertSelection(viewModel, nextQuestion, nextEvaluator, nextCriterion, nextSpecial);
        Assert.Same(nextQuestion, viewModel.Questions[0]);
        Assert.Same(nextEvaluator, nextQuestion.Evaluators[0]);
        Assert.Same(nextCriterion, nextEvaluator.Criteria[0]);
        Assert.Same(nextSpecial, nextQuestion.SpecialEvaluations[0]);
        AssertDefinitionContent(incoming, viewModel.Draft);
    }

    [Fact]
    public void Crud_commands_keep_working_after_sync()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        SelectSecondItems(viewModel);
        QuestionDesignItemViewModel question = viewModel.Questions[1];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[1];
        SpecialEvaluationDesignItemViewModel special = question.SpecialEvaluations[1];
        QuantificationSnapshot original = viewModel.BuildSnapshot();
        viewModel.SynchronizeFromInput(viewModel.Draft with { Revision = "input-revision" }, viewModel.AvailableColumnNames);

        viewModel.AddQuestionCommand.Execute(null);
        QuestionDesignItemViewModel addedQuestion = viewModel.Questions[^1];
        question.DuplicateCommand.Execute(null);
        QuestionDesignItemViewModel copiedQuestion = viewModel.Questions[2];
        Assert.NotEqual(question.Id, copiedQuestion.Id);
        Assert.NotEqual(evaluator.Id, copiedQuestion.Evaluators[1].Id);
        Assert.NotEqual(criterion.Id, copiedQuestion.Evaluators[1].Criteria[1].Id);
        Assert.NotEqual(special.Id, copiedQuestion.SpecialEvaluations[1].Id);
        question.MoveUpCommand.Execute(null);
        Assert.Same(question, viewModel.Questions[0]);
        Assert.False(question.MoveUpCommand.CanExecute(null));
        question.MoveDownCommand.Execute(null);
        copiedQuestion.DeleteCommand.Execute(null);
        addedQuestion.DeleteCommand.Execute(null);
        AssertSelection(viewModel, question, evaluator, criterion, special);

        question.AddCustomEvaluatorCommand.Execute(null);
        EvaluatorDesignItemViewModel addedEvaluator = question.Evaluators[^1];
        Assert.Equal(EvaluatorType.CustomPrompt, addedEvaluator.Type);
        evaluator.DuplicateCommand.Execute(null);
        EvaluatorDesignItemViewModel copiedEvaluator = question.Evaluators[2];
        Assert.NotEqual(evaluator.Id, copiedEvaluator.Id);
        evaluator.MoveUpCommand.Execute(null);
        Assert.Same(evaluator, question.Evaluators[0]);
        evaluator.MoveDownCommand.Execute(null);
        copiedEvaluator.DeleteCommand.Execute(null);
        addedEvaluator.DeleteCommand.Execute(null);
        question.AddKnowledgeEvaluatorCommand.Execute(null);
        Assert.Equal(EvaluatorType.KnowledgeCoverage, question.Evaluators[^1].Type);
        question.Evaluators[^1].DeleteCommand.Execute(null);
        AssertSelection(viewModel, question, evaluator, criterion, special);

        evaluator.AddCriterionCommand.Execute(null);
        CriterionDesignItemViewModel addedCriterion = evaluator.Criteria[^1];
        criterion.DuplicateCommand.Execute(null);
        CriterionDesignItemViewModel copiedCriterion = evaluator.Criteria[2];
        Assert.NotEqual(criterion.Id, copiedCriterion.Id);
        criterion.MoveUpCommand.Execute(null);
        Assert.Same(criterion, evaluator.Criteria[0]);
        criterion.MoveDownCommand.Execute(null);
        copiedCriterion.DeleteCommand.Execute(null);
        addedCriterion.DeleteCommand.Execute(null);
        AssertSelection(viewModel, question, evaluator, criterion, special);

        question.AddSpecialEvaluationCommand.Execute(null);
        SpecialEvaluationDesignItemViewModel addedSpecial = question.SpecialEvaluations[^1];
        special.DuplicateCommand.Execute(null);
        SpecialEvaluationDesignItemViewModel copiedSpecial = question.SpecialEvaluations[2];
        Assert.NotEqual(special.Id, copiedSpecial.Id);
        special.MoveUpCommand.Execute(null);
        Assert.Same(special, question.SpecialEvaluations[0]);
        special.MoveDownCommand.Execute(null);
        copiedSpecial.DeleteCommand.Execute(null);
        addedSpecial.DeleteCommand.Execute(null);
        AssertSelection(viewModel, question, evaluator, criterion, special);

        question.QuestionText = "同期後の手動編集";
        evaluator.Weight = 3m;
        criterion.HasCustomRange = true;
        criterion.Minimum = 1m;
        criterion.Maximum = 8m;
        special.PromptTemplate = "同期後の固有評価 {回答}";

        Assert.Equal("同期後の手動編集", viewModel.Draft.Questions[1].QuestionText);
        Assert.Equal(3m, viewModel.Draft.Questions[1].Evaluators[1].Weight);
        Assert.Equal(new ScoreRange(1m, 8m), viewModel.Draft.Questions[1].Evaluators[1].Criteria[1].Range);
        Assert.Equal("同期後の固有評価 {回答}", viewModel.Draft.Questions[1].SpecialEvaluations[1].PromptTemplate);
        Assert.Equal("input-revision", viewModel.Revision);
        Assert.True(viewModel.BuildSnapshot().HasValidHash());
        Assert.True(original.HasValidHash());
        Assert.Equal("1", original.Definition.Revision);
        Assert.Null(original.Definition.Questions[1].Evaluators[1].Criteria[1].Range);
        AssertSelection(viewModel, question, evaluator, criterion, special);
    }

    [Fact]
    public void Imported_prompts_remain_ordered_and_require_explicit_apply()
    {
        ImportedPrompt[] sources = CreatePrompts();
        QuantificationDesignViewModel viewModel = CreateDesign(sources);
        SelectSecondItems(viewModel);
        QuestionDesignItemViewModel question = viewModel.Questions[1];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        SpecialEvaluationDesignItemViewModel special = question.SpecialEvaluations[1];
        var prompts = viewModel.ImportedPrompts;
        ImportedPromptViewModel first = prompts[0];
        ImportedPromptViewModel second = prompts[1];
        var apply = viewModel.ApplyImportedPromptCommand;
        string? customTemplate = evaluator.CustomPromptTemplate;
        string specialTemplate = special.PromptTemplate;
        viewModel.SelectedImportedPrompt = second;
        viewModel.SelectedPromptTarget = ImportedPromptTarget.SpecialEvaluation;

        viewModel.SynchronizeFromInput(CloneForInput(viewModel.Draft), viewModel.AvailableColumnNames);
        viewModel.SynchronizeFromInput(viewModel.Draft with { Name = "入力で改名" }, ["C", "B", "A"]);

        Assert.Same(prompts, viewModel.ImportedPrompts);
        Assert.Same(first, prompts[0]);
        Assert.Same(second, prompts[1]);
        Assert.Same(second, viewModel.SelectedImportedPrompt);
        Assert.Same(apply, viewModel.ApplyImportedPromptCommand);
        Assert.Same(sources[0], viewModel.ImportedPromptSources[0]);
        Assert.Same(sources[1], viewModel.ImportedPromptSources[1]);
        Assert.Equal(["02-custom.txt", "01-special.txt"], prompts.Select(item => item.DisplayName));
        Assert.Equal(sources.Select(item => item.Content), prompts.Select(item => item.Content));
        Assert.Equal(ImportedPromptTarget.SpecialEvaluation, viewModel.SelectedPromptTarget);
        Assert.Equal(second.Content, viewModel.ImportedPromptPreview);
        Assert.Equal(customTemplate, evaluator.CustomPromptTemplate);
        Assert.Equal(specialTemplate, special.PromptTemplate);
        Assert.True(apply.CanExecute(null));

        apply.Execute(null);

        Assert.Equal(second.Content, special.PromptTemplate);
        Assert.Equal(customTemplate, evaluator.CustomPromptTemplate);
        Assert.Equal(specialTemplate, question.SpecialEvaluations[0].PromptTemplate);
        viewModel.SelectedPromptTarget = ImportedPromptTarget.CustomEvaluator;
        viewModel.SelectedImportedPrompt = first;
        apply.Execute(null);
        Assert.Equal(first.Content, evaluator.CustomPromptTemplate);
        Assert.Null(question.Evaluators[0].CustomPromptTemplate);
        Assert.Equal(second.Content, special.PromptTemplate);

        viewModel.SynchronizeFromInput(viewModel.Draft with { Questions = [] }, []);

        Assert.Null(viewModel.SelectedQuestion);
        Assert.False(apply.CanExecute(null));
        Assert.Same(first, viewModel.SelectedImportedPrompt);
        Assert.Same(prompts, viewModel.ImportedPrompts);
        Assert.Equal(sources.Select(item => item.Content), prompts.Select(item => item.Content));
        Assert.Equal(ImportedPromptTarget.CustomEvaluator, viewModel.SelectedPromptTarget);
    }

    [Fact]
    public void Sync_refreshes_apply_command_when_evaluator_type_changes()
    {
        QuantificationDesignViewModel viewModel = CreateDesign(CreatePrompts());
        SelectSecondItems(viewModel);
        QuestionDesignItemViewModel question = viewModel.Questions[1];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        var apply = viewModel.ApplyImportedPromptCommand;
        List<string?> properties = [];
        int commandChanges = 0;
        viewModel.PropertyChanged += (_, args) => properties.Add(args.PropertyName);
        apply.CanExecuteChanged += (_, _) => commandChanges++;
        Assert.True(apply.CanExecute(null));
        QuantificationDefinition incoming = CloneForInput(viewModel.Draft);
        QuestionDefinition nextQuestion = incoming.Questions[1];
        nextQuestion = nextQuestion with
        {
            Evaluators = nextQuestion.Evaluators.SetItem(1, nextQuestion.Evaluators[1] with
            {
                Type = EvaluatorType.KnowledgeCoverage,
                BuiltInTemplateVersion = BuiltInPromptTemplates.KnowledgeTemplateVersion,
                CustomPromptTemplate = null,
            }),
        };
        incoming = incoming with { Questions = incoming.Questions.SetItem(1, nextQuestion) };

        viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);

        Assert.Same(evaluator, question.SelectedEvaluator);
        Assert.True(evaluator.IsKnowledge);
        Assert.False(viewModel.CanApplyImportedPrompt);
        Assert.False(apply.CanExecute(null));
        Assert.True(commandChanges > 0);
        Assert.Contains(nameof(QuantificationDesignViewModel.CanApplyImportedPrompt), properties);
        QuantificationDefinition knowledgeDraft = viewModel.Draft;
        apply.Execute(null);
        Assert.Same(knowledgeDraft, viewModel.Draft);

        nextQuestion = nextQuestion with
        {
            Evaluators = nextQuestion.Evaluators.SetItem(1, nextQuestion.Evaluators[1] with
            {
                Type = EvaluatorType.CustomPrompt,
                BuiltInTemplateVersion = null,
                CustomPromptTemplate = "編集中の不完全なtemplate",
            }),
        };
        incoming = incoming with { Questions = incoming.Questions.SetItem(1, nextQuestion) };
        properties.Clear();
        commandChanges = 0;

        viewModel.SynchronizeFromInput(incoming, viewModel.AvailableColumnNames);

        Assert.Same(evaluator, question.SelectedEvaluator);
        Assert.False(viewModel.IsValid);
        Assert.Contains(viewModel.ValidationErrors, error => error.NodeId == evaluator.Id);
        Assert.True(apply.CanExecute(null));
        Assert.True(commandChanges > 0);
        Assert.Contains(nameof(QuantificationDesignViewModel.CanApplyImportedPrompt), properties);
        QuantificationDefinition invalidDraft = viewModel.Draft;
        viewModel.SynchronizeFromInput(CloneForInput(invalidDraft), viewModel.AvailableColumnNames);
        Assert.Same(invalidDraft, viewModel.Draft);

        apply.Execute(null);

        Assert.Equal(viewModel.ImportedPrompts[0].Content, evaluator.CustomPromptTemplate);
        Assert.True(viewModel.IsValid);
    }

    [Fact]
    public void Cleared_selections_survive_sync_and_argument_errors_are_nonmutating()
    {
        QuantificationDesignViewModel viewModel = CreateDesign();
        QuestionDesignItemViewModel question = viewModel.Questions[1];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[1];
        viewModel.SelectedQuestion = null;
        question.SelectedEvaluator = null;
        question.SelectedSpecialEvaluation = null;
        evaluator.SelectedCriterion = null;

        viewModel.SynchronizeFromInput(CloneForInput(viewModel.Draft), viewModel.AvailableColumnNames);
        viewModel.SynchronizeFromInput(viewModel.Draft with { Revision = "2" }, viewModel.AvailableColumnNames);

        Assert.Null(viewModel.SelectedQuestion);
        Assert.Null(question.SelectedEvaluator);
        Assert.Null(question.SelectedSpecialEvaluation);
        Assert.Null(evaluator.SelectedCriterion);
        QuantificationDefinition draft = viewModel.Draft;
        IReadOnlyList<string> columns = viewModel.AvailableColumnNames;

        Assert.Throws<ArgumentNullException>(() => viewModel.SynchronizeFromInput(null!, ["D"]));
        Assert.Throws<ArgumentNullException>(() => viewModel.SynchronizeFromInput(draft with { Revision = "3" }, null!));

        Assert.Same(draft, viewModel.Draft);
        Assert.Same(columns, viewModel.AvailableColumnNames);
        Assert.Null(viewModel.SelectedQuestion);
        Assert.Null(question.SelectedEvaluator);
        Assert.Null(question.SelectedSpecialEvaluation);
        Assert.Null(evaluator.SelectedCriterion);
    }

    private static QuantificationDesignViewModel CreateDesign(IEnumerable<ImportedPrompt>? prompts = null)
    {
        QuantificationDefinition seed = new QuantificationDesignViewModel().Draft;
        QuestionDefinition question = seed.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        CriterionDefinition criterion = evaluator.Criteria[0];
        return new QuantificationDesignViewModel(seed with
        {
            Id = "definition-state",
            Questions =
            [
                .. Enumerable.Range(1, 3).Select(questionIndex => question with
                {
                    Id = $"question-{questionIndex}",
                    Points = questionIndex == 1 ? 20m : 10m,
                    SupportingSourceColumns = ["B", "C"],
                    Evaluators =
                    [
                        .. Enumerable.Range(1, 3).Select(evaluatorIndex => evaluator with
                        {
                            Id = $"evaluator-{questionIndex}-{evaluatorIndex}",
                            Type = evaluatorIndex == 2 ? EvaluatorType.CustomPrompt : EvaluatorType.KnowledgeCoverage,
                            BuiltInTemplateVersion = evaluatorIndex == 2 ? null : BuiltInPromptTemplates.KnowledgeTemplateVersion,
                            CustomPromptTemplate = evaluatorIndex == 2 ? BuiltInPromptTemplates.CustomPromptPreset : null,
                            Criteria =
                            [
                                .. Enumerable.Range(1, 3).Select(criterionIndex => criterion with
                                {
                                    Id = $"criterion-{questionIndex}-{evaluatorIndex}-{criterionIndex}",
                                }),
                            ],
                        }),
                    ],
                    SpecialEvaluations =
                    [
                        .. Enumerable.Range(1, 3).Select(specialIndex => new SpecialEvaluationDefinition
                        {
                            Id = $"special-{questionIndex}-{specialIndex}",
                            DisplayName = "固有項目",
                            PrimarySourceColumn = "B",
                            SupportingSourceColumns = ["C"],
                            PromptTemplate = "固有評価 {回答}",
                        }),
                    ],
                }),
            ],
        }, ["A", "B", "C"], prompts);
    }

    private static ImportedPrompt[] CreatePrompts() =>
    [
        new ImportedPrompt
        {
            Path = "02-custom.txt",
            DisplayName = "02-custom.txt",
            Content = "Custom import\n{回答} {評価項目}",
        },
        new ImportedPrompt
        {
            Path = "01-special.txt",
            DisplayName = "01-special.txt",
            Content = "Special import\r\n{回答}",
        },
    ];

    private static void SelectSecondItems(QuantificationDesignViewModel viewModel)
    {
        viewModel.SelectedQuestion = viewModel.Questions[1];
        foreach (QuestionDesignItemViewModel question in viewModel.Questions)
        {
            question.SelectedEvaluator = question.Evaluators[1];
            question.SelectedSpecialEvaluation = question.SpecialEvaluations[1];
            foreach (EvaluatorDesignItemViewModel evaluator in question.Evaluators)
            {
                evaluator.SelectedCriterion = evaluator.Criteria[1];
            }
        }
    }

    private static void AssertSelection(
        QuantificationDesignViewModel viewModel,
        QuestionDesignItemViewModel question,
        EvaluatorDesignItemViewModel evaluator,
        CriterionDesignItemViewModel criterion,
        SpecialEvaluationDesignItemViewModel special)
    {
        Assert.Same(question, viewModel.SelectedQuestion);
        Assert.Same(evaluator, question.SelectedEvaluator);
        Assert.Same(criterion, evaluator.SelectedCriterion);
        Assert.Same(special, question.SelectedSpecialEvaluation);
    }

    private static IEnumerable<UiObservableObject> EnumerateEditorItems(QuantificationDesignViewModel viewModel)
    {
        yield return viewModel;
        foreach (QuestionDesignItemViewModel question in viewModel.Questions)
        {
            yield return question;
            foreach (EvaluatorDesignItemViewModel evaluator in question.Evaluators)
            {
                yield return evaluator;
                foreach (CriterionDesignItemViewModel criterion in evaluator.Criteria)
                {
                    yield return criterion;
                }
            }

            foreach (SpecialEvaluationDesignItemViewModel special in question.SpecialEvaluations)
            {
                yield return special;
                foreach (SpecialSupportingColumnSelectionViewModel column in special.SupportingColumns)
                {
                    yield return column;
                }
            }
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

    private static void AssertDefinitionContent(QuantificationDefinition expected, QuantificationDefinition actual)
    {
        CanonicalDefinitionSerializer serializer = new();
        Assert.Equal(serializer.Serialize(expected), serializer.Serialize(actual));
    }
}