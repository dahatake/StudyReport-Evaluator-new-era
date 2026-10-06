using System.Collections.Immutable;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.Visualization;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Scoring;
using StudyReportEvaluator.Core.Validation;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Visualization;

// Requirements: FR-066 (AC-088), FR-067 (AC-089), FR-070 (AC-092)
public sealed class ResultChartCalculatorTests
{
    [Fact]
    public void Histogram_bins_are_ten_wide_with_100_in_the_last_bin_and_blank_counted_apart()
    {
        decimal?[] scores = [0m, 9.99m, 10m, 19.999999m, 50m, 89.9m, 90m, 99.99m, 100m, null, null];
        ResultChartRow[] rows = [.. scores.Select((score, index) => new ResultChartRow(index + 2, score, []))];

        ScoreHistogram histogram = ResultChartCalculator.BuildHistogram(rows);

        Assert.Equal(10, histogram.Bins.Length);
        Assert.Equal(["0〜10", "10〜20", "20〜30", "30〜40", "40〜50", "50〜60", "60〜70", "70〜80", "80〜90", "90〜100"],
            histogram.Bins.Select(bin => bin.Label));
        Assert.Equal([2, 3], histogram.Bins[0].SourceRows);
        Assert.Equal([4, 5], histogram.Bins[1].SourceRows);
        Assert.Equal([6], histogram.Bins[5].SourceRows);
        Assert.Equal([7], histogram.Bins[8].SourceRows);
        Assert.Equal([8, 9, 10], histogram.Bins[9].SourceRows);
        Assert.True(histogram.Bins[9].IncludesUpper);
        Assert.False(histogram.Bins[8].IncludesUpper);
        Assert.Equal([11, 12], histogram.BlankRows);
        Assert.Equal(rows.Length, histogram.Bins.Sum(bin => bin.Count) + histogram.BlankCount);
        Assert.Equal(rows.Length, histogram.TotalCount);
        Assert.Null(ResultChartCalculator.HistogramBinIndex(null));
        Assert.Equal(0, ResultChartCalculator.HistogramBinIndex(0m));
        Assert.Equal(0, ResultChartCalculator.HistogramBinIndex(9.99m));
        Assert.Equal(1, ResultChartCalculator.HistogramBinIndex(10m));
        Assert.Equal(9, ResultChartCalculator.HistogramBinIndex(100m));
    }

    [Fact]
    public void Summary_states_count_mean_median_minimum_maximum_and_blank_count()
    {
        ChartSummary summary = ResultChartCalculator.Summarize([4m, null, 1m, 3m, 2m, null]);

        Assert.Equal(4, summary.Count);
        Assert.Equal(2, summary.BlankCount);
        Assert.Equal(2.5m, summary.Mean);
        Assert.Equal(2.5m, summary.Median);
        Assert.Equal(1m, summary.Minimum);
        Assert.Equal(4m, summary.Maximum);
        Assert.Equal("最終点: 件数 4・平均 2.5・中央値 2.5・最小 1・最大 4・空欄 2 件",
            summary.Describe("最終点", ChartFormat.Exact));
        Assert.Equal("最終点: 件数 0・空欄 1 件", ResultChartCalculator.Summarize([null]).Describe("最終点", ChartFormat.Exact));
        Assert.Equal(7m, ResultChartCalculator.Summarize([7m, 1m, 9m]).Median);

        ChartSummary extreme = ResultChartCalculator.Summarize([decimal.MaxValue, decimal.MaxValue]);
        Assert.Equal(decimal.MaxValue, extreme.Maximum);
        Assert.NotNull(extreme.Mean);
        Assert.NotNull(extreme.Median);
    }

