using System.Collections.Immutable;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.App.Workflow;

/// <summary>
/// Publishes immutable, ordered snapshots of one row's evaluation items to a presentation observer.
/// Observer failures cannot change evaluation state, and no content is stored by the tracker owner.
/// </summary>
internal sealed class LivePreviewRowTracker
{
    private readonly object gate = new();
    private readonly int sourceRowNumber;
    private readonly Action<LivePreviewRow> publish;
    private readonly LivePreviewItem[] items;
    private LivePreviewRowState state = LivePreviewRowState.Running;

    public LivePreviewRowTracker(
        int sourceRowNumber,
        IEnumerable<LivePreviewItem> initialItems,
        Action<LivePreviewRow> publish)
    {
        this.sourceRowNumber = sourceRowNumber;
        this.publish = publish ?? throw new ArgumentNullException(nameof(publish));
        items = initialItems.ToArray();
        lock (gate)
        {
            PublishLocked();
        }
    }

    public void Update(int slot, Func<LivePreviewItem, LivePreviewItem> change)
    {
        lock (gate)
        {
            try
            {
                items[slot] = change(items[slot]);
            }
            catch
            {
                // Presentation formatting cannot alter durable evaluation.
                return;
            }

            PublishLocked();
        }
    }

    public void UpdateAll(Func<int, LivePreviewItem, LivePreviewItem> change)
    {
        lock (gate)
        {
            try
            {
                for (int slot = 0; slot < items.Length; slot++)
                {
                    items[slot] = change(slot, items[slot]);
                }
            }
            catch
            {
                return;
            }

            PublishLocked();
        }
    }

    public void Complete()
    {
        lock (gate)
        {
            state = LivePreviewRowState.Completed;
            PublishLocked();
        }
    }

    private void PublishLocked()
    {
        try
        {
            publish(new LivePreviewRow
            {
                SourceRowNumber = sourceRowNumber,
                State = state,
                Items = [.. items],
            });
        }
        catch
        {
            // Observers are best-effort.
        }
    }
}

internal static class LivePreviewItems
{
    public static string NormalTitle(QuestionDefinition question, EvaluatorDefinition evaluator) =>
        $"通常評価 · 設問「{question.DisplayName}」· 評価「{evaluator.DisplayName}」";

    public static string SpecialTitle(QuestionDefinition question, SpecialEvaluationDefinition special) =>
        $"固有評価 · 設問「{question.DisplayName}」· 項目「{special.DisplayName}」";

    public static string SimilarityTitle(QuestionDefinition question) =>
        $"類似度 · 設問「{question.DisplayName}」";

    public static LivePreviewItem Waiting(LivePreviewItemKind kind, string title) =>
        new() { Kind = kind, Title = title, Phase = LivePreviewItemPhase.Waiting };

    public static ImmutableArray<LivePreviewCell> Cells(
        string primaryColumn,
        IEnumerable<string> columns,
        Func<string, string> read) =>
        columns
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(column => new LivePreviewCell(
                column,
                string.Equals(column, primaryColumn, StringComparison.OrdinalIgnoreCase),
                read(column)))
            .OrderByDescending(cell => cell.IsPrimary)
            .ToImmutableArray();

    public static LivePreviewItem Settled(
        LivePreviewItem item,
        string statusCode,
        ImmutableArray<LivePreviewMeasure> measures = default) =>
        item with
        {
            Phase = LivePreviewItemPhase.Settled,
            StatusCode = statusCode,
            Measures = measures.IsDefault ? [] : measures,
        };

