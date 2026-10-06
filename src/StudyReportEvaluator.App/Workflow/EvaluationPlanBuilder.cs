using System.Collections.Immutable;
using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.App.Workflow;

public sealed class EvaluationRowRequest
{
    public EvaluationRowRequest(
        string sourceSheet,
        int sourceRowNumber,
        IEnumerable<string> selectedColumns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSheet);
        ArgumentNullException.ThrowIfNull(selectedColumns);
        if (sourceRowNumber is < 1 or > 1_048_576)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRowNumber));
        }

        ImmutableArray<string>.Builder columns = ImmutableArray.CreateBuilder<string>();
        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? column in selectedColumns)
        {
            string canonical = CanonicalColumn(column);
            if (!unique.Add(canonical))
            {
                throw new ArgumentException("Selected columns must be unique.", nameof(selectedColumns));
            }

            columns.Add(canonical);
        }

        if (columns.Count == 0)
        {
            throw new ArgumentException("At least one selected column is required.", nameof(selectedColumns));
        }

        SourceSheet = sourceSheet;
        SourceRowNumber = sourceRowNumber;
        SelectedColumns = columns.ToImmutable();
    }

    public string SourceSheet { get; }

    public int SourceRowNumber { get; }

    public ImmutableArray<string> SelectedColumns { get; }

    public override string ToString() =>
        $"{nameof(EvaluationRowRequest)} {{ SourceRowNumber = {SourceRowNumber.ToString(CultureInfo.InvariantCulture)}, SelectedColumnCount = {SelectedColumns.Length.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";

    internal static string CanonicalColumn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 1 or > 3)
        {
            throw new ArgumentException("A selected source column is invalid.", nameof(value));
        }

        Span<char> canonical = stackalloc char[value.Length];
        uint columnNumber = 0;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (!char.IsAsciiLetter(character))
            {
                throw new ArgumentException("A selected source column is invalid.", nameof(value));
            }

            char upper = char.ToUpperInvariant(character);
            canonical[index] = upper;
            columnNumber = checked((columnNumber * 26) + (uint)(upper - 'A' + 1));
        }

        if (columnNumber > 16_384)
        {
            throw new ArgumentException("A selected source column is invalid.", nameof(value));
        }

        return new string(canonical);
    }
}

public sealed class EvaluationRowData
{
    public EvaluationRowData(
        int sourceRowNumber,
        IEnumerable<KeyValuePair<string, string?>> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (sourceRowNumber is < 1 or > 1_048_576)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRowNumber));
        }

        ImmutableDictionary<string, string?>.Builder copy =
            ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach ((string? column, string? value) in cells)
        {
            string canonical = EvaluationRowRequest.CanonicalColumn(column);
            if (!copy.TryAdd(canonical, value))
            {
                throw new ArgumentException("Row cells must have unique source columns.", nameof(cells));
            }
        }

        SourceRowNumber = sourceRowNumber;
        Cells = copy.ToImmutable();
    }

    public int SourceRowNumber { get; }

    public ImmutableDictionary<string, string?> Cells { get; }

    public override string ToString() =>
        $"{nameof(EvaluationRowData)} {{ SourceRowNumber = {SourceRowNumber.ToString(CultureInfo.InvariantCulture)}, CellCount = {Cells.Count.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public interface IEvaluationRowSource
{
    Task<EvaluationRowData> ReadAsync(
        EvaluationRowRequest request,
        CancellationToken cancellationToken);
}

