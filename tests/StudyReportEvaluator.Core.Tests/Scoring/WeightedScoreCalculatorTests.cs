using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Scoring;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Scoring;

// Requirements: FR-009 (AC-010), FR-010 (AC-011)
public sealed class WeightedScoreCalculatorTests
{
    private readonly WeightedScoreCalculator _calculator = new();

    [Fact]
    public void Hand_calculated_criterion_and_evaluator_oracle_matches_rounded_child_contract()
    {
        decimal? criterionA = _calculator.Normalize(25m, new ScoreRange(0m, 30m), 1);
        decimal? criterionB = _calculator.Normalize(8m, new ScoreRange(1m, 10m), 1);
        decimal? evaluator = _calculator.Aggregate(
            [new WeightedScoreInput(criterionA, 2m), new WeightedScoreInput(criterionB, 1m)],
            1);

        Assert.Equal(83.3m, criterionA);
        Assert.Equal(77.8m, criterionB);
        Assert.Equal(81.5m, evaluator);
    }

    [Fact]
    public void Empty_primary_is_unscorable_and_override_cannot_create_a_score()
    {
        EffectiveRawSelection selection = _calculator.SelectEffectiveRaw(
            scorable: false,
            aiRaw: 7m,
            overrideValue: 5m,
            new ScoreRange(0m, 10m));

        Assert.Equal(EffectiveRawStatus.Unscorable, selection.Status);
        Assert.Null(selection.Value);
        Assert.Null(_calculator.Normalize(selection.Value, new ScoreRange(0m, 10m), 1));
    }

    [Fact]
    public void Missing_ai_and_override_remain_blank_never_zero()
    {
        EffectiveRawSelection selection = _calculator.SelectEffectiveRaw(true, null, null, new ScoreRange(0m, 10m));

        Assert.Equal(EffectiveRawStatus.Missing, selection.Status);
        Assert.Null(selection.Value);
        Assert.Null(_calculator.Aggregate([new WeightedScoreInput(null, 1m)], 1));
    }

