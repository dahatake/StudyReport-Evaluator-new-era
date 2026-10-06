using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Reading;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Reading;

// Requirements: FR-003 (AC-003)
public sealed class WorkbookMetadataReaderTests
{
    private readonly WorkbookMetadataReader reader = new();

    [Fact]
    public void Reads_sheet_state_dimension_boundaries_and_all_header_string_forms()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();

        WorkbookMetadata metadata = reader.Read(workbook.Path);

        Assert.Equal(1U, metadata.HeaderRowNumber);
        Assert.Equal(2, metadata.Worksheets.Count);
        Assert.Equal(1, metadata.SharedStringCount);
        Assert.True(metadata.PackagePartCount >= 6);
        Assert.True(metadata.RelationshipCount >= 3);

        WorksheetMetadata original = metadata.Worksheets[0];
        Assert.Equal("Original", original.Name);
        Assert.Equal(WorkbookSheetState.Visible, original.State);
        Assert.Equal("A1:C3", original.DimensionReference);
        Assert.Equal(1U, original.FirstRowIndex);
        Assert.Equal(3U, original.LastRowIndex);
        Assert.Equal(3U, original.RowCount);
        Assert.Equal(1U, original.FirstColumnIndex);
        Assert.Equal(3U, original.LastColumnIndex);
        Assert.Equal(3U, original.ColumnCount);
        Assert.Collection(
            original.HeaderCells,
            cell => AssertHeader(cell, 1, "A", X01SyntheticWorkbookFactory.SharedHeader),
            cell => AssertHeader(cell, 2, "B", X01SyntheticWorkbookFactory.InlineHeader),
            cell => AssertHeader(cell, 3, "C", "Synthetic plain header"));
        Assert.Equal(
            [
                X01SyntheticWorkbookFactory.SharedHeader,
                X01SyntheticWorkbookFactory.InlineHeader,
                "Synthetic plain header",
            ],
            original.HeaderRowValues);

