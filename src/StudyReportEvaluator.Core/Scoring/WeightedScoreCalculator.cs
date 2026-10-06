using System.Collections.Immutable;
using System.Numerics;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.Core.Scoring;

public enum EffectiveRawStatus
{
    Unscorable,
    AiRaw,
    Override,
    Missing,
    InvalidAiRaw,
    InvalidOverride,
}

public readonly record struct EffectiveRawSelection(decimal? Value, EffectiveRawStatus Status);

public readonly record struct WeightedScoreInput(decimal? Score, decimal Weight, bool Enabled = true);

public sealed class WeightedScoreCalculator
{
    public EffectiveRawSelection SelectEffectiveRaw(
        bool scorable,
        decimal? aiRaw,
        decimal? overrideValue,
        ScoreRange range)
    {
        if (!scorable)
        {
            return new EffectiveRawSelection(null, EffectiveRawStatus.Unscorable);
        }

        if (overrideValue is decimal manual)
        {
            return IsInRange(manual, range)
                ? new EffectiveRawSelection(manual, EffectiveRawStatus.Override)
                : new EffectiveRawSelection(null, EffectiveRawStatus.InvalidOverride);
        }

        if (aiRaw is decimal ai)
        {
            return IsInRange(ai, range)
                ? new EffectiveRawSelection(ai, EffectiveRawStatus.AiRaw)
                : new EffectiveRawSelection(null, EffectiveRawStatus.InvalidAiRaw);
        }

        return new EffectiveRawSelection(null, EffectiveRawStatus.Missing);
    }

    public decimal? Normalize(decimal? effectiveRaw, ScoreRange range, int roundingDigits)
    {
        ValidateRoundingDigits(roundingDigits);
        if (effectiveRaw is not decimal raw || !IsInRange(raw, range) || range.Minimum >= range.Maximum)
        {
            return null;
        }

        return NormalizeExactly(raw, range, roundingDigits);
    }

    public decimal? Aggregate(IEnumerable<WeightedScoreInput> children, int roundingDigits)
    {
        ArgumentNullException.ThrowIfNull(children);
        ValidateRoundingDigits(roundingDigits);

        ImmutableArray<WeightedScoreInput> enabled = children.Where(child => child.Enabled).ToImmutableArray();
        if (enabled.IsEmpty || enabled.Any(child => child.Score is null))
        {
            return null;
        }

        foreach (WeightedScoreInput child in enabled)
        {
            if (child.Weight <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(children), child.Weight, "Enabled child weights must be positive.");
            }
        }

        decimal maximumWeight = enabled.Max(child => child.Weight);
        decimal scaledWeightTotal = 0m;
        decimal scaledWeightedScoreTotal = 0m;
        foreach (WeightedScoreInput child in enabled)
        {
            decimal scaledWeight = child.Weight / maximumWeight;
            scaledWeightTotal += scaledWeight;
            scaledWeightedScoreTotal += child.Score!.Value * scaledWeight;
        }

