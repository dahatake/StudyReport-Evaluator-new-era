using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workflow;

// Requirements: FR-032 (AC-033)
public sealed class DurableEvaluationSchedulerTests
{
    [Fact]
    public async Task One_row_read_drives_bounded_normal_special_and_similarity_operations_with_stable_results()
    {
        EvaluationPlan plan = Plan(specialPoints: 10m);
        ScriptedRowSource rows = new((request, _) => Task.FromResult(new EvaluationRowData(
            request.SourceRowNumber,
            new Dictionary<string, string?>
            {
                ["A"] = "student answer",
                ["B"] = "special answer",
                ["C"] = "UNSENT-ROW-CANARY",
            })));
        ConcurrentGate gate = new(expectedStarts: 2);
        ScriptedRunner normal = new(async (payload, _, token) =>
        {
            await gate.EnterAsync(token);
            return EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload, _ => 7m));
        });
        ScriptedSpecialRunner special = new(async (payload, _, token) =>
        {
            await gate.EnterAsync(token);
            return AuxiliaryOperationResult<SpecialQuantificationResult>.Succeeded(
                new SpecialQuantificationResult
                {
                    SpecialEvaluationId = payload.SpecialEvaluationId,
                    Score = 0.8m,
                    Reason = "special reason",
                    Evidence = string.Empty,
                    EvidenceSource = EvidenceSourceKind.None,
                    EvidenceSourceColumnId = string.Empty,
                });
        });
        CheckpointReference reference = Reference("reference answer");
        List<int> inFlight = [];

        DurableRowEvaluationResult result = await new DurableEvaluationScheduler(
            rows,
            normal,
            special).EvaluateRowAsync(
                plan,
                2,
                new Dictionary<string, CheckpointReference>(StringComparer.Ordinal)
                {
                    ["Q1"] = reference,
                },
                "model-test",
                maxConcurrency: 3,
                maximumPromptTokens: 64_000,
                maximumContextWindowTokens: 128_000,
                inFlightChanged: value =>
                {
                    lock (inFlight)
                    {
                        inFlight.Add(value);
                    }
                },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        CheckpointCompletedRow completed = Assert.IsType<CheckpointCompletedRow>(result.CompletedRow);
        Assert.Equal(7m, Assert.Single(Assert.Single(completed.NormalResults).AcceptedResult!.Criteria).RawScore);
        Assert.Equal(0.8m, Assert.Single(completed.SpecialResults).AcceptedResult!.Score);
        Assert.Equal(ResultsStatusCodes.Success, Assert.Single(completed.SimilarityResults).StatusCode);
        Assert.Equal(2, gate.MaximumActive);
        Assert.InRange(inFlight.Max(), 1, 2);
        Assert.Single(rows.Requests);
        Assert.Equal(["A", "B"], rows.Requests[0].SelectedColumns);
        Assert.Single(normal.Payloads);
        Assert.Single(special.Payloads);
        Assert.DoesNotContain("UNSENT-ROW-CANARY", normal.Payloads[0].RenderedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("UNSENT-ROW-CANARY", special.Payloads[0].RenderedPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_sources_skip_all_AI_and_zero_special_budget_has_an_explicit_not_run_status()
    {
        EvaluationPlan plan = Plan(specialPoints: 0m);
        ScriptedRowSource rows = new((request, _) => Task.FromResult(new EvaluationRowData(
            request.SourceRowNumber,
            new Dictionary<string, string?>
            {
                ["A"] = "   ",
                ["B"] = "special content",
            })));
        ScriptedRunner normal = new((_, _, _) => throw new InvalidOperationException("must not run"));
        ScriptedSpecialRunner special = new((_, _, _) => throw new InvalidOperationException("must not run"));

        DurableRowEvaluationResult result = await new DurableEvaluationScheduler(
            rows,
            normal,
            special).EvaluateRowAsync(
                plan,
                2,
                new Dictionary<string, CheckpointReference>(StringComparer.Ordinal)
                {
                    ["Q1"] = Reference("reference answer"),
                },
                "model-test",
                maxConcurrency: 1,
                cancellationToken: TestContext.Current.CancellationToken);

        CheckpointCompletedRow completed = Assert.IsType<CheckpointCompletedRow>(result.CompletedRow);
        Assert.Equal(ResultsStatusCodes.Empty, Assert.Single(completed.NormalResults).StatusCode);
        Assert.Equal(ResultsStatusCodes.NotRunZeroBudget, Assert.Single(completed.SpecialResults).StatusCode);
        Assert.Equal(ResultsStatusCodes.Empty, Assert.Single(completed.SimilarityResults).StatusCode);
        Assert.Empty(normal.Payloads);
        Assert.Empty(special.Payloads);
    }

    [Fact]
    public async Task Reference_failure_propagates_to_similarity_without_dispatch_but_row_remains_complete()
    {
        EvaluationPlan plan = Plan(specialPoints: 10m);
        ScriptedRowSource rows = Rows("student answer", "special answer");
        ScriptedRunner normal = NormalSuccess();
        ScriptedSpecialRunner special = SpecialSuccess();
        CheckpointReference failedReference = new()
        {
            QuestionId = "Q1",
            StatusCode = ResultsStatusCodes.AiTimeout,
            GeneratedAtUtc = new DateTimeOffset(2026, 9, 2, 5, 30, 10, TimeSpan.Zero),
            AttemptCount = 3,
        };

        DurableRowEvaluationResult result = await new DurableEvaluationScheduler(
            rows,
            normal,
            special).EvaluateRowAsync(
                plan,
                2,
                new Dictionary<string, CheckpointReference> { ["Q1"] = failedReference },
                "model-test",
                maxConcurrency: 2,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(
            ResultsStatusCodes.AiTimeout,
            Assert.Single(result.CompletedRow!.SimilarityResults).StatusCode);
    }

    [Fact]
    public async Task Cancellation_during_any_operation_discards_the_whole_in_progress_row()
    {
        EvaluationPlan plan = Plan(specialPoints: 10m);
        ScriptedRowSource rows = Rows("student answer", "special answer");
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource normalStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedRunner normal = new(async (payload, _, token) =>
        {
            normalStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload));
        });
        ScriptedSpecialRunner special = new(async (_, _, _) =>
        {
            await normalStarted.Task;
            cancellation.Cancel();
            return AuxiliaryOperationResult<SpecialQuantificationResult>.Failed(ResultsStatusCodes.Cancelled);
        });
        DurableRowEvaluationResult result = await new DurableEvaluationScheduler(
            rows,
            normal,
            special).EvaluateRowAsync(
                plan,
                2,
                new Dictionary<string, CheckpointReference> { ["Q1"] = Reference("reference") },
                "model-test",
                maxConcurrency: 2,
                cancellationToken: cancellation.Token);
        Assert.False(result.IsComplete);
        Assert.Null(result.CompletedRow);
        Assert.Null(result.CompletedRow);
    }

    [Fact]
    public async Task Capacity_excess_blocks_normal_special_and_similarity_dispatch_before_AI_send()
    {
        EvaluationPlan plan = Plan(specialPoints: 10m);
        string oversized = new('X', 1_000);
        ScriptedRowSource rows = Rows(oversized, oversized);
        ScriptedRunner normal = new((_, _, _) => throw new InvalidOperationException("must not run"));
        ScriptedSpecialRunner special = new((_, _, _) => throw new InvalidOperationException("must not run"));

        DurableRowEvaluationResult result = await new DurableEvaluationScheduler(
            rows,
            normal,
            special).EvaluateRowAsync(
                plan,
                2,
                new Dictionary<string, CheckpointReference> { ["Q1"] = Reference(oversized) },
                "model-test",
                maxConcurrency: 3,
                maximumPromptTokens: 100,
                maximumContextWindowTokens: 100,
                cancellationToken: TestContext.Current.CancellationToken);

        CheckpointCompletedRow completed = Assert.IsType<CheckpointCompletedRow>(result.CompletedRow);
        Assert.Equal(ResultsStatusCodes.AiOutputInvalid, Assert.Single(completed.NormalResults).StatusCode);
        Assert.Equal(ResultsStatusCodes.AiOutputInvalid, Assert.Single(completed.SpecialResults).StatusCode);
        Assert.Equal(ResultsStatusCodes.Success, Assert.Single(completed.SimilarityResults).StatusCode);
        Assert.Empty(normal.Payloads);
        Assert.Empty(special.Payloads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task Concurrency_outside_one_through_sixteen_is_rejected(int concurrency)
    {
        EvaluationPlan plan = Plan(specialPoints: 10m);
        DurableEvaluationScheduler scheduler = new(
            Rows("answer", "special"),
            NormalSuccess(),
            SpecialSuccess());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => scheduler.EvaluateRowAsync(
            plan,
            2,
            new Dictionary<string, CheckpointReference> { ["Q1"] = Reference("reference") },
            "model-test",
            concurrency,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    private static EvaluationPlan Plan(decimal specialPoints)
    {
        SpecialEvaluationDefinition special = new()
        {
            Id = "S1",
            DisplayName = "Special",
            PrimarySourceColumn = "B",
            SupportingSourceColumns = ["A"],
            PromptTemplate = "Special {回答} {補助情報}",
        };
        QuantificationDefinition source = U01TestSupport.Definition(
            2,
            2,
            U01TestSupport.Question(
                "Q1",
                "A",
                ["B"],
                true,
                U01TestSupport.Evaluator("E1", "C1")));
        QuantificationDefinition definition = source with
        {
            BasePoints = 98m - specialPoints,
            SpecialPoints = specialPoints,
            Questions = [source.Questions[0] with { SpecialEvaluations = [special] }],
        };
        return U01TestSupport.Plan(definition);
    }

    private static ScriptedRowSource Rows(string primary, string special) =>
        new((request, _) => Task.FromResult(new EvaluationRowData(
            request.SourceRowNumber,
            new Dictionary<string, string?>
            {
                ["A"] = primary,
                ["B"] = special,
            })));

    private static ScriptedRunner NormalSuccess() =>
        new((payload, _, _) => Task.FromResult(
            EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload, _ => 5m))));

    private static ScriptedSpecialRunner SpecialSuccess() =>
        new((payload, _, _) => Task.FromResult(
            AuxiliaryOperationResult<SpecialQuantificationResult>.Succeeded(
                new SpecialQuantificationResult
                {
                    SpecialEvaluationId = payload.SpecialEvaluationId,
                    Score = 0.5m,
                    Reason = "special reason",
                    Evidence = string.Empty,
                    EvidenceSource = EvidenceSourceKind.None,
                    EvidenceSourceColumnId = string.Empty,
                })));

    private static CheckpointReference Reference(string answer) =>
        new()
        {
            QuestionId = "Q1",
            Answer = answer,
            StatusCode = ResultsStatusCodes.Success,
            GeneratedAtUtc = new DateTimeOffset(2026, 9, 2, 5, 30, 10, TimeSpan.Zero),
            AttemptCount = 1,
        };

    private sealed class ScriptedSpecialRunner(
        Func<SafeSpecialEvaluationPayload, string, CancellationToken, Task<AuxiliaryOperationResult<SpecialQuantificationResult>>> evaluate)
        : ISpecialEvaluationOperationRunner
    {
        private readonly List<SafeSpecialEvaluationPayload> payloads = [];

        internal IReadOnlyList<SafeSpecialEvaluationPayload> Payloads => payloads.AsReadOnly();

        public Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(
            SafeSpecialEvaluationPayload payload,
            string modelId,
            CancellationToken cancellationToken)
        {
            payloads.Add(payload);
            return evaluate(payload, modelId, cancellationToken);
        }
    }

    private sealed class ConcurrentGate(int expectedStarts)
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int started;
        private int active;

        internal int MaximumActive { get; private set; }

        internal async Task EnterAsync(CancellationToken cancellationToken)
        {
            int current = Interlocked.Increment(ref active);
            MaximumActive = Math.Max(MaximumActive, current);
            if (Interlocked.Increment(ref started) == expectedStarts)
            {
                ready.TrySetResult();
            }

            await ready.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref active);
        }
    }
}
