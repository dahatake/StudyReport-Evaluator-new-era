using System.Text.Json;
using GitHub.Copilot;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

// Requirements: FR-019 (AC-020)
public sealed class SubmitQuantificationToolTests
{
    [Fact]
    public async Task One_complete_valid_invocation_is_accepted_as_one_whole_result()
    {
        SubmitQuantificationTool tool = new(CreatePayload());

        string acknowledgement = await tool.InvokeAsync(
            Invocation(ValidJson()),
            TestContext.Current.CancellationToken);

        Assert.Equal(SubmitQuantificationTool.AcceptedAcknowledgement, acknowledgement);
        Assert.True(tool.TryGetAcceptedResult(out QuantificationResult? accepted));
        Assert.Equal("E1", accepted.EvaluatorId);
        Assert.Equal(["C1", "C2"], accepted.Criteria.Select(criterion => criterion.CriterionId));
        Assert.Equal([4m, 1.5m], accepted.Criteria.Select(criterion => criterion.RawScore));
        SubmitQuantificationOutcome outcome = tool.GetOutcome();
        Assert.True(outcome.IsAccepted);
        Assert.Equal(SubmitQuantificationStatus.Accepted, outcome.Status);
        Assert.Equal(1, outcome.InvocationCount);
        Assert.Empty(outcome.ErrorCodes);
    }

    [Theory]
    [MemberData(nameof(InvalidCriterionPayloads))]
    public async Task Missing_duplicate_unknown_and_partial_criteria_reject_the_whole_payload(
        string criteriaJson,
        string expectedCode)
    {
        SubmitQuantificationTool tool = new(CreatePayload());
        string json = $$"""
            {
              "EvaluatorId": "E1",
              "Criteria": {{criteriaJson}}
            }
            """;

        string acknowledgement = await tool.InvokeAsync(
            Invocation(json),
            TestContext.Current.CancellationToken);

        Assert.Equal(SubmitQuantificationTool.RejectedAcknowledgement, acknowledgement);
        Assert.False(tool.TryGetAcceptedResult(out _));
        SubmitQuantificationOutcome outcome = tool.GetOutcome();
        Assert.Equal(SubmitQuantificationStatus.InvalidPayload, outcome.Status);
        Assert.Contains(expectedCode, outcome.ErrorCodes);
        Assert.Null(outcome.AcceptedResult);
    }

