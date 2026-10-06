using System.Collections.Immutable;
using System.Globalization;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;

namespace StudyReportEvaluator.Core.Validation;

public sealed record AuxiliaryResultValidationError(
    string Code,
    string NodeId,
    string Field,
    string SafeOffendingValue);

public sealed class AuxiliaryResultValidationOutcome<T>
    where T : class
{
    internal AuxiliaryResultValidationOutcome(
        ImmutableArray<AuxiliaryResultValidationError> errors,
        T? acceptedResult)
    {
        Errors = errors;
        AcceptedResult = acceptedResult;
    }

    public ImmutableArray<AuxiliaryResultValidationError> Errors { get; }

    public T? AcceptedResult { get; }

    public bool IsValid => Errors.IsEmpty && AcceptedResult is not null;
}

public sealed class AuxiliaryQuantificationResultValidator
{
    public AuxiliaryResultValidationOutcome<ReferenceAnswerResult> ValidateReference(
        SafeReferenceAnswerPayload expected,
        ReferenceAnswerResult submitted)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(submitted);
        ImmutableArray<AuxiliaryResultValidationError>.Builder errors =
            ImmutableArray.CreateBuilder<AuxiliaryResultValidationError>();
        ValidateExactId(errors, expected.QuestionId, submitted.QuestionId, "QuestionId");
        ValidateBody(errors, expected.QuestionId, submitted.Answer, "Answer", required: true);
        return Outcome(errors, submitted with { });
    }

    public AuxiliaryResultValidationOutcome<SpecialQuantificationResult> ValidateSpecial(
        SafeSpecialEvaluationPayload expected,
        SpecialQuantificationResult submitted)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(submitted);
        ImmutableArray<AuxiliaryResultValidationError>.Builder errors =
            ImmutableArray.CreateBuilder<AuxiliaryResultValidationError>();
        ValidateExactId(
            errors,
            expected.SpecialEvaluationId,
            submitted.SpecialEvaluationId,
            "SpecialEvaluationId");
        if (submitted.Score is < 0m or > 1m)
        {
            Add(errors, "SCORE_OUT_OF_RANGE", expected.SpecialEvaluationId, "Score", Invariant(submitted.Score));
        }

        ValidateBody(errors, expected.SpecialEvaluationId, submitted.Reason, "Reason", required: true);
        ValidateEvidence(
            errors,
            expected.SpecialEvaluationId,
            expected.PrimarySource,
            expected.SupportingSources,
            submitted.Evidence,
            submitted.EvidenceSource,
            submitted.EvidenceSourceColumnId);
        return Outcome(errors, submitted with { });
    }

    private static AuxiliaryResultValidationOutcome<T> Outcome<T>(
        ImmutableArray<AuxiliaryResultValidationError>.Builder errors,
        T submitted)
        where T : class
    {
        ImmutableArray<AuxiliaryResultValidationError> immutable = errors.ToImmutable();
        return new AuxiliaryResultValidationOutcome<T>(
            immutable,
            immutable.IsEmpty ? submitted : null);
    }

    private static void ValidateExactId(
        ImmutableArray<AuxiliaryResultValidationError>.Builder errors,
        string expected,
        string? actual,
        string field)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Add(errors, "ID_MISMATCH", expected, field, SafeId(actual));
        }
    }

    private static void ValidateBody(
        ImmutableArray<AuxiliaryResultValidationError>.Builder errors,
        string nodeId,
        string? value,
        string field,
        bool required)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            Add(errors, $"{field.ToUpperInvariant()}_REQUIRED", nodeId, field, "<blank-body>");
        }
        else if (value is not null && value.Length > QuantificationDefinitionValidator.MaximumCellCharacters)
        {
            Add(
                errors,
                "CELL_TEXT_LIMIT_EXCEEDED",
                nodeId,
                field,
                value.Length.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void ValidateEvidence(
        ImmutableArray<AuxiliaryResultValidationError>.Builder errors,
        string nodeId,
        EvaluationSourceCell primary,
        ImmutableArray<EvaluationSourceCell> supporting,
        string? evidence,
        EvidenceSourceKind sourceKind,
        string? sourceColumnId)
    {
        ValidateBody(errors, nodeId, evidence, "Evidence", required: false);
        ValidateBody(errors, nodeId, sourceColumnId, "EvidenceSourceColumnId", required: false);
        if (!Enum.IsDefined(typeof(EvidenceSourceKind), sourceKind))
        {
            Add(errors, "EVIDENCE_SOURCE_INVALID", nodeId, "EvidenceSource", ((int)sourceKind).ToString(CultureInfo.InvariantCulture));
            return;
        }

        if (sourceKind == EvidenceSourceKind.None)
        {
            if (!string.IsNullOrEmpty(evidence))
            {
                Add(errors, "NONE_EVIDENCE_MUST_BE_EMPTY", nodeId, "Evidence", "<nonempty-body>");
            }

            if (!string.IsNullOrEmpty(sourceColumnId))
            {
                Add(errors, "NONE_SOURCE_COLUMN_MUST_BE_EMPTY", nodeId, "EvidenceSourceColumnId", SafeId(sourceColumnId));
            }

            return;
        }

        EvaluationSourceCell? expectedSource = sourceKind == EvidenceSourceKind.PrimaryAnswer
            ? primary
            : supporting.SingleOrDefault(source => string.Equals(
                source.SourceColumnId,
                sourceColumnId,
                StringComparison.Ordinal));
        if (expectedSource is null)
        {
            Add(errors, "EVIDENCE_SOURCE_NOT_SENT", nodeId, "EvidenceSourceColumnId", SafeId(sourceColumnId));
            return;
        }

        if (!string.Equals(sourceColumnId, expectedSource.SourceColumnId, StringComparison.Ordinal))
        {
            Add(errors, "EVIDENCE_SOURCE_COLUMN_MISMATCH", nodeId, "EvidenceSourceColumnId", SafeId(sourceColumnId));
            return;
        }

        if (string.IsNullOrWhiteSpace(evidence))
        {
            Add(errors, "EVIDENCE_REQUIRED", nodeId, "Evidence", "<blank-body>");
        }
        else if (!expectedSource.Value.Contains(evidence, StringComparison.Ordinal))
        {
            Add(errors, "EVIDENCE_NOT_EXACT_SUBSTRING", nodeId, "Evidence", "<nonmatching-body>");
        }
    }

    private static void Add(
        ImmutableArray<AuxiliaryResultValidationError>.Builder errors,
        string code,
        string nodeId,
        string field,
        string safeOffendingValue) =>
        errors.Add(new AuxiliaryResultValidationError(code, SafeId(nodeId), field, safeOffendingValue));

    private static string SafeId(string? value) => string.IsNullOrWhiteSpace(value)
        ? "<blank>"
        : value.Length <= 64
            ? value
            : $"{value[..64]}…(length={value.Length.ToString(CultureInfo.InvariantCulture)})";

    private static string Invariant(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
