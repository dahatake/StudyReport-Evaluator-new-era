using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views.Charts;
using StudyReportEvaluator.App.Visualization;
using StudyReportEvaluator.Core.Domain;
using Xunit;
using static StudyReportEvaluator.App.Tests.UI.ChartTestSupport;
using static StudyReportEvaluator.App.Tests.UI.ResponsiveLayoutTests;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-067 (AC-089), FR-069, FR-070 (AC-092), FR-071
public sealed class AllocationChartViewTests
{
    [AvaloniaFact]
    public void Half_point_shortfall_shows_difference_minus_0_5_equal_to_the_validation_error()
    {
        QuantificationDesignViewModel design = new(Definition(19.5m));
        AllocationChartViewModel chart = design.AllocationChart;
        Assert.Same(chart, design.AllocationVisualization);
        Assert.Equal(-0.5m, chart.Difference);
        Assert.Equal(design.AllocationTotal - 100m, chart.Difference);
        Assert.Equal("差分 −0.5", chart.DifferenceText);
        Assert.Equal("100 に 0.5 足りません", chart.StateText);
        DesignValidationError error = Assert.Single(design.ValidationErrors, item => item.Code == "ALLOCATION_TOTAL_INVALID");
        Assert.Contains("差分 −0.5", error.Message, StringComparison.Ordinal);

        AllocationChartView view = new() { DataContext = chart };
        Window window = Host(view, 360d, 480d);
        try
        {
            Assert.Equal("差分 −0.5", ById<TextBlock>(view, "DesignAllocationDifference").Text);
            Assert.Equal("合計 99.5 / 100", ById<TextBlock>(view, "DesignAllocationTotal").Text);
            AllocationBarPanel bar = ById<AllocationBarPanel>(view, "DesignAllocationBar");
            AllocationSegmentControl[] segments = [.. bar.SegmentControls];
            Assert.Equal(["DesignAllocationSegment-Base", "DesignAllocationSegment-Special",
                "DesignAllocationSegment-Question-Q1", "DesignAllocationSegment-Question-Q2"],
                segments.Select(segment => AutomationProperties.GetAutomationId(segment)));
            Assert.Equal("設問 2: Question Q2・配点 19.5", AutomationProperties.GetName(segments[3]));
            double barWidth = bar.Bounds.Width;
            Assert.Equal(barWidth * 0.6d, segments[0].Bounds.Width, 1d);
            Assert.Equal(barWidth * 0.195d, segments[3].Bounds.Width, 1d);
            Assert.Equal(barWidth * 0.995d, segments[3].Bounds.Right, 1d);
            Assert.Contains("差分 −0.5", AutomationProperties.GetName(ById<AllocationReferenceLine>(view, "DesignAllocationReferenceLine")),
                StringComparison.Ordinal);

            // The points table equals the bar.
            Assert.All(chart.Segments, segment =>
            {
                Grid row = ById<Grid>(view, segment.TableAutomationId);
                Assert.Equal(segment.AccessibleName, AutomationProperties.GetName(row));
                Assert.Contains(row.Children.OfType<TextBlock>(), text => text.Text == segment.PointsText);
            });
            TextBlock summary = ById<TextBlock>(view, "DesignAllocationSummaryText");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(summary));
            Assert.Contains("差分 −0.5", summary.Text, StringComparison.Ordinal);
            Assert.Contains("件数 2・平均 19.75・中央値 19.75・最小 19.5・最大 20・空欄 0 件", summary.Text, StringComparison.Ordinal);

            // Keyboard: arrows move between segments, Enter selects the question in the design form (FR-069).
            Assert.True(segments[0].Focus(NavigationMethod.Tab));
            Key(window, Avalonia.Input.Key.Right);
            Key(window, Avalonia.Input.Key.End);
            Assert.Same(segments[3], Focused(window));
            Key(window, Avalonia.Input.Key.Left);
            Assert.Same(segments[2], Focused(window));
            Key(window, Avalonia.Input.Key.Right);
            Key(window, Avalonia.Input.Key.Enter);
            Assert.Equal("Q2", design.SelectedQuestion?.Id);
            Assert.True(chart.Segments[3].IsSelected);
            Assert.Equal("▶", chart.Segments[3].Marker);

            design.Questions[1].Points = 20m;
            Render();
            Assert.Equal("差分 0", ById<TextBlock>(view, "DesignAllocationDifference").Text);
            Assert.Equal("合計は 100 です", chart.StateText);
            Assert.DoesNotContain(design.ValidationErrors, item => item.Code == "ALLOCATION_TOTAL_INVALID");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Excess_keeps_the_100_line_inside_the_bar_and_an_incomputable_total_states_the_reason()
    {
        QuantificationDesignViewModel design = new(Definition(20.5m));
        AllocationChartViewModel chart = design.AllocationChart;
        Assert.Equal("差分 +0.5", chart.DifferenceText);
        Assert.Equal(100d / 100.5d, chart.ReferenceFraction, 6);
        Assert.Contains(design.ValidationErrors, item => item.Code == "ALLOCATION_TOTAL_INVALID"
            && item.Message.Contains("差分 +0.5", StringComparison.Ordinal));

        AllocationChartView view = new() { DataContext = chart };
        Window window = Host(view, 360d, 480d);
        try
        {
            AllocationBarPanel bar = ById<AllocationBarPanel>(view, "DesignAllocationBar");
            Assert.Equal(bar.Bounds.Width, bar.SegmentControls.Last().Bounds.Right, 1d);
            AllocationReferenceLine line = ById<AllocationReferenceLine>(view, "DesignAllocationReferenceLine");
            Assert.Equal(bar.Bounds.Width * 100d / 100.5d, line.LineX, 1d);
            Assert.False(line.IsHitTestVisible);

            design.Questions[0].Points = -1m;
            Render();
            Assert.False(chart.CanDraw);
            Assert.True(ById<TextBlock>(view, "DesignAllocationChartEmpty").IsEffectivelyVisible);
            Assert.Equal(AllocationChartViewModel.NoTotalReason, ById<TextBlock>(view, "DesignAllocationChartEmptyReason").Text);
            Assert.False(bar.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static QuantificationDefinition Definition(decimal secondPoints) => U01TestSupport.Definition(
        2,
        3,
        U01TestSupport.Question("Q1", "A", ["B"], true, U01TestSupport.Evaluator("E1", "C1")) with { Points = 20m },
        U01TestSupport.Question("Q2", "C", [], true, U01TestSupport.Evaluator("E2", "C2")) with { Points = secondPoints })
        with { BasePoints = 60m, SpecialPoints = 0m };
}
