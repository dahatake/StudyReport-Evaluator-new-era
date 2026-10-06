using StudyReportEvaluator.Core.Formulas;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Formulas;

// Requirements: FR-025 (AC-026)
public sealed class FormulaPreflightValidatorTests
{
    private readonly FormulaPreflightValidator _validator = new();
    private readonly FormulaSerializer _serializer = new();
    private static readonly FormulaIdentity Identity = new("Criterion", "C1", "Coverage", "Normalized");

    [Fact]
    public void Closed_formula_with_verified_app_owned_references_is_accepted()
    {
        FormulaCellAddress source = new("Results", "A", 2);
        FormulaCellDefinition definition = new(
            new FormulaCellAddress("Results", "B", 2),
            Identity,
            FormulaExpressions.Function(FormulaFunctionName.Round, new FormulaCell(new FormulaCellReference(source)), new FormulaNumber(1m)));
        FormulaPreflightContext context = new(["Results"], [source]);

        FormulaPreflightResult result = _validator.Validate([definition], context);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(8191, true)]
    [InlineData(8192, false)]
    public void Formula_length_boundary_is_exact_and_reports_hierarchy_identity(int length, bool expectedValid)
    {
        FormulaCellAddress source = new("R", "A", 1);
        FormulaExpression expression = BuildExactLength(length, source);
        Assert.Equal(length, _serializer.Serialize(expression).Length);
        FormulaCellDefinition definition = new(new FormulaCellAddress("R", "B", 1), Identity, expression);

        FormulaPreflightResult result = _validator.Validate([definition], new FormulaPreflightContext(["R"], [source]));

        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            FormulaPreflightError error = Assert.Single(result.Errors, item => item.Code == "FORMULA_LENGTH_EXCEEDED");
            Assert.Equal("C1", error.Identity.NodeId);
            Assert.Equal("Coverage", error.Identity.DisplayName);
            Assert.Equal("Normalized", error.Identity.Field);
            Assert.Equal("8192", error.ActualDimension);
        }
    }

    [Theory]
    [InlineData(255, true)]
    [InlineData(256, false)]
    public void Function_argument_boundary_is_enforced(int argumentCount, bool expectedValid)
    {
        FormulaExpression expression = new FormulaFunction(
            FormulaFunctionName.Sum,
            Enumerable.Repeat<FormulaExpression>(new FormulaNumber(1m), argumentCount));
        FormulaCellDefinition definition = new(new FormulaCellAddress("R", "A", 1), Identity, expression);

        FormulaPreflightResult result = _validator.Validate([definition], new FormulaPreflightContext(["R"], []));

        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            Assert.Contains(result.Errors, error => error.Code == "FUNCTION_ARGUMENT_LIMIT_EXCEEDED" && error.ActualDimension == "256");
        }
    }

    [Theory]
    [InlineData("XFD", true)]
    [InlineData("XFE", false)]
    public void Excel_column_boundary_is_enforced(string column, bool expectedValid)
    {
        FormulaCellAddress source = new("R", column, 1);
        FormulaCellDefinition definition = new(
            new FormulaCellAddress("R", "A", 1),
            Identity,
            new FormulaCell(new FormulaCellReference(source)));

        FormulaPreflightResult result = _validator.Validate([definition], new FormulaPreflightContext(["R"], [source]));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1048576, true)]
    [InlineData(0, false)]
    [InlineData(1048577, false)]
    public void Excel_row_boundary_is_enforced(int row, bool expectedValid)
    {
        FormulaCellAddress source = new("R", "A", row);
        FormulaCellDefinition definition = new(
            new FormulaCellAddress("R", "B", 1),
            Identity,
            new FormulaCell(new FormulaCellReference(source)));

        FormulaPreflightResult result = _validator.Validate([definition], new FormulaPreflightContext(["R"], [source]));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void External_unowned_and_unverified_references_are_rejected()
    {
        FormulaCellAddress unowned = new("[evil.xlsx]Sheet", "A", 1);
        FormulaCellDefinition definition = new(
            new FormulaCellAddress("Results", "A", 1),
            Identity,
            new FormulaCell(new FormulaCellReference(unowned)));

        FormulaPreflightResult result = _validator.Validate(
            [definition],
            new FormulaPreflightContext(["Results"], []));

        Assert.Contains(result.Errors, error => error.Code == "SHEET_NAME_INVALID");
        Assert.Contains(result.Errors, error => error.Code == "SHEET_NOT_ALLOWED");
        Assert.Contains(result.Errors, error => error.Code == "REFERENCE_NOT_VERIFIED");
    }

    [Fact]
    public void Formula_cycles_are_rejected_before_write()
    {
        FormulaCellAddress first = new("Results", "A", 1);
        FormulaCellAddress second = new("Results", "B", 1);
        FormulaCellDefinition firstFormula = new(first, Identity, new FormulaCell(new FormulaCellReference(second)));
        FormulaCellDefinition secondFormula = new(
            second,
            Identity with { NodeId = "C2" },
            new FormulaCell(new FormulaCellReference(first)));

        FormulaPreflightResult result = _validator.Validate(
            [firstFormula, secondFormula],
            new FormulaPreflightContext(["Results"], [first, second]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "FORMULA_CYCLE");
    }

    [Fact]
    public void Formula_range_covering_its_target_is_rejected_as_a_cycle()
    {
        FormulaCellAddress target = new("Results", "B", 2);
        FormulaRangeAddress range = new("Results", "A", 1, "C", 3);
        FormulaCellDefinition formula = new(
            target,
            Identity,
            new FormulaFunction(
                FormulaFunctionName.Sum,
                [new FormulaRange(new FormulaRangeReference(range))]));

        FormulaPreflightResult result = _validator.Validate(
            [formula],
            new FormulaPreflightContext(["Results"], [], [range]));

        Assert.Contains(result.Errors, error => error.Code == "FORMULA_CYCLE");
    }

    [Fact]
    public void Raw_formula_text_defined_names_and_unknown_functions_have_no_AST_surface()
    {
        Type[] subclasses = typeof(FormulaExpression).Assembly.GetTypes()
            .Where(type => type.BaseType == typeof(FormulaExpression))
            .ToArray();

        Assert.DoesNotContain(subclasses, type => type.Name.Contains("Raw", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(subclasses, type => type.Name.Contains("Name", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            ["COUNT", "IF", "IFERROR", "ISNUMBER", "ROUND", "SUM", "SUMPRODUCT"],
            Enum.GetValues<FormulaFunctionName>()
                .Select(value => value.ToString().ToUpperInvariant())
                .Order(StringComparer.Ordinal));
    }

    private FormulaExpression BuildExactLength(int targetLength, FormulaCellAddress source)
    {
        FormulaExpression[] arguments = Enumerable.Repeat<FormulaExpression>(
            new FormulaCell(new FormulaCellReference(source)),
            FormulaPreflightValidator.MaximumFunctionArguments).ToArray();
        FormulaExpression expression = new FormulaFunction(FormulaFunctionName.Sum, arguments);
        int remaining = targetLength - _serializer.Serialize(expression).Length;
        Assert.True(remaining >= 0);

        int fiveCharacterAdditions = remaining % 4;
        while (fiveCharacterAdditions > 0 && (remaining - (fiveCharacterAdditions * 5)) < 0)
        {
            fiveCharacterAdditions += 4;
        }

        int fourCharacterAdditions = (remaining - (fiveCharacterAdditions * 5)) / 4;
        Assert.Equal(remaining, (fiveCharacterAdditions * 5) + (fourCharacterAdditions * 4));
        int argumentIndex = 0;
        for (int index = 0; index < fiveCharacterAdditions; index++)
        {
            arguments[argumentIndex] = FormulaExpressions.Binary(arguments[argumentIndex], FormulaBinaryOperator.Add, new FormulaNumber(10m));
            argumentIndex = (argumentIndex + 1) % arguments.Length;
        }

        for (int index = 0; index < fourCharacterAdditions; index++)
        {
            arguments[argumentIndex] = FormulaExpressions.Binary(arguments[argumentIndex], FormulaBinaryOperator.Add, new FormulaNumber(0m));
            argumentIndex = (argumentIndex + 1) % arguments.Length;
        }

        return new FormulaFunction(FormulaFunctionName.Sum, arguments);
    }
}
