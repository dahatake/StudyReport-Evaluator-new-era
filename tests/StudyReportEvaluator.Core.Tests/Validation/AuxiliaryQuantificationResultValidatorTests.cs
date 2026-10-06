using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Validation;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Validation;

// Requirements: FR-010 (AC-011), FR-020 (AC-021)
public sealed class AuxiliaryQuantificationResultValidatorTests
{
    private readonly AuxiliaryQuantificationResultValidator validator = new();

    [Fact]
    public void Reference_requires_exact_question_id_and_nonempty_bounded_answer()
    {
        SafeReferenceAnswerPayload expected = new("Q1", "<redacted>");
        ReferenceAnswerResult valid = new() { QuestionId = "Q1", Answer = "reference" };

        AuxiliaryResultValidationOutcome<ReferenceAnswerResult> accepted =
            validator.ValidateReference(expected, valid);
        AuxiliaryResultValidationOutcome<ReferenceAnswerResult> wrong =
            validator.ValidateReference(expected, valid with { QuestionId = "q1", Answer = " " });
        AuxiliaryResultValidationOutcome<ReferenceAnswerResult> oversized =
            validator.ValidateReference(expected, valid with
            {
                Answer = new string('X', QuantificationDefinitionValidator.MaximumCellCharacters + 1),
            });

        Assert.True(accepted.IsValid);
        Assert.NotSame(valid, accepted.AcceptedResult);
        Assert.Contains(wrong.Errors, error => error.Code == "ID_MISMATCH");
        Assert.Contains(wrong.Errors, error => error.Code == "ANSWER_REQUIRED");
        Assert.Contains(oversized.Errors, error => error.Code == "CELL_TEXT_LIMIT_EXCEEDED");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.5")]
    [InlineData("1")]
    public void Special_accepts_exact_zero_to_one_boundaries_with_same_row_evidence(string scoreText)
    {
        decimal score = decimal.Parse(scoreText, System.Globalization.CultureInfo.InvariantCulture);
        SafeSpecialEvaluationPayload expected = CreateSpecialPayload();
        SpecialQuantificationResult submitted = ValidSpecial(score);

        AuxiliaryResultValidationOutcome<SpecialQuantificationResult> outcome =
            validator.ValidateSpecial(expected, submitted);

        Assert.True(outcome.IsValid);
        Assert.Equal(score, outcome.AcceptedResult!.Score);
        Assert.NotSame(submitted, outcome.AcceptedResult);
    }

    [Theory]
    [InlineData("-0.0001")]
    [InlineData("1.0001")]
    public void Special_rejects_out_of_range_score_and_nonmatching_evidence_without_leaking_content(
        string scoreText)
    {
        const string evidenceCanary = "OTHER-ROW-EVIDENCE-CANARY";
        decimal score = decimal.Parse(scoreText, System.Globalization.CultureInfo.InvariantCulture);
        SpecialQuantificationResult submitted = ValidSpecial(score) with { Evidence = evidenceCanary };

        AuxiliaryResultValidationOutcome<SpecialQuantificationResult> outcome =
            validator.ValidateSpecial(CreateSpecialPayload(), submitted);

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.AcceptedResult);
        Assert.Contains(outcome.Errors, error => error.Code == "SCORE_OUT_OF_RANGE");
        Assert.Contains(outcome.Errors, error => error.Code == "EVIDENCE_NOT_EXACT_SUBSTRING");
        Assert.DoesNotContain(evidenceCanary, string.Join('|', outcome.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Special_evidence_must_bind_to_an_exact_sent_source_or_explicit_none()
    {
        SafeSpecialEvaluationPayload expected = CreateSpecialPayload();
        SpecialQuantificationResult support = ValidSpecial(0.5m) with
        {
            Evidence = "support evidence",
            EvidenceSource = EvidenceSourceKind.SupportingColumn,
            EvidenceSourceColumnId = "K",
        };
        SpecialQuantificationResult unsent = support with { EvidenceSourceColumnId = "A" };
        SpecialQuantificationResult none = support with
        {
            Evidence = "",
            EvidenceSource = EvidenceSourceKind.None,
            EvidenceSourceColumnId = "",
        };

        Assert.True(validator.ValidateSpecial(expected, support).IsValid);
        Assert.Contains(
            validator.ValidateSpecial(expected, unsent).Errors,
            error => error.Code == "EVIDENCE_SOURCE_NOT_SENT");
        Assert.True(validator.ValidateSpecial(expected, none).IsValid);
    }

    [Fact]
    public void Auxiliary_results_redact_content_from_string_representations()
    {
        const string canary = "PRIVATE-CONTENT-CANARY";
        object[] values =
        [
            new ReferenceAnswerResult { QuestionId = "Q1", Answer = canary },
            ValidSpecial(0.5m) with { Reason = canary, Evidence = canary },
            new SimilarityQuantificationResult { QuestionId = "Q1", Similarity = 0.5m, Reason = canary },
        ];

        Assert.All(values, value => Assert.DoesNotContain(canary, value.ToString(), StringComparison.Ordinal));
    }

    private static SafeSpecialEvaluationPayload CreateSpecialPayload() => new(
        "Q1",
        "S1",
        "<redacted>",
        new EvaluationSourceCell(EvaluationSourceKind.PrimaryAnswer, "G", "primary evidence is here"),
        [new EvaluationSourceCell(EvaluationSourceKind.SupportingColumn, "K", "support evidence is here")]);

    private static SpecialQuantificationResult ValidSpecial(decimal score) => new()
    {
        SpecialEvaluationId = "S1",
        Score = score,
        Reason = "reason",
        Evidence = "primary evidence",
        EvidenceSource = EvidenceSourceKind.PrimaryAnswer,
        EvidenceSourceColumnId = "G",
    };
}
