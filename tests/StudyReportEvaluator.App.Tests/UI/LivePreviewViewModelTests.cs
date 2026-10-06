using System.Collections.Immutable;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Usage;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-046 (AC-047)
public sealed class LivePreviewViewModelTests
{
    [Fact]
    public async Task Planned_rows_are_listed_one_to_one_then_updates_and_selection_drive_the_detail()
    {
        (ExecutionViewModel viewModel, PreviewBoundary boundary) = await Configured(2, 4, async (publish, token) =>
        {
            publish(Planned(2, 4));
            publish(Row(3, LivePreviewRowState.Running, Item("prompt-3", running: true)));
            publish(Row(2, LivePreviewRowState.Running, Item("prompt-2", running: true)));
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        Assert.Equal(LiveQuantificationPreviewViewModel.EmptyDetailText, viewModel.LivePreview.DetailText);
        Assert.Equal("速報値（Excel 行ごと・0 行）", viewModel.LivePreview.HeadingText);

        Task run = viewModel.StartAsync(TestContext.Current.CancellationToken);
        await boundary.Published.WaitAsync(TestContext.Current.CancellationToken);
        LiveQuantificationPreviewViewModel preview = viewModel.LivePreview;

        Assert.Equal("速報値（Excel 行ごと・3 行）", preview.HeadingText);
        Assert.Equal([2, 3, 4], preview.Rows.Select(row => row.SourceRowNumber));
        Assert.Equal("Excel 行 4", preview.Rows[2].Label);
        Assert.Equal("待機中 · 項目 0 / 3", preview.Rows[2].StateLine);
        Assert.Equal("評価中 · 項目 0 / 1", preview.Rows[0].StateLine);
        Assert.Contains("prompt-2", preview.DetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt-3", preview.DetailText, StringComparison.Ordinal);

        preview.SelectedRow = preview.Rows[1];
        Assert.Contains("prompt-3", preview.DetailText, StringComparison.Ordinal);
        Assert.Contains("Excel 行 3", preview.DetailText, StringComparison.Ordinal);
        preview.SelectedRow = null;
        Assert.Contains("prompt-2", preview.DetailText, StringComparison.Ordinal);

        viewModel.Cancel();
        await run;
        Assert.Equal(LivePreviewRowState.Interrupted, preview.Rows[0].State);
        Assert.Equal(LivePreviewRowState.Interrupted, preview.Rows[1].State);
        Assert.Equal(LivePreviewRowState.Unprocessed, preview.Rows[2].State);
        Assert.Equal("未処理 · 項目 0 / 3", preview.Rows[2].StateLine);
        Assert.Equal(3, preview.RowCount);
        viewModel.Dispose();
    }

    [Fact]
    public async Task Completed_row_shows_summary_and_a_new_run_discards_the_previous_content()
    {
        int runs = 0;
        (ExecutionViewModel viewModel, PreviewBoundary _) = await Configured(2, 2, (publish, _) =>
        {
            runs++;
            publish(Planned(2, 2));
            publish(Row(2, LivePreviewRowState.Completed, Item("prompt-run-" + runs, running: false), last: 2));
            return Task.CompletedTask;
        });

        await viewModel.StartAsync(TestContext.Current.CancellationToken);
        LivePreviewRowViewModel row = Assert.Single(viewModel.LivePreview.Rows);
        Assert.Equal("完了 · 項目 1 / 1", row.StateLine);
        Assert.Equal("通常 8,6", row.ValueSummary);
        Assert.Contains("prompt-run-1", viewModel.LivePreview.DetailText, StringComparison.Ordinal);
        Assert.Equal(LivePreviewRowState.Completed, row.State);

        await viewModel.StartAsync(TestContext.Current.CancellationToken);
        Assert.Contains("prompt-run-2", viewModel.LivePreview.DetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt-run-1", viewModel.LivePreview.DetailText, StringComparison.Ordinal);
        Assert.NotSame(row, Assert.Single(viewModel.LivePreview.Rows));

        viewModel.Dispose();
        Assert.Empty(viewModel.LivePreview.Rows);
        Assert.Equal(LiveQuantificationPreviewViewModel.EmptyDetailText, viewModel.LivePreview.DetailText);
    }

    [Fact]
    public void Long_text_is_limited_and_a_large_retained_total_lowers_the_limit_without_dropping_rows()
    {
        LiveQuantificationPreviewViewModel preview = new();
        preview.Apply(Planned(2, 3));
        preview.Apply(Row(2, LivePreviewRowState.Completed, Item(new string('p', 2_500), running: false), last: 3));

        string detail = preview.Rows[0].DetailText;
        Assert.Contains("…（全 2,500 文字中、先頭 2,000 文字を表示）", detail, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('p', 2_001), detail, StringComparison.Ordinal);

        LivePreviewUpdate big = Row(3, LivePreviewRowState.Completed, Item(new string('q', 1_000), running: false), last: 3);
        LiveQuantificationPreviewViewModel budgeted = new();
        budgeted.Apply(Planned(2, 3));
        typeof(LiveQuantificationPreviewViewModel)
            .GetField("retainedCharacters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(budgeted, LiveQuantificationPreviewViewModel.RetainedCharacterBudget + 1);
        budgeted.Apply(big);
        string compact = budgeted.Rows[1].DetailText;
        Assert.Contains("先頭 200 文字を表示）", compact, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('q', 201), compact, StringComparison.Ordinal);
        Assert.Equal(2, budgeted.RowCount);
    }

    [Fact]
    public void Twenty_thousand_rows_are_all_kept_in_order_and_out_of_range_rows_are_ignored()
    {
        LiveQuantificationPreviewViewModel preview = new();
        preview.Apply(Planned(2, 20_001));
        preview.Apply(Row(20_001, LivePreviewRowState.Completed, Item("last", running: false), last: 20_001));
        preview.Apply(new LivePreviewUpdate
        {
            FirstDataRow = 2,
            LastDataRow = 20_001,
            Row = Row(99_999, LivePreviewRowState.Completed, Item("outside", running: false), last: 20_001).Row,
        });

        Assert.Equal(20_000, preview.RowCount);
        Assert.Equal("速報値（Excel 行ごと・20000 行）", preview.HeadingText);
        Assert.Equal(2, preview.Rows[0].SourceRowNumber);
        Assert.Equal(20_001, preview.Rows[^1].SourceRowNumber);
        Assert.Equal(LivePreviewRowState.Completed, preview.Rows[^1].State);
        Assert.DoesNotContain("outside", preview.DetailText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_content_never_reaches_ToString_and_a_faulting_preview_cannot_fail_the_run()
    {
        (ExecutionViewModel viewModel, PreviewBoundary _) = await Configured(2, 2, (publish, _) =>
        {
            publish(Planned(2, 2));
            publish(Row(2, LivePreviewRowState.Completed, Item("CANARY-PROMPT", running: false), last: 2));
            publish(new LivePreviewUpdate { FirstDataRow = 5, LastDataRow = 1 });
            return Task.CompletedTask;
        });

        await viewModel.StartAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(viewModel.LastRunContext);
        string text = string.Join(
            "|",
            viewModel.LivePreview.ToString(),
            viewModel.LivePreview.Rows[0].ToString(),
            viewModel.ToString());
        Assert.DoesNotContain("CANARY", text, StringComparison.Ordinal);
        viewModel.Dispose();
    }

    internal static LivePreviewUpdate Planned(int first, int last) =>
        new() { FirstDataRow = first, LastDataRow = last, ItemsPerRow = 3 };

    internal static LivePreviewUpdate Row(
        int number,
        LivePreviewRowState state,
        LivePreviewItem item,
        int last = 4) =>
        new()
        {
            FirstDataRow = 2,
            LastDataRow = last,
            ItemsPerRow = 3,
            Row = new LivePreviewRow { SourceRowNumber = number, State = state, Items = [item] },
        };

    internal static LivePreviewItem Item(string prompt, bool running) =>
        new()
        {
            Kind = LivePreviewItemKind.Normal,
            Title = "通常評価 · 設問「Question Q1」· 評価「Evaluator E1」",
            Cells = [new LivePreviewCell("A", true, "answer text"), new LivePreviewCell("B", false, "support text")],
            PromptText = prompt,
            Phase = running ? LivePreviewItemPhase.Running : LivePreviewItemPhase.Settled,
            StatusCode = running ? null : "SUCCESS",
            Measures = running
                ? []
                :
                [
                    new LivePreviewMeasure("Criterion C1", "8", "範囲 0〜10", "reason one", "evidence one"),
                    new LivePreviewMeasure("Criterion C2", "6", "範囲 0〜10", "reason two", string.Empty),
                ],
        };

    private static async Task<(ExecutionViewModel ViewModel, PreviewBoundary Boundary)> Configured(
        int firstRow,
        int lastRow,
        Func<Action<LivePreviewUpdate>, CancellationToken, Task> script)
    {
        QuantificationDefinition definition = U04TestSupport.Definition(firstRow, lastRow);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        PreviewBoundary boundary = new(summary, script);
        ExecutionViewModel viewModel = U04TestSupport.ConfiguredExecutionViewModel(definition, metadata, boundary);
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        return (viewModel, boundary);
    }

    internal sealed class PreviewBoundary(
        RunSummary summary,
        Func<Action<LivePreviewUpdate>, CancellationToken, Task> script) : IQuantificationRunBoundary
    {
        private readonly TaskCompletionSource published = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Published => published.Task;

        public Task<RunSummary> RunAsync(
            QuantificationRunRequest request,
            Action<EvaluationProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The preview overload must be used.");

        public async Task<RunSummary> RunAsync(
            QuantificationRunRequest request,
            Action<EvaluationProgress>? progress,
            Action<JobCostSnapshot>? costChanged,
            Action<LivePreviewUpdate>? livePreview,
            CancellationToken cancellationToken)
        {
            Assert.NotNull(livePreview);
            int count = 0;
            await script(
                update =>
                {
                    livePreview(update);
                    if (++count >= 3)
                    {
                        published.TrySetResult();
                    }
                },
                cancellationToken);
            published.TrySetResult();
            return summary;
        }
    }
}
