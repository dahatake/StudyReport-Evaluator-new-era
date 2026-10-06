using System.Collections.Immutable;

namespace StudyReportEvaluator.App.Workspace;

/// <summary>Direction in which the panel groups of one screen are arranged (NFR-UX-003).</summary>
public enum WorkspaceOrientation
{
    Horizontal,
    Vertical,
}

/// <summary>Non-drag panel operations (NFR-UX-006). The same set backs menus and command search.</summary>
public enum WorkspacePanelOperation
{
    MoveEarlier,
    MoveLater,
    ToggleOrientation,
    Grow,
    Shrink,
    MergeIntoTabs,
    SeparateFromTabs,
    Hide,
    Show,
    Maximize,
    Restore,
}

/// <summary>Effective arrangement after NFR-UX-008 reflow. Never persisted.</summary>
public enum WorkspaceReflowMode
{
    Full,
    Compact,
    Single,
}

/// <summary>One side-by-side (or stacked) slot. Several panels form a tab group.</summary>
public sealed class WorkspaceGroup : IEquatable<WorkspaceGroup>
{
    public const double MinimumSize = 0.25d;
    public const double MaximumSize = 20d;

    public WorkspaceGroup(IEnumerable<string> panelIds, double size)
    {
        ArgumentNullException.ThrowIfNull(panelIds);
        PanelIds = [.. panelIds];
        Size = double.IsFinite(size) ? Math.Clamp(size, MinimumSize, MaximumSize) : 1d;
    }

    public ImmutableArray<string> PanelIds { get; }

    /// <summary>Relative (star) size of the group along the arrangement direction.</summary>
    public double Size { get; }

    public WorkspaceGroup WithPanels(IEnumerable<string> panelIds) => new(panelIds, Size);

    public WorkspaceGroup WithSize(double size) => new(PanelIds, size);

    public bool Equals(WorkspaceGroup? other) => other is not null
        && PanelIds.SequenceEqual(other.PanelIds, StringComparer.Ordinal)
        && Size.Equals(other.Size);

    public override bool Equals(object? obj) => Equals(obj as WorkspaceGroup);

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (string id in PanelIds)
        {
            hash.Add(id, StringComparer.Ordinal);
        }

        hash.Add(Size);
        return hash.ToHashCode();
    }
}

/// <summary>A displayed group: visible panels only, with the persisted group index for size updates.</summary>
public sealed record WorkspaceEffectiveGroup(ImmutableArray<string> PanelIds, double Size, int SourceGroupIndex);

/// <summary>What a workspace actually shows for its current viewport (NFR-UX-008).</summary>
public sealed record WorkspaceEffectiveLayout(
    WorkspaceOrientation Orientation,
    ImmutableArray<WorkspaceEffectiveGroup> Groups,
    string? MaximizedPanelId,
    WorkspaceReflowMode Mode);

/// <summary>
/// Immutable layout of the content panels of one screen: panel order, grouping into tabs,
/// relative sizes, hidden panels, maximized panel and orientation. It holds panel IDs only,
/// never paths, workbook data, answers or prompts (NFR-UX-004).
/// </summary>
public sealed class WorkspaceLayout : IEquatable<WorkspaceLayout>
{
    private const double SizeStep = 1.25d;

