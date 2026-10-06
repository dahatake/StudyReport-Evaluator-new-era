using System.Collections.Immutable;
using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Workflow;
using SpreadsheetText = DocumentFormat.OpenXml.Spreadsheet.Text;

namespace StudyReportEvaluator.App.Tests.E2E;

internal static class SyntheticWorkbookFactory
{
    internal const int Seed = 20260901;
    internal const string SourceSheetName = "Original";
    internal const int HeaderRow = 1;
    internal const int FirstDataRow = 2;
    internal const int LastDataRow = 531;
    internal const int DataRowCount = LastDataRow - FirstDataRow + 1;
    internal const string Dimension = "A1:L531";

    private static readonly string[] Columns =
    [
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L",
    ];

    private static readonly char[] FormulaMarkers = ['=', '+', '-', '@'];

    internal static SyntheticWorkbook Create()
    {
        string rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "StudyReportEvaluator-E01-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);
        string path = Path.Combine(rootDirectory, "synthetic-e01-input.xlsx");
        ImmutableDictionary<int, ImmutableDictionary<string, string>> rows = CreateRows();

        try
        {
            WriteWorkbook(path, rows);
            return new SyntheticWorkbook(rootDirectory, path, rows);
        }
        catch (Exception creationException)
        {
            try
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
            catch (Exception cleanupException)
                when (cleanupException is IOException or UnauthorizedAccessException)
            {
                throw new AggregateException(
                    "Synthetic workbook creation and cleanup both failed.",
                    creationException,
                    cleanupException);
            }

            throw;
        }
    }

    internal static char FormulaMarkerForRow(int sourceRowNumber)
    {
        ValidateDataRow(sourceRowNumber);
        return FormulaMarkers[(sourceRowNumber - FirstDataRow) % FormulaMarkers.Length];
    }

    internal static string RowMarker(int sourceRowNumber)
    {
        ValidateDataRow(sourceRowNumber);
        return "ROW-" + sourceRowNumber.ToString("D4", CultureInfo.InvariantCulture);
    }

    private static ImmutableDictionary<int, ImmutableDictionary<string, string>> CreateRows()
    {
        Random random = new(Seed);
        ImmutableDictionary<int, ImmutableDictionary<string, string>>.Builder rows =
            ImmutableDictionary.CreateBuilder<int, ImmutableDictionary<string, string>>();
        rows.Add(HeaderRow, CreateHeader());

        for (int rowNumber = FirstDataRow; rowNumber <= LastDataRow; rowNumber++)
        {
            string rowMarker = RowMarker(rowNumber);
            char formulaMarker = FormulaMarkerForRow(rowNumber);
            string nonce = random.Next(100_000, 1_000_000)
                .ToString(CultureInfo.InvariantCulture);
            bool emptyKnowledgePrimary = rowNumber == 17 || rowNumber % 47 == 0;
            bool emptyPromptPrimary = rowNumber == 17 || rowNumber % 53 == 0;
            ImmutableDictionary<string, string>.Builder cells =
                ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
            cells.Add("A", $"SYNTHETIC-ID-{rowNumber.ToString("D4", CultureInfo.InvariantCulture)}-{nonce}");
            cells.Add("B", $"SYNTHETIC-TIME-{rowNumber.ToString("D4", CultureInfo.InvariantCulture)}");
            cells.Add("C", $"SYNTHETIC-NAME-{nonce}");
            cells.Add("D", $"SYNTHETIC-EMAIL-{rowNumber.ToString("D4", CultureInfo.InvariantCulture)}");
            cells.Add("E", $"SYNTHETIC-CLASS-{rowNumber % 7}");
            cells.Add(
                "F",
                emptyKnowledgePrimary
                    ? string.Empty
                    : $"{formulaMarker}{rowMarker} SYNTHETIC-REPORT-ONE TOKEN-{nonce}");
            cells.Add("G", $"{formulaMarker}{rowMarker} SYNTHETIC-STUDENT-PROMPT-ONE TOKEN-{nonce}");
            cells.Add("H", $"{formulaMarker}{rowMarker} SYNTHETIC-QUESTION-RESPONSE TOKEN-{nonce}");
            cells.Add("I", $"{formulaMarker}{rowMarker} SYNTHETIC-REPORT-TWO TOKEN-{nonce}");
            cells.Add(
                "J",
                emptyPromptPrimary
                    ? string.Empty
                    : $"{formulaMarker}{rowMarker} SYNTHETIC-STUDENT-PROMPT-TWO TOKEN-{nonce}");
            cells.Add("K", $"{formulaMarker}{rowMarker} SYNTHETIC-PROMPT-CONSIDERATION TOKEN-{nonce}");
            cells.Add("L", $"SYNTHETIC-FEEDBACK-{rowNumber.ToString("D4", CultureInfo.InvariantCulture)}-{nonce}");
            rows.Add(rowNumber, cells.ToImmutable());
        }

        return rows.ToImmutable();
    }

    private static ImmutableDictionary<string, string> CreateHeader()
    {
        ImmutableDictionary<string, string>.Builder headers =
            ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        headers.Add("A", "Synthetic Response ID");
        headers.Add("B", "Synthetic Timestamp");
        headers.Add("C", "Synthetic Name");
        headers.Add("D", "Synthetic Email");
        headers.Add("E", "Synthetic Class");
        headers.Add("F", "Synthetic Report Answer One");
        headers.Add("G", "Synthetic Student Prompt One");
        headers.Add("H", "Question");
        headers.Add("I", "Synthetic Report Answer Two");
        headers.Add("J", "Synthetic Student Prompt Two");
        headers.Add("K", "Synthetic Prompt Consideration");
        headers.Add("L", "Synthetic Feedback");
        return headers.ToImmutable();
    }

