using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.App.Workbooks.Checkpoint;

public sealed record CheckpointRuntimeIdentity
{
    public required string ApplicationIdentity { get; init; }

    public required string CliVersion { get; init; }

    public required string CliSha256 { get; init; }

    public required string SdkInformationalVersion { get; init; }

    public override string ToString() =>
        $"{nameof(CheckpointRuntimeIdentity)} {{ Content = <redacted> }}";
}

public sealed record CheckpointTokenUsage
{
    public bool IsAvailable { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long ReasoningTokens { get; init; }

    public long CacheReadTokens { get; init; }

    public long CacheWriteTokens { get; init; }

    public override string ToString() =>
        $"{nameof(CheckpointTokenUsage)} {{ IsAvailable = {IsAvailable}, Content = <redacted> }}";
}

public sealed record CheckpointReference
{
    public required string QuestionId { get; init; }

    public string? Answer { get; init; }

    public required string StatusCode { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public int AttemptCount { get; init; }

    public CheckpointTokenUsage TokenUsage { get; init; } = new();

    public override string ToString() =>
        $"{nameof(CheckpointReference)} {{ StatusCode = {StatusCode}, Content = <redacted> }}";
}

public sealed record CheckpointNormalResult
{
    public required string QuestionId { get; init; }

    public required string EvaluatorId { get; init; }

    public required string StatusCode { get; init; }

    public int AttemptCount { get; init; }

    public bool Scorable { get; init; }

    public bool ScorableKnown { get; init; }

    public QuantificationResult? AcceptedResult { get; init; }

    public CheckpointTokenUsage TokenUsage { get; init; } = new();

    public override string ToString() =>
        $"{nameof(CheckpointNormalResult)} {{ StatusCode = {StatusCode}, Content = <redacted> }}";
}

public sealed record CheckpointSpecialResult
{
    public required string QuestionId { get; init; }

    public required string SpecialEvaluationId { get; init; }

    public required string StatusCode { get; init; }

    public int AttemptCount { get; init; }

    public SpecialQuantificationResult? AcceptedResult { get; init; }

    public CheckpointTokenUsage TokenUsage { get; init; } = new();

    public override string ToString() =>
        $"{nameof(CheckpointSpecialResult)} {{ StatusCode = {StatusCode}, Content = <redacted> }}";
}

public sealed record CheckpointSimilarityResult
{
    public required string QuestionId { get; init; }

    public required string StatusCode { get; init; }

    public int AttemptCount { get; init; }

    public SimilarityQuantificationResult? AcceptedResult { get; init; }

    public CheckpointTokenUsage TokenUsage { get; init; } = new();

    public override string ToString() =>
        $"{nameof(CheckpointSimilarityResult)} {{ StatusCode = {StatusCode}, Content = <redacted> }}";
}

public sealed record CheckpointCompletedRow
{
    public int SourceRowNumber { get; init; }

    public ImmutableArray<CheckpointNormalResult> NormalResults { get; init; } = [];

    public ImmutableArray<CheckpointSpecialResult> SpecialResults { get; init; } = [];

    public ImmutableArray<CheckpointSimilarityResult> SimilarityResults { get; init; } = [];

    public override string ToString() =>
        $"{nameof(CheckpointCompletedRow)} {{ SourceRowNumber = {SourceRowNumber.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed record CheckpointEnvelope
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string InputPath { get; init; }

    public required InputSnapshot Input { get; init; }

    public required string DefinitionCanonicalJson { get; init; }

    public required string DefinitionSha256 { get; init; }

    public required string NormalModelId { get; init; }

    public string ReferenceModelId { get; init; } = "auto";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningEffort { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContextTier { get; init; }

    public required CheckpointRuntimeIdentity Runtime { get; init; }

    public required string FinalPath { get; init; }

    public required string PartialPath { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset SavedAtUtc { get; init; }

    public ImmutableArray<CheckpointReference> References { get; init; } = [];

    public ImmutableArray<CheckpointCompletedRow> CompletedRows { get; init; } = [];

    public override string ToString() =>
        $"{nameof(CheckpointEnvelope)} {{ SchemaVersion = {SchemaVersion.ToString(CultureInfo.InvariantCulture)}, ReferenceCount = {References.Length.ToString(CultureInfo.InvariantCulture)}, CompletedRowCount = {CompletedRows.Length.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}
