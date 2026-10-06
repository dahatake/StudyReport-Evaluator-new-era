using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace StudyReportEvaluator.App.Usage;

public enum UsageOperation { Normal, Reference, Special, Similarity }

public sealed record JobCostSnapshot(
    Guid JobId,
    long Revision,
    bool IsFinished,
    string SummaryText,
    string DetailsText,
    string LogText,
    string? LogPath,
    string LogStatusText)
{
    public UsageMetrics Metrics { get; init; } = new();
    public ImmutableDictionary<UsageMetric, MetricObservation> MetricObservations { get; init; }
        = ImmutableDictionary<UsageMetric, MetricObservation>.Empty;
    public int AttemptCount { get; init; }
    public ImmutableDictionary<UsageObservationStatus, int> AttemptStatuses { get; init; }
        = ImmutableDictionary<UsageObservationStatus, int>.Empty;
    public ImmutableArray<OperationUsageSnapshot> OperationBreakdown { get; init; } = [];
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? ObservedAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; init; }
    public ImmutableArray<ModelUsageSnapshot> Models { get; init; } = [];
    public bool ModelsTruncated { get; init; }
    public int ModelCostMismatchCount { get; init; }
}

public enum UsageMetric { InputTokens, OutputTokens, ReasoningTokens, CacheReadTokens, CacheWriteTokens, TotalNanoAiu, PremiumRequests }

public sealed record MetricObservation(int ObservedAttemptCount, int CompleteAttemptCount, int AttemptCount)
{
    public bool IsPartial => AttemptCount > 0 && CompleteAttemptCount < AttemptCount;
    public ImmutableDictionary<UsageSource, int> SourceCounts { get; init; }
        = ImmutableDictionary<UsageSource, int>.Empty;

    // ImmutableDictionary compares by reference; this record must stay a value.
    public bool Equals(MetricObservation? other) =>
        other is not null
        && ObservedAttemptCount == other.ObservedAttemptCount
        && CompleteAttemptCount == other.CompleteAttemptCount
        && AttemptCount == other.AttemptCount
        && (SourceCounts?.Count ?? 0) == (other.SourceCounts?.Count ?? 0)
        && (SourceCounts?.All(pair => other.SourceCounts is { } counts
            && counts.TryGetValue(pair.Key, out int value) && value == pair.Value) ?? true);

    public override int GetHashCode() => HashCode.Combine(
        ObservedAttemptCount, CompleteAttemptCount, AttemptCount, SourceCounts?.Count ?? 0);
}

public sealed record OperationUsageSnapshot(
    UsageOperation Operation,
    int AttemptCount,
    UsageMetrics Metrics,
    ImmutableDictionary<UsageMetric, MetricObservation> MetricObservations);

public enum UsageSource { None, Events, FinalRpc, LastCall }

public sealed record MetricProvenance(UsageSource Source = UsageSource.None, bool IsPartial = true);

public enum ModelCostComparison { NotComparable, Match, Mismatch }

public enum UsageObservationStatus
{
    Pending, EventObserved, FinalObserved, RpcUnavailable, RpcTimedOut,
    SendPending, AbortPending, EventsIncomplete, EventsUnavailable,
}

/// <summary>SDK observations only; null is unknown, never a manufactured zero.</summary>
public sealed record UsageMetrics(
    long? InputTokens = null,
    long? OutputTokens = null,
    long? ReasoningTokens = null,
    long? CacheReadTokens = null,
    long? CacheWriteTokens = null,
    decimal? TotalNanoAiu = null,
    decimal? PremiumRequests = null);

/// <summary>Reported model identity is a pseudonym, never an SDK-provided name.</summary>
public sealed record ModelUsageSnapshot(string ModelKey, UsageMetrics Metrics, bool IsPartial = true)
{
    public const int MaximumModels = 32;

    public static string CreateModelKey(string? modelId) => string.IsNullOrWhiteSpace(modelId)
        ? "未確認"
        : "model-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(modelId)))[..16].ToLowerInvariant();

    // Snapshots are a public input boundary too; never trust a caller's display text.
    internal static string SafeKey(string? key) => key == "未確認"
        || (key is { Length: 22 } && key.StartsWith("model-", StringComparison.Ordinal)
            && key.AsSpan(6).IndexOfAnyExcept("0123456789abcdef") < 0)
        ? key! : CreateModelKey(key);
}

public sealed record AttemptUsageSnapshot(
    Guid AttemptId,
    UsageOperation Operation,
    long Revision,
    UsageMetrics Metrics,
    UsageSource Source,
    bool IsFinished = false,
    bool HasInvalidValues = false,
    bool IsPartial = true,
    UsageObservationStatus Status = UsageObservationStatus.Pending)
{
    public bool HasDownwardCorrection { get; init; }
    public ImmutableArray<ModelUsageSnapshot> Models { get; init; } = [];
    public bool ModelsTruncated { get; init; }
    public string? RequestedModelKey { get; init; }
    public bool? RequestedModelIsAuto { get; init; }
    // The run-level value the app set on SessionConfig.ReasoningEffort; null when unset (or a legacy record).
    public string? RequestedReasoningEffort { get; init; }
    // Null identifies legacy callers. An empty or incomplete map is NOT evidence of completion.
    public ImmutableDictionary<UsageMetric, MetricProvenance>? MetricProvenance { get; init; }
    public ModelCostComparison ModelCostComparison { get; init; }
}