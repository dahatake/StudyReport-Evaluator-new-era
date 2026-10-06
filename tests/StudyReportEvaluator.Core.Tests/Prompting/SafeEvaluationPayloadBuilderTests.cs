using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Tests.Validation;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Prompting;

// Requirements: NFR-SEC-001 (AC-066)
public sealed class SafeEvaluationPayloadBuilderTests
{
    private readonly SafeEvaluationPayloadBuilder _builder = new();

    [Fact]
    public void Payload_contains_only_selected_same_row_sources_and_stable_column_ids()
    {
        const string otherColumnCanary = "OTHER-COLUMN-MUST-NOT-APPEAR";
        const string otherRowCanary = "OTHER-ROW-MUST-NOT-APPEAR";
        const string pathCanary = "C:\\private\\input.xlsx";
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(C02TestDefinitions.CreateValid());
        Dictionary<string, string?> selectedRow = new(StringComparer.OrdinalIgnoreCase)
        {
            ["G"] = "selected primary",
            ["K"] = "selected support K",
            ["L"] = "selected support L",
            ["A"] = otherColumnCanary,
            ["FILE_PATH"] = pathCanary,
        };
        Dictionary<string, string?> otherRow = new() { ["G"] = otherRowCanary };

        SafeEvaluationPayload payload = _builder.Build(snapshot, "Q1", "E2", selectedRow);

        Assert.Equal("G", payload.PrimarySource.SourceColumnId);
        Assert.Equal(["K", "L"], payload.SupportingSources.Select(source => source.SourceColumnId));
        Assert.Equal(["selected support K", "selected support L"], payload.SupportingSources.Select(source => source.Value));
        Assert.Equal([EvaluationSourceKind.PrimaryAnswer, EvaluationSourceKind.SupportingColumn, EvaluationSourceKind.SupportingColumn], payload.Sources.Select(source => source.Kind));
        Assert.Contains("selected primary", payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(otherColumnCanary, payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(otherRow["G"]!, payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(pathCanary, payload.RenderedPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluator_defaults_feed_min_max_while_criteria_include_effective_ranges()
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        EvaluatorDefinition custom = source.Questions[0].Evaluators[1] with
        {
            Range = new ScoreRange(1m, 10m),
            Criteria =
            [
                source.Questions[0].Evaluators[1].Criteria[0] with
                {
                    Range = new ScoreRange(20m, 30m),
                },
            ],
            CustomPromptTemplate = "defaults={最小点}..{最大点}; answer={回答}; criteria={評価項目}",
        };
        QuantificationDefinition definition = source with
        {
            Questions =
            [
                source.Questions[0] with { Evaluators = [source.Questions[0].Evaluators[0], custom] },
                source.Questions[1],
            ],
        };
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);

        SafeEvaluationPayload payload = _builder.Build(snapshot, "Q1", "E2", new Dictionary<string, string?>
        {
            ["G"] = "answer",
            ["K"] = "",
            ["L"] = "support",
        });

        Assert.Contains("defaults=1..10", payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.Contains("EffectiveRange: 20..30", payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.Equal(new ScoreRange(20m, 30m), Assert.Single(payload.ExpectedCriteria).Range);
    }

    [Fact]
    public void Valid_knowledge_dispatch_uses_app_owned_semantic_and_structured_instructions()
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();

        SafeEvaluationPayload payload = _builder.Build(
            QuantificationSnapshot.Create(source),
            "Q1",
            "E1",
            new Dictionary<string, string?> { ["G"] = "answer", ["K"] = "", ["L"] = "" });

        Assert.Contains("説明", payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.Contains("関係", payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.Contains("適用", payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.Contains("APP-OWNED STRUCTURED OUTPUT CONTRACT", payload.RenderedPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_primary_never_produces_a_dispatch_payload()
    {
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(C02TestDefinitions.CreateValid());

        PromptConfigurationException exception = Assert.Throws<PromptConfigurationException>(
            () => _builder.Build(snapshot, "Q1", "E1", new Dictionary<string, string?> { ["G"] = "  " }));

        Assert.Equal("EMPTY_PRIMARY", exception.Code);
    }

    [Fact]
    public void Content_bearing_objects_redact_their_string_representation()
    {
        const string canary = "CONTENT-CANARY";
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(C02TestDefinitions.CreateValid());
        SafeEvaluationPayload payload = _builder.Build(
            snapshot,
            "Q1",
            "E2",
            new Dictionary<string, string?> { ["G"] = canary, ["K"] = canary, ["L"] = canary });

        Assert.DoesNotContain(canary, payload.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(canary, payload.PrimarySource.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(canary, new PromptRenderContext(canary, canary, canary, canary, "0", "1").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_criteria_are_excluded_from_prompt_and_expected_schema()
    {
        const string disabledCanary = "DISABLED-CRITERION-MUST-NOT-APPEAR";
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        EvaluatorDefinition evaluator = source.Questions[0].Evaluators[1];
        CriterionDefinition disabled = evaluator.Criteria[0] with
        {
            Id = "C-DISABLED",
            DisplayName = disabledCanary,
            Description = disabledCanary,
            Enabled = false,
        };
        evaluator = evaluator with
        {
            Criteria = [evaluator.Criteria[0], disabled],
        };
        QuantificationDefinition definition = source with
        {
            Questions =
            [
                source.Questions[0] with { Evaluators = [source.Questions[0].Evaluators[0], evaluator] },
                source.Questions[1],
            ],
        };

        SafeEvaluationPayload payload = _builder.Build(
            QuantificationSnapshot.Create(definition),
            "Q1",
            "E2",
            new Dictionary<string, string?> { ["G"] = "answer", ["K"] = "", ["L"] = "" });

        Assert.Single(payload.ExpectedCriteria);
        Assert.DoesNotContain(disabledCanary, payload.RenderedPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Reference_payload_contains_only_the_question_and_closed_reference_contract()
    {
        const string questionCanary = "QUESTION {回答} CANARY";
        const string answerCanary = "STUDENT-ANSWER-MUST-NOT-APPEAR";
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(source with
        {
            Questions =
            [
                source.Questions[0] with { QuestionText = questionCanary },
                source.Questions[1],
            ],
        });

        SafeReferenceAnswerPayload payload = _builder.BuildReferenceAnswer(snapshot, "Q1");

        Assert.Contains(questionCanary, payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.Contains("submit_reference_answer", payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(answerCanary, payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.Equal(1, Count(payload.RenderedPrompt, questionCanary));
        Assert.DoesNotContain(questionCanary, payload.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Special_payload_uses_only_its_selected_same_row_sources_and_closed_contract()
    {
        const string otherColumnCanary = "OTHER-COLUMN-MUST-NOT-APPEAR";
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        SpecialEvaluationDefinition special = new()
        {
            Id = "S1",
            DisplayName = "Prompt quality",
            PrimarySourceColumn = "G",
            SupportingSourceColumns = ["K"],
            PromptTemplate = "Question={設問}; Prompt={回答}; Note={補助情報}",
        };
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(source with
        {
            SpecialPoints = 10m,
            Questions =
            [
                source.Questions[0] with { Points = 30m, SpecialEvaluations = [special] },
                source.Questions[1],
            ],
        });

        SafeSpecialEvaluationPayload payload = _builder.BuildSpecialEvaluation(
            snapshot,
            "Q1",
            "S1",
            new Dictionary<string, string?>
            {
                ["G"] = "student prompt",
                ["K"] = "student consideration",
                ["A"] = otherColumnCanary,
            });

        Assert.Equal("G", payload.PrimarySource.SourceColumnId);
        Assert.Equal(["K"], payload.SupportingSources.Select(item => item.SourceColumnId));
        Assert.Contains("student prompt", payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.Contains("student consideration", payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.Contains("submit_special_quantification", payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(otherColumnCanary, payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("student prompt", payload.ToString(), StringComparison.Ordinal);
    }

    private static int Count(string value, string fragment)
    {
        int count = 0;
        int start = 0;
        while ((start = value.IndexOf(fragment, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += fragment.Length;
        }

        return count;
    }
}