        return Round(scaledWeightedScoreTotal / scaledWeightTotal, roundingDigits);
    }

    public decimal? QuestionRate(bool answerPresent, decimal? questionNormalized)
    {
        if (!answerPresent)
        {
            return 0m;
        }

        return questionNormalized is decimal normalized && normalized is >= 0m and <= 100m
            ? normalized / 100m
            : null;
    }

    public decimal? QuestionEarned(decimal? questionRate, decimal questionPoints, int roundingDigits)
    {
        ValidateRoundingDigits(roundingDigits);
        if (questionRate is not decimal rate
            || rate is < 0m or > 1m
            || questionPoints is < 0m or > 100m)
        {
            return null;
        }

        return TryRoundProduct(rate, questionPoints, roundingDigits);
    }

    public decimal? SpecialQuestionRate(IEnumerable<decimal?> scores, int roundingDigits)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ValidateRoundingDigits(roundingDigits);
        ImmutableArray<decimal?> values = scores.ToImmutableArray();
        if (values.IsEmpty
            || values.Any(value => value is null or < 0m or > 1m))
        {
            return null;
        }

        try
        {
            return Round(values.Sum(value => value!.Value) / values.Length, roundingDigits);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public decimal? SpecialEarned(
        decimal specialPoints,
        IEnumerable<decimal?> specialQuestionRates,
        int roundingDigits)
    {
        ArgumentNullException.ThrowIfNull(specialQuestionRates);
        ValidateRoundingDigits(roundingDigits);
        if (specialPoints is < 0m or > 100m)
        {
            return null;
        }

        if (specialPoints == 0m)
        {
            return 0m;
        }

        ImmutableArray<decimal?> rates = specialQuestionRates.ToImmutableArray();
        if (rates.IsEmpty || rates.Any(rate => rate is null or < 0m or > 1m))
        {
            return null;
        }

        try
        {
            decimal average = rates.Sum(rate => rate!.Value) / rates.Length;
            return Round(specialPoints * average, roundingDigits);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public decimal? SimilarityPenalty(
        decimal questionPoints,
        decimal? similarity,
        decimal penaltyWeight,
        int roundingDigits)
    {
        ValidateRoundingDigits(roundingDigits);
        if (questionPoints is < 0m or > 100m
            || similarity is not decimal score
            || score is < 0m or > 1m
            || penaltyWeight is < 0m or > 1m)
        {
            return null;
        }

        try
        {
            return Round(questionPoints * score * penaltyWeight, roundingDigits);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public decimal? FinalRaw(
        decimal basePoints,
        IEnumerable<decimal?> questionEarned,
        decimal? specialEarned,
        IEnumerable<decimal?> similarityPenalties,
        int roundingDigits)
    {
        ArgumentNullException.ThrowIfNull(questionEarned);
        ArgumentNullException.ThrowIfNull(similarityPenalties);
        ValidateRoundingDigits(roundingDigits);
        ImmutableArray<decimal?> questions = questionEarned.ToImmutableArray();
        ImmutableArray<decimal?> penalties = similarityPenalties.ToImmutableArray();
        if (basePoints is < 0m or > 100m
            || questions.IsEmpty
            || questions.Any(value => value is null)
            || specialEarned is null
            || penalties.Length != questions.Length
            || penalties.Any(value => value is null))
        {
            return null;
        }

        try
        {
            decimal value = checked(
                basePoints
                + questions.Sum(item => item!.Value)
                + specialEarned.Value
                - penalties.Sum(item => item!.Value));
            return Round(value, roundingDigits);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public static decimal? FinalScore(decimal? finalRaw) => finalRaw switch
    {
        null => null,
        < 0m => 0m,
        > 100m => 100m,
        _ => finalRaw,
    };

    public decimal Round(decimal value, int roundingDigits)
    {
        ValidateRoundingDigits(roundingDigits);
        return decimal.Round(value, roundingDigits, MidpointRounding.AwayFromZero);
    }

    private static bool IsInRange(decimal value, ScoreRange range) =>
        range.Minimum < range.Maximum && value >= range.Minimum && value <= range.Maximum;

    private static decimal NormalizeExactly(decimal raw, ScoreRange range, int roundingDigits)
    {
        (BigInteger rawValue, int rawScale) = ToIntegerAndScale(raw);
        (BigInteger minimumValue, int minimumScale) = ToIntegerAndScale(range.Minimum);
        (BigInteger maximumValue, int maximumScale) = ToIntegerAndScale(range.Maximum);
        int commonScale = Math.Max(rawScale, Math.Max(minimumScale, maximumScale));
        rawValue *= BigInteger.Pow(10, commonScale - rawScale);
        minimumValue *= BigInteger.Pow(10, commonScale - minimumScale);
        maximumValue *= BigInteger.Pow(10, commonScale - maximumScale);

        BigInteger numerator = rawValue - minimumValue;
        BigInteger denominator = maximumValue - minimumValue;
        BigInteger decimalPlaces = BigInteger.Pow(10, roundingDigits);
        BigInteger scaledNumerator = numerator * 100 * decimalPlaces;
        BigInteger quotient = BigInteger.DivRem(scaledNumerator, denominator, out BigInteger remainder);
        if ((remainder * 2) >= denominator)
        {
            quotient++;
        }

        return (decimal)quotient / (decimal)decimalPlaces;
    }

    private static (BigInteger Value, int Scale) ToIntegerAndScale(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        BigInteger integer = (uint)bits[0]
            | ((BigInteger)(uint)bits[1] << 32)
            | ((BigInteger)(uint)bits[2] << 64);
        bool negative = (bits[3] & int.MinValue) != 0;
        int scale = (bits[3] >> 16) & 0x7F;
        return (negative ? -integer : integer, scale);
    }

    private static void ValidateRoundingDigits(int roundingDigits)
    {
        if (roundingDigits is < 0 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(roundingDigits), roundingDigits, "Rounding digits must be between 0 and 6.");
        }
    }

    private decimal? TryRoundProduct(decimal left, decimal right, int roundingDigits)
    {
        try
        {
            return Round(left * right, roundingDigits);
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
