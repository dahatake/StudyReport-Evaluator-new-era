using System.Collections.Immutable;
using System.Globalization;

namespace StudyReportEvaluator.Core.Formulas;

public sealed record FormulaIdentity(
    string NodeKind,
    string NodeId,
    string DisplayName,
    string Field);

public sealed record FormulaPreflightError(
    string Code,
    FormulaIdentity Identity,
    string ActualDimension,
    string Limit);

public sealed record FormulaCellDefinition(
    FormulaCellAddress Target,
    FormulaIdentity Identity,
    FormulaExpression Expression);

public sealed class FormulaPreflightContext
{
    public FormulaPreflightContext(
        IEnumerable<string> appOwnedSheetNames,
        IEnumerable<FormulaCellAddress> verifiedCells,
        IEnumerable<FormulaRangeAddress>? verifiedRanges = null)
    {
        ArgumentNullException.ThrowIfNull(appOwnedSheetNames);
        ArgumentNullException.ThrowIfNull(verifiedCells);
        AppOwnedSheetNames = appOwnedSheetNames.ToImmutableHashSet(StringComparer.Ordinal);
        VerifiedCells = verifiedCells.Select(Normalize).ToImmutableHashSet();
        VerifiedRanges = (verifiedRanges ?? []).Select(Normalize).ToImmutableHashSet();
    }

    public ImmutableHashSet<string> AppOwnedSheetNames { get; }

    public ImmutableHashSet<FormulaCellAddress> VerifiedCells { get; }

    public ImmutableHashSet<FormulaRangeAddress> VerifiedRanges { get; }

    private static FormulaCellAddress Normalize(FormulaCellAddress address) =>
        address with { ColumnName = address.NormalizedColumnName };

    private static FormulaRangeAddress Normalize(FormulaRangeAddress address) => address with
    {
        StartColumnName = address.StartColumnName.ToUpperInvariant(),
        EndColumnName = address.EndColumnName.ToUpperInvariant(),
    };
}

public sealed class FormulaPreflightResult
{
    internal FormulaPreflightResult(ImmutableArray<FormulaPreflightError> errors)
    {
        Errors = errors;
    }

    public ImmutableArray<FormulaPreflightError> Errors { get; }

    public bool IsValid => Errors.IsEmpty;
}

public sealed class FormulaPreflightValidator
{
    public const int MaximumFormulaLength = 8_191;
    public const int MaximumFunctionArguments = 255;
    public const int MaximumExcelColumn = 16_384;
    public const int MaximumExcelRow = 1_048_576;
    public const int MaximumSheetNameLength = 31;

    private readonly FormulaSerializer _serializer = new();

    public FormulaPreflightResult Validate(
        IEnumerable<FormulaCellDefinition> formulas,
        FormulaPreflightContext context)
    {
        ArgumentNullException.ThrowIfNull(formulas);
        ArgumentNullException.ThrowIfNull(context);

        ImmutableArray<FormulaCellDefinition> definitions = formulas.ToImmutableArray();
        ImmutableArray<FormulaPreflightError>.Builder errors = ImmutableArray.CreateBuilder<FormulaPreflightError>();
        Dictionary<FormulaCellAddress, FormulaCellDefinition> targets = [];
        foreach (FormulaCellDefinition definition in definitions)
        {
            FormulaCellAddress target = Normalize(definition.Target);
            if (!targets.TryAdd(target, definition))
            {
                Add(errors, "DUPLICATE_FORMULA_TARGET", definition.Identity, target.ToString(), "unique");
            }

            ValidateAddress(target, definition.Identity, context, requireVerified: false, errors);
            ValidateExpression(definition, context, errors);
        }

        ValidateAcyclicGraph(targets, errors);
        return new FormulaPreflightResult(errors.ToImmutable());
    }

