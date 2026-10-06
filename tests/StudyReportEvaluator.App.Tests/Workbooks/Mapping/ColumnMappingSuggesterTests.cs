using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Reading;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Mapping;

// Requirements: FR-004 (AC-004)
public sealed class ColumnMappingSuggesterTests
{
    private readonly WorkbookMetadataReader reader = new();
    private readonly ColumnMappingSuggester suggester = new();

    [Fact]
    public void Sample_like_headers_suggest_Original_F_through_K_without_fixed_mapping()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        WorkbookMetadata metadata = reader.Read(workbook.Path);

        ColumnMappingSuggestionResult result = suggester.Suggest(metadata);

        WorksheetMappingSuggestion? suggestion = result.SuggestedWorksheet;
        Assert.NotNull(suggestion);
        Assert.Equal("Original", result.SuggestedWorksheetName);
        Assert.Equal(1U, suggestion!.HeaderRow);
        Assert.Equal(2U, suggestion.FirstDataRow);
        Assert.Equal(531U, suggestion.LastDataRow);
        Assert.Equal(530U, suggestion.SuggestedDataRowCount);
        Assert.Equal(["F", "G", "H", "I", "J", "K"], suggestion.InitialTargetColumns);
        Assert.Equal(["A", "B", "C", "D", "E", "L"], suggestion.InitiallyUnselectedColumns);
        Assert.True(Assert.Single(
            result.WorksheetSuggestions,
            worksheet => worksheet.WorksheetName == "Final").Candidates.Count > suggestion.Candidates.Count);

