using System.Collections.Immutable;

namespace StudyReportEvaluator.App.Usage;

/// <summary>Closed numeric provenance; never retain external labels or manufacture observations.</summary>
internal static class UsageProvenance
{
    internal static decimal? Value(UsageMetrics metrics, UsageMetric metric) => metric switch
    {
        UsageMetric.InputTokens => metrics.InputTokens,
        UsageMetric.OutputTokens => metrics.OutputTokens,
        UsageMetric.ReasoningTokens => metrics.ReasoningTokens,
        UsageMetric.CacheReadTokens => metrics.CacheReadTokens,
        UsageMetric.CacheWriteTokens => metrics.CacheWriteTokens,
        UsageMetric.TotalNanoAiu => metrics.TotalNanoAiu,
        UsageMetric.PremiumRequests => metrics.PremiumRequests,
        _ => null,
    };

    internal static ImmutableDictionary<UsageMetric, MetricProvenance> Sanitize(UsageMetrics metrics,
        ImmutableDictionary<UsageMetric, MetricProvenance>? metadata) => Enum.GetValues<UsageMetric>()
        .ToImmutableDictionary(m => m, m =>
        {
            MetricProvenance? item = metadata?.GetValueOrDefault(m);
            if (Value(metrics, m) is not >= 0 || item is null || !Enum.IsDefined(item.Source))
            {
                return new MetricProvenance();
            }
            return item with { IsPartial = item.IsPartial || item.Source != UsageSource.FinalRpc };
        });

    internal static ImmutableDictionary<UsageMetric, MetricProvenance> FromSource(UsageMetrics metrics,
        UsageSource source) => Sanitize(metrics, Enum.GetValues<UsageMetric>()
            .ToImmutableDictionary(m => m, _ => new MetricProvenance(source)));

    internal static MetricProvenance ForAttempt(AttemptUsageSnapshot attempt, UsageMetric metric)
    {
        if (Value(attempt.Metrics, metric) is not >= 0) { return new(); }
        if (attempt.MetricProvenance is { } metadata)
        {
            MetricProvenance? item = metadata.GetValueOrDefault(metric);
            return item is null || !Enum.IsDefined(item.Source) ? new()
                : item with { IsPartial = item.IsPartial || !attempt.IsFinished || item.Source != UsageSource.FinalRpc };
        }
        return new(Enum.IsDefined(attempt.Source) ? attempt.Source : UsageSource.None,
            attempt.IsPartial || !attempt.IsFinished || attempt.HasInvalidValues || attempt.Source != UsageSource.FinalRpc);
    }

    // Numeric priority and provenance travel together, even when values happen to be equal.
    internal static UsageMetrics MergeFinal(UsageMetrics final, UsageMetrics events,
        ImmutableDictionary<UsageMetric, MetricProvenance> finalMetadata,
        out ImmutableDictionary<UsageMetric, MetricProvenance> metadata)
    {
        final = UsageMath.Sanitize(final, out _);
        events = UsageMath.Sanitize(events, out _);
        var clean = Sanitize(final, finalMetadata);
        var selected = ImmutableDictionary.CreateBuilder<UsageMetric, MetricProvenance>();
        bool UseFinal(UsageMetric metric)
        {
            bool useFinal = Value(final, metric) is not null
                && (clean[metric].Source != UsageSource.LastCall || Value(events, metric) is null);
            selected[metric] = useFinal ? clean[metric]
                : Value(events, metric) is not null ? new(UsageSource.Events) : new();
            return useFinal;
        }
        var merged = new UsageMetrics(
            UseFinal(UsageMetric.InputTokens) ? final.InputTokens : events.InputTokens,
            UseFinal(UsageMetric.OutputTokens) ? final.OutputTokens : events.OutputTokens,
            UseFinal(UsageMetric.ReasoningTokens) ? final.ReasoningTokens : events.ReasoningTokens,
            UseFinal(UsageMetric.CacheReadTokens) ? final.CacheReadTokens : events.CacheReadTokens,
            UseFinal(UsageMetric.CacheWriteTokens) ? final.CacheWriteTokens : events.CacheWriteTokens,
            UseFinal(UsageMetric.TotalNanoAiu) ? final.TotalNanoAiu : events.TotalNanoAiu,
            UseFinal(UsageMetric.PremiumRequests) ? final.PremiumRequests : events.PremiumRequests);
        metadata = selected.ToImmutable();
        return merged;
    }

    internal static ModelCostComparison Compare(decimal? sessionCost, IEnumerable<decimal?> modelCosts,
        bool truncated)
    {
        if (truncated || sessionCost is not >= 0) { return ModelCostComparison.NotComparable; }
        decimal sum = 0;
        bool any = false;
        foreach (decimal? cost in modelCosts)
        {
            if (cost is not >= 0) { return ModelCostComparison.NotComparable; }
            any = true;
            try { sum = checked(sum + cost.Value); }
            catch (OverflowException) { return ModelCostComparison.NotComparable; }
        }
        return !any ? ModelCostComparison.NotComparable
            : sum == sessionCost ? ModelCostComparison.Match : ModelCostComparison.Mismatch;
    }

    internal static ModelCostComparison SanitizeComparison(AttemptUsageSnapshot attempt)
    {
        // Never create comparison evidence from merged/event/legacy data.
        if (!Enum.IsDefined(attempt.ModelCostComparison)
            || attempt.ModelCostComparison == ModelCostComparison.NotComparable
            || attempt.Status != UsageObservationStatus.FinalObserved
            || attempt.MetricProvenance?.GetValueOrDefault(UsageMetric.TotalNanoAiu)
                is not { Source: UsageSource.FinalRpc, IsPartial: false }
            || attempt.Models.IsDefaultOrEmpty || attempt.ModelsTruncated)
        {
            return ModelCostComparison.NotComparable;
        }
        ModelCostComparison actual = Compare(attempt.Metrics.TotalNanoAiu,
            attempt.Models.Select(m => m?.Metrics?.TotalNanoAiu), attempt.ModelsTruncated);
        return actual == attempt.ModelCostComparison ? actual : ModelCostComparison.NotComparable;
    }
}