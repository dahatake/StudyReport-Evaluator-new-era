using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using StudyReportEvaluator.App.Usage;
using Xunit;

// SDK 1.0.11 usage.getMetrics is experimental; keep suppression local to these adapter tests.
#pragma warning disable GHCP001

namespace StudyReportEvaluator.App.Tests.Usage;

public sealed class SdkUsageAdapterTests
{
    [Fact]
    public void FromEvent_preserves_explicit_zero_input_and_leaves_absent_input_unknown()
    {
        UsageMetrics absent = SdkUsageAdapter.FromEvent(new AssistantUsageData
        {
            Model = "test-model",
        }, out bool absentInvalid);
        UsageMetrics zero = SdkUsageAdapter.FromEvent(new AssistantUsageData
        {
            Model = "test-model",
            InputTokens = 0,
        }, out bool zeroInvalid);

        Assert.Null(absent.InputTokens);
        Assert.Equal(0L, zero.InputTokens);
        Assert.Null(zero.OutputTokens);
        Assert.False(absentInvalid);
        Assert.False(zeroInvalid);
    }

    [Fact]
    public void FromFinal_model_totals_replace_events_without_adding_model_prices()
    {
        var metrics = new UsageGetMetricsResult
        {
            LastCallInputTokens = 999,
            LastCallOutputTokens = 999,
            TotalNanoAiu = 100,
            TotalPremiumRequestCost = 0.5,
            ModelMetrics = new Dictionary<string, UsageMetricsModelMetric>
            {
                ["first-model"] = new UsageMetricsModelMetric
                {
                    Usage = new UsageMetricsModelMetricUsage
                    {
                        InputTokens = 10, OutputTokens = 2, ReasoningTokens = 0,
                        CacheReadTokens = 3, CacheWriteTokens = 4,
                    },
                    TotalNanoAiu = 40,
                    Requests = new UsageMetricsModelMetricRequests { Cost = 0.25, Count = 1 },
                },
                ["second-model"] = new UsageMetricsModelMetric
                {
                    Usage = new UsageMetricsModelMetricUsage
                    {
                        InputTokens = 20, OutputTokens = 5, ReasoningTokens = 1,
                        CacheReadTokens = 6, CacheWriteTokens = 7,
                    },
                    TotalNanoAiu = 60,
                    Requests = new UsageMetricsModelMetricRequests { Cost = 0.25, Count = 1 },
                },
            },
        };

        UsageMetrics final = SdkUsageAdapter.FromFinal(metrics, out UsageSource source,
            out bool partial, out bool invalid);
        UsageMetrics events = SdkUsageAdapter.FromEvent(new AssistantUsageData
        {
            Model = "test-model",
            InputTokens = 300,
            OutputTokens = 70,
            ReasoningTokens = 10,
            CacheReadTokens = 90,
            CacheWriteTokens = 110,
            CopilotUsage = new AssistantUsageCopilotUsage { TotalNanoAiu = 1000 },
        }, out bool eventInvalid);

        Assert.Equal(new UsageMetrics(30, 7, 1, 9, 11, 100m, 0.5m), final);
        Assert.Equal(final, UsageMath.PreferFinal(final, events));
        Assert.Equal(UsageSource.FinalRpc, source);
        Assert.False(partial);
        Assert.False(invalid);
        Assert.False(eventInvalid);
    }

    [Fact]
    public void FromFinal_empty_model_metrics_uses_last_call_as_partial()
    {
        UsageMetrics result = SdkUsageAdapter.FromFinal(new UsageGetMetricsResult
        {
            ModelMetrics = new Dictionary<string, UsageMetricsModelMetric>(),
            LastCallInputTokens = 12,
            LastCallOutputTokens = 0,
        }, out UsageSource source, out bool partial, out bool invalid);

        Assert.Equal(new UsageMetrics(InputTokens: 12), result);
        Assert.Equal(UsageSource.LastCall, source);
        Assert.True(partial);
        Assert.False(invalid);
    }

    [Fact]
    public void FromFinal_preserves_nullable_nano_zero_but_nonnullable_premium_zero_is_unknown()
    {
        UsageMetrics absent = SdkUsageAdapter.FromFinal(new UsageGetMetricsResult(),
            out _, out _, out bool absentInvalid);
        UsageMetrics zero = SdkUsageAdapter.FromFinal(new UsageGetMetricsResult
        {
            TotalNanoAiu = 0,
            TotalPremiumRequestCost = 0,
        }, out _, out _, out bool zeroInvalid);

        Assert.Null(absent.TotalNanoAiu);
        Assert.Equal(0m, zero.TotalNanoAiu);
        Assert.Null(absent.PremiumRequests);
        Assert.Null(zero.PremiumRequests);
        Assert.False(absentInvalid);
        Assert.False(zeroInvalid);
    }

    [Fact]
    public void FromEvent_rejects_negative_tokens_and_nan_cost_without_erasing_valid_fields()
    {
        UsageMetrics result = SdkUsageAdapter.FromEvent(new AssistantUsageData
        {
            Model = "test-model",
            InputTokens = -1,
            OutputTokens = 2,
            ReasoningTokens = -1,
            CacheReadTokens = -1,
            CacheWriteTokens = 0,
            CopilotUsage = new AssistantUsageCopilotUsage { TotalNanoAiu = double.NaN },
        }, out bool invalid);

        Assert.Equal(new UsageMetrics(OutputTokens: 2, CacheWriteTokens: 0), result);
        Assert.True(invalid);
    }

    [Fact]
    public void FromFinal_rejects_negative_model_tokens_nan_nano_and_negative_premium_cost()
    {
        UsageMetrics result = SdkUsageAdapter.FromFinal(new UsageGetMetricsResult
        {
            TotalNanoAiu = double.NaN,
            TotalPremiumRequestCost = -0.5,
            ModelMetrics = new Dictionary<string, UsageMetricsModelMetric>
            {
                ["test-model"] = new UsageMetricsModelMetric
                {
                    Usage = new UsageMetricsModelMetricUsage
                    {
                        InputTokens = -1, OutputTokens = 2, ReasoningTokens = 0,
                        CacheReadTokens = 3, CacheWriteTokens = 4,
                    },
                },
            },
        }, out UsageSource source, out bool partial, out bool invalid);

        Assert.Equal(new UsageMetrics(OutputTokens: 2, ReasoningTokens: 0,
            CacheReadTokens: 3, CacheWriteTokens: 4), result);
        Assert.Equal(UsageSource.FinalRpc, source);
        Assert.True(partial);
        Assert.True(invalid);
    }
}

#pragma warning restore GHCP001