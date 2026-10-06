using System.Text.Json;
using System.Text.Json.Nodes;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;
using StudyReportEvaluator.Core.Prompting;

#pragma warning disable GHCP001 // Pinned SDK 1.0.11 capability controls are intentionally explicit.

namespace StudyReportEvaluator.App.Copilot;

public sealed class EvaluationSchemaFactory
{
    public const string ToolName = "submit_quantification";
    public const string ToolDescription = "Submit the complete criterion-level quantification result exactly once.";

    private const int MaximumCellCharacters = 32_767;

    // EvaluatorId stays in the closed schema but is optional: the app already knows it and fills it when omitted.
    private static readonly string[] RootRequiredProperties =
    [
        "Criteria",
    ];

    private static readonly string[] CriterionRequiredProperties =
    [
        "CriterionId",
        "RawScore",
        "Reason",
        "Evidence",
        "EvidenceSource",
        "EvidenceSourceColumnId",
    ];

    public JsonElement CreateSchema(SafeEvaluationPayload payload)
    {
        ValidatePayloadContract(payload);

        JsonArray criterionAlternatives = [];
        foreach (ExpectedCriterion criterion in payload.ExpectedCriteria)
        {
            criterionAlternatives.Add(CreateCriterionSchema(payload, criterion));
        }

        JsonObject schema = new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["EvaluatorId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = StringArray([payload.EvaluatorId]),
                },
                ["Criteria"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = payload.ExpectedCriteria.Length,
                    ["maxItems"] = payload.ExpectedCriteria.Length,
                    ["items"] = new JsonObject
                    {
                        ["oneOf"] = criterionAlternatives,
                    },
                },
            },
            ["required"] = StringArray(RootRequiredProperties),
        };

        return JsonSerializer.SerializeToElement(schema);
    }

    public AIFunction CreateTool(SubmitQuantificationTool collector)
    {
        ArgumentNullException.ThrowIfNull(collector);

        JsonElement schema = CreateSchema(collector.ExpectedPayload);
        Func<ToolInvocation, CancellationToken, ValueTask<string>> handler = collector.InvokeAsync;
        AIFunction inner = CopilotTool.DefineTool(
            handler,
            new CopilotToolOptions
            {
                OverridesBuiltInTool = false,
                SkipPermission = true,
                IsTerminal = true,
                Defer = CopilotToolDefer.Never,
            },
            new AIFunctionFactoryOptions
            {
                Name = ToolName,
                Description = ToolDescription,
                ExcludeResultSchema = true,
            });

        return BindSchema(inner, schema);
    }

    public SessionConfig CreateSessionConfig(SubmitQuantificationTool collector) =>
        CreateRestrictedSessionConfig(CreateTool(collector), ToolName);

    public SessionConfig CreateSessionConfig(
        SafeEvaluationPayload payload,
        out SubmitQuantificationTool collector)
    {
        collector = new SubmitQuantificationTool(payload);
        return CreateSessionConfig(collector);
    }

    internal static void ValidatePayloadContract(SafeEvaluationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (string.IsNullOrWhiteSpace(payload.EvaluatorId)
            || payload.ExpectedCriteria.IsDefaultOrEmpty
            || payload.SupportingSources.IsDefault
            || payload.PrimarySource is null
            || payload.PrimarySource.Kind != EvaluationSourceKind.PrimaryAnswer
            || string.IsNullOrWhiteSpace(payload.PrimarySource.SourceColumnId)
            || payload.PrimarySource.Value is null)
        {
            throw new ArgumentException(
                "The evaluation payload does not define a valid result contract.",
                nameof(payload));
        }

        HashSet<string> criterionIds = new(StringComparer.Ordinal);
        foreach (ExpectedCriterion? criterion in payload.ExpectedCriteria)
        {
            if (criterion is null
                || string.IsNullOrWhiteSpace(criterion.CriterionId)
                || !criterionIds.Add(criterion.CriterionId)
                || criterion.Range.Minimum >= criterion.Range.Maximum)
            {
                throw new ArgumentException(
                    "The evaluation payload does not define a valid result contract.",
                    nameof(payload));
            }
        }

        HashSet<string> sourceIds = new(StringComparer.Ordinal)
        {
            payload.PrimarySource.SourceColumnId,
        };
        foreach (EvaluationSourceCell? source in payload.SupportingSources)
        {
            if (source is null
                || source.Kind != EvaluationSourceKind.SupportingColumn
                || string.IsNullOrWhiteSpace(source.SourceColumnId)
                || source.Value is null
                || !sourceIds.Add(source.SourceColumnId))
            {
                throw new ArgumentException(
                    "The evaluation payload does not define a valid result contract.",
                    nameof(payload));
            }
        }
    }

    private static JsonObject CreateCriterionSchema(
        SafeEvaluationPayload payload,
        ExpectedCriterion criterion)
    {
        List<string> evidenceSources =
        [
            "PRIMARY_ANSWER",
        ];
        if (!payload.SupportingSources.IsEmpty)
        {
            evidenceSources.Add("SUPPORTING_COLUMN");
        }

        evidenceSources.Add("NONE");

        List<string> sourceColumnIds =
        [
            payload.PrimarySource.SourceColumnId,
        ];
        sourceColumnIds.AddRange(payload.SupportingSources.Select(source => source.SourceColumnId));
        sourceColumnIds.Add(string.Empty);

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["CriterionId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = StringArray([criterion.CriterionId]),
                },
                ["RawScore"] = new JsonObject
                {
                    ["type"] = "number",
                    ["minimum"] = JsonValue.Create(criterion.Range.Minimum),
                    ["maximum"] = JsonValue.Create(criterion.Range.Maximum),
                },
                ["Reason"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1,
                    ["maxLength"] = MaximumCellCharacters,
                },
                ["Evidence"] = new JsonObject
                {
                    ["type"] = "string",
                    ["maxLength"] = MaximumCellCharacters,
                },
                ["EvidenceSource"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = StringArray(evidenceSources),
                },
                ["EvidenceSourceColumnId"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = StringArray(sourceColumnIds),
                },
            },
            ["required"] = StringArray(CriterionRequiredProperties),
        };
    }

    internal static SessionConfig CreateRestrictedSessionConfig(
        AIFunction tool,
        string toolName) =>
        new()
        {
            EnableCitations = false,
            EnableFileChangeTracking = false,
            EnableConfigDiscovery = false,
            SkipEmbeddingRetrieval = true,
            EmbeddingCacheStorage = EmbeddingCacheStorageMode.InMemory,
            EnableOnDemandInstructionDiscovery = false,
            EnableFileHooks = false,
            EnableHostGitOperations = false,
            EnableSessionStore = false,
            EnableSkills = false,
            Tools = [tool],
            AvailableTools = [toolName],
            ExcludedTools = [],
            ExcludedBuiltInAgents = [],
            EnableSessionTelemetry = false,
            EnableExperimentalMode = false,
            SkipCustomInstructions = true,
            CustomAgentsLocalOnly = true,
            CoauthorEnabled = false,
            ManageScheduleEnabled = false,
            OnPermissionRequest = static (_, _) =>
                Task.FromResult(PermissionDecision.Reject("Permission denied by the evaluation boundary.")),
            OnUserInputRequest = null,
            Commands = [],
            OnElicitationRequest = null,
            OnExitPlanModeRequest = null,
            OnAutoModeSwitchRequest = null,
            EnableMcpApps = false,
            GitHubMcpToolConfig = new GitHubMcpToolConfig
            {
                EnableAllTools = false,
                AdditionalToolsets = [],
                AdditionalTools = [],
                EnableInsidersMode = false,
                DisableFormDeferral = true,
            },
            Hooks = null,
            AdditionalDirectories = [],
            Streaming = false,
            IncludeSubAgentStreamingEvents = false,
            McpServers = new Dictionary<string, McpServerConfig>(StringComparer.Ordinal),
            McpOAuthTokenStorage = McpOAuthTokenStorageMode.InMemory,
            CustomAgents = [],
            DefaultAgent = null,
            Agent = null,
            SkillDirectories = [],
            PluginDirectories = [],
            InstructionDirectories = [],
            DisabledSkills = [],
            DisabledMcpServers = [],
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            LargeOutput = new LargeToolOutputConfig { Enabled = false },
            ToolSearch = new ToolSearchConfig { Enabled = false },
            Memory = new MemoryConfiguration { Enabled = false },
            Canvases = [],
            RequestCanvasRenderer = false,
            RequestExtensions = false,
            OnMcpAuthRequest = null,
        };

    internal static JsonArray StringArray(IEnumerable<string> values)
    {
        JsonArray array = [];
        foreach (string value in values)
        {
            array.Add(JsonValue.Create(value));
        }

        return array;
    }

    internal static AIFunction BindSchema(AIFunction inner, JsonElement schema) =>
        new SchemaBoundAIFunction(inner, schema);

    private sealed class SchemaBoundAIFunction(AIFunction inner, JsonElement schema) : AIFunction
    {
        private readonly JsonElement _schema = schema.Clone();

        public override string Name => inner.Name;

        public override string Description => inner.Description;

        public override IReadOnlyDictionary<string, object?> AdditionalProperties => inner.AdditionalProperties;

        public override JsonElement JsonSchema => _schema;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            inner.InvokeAsync(arguments, cancellationToken);
    }
}

#pragma warning restore GHCP001