    public static ImmutableArray<LivePreviewMeasure> NormalMeasures(
        QuestionDefinition? question,
        QuantificationResult? result)
    {
        if (result is null)
        {
            return [];
        }

        EvaluatorDefinition? evaluator = question?.Evaluators.FirstOrDefault(
            item => string.Equals(item.Id, result.EvaluatorId, StringComparison.Ordinal));
        return result.Criteria.Select(criterion =>
        {
            CriterionDefinition? definition = evaluator?.Criteria.FirstOrDefault(
                item => string.Equals(item.Id, criterion.CriterionId, StringComparison.Ordinal));
            ScoreRange? range = definition?.Range ?? evaluator?.Range;
            return new LivePreviewMeasure(
                definition?.DisplayName ?? criterion.CriterionId,
                LivePreviewFormatter.FormatNumber(criterion.RawScore),
                range is { } value
                    ? LivePreviewFormatter.RangeText(value.Minimum, value.Maximum)
                    : "範囲 —",
                criterion.Reason,
                criterion.Evidence);
        }).ToImmutableArray();
    }

    public static ImmutableArray<LivePreviewMeasure> SpecialMeasures(SpecialQuantificationResult? result) =>
        result is null
            ? []
            :
            [
                new LivePreviewMeasure(
                    "固有評価スコア",
                    LivePreviewFormatter.FormatNumber(result.Score),
                    LivePreviewFormatter.RangeText(0m, 1m),
                    result.Reason,
                    result.Evidence),
            ];

    public static ImmutableArray<LivePreviewMeasure> SimilarityMeasures(SimilarityQuantificationResult? result) =>
        result is null
            ? []
            :
            [
                new LivePreviewMeasure(
                    "類似度",
                    LivePreviewFormatter.FormatNumber(result.Similarity),
                    LivePreviewFormatter.RangeText(0m, 1m),
                    result.Reason,
                    string.Empty),
            ];

    /// <summary>
    /// Rebuilds a settled row from a stored checkpoint row. The checkpoint never stores answer or
    /// prompt text, so those parts are marked unavailable rather than reconstructed.
    /// </summary>
    public static LivePreviewRow Restored(QuantificationDefinition definition, CheckpointCompletedRow row)
    {
        QuestionDefinition? Question(string id) => definition.Questions.FirstOrDefault(
            item => string.Equals(item.Id, id, StringComparison.Ordinal));
        List<LivePreviewItem> items = [];
        foreach (CheckpointNormalResult result in row.NormalResults)
        {
            QuestionDefinition? question = Question(result.QuestionId);
            EvaluatorDefinition? evaluator = question?.Evaluators.FirstOrDefault(
                item => string.Equals(item.Id, result.EvaluatorId, StringComparison.Ordinal));
            items.Add(Settled(
                Waiting(
                    LivePreviewItemKind.Normal,
                    question is not null && evaluator is not null
                        ? NormalTitle(question, evaluator)
                        : $"通常評価 · {result.QuestionId} / {result.EvaluatorId}") with
                {
                    TextAvailable = false,
                },
                result.StatusCode,
                NormalMeasures(question, result.AcceptedResult)));
        }

        foreach (CheckpointSpecialResult result in row.SpecialResults)
        {
            QuestionDefinition? question = Question(result.QuestionId);
            SpecialEvaluationDefinition? special = question?.SpecialEvaluations.FirstOrDefault(
                item => string.Equals(item.Id, result.SpecialEvaluationId, StringComparison.Ordinal));
            items.Add(Settled(
                Waiting(
                    LivePreviewItemKind.Special,
                    question is not null && special is not null
                        ? SpecialTitle(question, special)
                        : $"固有評価 · {result.QuestionId} / {result.SpecialEvaluationId}") with
                {
                    TextAvailable = false,
                },
                result.StatusCode,
                SpecialMeasures(result.AcceptedResult)));
        }

        foreach (CheckpointSimilarityResult result in row.SimilarityResults)
        {
            QuestionDefinition? question = Question(result.QuestionId);
            items.Add(Settled(
                Waiting(
                    LivePreviewItemKind.Similarity,
                    question is not null ? SimilarityTitle(question) : $"類似度 · {result.QuestionId}") with
                {
                    TextAvailable = false,
                },
                result.StatusCode,
                SimilarityMeasures(result.AcceptedResult)));
        }

        return new LivePreviewRow
        {
            SourceRowNumber = row.SourceRowNumber,
            State = LivePreviewRowState.RestoredFromCheckpoint,
            Items = [.. items],
        };
    }
}
