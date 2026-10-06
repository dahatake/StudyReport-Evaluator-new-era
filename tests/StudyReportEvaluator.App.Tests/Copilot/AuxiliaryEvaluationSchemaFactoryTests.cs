using System.Text.Json;
using GitHub.Copilot;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

#pragma warning disable GHCP001 // Pinned SDK capability controls are intentionally verified.

namespace StudyReportEvaluator.App.Tests.Copilot;

public sealed class AuxiliaryEvaluationSchemaFactoryTests
{
    private readonly AuxiliaryEvaluationSchemaFactory factory = new();

    [Fact]
    public void Reference_schema_is_closed_and_bound_to_one_question()
    {
        JsonElement schema = factory.CreateReferenceSchema(new SafeReferenceAnswerPayload("Q1", "prompt"));
        JsonElement properties = schema.GetProperty("properties");

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["QuestionId", "Answer"], Required(schema));
        Assert.Equal(["Q1"], Strings(properties.GetProperty("QuestionId").GetProperty("enum")));
        Assert.Equal(1, properties.GetProperty("Answer").GetProperty("minLength").GetInt32());
        Assert.Equal(32_767, properties.GetProperty("Answer").GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public void Special_schema_is_closed_to_zero_one_and_actually_sent_sources()
    {
        JsonElement schema = factory.CreateSpecialSchema(CreateSpecialPayload());
        JsonElement properties = schema.GetProperty("properties");

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(0m, properties.GetProperty("Score").GetProperty("minimum").GetDecimal());
        Assert.Equal(1m, properties.GetProperty("Score").GetProperty("maximum").GetDecimal());
        Assert.Equal(["S1"], Strings(properties.GetProperty("SpecialEvaluationId").GetProperty("enum")));
        Assert.Equal(
            ["PRIMARY_ANSWER", "SUPPORTING_COLUMN", "NONE"],
            Strings(properties.GetProperty("EvidenceSource").GetProperty("enum")));
        Assert.Equal(
            ["G", "K", ""],
            Strings(properties.GetProperty("EvidenceSourceColumnId").GetProperty("enum")));
    }

    [Fact]
    public void Auxiliary_sessions_expose_exactly_one_terminal_tool_and_reject_permissions()
    {
        SessionConfig reference = factory.CreateReferenceSessionConfig(
            new SafeReferenceAnswerPayload("Q1", "prompt"),
            out _);
        SessionConfig special = factory.CreateSpecialSessionConfig(CreateSpecialPayload(), out _);

        AssertRestricted(reference, AuxiliaryEvaluationSchemaFactory.ReferenceToolName);
        AssertRestricted(special, AuxiliaryEvaluationSchemaFactory.SpecialToolName);
    }

    private static void AssertRestricted(SessionConfig config, string expectedToolName)
    {
        Assert.Equal([expectedToolName], config.AvailableTools);
        Assert.Equal(expectedToolName, Assert.Single(config.Tools ?? []).Name);
        Assert.False(config.EnableSessionStore);
        Assert.False(config.EnableSkills);
        Assert.False(config.EnableHostGitOperations);
        Assert.False(config.EnableFileHooks);
        Assert.False(config.EnableMcpApps);
        Assert.NotNull(config.OnPermissionRequest);
    }

    internal static SafeSpecialEvaluationPayload CreateSpecialPayload() => new(
        "Q1",
        "S1",
        "prompt",
        new EvaluationSourceCell(EvaluationSourceKind.PrimaryAnswer, "G", "primary evidence"),
        [new EvaluationSourceCell(EvaluationSourceKind.SupportingColumn, "K", "support evidence")]);

    private static string[] Required(JsonElement schema) =>
        schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString()!).ToArray();
}

#pragma warning restore GHCP001
