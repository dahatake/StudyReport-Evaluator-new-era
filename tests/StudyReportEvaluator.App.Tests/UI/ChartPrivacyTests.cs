using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Views.Charts;
using StudyReportEvaluator.App.Visualization;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workspace;
using Xunit;
using static StudyReportEvaluator.App.Tests.UI.ChartTestSupport;
using static StudyReportEvaluator.App.Tests.UI.ResponsiveLayoutTests;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: NFR-SEC-006 (AC-094)
public sealed partial class ChartPrivacyTests
{
    [AvaloniaFact]
    public async Task Name_canary_appears_zero_times_in_every_chart_text_table_tooltip_and_accessible_name()
    {
        RunSummary summary = await CreateRunAsync(40, SpreadScore);
        RecordingOutputBoundary boundary = new();
        using ResultsOutputViewModel results = new(boundary, U04TestSupport.Context(summary));
        // Not vacuous: the run carries the canary (the synthetic AI echoes the name in its reason).
        Assert.Contains(results.Results, item => item.ReasonText.Contains(NameCanary, StringComparison.Ordinal));

        ResultsOutputView view = new(results);
        Window window = Host(view, 1180d, 800d);
        try
        {
            PanelWorkspace workspace = Required<PanelWorkspace>(view, "ResultsWorkspace");
            workspace.Execute(WorkspacePanelOperation.Maximize, WorkspacePanelIds.ResultsChart);
            Render();
            ResultsChartsViewModel charts = results.Charts;
            int checkedTexts = 0;
            // Personas only rearrange panels; every representation and alternative is inspected here.
            foreach (bool filtered in new[] { false, true })
            {
                if (filtered)
                {
                    charts.ActivateBin(charts.HistogramBins.First(bin => bin.Count > 0 && !bin.IsBlank));
                    Assert.True(results.IsRowFilterActive);
                }

                foreach (ResultsChartKind kind in Enum.GetValues<ResultsChartKind>())
                {
                    foreach (bool table in new[] { false, true })
                    {
                        charts.SelectedKind = kind;
                        charts.ShowTable = table;
                        Render();
                        ResultsChartsView chartsView = Assert.Single(view.GetVisualDescendants().OfType<ResultsChartsView>());
                        string[] texts = [.. ExposedTexts(chartsView), .. ModelTexts(charts)];
                        checkedTexts += texts.Length;
                        Assert.DoesNotContain(texts, text => text.Contains(NameCanary, StringComparison.Ordinal));
                        Assert.DoesNotContain(texts, text => text.Contains("合成の理由", StringComparison.Ordinal));
                        Assert.DoesNotContain(texts, text => text.Contains("row:", StringComparison.Ordinal));
                        Assert.DoesNotContain(texts, text => text.Contains("synthetic support", StringComparison.Ordinal));
                    }
                }
            }

            Assert.True(checkedTexts > 200, $"Only {checkedTexts} texts were inspected.");
            // Only Excel row numbers, snapshot question text and numeric values name a heatmap cell.
            Regex allowed = CellName();
            Assert.All(charts.HeatmapRows, row =>
            {
                for (int column = 0; column < row.CellCount; column++)
                {
                    Assert.Matches(allowed, row.CellAccessibleName(column));
                }
            });
            Assert.Equal(0, boundary.ExportCount);
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<string> ModelTexts(ResultsChartsViewModel charts)
    {
        yield return charts.SummaryText;
        yield return charts.FilterText;
        yield return charts.StatusText;
        yield return charts.EmptyReason;
        foreach (HistogramBinItem bin in charts.HistogramBins)
        {
            yield return bin.AccessibleName;
            yield return bin.RangeText;
        }

        foreach (ChartQuestionItem question in charts.Questions)
        {
            yield return question.DisplayText;
        }

        foreach (HeatmapRowItem row in charts.HeatmapRows)
        {
            yield return row.HeaderAccessibleName;
            for (int column = 0; column < row.CellCount; column++)
            {
                yield return row.CellAccessibleName(column);
                yield return row.Cell(column).Text;
            }
        }

        foreach (SimilarityEntryItem entry in charts.SimilarityEntries)
        {
            yield return entry.AccessibleName;
        }
    }

    [GeneratedRegex(@"^行 \d+・(設問文A：読解|設問文B：考察)・(得点率 \d+\.\d\d|空欄（技術的失敗）|配点 0（得点率なし）)$")]
    private static partial Regex CellName();
}
