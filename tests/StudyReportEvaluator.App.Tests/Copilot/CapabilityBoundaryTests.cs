using GitHub.Copilot;
using Microsoft.Extensions.AI;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

#pragma warning disable GHCP001 // Verify the pinned SDK 1.0.11 capability controls used by A-02.

namespace StudyReportEvaluator.App.Tests.Copilot;

// Requirements: FR-017 (AC-018)
public sealed class CapabilityBoundaryTests
{
    [Fact]
    public void A02_session_config_exposes_one_result_tool_and_zero_external_tools()
    {
        EvaluationSchemaFactory factory = new();

        SessionConfig config = factory.CreateSessionConfig(CreatePayload(), out _);

        AIFunction onlyTool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(config.Tools!));
        Assert.Equal(EvaluationSchemaFactory.ToolName, onlyTool.Name);
        Assert.Equal([EvaluationSchemaFactory.ToolName], config.AvailableTools);

        int externalToolCount = 0;
        externalToolCount += config.McpServers?.Count ?? 0;
        externalToolCount += config.Commands?.Count ?? 0;
        externalToolCount += config.CustomAgents?.Count ?? 0;
        externalToolCount += config.SkillDirectories?.Count ?? 0;
        externalToolCount += config.PluginDirectories?.Count ?? 0;
        externalToolCount += config.InstructionDirectories?.Count ?? 0;
        externalToolCount += config.AdditionalDirectories?.Count ?? 0;
        externalToolCount += config.GitHubMcpToolConfig?.AdditionalTools?.Count ?? 0;
        externalToolCount += config.GitHubMcpToolConfig?.AdditionalToolsets?.Count ?? 0;

        Assert.Equal(0, externalToolCount);
        Assert.False(config.GitHubMcpToolConfig!.EnableAllTools);
        Assert.False(config.EnableFileChangeTracking);
        Assert.False(config.EnableFileHooks);
        Assert.False(config.EnableHostGitOperations);
        Assert.False(config.EnableSkills);
        Assert.False(config.EnableMcpApps);
        Assert.False(config.EnableConfigDiscovery);
        Assert.False(config.EnableOnDemandInstructionDiscovery);
        Assert.False(config.EnableSessionStore);
        Assert.False(config.ToolSearch!.Enabled);
        Assert.False(config.Memory!.Enabled);
        Assert.False(config.InfiniteSessions!.Enabled);
        Assert.True(config.SkipEmbeddingRetrieval);
        Assert.True(config.SkipCustomInstructions);
        Assert.Null(config.OnUserInputRequest);
        Assert.Null(config.OnElicitationRequest);
        Assert.Null(config.OnMcpAuthRequest);
        Assert.Null(config.Hooks);
        Assert.Null(config.Provider);
        Assert.Null(config.GitHubToken);
    }

    private static SafeEvaluationPayload CreatePayload() => new(
        "Q1",
        "E1",
        "private prompt",
        new EvaluationSourceCell(
            EvaluationSourceKind.PrimaryAnswer,
            "G",
            "private primary answer"),
        [],
        [new ExpectedCriterion("C1", "Criterion", new ScoreRange(0m, 10m))]);
}

#pragma warning restore GHCP001