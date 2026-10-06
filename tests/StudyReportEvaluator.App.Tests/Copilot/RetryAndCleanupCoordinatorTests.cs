using System.Collections.Immutable;
using System.Net.Http;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Logging;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

// Requirements: FR-022 (AC-023)
public sealed class RetryAndCleanupCoordinatorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(1);

    [Fact]
    public void Failure_and_result_status_sets_are_closed()
    {
        Assert.Equal(
            [
                EvaluationAttemptFailureKind.SchemaInvalid,
                EvaluationAttemptFailureKind.Network,
                EvaluationAttemptFailureKind.Timeout,
                EvaluationAttemptFailureKind.RateLimited,
                EvaluationAttemptFailureKind.QuotaExhausted,
                EvaluationAttemptFailureKind.Authentication,
                EvaluationAttemptFailureKind.Cancelled,
                EvaluationAttemptFailureKind.Fatal,
                EvaluationAttemptFailureKind.Cleanup,
            ],
            Enum.GetValues<EvaluationAttemptFailureKind>());
        Assert.Equal(
            [
                EphemeralEvaluationStatus.Succeeded,
                EphemeralEvaluationStatus.AiOutputInvalid,
                EphemeralEvaluationStatus.AiTimeout,
                EphemeralEvaluationStatus.NetworkFailed,
                EphemeralEvaluationStatus.RateLimited,
                EphemeralEvaluationStatus.QuotaExhausted,
                EphemeralEvaluationStatus.AuthRequired,
                EphemeralEvaluationStatus.Cancelled,
                EphemeralEvaluationStatus.CleanupFailed,
                EphemeralEvaluationStatus.Fatal,
            ],
            Enum.GetValues<EphemeralEvaluationStatus>());
    }

    [Fact]
    public async Task Schema_invalid_retries_once_in_a_new_attempt_then_returns_exact_code()
    {
        List<FakeAttempt> attempts = [];
        RetryAndCleanupCoordinator coordinator = new();

        EphemeralEvaluationResult result = await coordinator.ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                static _ => Task.FromException<QuantificationResult>(new EvaluationSchemaException())),
            TestTimeout,
            TestTimeout,
            maxConcurrency: 1,
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.AiOutputInvalid, result.Status);
        Assert.Equal("AI_OUTPUT_INVALID", result.StatusCode);
        Assert.Equal(RetryAndCleanupCoordinator.MaximumSchemaAttempts, result.AttemptCount);
        Assert.Null(result.AcceptedResult);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(2, attempts.Select(attempt => attempt.SessionId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(attempts, attempt => Assert.Equal(["execute", "dispose", "delete"], attempt.Operations));
    }

    [Theory]
    [InlineData(FailureScenario.Network, EphemeralEvaluationStatus.NetworkFailed, "NETWORK_FAILED")]
    [InlineData(FailureScenario.Timeout, EphemeralEvaluationStatus.AiTimeout, "AI_TIMEOUT")]
    public async Task Transient_network_and_timeout_failures_retry_twice(
        FailureScenario scenario,
        EphemeralEvaluationStatus expectedStatus,
        string expectedCode)
    {
        List<FakeAttempt> attempts = [];
        RetryAndCleanupCoordinator coordinator = new();

        EphemeralEvaluationResult result = await coordinator.ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                _ => Task.FromException<QuantificationResult>(CreateException(scenario))),
            TestTimeout,
            TestTimeout,
            maxConcurrency: 1,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedCode, result.StatusCode);
        Assert.Equal(RetryAndCleanupCoordinator.MaximumTransientAttempts, result.AttemptCount);
        Assert.Equal(3, attempts.Count);
        Assert.All(attempts, attempt => Assert.Equal(["execute", "abort", "dispose", "delete"], attempt.Operations));
    }

    [Fact]
    public async Task Rate_limit_retries_with_delay_and_observer_then_returns_distinct_status()
    {
        List<FakeAttempt> attempts = [];
        RecordingObserver observer = new();
        ZeroRetryDelayProvider delays = new();
        RetryAndCleanupCoordinator coordinator = new();

        EphemeralEvaluationResult result = await coordinator.ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                _ => Task.FromException<QuantificationResult>(new EvaluationRateLimitException(TimeSpan.FromSeconds(5)))),
            TestTimeout,
            TestTimeout,
            maxConcurrency: 4,
            TestContext.Current.CancellationToken,
            observer,
            delays);

        Assert.Equal(EphemeralEvaluationStatus.RateLimited, result.Status);
        Assert.Equal("RATE_LIMITED", result.StatusCode);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal([1, 2], delays.RetryNumbers);
        Assert.Equal(3, observer.RateLimitedCount);
        Assert.All(attempts, attempt => Assert.Equal(["execute", "abort", "dispose", "delete"], attempt.Operations));
    }

    [Fact]
    public async Task Quota_exhausted_is_not_retried_and_returns_distinct_status()
    {
        List<FakeAttempt> attempts = [];
        RetryAndCleanupCoordinator coordinator = new();

        EphemeralEvaluationResult result = await coordinator.ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                _ => Task.FromException<QuantificationResult>(new EvaluationQuotaExhaustedException())),
            TestTimeout,
            TestTimeout,
            maxConcurrency: 4,
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.QuotaExhausted, result.Status);
        Assert.Equal("QUOTA_EXHAUSTED", result.StatusCode);
        Assert.Equal(1, result.AttemptCount);
        Assert.Single(attempts);
    }

    [Theory]
    [InlineData(StandardFailure.Network, EphemeralEvaluationStatus.NetworkFailed)]
    [InlineData(StandardFailure.Timeout, EphemeralEvaluationStatus.AiTimeout)]
    public async Task Known_standard_transport_exceptions_use_the_same_closed_retry_classes(
        StandardFailure failure,
        EphemeralEvaluationStatus expectedStatus)
    {
        List<FakeAttempt> attempts = [];
        RetryAndCleanupCoordinator coordinator = new();

        EphemeralEvaluationResult result = await coordinator.ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                _ => Task.FromException<QuantificationResult>(failure switch
                {
                    StandardFailure.Network => new HttpRequestException("PRIVATE NETWORK BODY"),
                    StandardFailure.Timeout => new TimeoutException("PRIVATE TIMEOUT BODY"),
                    _ => new InvalidOperationException("PRIVATE UNKNOWN BODY"),
                })),
            TestTimeout,
            TestTimeout,
            maxConcurrency: 1,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(3, result.AttemptCount);
        Assert.DoesNotContain("PRIVATE", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mixed_failure_categories_honor_each_retry_class_without_exceeding_three_attempts()
    {
        List<FakeAttempt> attempts = [];
        RetryAndCleanupCoordinator coordinator = new();

        EphemeralEvaluationResult result = await coordinator.ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                _ => Task.FromException<QuantificationResult>(attemptNumber switch
                {
                    1 => new EvaluationNetworkException(),
                    _ => new EvaluationSchemaException(),
                })),
            TestTimeout,
            TestTimeout,
            maxConcurrency: 1,
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.AiOutputInvalid, result.Status);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal(3, attempts.Count);
        Assert.Equal(3, attempts.Select(attempt => attempt.SessionId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Authentication_failure_is_not_retried()
    {
        List<FakeAttempt> attempts = [];
        RetryAndCleanupCoordinator coordinator = new();

        EphemeralEvaluationResult result = await coordinator.ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                static _ => Task.FromException<QuantificationResult>(new EvaluationAuthenticationException())),
            TestTimeout,
            TestTimeout,
            maxConcurrency: 1,
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.AuthRequired, result.Status);
        Assert.Equal("AUTH_REQUIRED", result.StatusCode);
        Assert.Equal(1, result.AttemptCount);
        Assert.Single(attempts);
        Assert.Equal(["execute", "dispose", "delete"], attempts[0].Operations);
    }

    [Fact]
    public async Task Unknown_exception_is_fatal_and_never_retried()
    {
        List<FakeAttempt> attempts = [];
        RetryAndCleanupCoordinator coordinator = new();

        EphemeralEvaluationResult result = await coordinator.ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                static _ => Task.FromException<QuantificationResult>(
                    new InvalidOperationException("PRIVATE RESPONSE TOKEN PATH"))),
            TestTimeout,
            TestTimeout,
            maxConcurrency: 1,
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.Fatal, result.Status);
        Assert.Equal("AI_RUNTIME_FAILED", result.StatusCode);
        Assert.Equal(1, result.AttemptCount);
        Assert.Single(attempts);
        Assert.DoesNotContain("PRIVATE", result.ToString(), StringComparison.Ordinal);
        Assert.Equal(["execute", "abort", "dispose", "delete"], attempts[0].Operations);
    }

    [Fact]
    public async Task App_owned_timeout_terminates_even_when_attempt_ignores_its_token()
    {
        List<FakeAttempt> attempts = [];
        RetryAndCleanupCoordinator coordinator = new();
        TaskCompletionSource<QuantificationResult> never = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EphemeralEvaluationResult result = await coordinator.ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                _ => never.Task),
            TimeSpan.FromMilliseconds(20),
            TestTimeout,
            maxConcurrency: 1,
            TestContext.Current.CancellationToken).WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.AiTimeout, result.Status);
        Assert.Equal("AI_TIMEOUT", result.StatusCode);
        Assert.Equal(3, attempts.Count);
        Assert.All(attempts, attempt => Assert.Equal(["execute", "abort", "dispose", "delete"], attempt.Operations));
    }

    [Fact]
    public async Task Cancellation_before_first_attempt_creates_and_dispatches_nothing()
    {
        int factoryCalls = 0;
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        RetryAndCleanupCoordinator coordinator = new();

        EphemeralEvaluationResult result = await coordinator.ExecuteAsync(
            _ =>
            {
                factoryCalls++;
                throw new InvalidOperationException("must not be called");
            },
            TestTimeout,
            TestTimeout,
            maxConcurrency: 1,
            cancellation.Token);

        Assert.Equal(EphemeralEvaluationStatus.Cancelled, result.Status);
        Assert.Equal("CANCELLED", result.StatusCode);
        Assert.Equal(0, result.AttemptCount);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task Cancellation_during_attempt_aborts_cleans_and_never_starts_a_retry()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<FakeAttempt> attempts = [];
        using CancellationTokenSource cancellation = new();
        RetryAndCleanupCoordinator coordinator = new();

        Task<EphemeralEvaluationResult> execution = coordinator.ExecuteAsync(
            attemptNumber => AddAttempt(
                attempts,
                attemptNumber,
                async token =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return ValidResult();
                }),
            TestTimeout,
            TestTimeout,
            maxConcurrency: 1,
            cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        EphemeralEvaluationResult result = await execution.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.Cancelled, result.Status);
        Assert.Equal(1, result.AttemptCount);
        Assert.Single(attempts);
        Assert.Equal(["execute", "abort", "dispose", "delete"], attempts[0].Operations);
    }

    [Fact]
    public async Task Cleanup_failure_overrides_an_accepted_payload_and_attempts_later_cleanup_steps()
    {
        List<FakeAttempt> attempts = [];
        RetryAndCleanupCoordinator coordinator = new();

        EphemeralEvaluationResult result = await coordinator.ExecuteAsync(
            attemptNumber =>
            {
                FakeAttempt attempt = AddAttempt(
                    attempts,
                    attemptNumber,
                    static _ => Task.FromResult(ValidResult()));
                attempt.FailurePoint = CleanupFailurePoint.Dispose;
                return attempt;
            },
            TestTimeout,
            TestTimeout,
            maxConcurrency: 1,
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.CleanupFailed, result.Status);
        Assert.Equal("CLEANUP_FAILED", result.StatusCode);
        Assert.False(result.IsSuccess);
        Assert.Null(result.AcceptedResult);
        FakeAttempt onlyAttempt = Assert.Single(attempts);
        Assert.Equal(["execute", "dispose", "delete"], onlyAttempt.Operations);
    }

    [Fact]
    public async Task Nonretryable_failure_logs_use_their_actual_single_attempt_limit()
    {
        CapturingSink sink = new();
        RetryAndCleanupCoordinator coordinator = new(new SafeLogger(sink));
        using CancellationTokenSource beforeCancellation = new();
        beforeCancellation.Cancel();
        _ = await coordinator.ExecuteAsync(
            _ => throw new InvalidOperationException("not reached"),
            TestTimeout,
            TestTimeout,
            1,
            beforeCancellation.Token);
        _ = await coordinator.ExecuteAsync(
            _ => throw new InvalidOperationException("factory failure"),
            TestTimeout,
            TestTimeout,
            1,
            TestContext.Current.CancellationToken);
        _ = await coordinator.ExecuteAsync(
            attemptNumber => new FakeAttempt(
                $"a03-{attemptNumber.ToString("x32", System.Globalization.CultureInfo.InvariantCulture)}",
                static _ => Task.FromResult(ValidResult()))
            {
                FailurePoint = CleanupFailurePoint.Dispose,
            },
            TestTimeout,
            TestTimeout,
            1,
            TestContext.Current.CancellationToken);
        using CancellationTokenSource duringCancellation = new();
        _ = await coordinator.ExecuteAsync(
            attemptNumber => new FakeAttempt(
                $"a03-{attemptNumber.ToString("x32", System.Globalization.CultureInfo.InvariantCulture)}",
                token =>
                {
                    duringCancellation.Cancel();
                    return Task.FromCanceled<QuantificationResult>(token);
                }),
            TestTimeout,
            TestTimeout,
            1,
            duringCancellation.Token);

        SafeLogEntry[] terminalEntries = sink.Entries
            .Where(entry => entry.EventCode is SafeLogEventCode.EvaluationCancelled
                or SafeLogEventCode.EvaluationCleanupFailed
                || (entry.EventCode == SafeLogEventCode.EvaluationAttemptFailed
                    && entry.Dimensions.FailureCategory == SafeLogFailureCategory.Fatal))
            .ToArray();
        Assert.Equal(4, terminalEntries.Length);
        Assert.All(terminalEntries, entry => Assert.Equal(1, entry.Dimensions.AttemptLimit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task Concurrency_outside_one_through_sixteen_is_rejected(int concurrency)
    {
        RetryAndCleanupCoordinator coordinator = new();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => coordinator.ExecuteAsync(
            _ => new FakeAttempt("a03-00000000000000000000000000000001", static _ => Task.FromResult(ValidResult())),
            TestTimeout,
            TestTimeout,
            concurrency,
            TestContext.Current.CancellationToken));
    }

    public enum FailureScenario
    {
        Network,
        Timeout,
    }

    public enum StandardFailure
    {
        Network,
        Timeout,
    }

    private static EvaluationAttemptException CreateException(FailureScenario scenario) =>
        scenario switch
        {
            FailureScenario.Network => new EvaluationNetworkException(),
            FailureScenario.Timeout => new EvaluationAttemptTimeoutException(),
            _ => new EvaluationFatalException(),
        };

    private static FakeAttempt AddAttempt(
        ICollection<FakeAttempt> attempts,
        int attemptNumber,
        Func<CancellationToken, Task<QuantificationResult>> execute)
    {
        FakeAttempt attempt = new(
            $"a03-{attemptNumber.ToString("x32", System.Globalization.CultureInfo.InvariantCulture)}",
            execute);
        attempts.Add(attempt);
        return attempt;
    }

    private static QuantificationResult ValidResult() => new()
    {
        EvaluatorId = "E1",
        Criteria = ImmutableArray.Create(new CriterionQuantificationResult
        {
            CriterionId = "C1",
            RawScore = 4m,
            Reason = "private reason",
            Evidence = "private evidence",
            EvidenceSource = EvidenceSourceKind.PrimaryAnswer,
            EvidenceSourceColumnId = "G",
        }),
    };

    private enum CleanupFailurePoint
    {
        None,
        Abort,
        Dispose,
        Delete,
    }

    private sealed class FakeAttempt(
        string sessionId,
        Func<CancellationToken, Task<QuantificationResult>> execute) : IEphemeralEvaluationAttempt
    {
        public CleanupFailurePoint FailurePoint { get; set; }

        public string SessionId { get; } = sessionId;

        public List<string> Operations { get; } = [];

        public Task<QuantificationResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            Operations.Add("execute");
            return execute(cancellationToken);
        }

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            Operations.Add("abort");
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIf(CleanupFailurePoint.Abort);
            return Task.CompletedTask;
        }

        public Task DisposeSessionAsync(CancellationToken cancellationToken)
        {
            Operations.Add("dispose");
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIf(CleanupFailurePoint.Dispose);
            return Task.CompletedTask;
        }

        public Task DeleteSessionAsync(CancellationToken cancellationToken)
        {
            Operations.Add("delete");
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIf(CleanupFailurePoint.Delete);
            return Task.CompletedTask;
        }

        private void ThrowIf(CleanupFailurePoint failurePoint)
        {
            if (FailurePoint == failurePoint)
            {
                throw new InvalidOperationException("PRIVATE CLEANUP FAILURE");
            }
        }
    }

    private sealed class CapturingSink : ISafeLogSink
    {
        public List<SafeLogEntry> Entries { get; } = [];

        public void Write(SafeLogEntry entry) => Entries.Add(entry);
    }

    private sealed class RecordingObserver : IEvaluationConcurrencyObserver
    {
        public int RateLimitedCount { get; private set; }

        public void RecordRateLimited(TimeSpan? retryAfter) => RateLimitedCount++;
    }

    private sealed class ZeroRetryDelayProvider : IRetryDelayProvider
    {
        public List<int> RetryNumbers { get; } = [];

        public TimeSpan GetRateLimitDelay(int retryNumber, TimeSpan? retryAfter)
        {
            RetryNumbers.Add(retryNumber);
            return TimeSpan.Zero;
        }
    }
}