    private void ValidateExpression(
        FormulaCellDefinition definition,
        FormulaPreflightContext context,
        ImmutableArray<FormulaPreflightError>.Builder errors)
    {
        string formula;
        try
        {
            formula = _serializer.Serialize(definition.Expression);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or NullReferenceException)
        {
            Add(errors, "UNSUPPORTED_FORMULA_EXPRESSION", definition.Identity, exception.GetType().Name, "closed AST");
            return;
        }

        if (formula.Length > MaximumFormulaLength)
        {
            Add(
                errors,
                "FORMULA_LENGTH_EXCEEDED",
                definition.Identity,
                formula.Length.ToString(CultureInfo.InvariantCulture),
                MaximumFormulaLength.ToString(CultureInfo.InvariantCulture));
        }

        Stack<FormulaExpression> pending = new();
        pending.Push(definition.Expression);
        while (pending.TryPop(out FormulaExpression? expression))
        {
            switch (expression)
            {
                case FormulaNumber or FormulaBlank:
                    break;
                case FormulaCell cell:
                    ValidateAddress(cell.Reference.Address, definition.Identity, context, requireVerified: true, errors);
                    break;
                case FormulaRange range:
                    ValidateRange(range.Reference.Address, definition.Identity, context, errors);
                    break;
                case FormulaBinary binary:
                    if (!Enum.IsDefined(typeof(FormulaBinaryOperator), binary.Operator))
                    {
                        Add(errors, "OPERATOR_NOT_ALLOWED", definition.Identity, ((int)binary.Operator).ToString(CultureInfo.InvariantCulture), "closed allowlist");
                    }

                    pending.Push(binary.Right);
                    pending.Push(binary.Left);
                    break;
                case FormulaFunction function:
                    if (!Enum.IsDefined(typeof(FormulaFunctionName), function.Name))
                    {
                        Add(errors, "FUNCTION_NOT_ALLOWED", definition.Identity, ((int)function.Name).ToString(CultureInfo.InvariantCulture), "closed allowlist");
                    }

                    if (function.Arguments.Length > MaximumFunctionArguments)
                    {
                        Add(
                            errors,
                            "FUNCTION_ARGUMENT_LIMIT_EXCEEDED",
                            definition.Identity,
                            function.Arguments.Length.ToString(CultureInfo.InvariantCulture),
                            MaximumFunctionArguments.ToString(CultureInfo.InvariantCulture));
                    }

                    for (int index = function.Arguments.Length - 1; index >= 0; index--)
                    {
                        pending.Push(function.Arguments[index]);
                    }

                    break;
                default:
                    Add(errors, "EXPRESSION_NOT_ALLOWED", definition.Identity, expression?.GetType().Name ?? "<null>", "closed AST");
                    break;
            }
        }
    }

    private static void ValidateAddress(
        FormulaCellAddress address,
        FormulaIdentity identity,
        FormulaPreflightContext context,
        bool requireVerified,
        ImmutableArray<FormulaPreflightError>.Builder errors)
    {
        FormulaCellAddress normalized = Normalize(address);
        ValidateSheet(normalized.SheetName, identity, context, errors);
        if (!TryGetColumnNumber(normalized.ColumnName, out int columnNumber))
        {
            Add(errors, "COLUMN_OUT_OF_RANGE", identity, normalized.ColumnName, MaximumExcelColumn.ToString(CultureInfo.InvariantCulture));
        }

        if (normalized.RowNumber is < 1 or > MaximumExcelRow)
        {
            Add(errors, "ROW_OUT_OF_RANGE", identity, normalized.RowNumber.ToString(CultureInfo.InvariantCulture), MaximumExcelRow.ToString(CultureInfo.InvariantCulture));
        }

        if (requireVerified && !context.VerifiedCells.Contains(normalized))
        {
            Add(errors, "REFERENCE_NOT_VERIFIED", identity, normalized.ToString(), "verified app-owned cell");
        }
    }

    private static void ValidateRange(
        FormulaRangeAddress range,
        FormulaIdentity identity,
        FormulaPreflightContext context,
        ImmutableArray<FormulaPreflightError>.Builder errors)
    {
        FormulaRangeAddress normalized = Normalize(range);
        ValidateSheet(normalized.SheetName, identity, context, errors);
        ValidateAddress(
            new FormulaCellAddress(normalized.SheetName, normalized.StartColumnName, normalized.StartRowNumber),
            identity,
            context,
            requireVerified: false,
            errors);
        ValidateAddress(
            new FormulaCellAddress(normalized.SheetName, normalized.EndColumnName, normalized.EndRowNumber),
            identity,
            context,
            requireVerified: false,
            errors);
        if (!context.VerifiedRanges.Contains(normalized))
        {
            Add(errors, "REFERENCE_NOT_VERIFIED", identity, normalized.ToString(), "verified app-owned range");
        }
    }

    private static void ValidateSheet(
        string sheetName,
        FormulaIdentity identity,
        FormulaPreflightContext context,
        ImmutableArray<FormulaPreflightError>.Builder errors)
    {
        if (string.IsNullOrWhiteSpace(sheetName)
            || sheetName.Length > MaximumSheetNameLength
            || sheetName.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) >= 0)
        {
            Add(errors, "SHEET_NAME_INVALID", identity, SafeDimension(sheetName), $"1..{MaximumSheetNameLength}");
        }

