using System.Collections.Immutable;

namespace StudyReportEvaluator.Core.Formulas;

public abstract record FormulaExpression;

public sealed record FormulaNumber(decimal Value) : FormulaExpression;

public sealed record FormulaBlank : FormulaExpression
{
    public static FormulaBlank Value { get; } = new();

    private FormulaBlank()
    {
    }
}

public sealed record FormulaCellAddress(string SheetName, string ColumnName, int RowNumber)
{
    public string NormalizedColumnName => ColumnName.ToUpperInvariant();
}

public sealed record FormulaRangeAddress(
    string SheetName,
    string StartColumnName,
    int StartRowNumber,
    string EndColumnName,
    int EndRowNumber);

public sealed record FormulaCellReference(
    FormulaCellAddress Address,
    bool AbsoluteColumn = false,
    bool AbsoluteRow = false);

public sealed record FormulaRangeReference(
    FormulaRangeAddress Address,
    bool AbsoluteColumns = false,
    bool AbsoluteRows = false);

public sealed record FormulaCell(FormulaCellReference Reference) : FormulaExpression;

public sealed record FormulaRange(FormulaRangeReference Reference) : FormulaExpression;

public enum FormulaFunctionName
{
    If,
    IfError,
    IsNumber,
    Count,
    Sum,
    SumProduct,
    Round,
}

public sealed record FormulaFunction : FormulaExpression
{
    public FormulaFunction(FormulaFunctionName name, IEnumerable<FormulaExpression> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        Name = name;
        Arguments = arguments.ToImmutableArray();
    }

    public FormulaFunctionName Name { get; }

    public ImmutableArray<FormulaExpression> Arguments { get; }
}

public enum FormulaBinaryOperator
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Equal,
    NotEqual,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,
}

public sealed record FormulaBinary(
    FormulaExpression Left,
    FormulaBinaryOperator Operator,
    FormulaExpression Right) : FormulaExpression;

public readonly record struct WeightedFormulaChild(
    FormulaCellReference Score,
    FormulaCellReference Weight,
    bool Enabled = true);

public static class FormulaExpressions
{
    public static FormulaExpression AllocationTotal(
        FormulaCellReference basePoints,
        FormulaCellReference specialPoints,
        IEnumerable<FormulaCellReference> questionPoints)
    {
        ArgumentNullException.ThrowIfNull(questionPoints);
        ImmutableArray<FormulaCellReference> questions = questionPoints.ToImmutableArray();
        if (questions.IsEmpty)
        {
            throw new ArgumentException("At least one enabled question is required.", nameof(questionPoints));
        }

        FormulaExpression[] values = [
            new FormulaCell(basePoints),
            new FormulaCell(specialPoints),
            .. questions.Select(reference => (FormulaExpression)new FormulaCell(reference)),
        ];
        return Function(FormulaFunctionName.Sum, values);
    }

    public static FormulaExpression AllocationValid(FormulaCellReference allocationTotal) =>
        If(
            Binary(new FormulaCell(allocationTotal), FormulaBinaryOperator.Equal, new FormulaNumber(100m)),
            new FormulaNumber(1m),
            new FormulaNumber(0m));

    public static FormulaExpression EffectiveRaw(
        FormulaCellReference scorable,
        FormulaCellReference aiRaw,
        FormulaCellReference overrideValue,
        FormulaCellReference minimum,
        FormulaCellReference maximum)
    {
        FormulaCell scorableCell = new(scorable);
        FormulaCell aiCell = new(aiRaw);
        FormulaCell overrideCell = new(overrideValue);
        FormulaCell minimumCell = new(minimum);
        FormulaCell maximumCell = new(maximum);
        FormulaBlank blank = FormulaBlank.Value;

        FormulaExpression validatedAi = If(
            IsNumber(aiCell),
            If(
                Binary(aiCell, FormulaBinaryOperator.LessThan, minimumCell),
                blank,
                If(Binary(aiCell, FormulaBinaryOperator.GreaterThan, maximumCell), blank, aiCell)),
            blank);
        FormulaExpression validatedOverride = If(
            IsNumber(overrideCell),
            If(
                Binary(overrideCell, FormulaBinaryOperator.LessThan, minimumCell),
                blank,
                If(Binary(overrideCell, FormulaBinaryOperator.GreaterThan, maximumCell), blank, overrideCell)),
            blank);

        return If(
            Binary(scorableCell, FormulaBinaryOperator.NotEqual, new FormulaNumber(1m)),
            blank,
            If(
                Binary(overrideCell, FormulaBinaryOperator.Equal, blank),
                validatedAi,
                validatedOverride));
    }