        WorksheetMetadata hidden = metadata.Worksheets[1];
        Assert.Equal("Hidden synthetic", hidden.Name);
        Assert.Equal(WorkbookSheetState.VeryHidden, hidden.State);
        Assert.Equal("D4:E5", hidden.DimensionReference);
        Assert.Equal(4U, hidden.FirstRowIndex);
        Assert.Equal(5U, hidden.LastRowIndex);
        Assert.Equal(4U, hidden.FirstColumnIndex);
        Assert.Equal(5U, hidden.LastColumnIndex);
        Assert.Empty(hidden.HeaderCells);
    }

    [Fact]
    public void Reads_a_caller_selected_header_row_without_fixed_sheet_positions()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();

        WorkbookMetadata metadata = reader.Read(workbook.Path, headerRowNumber: 4);

        Assert.Empty(metadata.Worksheets[0].HeaderCells);
        Assert.Collection(
            metadata.Worksheets[1].HeaderCells,
            cell => AssertHeader(cell, 4, "D", "Synthetic hidden header"),
            cell => AssertHeader(cell, 5, "E", "7"));
    }

    [Fact]
    public void Reads_question_text_from_the_second_row_for_forms_exports()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();

        WorkbookMetadata metadata = reader.Read(workbook.Path, headerRowNumber: 2);

        Assert.Equal(2U, metadata.HeaderRowNumber);
        Assert.Collection(
            metadata.Worksheets[0].HeaderCells,
            cell => AssertHeader(cell, 1, "A", "1"),
            cell => AssertHeader(cell, 3, "C", "2"));
        Assert.Empty(metadata.Worksheets[1].HeaderCells);
    }

    [Fact]
    public void Accepts_a_cell_value_at_the_32767_character_boundary()
    {
        string boundaryValue = CreateDeterministicText(WorkbookMetadataReader.MaxCellCharacters);
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create(
            inlineHeader: boundaryValue);

        WorkbookMetadata metadata = reader.Read(workbook.Path);

        Assert.Equal(boundaryValue, metadata.Worksheets[0].HeaderCells[1].Value);
    }

    [Fact]
    public void Rejects_a_cell_value_above_the_32767_character_boundary()
    {
        string oversizedValue = CreateDeterministicText(WorkbookMetadataReader.MaxCellCharacters + 1);
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create(
            inlineHeader: oversizedValue);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => reader.Read(workbook.Path));

        Assert.DoesNotContain(oversizedValue[..64], exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(workbook.Path, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_exact_excel_row_and_column_boundaries()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create(
            dimension: "A1:XFD1048576");

        WorksheetMetadata metadata = reader.Read(workbook.Path).Worksheets[0];

        Assert.Equal(1_048_576U, metadata.RowCount);
        Assert.Equal(16_384U, metadata.ColumnCount);
    }

    [Theory]
    [InlineData("A1:XFD1048577")]
    [InlineData("A1:XFE1048576")]
    public void Rejects_dimensions_above_excel_row_or_column_limits(string dimension)
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create(
            dimension: dimension);

        Assert.Throws<InvalidDataException>(() => reader.Read(workbook.Path));
    }

    [Fact]
    public void Read_is_read_only_and_input_snapshot_remains_exactly_equal()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        InputSnapshotService snapshots = new();
        InputSnapshot before = snapshots.Capture(workbook.Path);
        File.SetAttributes(workbook.Path, FileAttributes.ReadOnly);
        try
        {
            _ = reader.Read(workbook.Path);
        }
        finally
        {
            File.SetAttributes(workbook.Path, FileAttributes.Normal);
        }

        InputSnapshotComparison comparison = snapshots.Recheck(workbook.Path, before);
        Assert.True(comparison.Sha256Matches);
        Assert.True(comparison.SizeMatches);
        Assert.True(comparison.LastWriteTimeUtcMatches);
        Assert.True(comparison.IsMatch);
    }

    [Fact]
    public void Public_content_bearing_metadata_types_redact_to_string_output()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        WorkbookMetadata metadata = reader.Read(workbook.Path);
        WorksheetMetadata worksheet = metadata.Worksheets[0];
        WorkbookHeaderCell header = worksheet.HeaderCells[0];

        AssertRedacted(metadata.ToString(), workbook.Path, worksheet.Name, header.Value);
        AssertRedacted(worksheet.ToString(), workbook.Path, worksheet.Name, header.Value);
        AssertRedacted(header.ToString(), workbook.Path, worksheet.Name, header.Value);
    }

    [Fact]
    public void Metadata_limits_are_finite_and_include_normative_cell_boundary()
    {
        Assert.Equal(FileFormatClassifier.MaxZipEntryCount, WorkbookMetadataReader.MaxWorksheetCount);
        Assert.Equal(1_048_576U, WorkbookMetadataReader.MaxWorksheetRows);
        Assert.Equal(16_384U, WorkbookMetadataReader.MaxWorksheetColumns);
        Assert.Equal(2_000_000, WorkbookMetadataReader.MaxSharedStringCount);
        Assert.Equal(20_000, WorkbookMetadataReader.MaxCellFormatCount);
        Assert.Equal(32_767, WorkbookMetadataReader.MaxCellCharacters);
        Assert.InRange(FileFormatClassifier.MaxCharactersInPart, 1, long.MaxValue);
    }

    private static void AssertHeader(
        WorkbookHeaderCell cell,
        uint expectedColumn,
        string expectedColumnName,
        string expectedValue)
    {
        Assert.Equal(expectedColumn, cell.ColumnIndex);
        Assert.Equal(expectedColumnName, cell.ColumnName);
        Assert.Equal(expectedValue, cell.Value);
    }

    private static void AssertRedacted(string representation, params string[] forbiddenValues)
    {
        Assert.Contains("<redacted>", representation, StringComparison.Ordinal);
        foreach (string forbiddenValue in forbiddenValues)
        {
            Assert.DoesNotContain(forbiddenValue, representation, StringComparison.Ordinal);
        }
    }

    private static string CreateDeterministicText(int length)
    {
        char[] value = new char[length];
        uint state = 0xC0FFEEU;
        for (int index = 0; index < value.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            value[index] = (char)('!' + (state % 90));
        }

        return new string(value);
    }
}