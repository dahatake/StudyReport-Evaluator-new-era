using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Workbooks.Writing;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Writing;

// Requirements: NFR-SEC-003 (AC-068)
public sealed class UntrustedStringCellWriterTests
{
    private readonly UntrustedStringCellWriter writer = new();

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+SUM(A1:A2)")]
    [InlineData("-1+2")]
    [InlineData("@hostile-marker")]
    [InlineData("  leading and trailing  \r\nnext line")]
    public void Formula_markers_and_whitespace_are_preserved_as_explicit_inline_strings(string value)
    {
        Cell cell = writer.CreateCell("A1", value);

        Assert.Equal("A1", cell.CellReference?.Value);
        Assert.Equal(CellValues.InlineString, cell.DataType?.Value);
        Assert.Equal(value, cell.InlineString?.Text?.Text);
        Assert.Equal(value, cell.InnerText);
        Assert.Null(cell.CellFormula);
        Assert.Null(cell.CellValue);
    }

    [Fact]
    public void Exact_excel_character_limit_is_preserved()
    {
        string value = new('x', UntrustedStringCellWriter.MaximumCellCharacters);

        Cell cell = writer.CreateCell("XFD1048576", value);

        Assert.Equal(value, cell.InlineString?.Text?.Text);
        Assert.Equal(UntrustedStringCellWriter.MaximumCellCharacters, cell.InnerText.Length);
    }

    [Fact]
    public void Null_and_oversize_values_are_rejected_without_echoing_content()
    {
        const string canary = "PRIVATE-CELL-CONTENT-CANARY";
        string oversize = canary + new string('z', UntrustedStringCellWriter.MaximumCellCharacters);

        Assert.Throws<ArgumentNullException>(() => writer.CreateCell("A1", null!));
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => writer.CreateCell("A1", oversize));

        Assert.DoesNotContain(canary, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains(oversize.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.Contains("<redacted>", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(canary, writer.ToString(), StringComparison.Ordinal);
    }
}