    public WorkspaceLayout(
        WorkspaceOrientation orientation,
        IEnumerable<WorkspaceGroup> groups,
        IEnumerable<string>? hiddenPanelIds = null,
        string? maximizedPanelId = null)
    {
        ArgumentNullException.ThrowIfNull(groups);
        Orientation = Enum.IsDefined(orientation) ? orientation : WorkspaceOrientation.Horizontal;
        Groups = [.. groups.Where(group => group is not null && group.PanelIds.Length > 0)];
        HashSet<string> known = new(Groups.SelectMany(group => group.PanelIds), StringComparer.Ordinal);
        HiddenPanelIds = [.. (hiddenPanelIds ?? []).Where(known.Contains).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        MaximizedPanelId = maximizedPanelId is not null && known.Contains(maximizedPanelId)
            && !HiddenPanelIds.Contains(maximizedPanelId, StringComparer.Ordinal)
                ? maximizedPanelId
                : null;
    }

    public WorkspaceOrientation Orientation { get; }

    public ImmutableArray<WorkspaceGroup> Groups { get; }

    public ImmutableArray<string> HiddenPanelIds { get; }

    public string? MaximizedPanelId { get; }

    public IEnumerable<string> PanelIds => Groups.SelectMany(group => group.PanelIds);

    public IEnumerable<string> VisiblePanelIds => PanelIds.Where(IsVisible);

    public bool IsVisible(string panelId) =>
        PanelIds.Contains(panelId, StringComparer.Ordinal)
        && !HiddenPanelIds.Contains(panelId, StringComparer.Ordinal);

    public bool IsHidden(string panelId) => HiddenPanelIds.Contains(panelId, StringComparer.Ordinal);

    public int GroupIndexOf(string panelId)
    {
        for (int index = 0; index < Groups.Length; index++)
        {
            if (Groups[index].PanelIds.Contains(panelId, StringComparer.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Indices of groups that contain at least one visible panel, in order.</summary>
    public ImmutableArray<int> VisibleGroupIndices =>
        [.. Enumerable.Range(0, Groups.Length).Where(index => Groups[index].PanelIds.Any(IsVisible))];

    /// <summary>Keeps only known panels, adds missing ones as their own groups, and keeps one panel visible.</summary>
    public WorkspaceLayout Normalize(IReadOnlyList<string> knownPanelIds)
    {
        ArgumentNullException.ThrowIfNull(knownPanelIds);
        HashSet<string> known = new(knownPanelIds, StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<WorkspaceGroup> groups = [];
        foreach (WorkspaceGroup group in Groups)
        {
            string[] ids = [.. group.PanelIds.Where(id => known.Contains(id) && seen.Add(id))];
            if (ids.Length > 0)
            {
                groups.Add(group.WithPanels(ids));
            }
        }

        foreach (string id in knownPanelIds.Where(seen.Add))
        {
            groups.Add(new WorkspaceGroup([id], 1d));
        }

        WorkspaceLayout normalized = new(Orientation, groups, HiddenPanelIds, MaximizedPanelId);
        if (!normalized.VisiblePanelIds.Any() && groups.Count > 0)
        {
            normalized = new WorkspaceLayout(Orientation, groups,
                normalized.HiddenPanelIds.Skip(1), MaximizedPanelId);
        }

        return normalized;
    }

    public bool CanExecute(WorkspacePanelOperation operation, string panelId)
    {
        ArgumentNullException.ThrowIfNull(panelId);
        int groupIndex = GroupIndexOf(panelId);
        if (groupIndex < 0)
        {
            return false;
        }

        bool visible = IsVisible(panelId);
        int visibleGroups = VisibleGroupIndices.Length;
        string[] groupVisible = [.. Groups[groupIndex].PanelIds.Where(IsVisible)];
        int visibleIndex = Array.IndexOf(groupVisible, panelId);
        bool normal = visible && MaximizedPanelId is null;
        return operation switch
        {
            WorkspacePanelOperation.MoveEarlier => normal
                && (visibleIndex > 0 || PreviousVisibleGroup(groupIndex) >= 0),
            WorkspacePanelOperation.MoveLater => normal
                && (visibleIndex < groupVisible.Length - 1 || NextVisibleGroup(groupIndex) >= 0),
            WorkspacePanelOperation.ToggleOrientation => MaximizedPanelId is null,
            WorkspacePanelOperation.Grow => normal && visibleGroups > 1
                && Groups[groupIndex].Size < WorkspaceGroup.MaximumSize,
            WorkspacePanelOperation.Shrink => normal && visibleGroups > 1
                && Groups[groupIndex].Size > WorkspaceGroup.MinimumSize,
            WorkspacePanelOperation.MergeIntoTabs => normal
                && (NextVisibleGroup(groupIndex) >= 0 || PreviousVisibleGroup(groupIndex) >= 0),
            WorkspacePanelOperation.SeparateFromTabs => normal && groupVisible.Length > 1,
            WorkspacePanelOperation.Hide => visible && VisiblePanelIds.Count() > 1,
            WorkspacePanelOperation.Show => !visible,
            WorkspacePanelOperation.Maximize => normal && VisiblePanelIds.Count() > 1,
            WorkspacePanelOperation.Restore => MaximizedPanelId is not null,
            _ => false,
        };
    }

    /// <summary>Applies one operation; returns this instance when the operation is not available.</summary>
    public WorkspaceLayout Execute(WorkspacePanelOperation operation, string panelId)
    {
        if (!CanExecute(operation, panelId))
        {
            return this;
        }

        int groupIndex = GroupIndexOf(panelId);
        WorkspaceGroup group = Groups[groupIndex];
        return operation switch
        {
            WorkspacePanelOperation.MoveEarlier => Move(panelId, groupIndex, earlier: true),
            WorkspacePanelOperation.MoveLater => Move(panelId, groupIndex, earlier: false),
            WorkspacePanelOperation.ToggleOrientation => new WorkspaceLayout(
                Orientation == WorkspaceOrientation.Horizontal ? WorkspaceOrientation.Vertical : WorkspaceOrientation.Horizontal,
                Groups, HiddenPanelIds, MaximizedPanelId),
            WorkspacePanelOperation.Grow => WithGroup(groupIndex, group.WithSize(group.Size * SizeStep)),
            WorkspacePanelOperation.Shrink => WithGroup(groupIndex, group.WithSize(group.Size / SizeStep)),
            WorkspacePanelOperation.MergeIntoTabs => Merge(panelId, groupIndex),
            WorkspacePanelOperation.SeparateFromTabs => Separate(panelId, groupIndex),
            WorkspacePanelOperation.Hide => new WorkspaceLayout(Orientation, Groups, HiddenPanelIds.Add(panelId),
                MaximizedPanelId == panelId ? null : MaximizedPanelId),
            WorkspacePanelOperation.Show => new WorkspaceLayout(Orientation, Groups,
                HiddenPanelIds.Remove(panelId, StringComparer.Ordinal), MaximizedPanelId),
            WorkspacePanelOperation.Maximize => new WorkspaceLayout(Orientation, Groups, HiddenPanelIds, panelId),
            WorkspacePanelOperation.Restore => new WorkspaceLayout(Orientation, Groups, HiddenPanelIds, null),
            _ => this,
        };
    }

    /// <summary>Sets relative sizes, e.g. after a splitter drag. Unknown indices are ignored.</summary>
    public WorkspaceLayout WithGroupSizes(IReadOnlyDictionary<int, double> sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        return new WorkspaceLayout(
            Orientation,
            Groups.Select((group, index) => sizes.TryGetValue(index, out double size) ? group.WithSize(size) : group),
            HiddenPanelIds,
            MaximizedPanelId);
    }

    /// <summary>
    /// NFR-UX-008 / AS-006: Compact shows the third and later side-by-side groups as tabs of their
    /// neighbour; Single stacks every group in one column. The persisted layout is not changed.
    /// </summary>
    public WorkspaceEffectiveLayout Reflow(WorkspaceReflowMode mode)
    {
        List<WorkspaceEffectiveGroup> groups = [.. VisibleGroupIndices.Select(index => new WorkspaceEffectiveGroup(
            [.. Groups[index].PanelIds.Where(IsVisible)], Groups[index].Size, index))];
        WorkspaceOrientation orientation = Orientation;
        if (mode == WorkspaceReflowMode.Single)
        {
            orientation = WorkspaceOrientation.Vertical;
        }
        else if (mode == WorkspaceReflowMode.Compact && orientation == WorkspaceOrientation.Horizontal && groups.Count > 2)
        {
            groups[1] = groups[1] with { PanelIds = [.. groups.Skip(1).SelectMany(group => group.PanelIds)] };
            groups.RemoveRange(2, groups.Count - 2);
        }

        return new WorkspaceEffectiveLayout(orientation, [.. groups], MaximizedPanelId, mode);
    }

    public bool Equals(WorkspaceLayout? other) => other is not null
        && Orientation == other.Orientation
        && Groups.SequenceEqual(other.Groups)
        && HiddenPanelIds.SequenceEqual(other.HiddenPanelIds, StringComparer.Ordinal)
        && string.Equals(MaximizedPanelId, other.MaximizedPanelId, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as WorkspaceLayout);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Orientation);
        foreach (WorkspaceGroup group in Groups)
        {
            hash.Add(group);
        }

        foreach (string id in HiddenPanelIds)
        {
            hash.Add(id, StringComparer.Ordinal);
        }

        hash.Add(MaximizedPanelId ?? string.Empty, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private int PreviousVisibleGroup(int groupIndex)
    {
        for (int index = groupIndex - 1; index >= 0; index--)
        {
            if (Groups[index].PanelIds.Any(IsVisible))
            {
                return index;
            }
        }

        return -1;
    }

    private int NextVisibleGroup(int groupIndex)
    {
        for (int index = groupIndex + 1; index < Groups.Length; index++)
        {
            if (Groups[index].PanelIds.Any(IsVisible))
            {
                return index;
            }
        }

        return -1;
    }

    private WorkspaceLayout WithGroup(int index, WorkspaceGroup group) =>
        new(Orientation, Groups.SetItem(index, group), HiddenPanelIds, MaximizedPanelId);

    private WorkspaceLayout Move(string panelId, int groupIndex, bool earlier)
    {
        WorkspaceGroup group = Groups[groupIndex];
        string[] visible = [.. group.PanelIds.Where(IsVisible)];
        int neighbourIndex = Array.IndexOf(visible, panelId) + (earlier ? -1 : 1);
        if (neighbourIndex >= 0 && neighbourIndex < visible.Length)
        {
            // Reorder inside the tab group; hidden members keep their slots.
            List<string> ids = [.. group.PanelIds];
            int a = ids.IndexOf(panelId);
            int b = ids.IndexOf(visible[neighbourIndex]);
            (ids[a], ids[b]) = (ids[b], ids[a]);
            return WithGroup(groupIndex, group.WithPanels(ids));
        }

        int target = earlier ? PreviousVisibleGroup(groupIndex) : NextVisibleGroup(groupIndex);
        if (target < 0)
        {
            return this;
        }

        // The panel passes its neighbouring group together with its own tab group.
        List<WorkspaceGroup> groups = [.. Groups];
        (groups[groupIndex], groups[target]) = (groups[target], groups[groupIndex]);
        return new WorkspaceLayout(Orientation, groups, HiddenPanelIds, MaximizedPanelId);
    }

    private WorkspaceLayout Merge(string panelId, int groupIndex)
    {
        int target = NextVisibleGroup(groupIndex);
        if (target < 0)
        {
            target = PreviousVisibleGroup(groupIndex);
        }

        List<WorkspaceGroup> groups = [.. Groups];
        groups[target] = groups[target].WithPanels(groups[target].PanelIds.Add(panelId));
        groups[groupIndex] = groups[groupIndex].WithPanels(groups[groupIndex].PanelIds.Remove(panelId, StringComparer.Ordinal));
        return new WorkspaceLayout(Orientation, groups, HiddenPanelIds, MaximizedPanelId);
    }

    private WorkspaceLayout Separate(string panelId, int groupIndex)
    {
        List<WorkspaceGroup> groups = [.. Groups];
        WorkspaceGroup group = groups[groupIndex];
        groups[groupIndex] = group.WithPanels(group.PanelIds.Remove(panelId, StringComparer.Ordinal));
        groups.Insert(groupIndex + 1, new WorkspaceGroup([panelId], group.Size));
        return new WorkspaceLayout(Orientation, groups, HiddenPanelIds, MaximizedPanelId);
    }
}
