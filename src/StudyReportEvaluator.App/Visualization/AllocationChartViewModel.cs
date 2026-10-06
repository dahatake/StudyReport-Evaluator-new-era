using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Scoring;

namespace StudyReportEvaluator.App.Visualization;

/// <summary>
/// The 「配点構成」 panel of 「2 採点設計」 (FR-067, FR-070, FR-071): a stacked bar of base, special and each
/// enabled question's points with a 100 reference line, the exact difference from 100 and a points table.
/// </summary>
public sealed class AllocationChartViewModel : UiObservableObject
{
    public const string NoTotalTitle = "配点構成を描けません";

    public const string NoTotalReason =
        "基礎配点・固有配点が 0〜100 の範囲外か、負の設問配点があるため、合計を計算できません。採点設計フォームの検証エラーを確認してください。";

    private readonly Action<string> selectQuestion;
    private AllocationComposition? composition;
    private ImmutableArray<AllocationSegmentItem> segments = [];
    private AllocationSegmentItem? focusedSegment;
    private string? selectedQuestionId;

    public AllocationChartViewModel(Action<string> selectQuestion)
    {
        this.selectQuestion = selectQuestion ?? throw new ArgumentNullException(nameof(selectQuestion));
    }

    public AllocationComposition? Composition => composition;

    public ImmutableArray<AllocationSegmentItem> Segments => segments;

    /// <summary>The bar exists whenever a definition exists and its total can be computed (FR-071).</summary>
    public bool CanDraw => composition?.Total is not null;

    public bool IsEmpty => !CanDraw;

    public string EmptyTitle => NoTotalTitle;

    public string EmptyReason => NoTotalReason;

    public decimal? Total => composition?.Total;

    /// <summary>Total − 100: the same value that the FR-012 validation reports.</summary>
    public decimal? Difference => composition?.Difference;

    public bool IsValid => composition?.IsValid == true;

    public string TotalText => Total is decimal total
        ? $"合計 {ChartFormat.Exact(total)} / 100"
        : "合計を計算できません";

    public string DifferenceText => Difference is decimal difference
        ? $"差分 {ChartFormat.SignedDifference(difference)}"
        : "差分 —";

    /// <summary>The state in words, never by colour alone.</summary>
    public string StateText => Difference switch
    {
        null => "配点に範囲外の値があります",
        < 0m => $"100 に {ChartFormat.Exact(-Difference.Value)} 足りません",
        > 0m => $"100 を {ChartFormat.Exact(Difference.Value)} 超えています",
        _ => IsValid ? "合計は 100 です" : "合計は 100 ですが、有効な設問がありません",
    };

    /// <summary>Where the 100 line sits on the bar (0〜1).</summary>
    public double ReferenceFraction => composition is { } current ? (double)(AllocationComposition.Target / current.Scale) : 1d;

    public string ReferenceLineName => $"基準線 100（{TotalText}・{DifferenceText}）";

    public string SummaryText
    {
        get
        {
            if (composition is not { } current)
            {
                return NoTotalTitle;
            }

            string questions = current.QuestionSummary.Describe("有効設問の配点", ChartFormat.Exact);
            return CanDraw
                ? $"{TotalText}・{DifferenceText}（{StateText}）。基礎配点 {current.Segments[0].PointsText}・固有配点 {current.Segments[1].PointsText}。{questions}。"
                : $"{NoTotalTitle}。{NoTotalReason}";
        }
    }

    public AllocationSegmentItem? FocusedSegment
    {
        get => focusedSegment;
        set => SetProperty(ref focusedSegment, value);
    }

    public void Update(QuantificationDefinition definition, ScoringAllocationCalculator calculator)
    {
        composition = AllocationComposition.Build(definition, calculator);
        string? focusedId = focusedSegment?.AutomationId;
        double scale = (double)composition.Scale;
        double start = 0d;
        List<AllocationSegmentItem> items = [];
        for (int index = 0; index < composition.Segments.Length; index++)
        {
            AllocationSegment segment = composition.Segments[index];
            double width = CanDraw && scale > 0d ? (double)segment.Points / scale : 0d;
            items.Add(new AllocationSegmentItem(segment, index, start, width,
                segment.Kind == AllocationSegmentKind.Question && segment.Key == selectedQuestionId));
            start += width;
        }

        segments = [.. items];
        focusedSegment = segments.FirstOrDefault(item => item.AutomationId == focusedId);
        OnPropertiesChanged(
            nameof(Composition),
            nameof(Segments),
            nameof(FocusedSegment),
            nameof(CanDraw),
            nameof(IsEmpty),
            nameof(Total),
            nameof(Difference),
            nameof(IsValid),
            nameof(TotalText),
            nameof(DifferenceText),
            nameof(StateText),
            nameof(ReferenceFraction),
            nameof(ReferenceLineName),
            nameof(SummaryText));
    }

    /// <summary>Follows the question selected in the design form (FR-069).</summary>
    public void SetSelectedQuestion(string? questionId)
    {
        selectedQuestionId = questionId;
        foreach (AllocationSegmentItem item in segments)
        {
            item.IsSelected = item.Kind == AllocationSegmentKind.Question && item.Key == questionId;
        }
    }

    /// <summary>Enter or click on a question segment selects that question in the design form.</summary>
    public void ActivateSegment(AllocationSegmentItem? item)
    {
        if (item is null || !segments.Contains(item))
        {
            return;
        }

        FocusedSegment = item;
        if (item.Kind == AllocationSegmentKind.Question)
        {
            selectQuestion(item.Key);
        }
    }

    public override string ToString() =>
        $"{nameof(AllocationChartViewModel)} {{ SegmentCount = {segments.Length.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed class AllocationSegmentItem : INotifyPropertyChanged
{
    private bool isSelected;

    internal AllocationSegmentItem(AllocationSegment segment, int index, double start, double width, bool isSelected)
    {
        Segment = segment;
        Index = index;
        StartFraction = start;
        WidthFraction = width;
        this.isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal AllocationSegment Segment { get; }

    public int Index { get; }

    public AllocationSegmentKind Kind => Segment.Kind;

    public string Key => Segment.Key;

    /// <summary>A short text code drawn on the segment: B, S, 1, 2, …</summary>
    public string Code => Segment.Code;

    public string Label => Segment.Label;

    public string KindText => Segment.KindText;

    public decimal Points => Segment.Points;

    public string PointsText => Segment.PointsText;

    public double StartFraction { get; }

    public double WidthFraction { get; }

    public string DisplayName => Kind == AllocationSegmentKind.Question ? $"設問 {Code}: {Label}" : Label;

    public bool IsSelected
    {
        get => isSelected;
        internal set
        {
            if (isSelected != value)
            {
                isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Marker)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
            }
        }
    }

    public string Marker => IsSelected ? "▶" : string.Empty;

    public string AccessibleName => $"{DisplayName}・配点 {PointsText}{(IsSelected ? "（選択中の設問）" : string.Empty)}";

    public string AutomationId => Kind == AllocationSegmentKind.Question
        ? $"DesignAllocationSegment-Question-{Key}"
        : $"DesignAllocationSegment-{Kind}";

    public string TableAutomationId => Kind == AllocationSegmentKind.Question
        ? $"DesignAllocationRow-Question-{Key}"
        : $"DesignAllocationRow-{Kind}";

    public override string ToString() => $"{nameof(AllocationSegmentItem)} {{ Key = {Key} }}";
}
