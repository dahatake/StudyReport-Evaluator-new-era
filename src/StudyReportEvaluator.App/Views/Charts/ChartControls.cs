using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Visualization;

namespace StudyReportEvaluator.App.Views.Charts;

/// <summary>Shared drawing helpers. Brushes always come from theme resources through styles.</summary>
internal static class ChartDrawing
{
    internal static FormattedText Text(Control owner, string text, double size, IBrush? brush, FontWeight weight = FontWeight.Normal) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(owner.GetValue(TextElement.FontFamilyProperty), FontStyle.Normal, weight),
            size,
            brush);

    internal static IBrush? WithOpacity(IBrush? brush, double opacity) => brush is ISolidColorBrush solid
        ? new ImmutableSolidColorBrush(solid.Color, Math.Clamp(opacity, 0d, 1d) * solid.Opacity)
        : brush;

    /// <summary>Diagonal hatching: a pattern, so that a state never depends on colour alone.</summary>
    internal static void Hatch(DrawingContext context, Rect area, IPen pen, double spacing = 6d)
    {
        using (context.PushClip(area))
        {
            for (double x = area.Left - area.Height; x < area.Right; x += spacing)
            {
                context.DrawLine(pen, new Point(x, area.Bottom), new Point(x + area.Height, area.Top));
            }
        }
    }
}

/// <summary>A bar whose filled length is <see cref="Fraction"/> of its track (histogram bins).</summary>
public sealed class ProportionalBar : Control
{
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<ProportionalBar, double>(nameof(Fraction));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<ProportionalBar, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> OutlineProperty =
        AvaloniaProperty.Register<ProportionalBar, IBrush?>(nameof(Outline));

    public static readonly StyledProperty<bool> IsHatchedProperty =
        AvaloniaProperty.Register<ProportionalBar, bool>(nameof(IsHatched));

    static ProportionalBar()
    {
        AffectsRender<ProportionalBar>(FractionProperty, FillProperty, OutlineProperty, IsHatchedProperty);
    }

    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Outline
    {
        get => GetValue(OutlineProperty);
        set => SetValue(OutlineProperty, value);
    }

    /// <summary>Blank (technical failure) bins are hatched instead of solid.</summary>
    public bool IsHatched
    {
        get => GetValue(IsHatchedProperty);
        set => SetValue(IsHatchedProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0d, 12d);

    public override void Render(DrawingContext context)
    {
        Rect track = new Rect(Bounds.Size).Deflate(0.5d);
        if (track.Width <= 0d || track.Height <= 0d)
        {
            return;
        }

        double fraction = double.IsFinite(Fraction) ? Math.Clamp(Fraction, 0d, 1d) : 0d;
        Rect filled = new(track.X, track.Y, track.Width * fraction, track.Height);
        if (IsHatched)
        {
            ChartDrawing.Hatch(context, filled, new Pen(Fill, 2d));
        }
        else
        {
            context.FillRectangle(Fill ?? Brushes.Transparent, filled);
        }

        context.DrawRectangle(new Pen(Outline, 1d), track);
    }
}

/// <summary>
/// One heatmap cell: the rate as text and as a bar length, or the blank symbol with a hatched pattern.
/// It is a lightweight focusable element with its own Japanese accessible name (FR-070).
/// </summary>
public sealed class HeatmapCell : Control
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<HeatmapCell, IBrush?>(nameof(Background));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<HeatmapCell, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> TintProperty =
        AvaloniaProperty.Register<HeatmapCell, IBrush?>(nameof(Tint));

    public static readonly StyledProperty<IBrush?> BarProperty =
        AvaloniaProperty.Register<HeatmapCell, IBrush?>(nameof(Bar));

    public static readonly StyledProperty<IBrush?> OutlineProperty =
        AvaloniaProperty.Register<HeatmapCell, IBrush?>(nameof(Outline));

    public static readonly StyledProperty<IBrush?> BlankProperty =
        AvaloniaProperty.Register<HeatmapCell, IBrush?>(nameof(Blank));

