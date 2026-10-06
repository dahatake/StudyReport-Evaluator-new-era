using System.Diagnostics;

namespace StudyReportEvaluator.App.Copilot;

public enum CopilotLoginStatus
{
    Completed,
    CliUnavailable,
    RuntimeFailed,
    Cancelled,
    AlreadyRunning,
    Disposed,
}

public enum CopilotLoginErrorCategory
{
    None,
    CliUnavailable,
    CliValidationFailed,
    ProcessStartFailed,
    ProcessWaitFailed,
    NonZeroExit,
    CleanupFailed,
}

/// <summary>Safe metadata only; Completed is not a claim of authentication.</summary>
public sealed class CopilotLoginResult
{
    internal CopilotLoginResult(
        CopilotLoginStatus status,
        CopilotLoginErrorCategory errorCategory = CopilotLoginErrorCategory.None)
    {
        Status = status;
        ErrorCategory = errorCategory;
    }

    public CopilotLoginStatus Status { get; }

    public CopilotLoginErrorCategory ErrorCategory { get; }

    public override string ToString() => $"{Status}; error={ErrorCategory}";
}

/// <summary>
/// A single, initially unstarted login process. No credential or standard-stream access.
/// Start must return true only when this instance started a new process.
/// </summary>
public interface ICopilotLoginProcess : IDisposable
{
    bool Start();

    bool HasExited { get; }

    int ExitCode { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Kill(bool entireProcessTree);

    bool WaitForExit(int milliseconds);
}

/// <summary>
/// Starts an explicitly requested bundled CLI login, not an authentication check.
/// Cancel the LoginAsync token to stop login; dispose this service on application close.
/// The UI must separately exclude login while an evaluation is running.
/// </summary>
public sealed class BundledCopilotLoginService : IDisposable
{
    // A shutdown wait, not a deadline for the user's interactive authentication.
    private const int CleanupWaitMilliseconds = 5_000;

    private readonly object _lifecycleGate = new();
    private readonly ICopilotCliPathResolver _pathResolver;
    private readonly Func<ProcessStartInfo, ICopilotLoginProcess> _processFactory;
    private CancellationTokenSource? _activeCancellation;
    private ICopilotLoginProcess? _ownedProcess;
    private bool _stopAttempted;
    private bool _cleanupFailed;
    private bool _disposed;

    public BundledCopilotLoginService()
        : this(new BundledCopilotCliPathResolver())
    {
    }

    public BundledCopilotLoginService(ICopilotCliPathResolver pathResolver)
        : this(pathResolver, static startInfo => new NativeLoginProcess(startInfo))
    {
    }

    public BundledCopilotLoginService(
        ICopilotCliPathResolver pathResolver,
        Func<ProcessStartInfo, ICopilotLoginProcess> processFactory)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(processFactory);
        _pathResolver = pathResolver;
        _processFactory = processFactory;
    }

