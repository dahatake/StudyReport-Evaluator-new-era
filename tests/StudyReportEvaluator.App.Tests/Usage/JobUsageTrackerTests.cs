using System.Text.Json;
using StudyReportEvaluator.App.Usage;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Usage;

// Requirements: FR-043 (AC-044)
public sealed class JobUsageTrackerTests
{
    [Fact]
    public async Task Revisions_replace_even_when_lower_and_distinct_attempts_add()
    {
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(logDirectory: temp.Path);
        Guid first = tracker.BeginAttempt(UsageOperation.Normal);
        Guid retry = tracker.BeginAttempt(UsageOperation.Normal);
        tracker.ReplaceAttempt(new(first, UsageOperation.Normal, 1, new(InputTokens: 100), UsageSource.Events));
        tracker.ReplaceAttempt(new(first, UsageOperation.Normal, 2, new(InputTokens: 80), UsageSource.FinalRpc, true));
        tracker.ReplaceAttempt(new(first, UsageOperation.Normal, 1, new(InputTokens: 999), UsageSource.Events));
        tracker.ReplaceAttempt(new(first, UsageOperation.Normal, 3, new(InputTokens: 999), UsageSource.FinalRpc, true));
        tracker.ReplaceAttempt(new(retry, UsageOperation.Normal, 1, new(InputTokens: 7), UsageSource.Events));
        await tracker.CompleteAsync("SUCCESS");

        using JsonDocument final = await ReadFinalAsync(tracker);
        Assert.Equal(87, final.RootElement.GetProperty("Metrics").GetProperty("InputTokens").GetInt64());
        Assert.Equal(2, final.RootElement.GetProperty("AttemptCount").GetInt32());
        Assert.Contains("下方訂正あり: 1 試行", tracker.Snapshot.DetailsText, StringComparison.Ordinal);
        Assert.Contains("観測値の下方訂正あり", tracker.Snapshot.LogText, StringComparison.Ordinal);
        Assert.True(tracker.Snapshot.IsFinished);
        string before = tracker.Snapshot.SummaryText;
        tracker.ReplaceAttempt(new(retry, UsageOperation.Normal, 99, new(InputTokens: 999), UsageSource.FinalRpc));
        Assert.Equal(before, tracker.Snapshot.SummaryText);
        Assert.Equal(Guid.Empty, tracker.BeginAttempt(UsageOperation.Reference));
    }

