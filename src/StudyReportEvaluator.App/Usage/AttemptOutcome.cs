namespace StudyReportEvaluator.App.Usage;

/// <summary>Evaluation outcome, independent of numeric usage observation availability.</summary>
public enum UsageAttemptOutcome
{
    Unknown,
    Succeeded,
    SchemaInvalid,
    NetworkFailed,
    TimedOut,
    RateLimited,
    QuotaExhausted,
    AuthRequired,
    Cancelled,
    CleanupFailed,
    Fatal,
}