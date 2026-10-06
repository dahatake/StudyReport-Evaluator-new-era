using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

public sealed class LivePreviewViewTests
{
    [AvaloniaTheory]
    [InlineData(1180d, 800d)]
    [InlineData(1024d, 720d)]
    public void Live_preview_sits_under_the_measured_progress_inside_the_viewport_with_named_controls(
        double width,
        double height)
    {
        using Harness harness = new(width, height);
        harness.ViewModel.LivePreview.Apply(Update(2, 6, 3));
        harness.ViewModel.LivePreview.Apply(Update(2, 6, 3, LivePreviewViewModelTests.Row(
            3,
            LivePreviewRowState.Running,
            LivePreviewViewModelTests.Item("PROMPT-ROW-3", running: true),
            last: 6)));
        Render();

        ListBox list = Required<ListBox>(harness.View, "LivePreviewRowsList");
        TextBox detail = Required<TextBox>(harness.View, "LivePreviewDetail");
        TextBlock heading = Required<TextBlock>(harness.View, "LivePreviewHeading");
        TextBlock measured = Required<TextBlock>(harness.View, "DurableProgressSummary");
        Assert.Equal("速報値（Excel 行ごと・5 行）", heading.Text);
        Assert.Equal("ExecutionLivePreviewList", AutomationProperties.GetAutomationId(list));
        Assert.Equal("ExecutionLivePreviewDetail", AutomationProperties.GetAutomationId(detail));
        Assert.Equal("ExecutionLivePreviewHeading", AutomationProperties.GetAutomationId(heading));
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(list)));
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(detail)));
        Assert.True(detail.IsReadOnly);
        Assert.Equal(harness.ViewModel.LivePreview.DetailText, detail.Text);
        Assert.Contains("PROMPT-ROW-3", detail.Text, StringComparison.Ordinal);
        Assert.Same(harness.ViewModel.LivePreview.Rows, list.ItemsSource);

        Point measuredOrigin = measured.TranslatePoint(default, harness.View)!.Value;
        Point headingOrigin = heading.TranslatePoint(default, harness.View)!.Value;
        Assert.True(headingOrigin.Y >= measuredOrigin.Y, "The preview must come after the measured line.");
        Assert.True(headingOrigin.X >= measuredOrigin.X - 1d);
        AssertInside(list, harness.View);
        AssertInside(detail, harness.View);
        AssertInside(Required<Grid>(harness.View, "ExecutionLivePreview"), harness.View);
        Assert.True(list.Bounds.Height > 40d, "The row list must stay usable.");
        Assert.True(detail.Bounds.Height > 40d, "The detail must stay usable.");
        AssertInside(Required<TextBox>(harness.View, "ReservedFinalPathTextBox"), harness.View);
        AssertInside(Required<TextBox>(harness.View, "PartialPathTextBox"), harness.View);
        AssertInside(Required<Grid>(harness.View, "ExecutionActions"), harness.View);
        ScrollViewer progress = Required<ScrollViewer>(harness.View, "ExecutionProgressScroll");
        Assert.True(progress.Extent.Height <= progress.Viewport.Height + 1d, "The progress block must fit without scrolling.");
        Assert.True(progress.Extent.Width <= progress.Viewport.Width + 1d);
    }

    [AvaloniaFact]
    public void Selecting_a_row_pins_its_detail_and_clearing_returns_to_the_evaluating_row()
    {
        using Harness harness = new(1180d, 800d);
        LiveQuantificationPreviewViewModel preview = harness.ViewModel.LivePreview;
        preview.Apply(Update(2, 4, 3));
        preview.Apply(Update(2, 4, 3, LivePreviewViewModelTests.Row(
            2, LivePreviewRowState.Running, LivePreviewViewModelTests.Item("PROMPT-A", running: true))));
        preview.Apply(Update(2, 4, 3, LivePreviewViewModelTests.Row(
            4, LivePreviewRowState.Completed, LivePreviewViewModelTests.Item("PROMPT-C", running: false))));
        Render();
        ListBox list = Required<ListBox>(harness.View, "LivePreviewRowsList");
        TextBox detail = Required<TextBox>(harness.View, "LivePreviewDetail");
        Assert.Contains("PROMPT-A", detail.Text, StringComparison.Ordinal);

        list.SelectedIndex = 2;
        Render();
        Assert.Same(preview.Rows[2], preview.SelectedRow);
        Assert.Contains("PROMPT-C", detail.Text, StringComparison.Ordinal);
        Assert.Contains("Criterion C1: 8（範囲 0〜10）", detail.Text, StringComparison.Ordinal);

        list.SelectedItem = null;
        Render();
        Assert.Null(preview.SelectedRow);
        Assert.Contains("PROMPT-A", detail.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Five_hundred_rows_are_all_in_the_list_but_only_visible_rows_get_controls()
    {
        using Harness harness = new(1180d, 800d);
        harness.ViewModel.LivePreview.Apply(Update(2, 501, 3));
        Render();
        ListBox list = Required<ListBox>(harness.View, "LivePreviewRowsList");

        Assert.Equal(500, harness.ViewModel.LivePreview.RowCount);
        Assert.Equal(500, list.Items.Count);
        int realized = list.GetVisualDescendants().OfType<ListBoxItem>().Count();
        Assert.InRange(realized, 1, 60);
        Assert.Equal("Excel 行 2", harness.ViewModel.LivePreview.Rows[0].Label);
        Assert.Equal("Excel 行 501", harness.ViewModel.LivePreview.Rows[^1].Label);
    }

    private static LivePreviewUpdate Update(int first, int last, int perRow, LivePreviewUpdate? withRow = null) =>
        withRow is null
            ? new LivePreviewUpdate { FirstDataRow = first, LastDataRow = last, ItemsPerRow = perRow }
            : new LivePreviewUpdate
            {
                FirstDataRow = first,
                LastDataRow = last,
                ItemsPerRow = perRow,
                Row = withRow.Row,
            };

    private static T Required<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(root.FindControl<T>(name));

    private static void AssertInside(Control control, Control container)
    {
        Point origin = control.TranslatePoint(default, container)
            ?? throw new InvalidOperationException("The control must be attached to its container.");
        Assert.True(origin.X >= -1d && origin.Y >= -1d, control.Name);
        Assert.True(origin.X + control.Bounds.Width <= container.Bounds.Width + 1d, control.Name);
        Assert.True(origin.Y + control.Bounds.Height <= container.Bounds.Height + 1d, control.Name);
    }

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Harness : IDisposable
    {
        public Harness(double width, double height)
        {
            QuantificationDefinition definition = U04TestSupport.Definition(2, 6);
            WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
            ViewModel = U04TestSupport.ConfiguredExecutionViewModel(
                definition,
                metadata,
                new RecordingRunBoundary((_, _, _) => throw new InvalidOperationException("Layout must not start a run.")));
            View = new ExecutionView(ViewModel);
            Window = new Window { Width = width, Height = height, Content = View };
            Window.Show();
            Render();
        }

        public ExecutionViewModel ViewModel { get; }

        public ExecutionView View { get; }

        public Window Window { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            Window.Close();
        }
    }
}
