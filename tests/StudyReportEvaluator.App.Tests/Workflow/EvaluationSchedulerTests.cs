using System.Collections.Immutable;
using System.Net.Http;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workflow;

// Requirements: NFR-PERF-001 (AC-071)
public sealed class EvaluationSchedulerTests
{
    [Fact]
    public async Task Empty_primary_skips_AI_with_EMPTY_while_empty_supporting_data_remains_scorable()
    {
        EvaluationPlan plan = U01TestSupport.Plan(OneQuestionDefinition(2, 3));
        ScriptedRowSource rows = new((request, _) => Task.FromResult(
            Row(
                request,
                request.SourceRowNumber == 2 ? "  " : "answer",
                string.Empty)));
        ScriptedRunner runner = new((payload, _, _) => Task.FromResult(
            EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload))));

        EvaluationScheduleResult result = await new EvaluationScheduler(rows, runner).RunAsync(
            plan,
            "model-test",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([ResultsStatusCodes.Empty, ResultsStatusCodes.Success], result.Units.Select(unit => unit.StatusCode));
        Assert.Equal(0, result.Units[0].AttemptCount);
        Assert.False(result.Units[0].Scorable);
        Assert.True(result.Units[0].ScorableKnown);
        Assert.True(result.Units[1].Scorable);
        Assert.Single(runner.Payloads);
        Assert.Equal(string.Empty, runner.Payloads[0].SupportingSources[0].Value);
        Assert.Equal(2, rows.Requests.Count);
        Assert.All(rows.Requests, request => Assert.Equal(["A", "B"], request.SelectedColumns));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Concurrency_is_bounded_and_results_remain_associated_with_deterministic_plan_order(
        int maxConcurrency)
    {
        QuantificationDefinition definition = U01TestSupport.Definition(
            2,
            3,
            U01TestSupport.Question(
                "Q1",
                "A",
                ["B"],
                enabled: true,
                U01TestSupport.Evaluator("E1", "C1"),
                U01TestSupport.Evaluator("E2", "C2"),
                U01TestSupport.Evaluator("E3", "C3")));
        EvaluationPlan plan = U01TestSupport.Plan(definition);
        ScriptedRowSource rows = new((request, _) => Task.FromResult(
            Row(request, $"answer-{request.SourceRowNumber}", "support")));
        int active = 0;
        int maximumActive = 0;
        int firstWaveStarted = 0;
        TaskCompletionSource releaseFirstWave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedRunner runner = new(async (payload, _, token) =>
        {
            int nowActive = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximumActive, nowActive);
            if (Interlocked.Increment(ref firstWaveStarted) == maxConcurrency)
            {
                releaseFirstWave.TrySetResult();
            }

            await releaseFirstWave.Task.WaitAsync(token);
            await Task.Yield();
            Interlocked.Decrement(ref active);
            int sourceRow = int.Parse(payload.PrimarySource.Value.AsSpan("answer-".Length));
            int evaluatorOrdinal = payload.EvaluatorId[^1] - '0';
            return EvaluationRunnerResult.Succeeded(
                U01TestSupport.ValidResult(payload, _ => sourceRow + evaluatorOrdinal));
        });

        EvaluationScheduleResult result = await new EvaluationScheduler(rows, runner).RunAsync(
            plan,
            "model-test",
            maxConcurrency,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(maxConcurrency, maximumActive);
        Assert.Equal(6, result.Units.Length);
        Assert.Equal(plan.Items.Select(item => item.SequenceNumber), result.Units.Select(unit => unit.Item.SequenceNumber));
        Assert.Equal(plan.Items.Select(item => item.EvaluatorId), result.Units.Select(unit => unit.AcceptedResult!.EvaluatorId));
        Assert.Equal([3m, 4m, 5m, 4m, 5m, 6m], result.Units.Select(UnitScore));
        Assert.Equal(
            ["E1", "E2", "E3", "E1", "E2", "E3"],
            runner.Payloads.Select(payload => payload.EvaluatorId));
    }

    [Fact]
    public async Task Cancellation_before_run_starts_no_row_read_or_evaluator_dispatch()
    {
        EvaluationPlan plan = U01TestSupport.Plan(OneQuestionDefinition(2, 4));
        ScriptedRowSource rows = new((_, _) => throw new InvalidOperationException("must not read"));
        ScriptedRunner runner = new((_, _, _) => throw new InvalidOperationException("must not dispatch"));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        List<EvaluationProgress> progress = [];

        EvaluationScheduleResult result = await new EvaluationScheduler(rows, runner).RunAsync(
            plan,
            "model-test",
            maxConcurrency: 3,
            progress.Add,
            cancellation.Token);

        Assert.Empty(rows.Requests);
        Assert.Empty(runner.Payloads);
        Assert.All(result.Units, unit => Assert.Equal(ResultsStatusCodes.Cancelled, unit.StatusCode));
        EvaluationProgress final = progress[^1];
        Assert.Equal(EvaluationProgressStatus.Cancelled, final.Status);
        Assert.Equal(plan.TotalCount, final.Total);
        Assert.Equal(plan.TotalCount, final.Completed);
        Assert.Equal(0, final.InFlight);
    }

    [Fact]
    public async Task Cancellation_during_dispatch_reaches_inflight_and_starts_no_later_evaluator()
    {
        EvaluationPlan plan = U01TestSupport.Plan(OneQuestionDefinition(2, 5));
        ScriptedRowSource rows = new((request, _) => Task.FromResult(Row(request, "answer", "support")));
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedRunner runner = new(async (_, _, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        });
        using CancellationTokenSource cancellation = new();

        Task<EvaluationScheduleResult> pending = new EvaluationScheduler(rows, runner).RunAsync(
            plan,
            "model-test",
            maxConcurrency: 1,
            cancellationToken: cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        EvaluationScheduleResult result = await pending.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Single(runner.Payloads);
        Assert.Single(rows.Requests);
        Assert.All(result.Units, unit => Assert.Equal(ResultsStatusCodes.Cancelled, unit.StatusCode));
        Assert.Equal(4, result.Units.Length);
        Assert.True(result.IsCancelled);
    }

    [Fact]
    public async Task Cancellation_after_a_completed_payload_retains_it_and_marks_only_unstarted_units_cancelled()
    {
        EvaluationPlan plan = U01TestSupport.Plan(OneQuestionDefinition(2, 3));
        ScriptedRowSource rows = new((request, _) => Task.FromResult(Row(request, "answer", "support")));
        using CancellationTokenSource cancellation = new();
        ScriptedRunner runner = new((payload, _, _) =>
        {
            EvaluationRunnerResult result = EvaluationRunnerResult.Succeeded(
                U01TestSupport.ValidResult(payload, _ => 7m));
            cancellation.Cancel();
            return Task.FromResult(result);
        });

        EvaluationScheduleResult result = await new EvaluationScheduler(rows, runner).RunAsync(
            plan,
            "model-test",
            maxConcurrency: 1,
            cancellationToken: cancellation.Token);

        Assert.Equal([ResultsStatusCodes.Success, ResultsStatusCodes.Cancelled], result.Units.Select(unit => unit.StatusCode));
        Assert.True(result.Units[0].HasCompletedPayload);
        Assert.Equal(7m, UnitScore(result.Units[0]));
        Assert.Null(result.Units[1].AcceptedResult);
        Assert.Single(runner.Payloads);
    }

    [Fact]
    public async Task Runtime_failures_are_classified_to_safe_blank_statuses_without_exposing_content()
    {
        string[] evaluatorIds = ["E-SCHEMA", "E-TIMEOUT", "E-NETWORK", "E-AUTH", "E-CLEANUP", "E-FATAL"];
        EvaluatorDefinition[] evaluators = evaluatorIds
            .Select((id, index) => U01TestSupport.Evaluator(id, $"C{index}"))
            .ToArray();
        EvaluationPlan plan = U01TestSupport.Plan(
            U01TestSupport.Definition(
                2,
                2,
                U01TestSupport.Question("Q1", "A", ["B"], true, evaluators)));
        ScriptedRowSource rows = new((request, _) => Task.FromResult(Row(request, "PRIVATE-ANSWER-CANARY", "PRIVATE-SUPPORT-CANARY")));
        ScriptedRunner runner = new((payload, _, _) => payload.EvaluatorId switch
        {
            "E-SCHEMA" => Task.FromResult(EvaluationRunnerResult.Failed(ResultsStatusCodes.AiOutputInvalid)),
            "E-TIMEOUT" => throw new TimeoutException("PRIVATE-TIMEOUT-CANARY"),
            "E-NETWORK" => throw new HttpRequestException("PRIVATE-NETWORK-CANARY"),
            "E-AUTH" => throw new EvaluationAuthenticationException(),
            "E-CLEANUP" => throw new EvaluationCleanupException(),
            _ => throw new InvalidOperationException("PRIVATE-FATAL-CANARY"),
        });

        EvaluationScheduleResult result = await new EvaluationScheduler(rows, runner).RunAsync(
            plan,
            "model-test",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                ResultsStatusCodes.AiOutputInvalid,
                ResultsStatusCodes.AiTimeout,
                ResultsStatusCodes.NetworkFailed,
                ResultsStatusCodes.AuthRequired,
                ResultsStatusCodes.CleanupFailed,
                ResultsStatusCodes.AiRuntimeFailed,
            ],
            result.Units.Select(unit => unit.StatusCode));
        Assert.All(result.Units, unit => Assert.Null(unit.AcceptedResult));
        string rendered = result.ToString() + string.Concat(result.Units.Select(unit => unit.ToString()));
        Assert.DoesNotContain("PRIVATE-ANSWER-CANARY", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-SUPPORT-CANARY", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-FATAL-CANARY", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Result_is_revalidated_against_the_snapshot_payload_and_invalid_data_is_not_partially_adopted()
    {
        QuantificationDefinition definition = U01TestSupport.Definition(
            2,
            2,
            U01TestSupport.Question(
                "Q1",
                "A",
                ["B"],
                true,
                U01TestSupport.Evaluator(
                    "E1",
                    "C1",
                    evaluatorRange: new ScoreRange(0m, 10m),
                    criterionRange: new ScoreRange(2m, 5m))));
        EvaluationPlan plan = U01TestSupport.Plan(definition);
        ScriptedRowSource rows = new((request, _) => Task.FromResult(Row(request, "answer", "support")));
        ScriptedRunner runner = new((payload, _, _) => Task.FromResult(
            EvaluationRunnerResult.Succeeded(
                U01TestSupport.ValidResult(payload, _ => 6m))));

        EvaluationScheduleResult result = await new EvaluationScheduler(rows, runner).RunAsync(
            plan,
            "model-test",
            cancellationToken: TestContext.Current.CancellationToken);

        EvaluationUnitResult unit = Assert.Single(result.Units);
        Assert.Equal(ResultsStatusCodes.AiOutputInvalid, unit.StatusCode);
        Assert.Null(unit.AcceptedResult);
    }

    [Fact]
    public async Task Row_source_extras_never_enter_the_payload_and_progress_observer_exceptions_cannot_corrupt_results()
    {
        const string unsentCanary = "UNSENT-COLUMN-CANARY";
        EvaluationPlan plan = U01TestSupport.Plan(OneQuestionDefinition(2, 2));
        ScriptedRowSource rows = new((request, _) => Task.FromResult(
            new EvaluationRowData(
                request.SourceRowNumber,
                new Dictionary<string, string?>
                {
                    ["A"] = "answer",
                    ["B"] = "support",
                    ["C"] = unsentCanary,
                })));
        ScriptedRunner runner = new((payload, _, _) => Task.FromResult(
            EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload))));
        int callbackCount = 0;

        EvaluationScheduleResult result = await new EvaluationScheduler(rows, runner).RunAsync(
            plan,
            "model-test",
            progress: _ =>
            {
                callbackCount++;
                throw new InvalidOperationException("observer failure");
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(callbackCount > 0);
        Assert.Equal(ResultsStatusCodes.Success, Assert.Single(result.Units).StatusCode);
        SafeEvaluationPayload payload = Assert.Single(runner.Payloads);
        Assert.DoesNotContain(unsentCanary, payload.RenderedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(payload.Sources, source => source.SourceColumnId == "C");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task Concurrency_outside_one_through_sixteen_is_rejected(int maxConcurrency)
    {
        EvaluationPlan plan = U01TestSupport.Plan(OneQuestionDefinition(2, 2));
        EvaluationScheduler scheduler = new(
            new ScriptedRowSource((request, _) => Task.FromResult(Row(request, "answer", "support"))),
            new ScriptedRunner((payload, _, _) => Task.FromResult(
                EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload)))));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            scheduler.RunAsync(
                plan,
                "model-test",
                maxConcurrency,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    private static QuantificationDefinition OneQuestionDefinition(int firstRow, int lastRow) =>
        U01TestSupport.Definition(
            firstRow,
            lastRow,
            U01TestSupport.Question(
                "Q1",
                "A",
                ["B"],
                enabled: true,
                U01TestSupport.Evaluator("E1", "C1")));

    private static EvaluationRowData Row(
        EvaluationRowRequest request,
        string primary,
        string support) =>
        new(
            request.SourceRowNumber,
            new Dictionary<string, string?>
            {
                ["A"] = primary,
                ["B"] = support,
            });

    private static decimal UnitScore(EvaluationUnitResult unit) =>
        Assert.Single(Assert.IsType<QuantificationResult>(unit.AcceptedResult).Criteria).RawScore;

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref maximum);
            if (candidate <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref maximum, candidate, observed) != observed);
    }
}

internal sealed class ScriptedRowSource(
    Func<EvaluationRowRequest, CancellationToken, Task<EvaluationRowData>> read) : IEvaluationRowSource
{
    private readonly object sync = new();
    private readonly List<EvaluationRowRequest> requests = [];

    internal IReadOnlyList<EvaluationRowRequest> Requests
    {
        get
        {
            lock (sync)
            {
                return Array.AsReadOnly(requests.ToArray());
            }
        }
    }

    public Task<EvaluationRowData> ReadAsync(
        EvaluationRowRequest request,
        CancellationToken cancellationToken)
    {
        lock (sync)
        {
            requests.Add(request);
        }

        return read(request, cancellationToken);
    }
}

internal sealed class ScriptedRunner(
    Func<SafeEvaluationPayload, string, CancellationToken, Task<EvaluationRunnerResult>> evaluate) : IEvaluationRunner
{
    private readonly object sync = new();
    private readonly List<SafeEvaluationPayload> payloads = [];

    internal IReadOnlyList<SafeEvaluationPayload> Payloads
    {
        get
        {
            lock (sync)
            {
                return Array.AsReadOnly(payloads.ToArray());
            }
        }
    }

    public Task<EvaluationRunnerResult> EvaluateAsync(
        SafeEvaluationPayload payload,
        string modelId,
        CancellationToken cancellationToken)
    {
        lock (sync)
        {
            payloads.Add(payload);
        }

        return evaluate(payload, modelId, cancellationToken);
    }
}
