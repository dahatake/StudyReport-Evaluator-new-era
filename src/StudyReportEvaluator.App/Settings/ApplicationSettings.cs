using System.Collections.Immutable;
using System.Text.Json.Serialization;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.App.Settings;

/// <summary>Persisted preferences and an optional definition, not effective run values or UI state.</summary>
/// <remarks>
/// Use JsonNamingPolicy.CamelCase for nested domain properties. Schema and value validation
/// belong to the settings store; this record does not resolve models or output directories.
/// </remarks>
public sealed record ApplicationSettings
{
    public const int CurrentSchemaVersion = 1;

    public const int MaximumCachedModels = 4096;

    [JsonRequired]
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The requested model ID, not a verified available model selection.</summary>
    [JsonPropertyName("preferredModelId")]
    public string? PreferredModelId { get; init; }

    [JsonPropertyName("modelPreferences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<ModelSelectionPreference>? ModelPreferences { get; init; }

    /// <summary>The requested concurrency, validated as 1 through 16 by the settings store.</summary>
    [JsonPropertyName("maxConcurrency")]
    public int MaxConcurrency { get; init; } = EphemeralEvaluationRunnerOptions.DefaultMaxConcurrency;

    /// <summary>An explicitly selected absolute directory, or null for the input-adjacent default.</summary>
    [JsonPropertyName("outputDirectoryOverride")]
    public string? OutputDirectoryOverride { get; init; }

    [JsonPropertyName("definition")]
    public QuantificationDefinition? Definition { get; init; }

    /// <summary>Last fetched catalog for display only; never proof of authentication or permission.</summary>
    [JsonPropertyName("cachedModels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<CachedCopilotModel>? CachedModels { get; init; }

    public override string ToString() =>
        $"{nameof(ApplicationSettings)} {{ Content = <redacted> }}";
}

public sealed record CachedCopilotModel(string Id, int? MaximumPromptTokens, int? MaximumContextWindowTokens)
{
    public override string ToString() => $"{nameof(CachedCopilotModel)} {{ Content = <redacted> }}";
}

public sealed record ModelSelectionPreference(string ModelId, string? ReasoningEffort, string ContextTier)
{
    public override string ToString() => $"{nameof(ModelSelectionPreference)} {{ Content = <redacted> }}";
}