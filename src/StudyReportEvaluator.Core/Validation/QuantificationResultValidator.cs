using System.Collections.Immutable;
using System.Globalization;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;

namespace StudyReportEvaluator.Core.Validation;

public sealed record QuantificationResultValidationError(
    string Code,
    string EvaluatorId,
    string CriterionId,
    string Field,
    string SafeOffendingValue);

public sealed class QuantificationResultValidationOutcome
{
    internal QuantificationResultValidationOutcome(
        ImmutableArray<QuantificationResultValidationError> errors,
        QuantificationResult? acceptedResult)
    {
        Errors = errors;
        AcceptedResult = acceptedResult;
    }

    public ImmutableArray<QuantificationResultValidationError> Errors { get; }

    public QuantificationResult? AcceptedResult { get; }

    public bool IsValid => Errors.IsEmpty && AcceptedResult is not null;
}

public sealed class QuantificationResultValidator
{
    public QuantificationResultValidationOutcome Validate(
        SafeEvaluationPayload expected,
        QuantificationResult submitted)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(submitted);

        ImmutableArray<QuantificationResultValidationError>.Builder errors =
            ImmutableArray.CreateBuilder<QuantificationResultValidationError>();
        if (!string.Equals(expected.EvaluatorId, submitted.EvaluatorId, StringComparison.Ordinal))
        {
            Add(errors, "EVALUATOR_ID_MISMATCH", expected.EvaluatorId, "<result>", "EvaluatorId", submitted.EvaluatorId);
        }

        ImmutableArray<CriterionQuantificationResult> submittedCriteria =
            submitted.Criteria.IsDefault ? [] : submitted.Criteria;
        Dictionary<string, ExpectedCriterion> expectedById = expected.ExpectedCriteria.ToDictionary(
            criterion => criterion.CriterionId,
            StringComparer.Ordinal);
        Dictionary<string, CriterionQuantificationResult> submittedById = new(StringComparer.Ordinal);
        foreach (CriterionQuantificationResult? criterion in submittedCriteria)
        {
            if (criterion is null)
            {
                Add(errors, "NULL_CRITERION_RESULT", expected.EvaluatorId, "<null>", "Criteria", "<null>");
                continue;
            }

            if (string.IsNullOrWhiteSpace(criterion.CriterionId))
            {
                Add(errors, "CRITERION_ID_REQUIRED", expected.EvaluatorId, "<blank>", "CriterionId", "<blank>");
                continue;
            }

            if (!submittedById.TryAdd(criterion.CriterionId, criterion))
            {
                Add(errors, "DUPLICATE_CRITERION", expected.EvaluatorId, criterion.CriterionId, "CriterionId", criterion.CriterionId);
                continue;
            }

            if (!expectedById.TryGetValue(criterion.CriterionId, out ExpectedCriterion? expectedCriterion))
            {
                Add(errors, "UNKNOWN_CRITERION", expected.EvaluatorId, criterion.CriterionId, "CriterionId", criterion.CriterionId);
                continue;
            }

            ValidateCriterion(expected, expectedCriterion, criterion, errors);
        }

        foreach (ExpectedCriterion missing in expected.ExpectedCriteria.Where(
                     expectedCriterion => !submittedById.ContainsKey(expectedCriterion.CriterionId)))
        {
            Add(errors, "MISSING_CRITERION", expected.EvaluatorId, missing.CriterionId, "Criteria", "missing");
        }

        if (submittedCriteria.Length != expected.ExpectedCriteria.Length)
        {
            Add(
                errors,
                "CRITERION_COUNT_MISMATCH",
                expected.EvaluatorId,
                "<result>",
                "Criteria.Count",
                $"expected={expected.ExpectedCriteria.Length.ToString(CultureInfo.InvariantCulture)},actual={submittedCriteria.Length.ToString(CultureInfo.InvariantCulture)}");
        }

