using System.Collections.Immutable;
using System.Text.Json;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

#pragma warning disable GHCP001 // Verify the pinned SDK 1.0.11 capability controls used by production.

namespace StudyReportEvaluator.App.Tests.Copilot;

public sealed class EvaluationSchemaFactoryTests
{
    [Fact]
    public void Schema_is_closed_dynamic_and_contains_only_raw_criterion_results()
    {
        const string privatePrompt = "PRIVATE-PROMPT-CANARY";
        const string privateAnswer = "PRIVATE-ANSWER-CANARY";
        EvaluationSchemaFactory factory = new();
        SafeEvaluationPayload payload = CreatePayload(privatePrompt, privateAnswer);

        JsonElement schema = factory.CreateSchema(payload);

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["Criteria"],
            Required(schema).Order(StringComparer.Ordinal).ToArray());

        JsonElement rootProperties = schema.GetProperty("properties");
        Assert.Equal(
            ["Criteria", "EvaluatorId"],
            PropertyNames(rootProperties).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["E1"], Strings(rootProperties.GetProperty("EvaluatorId").GetProperty("enum")));

        JsonElement criteria = rootProperties.GetProperty("Criteria");
        Assert.Equal(2, criteria.GetProperty("minItems").GetInt32());
        Assert.Equal(2, criteria.GetProperty("maxItems").GetInt32());
        JsonElement[] alternatives = criteria
            .GetProperty("items")
            .GetProperty("oneOf")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(2, alternatives.Length);

        JsonElement c1 = FindCriterion(alternatives, "C1");
        JsonElement c2 = FindCriterion(alternatives, "C2");
        Assert.Equal(0m, CriterionProperty(c1, "RawScore").GetProperty("minimum").GetDecimal());
        Assert.Equal(10m, CriterionProperty(c1, "RawScore").GetProperty("maximum").GetDecimal());
        Assert.Equal(-2.5m, CriterionProperty(c2, "RawScore").GetProperty("minimum").GetDecimal());
        Assert.Equal(3.5m, CriterionProperty(c2, "RawScore").GetProperty("maximum").GetDecimal());

