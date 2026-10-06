using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Workbooks.Intake;

namespace StudyReportEvaluator.App.Workbooks.Reading;

public enum WorkbookSheetState
{
    Visible,
    Hidden,
    VeryHidden,
}

public sealed class WorkbookHeaderCell
{
    internal WorkbookHeaderCell(uint columnIndex, string columnName, string value)
    {
        ColumnIndex = columnIndex;
        ColumnName = columnName;
        Value = value;
    }

    public uint ColumnIndex { get; }

    public string ColumnName { get; }

    public string Value { get; }

    public override string ToString() =>
        $"{nameof(WorkbookHeaderCell)} {{ ColumnIndex = {ColumnIndex}, Content = <redacted> }}";
}

public sealed class WorksheetMetadata
{
    internal WorksheetMetadata(
        string name,
        WorkbookSheetState state,
        string dimensionReference,
        uint firstRowIndex,
        uint lastRowIndex,
        uint firstColumnIndex,
        uint lastColumnIndex,
        IReadOnlyList<WorkbookHeaderCell> headerCells)
    {
        Name = name;
        State = state;
        DimensionReference = dimensionReference;
        FirstRowIndex = firstRowIndex;
        LastRowIndex = lastRowIndex;
        FirstColumnIndex = firstColumnIndex;
        LastColumnIndex = lastColumnIndex;
        HeaderCells = headerCells;
        HeaderRowValues = Array.AsReadOnly(headerCells.Select(cell => cell.Value).ToArray());
    }

    public string Name { get; }

    public WorkbookSheetState State { get; }

    public string DimensionReference { get; }

    public uint FirstRowIndex { get; }

    public uint LastRowIndex { get; }

    public uint RowCount => LastRowIndex - FirstRowIndex + 1;

    public uint FirstColumnIndex { get; }

    public uint LastColumnIndex { get; }

    public uint ColumnCount => LastColumnIndex - FirstColumnIndex + 1;

    public IReadOnlyList<WorkbookHeaderCell> HeaderCells { get; }

    public IReadOnlyList<string> HeaderRowValues { get; }

    public override string ToString() =>
        $"{nameof(WorksheetMetadata)} {{ State = {State}, Content = <redacted> }}";
}

public sealed class WorkbookMetadata
{
    internal WorkbookMetadata(
        uint headerRowNumber,
        int packagePartCount,
        int relationshipCount,
        int sharedStringCount,
        IReadOnlyList<WorksheetMetadata> worksheets)
    {
        HeaderRowNumber = headerRowNumber;
        PackagePartCount = packagePartCount;
        RelationshipCount = relationshipCount;
        SharedStringCount = sharedStringCount;
        Worksheets = worksheets;
    }

    public uint HeaderRowNumber { get; }

    public int PackagePartCount { get; }

    public int RelationshipCount { get; }

    public int SharedStringCount { get; }

    public IReadOnlyList<WorksheetMetadata> Worksheets { get; }

    public override string ToString() =>
        $"{nameof(WorkbookMetadata)} {{ WorksheetCount = {Worksheets.Count}, Content = <redacted> }}";
}

public sealed class WorkbookMetadataReader
{
    public const int MaxWorksheetCount = FileFormatClassifier.MaxZipEntryCount;
    public const uint MaxWorksheetRows = 1_048_576;
    public const uint MaxWorksheetColumns = 16_384;
    public const int MaxSharedStringCount = 2_000_000;
    public const int MaxCellFormatCount = 20_000;
    public const int MaxCellCharacters = 32_767;

    private readonly FileFormatClassifier classifier;

    public WorkbookMetadataReader()
        : this(new FileFormatClassifier())
    {
    }

