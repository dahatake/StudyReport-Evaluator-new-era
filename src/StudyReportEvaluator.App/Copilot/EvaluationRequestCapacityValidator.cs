using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using StudyReportEvaluator.Core.Prompting;

namespace StudyReportEvaluator.App.Copilot;

public sealed record EvaluationRequestCapacityError(
    string Code,
    string Field,
    long ActualDimension,
    int Limit)
{
    public override string ToString() =>
        $"{nameof(EvaluationRequestCapacityError)} {{ Code = {Code}, Field = {Field}, ActualDimension = {ActualDimension.ToString(CultureInfo.InvariantCulture)}, Limit = {Limit.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed class EvaluationRequestCapacityResult
{
    internal EvaluationRequestCapacityResult(
        long promptUnicodeScalarCount,
        long promptUtf8ByteCount,
        long schemaUtf8ByteCount,
        long appOwnedRequestUtf8ByteCount,
        int? modelContextBudget,
        ImmutableArray<EvaluationRequestCapacityError> errors)
    {
        PromptUnicodeScalarCount = promptUnicodeScalarCount;
        PromptUtf8ByteCount = promptUtf8ByteCount;
        SchemaUtf8ByteCount = schemaUtf8ByteCount;
        AppOwnedRequestUtf8ByteCount = appOwnedRequestUtf8ByteCount;
        ModelContextBudget = modelContextBudget;
        Errors = errors;
    }

    public long PromptUnicodeScalarCount { get; }

    public long PromptUtf8ByteCount { get; }

    public long SchemaUtf8ByteCount { get; }

    public long AppOwnedRequestUtf8ByteCount { get; }

    /// null は SDK が当該 model の上限を公開しておらず、model 相対の予算検査を適用しなかったことを表す。
    public int? ModelContextBudget { get; }

    public ImmutableArray<EvaluationRequestCapacityError> Errors { get; }

    public bool IsValid => Errors.IsEmpty;

    public override string ToString() =>
        $"{nameof(EvaluationRequestCapacityResult)} {{ PromptUnicodeScalarCount = {PromptUnicodeScalarCount.ToString(CultureInfo.InvariantCulture)}, PromptUtf8ByteCount = {PromptUtf8ByteCount.ToString(CultureInfo.InvariantCulture)}, SchemaUtf8ByteCount = {SchemaUtf8ByteCount.ToString(CultureInfo.InvariantCulture)}, AppOwnedRequestUtf8ByteCount = {AppOwnedRequestUtf8ByteCount.ToString(CultureInfo.InvariantCulture)}, ModelContextBudget = {ModelContextBudget?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, ErrorCount = {Errors.Length.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed class EvaluationRequestCapacityValidator
{
    public const int MaximumRequestUnicodeScalars = 65_536;
    public const int MaximumWorstCaseAttemptsPerRun = 20_000;
    public const int ModelContextBudgetPercent = 80;

    private readonly EvaluationSchemaFactory schemaFactory = new();
    private readonly AuxiliaryEvaluationSchemaFactory auxiliarySchemaFactory = new();

    public EvaluationRequestCapacityResult Validate(
        SafeEvaluationPayload payload,
        int? maximumPromptTokens,
        int? maximumContextWindowTokens)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return ValidateCore(
            payload.RenderedPrompt,
            schemaFactory.CreateSchema(payload).GetRawText(),
            EvaluationSchemaFactory.ToolName,
            EvaluationSchemaFactory.ToolDescription,
            maximumPromptTokens,
            maximumContextWindowTokens);
    }

    public EvaluationRequestCapacityResult Validate(
        SafeReferenceAnswerPayload payload,
        int? maximumPromptTokens,
        int? maximumContextWindowTokens)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return ValidateCore(
            payload.RenderedPrompt,
            auxiliarySchemaFactory.CreateReferenceSchema(payload).GetRawText(),
            AuxiliaryEvaluationSchemaFactory.ReferenceToolName,
            AuxiliaryEvaluationSchemaFactory.ReferenceToolDescription,
            maximumPromptTokens,
            maximumContextWindowTokens);
    }

    public EvaluationRequestCapacityResult Validate(
        SafeSpecialEvaluationPayload payload,
        int? maximumPromptTokens,
        int? maximumContextWindowTokens)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return ValidateCore(
            payload.RenderedPrompt,
            auxiliarySchemaFactory.CreateSpecialSchema(payload).GetRawText(),
            AuxiliaryEvaluationSchemaFactory.SpecialToolName,
            AuxiliaryEvaluationSchemaFactory.SpecialToolDescription,
            maximumPromptTokens,
            maximumContextWindowTokens);
    }

    private static EvaluationRequestCapacityResult ValidateCore(
        string renderedPrompt,
        string schema,
        string toolName,
        string toolDescription,
        int? maximumPromptTokens,
        int? maximumContextWindowTokens)
    {
        if (maximumPromptTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPromptTokens));
        }

        if (maximumContextWindowTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumContextWindowTokens));
        }

        // null は SDK が当該 model の上限を公開していないことを表す。router の `auto` が該当する。
        int? effectiveLimit = (maximumPromptTokens, maximumContextWindowTokens) switch
        {
            (int prompt, int context) => Math.Min(prompt, context),
            (int prompt, null) => prompt,
            (null, int context) => context,
            _ => null,
        };
        long promptScalars = CountUnicodeScalars(renderedPrompt);
        long promptBytes = Encoding.UTF8.GetByteCount(renderedPrompt);
        long schemaScalars = CountUnicodeScalars(schema);
        long schemaBytes = Encoding.UTF8.GetByteCount(schema);
        long toolContractScalars = checked(
            CountUnicodeScalars(toolName)
            + CountUnicodeScalars(toolDescription));
        long toolContractBytes = checked(
            Encoding.UTF8.GetByteCount(toolName)
            + Encoding.UTF8.GetByteCount(toolDescription));
        long appOwnedScalars = checked(promptScalars + schemaScalars + toolContractScalars);
        long appOwnedBytes = checked(promptBytes + schemaBytes + toolContractBytes);
        int? modelContextBudget = effectiveLimit is int limit
            ? checked((int)((long)limit * ModelContextBudgetPercent / 100L))
            : null;
        ImmutableArray<EvaluationRequestCapacityError>.Builder errors =
            ImmutableArray.CreateBuilder<EvaluationRequestCapacityError>();

        // The model-independent ceiling always applies, including when the SDK publishes no limit.
        if (appOwnedScalars > MaximumRequestUnicodeScalars)
        {
            errors.Add(new EvaluationRequestCapacityError(
                "REQUEST_SCALAR_LIMIT_EXCEEDED",
                "AppOwnedRequestUnicodeScalars",
                appOwnedScalars,
                MaximumRequestUnicodeScalars));
        }

        // The SDK exposes model limits but not pre-send tokenization for an uncreated
        // ephemeral session. UTF-8 bytes are an explicit conservative upper bound for
        // app-owned tokenization and leave 20% of the model budget for runtime context.
        // This dimension is never reported as measured model tokens.
        if (modelContextBudget is int budget && appOwnedBytes > budget)
        {
            errors.Add(new EvaluationRequestCapacityError(
                "REQUEST_CONTEXT_BUDGET_EXCEEDED",
                "AppOwnedRequestUtf8Bytes",
                appOwnedBytes,
                budget));
        }

        return new EvaluationRequestCapacityResult(
            promptScalars,
            promptBytes,
            schemaBytes,
            appOwnedBytes,
            modelContextBudget,
            errors.ToImmutable());
    }

    private static long CountUnicodeScalars(string value)
    {
        long count = 0;
        foreach (Rune _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }
}
