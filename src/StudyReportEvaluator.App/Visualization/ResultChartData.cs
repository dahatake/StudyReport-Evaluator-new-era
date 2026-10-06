using System.Collections.Immutable;
using System.Globalization;

namespace StudyReportEvaluator.App.Visualization;

/// <summary>An enabled question of the run snapshot, in snapshot order. Label is the snapshot question text.</summary>
public sealed record ResultChartQuestion(string Id, string Label, decimal Points)
{
    public override string ToString() =>
        $"{nameof(ResultChartQuestion)} {{ Id = {Id}, Points = {Points.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

/// <summary>
/// The numeric values of one row and question that the result screen already computes:
/// Question_Earned, the similarity to the reference answer, the similarity penalty and the peer maximum.
/// </summary>
public readonly record struct ResultChartQuestionValue(
    decimal? Earned,
    decimal? Similarity,
    decimal? Penalty,
    decimal? PeerMax,
    int? PeerRow);

/// <summary>One target row of the run: only the Excel row number and numeric values (NFR-SEC-006).</summary>
public sealed class ResultChartRow
{
    public ResultChartRow(int sourceRow, decimal? finalScore, IEnumerable<ResultChartQuestionValue> questions)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceRow, 1);
        ArgumentNullException.ThrowIfNull(questions);
        SourceRow = sourceRow;
        FinalScore = finalScore;
        Questions = [.. questions];
    }

    public int SourceRow { get; }

    public decimal? FinalScore { get; }

    /// <summary>Values in the order of <see cref="ResultChartDataSet.Questions"/>.</summary>
    public ImmutableArray<ResultChartQuestionValue> Questions { get; }

    public override string ToString() =>
        $"{nameof(ResultChartRow)} {{ SourceRow = {SourceRow.ToString(CultureInfo.InvariantCulture)} }}";
}

/// <summary>The chart input of the current result: its snapshot questions and its target rows.</summary>
public sealed class ResultChartDataSet
{
    public ResultChartDataSet(
        IEnumerable<ResultChartQuestion> questions,
        IEnumerable<ResultChartRow> rows,
        bool isProvisional = false)
    {
        ArgumentNullException.ThrowIfNull(questions);
        ArgumentNullException.ThrowIfNull(rows);
        Questions = [.. questions];
        Rows = [.. rows.OrderBy(row => row.SourceRow)];
        if (Rows.Any(row => row.Questions.Length != Questions.Length))
        {
            throw new ArgumentException("Every row must have one value per question.", nameof(rows));
        }

        IsProvisional = isProvisional;
    }

    public ImmutableArray<ResultChartQuestion> Questions { get; }

    /// <summary>Target rows in ascending Excel row order.</summary>
    public ImmutableArray<ResultChartRow> Rows { get; }

    /// <summary>True only while the data are live preview values (FR-046); charts then say 「暫定」.</summary>
    public bool IsProvisional { get; }

    public override string ToString() =>
        $"{nameof(ResultChartDataSet)} {{ QuestionCount = {Questions.Length.ToString(CultureInfo.InvariantCulture)}, RowCount = {Rows.Length.ToString(CultureInfo.InvariantCulture)}, IsProvisional = {IsProvisional} }}";
}

/// <summary>A filter of the result list by Excel row numbers, e.g. the rows of one histogram bin (FR-069).</summary>
public sealed class ResultsRowFilter
{
    public ResultsRowFilter(string label, IEnumerable<int> sourceRows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(sourceRows);
        Label = label;
        SourceRows = [.. sourceRows];
    }

    public string Label { get; }

    public ImmutableHashSet<int> SourceRows { get; }

    public int Count => SourceRows.Count;

    public bool Contains(int sourceRow) => SourceRows.Contains(sourceRow);

    public override string ToString() =>
        $"{nameof(ResultsRowFilter)} {{ Count = {Count.ToString(CultureInfo.InvariantCulture)} }}";
}
