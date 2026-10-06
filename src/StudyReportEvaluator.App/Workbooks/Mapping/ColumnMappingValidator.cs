using System.Globalization;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.App.Workbooks.Mapping;

public sealed class ColumnMappingValidationError
{
    internal ColumnMappingValidationError(
        string code,
        string path,
        string questionId,
        string field,
        string safeOffendingValue)
    {
        Code = code;
        Path = path;
        QuestionId = questionId;
        Field = field;
        SafeOffendingValue = safeOffendingValue;
    }

    public string Code { get; }

    public string Path { get; }

    public string QuestionId { get; }

    public string Field { get; }

    public string SafeOffendingValue { get; }

    public override string ToString() =>
        $"{nameof(ColumnMappingValidationError)} {{ Code = {Code}, Content = <redacted> }}";
}

public sealed class ValidatedQuestionColumnMapping
{
    internal ValidatedQuestionColumnMapping(
        string questionId,
        string primarySourceColumn,
        IEnumerable<string> supportingSourceColumns)
    {
        QuestionId = questionId;
        PrimarySourceColumn = primarySourceColumn;
        SupportingSourceColumns = Array.AsReadOnly(supportingSourceColumns.ToArray());
    }

    public string QuestionId { get; }

    public string PrimarySourceColumn { get; }

    public IReadOnlyList<string> SupportingSourceColumns { get; }

    public override string ToString() =>
        $"{nameof(ValidatedQuestionColumnMapping)} {{ SupportingColumnCount = {SupportingSourceColumns.Count}, Content = <redacted> }}";
}

public sealed class ValidatedColumnMapping
{
    internal ValidatedColumnMapping(
        string sourceSheet,
        int headerRow,
        int firstDataRow,
        int lastDataRow,
        IEnumerable<ValidatedQuestionColumnMapping> questions)
    {
        SourceSheet = sourceSheet;
        HeaderRow = headerRow;
        FirstDataRow = firstDataRow;
        LastDataRow = lastDataRow;
        Questions = Array.AsReadOnly(questions.ToArray());
    }

    public string SourceSheet { get; }

    public int HeaderRow { get; }

    public int FirstDataRow { get; }

    public int LastDataRow { get; }

    public int SelectedRowCount => LastDataRow - FirstDataRow + 1;

    public IReadOnlyList<ValidatedQuestionColumnMapping> Questions { get; }

    public override string ToString() =>
        $"{nameof(ValidatedColumnMapping)} {{ SelectedRowCount = {SelectedRowCount}, QuestionCount = {Questions.Count}, Content = <redacted> }}";
}

public sealed class ColumnMappingValidationResult
{
    internal ColumnMappingValidationResult(
        IEnumerable<ColumnMappingValidationError> errors,
        ValidatedColumnMapping? mapping)
    {
        Errors = Array.AsReadOnly(errors.ToArray());
        Mapping = mapping;
    }

    public IReadOnlyList<ColumnMappingValidationError> Errors { get; }

    public ValidatedColumnMapping? Mapping { get; }

    public bool IsValid => Errors.Count == 0 && Mapping is not null;

    public override string ToString() =>
        $"{nameof(ColumnMappingValidationResult)} {{ IsValid = {IsValid}, ErrorCount = {Errors.Count}, Content = <redacted> }}";
}

public sealed class ColumnMappingValidator
{
    public const int MaximumSelectedRows = QuantificationDefinitionValidator.MaximumSelectedRows;
    public const int MaximumExcelRow = QuantificationDefinitionValidator.MaximumExcelRow;
    public const int MaximumExcelColumn = QuantificationDefinitionValidator.MaximumExcelColumn;

