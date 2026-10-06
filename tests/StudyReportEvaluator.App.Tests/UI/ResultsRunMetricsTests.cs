using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Usage;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using Xunit;
using Control = Avalonia.Controls.Control;

namespace StudyReportEvaluator.App.Tests.UI;

/// <summary>AC-053 / TR-48: total duration and AI credits on the results screen (§11.21).</summary>
// Requirements: FR-044 (AC-045)
public sealed class ResultsRunMetricsTests
{
    private const string Note = "GitHub Copilot SDKが報告したnano-AI unitsを1,000,000,000で割ったAIクレジットです。"
        + "今回のジョブで観測できた値だけで、請求確定額ではありません。";

    private static readonly DateTimeOffset Start = new(2026, 10, 1, 3, 34, 24, 100, TimeSpan.Zero);

    [Theory]
    [InlineData(0L, "00:00:00")]
    [InlineData(4_000_000L, "00:00:00")]
    [InlineData(1_298_000_000L, "00:02:09")]
    [InlineData(35_999_990_000L, "00:59:59")]
    [InlineData(36_000_000_000L, "01:00:00")]
    [InlineData(3_600_000_000_000L, "100:00:00")]
    public void Duration_truncates_seconds_and_widens_hours(long elapsedTicks, string expected)
    {
        Assert.Equal(expected, RunMetricsFormatter.FormatDuration(Start, Start.AddTicks(elapsedTicks)));
    }

    [Fact]
    public void Duration_is_not_measured_without_both_times_or_with_a_negative_span()
    {
        Assert.Equal("—（未計測）", RunMetricsFormatter.FormatDuration(null, Start));
        Assert.Equal("—（未計測）", RunMetricsFormatter.FormatDuration(Start, null));
        Assert.Equal("—（未計測）", RunMetricsFormatter.FormatDuration(Start, Start.AddTicks(-1)));
    }

    [Theory]
    [InlineData("0", "0.0000")]
    [InlineData("0.000000000001", "<0.0001")]
    [InlineData("49999", "<0.0001")]
    [InlineData("50000", "0.0001")]
    [InlineData("149999", "0.0001")]
    [InlineData("150000", "0.0002")]
    [InlineData("12345678901", "12.3457")]
    [InlineData("1000000000", "1.0000")]
    [InlineData("1234567890000000", "1,234,567.8900")]
    public void Ai_credits_divide_by_one_billion_and_round_half_away_from_zero(string nanoAiu, string expected)
    {
        string actual = RunMetricsFormatter.FormatAiCredits(decimal.Parse(nanoAiu, System.Globalization.CultureInfo.InvariantCulture));
        Assert.IsType<string>(actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Negative_nano_ai_units_are_rejected_rather_than_shown()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RunMetricsFormatter.FormatAiCredits(-1m));
    }

