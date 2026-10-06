using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using GitHub.Copilot;
using StudyReportEvaluator.App.Copilot;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

// Requirements: FR-037 (AC-038)
public sealed class BundledCopilotLoginServiceTests
{
    private const string SensitiveFailure = "synthetic-token device-code C:\\private\\copilot.exe";

    [Fact]
    public void Default_service_uses_the_existing_bundled_resolver_and_does_not_start_login()
    {
        using BundledCopilotLoginService service = new();
        FieldInfo resolver = typeof(BundledCopilotLoginService).GetField(
            "_pathResolver", BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo process = typeof(BundledCopilotLoginService).GetField(
            "_ownedProcess", BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.IsType<BundledCopilotCliPathResolver>(resolver.GetValue(service));
        Assert.Null(process.GetValue(service));
    }

    [Fact]
    public void Constructor_requires_both_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new BundledCopilotLoginService(null!));
        Assert.Throws<ArgumentNullException>(() => new BundledCopilotLoginService(new StubResolver(CliPath()), null!));
    }

    [Fact]
    public async Task Login_uses_only_the_explicit_path_fixed_arguments_and_unredirected_console()
    {
        string path = CliPath();
        StubResolver resolver = new(path);
        FakeLoginProcess process = new() { ExitOnStart = 0 };
        FakeProcessFactory factory = new(process);
        using BundledCopilotLoginService service = new(resolver, factory.Create);
        Assert.Equal(0, resolver.CallCount);
        Assert.Empty(factory.StartInfos);

        CopilotLoginResult result = await service.LoginAsync(TestContext.Current.CancellationToken);

        ProcessStartInfo info = Assert.Single(factory.StartInfos);
        Assert.Equal(Path.GetFullPath(path), info.FileName);
        Assert.Equal(["--no-auto-update", "--log-level", "none", "login", "--web-flow"], info.ArgumentList);
        Assert.Empty(info.Arguments);
        Assert.False(info.UseShellExecute);
        Assert.False(info.CreateNoWindow);
        Assert.False(info.RedirectStandardInput);
        Assert.False(info.RedirectStandardOutput);
        Assert.False(info.RedirectStandardError);
        Assert.Empty(info.Verb);
        Assert.Empty(info.WorkingDirectory);
        Assert.Equal(ProcessWindowStyle.Normal, info.WindowStyle);
        // Compare without including any ambient credential/environment values in assertions.
        IDictionary<string, string?> inherited = new ProcessStartInfo().Environment;
        Assert.True(inherited.Count == info.Environment.Count
            && inherited.All(pair => info.Environment.TryGetValue(pair.Key, out string? value)
                && string.Equals(pair.Value, value, StringComparison.Ordinal)));
        Assert.Equal(CopilotLoginStatus.Completed, result.Status);
        Assert.Equal(CopilotLoginErrorCategory.None, result.ErrorCategory);
        Assert.Equal("Completed; error=None", result.ToString());
        Assert.DoesNotContain("Authenticated", Enum.GetNames<CopilotLoginStatus>());
        Assert.Empty(process.KillTreeArguments);
        Assert.Equal(1, process.DisposeCount);
    }

    [Fact]
    public void Public_result_and_process_seam_cannot_return_login_output_or_credentials()
    {
        Assert.All(typeof(CopilotLoginResult).GetProperties(), property => Assert.True(property.PropertyType.IsEnum));
        Assert.Equal(["HasExited", "ExitCode"], typeof(ICopilotLoginProcess).GetProperties().Select(property => property.Name));
        Assert.All(
            typeof(BundledCopilotLoginService)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetParameters()),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
    }

    [Fact]
    public async Task Pre_cancelled_call_does_not_resolve_create_or_start()
    {
        StubResolver resolver = new(CliPath());
        FakeLoginProcess process = new();
        FakeProcessFactory factory = new(process);
        using BundledCopilotLoginService service = new(resolver, factory.Create);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        CopilotLoginResult result = await service.LoginAsync(cancellation.Token);

        Assert.Equal(CopilotLoginStatus.Cancelled, result.Status);
        Assert.Equal(0, resolver.CallCount);
        Assert.Empty(factory.StartInfos);
        Assert.Equal(0, process.StartCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("copilot.exe")]
    [InlineData("./runtimes/win-x64/native/copilot.exe")]
    public async Task Missing_or_relative_path_is_unavailable_without_any_fallback(string? path)
    {
        FakeProcessFactory factory = new(new FakeLoginProcess());
        using BundledCopilotLoginService service = new(new StubResolver(path), factory.Create);

        CopilotLoginResult result = await service.LoginAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotLoginStatus.CliUnavailable, result.Status);
        Assert.Equal(CopilotLoginErrorCategory.CliUnavailable, result.ErrorCategory);
        Assert.Empty(factory.StartInfos);
    }

