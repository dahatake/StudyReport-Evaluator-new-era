using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Navigation;
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

// Requirements: FR-066 (AC-088), FR-069 (AC-091), FR-070 (AC-092), FR-071 (AC-093)
public sealed class ResultsChartsViewTests
{
    [AvaloniaFact]
    public async Task Histogram_of_530_rows_counts_every_row_and_a_heatmap_cell_opens_its_detail()
    {
        RunSummary summary = await CreateRunAsync(530, SpreadScore);
        RecordingOutputBoundary boundary = new();
        using ResultsOutputViewModel results = new(boundary, U04TestSupport.Context(summary));
        ResultsChartsViewModel charts = results.Charts;

        Assert.Equal(530, results.RowScores.Count);
        Assert.Equal(11, charts.HistogramBins.Length);
        Assert.Equal(530, charts.HistogramBins.Sum(bin => bin.Count));
        Assert.Equal(10, charts.HistogramBins.Single(bin => bin.IsBlank).Count);
        Assert.Equal(
            results.RowScores.Count(row => row.FinalScore is >= 90m),
            charts.HistogramBins.Single(bin => bin.Key == "9").Count);
        Assert.False(charts.IsProvisional);

        ResultsOutputView view = new(results);
        Window window = Host(view, 1180d, 800d);
        try
        {
            PanelWorkspace workspace = Required<PanelWorkspace>(view, "ResultsWorkspace");
            workspace.Select(WorkspacePanelIds.ResultsChart);
            Render();
            ListBox histogram = ById<ListBox>(view, "ResultsHistogramChart");
            Assert.True(histogram.IsEffectivelyVisible);
            int lowest = results.RowScores.Count(row => row.FinalScore < 10m);
            Assert.Equal(
                $"最終点 0 点以上 10 点未満: {lowest} 件",
                AutomationProperties.GetName(Assert.IsType<ListBoxItem>(histogram.ContainerFromIndex(0))));

            charts.SelectedKind = ResultsChartKind.Heatmap;
            Render();
            ResultsChartsView chartsView = Assert.Single(view.GetVisualDescendants().OfType<ResultsChartsView>());
            HeatmapCell cell = Assert.IsType<HeatmapCell>(chartsView.FocusHeatmapCell(4, 1));
            Render();
            Assert.Same(cell, Focused(window));
            Assert.Equal($"行 6・{SecondQuestionText}・得点率 0.60", AutomationProperties.GetName(cell));
            Assert.Equal("ResultsHeatmapCell-6-Q2", AutomationProperties.GetAutomationId(cell));

            Key(window, Avalonia.Input.Key.Enter);
            Render();
            Assert.Equal(6, results.SelectedRow?.SourceRowNumber);
            Assert.Equal("Q2", results.SelectedCriterion?.QuestionId);
            Assert.True(results.IsDetailVisible);
            Assert.True(workspace.IsPresented(WorkspacePanelIds.ResultsDetail));

            workspace.Select(WorkspacePanelIds.ResultsChart);
            charts.SelectedKind = ResultsChartKind.Similarity;
            Render();
            TextBlock notice = ById<TextBlock>(view, "ResultsSimilarityNoticeText");
            Assert.True(notice.IsEffectivelyVisible);
            Assert.Equal("類似度は不正行為の証明ではありません。", notice.Text);
            Assert.Equal(0, boundary.ExportCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Selecting_the_0_to_10_bin_filters_the_paged_list_and_heatmap_keeps_filter_selection_and_overrides()
    {
        using ResultsOutputViewModel results = await CreateChartResultsAsync(60);
        ResultsChartsViewModel charts = results.Charts;
        ResultsCriterionViewModel edited = results.Results.First(item => item.SourceRowNumber == 22 && item.QuestionId == "Q1");
        edited.OverrideText = "1";
        Assert.True(results.HasUnsavedOverrides);
        ResultsOutputView view = new(results);
        Window window = Host(view, 1180d, 800d);
        try
        {
            PanelWorkspace workspace = Required<PanelWorkspace>(view, "ResultsWorkspace");
            workspace.Select(WorkspacePanelIds.ResultsChart);
            Render();
            ListBox histogram = ById<ListBox>(view, "ResultsHistogramChart");
            ListBoxItem first = Assert.IsType<ListBoxItem>(histogram.ContainerFromIndex(0));
            Assert.True(first.Focus(NavigationMethod.Tab));
            Key(window, Avalonia.Input.Key.Down);
            Assert.Equal("1", charts.FocusedBin?.Key);
            Assert.Null(results.RowFilter);
            Key(window, Avalonia.Input.Key.Up);
            Assert.Equal("0", charts.FocusedBin?.Key);
            Key(window, Avalonia.Input.Key.Enter);

            int[] expected = [11, 22, 33, 44, 55];
            Assert.NotNull(results.RowFilter);
            Assert.Equal(expected, results.FilteredRowScores.Select(row => row.SourceRowNumber));
            Assert.All(results.FilteredRowScores, row => Assert.True(row.FinalScore < 10m));
            Assert.True(charts.HistogramBins[0].IsActiveFilter);
            Assert.Equal("▶", charts.HistogramBins[0].Marker);
            results.PageSize = 3;
            Render();
            Assert.Equal([11, 22, 33], results.VisibleRowScores.Select(row => row.SourceRowNumber));
            Assert.Equal("1–3 / 5 行（絞り込み中・全 60 行）", results.PageSummary);
            results.NextPageCommand.Execute(null);
            Assert.Equal([44, 55], results.VisibleRowScores.Select(row => row.SourceRowNumber));
            Assert.False(results.NextPageCommand.CanExecute(null));
            results.SelectedRow = results.VisibleRowScores[1];
            Assert.Equal(55, charts.SelectedSourceRow);

            charts.SelectedKind = ResultsChartKind.Heatmap;
            Render();
            Assert.Equal(expected, charts.HeatmapRows.Select(row => row.SourceRow));
            Assert.Equal(55, charts.SelectedSourceRow);
            HeatmapRowItem selected = Assert.Single(charts.HeatmapRows, row => row.IsSelected);
            Assert.Equal(55, selected.SourceRow);
            Assert.Equal("▶55", selected.HeaderText);
            Assert.True(charts.IsFilterActive);
            Assert.Equal(55, results.SelectedRow?.SourceRowNumber);
            charts.SelectedKind = ResultsChartKind.Similarity;
            Render();
            Assert.Equal(expected.Order(), charts.SimilarityEntries.Select(entry => entry.SourceRow).Order());
            Assert.Equal(55, charts.FocusedSimilarityEntry?.SourceRow);
            Assert.Equal("1", edited.OverrideText);
            Assert.True(results.HasUnsavedOverrides);

            Button clear = ById<Button>(view, "ResultsChartClearFilter");
            Assert.Equal("絞り込みを解除", clear.Content);
            Activate(clear);
            Assert.Null(results.RowFilter);
            Assert.False(charts.IsFilterActive);
            Assert.Equal(60, results.FilteredRowScores.Count);
            Assert.Equal(55, results.SelectedRow?.SourceRowNumber);
            Assert.Contains(results.VisibleRowScores, row => row.SourceRowNumber == 55);
            Assert.Equal("1", edited.OverrideText);

            // An explicit move to a row outside the filter clears it rather than failing silently.
            charts.ActivateBin(charts.HistogramBins[0]);
            Assert.NotNull(results.RowFilter);
            results.GoToRowNumber = 3;
            results.GoToRowCommand.Execute(null);
            Assert.Null(results.RowFilter);
            Assert.Equal(3, results.SelectedRow?.SourceRowNumber);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Heatmap_moves_by_arrow_keys_names_row_question_and_rate_and_its_table_shows_the_same_values()
    {
        (ResultsChartsViewModel charts, RecordingChartActions actions) = Create(DataSet(3, 2, (row, column) => (row, column) switch
        {
            (2, 0) => new(15m, null, null, null, null),
            (2, 1) => new(20m, null, null, null, null),
            (3, 0) => new(null, null, null, null, null),
            (3, 1) => new(0m, null, null, null, null),
            (4, 0) => new(10m, null, null, null, null),
            _ => new(5m, null, null, null, null),
        }));
        ResultsChartsView view = new() { DataContext = charts };
        Window window = Host(view, 900d, 600d);
        try
        {
            charts.SelectedKind = ResultsChartKind.Heatmap;
            Render();
            Assert.NotNull(view.FocusHeatmapCell(0, 0));
            Render();
            Assert.Equal("行 2・設問文1・得点率 0.75", FocusedName());
            Key(window, Avalonia.Input.Key.Right);
            Assert.Equal("行 2・設問文2・得点率 1.00", FocusedName());
            Key(window, Avalonia.Input.Key.Down);
            Assert.Equal("行 3・設問文2・得点率 0.00", FocusedName());
            Key(window, Avalonia.Input.Key.Left);
            HeatmapCell blank = Assert.IsType<HeatmapCell>(Focused(window));
            Assert.Equal("行 3・設問文1・空欄（技術的失敗）", AutomationProperties.GetName(blank));
            Assert.Equal(HeatmapCellKind.Blank, blank.Value.Kind);
            Assert.Equal("×", blank.DisplayText);
            Assert.Equal("行 3・設問文1・空欄（技術的失敗）", (string?)ToolTip.GetTip(blank));
            Key(window, Avalonia.Input.Key.End);
            Assert.Equal("行 3・設問文2・得点率 0.00", FocusedName());
            Key(window, Avalonia.Input.Key.End, RawInputModifiers.Control);
            Assert.Equal("行 4・設問文2・得点率 0.25", FocusedName());
            Key(window, Avalonia.Input.Key.Enter);
            Assert.Equal([(4, "Q2")], actions.Opened);

            // Grayscale: the state is a symbol and text, explained by a visible legend, not only a colour.
            TextBlock legend = ById<TextBlock>(view, "ResultsHeatmapLegend");
            Assert.True(legend.IsEffectivelyVisible);
            Assert.Contains("× と斜線 = 空欄（技術的失敗）", legend.Text, StringComparison.Ordinal);
            HeatmapCell[] chartCells = [.. ById<ItemsControl>(view, "ResultsHeatmapChart").GetVisualDescendants().OfType<HeatmapCell>()];
            Assert.Equal(6, chartCells.Length);
            Assert.Equal(6, chartCells.Select(cell => AutomationProperties.GetAutomationId(cell)).Distinct().Count());
            Assert.All(chartCells.Where(cell => cell.Value.Kind == HeatmapCellKind.Value),
                cell => Assert.Matches(@"^\d\.\d\d$", cell.DisplayText));

            charts.ShowTable = true;
            Render();
            Assert.False(ById<ItemsControl>(view, "ResultsHeatmapChart").IsEffectivelyVisible);
            HeatmapCell[] tableCells = [.. ById<ItemsControl>(view, "ResultsHeatmapTable").GetVisualDescendants().OfType<HeatmapCell>()];
            Assert.Equal(6, tableCells.Length);
            foreach (HeatmapCell tableCell in tableCells)
            {
                string chartId = AutomationProperties.GetAutomationId(tableCell)!.Replace("ResultsHeatmapValue-", "ResultsHeatmapCell-", StringComparison.Ordinal);
                HeatmapCell chartCell = Assert.Single(chartCells, cell => AutomationProperties.GetAutomationId(cell) == chartId);
                Assert.Equal(chartCell.Value, tableCell.Value);
                Assert.Equal(chartCell.Value.Text, tableCell.DisplayText);
                Assert.Equal(AutomationProperties.GetName(chartCell), AutomationProperties.GetName(tableCell));
            }

            Assert.Equal("空欄（技術的失敗）", ById<HeatmapCell>(view, "ResultsHeatmapValue-3-Q1").DisplayText);
            Assert.Equal("0.75", ById<HeatmapCell>(view, "ResultsHeatmapValue-2-Q1").DisplayText);

            charts.SelectedKind = ResultsChartKind.Histogram;
            Render();
            ListBox histogramTable = ById<ListBox>(view, "ResultsHistogramTable");
            Assert.True(histogramTable.IsEffectivelyVisible);
            Assert.Same(charts.HistogramBins[0], Assert.IsType<ListBoxItem>(histogramTable.ContainerFromIndex(0)).DataContext);
            Assert.Equal("最終点 0 点以上 10 点未満: 3 件", AutomationProperties.GetName(Assert.IsType<ListBoxItem>(histogramTable.ContainerFromIndex(0))));

            TextBlock summary = ById<TextBlock>(view, "ResultsChartSummary");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(summary));
            Assert.Equal(summary.Text, AutomationProperties.GetName(summary));
            Assert.Contains("件数 3", summary.Text, StringComparison.Ordinal);
            Assert.Contains("空欄 0 件", summary.Text, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }

        string? FocusedName() => Focused(window) is { } focused ? AutomationProperties.GetName(focused) : null;
    }

    [AvaloniaFact]
    public void Similarity_list_is_descending_always_shows_the_notice_and_opens_a_row_only_on_enter()
    {
        decimal?[] similarity = [0.4m, null, 0.92m, 0.4m, 0.97m];
        (ResultsChartsViewModel charts, RecordingChartActions actions) = Create(DataSet(5, 2, (row, column) =>
            new(10m, column == 0 ? similarity[row - 2] : 0.1m, column == 0 ? similarity[row - 2] * 2m : 0.2m, 0.5m, row == 2 ? 3 : 2)));
        ResultsChartsView view = new() { DataContext = charts };
        Window window = Host(view, 600d, 600d);
        try
        {
            charts.SelectedKind = ResultsChartKind.Similarity;
            Render();
            Assert.False(ById<ToggleButton>(view, "ResultsChartShowTable").IsEffectivelyVisible);
            Assert.Equal("類似度は不正行為の証明ではありません。", ById<TextBlock>(view, "ResultsSimilarityNoticeText").Text);
            Assert.True(ById<Border>(view, "ResultsSimilarityNotice").IsEffectivelyVisible);
            Assert.Equal([6, 4, 2, 5, 3], charts.SimilarityEntries.Select(entry => entry.SourceRow));
            SimilarityEntryItem top = charts.SimilarityEntries[0];
            Assert.Equal("0.97", top.SimilarityText);
            Assert.Equal("1.94", top.PenaltyText);
            Assert.Equal("0.5", top.PeerMaxText);
            Assert.Equal("行 2", top.PeerRowText);
            Assert.Equal("行 6・参照回答との類似度 0.97・類似減点 1.94・他学生との最大類似度 0.5・相手 行 2", top.AccessibleName);
            Assert.Equal("—", charts.SimilarityEntries[^1].SimilarityText);

            ListBox list = ById<ListBox>(view, "ResultsSimilarityList");
            ListBoxItem item = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            Assert.Equal("ResultsSimilarity-6-Q1", AutomationProperties.GetAutomationId(item));
            Assert.True(item.Focus(NavigationMethod.Tab));
            Key(window, Avalonia.Input.Key.Down);
            Assert.Empty(actions.Opened);
            Assert.Equal(4, charts.FocusedSimilarityEntry?.SourceRow);
            Key(window, Avalonia.Input.Key.Enter);
            Assert.Equal([(4, "Q1")], actions.Opened);

            charts.SelectedSimilarityQuestion = charts.Questions[1];
            Render();
            Assert.Equal("0.1", charts.SimilarityEntries[0].SimilarityText);
            Assert.True(ById<TextBlock>(view, "ResultsSimilarityNoticeText").IsEffectivelyVisible);
            Assert.Contains("類似度は不正行為の証明ではありません。", charts.SummaryText, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Without_data_the_charts_say_there_is_no_result_and_why_and_live_data_is_marked_provisional()
    {
        using ResultsOutputViewModel results = new(new RecordingOutputBoundary());
        Assert.True(results.Charts.IsEmpty);
        Assert.Equal(ResultsChartsViewModel.NoRunReason, results.Charts.EmptyReason);
        Assert.Same(results.Charts, results.Visualization);

        (ResultsChartsViewModel charts, _) = Create(null);
        ResultsChartsView view = new() { DataContext = charts };
        Window window = Host(view, 400d, 300d);
        try
        {
            Assert.Equal("結果がありません", ById<TextBlock>(view, "ResultsChartEmpty").Text);
            Assert.Contains("定量化の実行が完了", ById<TextBlock>(view, "ResultsChartEmptyReason").Text, StringComparison.Ordinal);
            Assert.False(ById<ComboBox>(view, "ResultsChartKind").IsEffectivelyVisible);

            charts.Update(DataSet(0, 2, (_, _) => default), null, null);
            Render();
            Assert.True(ById<TextBlock>(view, "ResultsChartEmpty").IsEffectivelyVisible);
            Assert.Equal(ResultsChartsViewModel.NoRowsReason, ById<TextBlock>(view, "ResultsChartEmptyReason").Text);
            Assert.False(charts.IsProvisional);

            charts.Update(DataSet(2, 1, (_, _) => new(10m, null, null, null, null), isProvisional: true), null, null);
            Render();
            Assert.False(ById<TextBlock>(view, "ResultsChartEmpty").IsEffectivelyVisible);
            Border provisional = ById<Border>(view, "ResultsChartProvisional");
            Assert.True(provisional.IsEffectivelyVisible);
            Assert.StartsWith("暫定", charts.SummaryText, StringComparison.Ordinal);
            Assert.Contains("暫定", AutomationProperties.GetName(provisional), StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Heatmap_of_20000_rows_by_20_questions_realizes_a_bounded_number_of_rows_and_stays_responsive()
    {
        Stopwatch watch = Stopwatch.StartNew();
        ResultChartDataSet data = DataSet(20_000, 20, (row, column) =>
            new((row + column) % 7 == 0 ? null : (row * (column + 1)) % 21, null, null, null, null));
        (ResultsChartsViewModel charts, RecordingChartActions actions) = Create(data);
        ResultsChartsView view = new() { DataContext = charts };
        Window window = Host(view, 1180d, 800d);
        try
        {
            charts.SelectedKind = ResultsChartKind.Heatmap;
            Render();
            Assert.Equal(20_000, charts.HeatmapRows.Count);
            ItemsControl rows = ById<ItemsControl>(view, "ResultsHeatmapChart");
            Assert.Contains(rows.GetVisualDescendants(), control => control is VirtualizingStackPanel);
            AssertBounded();

            Assert.NotNull(view.FocusHeatmapCell(0, 0));
            Render();
            Key(window, Avalonia.Input.Key.End, RawInputModifiers.Control);
            Assert.Equal($"行 20001・設問文1・{charts.HeatmapRows[^1].Cell(0).AccessibleValue}", AutomationProperties.GetName(Focused(window)!));
            Key(window, Avalonia.Input.Key.End);
            Key(window, Avalonia.Input.Key.PageDown);
            Assert.Equal("ResultsHeatmapCell-20001-Q20", AutomationProperties.GetAutomationId(Focused(window)!));
            AssertBounded();

            charts.ShowTable = true;
            Render();
            Assert.NotNull(view.FocusHeatmapCell(10_000, 19));
            Render();
            Assert.Equal("ResultsHeatmapValue-10002-Q20", AutomationProperties.GetAutomationId(Focused(window)!));
            AssertBounded();
            Assert.Empty(actions.Opened);
            watch.Stop();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"20,000 × 20 took {watch.Elapsed}.");
        }
        finally
        {
            window.Close();
        }

        void AssertBounded()
        {
            HeatmapRowControl[] realized = [.. view.GetVisualDescendants().OfType<HeatmapRowControl>()];
            Assert.InRange(realized.Length, 1, 80);
            Assert.InRange(view.GetVisualDescendants().OfType<HeatmapCell>().Count(), 20, 80 * 20);
        }
    }

    [AvaloniaTheory]
    [InlineData(1024d, 720d)]
    [InlineData(1180d, 800d)]
    public async Task P01_results_with_a_chart_filter_keep_the_normal_body_and_a_visible_clear_action(double width, double height)
    {
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync(width: width, height: height);
        fixture.NavigateTo(WorkflowStep.Results);
        ResultsOutputViewModel results = fixture.Shell.ResultsOutputViewModel;
        results.Charts.ActivateBin(results.Charts.HistogramBins.First(bin => bin.Count > 0));
        Render();

        Assert.True(results.IsRowFilterActive);
        Button clear = ById<Button>(fixture.CurrentView, "ResultsClearRowFilter");
        Assert.True(clear.IsEffectivelyVisible);
        Assert.Equal("絞り込みを解除", clear.Content);
        AssertNormalBody(fixture.Window);
        Activate(clear);
        Assert.False(results.IsRowFilterActive);
        AssertNormalBody(fixture.Window);
        fixture.AssertPassive();
    }

    private static (ResultsChartsViewModel Charts, RecordingChartActions Actions) Create(ResultChartDataSet? data)
    {
        RecordingChartActions actions = new() { Data = data };
        ResultsChartsViewModel charts = new(actions);
        actions.Charts = charts;
        charts.Update(data, null, null);
        return (charts, actions);
    }
}
