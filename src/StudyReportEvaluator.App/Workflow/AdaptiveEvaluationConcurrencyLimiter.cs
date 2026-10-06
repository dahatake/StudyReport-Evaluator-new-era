using StudyReportEvaluator.App.Copilot;

namespace StudyReportEvaluator.App.Workflow;

public sealed class AdaptiveEvaluationConcurrencyLimiter : IEvaluationConcurrencyObserver
{
    private const int SuccessesPerIncrease = 8;
    private readonly SemaphoreSlim gate;
    private readonly object stateGate = new();
    private int active;
    private int effectiveLimit;
    private int successStreak;

    public AdaptiveEvaluationConcurrencyLimiter(int maximumConcurrency)
    {
        _ = new EvaluationSchedulerOptions(maximumConcurrency);
        MaximumConcurrency = maximumConcurrency;
        effectiveLimit = maximumConcurrency;
        gate = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
    }

    public int MaximumConcurrency { get; }

    public int EffectiveLimit
    {
        get
        {
            lock (stateGate)
            {
                return effectiveLimit;
            }
        }
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (stateGate)
            {
                if (active < effectiveLimit)
                {
                    active++;
                    return new Lease(this);
                }
            }

            gate.Release();
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
        }
    }

    public void RecordAttemptSucceeded()
    {
        lock (stateGate)
        {
            if (effectiveLimit >= MaximumConcurrency)
            {
                successStreak = 0;
                return;
            }

            successStreak++;
            if (successStreak >= SuccessesPerIncrease)
            {
                effectiveLimit++;
                successStreak = 0;
            }
        }
    }

    public void RecordRateLimited(TimeSpan? retryAfter)
    {
        lock (stateGate)
        {
            effectiveLimit = Math.Max(1, effectiveLimit / 2);
            successStreak = 0;
        }
    }

    private void Release()
    {
        lock (stateGate)
        {
            active--;
        }

        gate.Release();
    }

    private sealed class Lease(AdaptiveEvaluationConcurrencyLimiter owner) : IAsyncDisposable
    {
        private AdaptiveEvaluationConcurrencyLimiter? current = owner;

        public ValueTask DisposeAsync()
        {
            AdaptiveEvaluationConcurrencyLimiter? limiter = Interlocked.Exchange(ref current, null);
            limiter?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
