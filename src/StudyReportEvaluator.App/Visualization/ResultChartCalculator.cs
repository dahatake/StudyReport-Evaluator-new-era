using System.Collections.Immutable;

namespace StudyReportEvaluator.App.Visualization;

/// <summary>One Final_Score bin: lower bound inclusive, upper bound exclusive except for the last bin (AS-008).</summary>
public sealed class HistogramBin
{
    internal HistogramBin(int index, decimal lower, decimal upper, ImmutableArray<int> sourceRows)
    {
        Index = index;
        Lower = lower;
        Upper = upper;
        SourceRows = sourceRows;
    }

    public int Index { get; }

    public decimal Lower { get; }

    public decimal Upper { get; }

    /// <summary>Only the last bin contains its upper bound (100).</summary>
    public bool IncludesUpper => Index == ResultChartCalculator.HistogramBinCount - 1;

    public ImmutableArray<int> SourceRows { get; }

    public int Count => SourceRows.Length;

    public string Label => $"{ChartFormat.Exact(Lower)}〜{ChartFormat.Exact(Upper)}";
}

public sealed class ScoreHistogram
{
    internal ScoreHistogram(ImmutableArray<HistogramBin> bins, ImmutableArray<int> blankRows)
    {
        Bins = bins;
        BlankRows = blankRows;
    }

    public ImmutableArray<HistogramBin> Bins { get; }

    /// <summary>Rows whose Final_Score is blank (technical failure); counted apart from the bins.</summary>
    public ImmutableArray<int> BlankRows { get; }

    public int BlankCount => BlankRows.Length;

    public int TotalCount => Bins.Sum(bin => bin.Count) + BlankCount;

    public int MaximumCount => Math.Max(BlankCount, Bins.Max(bin => bin.Count));
}

/// <summary>件数・平均・中央値・最小・最大・空欄件数 of a set of values (FR-070).</summary>
public sealed class ChartSummary
{
    internal ChartSummary(int count, int blankCount, decimal? mean, decimal? median, decimal? minimum, decimal? maximum)
    {
        Count = count;
        BlankCount = blankCount;
        Mean = mean;
        Median = median;
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>Number of non-blank values.</summary>
    public int Count { get; }

    public int BlankCount { get; }

    public decimal? Mean { get; }

    public decimal? Median { get; }

    public decimal? Minimum { get; }

    public decimal? Maximum { get; }

    /// <summary>One line of Japanese text; <paramref name="format"/> formats median, minimum and maximum.</summary>
    public string Describe(string subject, Func<decimal, string> format)
    {
        ArgumentNullException.ThrowIfNull(format);
        string blank = $"空欄 {ChartFormat.Count(BlankCount)} 件";
        if (Count == 0)
        {
            return $"{subject}: 件数 0・{blank}";
        }

        return $"{subject}: 件数 {ChartFormat.Count(Count)}・平均 {ChartFormat.Mean(Mean!.Value)}・中央値 {format(Median!.Value)}・最小 {format(Minimum!.Value)}・最大 {format(Maximum!.Value)}・{blank}";
    }
}

public enum HeatmapCellKind
{
    Value,

    /// <summary>Question_Earned is blank: a technical failure.</summary>
    Blank,

    /// <summary>The question has 0 points, so no rate exists.</summary>
    NoPoints,
}

public readonly record struct HeatmapCellValue(HeatmapCellKind Kind, decimal? Rate)
{
    public string Text => Kind switch
    {
        HeatmapCellKind.Value => ChartFormat.Rate(Rate!.Value),
        HeatmapCellKind.Blank => ChartFormat.BlankText,
        _ => ChartFormat.NoPointsText,
    };

    public string Symbol => Kind switch
    {
        HeatmapCellKind.Value => ChartFormat.Rate(Rate!.Value),
        HeatmapCellKind.Blank => ChartFormat.BlankSymbol,
        _ => ChartFormat.NoPointsSymbol,
    };

    public string AccessibleValue => Kind == HeatmapCellKind.Value
        ? $"得点率 {ChartFormat.Rate(Rate!.Value)}"
        : Text;
}

public sealed record SimilarityEntry(int SourceRow, decimal? Similarity, decimal? Penalty, decimal? PeerMax, int? PeerRow);

/// <summary>UI-independent computations of the result charts (FR-066) and their summaries (FR-070).</summary>
public static class ResultChartCalculator
{
    public const int HistogramBinCount = 10;
    public const decimal HistogramBinWidth = 10m;

