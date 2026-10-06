using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Domain;

public sealed class DefinitionCollectionTests
{
    [Fact]
    public void Questions_can_be_added_moved_disabled_and_deleted_immutably()
    {
        QuantificationDefinition original = TestDefinitions.Create();
        QuestionDefinition added = original.Questions[0] with { Id = "Q3" };

        QuantificationDefinition changed = original
            .AddQuestion(added)
            .MoveQuestion(2, 0)
            .SetQuestionEnabled(1, false)
            .RemoveQuestion(2);

        Assert.Equal(["Q1", "Q2"], original.Questions.Select(question => question.Id));
        Assert.Equal(["Q3", "Q1"], changed.Questions.Select(question => question.Id));
        Assert.False(changed.Questions[1].Enabled);
    }

    [Fact]
    public void Evaluators_can_be_added_duplicated_moved_disabled_and_deleted_immutably()
    {
        QuestionDefinition original = TestDefinitions.Create().Questions[0];
        EvaluatorDefinition added = original.Evaluators[0] with { Id = "E3" };

        QuestionDefinition changed = original
            .AddEvaluator(added)
            .DuplicateEvaluator(0, "E1-COPY", criterion => $"{criterion.Id}-DUP")
            .MoveEvaluator(3, 0)
            .SetEvaluatorEnabled(1, false)
            .RemoveEvaluator(2);

        Assert.Equal(["E1", "E2"], original.Evaluators.Select(evaluator => evaluator.Id));
        Assert.Equal(["E3", "E1", "E2"], changed.Evaluators.Select(evaluator => evaluator.Id));
        Assert.False(changed.Evaluators[1].Enabled);
    }

    [Fact]
    public void Criteria_can_be_added_duplicated_moved_disabled_and_deleted_immutably()
    {
        EvaluatorDefinition original = TestDefinitions.Create().Questions[0].Evaluators[0];
        CriterionDefinition added = original.Criteria[0] with { Id = "C2" };

        EvaluatorDefinition changed = original
            .AddCriterion(added)
            .DuplicateCriterion(0, "C1-COPY")
            .MoveCriterion(2, 0)
            .SetCriterionEnabled(1, false)
            .RemoveCriterion(2);

        Assert.Equal(["C1"], original.Criteria.Select(criterion => criterion.Id));
        Assert.Equal(["C2", "C1"], changed.Criteria.Select(criterion => criterion.Id));
        Assert.False(changed.Criteria[1].Enabled);
    }

    [Fact]
    public void Special_evaluations_can_be_added_duplicated_moved_disabled_and_deleted_immutably()
    {
        QuestionDefinition original = TestDefinitions.Create().Questions[0];
        SpecialEvaluationDefinition first = new()
        {
            Id = "S1",
            DisplayName = "Student prompt",
            PrimarySourceColumn = "G",
            SupportingSourceColumns = ["K"],
            PromptTemplate = "{回答}",
        };
        SpecialEvaluationDefinition second = first with { Id = "S2" };

        QuestionDefinition changed = original
            .AddSpecialEvaluation(first)
            .AddSpecialEvaluation(second)
            .DuplicateSpecialEvaluation(0, "S1-COPY")
            .MoveSpecialEvaluation(2, 0)
            .SetSpecialEvaluationEnabled(1, false)
            .RemoveSpecialEvaluation(2);

        Assert.Empty(original.SpecialEvaluations);
        Assert.Equal(["S2", "S1"], changed.SpecialEvaluations.Select(item => item.Id));
        Assert.False(changed.SpecialEvaluations[1].Enabled);
        Assert.Equal(["K"], changed.SpecialEvaluations[1].SupportingSourceColumns);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Collection_operations_reject_invalid_indexes(int index)
    {
        QuantificationDefinition definition = TestDefinitions.Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => definition.MoveQuestion(index, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => definition.SetQuestionEnabled(index, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => definition.RemoveQuestion(index));
    }
}
