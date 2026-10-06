using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Visualization;

namespace StudyReportEvaluator.App.Views.Charts;

/// <summary>The 「配点構成」 panel content: Left/Right move between segments, Enter selects a question (FR-070).</summary>
public sealed partial class AllocationChartView : UserControl
{
    private readonly AllocationBarPanel bar;

    public AllocationChartView()
    {
        InitializeComponent();
        bar = this.FindControl<AllocationBarPanel>("AllocationBar")!;
        bar.AddHandler(KeyDownEvent, HandleBarKeyDown, RoutingStrategies.Bubble);
        bar.AddHandler(TappedEvent, HandleBarTapped, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public AllocationChartViewModel? ViewModel => DataContext as AllocationChartViewModel;

    private void HandleBarTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<AllocationSegmentControl>(includeSelf: true) is { Item: { } item })
        {
            ViewModel?.ActivateSegment(item);
        }
    }

    private void HandleBarKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not Visual source
            || source.FindAncestorOfType<AllocationSegmentControl>(includeSelf: true) is not { Item: { } item })
        {
            return;
        }

        AllocationSegmentControl[] segments = [.. bar.SegmentControls];
        int index = Array.FindIndex(segments, segment => ReferenceEquals(segment.Item, item));
        switch (e.Key)
        {
            case Key.Enter:
            case Key.Space:
                ViewModel?.ActivateSegment(item);
                e.Handled = true;
                return;
            case Key.Left:
            case Key.Up:
                index--;
                break;
            case Key.Right:
            case Key.Down:
                index++;
                break;
            case Key.Home:
                index = 0;
                break;
            case Key.End:
                index = segments.Length - 1;
                break;
            default:
                return;
        }

        e.Handled = true;
        if (segments.Length > 0)
        {
            AllocationSegmentControl target = segments[Math.Clamp(index, 0, segments.Length - 1)];
            target.Focus(NavigationMethod.Directional);
            if (ViewModel is { } viewModel)
            {
                viewModel.FocusedSegment = target.Item;
            }
        }
    }
}