    /// <summary>0〜10, …, 90〜100 (100 is in the last bin); null for a blank score.</summary>
    public static int? HistogramBinIndex(decimal? finalScore)
    {
        if (finalScore is not decimal score)
        {
            return null;
        }

        decimal index = decimal.Floor(score / HistogramBinWidth);
        return (int)Math.Clamp(index, 0m, HistogramBinCount - 1);
    }

    public static ScoreHistogram BuildHistogram(IEnumerable<ResultChartRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        List<int>[] members = [.. Enumerable.Range(0, HistogramBinCount).Select(_ => new List<int>())];
        List<int> blank = [];
        foreach (ResultChartRow row in rows)
        {
            if (HistogramBinIndex(row.FinalScore) is int index)
            {
                members[index].Add(row.SourceRow);
            }
            else
            {
                blank.Add(row.SourceRow);
            }
        }

        return new ScoreHistogram(
            [.. members.Select((list, index) => new HistogramBin(
                index,
                index * HistogramBinWidth,
                (index + 1) * HistogramBinWidth,
                [.. list]))],
            [.. blank]);
    }

    public static ChartSummary Summarize(IEnumerable<decimal?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        List<decimal> present = [];
        int blank = 0;
        foreach (decimal? value in values)
        {
            if (value is decimal number)
            {
                present.Add(number);
            }
            else
            {
                blank++;
            }
        }

        if (present.Count == 0)
        {
            return new ChartSummary(0, blank, null, null, null, null);
        }

        present.Sort();
        decimal mean;
        try
        {
            decimal sum = 0m;
            foreach (decimal value in present)
            {
                sum = checked(sum + value);
            }

            mean = sum / present.Count;
        }
        catch (OverflowException)
        {
            // Extreme values: a display-only mean from doubles, which cannot overflow the decimal range.
            double average = present.Average(value => (double)value);
            mean = average >= (double)decimal.MaxValue ? decimal.MaxValue
                : average <= (double)decimal.MinValue ? decimal.MinValue
                : (decimal)average;
        }

        int middle = present.Count / 2;
        decimal median = present.Count % 2 == 1
            ? present[middle]
            : Midpoint(present[middle - 1], present[middle]);
        return new ChartSummary(present.Count, blank, mean, median, present[0], present[^1]);
    }

    private static decimal Midpoint(decimal low, decimal high)
    {
        try
        {
            return (low + high) / 2m;
        }
        catch (OverflowException)
        {
            try
            {
                return low + ((high - low) / 2m);
            }
            catch (OverflowException)
            {
                return (low / 2m) + (high / 2m);
            }
        }
    }

    /// <summary>Question_Earned ÷ Points; blank earned is a technical failure; 0 points has no rate.</summary>
    public static HeatmapCellValue Cell(decimal? earned, decimal points)
    {
        if (earned is not decimal value)
        {
            return new HeatmapCellValue(HeatmapCellKind.Blank, null);
        }

        return points > 0m
            ? new HeatmapCellValue(HeatmapCellKind.Value, value / points)
            : new HeatmapCellValue(HeatmapCellKind.NoPoints, null);
    }

    /// <summary>The rate as shown (two decimals), so summaries describe the displayed values.</summary>
    public static decimal? DisplayedRate(HeatmapCellValue cell) => cell.Rate is decimal rate
        ? decimal.Round(rate, 2, MidpointRounding.AwayFromZero)
        : null;

    /// <summary>Descending similarity to the reference answer; blanks last; ties by Excel row.</summary>
    public static ImmutableArray<SimilarityEntry> RankSimilarity(IEnumerable<ResultChartRow> rows, int questionIndex)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return [.. rows
            .Select(row =>
            {
                ResultChartQuestionValue value = row.Questions[questionIndex];
                return new SimilarityEntry(row.SourceRow, value.Similarity, value.Penalty, value.PeerMax, value.PeerRow);
            })
            .OrderBy(entry => entry.Similarity is null)
            .ThenByDescending(entry => entry.Similarity ?? 0m)
            .ThenBy(entry => entry.SourceRow)];
    }
}