    [Fact]
    public async Task Invalid_absolute_path_is_unavailable_and_is_not_passed_to_process_factory()
    {
        FakeProcessFactory factory = new(new FakeLoginProcess());
        using BundledCopilotLoginService service = new(new StubResolver(CliPath() + '\0'), factory.Create);

        CopilotLoginResult result = await service.LoginAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotLoginStatus.CliUnavailable, result.Status);
        Assert.Empty(factory.StartInfos);
    }

    [Fact]
    public async Task Resolver_failure_is_safe_and_a_retry_resolves_again()
    {
        StubResolver resolver = new(CliPath())
        {
            ResolveOverride = _ => throw new InvalidDataException(SensitiveFailure),
        };
        FakeProcessFactory factory = new(new FakeLoginProcess { ExitOnStart = 0 });
        using BundledCopilotLoginService service = new(resolver, factory.Create);

        CopilotLoginResult failure = await service.LoginAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotLoginStatus.CliUnavailable, failure.Status);
        Assert.Equal(CopilotLoginErrorCategory.CliValidationFailed, failure.ErrorCategory);
        AssertSafe(failure);
        Assert.Empty(factory.StartInfos);
        resolver.ResolveOverride = null;
        CopilotLoginResult retry = await service.LoginAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CopilotLoginStatus.Completed, retry.Status);
        Assert.Equal(2, resolver.CallCount);
        Assert.Single(factory.StartInfos);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_bundled_resolver_missing_manifest_or_wrong_hash_never_launches(bool wrongHash)
    {
        using TemporaryBundle bundle = new(wrongHash);
        FakeProcessFactory factory = new(new FakeLoginProcess());
        using BundledCopilotLoginService service = new(new BundledCopilotCliPathResolver(bundle.Directory), factory.Create);

        CopilotLoginResult result = await service.LoginAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotLoginStatus.CliUnavailable, result.Status);
        Assert.Equal(wrongHash ? CopilotLoginErrorCategory.CliValidationFailed : CopilotLoginErrorCategory.CliUnavailable,
            result.ErrorCategory);
        Assert.Empty(factory.StartInfos);
        Assert.DoesNotContain(bundle.Directory, result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Concurrent_calls_are_rejected_during_resolution_and_after_process_start()
    {
        TaskCompletionSource<string?> resolution = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StubResolver resolver = new(CliPath()) { ResolveOverride = _ => new(resolution.Task) };
        FakeLoginProcess process = new();
        FakeProcessFactory factory = new(process);
        using BundledCopilotLoginService service = new(resolver, factory.Create);

        Task<CopilotLoginResult> first = service.LoginAsync(TestContext.Current.CancellationToken);
        await resolver.Called.Task.WaitAsync(TestContext.Current.CancellationToken);
        CopilotLoginResult duringResolution = await service.LoginAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CopilotLoginStatus.AlreadyRunning, duringResolution.Status);
        Assert.Empty(factory.StartInfos);
        resolution.SetResult(CliPath());
        await process.Waiting.Task.WaitAsync(TestContext.Current.CancellationToken);
        CopilotLoginResult duringLogin = await service.LoginAsync(TestContext.Current.CancellationToken);
        Assert.Equal(CopilotLoginStatus.AlreadyRunning, duringLogin.Status);
        Assert.Equal(1, resolver.CallCount);
        Assert.Equal(1, process.StartCount);
        Assert.Single(factory.StartInfos);
        Assert.False(first.IsCompleted);

        process.Complete(0);
        Assert.Equal(CopilotLoginStatus.Completed, (await first).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Start_failure_releases_only_the_unstarted_component_and_allows_retry(bool throws)
    {
        FakeLoginProcess failed = new() { StartReturnsFalse = !throws, Failure = throws ? FailurePoint.Start : FailurePoint.None };
        FakeLoginProcess next = new() { ExitOnStart = 0 };
        FakeProcessFactory factory = new(failed, next);
        using BundledCopilotLoginService service = new(new StubResolver(CliPath()), factory.Create);

        CopilotLoginResult result = await service.LoginAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotLoginStatus.RuntimeFailed, result.Status);
        Assert.Equal(CopilotLoginErrorCategory.ProcessStartFailed, result.ErrorCategory);
        AssertSafe(result);
        Assert.Empty(failed.KillTreeArguments);
        Assert.Equal(1, failed.DisposeCount);
        Assert.Equal(CopilotLoginStatus.Completed, (await service.LoginAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, next.StartCount);
    }

    [Fact]
    public async Task Process_factory_exception_is_closed_without_exception_content()
    {
        using BundledCopilotLoginService service = new(new StubResolver(CliPath()),
            _ => throw new InvalidOperationException(SensitiveFailure));

        CopilotLoginResult result = await service.LoginAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotLoginStatus.RuntimeFailed, result.Status);
        Assert.Equal(CopilotLoginErrorCategory.ProcessStartFailed, result.ErrorCategory);
        AssertSafe(result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task Nonzero_natural_exit_is_failure_and_is_never_killed(int exitCode)
    {
        FakeLoginProcess process = new() { ExitOnStart = exitCode };
        using BundledCopilotLoginService service = new(new StubResolver(CliPath()), _ => process);

        CopilotLoginResult result = await service.LoginAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotLoginStatus.RuntimeFailed, result.Status);
        Assert.Equal(CopilotLoginErrorCategory.NonZeroExit, result.ErrorCategory);
        Assert.Empty(process.KillTreeArguments);
        Assert.Equal(1, process.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_stops_only_owned_login_and_allows_retry_even_when_wait_ignores_token(bool ignoreToken)
    {
        FakeLoginProcess browser = new();
        FakeLoginProcess process = new() { IgnoreWaitCancellation = ignoreToken, Child = browser };
        FakeLoginProcess retryProcess = new() { ExitOnStart = 0 };
        FakeProcessFactory factory = new(process, retryProcess);
        using BundledCopilotLoginService service = new(new StubResolver(CliPath()), factory.Create);
        using CancellationTokenSource cancellation = new();
        Task<CopilotLoginResult> login = service.LoginAsync(cancellation.Token);
        await process.Waiting.Task.WaitAsync(TestContext.Current.CancellationToken);

        cancellation.Cancel();
        CopilotLoginResult result = await login.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotLoginStatus.Cancelled, result.Status);
        Assert.Equal([false], process.KillTreeArguments);
        Assert.Equal([5_000], process.CleanupWaits);
        Assert.Equal(1, process.DisposeCount);
        Assert.Empty(browser.KillTreeArguments);
        Assert.Equal(0, browser.DisposeCount);
        Assert.True(process.WaitToken.CanBeCanceled);
        Assert.Equal(CopilotLoginStatus.Completed, (await service.LoginAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, retryProcess.StartCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_or_dispose_during_an_ignoring_resolver_prevents_late_start(bool dispose)
    {
        TaskCompletionSource<string?> resolution = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StubResolver resolver = new(CliPath()) { ResolveOverride = _ => new(resolution.Task) };
        FakeProcessFactory factory = new(new FakeLoginProcess());
        using BundledCopilotLoginService service = new(resolver, factory.Create);
        using CancellationTokenSource cancellation = new();
        Task<CopilotLoginResult> login = service.LoginAsync(cancellation.Token);
        await resolver.Called.Task.WaitAsync(TestContext.Current.CancellationToken);

        if (dispose)
        {
            service.Dispose();
        }
        else
        {
            cancellation.Cancel();
        }

        Assert.Equal(CopilotLoginStatus.Cancelled, (await login.WaitAsync(TestContext.Current.CancellationToken)).Status);
        resolution.SetResult(CliPath());
        Assert.Empty(factory.StartInfos);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_factory_or_start_does_not_lose_process_ownership(bool duringStart)
    {
        using CancellationTokenSource cancellation = new();
        FakeLoginProcess process = new() { OnStart = duringStart ? cancellation.Cancel : null };
        using BundledCopilotLoginService service = new(new StubResolver(CliPath()), _ =>
        {
            if (!duringStart)
            {
                cancellation.Cancel();
            }

            return process;
        });

        CopilotLoginResult result = await service.LoginAsync(cancellation.Token);

        Assert.Equal(CopilotLoginStatus.Cancelled, result.Status);
        Assert.Equal(duringStart ? 1 : 0, process.StartCount);
        Assert.Equal(duringStart ? new[] { false } : [], process.KillTreeArguments);
        Assert.Equal(1, process.DisposeCount);
    }

    [Fact]
    public async Task Dispose_stops_owned_process_synchronously_without_touching_another_service()
    {
        FakeLoginProcess owned = new() { IgnoreWaitCancellation = true };
        FakeLoginProcess other = new();
        using BundledCopilotLoginService service = new(new StubResolver(CliPath()), _ => owned);
        using BundledCopilotLoginService otherService = new(new StubResolver(CliPath()), _ => other);
        Task<CopilotLoginResult> login = service.LoginAsync(TestContext.Current.CancellationToken);
        Task<CopilotLoginResult> otherLogin = otherService.LoginAsync(TestContext.Current.CancellationToken);
        await owned.Waiting.Task.WaitAsync(TestContext.Current.CancellationToken);
        await other.Waiting.Task.WaitAsync(TestContext.Current.CancellationToken);

        service.Dispose();
        service.Dispose();

        Assert.True(owned.HasExited);
        Assert.Equal([false], owned.KillTreeArguments);
        Assert.Equal(1, owned.DisposeCount);
        Assert.Empty(other.KillTreeArguments);
        Assert.False(other.HasExited);
        Assert.Equal(CopilotLoginStatus.Cancelled, (await login).Status);
        Assert.Equal(CopilotLoginStatus.Disposed, (await service.LoginAsync(TestContext.Current.CancellationToken)).Status);
        other.Complete(0);
        Assert.Equal(CopilotLoginStatus.Completed, (await otherLogin).Status);
    }

    [Fact]
    public async Task Dispose_racing_with_start_waits_for_ownership_publication_and_stops_that_instance()
    {
        CancellationToken testCancellation = TestContext.Current.CancellationToken;
        TaskCompletionSource enteringStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource enteringDispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim releaseStart = new(false);
        FakeLoginProcess process = new()
        {
            OnStart = () =>
            {
                enteringStart.SetResult();
                releaseStart.Wait(testCancellation);
            },
        };
        using BundledCopilotLoginService service = new(new StubResolver(CliPath()), _ => process);
        Task<CopilotLoginResult> login = Task.Run(() => service.LoginAsync(testCancellation), testCancellation);
        Task? disposal = null;
        try
        {
            await enteringStart.Task.WaitAsync(testCancellation);
            disposal = Task.Run(() =>
            {
                enteringDispose.SetResult();
                service.Dispose();
            }, testCancellation);
            await enteringDispose.Task.WaitAsync(testCancellation);
            Assert.False(disposal.IsCompleted);
        }
        finally
        {
            releaseStart.Set();
        }

        await disposal!.WaitAsync(testCancellation);
        Assert.Equal(CopilotLoginStatus.Cancelled, (await login.WaitAsync(testCancellation)).Status);
        Assert.Equal(1, process.StartCount);
        Assert.Equal([false], process.KillTreeArguments);
        Assert.Equal(1, process.DisposeCount);
    }

    [Fact]
    public async Task Cancelling_a_completed_attempt_does_not_cancel_the_next_login()
    {
        FakeLoginProcess first = new() { ExitOnStart = 0 };
        FakeLoginProcess second = new();
        FakeProcessFactory factory = new(first, second);
        using BundledCopilotLoginService service = new(new StubResolver(CliPath()), factory.Create);
        using CancellationTokenSource oldCancellation = new();
        Assert.Equal(CopilotLoginStatus.Completed, (await service.LoginAsync(oldCancellation.Token)).Status);
        Task<CopilotLoginResult> next = service.LoginAsync(TestContext.Current.CancellationToken);
        await second.Waiting.Task.WaitAsync(TestContext.Current.CancellationToken);

        oldCancellation.Cancel();

        Assert.Empty(second.KillTreeArguments);
        Assert.False(next.IsCompleted);
        second.Complete(0);
        Assert.Equal(CopilotLoginStatus.Completed, (await next).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_premature_wait_never_claims_completion_or_kills_without_cancellation(bool premature)
    {
        FakeLoginProcess process = new()
        {
            CompleteWaitEarly = premature,
            Failure = premature ? FailurePoint.None : FailurePoint.Wait,
        };
        using BundledCopilotLoginService service = new(new StubResolver(CliPath()), _ => process);

        CopilotLoginResult result = await service.LoginAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotLoginStatus.RuntimeFailed, result.Status);
        Assert.Equal(CopilotLoginErrorCategory.ProcessWaitFailed, result.ErrorCategory);
        AssertSafe(result);
        Assert.Empty(process.KillTreeArguments);
        Assert.Equal(0, process.DisposeCount);
        Assert.Equal(CopilotLoginStatus.AlreadyRunning, (await service.LoginAsync(TestContext.Current.CancellationToken)).Status);
        service.Dispose();
        Assert.Equal([false], process.KillTreeArguments);
        Assert.Equal(1, process.DisposeCount);
    }

    [Fact]
    public async Task Unconfirmed_shutdown_is_bounded_failed_and_blocks_retry_until_exit_is_known()
    {
        FakeLoginProcess process = new() { ExitOnKill = false };
        FakeProcessFactory factory = new(process, new FakeLoginProcess { ExitOnStart = 0 });
        using BundledCopilotLoginService service = new(new StubResolver(CliPath()), factory.Create);
        using CancellationTokenSource cancellation = new();
        Task<CopilotLoginResult> login = service.LoginAsync(cancellation.Token);
        await process.Waiting.Task.WaitAsync(TestContext.Current.CancellationToken);

        cancellation.Cancel();
        CopilotLoginResult result = await login.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotLoginStatus.RuntimeFailed, result.Status);
        Assert.Equal(CopilotLoginErrorCategory.CleanupFailed, result.ErrorCategory);
        Assert.Equal([false], process.KillTreeArguments);
        Assert.Equal([5_000], process.CleanupWaits);
        Assert.Equal(0, process.DisposeCount);
        Assert.Equal(CopilotLoginStatus.AlreadyRunning, (await service.LoginAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Single(factory.StartInfos);
        process.Complete(-1);
        Assert.Equal(CopilotLoginStatus.Completed, (await service.LoginAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, process.DisposeCount);
    }

    [Theory]
    [InlineData(FailurePoint.Kill)]
    [InlineData(FailurePoint.CleanupWait)]
    [InlineData(FailurePoint.Dispose)]
    public async Task Cleanup_exceptions_are_closed_and_never_report_completion(FailurePoint failure)
    {
        FakeLoginProcess process = new() { Failure = failure };
        using BundledCopilotLoginService service = new(new StubResolver(CliPath()), _ => process);
        using CancellationTokenSource cancellation = new();
        Task<CopilotLoginResult> login = service.LoginAsync(cancellation.Token);
        await process.Waiting.Task.WaitAsync(TestContext.Current.CancellationToken);

        cancellation.Cancel();
        CopilotLoginResult result = await login.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotLoginStatus.RuntimeFailed, result.Status);
        Assert.Equal(CopilotLoginErrorCategory.CleanupFailed, result.ErrorCategory);
        AssertSafe(result);
        Assert.Equal([false], process.KillTreeArguments);
        process.Complete(-1);
    }

    [Fact]
    public async Task Natural_exit_racing_with_kill_is_cancelled_without_double_kill()
    {
        FakeLoginProcess process = new() { Failure = FailurePoint.Kill, ExitBeforeKillFailure = true };
        using BundledCopilotLoginService service = new(new StubResolver(CliPath()), _ => process);
        using CancellationTokenSource cancellation = new();
        Task<CopilotLoginResult> login = service.LoginAsync(cancellation.Token);
        await process.Waiting.Task.WaitAsync(TestContext.Current.CancellationToken);

        cancellation.Cancel();

        Assert.Equal(CopilotLoginStatus.Cancelled, (await login.WaitAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal([false], process.KillTreeArguments);
        Assert.Equal(1, process.DisposeCount);
    }

    private static string CliPath() => Path.Combine(
        Path.GetTempPath(), "StudyReportEvaluator login 日本語 & space", "runtimes", "win-x64", "native", "copilot.exe");

    private static void AssertSafe(CopilotLoginResult result)
    {
        Assert.DoesNotContain(SensitiveFailure, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\private", result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.All(typeof(CopilotLoginResult).GetProperties(), property => Assert.True(property.PropertyType.IsEnum));
    }

    public enum FailurePoint { None, Start, Wait, Kill, CleanupWait, Dispose }

    private sealed class StubResolver(string? path) : ICopilotCliPathResolver
    {
        public int CallCount { get; private set; }
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<CancellationToken, ValueTask<string?>>? ResolveOverride { get; set; }

        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            Called.TrySetResult();
            if (ResolveOverride is not null)
            {
                return ResolveOverride(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(path);
        }
    }

    private sealed class FakeProcessFactory(params FakeLoginProcess[] processes)
    {
        private readonly Queue<FakeLoginProcess> _processes = new(processes);
        public List<ProcessStartInfo> StartInfos { get; } = [];

        public ICopilotLoginProcess Create(ProcessStartInfo startInfo)
        {
            StartInfos.Add(startInfo);
            return _processes.Dequeue();
        }
    }

    private sealed class FakeLoginProcess : ICopilotLoginProcess
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FailurePoint Failure { get; init; }
        public Action? OnStart { get; init; }
        public int? ExitOnStart { get; init; }
        public bool StartReturnsFalse { get; init; }
        public bool IgnoreWaitCancellation { get; init; }
        public bool CompleteWaitEarly { get; init; }
        public bool ExitOnKill { get; init; } = true;
        public bool ExitBeforeKillFailure { get; init; }
        public FakeLoginProcess? Child { get; init; }
        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }
        public int ExitCode { get; private set; }
        public bool HasExited => _exit.Task.IsCompletedSuccessfully;
        public CancellationToken WaitToken { get; private set; }
        public List<bool> KillTreeArguments { get; } = [];
        public List<int> CleanupWaits { get; } = [];

        public bool Start()
        {
            StartCount++;
            OnStart?.Invoke();
            ThrowIf(FailurePoint.Start);
            if (StartReturnsFalse)
            {
                return false;
            }

            if (ExitOnStart is int exitCode)
            {
                Complete(exitCode);
            }

            return true;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitToken = cancellationToken;
            Waiting.TrySetResult();
            ThrowIf(FailurePoint.Wait);
            return CompleteWaitEarly ? Task.CompletedTask
                : IgnoreWaitCancellation ? _exit.Task : _exit.Task.WaitAsync(cancellationToken);
        }

        public void Kill(bool entireProcessTree)
        {
            KillTreeArguments.Add(entireProcessTree);
            if (entireProcessTree)
            {
                Child?.Kill(entireProcessTree: true);
            }

            if (ExitBeforeKillFailure)
            {
                Complete(0);
            }

            ThrowIf(FailurePoint.Kill);
            if (ExitOnKill)
            {
                Complete(-1);
            }
        }

        public bool WaitForExit(int milliseconds)
        {
            CleanupWaits.Add(milliseconds);
            ThrowIf(FailurePoint.CleanupWait);
            return HasExited;
        }

        public void Complete(int exitCode)
        {
            ExitCode = exitCode;
            _exit.TrySetResult();
        }

        public void Dispose()
        {
            DisposeCount++;
            ThrowIf(FailurePoint.Dispose);
        }

        private void ThrowIf(FailurePoint point)
        {
            if (Failure == point)
            {
                throw new InvalidOperationException(SensitiveFailure);
            }
        }
    }

    private sealed class TemporaryBundle : IDisposable
    {
        public TemporaryBundle(bool writeWrongHashManifest)
        {
            Directory = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-Login-" + Guid.NewGuid().ToString("N"));
            string rid = (OperatingSystem.IsWindows() ? "win" : "osx") + "-"
                + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");
            string binaryName = OperatingSystem.IsWindows() ? "copilot.exe" : "copilot";
            string nativeDirectory = Path.Combine(Directory, "runtimes", rid, "native");
            System.IO.Directory.CreateDirectory(nativeDirectory);
            string binary = Path.Combine(nativeDirectory, binaryName);
            File.Copy(typeof(BundledCopilotLoginServiceTests).Assembly.Location, binary);
            if (writeWrongHashManifest)
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(binary);
                string sdkVersion = typeof(CopilotClient).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+', 2)[0];
                File.WriteAllText(Path.Combine(Directory, BundledCopilotCliPathResolver.ManifestFileName),
                    JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1,
                        runtimeIdentifier = rid,
                        cliVersion = version.ProductVersion ?? version.FileVersion,
                        cliSha256 = new string('0', 64),
                        sdkVersion,
                        cliRelativePath = $"runtimes/{rid}/native/{binaryName}",
                    }));
            }
        }

        public string Directory { get; }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}