namespace StudyReportEvaluator.Core.Domain;

public sealed record ReferenceAnswerResult
{
    public required string QuestionId { get; init; }

    public required string Answer { get; init; }

    public override string ToString() =>
        $"{nameof(ReferenceAnswerResult)} {{ QuestionId = {QuestionId}, Content = <redacted> }}";
}

public sealed record SpecialQuantificationResult
{
    public required string SpecialEvaluationId { get; init; }

    public decimal Score { get; init; }

    public required string Reason { get; init; }

    public required string Evidence { get; init; }

    public EvidenceSourceKind EvidenceSource { get; init; }

    public required string EvidenceSourceColumnId { get; init; }

    public override string ToString() =>
        $"{nameof(SpecialQuantificationResult)} {{ SpecialEvaluationId = {SpecialEvaluationId}, Score = {Score}, Content = <redacted>, EvidenceSource = {EvidenceSource}, EvidenceSourceColumnId = {EvidenceSourceColumnId} }}";
}

public sealed record SimilarityQuantificationResult
{
    public required string QuestionId { get; init; }

    public decimal Similarity { get; init; }

    public required string Reason { get; init; }

    public override string ToString() =>
        $"{nameof(SimilarityQuantificationResult)} {{ QuestionId = {QuestionId}, Similarity = {Similarity}, Content = <redacted> }}";
}
