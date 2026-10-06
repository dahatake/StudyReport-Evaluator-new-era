using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StudyReportEvaluator.App.Workspace;

/// <summary>
/// NFR-UX-003 workspace built from standard Avalonia parts (Grid, GridSplitter, TabStrip; AS-011).
/// Panels can be moved, re-oriented, resized (splitter or menu), grouped into tabs, hidden, maximized
/// and restored. Every operation is available from the per-panel menu without dragging (NFR-UX-006).
/// The layout comes from <see cref="WorkspaceLayoutService"/>; the displayed arrangement is reflowed
/// for narrow or scaled viewports without changing the saved layout (NFR-UX-008).
/// </summary>
public sealed class PanelWorkspace : Control
{
    public const double CompactViewportWidth = 1180d;
    public const double SingleViewportWidth = 760d;
    public const double SingleScale = 2d;
    public const double SplitterSize = 6d;
    public const double HeaderHeight = 44d;

    private static readonly Geometry MenuIconGeometry = Geometry.Parse(
        "M 1 8 A 2 2 0 1 1 5 8 A 2 2 0 1 1 1 8 Z M 6 8 A 2 2 0 1 1 10 8 A 2 2 0 1 1 6 8 Z M 11 8 A 2 2 0 1 1 15 8 A 2 2 0 1 1 11 8 Z");

    public static readonly StyledProperty<string> ScreenKeyProperty =
        AvaloniaProperty.Register<PanelWorkspace, string>(nameof(ScreenKey), string.Empty);

    /// <summary>Supplied by the application window; views shown on their own use generic layouts.</summary>
    public static readonly AttachedProperty<WorkspaceLayoutService?> LayoutServiceProperty =
        AvaloniaProperty.RegisterAttached<PanelWorkspace, Control, WorkspaceLayoutService?>("LayoutService", inherits: true);

    /// <summary>Additional OS text scale applied by the host (NFR-UX-010). It counts as display scaling.</summary>
    public static readonly AttachedProperty<double> ContentScaleProperty =
        AvaloniaProperty.RegisterAttached<PanelWorkspace, Control, double>("ContentScale", 1d, inherits: true);

    private readonly ScrollViewer scroll;
    private readonly Grid grid;
    private readonly Dictionary<string, Button> menuButtons = new(StringComparer.Ordinal);
    private readonly List<string> recency = [];
    private readonly List<Control> chrome = [];
    private readonly List<(WorkspaceEffectiveGroup Group, TabStrip? Tabs)> displayed = [];
    private WorkspaceLayoutService? service;
    private WorkspaceLayoutService? fallback;
    private WorkspaceLayoutService? subscribed;
    private TopLevel? topLevel;
    private WorkspaceEffectiveLayout? effective;
    private bool attached;
    private bool stacked;
    private double stackedHeight;
    private bool applyingSelection;
    private string renderedInline = string.Empty;

