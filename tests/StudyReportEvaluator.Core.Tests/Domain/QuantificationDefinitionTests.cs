using System.Collections.Immutable;
using System.Text.Json;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Domain;

// Requirements: FR-008 (AC-009), FR-009 (AC-010)
public sealed class QuantificationDefinitionTests
{
    [Fact]
    public void Definition_has_v4_allocation_defaults()
    {
        QuantificationDefinition definition = TestDefinitions.Create();

        Assert.Equal(60m, definition.BasePoints);
        Assert.Equal(0m, definition.SpecialPoints);
        Assert.Equal(0.1m, definition.SimilarityPenaltyWeight);
        Assert.Equal(40m, definition.Questions[0].Points);
    }

    [Fact]
    public void Definition_preserves_order_arbitrary_columns_and_disabled_nodes()
    {
        QuantificationDefinition definition = TestDefinitions.Create();

        Assert.Equal(["Q1", "Q2"], definition.Questions.Select(question => question.Id));
        Assert.Equal("G", definition.Questions[0].PrimarySourceColumn);
        Assert.Equal(["K", "L"], definition.Questions[0].SupportingSourceColumns);
        Assert.False(definition.Questions[1].Enabled);
        Assert.Equal([EvaluatorType.KnowledgeCoverage, EvaluatorType.CustomPrompt], definition.Questions[0].Evaluators.Select(evaluator => evaluator.Type));
    }

    [Fact]
    public void Evaluator_type_serializes_to_the_normative_names()
    {
        JsonSerializerOptions options = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

        Assert.Equal("\"KNOWLEDGE_COVERAGE\"", JsonSerializer.Serialize(EvaluatorType.KnowledgeCoverage, options));
        Assert.Equal("\"CUSTOM_PROMPT\"", JsonSerializer.Serialize(EvaluatorType.CustomPrompt, options));
    }

    [Fact]
    public void Duplicate_question_regenerates_every_hierarchical_id_without_mutating_source()
    {
        QuantificationDefinition source = TestDefinitions.Create() with
        {
            Questions =
            [
                TestDefinitions.Create().Questions[0] with
                {
                    SpecialEvaluations =
                    [
                        new SpecialEvaluationDefinition
                        {
                            Id = "S1",
                            DisplayName = "Prompt quality",
                            PrimarySourceColumn = "G",
                            SupportingSourceColumns = ["K"],
                            PromptTemplate = "{回答}",
                        },
                    ],
                },
            ],
        };

        QuantificationDefinition duplicate = source.DuplicateQuestion(
            0,
            "Q-COPY",
            evaluator => $"{evaluator.Id}-COPY",
            criterion => $"{criterion.Id}-COPY",
            special => $"{special.Id}-COPY");

        Assert.Single(source.Questions);
        Assert.Equal(2, duplicate.Questions.Length);
        Assert.Equal("Q-COPY", duplicate.Questions[1].Id);
        Assert.Equal(["E1-COPY", "E2-COPY"], duplicate.Questions[1].Evaluators.Select(evaluator => evaluator.Id));
        Assert.All(duplicate.Questions[1].Evaluators.SelectMany(evaluator => evaluator.Criteria), criterion => Assert.EndsWith("-COPY", criterion.Id, StringComparison.Ordinal));
        Assert.Equal("S1-COPY", duplicate.Questions[1].SpecialEvaluations.Single().Id);
        Assert.Equal(["K"], duplicate.Questions[1].SpecialEvaluations.Single().SupportingSourceColumns);
        Assert.Equal("S1", source.Questions[0].SpecialEvaluations.Single().Id);
    }
}

internal static class TestDefinitions
{
    internal static QuantificationDefinition Create()
    {
        CriterionDefinition knowledge = new()
        {
            Id = "C1",
            DisplayName = "Knowledge point",
            Description = "Explain the concept",
            Weight = 2m,
            Range = new ScoreRange(0m, 30m),
        };
        CriterionDefinition custom = new()
        {
            Id = "C2",
            DisplayName = "Specificity",
            Description = "Assess specificity",
            Weight = 1m,
        };
        EvaluatorDefinition knowledgeEvaluator = new()
        {
            Id = "E1",
            DisplayName = "Knowledge",
            Type = EvaluatorType.KnowledgeCoverage,
            Weight = 2m,
            Range = new ScoreRange(0m, 10m),
            BuiltInTemplateVersion = "knowledge-v1",
            Criteria = [knowledge],
        };
        EvaluatorDefinition customEvaluator = new()
        {
            Id = "E2",
            DisplayName = "Prompt quality",
            Type = EvaluatorType.CustomPrompt,
            Weight = 1m,
            Range = new ScoreRange(1m, 10m),
            CustomPromptTemplate = "{回答} {評価項目}",
            Criteria = [custom],
        };
        QuestionDefinition first = new()
        {
            Id = "Q1",
            DisplayName = "Prompt question",
            QuestionText = "Create a useful prompt",
            PrimarySourceColumn = "G",
            SupportingSourceColumns = ["K", "L"],
            Points = 40m,
            Evaluators = [knowledgeEvaluator, customEvaluator],
        };
        QuestionDefinition second = first with
        {
            Id = "Q2",
            PrimarySourceColumn = "F",
            SupportingSourceColumns = [],
            Enabled = false,
        };
        return new QuantificationDefinition
        {
            Id = "DEF-1",
            Name = "Definition",
            Revision = "1",
            SourceSheet = "Original",
            HeaderRow = 1,
            FirstDataRow = 2,
            LastDataRow = 531,
            RoundingDigits = 1,
            Questions = [first, second],
        };
    }
}
