using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace StudyReportEvaluator.App.Workflow;

public enum LivePreviewRowState
{
    Waiting,
    Running,
    Completed,
    Unprocessed,
    Interrupted,
    RestoredFromCheckpoint,
}

public enum LivePreviewItemKind
{
    Normal,
    Special,
    Similarity,
}

public enum LivePreviewItemPhase
{
    Waiting,
    Running,
    Settled,
}

/// <summary>One workbook cell whose text is used by an evaluation item.</summary>
public sealed record LivePreviewCell(string ColumnId, bool IsPrimary, string Value)
{
    public override string ToString() =>
        $"{nameof(LivePreviewCell)} {{ ColumnId = {ColumnId}, IsPrimary = {IsPrimary}, Content = <redacted> }}";
}

/// <summary>One raw quantified value (criterion raw score, special score or similarity).</summary>
public sealed record LivePreviewMeasure(
    string Label,
    string Value,
    string RangeText,
    string Reason,
    string Evidence)
{
    public override string ToString() =>
        $"{nameof(LivePreviewMeasure)} {{ Content = <redacted> }}";
}

public sealed record LivePreviewItem
{
    public required LivePreviewItemKind Kind { get; init; }

    public required string Title { get; init; }

    public ImmutableArray<LivePreviewCell> Cells { get; init; } = [];

    /// <summary>The complete prompt sent to the AI; null when no AI prompt exists (yet or at all).</summary>
    public string? PromptText { get; init; }

    /// <summary>Text compared with the source (the reference answer for similarity).</summary>
    public string? ComparisonText { get; init; }

    public LivePreviewItemPhase Phase { get; init; }

    /// <summary>Closed status code once settled; null before.</summary>
    public string? StatusCode { get; init; }

    public ImmutableArray<LivePreviewMeasure> Measures { get; init; } = [];

    /// <summary>False when the run resumed a stored row whose text was never persisted.</summary>
    public bool TextAvailable { get; init; } = true;

    public string SummaryPrefix => Kind switch
    {
        LivePreviewItemKind.Normal => "通常",
        LivePreviewItemKind.Special => "固有",
        _ => "類似",
    };

    public override string ToString() =>
        $"{nameof(LivePreviewItem)} {{ Kind = {Kind}, Phase = {Phase}, StatusCode = {StatusCode}, Content = <redacted> }}";
}

public sealed record LivePreviewRow
{
    public required int SourceRowNumber { get; init; }

    public LivePreviewRowState State { get; init; }

    public ImmutableArray<LivePreviewItem> Items { get; init; } = [];

    public int SettledCount => Items.Count(item => item.Phase == LivePreviewItemPhase.Settled);

    public override string ToString() =>
        $"{nameof(LivePreviewRow)} {{ SourceRowNumber = {SourceRowNumber.ToString(CultureInfo.InvariantCulture)}, State = {State}, Items = {Items.Length.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

/// <summary>
/// A run-time observation for the execution screen. Every update names the run's student-row range;
/// <see cref="Row"/> is present when a row changed. Content is never persisted or logged.
/// </summary>
public sealed record LivePreviewUpdate
{
    public required int FirstDataRow { get; init; }

    public required int LastDataRow { get; init; }

    /// <summary>Evaluation items per student row (normal + special + similarity), shown before a row starts.</summary>
    public int ItemsPerRow { get; init; }

    public LivePreviewRow? Row { get; init; }

