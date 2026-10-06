using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Tests.Domain;
using StudyReportEvaluator.Core.Tests.Validation;
using StudyReportEvaluator.Core.Validation;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Validation;

// Requirements: FR-019 (AC-020)
public sealed class QuantificationResultValidatorTests
{
    private readonly QuantificationResultValidator _validator = new();

    [Fact]
    public void Complete_exact_result_is_accepted_as_a_deep_copy()
    {
        SafeEvaluationPayload payload = CreatePayload();
        QuantificationResult submitted = ValidResult();

        QuantificationResultValidationOutcome outcome = _validator.Validate(payload, submitted);

        Assert.True(outcome.IsValid);
        Assert.Empty(outcome.Errors);
        QuantificationResult accepted = Assert.IsType<QuantificationResult>(outcome.AcceptedResult);
        Assert.NotSame(submitted, accepted);
        Assert.NotSame(submitted.Criteria[0], accepted.Criteria[0]);
        Assert.Equal(5m, accepted.Criteria[0].RawScore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Raw_score_exact_range_boundaries_are_accepted(int score)
    {
        QuantificationResult result = ValidResult() with
        {
            Criteria = [ResultTestData.Criterion(rawScore: score)],
        };

        Assert.True(_validator.Validate(CreatePayload(), result).IsValid);
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(10.0001)]
    public void Raw_score_outside_range_by_epsilon_rejects_the_whole_payload(double score)
    {
        QuantificationResult result = ValidResult() with
        {
            Criteria = [ResultTestData.Criterion(rawScore: (decimal)score)],
        };

        QuantificationResultValidationOutcome outcome = _validator.Validate(CreatePayload(), result);

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.AcceptedResult);
        Assert.Contains(outcome.Errors, error => error.Code == "RAW_SCORE_OUT_OF_RANGE" && error.CriterionId == "C1");
    }

    [Theory]
    [MemberData(nameof(InvalidCriterionSets))]
    public void Missing_duplicate_unknown_and_partial_sets_reject_every_result(
        CriterionQuantificationResult[] criteria,
        string expectedCode)
    {
        QuantificationResult submitted = new() { EvaluatorId = "E1", Criteria = [.. criteria] };

        QuantificationResultValidationOutcome outcome = _validator.Validate(CreatePayload(), submitted);

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.AcceptedResult);
        Assert.Contains(outcome.Errors, error => error.Code == expectedCode);
    }

    public static TheoryData<CriterionQuantificationResult[], string> InvalidCriterionSets => new()
    {
        { [], "MISSING_CRITERION" },
        { [ResultTestData.Criterion(), ResultTestData.Criterion()], "DUPLICATE_CRITERION" },
        { [ResultTestData.Criterion(id: "UNKNOWN")], "UNKNOWN_CRITERION" },
    };