    public PanelWorkspace()
    {
        grid = new Grid();
        scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = grid,
        };
        LogicalChildren.Add(scroll);
        VisualChildren.Add(scroll);
        Panels.CollectionChanged += HandlePanelsChanged;
        Focusable = false;
    }

    /// <summary>Raised after the displayed panels or the selected tabs change.</summary>
    public event EventHandler? PresentationChanged;

    [Content]
    public AvaloniaList<WorkspacePanel> Panels { get; } = [];

    public string ScreenKey
    {
        get => GetValue(ScreenKeyProperty);
        set => SetValue(ScreenKeyProperty, value);
    }

    public WorkspaceLayoutService Service => service ?? (fallback ??= WorkspaceLayoutService.CreateGeneric());

    /// <summary>The saved (or default) layout of this screen; reflow never changes it.</summary>
    public WorkspaceLayout Layout => Service.GetLayout(ScreenKey);

    public WorkspaceEffectiveLayout? EffectiveLayout => effective;

    public WorkspaceReflowMode Mode => effective?.Mode ?? WorkspaceReflowMode.Full;

    public bool IsStacked => stacked;

    public static WorkspaceLayoutService? GetLayoutService(Control control) => control.GetValue(LayoutServiceProperty);

    public static void SetLayoutService(Control control, WorkspaceLayoutService? value) =>
        control.SetValue(LayoutServiceProperty, value);

    public static double GetContentScale(Control control) => control.GetValue(ContentScaleProperty);

    public static void SetContentScale(Control control, double value) => control.SetValue(ContentScaleProperty, value);

    public WorkspacePanel? FindPanel(string panelId) =>
        Panels.FirstOrDefault(panel => string.Equals(panel.PanelId, panelId, StringComparison.Ordinal));

    public Button? GetMenuButton(string panelId) => menuButtons.GetValueOrDefault(panelId);

    public string TitleOf(string panelId) => FindPanel(panelId)?.Title ?? panelId;

    /// <summary>True when the panel is displayed and is the selected tab of its group.</summary>
    public bool IsPresented(string panelId) =>
        displayed.Any(entry => string.Equals(SelectedIn(entry.Group), panelId, StringComparison.Ordinal));

    /// <summary>True when the panel is displayed in a group of its own (not as a tab).</summary>
    public bool IsAlone(string panelId) =>
        displayed.Any(entry => entry.Group.PanelIds.Length == 1
            && string.Equals(entry.Group.PanelIds[0], panelId, StringComparison.Ordinal));

    public bool CanExecute(WorkspacePanelOperation operation, string panelId) =>
        FindPanel(panelId) is not null && Layout.CanExecute(operation, panelId);

    public void Execute(WorkspacePanelOperation operation, string panelId)
    {
        if (!CanExecute(operation, panelId))
        {
            return;
        }

        if (operation is WorkspacePanelOperation.Show or WorkspacePanelOperation.Maximize
            or WorkspacePanelOperation.SeparateFromTabs or WorkspacePanelOperation.MergeIntoTabs)
        {
            MoveToFront(panelId);
        }

        Service.SetLayout(ScreenKey, Layout.Execute(operation, panelId));
    }

    /// <summary>NFR-UX-005: restore the persona default. Panel content and its state are untouched.</summary>
    public void ResetLayout()
    {
        // The default arrangement also shows each group's first tab again.
        recency.Clear();
        Service.ResetLayout(ScreenKey);
    }

    /// <summary>Brings a panel to the front of its tab group, showing or restoring it when needed.</summary>
    public void Reveal(string panelId)
    {
        if (FindPanel(panelId) is null)
        {
            return;
        }

        MoveToFront(panelId);
        WorkspaceLayout layout = Layout;
        WorkspaceLayout next = layout;
        if (next.IsHidden(panelId))
        {
            next = next.Execute(WorkspacePanelOperation.Show, panelId);
        }

        if (next.MaximizedPanelId is { } maximized && !string.Equals(maximized, panelId, StringComparison.Ordinal))
        {
            next = next.Execute(WorkspacePanelOperation.Restore, panelId);
        }

        if (!next.Equals(layout))
        {
            Service.SetLayout(ScreenKey, next);
        }
        else
        {
            RenderOrSelect();
        }
    }

    /// <summary>Selects the panel's tab if it is displayed, without showing, restoring or saving anything.</summary>
    public void Select(string panelId)
    {
        if (FindPanel(panelId) is null)
        {
            return;
        }

        MoveToFront(panelId);
        RenderOrSelect();
    }

    /// <summary>Selects the next most recent tab of the panel's group; a panel shown alone stays shown.</summary>
    public void Conceal(string panelId)
    {
        if (displayed.FirstOrDefault(entry => entry.Group.PanelIds.Contains(panelId, StringComparer.Ordinal)).Group is not { } group
            || !string.Equals(SelectedIn(group), panelId, StringComparison.Ordinal) || group.PanelIds.Length < 2)
        {
            return;
        }

        recency.Remove(panelId);
        if (string.Equals(SelectedIn(group), panelId, StringComparison.Ordinal))
        {
            MoveToFront(group.PanelIds.First(id => !string.Equals(id, panelId, StringComparison.Ordinal)));
        }

        RenderOrSelect();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        scroll.Measure(availableSize);
        return scroll.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        scroll.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not null)
        {
            topLevel.PropertyChanged += HandleTopLevelPropertyChanged;
            topLevel.ScalingChanged += HandleTopLevelScalingChanged;
        }

        AttachService(GetLayoutService(this));
        Render();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        if (topLevel is not null)
        {
            topLevel.PropertyChanged -= HandleTopLevelPropertyChanged;
            topLevel.ScalingChanged -= HandleTopLevelScalingChanged;
            topLevel = null;
        }

        AttachService(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LayoutServiceProperty && attached)
        {
            AttachService(GetLayoutService(this));
            Render();
        }
        else if ((change.Property == ScreenKeyProperty || change.Property == ContentScaleProperty) && attached)
        {
            Render();
        }
        else if (change.Property == BoundsProperty && attached)
        {
            HandleSizeChanged();
        }
    }

    private void AttachService(WorkspaceLayoutService? value)
    {
        service = value;
        WorkspaceLayoutService? next = attached ? Service : null;
        if (ReferenceEquals(subscribed, next))
        {
            return;
        }

        if (subscribed is not null)
        {
            subscribed.LayoutChanged -= HandleLayoutChanged;
        }

        subscribed = next;
        if (subscribed is not null)
        {
            subscribed.LayoutChanged += HandleLayoutChanged;
        }
    }

    private void HandleLayoutChanged(object? sender, WorkspaceLayoutChangedEventArgs e)
    {
        if (attached && (e.ScreenKey is null || string.Equals(e.ScreenKey, ScreenKey, StringComparison.Ordinal)))
        {
            Render();
        }
    }

    private void HandleTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TopLevel.ClientSizeProperty)
        {
            RenderIfModeChanged();
        }
    }

    private void HandleTopLevelScalingChanged(object? sender, EventArgs e) => RenderIfModeChanged();

    private void HandleSizeChanged()
    {
        if (!RenderIfModeChanged() && stacked)
        {
            UpdateStackedHeight();
        }
    }

    private bool RenderIfModeChanged()
    {
        if (!attached || effective is null)
        {
            return false;
        }

        WorkspaceLayout layout = Layout;
        WorkspaceReflowMode mode = ComputeMode(layout);
        bool nextStacked = ComputeStacked(layout.Reflow(mode));
        if (mode == effective.Mode && nextStacked == stacked
            && string.Equals(InlineSignature(), renderedInline, StringComparison.Ordinal))
        {
            return false;
        }

        Render();
        return true;
    }

    private void HandlePanelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (WorkspacePanel panel in e.OldItems?.OfType<WorkspacePanel>() ?? [])
        {
            grid.Children.Remove(panel);
            recency.Remove(panel.PanelId);
            menuButtons.Remove(panel.PanelId);
        }

        foreach (WorkspacePanel panel in e.NewItems?.OfType<WorkspacePanel>() ?? [])
        {
            grid.Children.Add(panel);
            panel.ClipToBounds = true;
        }

        if (attached)
        {
            Render();
        }
    }

    private WorkspaceReflowMode ComputeMode(WorkspaceLayout layout)
    {
        double contentScale = Math.Max(1d, GetContentScale(this));
        double scale = (topLevel?.RenderScaling ?? 1d) * contentScale;
        double viewportWidth = (topLevel?.ClientSize.Width ?? Bounds.Width) / contentScale;
        WorkspaceReflowMode mode = viewportWidth <= SingleViewportWidth || scale >= SingleScale
            ? WorkspaceReflowMode.Single
            : viewportWidth < CompactViewportWidth ? WorkspaceReflowMode.Compact : WorkspaceReflowMode.Full;

        // A narrower host than the window (or large panel minimums) degrades further: never overflow sideways.
        double available = Bounds.Width;
        while (mode != WorkspaceReflowMode.Single && available > 0d && !FitsHorizontally(layout.Reflow(mode), available))
        {
            mode++;
        }

        return mode;
    }

    private bool FitsHorizontally(WorkspaceEffectiveLayout layout, double width)
    {
        if (layout.Orientation != WorkspaceOrientation.Horizontal || layout.MaximizedPanelId is not null)
        {
            return true;
        }

        double required = layout.Groups.Sum(GroupMinWidth) + Math.Max(0, layout.Groups.Length - 1) * SplitterSize;
        return required <= width + 0.5d;
    }

    private bool ComputeStacked(WorkspaceEffectiveLayout layout)
    {
        if (layout.MaximizedPanelId is not null || layout.Orientation != WorkspaceOrientation.Vertical)
        {
            return false;
        }

        if (layout.Mode == WorkspaceReflowMode.Single)
        {
            return true;
        }

        double height = Bounds.Height;
        return height > 0d && RequiredStackHeight(layout.Groups) + Math.Max(0, layout.Groups.Length - 1) * SplitterSize
            > height + 0.5d;
    }

    private double RequiredStackHeight(IEnumerable<WorkspaceEffectiveGroup> groups) =>
        groups.Sum(group => GroupMinHeight(group) + (HasInlineHeader(group) ? 0d : HeaderHeight));

    private double GroupMinWidth(WorkspaceEffectiveGroup group) =>
        group.PanelIds.Select(FindPanel).OfType<WorkspacePanel>().Select(panel => panel.MinContentWidth).DefaultIfEmpty(0d).Max();

    private double GroupMinHeight(WorkspaceEffectiveGroup group) =>
        group.PanelIds.Select(FindPanel).OfType<WorkspacePanel>().Select(panel => panel.MinContentHeight).DefaultIfEmpty(0d).Max();

    // The group's tabs and menu go into the selected panel's own header slot when it has one,
    // so that no extra title row is needed; otherwise a header row is added above the group.
    private ContentControl? HeaderSlotOf(WorkspaceEffectiveGroup group) =>
        SelectedIn(group) is { } selected && FindPanel(selected) is { } panel
            && (group.PanelIds.Length == 1 || EstimatedGroupWidth(group) >= panel.TabsInHeaderSlotMinWidth)
                ? panel.FindHeaderSlot()
                : null;

    private double EstimatedGroupWidth(WorkspaceEffectiveGroup group)
    {
        if (effective is not { Orientation: WorkspaceOrientation.Horizontal } layout || layout.Groups.Length < 2)
        {
            return Bounds.Width;
        }

        double total = layout.Groups.Sum(item => item.Size);
        return total > 0d ? Math.Max(0d, Bounds.Width - (layout.Groups.Length - 1) * SplitterSize) * group.Size / total : 0d;
    }

    private string InlineSignature() => effective is null
        ? string.Empty
        : string.Concat(effective.Groups.Select(group => HasInlineHeader(group) ? '1' : '0'));

    private bool HasInlineHeader(WorkspaceEffectiveGroup group) => HeaderSlotOf(group) is not null;

    private void Render()
    {
        if (Panels.Count == 0)
        {
            return;
        }

        recency.RemoveAll(id => FindPanel(id) is null);

        IInputElement? focused = topLevel?.FocusManager?.GetFocusedElement();
        string? focusedMenu = menuButtons.FirstOrDefault(pair => ReferenceEquals(pair.Value, focused)).Key;
        WorkspaceLayout layout = Layout;
        WorkspaceReflowMode mode = ComputeMode(layout);
        WorkspaceEffectiveLayout reflowed = layout.Reflow(mode);
        if (reflowed.MaximizedPanelId is { } maximized)
        {
            int source = layout.GroupIndexOf(maximized);
            reflowed = reflowed with
            {
                Orientation = WorkspaceOrientation.Horizontal,
                Groups = [new WorkspaceEffectiveGroup([maximized], 1d, source)],
            };
        }

        effective = reflowed;
        stacked = ComputeStacked(reflowed);
        ClearChrome();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        displayed.Clear();
        // Visibility is set once in ApplySelection: hiding a still-shown panel would drop its focus.
        foreach (WorkspacePanel panel in Panels)
        {
            Grid.SetRow(panel, 0);
            Grid.SetColumn(panel, 0);
            Grid.SetRowSpan(panel, 1);
            Grid.SetColumnSpan(panel, 1);
        }

        ImmutableArray<WorkspaceEffectiveGroup> groups = reflowed.Groups;
        if (reflowed.Orientation == WorkspaceOrientation.Horizontal)
        {
            bool anyHeader = groups.Any(group => !HasInlineHeader(group));
            if (anyHeader)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            for (int index = 0; index < groups.Length; index++)
            {
                if (index > 0)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition(SplitterSize, GridUnitType.Pixel));
                    AddSplitter(GridResizeDirection.Columns, row: 0, column: grid.ColumnDefinitions.Count - 1,
                        span: grid.RowDefinitions.Count, index);
                }

                grid.ColumnDefinitions.Add(new ColumnDefinition(groups[index].Size, GridUnitType.Star)
                {
                    MinWidth = GroupMinWidth(groups[index]),
                });
                int column = grid.ColumnDefinitions.Count - 1;
                if (HasInlineHeader(groups[index]))
                {
                    PlaceGroup(groups[index], null, row: 0, column, rowSpan: grid.RowDefinitions.Count);
                }
                else
                {
                    PlaceGroup(groups[index], (0, column), row: 1, column, rowSpan: 1);
                }
            }
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (int index = 0; index < groups.Length; index++)
            {
                if (index > 0 && !stacked)
                {
                    grid.RowDefinitions.Add(new RowDefinition(SplitterSize, GridUnitType.Pixel));
                    AddSplitter(GridResizeDirection.Rows, row: grid.RowDefinitions.Count - 1, column: 0, span: 1, index);
                }

                (int Row, int Column)? header = null;
                if (!HasInlineHeader(groups[index]))
                {
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    header = (grid.RowDefinitions.Count - 1, 0);
                }

                grid.RowDefinitions.Add(new RowDefinition(groups[index].Size, GridUnitType.Star)
                {
                    MinHeight = GroupMinHeight(groups[index]),
                });
                PlaceGroup(groups[index], header, row: grid.RowDefinitions.Count - 1, column: 0, rowSpan: 1);
            }
        }

        scroll.VerticalScrollBarVisibility = stacked ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        if (!stacked)
        {
            scroll.Offset = default;
        }

        UpdateStackedHeight();
        renderedInline = InlineSignature();
        ApplySelection();
        if (focusedMenu is not null && menuButtons.TryGetValue(focusedMenu, out Button? button))
        {
            RestoreFocus(button);
        }
    }

    private void UpdateStackedHeight()
    {
        double height = stacked && effective is not null
            ? Math.Max(Bounds.Height, RequiredStackHeight(effective.Groups))
            : double.NaN;
        if (!height.Equals(stackedHeight))
        {
            stackedHeight = height;
            grid.Height = height;
        }
    }

    private void PlaceGroup(WorkspaceEffectiveGroup group, (int Row, int Column)? header, int row, int column, int rowSpan)
    {
        TabStrip? tabs = null;
        if (header is { } position)
        {
            Control headerControl = CreateHeader(group, out tabs);
            Grid.SetRow(headerControl, position.Row);
            Grid.SetColumn(headerControl, position.Column);
            AddChrome(headerControl);
        }
        else if (HeaderSlotOf(group) is { } slot)
        {
            if (group.PanelIds.Length == 1)
            {
                Button menu = MenuButton(group.PanelIds[0]);
                Detach(menu);
                menu.IsVisible = true;
                slot.Content = menu;
            }
            else
            {
                slot.Content = CreateHeaderContent(group, out tabs, showTitle: false);
            }
        }

        foreach (string id in group.PanelIds)
        {
            if (FindPanel(id) is { } panel)
            {
                Grid.SetRow(panel, row);
                Grid.SetColumn(panel, column);
                Grid.SetRowSpan(panel, rowSpan);
            }
        }

        displayed.Add((group, tabs));
    }

    private Control CreateHeader(WorkspaceEffectiveGroup group, out TabStrip? tabs)
    {
        Control content = CreateHeaderContent(group, out tabs, showTitle: true);
        Border border = new()
        {
            Child = content,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        border.Classes.Add("workspace-header");
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("AppSurfaceMutedBrush"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("AppBorderBrush"));
        return border;
    }

    private Grid CreateHeaderContent(WorkspaceEffectiveGroup group, out TabStrip? tabs, bool showTitle)
    {
        Grid content = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = HeaderHeight };
        tabs = null;
        if (group.PanelIds.Length == 1 && showTitle)
        {
            string id = group.PanelIds[0];
            TextBlock title = new()
            {
                Text = TitleOf(id),
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            AutomationProperties.SetAutomationId(title, id + ".Title");
            content.Children.Add(title);
        }
        else
        {
            TabStrip strip = new()
            {
                VerticalAlignment = VerticalAlignment.Center,
                ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new WrapPanel()),
            };
            AutomationProperties.SetAutomationId(strip, $"{ScreenKey}.Tabs.{group.PanelIds[0]}");
            AutomationProperties.SetName(strip, "パネルの切替");
            foreach (string id in group.PanelIds)
            {
                TabStripItem item = new()
                {
                    Content = TitleOf(id),
                    Tag = id,
                    MinHeight = HeaderHeight,
                    MinWidth = 44d,
                    FontSize = 14,
                    Padding = new Thickness(10, 0),
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                AutomationProperties.SetAutomationId(item, id + ".Tab");
                AutomationProperties.SetName(item, TitleOf(id));
                strip.Items.Add(item);
            }

            // Select the current tab before listening, so automatic selection never reorders panels.
            string? selected = SelectedIn(group);
            strip.SelectedItem = strip.Items.OfType<TabStripItem>()
                .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, selected, StringComparison.Ordinal));
            strip.SelectionChanged += HandleTabSelectionChanged;
            content.Children.Add(strip);
            tabs = strip;
        }

        StackPanel menus = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(menus, 1);
        foreach (string id in group.PanelIds)
        {
            Button menu = MenuButton(id);
            Detach(menu);
            menus.Children.Add(menu);
        }

        content.Children.Add(menus);
        return content;
    }

    private void HandleTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (applyingSelection || sender is not TabStrip { SelectedItem: TabStripItem { Tag: string id } })
        {
            return;
        }

        MoveToFront(id);
        ApplySelection();
        // Header placement may follow the new tab: rebuild after the strip finishes its own change.
        Dispatcher.UIThread.Post(() =>
        {
            if (attached)
            {
                Render();
            }
        }, DispatcherPriority.Send);
    }

    private void ApplySelection()
    {
        applyingSelection = true;
        try
        {
            HashSet<string> shown = new(displayed.SelectMany(entry => entry.Group.PanelIds), StringComparer.Ordinal);
            foreach (WorkspacePanel panel in Panels.Where(panel => !shown.Contains(panel.PanelId)))
            {
                panel.IsVisible = false;
            }

            foreach ((WorkspaceEffectiveGroup group, TabStrip? tabs) in displayed)
            {
                string? selected = SelectedIn(group);
                foreach (string id in group.PanelIds)
                {
                    bool isSelected = string.Equals(id, selected, StringComparison.Ordinal);
                    if (FindPanel(id) is { } panel)
                    {
                        panel.IsVisible = isSelected;
                    }

                    if (tabs is not null && menuButtons.TryGetValue(id, out Button? menu))
                    {
                        menu.IsVisible = isSelected;
                    }
                }

                if (tabs is not null)
                {
                    tabs.SelectedItem = tabs.Items.OfType<TabStripItem>()
                        .FirstOrDefault(item => string.Equals(item.Tag as string, selected, StringComparison.Ordinal));
                }
            }
        }
        finally
        {
            applyingSelection = false;
        }

        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenderOrSelect()
    {
        if (attached)
        {
            Render();
        }
        else
        {
            ApplySelection();
        }
    }

    private string? SelectedIn(WorkspaceEffectiveGroup group) =>
        recency.FirstOrDefault(id => group.PanelIds.Contains(id, StringComparer.Ordinal)) ?? group.PanelIds.FirstOrDefault();

    private void MoveToFront(string panelId)
    {
        recency.Remove(panelId);
        recency.Insert(0, panelId);
    }

    private void AddSplitter(GridResizeDirection direction, int row, int column, int span, int groupIndex)
    {
        GridSplitter splitter = new()
        {
            ResizeDirection = direction,
            Focusable = false,
            IsTabStop = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        splitter.Bind(GridSplitter.BackgroundProperty, splitter.GetResourceObservable("AppBorderBrush"));
        AutomationProperties.SetAutomationId(splitter, $"{ScreenKey}.Splitter.{groupIndex}");
        AutomationProperties.SetName(splitter, "パネルの境界（ドラッグで大きさを変更。キーボードではパネルのメニューの「広げる」「狭める」）");
        Grid.SetRow(splitter, row);
        Grid.SetColumn(splitter, column);
        if (direction == GridResizeDirection.Columns)
        {
            Grid.SetRowSpan(splitter, span);
        }

        splitter.DragCompleted += HandleSplitterDragCompleted;
        AddChrome(splitter);
    }

    private void HandleSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        if (effective is null || effective.MaximizedPanelId is not null)
        {
            return;
        }

        bool horizontal = effective.Orientation == WorkspaceOrientation.Horizontal;
        List<(WorkspaceEffectiveGroup Group, double Actual)> measured = [];
        foreach ((WorkspaceEffectiveGroup group, _) in displayed)
        {
            WorkspacePanel? panel = FindPanel(group.PanelIds[0]);
            if (panel is null)
            {
                return;
            }

            int index = horizontal ? Grid.GetColumn(panel) : Grid.GetRow(panel);
            double actual = horizontal ? grid.ColumnDefinitions[index].ActualWidth : grid.RowDefinitions[index].ActualHeight;
            measured.Add((group, actual));
        }

        double totalActual = measured.Sum(item => item.Actual);
        double totalSize = measured.Sum(item => item.Group.Size);
        if (totalActual <= 0d)
        {
            return;
        }

        Dictionary<int, double> sizes = measured.ToDictionary(
            item => item.Group.SourceGroupIndex, item => item.Actual / totalActual * totalSize);
        Service.SetLayout(ScreenKey, Layout.WithGroupSizes(sizes));
    }

    private Button MenuButton(string panelId)
    {
        if (menuButtons.TryGetValue(panelId, out Button? existing))
        {
            return existing;
        }

        string title = TitleOf(panelId);
        MenuFlyout flyout = new() { Placement = PlacementMode.BottomEdgeAlignedRight };
        Button button = new()
        {
            // A geometry icon: a symbol character would need a fallback font and change text rendering.
            Content = new PathIcon
            {
                Data = MenuIconGeometry,
                Width = 16,
                Height = 16,
            },
            MinWidth = 44d,
            MinHeight = 44d,
            Width = 44d,
            Height = 44d,
            FontSize = 14,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Flyout = flyout,
        };
        button.Classes.Add("workspace-panel-menu");
        AutomationProperties.SetAutomationId(button, panelId + ".Menu");
        AutomationProperties.SetName(button, $"「{title}」パネルの操作");
        ToolTip.SetTip(button, $"「{title}」パネルの移動・並べ方・大きさ・タブ・表示を変更します。ドラッグは不要です。");
        flyout.Opening += (_, _) => PopulateMenu(flyout, panelId);
        // Keyboard users land on the first available item, so arrow keys and Enter operate the menu.
        flyout.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
            flyout.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEffectivelyEnabled)?.Focus(NavigationMethod.Tab),
            DispatcherPriority.Loaded);
        menuButtons[panelId] = button;
        return button;
    }

    private void PopulateMenu(MenuFlyout flyout, string panelId)
    {
        flyout.Items.Clear();
        foreach (WorkspaceMenuEntry entry in MenuEntries(panelId))
        {
            flyout.Items.Add(entry.Separator ? new Separator() : CreateMenuItem(entry));
        }
    }

    private MenuItem CreateMenuItem(WorkspaceMenuEntry entry)
    {
        MenuItem item = new()
        {
            Header = entry.Label,
            Command = new WorkspaceActionCommand(entry.Action, entry.Enabled),
            MinHeight = 44d,
            FontSize = 14,
        };
        AutomationProperties.SetAutomationId(item, entry.AutomationId);
        AutomationProperties.SetName(item, entry.Label);
        foreach (WorkspaceMenuEntry child in entry.Children)
        {
            item.Items.Add(CreateMenuItem(child));
        }

        if (!entry.Children.IsEmpty)
        {
            item.Command = null;
            item.IsEnabled = entry.Enabled;
        }

        return item;
    }

    /// <summary>The panel menu (NFR-UX-006), also used to build command search entries.</summary>
    public ImmutableArray<WorkspaceMenuEntry> MenuEntries(string panelId)
    {
        WorkspaceLayout layout = Layout;
        string prefix = panelId + ".Menu.";
        WorkspaceMenuEntry Operation(WorkspacePanelOperation operation, string label) => new(
            label, prefix + operation, layout.CanExecute(operation, panelId),
            () => ExecuteFromMenu(operation, panelId));
        string orientationLabel = layout.Orientation == WorkspaceOrientation.Horizontal ? "上下に並べる" : "左右に並べる";
        ImmutableArray<WorkspaceMenuEntry> hidden =
        [
            .. layout.HiddenPanelIds.Select(id => new WorkspaceMenuEntry(
                $"「{TitleOf(id)}」を表示", $"{prefix}Show.{id}", true, () => ExecuteFromMenu(WorkspacePanelOperation.Show, id))),
        ];
        return
        [
            Operation(WorkspacePanelOperation.MoveEarlier, "前へ移動"),
            Operation(WorkspacePanelOperation.MoveLater, "後ろへ移動"),
            Operation(WorkspacePanelOperation.ToggleOrientation, orientationLabel),
            Operation(WorkspacePanelOperation.Grow, "広げる"),
            Operation(WorkspacePanelOperation.Shrink, "狭める"),
            Operation(WorkspacePanelOperation.MergeIntoTabs, "タブにまとめる"),
            Operation(WorkspacePanelOperation.SeparateFromTabs, "タブから出す"),
            Operation(WorkspacePanelOperation.Hide, "隠す"),
            Operation(WorkspacePanelOperation.Maximize, "最大化"),
            Operation(WorkspacePanelOperation.Restore, "元に戻す"),
            WorkspaceMenuEntry.SeparatorEntry,
            new("表示するパネル", prefix + "ShowPanels", !hidden.IsEmpty, () => { }) { Children = hidden },
            new("既定のレイアウトに戻す", prefix + "ResetLayout", true, () =>
            {
                ResetLayout();
                RestoreFocus(MenuButton(panelId));
            }),
        ];
    }

    private void ExecuteFromMenu(WorkspacePanelOperation operation, string panelId)
    {
        Execute(operation, panelId);
        if (menuButtons.TryGetValue(panelId, out Button? button))
        {
            RestoreFocus(button);
        }
    }

    private void RestoreFocus(Button button) => Dispatcher.UIThread.Post(() =>
    {
        if (button.IsEffectivelyVisible && TopLevel.GetTopLevel(button) is not null)
        {
            button.Focus(NavigationMethod.Tab);
        }
    }, DispatcherPriority.Background);

    private void AddChrome(Control control)
    {
        chrome.Add(control);
        grid.Children.Insert(0, control);
    }

    private void ClearChrome()
    {
        foreach (Control control in chrome)
        {
            if (control is GridSplitter splitter)
            {
                splitter.DragCompleted -= HandleSplitterDragCompleted;
            }

            grid.Children.Remove(control);
        }

        chrome.Clear();
        foreach (Button menu in menuButtons.Values)
        {
            Detach(menu);
        }

        foreach (WorkspacePanel panel in Panels)
        {
            if (panel.FindHeaderSlot() is { } slot)
            {
                slot.Content = null;
            }
        }
    }

    private static void Detach(Control control)
    {
        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case ContentControl content when ReferenceEquals(content.Content, control):
                content.Content = null;
                break;
        }
    }

    private sealed class WorkspaceActionCommand(Action action, bool enabled) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => enabled;

        public void Execute(object? parameter)
        {
            if (enabled)
            {
                action();
            }
        }
    }
}

/// <summary>One panel menu entry; command search reuses the same labels and actions.</summary>
public sealed record WorkspaceMenuEntry(string Label, string AutomationId, bool Enabled, Action Action)
{
    public static WorkspaceMenuEntry SeparatorEntry { get; } = new("-", string.Empty, false, () => { }) { Separator = true };

    public bool Separator { get; init; }

    public ImmutableArray<WorkspaceMenuEntry> Children { get; init; } = [];
}