        AssertRoles(suggestion, "F", ColumnMappingCandidateRole.PrimaryAnswer);
        AssertRoles(
            suggestion,
            "G",
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.StudentPromptPrimary);
        AssertRoles(
            suggestion,
            "H",
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.Supporting);
        AssertRoles(suggestion, "I", ColumnMappingCandidateRole.PrimaryAnswer);
        ColumnMappingCandidate prompt = AssertRoles(
            suggestion,
            "J",
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.StudentPromptPrimary);
        Assert.Equal(["K"], prompt.SuggestedSupportingColumns);
        AssertRoles(suggestion, "K", ColumnMappingCandidateRole.Supporting);
        Assert.Empty(Assert.Single(suggestion.Candidates, candidate => candidate.ColumnName == "G").SuggestedSupportingColumns);
    }

    [Fact]
    public void Normalized_Japanese_and_full_width_headers_drive_roles_at_arbitrary_columns_and_header_row()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Responses 2026",
            headerRow: 4,
            lastRow: 12,
            lastColumn: 14,
            new X02Header(2, "名前"),
            new X02Header(3, "　レポート：回答　"),
            new X02Header(13, "学生　ＰＲＯＭＰＴ"),
            new X02Header(14, "Ｐｒｏｍｐｔ作成時：工夫／観点"));
        WorkbookMetadata metadata = reader.Read(workbook.Path, headerRowNumber: 4);

        ColumnMappingSuggestionResult result = suggester.Suggest(metadata);

        WorksheetMappingSuggestion? suggestion = result.SuggestedWorksheet;
        Assert.NotNull(suggestion);
        Assert.Equal("Responses 2026", suggestion!.WorksheetName);
        Assert.Equal(4U, suggestion.HeaderRow);
        Assert.Equal(5U, suggestion.FirstDataRow);
        Assert.Equal(["C", "M", "N"], suggestion.InitialTargetColumns);
        Assert.True(Assert.Single(suggestion.Candidates, candidate => candidate.ColumnName == "M").IsStudentPromptPrimaryCandidate);
        Assert.Equal(
            ["N"],
            Assert.Single(suggestion.Candidates, candidate => candidate.ColumnName == "M").SuggestedSupportingColumns);
    }

    [Theory]
    [InlineData(1U)]
    [InlineData(2U)]
    public void Microsoft_Forms_like_profile_suggests_roles_at_question_rows(
        uint questionTextRow)
    {
        using X02TemporaryWorkbook workbook =
            X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(questionTextRow);

        WorksheetMappingSuggestion suggestion = Assert.IsType<WorksheetMappingSuggestion>(
            suggester.Suggest(reader.Read(workbook.Path, questionTextRow)).SuggestedWorksheet);

        Assert.Equal(questionTextRow, suggestion.HeaderRow);
        Assert.Equal(questionTextRow + 1, suggestion.FirstDataRow);
        Assert.Equal(["F", "G", "H", "I", "J", "K"], suggestion.InitialTargetColumns);
        Assert.Equal(["A", "B", "C", "D", "E", "L"], suggestion.InitiallyUnselectedColumns);
        AssertRoles(suggestion, "F", ColumnMappingCandidateRole.PrimaryAnswer);
        AssertRoles(
            suggestion,
            "G",
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.StudentPromptPrimary);
        AssertRoles(
            suggestion,
            "H",
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.Supporting);
        AssertRoles(suggestion, "I", ColumnMappingCandidateRole.PrimaryAnswer);
        ColumnMappingCandidate prompt = AssertRoles(
            suggestion,
            "J",
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.StudentPromptPrimary);
        Assert.Equal(["K"], prompt.SuggestedSupportingColumns);
        AssertRoles(suggestion, "K", ColumnMappingCandidateRole.Supporting);
    }

    [Theory]
    [InlineData(1U)]
    [InlineData(2U)]
    public void Google_Forms_like_profile_suggests_roles_at_question_rows(
        uint questionTextRow)
    {
        using X02TemporaryWorkbook workbook =
            X02SyntheticWorkbookFactory.CreateGoogleFormsLikeProfile(questionTextRow);

        WorksheetMappingSuggestion suggestion = Assert.IsType<WorksheetMappingSuggestion>(
            suggester.Suggest(reader.Read(workbook.Path, questionTextRow)).SuggestedWorksheet);

        Assert.Equal(questionTextRow, suggestion.HeaderRow);
        Assert.Equal(questionTextRow + 1, suggestion.FirstDataRow);
        Assert.Equal(["C", "D", "E", "F", "G"], suggestion.InitialTargetColumns);
        Assert.Equal(["A", "B"], suggestion.InitiallyUnselectedColumns);
        AssertRoles(suggestion, "C", ColumnMappingCandidateRole.PrimaryAnswer);
        AssertRoles(
            suggestion,
            "D",
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.StudentPromptPrimary);
        AssertRoles(suggestion, "E", ColumnMappingCandidateRole.PrimaryAnswer);
        ColumnMappingCandidate prompt = AssertRoles(
            suggestion,
            "F",
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.StudentPromptPrimary);
        Assert.Equal(["G"], prompt.SuggestedSupportingColumns);
        AssertRoles(suggestion, "G", ColumnMappingCandidateRole.Supporting);
    }

    [Fact]
    public void Student_prompt_semantics_infer_the_adjacent_unclassified_answer_at_arbitrary_columns()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Responses",
            headerRow: 3,
            lastRow: 12,
            lastColumn: 22,
            new X02Header(20, "Course-specific long response field"),
            new X02Header(21, "Student Prompt"),
            new X02Header(22, "Prompt rationale and viewpoint"));

        WorksheetMappingSuggestion suggestion = Assert.IsType<WorksheetMappingSuggestion>(
            suggester.Suggest(reader.Read(workbook.Path, 3)).SuggestedWorksheet);

        AssertRoles(suggestion, "T", ColumnMappingCandidateRole.PrimaryAnswer);
        ColumnMappingCandidate prompt = AssertRoles(
            suggestion,
            "U",
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.StudentPromptPrimary);
        Assert.Equal(["V"], prompt.SuggestedSupportingColumns);
    }

    [Fact]
    public void Management_header_before_a_student_prompt_is_never_inferred_as_an_answer()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Responses",
            headerRow: 1,
            lastRow: 4,
            lastColumn: 4,
            new X02Header(2, "Email address"),
            new X02Header(3, "Student Prompt"));

        WorksheetMappingSuggestion suggestion = Assert.IsType<WorksheetMappingSuggestion>(
            suggester.Suggest(reader.Read(workbook.Path)).SuggestedWorksheet);

        Assert.DoesNotContain(suggestion.Candidates, candidate => candidate.ColumnName == "B");
        AssertRoles(
            suggestion,
            "C",
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.StudentPromptPrimary);
    }

    [Fact]
    public void Submitter_name_before_a_student_prompt_is_never_inferred_as_an_answer()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Responses",
            headerRow: 1,
            lastRow: 4,
            lastColumn: 4,
            new X02Header(2, "提出者名"),
            new X02Header(3, "学生 Prompt"));

        WorksheetMappingSuggestion suggestion = Assert.IsType<WorksheetMappingSuggestion>(
            suggester.Suggest(reader.Read(workbook.Path)).SuggestedWorksheet);

        Assert.DoesNotContain(suggestion.Candidates, candidate => candidate.ColumnName == "B");
        AssertRoles(
            suggestion,
            "C",
            ColumnMappingCandidateRole.PrimaryAnswer | ColumnMappingCandidateRole.StudentPromptPrimary);
    }

    [Fact]
    public void Sample_positions_and_Original_name_alone_do_not_force_unknown_headers()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original",
            headerRow: 1,
            lastRow: 20,
            lastColumn: 12,
            new X02Header(6, "Synthetic field six"),
            new X02Header(7, "Synthetic field seven"),
            new X02Header(8, "Synthetic field eight"),
            new X02Header(9, "Synthetic field nine"),
            new X02Header(10, "Synthetic field ten"),
            new X02Header(11, "Synthetic field eleven"));
        WorkbookMetadata metadata = reader.Read(workbook.Path);

        ColumnMappingSuggestionResult result = suggester.Suggest(metadata);

        Assert.Null(result.SuggestedWorksheet);
        WorksheetMappingSuggestion worksheet = Assert.Single(result.WorksheetSuggestions);
        Assert.Empty(worksheet.Candidates);
        Assert.Empty(worksheet.InitialTargetColumns);
        Assert.Equal(
            ["A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L"],
            worksheet.InitiallyUnselectedColumns);
    }

    [Fact]
    public void Ambiguous_equally_semantic_sheets_are_not_forced_without_the_Original_tie_breaker()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.Create(
            new X02SheetSpec(
                "Attempt A",
                1,
                5,
                2,
                [new X02Header(2, "レポート回答")]),
            new X02SheetSpec(
                "Attempt B",
                1,
                5,
                2,
                [new X02Header(2, "レポート回答")]));
        WorkbookMetadata metadata = reader.Read(workbook.Path);

        ColumnMappingSuggestionResult result = suggester.Suggest(metadata);

        Assert.Null(result.SuggestedWorksheet);
        Assert.All(result.WorksheetSuggestions, suggestion => Assert.True(suggestion.HasConfidentMapping));
    }

    [Fact]
    public void Public_collections_are_read_only_copies()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        ColumnMappingSuggestionResult result = suggester.Suggest(reader.Read(workbook.Path));
        WorksheetMappingSuggestion suggestion = result.SuggestedWorksheet!;
        ColumnMappingCandidate prompt = Assert.Single(
            suggestion.Candidates,
            candidate => candidate.ColumnName == "J");

        AssertReadOnly(result.WorksheetSuggestions, suggestion);
        AssertReadOnly(suggestion.Candidates, prompt);
        AssertReadOnly(suggestion.InitialTargetColumns, "Z");
        AssertReadOnly(suggestion.InitiallyUnselectedColumns, "Z");
        AssertReadOnly(prompt.SuggestedSupportingColumns, "Z");
        Assert.Equal(["K"], prompt.SuggestedSupportingColumns);
    }

    [Fact]
    public void Public_representations_redact_sheet_header_and_column_identifiers()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "SHEET-CANARY",
            headerRow: 1,
            lastRow: 3,
            lastColumn: 2,
            new X02Header(2, "HEADER-CANARY レポート回答"));
        ColumnMappingSuggestionResult result = suggester.Suggest(reader.Read(workbook.Path));
        WorksheetMappingSuggestion suggestion = result.SuggestedWorksheet!;
        ColumnMappingCandidate candidate = Assert.Single(suggestion.Candidates);

        AssertRedacted(result.ToString(), "SHEET-CANARY", "HEADER-CANARY", "B");
        AssertRedacted(suggestion.ToString(), "SHEET-CANARY", "HEADER-CANARY", "B");
        AssertRedacted(candidate.ToString(), "SHEET-CANARY", "HEADER-CANARY", "B");
    }

    [Fact]
    public void Null_metadata_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => suggester.Suggest(null!));
    }

    private static ColumnMappingCandidate AssertRoles(
        WorksheetMappingSuggestion suggestion,
        string column,
        ColumnMappingCandidateRole expected)
    {
        ColumnMappingCandidate candidate = Assert.Single(
            suggestion.Candidates,
            item => item.ColumnName == column);
        Assert.Equal(expected, candidate.Roles);
        return candidate;
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

internal sealed record X02Header(uint ColumnIndex, string Value);

internal sealed record X02SheetSpec(
    string Name,
    uint HeaderRow,
    uint LastRow,
    uint LastColumn,
    IReadOnlyList<X02Header> Headers);

internal sealed class X02TemporaryWorkbook(string directory, string path) : IDisposable
{
    public string Directory { get; } = directory;

    public string Path { get; } = path;

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}

internal static class X02SyntheticWorkbookFactory
{
    // Synthetic structural profile, not an actual export fixture.
    // https://support.microsoft.com/en-us/office/check-and-share-your-form-results-02859424-341d-406f-b32a-9a0fbaf357af
    internal static X02TemporaryWorkbook CreateMicrosoftFormsLikeProfile(uint headerRow) =>
        CreateSingleSheet(
            "Responses",
            headerRow,
            lastRow: headerRow + 10,
            lastColumn: 12,
            new X02Header(1, "Response ID"),
            new X02Header(2, "Start time"),
            new X02Header(3, "Completion time"),
            new X02Header(4, "Email"),
            new X02Header(5, "Name"),
            new X02Header(6, "Report answer 1"),
            new X02Header(7, "Student Prompt 1"),
            new X02Header(8, "Question"),
            new X02Header(9, "Report answer 2"),
            new X02Header(10, "Student Prompt 2"),
            new X02Header(11, "Prompt considerations and viewpoint"),
            new X02Header(12, "Feedback"));

    // Synthetic structural profile, not an actual export fixture.
    // https://developers.google.com/apps-script/guides/triggers/events#google_sheets_events
    internal static X02TemporaryWorkbook CreateGoogleFormsLikeProfile(uint headerRow) =>
        CreateSingleSheet(
            "Responses",
            headerRow,
            lastRow: headerRow + 10,
            lastColumn: 7,
            new X02Header(1, "Timestamp"),
            new X02Header(2, "Email Address"),
            new X02Header(3, "Report answer 1"),
            new X02Header(4, "Prompt used for report 1"),
            new X02Header(5, "Report answer 2"),
            new X02Header(6, "Prompt used for report 2"),
            new X02Header(7, "Prompt considerations and viewpoint"));

    internal static X02TemporaryWorkbook CreateSampleLike() => Create(
        new X02SheetSpec(
            "Old",
            1,
            531,
            32,
            [new X02Header(1, "Synthetic archive field")]),
        new X02SheetSpec(
            "Original",
            1,
            531,
            12,
            [
                new X02Header(1, "回答 ID"),
                new X02Header(2, "開始時刻"),
                new X02Header(3, "完了時刻"),
                new X02Header(4, "電子メール"),
                new X02Header(5, "氏名"),
                new X02Header(6, "質問 1 のレポート回答"),
                new X02Header(7, "質問 1 の学生プロンプト"),
                new X02Header(8, "質問"),
                new X02Header(9, "質問 2 のレポート回答"),
                new X02Header(10, "質問 2 の学生 Prompt"),
                new X02Header(11, "Prompt 作成時の工夫・観点・論点"),
                new X02Header(12, "PBL feedback"),
            ]),
        new X02SheetSpec(
            "Final",
            1,
            531,
            30,
            [
                new X02Header(1, "集計レポート回答 1"),
                new X02Header(2, "集計レポート回答 2"),
                new X02Header(3, "集計レポート回答 3"),
                new X02Header(4, "集計レポート回答 4"),
                new X02Header(5, "集計レポート回答 5"),
                new X02Header(6, "集計レポート回答 6"),
                new X02Header(7, "集計レポート回答 7"),
                new X02Header(8, "集計レポート回答 8"),
                new X02Header(9, "集計レポート回答 9"),
            ]));

    internal static X02TemporaryWorkbook CreateSingleSheet(
        string sheetName,
        uint headerRow,
        uint lastRow,
        uint lastColumn,
        params X02Header[] headers) =>
        Create(new X02SheetSpec(sheetName, headerRow, lastRow, lastColumn, headers));

    internal static X02TemporaryWorkbook Create(params X02SheetSpec[] sheets)
    {
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "StudyReportEvaluator-X02-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, "synthetic.xlsx");

        using (SpreadsheetDocument document = SpreadsheetDocument.Create(
            path,
            SpreadsheetDocumentType.Workbook))
        {
            WorkbookPart workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            Sheets workbookSheets = workbookPart.Workbook.AppendChild(new Sheets());

            uint sheetId = 1;
            foreach (X02SheetSpec specification in sheets)
            {
                WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                worksheetPart.Worksheet = CreateWorksheet(specification);
                workbookSheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = sheetId++,
                    Name = specification.Name,
                });
            }

            workbookPart.Workbook.Save();
        }

        return new X02TemporaryWorkbook(directory, path);
    }

    private static Worksheet CreateWorksheet(X02SheetSpec specification)
    {
        Row header = new(
            specification.Headers
                .OrderBy(item => item.ColumnIndex)
                .Select(item => InlineCell(
                    GetColumnName(item.ColumnIndex)
                        + specification.HeaderRow.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    item.Value)))
        {
            RowIndex = specification.HeaderRow,
        };
        SheetData data = new(header);
        if (specification.LastRow > specification.HeaderRow)
        {
            data.Append(new Row(NumberCell(
                "A" + specification.LastRow.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "1"))
            {
                RowIndex = specification.LastRow,
            });
        }

        string dimension = "A1:"
            + GetColumnName(specification.LastColumn)
            + specification.LastRow.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new Worksheet(
            new SheetDimension { Reference = dimension },
            data);
    }

    private static Cell InlineCell(string reference, string value) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(value)),
        };

    private static Cell NumberCell(string reference, string value) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.Number,
            CellValue = new CellValue(value),
        };

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
}