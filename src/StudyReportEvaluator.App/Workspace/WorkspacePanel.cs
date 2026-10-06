using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace StudyReportEvaluator.App.Workspace;

/// <summary>
/// One movable content panel of a <see cref="PanelWorkspace"/>. The content is kept in the visual
/// tree for the lifetime of the workspace, so arranging panels never resets input, selection or
/// scroll position. A descendant <see cref="ContentControl"/> marked with <c>IsHeaderSlot</c> may
/// host the panel menu when the panel is shown alone, instead of a separate title row.
/// </summary>
public sealed class WorkspacePanel : Decorator
{
    public static readonly StyledProperty<string> PanelIdProperty =
        AvaloniaProperty.Register<WorkspacePanel, string>(nameof(PanelId), string.Empty);

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<WorkspacePanel, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<double> MinContentWidthProperty =
        AvaloniaProperty.Register<WorkspacePanel, double>(nameof(MinContentWidth), 160d);

    public static readonly StyledProperty<double> MinContentHeightProperty =
        AvaloniaProperty.Register<WorkspacePanel, double>(nameof(MinContentHeight), 88d);

    public static readonly StyledProperty<double> TabsInHeaderSlotMinWidthProperty =
        AvaloniaProperty.Register<WorkspacePanel, double>(nameof(TabsInHeaderSlotMinWidth), double.PositiveInfinity);

    public static readonly AttachedProperty<bool> IsHeaderSlotProperty =
        AvaloniaProperty.RegisterAttached<WorkspacePanel, ContentControl, bool>("IsHeaderSlot");

    public string PanelId
    {
        get => GetValue(PanelIdProperty);
        set => SetValue(PanelIdProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Smallest width at which the panel content fits without horizontal overflow.</summary>
    public double MinContentWidth
    {
        get => GetValue(MinContentWidthProperty);
        set => SetValue(MinContentWidthProperty, value);
    }

    /// <summary>Smallest useful height of the content below any title row.</summary>
    public double MinContentHeight
    {
        get => GetValue(MinContentHeightProperty);
        set => SetValue(MinContentHeightProperty, value);
    }

    /// <summary>
    /// Smallest group width at which the header slot also holds the tab strip of a tab group
    /// (otherwise the tabs get their own row). Infinity keeps tabs out of the slot.
    /// </summary>
    public double TabsInHeaderSlotMinWidth
    {
        get => GetValue(TabsInHeaderSlotMinWidthProperty);
        set => SetValue(TabsInHeaderSlotMinWidthProperty, value);
    }

    public static bool GetIsHeaderSlot(ContentControl control) => control.GetValue(IsHeaderSlotProperty);

    public static void SetIsHeaderSlot(ContentControl control, bool value) => control.SetValue(IsHeaderSlotProperty, value);

    internal ContentControl? FindHeaderSlot() => Child is null
        ? null
        : Avalonia.LogicalTree.LogicalExtensions.GetSelfAndLogicalDescendants(Child)
            .OfType<ContentControl>()
            .FirstOrDefault(GetIsHeaderSlot);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PanelIdProperty && string.IsNullOrEmpty(AutomationProperties.GetAutomationId(this)))
        {
            AutomationProperties.SetAutomationId(this, PanelId);
        }
        else if (change.Property == TitleProperty)
        {
            AutomationProperties.SetName(this, Title);
        }
    }
}
