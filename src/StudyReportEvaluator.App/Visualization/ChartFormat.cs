using System.Globalization;

namespace StudyReportEvaluator.App.Visualization;

/// <summary>
/// Display formatting shared by every chart element, its table alternative and its accessible
/// name (FR-070): one function per kind of value, so the table always shows the chart's value.
/// </summary>
public static class ChartFormat
{
    public const string BlankText = "空欄（技術的失敗）";

    /// <summary>The distinct non-colour symbol of a blank (technical failure) cell.</summary>
    public const string BlankSymbol = "×";

    public const string NoPointsText = "配点 0（得点率なし）";

    public const string NoPointsSymbol = "－";

    public const string MissingText = "—";

    /// <summary>Exact decimal value without exponent or trailing zeros.</summary>
    public static string Exact(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    public static string Exact(decimal? value) => value is decimal number ? Exact(number) : MissingText;

    /// <summary>A score rate (Question_Earned ÷ Points) rounded to two decimals for display.</summary>
    public static string Rate(decimal rate) =>
        decimal.Round(rate, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A mean shown with at most two decimals.</summary>
    public static string Mean(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A similarity (0〜1) shown with at most four decimals.</summary>
    public static string Similarity(decimal? value) => value is decimal number
        ? decimal.Round(number, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture)
        : MissingText;

    public static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Row(int sourceRow) => sourceRow.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A signed exact difference: "−0.5" with U+2212 for a shortfall, "+0.5" for an excess, "0" when equal.
    /// </summary>
    public static string SignedDifference(decimal value) => value switch
    {
        < 0m => "−" + Exact(-value),
        > 0m => "+" + Exact(value),
        _ => "0",
    };

    /// <summary>Collapses whitespace runs to one half-width space and trims, as FR-045 does for question text.</summary>
    public static string CollapseWhitespace(string? text) => string.Join(
        ' ',
        (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
