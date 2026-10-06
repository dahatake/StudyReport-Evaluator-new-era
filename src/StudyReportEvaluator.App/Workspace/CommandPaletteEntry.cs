namespace StudyReportEvaluator.App.Workspace;

/// <summary>One entry of the command search (NFR-UX-007). It never starts AI evaluation or login.</summary>
public sealed record CommandPaletteEntry(string Id, string Title, string Group, Action Execute)
{
    public string AutomationId => "Command." + Id;

    public string AccessibleName => $"{Group}: {Title}";

    public override string ToString() => AccessibleName;
}

public static class CommandPaletteFilter
{
    /// <summary>Case-insensitive match of every space-separated term against the group and title.</summary>
    public static IReadOnlyList<CommandPaletteEntry> Filter(IEnumerable<CommandPaletteEntry> entries, string? query)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string[] terms = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return [.. entries.Where(entry => terms.All(term =>
            entry.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || entry.Group.Contains(term, StringComparison.CurrentCultureIgnoreCase)))];
    }
}