    public static readonly StyledProperty<IBrush?> FocusBrushProperty =
        AvaloniaProperty.Register<HeatmapCell, IBrush?>(nameof(FocusBrush));

    public static readonly StyledProperty<IBrush?> SelectedProperty =
        AvaloniaProperty.Register<HeatmapCell, IBrush?>(nameof(Selected));

    private HeatmapCellValue value;
    private bool isTable;
    private bool isRowSelected;

    static HeatmapCell()
    {
        FocusableProperty.OverrideDefaultValue<HeatmapCell>(true);
        AffectsRender<HeatmapCell>(BackgroundProperty, ForegroundProperty, TintProperty, BarProperty, OutlineProperty,
            BlankProperty, FocusBrushProperty, SelectedProperty, IsFocusedProperty);
    }

    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public IBrush? Tint { get => GetValue(TintProperty); set => SetValue(TintProperty, value); }

    public IBrush? Bar { get => GetValue(BarProperty); set => SetValue(BarProperty, value); }

    public IBrush? Outline { get => GetValue(OutlineProperty); set => SetValue(OutlineProperty, value); }

    public IBrush? Blank { get => GetValue(BlankProperty); set => SetValue(BlankProperty, value); }

    public IBrush? FocusBrush { get => GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }

    public IBrush? Selected { get => GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }

    /// <summary>Zero-based question column of this cell.</summary>
    public int Column { get; internal set; }

    public HeatmapCellValue Value => value;

    /// <summary>The visible text: the rate, or the blank symbol in the chart and the full text in the table.</summary>
    public string DisplayText => isTable ? value.Text : value.Symbol;

    internal void Apply(int column, HeatmapCellValue next, bool table, bool rowSelected, string accessibleName, string automationId)
    {
        Column = column;
        value = next;
        isTable = table;
        isRowSelected = rowSelected;
        AutomationProperties.SetName(this, accessibleName);
        AutomationProperties.SetAutomationId(this, automationId);
        ToolTip.SetTip(this, accessibleName);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = new Rect(Bounds.Size).Deflate(1d);
        if (bounds.Width <= 0d || bounds.Height <= 0d)
        {
            return;
        }

        context.FillRectangle(Background ?? Brushes.Transparent, bounds);
        if (!isTable && value.Kind == HeatmapCellKind.Value && value.Rate is decimal rate)
        {
            context.FillRectangle(ChartDrawing.WithOpacity(Tint, (double)rate) ?? Brushes.Transparent, bounds);
        }

        if (value.Kind == HeatmapCellKind.Blank)
        {
            ChartDrawing.Hatch(context, bounds.Deflate(2d), new Pen(ChartDrawing.WithOpacity(Blank, 0.45d), 1d), 8d);
        }

        FormattedText text = ChartDrawing.Text(this, DisplayText, 14d, Foreground,
            value.Kind == HeatmapCellKind.Blank ? FontWeight.Bold : FontWeight.Normal);
        double textTop = isTable ? (bounds.Height - text.Height) / 2d : 3d;
        double textLeft = isTable ? bounds.X + 6d : bounds.X + Math.Max(0d, (bounds.Width - text.Width) / 2d);
        using (context.PushClip(bounds))
        {
            context.DrawText(text, new Point(textLeft, bounds.Y + textTop));
        }

        if (!isTable)
        {
            Rect track = new(bounds.X + 4d, bounds.Bottom - 12d, Math.Max(0d, bounds.Width - 8d), 7d);
            if (value.Kind == HeatmapCellKind.Value && value.Rate is decimal barRate)
            {
                double fraction = Math.Clamp((double)barRate, 0d, 1d);
                context.FillRectangle(Bar ?? Brushes.Transparent, new Rect(track.X, track.Y, track.Width * fraction, track.Height));
            }

            context.DrawRectangle(new Pen(Outline, 1d), track);
        }

        if (value.Kind == HeatmapCellKind.Blank)
        {
            context.DrawRectangle(new Pen(Blank, 2d, new ImmutableDashStyle([2d, 2d], 0d)), bounds.Deflate(1d));
        }
        else
        {
            context.DrawRectangle(new Pen(isRowSelected ? Selected : Outline, isRowSelected ? 2d : 1d), bounds);
        }

        if (IsFocused)
        {
            context.DrawRectangle(new Pen(FocusBrush, 3d), bounds.Deflate(1.5d));
        }
    }
}

