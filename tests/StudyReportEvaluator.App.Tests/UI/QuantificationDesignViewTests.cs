using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

public sealed class QuantificationDesignViewTests
{
    [Fact]
    public void Dynamic_question_evaluator_and_criterion_operations_preserve_stable_ids_and_snapshot_isolation()
    {
        QuantificationDesignViewModel viewModel = new();
        Assert.True(viewModel.IsValid);
        QuestionDesignItemViewModel originalQuestion = Assert.Single(viewModel.Questions);
        EvaluatorDesignItemViewModel originalEvaluator = Assert.Single(originalQuestion.Evaluators);
        CriterionDesignItemViewModel originalCriterion = Assert.Single(originalEvaluator.Criteria);
        QuantificationSnapshot activeSnapshot = viewModel.BuildSnapshot();
        QuantificationDefinition beforeMutation = viewModel.Draft;

        QuestionDesignItemViewModel questionCopy = viewModel.DuplicateQuestion(originalQuestion.Id);
        Assert.NotSame(beforeMutation, viewModel.Draft);
        Assert.NotEqual(originalQuestion.Id, questionCopy.Id);
        Assert.NotEqual(originalEvaluator.Id, Assert.Single(questionCopy.Evaluators).Id);
        Assert.NotEqual(originalCriterion.Id, Assert.Single(Assert.Single(questionCopy.Evaluators).Criteria).Id);
        string copiedQuestionId = questionCopy.Id;
        viewModel.MoveQuestionUp(copiedQuestionId);
        Assert.Equal(copiedQuestionId, viewModel.Questions[0].Id);
        Assert.Equal(copiedQuestionId, viewModel.Draft.Questions[0].Id);
        questionCopy.Enabled = false;
        Assert.False(Assert.Single(viewModel.Draft.Questions, question => question.Id == copiedQuestionId).Enabled);
        Assert.Contains(viewModel.Questions, question => question.Id == copiedQuestionId);

        QuantificationSnapshot disabledQuestionSnapshot = viewModel.BuildSnapshot();
        Assert.False(Assert.Single(
            disabledQuestionSnapshot.Definition.Questions,
            question => question.Id == copiedQuestionId).Enabled);
        viewModel.DeleteQuestion(copiedQuestionId);
        Assert.DoesNotContain(viewModel.Questions, question => question.Id == copiedQuestionId);

        EvaluatorDesignItemViewModel evaluatorCopy = viewModel.DuplicateEvaluator(
            originalQuestion.Id,
            originalEvaluator.Id);
        Assert.NotEqual(originalEvaluator.Id, evaluatorCopy.Id);
        Assert.NotEqual(originalCriterion.Id, Assert.Single(evaluatorCopy.Criteria).Id);
        string copiedEvaluatorId = evaluatorCopy.Id;
        viewModel.MoveEvaluatorUp(originalQuestion.Id, copiedEvaluatorId);
        Assert.Equal(copiedEvaluatorId, originalQuestion.Evaluators[0].Id);
        evaluatorCopy.Enabled = false;
        Assert.False(Assert.Single(
            viewModel.Draft.Questions.Single(question => question.Id == originalQuestion.Id).Evaluators,
            evaluator => evaluator.Id == copiedEvaluatorId).Enabled);
        Assert.True(viewModel.BuildSnapshot().Definition.Questions
            .Single(question => question.Id == originalQuestion.Id).Evaluators
            .Any(evaluator => evaluator.Id == copiedEvaluatorId && !evaluator.Enabled));
        viewModel.DeleteEvaluator(originalQuestion.Id, copiedEvaluatorId);
        Assert.DoesNotContain(originalQuestion.Evaluators, evaluator => evaluator.Id == copiedEvaluatorId);

        EvaluatorDesignItemViewModel custom = viewModel.AddEvaluator(
            originalQuestion.Id,
            EvaluatorType.CustomPrompt);
        Assert.Equal(EvaluatorType.CustomPrompt, custom.Type);
        CriterionDesignItemViewModel criterionCopy = viewModel.DuplicateCriterion(
            originalQuestion.Id,
            custom.Id,
            Assert.Single(custom.Criteria).Id);
        string copiedCriterionId = criterionCopy.Id;
        viewModel.MoveCriterionUp(originalQuestion.Id, custom.Id, copiedCriterionId);
        Assert.Equal(copiedCriterionId, custom.Criteria[0].Id);
        criterionCopy.Enabled = false;
        Assert.False(Assert.Single(
            viewModel.Draft.Questions.Single(question => question.Id == originalQuestion.Id).Evaluators
                .Single(evaluator => evaluator.Id == custom.Id).Criteria,
            criterion => criterion.Id == copiedCriterionId).Enabled);
        Assert.True(viewModel.BuildSnapshot().Definition.Questions
            .Single(question => question.Id == originalQuestion.Id).Evaluators
            .Single(evaluator => evaluator.Id == custom.Id).Criteria
            .Any(criterion => criterion.Id == copiedCriterionId && !criterion.Enabled));
        viewModel.DeleteCriterion(originalQuestion.Id, custom.Id, copiedCriterionId);
        Assert.DoesNotContain(custom.Criteria, criterion => criterion.Id == copiedCriterionId);

        QuestionDesignItemViewModel addedQuestion = viewModel.AddQuestion();
        string addedQuestionId = addedQuestion.Id;
        viewModel.DeleteQuestion(addedQuestionId);
        Assert.DoesNotContain(viewModel.Questions, question => question.Id == addedQuestionId);
        Assert.Equal(originalQuestion.Id, activeSnapshot.Definition.Questions[0].Id);
        Assert.Single(activeSnapshot.Definition.Questions);
        Assert.Single(activeSnapshot.Definition.Questions[0].Evaluators);
        Assert.Single(activeSnapshot.Definition.Questions[0].Evaluators[0].Criteria);
        Assert.True(activeSnapshot.HasValidHash());
        Assert.True(viewModel.IsValid);
    }