    public static TheoryData<string, string> InvalidCriterionPayloads => new()
    {
        { "[]", "MISSING_CRITERION" },
        { $"[{CriterionJson("C1", 4m)}, {CriterionJson("C1", 4m)}]", "DUPLICATE_CRITERION" },
        { $"[{CriterionJson("C1", 4m)}, {CriterionJson("UNKNOWN", 1.5m)}]", "UNKNOWN_CRITERION" },
        { $"[{CriterionJson("C1", 4m)}]", "MISSING_CRITERION" },
    };

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(10.0001)]
    public async Task Criterion_specific_range_is_enforced_without_clamp_or_partial_adoption(double score)
    {
        SubmitQuantificationTool tool = new(CreatePayload());
        string json = $$"""
            {
              "EvaluatorId": "E1",
              "Criteria": [
                {{CriterionJson("C1", (decimal)score)}},
                {{CriterionJson("C2", 1.5m)}}
              ]
            }
            """;

        await tool.InvokeAsync(Invocation(json), TestContext.Current.CancellationToken);

        SubmitQuantificationOutcome outcome = tool.GetOutcome();
        Assert.Equal(SubmitQuantificationStatus.InvalidPayload, outcome.Status);
        Assert.Contains("RAW_SCORE_OUT_OF_RANGE", outcome.ErrorCodes);
        Assert.Null(outcome.AcceptedResult);
    }

    [Fact]
    public async Task Evidence_must_bind_to_the_exact_sent_same_row_source()
    {
        SubmitQuantificationTool tool = new(CreatePayload());
        string json = $$"""
            {
              "EvaluatorId": "E1",
              "Criteria": [
                {{CriterionJson("C1", 4m, evidence: "different-row evidence")}},
                {{CriterionJson("C2", 1.5m)}}
              ]
            }
            """;

        await tool.InvokeAsync(Invocation(json), TestContext.Current.CancellationToken);

        SubmitQuantificationOutcome outcome = tool.GetOutcome();
        Assert.Contains("EVIDENCE_NOT_EXACT_SUBSTRING", outcome.ErrorCodes);
        Assert.Null(outcome.AcceptedResult);
    }

    [Fact]
    public async Task Valid_supporting_and_none_evidence_contracts_are_accepted()
    {
        SubmitQuantificationTool tool = new(CreatePayload());
        string json = $$"""
            {
              "EvaluatorId": "E1",
              "Criteria": [
                {{CriterionJson(
                    "C1",
                    4m,
                    source: "SUPPORTING_COLUMN",
                    sourceColumn: "K",
                    evidence: "support K")}},
                {{CriterionJson(
                    "C2",
                    1.5m,
                    source: "NONE",
                    sourceColumn: "",
                    evidence: "")}}
              ]
            }
            """;

        string acknowledgement = await tool.InvokeAsync(
            Invocation(json),
            TestContext.Current.CancellationToken);

        Assert.Equal(SubmitQuantificationTool.AcceptedAcknowledgement, acknowledgement);
        Assert.True(tool.TryGetAcceptedResult(out _));
    }

    [Fact]
    public async Task Omitted_evaluator_id_is_filled_from_the_payload_but_a_wrong_one_is_rejected()
    {
        string criteria = $"[{CriterionJson("C1", 4m)}, {CriterionJson("C2", 1.5m)}]";
        SubmitQuantificationTool omitted = new(CreatePayload());
        SubmitQuantificationTool wrong = new(CreatePayload());

        await omitted.InvokeAsync(
            Invocation($$"""{ "Criteria": {{criteria}} }"""),
            TestContext.Current.CancellationToken);
        await wrong.InvokeAsync(
            Invocation($$"""{ "EvaluatorId": "E2", "Criteria": {{criteria}} }"""),
            TestContext.Current.CancellationToken);

        Assert.True(omitted.TryGetAcceptedResult(out QuantificationResult? accepted));
        Assert.Equal("E1", accepted.EvaluatorId);
        Assert.False(wrong.TryGetAcceptedResult(out _));
        Assert.Contains("EVALUATOR_ID_MISMATCH", wrong.GetOutcome().ErrorCodes);
    }

    [Theory]
    [MemberData(nameof(ClosedWirePayloads))]
    public async Task Unknown_missing_duplicate_and_wrong_type_fields_are_rejected_by_the_wire_parser(
        string json,
        string expectedCode)
    {
        SubmitQuantificationTool tool = new(CreatePayload());

        await tool.InvokeAsync(Invocation(json), TestContext.Current.CancellationToken);

        SubmitQuantificationOutcome outcome = tool.GetOutcome();
        Assert.Equal(SubmitQuantificationStatus.InvalidPayload, outcome.Status);
        Assert.Contains(expectedCode, outcome.ErrorCodes);
        Assert.Null(outcome.AcceptedResult);
    }

    public static TheoryData<string, string> ClosedWirePayloads => new()
    {
        {
            $$"""
            {
              "EvaluatorId": "E1",
              "Criteria": [{{CriterionJson("C1", 4m)}}, {{CriterionJson("C2", 1.5m)}}],
              "OverallScore": 100
            }
            """,
            "ROOT_UNKNOWN_PROPERTY"
        },
        {
            $$"""
            {
              "EvaluatorId": "E1",
              "EvaluatorId": "E1",
              "Criteria": [{{CriterionJson("C1", 4m)}}, {{CriterionJson("C2", 1.5m)}}]
            }
            """,
            "ROOT_DUPLICATE_PROPERTY"
        },
        {
            $$"""
            {
              "EvaluatorId": "E1",
              "Criteria": [
                {
                  "CriterionId": "C1",
                  "RawScore": 4,
                  "Reason": "reason",
                  "Evidence": "primary evidence",
                  "EvidenceSource": "PRIMARY_ANSWER",
                  "EvidenceSourceColumnId": "G",
                  "Weight": 1
                },
                {{CriterionJson("C2", 1.5m)}}
              ]
            }
            """,
            "CRITERION_UNKNOWN_PROPERTY"
        },
        {
            $$"""
            {
              "EvaluatorId": "E1",
              "Criteria": [
                {
                  "CriterionId": "C1",
                  "RawScore": 4,
                  "Reason": "reason",
                  "Evidence": "primary evidence",
                  "EvidenceSource": "PRIMARY_ANSWER"
                },
                {{CriterionJson("C2", 1.5m)}}
              ]
            }
            """,
            "CRITERION_MISSING_PROPERTY"
        },
        {
            $$"""
            {
              "EvaluatorId": "E1",
              "Criteria": [
                {
                  "CriterionId": "C1",
                  "RawScore": "4",
                  "Reason": "reason",
                  "Evidence": "primary evidence",
                  "EvidenceSource": "PRIMARY_ANSWER",
                  "EvidenceSourceColumnId": "G"
                },
                {{CriterionJson("C2", 1.5m)}}
              ]
            }
            """,
            "RAW_SCORE_INVALID"
        },
    };

    [Fact]
    public void Normal_assistant_text_without_a_tool_invocation_is_not_a_result()
    {
        const string ignoredAssistantBody = "Here is a normal assistant answer with score 10.";
        SubmitQuantificationTool tool = new(CreatePayload());

        SubmitQuantificationOutcome outcome = tool.GetOutcome();

        Assert.NotEmpty(ignoredAssistantBody);
        Assert.Equal(SubmitQuantificationStatus.NotInvoked, outcome.Status);
        Assert.Equal(0, outcome.InvocationCount);
        Assert.False(outcome.IsAccepted);
        Assert.False(tool.TryGetAcceptedResult(out _));
    }

    [Fact]
    public async Task A_duplicate_call_invalidates_an_earlier_valid_submission()
    {
        SubmitQuantificationTool tool = new(CreatePayload());
        ToolInvocation invocation = Invocation(ValidJson());

        string first = await tool.InvokeAsync(invocation, TestContext.Current.CancellationToken);
        string second = await tool.InvokeAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Equal(SubmitQuantificationTool.AcceptedAcknowledgement, first);
        Assert.Equal(SubmitQuantificationTool.RejectedAcknowledgement, second);
        SubmitQuantificationOutcome outcome = tool.GetOutcome();
        Assert.Equal(SubmitQuantificationStatus.DuplicateInvocation, outcome.Status);
        Assert.Equal(2, outcome.InvocationCount);
        Assert.Equal(["TOOL_INVOCATION_COUNT_INVALID"], outcome.ErrorCodes);
        Assert.Null(outcome.AcceptedResult);
        Assert.False(tool.TryGetAcceptedResult(out _));
    }

    [Fact]
    public async Task Concurrent_calls_are_counted_atomically_and_cannot_leave_a_result_adopted()
    {
        SubmitQuantificationTool tool = new(CreatePayload());

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task<string>[] calls = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(
                async () => await tool.InvokeAsync(Invocation(ValidJson()), cancellationToken),
                cancellationToken))
            .ToArray();
        await Task.WhenAll(calls);

        SubmitQuantificationOutcome outcome = tool.GetOutcome();
        Assert.Equal(SubmitQuantificationStatus.DuplicateInvocation, outcome.Status);
        Assert.Equal(32, outcome.InvocationCount);
        Assert.Null(outcome.AcceptedResult);
        Assert.False(tool.TryGetAcceptedResult(out _));
    }

    [Fact]
    public async Task Rejections_and_string_representations_do_not_disclose_reason_or_evidence()
    {
        const string canary = "PRIVATE-REASON-EVIDENCE-CANARY";
        SubmitQuantificationTool tool = new(CreatePayload());
        string json = $$"""
            {
              "EvaluatorId": "E1",
              "Criteria": [
                {{CriterionJson("C1", 4m, reason: canary, evidence: canary)}},
                {{CriterionJson("C2", 1.5m)}}
              ]
            }
            """;

        string acknowledgement = await tool.InvokeAsync(
            Invocation(json),
            TestContext.Current.CancellationToken);
        SubmitQuantificationOutcome outcome = tool.GetOutcome();
        string rendered = string.Join(
            "|",
            [acknowledgement, tool.ToString(), outcome.ToString(), .. outcome.ErrorCodes]);

        Assert.DoesNotContain(canary, rendered, StringComparison.Ordinal);
        Assert.Equal(SubmitQuantificationTool.RejectedAcknowledgement, acknowledgement);
        Assert.Null(outcome.AcceptedResult);
    }

    [Fact]
    public async Task Invocation_must_use_the_registered_tool_name()
    {
        SubmitQuantificationTool tool = new(CreatePayload());
        ToolInvocation invocation = Invocation(ValidJson());
        invocation.ToolName = "other_tool";

        await tool.InvokeAsync(invocation, TestContext.Current.CancellationToken);

        Assert.Contains("UNEXPECTED_TOOL_NAME", tool.GetOutcome().ErrorCodes);
        Assert.False(tool.TryGetAcceptedResult(out _));
    }

    private static ToolInvocation Invocation(string json) => new()
    {
        SessionId = "synthetic-session",
        ToolCallId = Guid.NewGuid().ToString("N"),
        ToolName = EvaluationSchemaFactory.ToolName,
        Arguments = JsonDocument.Parse(json).RootElement.Clone(),
    };

    private static string ValidJson() => $$"""
        {
          "EvaluatorId": "E1",
          "Criteria": [
            {{CriterionJson("C1", 4m)}},
            {{CriterionJson("C2", 1.5m)}}
          ]
        }
        """;

    private static string CriterionJson(
        string id,
        decimal score,
        string reason = "short reason",
        string source = "PRIMARY_ANSWER",
        string sourceColumn = "G",
        string evidence = "primary evidence") =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["CriterionId"] = id,
            ["RawScore"] = score,
            ["Reason"] = reason,
            ["Evidence"] = evidence,
            ["EvidenceSource"] = source,
            ["EvidenceSourceColumnId"] = sourceColumn,
        });

    private static SafeEvaluationPayload CreatePayload() => new(
        "Q1",
        "E1",
        "<private-prompt>",
        new EvaluationSourceCell(
            EvaluationSourceKind.PrimaryAnswer,
            "G",
            "primary evidence is here"),
        [
            new EvaluationSourceCell(
                EvaluationSourceKind.SupportingColumn,
                "K",
                "support K is here"),
        ],
        [
            new ExpectedCriterion("C1", "Criterion 1", new ScoreRange(0m, 10m)),
            new ExpectedCriterion("C2", "Criterion 2", new ScoreRange(-2.5m, 3.5m)),
        ]);
}