    public static FormulaExpression Normalized(
        FormulaCellReference effectiveRaw,
        FormulaCellReference minimum,
        FormulaCellReference maximum,
        FormulaCellReference roundingDigits)
    {
        FormulaCell effective = new(effectiveRaw);
        FormulaCell minimumCell = new(minimum);
        FormulaCell maximumCell = new(maximum);
        FormulaExpression difference = Binary(effective, FormulaBinaryOperator.Subtract, minimumCell);
        FormulaExpression range = Binary(maximumCell, FormulaBinaryOperator.Subtract, minimumCell);
        FormulaExpression percentage = Binary(
            Binary(difference, FormulaBinaryOperator.Divide, range),
            FormulaBinaryOperator.Multiply,
            new FormulaNumber(100m));
        FormulaExpression rounded = Function(FormulaFunctionName.Round, percentage, new FormulaCell(roundingDigits));

        return If(
            IsNumber(effective),
            IfError(rounded, FormulaBlank.Value),
            FormulaBlank.Value);
    }

    public static FormulaExpression Aggregate(
        IEnumerable<WeightedFormulaChild> children,
        FormulaCellReference roundingDigits)
    {
        ArgumentNullException.ThrowIfNull(children);
        ImmutableArray<WeightedFormulaChild> enabledChildren = children
            .Where(child => child.Enabled)
            .ToImmutableArray();
        if (enabledChildren.IsEmpty)
        {
            throw new ArgumentException("At least one enabled child is required.", nameof(children));
        }

        FormulaExpression[] scores = enabledChildren
            .Select(child => (FormulaExpression)new FormulaCell(child.Score))
            .ToArray();
        FormulaExpression[] weightedTerms = enabledChildren
            .Select(child => Binary(
                new FormulaCell(child.Score),
                FormulaBinaryOperator.Multiply,
                new FormulaCell(child.Weight)))
            .ToArray();
        FormulaExpression[] weights = enabledChildren
            .Select(child => (FormulaExpression)new FormulaCell(child.Weight))
            .ToArray();
        FormulaExpression allPresent = Binary(
            Function(FormulaFunctionName.Count, scores),
            FormulaBinaryOperator.Equal,
            new FormulaNumber(enabledChildren.Length));
        FormulaExpression weightedAverage = Binary(
            Function(FormulaFunctionName.Sum, weightedTerms),
            FormulaBinaryOperator.Divide,
            Function(FormulaFunctionName.Sum, weights));
        FormulaExpression rounded = Function(
            FormulaFunctionName.Round,
            weightedAverage,
            new FormulaCell(roundingDigits));

        return If(allPresent, IfError(rounded, FormulaBlank.Value), FormulaBlank.Value);
    }

    public static FormulaExpression QuestionRate(
        FormulaCellReference answerPresent,
        FormulaCellReference questionNormalized)
    {
        FormulaCell present = new(answerPresent);
        FormulaCell normalized = new(questionNormalized);
        return If(
            Binary(present, FormulaBinaryOperator.Equal, new FormulaNumber(0m)),
            new FormulaNumber(0m),
            If(
                IsNumber(normalized),
                Binary(normalized, FormulaBinaryOperator.Divide, new FormulaNumber(100m)),
                FormulaBlank.Value));
    }

