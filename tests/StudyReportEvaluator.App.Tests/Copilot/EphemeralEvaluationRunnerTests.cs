using System.Text.Json;
using GitHub.Copilot;
using Microsoft.Extensions.AI;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

// Requirements: FR-022 (AC-023)
public sealed class EphemeralEvaluationRunnerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("long-context")]
    public async Task Selected_effort_and_context_tier_are_carried_into_every_normal_retry(string? tier)
    {
        FakeTransportFactory factory = new(static (_, _, _) => Task.CompletedTask);
        EphemeralEvaluationRunner runner = new(factory,
            new EphemeralEvaluationRunnerOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), contextTier: tier));
        var result = await runner.EvaluateAsync(CreatePayload(), "model-test", "high", TestContext.Current.CancellationToken);
        Assert.Equal(2, result.AttemptCount);
        Assert.All(factory.Transports, transport =>
        {
            SessionConfig config = Assert.Single(transport.SessionConfigs);
            Assert.Equal("high", config.ReasoningEffort);
            Assert.Equal(ModelOptionPolicy.ToSdkContextTier(tier), config.ContextTier);
            Assert.Equal([EvaluationSchemaFactory.ToolName], config.AvailableTools);
        });
    }

    [Fact]
    public async Task Valid_tool_submission_is_returned_after_dispose_and_explicit_delete()
    {
        FakeTransportFactory factory = new(
            static (session, options, token) => session.InvokeValidToolAsync(options, token));
        EphemeralEvaluationRunner runner = CreateRunner(factory);

        EphemeralEvaluationResult result = await runner.EvaluateAsync(
            CreatePayload(),
            "model-test",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("SUCCESS", result.StatusCode);
        Assert.Equal(1, result.AttemptCount);
        Assert.Equal("E1", Assert.IsType<QuantificationResult>(result.AcceptedResult).EvaluatorId);
        Assert.False(result.TokenUsage.IsAvailable);

        FakeTransport transport = Assert.Single(factory.Transports);
        SessionConfig config = Assert.Single(transport.SessionConfigs);
        string sessionId = Assert.IsType<string>(config.SessionId);
        Assert.StartsWith("a03-", sessionId, StringComparison.Ordinal);
        Assert.Equal("model-test", config.Model);
        Assert.Single(config.Tools!);
        Assert.Equal([EvaluationSchemaFactory.ToolName], config.AvailableTools);
        Assert.Equal(
            ["start", "auth", "create", "send", "session-dispose", "delete", "stop", "transport-dispose"],
            transport.Operations);
        Assert.Equal([sessionId], transport.DeletedSessionIds);
    }

    [Fact]
    public async Task Normal_assistant_body_is_ignored_and_schema_retry_uses_a_fresh_session()
    {
        const string normalBody = "PRIVATE NORMAL BODY WITH A FAKE SCORE 10";
        FakeTransportFactory factory = new(
            static (_, _, _) => Task.CompletedTask,
            normalAssistantBody: normalBody);
        EphemeralEvaluationRunner runner = CreateRunner(factory);

        EphemeralEvaluationResult result = await runner.EvaluateAsync(
            CreatePayload(),
            "model-test",
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.AiOutputInvalid, result.Status);
        Assert.Equal("AI_OUTPUT_INVALID", result.StatusCode);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(2, factory.Transports.Count);
        string[] sessionIds = factory.Transports
            .Select(transport => Assert.Single(transport.SessionConfigs).SessionId!)
            .ToArray();
        Assert.Equal(2, sessionIds.Distinct(StringComparer.Ordinal).Count());
        Assert.All(factory.Transports, transport =>
        {
            Assert.Equal(normalBody, transport.NormalAssistantBody);
            Assert.Single(Assert.Single(transport.SessionConfigs).Tools!);
            Assert.Equal(
                ["start", "auth", "create", "send", "session-dispose", "delete", "stop", "transport-dispose"],
                transport.Operations);
        });
        Assert.DoesNotContain(normalBody, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Numeric_usage_is_aggregated_across_schema_retry_without_content()
    {
        int invocation = 0;
        FakeTransportFactory factory = new(
            async (session, options, token) =>
            {
                if (Interlocked.Increment(ref invocation) == 2)
                {
                    await session.InvokeValidToolAsync(options, token);
                }
            },
            tokenUsage: new EvaluationTokenUsage(
                true,
                inputTokens: 100,
                outputTokens: 20,
                reasoningTokens: 5,
                cacheReadTokens: 7,
                cacheWriteTokens: 3));
        EphemeralEvaluationRunner runner = CreateRunner(factory);

        EphemeralEvaluationResult result = await runner.EvaluateAsync(
            CreatePayload(),
            "model-test",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.AttemptCount);
        Assert.True(result.TokenUsage.IsAvailable);
        Assert.Equal(200, result.TokenUsage.InputTokens);
        Assert.Equal(40, result.TokenUsage.OutputTokens);
        Assert.Equal(10, result.TokenUsage.ReasoningTokens);
        Assert.Equal(14, result.TokenUsage.CacheReadTokens);
        Assert.Equal(6, result.TokenUsage.CacheWriteTokens);
        Assert.DoesNotContain("PRIVATE PROMPT CANARY", result.TokenUsage.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_tool_calls_invalidate_each_attempt_and_never_expose_the_first_payload()
    {
        FakeTransportFactory factory = new(
            static async (session, options, token) =>
            {
                await session.InvokeValidToolAsync(options, token);
                await session.InvokeValidToolAsync(options, token);
            });
        EphemeralEvaluationRunner runner = CreateRunner(factory);

        EphemeralEvaluationResult result = await runner.EvaluateAsync(
            CreatePayload(),
            "model-test",
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.AiOutputInvalid, result.Status);
        Assert.Equal(2, result.AttemptCount);
        Assert.Null(result.AcceptedResult);
        Assert.Equal(2, factory.Transports.Count);
        Assert.All(factory.Transports, transport =>
            Assert.Equal(2, Assert.Single(transport.Sessions).ToolInvocationCount));
    }

    [Fact]
    public async Task Cancellation_before_evaluation_does_not_create_a_transport_or_session()
    {
        FakeTransportFactory factory = new(
            static (session, options, token) => session.InvokeValidToolAsync(options, token));
        EphemeralEvaluationRunner runner = CreateRunner(factory);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        EphemeralEvaluationResult result = await runner.EvaluateAsync(
            CreatePayload(),
            "model-test",
            cancellation.Token);

        Assert.Equal(EphemeralEvaluationStatus.Cancelled, result.Status);
        Assert.Equal(0, result.AttemptCount);
        Assert.Empty(factory.Transports);
    }

    [Fact]
    public async Task Cancellation_during_send_aborts_disposes_deletes_and_starts_no_new_session()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeTransportFactory factory = new(
            async (_, _, token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
        EphemeralEvaluationRunner runner = CreateRunner(factory);
        using CancellationTokenSource cancellation = new();

        Task<EphemeralEvaluationResult> evaluation = runner.EvaluateAsync(
            CreatePayload(),
            "model-test",
            cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        EphemeralEvaluationResult result = await evaluation.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.Cancelled, result.Status);
        Assert.Equal(1, result.AttemptCount);
        FakeTransport transport = Assert.Single(factory.Transports);
        Assert.Equal(
            ["start", "auth", "create", "send", "abort", "session-dispose", "delete", "stop", "transport-dispose"],
            transport.Operations);
        Assert.Single(transport.SessionConfigs);
    }

    [Fact]
    public async Task Cancellation_after_a_complete_tool_result_preserves_the_completed_payload()
    {
        using CancellationTokenSource cancellation = new();
        FakeTransportFactory factory = new(
            async (session, options, token) =>
            {
                await session.InvokeValidToolAsync(options, token);
                cancellation.Cancel();
            });
        EphemeralEvaluationRunner runner = CreateRunner(factory);

        EphemeralEvaluationResult result = await runner.EvaluateAsync(
            CreatePayload(),
            "model-test",
            cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.AttemptCount);
        Assert.NotNull(result.AcceptedResult);
        Assert.DoesNotContain("abort", Assert.Single(factory.Transports).Operations);
    }

    [Fact]
    public async Task Unauthenticated_runtime_returns_auth_required_without_creating_a_session()
    {
        FakeTransportFactory factory = new(
            static (session, options, token) => session.InvokeValidToolAsync(options, token))
        {
            IsAuthenticated = false,
        };
        EphemeralEvaluationRunner runner = CreateRunner(factory);

        EphemeralEvaluationResult result = await runner.EvaluateAsync(
            CreatePayload(),
            "model-test",
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.AuthRequired, result.Status);
        Assert.Equal("AUTH_REQUIRED", result.StatusCode);
        FakeTransport transport = Assert.Single(factory.Transports);
        Assert.Empty(transport.SessionConfigs);
        Assert.Equal(["start", "auth", "stop", "transport-dispose"], transport.Operations);
    }

    [Fact]
    public void Defaults_are_finite_120_seconds_and_runner_remains_a_single_evaluator_surface()
    {
        EphemeralEvaluationRunnerOptions options = new();

        Assert.Equal(TimeSpan.FromSeconds(120), options.AttemptTimeout);
        Assert.Equal(8, options.MaxConcurrency);
        Assert.InRange(options.CleanupTimeout, TimeSpan.FromMilliseconds(1), TimeSpan.FromMinutes(1));
        Assert.DoesNotContain(
            typeof(EphemeralEvaluationRunner).GetMethods(),
            method => method.Name.Contains("Batch", StringComparison.Ordinal)
                || method.Name.Contains("Schedule", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("claude-sonnet-5", "low")]
    [InlineData("gpt-5.5", "low")]
    [InlineData("auto", null)]
    [InlineData("claude-haiku-4.5", null)]
    [InlineData("listless", null)]
    [InlineData("high-only", "high")]
    [InlineData("not-listed", null)]
    public void Reasoning_effort_prefers_low_and_falls_back_to_nearest_supported_value(string modelId, string? expected)
    {
        static ModelInfo Model(string id, bool supports, params string[]? efforts) => new()
        {
            Id = id,
            Capabilities = new ModelCapabilities { Supports = new ModelSupports { ReasoningEffort = supports } },
            SupportedReasoningEfforts = efforts,
        };
        ModelInfo[] models =
        [
            Model("claude-sonnet-5", true, "low", "medium", "high"),
            Model("gpt-5.5", true, "low", "medium", "high", "xhigh"),
            Model("auto", false, null),
            Model("claude-haiku-4.5", false, null),
            Model("listless", true, null),
            Model("high-only", true, "high"),
        ];

        Assert.Equal(expected, SdkEphemeralCopilotTransport.ResolveReasoningEffort(models, modelId));
        Assert.Equal("medium", SdkEphemeralCopilotTransport.ResolveReasoningEffort(models, "claude-sonnet-5", "medium"));
    }

    [Theory]
    [InlineData("Session error: Execution failed: Error: error sending request for url (https://api.enterprise.githubcopilot.com/models): client error (Connect): operation timed out [ETIMEDOUT]", true)]
    [InlineData("Session error: Execution failed: HTTP status client error (400 Bad Request) for url (https://example.invalid/)", false)]
    [InlineData("Session error: session error", false)]
    public void Cli_transport_session_errors_are_network_failures(string message, bool expected)
    {
        Assert.Equal(expected, SdkEphemeralCopilotSession.IsTransportSessionError(new InvalidOperationException(message)));
        Assert.False(SdkEphemeralCopilotSession.IsTransportSessionError(new FormatException(message)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void Concurrency_one_through_eight_is_a_configuration_contract(int concurrency)
    {
        EphemeralEvaluationRunnerOptions options = new(maxConcurrency: concurrency);

        Assert.Equal(concurrency, options.MaxConcurrency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void Concurrency_outside_one_through_sixteen_is_rejected(int concurrency)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EphemeralEvaluationRunnerOptions(maxConcurrency: concurrency));
    }

    private static EphemeralEvaluationRunner CreateRunner(FakeTransportFactory factory) =>
        new(
            factory,
            new EphemeralEvaluationRunnerOptions(
                attemptTimeout: TimeSpan.FromSeconds(1),
                cleanupTimeout: TimeSpan.FromSeconds(1)));

    private static SafeEvaluationPayload CreatePayload() => new(
        "Q1",
        "E1",
        "PRIVATE PROMPT CANARY",
        new EvaluationSourceCell(
            EvaluationSourceKind.PrimaryAnswer,
            "G",
            "primary evidence is here"),
        [],
        [new ExpectedCriterion("C1", "Criterion", new ScoreRange(0m, 10m))]);

    private sealed class FakeTransportFactory(
        Func<FakeSession, MessageOptions, CancellationToken, Task> send,
        string normalAssistantBody = "",
        EvaluationTokenUsage? tokenUsage = null) : IEphemeralCopilotTransportFactory
    {
        public bool IsAuthenticated { get; init; } = true;

        public List<FakeTransport> Transports { get; } = [];

        public IEphemeralCopilotTransport Create()
        {
            FakeTransport transport = new(
                send,
                normalAssistantBody,
                IsAuthenticated,
                tokenUsage ?? EvaluationTokenUsage.Unavailable);
            Transports.Add(transport);
            return transport;
        }
    }

    private sealed class FakeTransport(
        Func<FakeSession, MessageOptions, CancellationToken, Task> send,
        string normalAssistantBody,
        bool isAuthenticated,
        EvaluationTokenUsage tokenUsage) : IEphemeralCopilotTransport
    {
        public List<string> Operations { get; } = [];

        public List<SessionConfig> SessionConfigs { get; } = [];

        public List<FakeSession> Sessions { get; } = [];

        public List<string> DeletedSessionIds { get; } = [];

        public string NormalAssistantBody { get; } = normalAssistantBody;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Record("start", cancellationToken);
            return Task.CompletedTask;
        }

        public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken)
        {
            Record("auth", cancellationToken);
            return Task.FromResult(isAuthenticated);
        }

        public Task<IEphemeralCopilotSession> CreateSessionAsync(
            SessionConfig config,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(config);
            Record("create", cancellationToken);
            SessionConfigs.Add(config);
            FakeSession session = new(config, send, Operations, tokenUsage);
            Sessions.Add(session);
            return Task.FromResult<IEphemeralCopilotSession>(session);
        }

        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            Record("delete", cancellationToken);
            DeletedSessionIds.Add(sessionId);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Record("stop", cancellationToken);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Operations.Add("transport-dispose");
            return ValueTask.CompletedTask;
        }

        private void Record(string operation, CancellationToken cancellationToken)
        {
            Operations.Add(operation);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class FakeSession(
        SessionConfig config,
        Func<FakeSession, MessageOptions, CancellationToken, Task> send,
        ICollection<string> operations,
        EvaluationTokenUsage tokenUsage) : IEphemeralCopilotSession
    {
        public string SessionId { get; } = config.SessionId
            ?? throw new InvalidOperationException("A test session ID is required.");

        public int ToolInvocationCount { get; private set; }

        public async Task SendAndWaitAsync(
            MessageOptions options,
            CancellationToken cancellationToken)
        {
            operations.Add("send");
            cancellationToken.ThrowIfCancellationRequested();
            await send(this, options, cancellationToken);
        }

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            operations.Add("abort");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<EvaluationTokenUsage> GetUsageAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(tokenUsage);
        }

        public ValueTask DisposeAsync()
        {
            operations.Add("session-dispose");
            return ValueTask.CompletedTask;
        }

        public async Task InvokeValidToolAsync(
            MessageOptions options,
            CancellationToken cancellationToken)
        {
            Assert.Equal("PRIVATE PROMPT CANARY", options.Prompt);
            Assert.Empty(options.Attachments!);
            AIFunction tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(config.Tools!));
            ToolInvocation invocation = new()
            {
                SessionId = SessionId,
                ToolCallId = Guid.NewGuid().ToString("N"),
                ToolName = EvaluationSchemaFactory.ToolName,
                Arguments = JsonDocument.Parse(
                    """
                    {
                      "EvaluatorId": "E1",
                      "Criteria": [
                        {
                          "CriterionId": "C1",
                          "RawScore": 4,
                          "Reason": "PRIVATE REASON CANARY",
                          "Evidence": "primary evidence",
                          "EvidenceSource": "PRIMARY_ANSWER",
                          "EvidenceSourceColumnId": "G"
                        }
                      ]
                    }
                    """).RootElement.Clone(),
            };
            AIFunctionArguments arguments = new()
            {
                Context = new Dictionary<object, object?>
                {
                    [typeof(ToolInvocation)] = invocation,
                },
            };

            ToolInvocationCount++;
            _ = await tool.InvokeAsync(arguments, cancellationToken);
        }
    }
}