    [Fact]
    public async Task Unknown_and_explicit_zero_are_distinct_and_partial_coverage_is_retained()
    {
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(logDirectory: temp.Path);
        Guid id = tracker.BeginAttempt(UsageOperation.Special);
        tracker.ReplaceAttempt(new(id, UsageOperation.Special, 1,
            new(InputTokens: 0, TotalNanoAiu: 0.000000000001m, PremiumRequests: 0.5m), UsageSource.Events));
        tracker.BeginAttempt(UsageOperation.Similarity);
        await tracker.CompleteAsync("Cancelled");

        using JsonDocument final = await ReadFinalAsync(tracker);
        JsonElement metrics = final.RootElement.GetProperty("Metrics");
        Assert.Equal(0, metrics.GetProperty("InputTokens").GetInt64());
        Assert.Equal(JsonValueKind.Null, metrics.GetProperty("OutputTokens").ValueKind);
        Assert.Equal(0.000000000001m, metrics.GetProperty("TotalNanoAiu").GetDecimal());
        Assert.Equal(0.5m, metrics.GetProperty("PremiumRequests").GetDecimal());
        Assert.Contains("観測 1/2", tracker.Snapshot.SummaryText, StringComparison.Ordinal);
        Assert.Contains("AIクレジット <0.0001（SDK報告値から換算・観測 1/2 試行・部分取得）", tracker.Snapshot.SummaryText, StringComparison.Ordinal);
        Assert.DoesNotContain("課金単位未確認", tracker.Snapshot.SummaryText, StringComparison.Ordinal);
        Assert.Contains("nano-AI units（SDK報告値） 0.000000000001（部分取得）", tracker.Snapshot.LogText, StringComparison.Ordinal);
        Assert.Contains("プレミアムリクエスト消費量 0.5（部分取得）", tracker.Snapshot.LogText, StringComparison.Ordinal);
        Assert.Contains("推論 —（未取得）", tracker.Snapshot.LogText, StringComparison.Ordinal);
        Assert.Contains("キャッシュ読み取り —（未取得）", tracker.Snapshot.LogText, StringComparison.Ordinal);
        Assert.Contains("／AIクレジット <0.0001（SDK報告値から換算・部分取得）", tracker.Snapshot.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("課金単位未確認", tracker.Snapshot.LogText, StringComparison.Ordinal);
        Assert.Equal("CurrentInvocation", final.RootElement.GetProperty("AggregationScope").GetString());
        Assert.Equal("SdkReportedNanoAiuAndPremiumRequests_NoCreditOrCurrencyConversion_v1",
            final.RootElement.GetProperty("UnitPolicy").GetString());
    }

    [Fact]
    public async Task All_missing_metrics_remain_unknown_in_terminal_record()
    {
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(logDirectory: temp.Path);
        _ = tracker.BeginAttempt(UsageOperation.Normal);

        await tracker.CompleteAsync("SUCCESS");

        using JsonDocument final = await ReadFinalAsync(tracker);
        JsonElement metrics = final.RootElement.GetProperty("Metrics");
        Assert.Equal(JsonValueKind.Null, metrics.GetProperty("InputTokens").ValueKind);
        Assert.Equal(JsonValueKind.Null, metrics.GetProperty("OutputTokens").ValueKind);
        Assert.Equal(JsonValueKind.Null, metrics.GetProperty("TotalNanoAiu").ValueKind);
        Assert.Equal(JsonValueKind.Null, metrics.GetProperty("PremiumRequests").ValueKind);
        Assert.DoesNotContain("入力 0", tracker.Snapshot.SummaryText, StringComparison.Ordinal);
        Assert.Contains("未取得", tracker.Snapshot.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Jsonl_contains_no_presentation_path_or_arbitrary_completion_text()
    {
        using var temp = new TemporaryDirectory();
        const string canary = "PRIVATE_PROMPT_ANSWER_ghp_CREDENTIAL_C:\\private\\student.xlsx";
        var context = new JobUsageContext("0.8.6+PRIVATE_PROMPT", "1.0.11", "1.0.79",
            canary, 3, true);
        Assert.Null(new JobUsageContext(canary, canary, canary, maxConcurrency: 99).ApplicationVersion);
        Assert.Null(new JobUsageContext(maxConcurrency: 99).MaxConcurrency);
        await using var tracker = new JobUsageTracker(_ => throw new InvalidOperationException(canary), temp.Path, context);
        await tracker.CompleteAsync(canary);
        string path = Assert.IsType<string>(tracker.Snapshot.LogPath);
        Assert.Equal($"{tracker.Snapshot.JobId:N}.jsonl", System.IO.Path.GetFileName(path));
        string text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("PRIVATE_PROMPT", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_CREDENTIAL", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SummaryText", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DetailsText", text, StringComparison.Ordinal);
        Assert.DoesNotContain("LogText", text, StringComparison.Ordinal);
        Assert.DoesNotContain("LogPath", text, StringComparison.Ordinal);
        Assert.DoesNotContain(temp.Path, text, StringComparison.Ordinal);
        foreach (string line in await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken))
        {
            using JsonDocument entry = JsonDocument.Parse(line);
            Assert.Equal(1, entry.RootElement.GetProperty("SchemaVersion").GetInt32());
            if (entry.RootElement.GetProperty("Context").ValueKind != JsonValueKind.Null)
            {
                JsonElement recorded = entry.RootElement.GetProperty("Context");
                Assert.Equal("0.8.6", recorded.GetProperty("ApplicationVersion").GetString());
                Assert.Equal("1.0.11", recorded.GetProperty("SdkVersion").GetString());
                Assert.Equal("1.0.79", recorded.GetProperty("CliVersion").GetString());
                Assert.Equal(3, recorded.GetProperty("MaxConcurrency").GetInt32());
                Assert.True(recorded.GetProperty("IsResume").GetBoolean());
                Assert.Equal(ModelUsageSnapshot.CreateModelKey(canary), recorded.GetProperty("RequestedModelKey").GetString());
            }
        }
    }

    [Fact]
    public async Task Requested_reasoning_effort_is_recorded_per_attempt_and_caller_text_is_not()
    {
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(logDirectory: temp.Path);
        Guid medium = tracker.BeginAttempt(UsageOperation.Normal);
        Guid unset = tracker.BeginAttempt(UsageOperation.Reference);
        Guid xhigh = tracker.BeginAttempt(UsageOperation.Normal);
        Guid invalid = tracker.BeginAttempt(UsageOperation.Normal);
        tracker.ReplaceAttempt(new(medium, UsageOperation.Normal, 1, new(), UsageSource.Events) { RequestedReasoningEffort = "medium" });
        tracker.ReplaceAttempt(new(unset, UsageOperation.Reference, 1, new(), UsageSource.Events));
        tracker.ReplaceAttempt(new(xhigh, UsageOperation.Normal, 1, new(), UsageSource.Events) { RequestedReasoningEffort = "xhigh" });
        tracker.ReplaceAttempt(new(invalid, UsageOperation.Normal, 1, new(), UsageSource.Events) { RequestedReasoningEffort = "PRIVATE\nCANARY" });
        await tracker.CompleteAsync("SUCCESS");

        string path = Assert.IsType<string>(tracker.Snapshot.LogPath);
        Dictionary<Guid, string?> recorded = [];
        foreach (string line in await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken))
        {
            using JsonDocument entry = JsonDocument.Parse(line);
            if (entry.RootElement.GetProperty("Attempt") is { ValueKind: JsonValueKind.Object } attempt
                && attempt.GetProperty("Revision").GetInt64() == 1)
            {
                JsonElement effort = attempt.GetProperty("RequestedReasoningEffort");
                recorded[attempt.GetProperty("AttemptId").GetGuid()] =
                    effort.ValueKind == JsonValueKind.Null ? null : effort.GetString();
            }
        }

        Assert.Equal("medium", recorded[medium]);
        Assert.Null(recorded[unset]);
        Assert.Equal("xhigh", recorded[xhigh]);
        Assert.Null(recorded[invalid]);
        Assert.DoesNotContain("PRIVATE", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writer_failure_does_not_erase_observations_or_throw()
    {
        using var temp = new TemporaryDirectory();
        Directory.CreateDirectory(temp.Path);
        string blocker = System.IO.Path.Combine(temp.Path, "not-a-directory");
        await File.WriteAllTextAsync(blocker, "blocker", TestContext.Current.CancellationToken);
        await using var tracker = new JobUsageTracker(logDirectory: blocker);
        Guid id = tracker.BeginAttempt(UsageOperation.Reference);
        tracker.ReplaceAttempt(new(id, UsageOperation.Reference, 1, new(InputTokens: 19), UsageSource.Events));
        await tracker.CompleteAsync("Failed");
        Assert.Contains("入力 19", tracker.Snapshot.SummaryText, StringComparison.Ordinal);
        Assert.Contains("保存失敗", tracker.Snapshot.LogStatusText, StringComparison.Ordinal);
        Assert.True(tracker.Snapshot.IsFinished);
    }

    [Fact]
    public async Task Recent_log_is_bounded_and_parallel_revisions_do_not_regress()
    {
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(logDirectory: temp.Path);
        Guid id = tracker.BeginAttempt(UsageOperation.Normal);
        // Ensure more than 200 accepted snapshots before racing additional revisions.
        for (int i = 1; i <= 210; i++)
        {
            tracker.ReplaceAttempt(new(id, UsageOperation.Normal, i, new(InputTokens: i), UsageSource.Events));
        }

        Parallel.For(211, 231, i => tracker.ReplaceAttempt(new(id, UsageOperation.Normal, i,
            new(InputTokens: i), UsageSource.Events)));
        await tracker.CompleteAsync("Completed");
        Assert.Equal(200, tracker.Snapshot.LogText.Split(Environment.NewLine).Length);
        Assert.Contains("入力 230", tracker.Snapshot.SummaryText, StringComparison.Ordinal);
        using JsonDocument final = await ReadFinalAsync(tracker);
        Assert.Equal(230, final.RootElement.GetProperty("Metrics").GetProperty("InputTokens").GetInt64());
    }

    [Fact]
    public async Task Overflow_and_negative_values_are_not_saturated_or_reported_as_zero()
    {
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(logDirectory: temp.Path);
        Guid first = tracker.BeginAttempt(UsageOperation.Normal);
        Guid second = tracker.BeginAttempt(UsageOperation.Special);
        tracker.ReplaceAttempt(new(first, UsageOperation.Normal, 1,
            new(InputTokens: long.MaxValue, OutputTokens: -1), UsageSource.Events));
        tracker.ReplaceAttempt(new(second, UsageOperation.Special, 1, new(InputTokens: 1), UsageSource.Events));
        await tracker.CompleteAsync("Completed");
        using JsonDocument final = await ReadFinalAsync(tracker);
        Assert.True(final.RootElement.GetProperty("HasInvalidValues").GetBoolean());
        Assert.Equal(JsonValueKind.Null, final.RootElement.GetProperty("Metrics").GetProperty("InputTokens").ValueKind);
        Assert.Contains("異常値", tracker.Snapshot.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelled_job_keeps_observed_usage_for_unsaved_row()
    {
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(logDirectory: temp.Path);
        Guid id = tracker.BeginAttempt(UsageOperation.Normal);
        tracker.ReplaceAttempt(new(id, UsageOperation.Normal, 1,
            new(InputTokens: 13, OutputTokens: 2, TotalNanoAiu: 0.25m), UsageSource.Events,
            IsFinished: false, IsPartial: true, Status: UsageObservationStatus.SendPending));

        await tracker.CompleteAsync("CANCELLED");

        using JsonDocument final = await ReadFinalAsync(tracker);
        JsonElement root = final.RootElement;
        Assert.Equal(2, root.GetProperty("Completion").GetInt32());
        Assert.Equal(13, root.GetProperty("Metrics").GetProperty("InputTokens").GetInt64());
        Assert.Equal(2, root.GetProperty("Metrics").GetProperty("OutputTokens").GetInt64());
        Assert.Equal(0.25m, root.GetProperty("Metrics").GetProperty("TotalNanoAiu").GetDecimal());
        Assert.Contains("今回のジョブのみ。再試行・失敗・未保存行を含む観測値", tracker.Snapshot.DetailsText,
            StringComparison.Ordinal);
        Assert.Contains("ジョブ終了（Cancelled）", tracker.Snapshot.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Operation_breakdown_keeps_all_operations_separate_under_parallel_updates()
    {
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(logDirectory: temp.Path);
        (UsageOperation Operation, long Tokens)[] cases =
        [
            (UsageOperation.Normal, 11),
            (UsageOperation.Reference, 13),
            (UsageOperation.Special, 17),
            (UsageOperation.Similarity, 19),
        ];
        var attempts = cases.Select(item => (item.Operation, item.Tokens, AttemptId: tracker.BeginAttempt(item.Operation)))
            .ToArray();

        using Barrier start = new(3);
        Task[] concurrent = attempts.Take(3).Select(item => Task.Run(() =>
        {
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            tracker.ReplaceAttempt(new(item.AttemptId, item.Operation, 1,
                new(InputTokens: item.Tokens, TotalNanoAiu: item.Tokens / 10m), UsageSource.Events)
            {
                Models =
                [
                    new ModelUsageSnapshot(ModelUsageSnapshot.CreateModelKey(item.Operation.ToString()),
                        new(InputTokens: item.Tokens, TotalNanoAiu: item.Tokens / 10m)),
                ],
            });
        })).ToArray();
        await Task.WhenAll(concurrent);
        var last = attempts[^1];
        tracker.ReplaceAttempt(new(last.AttemptId, last.Operation, 1,
            new(InputTokens: last.Tokens, TotalNanoAiu: last.Tokens / 10m), UsageSource.Events)
        {
            Models =
            [
                new ModelUsageSnapshot(ModelUsageSnapshot.CreateModelKey(last.Operation.ToString()),
                    new(InputTokens: last.Tokens, TotalNanoAiu: last.Tokens / 10m)),
            ],
        });

        await tracker.CompleteAsync("SUCCESS");
        JobCostSnapshot snapshot = tracker.Snapshot;
        Assert.Equal(60, snapshot.Metrics.InputTokens);
        Assert.Equal(6.0m, snapshot.Metrics.TotalNanoAiu);
        Assert.Equal(4, snapshot.AttemptCount);
        Assert.Equal(Enum.GetValues<UsageOperation>().OrderBy(item => item).ToArray(),
            snapshot.OperationBreakdown.Select(item => item.Operation).ToArray());
        Assert.Equal(4, snapshot.Models.Length);
        foreach (var expected in cases)
        {
            OperationUsageSnapshot operation = Assert.Single(snapshot.OperationBreakdown,
                item => item.Operation == expected.Operation);
            Assert.Equal(1, operation.AttemptCount);
            Assert.Equal(expected.Tokens, operation.Metrics.InputTokens);
            Assert.Equal(expected.Tokens / 10m, operation.Metrics.TotalNanoAiu);
            ModelUsageSnapshot model = Assert.Single(snapshot.Models,
                item => item.ModelKey == ModelUsageSnapshot.CreateModelKey(expected.Operation.ToString()));
            Assert.Equal(expected.Tokens, model.Metrics.InputTokens);
            Assert.Equal(expected.Tokens / 10m, model.Metrics.TotalNanoAiu);
        }
    }

    [Fact]
    public async Task Unobserved_retry_counts_without_manufacturing_zero_usage()
    {
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(logDirectory: temp.Path);
        Guid observed = tracker.BeginAttempt(UsageOperation.Normal);
        _ = tracker.BeginAttempt(UsageOperation.Normal);
        tracker.ReplaceAttempt(new(observed, UsageOperation.Normal, 1,
            new(InputTokens: 7), UsageSource.Events));

        await tracker.CompleteAsync("SUCCESS");

        using JsonDocument final = await ReadFinalAsync(tracker);
        Assert.Equal(2, final.RootElement.GetProperty("AttemptCount").GetInt32());
        JsonElement metrics = final.RootElement.GetProperty("Metrics");
        Assert.Equal(7, metrics.GetProperty("InputTokens").GetInt64());
        Assert.Equal(JsonValueKind.Null, metrics.GetProperty("OutputTokens").ValueKind);
        Assert.Contains("観測 1/2", tracker.Snapshot.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resume_context_logs_current_invocation_without_importing_previous_usage()
    {
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(
            logDirectory: temp.Path,
            context: new JobUsageContext(requestedModelKey: "model-test", isResume: true));
        Guid id = tracker.BeginAttempt(UsageOperation.Normal);
        tracker.ReplaceAttempt(new(id, UsageOperation.Normal, 1,
            new(InputTokens: 23, TotalNanoAiu: 0.75m), UsageSource.FinalRpc,
            IsFinished: true, IsPartial: false, Status: UsageObservationStatus.FinalObserved));

        await tracker.CompleteAsync("SUCCESS");

        using JsonDocument final = await ReadFinalAsync(tracker);
        JsonElement root = final.RootElement;
        Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal("CurrentInvocation", root.GetProperty("AggregationScope").GetString());
        Assert.Equal(1, root.GetProperty("AttemptCount").GetInt32());
        Assert.Equal(23, root.GetProperty("Metrics").GetProperty("InputTokens").GetInt64());
        Assert.Equal(0.75m, root.GetProperty("Metrics").GetProperty("TotalNanoAiu").GetDecimal());
        Assert.True(root.GetProperty("Context").GetProperty("IsResume").GetBoolean());
    }

    private static async Task<JsonDocument> ReadFinalAsync(JobUsageTracker tracker)
    {
        Assert.StartsWith("保存済み", tracker.Snapshot.LogStatusText, StringComparison.Ordinal);
        string path = Assert.IsType<string>(tracker.Snapshot.LogPath);
        string[] lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        return JsonDocument.Parse(lines[^1]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "StudyReportEvaluator-usage-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path)) { Directory.Delete(Path, recursive: true); }
        }
    }
}