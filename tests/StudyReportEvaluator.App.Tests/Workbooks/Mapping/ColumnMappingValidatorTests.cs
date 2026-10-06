using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Mapping;

// Requirements: FR-004 (AC-004), FR-005 (AC-006)
public sealed class ColumnMappingValidatorTests
{
    private readonly WorkbookMetadataReader reader = new();
    private readonly ColumnMappingValidator validator = new();

    [Fact]
    public void Validates_student_Prompt_primaries_and_shared_columns_across_questions()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        WorkbookMetadata metadata = reader.Read(workbook.Path);
        QuantificationDefinition definition = CreateDefinition(
            "original",
            1,
            2,
            531,
            CreateQuestion("Q1", "j", ["k"]),
            CreateQuestion("Q2", "g", ["K"]));

        ColumnMappingValidationResult result = validator.Validate(metadata, definition);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        ValidatedColumnMapping mapping = Assert.IsType<ValidatedColumnMapping>(result.Mapping);
        Assert.Equal("Original", mapping.SourceSheet);
        Assert.Equal(530, mapping.SelectedRowCount);
        Assert.Equal(["J", "G"], mapping.Questions.Select(question => question.PrimarySourceColumn));
        Assert.All(mapping.Questions, question => Assert.Equal(["K"], question.SupportingSourceColumns));
    }

    [Fact]
    public void Candidate_lists_are_advisory_and_arbitrary_existing_columns_remain_valid()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        WorkbookMetadata metadata = reader.Read(workbook.Path);
        QuantificationDefinition definition = CreateDefinition(
            "Original",
            1,
            2,
            531,
            CreateQuestion("Q-ARBITRARY", "A", ["L"]));

        ColumnMappingValidationResult result = validator.Validate(metadata, definition);

        Assert.True(result.IsValid);
        ValidatedQuestionColumnMapping question = Assert.Single(result.Mapping!.Questions);
        Assert.Equal("A", question.PrimarySourceColumn);
        Assert.Equal(["L"], question.SupportingSourceColumns);
    }

    [Fact]
    public void Validated_mapping_preserves_the_exact_stable_question_id()
    {
        string questionId = "Q-" + new string('X', 96);
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        WorkbookMetadata metadata = reader.Read(workbook.Path);
        QuantificationDefinition definition = CreateDefinition(
            "Original",
            1,
            2,
            531,
            CreateQuestion(questionId, "F", []));

        ColumnMappingValidationResult result = validator.Validate(metadata, definition);

        Assert.True(result.IsValid);
        Assert.Equal(questionId, Assert.Single(result.Mapping!.Questions).QuestionId);
    }

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(2, 20001, true)]
    [InlineData(2, 20002, false)]
    [InlineData(3, 2, false)]
    public void Selected_row_count_is_limited_to_one_through_20000(
        int firstDataRow,
        int lastDataRow,
        bool expectedValid)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Rows",
            headerRow: 1,
            lastRow: 20002,
            lastColumn: 2,
            new X02Header(2, "レポート回答"));
        WorkbookMetadata metadata = reader.Read(workbook.Path);
        QuantificationDefinition definition = CreateDefinition(
            "Rows",
            1,
            firstDataRow,
            lastDataRow,
            CreateQuestion("Q-ROWS", "B", []));

        ColumnMappingValidationResult result = validator.Validate(metadata, definition);

        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            Assert.Contains(
                result.Errors,
                error => error.Code is "SELECTED_ROW_LIMIT_EXCEEDED" or "DATA_ROW_RANGE_REVERSED");
            Assert.Null(result.Mapping);
        }
    }

    [Fact]
    public void Selected_sheet_header_and_data_rows_must_match_the_metadata()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        WorkbookMetadata metadata = reader.Read(workbook.Path);

        ColumnMappingValidationResult missingSheet = validator.Validate(
            metadata,
            CreateDefinition(
                "Missing sheet",
                1,
                2,
                531,
                CreateQuestion("Q-SHEET", "F", [])));
        ColumnMappingValidationResult staleHeader = validator.Validate(
            metadata,
            CreateDefinition(
                "Original",
                2,
                3,
                531,
                CreateQuestion("Q-HEADER", "F", [])));
        ColumnMappingValidationResult outsideRows = validator.Validate(
            metadata,
            CreateDefinition(
                "Original",
                1,
                2,
                532,
                CreateQuestion("Q-DATA", "F", [])));

        Assert.Contains(missingSheet.Errors, error => error.Code == "SOURCE_SHEET_NOT_FOUND");
        Assert.Contains(staleHeader.Errors, error => error.Code == "HEADER_METADATA_MISMATCH");
        Assert.Contains(outsideRows.Errors, error => error.Code == "DATA_ROW_OUTSIDE_WORKSHEET");
        Assert.Null(missingSheet.Mapping);
        Assert.Null(staleHeader.Mapping);
        Assert.Null(outsideRows.Mapping);
    }

    [Fact]
    public void Primary_and_supporting_columns_must_exist_and_be_unique_within_each_question()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        WorkbookMetadata metadata = reader.Read(workbook.Path);
        QuantificationDefinition definition = CreateDefinition(
            "Original",
            1,
            2,
            531,
            CreateQuestion("Q-COLUMNS", "J", ["K", "k", "J", "M", "1", ""]));

        ColumnMappingValidationResult result = validator.Validate(metadata, definition);

        Assert.False(result.IsValid);
        Assert.Null(result.Mapping);
        Assert.Contains(result.Errors, error => error.Code == "DUPLICATE_SUPPORTING_COLUMN");
        Assert.Contains(result.Errors, error => error.Code == "PRIMARY_COLUMN_REUSED");
        Assert.Contains(
            result.Errors,
            error => error.Code == "SOURCE_COLUMN_NOT_FOUND" && error.SafeOffendingValue == "M");
        Assert.Contains(
            result.Errors,
            error => error.Code == "INVALID_SOURCE_COLUMN" && error.SafeOffendingValue == "1");
        Assert.Contains(result.Errors, error => error.Code == "SOURCE_COLUMN_REQUIRED");
    }

    [Fact]
    public void Primary_column_outside_the_selected_worksheet_is_rejected()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        WorkbookMetadata metadata = reader.Read(workbook.Path);
        QuantificationDefinition definition = CreateDefinition(
            "Original",
            1,
            2,
            531,
            CreateQuestion("Q-MISSING", "M", []));

        ColumnMappingValidationResult result = validator.Validate(metadata, definition);

        ColumnMappingValidationError error = Assert.Single(
            result.Errors,
            item => item.Code == "SOURCE_COLUMN_NOT_FOUND");
        Assert.Equal("PrimarySourceColumn", error.Field);
        Assert.Equal("M", error.SafeOffendingValue);
    }

    [Fact]
    public void Special_primary_and_supporting_columns_are_validated_against_the_selected_worksheet()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        WorkbookMetadata metadata = reader.Read(workbook.Path);
        QuestionDefinition question = CreateQuestion("Q-SPECIAL", "F", []) with
        {
            SpecialEvaluations =
            [
                new SpecialEvaluationDefinition
                {
                    Id = "S1",
                    DisplayName = "Special",
                    PrimarySourceColumn = "J",
                    SupportingSourceColumns = ["K", "k", "J", "M"],
                    PromptTemplate = "Evaluate {回答}",
                },
            ],
        };

        ColumnMappingValidationResult result = validator.Validate(
            metadata,
            CreateDefinition("Original", 1, 2, 531, question));

        Assert.False(result.IsValid);
        Assert.Null(result.Mapping);
        Assert.Contains(result.Errors, error => error.QuestionId == "S1"
            && error.Code == "DUPLICATE_SUPPORTING_COLUMN");
        Assert.Contains(result.Errors, error => error.QuestionId == "S1"
            && error.Code == "PRIMARY_COLUMN_REUSED");
        Assert.Contains(result.Errors, error => error.QuestionId == "S1"
            && error.Code == "SOURCE_COLUMN_NOT_FOUND"
            && error.SafeOffendingValue == "M");
    }

    [Fact]
    public void Invalid_mapping_errors_do_not_echo_question_or_Prompt_bodies()
    {
        const string questionBodyCanary = "QUESTION-BODY-CANARY";
        const string promptBodyCanary = "PROMPT-BODY-CANARY";
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        WorkbookMetadata metadata = reader.Read(workbook.Path);
        QuestionDefinition question = CreateQuestion("Q-SAFE", "M", []) with
        {
            QuestionText = questionBodyCanary,
            Evaluators =
            [
                CreateQuestion("Q-NESTED", "F", []).Evaluators[0] with
                {
                    CustomPromptTemplate = promptBodyCanary,
                },
            ],
        };

        ColumnMappingValidationResult result = validator.Validate(
            metadata,
            CreateDefinition("Original", 1, 2, 531, question));
        string rendered = string.Join(
            '|',
            result.Errors.Select(error => string.Join(
                ':',
                error.Code,
                error.Path,
                error.QuestionId,
                error.Field,
                error.SafeOffendingValue,
                error.ToString())));

        Assert.DoesNotContain(questionBodyCanary, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(promptBodyCanary, rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Validated_mapping_and_error_collections_are_copy_safe_and_representations_are_redacted()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        WorkbookMetadata metadata = reader.Read(workbook.Path);
        QuantificationDefinition validDefinition = CreateDefinition(
            "Original",
            1,
            2,
            531,
            CreateQuestion("QUESTION-ID-CANARY", "J", ["K"]));
        ColumnMappingValidationResult valid = validator.Validate(metadata, validDefinition);
        ValidatedColumnMapping mapping = valid.Mapping!;
        ValidatedQuestionColumnMapping question = Assert.Single(mapping.Questions);

        AssertReadOnly(mapping.Questions, question);
        AssertReadOnly(question.SupportingSourceColumns, "Z");
        AssertRedacted(valid.ToString(), "Original", "QUESTION-ID-CANARY", "J", "K");
        AssertRedacted(mapping.ToString(), "Original", "QUESTION-ID-CANARY", "J", "K");
        AssertRedacted(question.ToString(), "Original", "QUESTION-ID-CANARY", "J", "K");

        ColumnMappingValidationResult invalid = validator.Validate(
            metadata,
            validDefinition with
            {
                Questions = [validDefinition.Questions[0] with { PrimarySourceColumn = "XFD" }],
            });
        ColumnMappingValidationError error = Assert.Single(invalid.Errors);
        AssertReadOnly(invalid.Errors, error);
        AssertRedacted(error.ToString(), "Original", "QUESTION-ID-CANARY", "XFD");
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        WorkbookMetadata metadata = reader.Read(workbook.Path);
        QuantificationDefinition definition = CreateDefinition(
            "Original",
            1,
            2,
            531,
            CreateQuestion("Q1", "F", []));

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, definition));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(metadata, null!));
    }

    private static QuantificationDefinition CreateDefinition(
        string sourceSheet,
        int headerRow,
        int firstDataRow,
        int lastDataRow,
        params QuestionDefinition[] questions) =>
        new()
        {
            Id = "DEF-MAPPING",
            Name = "Synthetic mapping definition",
            Revision = "1",
            SourceSheet = sourceSheet,
            HeaderRow = headerRow,
            FirstDataRow = firstDataRow,
            LastDataRow = lastDataRow,
            Questions = [.. questions],
        };

    private static QuestionDefinition CreateQuestion(
        string id,
        string primary,
        string[] supporting)
    {
        CriterionDefinition criterion = new()
        {
            Id = "C-" + id,
            DisplayName = "Synthetic criterion",
            Description = "Fixed synthetic criterion description",
            Weight = 1m,
        };
        EvaluatorDefinition evaluator = new()
        {
            Id = "E-" + id,
            DisplayName = "Synthetic Custom evaluator",
            Type = EvaluatorType.CustomPrompt,
            Weight = 1m,
            Range = new ScoreRange(0m, 10m),
            Criteria = [criterion],
            CustomPromptTemplate = "{回答} {評価項目}",
        };
        return new QuestionDefinition
        {
            Id = id,
            DisplayName = "Synthetic question " + id,
            QuestionText = "Fixed synthetic question text",
            PrimarySourceColumn = primary,
            SupportingSourceColumns = [.. supporting],
            Points = 1m,
            Evaluators = [evaluator],
        };
    }

    private static void AssertReadOnly<T>(IReadOnlyList<T> values, T attemptedValue)
    {
        IList<T> list = Assert.IsAssignableFrom<IList<T>>(values);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(attemptedValue));
    }

    private static void AssertRedacted(string representation, params string[] forbiddenValues)
    {
        Assert.Contains("<redacted>", representation, StringComparison.Ordinal);
        foreach (string forbiddenValue in forbiddenValues)
        {
            Assert.DoesNotContain(forbiddenValue, representation, StringComparison.Ordinal);
        }
    }
}