using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using SpreadsheetText = DocumentFormat.OpenXml.Spreadsheet.Text;

namespace StudyReportEvaluator.App.Workbooks.Writing;

public sealed class UntrustedStringCellWriter
{
    public const int MaximumCellCharacters = 32_767;

    public Cell CreateCell(string cellReference, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cellReference);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaximumCellCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"An Excel string cell cannot exceed {MaximumCellCharacters} characters; actual length was {value.Length}.");
        }

        return new Cell
        {
            CellReference = cellReference,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(
                new SpreadsheetText(value) { Space = SpaceProcessingModeValues.Preserve }),
        };
    }

    public void Write(Row row, string cellReference, string value)
    {
        ArgumentNullException.ThrowIfNull(row);
        row.Append(CreateCell(cellReference, value));
    }

    public override string ToString() =>
        $"{nameof(UntrustedStringCellWriter)} {{ Content = <redacted> }}";
}
