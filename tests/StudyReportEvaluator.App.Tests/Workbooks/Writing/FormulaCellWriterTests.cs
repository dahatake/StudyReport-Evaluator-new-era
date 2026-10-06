using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Formulas;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Writing;

// Requirements: FR-025 (AC-026)
public sealed class FormulaCellWriterTests
{
    private readonly FormulaCellWriter writer = new();

    [Fact]
    public void Numeric_preview_is_written_as_cached_number_and_formula_omits_the_leading_equals()
    {
        FormulaCellDefinition definition = new(
            new FormulaCellAddress("Quantification_Results (2)", "e", 2),
            new FormulaIdentity("Criterion", "C-A", "Criterion A", "Normalized"),
            FormulaExpressions.Normalized(
                Ref("Quantification_Results (2)", "D", 2),
                Ref("Quantification_Config (2)", "S", 6, absolute: true),
                Ref("Quantification_Config (2)", "T", 6, absolute: true),
                Ref("Quantification_Config (2)", "U", 2, absolute: true)));

        Cell cell = writer.CreateCell(definition, 83.3m);

        Assert.Equal("E2", cell.CellReference?.Value);
        Assert.Equal(CellValues.Number, cell.DataType?.Value);
        Assert.Equal("83.3", cell.CellValue?.Text);
        Assert.Equal(
            "IF(ISNUMBER('Quantification_Results (2)'!D2),IFERROR(ROUND(((('Quantification_Results (2)'!D2-'Quantification_Config (2)'!$S$6)/('Quantification_Config (2)'!$T$6-'Quantification_Config (2)'!$S$6))*100),'Quantification_Config (2)'!$U$2),\"\"),\"\")",
            cell.CellFormula?.Text);
        Assert.False((cell.CellFormula?.Text ?? string.Empty).StartsWith("=", StringComparison.Ordinal));
    }

    [Fact]
    public void Blank_preview_has_a_formula_but_no_cached_value_or_numeric_type()
    {
        FormulaCellDefinition definition = new(
            new FormulaCellAddress("Results", "D", 3),
            new FormulaIdentity("Criterion", "C1", "Criterion", "Effective_Raw"),
            FormulaBlank.Value);

        Cell cell = writer.CreateCell(definition, cachedValue: null);

        Assert.Equal("D3", cell.CellReference?.Value);
        Assert.Equal("\"\"", cell.CellFormula?.Text);
        Assert.Null(cell.CellValue);
        Assert.Null(cell.DataType);
        Assert.Contains("<redacted>", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Null_definition_and_row_are_rejected()
    {
        FormulaCellDefinition definition = new(
            new FormulaCellAddress("Results", "A", 1),
            new FormulaIdentity("Criterion", "C1", "Criterion", "Normalized"),
            new FormulaNumber(1m));

        Assert.Throws<ArgumentNullException>(() => writer.CreateCell(null!, 1m));
        Assert.Throws<ArgumentNullException>(() => writer.Write(null!, definition, 1m));
    }

    private static FormulaCellReference Ref(
        string sheet,
        string column,
        int row,
        bool absolute = false) =>
        new(new FormulaCellAddress(sheet, column, row), absolute, absolute);
}
