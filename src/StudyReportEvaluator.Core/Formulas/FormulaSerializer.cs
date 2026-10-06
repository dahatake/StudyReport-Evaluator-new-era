using System.Globalization;
using System.Text;

namespace StudyReportEvaluator.Core.Formulas;

public sealed class FormulaSerializer
{
    public string Serialize(FormulaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        StringBuilder builder = new("=");
        WriteExpression(builder, expression);
        return builder.ToString();
    }

    private static void WriteExpression(StringBuilder builder, FormulaExpression expression)
    {
        switch (expression)
        {
            case FormulaNumber number:
                builder.Append(number.Value.ToString("G29", CultureInfo.InvariantCulture));
                break;
            case FormulaBlank:
                builder.Append("\"\"");
                break;
            case FormulaCell cell:
                WriteCellReference(builder, cell.Reference);
                break;
            case FormulaRange range:
                WriteRangeReference(builder, range.Reference);
                break;
            case FormulaFunction function:
                WriteFunction(builder, function);
                break;
            case FormulaBinary binary:
                builder.Append('(');
                WriteExpression(builder, binary.Left);
                builder.Append(GetOperator(binary.Operator));
                WriteExpression(builder, binary.Right);
                builder.Append(')');
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(expression), expression.GetType().FullName, "Unsupported formula expression type.");
        }
    }

    private static void WriteFunction(StringBuilder builder, FormulaFunction function)
    {
        builder.Append(GetFunctionName(function.Name)).Append('(');
        for (int index = 0; index < function.Arguments.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            WriteExpression(builder, function.Arguments[index]);
        }

        builder.Append(')');
    }

    private static void WriteCellReference(StringBuilder builder, FormulaCellReference reference)
    {
        WriteSheetName(builder, reference.Address.SheetName);
        builder.Append('!');
        if (reference.AbsoluteColumn)
        {
            builder.Append('$');
        }

        builder.Append(reference.Address.NormalizedColumnName);
        if (reference.AbsoluteRow)
        {
            builder.Append('$');
        }

        builder.Append(reference.Address.RowNumber.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteRangeReference(StringBuilder builder, FormulaRangeReference reference)
    {
        WriteSheetName(builder, reference.Address.SheetName);
        builder.Append('!');
        WriteLocalAddress(
            builder,
            reference.Address.StartColumnName,
            reference.Address.StartRowNumber,
            reference.AbsoluteColumns,
            reference.AbsoluteRows);
        builder.Append(':');
        WriteLocalAddress(
            builder,
            reference.Address.EndColumnName,
            reference.Address.EndRowNumber,
            reference.AbsoluteColumns,
            reference.AbsoluteRows);
    }

    private static void WriteLocalAddress(
        StringBuilder builder,
        string column,
        int row,
        bool absoluteColumn,
        bool absoluteRow)
    {
        if (absoluteColumn)
        {
            builder.Append('$');
        }

        builder.Append(column.ToUpperInvariant());
        if (absoluteRow)
        {
            builder.Append('$');
        }

        builder.Append(row.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteSheetName(StringBuilder builder, string sheetName)
    {
        builder.Append('\'').Append(sheetName.Replace("'", "''", StringComparison.Ordinal)).Append('\'');
    }

    private static string GetFunctionName(FormulaFunctionName name) => name switch
    {
        FormulaFunctionName.If => "IF",
        FormulaFunctionName.IfError => "IFERROR",
        FormulaFunctionName.IsNumber => "ISNUMBER",
        FormulaFunctionName.Count => "COUNT",
        FormulaFunctionName.Sum => "SUM",
        FormulaFunctionName.SumProduct => "SUMPRODUCT",
        FormulaFunctionName.Round => "ROUND",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unsupported formula function."),
    };

    private static string GetOperator(FormulaBinaryOperator operation) => operation switch
    {
        FormulaBinaryOperator.Add => "+",
        FormulaBinaryOperator.Subtract => "-",
        FormulaBinaryOperator.Multiply => "*",
        FormulaBinaryOperator.Divide => "/",
        FormulaBinaryOperator.Equal => "=",
        FormulaBinaryOperator.NotEqual => "<>",
        FormulaBinaryOperator.LessThan => "<",
        FormulaBinaryOperator.GreaterThan => ">",
        FormulaBinaryOperator.LessThanOrEqual => "<=",
        FormulaBinaryOperator.GreaterThanOrEqual => ">=",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported formula operator."),
    };
}
