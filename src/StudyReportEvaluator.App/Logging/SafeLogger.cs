using System.Globalization;

namespace StudyReportEvaluator.App.Logging;

public enum SafeLogSeverity
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical,
}

public enum SafeLogEventCode
{
    EvaluationAttemptStarted,
    EvaluationAttemptSucceeded,
    EvaluationAttemptFailed,
    EvaluationRetryScheduled,
    EvaluationCancelled,
    EvaluationCleanupFailed,
}

public enum SafeLogFailureCategory
{
    None,
    SchemaInvalid,
    Network,
    Timeout,
    Authentication,
    Cancelled,
    Cleanup,
    Fatal,
}

public sealed class SafeLogIdentity
{
    private const string SessionPrefix = "a03-";
    private const int SessionSuffixLength = 32;

    private SafeLogIdentity(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreateSession(
        string? value,
        out SafeLogIdentity? identity)
    {
        identity = null;
        if (value is null
            || value.Length != SessionPrefix.Length + SessionSuffixLength
            || !value.StartsWith(SessionPrefix, StringComparison.Ordinal)
            || value.AsSpan(SessionPrefix.Length).ContainsAnyExcept(
                "0123456789abcdef"))
        {
            return false;
        }

        identity = new SafeLogIdentity(value);
        return true;
    }

    public override string ToString() => Value;
}

public sealed class SafeLogDimensions
{
    public SafeLogDimensions(
        int attemptNumber,
        int attemptLimit,
        int maxConcurrency,
        SafeLogFailureCategory failureCategory)
    {
        if (attemptNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        if (attemptLimit is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptLimit));
        }

        if (maxConcurrency is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        }

        if (!Enum.IsDefined(failureCategory))
        {
            throw new ArgumentOutOfRangeException(nameof(failureCategory));
        }

        AttemptNumber = attemptNumber;
        AttemptLimit = attemptLimit;
        MaxConcurrency = maxConcurrency;
        FailureCategory = failureCategory;
    }

    public int AttemptNumber { get; }

    public int AttemptLimit { get; }

    public int MaxConcurrency { get; }

    public SafeLogFailureCategory FailureCategory { get; }

    public override string ToString() =>
        $"attempt={AttemptNumber.ToString(CultureInfo.InvariantCulture)};limit={AttemptLimit.ToString(CultureInfo.InvariantCulture)};concurrency={MaxConcurrency.ToString(CultureInfo.InvariantCulture)};failure={FailureCategory}";
}

public sealed class SafeLogEntry
{
    internal SafeLogEntry(
        SafeLogSeverity severity,
        SafeLogEventCode eventCode,
        SafeLogDimensions dimensions,
        SafeLogIdentity? identity)
    {
        Severity = severity;
        EventCode = eventCode;
        Dimensions = dimensions;
        Identity = identity;
    }

    public SafeLogSeverity Severity { get; }

    public SafeLogEventCode EventCode { get; }

    public SafeLogDimensions Dimensions { get; }

    public SafeLogIdentity? Identity { get; }

    public override string ToString() =>
        $"severity={Severity};event={EventCode};{Dimensions};identity={Identity?.Value ?? "none"}";
}

public interface ISafeLogSink
{
    void Write(SafeLogEntry entry);
}

public sealed class SafeLogger
{
    private sealed class NullSafeLogSink : ISafeLogSink
    {
        public void Write(SafeLogEntry entry)
        {
        }
    }

    private readonly ISafeLogSink _sink;

    public SafeLogger(ISafeLogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    public static SafeLogger None { get; } = new(new NullSafeLogSink());

    public void Trace(
        SafeLogEventCode eventCode,
        SafeLogDimensions dimensions,
        SafeLogIdentity? identity = null) =>
        Log(SafeLogSeverity.Trace, eventCode, dimensions, identity);

    public void Debug(
        SafeLogEventCode eventCode,
        SafeLogDimensions dimensions,
        SafeLogIdentity? identity = null) =>
        Log(SafeLogSeverity.Debug, eventCode, dimensions, identity);

    public void Information(
        SafeLogEventCode eventCode,
        SafeLogDimensions dimensions,
        SafeLogIdentity? identity = null) =>
        Log(SafeLogSeverity.Information, eventCode, dimensions, identity);

    public void Warning(
        SafeLogEventCode eventCode,
        SafeLogDimensions dimensions,
        SafeLogIdentity? identity = null) =>
        Log(SafeLogSeverity.Warning, eventCode, dimensions, identity);

    public void Error(
        SafeLogEventCode eventCode,
        SafeLogDimensions dimensions,
        SafeLogIdentity? identity = null) =>
        Log(SafeLogSeverity.Error, eventCode, dimensions, identity);

    public void Critical(
        SafeLogEventCode eventCode,
        SafeLogDimensions dimensions,
        SafeLogIdentity? identity = null) =>
        Log(SafeLogSeverity.Critical, eventCode, dimensions, identity);

    public void Log(
        SafeLogSeverity severity,
        SafeLogEventCode eventCode,
        SafeLogDimensions dimensions,
        SafeLogIdentity? identity = null)
    {
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        if (!Enum.IsDefined(eventCode))
        {
            throw new ArgumentOutOfRangeException(nameof(eventCode));
        }

        ArgumentNullException.ThrowIfNull(dimensions);

        try
        {
            _sink.Write(new SafeLogEntry(severity, eventCode, dimensions, identity));
        }
        catch
        {
            // Logging is deliberately non-interfering. No sink exception is retained or re-emitted.
        }
    }
}