    public WorkbookMetadataReader(FileFormatClassifier classifier)
    {
        this.classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    public WorkbookMetadata Read(string filePath, uint headerRowNumber = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (headerRowNumber is 0 or > MaxWorksheetRows)
        {
            throw new ArgumentOutOfRangeException(nameof(headerRowNumber));
        }

        if (!string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            FileFormatClassificationResult unsupported = classifier.Classify(filePath);
            throw new InvalidDataException($"Workbook input was rejected: {unsupported.Classification}.");
        }

        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        FileFormatClassificationResult classification = classifier.ClassifyOpenedStream(stream);
        if (!classification.IsAccepted)
        {
            throw new InvalidDataException($"Workbook input was rejected: {classification.Classification}.");
        }

        stream.Position = 0;
        OpenSettings settings = new()
        {
            AutoSave = false,
            MaxCharactersInPart = FileFormatClassifier.MaxCharactersInPart,
        };

        using SpreadsheetDocument document = SpreadsheetDocument.Open(stream, false, settings);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw InvalidMetadata();
        Workbook workbook = workbookPart.Workbook ?? throw InvalidMetadata();
        Sheet[] sheets = workbook.Descendants<Sheet>().ToArray();
        if (sheets.Length is 0 or > MaxWorksheetCount)
        {
            throw LimitExceeded();
        }

        int sharedStringCount = ValidateSharedStrings(workbookPart.SharedStringTablePart);
        ValidateCellFormats(workbookPart.WorkbookStylesPart);

        List<WorksheetMetadataDraft> drafts = new(sheets.Length);
        HashSet<int> requiredSharedStrings = [];
        foreach (Sheet sheet in sheets)
        {
            string name = sheet.Name?.Value ?? string.Empty;
            string relationshipId = sheet.Id?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)
                || name.Length > 31
                || string.IsNullOrWhiteSpace(relationshipId)
                || workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
            {
                throw InvalidMetadata();
            }

            WorksheetMetadataDraft draft = ReadWorksheet(
                worksheetPart,
                name,
                ConvertSheetState(sheet.State?.Value),
                headerRowNumber,
                sharedStringCount);
            drafts.Add(draft);
            foreach (PendingHeaderCell cell in draft.HeaderCells)
            {
                if (cell.SharedStringIndex is int sharedStringIndex)
                {
                    requiredSharedStrings.Add(sharedStringIndex);
                }
            }
        }

        IReadOnlyDictionary<int, string> sharedStrings = ReadSelectedSharedStrings(
            workbookPart.SharedStringTablePart,
            requiredSharedStrings,
            sharedStringCount);
        ReadOnlyCollection<WorksheetMetadata> worksheets = Array.AsReadOnly(
            drafts.Select(draft => draft.ToMetadata(sharedStrings)).ToArray());
        return new WorkbookMetadata(
            headerRowNumber,
            classification.PackagePartCount,
            classification.RelationshipCount,
            sharedStringCount,
            worksheets);
    }

    private static int ValidateSharedStrings(SharedStringTablePart? sharedStringPart)
    {
        if (sharedStringPart is null)
        {
            return 0;
        }

        int count = 0;
        using OpenXmlPartReader reader = CreateReader(sharedStringPart);
        while (reader.Read())
        {
            if (!reader.IsStartElement || reader.ElementType != typeof(SharedStringItem))
            {
                continue;
            }

            if (count >= MaxSharedStringCount)
            {
                throw LimitExceeded();
            }

            SharedStringItem item = reader.LoadCurrentElement() as SharedStringItem
                ?? throw InvalidMetadata();
            _ = ReadText(item);
            count++;
        }

        return count;
    }

    private static IReadOnlyDictionary<int, string> ReadSelectedSharedStrings(
        SharedStringTablePart? sharedStringPart,
        IReadOnlySet<int> requiredIndices,
        int expectedCount)
    {
        if (requiredIndices.Count == 0)
        {
            return new ReadOnlyDictionary<int, string>(new Dictionary<int, string>());
        }

        if (sharedStringPart is null)
        {
            throw InvalidMetadata();
        }

        Dictionary<int, string> selected = [];
        int index = 0;
        using OpenXmlPartReader reader = CreateReader(sharedStringPart);
        while (reader.Read())
        {
            if (!reader.IsStartElement || reader.ElementType != typeof(SharedStringItem))
            {
                continue;
            }

            SharedStringItem item = reader.LoadCurrentElement() as SharedStringItem
                ?? throw InvalidMetadata();
            if (requiredIndices.Contains(index))
            {
                selected.Add(index, ReadText(item));
            }

            index++;
        }

        if (index != expectedCount || selected.Count != requiredIndices.Count)
        {
            throw InvalidMetadata();
        }

        return new ReadOnlyDictionary<int, string>(selected);
    }

