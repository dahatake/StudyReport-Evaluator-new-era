using System.Collections.Immutable;
using System.Text.Json.Serialization;
using StudyReportEvaluator.App.Usage;

namespace StudyReportEvaluator.App.Logging;

internal enum JobCostEvent { JobStarted, AttemptStarted, UsageUpdated, JobFinished, AttemptFinished }
internal enum JobCostCompletion { Running, Completed, Cancelled, TimedOut, Failed, Disposed, Unknown }

// Deliberately closed DTO: no presentation strings, SDK objects, paths or exceptions.
internal sealed record JobCostLogEntry(
    int SchemaVersion,
    Guid JobId,
    long Revision,
    DateTimeOffset Timestamp,
    JobCostEvent Event,
    JobCostCompletion Completion,
    AttemptUsageSnapshot? Attempt,
    UsageMetrics Metrics,
    int AttemptCount,
    int[] ObservedAttemptCounts,
    bool IsPartial,
    bool HasInvalidValues,
    long DroppedEntries = 0)
{
    // Fixed policy identifiers, never caller/SDK text. Older schema-1 records can
    // omit these additive metadata fields; numeric metrics keep their original units.
    public string AggregationScope => "CurrentInvocation";
    public string UnitPolicy => "SdkReportedNanoAiuAndPremiumRequests_NoCreditOrCurrencyConversion_v1";
    public JobUsageContext? Context { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? ObservedAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; init; }
    public ImmutableDictionary<UsageMetric, MetricObservation>? MetricObservations { get; init; }
    public ImmutableDictionary<UsageObservationStatus, int>? AttemptStatuses { get; init; }
    public ImmutableArray<OperationUsageSnapshot> OperationBreakdown { get; init; } = [];
    public int? AttemptNumber { get; init; }
    public Guid? OperationId { get; init; }
    public UsageAttemptOutcome? AttemptOutcome { get; init; }
    public int ModelCostMismatchCount { get; init; }
}

[JsonSerializable(typeof(JobCostLogEntry))]
internal partial class JobCostJsonContext : JsonSerializerContext;