    public override string ToString() =>
        $"{nameof(LivePreviewUpdate)} {{ FirstDataRow = {FirstDataRow.ToString(CultureInfo.InvariantCulture)}, LastDataRow = {LastDataRow.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public static class LivePreviewFormatter
{
    public const int DefaultTextLimit = 2_000;
    public const int CompactTextLimit = 200;
    public const string NoValueMark = "—";
    public const string NoPromptText = "なし（ローカル計算のためAIへ送信しません）";
    public const string TextNotStoredText = "（再開前に完了した行のため、対象の文字列とPromptは保存されていません）";

    public static string FormatNumber(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    public static string RangeText(decimal minimum, decimal maximum) =>
        $"範囲 {FormatNumber(minimum)}〜{FormatNumber(maximum)}";

    public static string StateText(LivePreviewRowState state) => state switch
    {
        LivePreviewRowState.Waiting => "待機中",
        LivePreviewRowState.Running => "評価中",
        LivePreviewRowState.Completed => "完了",
        LivePreviewRowState.Unprocessed => "未処理",
        LivePreviewRowState.Interrupted => "中断",
        _ => "再開前に完了",
    };

    public static string StatusText(LivePreviewItem item) => item.Phase switch
    {
        LivePreviewItemPhase.Waiting => "待機中",
        LivePreviewItemPhase.Running => "AI評価中",
        _ => item.StatusCode switch
        {
            "SUCCESS" => "成功",
            "EMPTY" => "空回答: AIへ送信せず0点相当",
            "NOT_RUN_ZERO_BUDGET" => "未実行: 固有配点0のためAIへ送信せず",
            "CANCELLED" => "取消",
            null or "" => "失敗（不明）: 値は空欄",
            var code => $"失敗（{code}）: 値は空欄",
        },
    };

    public static string RowLabel(LivePreviewRow row) =>
        $"Excel 行 {row.SourceRowNumber.ToString(CultureInfo.InvariantCulture)}";

    public static string StateLine(LivePreviewRow row, int plannedItemCount = 0) =>
        $"{StateText(row.State)} · 項目 {row.SettledCount.ToString(CultureInfo.InvariantCulture)} / {(row.Items.IsDefaultOrEmpty ? plannedItemCount : row.Items.Length).ToString(CultureInfo.InvariantCulture)}";

    public static string ValueSummary(LivePreviewRow row)
    {
        if (row.Items.IsDefaultOrEmpty)
        {
            return NoValueMark;
        }

        return string.Join(
            " / ",
            row.Items.Select(item => item.Measures.IsDefaultOrEmpty
                ? NoValueMark
                : item.SummaryPrefix + " " + string.Join(",", item.Measures.Select(measure => measure.Value))));
    }

    public static string DetailText(LivePreviewRow row, int plannedItemCount = 0)
    {
        StringBuilder text = new();
        text.Append(RowLabel(row)).Append(" · ").AppendLine(StateLine(row, plannedItemCount));
        text.AppendLine("（速報値: AIまたはローカル計算が返した生の定量値です。配点とFinalScoreはExcel数式が計算するため、最終評点ではありません。）");
        if (row.Items.IsDefaultOrEmpty)
        {
            text.AppendLine();
            text.AppendLine(row.State == LivePreviewRowState.Waiting
                ? "この行はまだ評価を開始していません。"
                : "この行は評価を開始しないまま終了しました。");
        }
        for (int index = 0; index < row.Items.Length; index++)
        {
            LivePreviewItem item = row.Items[index];
            text.AppendLine();
            text.Append("■ 項目 ")
                .Append((index + 1).ToString(CultureInfo.InvariantCulture))
                .Append(" / ")
                .Append(row.Items.Length.ToString(CultureInfo.InvariantCulture))
                .Append(" · ")
                .AppendLine(item.Title);
            text.Append("状態: ").AppendLine(StatusText(item));
            text.AppendLine("対象の文字列:");
            if (!item.TextAvailable)
            {
                text.AppendLine(TextNotStoredText);
            }
            else if (item.Cells.IsDefaultOrEmpty)
            {
                text.AppendLine(NoValueMark);
            }
            else
            {
                foreach (LivePreviewCell cell in item.Cells)
                {
                    text.Append("[列 ").Append(cell.ColumnId).Append(" · ")
                        .Append(cell.IsPrimary ? "主回答" : "補助").AppendLine("]");
                    text.AppendLine(cell.Value);
                }
            }

            if (item.Kind == LivePreviewItemKind.Similarity && item.TextAvailable)
            {
                text.AppendLine("比較文字列（参照回答）:");
                text.AppendLine(string.IsNullOrEmpty(item.ComparisonText) ? NoValueMark : item.ComparisonText);
            }

            text.AppendLine("Prompt:");
            if (!item.TextAvailable)
            {
                text.AppendLine(TextNotStoredText);
            }
            else if (item.Kind == LivePreviewItemKind.Similarity)
            {
                text.AppendLine(NoPromptText);
            }
            else
            {
                text.AppendLine(string.IsNullOrEmpty(item.PromptText) ? NoValueMark : item.PromptText);
            }

            text.AppendLine("定量化の結果（速報値）:");
            if (item.Measures.IsDefaultOrEmpty)
            {
                text.AppendLine(NoValueMark);
                continue;
            }

            foreach (LivePreviewMeasure measure in item.Measures)
            {
                text.Append(measure.Label).Append(": ").Append(measure.Value)
                    .Append('（').Append(measure.RangeText).AppendLine("）");
                if (!string.IsNullOrEmpty(measure.Reason))
                {
                    text.Append("  理由: ").AppendLine(measure.Reason);
                }

                if (!string.IsNullOrEmpty(measure.Evidence))
                {
                    text.Append("  根拠: ").AppendLine(measure.Evidence);
                }
            }
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Applies the per-text limit and appends the closed truncation note.</summary>
    public static string Limit(string? value, int limit)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= limit)
        {
            return value;
        }

        int cut = limit;
        if (char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return string.Concat(
            value[..cut],
            "…（全 ",
            value.Length.ToString("N0", CultureInfo.InvariantCulture),
            " 文字中、先頭 ",
            limit.ToString("N0", CultureInfo.InvariantCulture),
            " 文字を表示）");
    }

    public static LivePreviewRow Limited(LivePreviewRow row, int limit) =>
        row with
        {
            Items = row.Items.Select(item => item with
            {
                Cells = item.Cells.Select(cell => cell with { Value = Limit(cell.Value, limit) }).ToImmutableArray(),
                PromptText = item.PromptText is null ? null : Limit(item.PromptText, limit),
                ComparisonText = item.ComparisonText is null ? null : Limit(item.ComparisonText, limit),
                Measures = item.Measures.Select(measure => measure with
                {
                    Reason = Limit(measure.Reason, limit),
                    Evidence = Limit(measure.Evidence, limit),
                }).ToImmutableArray(),
            }).ToImmutableArray(),
        };

    public static long CharacterCount(LivePreviewRow row)
    {
        long total = 0;
        foreach (LivePreviewItem item in row.Items)
        {
            total += item.Title.Length + (item.PromptText?.Length ?? 0) + (item.ComparisonText?.Length ?? 0);
            foreach (LivePreviewCell cell in item.Cells)
            {
                total += cell.Value.Length;
            }

            foreach (LivePreviewMeasure measure in item.Measures)
            {
                total += measure.Label.Length + measure.Value.Length + measure.Reason.Length + measure.Evidence.Length;
            }
        }

        return total;
    }
}