    private static void ValidateCellFormats(WorkbookStylesPart? stylesPart)
    {
        if (stylesPart is null)
        {
            return;
        }

        Stylesheet stylesheet = stylesPart.Stylesheet ?? throw InvalidMetadata();
        CellFormats? cellFormats = stylesheet.CellFormats;
        if (cellFormats is null)
        {
            return;
        }

        int count = cellFormats.Elements<CellFormat>().Count();
        if (count > MaxCellFormatCount
            || (cellFormats.Count is not null && cellFormats.Count.Value != (uint)count))
        {
            throw LimitExceeded();
        }
    }

    private static WorksheetMetadataDraft ReadWorksheet(
        WorksheetPart worksheetPart,
        string name,
        WorkbookSheetState state,
        uint headerRowNumber,
        int sharedStringCount)
    {
        CellRange? declaredRange = null;
        uint? firstActualRow = null;
        uint? lastActualRow = null;
        uint? firstActualColumn = null;
        uint? lastActualColumn = null;
        uint previousRowIndex = 0;
        List<PendingHeaderCell> headerCells = [];

        using OpenXmlPartReader reader = CreateReader(worksheetPart);
        while (reader.Read())
        {
            if (!reader.IsStartElement)
            {
                continue;
            }

            if (reader.ElementType == typeof(SheetDimension))
            {
                if (declaredRange is not null)
                {
                    throw InvalidMetadata();
                }

                SheetDimension dimension = reader.LoadCurrentElement() as SheetDimension
                    ?? throw InvalidMetadata();
                declaredRange = ParseRange(dimension.Reference?.Value);
                ValidateRangeLimits(declaredRange.Value);
                continue;
            }

            if (reader.ElementType != typeof(Row))
            {
                continue;
            }

            Row row = reader.LoadCurrentElement() as Row
                ?? throw InvalidMetadata();
            uint rowIndex = row.RowIndex?.Value
                ?? checked(previousRowIndex + 1);
            if (rowIndex is 0 or > MaxWorksheetRows || rowIndex <= previousRowIndex)
            {
                throw InvalidMetadata();
            }

            previousRowIndex = rowIndex;
            firstActualRow ??= rowIndex;
            lastActualRow = rowIndex;
            ValidateSpan(firstActualRow.Value, lastActualRow.Value, MaxWorksheetRows);

            uint previousColumnIndex = 0;
            foreach (Cell cell in row.Elements<Cell>())
            {
                uint columnIndex;
                if (string.IsNullOrWhiteSpace(cell.CellReference?.Value))
                {
                    columnIndex = checked(previousColumnIndex + 1);
                }
                else
                {
                    CellAddress address = ParseAddress(cell.CellReference!.Value!);
                    if (address.RowIndex != rowIndex)
                    {
                        throw InvalidMetadata();
                    }

                    columnIndex = address.ColumnIndex;
                }

                if (columnIndex is 0 or > MaxWorksheetColumns || columnIndex <= previousColumnIndex)
                {
                    throw InvalidMetadata();
                }

                previousColumnIndex = columnIndex;
                firstActualColumn = firstActualColumn is null
                    ? columnIndex
                    : Math.Min(firstActualColumn.Value, columnIndex);
                lastActualColumn = lastActualColumn is null
                    ? columnIndex
                    : Math.Max(lastActualColumn.Value, columnIndex);
                ValidateSpan(firstActualColumn.Value, lastActualColumn.Value, MaxWorksheetColumns);

                PendingCellValue value = ReadCellValue(cell, sharedStringCount);
                if (rowIndex == headerRowNumber)
                {
                    headerCells.Add(new PendingHeaderCell(columnIndex, value.Value, value.SharedStringIndex));
                }
            }
        }

        CellRange effectiveRange;
        if (declaredRange is CellRange range)
        {
            if (firstActualRow is not null
                && (firstActualRow.Value < range.FirstRow
                    || lastActualRow!.Value > range.LastRow))
            {
                throw InvalidMetadata();
            }

            if (firstActualColumn is not null
                && (firstActualColumn.Value < range.FirstColumn
                    || lastActualColumn!.Value > range.LastColumn))
            {
                throw InvalidMetadata();
            }

            effectiveRange = range;
        }
        else if (firstActualRow is not null)
        {
            effectiveRange = new CellRange(
                firstActualColumn ?? 1,
                firstActualRow.Value,
                lastActualColumn ?? 1,
                lastActualRow ?? firstActualRow.Value);
            ValidateRangeLimits(effectiveRange);
        }
        else
        {
            effectiveRange = new CellRange(1, 1, 1, 1);
        }

        return new WorksheetMetadataDraft(
            name,
            state,
            effectiveRange,
            headerCells);
    }

