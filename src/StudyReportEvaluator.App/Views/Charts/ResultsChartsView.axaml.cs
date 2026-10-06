using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Visualization;

namespace StudyReportEvaluator.App.Views.Charts;

/// <summary>
/// The 「図」 panel content. Keyboard: arrows move between bins, cells and rows; Enter selects (FR-070).
/// Selection only calls the view model; the view model never writes files or calls AI (FR-071).
/// </summary>
public sealed partial class ResultsChartsView : UserControl
{
    private const int PageRows = 10;
    private readonly ListBox histogramChart;
    private readonly ListBox histogramTable;
    private readonly ListBox similarityList;
    private readonly ItemsControl heatmapChartRows;
    private readonly ItemsControl heatmapTableRows;
    private readonly ScrollViewer heatmapChartScroll;
    private readonly ScrollViewer heatmapTableScroll;
    private readonly ScrollViewer heatmapChartHeaderScroll;
    private readonly ScrollViewer heatmapTableHeaderScroll;
    private ResultsChartsViewModel? observed;
    private bool revealQueued;

    public ResultsChartsView()
    {
        InitializeComponent();
        histogramChart = this.FindControl<ListBox>("HistogramChartList")!;
        histogramTable = this.FindControl<ListBox>("HistogramTableList")!;
        similarityList = this.FindControl<ListBox>("SimilarityList")!;
        heatmapChartRows = this.FindControl<ItemsControl>("HeatmapChartRows")!;
        heatmapTableRows = this.FindControl<ItemsControl>("HeatmapTableRows")!;
        heatmapChartScroll = this.FindControl<ScrollViewer>("HeatmapChartScroll")!;
        heatmapTableScroll = this.FindControl<ScrollViewer>("HeatmapTableScroll")!;
        heatmapChartHeaderScroll = this.FindControl<ScrollViewer>("HeatmapChartHeaderScroll")!;
        heatmapTableHeaderScroll = this.FindControl<ScrollViewer>("HeatmapTableHeaderScroll")!;

        // ListBoxItem handles Enter for selection before it bubbles to the list.
        foreach (ListBox list in new[] { histogramChart, histogramTable, similarityList })
        {
            list.AddHandler(KeyDownEvent, HandleListKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
            list.AddHandler(TappedEvent, HandleListTapped, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        // Tunnel: ItemsControl's own directional navigation (a bubble class handler) must not run first.
        foreach (ItemsControl rows in new[] { heatmapChartRows, heatmapTableRows })
        {
            rows.AddHandler(KeyDownEvent, HandleHeatmapKeyDown, RoutingStrategies.Tunnel);
            rows.AddHandler(TappedEvent, HandleHeatmapTapped, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        heatmapChartScroll.ScrollChanged += (_, _) => SyncHeader(heatmapChartScroll, heatmapChartHeaderScroll);
        heatmapTableScroll.ScrollChanged += (_, _) => SyncHeader(heatmapTableScroll, heatmapTableHeaderScroll);
        DataContextChanged += (_, _) => Observe();
    }

    public ResultsChartsViewModel? ViewModel => DataContext as ResultsChartsViewModel;

    /// <summary>The visible heatmap rows control (chart or table), or null when the heatmap is hidden.</summary>
    public ItemsControl? ActiveHeatmapRows => ViewModel switch
    {
        { ShowHeatmapChart: true } => heatmapChartRows,
        { ShowHeatmapTable: true } => heatmapTableRows,
        _ => null,
    };

    /// <summary>Moves keyboard focus to a heatmap cell, scrolling its virtualized row into view.</summary>
    public HeatmapCell? FocusHeatmapCell(int rowIndex, int column)
    {
        if (ActiveHeatmapRows is not { } rows || ViewModel is not { } viewModel || viewModel.HeatmapRows.Count == 0)
        {
            return null;
        }

        rowIndex = Math.Clamp(rowIndex, 0, viewModel.HeatmapRows.Count - 1);
        column = Math.Clamp(column, 0, Math.Max(0, viewModel.HeatmapRows[rowIndex].CellCount - 1));
        ScrollViewer scroll = ReferenceEquals(rows, heatmapChartRows) ? heatmapChartScroll : heatmapTableScroll;
        RevealCell(scroll, rowIndex, column, ReferenceEquals(rows, heatmapTableRows));
        HeatmapRowControl? rowControl = RealizeRow(rows, rowIndex);
        if (rowControl?.CellAt(column) is not { } cell)
        {
            return null;
        }

        cell.Focus(NavigationMethod.Directional);
        return cell;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Observe();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (observed is not null)
        {
            observed.PropertyChanged -= HandleViewModelChanged;
            observed = null;
        }
    }

    // Rows have a fixed height, so the target offset is exact; this avoids estimation feedback of
    // bring-into-view requests across 20,000 virtualized rows.
    private static void RevealCell(ScrollViewer scroll, int rowIndex, int column, bool table)
    {
        double cellWidth = table ? HeatmapRowControl.TableCellWidth : HeatmapRowControl.ChartCellWidth;
        Size viewport = scroll.Viewport;
        double top = rowIndex * HeatmapRowControl.RowHeight;
        double y = scroll.Offset.Y;
        if (top < y)
        {
            y = top;
        }
        else if (top + HeatmapRowControl.RowHeight > y + viewport.Height)
        {
            y = top + HeatmapRowControl.RowHeight - viewport.Height;
        }

        double x = scroll.Offset.X;
        double left = HeatmapRowControl.HeaderWidth + (column * cellWidth);
        if (column < 0)
        {
            // Rows only: keep the horizontal position.
        }
        else if (left - HeatmapRowControl.HeaderWidth < x)
        {
            x = left - HeatmapRowControl.HeaderWidth;
        }
        else if (left + cellWidth > x + viewport.Width)
        {
            x = left + cellWidth - viewport.Width;
        }

        Vector next = new(Math.Max(0d, x), Math.Max(0d, y));
        if (next != scroll.Offset)
        {
            scroll.Offset = next;
            scroll.UpdateLayout();
        }
    }

    private static HeatmapRowControl? RealizeRow(ItemsControl rows, int rowIndex)
    {
        HeatmapRowControl? found = FindRowControl(rows.ContainerFromIndex(rowIndex));
        if (found is null)
        {
            rows.ScrollIntoView(rowIndex);
            found = FindRowControl(rows.ContainerFromIndex(rowIndex));
        }

        return found;
    }

    private static HeatmapRowControl? FindRowControl(Control? container) => container switch
    {
        null => null,
        HeatmapRowControl row => row,
        _ => container.GetVisualDescendants().OfType<HeatmapRowControl>().FirstOrDefault(),
    };

    private static void SyncHeader(ScrollViewer body, ScrollViewer header)
    {
        if (Math.Abs(header.Offset.X - body.Offset.X) > 0.1d)
        {
            header.Offset = new Vector(body.Offset.X, 0d);
        }
    }

    private static int IndexOf(IReadOnlyList<HeatmapRowItem> rows, HeatmapRowItem item)
    {
        // Rows are in ascending Excel row order.
        int low = 0;
        int high = rows.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int comparison = rows[middle].SourceRow.CompareTo(item.SourceRow);
            if (comparison == 0)
            {
                return ReferenceEquals(rows[middle], item) ? middle : -1;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    private void Observe()
    {
        if (observed is not null)
        {
            observed.PropertyChanged -= HandleViewModelChanged;
        }

        observed = this.IsAttachedToVisualTree() ? ViewModel : null;
        if (observed is not null)
        {
            observed.PropertyChanged += HandleViewModelChanged;
            QueueRevealSelection();
        }
    }

    private void HandleViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResultsChartsViewModel.ShowHeatmapChart)
            or nameof(ResultsChartsViewModel.ShowHeatmapTable)
            or nameof(ResultsChartsViewModel.ShowSimilarity)
            or nameof(ResultsChartsViewModel.HeatmapRows)
            or nameof(ResultsChartsViewModel.SelectedSourceRow))
        {
            QueueRevealSelection();
        }
    }

    // FR-069: a representation that appears keeps the selected row in view, unless the reader is moving in it.
    private void QueueRevealSelection()
    {
        if (revealQueued)
        {
            return;
        }

        revealQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            revealQueued = false;
            if (ViewModel is not { } viewModel || !this.IsAttachedToVisualTree())
            {
                return;
            }

            if (ActiveHeatmapRows is { } rows && !rows.IsKeyboardFocusWithin && viewModel.SelectedHeatmapRowIndex is int index and >= 0)
            {
                RevealCell(
                    ReferenceEquals(rows, heatmapChartRows) ? heatmapChartScroll : heatmapTableScroll,
                    index,
                    -1,
                    ReferenceEquals(rows, heatmapTableRows));
            }

            if (viewModel.ShowSimilarity && !similarityList.IsKeyboardFocusWithin && viewModel.FocusedSimilarityEntry is { } entry)
            {
                similarityList.ScrollIntoView(entry);
            }
        }, DispatcherPriority.Loaded);
    }

    private void HandleListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ListBox list)
        {
            Activate(list, list.SelectedItem);
            e.Handled = true;
        }
    }

