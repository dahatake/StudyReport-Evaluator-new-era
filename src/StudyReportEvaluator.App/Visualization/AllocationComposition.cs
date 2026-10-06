using System.Collections.Immutable;
using System.Globalization;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Scoring;

namespace StudyReportEvaluator.App.Visualization;

public enum AllocationSegmentKind
{
    Base,
    Special,
    Question,
}

/// <summary>One part of the 100-point allocation: base, special or one enabled question.</summary>
public sealed record AllocationSegment(AllocationSegmentKind Kind, string Key, string Code, string Label, decimal Points)
{
    public string KindText => Kind switch
    {
        AllocationSegmentKind.Base => "基礎配点",
        AllocationSegmentKind.Special => "固有配点",
        _ => "設問配点",
    };

    public string PointsText => ChartFormat.Exact(Points);

    public override string ToString() =>
        $"{nameof(AllocationSegment)} {{ Kind = {Kind}, Key = {Key}, Points = {Points.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

/// <summary>
/// The stacked-bar data of FR-067. The total and its validity come from <see cref="ScoringAllocationCalculator"/>,
/// the same calculation that the FR-012 validation uses, so the difference equals the validation's value.
/// </summary>
public sealed class AllocationComposition
{
    private AllocationComposition(
        ImmutableArray<AllocationSegment> segments,
        ScoringAllocationValidationResult validation,
        ChartSummary questionSummary)
    {
        Segments = segments;
        Total = validation.Total;
        IsValid = validation.IsValid;
        QuestionSummary = questionSummary;
    }

    public const decimal Target = 100m;

    public ImmutableArray<AllocationSegment> Segments { get; }

    /// <summary>Null when a value is out of range (e.g. a negative question points), as in FR-012.</summary>
    public decimal? Total { get; }

    public bool IsValid { get; }

    /// <summary>Total − 100 (e.g. −0.5 when the total falls short by 0.5); null when no total exists.</summary>
    public decimal? Difference => Total is decimal total ? total - Target : null;

    /// <summary>The bar spans max(total, 100) so that both the excess and the 100 line are visible.</summary>
    public decimal Scale => Total is decimal total && total > Target ? total : Target;

    public ChartSummary QuestionSummary { get; }

    public static AllocationComposition Build(QuantificationDefinition definition, ScoringAllocationCalculator calculator)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(calculator);
        QuestionDefinition[] enabled = [.. definition.Questions.Where(question => question is not null && question.Enabled)];
        ScoringAllocationValidationResult validation = calculator.Validate(
            definition.BasePoints,
            definition.SpecialPoints,
            enabled.Select(question => question.Points));
        ImmutableArray<AllocationSegment>.Builder segments = ImmutableArray.CreateBuilder<AllocationSegment>();
        segments.Add(new AllocationSegment(AllocationSegmentKind.Base, "Base", "B", "基礎配点", definition.BasePoints));
        segments.Add(new AllocationSegment(AllocationSegmentKind.Special, "Special", "S", "固有配点", definition.SpecialPoints));
        for (int index = 0; index < enabled.Length; index++)
        {
            QuestionDefinition question = enabled[index];
            string label = ChartFormat.CollapseWhitespace(question.DisplayName);
            segments.Add(new AllocationSegment(
                AllocationSegmentKind.Question,
                question.Id,
                (index + 1).ToString(CultureInfo.InvariantCulture),
                label.Length > 0 ? label : question.Id,
                question.Points));
        }

        return new AllocationComposition(
            segments.ToImmutable(),
            validation,
            ResultChartCalculator.Summarize(enabled.Select(question => (decimal?)question.Points)));
    }
}
