namespace StudyReportEvaluator.App.Usage;

internal static class UsageMath
{
    internal static bool HasDecrease(UsageMetrics previous, UsageMetrics current) =>
        current.InputTokens < previous.InputTokens || current.OutputTokens < previous.OutputTokens
        || current.ReasoningTokens < previous.ReasoningTokens || current.CacheReadTokens < previous.CacheReadTokens
        || current.CacheWriteTokens < previous.CacheWriteTokens || current.TotalNanoAiu < previous.TotalNanoAiu
        || current.PremiumRequests < previous.PremiumRequests;

    internal static UsageMetrics Sanitize(UsageMetrics value, out bool invalid)
    {
        invalid = value.InputTokens < 0 || value.OutputTokens < 0 || value.ReasoningTokens < 0
            || value.CacheReadTokens < 0 || value.CacheWriteTokens < 0
            || value.TotalNanoAiu < 0 || value.PremiumRequests < 0;
        return new UsageMetrics(
            Valid(value.InputTokens), Valid(value.OutputTokens), Valid(value.ReasoningTokens),
            Valid(value.CacheReadTokens), Valid(value.CacheWriteTokens),
            Valid(value.TotalNanoAiu), Valid(value.PremiumRequests));
    }

    internal static UsageMetrics Sum(IEnumerable<UsageMetrics> observations, out bool invalid, out int[] counts)
    {
        UsageMetrics[] values = observations.ToArray();
        bool overflow = false;
        int[] observed = new int[7];
        long? Tokens(Func<UsageMetrics, long?> select, int index)
        {
            long total = 0;
            bool failed = false;
            foreach (UsageMetrics value in values)
            {
                if (select(value) is not { } number) { continue; }
                observed[index]++;
                try { total = checked(total + number); }
                catch (OverflowException) { overflow = failed = true; }
            }

            return failed || observed[index] == 0 ? null : total;
        }

        decimal? Cost(Func<UsageMetrics, decimal?> select, int index)
        {
            decimal total = 0;
            bool failed = false;
            foreach (UsageMetrics value in values)
            {
                if (select(value) is not { } number) { continue; }
                observed[index]++;
                try { total = checked(total + number); }
                catch (OverflowException) { overflow = failed = true; }
            }

            return failed || observed[index] == 0 ? null : total;
        }

        var result = new UsageMetrics(
            Tokens(v => v.InputTokens, 0), Tokens(v => v.OutputTokens, 1),
            Tokens(v => v.ReasoningTokens, 2), Tokens(v => v.CacheReadTokens, 3),
            Tokens(v => v.CacheWriteTokens, 4), Cost(v => v.TotalNanoAiu, 5), Cost(v => v.PremiumRequests, 6));
        invalid = overflow;
        counts = observed;
        return result;
    }

    internal static UsageMetrics PreferFinal(UsageMetrics final, UsageMetrics events) => new(
        final.InputTokens ?? events.InputTokens,
        final.OutputTokens ?? events.OutputTokens,
        final.ReasoningTokens ?? events.ReasoningTokens,
        final.CacheReadTokens ?? events.CacheReadTokens,
        final.CacheWriteTokens ?? events.CacheWriteTokens,
        final.TotalNanoAiu ?? events.TotalNanoAiu,
        final.PremiumRequests ?? events.PremiumRequests);

    private static long? Valid(long? value) => value < 0 ? null : value;
    private static decimal? Valid(decimal? value) => value < 0 ? null : value;
}