using StudyReportEvaluator.Core.Scoring;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Scoring;

// Requirements: FR-012 (AC-013), FR-013 (AC-014)
public sealed class ScoringAllocationCalculatorTests
{
    private readonly ScoringAllocationCalculator calculator = new();

    [Theory]
    [InlineData("60", "0", 1, "40")]
    [InlineData("60", "0", 2, "20,20")]
    [InlineData("60", "10", 2, "15,15")]
    [InlineData("60", "0", 3, "13.333333,13.333333,13.333334")]
    [InlineData("99.9999985", "0", 3, "0,0,0.0000015")]
    public void Equalize_preserves_an_exact_total_and_puts_the_remainder_last(
        string baseText,
        string specialText,
        int questionCount,
        string expectedText)
    {
        decimal basePoints = decimal.Parse(baseText, System.Globalization.CultureInfo.InvariantCulture);
        decimal specialPoints = decimal.Parse(specialText, System.Globalization.CultureInfo.InvariantCulture);
        decimal[] expected = expectedText
            .Split(',')
            .Select(value => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        var result = calculator.Equalize(basePoints, specialPoints, questionCount);

        Assert.Equal(expected, result);
        Assert.Equal(100m, basePoints + specialPoints + result.Sum());
        Assert.All(result, value => Assert.True(value >= 0m));
    }

    [Theory]
    [InlineData("60", "0", new[] { "20", "20" }, true, "100")]
    [InlineData("60", "10", new[] { "15", "15" }, true, "100")]
    [InlineData("60", "0", new[] { "30", "10" }, true, "100")]
    [InlineData("60", "0", new[] { "20", "19.99" }, false, "99.99")]
    public void Validate_requires_an_exact_total_of_100(
        string baseText,
        string specialText,
        string[] pointTexts,
        bool expectedValid,
        string expectedTotalText)
    {
        decimal basePoints = decimal.Parse(baseText, System.Globalization.CultureInfo.InvariantCulture);
        decimal specialPoints = decimal.Parse(specialText, System.Globalization.CultureInfo.InvariantCulture);
        decimal[] points = pointTexts
            .Select(value => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        ScoringAllocationValidationResult result = calculator.Validate(basePoints, specialPoints, points);

        Assert.Equal(expectedValid, result.IsValid);
        Assert.Equal(
            decimal.Parse(expectedTotalText, System.Globalization.CultureInfo.InvariantCulture),
            result.Total);
    }

    [Fact]
    public void Validate_rejects_missing_questions_negative_points_and_overflow_without_throwing()
    {
        Assert.False(calculator.Validate(60m, 0m, []).IsValid);
        Assert.False(calculator.Validate(60m, 0m, [-1m, 41m]).IsValid);
        Assert.False(calculator.Validate(60m, 0m, [decimal.MaxValue]).IsValid);
    }

    [Theory]
    [InlineData("-0.1", "0", 1)]
    [InlineData("100.1", "0", 1)]
    [InlineData("60", "40.1", 1)]
    public void Equalize_rejects_invalid_root_allocation(
        string baseText,
        string specialText,
        int questionCount)
    {
        decimal basePoints = decimal.Parse(baseText, System.Globalization.CultureInfo.InvariantCulture);
        decimal specialPoints = decimal.Parse(specialText, System.Globalization.CultureInfo.InvariantCulture);

        Assert.ThrowsAny<ArgumentException>(() => calculator.Equalize(basePoints, specialPoints, questionCount));
    }
}
