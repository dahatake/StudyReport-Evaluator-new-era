using System.Text.Json;
using StudyReportEvaluator.App.Usage;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Usage;

public sealed class JobCostSnapshotTests
{
    [Theory]
    [InlineData("RUN_FAILED")]
    [InlineData("OUTPUT_INVALID")]
    [InlineData("CHECKPOINT_SAVE_FAILED")]
    [InlineData("INPUT_CHANGED")]
    public async Task Backend_failure_codes_are_failed_in_terminal_record(string code)
    {
        string directory = Path.Combine(Path.GetTempPath(), "job-cost-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var tracker = new JobUsageTracker(logDirectory: directory);
            await tracker.CompleteAsync(code);
            string[] lines = await File.ReadAllLinesAsync(Assert.IsType<string>(tracker.Snapshot.LogPath), TestContext.Current.CancellationToken);
            using JsonDocument final = JsonDocument.Parse(lines[^1]);
            Assert.Equal(4, final.RootElement.GetProperty("Completion").GetInt32()); // Failed, not Unknown.
            Assert.Contains("Failed", tracker.Snapshot.LogText, StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }

    [Theory]
    [InlineData(UsageSource.Events)]
    [InlineData(UsageSource.LastCall)]
    [InlineData(UsageSource.FinalRpc)]
    public async Task Typed_snapshot_has_utc_times_immutable_breakdown_and_per_metric_coverage(UsageSource source)
    {
        string directory = Path.Combine(Path.GetTempPath(), "job-cost-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var tracker = new JobUsageTracker(logDirectory: directory);
            JobCostSnapshot initial = tracker.Snapshot;
            Assert.Null(initial.ObservedAtUtc);
            Assert.Null(initial.EndedAtUtc);
            Guid id = tracker.BeginAttempt(UsageOperation.Reference);
            tracker.ReplaceAttempt(new(id, UsageOperation.Reference, 1, new(InputTokens: 0), source,
                IsFinished: true, IsPartial: false, Status: UsageObservationStatus.FinalObserved));
            await tracker.CompleteAsync("SUCCESS");
            JobCostSnapshot snapshot = tracker.Snapshot;
            Assert.Equal(0L, snapshot.Metrics.InputTokens);
            Assert.Null(snapshot.Metrics.OutputTokens);
            Assert.Equal(1, snapshot.MetricObservations[UsageMetric.InputTokens].ObservedAttemptCount);
            Assert.Equal(source != UsageSource.FinalRpc, snapshot.MetricObservations[UsageMetric.InputTokens].IsPartial);
            Assert.True(snapshot.MetricObservations[UsageMetric.OutputTokens].IsPartial);
            Assert.Equal(1, snapshot.AttemptStatuses[UsageObservationStatus.FinalObserved]);
            Assert.Equal(UsageOperation.Reference, Assert.Single(snapshot.OperationBreakdown).Operation);
            Assert.Empty(initial.OperationBreakdown);
            Assert.Equal(TimeSpan.Zero, snapshot.StartedAtUtc!.Value.Offset);
            Assert.Equal(TimeSpan.Zero, snapshot.ObservedAtUtc!.Value.Offset);
            Assert.Equal(TimeSpan.Zero, snapshot.EndedAtUtc!.Value.Offset);
            Assert.True(snapshot.StartedAtUtc <= snapshot.ObservedAtUtc);
            Assert.True(snapshot.ObservedAtUtc <= snapshot.EndedAtUtc);
            Assert.Contains(source == UsageSource.FinalRpc ? "・観測完了" : "・部分取得", snapshot.SummaryText, StringComparison.Ordinal);
            if (source != UsageSource.FinalRpc)
            {
                Assert.DoesNotContain("観測完了", snapshot.LogText, StringComparison.Ordinal);
            }
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }

    [Theory]
    [InlineData("OUTPUT_INVALID")]
    [InlineData("CHECKPOINT_SAVE_FAILED")]
    public async Task Summaryless_terminal_failures_keep_observed_metrics_and_failure_reason(string code)
    {
        string directory = Path.Combine(Path.GetTempPath(), "job-cost-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var tracker = new JobUsageTracker(logDirectory: directory);
            Guid id = tracker.BeginAttempt(UsageOperation.Normal);
            tracker.ReplaceAttempt(new(id, UsageOperation.Normal, 1,
                new(InputTokens: 31, OutputTokens: 4), UsageSource.Events,
                IsFinished: false, IsPartial: true, Status: UsageObservationStatus.EventObserved));

            await tracker.CompleteAsync(code);

            JobCostSnapshot snapshot = tracker.Snapshot;
            Assert.Equal(31, snapshot.Metrics.InputTokens);
            Assert.Equal(4, snapshot.Metrics.OutputTokens);
            Assert.Contains("終了", snapshot.SummaryText, StringComparison.Ordinal);
            Assert.Contains("ジョブ終了（Failed）", snapshot.LogText, StringComparison.Ordinal);
            string[] lines = await File.ReadAllLinesAsync(Assert.IsType<string>(snapshot.LogPath),
                TestContext.Current.CancellationToken);
            using JsonDocument final = JsonDocument.Parse(lines[^1]);
            Assert.Equal(4, final.RootElement.GetProperty("Completion").GetInt32());
            Assert.Equal(31, final.RootElement.GetProperty("Metrics").GetProperty("InputTokens").GetInt64());
            Assert.Equal(4, final.RootElement.GetProperty("Metrics").GetProperty("OutputTokens").GetInt64());
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }
}