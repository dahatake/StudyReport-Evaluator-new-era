using System.Collections.ObjectModel;
using System.Globalization;
using StudyReportEvaluator.App.Workflow;

namespace StudyReportEvaluator.App.ViewModels;

/// <summary>One student row of the live preview; its number is the workbook row number.</summary>
public sealed class LivePreviewRowViewModel : UiObservableObject
{
    private LivePreviewRow row;
    private readonly int plannedItemCount;

    internal LivePreviewRowViewModel(int sourceRowNumber, int plannedItemCount)
    {
        this.plannedItemCount = plannedItemCount;
        row = new LivePreviewRow { SourceRowNumber = sourceRowNumber, State = LivePreviewRowState.Waiting };
    }

    public int SourceRowNumber => row.SourceRowNumber;

    public LivePreviewRowState State => row.State;

    public LivePreviewRow Row => row;

    public string Label => LivePreviewFormatter.RowLabel(row);

    public string StateLine => LivePreviewFormatter.StateLine(row, plannedItemCount);

    public string ValueSummary => LivePreviewFormatter.ValueSummary(row);

    public string AutomationName => $"{Label}、{StateLine}、速報値 {ValueSummary}";

    public string DetailText => LivePreviewFormatter.DetailText(row, plannedItemCount);

    internal void Apply(LivePreviewRow next)
    {
        row = next;
        OnPropertiesChanged(
            nameof(State),
            nameof(Row),
            nameof(StateLine),
            nameof(ValueSummary),
            nameof(AutomationName),
            nameof(DetailText));
    }

    public override string ToString() =>
        $"{nameof(LivePreviewRowViewModel)} {{ SourceRowNumber = {SourceRowNumber.ToString(CultureInfo.InvariantCulture)}, State = {State}, Content = <redacted> }}";
}

/// <summary>
/// Screen-only list of every student row with the text, prompt and raw quantified values of the
/// current run. Nothing here is persisted or logged; the content lives until the next run,
/// an input/definition change, or disposal.
/// </summary>
public sealed class LiveQuantificationPreviewViewModel : UiObservableObject
{
    public const string EmptyDetailText = "実行を開始すると、Excel の行ごとの速報値をここに表示します。";

    public const long RetainedCharacterBudget = 32_000_000;

    private ObservableCollection<LivePreviewRowViewModel> rows = [];
    private readonly SortedSet<int> runningRows = [];
    private int firstRow;
    private int lastRow = -1;
    private int plannedItemCount;
    private long retainedCharacters;
    private LivePreviewRowViewModel? selectedRow;
    private LivePreviewRowViewModel? latestRow;

    public ObservableCollection<LivePreviewRowViewModel> Rows => rows;

    public int RowCount => rows.Count;

    public bool IsEmpty => rows.Count == 0;

    public string HeadingText =>
        $"速報値（Excel 行ごと・{RowCount.ToString(CultureInfo.InvariantCulture)} 行）";

    public long RetainedCharacters => retainedCharacters;

    public LivePreviewRowViewModel? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (SetProperty(ref selectedRow, value))
            {
                OnPropertyChanged(nameof(DetailText));
            }
        }
    }

    /// <summary>The pinned row, else the lowest-numbered row still being evaluated, else the last updated row, else the first row.</summary>
    public LivePreviewRowViewModel? DisplayedRow => selectedRow
        ?? (runningRows.Count > 0 ? RowAt(runningRows.Min) : null)
        ?? latestRow
        ?? (rows.Count > 0 ? rows[0] : null);

    public string DetailText => DisplayedRow?.DetailText ?? EmptyDetailText;

    internal void Reset()
    {
        if (rows.Count == 0 && retainedCharacters == 0 && selectedRow is null && latestRow is null)
        {
            return;
        }

        rows = [];
        runningRows.Clear();
        firstRow = 0;
        lastRow = -1;
        plannedItemCount = 0;
        retainedCharacters = 0;
        selectedRow = null;
        latestRow = null;
        NotifyAll();
    }

    internal void Apply(LivePreviewUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.LastDataRow < update.FirstDataRow)
        {
            return;
        }

        if (rows.Count == 0 || update.FirstDataRow != firstRow || update.LastDataRow != lastRow)
        {
            Rebuild(update);
        }

        if (update.Row is not { } incoming)
        {
            return;
        }

        LivePreviewRowViewModel? target = RowAt(incoming.SourceRowNumber);
        if (target is null)
        {
            return;
        }

        LivePreviewRow stored = LivePreviewFormatter.Limited(
            incoming,
            retainedCharacters > RetainedCharacterBudget
                ? LivePreviewFormatter.CompactTextLimit
                : LivePreviewFormatter.DefaultTextLimit);
        retainedCharacters += LivePreviewFormatter.CharacterCount(stored)
            - LivePreviewFormatter.CharacterCount(target.Row);
        Store(target, stored);
    }

    /// <summary>Marks rows that did not finish once the run has ended.</summary>
    internal void Finish()
    {
        foreach (LivePreviewRowViewModel row in rows)
        {
            LivePreviewRowState? next = row.State switch
            {
                LivePreviewRowState.Running => LivePreviewRowState.Interrupted,
                LivePreviewRowState.Waiting => LivePreviewRowState.Unprocessed,
                _ => null,
            };
            if (next is { } state)
            {
                Store(row, row.Row with { State = state });
            }
        }

        runningRows.Clear();
        OnPropertyChanged(nameof(DetailText));
    }

    public override string ToString() =>
        $"{nameof(LiveQuantificationPreviewViewModel)} {{ Rows = {RowCount.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";

    private void Store(LivePreviewRowViewModel target, LivePreviewRow stored)
    {
        target.Apply(stored);
        if (stored.State == LivePreviewRowState.Running)
        {
            runningRows.Add(stored.SourceRowNumber);
        }
        else
        {
            runningRows.Remove(stored.SourceRowNumber);
        }

        latestRow = target;
        if (selectedRow is null || ReferenceEquals(selectedRow, target))
        {
            OnPropertyChanged(nameof(DetailText));
        }
    }

    private LivePreviewRowViewModel? RowAt(int sourceRowNumber)
    {
        int index = sourceRowNumber - firstRow;
        return index >= 0 && index < rows.Count ? rows[index] : null;
    }

    private void Rebuild(LivePreviewUpdate update)
    {
        firstRow = update.FirstDataRow;
        lastRow = update.LastDataRow;
        plannedItemCount = update.ItemsPerRow;
        List<LivePreviewRowViewModel> created = new(lastRow - firstRow + 1);
        for (int number = firstRow; number <= lastRow; number++)
        {
            created.Add(new LivePreviewRowViewModel(number, plannedItemCount));
        }

        rows = new ObservableCollection<LivePreviewRowViewModel>(created);
        runningRows.Clear();
        retainedCharacters = 0;
        selectedRow = null;
        latestRow = null;
        NotifyAll();
    }

    private void NotifyAll() => OnPropertiesChanged(
        nameof(Rows),
        nameof(RowCount),
        nameof(IsEmpty),
        nameof(HeadingText),
        nameof(RetainedCharacters),
        nameof(SelectedRow),
        nameof(DisplayedRow),
        nameof(DetailText));
}
