using System.Text.Json;
using StudyReportEvaluator.App.Usage;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Usage;

public sealed class AttemptOutcomeLoggingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Outcome_is_logged_once_without_changing_numeric_observations(bool observationFinished)
    {
        string directory = Path.Combine(Path.GetTempPath(), "job-cost-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var tracker = new JobUsageTracker(logDirectory: directory);
            Guid id = tracker.BeginAttempt(UsageOperation.Normal);
            Guid operationId = Guid.NewGuid();
            tracker.ReplaceAttempt(new(id, UsageOperation.Normal, 1, new(InputTokens: 0), UsageSource.Events,
                IsFinished: observationFinished, Status: UsageObservationStatus.EventObserved));
            JobCostSnapshot before = tracker.Snapshot;

            tracker.RecordAttemptOutcome(id, 2, operationId, UsageAttemptOutcome.CleanupFailed);
            tracker.RecordAttemptOutcome(id, 2, operationId, UsageAttemptOutcome.Succeeded);
            JobCostSnapshot after = tracker.Snapshot;
            Assert.Equal(before.Metrics, after.Metrics);
            Assert.Equal(before.ObservedAtUtc, after.ObservedAtUtc);
            Assert.Equal(before.AttemptCount, after.AttemptCount);
            Assert.Equal(before.AttemptStatuses[UsageObservationStatus.EventObserved],
                after.AttemptStatuses[UsageObservationStatus.EventObserved]);
            foreach (UsageMetric metric in Enum.GetValues<UsageMetric>())
            {
                Assert.Equal(before.MetricObservations[metric], after.MetricObservations[metric]);
            }
            Assert.Contains("CleanupFailed", after.LogText, StringComparison.Ordinal);
            Assert.DoesNotContain("Succeeded", after.LogText, StringComparison.Ordinal);

            // An outcome must not seal a pending observation or reopen an already sealed one.
            tracker.ReplaceAttempt(new(id, UsageOperation.Normal, 2, new(InputTokens: 7), UsageSource.FinalRpc,
                IsFinished: true, IsPartial: false, Status: UsageObservationStatus.FinalObserved));
            Assert.Equal(observationFinished ? 0L : 7L, tracker.Snapshot.Metrics.InputTokens);
            await tracker.CompleteAsync("FAILED");
            string[] lines = await File.ReadAllLinesAsync(Assert.IsType<string>(tracker.Snapshot.LogPath),
                TestContext.Current.CancellationToken);
            using JsonDocument record = JsonDocument.Parse(Assert.Single(lines, line =>
            {
                using JsonDocument json = JsonDocument.Parse(line);
                return json.RootElement.GetProperty("Event").GetInt32() == 4;
            }));
            JsonElement entry = record.RootElement;
            Assert.Equal((int)UsageAttemptOutcome.CleanupFailed, entry.GetProperty("AttemptOutcome").GetInt32());
            Assert.Equal(2, entry.GetProperty("AttemptNumber").GetInt32());
            Assert.Equal(operationId, entry.GetProperty("OperationId").GetGuid());
            Assert.Equal(id, entry.GetProperty("Attempt").GetProperty("AttemptId").GetGuid());
            Assert.Equal(1, entry.GetProperty("Attempt").GetProperty("Revision").GetInt64());
            Assert.Equal(observationFinished, entry.GetProperty("Attempt").GetProperty("IsFinished").GetBoolean());
            Assert.Equal(0, entry.GetProperty("Metrics").GetProperty("InputTokens").GetInt64());
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("Metrics").GetProperty("OutputTokens").ValueKind);
            if (OperatingSystem.IsWindows())
            {
                var read = await tracker.ReadLogAsync();
                Assert.False(read.ReadFailed);
                Assert.False(read.HasInvalidRecords);
                Assert.Contains(read.Lines, line => line.Contains("CleanupFailed", StringComparison.Ordinal));
            }
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }
}