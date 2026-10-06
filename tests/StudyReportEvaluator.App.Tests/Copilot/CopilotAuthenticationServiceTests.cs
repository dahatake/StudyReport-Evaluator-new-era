using System.Reflection;
using StudyReportEvaluator.App.Copilot;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

// Requirements: FR-038 (AC-039)
public sealed class CopilotAuthenticationServiceTests
{
    [Fact]
    public void Status_mapping_is_closed()
    {
        Assert.Equal(
            [
                CopilotAuthenticationStatus.Available,
                CopilotAuthenticationStatus.AuthRequired,
                CopilotAuthenticationStatus.CliUnavailable,
                CopilotAuthenticationStatus.RuntimeFailed,
                CopilotAuthenticationStatus.Cancelled,
            ],
            Enum.GetValues<CopilotAuthenticationStatus>());
    }

    [Fact]
    public async Task Authenticated_runtime_returns_safe_distinct_model_capacities_and_identity()
    {
        FakeAuthenticationRuntime runtime = new()
        {
            AuthenticationState = CopilotRuntimeAuthenticationState.Authenticated,
            Models = [Model("model-b", 64_000, 128_000), Model("model-a", 32_000, 64_000), Model("model-b", 1, 1)],
        };
        CopilotRuntimeIdentity identity = CreateIdentity();
        FakeAuthenticationRuntimeFactory factory = new(
            CopilotAuthenticationRuntimeCreation.Created(runtime, identity));
        CopilotAuthenticationService service = new(factory, TimeSpan.FromSeconds(5));

        CopilotAuthenticationResult result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotAuthenticationStatus.Available, result.Status);
        Assert.Equal(["model-b", "model-a"], result.AvailableModelIds);
        Assert.Equal(64_000, result.AvailableModels[0].EffectivePromptTokenLimit);
        Assert.Equal(128_000, result.AvailableModels[0].MaximumContextWindowTokens);
        Assert.Same(identity, result.Identity);
        Assert.Equal(["start", "ping", "auth", "models", "stop", "dispose"], runtime.Operations);
        Assert.True(factory.ReceivedCancelableToken);
        Assert.All(runtime.OperationTokens, token => Assert.True(token.CanBeCanceled));
    }

    [Fact]
    public async Task Existing_login_missing_maps_to_auth_required_without_listing_models()
    {
        FakeAuthenticationRuntime runtime = new()
        {
            AuthenticationState = CopilotRuntimeAuthenticationState.Unauthenticated,
        };
        FakeAuthenticationRuntimeFactory factory = new(
            CopilotAuthenticationRuntimeCreation.Created(runtime, CreateIdentity()));
        CopilotAuthenticationService service = new(factory, TimeSpan.FromSeconds(5));

        CopilotAuthenticationResult result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotAuthenticationStatus.AuthRequired, result.Status);
        Assert.Empty(result.AvailableModelIds);
        Assert.Equal(["start", "ping", "auth", "stop", "dispose"], runtime.Operations);
    }

    [Fact]
    public async Task Missing_cli_maps_without_creating_or_calling_a_runtime()
    {
        FakeAuthenticationRuntimeFactory factory = new(
            CopilotAuthenticationRuntimeCreation.Failed(CopilotClientCreationStatus.CliUnavailable));
        CopilotAuthenticationService service = new(factory, TimeSpan.FromSeconds(5));

        CopilotAuthenticationResult result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotAuthenticationStatus.CliUnavailable, result.Status);
        Assert.Empty(result.AvailableModelIds);
        Assert.Null(result.Identity);
        Assert.Equal(1, factory.CallCount);
    }

    [Theory]
    [InlineData(FailurePoint.Start)]
    [InlineData(FailurePoint.Ping)]
    [InlineData(FailurePoint.Authentication)]
    [InlineData(FailurePoint.Models)]
    public async Task Runtime_failures_map_closed_without_raw_exception_content(FailurePoint failurePoint)
    {
        const string SensitiveException = "ghp_secret student-content C:\\private\\copilot.exe";
        FakeAuthenticationRuntime runtime = new()
        {
            AuthenticationState = CopilotRuntimeAuthenticationState.Authenticated,
            FailurePoint = failurePoint,
            FailureMessage = SensitiveException,
        };
        FakeAuthenticationRuntimeFactory factory = new(
            CopilotAuthenticationRuntimeCreation.Created(runtime, CreateIdentity()));
        CopilotAuthenticationService service = new(factory, TimeSpan.FromSeconds(5));

        CopilotAuthenticationResult result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotAuthenticationStatus.RuntimeFailed, result.Status);
        Assert.Empty(result.AvailableModelIds);
        Assert.DoesNotContain(SensitiveException, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\private", result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            typeof(CopilotAuthenticationResult).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => typeof(Exception).IsAssignableFrom(property.PropertyType));
        Assert.Contains("stop", runtime.Operations);
        Assert.Contains("dispose", runtime.Operations);
    }

    [Fact]
    public async Task Caller_cancellation_is_forwarded_and_maps_to_cancelled()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeAuthenticationRuntime runtime = new()
        {
            StartOverride = async cancellationToken =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
        };
        FakeAuthenticationRuntimeFactory factory = new(
            CopilotAuthenticationRuntimeCreation.Created(runtime, CreateIdentity()));
        CopilotAuthenticationService service = new(factory, TimeSpan.FromSeconds(5));
        using CancellationTokenSource cancellation = new();

        Task<CopilotAuthenticationResult> check = service.CheckAsync(cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        CopilotAuthenticationResult result = await check;

        Assert.Equal(CopilotAuthenticationStatus.Cancelled, result.Status);
        Assert.Empty(result.AvailableModelIds);
        Assert.True(runtime.OperationTokens[0].CanBeCanceled);
        Assert.Contains("stop", runtime.Operations);
        Assert.Contains("dispose", runtime.Operations);
    }

    [Fact]
    public async Task Internal_timeout_maps_to_runtime_failed_when_runtime_ignores_its_token()
    {
        TaskCompletionSource neverCompletes = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeAuthenticationRuntime runtime = new()
        {
            StartOverride = _ => neverCompletes.Task,
        };
        FakeAuthenticationRuntimeFactory factory = new(
            CopilotAuthenticationRuntimeCreation.Created(runtime, CreateIdentity()));
        CopilotAuthenticationService service = new(factory, TimeSpan.FromMilliseconds(50));

        CopilotAuthenticationResult result = await service
            .CheckAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(CopilotAuthenticationStatus.RuntimeFailed, result.Status);
        Assert.Contains("stop", runtime.Operations);
        Assert.Contains("dispose", runtime.Operations);
    }

    [Fact]
    public async Task Cleanup_failure_maps_an_otherwise_available_runtime_to_runtime_failed()
    {
        FakeAuthenticationRuntime runtime = new()
        {
            AuthenticationState = CopilotRuntimeAuthenticationState.Authenticated,
            FailurePoint = FailurePoint.Stop,
        };
        FakeAuthenticationRuntimeFactory factory = new(
            CopilotAuthenticationRuntimeCreation.Created(runtime, CreateIdentity()));
        CopilotAuthenticationService service = new(factory, TimeSpan.FromSeconds(5));

        CopilotAuthenticationResult result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotAuthenticationStatus.RuntimeFailed, result.Status);
        Assert.Empty(result.AvailableModelIds);
        Assert.Contains("dispose", runtime.Operations);
    }

    [Fact]
    public async Task Client_factory_failure_maps_to_runtime_failed_without_exception_details()
    {
        FakeAuthenticationRuntimeFactory factory = new(
            new InvalidOperationException("token path and response content"));
        CopilotAuthenticationService service = new(factory, TimeSpan.FromSeconds(5));

        CopilotAuthenticationResult result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotAuthenticationStatus.RuntimeFailed, result.Status);
        Assert.Equal("RuntimeFailed; models=0; runtime=none", result.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Authentication_check_rejects_non_finite_or_non_positive_timeout(int timeoutMilliseconds)
    {
        TimeSpan timeout = timeoutMilliseconds == -1
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.Zero;
        FakeAuthenticationRuntimeFactory factory = new(
            CopilotAuthenticationRuntimeCreation.Failed(CopilotClientCreationStatus.CliUnavailable));

        Assert.Throws<ArgumentOutOfRangeException>(() => new CopilotAuthenticationService(factory, timeout));
    }

    [Fact]
    public void Default_authentication_timeout_is_positive_and_finite()
    {
        Assert.True(CopilotAuthenticationService.DefaultCheckTimeout > TimeSpan.Zero);
        Assert.NotEqual(Timeout.InfiniteTimeSpan, CopilotAuthenticationService.DefaultCheckTimeout);
    }

    [Fact]
    public void Model_capacity_requires_a_safe_identity_and_never_invents_missing_limits()
    {
        Assert.Throws<ArgumentException>(() => new CopilotModelAvailability(" invalid ", 1, 1));
        Assert.Throws<ArgumentException>(() => new CopilotModelAvailability("bad\nmodel", 1, 1));

        CopilotModelAvailability unknown = new("model-unknown", null, 0);

        Assert.Null(unknown.MaximumPromptTokens);
        Assert.Null(unknown.MaximumContextWindowTokens);
        Assert.Null(unknown.EffectivePromptTokenLimit);
        Assert.Contains("<redacted>", unknown.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Router_auto_shape_from_the_bundled_cli_normalizes_to_unknown_limits()
    {
        // 実 CLI 1.0.79 の list models 応答は auto だけ max_prompt_tokens=null / max_context_window_tokens=0。
        CopilotModelAvailability auto = new("auto", null, 0);

        Assert.Equal("auto", auto.Id);
        Assert.Null(auto.MaximumPromptTokens);
        Assert.Null(auto.MaximumContextWindowTokens);
        Assert.Null(auto.EffectivePromptTokenLimit);
    }

    private static CopilotRuntimeIdentity CreateIdentity() =>
        new(
            Path.GetFullPath(typeof(CopilotAuthenticationServiceTests).Assembly.Location),
            "1.2.3",
            new string('A', 64),
            "1.0.11+test");

    private static CopilotModelAvailability Model(
        string id,
        int maximumPromptTokens = 64_000,
        int maximumContextWindowTokens = 128_000) =>
        new(id, maximumPromptTokens, maximumContextWindowTokens);

    public enum FailurePoint
    {
        None,
        Start,
        Ping,
        Authentication,
        Models,
        Stop,
    }

    private sealed class FakeAuthenticationRuntime : ICopilotAuthenticationRuntime
    {
        public CopilotRuntimeAuthenticationState AuthenticationState { get; init; } =
            CopilotRuntimeAuthenticationState.Authenticated;

        public IReadOnlyList<CopilotModelAvailability> Models { get; init; } = [Model("model-default")];

        public FailurePoint FailurePoint { get; init; }

        public string FailureMessage { get; init; } = "runtime failed";

        public Func<CancellationToken, Task>? StartOverride { get; init; }

        public CopilotRuntimeState State { get; private set; } = CopilotRuntimeState.Created;

        public List<string> Operations { get; } = [];

        public List<CancellationToken> OperationTokens { get; } = [];

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Record("start", cancellationToken);
            if (StartOverride is not null)
            {
                await StartOverride(cancellationToken);
                State = CopilotRuntimeState.Ready;
                return;
            }

            ThrowIf(FailurePoint.Start);
            State = CopilotRuntimeState.Ready;
        }

        public Task PingAsync(CancellationToken cancellationToken)
        {
            Record("ping", cancellationToken);
            ThrowIf(FailurePoint.Ping);
            return Task.CompletedTask;
        }

        public Task<CopilotRuntimeAuthenticationState> GetAuthenticationStatusAsync(
            CancellationToken cancellationToken)
        {
            Record("auth", cancellationToken);
            ThrowIf(FailurePoint.Authentication);
            return Task.FromResult(AuthenticationState);
        }

        public Task<IReadOnlyList<CopilotModelAvailability>> ListModelsAsync(
            CancellationToken cancellationToken)
        {
            Record("models", cancellationToken);
            ThrowIf(FailurePoint.Models);
            return Task.FromResult(Models);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Record("stop", cancellationToken);
            ThrowIf(FailurePoint.Stop);
            State = CopilotRuntimeState.Stopped;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Operations.Add("dispose");
            State = CopilotRuntimeState.Disposed;
            return ValueTask.CompletedTask;
        }

        private void Record(string operation, CancellationToken cancellationToken)
        {
            Operations.Add(operation);
            OperationTokens.Add(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void ThrowIf(FailurePoint point)
        {
            if (FailurePoint == point)
            {
                State = CopilotRuntimeState.Faulted;
                throw new InvalidOperationException(FailureMessage);
            }
        }
    }

    private sealed class FakeAuthenticationRuntimeFactory : ICopilotAuthenticationRuntimeFactory
    {
        private readonly CopilotAuthenticationRuntimeCreation? _creation;
        private readonly Exception? _exception;

        public FakeAuthenticationRuntimeFactory(CopilotAuthenticationRuntimeCreation creation)
        {
            _creation = creation;
        }

        public FakeAuthenticationRuntimeFactory(Exception exception)
        {
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public bool ReceivedCancelableToken { get; private set; }

        public ValueTask<CopilotAuthenticationRuntimeCreation> CreateAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            ReceivedCancelableToken = cancellationToken.CanBeCanceled;
            cancellationToken.ThrowIfCancellationRequested();
            if (_exception is not null)
            {
                throw _exception;
            }

            return ValueTask.FromResult(_creation!);
        }
    }
}