        foreach (JsonElement alternative in alternatives)
        {
            Assert.False(alternative.GetProperty("additionalProperties").GetBoolean());
            Assert.Equal(
                [
                    "CriterionId",
                    "Evidence",
                    "EvidenceSource",
                    "EvidenceSourceColumnId",
                    "RawScore",
                    "Reason",
                ],
                Required(alternative).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal(
                ["PRIMARY_ANSWER", "SUPPORTING_COLUMN", "NONE"],
                Strings(CriterionProperty(alternative, "EvidenceSource").GetProperty("enum")));
            Assert.Equal(
                ["G", "K", "L", ""],
                Strings(CriterionProperty(alternative, "EvidenceSourceColumnId").GetProperty("enum")));
        }

        string serialized = schema.GetRawText();
        Assert.DoesNotContain(privatePrompt, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(privateAnswer, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Knowledge display", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Weight", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EvaluatorScore", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("QuestionScore", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("OverallScore", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Pass", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schema_omits_supporting_source_kind_and_ids_when_none_were_sent()
    {
        EvaluationSchemaFactory factory = new();
        SafeEvaluationPayload payload = new(
            "Q1",
            "E1",
            "<private>",
            new EvaluationSourceCell(EvaluationSourceKind.PrimaryAnswer, "J", "primary"),
            [],
            [new ExpectedCriterion("C1", "Criterion", new ScoreRange(0m, 1m))]);

        JsonElement criterion = factory.CreateSchema(payload)
            .GetProperty("properties")
            .GetProperty("Criteria")
            .GetProperty("items")
            .GetProperty("oneOf")[0];

        Assert.Equal(
            ["PRIMARY_ANSWER", "NONE"],
            Strings(CriterionProperty(criterion, "EvidenceSource").GetProperty("enum")));
        Assert.Equal(
            ["J", ""],
            Strings(CriterionProperty(criterion, "EvidenceSourceColumnId").GetProperty("enum")));
    }

    [Fact]
    public async Task Session_exposes_one_terminal_result_tool_and_disables_ambient_capabilities()
    {
        EvaluationSchemaFactory factory = new();

        SessionConfig config = factory.CreateSessionConfig(CreatePayload(), out SubmitQuantificationTool collector);

        AIFunction tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(config.Tools!));
        Assert.Equal(EvaluationSchemaFactory.ToolName, tool.Name);
        Assert.Equal(factory.CreateSchema(CreatePayload()).GetRawText(), tool.JsonSchema.GetRawText());
        Assert.Equal([EvaluationSchemaFactory.ToolName], config.AvailableTools);
        Assert.NotNull(collector);

        Assert.False(config.EnableCitations);
        Assert.False(config.EnableFileChangeTracking);
        Assert.False(config.EnableConfigDiscovery);
        Assert.True(config.SkipEmbeddingRetrieval);
        Assert.Equal(EmbeddingCacheStorageMode.InMemory, config.EmbeddingCacheStorage);
        Assert.False(config.EnableOnDemandInstructionDiscovery);
        Assert.False(config.EnableFileHooks);
        Assert.False(config.EnableHostGitOperations);
        Assert.False(config.EnableSessionStore);
        Assert.False(config.EnableSkills);
        Assert.False(config.EnableSessionTelemetry);
        Assert.False(config.EnableExperimentalMode);
        Assert.True(config.SkipCustomInstructions);
        Assert.False(config.CoauthorEnabled);
        Assert.False(config.ManageScheduleEnabled);
        Assert.False(config.EnableMcpApps);
        Assert.False(config.Streaming);
        Assert.False(config.IncludeSubAgentStreamingEvents);
        Assert.False(config.InfiniteSessions!.Enabled);
        Assert.False(config.LargeOutput!.Enabled);
        Assert.False(config.ToolSearch!.Enabled);
        Assert.False(config.Memory!.Enabled);
        Assert.False(config.RequestCanvasRenderer);
        Assert.False(config.RequestExtensions);

        Assert.Empty(config.Commands!);
        Assert.Empty(config.AdditionalDirectories!);
        Assert.Empty(config.McpServers!);
        Assert.Equal(McpOAuthTokenStorageMode.InMemory, config.McpOAuthTokenStorage);
        Assert.Empty(config.CustomAgents!);
        Assert.Empty(config.SkillDirectories!);
        Assert.Empty(config.PluginDirectories!);
        Assert.Empty(config.InstructionDirectories!);
        Assert.Empty(config.Canvases!);
        Assert.Null(config.OnUserInputRequest);
        Assert.Null(config.OnElicitationRequest);
        Assert.Null(config.Hooks);
        Assert.Null(config.DefaultAgent);
        Assert.Null(config.Agent);
        Assert.Null(config.OnMcpAuthRequest);
        Assert.False(config.GitHubMcpToolConfig!.EnableAllTools);
        Assert.Empty(config.GitHubMcpToolConfig.AdditionalTools!);
        Assert.Empty(config.GitHubMcpToolConfig.AdditionalToolsets!);

        Assert.NotNull(config.OnPermissionRequest);
        PermissionDecision decision = await config.OnPermissionRequest(null!, null!);
        Assert.Equal("reject", decision.Kind);
    }

    [Fact]
    public void Tool_metadata_forces_preloaded_permission_bypassed_terminal_execution()
    {
        EvaluationSchemaFactory factory = new();
        SubmitQuantificationTool collector = new(CreatePayload());

        AIFunction tool = factory.CreateTool(collector);

        Assert.Equal(EvaluationSchemaFactory.ToolName, tool.Name);
        AssertMetadataBoolean(tool, "skip_permission", expected: true);
        AssertMetadataBoolean(tool, "is_terminal", expected: true);
        Assert.True(tool.AdditionalProperties.TryGetValue("defer", out object? defer));
        Assert.Equal(CopilotToolDefer.Never, Assert.IsType<CopilotToolDefer>(defer));
    }

    [Fact]
    public async Task Generated_function_invokes_the_collector_through_the_sdk_context_binding()
    {
        EvaluationSchemaFactory factory = new();
        SessionConfig config = factory.CreateSessionConfig(CreatePayload(), out SubmitQuantificationTool collector);
        AIFunction tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(config.Tools!));
        ToolInvocation invocation = new()
        {
            SessionId = "synthetic-session",
            ToolCallId = "synthetic-call",
            ToolName = EvaluationSchemaFactory.ToolName,
            Arguments = JsonDocument.Parse(
                """
                {
                  "EvaluatorId": "E1",
                  "Criteria": [
                    {
                      "CriterionId": "C1",
                      "RawScore": 4,
                      "Reason": "short reason",
                      "Evidence": "primary",
                      "EvidenceSource": "PRIMARY_ANSWER",
                      "EvidenceSourceColumnId": "G"
                    },
                    {
                      "CriterionId": "C2",
                      "RawScore": 1.5,
                      "Reason": "short reason",
                      "Evidence": "support K",
                      "EvidenceSource": "SUPPORTING_COLUMN",
                      "EvidenceSourceColumnId": "K"
                    }
                  ]
                }
                """).RootElement.Clone(),
        };
        AIFunctionArguments arguments = new()
        {
            Context = new Dictionary<object, object?>
            {
                [typeof(ToolInvocation)] = invocation,
            },
        };

        object? response = await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        JsonElement responseJson = Assert.IsType<JsonElement>(response);
        Assert.Equal(SubmitQuantificationTool.AcceptedAcknowledgement, responseJson.GetString());
        Assert.True(collector.TryGetAcceptedResult(out _));
    }

    [Fact]
    public void Invalid_contract_is_rejected_without_echoing_content()
    {
        const string canary = "PRIVATE-CONTRACT-CANARY";
        EvaluationSchemaFactory factory = new();
        SafeEvaluationPayload payload = new(
            "Q1",
            "E1",
            canary,
            new EvaluationSourceCell(EvaluationSourceKind.PrimaryAnswer, "G", canary),
            [],
            [
                new ExpectedCriterion("C1", canary, new ScoreRange(0m, 10m)),
                new ExpectedCriterion("C1", canary, new ScoreRange(0m, 10m)),
            ]);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => factory.CreateSchema(payload));

        Assert.DoesNotContain(canary, exception.ToString(), StringComparison.Ordinal);
    }