    private static void WriteWorkbook(
        string path,
        IReadOnlyDictionary<int, ImmutableDictionary<string, string>> rows)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Create(
            path,
            SpreadsheetDocumentType.Workbook);
        WorkbookPart workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        SheetData sheetData = new();
        for (int rowNumber = HeaderRow; rowNumber <= LastDataRow; rowNumber++)
        {
            Row row = new() { RowIndex = checked((uint)rowNumber) };
            IReadOnlyDictionary<string, string> values = rows[rowNumber];
            foreach (string column in Columns)
            {
                string value = values[column];
                if (value.Length > 0)
                {
                    row.Append(CreateInlineStringCell(
                        column + rowNumber.ToString(CultureInfo.InvariantCulture),
                        value));
                }
            }

            sheetData.Append(row);
        }

        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetPart.Worksheet = new Worksheet(
            new SheetDimension { Reference = Dimension },
            sheetData);
        worksheetPart.Worksheet.Save();

        Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = SourceSheetName,
        });
        workbookPart.Workbook.Save();
    }

    private static Cell CreateInlineStringCell(string reference, string value) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(
                new SpreadsheetText(value) { Space = SpaceProcessingModeValues.Preserve }),
        };

    private static void ValidateDataRow(int sourceRowNumber)
    {
        if (sourceRowNumber is < FirstDataRow or > LastDataRow)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRowNumber));
        }
    }
}

internal sealed class SyntheticWorkbook : IDisposable
{
    private readonly object disposeGate = new();
    private readonly ImmutableDictionary<int, ImmutableDictionary<string, string>> rows;
    private bool disposed;

    internal SyntheticWorkbook(
        string rootDirectory,
        string path,
        ImmutableDictionary<int, ImmutableDictionary<string, string>> rows)
    {
        RootDirectory = rootDirectory;
        Path = path;
        this.rows = rows;
    }

    internal string RootDirectory { get; }

    internal string Path { get; }

    internal string GetCell(int sourceRowNumber, string column)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!rows.TryGetValue(sourceRowNumber, out ImmutableDictionary<string, string>? row)
            || !row.TryGetValue(column, out string? value))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRowNumber));
        }

        return value;
    }

    internal int CountBlankRows(string column) => Enumerable
        .Range(SyntheticWorkbookFactory.FirstDataRow, SyntheticWorkbookFactory.DataRowCount)
        .Count(rowNumber => string.IsNullOrWhiteSpace(GetCell(rowNumber, column)));

    internal int FindUniqueSourceRow(string primaryColumn, string primaryValue)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryValue);

        int match = 0;
        foreach (int rowNumber in Enumerable.Range(
                     SyntheticWorkbookFactory.FirstDataRow,
                     SyntheticWorkbookFactory.DataRowCount))
        {
            if (!string.Equals(
                    GetCell(rowNumber, primaryColumn),
                    primaryValue,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (match != 0)
            {
                throw new InvalidOperationException("A synthetic primary source value is not unique.");
            }

            match = rowNumber;
        }

        return match != 0
            ? match
            : throw new InvalidOperationException("A synthetic primary source value was not found.");
    }

    public void Dispose()
    {
        lock (disposeGate)
        {
            if (disposed)
            {
                return;
            }

            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }

            if (Directory.Exists(RootDirectory))
            {
                throw new IOException("The synthetic workbook directory was not removed.");
            }

            disposed = true;
        }
    }

    public override string ToString() =>
        $"{nameof(SyntheticWorkbook)} {{ RowCount = {SyntheticWorkbookFactory.DataRowCount.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

internal sealed record SyntheticRowRead(
    int SourceRowNumber,
    ImmutableArray<string> SelectedColumns)
{
    public override string ToString() =>
        $"{nameof(SyntheticRowRead)} {{ SourceRowNumber = {SourceRowNumber.ToString(CultureInfo.InvariantCulture)}, SelectedColumnCount = {SelectedColumns.Length.ToString(CultureInfo.InvariantCulture)}, ColumnIds = [{string.Join(',', SelectedColumns)}], Content = <redacted> }}";
}

internal sealed class SyntheticEvaluationRowSource(SyntheticWorkbook workbook) : IEvaluationRowSource
{
    private readonly object gate = new();
    private readonly List<SyntheticRowRead> reads = [];

    internal ImmutableArray<SyntheticRowRead> Reads
    {
        get
        {
            lock (gate)
            {
                return reads.ToImmutableArray();
            }
        }
    }

    public Task<EvaluationRowData> ReadAsync(
        EvaluationRowRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
                request.SourceSheet,
                SyntheticWorkbookFactory.SourceSheetName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The synthetic row source received an unexpected sheet.");
        }

        KeyValuePair<string, string?>[] selected = request.SelectedColumns
            .Select(column => new KeyValuePair<string, string?>(
                column,
                workbook.GetCell(request.SourceRowNumber, column)))
            .ToArray();
        lock (gate)
        {
            reads.Add(new SyntheticRowRead(
                request.SourceRowNumber,
                request.SelectedColumns));
        }

        return Task.FromResult(new EvaluationRowData(request.SourceRowNumber, selected));
    }

    public override string ToString()
    {
        lock (gate)
        {
            return $"{nameof(SyntheticEvaluationRowSource)} {{ ReadCount = {reads.Count.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
        }
    }
}