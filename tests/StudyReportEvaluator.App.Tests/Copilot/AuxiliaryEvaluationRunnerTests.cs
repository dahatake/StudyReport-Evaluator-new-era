using System.Text.Json;
using GitHub.Copilot;
using Microsoft.Extensions.AI;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

// Requirements: FR-018 (AC-019)
public sealed class AuxiliaryEvaluationRunnerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("long-context")]
    public async Task Selected_effort_and_context_tier_are_carried_into_reference_and_special_retries(string? tier)
    {
        EphemeralEvaluationRunnerOptions options = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), contextTier: tier);
        RecordingFactory reference = new(AuxiliaryEvaluationSchemaFactory.ReferenceToolName, ReferenceJson(), invokeTool: false);
        RecordingFactory special = new(AuxiliaryEvaluationSchemaFactory.SpecialToolName, SpecialJson(), invokeTool: false);
        var referenceResult = await new ReferenceAnswerEvaluationRunner(reference, options).EvaluateAsync(
            new SafeReferenceAnswerPayload("Q1", "synthetic prompt"), "model-test", "high", TestContext.Current.CancellationToken);
        var specialResult = await new SpecialEvaluationRunner(special, options).EvaluateAsync(
            AuxiliaryEvaluationSchemaFactoryTests.CreateSpecialPayload(), "model-test", "high", TestContext.Current.CancellationToken);
        Assert.Equal(2, referenceResult.AttemptCount);
        Assert.Equal(2, specialResult.AttemptCount);
        foreach (RecordingFactory factory in new[] { reference, special })
        {
            Assert.All(factory.Transports, transport =>
            {
                SessionConfig config = Assert.Single(transport.Configs);
                Assert.Equal("high", config.ReasoningEffort);
                Assert.Equal(ModelOptionPolicy.ToSdkContextTier(tier), config.ContextTier);
                Assert.Single(config.AvailableTools!);
            });
        }
    }

    [Fact]
    public async Task Reference_runner_uses_auto_one_tool_and_complete_cleanup()
    {
        RecordingFactory factory = new(AuxiliaryEvaluationSchemaFactory.ReferenceToolName, ReferenceJson());
        ReferenceAnswerEvaluationRunner runner = CreateReferenceRunner(factory);

        EphemeralEvaluationResult<ReferenceAnswerResult> result = await runner.EvaluateAsync(
            new SafeReferenceAnswerPayload("Q1", "PRIVATE REFERENCE PROMPT"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("reference answer", result.AcceptedResult!.Answer);
        RecordingTransport transport = Assert.Single(factory.Transports);
        SessionConfig config = Assert.Single(transport.Configs);
        Assert.Equal("auto", config.Model);
        Assert.StartsWith("ref-", config.SessionId, StringComparison.Ordinal);
        Assert.Equal([AuxiliaryEvaluationSchemaFactory.ReferenceToolName], config.AvailableTools);
        Assert.Equal(
            ["start", "auth", "create", "send", "session-dispose", "delete", "stop", "transport-dispose"],
            transport.Operations);
    }

    [Fact]
    public async Task Special_runner_uses_selected_model_and_returns_zero_to_one_result()
    {
        RecordingFactory factory = new(AuxiliaryEvaluationSchemaFactory.SpecialToolName, SpecialJson());
        SpecialEvaluationRunner runner = CreateSpecialRunner(factory);

        EphemeralEvaluationResult<SpecialQuantificationResult> result = await runner.EvaluateAsync(
            AuxiliaryEvaluationSchemaFactoryTests.CreateSpecialPayload(),
            "model-test",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.5m, result.AcceptedResult!.Score);
        Assert.Equal("model-test", Assert.Single(Assert.Single(factory.Transports).Configs).Model);
    }

    [Fact]
    public async Task Missing_tool_result_retries_once_with_fresh_reference_sessions()
    {
        RecordingFactory factory = new(
            AuxiliaryEvaluationSchemaFactory.ReferenceToolName,
            ReferenceJson(),
            invokeTool: false);
        ReferenceAnswerEvaluationRunner runner = CreateReferenceRunner(factory);

        EphemeralEvaluationResult<ReferenceAnswerResult> result = await runner.EvaluateAsync(
            new SafeReferenceAnswerPayload("Q1", "PRIVATE REFERENCE PROMPT"),
            TestContext.Current.CancellationToken);

        Assert.Equal(EphemeralEvaluationStatus.AiOutputInvalid, result.Status);
        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(2, factory.Transports.Count);
        Assert.Equal(
            2,
            factory.Transports.Select(item => Assert.Single(item.Configs).SessionId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(factory.Transports, transport => Assert.Contains("delete", transport.Operations));
    }

    [Fact]
    public async Task Cancellation_before_auxiliary_run_creates_no_transport()
    {
        RecordingFactory factory = new(AuxiliaryEvaluationSchemaFactory.ReferenceToolName, ReferenceJson());
        ReferenceAnswerEvaluationRunner runner = CreateReferenceRunner(factory);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        EphemeralEvaluationResult<ReferenceAnswerResult> result = await runner.EvaluateAsync(
            new SafeReferenceAnswerPayload("Q1", "PRIVATE REFERENCE PROMPT"),
            cancellation.Token);

        Assert.Equal(EphemeralEvaluationStatus.Cancelled, result.Status);
        Assert.Equal(0, result.AttemptCount);
        Assert.Empty(factory.Transports);
    }

    private static ReferenceAnswerEvaluationRunner CreateReferenceRunner(RecordingFactory factory) => new(
        factory,
        new EphemeralEvaluationRunnerOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    private static SpecialEvaluationRunner CreateSpecialRunner(RecordingFactory factory) => new(
        factory,
        new EphemeralEvaluationRunnerOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    private static string ReferenceJson() =>
        """{"QuestionId":"Q1","Answer":"reference answer"}""";

    private static string SpecialJson() =>
        """
        {
          "SpecialEvaluationId":"S1",
          "Score":0.5,
          "Reason":"reason",
          "Evidence":"primary evidence",
          "EvidenceSource":"PRIMARY_ANSWER",
          "EvidenceSourceColumnId":"G"
        }
        """;

    private sealed class RecordingFactory(
        string expectedToolName,
        string resultJson,
        bool invokeTool = true) : IEphemeralCopilotTransportFactory
    {
        public List<RecordingTransport> Transports { get; } = [];

        public IEphemeralCopilotTransport Create()
        {
            RecordingTransport transport = new(expectedToolName, resultJson, invokeTool);
            Transports.Add(transport);
            return transport;
        }
    }

    private sealed class RecordingTransport(
        string expectedToolName,
        string resultJson,
        bool invokeTool) : IEphemeralCopilotTransport
    {
        public List<string> Operations { get; } = [];

        public List<SessionConfig> Configs { get; } = [];

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Record("start", cancellationToken);
            return Task.CompletedTask;
        }

        public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken)
        {
            Record("auth", cancellationToken);
            return Task.FromResult(true);
        }

        public Task<IEphemeralCopilotSession> CreateSessionAsync(SessionConfig config, CancellationToken cancellationToken)
        {
            Record("create", cancellationToken);
            Configs.Add(config);
            return Task.FromResult<IEphemeralCopilotSession>(
                new RecordingSession(config, Operations, expectedToolName, resultJson, invokeTool));
        }

        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            Record("delete", cancellationToken);
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

        private void Record(string value, CancellationToken cancellationToken)
        {
            Operations.Add(value);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class RecordingSession(
        SessionConfig config,
        ICollection<string> operations,
        string expectedToolName,
        string resultJson,
        bool invokeTool) : IEphemeralCopilotSession
    {
        public string SessionId { get; } = config.SessionId
            ?? throw new InvalidOperationException("A session ID is required.");

        public async Task SendAndWaitAsync(MessageOptions options, CancellationToken cancellationToken)
        {
            operations.Add("send");
            cancellationToken.ThrowIfCancellationRequested();
            Assert.NotNull(options.Prompt);
            if (!invokeTool)
            {
                return;
            }

            AIFunction tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(config.Tools ?? []));
            Assert.Equal(expectedToolName, tool.Name);
            ToolInvocation invocation = new()
            {
                SessionId = SessionId,
                ToolCallId = Guid.NewGuid().ToString("N"),
                ToolName = expectedToolName,
                Arguments = JsonDocument.Parse(resultJson).RootElement.Clone(),
            };
            AIFunctionArguments arguments = new()
            {
                Context = new Dictionary<object, object?> { [typeof(ToolInvocation)] = invocation },
            };
            _ = await tool.InvokeAsync(arguments, cancellationToken);
        }

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            operations.Add("abort");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            operations.Add("session-dispose");
            return ValueTask.CompletedTask;
        }
    }
}