/// <summary>
/// One virtualized heatmap row: the Excel row number and one <see cref="HeatmapCell"/> per question.
/// Children are reused when the row container is recycled, so realized elements stay bounded.
/// </summary>
public sealed class HeatmapRowControl : Panel
{
    public const double RowHeight = 44d;
    public const double HeaderWidth = 72d;
    public const double ChartCellWidth = 44d;
    public const double TableCellWidth = 152d;

    public static readonly StyledProperty<bool> IsTableProperty =
        AvaloniaProperty.Register<HeatmapRowControl, bool>(nameof(IsTable));

    private readonly TextBlock header = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(4d, 0d),
    };

    private HeatmapRowItem? item;

    public HeatmapRowControl()
    {
        Children.Add(header);
        Height = RowHeight;
    }

    public bool IsTable
    {
        get => GetValue(IsTableProperty);
        set => SetValue(IsTableProperty, value);
    }

    public HeatmapRowItem? Item => item;

    public double CellWidth => IsTable ? TableCellWidth : ChartCellWidth;

    public IEnumerable<HeatmapCell> Cells => Children.OfType<HeatmapCell>();

    public HeatmapCell? CellAt(int column) => column >= 0 && column + 1 < Children.Count ? Children[column + 1] as HeatmapCell : null;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (item is not null)
        {
            item.PropertyChanged -= HandleItemChanged;
        }

        item = DataContext as HeatmapRowItem;
        if (item is not null && this.IsAttachedToVisualTree())
        {
            item.PropertyChanged += HandleItemChanged;
        }

        Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (item is not null)
        {
            item.PropertyChanged -= HandleItemChanged;
            item.PropertyChanged += HandleItemChanged;
            Refresh();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (item is not null)
        {
            item.PropertyChanged -= HandleItemChanged;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsTableProperty)
        {
            Refresh();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int cells = Children.Count - 1;
        header.Measure(new Size(HeaderWidth, RowHeight));
        foreach (HeatmapCell cell in Cells)
        {
            cell.Measure(new Size(CellWidth, RowHeight));
        }

        return new Size(HeaderWidth + (cells * CellWidth), RowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        header.Arrange(new Rect(0d, 0d, HeaderWidth, RowHeight));
        int column = 0;
        foreach (HeatmapCell cell in Cells)
        {
            cell.Arrange(new Rect(HeaderWidth + (column * CellWidth), 0d, CellWidth, RowHeight));
            column++;
        }

        return new Size(HeaderWidth + (column * CellWidth), RowHeight);
    }

    private void HandleItemChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        int count = item?.CellCount ?? 0;
        while (Children.Count - 1 > count)
        {
            Children.RemoveAt(Children.Count - 1);
        }

        while (Children.Count - 1 < count)
        {
            Children.Add(new HeatmapCell());
        }

        if (item is null)
        {
            header.Text = string.Empty;
            return;
        }

        header.Text = item.HeaderText;
        header.FontWeight = item.IsSelected ? FontWeight.Bold : FontWeight.Normal;
        AutomationProperties.SetName(header, item.HeaderAccessibleName);
        bool table = IsTable;
        for (int column = 0; column < count; column++)
        {
            CellAt(column)!.Apply(
                column,
                item.Cell(column),
                table,
                item.IsSelected,
                item.CellAccessibleName(column),
                item.CellAutomationId(column, table));
        }

        InvalidateMeasure();
    }
}

