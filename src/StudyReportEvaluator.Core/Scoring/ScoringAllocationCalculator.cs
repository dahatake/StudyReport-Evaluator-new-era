using System.Collections.Immutable;

namespace StudyReportEvaluator.Core.Scoring;

public readonly record struct ScoringAllocationValidationResult(
    decimal? Total,
    bool IsValid);

public sealed class ScoringAllocationCalculator
{
    public const int AllocationDecimalPlaces = 6;

    public ImmutableArray<decimal> Equalize(
        decimal basePoints,
        decimal specialPoints,
        int enabledQuestionCount)
    {
        ValidateRootPoints(basePoints, nameof(basePoints));
        ValidateRootPoints(specialPoints, nameof(specialPoints));
        if (enabledQuestionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(enabledQuestionCount),
                enabledQuestionCount,
                "At least one enabled question is required.");
        }

        decimal available = 100m - basePoints - specialPoints;
        if (available < 0m)
        {
            throw new ArgumentException("Base and special points must not exceed 100 in total.");
        }

        decimal shared = decimal.Round(
            available / enabledQuestionCount,
            AllocationDecimalPlaces,
            MidpointRounding.ToZero);
        ImmutableArray<decimal>.Builder result =
            ImmutableArray.CreateBuilder<decimal>(enabledQuestionCount);
        decimal allocated = 0m;
        for (int index = 0; index < enabledQuestionCount - 1; index++)
        {
            result.Add(shared);
            allocated += shared;
        }

        result.Add(available - allocated);
        return result.MoveToImmutable();
    }

    public ScoringAllocationValidationResult Validate(
        decimal basePoints,
        decimal specialPoints,
        IEnumerable<decimal> enabledQuestionPoints)
    {
        ArgumentNullException.ThrowIfNull(enabledQuestionPoints);
        if (!IsRootPoints(basePoints) || !IsRootPoints(specialPoints))
        {
            return new ScoringAllocationValidationResult(null, false);
        }

        try
        {
            decimal total = checked(basePoints + specialPoints);
            bool hasQuestion = false;
            foreach (decimal points in enabledQuestionPoints)
            {
                hasQuestion = true;
                if (points < 0m)
                {
                    return new ScoringAllocationValidationResult(null, false);
                }

                total = checked(total + points);
            }

            return new ScoringAllocationValidationResult(total, hasQuestion && total == 100m);
        }
        catch (OverflowException)
        {
            return new ScoringAllocationValidationResult(null, false);
        }
    }

    private static void ValidateRootPoints(decimal value, string parameterName)
    {
        if (!IsRootPoints(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Points must be between 0 and 100.");
        }
    }

    private static bool IsRootPoints(decimal value) => value is >= 0m and <= 100m;
}
