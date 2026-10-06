using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using StudyReportEvaluator.App.ViewModels;

namespace StudyReportEvaluator.App.Visualization;

public enum ResultsChartKind
{
    Histogram,
    Heatmap,
    Similarity,
}

/// <summary>What a chart may ask of the result screen (FR-069). Charts never start AI calls or file writes (FR-071).</summary>
public interface IResultsChartActions
{
    /// <summary>Filters the result list (and the heatmap and similarity list) to the given rows.</summary>
    void ApplyRowFilter(ResultsRowFilter filter);

    void ClearRowFilter();

    /// <summary>Selects the row (and the question's first criterion, if given) and opens its detail.</summary>
    void OpenRow(int sourceRow, string? questionId);
}

/// <summary>
/// The 「図」 panel of 「4 結果」 (FR-066, FR-069, FR-070, FR-071): a Final_Score histogram, a row × question
/// heatmap and a similarity list of the current result, with a table alternative and a text summary.
/// It only shows Excel row numbers, snapshot question text and numeric values (NFR-SEC-006).
/// </summary>
public sealed class ResultsChartsViewModel : UiObservableObject
{
    public const string NoResultTitle = "結果がありません";

    public const string NoRunReason =
        "定量化の実行が完了（または一部完了）すると、この結果の図を表示できます。図は追加のAI送信やファイル保存をしません。";

    public const string NoRowsReason =
        "この実行結果には対象の学生行がありません（0 行）。結果一覧と実行の状態を確認してください。図は追加のAI送信やファイル保存をしません。";

    public const string SimilarityNotice = "類似度は不正行為の証明ではありません。";

    public const string ProvisionalLabel = "暫定";

    public const string ProvisionalText = "暫定: 実行中の速報値です。完了後の結果とは異なる場合があります。";

    public const string BinBoundaryNote = "区間は下限以上・上限未満（90〜100 は 100 を含む）。";

    private static readonly IReadOnlyList<string> Kinds = Array.AsReadOnly(["分布（ヒストグラム）", "ヒートマップ", "類似度一覧"]);

    private readonly IResultsChartActions actions;
    private readonly ViewModelCommand clearFilterCommand;
    private ResultChartDataSet? data;
    private ResultsRowFilter? filter;
    private int? selectedSourceRow;
    private string emptyReason = NoRunReason;
    private ResultsChartKind selectedKind;
    private bool showTable;
    private ImmutableArray<HistogramBinItem> histogramBins = [];
    private HistogramBinItem? focusedBin;
    private ImmutableArray<ChartQuestionItem> questions = [];
    private IReadOnlyList<HeatmapRowItem> heatmapRows = [];
    private ChartQuestionItem? selectedSimilarityQuestion;
    private IReadOnlyList<SimilarityEntryItem> similarityEntries = [];
    private SimilarityEntryItem? focusedSimilarityEntry;
    private string statusText = string.Empty;

    public ResultsChartsViewModel(IResultsChartActions actions)
    {
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        clearFilterCommand = new ViewModelCommand(_ => actions.ClearRowFilter(), _ => filter is not null);
    }

    public IReadOnlyList<string> KindNames => Kinds;

