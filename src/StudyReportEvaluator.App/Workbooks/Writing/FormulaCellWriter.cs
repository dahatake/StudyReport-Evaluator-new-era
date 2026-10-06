using System.Globalization;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.Core.Formulas;

namespace StudyReportEvaluator.App.Workbooks.Writing;

public sealed class FormulaCellWriter
{
    private readonly FormulaSerializer serializer = new();

    public Cell CreateCell(FormulaCellDefinition definition, decimal? cachedValue)
    {
        ArgumentNullException.ThrowIfNull(definition);
        string serialized = serializer.Serialize(definition.Expression);
        if (serialized.Length < 2 || serialized[0] != '=')
        {
            throw new InvalidDataException("The formula serializer did not produce a complete formula.");
        }

        FormulaCellAddress target = definition.Target;
        Cell cell = new()
        {
            CellReference = target.NormalizedColumnName
                + target.RowNumber.ToString(CultureInfo.InvariantCulture),
            CellFormula = new CellFormula(serialized[1..]),
        };
        if (cachedValue is decimal value)
        {
            cell.DataType = CellValues.Number;
            cell.CellValue = new CellValue(value.ToString("G29", CultureInfo.InvariantCulture));
        }

        return cell;
    }

    public void Write(Row row, FormulaCellDefinition definition, decimal? cachedValue)
    {
        ArgumentNullException.ThrowIfNull(row);
        row.Append(CreateCell(definition, cachedValue));
    }

    public override string ToString() =>
        $"{nameof(FormulaCellWriter)} {{ Content = <redacted> }}";
}