        ImmutableArray<QuantificationResultValidationError> immutableErrors = errors.ToImmutable();
        return new QuantificationResultValidationOutcome(
            immutableErrors,
            immutableErrors.IsEmpty ? Freeze(submitted) : null);
    }

    private static void ValidateCriterion(
        SafeEvaluationPayload expectedPayload,
        ExpectedCriterion expectedCriterion,
        CriterionQuantificationResult submitted,
        ImmutableArray<QuantificationResultValidationError>.Builder errors)
    {
        if (submitted.RawScore < expectedCriterion.Range.Minimum
            || submitted.RawScore > expectedCriterion.Range.Maximum)
        {
            Add(
                errors,
                "RAW_SCORE_OUT_OF_RANGE",
                expectedPayload.EvaluatorId,
                submitted.CriterionId,
                "RawScore",
                Invariant(submitted.RawScore));
        }

        if (string.IsNullOrWhiteSpace(submitted.Reason))
        {
            Add(errors, "REASON_REQUIRED", expectedPayload.EvaluatorId, submitted.CriterionId, "Reason", "<blank-body>");
        }

        if (!Enum.IsDefined(typeof(EvidenceSourceKind), submitted.EvidenceSource))
        {
            Add(
                errors,
                "EVIDENCE_SOURCE_INVALID",
                expectedPayload.EvaluatorId,
                submitted.CriterionId,
                "EvidenceSource",
                ((int)submitted.EvidenceSource).ToString(CultureInfo.InvariantCulture));
            return;
        }

        switch (submitted.EvidenceSource)
        {
            case EvidenceSourceKind.None:
                if (!string.IsNullOrEmpty(submitted.Evidence))
                {
                    Add(errors, "NONE_EVIDENCE_MUST_BE_EMPTY", expectedPayload.EvaluatorId, submitted.CriterionId, "Evidence", "<nonempty-body>");
                }

                if (!string.IsNullOrEmpty(submitted.EvidenceSourceColumnId))
                {
                    Add(errors, "NONE_SOURCE_COLUMN_MUST_BE_EMPTY", expectedPayload.EvaluatorId, submitted.CriterionId, "EvidenceSourceColumnId", SafeId(submitted.EvidenceSourceColumnId));
                }

                break;

            case EvidenceSourceKind.PrimaryAnswer:
                ValidateEvidenceBinding(
                    expectedPayload,
                    submitted,
                    expectedPayload.PrimarySource,
                    EvidenceSourceKind.PrimaryAnswer,
                    errors);
                break;

            case EvidenceSourceKind.SupportingColumn:
                EvaluationSourceCell? supporting = expectedPayload.SupportingSources.SingleOrDefault(
                    source => string.Equals(source.SourceColumnId, submitted.EvidenceSourceColumnId, StringComparison.Ordinal));
                if (supporting is null)
                {
                    Add(errors, "SUPPORTING_SOURCE_NOT_SENT", expectedPayload.EvaluatorId, submitted.CriterionId, "EvidenceSourceColumnId", SafeId(submitted.EvidenceSourceColumnId));
                    break;
                }

                ValidateEvidenceBinding(
                    expectedPayload,
                    submitted,
                    supporting,
                    EvidenceSourceKind.SupportingColumn,
                    errors);
                break;
        }
    }

    private static void ValidateEvidenceBinding(
        SafeEvaluationPayload expectedPayload,
        CriterionQuantificationResult submitted,
        EvaluationSourceCell expectedSource,
        EvidenceSourceKind expectedKind,
        ImmutableArray<QuantificationResultValidationError>.Builder errors)
    {
        if (!string.Equals(submitted.EvidenceSourceColumnId, expectedSource.SourceColumnId, StringComparison.Ordinal))
        {
            Add(errors, "EVIDENCE_SOURCE_COLUMN_MISMATCH", expectedPayload.EvaluatorId, submitted.CriterionId, "EvidenceSourceColumnId", SafeId(submitted.EvidenceSourceColumnId));
            return;
        }

        EvaluationSourceKind payloadKind = expectedKind == EvidenceSourceKind.PrimaryAnswer
            ? EvaluationSourceKind.PrimaryAnswer
            : EvaluationSourceKind.SupportingColumn;
        if (expectedSource.Kind != payloadKind)
        {
            Add(errors, "EVIDENCE_SOURCE_KIND_MISMATCH", expectedPayload.EvaluatorId, submitted.CriterionId, "EvidenceSource", submitted.EvidenceSource.ToString());
            return;
        }

        if (string.IsNullOrWhiteSpace(submitted.Evidence))
        {
            Add(errors, "EVIDENCE_REQUIRED", expectedPayload.EvaluatorId, submitted.CriterionId, "Evidence", "<blank-body>");
            return;
        }

        if (!expectedSource.Value.Contains(submitted.Evidence, StringComparison.Ordinal))
        {
            Add(errors, "EVIDENCE_NOT_EXACT_SUBSTRING", expectedPayload.EvaluatorId, submitted.CriterionId, "Evidence", "<nonmatching-body>");
        }
    }

    private static QuantificationResult Freeze(QuantificationResult submitted) => submitted with
    {
        Criteria = (submitted.Criteria.IsDefault ? [] : submitted.Criteria)
            .Select(criterion => criterion with { })
            .ToImmutableArray(),
    };

    private static void Add(
        ImmutableArray<QuantificationResultValidationError>.Builder errors,
        string code,
        string evaluatorId,
        string criterionId,
        string field,
        string safeOffendingValue) =>
        errors.Add(new QuantificationResultValidationError(
            code,
            SafeId(evaluatorId),
            SafeId(criterionId),
            field,
            safeOffendingValue));

    private static string SafeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<blank>";
        }

        return value.Length <= 64
            ? value
            : $"{value[..64]}…(length={value.Length.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string Invariant(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