    [Fact]
    public async Task Heading_value_distinguishes_missing_record_no_send_unobserved_partial_and_complete()
    {
        using TemporaryDirectory temp = new();
        Assert.Equal("総実行時間 —（未計測） · AIクレジット —（コスト記録なし）", RunMetricsFormatter.DescribeRun(null));

        await using (JobUsageTracker none = new(logDirectory: temp.Path))
        {
            await none.CompleteAsync("SUCCESS");
            Assert.Equal("—（AI送信なし）", RunMetricsFormatter.DescribeAiCredits(none.Snapshot));
        }

        await using (JobUsageTracker unobserved = new(logDirectory: temp.Path))
        {
            Guid id = unobserved.BeginAttempt(UsageOperation.Normal);
            unobserved.ReplaceAttempt(new(id, UsageOperation.Normal, 1, new(InputTokens: 10), UsageSource.FinalRpc, true, IsPartial: false));
            await unobserved.CompleteAsync("SUCCESS");
            Assert.Equal("—（未取得）", RunMetricsFormatter.DescribeAiCredits(unobserved.Snapshot));
            Assert.Contains("AIクレジット —（未取得）", unobserved.Snapshot.SummaryText, StringComparison.Ordinal);
            Assert.Contains("／AIクレジット —（未取得）", unobserved.Snapshot.LogText, StringComparison.Ordinal);
        }

        JobCostSnapshot complete = await CompleteAsync(temp.Path, partialSecond: false);
        Assert.Equal("12.3457", RunMetricsFormatter.DescribeAiCredits(complete));
        Assert.Contains("AIクレジット 12.3457（SDK報告値から換算・観測 2/2 試行・観測完了）", complete.SummaryText, StringComparison.Ordinal);
        Assert.Contains("AIクレジット（nano-AI units ÷ 1,000,000,000。SDK報告値からの換算で請求確定額ではありません） 12.3457（SDK報告値から換算・観測 2/2 試行・観測完了）",
            complete.DetailsText, StringComparison.Ordinal);

        JobCostSnapshot partial = await CompleteAsync(temp.Path, partialSecond: true);
        Assert.Equal("12.3457（一部取得）", RunMetricsFormatter.DescribeAiCredits(partial));
        Assert.Contains("AIクレジット 12.3457（SDK報告値から換算・観測 2/2 試行・部分取得）", partial.SummaryText, StringComparison.Ordinal);
        Assert.Contains("／AIクレジット 12.3457（SDK報告値から換算・部分取得）", partial.LogText, StringComparison.Ordinal);

        await using JobUsageTracker running = new(logDirectory: temp.Path);
        Guid attempt = running.BeginAttempt(UsageOperation.Reference);
        running.ReplaceAttempt(new(attempt, UsageOperation.Reference, 1, new(TotalNanoAiu: 1_000_000_000m), UsageSource.FinalRpc, true, IsPartial: false));
        Assert.False(running.Snapshot.IsFinished);
        Assert.Equal("1.0000（一部取得）", RunMetricsFormatter.DescribeAiCredits(running.Snapshot));
        Assert.Equal("1.0000（一部取得）", RunMetricsFormatter.DescribeAiCredits(
            complete with { Metrics = new(TotalNanoAiu: 1_000_000_000m), MetricObservations = complete.MetricObservations.Clear() }));

        foreach (JobCostSnapshot snapshot in new[] { complete, partial })
        {
            foreach (string text in new[] { snapshot.SummaryText, snapshot.DetailsText, snapshot.LogText })
            {
                Assert.DoesNotContain("課金単位未確認", text, StringComparison.Ordinal);
                foreach (string currency in new[] { "円", "USD", "ドル", "$", "¥" })
                {
                    Assert.DoesNotContain(currency, text, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public async Task Jsonl_records_raw_nano_ai_units_without_a_credit_field()
    {
        using TemporaryDirectory temp = new();
        JobCostSnapshot complete = await CompleteAsync(temp.Path, partialSecond: false);
        string path = Assert.IsType<string>(complete.LogPath);
        string[] lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        Assert.NotEmpty(lines);
        foreach (string line in lines)
        {
            using JsonDocument entry = JsonDocument.Parse(line);
            AssertNoCreditProperty(entry.RootElement);
            Assert.DoesNotContain("クレジット", line, StringComparison.Ordinal);
        }

        using JsonDocument final = JsonDocument.Parse(lines[^1]);
        Assert.Equal(12_345_678_901m, final.RootElement.GetProperty("Metrics").GetProperty("TotalNanoAiu").GetDecimal());
        Assert.Equal(1, final.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal("SdkReportedNanoAiuAndPremiumRequests_NoCreditOrCurrencyConversion_v1",
            final.RootElement.GetProperty("UnitPolicy").GetString());
    }

    private static void AssertNoCreditProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                Assert.DoesNotContain("Credit", property.Name, StringComparison.OrdinalIgnoreCase);
                AssertNoCreditProperty(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray()) { AssertNoCreditProperty(item); }
        }
    }

    [AvaloniaFact]
    public async Task Results_heading_shows_duration_and_credits_for_the_loaded_run_only()
    {
        using TemporaryDirectory temp = new();
        ExecutionRunContext baseContext = await new ResultsDurableFixture().RunAsync();
        JobCostSnapshot cost = (await CompleteAsync(temp.Path, partialSecond: false)) with
        {
            StartedAtUtc = Start,
            EndedAtUtc = Start.AddMilliseconds(129_800),
        };
        RecordingOutputBoundary output = new();
        using ResultsOutputViewModel viewModel = new(output);
        Assert.Equal(string.Empty, viewModel.RunMetricsText);
        Assert.Equal(string.Empty, viewModel.RunMetricsToolTip);
        using Host host = new(viewModel);
        TextBlock metrics = ById<TextBlock>(host.View, "ResultsRunMetrics");
        Assert.False(metrics.IsEffectivelyVisible);
        Assert.True(string.IsNullOrEmpty(metrics.Text));

        viewModel.Load(WithCost(baseContext, cost));
        Render();

        const string expected = "総実行時間 00:02:09 · AIクレジット 12.3457";
        Assert.Equal(expected, viewModel.RunMetricsText);
        Assert.Equal(expected, metrics.Text);
        Assert.True(metrics.IsEffectivelyVisible);
        Assert.Equal(expected + Environment.NewLine + Note, ToolTip.GetTip(metrics));
        Assert.Equal(expected + Environment.NewLine + Note, AutomationProperties.GetName(metrics));
        AssertNotTrimmed(metrics);

        viewModel.Load(WithCost(baseContext, cost with
        {
            EndedAtUtc = Start.AddHours(100),
            Metrics = cost.Metrics with { TotalNanoAiu = 49_999m },
        }));
        Render();
        Assert.Equal("総実行時間 100:00:00 · AIクレジット <0.0001", metrics.Text);

        JobCostSnapshot partial = (await CompleteAsync(temp.Path, partialSecond: true)) with
        {
            StartedAtUtc = Start,
            EndedAtUtc = Start.AddHours(100),
        };
        viewModel.Load(WithCost(baseContext, partial with
        {
            Metrics = partial.Metrics with { TotalNanoAiu = 1_234_567_890_000_000m },
        }));
        Render();
        Assert.Equal("総実行時間 100:00:00 · AIクレジット 1,234,567.8900（一部取得）", metrics.Text);
        AssertNotTrimmed(metrics);

        viewModel.Load(baseContext);
        Render();
        Assert.Equal("総実行時間 —（未計測） · AIクレジット —（コスト記録なし）", metrics.Text);
        Assert.Equal(0, output.ExportCount);
    }

    private static ExecutionRunContext WithCost(ExecutionRunContext context, JobCostSnapshot cost) =>
        new(context.Summary, context.InputPath, context.ModelId, context.RuntimeIdentity, cost,
            context.ReasoningEffort, context.ContextTier);

    private static async Task<JobCostSnapshot> CompleteAsync(string directory, bool partialSecond)
    {
        await using JobUsageTracker tracker = new(logDirectory: directory);
        Guid first = tracker.BeginAttempt(UsageOperation.Normal);
        Guid second = tracker.BeginAttempt(UsageOperation.Special);
        tracker.ReplaceAttempt(new(first, UsageOperation.Normal, 1,
            new(InputTokens: 100, OutputTokens: 10, TotalNanoAiu: 12_000_000_000m), UsageSource.FinalRpc, true, IsPartial: false));
        tracker.ReplaceAttempt(new(second, UsageOperation.Special, 1,
            new(InputTokens: 5, OutputTokens: 1, TotalNanoAiu: 345_678_901m),
            partialSecond ? UsageSource.Events : UsageSource.FinalRpc, true, IsPartial: partialSecond));
        await tracker.CompleteAsync("SUCCESS");
        JobCostSnapshot snapshot = tracker.Snapshot;
        Assert.True(snapshot.IsFinished);
        Assert.NotNull(snapshot.StartedAtUtc);
        Assert.NotNull(snapshot.EndedAtUtc);
        return snapshot;
    }

    private static void AssertNotTrimmed(TextBlock text)
    {
        Assert.NotNull(text.TextLayout);
        Assert.DoesNotContain(text.TextLayout.TextLines, line => line.HasCollapsed);
        Assert.True(text.Bounds.Width > 0d);
    }

    private static T ById<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    private static void Render()
    {
        for (int pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Host : IDisposable
    {
        public Host(ResultsOutputViewModel viewModel)
        {
            View = new ResultsOutputView(viewModel);
            // The results content area inside the 1024x720 shell.
            Window = new Window { Width = 950, Height = 450, Content = View };
            Window.Show();
            Render();
        }

        public ResultsOutputView View { get; }

        public Window Window { get; }

        public void Dispose() => Window.Close();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "StudyReportEvaluator-run-metrics-tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path)) { Directory.Delete(Path, recursive: true); }
        }
    }
}
