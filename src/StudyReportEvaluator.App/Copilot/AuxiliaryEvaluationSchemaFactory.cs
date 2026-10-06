using System.Text.Json;
using System.Text.Json.Nodes;
using GitHub.Copilot;
using Microsoft.Extensions.AI;
using StudyReportEvaluator.Core.Prompting;

#pragma warning disable GHCP001 // Pinned SDK capability controls are intentionally explicit.

namespace StudyReportEvaluator.App.Copilot;

public sealed class AuxiliaryEvaluationSchemaFactory
{
    public const string ReferenceToolName = "submit_reference_answer";
    public const string SpecialToolName = "submit_special_quantification";
    public const string ReferenceToolDescription = "Submit one reference answer exactly once.";
    public const string SpecialToolDescription = "Submit one special quantification score from zero through one exactly once.";

    private static readonly string[] ReferenceProperties = ["QuestionId", "Answer"];
    private static readonly string[] SpecialProperties =
    [
        "SpecialEvaluationId",
        "Score",
        "Reason",
        "Evidence",
        "EvidenceSource",
        "EvidenceSourceColumnId",
    ];

    public JsonElement CreateReferenceSchema(SafeReferenceAnswerPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.QuestionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.RenderedPrompt);
        JsonObject schema = ClosedObject(
            ReferenceProperties,
            new JsonObject
            {
                ["QuestionId"] = StringEnum([payload.QuestionId]),
                ["Answer"] = BoundedString(requireNonEmpty: true),
            });
        return JsonSerializer.SerializeToElement(schema);
    }

    public JsonElement CreateSpecialSchema(SafeSpecialEvaluationPayload payload)
    {
        ValidateSpecialPayload(payload);
        List<string> evidenceKinds = ["PRIMARY_ANSWER"];
        if (!payload.SupportingSources.IsEmpty)
        {
            evidenceKinds.Add("SUPPORTING_COLUMN");
        }

        evidenceKinds.Add("NONE");
        List<string> sourceIds = [payload.PrimarySource.SourceColumnId];
        sourceIds.AddRange(payload.SupportingSources.Select(source => source.SourceColumnId));
        sourceIds.Add(string.Empty);
        JsonObject schema = ClosedObject(
            SpecialProperties,
            new JsonObject
            {
                ["SpecialEvaluationId"] = StringEnum([payload.SpecialEvaluationId]),
                ["Score"] = new JsonObject
                {
                    ["type"] = "number",
                    ["minimum"] = 0,
                    ["maximum"] = 1,
                },
                ["Reason"] = BoundedString(requireNonEmpty: true),
                ["Evidence"] = BoundedString(requireNonEmpty: false),
                ["EvidenceSource"] = StringEnum(evidenceKinds),
                ["EvidenceSourceColumnId"] = StringEnum(sourceIds),
            });
        return JsonSerializer.SerializeToElement(schema);
    }

    public AIFunction CreateReferenceTool(SubmitReferenceAnswerTool collector)
    {
        ArgumentNullException.ThrowIfNull(collector);
        return CreateTool(
            ReferenceToolName,
            ReferenceToolDescription,
            CreateReferenceSchema(collector.ExpectedPayload),
            collector.InvokeAsync);
    }

    public AIFunction CreateSpecialTool(SubmitSpecialQuantificationTool collector)
    {
        ArgumentNullException.ThrowIfNull(collector);
        return CreateTool(
            SpecialToolName,
            SpecialToolDescription,
            CreateSpecialSchema(collector.ExpectedPayload),
            collector.InvokeAsync);
    }

    public SessionConfig CreateReferenceSessionConfig(
        SafeReferenceAnswerPayload payload,
        out SubmitReferenceAnswerTool collector)
    {
        collector = new SubmitReferenceAnswerTool(payload);
        return EvaluationSchemaFactory.CreateRestrictedSessionConfig(
            CreateReferenceTool(collector),
            ReferenceToolName);
    }

    public SessionConfig CreateSpecialSessionConfig(
        SafeSpecialEvaluationPayload payload,
        out SubmitSpecialQuantificationTool collector)
    {
        collector = new SubmitSpecialQuantificationTool(payload);
        return EvaluationSchemaFactory.CreateRestrictedSessionConfig(
            CreateSpecialTool(collector),
            SpecialToolName);
    }

    internal static void ValidateSpecialPayload(SafeSpecialEvaluationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(payload.QuestionId)
            || string.IsNullOrWhiteSpace(payload.SpecialEvaluationId)
            || string.IsNullOrWhiteSpace(payload.RenderedPrompt)
            || payload.PrimarySource is null
            || payload.PrimarySource.Kind != EvaluationSourceKind.PrimaryAnswer
            || string.IsNullOrWhiteSpace(payload.PrimarySource.SourceColumnId)
            || payload.PrimarySource.Value is null
            || payload.SupportingSources.IsDefault)
        {
            throw new ArgumentException("The special-evaluation payload is invalid.", nameof(payload));
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
                throw new ArgumentException("The special-evaluation payload is invalid.", nameof(payload));
            }
        }
    }

    private static AIFunction CreateTool(
        string name,
        string description,
        JsonElement schema,
        Func<ToolInvocation, CancellationToken, ValueTask<string>> handler)
    {
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
                Name = name,
                Description = description,
                ExcludeResultSchema = true,
            });
        return EvaluationSchemaFactory.BindSchema(inner, schema);
    }

    private static JsonObject ClosedObject(string[] required, JsonObject properties) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = properties,
        ["required"] = EvaluationSchemaFactory.StringArray(required),
    };

    private static JsonObject StringEnum(IEnumerable<string> values) => new()
    {
        ["type"] = "string",
        ["enum"] = EvaluationSchemaFactory.StringArray(values),
    };

    private static JsonObject BoundedString(bool requireNonEmpty)
    {
        JsonObject schema = new()
        {
            ["type"] = "string",
            ["maxLength"] = AuxiliaryToolWireParser.MaximumCellCharacters,
        };
        if (requireNonEmpty)
        {
            schema["minLength"] = 1;
        }

        return schema;
    }
}

#pragma warning restore GHCP001
