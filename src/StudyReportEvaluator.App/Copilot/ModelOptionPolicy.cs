using System.Globalization;
using GitHub.Copilot;

namespace StudyReportEvaluator.App.Copilot;

public sealed record ReasoningEffortOption(string Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record ContextSizeOption(string Tier, int? MaximumPromptTokens)
{
    public string DisplayName => (MaximumPromptTokens switch
    {
        int value when value % 1_000_000 == 0 => $"{value / 1_000_000}M",
        int value when value % 1_000 == 0 => $"{value / 1_000}K",
        int value => value.ToString(CultureInfo.InvariantCulture),
        _ => "SDK未公開",
    }) + (Tier == ModelOptionPolicy.DefaultContextTier ? " (Default)" : string.Empty);

    public override string ToString() => DisplayName;
}

public static class ModelOptionPolicy
{
    public const string DefaultContextTier = "default";
    public const string LongContextTier = "long-context";

    private static readonly string[] Efforts = ["none", "minimal", "low", "medium", "high", "xhigh", "max"];
    private static readonly string[] Labels = ["なし", "最小", "低", "中", "高", "非常に高い", "最大"];

    public static IReadOnlyList<ReasoningEffortOption> ReasoningOptions(CopilotModelAvailability model) =>
        model.Id == "auto" ? [] : model.SupportedReasoningEfforts
            .OrderBy(value => Array.IndexOf(Efforts, value) is int index && index >= 0 ? index : int.MaxValue)
            .ThenBy(value => value, StringComparer.Ordinal)
            .Select(value => new ReasoningEffortOption(value,
                (Array.IndexOf(Efforts, value) is int index && index >= 0 ? Labels[index] : value)
                + (value == model.DefaultReasoningEffort ? " (Default)" : string.Empty)))
            .ToArray();

    public static IReadOnlyList<ContextSizeOption> ContextOptions(CopilotModelAvailability model)
    {
        ContextSizeOption standard = new(DefaultContextTier, model.MaximumPromptTokens);
        return model.Id != "auto" && model.LongContextPromptTokens is int extended
            ? [standard, new(LongContextTier, extended)]
            : [standard];
    }

    public static bool IsValidContextTier(string? tier) =>
        tier is null or DefaultContextTier or LongContextTier;

    public static ContextTier? ToSdkContextTier(string? tier) => tier switch
    {
        null or DefaultContextTier => null,
        LongContextTier => ContextTier.LongContext,
        _ => throw new ArgumentException("The context tier is invalid.", nameof(tier)),
    };
}