    [Fact]
    public void Heatmap_cell_is_earned_over_points_and_blank_is_a_distinct_symbol_and_text()
    {
        HeatmapCellValue value = ResultChartCalculator.Cell(15m, 20m);
        HeatmapCellValue blank = ResultChartCalculator.Cell(null, 20m);
        HeatmapCellValue noPoints = ResultChartCalculator.Cell(0m, 0m);

        Assert.Equal(HeatmapCellKind.Value, value.Kind);
        Assert.Equal(0.75m, value.Rate);
        Assert.Equal("0.75", value.Text);
        Assert.Equal("0.75", value.Symbol);
        Assert.Equal("得点率 0.75", value.AccessibleValue);
        Assert.Equal(HeatmapCellKind.Blank, blank.Kind);
        Assert.Null(blank.Rate);
        Assert.Equal("×", blank.Symbol);
        Assert.Equal("空欄（技術的失敗）", blank.Text);
        Assert.Equal("空欄（技術的失敗）", blank.AccessibleValue);
        Assert.Equal(HeatmapCellKind.NoPoints, noPoints.Kind);
        Assert.NotEqual(blank.Symbol, noPoints.Symbol);
        Assert.Equal("1.00", ResultChartCalculator.Cell(20m, 20m).Text);
        Assert.Equal("0.33", ResultChartCalculator.Cell(1m, 3m).Text);
    }

    [Fact]
    public void Similarity_ranking_is_descending_with_blanks_last_and_ties_by_excel_row()
    {
        ResultChartRow[] rows =
        [
            Row(2, 0.4m), Row(3, null), Row(4, 0.9m), Row(5, 0.4m), Row(6, 0.95m),
        ];

        ImmutableArray<SimilarityEntry> ranked = ResultChartCalculator.RankSimilarity(rows, 0);

        Assert.Equal([6, 4, 2, 5, 3], ranked.Select(entry => entry.SourceRow));
        Assert.Equal(0.95m, ranked[0].Similarity);
        Assert.Equal(1.9m, ranked[0].Penalty);
        Assert.Equal(0.8m, ranked[0].PeerMax);
        Assert.Equal(9, ranked[0].PeerRow);

        static ResultChartRow Row(int row, decimal? similarity) =>
            new(row, 50m, [new ResultChartQuestionValue(10m, similarity, similarity * 2m, 0.8m, 9)]);
    }

    [Theory]
    [InlineData("19.5", "−0.5")]
    [InlineData("20.5", "+0.5")]
    [InlineData("20", "0")]
    public void Allocation_difference_is_the_validation_total_minus_100(string secondPoints, string expected)
    {
        QuantificationDefinition definition = U01TestSupport.Definition(
            2,
            3,
            U01TestSupport.Question("Q1", "A", ["B"], true, U01TestSupport.Evaluator("E1", "C1")) with { Points = 20m },
            U01TestSupport.Question("Q2", "C", [], true, U01TestSupport.Evaluator("E2", "C2")) with
            {
                Points = decimal.Parse(secondPoints, System.Globalization.CultureInfo.InvariantCulture),
            }) with { BasePoints = 60m, SpecialPoints = 0m };

        AllocationComposition composition = AllocationComposition.Build(definition, new ScoringAllocationCalculator());

        Assert.Equal($"差分 {expected}", "差分 " + ChartFormat.SignedDifference(composition.Difference!.Value));
        Assert.Equal(["B", "S", "1", "2"], composition.Segments.Select(segment => segment.Code));
        Assert.Equal([60m, 0m, 20m, composition.Segments[3].Points], composition.Segments.Select(segment => segment.Points));
        DefinitionValidationError? error = new QuantificationDefinitionValidator().Validate(definition).Errors
            .SingleOrDefault(item => item.Code == "ALLOCATION_TOTAL_INVALID");
        if (expected == "0")
        {
            Assert.Null(error);
            Assert.True(composition.IsValid);
        }
        else
        {
            Assert.NotNull(error);
            decimal validationTotal = decimal.Parse(error.SafeOffendingValue, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(validationTotal, composition.Total);
            Assert.Equal(validationTotal - 100m, composition.Difference);
        }
    }

    [Fact]
    public void Allocation_without_a_computable_total_has_no_difference()
    {
        QuantificationDefinition definition = U01TestSupport.Definition(
            2,
            3,
            U01TestSupport.Question("Q1", "A", ["B"], true, U01TestSupport.Evaluator("E1", "C1")) with { Points = -1m });

        AllocationComposition composition = AllocationComposition.Build(definition, new ScoringAllocationCalculator());

        Assert.Null(composition.Total);
        Assert.Null(composition.Difference);
        Assert.False(composition.IsValid);
    }
}