    public static FormulaExpression QuestionEarned(
        FormulaCellReference questionRate,
        FormulaCellReference questionPoints,
        FormulaCellReference roundingDigits)
    {
        FormulaCell rate = new(questionRate);
        FormulaExpression rounded = Function(
            FormulaFunctionName.Round,
            Binary(rate, FormulaBinaryOperator.Multiply, new FormulaCell(questionPoints)),
            new FormulaCell(roundingDigits));
        return If(IsNumber(rate), rounded, FormulaBlank.Value);
    }

    public static FormulaExpression SpecialQuestionRate(
        IEnumerable<FormulaCellReference> specialRawScores,
        FormulaCellReference specialPoints,
        FormulaCellReference roundingDigits)
    {
        ArgumentNullException.ThrowIfNull(specialRawScores);
        ImmutableArray<FormulaCellReference> scores = specialRawScores.ToImmutableArray();
        if (scores.IsEmpty)
        {
            throw new ArgumentException("At least one enabled special score is required.", nameof(specialRawScores));
        }

        FormulaExpression[] scoreCells = scores
            .Select(score => (FormulaExpression)new FormulaCell(score))
            .ToArray();
        FormulaExpression allPresent = Binary(
            Function(FormulaFunctionName.Count, scoreCells),
            FormulaBinaryOperator.Equal,
            new FormulaNumber(scores.Length));
        FormulaExpression average = Binary(
            Function(FormulaFunctionName.Sum, scoreCells),
            FormulaBinaryOperator.Divide,
            new FormulaNumber(scores.Length));
        FormulaExpression rounded = Function(
            FormulaFunctionName.Round,
            average,
            new FormulaCell(roundingDigits));
        return If(
            Binary(new FormulaCell(specialPoints), FormulaBinaryOperator.Equal, new FormulaNumber(0m)),
            FormulaBlank.Value,
            If(allPresent, rounded, FormulaBlank.Value));
    }

    public static FormulaExpression SpecialEarned(
        IEnumerable<FormulaCellReference> specialQuestionRates,
        FormulaCellReference specialPoints,
        FormulaCellReference roundingDigits)
    {
        ArgumentNullException.ThrowIfNull(specialQuestionRates);
        ImmutableArray<FormulaCellReference> rates = specialQuestionRates.ToImmutableArray();
        if (rates.IsEmpty)
        {
            return If(
                Binary(new FormulaCell(specialPoints), FormulaBinaryOperator.Equal, new FormulaNumber(0m)),
                new FormulaNumber(0m),
                FormulaBlank.Value);
        }

        FormulaExpression[] rateCells = rates
            .Select(rate => (FormulaExpression)new FormulaCell(rate))
            .ToArray();
        FormulaExpression allPresent = Binary(
            Function(FormulaFunctionName.Count, rateCells),
            FormulaBinaryOperator.Equal,
            new FormulaNumber(rates.Length));
        FormulaExpression average = Binary(
            Function(FormulaFunctionName.Sum, rateCells),
            FormulaBinaryOperator.Divide,
            new FormulaNumber(rates.Length));
        FormulaExpression rounded = Function(
            FormulaFunctionName.Round,
            Binary(new FormulaCell(specialPoints), FormulaBinaryOperator.Multiply, average),
            new FormulaCell(roundingDigits));
        return If(
            Binary(new FormulaCell(specialPoints), FormulaBinaryOperator.Equal, new FormulaNumber(0m)),
            new FormulaNumber(0m),
            If(allPresent, rounded, FormulaBlank.Value));
    }

