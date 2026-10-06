using System.Globalization;

namespace StudyReportEvaluator.App.Usage;

/// <summary>Display-only total duration and AI credit conversion for a job cost record (§11.21).</summary>
public static class RunMetricsFormatter
{
    /// <summary>The SDK reports AI credit cost in nano-AI units (A-CR-01).</summary>
    public const decimal NanoAiuPerAiCredit = 1_000_000_000m;

    public const string NotMeasuredText = "—（未計測）";
    public const string NoCostRecordText = "—（コスト記録なし）";
    public const string NoAiSendText = "—（AI送信なし）";
    public const string NotObservedText = "—（未取得）";
    public const string PartialSuffix = "（一部取得）";

    public const string AiCreditNote =
        "GitHub Copilot SDKが報告したnano-AI unitsを1,000,000,000で割ったAIクレジットです。"
        + "今回のジョブで観測できた値だけで、請求確定額ではありません。";

    /// <summary>Formats elapsed time as HH:MM:SS, truncating sub-second precision.</summary>
    public static string FormatDuration(DateTimeOffset? startedAtUtc, DateTimeOffset? endedAtUtc)
    {
        if (startedAtUtc is not { } start || endedAtUtc is not { } end || end < start)
        {
            return NotMeasuredText;
        }

        long totalSeconds = (end - start).Ticks / TimeSpan.TicksPerSecond;
        long hours = totalSeconds / 3600;
        long minutes = totalSeconds % 3600 / 60;
        long seconds = totalSeconds % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00}");
    }

    /// <summary>Formats a non-negative nano-AI unit total as AI credits with 4 decimals.</summary>
    public static string FormatAiCredits(decimal nanoAiu)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nanoAiu);
        decimal credits = Math.Round(nanoAiu / NanoAiuPerAiCredit, 4, MidpointRounding.AwayFromZero);
        if (credits == 0m && nanoAiu > 0m)
        {
            return "<0.0001";
        }

        return credits.ToString("#,##0.0000", CultureInfo.InvariantCulture);
    }

    /// <summary>The value shown for the results heading (FR-CR-02).</summary>
    public static string DescribeAiCredits(JobCostSnapshot? cost)
    {
        if (cost is null) { return NoCostRecordText; }
        if (cost.AttemptCount <= 0) { return NoAiSendText; }
        if (cost.Metrics.TotalNanoAiu is not { } nanoAiu || nanoAiu < 0m) { return NotObservedText; }
        bool partial = !cost.IsFinished
            || !cost.MetricObservations.TryGetValue(UsageMetric.TotalNanoAiu, out MetricObservation? observation)
            || observation is null
            || observation.AttemptCount != cost.AttemptCount
            || observation.IsPartial;
        return FormatAiCredits(nanoAiu) + (partial ? PartialSuffix : string.Empty);
    }

    /// <summary>The results heading text: total duration and AI credits (FR-RT-01).</summary>
    public static string DescribeRun(JobCostSnapshot? cost) =>
        $"総実行時間 {FormatDuration(cost?.StartedAtUtc, cost?.EndedAtUtc)} · AIクレジット {DescribeAiCredits(cost)}";
}
