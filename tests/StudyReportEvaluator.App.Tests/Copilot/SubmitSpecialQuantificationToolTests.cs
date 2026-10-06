using System.Text.Json;
using GitHub.Copilot;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

// Requirements: FR-020 (AC-021)
public sealed class SubmitSpecialQuantificationToolTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("0.5")]
    [InlineData("1")]
    public async Task One_closed_valid_invocation_accepts_zero_through_one(string scoreText)
    {
        SubmitSpecialQuantificationTool tool = new(AuxiliaryEvaluationSchemaFactoryTests.CreateSpecialPayload());
        string json = ValidJson(scoreText);

        string acknowledgement = await tool.InvokeAsync(Invocation(json), TestContext.Current.CancellationToken);

        Assert.Equal("accepted", acknowledgement);
        Assert.True(tool.TryGetAcceptedResult(out SpecialQuantificationResult? result));
        Assert.Equal(decimal.Parse(scoreText, System.Globalization.CultureInfo.InvariantCulture), result.Score);
        Assert.Equal(AuxiliarySubmitStatus.Accepted, tool.Status);
    }

    [Theory]
    [InlineData("-0.001", "SCORE_OUT_OF_RANGE")]
    [InlineData("1.001", "SCORE_OUT_OF_RANGE")]
    [InlineData("\"0.5\"", "NUMBER_INVALID")]
    public async Task Score_outside_zero_one_or_wrong_type_is_rejected(string scoreJson, string expectedCode)
    {
        SubmitSpecialQuantificationTool tool = new(AuxiliaryEvaluationSchemaFactoryTests.CreateSpecialPayload());

        await tool.InvokeAsync(Invocation(ValidJson(scoreJson)), TestContext.Current.CancellationToken);

        Assert.Equal(AuxiliarySubmitStatus.InvalidPayload, tool.Status);
        Assert.Contains(expectedCode, tool.ErrorCodes);
        Assert.False(tool.TryGetAcceptedResult(out _));
    }

    [Fact]
    public async Task Evidence_must_bind_to_the_exact_sent_same_row_source()
    {
        SubmitSpecialQuantificationTool tool = new(AuxiliaryEvaluationSchemaFactoryTests.CreateSpecialPayload());
        string json = """
            {
              "SpecialEvaluationId":"S1",
              "Score":0.5,
              "Reason":"reason",
              "Evidence":"other row",
              "EvidenceSource":"PRIMARY_ANSWER",
              "EvidenceSourceColumnId":"G"
            }
            """;

        await tool.InvokeAsync(Invocation(json), TestContext.Current.CancellationToken);

        Assert.Contains("EVIDENCE_NOT_EXACT_SUBSTRING", tool.ErrorCodes);
        Assert.False(tool.TryGetAcceptedResult(out _));
    }

    [Fact]
    public async Task Unknown_property_and_duplicate_call_are_closed()
    {
        SubmitSpecialQuantificationTool unknown = new(AuxiliaryEvaluationSchemaFactoryTests.CreateSpecialPayload());
        string unknownJson = ValidJson("0.5").Replace("}", ",\"FinalScore\":100}", StringComparison.Ordinal);
        await unknown.InvokeAsync(Invocation(unknownJson), TestContext.Current.CancellationToken);
        Assert.Contains("ROOT_UNKNOWN_PROPERTY", unknown.ErrorCodes);

        SubmitSpecialQuantificationTool duplicate = new(AuxiliaryEvaluationSchemaFactoryTests.CreateSpecialPayload());
        ToolInvocation invocation = Invocation(ValidJson("0.5"));
        Assert.Equal("accepted", await duplicate.InvokeAsync(invocation, TestContext.Current.CancellationToken));
        Assert.Equal("rejected", await duplicate.InvokeAsync(invocation, TestContext.Current.CancellationToken));
        Assert.Equal(AuxiliarySubmitStatus.DuplicateInvocation, duplicate.Status);
        Assert.False(duplicate.TryGetAcceptedResult(out _));
    }

    private static string ValidJson(string scoreJson) => $$"""
        {
          "SpecialEvaluationId":"S1",
          "Score":{{scoreJson}},
          "Reason":"reason",
          "Evidence":"primary evidence",
          "EvidenceSource":"PRIMARY_ANSWER",
          "EvidenceSourceColumnId":"G"
        }
        """;

    private static ToolInvocation Invocation(string json) => new()
    {
        SessionId = "special-test-session",
        ToolCallId = Guid.NewGuid().ToString("N"),
        ToolName = AuxiliaryEvaluationSchemaFactory.SpecialToolName,
        Arguments = JsonDocument.Parse(json).RootElement.Clone(),
    };
}