/// <summary>
/// A stacked bar of the allocation (FR-067): one focusable segment per part, proportional to its points,
/// with a 100 reference line on top.
/// </summary>
public sealed class AllocationBarPanel : Panel
{
    public const double BarHeight = 32d;
    public const double LabelHeight = 22d;
    public const double ScaleHeight = 20d;

    public static readonly StyledProperty<IEnumerable<AllocationSegmentItem>?> SegmentsProperty =
        AvaloniaProperty.Register<AllocationBarPanel, IEnumerable<AllocationSegmentItem>?>(nameof(Segments));

    public static readonly StyledProperty<double> ReferenceFractionProperty =
        AvaloniaProperty.Register<AllocationBarPanel, double>(nameof(ReferenceFraction), 1d);

    public static readonly StyledProperty<string?> ReferenceNameProperty =
        AvaloniaProperty.Register<AllocationBarPanel, string?>(nameof(ReferenceName));

    private readonly AllocationReferenceLine referenceLine = new();

    public AllocationBarPanel()
    {
        AutomationProperties.SetAutomationId(referenceLine, "DesignAllocationReferenceLine");
        Children.Add(referenceLine);
    }

    public IEnumerable<AllocationSegmentItem>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double ReferenceFraction
    {
        get => GetValue(ReferenceFractionProperty);
        set => SetValue(ReferenceFractionProperty, value);
    }

    public string? ReferenceName
    {
        get => GetValue(ReferenceNameProperty);
        set => SetValue(ReferenceNameProperty, value);
    }

    public IEnumerable<AllocationSegmentControl> SegmentControls => Children.OfType<AllocationSegmentControl>();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SegmentsProperty)
        {
            RebuildSegments();
        }
        else if (change.Property == ReferenceFractionProperty)
        {
            InvalidateArrange();
        }
        else if (change.Property == ReferenceNameProperty)
        {
            AutomationProperties.SetName(referenceLine, ReferenceName);
            ToolTip.SetTip(referenceLine, ReferenceName);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = ScaleHeight + BarHeight + LabelHeight;
        foreach (Control child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, height));
        }

        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 200d;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = finalSize.Width;
        foreach (AllocationSegmentControl segment in SegmentControls)
        {
            AllocationSegmentItem item = segment.Item!;
            segment.Arrange(new Rect(item.StartFraction * width, ScaleHeight, item.WidthFraction * width, BarHeight + LabelHeight));
        }

        double x = Math.Clamp(ReferenceFraction, 0d, 1d) * width;
        referenceLine.LineX = x;
        referenceLine.Arrange(new Rect(0d, 0d, width, finalSize.Height));
        return finalSize;
    }

    private void RebuildSegments()
    {
        foreach (AllocationSegmentControl old in SegmentControls.ToArray())
        {
            Children.Remove(old);
        }

        int index = 0;
        foreach (AllocationSegmentItem item in Segments ?? [])
        {
            Children.Insert(index++, new AllocationSegmentControl(item));
        }

        InvalidateMeasure();
    }
}