    public static FormulaExpression SimilarityPenalty(
        FormulaCellReference questionPoints,
        FormulaCellReference similarity,
        FormulaCellReference penaltyWeight,
        FormulaCellReference roundingDigits)
    {
        FormulaCell similarityCell = new(similarity);
        FormulaExpression product = Binary(
            Binary(
                new FormulaCell(questionPoints),
                FormulaBinaryOperator.Multiply,
                similarityCell),
            FormulaBinaryOperator.Multiply,
            new FormulaCell(penaltyWeight));
        return If(
            IsNumber(similarityCell),
            Function(FormulaFunctionName.Round, product, new FormulaCell(roundingDigits)),
            FormulaBlank.Value);
    }

    public static FormulaExpression FinalRaw(
        FormulaCellReference allocationValid,
        FormulaCellReference basePoints,
        IEnumerable<FormulaCellReference> questionEarned,
        FormulaCellReference specialEarned,
        IEnumerable<FormulaCellReference> similarityPenalties,
        FormulaCellReference roundingDigits)
    {
        ArgumentNullException.ThrowIfNull(questionEarned);
        ArgumentNullException.ThrowIfNull(similarityPenalties);
        ImmutableArray<FormulaCellReference> questions = questionEarned.ToImmutableArray();
        ImmutableArray<FormulaCellReference> penalties = similarityPenalties.ToImmutableArray();
        if (questions.IsEmpty || penalties.Length != questions.Length)
        {
            throw new ArgumentException("Question earned values and similarity penalties must have the same positive count.");
        }

        FormulaExpression[] allRequired = questions
            .Concat([specialEarned])
            .Concat(penalties)
            .Select(reference => (FormulaExpression)new FormulaCell(reference))
            .ToArray();
        FormulaExpression complete = Binary(
            Function(FormulaFunctionName.Count, allRequired),
            FormulaBinaryOperator.Equal,
            new FormulaNumber(allRequired.Length));
        FormulaExpression additions = Binary(
            Binary(
                new FormulaCell(basePoints),
                FormulaBinaryOperator.Add,
                Function(FormulaFunctionName.Sum, questions
                    .Select(reference => (FormulaExpression)new FormulaCell(reference))
                    .ToArray())),
            FormulaBinaryOperator.Add,
            new FormulaCell(specialEarned));
        FormulaExpression raw = Binary(
            additions,
            FormulaBinaryOperator.Subtract,
            Function(FormulaFunctionName.Sum, penalties
                .Select(reference => (FormulaExpression)new FormulaCell(reference))
                .ToArray()));
        FormulaExpression rounded = Function(
            FormulaFunctionName.Round,
            raw,
            new FormulaCell(roundingDigits));
        return If(
            Binary(new FormulaCell(allocationValid), FormulaBinaryOperator.NotEqual, new FormulaNumber(1m)),
            FormulaBlank.Value,
            If(complete, rounded, FormulaBlank.Value));
    }

    public static FormulaExpression FinalScore(FormulaCellReference finalRaw)
    {
        FormulaCell raw = new(finalRaw);
        return If(
            IsNumber(raw),
            If(
                Binary(raw, FormulaBinaryOperator.LessThan, new FormulaNumber(0m)),
                new FormulaNumber(0m),
                If(
                    Binary(raw, FormulaBinaryOperator.GreaterThan, new FormulaNumber(100m)),
                    new FormulaNumber(100m),
                    raw)),
            FormulaBlank.Value);
    }

    public static FormulaFunction Function(FormulaFunctionName name, params FormulaExpression[] arguments) =>
        new(name, arguments);

    public static FormulaFunction If(
        FormulaExpression condition,
        FormulaExpression whenTrue,
        FormulaExpression whenFalse) =>
        Function(FormulaFunctionName.If, condition, whenTrue, whenFalse);

    public static FormulaFunction IfError(FormulaExpression value, FormulaExpression fallback) =>
        Function(FormulaFunctionName.IfError, value, fallback);

    public static FormulaFunction IsNumber(FormulaExpression value) =>
        Function(FormulaFunctionName.IsNumber, value);

    public static FormulaBinary Binary(
        FormulaExpression left,
        FormulaBinaryOperator operation,
        FormulaExpression right) => new(left, operation, right);
}
