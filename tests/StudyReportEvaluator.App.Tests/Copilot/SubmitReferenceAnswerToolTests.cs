using System.Text.Json;
using GitHub.Copilot;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

// Requirements: FR-018 (AC-019)
public sealed class SubmitReferenceAnswerToolTests
{
    [Fact]
    public async Task One_closed_valid_invocation_is_accepted()
    {
        SubmitReferenceAnswerTool tool = new(new SafeReferenceAnswerPayload("Q1", "private prompt"));

        string acknowledgement = await tool.InvokeAsync(
            Invocation("""{"QuestionId":"Q1","Answer":"reference answer"}"""),
            TestContext.Current.CancellationToken);

        Assert.Equal("accepted", acknowledgement);
        Assert.True(tool.TryGetAcceptedResult(out ReferenceAnswerResult? result));
        Assert.Equal("Q1", result.QuestionId);
        Assert.Equal("reference answer", result.Answer);
        Assert.Equal(AuxiliarySubmitStatus.Accepted, tool.Status);
        Assert.Empty(tool.ErrorCodes);
    }

    [Theory]
    [InlineData("{\"QuestionId\":\"Q1\"}", "ROOT_MISSING_PROPERTY")]
    [InlineData("{\"QuestionId\":\"Q1\",\"Answer\":\"a\",\"Score\":1}", "ROOT_UNKNOWN_PROPERTY")]
    [InlineData("{\"QuestionId\":\"Q1\",\"QuestionId\":\"Q1\",\"Answer\":\"a\"}", "ROOT_DUPLICATE_PROPERTY")]
    [InlineData("{\"QuestionId\":\"Q1\",\"Answer\":\" \"}", "TEXT_REQUIRED")]
    [InlineData("{\"QuestionId\":\"q1\",\"Answer\":\"a\"}", "ID_MISMATCH")]
    public async Task Missing_unknown_duplicate_blank_and_wrong_id_are_rejected(string json, string expectedCode)
    {
        SubmitReferenceAnswerTool tool = new(new SafeReferenceAnswerPayload("Q1", "private prompt"));

        string acknowledgement = await tool.InvokeAsync(Invocation(json), TestContext.Current.CancellationToken);

        Assert.Equal("rejected", acknowledgement);
        Assert.Equal(AuxiliarySubmitStatus.InvalidPayload, tool.Status);
        Assert.Contains(expectedCode, tool.ErrorCodes);
        Assert.False(tool.TryGetAcceptedResult(out _));
    }

    [Fact]
    public async Task Duplicate_invocation_invalidates_an_accepted_reference()
    {
        SubmitReferenceAnswerTool tool = new(new SafeReferenceAnswerPayload("Q1", "private prompt"));
        ToolInvocation invocation = Invocation("""{"QuestionId":"Q1","Answer":"reference"}""");

        Assert.Equal("accepted", await tool.InvokeAsync(invocation, TestContext.Current.CancellationToken));
        Assert.Equal("rejected", await tool.InvokeAsync(invocation, TestContext.Current.CancellationToken));

        Assert.Equal(AuxiliarySubmitStatus.DuplicateInvocation, tool.Status);
        Assert.Equal(2, tool.InvocationCount);
        Assert.Equal(["TOOL_INVOCATION_COUNT_INVALID"], tool.ErrorCodes);
        Assert.False(tool.TryGetAcceptedResult(out _));
    }

    [Fact]
    public async Task Rejection_and_string_representation_do_not_disclose_content()
    {
        const string canary = "PRIVATE-REFERENCE-CANARY";
        SubmitReferenceAnswerTool tool = new(new SafeReferenceAnswerPayload("Q1", canary));
        ToolInvocation invocation = Invocation($$"""{"QuestionId":"q1","Answer":"{{canary}}"}""");

        string acknowledgement = await tool.InvokeAsync(invocation, TestContext.Current.CancellationToken);
        string rendered = string.Join('|', acknowledgement, tool.ToString(), string.Join(',', tool.ErrorCodes));

        Assert.DoesNotContain(canary, rendered, StringComparison.Ordinal);
        Assert.False(tool.TryGetAcceptedResult(out _));
    }

    private static ToolInvocation Invocation(string json) => new()
    {
        SessionId = "reference-test-session",
        ToolCallId = Guid.NewGuid().ToString("N"),
        ToolName = AuxiliaryEvaluationSchemaFactory.ReferenceToolName,
        Arguments = JsonDocument.Parse(json).RootElement.Clone(),
    };
}
