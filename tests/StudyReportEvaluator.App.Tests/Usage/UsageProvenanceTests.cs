using System.Collections.Immutable;
using System.Text.Json;
using GitHub.Copilot.Rpc;
using StudyReportEvaluator.App.Usage;
using Xunit;

// SDK 1.0.11 usage.getMetrics is experimental; keep suppression local to these tests.
#pragma warning disable GHCP001

namespace StudyReportEvaluator.App.Tests.Usage;

public sealed class UsageProvenanceTests
{
    [Fact]
    public void Final_cost_is_complete_even_when_tokens_fall_back_to_last_call()
    {
        UsageMetrics final = SdkUsageAdapter.FromFinal(new UsageGetMetricsResult
        {
            ModelMetrics = new Dictionary<string, UsageMetricsModelMetric>(),
            LastCallInputTokens = 12,
            LastCallOutputTokens = 0,
            TotalNanoAiu = 5,
            TotalPremiumRequestCost = 0.25,
        }, out UsageSource source, out bool partial, out bool invalid,
            out ImmutableDictionary<UsageMetric, MetricProvenance> provenance);

        Assert.Equal(UsageSource.LastCall, source);
        Assert.True(partial);
        Assert.False(invalid);
        Assert.Equal(new MetricProvenance(UsageSource.LastCall, IsPartial: true), provenance[UsageMetric.InputTokens]);
        Assert.Equal(new MetricProvenance(UsageSource.FinalRpc, IsPartial: false), provenance[UsageMetric.TotalNanoAiu]);
        Assert.Equal(new MetricProvenance(UsageSource.FinalRpc, IsPartial: false), provenance[UsageMetric.PremiumRequests]);
        Assert.Equal(new MetricProvenance(), provenance[UsageMetric.OutputTokens]);

        // Cumulative events outrank a last-call-only token value; cost keeps its own provenance.
        UsageMetrics merged = UsageProvenance.MergeFinal(final, new UsageMetrics(InputTokens: 100),
            provenance, out ImmutableDictionary<UsageMetric, MetricProvenance> metadata);
        Assert.Equal(100L, merged.InputTokens);
        Assert.Equal(5m, merged.TotalNanoAiu);
        Assert.Equal(new MetricProvenance(UsageSource.Events, IsPartial: true), metadata[UsageMetric.InputTokens]);
        Assert.Equal(new MetricProvenance(UsageSource.FinalRpc, IsPartial: false), metadata[UsageMetric.TotalNanoAiu]);
    }

    [Theory]
    [InlineData(40, 50, ModelCostComparison.Mismatch)]
    [InlineData(40, 60, ModelCostComparison.Match)]
    public void Model_cost_comparison_uses_one_final_rpc_only(double first, double second, ModelCostComparison expected) =>
        Assert.Equal(expected, SdkUsageAdapter.CompareModelCosts(new UsageGetMetricsResult
        {
            TotalNanoAiu = 100,
            ModelMetrics = new Dictionary<string, UsageMetricsModelMetric>
            {
                ["first"] = new UsageMetricsModelMetric { TotalNanoAiu = first },
                ["second"] = new UsageMetricsModelMetric { TotalNanoAiu = second },
            },
        }));

    [Fact]
    public void Missing_model_cost_is_not_comparable_and_is_never_allocated() =>
        Assert.Equal(ModelCostComparison.NotComparable, SdkUsageAdapter.CompareModelCosts(new UsageGetMetricsResult
        {
            TotalNanoAiu = 100,
            ModelMetrics = new Dictionary<string, UsageMetricsModelMetric>
            {
                ["first"] = new UsageMetricsModelMetric { TotalNanoAiu = 40 },
                ["second"] = new UsageMetricsModelMetric(),
            },
        }));