/// <summary>One focusable allocation segment; its code text is drawn below the bar for contrast.</summary>
public sealed class AllocationSegmentControl : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<AllocationSegmentControl, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> AlternateFillProperty =
        AvaloniaProperty.Register<AllocationSegmentControl, IBrush?>(nameof(AlternateFill));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<AllocationSegmentControl, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> SeparatorProperty =
        AvaloniaProperty.Register<AllocationSegmentControl, IBrush?>(nameof(Separator));

    public static readonly StyledProperty<IBrush?> OutlineProperty =
        AvaloniaProperty.Register<AllocationSegmentControl, IBrush?>(nameof(Outline));

    public static readonly StyledProperty<IBrush?> FocusBrushProperty =
        AvaloniaProperty.Register<AllocationSegmentControl, IBrush?>(nameof(FocusBrush));

    static AllocationSegmentControl()
    {
        FocusableProperty.OverrideDefaultValue<AllocationSegmentControl>(true);
        AffectsRender<AllocationSegmentControl>(FillProperty, AlternateFillProperty, ForegroundProperty, SeparatorProperty,
            OutlineProperty, FocusBrushProperty, IsFocusedProperty);
    }

    public AllocationSegmentControl()
    {
    }

    internal AllocationSegmentControl(AllocationSegmentItem item)
    {
        Item = item;
        AutomationProperties.SetAutomationId(this, item.AutomationId);
        AutomationProperties.SetName(this, item.AccessibleName);
        ToolTip.SetTip(this, item.AccessibleName);
        item.PropertyChanged += (_, _) =>
        {
            AutomationProperties.SetName(this, item.AccessibleName);
            ToolTip.SetTip(this, item.AccessibleName);
            InvalidateVisual();
        };
    }

    public AllocationSegmentItem? Item { get; }

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public IBrush? AlternateFill { get => GetValue(AlternateFillProperty); set => SetValue(AlternateFillProperty, value); }

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public IBrush? Separator { get => GetValue(SeparatorProperty); set => SetValue(SeparatorProperty, value); }

    /// <summary>A foreground-coloured outline keeps every segment visible in high contrast.</summary>
    public IBrush? Outline { get => GetValue(OutlineProperty); set => SetValue(OutlineProperty, value); }

    public IBrush? FocusBrush { get => GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Item is null || Bounds.Width <= 0d)
        {
            return;
        }

        Rect bar = new(0d, 0d, Bounds.Width, AllocationBarPanel.BarHeight);
        // Base and special are hatched; questions alternate two fills and are separated by gaps.
        IBrush? fill = Item.Kind == AllocationSegmentKind.Question && Item.Index % 2 == 1 ? AlternateFill : Fill;
        context.FillRectangle(fill ?? Brushes.Transparent, bar);
        if (Item.Kind != AllocationSegmentKind.Question)
        {
            ChartDrawing.Hatch(context, bar, new Pen(Separator, 1.5d), 7d);
        }

        context.DrawRectangle(new Pen(Separator, 2d), bar);
        context.DrawRectangle(new Pen(Outline, 1d), bar.Deflate(2d));
        string label = Item.Marker + Item.Code;
        FormattedText text = ChartDrawing.Text(this, label, 14d, Foreground, Item.IsSelected ? FontWeight.Bold : FontWeight.Normal);
        if (text.Width <= Bounds.Width)
        {
            context.DrawText(text, new Point((Bounds.Width - text.Width) / 2d, AllocationBarPanel.BarHeight + 2d));
        }

        if (IsFocused)
        {
            context.DrawRectangle(new Pen(FocusBrush, 3d), new Rect(Bounds.Size).Deflate(1.5d));
        }
    }
}

/// <summary>The dashed 100 line with its label; it spans the panel and never takes pointer input.</summary>
public sealed class AllocationReferenceLine : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<AllocationReferenceLine, IBrush?>(nameof(Stroke));

    private double lineX;

    static AllocationReferenceLine()
    {
        AffectsRender<AllocationReferenceLine>(StrokeProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<AllocationReferenceLine>(false);
    }

    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    /// <summary>The horizontal position of the 100 line within this control.</summary>
    public double LineX
    {
        get => lineX;
        internal set
        {
            if (Math.Abs(lineX - value) > 0.01d)
            {
                lineX = value;
                InvalidateVisual();
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        double x = Math.Clamp(LineX, 1d, Math.Max(1d, Bounds.Width - 1d));
        FormattedText text = ChartDrawing.Text(this, "100", 14d, Stroke, FontWeight.SemiBold);
        double left = Math.Clamp(x - (text.Width / 2d), 0d, Math.Max(0d, Bounds.Width - text.Width));
        context.DrawText(text, new Point(left, -2d));
        context.DrawLine(
            new Pen(Stroke, 2d, new ImmutableDashStyle([3d, 2d], 0d)),
            new Point(x, AllocationBarPanel.ScaleHeight - 4d),
            new Point(x, AllocationBarPanel.ScaleHeight + AllocationBarPanel.BarHeight + 4d));
    }
}