    public ColumnMappingValidationResult Validate(
        WorkbookMetadata metadata,
        QuantificationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(definition);

        List<ColumnMappingValidationError> errors = [];
        WorksheetMetadata? worksheet = ResolveWorksheet(metadata, definition, errors);
        ValidateRows(metadata, worksheet, definition, errors);
        List<ValidatedQuestionColumnMapping> questions = ValidateQuestions(
            worksheet,
            definition,
            errors);

        ValidatedColumnMapping? mapping = errors.Count == 0 && worksheet is not null
            ? new ValidatedColumnMapping(
                worksheet.Name,
                definition.HeaderRow,
                definition.FirstDataRow,
                definition.LastDataRow,
                questions)
            : null;
        return new ColumnMappingValidationResult(errors, mapping);
    }

    private static WorksheetMetadata? ResolveWorksheet(
        WorkbookMetadata metadata,
        QuantificationDefinition definition,
        List<ColumnMappingValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(definition.SourceSheet))
        {
            Add(
                errors,
                "SOURCE_SHEET_REQUIRED",
                "$",
                "<definition>",
                "SourceSheet",
                "<blank>");
            return null;
        }

        WorksheetMetadata? worksheet = metadata.Worksheets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, definition.SourceSheet, StringComparison.OrdinalIgnoreCase));
        if (worksheet is null)
        {
            Add(
                errors,
                "SOURCE_SHEET_NOT_FOUND",
                "$",
                "<definition>",
                "SourceSheet",
                SafeScalar(definition.SourceSheet));
        }

        return worksheet;
    }

    private static void ValidateRows(
        WorkbookMetadata metadata,
        WorksheetMetadata? worksheet,
        QuantificationDefinition definition,
        List<ColumnMappingValidationError> errors)
    {
        bool headerInExcel = ValidateExcelRow(
            definition.HeaderRow,
            "HeaderRow",
            definition,
            errors);
        bool firstInExcel = ValidateExcelRow(
            definition.FirstDataRow,
            "FirstDataRow",
            definition,
            errors);
        bool lastInExcel = ValidateExcelRow(
            definition.LastDataRow,
            "LastDataRow",
            definition,
            errors);

        if (headerInExcel && (uint)definition.HeaderRow != metadata.HeaderRowNumber)
        {
            Add(
                errors,
                "HEADER_METADATA_MISMATCH",
                "$",
                SafeIdentity(definition.Id),
                "HeaderRow",
                Invariant(definition.HeaderRow));
        }

        if (headerInExcel
            && worksheet is not null
            && !ContainsRow(worksheet, definition.HeaderRow))
        {
            Add(
                errors,
                "HEADER_ROW_OUTSIDE_WORKSHEET",
                "$",
                SafeIdentity(definition.Id),
                "HeaderRow",
                Invariant(definition.HeaderRow));
        }

        if (!headerInExcel || !firstInExcel || !lastInExcel)
        {
            return;
        }

        if (definition.FirstDataRow <= definition.HeaderRow)
        {
            Add(
                errors,
                "DATA_ROW_NOT_AFTER_HEADER",
                "$",
                SafeIdentity(definition.Id),
                "FirstDataRow",
                Invariant(definition.FirstDataRow));
        }

        if (definition.LastDataRow < definition.FirstDataRow)
        {
            Add(
                errors,
                "DATA_ROW_RANGE_REVERSED",
                "$",
                SafeIdentity(definition.Id),
                "LastDataRow",
                Invariant(definition.LastDataRow));
            return;
        }

        long selectedRows = (long)definition.LastDataRow - definition.FirstDataRow + 1;
        if (selectedRows > MaximumSelectedRows)
        {
            Add(
                errors,
                "SELECTED_ROW_LIMIT_EXCEEDED",
                "$",
                SafeIdentity(definition.Id),
                "SelectedRowCount",
                Invariant(selectedRows));
        }

        if (worksheet is not null
            && (!ContainsRow(worksheet, definition.FirstDataRow)
                || !ContainsRow(worksheet, definition.LastDataRow)))
        {
            Add(
                errors,
                "DATA_ROW_OUTSIDE_WORKSHEET",
                "$",
                SafeIdentity(definition.Id),
                "DataRows",
                $"{Invariant(definition.FirstDataRow)}..{Invariant(definition.LastDataRow)}");
        }
    }

    private static List<ValidatedQuestionColumnMapping> ValidateQuestions(
        WorksheetMetadata? worksheet,
        QuantificationDefinition definition,
        List<ColumnMappingValidationError> errors)
    {
        List<ValidatedQuestionColumnMapping> validated = [];
        if (definition.Questions.IsDefault)
        {
            return validated;
        }

        for (int questionIndex = 0; questionIndex < definition.Questions.Length; questionIndex++)
        {
            QuestionDefinition? question = definition.Questions[questionIndex];
            string path = $"$.questions[{questionIndex.ToString(CultureInfo.InvariantCulture)}]";
            if (question is null)
            {
                Add(errors, "NULL_QUESTION", path, "<null>", "Question", "<null>");
                continue;
            }

            ValidateQuestion(worksheet, question, path, errors, validated);
        }

        return validated;
    }

    private static void ValidateQuestion(
        WorksheetMetadata? worksheet,
        QuestionDefinition question,
        string path,
        List<ColumnMappingValidationError> errors,
        List<ValidatedQuestionColumnMapping> validated)
    {
        int initialErrorCount = errors.Count;
        string questionId = question.Id ?? string.Empty;
        string safeQuestionId = SafeIdentity(question.Id);
        string? primary = ValidateColumn(
            worksheet,
            question.PrimarySourceColumn,
            path,
            safeQuestionId,
            "PrimarySourceColumn",
            errors);

        List<string> supporting = [];
        HashSet<string> uniqueSupporting = new(StringComparer.OrdinalIgnoreCase);
        if (!question.SupportingSourceColumns.IsDefault)
        {
            for (int index = 0; index < question.SupportingSourceColumns.Length; index++)
            {
                string? sourceColumn = question.SupportingSourceColumns[index];
                string field = $"SupportingSourceColumns[{index.ToString(CultureInfo.InvariantCulture)}]";
                string? column = ValidateColumn(
                    worksheet,
                    sourceColumn,
                    path,
                    safeQuestionId,
                    field,
                    errors);
                if (string.IsNullOrWhiteSpace(sourceColumn))
                {
                    continue;
                }

                if (!uniqueSupporting.Add(sourceColumn))
                {
                    Add(
                        errors,
                        "DUPLICATE_SUPPORTING_COLUMN",
                        path,
                        safeQuestionId,
                        field,
                        SafeScalar(sourceColumn));
                }

                if (string.Equals(
                    question.PrimarySourceColumn,
                    sourceColumn,
                    StringComparison.OrdinalIgnoreCase))
                {
                    Add(
                        errors,
                        "PRIMARY_COLUMN_REUSED",
                        path,
                        safeQuestionId,
                        field,
                        SafeScalar(sourceColumn));
                }

                if (column is not null)
                {
                    supporting.Add(column);
                }
            }
        }

        if (!question.SpecialEvaluations.IsDefault)
        {
            for (int specialIndex = 0; specialIndex < question.SpecialEvaluations.Length; specialIndex++)
            {
                SpecialEvaluationDefinition? special = question.SpecialEvaluations[specialIndex];
                if (special is null)
                {
                    Add(errors, "NULL_SPECIAL_EVALUATION", path, safeQuestionId, "SpecialEvaluations", "<null>");
                    continue;
                }

                string specialPath = $"{path}.specialEvaluations[{specialIndex.ToString(CultureInfo.InvariantCulture)}]";
                ValidateColumn(
                    worksheet,
                    special.PrimarySourceColumn,
                    specialPath,
                    SafeIdentity(special.Id),
                    "PrimarySourceColumn",
                    errors);
                HashSet<string> uniqueSpecialSupporting = new(StringComparer.OrdinalIgnoreCase);
                foreach ((string? sourceColumn, int supportingIndex) in special.SupportingSourceColumns
                             .Select((column, index) => (column, index)))
                {
                    string field = $"SupportingSourceColumns[{supportingIndex.ToString(CultureInfo.InvariantCulture)}]";
                    ValidateColumn(
                        worksheet,
                        sourceColumn,
                        specialPath,
                        SafeIdentity(special.Id),
                        field,
                        errors);
                    if (string.IsNullOrWhiteSpace(sourceColumn))
                    {
                        continue;
                    }

                    if (!uniqueSpecialSupporting.Add(sourceColumn))
                    {
                        Add(errors, "DUPLICATE_SUPPORTING_COLUMN", specialPath, SafeIdentity(special.Id), field, SafeScalar(sourceColumn));
                    }

                    if (string.Equals(special.PrimarySourceColumn, sourceColumn, StringComparison.OrdinalIgnoreCase))
                    {
                        Add(errors, "PRIMARY_COLUMN_REUSED", specialPath, SafeIdentity(special.Id), field, SafeScalar(sourceColumn));
                    }
                }
            }
        }

        if (errors.Count == initialErrorCount && primary is not null)
        {
            validated.Add(new ValidatedQuestionColumnMapping(
                questionId,
                primary,
                supporting));
        }
    }

    private static string? ValidateColumn(
        WorksheetMetadata? worksheet,
        string? sourceColumn,
        string path,
        string questionId,
        string field,
        List<ColumnMappingValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(sourceColumn))
        {
            Add(errors, "SOURCE_COLUMN_REQUIRED", path, questionId, field, "<blank>");
            return null;
        }

        if (!TryGetExcelColumn(sourceColumn, out uint columnIndex, out string canonicalColumn))
        {
            Add(
                errors,
                "INVALID_SOURCE_COLUMN",
                path,
                questionId,
                field,
                SafeScalar(sourceColumn));
            return null;
        }

        if (worksheet is not null
            && (columnIndex < worksheet.FirstColumnIndex
                || columnIndex > worksheet.LastColumnIndex))
        {
            Add(
                errors,
                "SOURCE_COLUMN_NOT_FOUND",
                path,
                questionId,
                field,
                canonicalColumn);
            return null;
        }

        return canonicalColumn;
    }

    private static bool ValidateExcelRow(
        int row,
        string field,
        QuantificationDefinition definition,
        List<ColumnMappingValidationError> errors)
    {
        if (row is >= 1 and <= MaximumExcelRow)
        {
            return true;
        }

        Add(
            errors,
            "ROW_OUT_OF_RANGE",
            "$",
            SafeIdentity(definition.Id),
            field,
            Invariant(row));
        return false;
    }

    private static bool ContainsRow(WorksheetMetadata worksheet, int row) =>
        row >= worksheet.FirstRowIndex && row <= worksheet.LastRowIndex;

    private static bool TryGetExcelColumn(
        string sourceColumn,
        out uint columnIndex,
        out string canonicalColumn)
    {
        columnIndex = 0;
        canonicalColumn = string.Empty;
        if (sourceColumn.Length is < 1 or > 3)
        {
            return false;
        }

        Span<char> canonical = stackalloc char[sourceColumn.Length];
        for (int index = 0; index < sourceColumn.Length; index++)
        {
            char value = sourceColumn[index];
            if (!char.IsAsciiLetter(value))
            {
                return false;
            }

            char upper = char.ToUpperInvariant(value);
            canonical[index] = upper;
            columnIndex = (columnIndex * 26) + (uint)(upper - 'A' + 1);
        }

        if (columnIndex > MaximumExcelColumn)
        {
            return false;
        }

        canonicalColumn = new string(canonical);
        return true;
    }

    private static void Add(
        List<ColumnMappingValidationError> errors,
        string code,
        string path,
        string questionId,
        string field,
        string safeOffendingValue) =>
        errors.Add(new ColumnMappingValidationError(
            code,
            path,
            questionId,
            field,
            safeOffendingValue));

    private static string SafeIdentity(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "<blank>" : SafeScalar(value);

    private static string SafeScalar(string value) => value.Length <= 64
        ? value
        : $"{value[..64]}…(length={value.Length.ToString(CultureInfo.InvariantCulture)})";

    private static string Invariant(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(long value) =>
        value.ToString(CultureInfo.InvariantCulture);
}