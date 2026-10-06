using System.Collections.Immutable;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

// Fixed SDK 1.0.11: usage.getMetrics is experimental. Keep suppression local to the adapter.
#pragma warning disable GHCP001

namespace StudyReportEvaluator.App.Usage;

// Verified against GitHub.Copilot.SDK 1.0.11 XML and reflection, not latest-main assumptions.
internal static class SdkUsageAdapter
{
    // Separate projection: model costs must never change the independent session total.
    // Null means the final RPC supplied no map; an empty map replaces event attribution.
    internal static ImmutableArray<ModelUsageSnapshot>? ModelsFromFinal(UsageGetMetricsResult metrics,
        out bool truncated)
    {
        truncated = false;
        if (metrics.ModelMetrics is null) { return null; }
        var models = ImmutableArray.CreateBuilder<ModelUsageSnapshot>();
        foreach (var pair in metrics.ModelMetrics)
        {
            if (models.Count >= ModelUsageSnapshot.MaximumModels)
            {
                truncated = true;
                break;
            }

            var usage = pair.Value?.Usage;
            bool invalid = false;
            decimal? nano = Number(pair.Value?.TotalNanoAiu, allowZero: true, ref invalid);
            decimal? premium = Number(pair.Value?.Requests?.Cost, allowZero: false, ref invalid);
            UsageMetrics value = UsageMath.Sanitize(new UsageMetrics(
                usage is null ? null : UnknownZero(usage.InputTokens),
                usage is null ? null : UnknownZero(usage.OutputTokens), usage?.ReasoningTokens,
                usage is null ? null : UnknownZero(usage.CacheReadTokens),
                usage is null ? null : UnknownZero(usage.CacheWriteTokens), nano, premium), out bool bad);
            models.Add(new ModelUsageSnapshot(ModelUsageSnapshot.CreateModelKey(pair.Key), value,
                invalid || bad || value.InputTokens is null || value.OutputTokens is null
                || value.ReasoningTokens is null || value.CacheReadTokens is null
                || value.CacheWriteTokens is null || nano is null || premium is null));
        }

        return models.ToImmutable();
    }

    internal static UsageMetrics FromEvent(AssistantUsageData data, out bool invalid)
    {
        if (data is null) { invalid = true; return new(); }
        bool invalidCost = false;
        decimal? nano = Number(data.CopilotUsage?.TotalNanoAiu, allowZero: false, ref invalidCost);
        UsageMetrics result = UsageMath.Sanitize(new UsageMetrics(
            data.InputTokens, data.OutputTokens, data.ReasoningTokens,
            data.CacheReadTokens, data.CacheWriteTokens, nano), out invalid);
        invalid |= invalidCost;
        return result;
    }

    internal static UsageMetrics FromFinal(UsageGetMetricsResult metrics,
        out UsageSource source, out bool partial, out bool invalid)
        => FromFinal(metrics, out source, out partial, out invalid, out _);

    internal static UsageMetrics FromFinal(UsageGetMetricsResult metrics,
        out UsageSource source, out bool partial, out bool invalid,
        out ImmutableDictionary<UsageMetric, MetricProvenance> provenance)
    {
        if (metrics is null)
        {
            source = UsageSource.None;
            partial = invalid = true;
            provenance = UsageProvenance.FromSource(new(), UsageSource.None);
            return new();
        }
        var models = new List<UsageMetrics>();
        bool invalidField = false;
        bool missingModel = false;
        if (metrics.ModelMetrics is not null)
        {
            foreach (var model in metrics.ModelMetrics.Values)
            {
                if (model?.Usage is not { } usage)
                {
                    missingModel = true;
                    continue;
                }

                // Nonnullable default zero has no JSON-presence evidence in this SDK.
                UsageMetrics value = UsageMath.Sanitize(new UsageMetrics(
                    UnknownZero(usage.InputTokens), UnknownZero(usage.OutputTokens), usage.ReasoningTokens,
                    UnknownZero(usage.CacheReadTokens), UnknownZero(usage.CacheWriteTokens)), out bool bad);
                invalidField |= bad;
                models.Add(value);
            }
        }

        UsageMetrics tokens = UsageMath.Sum(models, out bool overflow, out int[] counts);
        invalid = invalidField || overflow;
        source = UsageSource.FinalRpc;
        partial = missingModel || counts.Take(5).Any(c => c < models.Count);
        if (models.Count == 0)
        {
            tokens = UsageMath.Sanitize(new UsageMetrics(
                UnknownZero(metrics.LastCallInputTokens), UnknownZero(metrics.LastCallOutputTokens)), out bool bad);
            invalid |= bad;
            source = UsageSource.LastCall;
            partial = true;
        }

        // Session total is authoritative; model cost breakdown is NOT added to it.
        decimal? nano = Number(metrics.TotalNanoAiu, allowZero: true, ref invalid);
        decimal? premium = Number(metrics.TotalPremiumRequestCost, allowZero: false, ref invalid);
        UsageMetrics result = tokens with { TotalNanoAiu = nano, PremiumRequests = premium };
        UsageSource tokenSource = source;
        provenance = Enum.GetValues<UsageMetric>().ToImmutableDictionary(m => m, m =>
        {
            if (UsageProvenance.Value(result, m) is null) { return new MetricProvenance(); }
            bool isCost = m is UsageMetric.TotalNanoAiu or UsageMetric.PremiumRequests;
            return new MetricProvenance(isCost ? UsageSource.FinalRpc : tokenSource,
                !isCost && (tokenSource != UsageSource.FinalRpc || missingModel || counts[(int)m] < models.Count));
        });
        return result;
    }

    // Examine only this RPC, before events/LastCall or retained model attribution are merged.
    internal static ModelCostComparison CompareModelCosts(UsageGetMetricsResult metrics)
    {
        if (metrics?.ModelMetrics is not { Count: > 0 } map || map.Count > ModelUsageSnapshot.MaximumModels)
        {
            return ModelCostComparison.NotComparable;
        }
        bool invalid = false;
        decimal? session = Number(metrics.TotalNanoAiu, allowZero: true, ref invalid);
        var costs = new List<decimal?>();
        foreach (var model in map.Values)
        {
            costs.Add(Number(model?.TotalNanoAiu, allowZero: true, ref invalid));
        }
        return UsageProvenance.Compare(session, costs, invalid);
    }

    private static long? UnknownZero(long value) => value == 0 ? null : value;

    private static decimal? Number(double? value, bool allowZero, ref bool invalid)
    {
        if (value is not { } number) { return null; }
        if (!double.IsFinite(number) || number < 0)
        {
            invalid = true;
            return null;
        }

        if (number == 0 && !allowZero) { return null; }
        try
        {
            decimal result = checked((decimal)number);
            if (result == 0 && number != 0)
            {
                invalid = true; // Underflow must never silently display a nonzero cost as zero.
                return null;
            }

            return result;
        }
        catch (OverflowException)
        {
            invalid = true;
            return null;
        }
    }
}