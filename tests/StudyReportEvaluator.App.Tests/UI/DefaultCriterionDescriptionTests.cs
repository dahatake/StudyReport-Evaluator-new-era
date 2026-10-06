using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-014 (AC-015)
public sealed class DefaultCriterionDescriptionTests
{
    private const string ExpectedKnowledge =
        "学生が作成したレポートを高度に分析・解析をしてそれぞれの{論点}について説明がされているかどうかの評価を行ってください。\n論点:\n- xxx\n- xxx\n- xxx";

    private const string ExpectedPromptAnalysis =
        "学生が作成したPromptについて高度に分析・解析をして{論点}を導き出そうとしているかの評価を行ってください。\n論点:\n- xxx\n- xxx\n- xxx";

    [Fact]
    public void Description_text_matches_the_requested_wording_for_each_evaluator_type()
    {
        Assert.Equal(ExpectedKnowledge, DefaultCriterionDescriptions.For(EvaluatorType.KnowledgeCoverage));
        Assert.Equal(ExpectedPromptAnalysis, DefaultCriterionDescriptions.For(EvaluatorType.CustomPrompt));
        Assert.Throws<ArgumentOutOfRangeException>(() => DefaultCriterionDescriptions.For((EvaluatorType)99));
    }

    [Fact]
    public void New_design_has_knowledge_description_and_added_criteria_follow_their_evaluator_type()
    {
        QuantificationDesignViewModel viewModel = new();
        QuestionDesignItemViewModel question = Assert.Single(viewModel.Questions);
        EvaluatorDesignItemViewModel knowledge = Assert.Single(question.Evaluators);
        Assert.Equal(ExpectedKnowledge, Assert.Single(knowledge.Criteria).Description);

        Assert.Equal(
            ExpectedKnowledge,
            viewModel.AddCriterion(question.Id, knowledge.Id).Description);

        EvaluatorDesignItemViewModel custom = viewModel.AddEvaluator(question.Id, EvaluatorType.CustomPrompt);
        Assert.Equal(ExpectedPromptAnalysis, Assert.Single(custom.Criteria).Description);
        Assert.Equal(
            ExpectedPromptAnalysis,
            viewModel.AddCriterion(question.Id, custom.Id).Description);

        EvaluatorDesignItemViewModel secondKnowledge = viewModel.AddEvaluator(question.Id, EvaluatorType.KnowledgeCoverage);
        Assert.Equal(ExpectedKnowledge, Assert.Single(secondKnowledge.Criteria).Description);
    }

    [Fact]
    public void Changing_evaluator_type_keeps_existing_descriptions_and_applies_new_type_to_later_criteria()
    {
        QuantificationDesignViewModel viewModel = new();
        QuestionDesignItemViewModel question = Assert.Single(viewModel.Questions);
        EvaluatorDesignItemViewModel evaluator = Assert.Single(question.Evaluators);
        Assert.Equal(ExpectedKnowledge, Assert.Single(evaluator.Criteria).Description);

        viewModel.ChangeEvaluatorType(question.Id, evaluator.Id, EvaluatorType.CustomPrompt);

        Assert.Equal(ExpectedKnowledge, evaluator.Criteria[0].Description);
        Assert.Equal(ExpectedPromptAnalysis, viewModel.AddCriterion(question.Id, evaluator.Id).Description);
    }

    [Fact]
    public void Edited_description_is_not_overwritten_when_another_criterion_is_added()
    {
        QuantificationDesignViewModel viewModel = new();
        QuestionDesignItemViewModel question = Assert.Single(viewModel.Questions);
        EvaluatorDesignItemViewModel evaluator = Assert.Single(question.Evaluators);
        evaluator.Criteria[0].Description = "利用者が編集した説明";

        viewModel.AddCriterion(question.Id, evaluator.Id);

        Assert.Equal("利用者が編集した説明", evaluator.Criteria[0].Description);
    }

    [Fact]
    public async Task Suggested_questions_use_prompt_analysis_text_for_student_prompt_columns_and_knowledge_text_otherwise()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel viewModel = new();

        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);

        Assert.All(viewModel.DefinitionDraft.Questions, question =>
        {
            EvaluatorDefinition evaluator = Assert.Single(question.Evaluators);
            string expected = evaluator.Type == EvaluatorType.CustomPrompt
                ? ExpectedPromptAnalysis
                : ExpectedKnowledge;
            Assert.Equal(expected, Assert.Single(evaluator.Criteria).Description);
        });
        Assert.Contains(
            viewModel.DefinitionDraft.Questions,
            question => question.Evaluators[0].Type == EvaluatorType.CustomPrompt);
        Assert.Contains(
            viewModel.DefinitionDraft.Questions,
            question => question.Evaluators[0].Type == EvaluatorType.KnowledgeCoverage);
        Assert.Empty(viewModel.ValidationErrors);
    }
}