    public async Task<CopilotLoginResult> LoginAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new(CopilotLoginStatus.Cancelled);
        }

        CancellationTokenSource operationCancellation;
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return new(CopilotLoginStatus.Disposed);
            }

            if (_activeCancellation is not null)
            {
                return new(CopilotLoginStatus.AlreadyRunning);
            }

            // A failed exit observation must not allow a second, overlapping login.
            if (!TryReleaseOwnedProcess(stop: false))
            {
                return _ownedProcess is null
                    ? Failed(CopilotLoginErrorCategory.CleanupFailed)
                    : new(CopilotLoginStatus.AlreadyRunning);
            }

            _stopAttempted = false;
            _cleanupFailed = false;
            operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCancellation = operationCancellation;
        }

        using (operationCancellation)
        {
            CopilotLoginResult result;
            try
            {
                result = await RunLoginAsync(operationCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
            {
                result = new(CopilotLoginStatus.Cancelled);
            }
            catch
            {
                result = Failed(CopilotLoginErrorCategory.ProcessWaitFailed);
            }

            lock (_lifecycleGate)
            {
                bool stop = _disposed || operationCancellation.IsCancellationRequested;
                bool released = TryReleaseOwnedProcess(stop);
                if (!stop && (_disposed || operationCancellation.IsCancellationRequested))
                {
                    stop = true;
                    released = TryReleaseOwnedProcess(stop: true);
                }

                _activeCancellation = null;
                if (_cleanupFailed || (!released && (stop || result.Status == CopilotLoginStatus.Completed)))
                {
                    return Failed(CopilotLoginErrorCategory.CleanupFailed);
                }

                return stop ? new(CopilotLoginStatus.Cancelled) : result;
            }
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_lifecycleGate)
        {
            _disposed = true;
            cancellation = _activeCancellation;
        }

        // Never run cancellation callbacks while holding the lifecycle lock.
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The pending call finished between taking the reference and cancelling it.
        }
        catch
        {
            // Callback exceptions must not leak credential-bearing exception text.
            lock (_lifecycleGate)
            {
                _cleanupFailed = true;
            }
        }

        lock (_lifecycleGate)
        {
            if (!TryReleaseOwnedProcess(stop: true))
            {
                _cleanupFailed = true;
            }
        }
    }

    private async Task<CopilotLoginResult> RunLoginAsync(CancellationToken cancellationToken)
    {
        string? cliPath;
        try
        {
            cliPath = await _pathResolver.ResolveAsync(cancellationToken)
                .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(CopilotLoginStatus.Cancelled);
        }
        catch
        {
            return new(CopilotLoginStatus.CliUnavailable, CopilotLoginErrorCategory.CliValidationFailed);
        }

        try
        {
            if (string.IsNullOrWhiteSpace(cliPath) || !Path.IsPathFullyQualified(cliPath))
            {
                return new(CopilotLoginStatus.CliUnavailable, CopilotLoginErrorCategory.CliUnavailable);
            }

            cliPath = Path.GetFullPath(cliPath);
        }
        catch
        {
            return new(CopilotLoginStatus.CliUnavailable, CopilotLoginErrorCategory.CliValidationFailed);
        }

        ICopilotLoginProcess process;
        Task exit;
        lock (_lifecycleGate)
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                return new(CopilotLoginStatus.Cancelled);
            }

            ICopilotLoginProcess? candidate = null;
            try
            {
                candidate = _processFactory(CreateStartInfo(cliPath));
                if (candidate is null)
                {
                    return Failed(CopilotLoginErrorCategory.ProcessStartFailed);
                }

                if (_disposed || cancellationToken.IsCancellationRequested)
                {
                    DisposeProcess(candidate);
                    return new(CopilotLoginStatus.Cancelled);
                }

                if (!candidate.Start())
                {
                    DisposeProcess(candidate);
                    return Failed(CopilotLoginErrorCategory.ProcessStartFailed);
                }

                // Start and ownership publication share the lock with Dispose.
                // No PID/name lookup, shell, or process-tree ownership is involved.
                _ownedProcess = process = candidate;
            }
            catch
            {
                if (candidate is not null)
                {
                    DisposeProcess(candidate);
                }

                return Failed(CopilotLoginErrorCategory.ProcessStartFailed);
            }

            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                return new(CopilotLoginStatus.Cancelled);
            }

            exit = process.WaitForExitAsync(cancellationToken);
        }

        await exit.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_lifecycleGate)
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                return new(CopilotLoginStatus.Cancelled);
            }

            if (!process.HasExited)
            {
                return Failed(CopilotLoginErrorCategory.ProcessWaitFailed);
            }

            return process.ExitCode == 0
                ? new(CopilotLoginStatus.Completed)
                : Failed(CopilotLoginErrorCategory.NonZeroExit);
        }
    }

    private static ProcessStartInfo CreateStartInfo(string cliPath) =>
        new()
        {
            FileName = cliPath,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            // Fixed CLI 1.0.79 global options precede the explicit web login command.
            ArgumentList = { "--no-auto-update", "--log-level", "none", "login", "--web-flow" },
        };

    // All process lifecycle operations below run under _lifecycleGate.
    private bool TryReleaseOwnedProcess(bool stop)
    {
        ICopilotLoginProcess? process = _ownedProcess;
        if (process is null)
        {
            return true;
        }

        if (!HasExited(process))
        {
            if (!stop || _stopAttempted)
            {
                return false;
            }

            _stopAttempted = true;
            try
            {
                process.Kill(entireProcessTree: false);
            }
            catch
            {
                if (!HasExited(process))
                {
                    _cleanupFailed = true;
                    return false;
                }
            }

            try
            {
                if (!process.WaitForExit(CleanupWaitMilliseconds))
                {
                    _cleanupFailed = true;
                    return false;
                }
            }
            catch
            {
                _cleanupFailed = true;
                return false;
            }
        }

        // Retain a process whose exit is unconfirmed, rather than lose its handle
        // or permit a retry to launch another login alongside it.
        _ownedProcess = null;
        return DisposeProcess(process);
    }

    private bool DisposeProcess(ICopilotLoginProcess process)
    {
        try
        {
            process.Dispose();
            return true;
        }
        catch
        {
            _cleanupFailed = true;
            return false;
        }
    }

    private static bool HasExited(ICopilotLoginProcess process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static CopilotLoginResult Failed(CopilotLoginErrorCategory category) =>
        new(CopilotLoginStatus.RuntimeFailed, category);

    private sealed class NativeLoginProcess(ProcessStartInfo startInfo) : ICopilotLoginProcess
    {
        private readonly Process _process = new() { StartInfo = startInfo };

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.ExitCode;

        public bool Start() => _process.Start();

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _process.WaitForExitAsync(cancellationToken);

        public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);

        public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);

        public void Dispose() => _process.Dispose();
    }
}