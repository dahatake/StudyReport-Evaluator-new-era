using GitHub.Copilot;

namespace StudyReportEvaluator.App.Copilot;

public static class ReasoningEffortPolicy
{
    public const string DefaultPreferredReasoningEffort = "low";

    private static readonly string[] OrderedEfforts =
    [
        "none",
        "minimal",
        "low",
        "medium",
        "high",
        "xhigh",
        "max",
    ];

    private static readonly Dictionary<string, int> OrderedEffortIndexes =
        OrderedEfforts.Select((value, index) => (value, index))
            .ToDictionary(item => item.value, item => item.index, StringComparer.Ordinal);

    public static string? ResolveReasoningEffort(
        IEnumerable<ModelInfo>? models,
        string? modelId,
        string? preferredEffort = null)
    {
        if (string.IsNullOrWhiteSpace(modelId)
            || string.Equals(modelId, "auto", StringComparison.Ordinal))
        {
            return null;
        }

        ModelInfo? model = models?.FirstOrDefault(
            candidate => candidate is not null
                && string.Equals(candidate.Id, modelId, StringComparison.Ordinal));
        if (model?.Capabilities?.Supports?.ReasoningEffort != true)
        {
            return null;
        }

        string[] supported = [.. (model.SupportedReasoningEfforts ?? [])
            .Where(IsSafeReasoningEffort)
            .Distinct(StringComparer.Ordinal)];
        if (supported.Length == 0)
        {
            return null;
        }

        string preference = IsSafeReasoningEffort(preferredEffort)
            ? preferredEffort!
            : DefaultPreferredReasoningEffort;
        if (supported.Contains(preference, StringComparer.Ordinal))
        {
            return preference;
        }

        if (!OrderedEffortIndexes.TryGetValue(preference, out int preferredIndex))
        {
            return supported
                .OrderBy(value => value, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        return supported
            .Select(value => new
            {
                Value = value,
                Index = OrderedEffortIndexes.TryGetValue(value, out int index) ? index : int.MaxValue,
            })
            .Where(item => item.Index != int.MaxValue)
            .OrderBy(item => Math.Abs(item.Index - preferredIndex))
            .ThenByDescending(item => item.Index)
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .Select(item => item.Value)
            .FirstOrDefault();
    }

    public static bool IsSafeReasoningEffort(string? value) =>
        value is { Length: > 0 and <= 64 }
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.All(character => !char.IsControl(character));
}