    [Fact]
    public async Task Per_metric_sources_and_mismatch_reach_the_snapshot_and_the_log()
    {
        string directory = Path.Combine(Path.GetTempPath(), "job-cost-provenance", Guid.NewGuid().ToString("N"));
        try
        {
            await using var tracker = new JobUsageTracker(logDirectory: directory);
            Guid observed = tracker.BeginAttempt(UsageOperation.Normal);
            Guid forged = tracker.BeginAttempt(UsageOperation.Reference);
            tracker.ReplaceAttempt(Attempt(observed, UsageOperation.Normal,
                UsageObservationStatus.FinalObserved, ModelCostComparison.Mismatch));
            // Same numbers, but an event-sourced attempt can never establish comparison evidence.
            tracker.ReplaceAttempt(Attempt(forged, UsageOperation.Reference,
                UsageObservationStatus.EventObserved, ModelCostComparison.Mismatch));

            JobCostSnapshot snapshot = tracker.Snapshot;
            Assert.Equal(1, snapshot.ModelCostMismatchCount);
            MetricObservation cost = snapshot.MetricObservations[UsageMetric.TotalNanoAiu];
            Assert.Equal(1, cost.SourceCounts[UsageSource.FinalRpc]);
            Assert.Equal(1, cost.SourceCounts[UsageSource.Events]);
            Assert.Equal(1, cost.CompleteAttemptCount);
            Assert.Equal(2, cost.ObservedAttemptCount);
            Assert.Contains("nano-AI unitsの取得元 イベント 1 試行／最終RPC 1 試行",
                snapshot.DetailsText, StringComparison.Ordinal);
            Assert.Contains("モデル内訳とセッション総量の不一致: 1 試行", snapshot.DetailsText, StringComparison.Ordinal);
            Assert.Contains("モデル内訳と総量の不一致 1 試行", snapshot.LogText, StringComparison.Ordinal);

            await tracker.CompleteAsync("Completed");
            string[] lines = await File.ReadAllLinesAsync(Assert.IsType<string>(tracker.Snapshot.LogPath),
                TestContext.Current.CancellationToken);
            using JsonDocument final = JsonDocument.Parse(lines[^1]);
            Assert.Equal(1, final.RootElement.GetProperty("ModelCostMismatchCount").GetInt32());
            JsonElement costEntry = final.RootElement.GetProperty("MetricObservations")
                .EnumerateObject().Single(p => p.Name is "5" or "TotalNanoAiu").Value;
            Assert.Equal(1, costEntry.GetProperty("SourceCounts")
                .EnumerateObject().Single(p => p.Name is "2" or "FinalRpc").Value.GetInt32());
            if (OperatingSystem.IsWindows())
            {
                var read = await tracker.ReadLogAsync();
                Assert.False(read.ReadFailed);
                Assert.False(read.HasInvalidRecords);
            }
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); } }
    }

    private static AttemptUsageSnapshot Attempt(Guid id, UsageOperation operation,
        UsageObservationStatus status, ModelCostComparison comparison) =>
        new(id, operation, 1, new UsageMetrics(InputTokens: 10, TotalNanoAiu: 100m),
            status == UsageObservationStatus.FinalObserved ? UsageSource.FinalRpc : UsageSource.Events,
            IsFinished: true, IsPartial: false, Status: status)
        {
            Models =
            [
                new ModelUsageSnapshot(ModelUsageSnapshot.CreateModelKey("first"), new UsageMetrics(TotalNanoAiu: 40m)),
                new ModelUsageSnapshot(ModelUsageSnapshot.CreateModelKey("second"), new UsageMetrics(TotalNanoAiu: 50m)),
            ],
            MetricProvenance = Enum.GetValues<UsageMetric>().ToImmutableDictionary(m => m,
                m => new MetricProvenance(status == UsageObservationStatus.FinalObserved
                    ? UsageSource.FinalRpc : UsageSource.Events, IsPartial: false)),
            ModelCostComparison = comparison,
        };
}

#pragma warning restore GHCP001