    [Fact]
    public void Knowledge_prompt_is_immutable_semantic_coverage_and_evaluator_types_are_closed()
    {
        QuantificationDesignViewModel viewModel = new();
        EvaluatorDesignItemViewModel knowledge = Assert.Single(Assert.Single(viewModel.Questions).Evaluators);
        string originalPreview = knowledge.PromptPreview;

        Assert.Equal(EvaluatorType.KnowledgeCoverage, knowledge.Type);
        Assert.Equal("KNOWLEDGE_COVERAGE", knowledge.TypeContractName);
        Assert.True(knowledge.IsPromptReadOnly);
        Assert.Null(knowledge.CustomPromptTemplate);
        Assert.Contains("説明", knowledge.PromptPreview, StringComparison.Ordinal);
        Assert.Contains("関係", knowledge.PromptPreview, StringComparison.Ordinal);
        Assert.Contains("適用", knowledge.PromptPreview, StringComparison.Ordinal);
        Assert.Contains("単語が存在するだけで満点にせず", knowledge.PromptPreview, StringComparison.Ordinal);
        Assert.Contains("APP-OWNED STRUCTURED OUTPUT CONTRACT", knowledge.PromptPreview, StringComparison.Ordinal);

        knowledge.CustomPromptTemplate = "単語数だけを数える PRIVATE-CANARY";

        Assert.Equal(originalPreview, knowledge.PromptPreview);
        Assert.Null(Assert.Single(viewModel.Draft.Questions).Evaluators[0].CustomPromptTemplate);
        Assert.Equal(
            [EvaluatorType.KnowledgeCoverage, EvaluatorType.CustomPrompt],
            QuantificationDesignViewModel.EvaluatorTypes.Select(choice => choice.Type));
        Assert.Equal(
            ["KNOWLEDGE_COVERAGE", "CUSTOM_PROMPT"],
            QuantificationDesignViewModel.EvaluatorTypes.Select(choice => choice.ContractName));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => viewModel.AddEvaluator(Assert.Single(viewModel.Questions).Id, (EvaluatorType)99));
    }

    [Fact]
    public void Root_allocation_defaults_manual_edits_and_explicit_equalize_follow_the_v4_contract()
    {
        QuantificationDesignViewModel viewModel = new();
        QuestionDesignItemViewModel first = Assert.Single(viewModel.Questions);

        Assert.Equal(60m, viewModel.BasePoints);
        Assert.Equal(0m, viewModel.SpecialPoints);
        Assert.Equal(0.1m, viewModel.SimilarityPenaltyWeight);
        Assert.Equal(40m, first.Points);
        Assert.Equal(100m, viewModel.AllocationTotal);
        Assert.Equal(0m, viewModel.AllocationRemaining);
        Assert.True(viewModel.IsAllocationValid);

        QuestionDesignItemViewModel second = viewModel.DuplicateQuestion(first.Id);
        Assert.Equal(40m, first.Points);
        Assert.Equal(40m, second.Points);
        Assert.Equal(140m, viewModel.AllocationTotal);
        Assert.False(viewModel.IsAllocationValid);

        viewModel.BasePoints = 50m;
        viewModel.SpecialPoints = 10m;
        viewModel.SimilarityPenaltyWeight = 0.25m;
        viewModel.AddSpecialEvaluation(first.Id);
        Assert.Equal(40m, first.Points);
        Assert.Equal(40m, second.Points);
        Assert.False(viewModel.IsAllocationValid);

        viewModel.EqualizeQuestionPoints();

        Assert.Equal(20m, first.Points);
        Assert.Equal(20m, second.Points);
        Assert.Equal(100m, viewModel.AllocationTotal);
        Assert.True(viewModel.IsAllocationValid);
        Assert.Contains("100", viewModel.AllocationSummary, StringComparison.Ordinal);
        Assert.True(viewModel.BuildSnapshot().HasValidHash());
    }

    [Fact]
    public void Special_items_support_crud_source_mapping_prompt_validation_and_snapshot_isolation()
    {
        QuantificationDesignViewModel viewModel = new(
            initialDefinition: null,
            availableColumnNames: ["A", "B", "C"]);
        QuestionDesignItemViewModel question = Assert.Single(viewModel.Questions);
        viewModel.BasePoints = 50m;
        viewModel.SpecialPoints = 10m;
        SpecialEvaluationDesignItemViewModel special = viewModel.AddSpecialEvaluation(question.Id);

        special.DisplayName = "学生Prompt";
        special.PrimarySourceColumn = "B";
        special.SupportingColumns.Single(item => item.ColumnName == "A").IsSelected = true;
        special.SupportingColumns.Single(item => item.ColumnName == "C").IsSelected = true;
        special.PromptTemplate = "観点に沿って評価してください。{回答} {補助情報}";

        Assert.Equal(["A", "C"], viewModel.Draft.Questions[0].SpecialEvaluations[0].SupportingSourceColumns);
        Assert.False(special.SupportingColumns.Single(item => item.ColumnName == "B").CanSelect);
        Assert.True(viewModel.IsValid);

        SpecialEvaluationDesignItemViewModel copy = viewModel.DuplicateSpecialEvaluation(question.Id, special.Id);
        Assert.NotEqual(special.Id, copy.Id);
        Assert.Equal(special.PromptTemplate, copy.PromptTemplate);
        viewModel.MoveSpecialEvaluationUp(question.Id, copy.Id);
        Assert.Equal(copy.Id, question.SpecialEvaluations[0].Id);
        copy.Enabled = false;
        Assert.False(viewModel.Draft.Questions[0].SpecialEvaluations[0].Enabled);
        viewModel.DeleteSpecialEvaluation(question.Id, copy.Id);
        Assert.Single(question.SpecialEvaluations);

        QuantificationSnapshot snapshot = viewModel.BuildSnapshot();
        special.PromptTemplate = "missing placeholder";
        Assert.False(viewModel.IsValid);
        Assert.Contains(viewModel.ValidationErrors, error =>
            error.NodeId == special.Id && error.Field == "PromptTemplate");
        Assert.Equal(
            "観点に沿って評価してください。{回答} {補助情報}",
            snapshot.Definition.Questions[0].SpecialEvaluations[0].PromptTemplate);

        special.PromptTemplate = "fixed {回答}";
        Assert.True(viewModel.IsValid);
    }

    [Fact]
    public void Custom_template_validation_handles_required_placeholders_unknown_and_escaped_braces()
    {
        QuantificationDesignViewModel viewModel = new();
        QuestionDesignItemViewModel question = Assert.Single(viewModel.Questions);
        EvaluatorDesignItemViewModel custom = viewModel.AddEvaluator(question.Id, EvaluatorType.CustomPrompt);

        custom.CustomPromptTemplate = string.Empty;
        AssertPromptError(viewModel, custom.Id, "TEMPLATE_REQUIRED");
        Assert.False(viewModel.TryBuildSnapshot(out _));

        custom.CustomPromptTemplate = "{評価項目}";
        AssertPromptError(viewModel, custom.Id, "ANSWER_PLACEHOLDER_REQUIRED");

        custom.CustomPromptTemplate = "{回答}";
        AssertPromptError(viewModel, custom.Id, "CRITERIA_PLACEHOLDER_REQUIRED");

        custom.CustomPromptTemplate = "{回答} {評価項目} {未知}";
        AssertPromptError(viewModel, custom.Id, "UNKNOWN_PLACEHOLDER");

        custom.CustomPromptTemplate = "{回答} {評価項目";
        AssertPromptError(viewModel, custom.Id, "UNCLOSED_PLACEHOLDER");

        custom.CustomPromptTemplate = "literal {{brace}} / answer={回答} / criteria={評価項目}";

        Assert.DoesNotContain(
            viewModel.ValidationErrors,
            error => error.NodeId == custom.Id && error.Field == "CustomPromptTemplate");
        Assert.Contains("literal {brace}", custom.PromptPreview, StringComparison.Ordinal);
        Assert.Contains("【回答 preview】", custom.PromptPreview, StringComparison.Ordinal);
        Assert.Contains("【評価項目 preview】", custom.PromptPreview, StringComparison.Ordinal);
        Assert.True(viewModel.IsValid);
        Assert.True(viewModel.TryBuildSnapshot(out QuantificationSnapshot? snapshot));
        Assert.NotNull(snapshot);

        const string canary = "PRIVATE-PROMPT-CONTENT-CANARY";
        custom.CustomPromptTemplate = canary;
        DesignValidationError error = Assert.Single(
            viewModel.ValidationErrors,
            item => item.NodeId == custom.Id && item.Code == "ANSWER_PLACEHOLDER_REQUIRED");
        Assert.DoesNotContain(canary, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Three_level_weights_and_optional_ranges_show_effective_values_and_block_invalid_snapshot()
    {
        QuantificationDesignViewModel viewModel = new();
        QuestionDesignItemViewModel firstQuestion = Assert.Single(viewModel.Questions);
        QuestionDesignItemViewModel secondQuestion = viewModel.DuplicateQuestion(firstQuestion.Id);
        firstQuestion.Weight = 2m;
        secondQuestion.Weight = 1m;

        Assert.Equal(200m / 3m, firstQuestion.EffectiveWeightPercentage);
        Assert.Equal(100m / 3m, secondQuestion.EffectiveWeightPercentage);

        EvaluatorDesignItemViewModel firstEvaluator = Assert.Single(firstQuestion.Evaluators);
        EvaluatorDesignItemViewModel secondEvaluator = viewModel.DuplicateEvaluator(
            firstQuestion.Id,
            firstEvaluator.Id);
        firstEvaluator.Weight = 3m;
        secondEvaluator.Weight = 1m;
        Assert.Equal(75m, firstEvaluator.EffectiveWeightPercentage);
        Assert.Equal(25m, secondEvaluator.EffectiveWeightPercentage);
        secondEvaluator.Enabled = false;
        Assert.Equal(100m, firstEvaluator.EffectiveWeightPercentage);
        Assert.Equal(0m, secondEvaluator.EffectiveWeightPercentage);
        secondEvaluator.Enabled = true;

        CriterionDesignItemViewModel firstCriterion = Assert.Single(firstEvaluator.Criteria);
        CriterionDesignItemViewModel secondCriterion = viewModel.DuplicateCriterion(
            firstQuestion.Id,
            firstEvaluator.Id,
            firstCriterion.Id);
        firstCriterion.Weight = 1m;
        secondCriterion.Weight = 3m;
        Assert.Equal(25m, firstCriterion.EffectiveWeightPercentage);
        Assert.Equal(75m, secondCriterion.EffectiveWeightPercentage);
        Assert.False(firstCriterion.HasCustomRange);
        Assert.Equal(new ScoreRange(0m, 10m), firstCriterion.EffectiveRange);

        firstCriterion.HasCustomRange = true;
        firstCriterion.Minimum = 1m;
        firstCriterion.Maximum = 5m;
        Assert.True(firstCriterion.HasCustomRange);
        Assert.Equal(new ScoreRange(1m, 5m), firstCriterion.EffectiveRange);
        Assert.Equal(new ScoreRange(1m, 5m), viewModel.Draft.Questions
            .Single(question => question.Id == firstQuestion.Id).Evaluators
            .Single(evaluator => evaluator.Id == firstEvaluator.Id).Criteria
            .Single(criterion => criterion.Id == firstCriterion.Id).Range);

        firstCriterion.Maximum = 1m;
        Assert.False(viewModel.CanBuildSnapshot);
        Assert.Contains(
            viewModel.ValidationErrors,
            error => error.NodeId == firstCriterion.Id && error.Field == "Range");
        Assert.Throws<QuantificationDesignValidationException>(() => viewModel.BuildSnapshot());
        firstCriterion.Maximum = 5m;
        firstCriterion.HasCustomRange = false;
        Assert.Equal(new ScoreRange(0m, 10m), firstCriterion.EffectiveRange);

        firstEvaluator.Minimum = 10m;
        Assert.False(viewModel.CanBuildSnapshot);
        firstEvaluator.Minimum = 0m;
        firstQuestion.Weight = -1m;
        Assert.False(viewModel.CanBuildSnapshot);
        Assert.Contains(
            viewModel.ValidationErrors,
            error => error.NodeId == firstQuestion.Id && error.Field == "Points");
        firstQuestion.Weight = 2m;
        viewModel.EqualizeQuestionPoints();
        Assert.True(viewModel.CanBuildSnapshot);
        Assert.True(viewModel.BuildSnapshot().HasValidHash());
    }

    [Fact]
    public void Extreme_positive_weights_show_effective_percentages_without_decimal_overflow()
    {
        QuantificationDesignViewModel viewModel = new();
        QuestionDesignItemViewModel firstQuestion = Assert.Single(viewModel.Questions);
        firstQuestion.Weight = decimal.MaxValue;
        QuestionDesignItemViewModel secondQuestion = viewModel.DuplicateQuestion(firstQuestion.Id);
        Assert.Equal(50m, firstQuestion.EffectiveWeightPercentage);
        Assert.Equal(50m, secondQuestion.EffectiveWeightPercentage);

        EvaluatorDesignItemViewModel firstEvaluator = Assert.Single(firstQuestion.Evaluators);
        firstEvaluator.Weight = decimal.MaxValue;
        EvaluatorDesignItemViewModel secondEvaluator = viewModel.DuplicateEvaluator(
            firstQuestion.Id,
            firstEvaluator.Id);
        Assert.Equal(50m, firstEvaluator.EffectiveWeightPercentage);
        Assert.Equal(50m, secondEvaluator.EffectiveWeightPercentage);

        CriterionDesignItemViewModel firstCriterion = Assert.Single(firstEvaluator.Criteria);
        firstCriterion.Weight = decimal.MaxValue;
        CriterionDesignItemViewModel secondCriterion = viewModel.DuplicateCriterion(
            firstQuestion.Id,
            firstEvaluator.Id,
            firstCriterion.Id);
        Assert.Equal(50m, firstCriterion.EffectiveWeightPercentage);
        Assert.Equal(50m, secondCriterion.EffectiveWeightPercentage);
        viewModel.EqualizeQuestionPoints();
        Assert.True(viewModel.CanBuildSnapshot);
    }

    [Fact]
    public void Removing_the_last_enabled_child_is_retained_as_an_invalid_draft_until_fixed()
    {
        QuantificationDesignViewModel viewModel = new();
        QuestionDesignItemViewModel question = Assert.Single(viewModel.Questions);
        EvaluatorDesignItemViewModel evaluator = Assert.Single(question.Evaluators);
        CriterionDesignItemViewModel criterion = Assert.Single(evaluator.Criteria);

        viewModel.DeleteCriterion(question.Id, evaluator.Id, criterion.Id);

        Assert.Empty(evaluator.Criteria);
        Assert.Empty(viewModel.Draft.Questions[0].Evaluators[0].Criteria);
        Assert.False(viewModel.TryBuildSnapshot(out QuantificationSnapshot? snapshot));
        Assert.Null(snapshot);
        Assert.Contains(
            viewModel.ValidationErrors,
            error => error.NodeId == evaluator.Id && error.Code == "ENABLED_CRITERION_REQUIRED");

        viewModel.AddCriterion(question.Id, evaluator.Id);
        Assert.True(viewModel.CanBuildSnapshot);
    }

    [Fact]
    public void Design_contract_has_no_warning_acknowledgement_state()
    {
        string[] prohibitedTerms = ["Warning", "Ethics", "Acknowledge", "Consent", "Dismiss"];
        MemberInfo[] members = typeof(QuantificationDesignViewModel).GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(
            members,
            member => prohibitedTerms.Any(term => member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    [AvaloniaFact]
    public void Large_valid_definition_pages_question_summaries_without_rendering_nested_editors()
    {
        QuantificationDesignViewModel viewModel = new(CreateLargeDefinition());
        QuantificationDesignView view = new(viewModel);
        Window window = new()
        {
            Width = 950,
            Height = 450,
            Content = view,
        };

        try
        {
            window.Show();
            window.SetRenderScaling(2d);
            Render();

            Assert.True(viewModel.IsValid);
            Assert.Equal(10, viewModel.Questions.Count);
            Assert.Equal(50, viewModel.Questions.Sum(question => question.Evaluators.Count));
            Assert.Equal(
                1_000,
                viewModel.Questions.Sum(question => question.Evaluators.Sum(evaluator => evaluator.Criteria.Count)));
            Assert.True(viewModel.BuildSnapshot().HasValidHash());
            QuantificationDefinition draft = viewModel.Draft;
            QuestionDesignItemViewModel selected = viewModel.SelectedQuestion!;
            ListBox list = Required<ListBox>(view, "QuestionEditorList");
            Assert.Same(viewModel.VisibleQuestions, list.ItemsSource);
            Assert.NotNull(BindingOperations.GetBindingExpressionBase(list, ListBox.SelectedItemProperty));
            AssertMeasuredPage(view, viewModel);
            Assert.InRange(viewModel.PageSize, 1, 9);
            int initialCapacity = viewModel.PageSize;
            AssertNormalBodyFits(view);
            AssertAccessibleInputsFit(view);
            Assert.Contains(view.GetVisualDescendants(), control => control is VirtualizingStackPanel);
            // Fluent also materializes a hidden PART_EditableTextBox for ComboBox.
            // Count actual editable surfaces, not that template part or read-only details.
            Assert.Equal(
                ["BasePointsTextBox", "SelectedQuestionPointsTextBox", "SimilarityPenaltyWeightTextBox", "SpecialPointsTextBox"],
                view.GetVisualDescendants().OfType<TextBox>()
                    .Where(textBox => textBox.IsEffectivelyVisible && !textBox.IsReadOnly)
                    .Select(textBox => Assert.IsType<string>(textBox.Name)).OrderBy(name => name, StringComparer.Ordinal).ToArray());
            Assert.True(Required<TextBox>(view, "AllocationSummaryText").IsReadOnly);
            TextBox errorDetail = Assert.IsType<TextBox>(
                Assert.IsType<Flyout>(Required<Button>(view, "ErrorDetailsButton").Flyout).Content);
            Assert.True(errorDetail.IsReadOnly);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(), control =>
                (AutomationProperties.GetAutomationId(control) ?? string.Empty).StartsWith("DesignEvaluator-", StringComparison.Ordinal)
                || (AutomationProperties.GetAutomationId(control) ?? string.Empty).StartsWith("DesignCriterion-", StringComparison.Ordinal));
            Assert.Equal(2d, window.RenderScaling);

            List<QuestionDesignItemViewModel> visited = [];
            do
            {
                visited.AddRange(viewModel.VisibleQuestions);
                AssertMeasuredPage(view, viewModel);
                Assert.Same(selected, viewModel.SelectedQuestion);
                Assert.Equal(viewModel.VisibleQuestions.Contains(selected) ? selected : null, list.SelectedItem);
                if (!viewModel.NextPageCommand.CanExecute(null))
                {
                    break;
                }

                Activate(Required<Button>(view, "NextQuestionPageButton"));
            }
            while (true);

            Assert.Equal(viewModel.Questions.ToArray(), visited.ToArray());
            Assert.False(Required<Button>(view, "NextQuestionPageButton").IsEffectivelyEnabled);
            window.Height = 550;
            Render();
            Assert.True(viewModel.PageSize > initialCapacity);
            AssertMeasuredPage(view, viewModel);
            window.Height = 450;
            Render();
            Assert.Equal(initialCapacity, viewModel.PageSize);
            AssertMeasuredPage(view, viewModel);
            Assert.Same(selected, viewModel.SelectedQuestion);
            Assert.Same(draft, viewModel.Draft);
            AssertNormalBodyFits(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Formula_guide_remains_fixed_and_reflects_current_values()
    {
        QuantificationDesignViewModel viewModel = new();
        for (int index = 0; index < 8; index++)
        {
            viewModel.AddQuestion();
        }

        QuantificationDesignView view = new(viewModel);
        Window window = new()
        {
            Width = 950,
            Height = 450,
            Content = view,
        };

        try
        {
            window.Show();
            Render();

            Border formulaGuide = Required<Border>(view, "FormulaGuide");
            TextBlock baseValue = RequiredByAutomationId<TextBlock>(view, "FormulaBaseValue");
            TextBlock specialValue = RequiredByAutomationId<TextBlock>(view, "FormulaSpecialValue");
            TextBlock similarityValue = RequiredByAutomationId<TextBlock>(view, "FormulaSimilarityValue");

            AssertNormalBodyFits(view);
            Assert.Equal("60", baseValue.Text);
            Assert.Equal("0", specialValue.Text);
            Assert.Equal("0.1", similarityValue.Text);
            Assert.Equal("B + Σ(Pq×Rq) + SpecialEarned − Σ(Pq×Lq×W)",
                RequiredByAutomationId<TextBlock>(view, "DesignFormulaSummary").Text);
            Button detailsButton = Required<Button>(view, "FormulaDetailsButton");
            Activate(detailsButton);
            Flyout details = Assert.IsType<Flyout>(detailsButton.Flyout);
            Border formulaDetails = Assert.IsType<Border>(details.Content);
            Assert.Contains(
                formulaDetails.GetVisualDescendants().OfType<TextBlock>(),
                text => string.Equals(text.Text, "設問獲得点", StringComparison.Ordinal));
            Assert.Contains(
                formulaDetails.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "FinalRaw = Base + Σ(Pq × Rq) + SpecialEarned − Σ(Pq × Lq × W)");
            Assert.Contains(
                formulaDetails.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "FinalScore = FinalRaw がblankならblank、それ以外は clamp(FinalRaw, 0, 100)");
            TextBlock earnedLabel = Assert.Single(
                formulaDetails.GetVisualDescendants().OfType<TextBlock>(),
                text => string.Equals(text.Text, "設問獲得点", StringComparison.Ordinal));
            TextBlock penaltyLabel = Assert.Single(
                formulaDetails.GetVisualDescendants().OfType<TextBlock>(),
                text => string.Equals(text.Text, "設問減点", StringComparison.Ordinal));
            Assert.Contains(
                "通常評価率 Rq",
                ToolTip.GetTip(earnedLabel)?.ToString() ?? string.Empty,
                StringComparison.Ordinal);
            Assert.Contains(
                "類似度 Lq",
                ToolTip.GetTip(penaltyLabel)?.ToString() ?? string.Empty,
                StringComparison.Ordinal);
            Assert.Contains(
                formulaDetails.GetVisualDescendants().OfType<TextBlock>(),
                text => (text.Text ?? string.Empty).Contains("計算の各段階", StringComparison.Ordinal));
            Assert.Contains(
                formulaDetails.GetVisualDescendants().OfType<TextBlock>(),
                text => (text.Text ?? string.Empty).Contains("有効設問間でも等分平均", StringComparison.Ordinal));
            details.Hide();
            Render();

            Point before = formulaGuide.TranslatePoint(default, window)
                ?? throw new InvalidOperationException("Formula guide position is unavailable.");
            viewModel.NextPageCommand.Execute(null);
            Render();
            Point after = formulaGuide.TranslatePoint(default, window)
                ?? throw new InvalidOperationException("Formula guide position is unavailable after paging.");
            Assert.InRange(Math.Abs(after.Y - before.Y), 0d, 1d);

            viewModel.BasePoints = 55m;
            viewModel.SpecialPoints = 5m;
            viewModel.SimilarityPenaltyWeight = 0.25m;
            Render();

            Assert.Equal("55", baseValue.Text);
            Assert.Equal("5", specialValue.Text);
            Assert.Equal("0.25", similarityValue.Text);

            viewModel.RoundingDigits = 0;
            viewModel.Questions[^1].Points = 0.000000000000000001m;
            viewModel.PageIndex = 0;
            Render();
            TextBox allocation = Required<TextBox>(view, "AllocationSummaryText");
            Assert.True(allocation.IsReadOnly);
            Assert.Equal("配点合計 100.000000000000000001 / 100 · 残り -0.000000000000000001", allocation.Text);
            Assert.Equal(100.000000000000000001m, viewModel.AllocationTotal);
            Assert.Equal(-0.000000000000000001m, viewModel.AllocationRemaining);
            Assert.False(viewModel.IsAllocationValid);
            Assert.Contains(viewModel.ValidationErrors, error => error.Code == "ALLOCATION_TOTAL_INVALID");
            Assert.Null(view.FindControl<TextBox>("RoundingDigitsTextBox"));

            viewModel.Questions[^1].Points = 0m;
            viewModel.Questions[0].Points = 39.999999999999999999m;
            Render();
            Assert.Equal("配点合計 99.999999999999999999 / 100 · 残り 0.000000000000000001", allocation.Text);
            Assert.Equal(99.999999999999999999m, viewModel.AllocationTotal);
            Assert.Equal(0.000000000000000001m, viewModel.AllocationRemaining);
            Assert.False(viewModel.IsAllocationValid);

            viewModel.Questions[0].Points = 40m;
            Render();
            Assert.Equal("配点合計 100 / 100 · 残り 0", allocation.Text);
            Assert.True(viewModel.IsAllocationValid);

            viewModel.Questions[0].Points = decimal.MaxValue;
            Render();
            Assert.Null(viewModel.AllocationTotal);
            Assert.Equal(viewModel.AllocationSummary, allocation.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void Increasing_the_finite_body_above_550_grows_page_capacity_and_actual_rendered_rows(double scale)
    {
        QuantificationDesignViewModel design = new(CreateLargeDefinition());
        QuestionDesignItemViewModel selected = design.Questions[5];
        design.SelectedQuestion = selected;
        QuantificationDefinition draft = design.Draft;
        QuantificationDesignView view = new(design);
        Window window = new()
        {
            Width = 950, Height = 550, WindowDecorations = WindowDecorations.None, Content = view,
        };
        try
        {
            window.Show();
            window.SetRenderScaling(scale);
            Render();
            ListBox list = Required<ListBox>(view, "QuestionEditorList");
            int compactCapacity = design.PageSize;
            int compactRows = list.GetVisualDescendants().OfType<ListBoxItem>().Count();
            Assert.InRange(compactRows, 1, design.Questions.Count - 1);
            AssertMeasuredPage(view, design);
            AssertNormalBodyFits(view);

            window.Height = 800;
            Render();

            Assert.True(design.PageSize > compactCapacity);
            Assert.True(list.GetVisualDescendants().OfType<ListBoxItem>().Count() > compactRows);
            AssertMeasuredPage(view, design);
            AssertNormalBodyFits(view);
            AssertAccessibleInputsFit(view);
            Assert.Same(selected, design.SelectedQuestion);
            Assert.Same(draft, design.Draft);

            window.Height = 550;
            Render();
            Assert.Equal(compactCapacity, design.PageSize);
            Assert.Equal(compactRows, list.GetVisualDescendants().OfType<ListBoxItem>().Count());
            Assert.Same(selected, design.SelectedQuestion);
            AssertMeasuredPage(view, design);
            AssertNormalBodyFits(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Loaded_input_A_and_design_B_mapping_link_opens_B_and_returns_to_input_B()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        using MainWindowViewModel shell = new(new WorkflowNavigator(), input, new QuantificationDesignViewModel());
        shell.NextCommand.Execute(null);
        InputQuestionMappingViewModel inputA = input.Questions[0];
        InputQuestionMappingViewModel inputB = input.Questions[1];
        QuestionDesignItemViewModel designB = shell.DesignViewModel.Questions.Single(question => question.Id == inputB.Id);
        shell.DesignViewModel.SelectedQuestion = designB;
        Assert.Same(inputA, input.SelectedQuestion);
        QuantificationDesignView view = new(shell.DesignViewModel);
        Window window = new() { Width = 950, Height = 550, DataContext = shell, Content = view };
        try
        {
            window.Show();
            Render();
            Button link = Required<Button>(view, "OpenQuestionSettingsButton");
            Assert.Same(shell.OpenSettingsCommand, link.Command);
            Activate(link);
            Assert.True(shell.IsSettingsOpen);
            Assert.Equal(SettingsCategory.Mapping, shell.Settings.SelectedCategory);
            Assert.Same(inputB, input.SelectedQuestion);
            Assert.Same(designB, shell.DesignViewModel.SelectedQuestion);
            MappingSettingsView mapping = new(shell.Settings.Input);
            window.Content = mapping;
            Render();
            Assert.Same(inputB, Required<ComboBox>(mapping, "QuestionSelector").SelectedItem);
            Assert.Equal(designB.QuestionText, Required<TextBox>(mapping, "QuestionTextEditor").Text);
            Assert.True(Required<TextBox>(mapping, "QuestionTextEditor").IsReadOnly);

            shell.NavigateCommand.Execute(WorkflowStep.Input);
            InputView mainInput = new(input);
            window.Content = mainInput;
            Render();
            Assert.False(shell.IsSettingsOpen);
            Assert.Equal(WorkflowStep.Input, shell.CurrentStep);
            Assert.Same(inputB, input.SelectedQuestion);
            Assert.Same(inputB, Required<ComboBox>(mainInput, "QuestionSelector").SelectedItem);
            Assert.Same(inputB, Required<TextBox>(mainInput, "QuestionTextEditor").DataContext);
            Assert.Equal(inputB.QuestionText, Required<TextBox>(mainInput, "QuestionTextEditor").Text);
            Assert.False(Required<TextBox>(mainInput, "QuestionTextEditor").IsReadOnly);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("FirstDataRow", "FirstDataRowTextBox")]
    [InlineData("LastDataRow", "LastDataRowTextBox")]
    [InlineData("HeaderRow", "HeaderRowComboBox")]
    [InlineData("SourceSheet", "WorksheetComboBox")]
    [InlineData("SelectedRowCount", "LastDataRowTextBox")]
    [InlineData("QuestionText", "QuestionTextEditor")]
    [InlineData("PrimarySourceColumn", "PrimaryColumnComboBox")]
    public async Task Input_owned_design_errors_navigate_to_the_actual_input_editor_not_mapping(string field, string editorName)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputQuestionMappingViewModel target = input.Questions[1];
        switch (field)
        {
            case "FirstDataRow": input.FirstDataRow = 0; break;
            case "LastDataRow": input.LastDataRow = 0; break;
            case "SourceSheet": input.SelectedSheet = string.Empty; break;
            case "SelectedRowCount": input.LastDataRow = 20_003; break;
            case "QuestionText": target.QuestionText = string.Empty; break;
            case "PrimarySourceColumn": target.PrimarySourceColumn = string.Empty; break;
        }

        using MainWindowViewModel shell = new(new WorkflowNavigator(), input, new QuantificationDesignViewModel());
        MainWindow window = new(shell) { Width = 1180, Height = 1000 };
        try
        {
            window.Show();
            shell.NextCommand.Execute(null);
            if (field == "HeaderRow")
            {
                // Invalid imported root values can reach Design even though the
                // Input selector only permits 1 or 2; its repair owner is Input.
                shell.DesignViewModel.SynchronizeFromInput(
                    shell.DesignViewModel.Draft with { HeaderRow = 3 }, input.AvailableColumnNames);
            }

            Render();
            QuantificationDesignView view = Assert.Single(window.GetVisualDescendants().OfType<QuantificationDesignView>());
            DesignValidationError error = Assert.Single(shell.DesignViewModel.ValidationErrors, item => item.Field == field);
            Required<ComboBox>(view, "ValidationErrorSelector").SetCurrentValue(ComboBox.SelectedItemProperty, error);
            Render();
            Assert.Equal(WorkflowStep.Design, shell.CurrentStep);
            Activate(Required<Button>(view, "GoToProblemButton"));
            Render();

            Assert.Equal(WorkflowStep.Input, shell.CurrentStep);
            Assert.False(shell.IsSettingsOpen);
            Assert.DoesNotContain(window.GetVisualDescendants(), control => control is MappingSettingsView);
            InputView inputView = Assert.Single(window.GetVisualDescendants().OfType<InputView>());
            Assert.Same(input, inputView.DataContext);
            Control editor = inputView.FindControl<Control>(editorName)!;
            Assert.NotNull(editor);
            Assert.True(editor.IsEffectivelyVisible && editor.IsEffectivelyEnabled);
            Assert.Same(editor, window.FocusManager?.GetFocusedElement());
            if (field == "FirstDataRow")
            {
                Assert.Equal(0, input.FirstDataRow);
                Assert.Equal("0", Assert.IsType<TextBox>(editor).Text);
            }
            else if (field is "QuestionText" or "PrimarySourceColumn")
            {
                Assert.Same(target, input.SelectedQuestion);
                Assert.Same(target, editor.DataContext);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Many_long_question_names_leave_a_usable_editor_viewport()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        QuantificationDefinition seed = input.CreateDesignDefinition();
        QuantificationDefinition definition = seed with
        {
            Questions = [.. Enumerable.Range(1, 40).Select(index => seed.Questions[0] with
            {
                Id = $"question-{index}",
                DisplayName = new string('設', 256) + index,
                Points = 1m,
                Evaluators = [seed.Questions[0].Evaluators[0] with
                {
                    Id = $"evaluator-{index}",
                    Criteria = [seed.Questions[0].Evaluators[0].Criteria[0] with { Id = $"criterion-{index}" }],
                }],
            })],
        };
        QuantificationDesignViewModel viewModel = new(definition, input.AvailableColumnNames);
        Assert.True(await input.ApplySavedDefinitionAsync(definition, TestContext.Current.CancellationToken));
        using MainWindowViewModel shell = new(new WorkflowNavigator(), input, viewModel);
        shell.NextCommand.Execute(null);
        Assert.Equal(40, viewModel.Questions.Count);
        QuantificationDesignView view = new(viewModel);
        Window window = new() { Width = 760, Height = 600, DataContext = shell, Content = view };
        try
        {
            window.Show();
            Render();
            ScrollViewer scroll = Required<ScrollViewer>(view, "QuantificationDesignScrollViewer");
            Assert.True(scroll.Viewport.Height >= 200d,
                $"Question names consumed the editor viewport: {scroll.Viewport.Height:F1} DIP.");
            AssertMeasuredPage(view, viewModel);
            AssertAccessibleInputsFit(view);
            foreach (QuestionDesignItemViewModel question in viewModel.VisibleQuestions)
            {
                TextBlock name = RequiredByAutomationId<TextBlock>(view, question.NameAutomationId);
                Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
                Assert.Equal(1, name.MaxLines);
                Assert.Equal(question.DisplayName, AutomationProperties.GetName(name));
            }

            Button open = Required<Button>(view, "OpenQuestionSettingsButton");
            Activate(open);
            Assert.Equal(SettingsCategory.Mapping, shell.Settings.SelectedCategory);
            Assert.Same(viewModel.Questions[0], viewModel.SelectedQuestion);
            // T22 owns cross-VM selection sync; verify the new mapping view's full
            // text surface, not another Input/Design synchronization implementation.
            MappingSettingsView settings = new(input);
            window.Content = settings;
            Render();
            TextBox fullName = Required<TextBox>(settings, "QuestionNameEditor");
            Assert.NotNull(viewModel.SelectedQuestion);
            Assert.Equal(viewModel.SelectedQuestion.DisplayName, fullName.Text);
            Assert.True(fullName.Focus(NavigationMethod.Tab));
            Press(window, Key.End, RawInputModifiers.Control);
            Assert.Equal(fullName.Text!.Length, fullName.CaretIndex);
            shell.CloseSettings();
            window.Content = view;
            Render();
            AssertMeasuredPage(view, viewModel);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Paged_questions_and_direct_settings_links_preserve_the_same_prompt_target()
    {
        ImportedPrompt source = new() { Path = "t19.txt", DisplayName = "t19.txt", Content = "原文\n{回答} {評価項目}" };
        QuantificationDesignViewModel viewModel = new(null, ["A", "B"], [source]);
        QuestionDesignItemViewModel first = Assert.Single(viewModel.Questions);
        first.DisplayName = "設問A";
        QuestionDesignItemViewModel second = viewModel.DuplicateQuestion(first.Id);
        second.DisplayName = "設問B";
        viewModel.EqualizeQuestionPoints();
        EvaluatorDesignItemViewModel custom = viewModel.AddEvaluator(first.Id, EvaluatorType.CustomPrompt);
        SpecialEvaluationDesignItemViewModel special = viewModel.AddSpecialEvaluation(first.Id);
        first.SelectedEvaluator = custom;
        first.SelectedSpecialEvaluation = special;
        viewModel.SelectedQuestion = first;
        using MainWindowViewModel shell = new(new WorkflowNavigator(), new InputViewModel(), viewModel);
        QuantificationDesignView view = new(viewModel);
        Window window = new()
        {
            Width = 950,
            Height = 450,
            DataContext = shell,
            Content = view,
        };

        try
        {
            window.Show();
            Render();

            AssertMeasuredPage(view, viewModel);
            QuantificationDefinition draft = viewModel.Draft;
            foreach ((string name, SettingsCategory category) in new[]
            {
                ("OpenEvaluatorSettingsButton", SettingsCategory.Evaluation),
                ("OpenSpecialSettingsButton", SettingsCategory.Special),
                ("OpenImportedPromptsButton", SettingsCategory.ImportedPrompts),
            })
            {
                Button link = Required<Button>(view, name);
                Assert.Same(shell.OpenSettingsCommand, link.Command);
                Assert.Equal(category, Assert.IsType<SettingsCategory>(link.CommandParameter));
                Activate(link);
                Assert.True(shell.IsSettingsOpen);
                Assert.Equal(category, shell.Settings.SelectedCategory);
                Assert.Same(first, viewModel.SelectedQuestion);
                Assert.Same(custom, first.SelectedEvaluator);
                Assert.Same(special, first.SelectedSpecialEvaluation);
                Assert.Equal(ImportedPromptTarget.CustomEvaluator, viewModel.SelectedPromptTarget);
                Assert.Same(draft, viewModel.Draft);
                shell.CloseSettings();
            }

            Assert.Equal("読込Prompt (1)", Required<Button>(view, "OpenImportedPromptsButton").Content);
            Assert.Contains(custom.DisplayName, Required<TextBlock>(view, "EvaluatorSummaryText").Text!, StringComparison.Ordinal);
            Assert.Contains(custom.Criteria[0].DisplayName, Required<TextBlock>(view, "EvaluatorSummaryText").Text!, StringComparison.Ordinal);
            Assert.Contains(special.DisplayName, Required<TextBlock>(view, "SpecialSummaryText").Text!, StringComparison.Ordinal);
            ImportedPromptSettingsView prompts = new(viewModel);
            window.Content = prompts;
            Render();
            TextBlock promptTarget = Required<TextBlock>(prompts, "SelectedPromptTargetSummary");
            Assert.Equal($"設問A → Custom: {custom.DisplayName}", promptTarget.Text);
            Assert.Same(viewModel.ImportedPrompts, Required<ListBox>(prompts, "ImportedPromptList").ItemsSource);
            Assert.True(Required<TextBox>(prompts, "ImportedPromptPreview").IsReadOnly);
            Assert.Equal(source.Content, Required<TextBox>(prompts, "ImportedPromptPreview").Text);
            viewModel.SelectedPromptTarget = ImportedPromptTarget.SpecialEvaluation;
            Render();
            Assert.Equal($"設問A → 固有評価: {special.DisplayName}", promptTarget.Text);

            viewModel.SelectedPromptTarget = ImportedPromptTarget.CustomEvaluator;

            viewModel.SelectedQuestion = second;
            Render();

            Assert.Equal("設問B → Custom評価方法を選択してください", promptTarget.Text);
            window.Content = view;
            Render();
            ListBoxItem selectedCard = Assert.Single(
                view.GetVisualDescendants().OfType<ListBoxItem>(),
                item => ReferenceEquals(item.DataContext, second));
            Assert.True(selectedCard.IsSelected);
            Assert.Equal(new Thickness(3d), selectedCard.BorderThickness);

            viewModel.DeleteQuestion(second.Id);
            Render();

            Assert.Same(first, viewModel.SelectedQuestion);
            AssertMeasuredPage(view, viewModel);
            window.Content = prompts;
            Render();
            Assert.Equal($"設問A → Custom: {custom.DisplayName}", promptTarget.Text);
            Assert.Same(first, Required<ComboBox>(prompts, "ImportedPromptQuestionSelector").SelectedItem);
            Assert.Same(source, Assert.Single(viewModel.ImportedPromptSources));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Standalone_design_keeps_keyboard_points_and_independent_settings_keep_full_prompt_surfaces()
    {
        QuantificationDesignViewModel viewModel = new();
        QuestionDesignItemViewModel question = Assert.Single(viewModel.Questions);
        EvaluatorDesignItemViewModel knowledge = Assert.Single(question.Evaluators);
        EvaluatorDesignItemViewModel custom = viewModel.AddEvaluator(question.Id, EvaluatorType.CustomPrompt);
        QuantificationDesignView view = new(viewModel);
        Window window = new()
        {
            Width = 950,
            Height = 450,
            Content = view,
        };

        try
        {
            window.Show();
            window.SetRenderScaling(2d);
            Render();

            TextBox basePoints = Required<TextBox>(view, "BasePointsTextBox");
            TextBox specialPoints = Required<TextBox>(view, "SpecialPointsTextBox");
            TextBox similarityWeight = Required<TextBox>(view, "SimilarityPenaltyWeightTextBox");
            Button equalize = Required<Button>(view, "EqualizeQuestionPointsButton");
            ListBox questions = Required<ListBox>(view, "QuestionEditorList");
            Border validation = Required<Border>(view, "DesignValidationSummary");

            Assert.Same(viewModel, view.DataContext);
            Assert.Equal(2d, window.RenderScaling);
            AssertNormalBodyFits(view);
            AssertAccessibleInputsFit(view);
            Assert.Equal("DesignQuestions", AutomationProperties.GetAutomationId(questions));
            Assert.Equal("DesignValidationSummary", AutomationProperties.GetAutomationId(validation));
            Assert.Equal("DesignBasePoints", AutomationProperties.GetAutomationId(basePoints));
            Assert.Equal("DesignSpecialPoints", AutomationProperties.GetAutomationId(specialPoints));
            Assert.Equal("DesignSimilarityPenaltyWeight", AutomationProperties.GetAutomationId(similarityWeight));
            Assert.Equal("EqualizeQuestionPoints", AutomationProperties.GetAutomationId(equalize));
            Assert.Same(basePoints, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab);
            Assert.Same(specialPoints, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(basePoints, window.FocusManager?.GetFocusedElement());
            TextBox points = Required<TextBox>(view, "SelectedQuestionPointsTextBox");
            Assert.Equal(question.WeightAutomationId, AutomationProperties.GetAutomationId(points));
            Assert.True(points.Focus(NavigationMethod.Tab));
            Assert.Equal(new Thickness(3d), points.BorderThickness);
            Assert.Contains(view.GetVisualDescendants(), descendant => descendant is VirtualizingStackPanel);
            Assert.Null(view.FindControl<Border>("EthicsWarningBanner"));
            Assert.Null(view.FindControl<TextBox>("DefinitionNameTextBox"));
            Assert.Null(view.FindControl<ListBox>("ImportedPromptList"));
            foreach (string name in new[]
            {
                "OpenQuestionSettingsButton", "OpenEvaluatorSettingsButton", "OpenSpecialSettingsButton", "OpenImportedPromptsButton",
            })
            {
                Button link = Required<Button>(view, name);
                Assert.Null(link.Command);
                Assert.False(link.IsEnabled);
                Assert.False(link.IsEffectivelyEnabled);
            }

            basePoints.SetCurrentValue(TextBox.TextProperty, "55");
            similarityWeight.SetCurrentValue(TextBox.TextProperty, "0.25");
            points.SetCurrentValue(TextBox.TextProperty, "44");
            Render();
            Assert.Equal(55m, viewModel.BasePoints);
            Assert.Equal(0.25m, viewModel.SimilarityPenaltyWeight);
            Assert.Equal(44m, question.Points);
            Assert.Equal(99m, viewModel.AllocationTotal);
            CheckBox enabled = Required<CheckBox>(view, "SelectedQuestionEnabled");
            enabled.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            Render();
            Assert.False(question.Enabled);
            Assert.False(viewModel.Draft.Questions[0].Enabled);
            Assert.Equal(44m, question.Points);
            enabled.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Render();
            Activate(equalize);
            Assert.Equal(45m, question.Points);
            Assert.Equal("45", points.Text);
            Assert.Equal(100m, viewModel.AllocationTotal);

            using ExecutionViewModel execution = new();
            using SettingsViewModel settingsOwner = new(new InputViewModel(), viewModel, execution);
            SettingsView settings = new(settingsOwner);
            window.Content = settings;
            Render();
            Assert.Equal(viewModel.DefinitionName, RequiredByAutomationId<TextBox>(settings, "DesignDefinitionName").Text);
            Assert.Equal(viewModel.Revision, RequiredByAutomationId<TextBox>(settings, "DesignRevision").Text);
            Assert.NotNull(RequiredByAutomationId<TextBox>(settings, "DesignRoundingDigits"));

            EvaluatorSettingsView evaluators = new(viewModel);
            window.Content = evaluators;
            Render();
            Required<TabControl>(evaluators, "EditorTabs").SelectedIndex = 1;
            Render();
            TextBox knowledgePreview = RequiredByAutomationId<TextBox>(evaluators, knowledge.PromptPreviewAutomationId);
            Assert.True(knowledgePreview.IsEffectivelyVisible);
            Assert.True(knowledgePreview.IsReadOnly);
            Assert.Contains("説明", knowledgePreview.Text ?? string.Empty, StringComparison.Ordinal);

            question.SelectedEvaluator = custom;
            Render();
            TextBox customEditor = RequiredByAutomationId<TextBox>(evaluators, custom.PromptAutomationId);
            TextBox customPreview = RequiredByAutomationId<TextBox>(evaluators, custom.PromptPreviewAutomationId);
            Assert.False(customEditor.IsReadOnly);
            Assert.True(customEditor.IsEffectivelyVisible);
            Assert.True(customPreview.IsEffectivelyVisible);
            Assert.True(customPreview.IsReadOnly);
            Assert.True(customEditor.Focus(NavigationMethod.Tab, KeyModifiers.None));
            Assert.Same(customEditor, window.FocusManager?.GetFocusedElement());
            Assert.Equal(new Thickness(3d), customEditor.BorderThickness);
            Press(window, Key.Tab);
            Assert.Same(customPreview, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(customEditor, window.FocusManager?.GetFocusedElement());

            SpecialEvaluationDesignItemViewModel special = viewModel.AddSpecialEvaluation(question.Id);
            SpecialEvaluationSettingsView specials = new(viewModel);
            window.Content = specials;
            Render();
            TabItem promptTab = RequiredByAutomationId<TabItem>(specials, "SpecialSettingsPromptTab");
            promptTab.GetVisualAncestors().OfType<TabControl>().Single().SelectedItem = promptTab;
            Render();
            Assert.False(RequiredByAutomationId<TextBox>(specials, special.PromptAutomationId).IsReadOnly);
            Assert.Equal(special.PromptTemplate, RequiredByAutomationId<TextBox>(specials, special.PromptAutomationId).Text);
            Assert.True(RequiredByAutomationId<TextBox>(specials, special.CardAutomationId + "-PromptPreview").IsReadOnly);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Validation_overview_preserves_exact_errors_and_opens_each_selected_problem()
    {
        QuantificationDesignViewModel design = new();
        QuestionDesignItemViewModel question = design.Questions[0];
        EvaluatorDesignItemViewModel custom = design.AddEvaluator(question.Id, EvaluatorType.CustomPrompt);
        CriterionDesignItemViewModel criterion = custom.Criteria[0];
        SpecialEvaluationDesignItemViewModel special = design.AddSpecialEvaluation(question.Id);
        QuestionDesignItemViewModel other = design.AddQuestion();
        custom.CustomPromptTemplate = "{回答}";
        criterion.Weight = 0m;
        special.PromptTemplate = "placeholder不足";
        design.RoundingDigits = 7;
        QuantificationDefinition draft = design.Draft;
        using MainWindowViewModel shell = new(new WorkflowNavigator(), new InputViewModel(), design);
        QuantificationDesignView view = new(design);
        Window window = new() { Width = 950, Height = 450, DataContext = shell, Content = view };
        try
        {
            window.Show();
            Render();
            ComboBox errors = Required<ComboBox>(view, "ValidationErrorSelector");
            Assert.Same(design.ValidationErrors, errors.ItemsSource);
            Assert.InRange(errors.MaxDropDownHeight, 44d, 220d);
            foreach ((string id, string field, SettingsCategory category) in new[]
            {
                (custom.Id, "CustomPromptTemplate", SettingsCategory.Evaluation),
                (criterion.Id, "Weight", SettingsCategory.Evaluation),
                (special.Id, "PromptTemplate", SettingsCategory.Special),
                (design.Draft.Id, "RoundingDigits", SettingsCategory.Common),
            })
            {
                design.SelectedQuestion = other;
                DesignValidationError error = Assert.Single(design.ValidationErrors, item => item.NodeId == id && item.Field == field);
                errors.SetCurrentValue(ComboBox.SelectedItemProperty, error);
                Render();
                Activate(Required<Button>(view, "ValidateDesignButton"));
                DesignValidationError refreshed = Assert.IsType<DesignValidationError>(errors.SelectedItem);
                Assert.Equal((error.NodeId, error.Field, error.Code), (refreshed.NodeId, refreshed.Field, refreshed.Code));
                Button detailsButton = Required<Button>(view, "ErrorDetailsButton");
                Flyout details = Assert.IsType<Flyout>(detailsButton.Flyout);
                TextBox detail = Assert.IsType<TextBox>(details.Content);
                Assert.Equal(refreshed.AccessibleText, detail.Text);
                Activate(detailsButton);
                Assert.True(detail.Focus(NavigationMethod.Tab));
                Press(Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(detail)), Key.End, RawInputModifiers.Control);
                Assert.Equal(detail.Text!.Length, detail.CaretIndex);
                details.Hide();
                Render();
                Activate(Required<Button>(view, "GoToProblemButton"));
                Assert.Equal(category, shell.Settings.SelectedCategory);
                Assert.True(shell.IsSettingsOpen);
                if (id != design.Draft.Id)
                {
                    Assert.Same(question, design.SelectedQuestion);
                }

                if (id == criterion.Id)
                {
                    Assert.Same(custom, question.SelectedEvaluator);
                    Assert.Same(criterion, custom.SelectedCriterion);
                }

                if (id == special.Id)
                {
                    Assert.Same(special, question.SelectedSpecialEvaluation);
                }

                Assert.Same(draft, design.Draft);
                shell.CloseSettings();
            }

            other.Points = -1m;
            Render();
            errors.SetCurrentValue(ComboBox.SelectedItemProperty,
                Assert.Single(design.ValidationErrors, error => error.NodeId == other.Id && error.Field == "Points"));
            Activate(Required<Button>(view, "GoToProblemButton"));
            Assert.Same(other, design.SelectedQuestion);
            Assert.Same(Required<TextBox>(view, "SelectedQuestionPointsTextBox"), window.FocusManager?.GetFocusedElement());
            Assert.False(shell.IsSettingsOpen);
            AssertNormalBodyFits(view);
            AssertAccessibleInputsFit(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Uncommitted_numeric_text_survives_paging_resize_and_cached_view_reattach()
    {
        QuantificationDesignViewModel design = new(CreateLargeDefinition());
        QuantificationDesignView view = new(design);
        Window window = new() { Width = 950, Height = 450, Content = view };
        try
        {
            window.Show();
            Render();
            TextBox basePoints = Required<TextBox>(view, "BasePointsTextBox");
            TextBox points = Required<TextBox>(view, "SelectedQuestionPointsTextBox");
            QuestionDesignItemViewModel selected = design.SelectedQuestion!;
            QuantificationDefinition draft = design.Draft;
            basePoints.SetCurrentValue(TextBox.TextProperty, "-");
            points.SetCurrentValue(TextBox.TextProperty, "編集中");
            Render();
            Assert.True(DataValidationErrors.GetHasErrors(basePoints));
            Assert.True(DataValidationErrors.GetHasErrors(points));
            Assert.Contains("未反映", Required<TextBlock>(view, "DesignStatusText").Text!, StringComparison.Ordinal);
            Assert.False(Required<Button>(view, "EqualizeQuestionPointsButton").IsEffectivelyEnabled);
            AssertNormalBodyFits(view);
            AssertAccessibleInputsFit(view);
            design.NextPageCommand.Execute(null);
            window.Height = 550;
            Render();
            window.Content = null;
            Render();
            window.Content = view; // T23's cache contract: same controls, not reconstructed VMs.
            Render();
            Assert.Same(basePoints, Required<TextBox>(view, "BasePointsTextBox"));
            Assert.Same(points, Required<TextBox>(view, "SelectedQuestionPointsTextBox"));
            Assert.Equal("-", basePoints.Text);
            Assert.Equal("編集中", points.Text);
            Assert.Same(selected, design.SelectedQuestion);
            Assert.Same(draft, design.Draft);
            Assert.Equal(45m, design.BasePoints);
            Assert.Equal(1m, selected.Points);
            basePoints.SetCurrentValue(TextBox.TextProperty, "45");
            points.SetCurrentValue(TextBox.TextProperty, "1");
            Render();
            Assert.False(DataValidationErrors.GetHasErrors(basePoints));
            Assert.False(DataValidationErrors.GetHasErrors(points));
            Assert.DoesNotContain("未反映", Required<TextBlock>(view, "DesignStatusText").Text!, StringComparison.Ordinal);
            Assert.True(Required<Button>(view, "EqualizeQuestionPointsButton").IsEffectivelyEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Uncommitted_question_points_survive_selection_switches_and_same_value_correction()
    {
        QuantificationDesignViewModel design = new(CreateLargeDefinition());
        QuestionDesignItemViewModel first = design.Questions[0];
        QuestionDesignItemViewModel second = design.Questions[1];
        QuantificationDefinition draft = design.Draft;
        QuantificationDesignView view = new(design);
        Window window = new() { Width = 950, Height = 600, Content = view };
        try
        {
            window.Show();
            Render();
            ListBox list = Required<ListBox>(view, "QuestionEditorList");
            TextBox points = Required<TextBox>(view, "SelectedQuestionPointsTextBox");
            TextBlock status = Required<TextBlock>(view, "DesignStatusText");
            var pointsBinding = BindingOperations.GetBindingExpressionBase(points, TextBox.TextProperty);
            var selectionBinding = BindingOperations.GetBindingExpressionBase(list, ListBox.SelectedItemProperty);
            Assert.NotNull(pointsBinding);
            Assert.NotNull(selectionBinding);
            Assert.NotEqual(first.Points, second.Points);
            Assert.True(points.Focus(NavigationMethod.Tab));
            points.SelectAll();
            window.KeyTextInput("-");
            Render();
            Assert.True(DataValidationErrors.GetHasErrors(points));

            list.SetCurrentValue(ListBox.SelectedItemProperty, second);
            Render();
            Assert.Same(second, design.SelectedQuestion);
            Assert.Equal("2", points.Text);
            Assert.False(DataValidationErrors.GetHasErrors(points));
            Assert.Contains("未反映の入力 1 件", status.Text, StringComparison.Ordinal);
            Assert.Contains(first.Id, status.Text, StringComparison.Ordinal);
            Assert.False(Required<Button>(view, "EqualizeQuestionPointsButton").IsEffectivelyEnabled);
            Activate(Required<Button>(view, "GoToProblemButton"));
            Assert.Same(first, design.SelectedQuestion);
            Assert.Same(points, window.FocusManager?.GetFocusedElement());
            Assert.Equal("-", points.Text);
            Assert.True(DataValidationErrors.GetHasErrors(points));

            // Exercise the actual TwoWay selection binding, including switches
            // before a queued validation/restore pass has run.
            list.SetCurrentValue(ListBox.SelectedItemProperty, second);
            list.SetCurrentValue(ListBox.SelectedItemProperty, first);
            Render();
            Assert.Same(first, list.SelectedItem);
            Assert.Equal("-", points.Text);
            Assert.True(DataValidationErrors.GetHasErrors(points));
            Assert.Same(draft, design.Draft);
            Assert.Equal(1m, first.Points);
            Assert.Equal(2m, second.Points);

            points.SetCurrentValue(TextBox.TextProperty, "1");
            list.SetCurrentValue(ListBox.SelectedItemProperty, second);
            Render();
            list.SetCurrentValue(ListBox.SelectedItemProperty, first);
            Render();
            Assert.Equal("1", points.Text);
            Assert.False(DataValidationErrors.GetHasErrors(points));
            Assert.DoesNotContain("未反映", status.Text, StringComparison.Ordinal);
            Assert.True(Required<Button>(view, "EqualizeQuestionPointsButton").IsEffectivelyEnabled);
            Assert.Same(draft, design.Draft);
            Assert.Same(pointsBinding, BindingOperations.GetBindingExpressionBase(points, TextBox.TextProperty));
            Assert.Same(selectionBinding, BindingOperations.GetBindingExpressionBase(list, ListBox.SelectedItemProperty));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Pending_question_points_are_counted_by_id_and_removed_with_the_question()
    {
        QuantificationDesignViewModel design = new(CreateLargeDefinition());
        QuestionDesignItemViewModel first = design.Questions[0];
        QuestionDesignItemViewModel second = design.Questions[1];
        first.DisplayName = second.DisplayName = "同名の設問";
        QuantificationDefinition draft = design.Draft;
        QuantificationDesignView view = new(design);
        Window window = new() { Width = 950, Height = 600, Content = view };
        try
        {
            window.Show();
            Render();
            ListBox list = Required<ListBox>(view, "QuestionEditorList");
            TextBox points = Required<TextBox>(view, "SelectedQuestionPointsTextBox");
            TextBlock status = Required<TextBlock>(view, "DesignStatusText");
            TextBox detail = Assert.IsType<TextBox>(Assert.IsType<Flyout>(Required<Button>(view, "ErrorDetailsButton").Flyout).Content);
            points.SetCurrentValue(TextBox.TextProperty, "-");
            list.SetCurrentValue(ListBox.SelectedItemProperty, second);
            Render();
            points.SetCurrentValue(TextBox.TextProperty, "編集中");
            Render();

            Assert.Contains("未反映の入力 2 件", status.Text, StringComparison.Ordinal);
            Assert.Contains(first.Id, detail.Text, StringComparison.Ordinal);
            Assert.Contains(second.Id, detail.Text, StringComparison.Ordinal);
            Assert.Same(draft, design.Draft);
            list.SetCurrentValue(ListBox.SelectedItemProperty, first);
            Render();
            Assert.Equal("-", points.Text);
            points.SetCurrentValue(TextBox.TextProperty, "1");
            Render();
            Assert.Contains("未反映の入力 1 件", status.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(first.Id, detail.Text, StringComparison.Ordinal);
            Assert.Contains(second.Id, detail.Text, StringComparison.Ordinal);
            Activate(Required<Button>(view, "GoToProblemButton"));
            Assert.Same(second, design.SelectedQuestion);
            Assert.Equal("編集中", points.Text);
            Assert.True(DataValidationErrors.GetHasErrors(points));

            design.DeleteQuestion(second.Id);
            Render();
            Assert.DoesNotContain("未反映", status.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(second.Id, detail.Text, StringComparison.Ordinal);
            Assert.False(DataValidationErrors.GetHasErrors(points));
            Assert.Equal(1m, first.Points);
            Assert.Equal(2m, second.Points);
            Assert.True(Required<Button>(view, "EqualizeQuestionPointsButton").IsEffectivelyEnabled);

            // Reintroducing the deleted ID must not resurrect its old local text.
            design.SynchronizeFromInput(draft, design.AvailableColumnNames);
            Render();
            QuestionDesignItemViewModel restored = Assert.Single(design.Questions, question => question.Id == second.Id);
            list.SetCurrentValue(ListBox.SelectedItemProperty, restored);
            Render();
            Assert.Equal("2", points.Text);
            Assert.False(DataValidationErrors.GetHasErrors(points));
            Assert.DoesNotContain("未反映", status.Text, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Pending_question_points_do_not_leak_when_the_view_is_rebound()
    {
        QuantificationDesignViewModel original = new(CreateLargeDefinition());
        QuantificationDesignViewModel replacement = new(CreateLargeDefinition());
        QuantificationDefinition originalDraft = original.Draft;
        QuantificationDefinition replacementDraft = replacement.Draft;
        QuantificationDesignView view = new(original);
        Window window = new() { Width = 950, Height = 600, Content = view };
        try
        {
            window.Show();
            Render();
            TextBox points = Required<TextBox>(view, "SelectedQuestionPointsTextBox");
            points.SetCurrentValue(TextBox.TextProperty, "-");
            Render();
            Assert.True(DataValidationErrors.GetHasErrors(points));
            Assert.Equal(original.SelectedQuestion!.Id, replacement.SelectedQuestion!.Id);

            view.DataContext = replacement;
            Render();

            Assert.Same(points, Required<TextBox>(view, "SelectedQuestionPointsTextBox"));
            Assert.Same(replacement, view.ViewModel);
            Assert.Equal("1", points.Text);
            Assert.False(DataValidationErrors.GetHasErrors(points));
            Assert.DoesNotContain("未反映", Required<TextBlock>(view, "DesignStatusText").Text, StringComparison.Ordinal);
            Assert.True(Required<Button>(view, "EqualizeQuestionPointsButton").IsEffectivelyEnabled);
            Assert.Same(originalDraft, original.Draft);
            Assert.Same(replacementDraft, replacement.Draft);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertPromptError(
        QuantificationDesignViewModel viewModel,
        string evaluatorId,
        string expectedCode) =>
        Assert.Contains(
            viewModel.ValidationErrors,
            error => error.NodeId == evaluatorId
                && error.Field == "CustomPromptTemplate"
                && error.Code == expectedCode);

    private static QuantificationDefinition CreateLargeDefinition() => new()
    {
        Id = "definition-large",
        Name = "Large virtualized design",
        Revision = "1",
        SourceSheet = "Original",
        HeaderRow = 1,
        FirstDataRow = 2,
        LastDataRow = 531,
        BasePoints = 45m,
        RoundingDigits = 1,
        Questions =
        [
            .. Enumerable.Range(1, 10).Select(question => new QuestionDefinition
            {
                Id = $"question-{question}",
                DisplayName = $"Question {question}",
                QuestionText = $"Question text {question}",
                PrimarySourceColumn = "A",
                SupportingSourceColumns = [],
                Points = question,
                Enabled = true,
                Evaluators =
                [
                    .. Enumerable.Range(1, 5).Select(evaluator => new EvaluatorDefinition
                    {
                        Id = $"evaluator-{question}-{evaluator}",
                        DisplayName = $"Knowledge {question}-{evaluator}",
                        Type = EvaluatorType.KnowledgeCoverage,
                        Weight = evaluator,
                        Range = new ScoreRange(0m, 10m),
                        BuiltInTemplateVersion = BuiltInPromptTemplates.KnowledgeTemplateVersion,
                        CustomPromptTemplate = null,
                        Enabled = true,
                        Criteria =
                        [
                            .. Enumerable.Range(1, 20).Select(criterion => new CriterionDefinition
                            {
                                Id = $"criterion-{question}-{evaluator}-{criterion}",
                                DisplayName = $"Criterion {question}-{evaluator}-{criterion}",
                                Description = "説明・関係・適用を確認する知識ポイント",
                                Weight = criterion,
                                Range = null,
                                Enabled = true,
                            }),
                        ],
                    }),
                ],
            }),
        ],
    };

    private static T Required<T>(Control root, string name)
        where T : Control =>
        Assert.IsType<T>(root.FindControl<T>(name));

    private static T RequiredByAutomationId<T>(Control root, string automationId)
        where T : Control =>
        Assert.Single(
            root.GetVisualDescendants().OfType<T>(),
            control => AutomationProperties.GetAutomationId(control) == automationId);

    private static void AssertMeasuredPage(QuantificationDesignView view, QuantificationDesignViewModel design)
    {
        ListBox list = Required<ListBox>(view, "QuestionEditorList");
        ListBoxItem[] rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Assert.Equal(design.VisibleQuestions.Count, rows.Length);
        Assert.NotEmpty(rows);
        double rowHeight = rows[0].Bounds.Height;
        Assert.True(rowHeight >= 44d);
        Assert.Equal(Math.Max(1, (int)Math.Floor(list.Bounds.Height / rowHeight)), design.PageSize);
        Assert.All(rows, row =>
        {
            Assert.Equal(rowHeight, row.Bounds.Height);
            AssertFits(row, list);
        });
        Assert.Equal(design.PageSummary, Required<TextBlock>(view, "QuestionPageSummary").Text);
        Assert.Equal(design.VisibleQuestions.Count, view.GetVisualDescendants().OfType<Border>()
            .Count(border => (AutomationProperties.GetAutomationId(border) ?? string.Empty).StartsWith("DesignQuestion-", StringComparison.Ordinal)));
    }

    private static void AssertNormalBodyFits(QuantificationDesignView view)
    {
        ScrollViewer scroll = Required<ScrollViewer>(view, "QuantificationDesignScrollViewer");
        Assert.True(double.IsFinite(scroll.Extent.Height) && double.IsFinite(scroll.Extent.Width));
        Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1d);
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1d);
        Assert.Equal(default, scroll.Offset);
    }

    private static void AssertAccessibleInputsFit(Control view)
    {
        TemplatedControl[] targets = view.GetVisualDescendants().OfType<TemplatedControl>()
            .Where(control => control.IsEffectivelyVisible && control is Button or TextBox or CheckBox or ComboBox
                && !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(control))).ToArray();
        Assert.NotEmpty(targets);
        foreach (TemplatedControl control in targets)
        {
            Assert.True(control.MinHeight >= 44d);
            Assert.True(control.Bounds.Height >= 44d);
            Assert.True(control.Bounds.Width >= 44d);
            Assert.Equal(14d, control.FontSize);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(control)?.ToString()));
            AssertFits(control, view);
        }

        string[] ids = view.GetVisualDescendants().OfType<Control>().Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static void AssertFits(Control control, Control container)
    {
        Point origin = control.TranslatePoint(default, container)!.Value;
        Assert.True(origin.X >= -1d && origin.Y >= -1d);
        Assert.True(origin.X + control.Bounds.Width <= container.Bounds.Width + 1d);
        Assert.True(origin.Y + control.Bounds.Height <= container.Bounds.Height + 1d);
    }

    private static void Activate(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        Assert.True(button.Focus(NavigationMethod.Tab));
        Press(Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(button)), Key.Enter);
    }

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(TopLevel window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physicalKey = key switch
        {
            Key.Tab => PhysicalKey.Tab,
            Key.Enter => PhysicalKey.Enter,
            Key.End => PhysicalKey.End,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported test key."),
        };
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
        Render();
    }
}