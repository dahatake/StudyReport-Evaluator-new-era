using System.Collections.Immutable;
using System.Text.Json;
using GitHub.Copilot.Rpc;
using StudyReportEvaluator.App.Logging;
using StudyReportEvaluator.App.Usage;
using Xunit;

#pragma warning disable GHCP001 // Fixed SDK 1.0.11 experimental usage metrics.

namespace StudyReportEvaluator.App.Tests.Usage;

public sealed class JobCostModelTests
{
    [Fact]
    public void Final_models_are_bounded_anonymous_and_independent_of_authoritative_total()
    {
        const string canary = "gpt-PRIVATE_PROMPT_ghp_SECRET";
        var rpc = new UsageGetMetricsResult
        {
            TotalNanoAiu = 999,
            ModelMetrics = new Dictionary<string, UsageMetricsModelMetric>
            {
                [canary] = new()
                {
                    Usage = new() { InputTokens = 10, OutputTokens = 2, ReasoningTokens = 0, CacheReadTokens = 3, CacheWriteTokens = 4 },
                    TotalNanoAiu = 7,
                    Requests = new() { Cost = 0.5 },
                },
            },
        };
        var models = SdkUsageAdapter.ModelsFromFinal(rpc, out bool truncated)!.Value;
        var model = Assert.Single(models);
        Assert.Equal(ModelUsageSnapshot.CreateModelKey(canary), model.ModelKey);
        Assert.Matches("^model-[0-9a-f]{16}$", model.ModelKey);
        Assert.DoesNotContain(canary, model.ModelKey, StringComparison.Ordinal);
        Assert.Equal(new UsageMetrics(10, 2, 0, 3, 4, 7m, 0.5m), model.Metrics);
        Assert.False(model.IsPartial);
        Assert.False(truncated);
        Assert.Equal(999m, SdkUsageAdapter.FromFinal(rpc, out _, out _, out _).TotalNanoAiu);

        for (int i = 0; i < ModelUsageSnapshot.MaximumModels; i++)
        {
            rpc.ModelMetrics.Add($"runtime-{i}", new());
        }
        Assert.Equal(ModelUsageSnapshot.MaximumModels, SdkUsageAdapter.ModelsFromFinal(rpc, out truncated)!.Value.Length);
        Assert.True(truncated);
        rpc.ModelMetrics.Clear();
        Assert.Empty(SdkUsageAdapter.ModelsFromFinal(rpc, out _)!.Value);
    // SDK 1.0.11 normalizes an assigned null ModelMetrics map to an empty map.
    // Keep the adapter's defensive null branch for deserialized/malformed SDK values,
    // but assert the actual public setter behavior here.
    #pragma warning disable CS8625
        rpc.ModelMetrics = null;
    #pragma warning restore CS8625
        Assert.Empty(SdkUsageAdapter.ModelsFromFinal(rpc, out _)!.Value);
    }

    [Fact]
    public async Task Tracker_replaces_then_groups_models_without_leaking_raw_identifiers()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-model-tests", Guid.NewGuid().ToString("N"));
        const string canary = "claude-PRIVATE_PROMPT_ghp_SECRET";
        string key = ModelUsageSnapshot.CreateModelKey(canary);
        try
        {
            await using var tracker = new JobUsageTracker(logDirectory: directory);
            Guid first = tracker.BeginAttempt(UsageOperation.Normal);
            Guid retry = tracker.BeginAttempt(UsageOperation.Normal);
            var observation = new AttemptUsageSnapshot(first, UsageOperation.Normal, 1,
                new(InputTokens: 100, TotalNanoAiu: 100), UsageSource.Events)
            {
                RequestedModelKey = canary, // Public input boundary must also reject raw identifiers.
                RequestedModelIsAuto = false,
                Models = [new(canary, new(InputTokens: 100, TotalNanoAiu: 10))],
            };
            tracker.ReplaceAttempt(observation);
            tracker.ReplaceAttempt(observation with
            {
                Revision = 2, Source = UsageSource.FinalRpc, IsFinished = true,
                Metrics = new(InputTokens: 80, TotalNanoAiu: 80),
                Models = [new(key, new(InputTokens: 8, TotalNanoAiu: 7))],
            });
            tracker.ReplaceAttempt(observation); // Stale event revision cannot restore event models.
            tracker.ReplaceAttempt(new(retry, UsageOperation.Normal, 1, new(InputTokens: 7, TotalNanoAiu: 70), UsageSource.Events)
            {
                RequestedModelKey = ModelUsageSnapshot.CreateModelKey("auto"), RequestedModelIsAuto = true,
                Models = [new(key, new(InputTokens: 2, TotalNanoAiu: 3))],
            });
            Assert.Equal(87, tracker.Snapshot.Metrics.InputTokens);
            Assert.Equal(150m, tracker.Snapshot.Metrics.TotalNanoAiu);
            var model = Assert.Single(tracker.Snapshot.Models);
            Assert.Equal(10, model.Metrics.InputTokens);
            Assert.Equal(10m, model.Metrics.TotalNanoAiu);
            Assert.True(model.IsPartial);
            Assert.Contains("報告モデル識別子（匿名化）", tracker.Snapshot.DetailsText, StringComparison.Ordinal);
            Assert.Contains("入力トークン", tracker.Snapshot.DetailsText, StringComparison.Ordinal);
            Assert.Contains("出力トークン", tracker.Snapshot.DetailsText, StringComparison.Ordinal);
            Assert.Contains("非auto", tracker.Snapshot.DetailsText, StringComparison.Ordinal);
            Assert.DoesNotContain(canary, tracker.Snapshot.DetailsText, StringComparison.Ordinal);

            tracker.ReplaceAttempt(new(retry, UsageOperation.Normal, 2, new(InputTokens: 7), UsageSource.Events)
            {
                Models = Enumerable.Range(0, ModelUsageSnapshot.MaximumModels + 2)
                    .Select(i => new ModelUsageSnapshot($"{canary}-{i}", new(InputTokens: i))).ToImmutableArray(),
            });
            Assert.Equal(ModelUsageSnapshot.MaximumModels, tracker.Snapshot.Models.Length);
            Assert.True(tracker.Snapshot.ModelsTruncated);
            await tracker.CompleteAsync("Completed");
            string text = await File.ReadAllTextAsync(Assert.IsType<string>(tracker.Snapshot.LogPath), TestContext.Current.CancellationToken);
            Assert.DoesNotContain(canary, text, StringComparison.Ordinal);
            Assert.Contains(key, text, StringComparison.Ordinal);
            foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // Uses the actual source-generated metadata; no reflection fallback.
                Assert.NotNull(JsonSerializer.Deserialize(line, JobCostJsonContext.Default.JobCostLogEntry));
            }
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }
    }
}

#pragma warning restore GHCP001