    private void HandleListTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox list && e.Source is Visual source
            && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } container)
        {
            Activate(list, container.DataContext);
        }
    }

    private void Activate(ListBox list, object? item)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (ReferenceEquals(list, similarityList))
        {
            viewModel.ActivateSimilarityEntry(item as SimilarityEntryItem);
        }
        else
        {
            viewModel.ActivateBin(item as HistogramBinItem);
        }
    }

    private void HandleHeatmapTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is { } viewModel && e.Source is Visual source
            && source.FindAncestorOfType<HeatmapCell>(includeSelf: true) is { } cell
            && cell.FindAncestorOfType<HeatmapRowControl>() is { Item: { } row })
        {
            viewModel.OpenCell(row, cell.Column);
        }
    }

    private void HandleHeatmapKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel
            || e.Source is not Visual source
            || source.FindAncestorOfType<HeatmapCell>(includeSelf: true) is not { } cell
            || cell.FindAncestorOfType<HeatmapRowControl>() is not { Item: { } row })
        {
            return;
        }

        int rowIndex = IndexOf(viewModel.HeatmapRows, row);
        if (rowIndex < 0)
        {
            return;
        }

        int column = cell.Column;
        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Enter:
            case Key.Space:
                viewModel.OpenCell(row, column);
                e.Handled = true;
                return;
            case Key.Left:
                column--;
                break;
            case Key.Right:
                column++;
                break;
            case Key.Up:
                rowIndex--;
                break;
            case Key.Down:
                rowIndex++;
                break;
            case Key.PageUp:
                rowIndex -= PageRows;
                break;
            case Key.PageDown:
                rowIndex += PageRows;
                break;
            case Key.Home when control:
                rowIndex = 0;
                break;
            case Key.End when control:
                rowIndex = viewModel.HeatmapRows.Count - 1;
                break;
            case Key.Home:
                column = 0;
                break;
            case Key.End:
                column = row.CellCount - 1;
                break;
            default:
                return;
        }

        e.Handled = true;
        FocusHeatmapCell(rowIndex, Math.Clamp(column, 0, Math.Max(0, row.CellCount - 1)));
    }
}