    [Fact]
    public void Valid_override_wins_even_when_ai_is_invalid_or_missing()
    {
        EffectiveRawSelection invalidAi = _calculator.SelectEffectiveRaw(true, 11m, 5m, new ScoreRange(0m, 10m));
        EffectiveRawSelection missingAi = _calculator.SelectEffectiveRaw(true, null, 5m, new ScoreRange(0m, 10m));

        Assert.Equal(new EffectiveRawSelection(5m, EffectiveRawStatus.Override), invalidAi);
        Assert.Equal(new EffectiveRawSelection(5m, EffectiveRawStatus.Override), missingAi);
        Assert.Equal(50.0m, _calculator.Normalize(invalidAi.Value, new ScoreRange(0m, 10m), 1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void Invalid_override_stays_blank_and_never_falls_back_to_ai(int overrideValue)
    {
        EffectiveRawSelection selection = _calculator.SelectEffectiveRaw(true, 5m, overrideValue, new ScoreRange(0m, 10m));

        Assert.Equal(EffectiveRawStatus.InvalidOverride, selection.Status);
        Assert.Null(selection.Value);
    }

    [Fact]
    public void Disabled_children_are_excluded_but_enabled_blank_propagates()
    {
        decimal? excludesDisabled = _calculator.Aggregate(
            [new WeightedScoreInput(80m, 1m), new WeightedScoreInput(null, 999m, Enabled: false)],
            1);
        decimal? propagatesEnabledBlank = _calculator.Aggregate(
            [new WeightedScoreInput(80m, 1m), new WeightedScoreInput(null, 1m)],
            1);

        Assert.Equal(80m, excludesDisabled);
        Assert.Null(propagatesEnabledBlank);
    }

    [Fact]
    public void Asymmetric_weights_are_normalized_without_requiring_a_total_of_one_hundred()
    {
        decimal? result = _calculator.Aggregate(
            [new WeightedScoreInput(20m, 2m), new WeightedScoreInput(80m, 8m)],
            1);

        Assert.Equal(68m, result);
    }

    [Theory]
    [InlineData(81.45, 81.5)]
    [InlineData(-81.45, -81.5)]
    public void Midpoints_round_away_from_zero(double input, double expected)
    {
        Assert.Equal((decimal)expected, _calculator.Round((decimal)input, 1));
    }

    [Fact]
    public void Very_large_weights_do_not_overflow_the_preview()
    {
        decimal? result = _calculator.Aggregate(
            [new WeightedScoreInput(50m, decimal.MaxValue), new WeightedScoreInput(100m, decimal.MaxValue)],
            1);

        Assert.Equal(75m, result);
    }

    [Fact]
    public void Extreme_but_valid_decimal_range_does_not_overflow_normalization()
    {
        decimal? result = _calculator.Normalize(
            0m,
            new ScoreRange(decimal.MinValue, decimal.MaxValue),
            1);

        Assert.Equal(50.0m, result);
    }

    [Fact]
    public void V4_absolute_points_similarity_and_special_hand_oracle_matches_excel_stages()
    {
        decimal? firstRate = _calculator.QuestionRate(answerPresent: true, 100m);
        decimal? secondRate = _calculator.QuestionRate(answerPresent: true, 50m);
        decimal? firstEarned = _calculator.QuestionEarned(firstRate, 15m, 2);
        decimal? secondEarned = _calculator.QuestionEarned(secondRate, 15m, 2);
        decimal? firstSpecial = _calculator.SpecialQuestionRate([0.8m, 0.8m], 2);
        decimal? secondSpecial = _calculator.SpecialQuestionRate([0.6m], 2);
        decimal? specialEarned = _calculator.SpecialEarned(10m, [firstSpecial, secondSpecial], 2);
        decimal? firstPenalty = _calculator.SimilarityPenalty(15m, 0.99m, 0.1m, 2);
        decimal? secondPenalty = _calculator.SimilarityPenalty(15m, 0.1m, 0.1m, 2);
        decimal? finalRaw = _calculator.FinalRaw(
            60m,
            [firstEarned, secondEarned],
            specialEarned,
            [firstPenalty, secondPenalty],
            2);

        Assert.Equal(1m, firstRate);
        Assert.Equal(0.5m, secondRate);
        Assert.Equal(15m, firstEarned);
        Assert.Equal(7.5m, secondEarned);
        Assert.Equal(0.8m, firstSpecial);
        Assert.Equal(0.6m, secondSpecial);
        Assert.Equal(7m, specialEarned);
        Assert.Equal(1.49m, firstPenalty);
        Assert.Equal(0.15m, secondPenalty);
        Assert.Equal(87.86m, finalRaw);
        Assert.Equal(87.86m, WeightedScoreCalculator.FinalScore(finalRaw));
    }

    [Fact]
    public void V4_system_prompt_hand_oracle_produces_86_point_8()
    {
        decimal? firstRate = _calculator.QuestionRate(answerPresent: true, 80m);
        decimal? secondRate = _calculator.QuestionRate(answerPresent: true, 50m);
        decimal? firstEarned = _calculator.QuestionEarned(firstRate, 20m, 1);
        decimal? secondEarned = _calculator.QuestionEarned(secondRate, 10m, 1);
        decimal? firstSpecial = _calculator.SpecialQuestionRate([0.8m], 1);
        decimal? secondSpecial = _calculator.SpecialQuestionRate([0.6m], 1);
        decimal? specialEarned = _calculator.SpecialEarned(10m, [firstSpecial, secondSpecial], 1);
        decimal? firstPenalty = _calculator.SimilarityPenalty(20m, 0.5m, 0.1m, 1);
        decimal? secondPenalty = _calculator.SimilarityPenalty(10m, 0.2m, 0.1m, 1);
        decimal? finalRaw = _calculator.FinalRaw(
            60m,
            [firstEarned, secondEarned],
            specialEarned,
            [firstPenalty, secondPenalty],
            1);

        Assert.Equal(0.8m, firstRate);
        Assert.Equal(0.5m, secondRate);
        Assert.Equal(16m, firstEarned);
        Assert.Equal(5m, secondEarned);
        Assert.Equal(0.8m, firstSpecial);
        Assert.Equal(0.6m, secondSpecial);
        Assert.Equal(7m, specialEarned);
        Assert.Equal(1m, firstPenalty);
        Assert.Equal(0.2m, secondPenalty);
        Assert.Equal(86.8m, finalRaw);
        Assert.Equal(86.8m, WeightedScoreCalculator.FinalScore(finalRaw));
    }

    [Fact]
    public void Empty_input_becomes_zero_while_technical_missing_remains_blank()
    {
        decimal? emptyRate = _calculator.QuestionRate(answerPresent: false, questionNormalized: null);
        decimal? failedRate = _calculator.QuestionRate(answerPresent: true, questionNormalized: null);

        Assert.Equal(0m, emptyRate);
        Assert.Equal(0m, _calculator.QuestionEarned(emptyRate, 20m, 1));
        Assert.Null(failedRate);
        Assert.Null(_calculator.QuestionEarned(failedRate, 20m, 1));
        Assert.Equal(0m, _calculator.SpecialQuestionRate([0m], 1));
        Assert.Null(_calculator.SpecialQuestionRate([0m, null], 1));
        Assert.Equal(0m, _calculator.SpecialEarned(0m, [], 1));
        Assert.Null(_calculator.SpecialEarned(10m, [0.5m, null], 1));
        Assert.Equal(0m, _calculator.SimilarityPenalty(20m, 0m, 0.1m, 1));
        Assert.Null(_calculator.SimilarityPenalty(20m, null, 0.1m, 1));
        Assert.Null(_calculator.FinalRaw(60m, [20m, null], 0m, [0m, 0m], 1));
    }

    [Theory]
    [InlineData(-0.01, 0)]
    [InlineData(0, 0)]
    [InlineData(99.99, 99.99)]
    [InlineData(100, 100)]
    [InlineData(100.01, 100)]
    public void Final_score_clamps_only_the_displayed_score(double raw, double expected)
    {
        Assert.Equal((decimal)expected, WeightedScoreCalculator.FinalScore((decimal)raw));
    }

    [Fact]
    public void V4_preview_rejects_out_of_range_values_and_decimal_overflow_as_blank()
    {
        Assert.Null(_calculator.QuestionRate(true, -0.1m));
        Assert.Null(_calculator.QuestionRate(true, 100.1m));
        Assert.Null(_calculator.QuestionEarned(1.1m, 20m, 1));
        Assert.Null(_calculator.SpecialQuestionRate([1.1m], 1));
        Assert.Null(_calculator.SpecialEarned(101m, [1m], 1));
        Assert.Null(_calculator.SimilarityPenalty(20m, 1.1m, 0.1m, 1));
        Assert.Null(_calculator.SimilarityPenalty(decimal.MaxValue, 1m, 1m, 1));
        Assert.Null(_calculator.FinalRaw(60m, [decimal.MaxValue], 100m, [0m], 1));
        Assert.Null(WeightedScoreCalculator.FinalScore(null));
    }
}