    public ResultsChartKind SelectedKind
    {
        get => selectedKind;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (SetProperty(ref selectedKind, value))
            {
                OnPropertiesChanged(nameof(SelectedKindIndex), nameof(IsHistogram), nameof(IsHeatmap), nameof(IsSimilarity),
                    nameof(CanShowTable), nameof(SummaryText));
                RaisePresentation();
            }
        }
    }

    public int SelectedKindIndex
    {
        get => (int)selectedKind;
        set
        {
            if (value >= 0 && value < Kinds.Count)
            {
                SelectedKind = (ResultsChartKind)value;
            }
        }
    }

    public bool IsHistogram => selectedKind == ResultsChartKind.Histogram;

    public bool IsHeatmap => selectedKind == ResultsChartKind.Heatmap;

    public bool IsSimilarity => selectedKind == ResultsChartKind.Similarity;

    /// <summary>「表で見る」: the numeric table instead of the graphic (FR-070). The similarity list is already a table.</summary>
    public bool ShowTable
    {
        get => showTable;
        set
        {
            if (SetProperty(ref showTable, value))
            {
                RaisePresentation();
            }
        }
    }

    public bool CanShowTable => !IsSimilarity;

    public bool HasData => data is { Rows.Length: > 0 };

    public bool IsEmpty => !HasData;

    public string EmptyTitle => NoResultTitle;

    public string EmptyReason => emptyReason;

    /// <summary>
    /// True only when the data come from the live preview (FR-046). The result screen shows completed or partial
    /// runs only, so this stays false there; the flag exists so that live data would always be marked 「暫定」.
    /// </summary>
    public bool IsProvisional => data?.IsProvisional == true;

    public bool ShowHistogramChart => HasData && IsHistogram && !ShowTable;

    public bool ShowHistogramTable => HasData && IsHistogram && ShowTable;

    public bool ShowHeatmapChart => HasData && IsHeatmap && !ShowTable;

    public bool ShowHeatmapTable => HasData && IsHeatmap && ShowTable;

    public bool ShowSimilarity => HasData && IsSimilarity;

    public bool IsFilterActive => filter is not null;

    public string FilterText => filter is { } current
        ? $"絞り込み中: {current.Label}（{ChartFormat.Count(current.Count)} 行）"
        : string.Empty;

    public ICommand ClearFilterCommand => clearFilterCommand;

    public int? SelectedSourceRow => selectedSourceRow;

    /// <summary>件数・平均・中央値・最小・最大・空欄件数 of the current representation; announced politely.</summary>
    public string SummaryText
    {
        get
        {
            if (data is not { Rows.Length: > 0 } current)
            {
                return $"{NoResultTitle}。{emptyReason}";
            }

            string text = selectedKind switch
            {
                ResultsChartKind.Histogram => ResultChartCalculator.Summarize(current.Rows.Select(row => row.FinalScore))
                    .Describe("最終点", ChartFormat.Exact) + "。",
                ResultsChartKind.Heatmap => HeatmapSummary(current),
                _ => SimilaritySummary(),
            };
            if (filter is { } active)
            {
                text += $" 絞り込み中: {active.Label}（{ChartFormat.Count(active.Count)} 行）。";
            }

            return IsProvisional ? $"{ProvisionalLabel}。{text}" : text;
        }
    }

    /// <summary>The latest action result (e.g. an empty bin), announced politely.</summary>
    public string StatusText
    {
        get => statusText;
        private set
        {
            if (SetProperty(ref statusText, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => StatusText.Length > 0;

    public ImmutableArray<HistogramBinItem> HistogramBins => histogramBins;

    /// <summary>The keyboard position in the histogram; moving it does not filter.</summary>
    public HistogramBinItem? FocusedBin
    {
        get => focusedBin;
        set => SetProperty(ref focusedBin, value);
    }

    public ImmutableArray<ChartQuestionItem> Questions => questions;

    /// <summary>Rows of the heatmap: the filtered rows in ascending Excel row order.</summary>
    public IReadOnlyList<HeatmapRowItem> HeatmapRows => heatmapRows;

    public int SelectedHeatmapRowIndex
    {
        get
        {
            if (selectedSourceRow is not int row)
            {
                return -1;
            }

            for (int index = 0; index < heatmapRows.Count; index++)
            {
                if (heatmapRows[index].SourceRow == row)
                {
                    return index;
                }
            }

            return -1;
        }
    }

    public ChartQuestionItem? SelectedSimilarityQuestion
    {
        get => selectedSimilarityQuestion;
        set
        {
            if (value is not null && !questions.Contains(value))
            {
                return;
            }

            if (SetProperty(ref selectedSimilarityQuestion, value))
            {
                RebuildSimilarity(force: true);
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public IReadOnlyList<SimilarityEntryItem> SimilarityEntries => similarityEntries;

    /// <summary>The keyboard position in the similarity list; moving it does not open the detail.</summary>
    public SimilarityEntryItem? FocusedSimilarityEntry
    {
        get => focusedSimilarityEntry;
        set => SetProperty(ref focusedSimilarityEntry, value);
    }

    public string SimilarityNoticeText => SimilarityNotice;

    public string HistogramLegend => "▶ = 一覧を絞り込み中の区間・斜線 = 空欄（技術的失敗）。" + BinBoundaryNote;

    public string HeatmapLegend =>
        $"数値と下の棒 = 得点率（獲得点 ÷ 配点）・{ChartFormat.BlankSymbol} と斜線 = {ChartFormat.BlankText}・{ChartFormat.NoPointsSymbol} = 配点 0・▶ = 選択中の行。矢印キーで移動、Enter で詳細。";

    public string ProvisionalLabelText => ProvisionalLabel;

    public string ProvisionalDescription => ProvisionalText;

    /// <summary>Replaces the chart data, the row filter and the selected row of the result screen.</summary>
    public void Update(ResultChartDataSet? dataSet, ResultsRowFilter? rowFilter, int? selectedRow, string? reason = null)
    {
        bool dataChanged = !ReferenceEquals(data, dataSet);
        bool filterChanged = !ReferenceEquals(filter, rowFilter);
        data = dataSet;
        filter = rowFilter;
        emptyReason = dataSet is null ? reason ?? NoRunReason : NoRowsReason;
        if (dataChanged)
        {
            ImmutableArray<ChartQuestionItem> next = dataSet is null
                ? []
                : [.. dataSet.Questions.Select((question, index) => new ChartQuestionItem(index, question))];
            if (!next.Select(item => item.Question).SequenceEqual(questions.Select(item => item.Question)))
            {
                string? previousId = selectedSimilarityQuestion?.Id;
                questions = next;
                selectedSimilarityQuestion = next.FirstOrDefault(item => item.Id == previousId) ?? next.FirstOrDefault();
                OnPropertiesChanged(nameof(Questions), nameof(SelectedSimilarityQuestion));
            }
        }

        if (dataChanged || filterChanged)
        {
            StatusText = string.Empty;
            RebuildHistogram();
            RebuildHeatmap();
            RebuildSimilarity(force: false);
        }

        SetSelection(selectedRow);
        OnPropertiesChanged(
            nameof(HasData),
            nameof(IsEmpty),
            nameof(EmptyReason),
            nameof(IsProvisional),
            nameof(IsFilterActive),
            nameof(FilterText),
            nameof(SummaryText));
        RaisePresentation();
        clearFilterCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Follows the selected row of the list and detail (FR-069).</summary>
    public void SetSelection(int? sourceRow)
    {
        if (selectedSourceRow == sourceRow)
        {
            return;
        }

        int? previous = selectedSourceRow;
        selectedSourceRow = sourceRow;
        foreach (HeatmapRowItem row in heatmapRows)
        {
            if (row.SourceRow == previous || row.SourceRow == sourceRow)
            {
                row.IsSelected = row.SourceRow == sourceRow;
            }
        }

        foreach (SimilarityEntryItem entry in similarityEntries)
        {
            if (entry.SourceRow == previous || entry.SourceRow == sourceRow)
            {
                entry.IsSelected = entry.SourceRow == sourceRow;
            }
        }

        SimilarityEntryItem? selectedEntry = similarityEntries.FirstOrDefault(entry => entry.SourceRow == sourceRow);
        if (selectedEntry is not null)
        {
            FocusedSimilarityEntry = selectedEntry;
        }

        OnPropertiesChanged(nameof(SelectedSourceRow), nameof(SelectedHeatmapRowIndex));
    }

    /// <summary>Enter or click on a bin: filter the list to the bin's rows (FR-069).</summary>
    public void ActivateBin(HistogramBinItem? bin)
    {
        if (bin is null || !histogramBins.Contains(bin))
        {
            return;
        }

        FocusedBin = bin;
        if (bin.Count == 0)
        {
            StatusText = $"{bin.FilterLabel}に該当する行はありません。絞り込みは変わっていません。";
            return;
        }

        StatusText = $"{bin.FilterLabel}の {ChartFormat.Count(bin.Count)} 行に絞り込みました。";
        actions.ApplyRowFilter(new ResultsRowFilter(bin.FilterLabel, bin.SourceRows));
    }

    /// <summary>Enter or click on a heatmap cell: select the row and open its detail.</summary>
    public void OpenCell(HeatmapRowItem? row, int columnIndex)
    {
        if (row is null || !heatmapRows.Contains(row))
        {
            return;
        }

        string? questionId = columnIndex >= 0 && columnIndex < questions.Length ? questions[columnIndex].Id : null;
        actions.OpenRow(row.SourceRow, questionId);
    }

    /// <summary>Enter or click on a similarity row: select the row and open its detail.</summary>
    public void ActivateSimilarityEntry(SimilarityEntryItem? entry)
    {
        if (entry is null || !similarityEntries.Contains(entry))
        {
            return;
        }

        FocusedSimilarityEntry = entry;
        actions.OpenRow(entry.SourceRow, selectedSimilarityQuestion?.Id);
    }

    public override string ToString() =>
        $"{nameof(ResultsChartsViewModel)} {{ Kind = {selectedKind}, HasData = {HasData}, Content = <redacted> }}";

    internal ResultChartQuestion QuestionAt(int index) => questions[index].Question;

    private IEnumerable<ResultChartRow> FilteredRows() => data is null
        ? []
        : filter is null ? data.Rows : data.Rows.Where(row => filter.Contains(row.SourceRow));

    private void RebuildHistogram()
    {
        string? focusedKey = focusedBin?.Key;
        if (data is null)
        {
            histogramBins = [];
        }
        else
        {
            ScoreHistogram histogram = ResultChartCalculator.BuildHistogram(data.Rows);
            int maximum = histogram.MaximumCount;
            histogramBins =
            [
                .. histogram.Bins.Select(bin => new HistogramBinItem(
                    bin.Index.ToString(CultureInfo.InvariantCulture),
                    bin.Label,
                    $"{ChartFormat.Exact(bin.Lower)} 点以上 {ChartFormat.Exact(bin.Upper)} 点{(bin.IncludesUpper ? "以下" : "未満")}",
                    $"最終点 {bin.Label} 点",
                    bin.SourceRows,
                    maximum,
                    filter)),
                new HistogramBinItem("Blank", "空欄", ChartFormat.BlankText, $"最終点が{ChartFormat.BlankText}", histogram.BlankRows, maximum, filter),
            ];
        }

        focusedBin = histogramBins.FirstOrDefault(bin => bin.Key == focusedKey)
            ?? histogramBins.FirstOrDefault(bin => bin.IsActiveFilter);
        OnPropertiesChanged(nameof(HistogramBins), nameof(FocusedBin));
    }

    private void RebuildHeatmap()
    {
        ResultChartRow[] rows = [.. FilteredRows()];
        if (heatmapRows.Count == rows.Length
            && heatmapRows.Select(row => row.SourceRow).SequenceEqual(rows.Select(row => row.SourceRow)))
        {
            // Same rows: update values in place so the scroll position and focus survive a recomputation.
            for (int index = 0; index < rows.Length; index++)
            {
                heatmapRows[index].Update(this, rows[index]);
            }

            return;
        }

        heatmapRows = Array.AsReadOnly(rows.Select(row => new HeatmapRowItem(this, row, row.SourceRow == selectedSourceRow)).ToArray());
        OnPropertiesChanged(nameof(HeatmapRows), nameof(SelectedHeatmapRowIndex));
    }

    private void RebuildSimilarity(bool force)
    {
        if (selectedSimilarityQuestion is not { } question)
        {
            similarityEntries = [];
            focusedSimilarityEntry = null;
            OnPropertiesChanged(nameof(SimilarityEntries), nameof(FocusedSimilarityEntry));
            return;
        }

        ImmutableArray<SimilarityEntry> ranked = ResultChartCalculator.RankSimilarity(FilteredRows(), question.Index);
        if (!force && ranked.SequenceEqual(similarityEntries.Select(entry => entry.Entry)))
        {
            return;
        }

        int? focusedRow = focusedSimilarityEntry?.SourceRow;
        similarityEntries = Array.AsReadOnly(ranked
            .Select(entry => new SimilarityEntryItem(entry, question.Id, entry.SourceRow == selectedSourceRow))
            .ToArray());
        focusedSimilarityEntry = similarityEntries.FirstOrDefault(entry => entry.SourceRow == (selectedSourceRow ?? focusedRow))
            ?? similarityEntries.FirstOrDefault(entry => entry.SourceRow == focusedRow);
        OnPropertiesChanged(nameof(SimilarityEntries), nameof(FocusedSimilarityEntry));
    }

    private string HeatmapSummary(ResultChartDataSet current)
    {
        ResultChartRow[] rows = [.. FilteredRows()];
        List<decimal?> rates = new(rows.Length * Math.Max(1, current.Questions.Length));
        foreach (ResultChartRow row in rows)
        {
            for (int index = 0; index < current.Questions.Length; index++)
            {
                HeatmapCellValue cell = ResultChartCalculator.Cell(row.Questions[index].Earned, current.Questions[index].Points);
                if (cell.Kind != HeatmapCellKind.NoPoints)
                {
                    rates.Add(ResultChartCalculator.DisplayedRate(cell));
                }
            }
        }

        string subject = $"得点率（{ChartFormat.Count(rows.Length)} 行 × {ChartFormat.Count(current.Questions.Length)} 設問）";
        return ResultChartCalculator.Summarize(rates).Describe(subject, ChartFormat.Rate) + "。";
    }

    private string SimilaritySummary()
    {
        string subject = selectedSimilarityQuestion is { } question
            ? $"設問 {question.Number} の参照回答との類似度"
            : "参照回答との類似度";
        return ResultChartCalculator.Summarize(similarityEntries.Select(entry => entry.Entry.Similarity))
            .Describe(subject, value => ChartFormat.Similarity(value)) + "。" + SimilarityNotice;
    }

    private void RaisePresentation() => OnPropertiesChanged(
        nameof(ShowHistogramChart),
        nameof(ShowHistogramTable),
        nameof(ShowHeatmapChart),
        nameof(ShowHeatmapTable),
        nameof(ShowSimilarity));
}

/// <summary>An enabled snapshot question as a heatmap column and a similarity-list choice.</summary>
public sealed class ChartQuestionItem
{
    internal ChartQuestionItem(int index, ResultChartQuestion question)
    {
        Index = index;
        Question = question;
    }

    internal ResultChartQuestion Question { get; }

    public int Index { get; }

    public int Number => Index + 1;

    public string Id => Question.Id;

    public string Label => Question.Label;

    public string ShortLabel => Number.ToString(CultureInfo.InvariantCulture);

    public string DisplayText => $"設問 {Number}: {Label}";

    public string AccessibleName => $"設問 {Number}: {Label}";

    public string AutomationId => $"ResultsHeatmapColumn-{Id}";

    public string TableAutomationId => $"ResultsHeatmapTableColumn-{Id}";

    public override string ToString() => $"{nameof(ChartQuestionItem)} {{ Id = {Id}, Content = <redacted> }}";
}

public sealed class HistogramBinItem
{
    internal HistogramBinItem(
        string key,
        string label,
        string rangeText,
        string filterLabel,
        ImmutableArray<int> sourceRows,
        int maximumCount,
        ResultsRowFilter? activeFilter)
    {
        Key = key;
        Label = label;
        RangeText = rangeText;
        FilterLabel = filterLabel;
        SourceRows = sourceRows;
        Fraction = maximumCount > 0 ? (double)sourceRows.Length / maximumCount : 0d;
        IsActiveFilter = activeFilter is not null && string.Equals(activeFilter.Label, filterLabel, StringComparison.Ordinal);
    }

    /// <summary>"0"〜"9" for the bins, "Blank" for blank Final_Score.</summary>
    public string Key { get; }

    /// <summary>Short label drawn by the bar: "0〜10", …, "空欄".</summary>
    public string Label { get; }

    /// <summary>"0 点以上 10 点未満", …, "90 点以上 100 点以下", or the blank text.</summary>
    public string RangeText { get; }

    public string FilterLabel { get; }

    public ImmutableArray<int> SourceRows { get; }

    public int Count => SourceRows.Length;

    public string CountText => $"{ChartFormat.Count(Count)} 件";

    public double Fraction { get; }

    public bool IsBlank => Key == "Blank";

    public bool IsActiveFilter { get; }

    /// <summary>The non-colour mark of the bin that filters the list.</summary>
    public string Marker => IsActiveFilter ? "▶" : string.Empty;

    public string StateText => IsActiveFilter ? "絞り込み中" : string.Empty;

    public string AccessibleName =>
        $"{(IsBlank ? "最終点が" + RangeText : "最終点 " + RangeText)}: {CountText}{(IsActiveFilter ? "（一覧を絞り込み中）" : string.Empty)}";

    public string AutomationId => $"ResultsHistogramBin-{Key}";

    public string TableAutomationId => $"ResultsHistogramTableRow-{Key}";

    public override string ToString() => $"{nameof(HistogramBinItem)} {{ Key = {Key}, Count = {Count.ToString(CultureInfo.InvariantCulture)} }}";
}

/// <summary>One heatmap row. Cell texts are computed on demand so that 20,000 rows stay light.</summary>
public sealed class HeatmapRowItem : INotifyPropertyChanged
{
    private ResultsChartsViewModel owner;
    private ResultChartRow row;
    private bool isSelected;

    internal HeatmapRowItem(ResultsChartsViewModel owner, ResultChartRow row, bool isSelected)
    {
        this.owner = owner;
        this.row = row;
        this.isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int SourceRow => row.SourceRow;

    public int CellCount => row.Questions.Length;

    public bool IsSelected
    {
        get => isSelected;
        internal set
        {
            if (isSelected != value)
            {
                isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    /// <summary>The Excel row with a non-colour mark for the selected row.</summary>
    public string HeaderText => IsSelected ? $"▶{ChartFormat.Row(SourceRow)}" : ChartFormat.Row(SourceRow);

    public string HeaderAccessibleName => $"行 {ChartFormat.Row(SourceRow)}{(IsSelected ? "（選択中）" : string.Empty)}";

    public HeatmapCellValue Cell(int column) =>
        ResultChartCalculator.Cell(row.Questions[column].Earned, owner.QuestionAt(column).Points);

    /// <summary>「行 {row}・{設問文}・得点率 {rate}」 or 「行 {row}・{設問文}・空欄（技術的失敗）」.</summary>
    public string CellAccessibleName(int column) =>
        $"行 {ChartFormat.Row(SourceRow)}・{owner.QuestionAt(column).Label}・{Cell(column).AccessibleValue}";

    public string CellAutomationId(int column, bool table) =>
        $"ResultsHeatmap{(table ? "Value" : "Cell")}-{ChartFormat.Row(SourceRow)}-{owner.QuestionAt(column).Id}";

    public override string ToString() => $"{nameof(HeatmapRowItem)} {{ SourceRow = {SourceRow.ToString(CultureInfo.InvariantCulture)} }}";

    internal void Update(ResultsChartsViewModel nextOwner, ResultChartRow next)
    {
        owner = nextOwner;
        row = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

public sealed class SimilarityEntryItem : INotifyPropertyChanged
{
    private readonly string questionId;
    private bool isSelected;

    internal SimilarityEntryItem(SimilarityEntry entry, string questionId, bool isSelected)
    {
        Entry = entry;
        this.questionId = questionId;
        this.isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal SimilarityEntry Entry { get; }

    public int SourceRow => Entry.SourceRow;

    public string SourceRowText => IsSelected ? $"▶{ChartFormat.Row(SourceRow)}" : ChartFormat.Row(SourceRow);

    public string SimilarityText => ChartFormat.Similarity(Entry.Similarity);

    public string PenaltyText => ChartFormat.Exact(Entry.Penalty);

    public string PeerMaxText => ChartFormat.Similarity(Entry.PeerMax);

    public string PeerRowText => Entry.PeerRow is int peer ? $"行 {ChartFormat.Row(peer)}" : ChartFormat.MissingText;

    public bool IsSelected
    {
        get => isSelected;
        internal set
        {
            if (isSelected != value)
            {
                isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SourceRowText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
            }
        }
    }

    public string AccessibleName =>
        $"行 {ChartFormat.Row(SourceRow)}{(IsSelected ? "（選択中）" : string.Empty)}・参照回答との類似度 {SimilarityText}・類似減点 {PenaltyText}・他学生との最大類似度 {PeerMaxText}・相手 {PeerRowText}";

    public string AutomationId => $"ResultsSimilarity-{ChartFormat.Row(SourceRow)}-{questionId}";

    public override string ToString() => $"{nameof(SimilarityEntryItem)} {{ SourceRow = {SourceRow.ToString(CultureInfo.InvariantCulture)} }}";
}
