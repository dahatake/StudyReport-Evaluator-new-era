using System.Collections.Immutable;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workflow;

// Requirements: FR-046 (AC-047)
public sealed class LivePreviewSchedulerTests
{
    private const string Reference = "reference answer";

    [Fact]
    public async Task Row_reports_source_text_prompt_and_raw_values_per_item_in_stable_order()
    {
        EvaluationPlan plan = Plan(specialPoints: 10m);
        ScriptedRowSource rows = Rows("student answer", "special answer");
        List<LivePreviewRow> snapshots = [];
        LivePreviewRow? whileNormalRuns = null;
        ScriptedRunner normal = new((payload, _, _) =>
        {
            lock (snapshots)
            {
                whileNormalRuns = snapshots[^1];
            }

            return Task.FromResult(EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload, _ => 7m)));
        });
        ScriptedSpecialRunner special = SpecialSuccess();

        DurableRowEvaluationResult result = await Evaluate(
            plan, rows, normal, special, snapshots, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        LivePreviewRow first = snapshots[0];
        Assert.Equal(LivePreviewRowState.Running, first.State);
        Assert.Equal(
            [LivePreviewItemKind.Normal, LivePreviewItemKind.Special, LivePreviewItemKind.Similarity],
            first.Items.Select(item => item.Kind));
        Assert.All(first.Items, item => Assert.Equal(LivePreviewItemPhase.Waiting, item.Phase));

        LivePreviewItem running = Assert.IsType<LivePreviewRow>(whileNormalRuns).Items[0];
        Assert.Equal(LivePreviewItemPhase.Running, running.Phase);
        Assert.Equal(normal.Payloads[0].RenderedPrompt, running.PromptText);
        Assert.Equal("AI評価中", LivePreviewFormatter.StatusText(running));
        Assert.Empty(running.Measures);

        LivePreviewRow last = snapshots[^1];
        Assert.Equal(LivePreviewRowState.Completed, last.State);
        Assert.Equal(3, last.SettledCount);

        LivePreviewItem normalItem = last.Items[0];
        Assert.Equal(
            [new LivePreviewCell("A", true, "student answer"), new LivePreviewCell("B", false, "special answer")],
            normalItem.Cells);
        Assert.Equal(normal.Payloads[0].RenderedPrompt, normalItem.PromptText);
        LivePreviewMeasure criterion = Assert.Single(normalItem.Measures);
        Assert.Equal("Criterion C1", criterion.Label);
        Assert.Equal("7", criterion.Value);
        Assert.Equal("範囲 0〜10", criterion.RangeText);
        Assert.Equal("Synthetic reason", criterion.Reason);

        LivePreviewItem specialItem = last.Items[1];
        Assert.Equal(
            [new LivePreviewCell("B", true, "special answer"), new LivePreviewCell("A", false, "student answer")],
            specialItem.Cells);
        Assert.Equal(special.Payloads[0].RenderedPrompt, specialItem.PromptText);
        Assert.Equal("0.8", Assert.Single(specialItem.Measures).Value);
        Assert.Equal("special reason", Assert.Single(specialItem.Measures).Reason);

        LivePreviewItem similarity = last.Items[2];
        Assert.Equal([new LivePreviewCell("A", true, "student answer")], similarity.Cells);
        Assert.Equal(Reference, similarity.ComparisonText);
        Assert.Null(similarity.PromptText);
        Assert.Equal("類似度", Assert.Single(similarity.Measures).Label);
        Assert.Equal(
            "通常 7 / 固有 0.8 / 類似 " + Assert.Single(similarity.Measures).Value,
            LivePreviewFormatter.ValueSummary(last));

        string detail = LivePreviewFormatter.DetailText(last);
        Assert.Contains("[列 A · 主回答]", detail, StringComparison.Ordinal);
        Assert.Contains("[列 B · 補助]", detail, StringComparison.Ordinal);
        Assert.Contains(normal.Payloads[0].RenderedPrompt, detail, StringComparison.Ordinal);
        Assert.Contains("Criterion C1: 7（範囲 0〜10）", detail, StringComparison.Ordinal);
        Assert.Contains(LivePreviewFormatter.NoPromptText, detail, StringComparison.Ordinal);
        Assert.Contains("比較文字列（参照回答）:", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_answers_zero_special_budget_and_technical_failures_are_shown_without_values()
    {
        EvaluationPlan empty = Plan(specialPoints: 0m);
        List<LivePreviewRow> emptySnapshots = [];
        await Evaluate(
            empty,
            Rows("   ", "special"),
            new ScriptedRunner((_, _, _) => throw new InvalidOperationException("must not run")),
            new ScriptedSpecialRunner((_, _, _) => throw new InvalidOperationException("must not run")),
            emptySnapshots,
            TestContext.Current.CancellationToken);

        LivePreviewRow emptyRow = emptySnapshots[^1];
        Assert.Equal(
            [ResultsStatusCodes.Empty, ResultsStatusCodes.NotRunZeroBudget, ResultsStatusCodes.Empty],
            emptyRow.Items.Select(item => item.StatusCode));
        Assert.All(emptyRow.Items, item => Assert.Empty(item.Measures));
        Assert.Equal("— / — / —", LivePreviewFormatter.ValueSummary(emptyRow));
        string emptyDetail = LivePreviewFormatter.DetailText(emptyRow);
        Assert.Contains("空回答: AIへ送信せず0点相当", emptyDetail, StringComparison.Ordinal);
        Assert.Contains("未実行: 固有配点0のためAIへ送信せず", emptyDetail, StringComparison.Ordinal);

        List<LivePreviewRow> failedSnapshots = [];
        await Evaluate(
            Plan(specialPoints: 10m),
            Rows("student answer", "special answer"),
            new ScriptedRunner((_, _, _) => Task.FromResult(
                EvaluationRunnerResult.Failed(ResultsStatusCodes.AiTimeout, attemptCount: 3))),
            SpecialSuccess(),
            failedSnapshots,
            TestContext.Current.CancellationToken);

        LivePreviewItem failed = failedSnapshots[^1].Items[0];
        Assert.Equal(ResultsStatusCodes.AiTimeout, failed.StatusCode);
        Assert.Empty(failed.Measures);
        Assert.NotNull(failed.PromptText);
        Assert.Contains(
            "失敗（AI_TIMEOUT）: 値は空欄",
            LivePreviewFormatter.DetailText(failedSnapshots[^1]),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelled_row_never_reports_completed_and_row_read_failure_reports_runtime_failure()
    {
        using CancellationTokenSource cancellation = new();
        List<LivePreviewRow> cancelledSnapshots = [];
        DurableRowEvaluationResult cancelled = await Evaluate(
            Plan(specialPoints: 10m),
            Rows("student answer", "special answer"),
            new ScriptedRunner(async (_, _, token) =>
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException();
            }),
            SpecialSuccess(),
            cancelledSnapshots,
            cancellation.Token);

        Assert.False(cancelled.IsComplete);
        Assert.DoesNotContain(cancelledSnapshots, snapshot => snapshot.State == LivePreviewRowState.Completed);

        List<LivePreviewRow> failedSnapshots = [];
        DurableRowEvaluationResult failed = await Evaluate(
            Plan(specialPoints: 10m),
            new ScriptedRowSource((_, _) => throw new IOException("PRIVATE-READ-CANARY")),
            NormalSuccess(),
            SpecialSuccess(),
            failedSnapshots,
            TestContext.Current.CancellationToken);

        Assert.True(failed.IsComplete);
        LivePreviewRow row = failedSnapshots[^1];
        Assert.Equal(LivePreviewRowState.Completed, row.State);
        Assert.All(row.Items, item => Assert.Equal(ResultsStatusCodes.AiRuntimeFailed, item.StatusCode));
        Assert.DoesNotContain("PRIVATE-READ-CANARY", LivePreviewFormatter.DetailText(row), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_adds_no_AI_calls_and_observer_failures_cannot_change_the_row_result()
    {
        EvaluationPlan plan = Plan(specialPoints: 10m);
        ScriptedRunner withoutPreviewNormal = NormalSuccess();
        ScriptedSpecialRunner withoutPreviewSpecial = SpecialSuccess();
        DurableRowEvaluationResult baseline = await new DurableEvaluationScheduler(
            Rows("student answer", "special answer"),
            withoutPreviewNormal,
            withoutPreviewSpecial).EvaluateRowAsync(
                plan,
                2,
                References(),
                "model-test",
                maxConcurrency: 2,
                cancellationToken: TestContext.Current.CancellationToken);

        ScriptedRunner normal = NormalSuccess();
        ScriptedSpecialRunner special = SpecialSuccess();
        DurableRowEvaluationResult observed = await new DurableEvaluationScheduler(
            Rows("student answer", "special answer"),
            normal,
            special).EvaluateRowAsync(
                plan,
                2,
                References(),
                "model-test",
                reasoningEffort: null,
                maxConcurrency: 2,
                cancellationToken: TestContext.Current.CancellationToken,
                livePreview: _ => throw new InvalidOperationException("observer failure"));

        Assert.True(observed.IsComplete);
        Assert.Equal(withoutPreviewNormal.Payloads.Count, normal.Payloads.Count);
        Assert.Equal(withoutPreviewSpecial.Payloads.Count, special.Payloads.Count);
        Assert.Equal(
            baseline.CompletedRow!.NormalResults.Select(item => item.StatusCode),
            observed.CompletedRow!.NormalResults.Select(item => item.StatusCode));
        Assert.Equal(
            baseline.CompletedRow.SpecialResults.Select(item => item.AcceptedResult!.Score),
            observed.CompletedRow.SpecialResults.Select(item => item.AcceptedResult!.Score));
    }

    [Fact]
    public void Types_redact_content_from_ToString()
    {
        LivePreviewItem item = new()
        {
            Kind = LivePreviewItemKind.Normal,
            Title = "title",
            Cells = [new LivePreviewCell("A", true, "CANARY-TEXT")],
            PromptText = "CANARY-PROMPT",
            ComparisonText = "CANARY-REFERENCE",
            Measures = [new LivePreviewMeasure("L", "1", "範囲 0〜1", "CANARY-REASON", "CANARY-EVIDENCE")],
        };
        LivePreviewRow row = new() { SourceRowNumber = 2, Items = [item] };
        LivePreviewUpdate update = new() { FirstDataRow = 2, LastDataRow = 2, Row = row };

        string text = string.Join(
            "|",
            item.ToString(),
            item.Cells[0].ToString(),
            item.Measures[0].ToString(),
            row.ToString(),
            update.ToString());

        Assert.DoesNotContain("CANARY", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Limit_truncates_with_a_closed_note_and_never_splits_a_surrogate_pair()
    {
        Assert.Equal("abc", LivePreviewFormatter.Limit("abc", 2_000));
        string exact = new('x', 2_000);
        Assert.Equal(exact, LivePreviewFormatter.Limit(exact, 2_000));

        string limited = LivePreviewFormatter.Limit(new string('x', 2_001), 2_000);
        Assert.Equal(new string('x', 2_000) + "…（全 2,001 文字中、先頭 2,000 文字を表示）", limited);

        string pair = new string('a', 199) + "😀tail";
        string compact = LivePreviewFormatter.Limit(pair, 200);
        Assert.StartsWith(new string('a', 199) + "…（全 ", compact, StringComparison.Ordinal);
        Assert.EndsWith("先頭 200 文字を表示）", compact, StringComparison.Ordinal);
    }

    private static Task<DurableRowEvaluationResult> Evaluate(
        EvaluationPlan plan,
        ScriptedRowSource rows,
        ScriptedRunner normal,
        ScriptedSpecialRunner special,
        List<LivePreviewRow> snapshots,
        CancellationToken cancellationToken) =>
        new DurableEvaluationScheduler(rows, normal, special).EvaluateRowAsync(
            plan,
            2,
            References(),
            "model-test",
            reasoningEffort: null,
            maxConcurrency: 1,
            cancellationToken: cancellationToken,
            livePreview: row =>
            {
                lock (snapshots)
                {
                    snapshots.Add(row);
                }
            });

    private static Dictionary<string, CheckpointReference> References() =>
        new(StringComparer.Ordinal)
        {
            ["Q1"] = new CheckpointReference
            {
                QuestionId = "Q1",
                Answer = Reference,
                StatusCode = ResultsStatusCodes.Success,
                GeneratedAtUtc = new DateTimeOffset(2026, 9, 2, 5, 30, 10, TimeSpan.Zero),
                AttemptCount = 1,
            },
        };

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
            U01TestSupport.Question("Q1", "A", ["B"], true, U01TestSupport.Evaluator("E1", "C1")));
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
            new Dictionary<string, string?> { ["A"] = primary, ["B"] = special })));

    private static ScriptedRunner NormalSuccess() =>
        new((payload, _, _) => Task.FromResult(
            EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload, _ => 5m))));

    private static ScriptedSpecialRunner SpecialSuccess() =>
        new((payload, _, _) => Task.FromResult(
            AuxiliaryOperationResult<SpecialQuantificationResult>.Succeeded(
                new SpecialQuantificationResult
                {
                    SpecialEvaluationId = payload.SpecialEvaluationId,
                    Score = 0.8m,
                    Reason = "special reason",
                    Evidence = string.Empty,
                    EvidenceSource = EvidenceSourceKind.None,
                    EvidenceSourceColumnId = string.Empty,
                })));

    private sealed class ScriptedSpecialRunner(
        Func<SafeSpecialEvaluationPayload, string, CancellationToken, Task<AuxiliaryOperationResult<SpecialQuantificationResult>>> evaluate)
        : ISpecialEvaluationOperationRunner
    {
        private readonly object sync = new();
        private readonly List<SafeSpecialEvaluationPayload> payloads = [];

        internal IReadOnlyList<SafeSpecialEvaluationPayload> Payloads
        {
            get
            {
                lock (sync)
                {
                    return payloads.ToArray();
                }
            }
        }

        public Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(
            SafeSpecialEvaluationPayload payload,
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
}