    private static PendingCellValue ReadCellValue(Cell cell, int sharedStringCount)
    {
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            string indexText = cell.CellValue?.Text ?? string.Empty;
            if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                || index < 0
                || index >= sharedStringCount)
            {
                throw InvalidMetadata();
            }

            return new PendingCellValue(null, index);
        }

        string value = cell.DataType?.Value == CellValues.InlineString
            ? ReadText(cell.InlineString)
            : cell.CellValue?.Text ?? string.Empty;
        EnsureCellLength(value.Length);
        return new PendingCellValue(value, null);
    }

    private static string ReadText(OpenXmlElement? element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        StringBuilder value = new();
        foreach (DocumentFormat.OpenXml.Spreadsheet.Text text in element.Descendants<DocumentFormat.OpenXml.Spreadsheet.Text>())
        {
            string fragment = text.Text ?? string.Empty;
            EnsureCellLength(checked(value.Length + fragment.Length));
            value.Append(fragment);
        }

        return value.ToString();
    }

    private static void EnsureCellLength(int length)
    {
        if (length > MaxCellCharacters)
        {
            throw LimitExceeded();
        }
    }

    private static CellRange ParseRange(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw InvalidMetadata();
        }

        string[] endpoints = reference.Split(':');
        if (endpoints.Length is < 1 or > 2)
        {
            throw InvalidMetadata();
        }

        CellAddress first = ParseAddress(endpoints[0]);
        CellAddress last = endpoints.Length == 1 ? first : ParseAddress(endpoints[1]);
        if (first.ColumnIndex > last.ColumnIndex || first.RowIndex > last.RowIndex)
        {
            throw InvalidMetadata();
        }

        return new CellRange(
            first.ColumnIndex,
            first.RowIndex,
            last.ColumnIndex,
            last.RowIndex);
    }

    private static CellAddress ParseAddress(string reference)
    {
        ReadOnlySpan<char> value = reference.AsSpan();
        int position = 0;
        if (position < value.Length && value[position] == '$')
        {
            position++;
        }

        uint column = 0;
        int columnCharacters = 0;
        while (position < value.Length && char.IsAsciiLetter(value[position]))
        {
            char character = char.ToUpperInvariant(value[position]);
            ulong nextColumn = ((ulong)column * 26) + (uint)(character - 'A' + 1);
            if (nextColumn > MaxWorksheetColumns)
            {
                throw InvalidMetadata();
            }

            column = (uint)nextColumn;
            columnCharacters++;
            position++;
        }

        if (columnCharacters == 0 || column > MaxWorksheetColumns)
        {
            throw InvalidMetadata();
        }

        if (position < value.Length && value[position] == '$')
        {
            position++;
        }

        uint row = 0;
        int rowCharacters = 0;
        while (position < value.Length && char.IsAsciiDigit(value[position]))
        {
            ulong nextRow = ((ulong)row * 10) + (uint)(value[position] - '0');
            if (nextRow > MaxWorksheetRows)
            {
                throw InvalidMetadata();
            }

            row = (uint)nextRow;
            rowCharacters++;
            position++;
        }

        if (position != value.Length
            || rowCharacters == 0
            || row is 0 or > MaxWorksheetRows)
        {
            throw InvalidMetadata();
        }

        return new CellAddress(column, row);
    }

    private static void ValidateRangeLimits(CellRange range)
    {
        ValidateSpan(range.FirstRow, range.LastRow, MaxWorksheetRows);
        ValidateSpan(range.FirstColumn, range.LastColumn, MaxWorksheetColumns);
    }

    private static void ValidateSpan(uint first, uint last, uint maximum)
    {
        if (last < first || last - first + 1 > maximum)
        {
            throw LimitExceeded();
        }
    }

    private static WorkbookSheetState ConvertSheetState(SheetStateValues? state)
    {
        SheetStateValues value = state ?? SheetStateValues.Visible;
        if (value == SheetStateValues.Visible)
        {
            return WorkbookSheetState.Visible;
        }

        if (value == SheetStateValues.Hidden)
        {
            return WorkbookSheetState.Hidden;
        }

        if (value == SheetStateValues.VeryHidden)
        {
            return WorkbookSheetState.VeryHidden;
        }

        throw InvalidMetadata();
    }

    private static OpenXmlPartReader CreateReader(OpenXmlPart part) =>
        new(part, new OpenXmlPartReaderOptions
        {
            MaxCharactersInPart = FileFormatClassifier.MaxCharactersInPart,
        });

    private static string GetColumnName(uint columnIndex)
    {
        Span<char> characters = stackalloc char[3];
        int position = characters.Length;
        uint value = columnIndex;
        while (value > 0)
        {
            value--;
            characters[--position] = (char)('A' + (value % 26));
            value /= 26;
        }

        return new string(characters[position..]);
    }

    private static InvalidDataException InvalidMetadata() =>
        new("Workbook metadata is invalid.");

    private static InvalidDataException LimitExceeded() =>
        new("Workbook metadata exceeds a configured safety limit.");

    private readonly record struct CellAddress(uint ColumnIndex, uint RowIndex);

    private readonly record struct CellRange(
        uint FirstColumn,
        uint FirstRow,
        uint LastColumn,
        uint LastRow)
    {
        public override string ToString()
        {
            string first = GetColumnName(FirstColumn) + FirstRow.ToString(CultureInfo.InvariantCulture);
            string last = GetColumnName(LastColumn) + LastRow.ToString(CultureInfo.InvariantCulture);
            return string.Equals(first, last, StringComparison.Ordinal)
                ? first
                : first + ":" + last;
        }
    }

    private readonly record struct PendingCellValue(string? Value, int? SharedStringIndex);

    private readonly record struct PendingHeaderCell(
        uint ColumnIndex,
        string? Value,
        int? SharedStringIndex);

    private sealed class WorksheetMetadataDraft(
        string name,
        WorkbookSheetState state,
        CellRange range,
        IReadOnlyList<PendingHeaderCell> headerCells)
    {
        public IReadOnlyList<PendingHeaderCell> HeaderCells { get; } = headerCells;

        public WorksheetMetadata ToMetadata(IReadOnlyDictionary<int, string> sharedStrings)
        {
            WorkbookHeaderCell[] resolvedHeaders = HeaderCells
                .Select(cell => new WorkbookHeaderCell(
                    cell.ColumnIndex,
                    GetColumnName(cell.ColumnIndex),
                    cell.SharedStringIndex is int sharedStringIndex
                        ? sharedStrings[sharedStringIndex]
                        : cell.Value ?? string.Empty))
                .ToArray();
            return new WorksheetMetadata(
                name,
                state,
                range.ToString(),
                range.FirstRow,
                range.LastRow,
                range.FirstColumn,
                range.LastColumn,
                Array.AsReadOnly(resolvedHeaders));
        }
    }
}