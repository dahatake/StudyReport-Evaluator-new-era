using System.Collections.Immutable;
using System.Globalization;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workflow;

namespace StudyReportEvaluator.App.ViewModels;

public enum ResultsAnswerState
{
    Loading,
    Loaded,
    InputChanged,
    Unavailable,
}

public sealed class ResultsAnswerRequest
{
    public ResultsAnswerRequest(
        string inputPath,
        InputSnapshot expectedInput,
        string sourceSheet,
        int sourceRowNumber,
        IEnumerable<string> columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSheet);
        ArgumentNullException.ThrowIfNull(columns);
        if (sourceRowNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRowNumber));
        }

        InputPath = inputPath;
        ExpectedInput = expectedInput ?? throw new ArgumentNullException(nameof(expectedInput));
        SourceSheet = sourceSheet;
        SourceRowNumber = sourceRowNumber;
        Columns = columns
            .Select(column => column.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        if (Columns.IsEmpty)
        {
            throw new ArgumentException("At least one answer column is required.", nameof(columns));
        }
    }

    public string InputPath { get; }

    public InputSnapshot ExpectedInput { get; }

    public string SourceSheet { get; }

    public int SourceRowNumber { get; }

    public ImmutableArray<string> Columns { get; }

    public override string ToString() =>
        $"{nameof(ResultsAnswerRequest)} {{ SourceRowNumber = {SourceRowNumber.ToString(CultureInfo.InvariantCulture)}, ColumnCount = {Columns.Length.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed class ResultsAnswerReadResult
{
    private ResultsAnswerReadResult(
        ResultsAnswerState state,
        ImmutableDictionary<string, string?> cells)
    {
        State = state;
        Cells = cells;
    }

    public static ResultsAnswerReadResult InputChanged { get; } =
        new(ResultsAnswerState.InputChanged, ImmutableDictionary<string, string?>.Empty);

    public static ResultsAnswerReadResult Unavailable { get; } =
        new(ResultsAnswerState.Unavailable, ImmutableDictionary<string, string?>.Empty);

    public ResultsAnswerState State { get; }

    public ImmutableDictionary<string, string?> Cells { get; }

    public static ResultsAnswerReadResult Loaded(IEnumerable<KeyValuePair<string, string?>> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ImmutableDictionary<string, string?>.Builder copy =
            ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach ((string column, string? value) in cells)
        {
            copy[column] = value;
        }

        return new ResultsAnswerReadResult(ResultsAnswerState.Loaded, copy.ToImmutable());
    }

    public override string ToString() =>
        $"{nameof(ResultsAnswerReadResult)} {{ State = {State}, CellCount = {Cells.Count.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public interface IResultsAnswerSource
{
    Task<ResultsAnswerReadResult> ReadAsync(
        ResultsAnswerRequest request,
        CancellationToken cancellationToken);
}

// FR-RV-03: reads one row of the run's input workbook only while its identity
// (SHA-256, size, last write) matches the run snapshot before and after the read.
public sealed class ResultsAnswerSource : IResultsAnswerSource
{
    private readonly IInputSnapshotBoundary inputSnapshots;
    private readonly Func<string, IEvaluationRowSource> rowSourceFactory;

    public ResultsAnswerSource()
        : this(new PhysicalInputSnapshotBoundary(), path => new OpenXmlEvaluationRowSource(path))
    {
    }

    public ResultsAnswerSource(
        IInputSnapshotBoundary inputSnapshots,
        Func<string, IEvaluationRowSource> rowSourceFactory)
    {
        this.inputSnapshots = inputSnapshots ?? throw new ArgumentNullException(nameof(inputSnapshots));
        this.rowSourceFactory = rowSourceFactory ?? throw new ArgumentNullException(nameof(rowSourceFactory));
    }

    public Task<ResultsAnswerReadResult> ReadAsync(
        ResultsAnswerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => ReadCoreAsync(request, cancellationToken), cancellationToken);
    }

    public override string ToString() =>
        $"{nameof(ResultsAnswerSource)} {{ Content = <redacted> }}";

    private async Task<ResultsAnswerReadResult> ReadCoreAsync(
        ResultsAnswerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!inputSnapshots.IsUnchanged(request.InputPath, request.ExpectedInput))
            {
                return ResultsAnswerReadResult.InputChanged;
            }

            EvaluationRowData row = await rowSourceFactory(request.InputPath).ReadAsync(
                new EvaluationRowRequest(
                    request.SourceSheet,
                    request.SourceRowNumber,
                    request.Columns),
                cancellationToken).ConfigureAwait(false);
            if (row.SourceRowNumber != request.SourceRowNumber)
            {
                return ResultsAnswerReadResult.Unavailable;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!inputSnapshots.IsUnchanged(request.InputPath, request.ExpectedInput))
            {
                return ResultsAnswerReadResult.InputChanged;
            }

            return ResultsAnswerReadResult.Loaded(request.Columns.Select(column =>
                new KeyValuePair<string, string?>(
                    column,
                    row.Cells.TryGetValue(column, out string? value) ? value : null)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ResultsAnswerReadResult.Unavailable;
        }
    }
}