    [Fact]
    public void Evaluator_id_must_match_exactly()
    {
        QuantificationResultValidationOutcome outcome = _validator.Validate(
            CreatePayload(),
            ValidResult() with { EvaluatorId = "e1" });

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.AcceptedResult);
        Assert.Contains(outcome.Errors, error => error.Code == "EVALUATOR_ID_MISMATCH");
    }

    [Fact]
    public void Primary_evidence_must_bind_to_exact_primary_id_and_contiguous_substring()
    {
        QuantificationResult wrongColumn = ValidResult() with
        {
            Criteria = [ResultTestData.Criterion(sourceColumn: "K")],
        };
        QuantificationResult wrongText = ValidResult() with
        {
            Criteria = [ResultTestData.Criterion(evidence: "evidence primary")],
        };

        QuantificationResultValidationOutcome columnOutcome = _validator.Validate(CreatePayload(), wrongColumn);
        QuantificationResultValidationOutcome textOutcome = _validator.Validate(CreatePayload(), wrongText);

        Assert.Contains(columnOutcome.Errors, error => error.Code == "EVIDENCE_SOURCE_COLUMN_MISMATCH");
        Assert.Contains(textOutcome.Errors, error => error.Code == "EVIDENCE_NOT_EXACT_SUBSTRING");
        Assert.Null(columnOutcome.AcceptedResult);
        Assert.Null(textOutcome.AcceptedResult);
    }

    [Fact]
    public void Supporting_evidence_must_bind_to_the_identified_actually_sent_same_row_cell()
    {
        SafeEvaluationPayload payload = CreatePayload();
        QuantificationResult valid = ValidResult() with
        {
            Criteria =
            [
                ResultTestData.Criterion(
                    source: EvidenceSourceKind.SupportingColumn,
                    sourceColumn: "K",
                    evidence: "support K"),
            ],
        };
        QuantificationResult foundOnlyInOtherSentCell = valid with
        {
            Criteria = [valid.Criteria[0] with { Evidence = "support L" }],
        };
        QuantificationResult unsentColumn = valid with
        {
            Criteria = [valid.Criteria[0] with { EvidenceSourceColumnId = "A" }],
        };

        Assert.True(_validator.Validate(payload, valid).IsValid);
        Assert.Contains(
            _validator.Validate(payload, foundOnlyInOtherSentCell).Errors,
            error => error.Code == "EVIDENCE_NOT_EXACT_SUBSTRING");
        Assert.Contains(
            _validator.Validate(payload, unsentColumn).Errors,
            error => error.Code == "SUPPORTING_SOURCE_NOT_SENT");
    }

    [Fact]
    public void Evidence_found_only_in_an_unsent_or_different_row_cell_is_rejected()
    {
        const string otherRowOrColumnEvidence = "other private evidence";
        SafeEvaluationPayload payload = CreatePayload();
        QuantificationResult submitted = ValidResult() with
        {
            Criteria = [ResultTestData.Criterion(evidence: otherRowOrColumnEvidence)],
        };

        QuantificationResultValidationOutcome outcome = _validator.Validate(payload, submitted);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Errors, error => error.Code == "EVIDENCE_NOT_EXACT_SUBSTRING");
        Assert.DoesNotContain(otherRowOrColumnEvidence, string.Join("|", outcome.Errors), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("evidence", "", "NONE_EVIDENCE_MUST_BE_EMPTY")]
    [InlineData("  ", "", "NONE_EVIDENCE_MUST_BE_EMPTY")]
    [InlineData("", "G", "NONE_SOURCE_COLUMN_MUST_BE_EMPTY")]
    [InlineData("", "\t", "NONE_SOURCE_COLUMN_MUST_BE_EMPTY")]
    public void None_requires_empty_evidence_and_empty_source_id(
        string evidence,
        string sourceColumn,
        string expectedCode)
    {
        QuantificationResult submitted = ValidResult() with
        {
            Criteria =
            [
                ResultTestData.Criterion(
                    source: EvidenceSourceKind.None,
                    sourceColumn: sourceColumn,
                    evidence: evidence),
            ],
        };

        QuantificationResultValidationOutcome outcome = _validator.Validate(CreatePayload(), submitted);

        Assert.Contains(outcome.Errors, error => error.Code == expectedCode);
        Assert.Null(outcome.AcceptedResult);
    }

    [Fact]
    public void Valid_none_source_is_accepted()
    {
        QuantificationResult submitted = ValidResult() with
        {
            Criteria = [ResultTestData.Criterion(source: EvidenceSourceKind.None, sourceColumn: "", evidence: "")],
        };

        Assert.True(_validator.Validate(CreatePayload(), submitted).IsValid);
    }

    [Fact]
    public void Whitespace_only_evidence_cannot_claim_a_primary_source()
    {
        SafeEvaluationPayload payload = new(
            "Q1",
            "E1",
            "<redacted-test-prompt>",
            new EvaluationSourceCell(EvaluationSourceKind.PrimaryAnswer, "G", "primary  answer"),
            [],
            [new ExpectedCriterion("C1", "Criterion", new ScoreRange(0m, 10m))]);
        QuantificationResult submitted = ValidResult() with
        {
            Criteria = [ResultTestData.Criterion(evidence: "  ")],
        };

        QuantificationResultValidationOutcome outcome = _validator.Validate(payload, submitted);

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.AcceptedResult);
        Assert.Contains(outcome.Errors, error => error.Code == "EVIDENCE_REQUIRED");
    }

    [Fact]
    public void Blank_reason_and_evidence_are_not_leaked_in_errors()
    {
        const string privateEvidence = "PRIVATE-EVIDENCE-CANARY";
        QuantificationResult submitted = ValidResult() with
        {
            Criteria =
            [
                ResultTestData.Criterion(evidence: privateEvidence) with { Reason = "" },
            ],
        };

        QuantificationResultValidationOutcome outcome = _validator.Validate(CreatePayload(), submitted);
        string rendered = string.Join("|", outcome.Errors.Select(error => error.ToString()));

        Assert.False(outcome.IsValid);
        Assert.DoesNotContain(privateEvidence, rendered, StringComparison.Ordinal);
        Assert.Contains(outcome.Errors, error => error.Code == "REASON_REQUIRED");
    }

    private static SafeEvaluationPayload CreatePayload() => new(
        "Q1",
        "E1",
        "<redacted-test-prompt>",
        new EvaluationSourceCell(EvaluationSourceKind.PrimaryAnswer, "G", "primary evidence is here"),
        [
            new EvaluationSourceCell(EvaluationSourceKind.SupportingColumn, "K", "support K is here"),
            new EvaluationSourceCell(EvaluationSourceKind.SupportingColumn, "L", "support L is here"),
        ],
        [new ExpectedCriterion("C1", "Criterion", new ScoreRange(0m, 10m))]);

    private static QuantificationResult ValidResult() => new()
    {
        EvaluatorId = "E1",
        Criteria = [ResultTestData.Criterion()],
    };
}