        if (!context.AppOwnedSheetNames.Contains(sheetName))
        {
            Add(errors, "SHEET_NOT_ALLOWED", identity, SafeDimension(sheetName), "app-owned sheet");
        }
    }

    private static void ValidateAcyclicGraph(
        Dictionary<FormulaCellAddress, FormulaCellDefinition> targets,
        ImmutableArray<FormulaPreflightError>.Builder errors)
    {
        Dictionary<FormulaCellAddress, int> state = [];
        foreach (FormulaCellAddress target in targets.Keys)
        {
            Visit(target, targets, state, errors);
        }
    }

    private static void Visit(
        FormulaCellAddress target,
        Dictionary<FormulaCellAddress, FormulaCellDefinition> targets,
        Dictionary<FormulaCellAddress, int> state,
        ImmutableArray<FormulaPreflightError>.Builder errors)
    {
        if (state.TryGetValue(target, out int existingState))
        {
            if (existingState == 1)
            {
                Add(errors, "FORMULA_CYCLE", targets[target].Identity, target.ToString(), "acyclic DAG");
            }

            return;
        }

        state[target] = 1;
        foreach (FormulaCellAddress dependency in EnumerateCellReferences(targets[target].Expression).Select(Normalize))
        {
            if (targets.ContainsKey(dependency))
            {
                Visit(dependency, targets, state, errors);
            }
        }

        foreach (FormulaRangeAddress range in EnumerateRangeReferences(targets[target].Expression).Select(Normalize))
        {
            foreach (FormulaCellAddress dependency in targets.Keys.Where(candidate => IsWithin(candidate, range)))
            {
                Visit(dependency, targets, state, errors);
            }
        }

        state[target] = 2;
    }

    private static IEnumerable<FormulaCellAddress> EnumerateCellReferences(FormulaExpression root)
    {
        Stack<FormulaExpression> pending = new();
        pending.Push(root);
        while (pending.TryPop(out FormulaExpression? expression))
        {
            switch (expression)
            {
                case FormulaCell cell:
                    yield return cell.Reference.Address;
                    break;
                case FormulaBinary binary:
                    pending.Push(binary.Right);
                    pending.Push(binary.Left);
                    break;
                case FormulaFunction function:
                    foreach (FormulaExpression argument in function.Arguments)
                    {
                        pending.Push(argument);
                    }

                    break;
            }
        }
    }

    private static IEnumerable<FormulaRangeAddress> EnumerateRangeReferences(FormulaExpression root)
    {
        Stack<FormulaExpression> pending = new();
        pending.Push(root);
        while (pending.TryPop(out FormulaExpression? expression))
        {
            switch (expression)
            {
                case FormulaRange range:
                    yield return range.Reference.Address;
                    break;
                case FormulaBinary binary:
                    pending.Push(binary.Right);
                    pending.Push(binary.Left);
                    break;
                case FormulaFunction function:
                    foreach (FormulaExpression argument in function.Arguments)
                    {
                        pending.Push(argument);
                    }

                    break;
            }
        }
    }

    private static bool IsWithin(FormulaCellAddress candidate, FormulaRangeAddress range)
    {
        if (!string.Equals(candidate.SheetName, range.SheetName, StringComparison.Ordinal)
            || !TryGetColumnNumber(candidate.ColumnName, out int candidateColumn)
            || !TryGetColumnNumber(range.StartColumnName, out int startColumn)
            || !TryGetColumnNumber(range.EndColumnName, out int endColumn))
        {
            return false;
        }

        int minimumColumn = Math.Min(startColumn, endColumn);
        int maximumColumn = Math.Max(startColumn, endColumn);
        int minimumRow = Math.Min(range.StartRowNumber, range.EndRowNumber);
        int maximumRow = Math.Max(range.StartRowNumber, range.EndRowNumber);
        return candidateColumn >= minimumColumn
            && candidateColumn <= maximumColumn
            && candidate.RowNumber >= minimumRow
            && candidate.RowNumber <= maximumRow;
    }

    private static bool TryGetColumnNumber(string column, out int number)
    {
        number = 0;
        if (column.Length is < 1 or > 3)
        {
            return false;
        }

        foreach (char character in column)
        {
            if (!char.IsAsciiLetter(character))
            {
                return false;
            }

            number = (number * 26) + (char.ToUpperInvariant(character) - 'A' + 1);
        }

        return number <= MaximumExcelColumn;
    }

    private static FormulaCellAddress Normalize(FormulaCellAddress address) =>
        address with { ColumnName = address.NormalizedColumnName };

    private static FormulaRangeAddress Normalize(FormulaRangeAddress address) => address with
    {
        StartColumnName = address.StartColumnName.ToUpperInvariant(),
        EndColumnName = address.EndColumnName.ToUpperInvariant(),
    };

    private static string SafeDimension(string value) => string.IsNullOrEmpty(value)
        ? "<blank>"
        : value.Length <= 64
            ? value
            : $"length={value.Length.ToString(CultureInfo.InvariantCulture)}";

    private static void Add(
        ImmutableArray<FormulaPreflightError>.Builder errors,
        string code,
        FormulaIdentity identity,
        string actual,
        string limit) => errors.Add(new FormulaPreflightError(code, identity, actual, limit));
}