public sealed class EvaluationRowSourceException : Exception
{
    public EvaluationRowSourceException(string code)
        : base("The selected evaluation row could not be read.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}

public sealed class OpenXmlEvaluationRowSource : IEvaluationRowSource
{
    private readonly string inputPath;

    public OpenXmlEvaluationRowSource(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        try
        {
            this.inputPath = Path.GetFullPath(inputPath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            throw new ArgumentException("The input workbook path is invalid.", nameof(inputPath));
        }
    }

    public Task<EvaluationRowData> ReadAsync(
        EvaluationRowRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return Task.FromResult(ReadCore(request, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw new EvaluationRowSourceException("ROW_SOURCE_FAILED");
        }
    }

    public override string ToString() =>
        $"{nameof(OpenXmlEvaluationRowSource)} {{ Content = <redacted> }}";

    private EvaluationRowData ReadCore(
        EvaluationRowRequest request,
        CancellationToken cancellationToken)
    {
        using FileStream stream = new(
            inputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using SpreadsheetDocument document = SpreadsheetDocument.Open(
            stream,
            false,
            new OpenSettings
            {
                AutoSave = false,
                MaxCharactersInPart = FileFormatClassifier.MaxCharactersInPart,
            });
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The workbook part is missing.");
        Sheet sheet = (workbookPart.Workbook
                ?? throw new InvalidDataException("The workbook root is missing."))
            .Descendants<Sheet>()
            .SingleOrDefault(candidate => string.Equals(
                candidate.Name?.Value,
                request.SourceSheet,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The selected worksheet is missing.");
        string relationshipId = sheet.Id?.Value
            ?? throw new InvalidDataException("The selected worksheet relationship is missing.");
        WorksheetPart worksheetPart = workbookPart.GetPartById(relationshipId) as WorksheetPart
            ?? throw new InvalidDataException("The selected worksheet part is missing.");

        ImmutableHashSet<string> selected = request.SelectedColumns.ToImmutableHashSet(
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, PendingCellValue> pending = new(StringComparer.OrdinalIgnoreCase);
        ReadSelectedRow(
            worksheetPart,
            request.SourceRowNumber,
            selected,
            pending,
            cancellationToken);
        IReadOnlyDictionary<int, string> sharedStrings = ReadSelectedSharedStrings(
            workbookPart.SharedStringTablePart,
            pending.Values
                .Where(value => value.SharedStringIndex is not null)
                .Select(value => value.SharedStringIndex!.Value)
                .ToImmutableHashSet(),
            cancellationToken);

        KeyValuePair<string, string?>[] cells = request.SelectedColumns
            .Select(column => new KeyValuePair<string, string?>(
                column,
                pending.TryGetValue(column, out PendingCellValue value)
                    ? value.SharedStringIndex is int index
                        ? sharedStrings[index]
                        : value.Text ?? string.Empty
                    : string.Empty))
            .ToArray();
        return new EvaluationRowData(request.SourceRowNumber, cells);
    }

    private static void ReadSelectedRow(
        WorksheetPart worksheetPart,
        int requestedRow,
        IReadOnlySet<string> selected,
        Dictionary<string, PendingCellValue> values,
        CancellationToken cancellationToken)
    {
        uint previousRow = 0;
        using OpenXmlPartReader reader = CreateReader(worksheetPart);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reader.IsStartElement || reader.ElementType != typeof(Row))
            {
                continue;
            }

            Row row = reader.LoadCurrentElement() as Row
                ?? throw new InvalidDataException("A worksheet row is invalid.");
            uint rowNumber = row.RowIndex?.Value ?? checked(previousRow + 1);
            if (rowNumber <= previousRow)
            {
                throw new InvalidDataException("Worksheet rows are not ordered.");
            }

            previousRow = rowNumber;
            if (rowNumber < requestedRow)
            {
                continue;
            }

            if (rowNumber > requestedRow)
            {
                return;
            }

            uint previousColumn = 0;
            foreach (Cell cell in row.Elements<Cell>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                (uint columnNumber, string columnName) = ResolveColumn(
                    cell.CellReference?.Value,
                    rowNumber,
                    previousColumn);
                if (columnNumber <= previousColumn)
                {
                    throw new InvalidDataException("Worksheet cells are not ordered.");
                }

                previousColumn = columnNumber;
                if (selected.Contains(columnName))
                {
                    values.Add(columnName, ReadCell(cell));
                }
            }

            return;
        }
    }

    private static IReadOnlyDictionary<int, string> ReadSelectedSharedStrings(
        SharedStringTablePart? sharedStringPart,
        IReadOnlySet<int> requiredIndices,
        CancellationToken cancellationToken)
    {
        if (requiredIndices.Count == 0)
        {
            return ImmutableDictionary<int, string>.Empty;
        }

        if (sharedStringPart is null || requiredIndices.Any(index => index < 0))
        {
            throw new InvalidDataException("A selected shared string is invalid.");
        }

        Dictionary<int, string> selected = [];
        int index = 0;
        using OpenXmlPartReader reader = CreateReader(sharedStringPart);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reader.IsStartElement || reader.ElementType != typeof(SharedStringItem))
            {
                continue;
            }

            SharedStringItem item = reader.LoadCurrentElement() as SharedStringItem
                ?? throw new InvalidDataException("A shared string item is invalid.");
            if (requiredIndices.Contains(index))
            {
                selected.Add(index, ReadText(item));
            }

            index++;
        }

        if (selected.Count != requiredIndices.Count)
        {
            throw new InvalidDataException("A selected shared string is missing.");
        }

        return selected;
    }

    private static PendingCellValue ReadCell(Cell cell)
    {
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (!int.TryParse(
                    cell.CellValue?.Text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int sharedStringIndex)
                || sharedStringIndex < 0)
            {
                throw new InvalidDataException("A selected shared string reference is invalid.");
            }

            return new PendingCellValue(null, sharedStringIndex);
        }

        string text = cell.DataType?.Value == CellValues.InlineString
            ? ReadText(cell.InlineString)
            : cell.CellValue?.Text ?? string.Empty;
        EnsureCellLength(text);
        return new PendingCellValue(text, null);
    }

    private static string ReadText(OpenXmlElement? element)
    {
        if (element is null)
        {
            return string.Empty;
        }

        string value = string.Concat(
            element.Descendants<DocumentFormat.OpenXml.Spreadsheet.Text>()
                .Select(text => text.Text ?? string.Empty));
        EnsureCellLength(value);
        return value;
    }

    private static void EnsureCellLength(string value)
    {
        if (value.Length > WorkbookMetadataReader.MaxCellCharacters)
        {
            throw new InvalidDataException("A selected cell exceeds the configured limit.");
        }
    }

    private static (uint ColumnNumber, string ColumnName) ResolveColumn(
        string? cellReference,
        uint rowNumber,
        uint previousColumn)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            uint inferred = checked(previousColumn + 1);
            return (inferred, ColumnName(inferred));
        }

        ReadOnlySpan<char> reference = cellReference.AsSpan();
        int position = 0;
        if (reference[position] == '$')
        {
            position++;
        }

        uint column = 0;
        int columnCharacters = 0;
        while (position < reference.Length && char.IsAsciiLetter(reference[position]))
        {
            char upper = char.ToUpperInvariant(reference[position]);
            column = checked((column * 26) + (uint)(upper - 'A' + 1));
            columnCharacters++;
            position++;
        }

        if (position < reference.Length && reference[position] == '$')
        {
            position++;
        }

        if (columnCharacters == 0
            || column is 0 or > 16_384
            || !uint.TryParse(
                reference[position..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out uint referencedRow)
            || referencedRow != rowNumber)
        {
            throw new InvalidDataException("A selected cell reference is invalid.");
        }

        return (column, ColumnName(column));
    }

    private static string ColumnName(uint columnNumber)
    {
        Span<char> buffer = stackalloc char[3];
        int position = buffer.Length;
        uint remaining = columnNumber;
        while (remaining > 0)
        {
            remaining--;
            buffer[--position] = (char)('A' + (remaining % 26));
            remaining /= 26;
        }

        return new string(buffer[position..]);
    }

    private static OpenXmlPartReader CreateReader(OpenXmlPart part) =>
        new(part, new OpenXmlPartReaderOptions
        {
            MaxCharactersInPart = FileFormatClassifier.MaxCharactersInPart,
        });

    private readonly record struct PendingCellValue(string? Text, int? SharedStringIndex);
}

public sealed class EvaluationPlanItem
{
    internal EvaluationPlanItem(
        int sequenceNumber,
        int sourceRowNumber,
        string questionId,
        string evaluatorId,
        string primarySourceColumn,
        IEnumerable<string> supportingSourceColumns)
    {
        SequenceNumber = sequenceNumber;
        SourceRowNumber = sourceRowNumber;
        QuestionId = questionId;
        EvaluatorId = evaluatorId;
        PrimarySourceColumn = primarySourceColumn;
        SupportingSourceColumns = supportingSourceColumns.ToImmutableArray();
        SelectedSourceColumns = SupportingSourceColumns.Prepend(PrimarySourceColumn).ToImmutableArray();
    }

    public int SequenceNumber { get; }

    public int SourceRowNumber { get; }

    public string QuestionId { get; }

    public string EvaluatorId { get; }

    public string PrimarySourceColumn { get; }

    public ImmutableArray<string> SupportingSourceColumns { get; }

    public ImmutableArray<string> SelectedSourceColumns { get; }

    public override string ToString() =>
        $"{nameof(EvaluationPlanItem)} {{ SequenceNumber = {SequenceNumber.ToString(CultureInfo.InvariantCulture)}, SourceRowNumber = {SourceRowNumber.ToString(CultureInfo.InvariantCulture)}, QuestionId = {QuestionId}, EvaluatorId = {EvaluatorId}, Content = <redacted> }}";
}

public sealed class EvaluationPlan
{
    internal EvaluationPlan(
        QuantificationSnapshot snapshot,
        ValidatedColumnMapping mapping,
        ImmutableArray<EvaluationPlanItem> items)
    {
        Snapshot = snapshot;
        Mapping = mapping;
        Items = items;
    }

    public QuantificationSnapshot Snapshot { get; }

    public string DefinitionSha256 => Snapshot.Sha256;

    public ValidatedColumnMapping Mapping { get; }

    public ImmutableArray<EvaluationPlanItem> Items { get; }

    public int TotalCount => Items.Length;

    public override string ToString() =>
        $"{nameof(EvaluationPlan)} {{ TotalCount = {TotalCount.ToString(CultureInfo.InvariantCulture)}, DefinitionSha256 = <redacted>, Content = <redacted> }}";
}

public sealed class EvaluationPlanBuilder
{
    public EvaluationPlan Build(
        QuantificationSnapshot snapshot,
        ValidatedColumnMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(mapping);
        if (!snapshot.HasValidHash())
        {
            throw new ArgumentException("The quantification snapshot hash is invalid.", nameof(snapshot));
        }

        ValidateMappingBinding(snapshot.Definition, mapping);
        int enabledEvaluatorsPerRow = snapshot.Definition.Questions
            .Where(question => question.Enabled)
            .Sum(question => question.Evaluators.Count(evaluator => evaluator.Enabled));
        long plannedCount = (long)mapping.SelectedRowCount * enabledEvaluatorsPerRow;
        if (plannedCount > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(mapping), "The evaluation plan is too large.");
        }

        ImmutableArray<EvaluationPlanItem>.Builder items =
            ImmutableArray.CreateBuilder<EvaluationPlanItem>((int)plannedCount);
        int sequenceNumber = 1;
        for (int sourceRow = mapping.FirstDataRow; sourceRow <= mapping.LastDataRow; sourceRow++)
        {
            foreach (QuestionDefinition question in snapshot.Definition.Questions.Where(question => question.Enabled))
            {
                foreach (EvaluatorDefinition evaluator in question.Evaluators.Where(evaluator => evaluator.Enabled))
                {
                    items.Add(new EvaluationPlanItem(
                        sequenceNumber++,
                        sourceRow,
                        question.Id,
                        evaluator.Id,
                        question.PrimarySourceColumn,
                        question.SupportingSourceColumns));
                }
            }
        }

        return new EvaluationPlan(snapshot, mapping, items.ToImmutable());
    }

    private static void ValidateMappingBinding(
        QuantificationDefinition definition,
        ValidatedColumnMapping mapping)
    {
        if (!string.Equals(definition.SourceSheet, mapping.SourceSheet, StringComparison.OrdinalIgnoreCase)
            || definition.HeaderRow != mapping.HeaderRow
            || definition.FirstDataRow != mapping.FirstDataRow
            || definition.LastDataRow != mapping.LastDataRow)
        {
            throw new ArgumentException("The validated column mapping does not match the run snapshot.", nameof(mapping));
        }

        Dictionary<string, ValidatedQuestionColumnMapping> mappedQuestions = new(StringComparer.Ordinal);
        foreach (ValidatedQuestionColumnMapping? mapped in mapping.Questions)
        {
            if (mapped is null || !mappedQuestions.TryAdd(mapped.QuestionId, mapped))
            {
                throw new ArgumentException("The validated column mapping does not match the run snapshot.", nameof(mapping));
            }
        }

        if (mappedQuestions.Count != definition.Questions.Length)
        {
            throw new ArgumentException("The validated column mapping does not match the run snapshot.", nameof(mapping));
        }

        foreach (QuestionDefinition question in definition.Questions)
        {
            if (!mappedQuestions.TryGetValue(question.Id, out ValidatedQuestionColumnMapping? mapped)
                || !string.Equals(
                    question.PrimarySourceColumn,
                    mapped.PrimarySourceColumn,
                    StringComparison.OrdinalIgnoreCase)
                || question.SupportingSourceColumns.Length != mapped.SupportingSourceColumns.Count)
            {
                throw new ArgumentException("The validated column mapping does not match the run snapshot.", nameof(mapping));
            }

            for (int index = 0; index < question.SupportingSourceColumns.Length; index++)
            {
                if (!string.Equals(
                    question.SupportingSourceColumns[index],
                    mapped.SupportingSourceColumns[index],
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("The validated column mapping does not match the run snapshot.", nameof(mapping));
                }
            }
        }
    }
}
