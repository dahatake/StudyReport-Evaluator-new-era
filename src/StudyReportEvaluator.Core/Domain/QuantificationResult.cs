using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace StudyReportEvaluator.Core.Domain;

public enum EvidenceSourceKind
{
    [JsonStringEnumMemberName("PRIMARY_ANSWER")]
    PrimaryAnswer,

    [JsonStringEnumMemberName("SUPPORTING_COLUMN")]
    SupportingColumn,

    [JsonStringEnumMemberName("NONE")]
    None,
}

public sealed record CriterionQuantificationResult
{
    public required string CriterionId { get; init; }

    public decimal RawScore { get; init; }

    public required string Reason { get; init; }

    public required string Evidence { get; init; }

    public EvidenceSourceKind EvidenceSource { get; init; }

    public required string EvidenceSourceColumnId { get; init; }

    public override string ToString() =>
        $"CriterionQuantificationResult {{ CriterionId = {CriterionId}, RawScore = {RawScore}, Content = <redacted>, EvidenceSource = {EvidenceSource}, EvidenceSourceColumnId = {EvidenceSourceColumnId} }}";
}

public sealed record QuantificationResult
{
    public required string EvaluatorId { get; init; }

    public ImmutableArray<CriterionQuantificationResult> Criteria { get; init; } = [];

    public override string ToString() =>
        $"QuantificationResult {{ EvaluatorId = {EvaluatorId}, Criteria = {Criteria.Length}, Content = <redacted> }}";
}