    private static SafeEvaluationPayload CreatePayload(
        string renderedPrompt = "<private-prompt>",
        string primaryValue = "primary evidence") =>
        new(
            "Q1",
            "E1",
            renderedPrompt,
            new EvaluationSourceCell(EvaluationSourceKind.PrimaryAnswer, "G", primaryValue),
            [
                new EvaluationSourceCell(EvaluationSourceKind.SupportingColumn, "K", "support K"),
                new EvaluationSourceCell(EvaluationSourceKind.SupportingColumn, "L", "support L"),
            ],
            [
                new ExpectedCriterion("C1", "Knowledge display", new ScoreRange(0m, 10m)),
                new ExpectedCriterion("C2", "Other display", new ScoreRange(-2.5m, 3.5m)),
            ]);

    private static JsonElement FindCriterion(IEnumerable<JsonElement> alternatives, string criterionId) =>
        alternatives.Single(alternative =>
            Strings(CriterionProperty(alternative, "CriterionId").GetProperty("enum"))
                .SequenceEqual([criterionId], StringComparer.Ordinal));

    private static JsonElement CriterionProperty(JsonElement criterion, string propertyName) =>
        criterion.GetProperty("properties").GetProperty(propertyName);

    private static string[] Required(JsonElement schema) => Strings(schema.GetProperty("required"));

    private static string[] PropertyNames(JsonElement properties) =>
        properties.EnumerateObject().Select(property => property.Name).ToArray();

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static void AssertMetadataBoolean(AIFunction tool, string key, bool expected)
    {
        Assert.True(tool.AdditionalProperties.TryGetValue(key, out object? value));
        Assert.Equal(expected, ReadBoolean(value));
    }

    private static bool ReadBoolean(object? value) => value switch
    {
        bool boolean => boolean,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        _ => throw new Xunit.Sdk.XunitException("Tool metadata is not a Boolean."),
    };
}

#pragma warning restore GHCP001