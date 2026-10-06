using GitHub.Copilot;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Usage;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

public sealed class CostAttemptLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Retries_share_generated_operation_id_and_report_actual_attempt_number_after_cleanup()
    {
        List<RecordingAttempt> attempts = [];
        RetryAndCleanupCoordinator coordinator = new();
        var result = await coordinator.ExecuteAuxiliaryAsync(
            number =>
            {
                RecordingAttempt attempt = new(() => number == 1
                    ? Task.FromException<string>(new EvaluationSchemaException())
                    : Task.FromResult("accepted"));
                attempts.Add(attempt);
                return attempt;
            }, Timeout, Timeout, 1, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(new[] { 1, 2 }, attempts.Select(attempt => attempt.AttemptNumber));
        Guid operationId = Assert.Single(attempts.Select(attempt => attempt.OperationId).Distinct());
        Assert.NotEqual(Guid.Empty, operationId);
        Assert.Equal(2, attempts.Select(attempt => attempt.SessionId).Distinct().Count());
        Assert.Equal(UsageAttemptOutcome.SchemaInvalid, Assert.Single(attempts[0].Outcomes));
        Assert.Equal(UsageAttemptOutcome.Succeeded, Assert.Single(attempts[1].Outcomes));
        Assert.All(attempts, attempt =>
            Assert.Equal(new[] { "context", "execute", "dispose", "delete", "outcome" }, attempt.Events));

        RecordingAttempt separate = new(() => Task.FromResult("accepted"));
        await coordinator.ExecuteAuxiliaryAsync(_ => separate,
            Timeout, Timeout, 1, TestContext.Current.CancellationToken);
        Assert.NotEqual(operationId, separate.OperationId);
        Assert.Equal(1, separate.AttemptNumber);
    }

    [Theory]
    [InlineData(UsageAttemptOutcome.SchemaInvalid, EphemeralEvaluationStatus.AiOutputInvalid, 2)]
    [InlineData(UsageAttemptOutcome.NetworkFailed, EphemeralEvaluationStatus.NetworkFailed, 3)]
    [InlineData(UsageAttemptOutcome.TimedOut, EphemeralEvaluationStatus.AiTimeout, 3)]
    [InlineData(UsageAttemptOutcome.AuthRequired, EphemeralEvaluationStatus.AuthRequired, 1)]
    [InlineData(UsageAttemptOutcome.Fatal, EphemeralEvaluationStatus.Fatal, 1)]
    public async Task Closed_outcomes_preserve_existing_retry_limits(
        UsageAttemptOutcome outcome, EphemeralEvaluationStatus status, int expectedAttempts)
    {
        List<RecordingAttempt> attempts = [];
        var result = await new RetryAndCleanupCoordinator().ExecuteAuxiliaryAsync(
            _ =>
            {
                RecordingAttempt attempt = new(() => Task.FromException<string>(outcome switch
                {
                    UsageAttemptOutcome.SchemaInvalid => new EvaluationSchemaException(),
                    UsageAttemptOutcome.NetworkFailed => new EvaluationNetworkException(),
                    UsageAttemptOutcome.TimedOut => new EvaluationAttemptTimeoutException(),
                    UsageAttemptOutcome.AuthRequired => new EvaluationAuthenticationException(),
                    _ => new EvaluationFatalException(),
                }));
                attempts.Add(attempt);
                return attempt;
            }, Timeout, Timeout, 1, TestContext.Current.CancellationToken);

        Assert.Equal(status, result.Status);
        Assert.Equal(expectedAttempts, result.AttemptCount);
        Assert.All(attempts, attempt =>
        {
            Assert.Equal(outcome, Assert.Single(attempt.Outcomes));
            Assert.Equal("outcome", attempt.Events[^1]);
            Assert.Equal("delete", attempt.Events[^2]);
        });
    }

    [Theory]
    [InlineData(false, false, UsageAttemptOutcome.Cancelled, EphemeralEvaluationStatus.Cancelled)]
    [InlineData(true, false, UsageAttemptOutcome.Succeeded, EphemeralEvaluationStatus.Succeeded)]
    [InlineData(false, true, UsageAttemptOutcome.CleanupFailed, EphemeralEvaluationStatus.CleanupFailed)]
    [InlineData(true, true, UsageAttemptOutcome.CleanupFailed, EphemeralEvaluationStatus.CleanupFailed)]
    public async Task Cancellation_and_cleanup_outcomes_match_final_result(
        bool accepted, bool cleanupFails, UsageAttemptOutcome outcome, EphemeralEvaluationStatus status)
    {
        using CancellationTokenSource cancellation = new();
        RecordingAttempt attempt = new(() =>
        {
            cancellation.Cancel();
            return accepted ? Task.FromResult("accepted") : Task.FromCanceled<string>(cancellation.Token);
        }) { CleanupFails = cleanupFails };

        var result = await new RetryAndCleanupCoordinator().ExecuteAuxiliaryAsync(
            _ => attempt, Timeout, Timeout, 1, cancellation.Token);

        Assert.Equal(status, result.Status);
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal(outcome, Assert.Single(attempt.Outcomes));
        Assert.Equal("delete", attempt.Events[^2]);
        Assert.Equal("outcome", attempt.Events[^1]);
        Assert.Equal(!accepted, attempt.Events.Contains("abort"));
    }

    [Fact]
    public async Task Disposal_failure_still_deletes_and_reports_cleanup_failure_without_retry()
    {
        RecordingAttempt attempt = new(() => Task.FromResult("accepted")) { DisposeFails = true };
        var result = await new RetryAndCleanupCoordinator().ExecuteAuxiliaryAsync(
            _ => attempt, Timeout, Timeout, 1, TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.CleanupFailed, result.Status);
        Assert.Equal(1, result.AttemptCount);
        Assert.Null(result.AcceptedResult);
        Assert.Equal(UsageAttemptOutcome.CleanupFailed, Assert.Single(attempt.Outcomes));
        Assert.Equal(new[] { "context", "execute", "dispose", "delete", "outcome" }, attempt.Events);
    }

    [Fact]
    public async Task Observation_exceptions_do_not_change_success_or_cleanup()
    {
        RecordingAttempt attempt = new(() => Task.FromResult("accepted")) { HooksThrow = true };
        var result = await new RetryAndCleanupCoordinator().ExecuteAuxiliaryAsync(
            _ => attempt, Timeout, Timeout, 1, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal(new[] { "context", "execute", "dispose", "delete", "outcome" }, attempt.Events);
    }

    [Fact]
    public async Task Pre_cancelled_operation_never_creates_or_reports_an_attempt()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        int created = 0;
        var result = await new RetryAndCleanupCoordinator().ExecuteAuxiliaryAsync(
            _ => { created++; return new RecordingAttempt(() => Task.FromResult("accepted")); },
            Timeout, Timeout, 1, cancellation.Token);

        Assert.Equal(EphemeralEvaluationStatus.Cancelled, result.Status);
        Assert.Equal(0, result.AttemptCount);
        Assert.Equal(0, created);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Normal_and_auxiliary_runners_forward_context_and_outcome_to_transport(bool auxiliary)
    {
        RecordingFactory factory = new();
        EphemeralEvaluationRunnerOptions options = new(Timeout, Timeout);
        if (auxiliary)
        {
            var result = await new ReferenceAnswerEvaluationRunner(factory, options).EvaluateAsync(
                new SafeReferenceAnswerPayload("Q1", "PRIVATE PROMPT CANARY"),
                TestContext.Current.CancellationToken);
            Assert.Equal(EphemeralEvaluationStatus.AiOutputInvalid, result.Status);
        }
        else
        {
            SafeEvaluationPayload payload = new("Q1", "E1", "PRIVATE PROMPT CANARY",
                new EvaluationSourceCell(EvaluationSourceKind.PrimaryAnswer, "G", "evidence"),
                [], [new ExpectedCriterion("C1", "Criterion", new ScoreRange(0m, 10m))]);
            var result = await new EphemeralEvaluationRunner(factory, options).EvaluateAsync(
                payload, "model-test", TestContext.Current.CancellationToken);
            Assert.Equal(EphemeralEvaluationStatus.AiOutputInvalid, result.Status);
        }

        Assert.Equal(new[] { 1, 2 }, factory.Transports.Select(transport => transport.AttemptNumber));
        Assert.NotEqual(Guid.Empty, Assert.Single(factory.Transports.Select(transport => transport.OperationId).Distinct()));
        Assert.All(factory.Transports, transport =>
        {
            Assert.Equal(UsageAttemptOutcome.SchemaInvalid, Assert.Single(transport.Outcomes));
            Assert.Equal(new[] { "context", "start", "auth", "create", "send", "session-dispose",
                "delete", "stop", "transport-dispose", "outcome" }, transport.Events);
        });
    }

    private sealed class RecordingAttempt(Func<Task<string>> execute) : IEphemeralEvaluationAttempt<string>
    {
        public string SessionId { get; } = $"a03-{Guid.NewGuid():N}";
        public int AttemptNumber { get; private set; }
        public Guid OperationId { get; private set; }
        public bool CleanupFails { get; init; }
        public bool DisposeFails { get; init; }
        public bool HooksThrow { get; init; }
        public List<string> Events { get; } = [];
        public List<UsageAttemptOutcome> Outcomes { get; } = [];

        public void SetUsageAttemptContext(int attemptNumber, Guid operationId)
        {
            Events.Add("context");
            AttemptNumber = attemptNumber;
            OperationId = operationId;
            if (HooksThrow) { throw new InvalidOperationException("PRIVATE OBSERVATION FAILURE"); }
        }

        public void CompleteUsageAttempt(UsageAttemptOutcome outcome)
        {
            Events.Add("outcome");
            Outcomes.Add(outcome);
            if (HooksThrow) { throw new InvalidOperationException("PRIVATE OBSERVATION FAILURE"); }
        }

        public Task<string> ExecuteAsync(CancellationToken cancellationToken) { Events.Add("execute"); return execute(); }
        public Task AbortAsync(CancellationToken cancellationToken) { Events.Add("abort"); return Task.CompletedTask; }
        public Task DisposeSessionAsync(CancellationToken cancellationToken)
        {
            Events.Add("dispose");
            return DisposeFails ? Task.FromException(new EvaluationCleanupException()) : Task.CompletedTask;
        }
        public Task DeleteSessionAsync(CancellationToken cancellationToken)
        {
            Events.Add("delete");
            return CleanupFails ? Task.FromException(new EvaluationCleanupException()) : Task.CompletedTask;
        }
    }

    private sealed class RecordingFactory : IEphemeralCopilotTransportFactory
    {
        public List<RecordingTransport> Transports { get; } = [];
        public IEphemeralCopilotTransport Create()
        {
            RecordingTransport transport = new();
            Transports.Add(transport);
            return transport;
        }
    }

    private sealed class RecordingTransport : IEphemeralCopilotTransport
    {
        public int AttemptNumber { get; private set; }
        public Guid OperationId { get; private set; }
        public List<string> Events { get; } = [];
        public List<UsageAttemptOutcome> Outcomes { get; } = [];
        public void SetUsageAttemptContext(int attemptNumber, Guid operationId)
        {
            Events.Add("context"); AttemptNumber = attemptNumber; OperationId = operationId;
        }
        public void CompleteUsageAttempt(UsageAttemptOutcome outcome) { Events.Add("outcome"); Outcomes.Add(outcome); }
        public Task StartAsync(CancellationToken cancellationToken) { Events.Add("start"); return Task.CompletedTask; }
        public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken) { Events.Add("auth"); return Task.FromResult(true); }
        public Task<IEphemeralCopilotSession> CreateSessionAsync(SessionConfig config, CancellationToken cancellationToken)
        {
            Events.Add("create");
            return Task.FromResult<IEphemeralCopilotSession>(new RecordingSession(config.SessionId!, Events));
        }
        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken) { Events.Add("delete"); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { Events.Add("stop"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Events.Add("transport-dispose"); return ValueTask.CompletedTask; }
    }

    private sealed class RecordingSession(string sessionId, List<string> events) : IEphemeralCopilotSession
    {
        public string SessionId => sessionId;
        public Task SendAndWaitAsync(MessageOptions options, CancellationToken cancellationToken) { events.Add("send"); return Task.CompletedTask; }
        public Task AbortAsync(CancellationToken cancellationToken) { events.Add("abort"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { events.Add("session-dispose"); return ValueTask.CompletedTask; }
    }
}