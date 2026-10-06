using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Formulas;
using StudyReportEvaluator.Core.Scoring;

namespace StudyReportEvaluator.App.Workbooks.Writing;

public static class ResultsStatusCodes
{
    public const string Success = "SUCCESS";
    public const string Empty = "EMPTY";
    public const string AiOutputInvalid = "AI_OUTPUT_INVALID";
    public const string AiTimeout = "AI_TIMEOUT";
    public const string NetworkFailed = "NETWORK_FAILED";
    public const string RateLimited = "RATE_LIMITED";
    public const string QuotaExhausted = "QUOTA_EXHAUSTED";
    public const string AuthRequired = "AUTH_REQUIRED";
    public const string Cancelled = "CANCELLED";
    public const string CleanupFailed = "CLEANUP_FAILED";
    public const string AiRuntimeFailed = "AI_RUNTIME_FAILED";
    public const string NotRunZeroBudget = "NOT_RUN_ZERO_BUDGET";

    private static readonly FrozenSet<string> Defined = new[]
    {
        Success,
        Empty,
        AiOutputInvalid,
        AiTimeout,
        NetworkFailed,
        RateLimited,
        QuotaExhausted,
        AuthRequired,
        Cancelled,
        CleanupFailed,
        AiRuntimeFailed,
        NotRunZeroBudget,
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsDefined(string? value) => value is not null && Defined.Contains(value);
}

public sealed record CriterionOverrideInput
{
    public required string CriterionId { get; init; }

    public string? Value { get; init; }

    public override string ToString() =>
        $"{nameof(CriterionOverrideInput)} {{ CriterionId = {CriterionId}, Content = <redacted> }}";
}

public sealed record EvaluatorResultInput
{
    public required string EvaluatorId { get; init; }

    public QuantificationResult? AiResult { get; init; }

    public ImmutableArray<CriterionOverrideInput> Overrides { get; init; } = [];

    public required string Status { get; init; }

    public override string ToString() =>
        $"{nameof(EvaluatorResultInput)} {{ EvaluatorId = {EvaluatorId}, OverrideCount = {(Overrides.IsDefault ? 0 : Overrides.Length)}, Content = <redacted> }}";
}

public sealed record SpecialResultInput
{
    public required string SpecialEvaluationId { get; init; }

    public decimal? AiRaw { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string Evidence { get; init; } = string.Empty;

    public string EvidenceSource { get; init; } = string.Empty;

    public string EvidenceSourceColumnId { get; init; } = string.Empty;

    public required string Status { get; init; }

    public override string ToString() =>
        $"{nameof(SpecialResultInput)} {{ SpecialEvaluationId = {SpecialEvaluationId}, Status = {Status}, Content = <redacted> }}";
}

public sealed record SimilarityResultInput
{
    public decimal? AiRaw { get; init; }

    public string Reason { get; init; } = string.Empty;

    public required string Status { get; init; }

    public decimal? PeerMax { get; init; }

    public int? PeerRow { get; init; }

    public override string ToString() =>
        $"{nameof(SimilarityResultInput)} {{ Status = {Status}, Content = <redacted> }}";
}

public sealed record QuestionResultInput
{
    public required string QuestionId { get; init; }

    public bool Scorable { get; init; }

    public ImmutableArray<EvaluatorResultInput> Evaluators { get; init; } = [];

    public ImmutableArray<SpecialResultInput> SpecialResults { get; init; } = [];

    public SimilarityResultInput? Similarity { get; init; }

    public override string ToString() =>
        $"{nameof(QuestionResultInput)} {{ QuestionId = {QuestionId}, Scorable = {Scorable}, EvaluatorCount = {(Evaluators.IsDefault ? 0 : Evaluators.Length)}, Content = <redacted> }}";
}

public sealed record ResultsSheetRowInput
{
    public int SourceRowNumber { get; init; }

    public ImmutableArray<QuestionResultInput> Questions { get; init; } = [];

    public override string ToString() =>
        $"{nameof(ResultsSheetRowInput)} {{ SourceRowNumber = {SourceRowNumber}, QuestionCount = {(Questions.IsDefault ? 0 : Questions.Length)}, Content = <redacted> }}";
}

public sealed record ResultsSheetValidationError(
    string Code,
    int? SourceRowNumber,
    string NodeKind,
    string NodeId,
    string DisplayName,
    string Field,
    string SafeOffendingValue,
    string? Limit = null)
{
    public override string ToString() =>
        $"{nameof(ResultsSheetValidationError)} {{ Code = {Code}, SourceRowNumber = {SourceRowNumber?.ToString(CultureInfo.InvariantCulture) ?? "<none>"}, Field = {Field}, Content = <redacted> }}";
}

public sealed class ResultsSheetValidationException : Exception
{
    internal ResultsSheetValidationException(IEnumerable<ResultsSheetValidationError> errors)
        : this(errors.ToImmutableArray())
    {
    }

    private ResultsSheetValidationException(ImmutableArray<ResultsSheetValidationError> errors)
        : base($"The Results worksheet has {errors.Length.ToString(CultureInfo.InvariantCulture)} validation error(s).")
    {
        Errors = errors;
    }

    public ImmutableArray<ResultsSheetValidationError> Errors { get; }

    public override string ToString() =>
        $"{nameof(ResultsSheetValidationException)} {{ ErrorCount = {Errors.Length}, Content = <redacted> }}";
}

public sealed record WrittenFormulaCell(
    FormulaCellDefinition Definition,
    decimal? CachedValue)
{
    public override string ToString() =>
        $"{nameof(WrittenFormulaCell)} {{ Content = <redacted> }}";
}

public sealed class ResultsSheetWriteResult
{
    internal ResultsSheetWriteResult(
        string sheetName,
        int headerRow,
        int dataRowCount,
        int columnCount,
        ImmutableArray<WrittenFormulaCell> formulaCells)
    {
        SheetName = sheetName;
        HeaderRow = headerRow;
        DataRowCount = dataRowCount;
        ColumnCount = columnCount;
        FormulaCells = formulaCells;
    }

    public string SheetName { get; }

    public int HeaderRow { get; }

    public int DataRowCount { get; }

    public int ColumnCount { get; }

    public ImmutableArray<WrittenFormulaCell> FormulaCells { get; }

    public override string ToString() =>
        $"{nameof(ResultsSheetWriteResult)} {{ DataRowCount = {DataRowCount}, ColumnCount = {ColumnCount}, FormulaCount = {FormulaCells.Length}, Content = <redacted> }}";
}

public sealed class ResultsSheetPreflightResult
{
    internal ResultsSheetPreflightResult(
        int columnCount,
        int formulaCount,
        ImmutableArray<ResultsSheetValidationError> errors)
    {
        ColumnCount = columnCount;
        FormulaCount = formulaCount;
        Errors = errors;
    }

    public int ColumnCount { get; }

    public int FormulaCount { get; }

    public ImmutableArray<ResultsSheetValidationError> Errors { get; }

    public bool IsValid => Errors.IsEmpty;

    public override string ToString() =>
        $"{nameof(ResultsSheetPreflightResult)} {{ ColumnCount = {ColumnCount.ToString(CultureInfo.InvariantCulture)}, FormulaCount = {FormulaCount.ToString(CultureInfo.InvariantCulture)}, ErrorCount = {Errors.Length.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed class ResultsSheetWriter
{
    public const string ScorableSuffix = "Scorable";
    public const string AiRawSuffix = "AI_Raw";
    public const string OverrideSuffix = "Override";
    public const string EffectiveRawSuffix = "Effective_Raw";
    public const string NormalizedSuffix = "Normalized";
    public const string ReasonSuffix = "Reason";
    public const string EvidenceSuffix = "Evidence";
    public const string EvidenceSourceSuffix = "Evidence_Source";
    public const string EvidenceSourceColumnSuffix = "Evidence_SourceColumn";
    public const string StatusSuffix = "Status";
    public const string EvaluatorScoreSuffix = "Evaluator_Score";
    public const string AnswerPresentSuffix = "Answer_Present";
    public const string QuestionNormalizedSuffix = "Question_Normalized";
    public const string QuestionRateSuffix = "Question_Rate";
    public const string QuestionEarnedSuffix = "Question_Earned";
    public const string SpecialAiRawSuffix = "Special_AI_Raw";
    public const string SpecialReasonSuffix = "Special_Reason";
    public const string SpecialEvidenceSuffix = "Special_Evidence";
    public const string SpecialEvidenceSourceSuffix = "Special_Evidence_Source";
    public const string SpecialEvidenceSourceColumnSuffix = "Special_Evidence_SourceColumn";
    public const string SpecialStatusSuffix = "Special_Status";
    public const string SpecialQuestionRateSuffix = "Special_Question_Rate";
    public const string SimilarityAiRawSuffix = "Similarity_AI_Raw";
    public const string SimilarityReasonSuffix = "Similarity_Reason";
    public const string SimilarityStatusSuffix = "Similarity_Status";
    public const string SimilarityPenaltySuffix = "Similarity_Penalty";
    public const string SimilarityPeerMaxSuffix = "Similarity_Peer_Max";
    public const string SimilarityPeerRowSuffix = "Similarity_Peer_Row";
    public const string BasePointsHeader = "Base_Points";
    public const string SpecialEarnedHeader = "Special_Earned";
    public const string FinalRawHeader = "Final_Raw";
    public const string FinalScoreHeader = "Final_Score";
    public const string SourceRowHeader = "SourceRow";

    public const string QuestionScoreSuffix = QuestionNormalizedSuffix;

    public const string OverallScoreHeader = FinalScoreHeader;

    private const int CriterionColumnCount = 10;
    private const int SpecialColumnCount = 6;
    private const int QuestionFixedColumnCount = 10;
    private const int RowTotalColumnCount = 4;
    private const NumberStyles OverrideNumberStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    private readonly FormulaCellWriter formulaCellWriter = new();
    private readonly FormulaSerializer formulaSerializer = new();
    private readonly UntrustedStringCellWriter stringCellWriter = new();
    private readonly WeightedScoreCalculator scoreCalculator = new();

    public ResultsSheetWriteResult Write(
        SpreadsheetDocument document,
        QuantificationSnapshot snapshot,
        AppOwnedSheetNames sheetNames,
        ConfigCellAddressMap configCells,
        IEnumerable<ResultsSheetRowInput> rows)
    {
        ArgumentNullException.ThrowIfNull(document);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The workbook part is missing.");
        return Write(workbookPart, snapshot, sheetNames, configCells, rows);
    }

    public ResultsSheetWriteResult Write(
        WorkbookPart workbookPart,
        QuantificationSnapshot snapshot,
        AppOwnedSheetNames sheetNames,
        ConfigCellAddressMap configCells,
        IEnumerable<ResultsSheetRowInput> rows)
    {
        ArgumentNullException.ThrowIfNull(workbookPart);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sheetNames);
        ArgumentNullException.ThrowIfNull(configCells);
        ArgumentNullException.ThrowIfNull(rows);

        PreparedSheet prepared = Prepare(workbookPart, snapshot, sheetNames, configCells, rows);
        WorkbookSheetWriter.AddWorksheet(workbookPart, sheetNames.ResultsSheetName, prepared.Worksheet);
        return new ResultsSheetWriteResult(
            sheetNames.ResultsSheetName,
            snapshot.Definition.HeaderRow,
            prepared.Rows.Length,
            prepared.Layout.ColumnCount,
            prepared.FormulaCells);
    }

    public ResultsSheetPreflightResult Preflight(
        QuantificationSnapshot snapshot,
        AppOwnedSheetNames sheetNames,
        ConfigCellAddressMap configCells)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sheetNames);
        ArgumentNullException.ThrowIfNull(configCells);

        List<ResultsSheetValidationError> errors = [];
        if (!snapshot.HasValidHash())
        {
            AddError(errors, "SNAPSHOT_HASH_INVALID", null, "Definition", snapshot.Definition.Id, snapshot.Definition.Name, "Sha256", "invalid");
        }

        if (!AppOwnedSheetNameResolver.IsValidWorksheetName(sheetNames.ResultsSheetName))
        {
            AddError(errors, "RESULTS_SHEET_NAME_INVALID", null, "Definition", snapshot.Definition.Id, snapshot.Definition.Name, "ResultsSheetName", "invalid");
        }

        if (!string.Equals(configCells.SheetName, sheetNames.ConfigSheetName, StringComparison.Ordinal))
        {
            AddError(errors, "CONFIG_BINDING_MISMATCH", null, "Definition", snapshot.Definition.Id, snapshot.Definition.Name, "ConfigSheetName", "mismatch");
        }

        ResultsLayout layout = CreateLayout(snapshot.Definition, errors);
        ValidateConfigReferences(layout, configCells, sheetNames, errors);
        if (errors.Count > 0)
        {
            return new ResultsSheetPreflightResult(layout.ColumnCount, 0, errors.ToImmutableArray());
        }

        ImmutableArray<int> representativeRows = [snapshot.Definition.LastDataRow];
        ImmutableArray<FormulaCellDefinition> formulas = BuildFormulas(
            snapshot.Definition,
            layout,
            representativeRows,
            sheetNames,
            configCells);
        ValidateFormulas(layout, representativeRows, formulas, sheetNames, configCells, errors);
        return new ResultsSheetPreflightResult(
            layout.ColumnCount,
            formulas.Length,
            errors.ToImmutableArray());
    }

    public override string ToString() =>
        $"{nameof(ResultsSheetWriter)} {{ Content = <redacted> }}";

    private PreparedSheet Prepare(
        WorkbookPart workbookPart,
        QuantificationSnapshot snapshot,
        AppOwnedSheetNames sheetNames,
        ConfigCellAddressMap configCells,
        IEnumerable<ResultsSheetRowInput> sourceRows)
    {
        List<ResultsSheetValidationError> errors = [];
        ValidateWorkbookBinding(workbookPart, snapshot, sheetNames, configCells, errors);
        ResultsLayout layout = CreateLayout(snapshot.Definition, errors);
        ImmutableArray<ResultsSheetRowInput> rows = sourceRows.ToImmutableArray();
        ValidateConfigReferences(layout, configCells, sheetNames, errors);
        ValidateRows(snapshot.Definition, layout, rows, errors);
        ThrowIfErrors(errors);

        ImmutableArray<PreparedRow> preparedRows = PrepareRows(
            snapshot.Definition,
            layout,
            rows);
        ImmutableArray<FormulaCellDefinition> formulas = BuildFormulas(
            snapshot.Definition,
            layout,
            preparedRows.Select(row => row.SourceRowNumber).ToImmutableArray(),
            sheetNames,
            configCells);
        ValidateFormulas(
            layout,
            preparedRows.Select(row => row.SourceRowNumber).ToImmutableArray(),
            formulas,
            sheetNames,
            configCells,
            errors);
        ThrowIfErrors(errors);

        Dictionary<FormulaCellAddress, FormulaCellDefinition> formulasByTarget = formulas.ToDictionary(
            definition => definition.Target);
        ImmutableArray<WrittenFormulaCell>.Builder writtenFormulaCells =
            ImmutableArray.CreateBuilder<WrittenFormulaCell>(formulas.Length);
        SheetData sheetData = new();
        sheetData.Append(CreateHeaderRow(snapshot.Definition.HeaderRow, layout));
        foreach (PreparedRow preparedRow in preparedRows)
        {
            sheetData.Append(CreateDataRow(
                preparedRow,
                formulasByTarget,
                sheetNames,
                layout,
                writtenFormulaCells));
        }

        Worksheet worksheet = new(sheetData);
        DataValidations dataValidations = CreateDataValidations(
            layout,
            preparedRows,
            sheetNames,
            configCells);
        if (dataValidations.Count?.Value > 0)
        {
            worksheet.Append(dataValidations);
        }

        return new PreparedSheet(
            worksheet,
            layout,
            preparedRows,
            writtenFormulaCells.ToImmutable());
    }

    private static void ValidateWorkbookBinding(
        WorkbookPart workbookPart,
        QuantificationSnapshot snapshot,
        AppOwnedSheetNames sheetNames,
        ConfigCellAddressMap configCells,
        List<ResultsSheetValidationError> errors)
    {
        if (!snapshot.HasValidHash())
        {
            AddError(errors, "SNAPSHOT_HASH_INVALID", null, "Definition", snapshot.Definition.Id, snapshot.Definition.Name, "Sha256", "invalid");
        }

        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("The workbook root is missing.");
        Sheets sheets = workbook.GetFirstChild<Sheets>()
            ?? throw new InvalidDataException("The workbook sheet collection is missing.");
        string[] existingNames = sheets.Elements<Sheet>()
            .Select(sheet => sheet.Name?.Value ?? throw new InvalidDataException("A worksheet name is missing."))
            .ToArray();

        if (!AppOwnedSheetNameResolver.IsValidWorksheetName(sheetNames.ResultsSheetName))
        {
            AddError(errors, "RESULTS_SHEET_NAME_INVALID", null, "Definition", snapshot.Definition.Id, snapshot.Definition.Name, "ResultsSheetName", "invalid");
        }

        if (!string.Equals(configCells.SheetName, sheetNames.ConfigSheetName, StringComparison.Ordinal))
        {
            AddError(errors, "CONFIG_BINDING_MISMATCH", null, "Definition", snapshot.Definition.Id, snapshot.Definition.Name, "ConfigSheetName", "mismatch");
        }

        if (!existingNames.Contains(sheetNames.ConfigSheetName, StringComparer.OrdinalIgnoreCase))
        {
            AddError(errors, "CONFIG_SHEET_MISSING", null, "Definition", snapshot.Definition.Id, snapshot.Definition.Name, "ConfigSheetName", "missing");
        }

        if (existingNames.Contains(sheetNames.ResultsSheetName, StringComparer.OrdinalIgnoreCase))
        {
            AddError(errors, "RESULTS_SHEET_ALREADY_EXISTS", null, "Definition", snapshot.Definition.Id, snapshot.Definition.Name, "ResultsSheetName", "collision");
        }
    }

    private static ResultsLayout CreateLayout(
        QuantificationDefinition definition,
        List<ResultsSheetValidationError> errors)
    {
        long requiredColumns = 1 + RowTotalColumnCount;
        foreach (QuestionDefinition question in definition.Questions.Where(question => question.Enabled))
        {
            foreach (EvaluatorDefinition evaluator in question.Evaluators.Where(evaluator => evaluator.Enabled))
            {
                requiredColumns += evaluator.Criteria.LongCount(criterion => criterion.Enabled) * CriterionColumnCount;
                requiredColumns++;
            }

            long enabledSpecials = question.SpecialEvaluations.LongCount(special => special.Enabled);
            requiredColumns += QuestionFixedColumnCount + (enabledSpecials * SpecialColumnCount);
            if (enabledSpecials > 0)
            {
                requiredColumns++;
            }
        }

        if (requiredColumns > FormulaPreflightValidator.MaximumExcelColumn)
        {
            AddError(
                errors,
                "COLUMN_LIMIT_EXCEEDED",
                null,
                "Definition",
                definition.Id,
                definition.Name,
                "ResultsColumns",
                requiredColumns.ToString(CultureInfo.InvariantCulture),
                FormulaPreflightValidator.MaximumExcelColumn.ToString(CultureInfo.InvariantCulture));
            return new ResultsLayout(
                Column(1, SourceRowHeader),
                [],
                Column(2, BasePointsHeader),
                Column(3, SpecialEarnedHeader),
                Column(4, FinalRawHeader),
                Column(5, FinalScoreHeader),
                checked((int)Math.Min(requiredColumns, FormulaPreflightValidator.MaximumExcelColumn)));
        }

        int nextColumn = 1;
        ColumnLayout sourceRow = Column(nextColumn++, SourceRowHeader);
        ImmutableArray<QuestionLayout>.Builder questions = ImmutableArray.CreateBuilder<QuestionLayout>();
        foreach (QuestionDefinition question in definition.Questions.Where(question => question.Enabled))
        {
            ImmutableArray<EvaluatorLayout>.Builder evaluators = ImmutableArray.CreateBuilder<EvaluatorLayout>();
            foreach (EvaluatorDefinition evaluator in question.Evaluators.Where(evaluator => evaluator.Enabled))
            {
                ImmutableArray<CriterionLayout>.Builder criteria = ImmutableArray.CreateBuilder<CriterionLayout>();
                foreach (CriterionDefinition criterion in evaluator.Criteria.Where(criterion => criterion.Enabled))
                {
                    string prefix = $"{question.Id}.{evaluator.Id}.{criterion.Id}";
                    CriterionLayout criterionLayout = new(
                        criterion,
                        evaluator,
                        Column(nextColumn++, prefix + "." + ScorableSuffix),
                        Column(nextColumn++, prefix + "." + AiRawSuffix),
                        Column(nextColumn++, prefix + "." + OverrideSuffix),
                        Column(nextColumn++, prefix + "." + EffectiveRawSuffix),
                        Column(nextColumn++, prefix + "." + NormalizedSuffix),
                        Column(nextColumn++, prefix + "." + ReasonSuffix),
                        Column(nextColumn++, prefix + "." + EvidenceSuffix),
                        Column(nextColumn++, prefix + "." + EvidenceSourceSuffix),
                        Column(nextColumn++, prefix + "." + EvidenceSourceColumnSuffix),
                        Column(nextColumn++, prefix + "." + StatusSuffix));
                    criteria.Add(criterionLayout);
                }

                evaluators.Add(new EvaluatorLayout(
                    evaluator,
                    criteria.ToImmutable(),
                    Column(nextColumn++, $"{question.Id}.{evaluator.Id}.{EvaluatorScoreSuffix}")));
            }

            string questionPrefix = question.Id + ".";
            ColumnLayout answerPresent = Column(nextColumn++, questionPrefix + AnswerPresentSuffix);
            ColumnLayout questionNormalized = Column(nextColumn++, questionPrefix + QuestionNormalizedSuffix);
            ColumnLayout questionRate = Column(nextColumn++, questionPrefix + QuestionRateSuffix);
            ColumnLayout questionEarned = Column(nextColumn++, questionPrefix + QuestionEarnedSuffix);
            ImmutableArray<SpecialLayout>.Builder specials = ImmutableArray.CreateBuilder<SpecialLayout>();
            foreach (SpecialEvaluationDefinition special in question.SpecialEvaluations.Where(special => special.Enabled))
            {
                string prefix = $"{question.Id}.{special.Id}.";
                specials.Add(new SpecialLayout(
                    special,
                    Column(nextColumn++, prefix + SpecialAiRawSuffix),
                    Column(nextColumn++, prefix + SpecialReasonSuffix),
                    Column(nextColumn++, prefix + SpecialEvidenceSuffix),
                    Column(nextColumn++, prefix + SpecialEvidenceSourceSuffix),
                    Column(nextColumn++, prefix + SpecialEvidenceSourceColumnSuffix),
                    Column(nextColumn++, prefix + SpecialStatusSuffix)));
            }

            ImmutableArray<SpecialLayout> builtSpecials = specials.ToImmutable();
            ColumnLayout? specialQuestionRate = builtSpecials.IsEmpty
                ? null
                : Column(nextColumn++, questionPrefix + SpecialQuestionRateSuffix);
            SimilarityLayout similarity = new(
                Column(nextColumn++, questionPrefix + SimilarityAiRawSuffix),
                Column(nextColumn++, questionPrefix + SimilarityReasonSuffix),
                Column(nextColumn++, questionPrefix + SimilarityStatusSuffix),
                Column(nextColumn++, questionPrefix + SimilarityPenaltySuffix),
                Column(nextColumn++, questionPrefix + SimilarityPeerMaxSuffix),
                Column(nextColumn++, questionPrefix + SimilarityPeerRowSuffix));
            questions.Add(new QuestionLayout(
                question,
                evaluators.ToImmutable(),
                answerPresent,
                questionNormalized,
                questionRate,
                questionEarned,
                builtSpecials,
                specialQuestionRate,
                similarity));
        }

        ColumnLayout basePoints = Column(nextColumn++, BasePointsHeader);
        ColumnLayout specialEarned = Column(nextColumn++, SpecialEarnedHeader);
        ColumnLayout finalRaw = Column(nextColumn++, FinalRawHeader);
        ColumnLayout finalScore = Column(nextColumn++, FinalScoreHeader);
        ResultsLayout layout = new(
            sourceRow,
            questions.ToImmutable(),
            basePoints,
            specialEarned,
            finalRaw,
            finalScore,
            nextColumn - 1);
        foreach (ColumnLayout column in layout.AllColumns)
        {
            if (column.Header.Length > UntrustedStringCellWriter.MaximumCellCharacters)
            {
                AddError(
                    errors,
                    "HEADER_CELL_LIMIT_EXCEEDED",
                    null,
                    "Definition",
                    definition.Id,
                    definition.Name,
                    "ResultsHeader",
                    column.Header.Length.ToString(CultureInfo.InvariantCulture),
                    UntrustedStringCellWriter.MaximumCellCharacters.ToString(CultureInfo.InvariantCulture));
            }
        }

        return layout;
    }

    private static void ValidateConfigReferences(
        ResultsLayout layout,
        ConfigCellAddressMap configCells,
        AppOwnedSheetNames sheetNames,
        List<ResultsSheetValidationError> errors)
    {
        HashSet<FormulaCellAddress> verified = configCells.VerifiedCells.ToHashSet();
        ValidateConfigAddress(configCells.RoundingDigitsCell, "Definition", "<rounding>", "<rounding>", "RoundingDigits", configCells, sheetNames, verified, errors);
        ValidateConfigAddress(configCells.BasePointsCell, "Definition", "<base>", "<base>", "BasePoints", configCells, sheetNames, verified, errors);
        ValidateConfigAddress(configCells.SpecialPointsCell, "Definition", "<special>", "<special>", "SpecialPoints", configCells, sheetNames, verified, errors);
        ValidateConfigAddress(configCells.SimilarityPenaltyWeightCell, "Definition", "<similarity>", "<similarity>", "SimilarityPenaltyWeight", configCells, sheetNames, verified, errors);
        ValidateConfigAddress(configCells.AllocationValidCell, "Definition", "<allocation>", "<allocation>", "AllocationValid", configCells, sheetNames, verified, errors);
        foreach (QuestionLayout question in layout.Questions)
        {
            ValidateMapAddress(configCells.QuestionPointsCells, question.Definition.Id, "Question", question.Definition.Id, question.Definition.DisplayName, "Points", configCells, sheetNames, verified, errors);
            foreach (EvaluatorLayout evaluator in question.Evaluators)
            {
                ValidateMapAddress(configCells.EvaluatorWeightCells, evaluator.Definition.Id, "Evaluator", evaluator.Definition.Id, evaluator.Definition.DisplayName, "Weight", configCells, sheetNames, verified, errors);
                foreach (CriterionLayout criterion in evaluator.Criteria)
                {
                    ValidateMapAddress(configCells.CriterionWeightCells, criterion.Definition.Id, "Criterion", criterion.Definition.Id, criterion.Definition.DisplayName, "Weight", configCells, sheetNames, verified, errors);
                    ValidateMapAddress(configCells.CriterionMinimumCells, criterion.Definition.Id, "Criterion", criterion.Definition.Id, criterion.Definition.DisplayName, "Minimum", configCells, sheetNames, verified, errors);
                    ValidateMapAddress(configCells.CriterionMaximumCells, criterion.Definition.Id, "Criterion", criterion.Definition.Id, criterion.Definition.DisplayName, "Maximum", configCells, sheetNames, verified, errors);
                }
            }
        }
    }

    private static void ValidateMapAddress(
        IReadOnlyDictionary<string, FormulaCellAddress> addresses,
        string key,
        string nodeKind,
        string nodeId,
        string displayName,
        string field,
        ConfigCellAddressMap configCells,
        AppOwnedSheetNames sheetNames,
        HashSet<FormulaCellAddress> verified,
        List<ResultsSheetValidationError> errors)
    {
        if (!addresses.TryGetValue(key, out FormulaCellAddress? address))
        {
            AddError(errors, "CONFIG_REFERENCE_MISSING", null, nodeKind, nodeId, displayName, field, "missing");
            return;
        }

        ValidateConfigAddress(address, nodeKind, nodeId, displayName, field, configCells, sheetNames, verified, errors);
    }

    private static void ValidateConfigAddress(
        FormulaCellAddress address,
        string nodeKind,
        string nodeId,
        string displayName,
        string field,
        ConfigCellAddressMap configCells,
        AppOwnedSheetNames sheetNames,
        HashSet<FormulaCellAddress> verified,
        List<ResultsSheetValidationError> errors)
    {
        if (!string.Equals(address.SheetName, configCells.SheetName, StringComparison.Ordinal)
            || !string.Equals(address.SheetName, sheetNames.ConfigSheetName, StringComparison.Ordinal)
            || !verified.Contains(address))
        {
            AddError(errors, "CONFIG_REFERENCE_NOT_VERIFIED", null, nodeKind, nodeId, displayName, field, "unverified");
        }
    }

    private static void ValidateRows(
        QuantificationDefinition definition,
        ResultsLayout layout,
        ImmutableArray<ResultsSheetRowInput> rows,
        List<ResultsSheetValidationError> errors)
    {
        Dictionary<int, ResultsSheetRowInput> byRow = [];
        foreach (ResultsSheetRowInput? row in rows)
        {
            if (row is null)
            {
                AddError(errors, "NULL_RESULT_ROW", null, "Definition", definition.Id, definition.Name, "Rows", "null");
                continue;
            }

            if (row.SourceRowNumber < definition.FirstDataRow || row.SourceRowNumber > definition.LastDataRow)
            {
                AddError(errors, "SOURCE_ROW_OUT_OF_RANGE", row.SourceRowNumber, "Definition", definition.Id, definition.Name, "SourceRowNumber", row.SourceRowNumber.ToString(CultureInfo.InvariantCulture));
                continue;
            }

            if (!byRow.TryAdd(row.SourceRowNumber, row))
            {
                AddError(errors, "DUPLICATE_SOURCE_ROW", row.SourceRowNumber, "Definition", definition.Id, definition.Name, "SourceRowNumber", "duplicate");
                continue;
            }

            ValidateQuestionInputs(row, layout, errors);
        }

        for (int sourceRow = definition.FirstDataRow; sourceRow <= definition.LastDataRow; sourceRow++)
        {
            if (!byRow.ContainsKey(sourceRow))
            {
                AddError(errors, "SOURCE_ROW_MISSING", sourceRow, "Definition", definition.Id, definition.Name, "Rows", "missing");
            }
        }
    }

    private static void ValidateQuestionInputs(
        ResultsSheetRowInput row,
        ResultsLayout layout,
        List<ResultsSheetValidationError> errors)
    {
        ImmutableArray<QuestionResultInput> questions = row.Questions.IsDefault ? [] : row.Questions;
        Dictionary<string, QuestionResultInput> byId = new(StringComparer.Ordinal);
        foreach (QuestionResultInput? question in questions)
        {
            if (question is null || string.IsNullOrWhiteSpace(question.QuestionId))
            {
                AddError(errors, "QUESTION_RESULT_ID_REQUIRED", row.SourceRowNumber, "Question", "<blank>", "<blank>", "QuestionId", "blank");
                continue;
            }

            if (!byId.TryAdd(question.QuestionId, question))
            {
                AddError(errors, "DUPLICATE_QUESTION_RESULT", row.SourceRowNumber, "Question", question.QuestionId, question.QuestionId, "QuestionId", "duplicate");
                continue;
            }

            QuestionLayout? expected = layout.Questions.SingleOrDefault(candidate => string.Equals(candidate.Definition.Id, question.QuestionId, StringComparison.Ordinal));
            if (expected is null)
            {
                AddError(errors, "QUESTION_RESULT_NOT_ENABLED", row.SourceRowNumber, "Question", question.QuestionId, question.QuestionId, "QuestionId", "unexpected");
                continue;
            }

            ValidateEvaluatorInputs(row.SourceRowNumber, question, expected, errors);
            ValidateAuxiliaryInputs(row.SourceRowNumber, question, expected, errors);
        }

        foreach (QuestionLayout expected in layout.Questions.Where(expected => !byId.ContainsKey(expected.Definition.Id)))
        {
            AddError(errors, "QUESTION_RESULT_MISSING", row.SourceRowNumber, "Question", expected.Definition.Id, expected.Definition.DisplayName, "Questions", "missing");
        }
    }

    private static void ValidateAuxiliaryInputs(
        int sourceRow,
        QuestionResultInput input,
        QuestionLayout layout,
        List<ResultsSheetValidationError> errors)
    {
        ImmutableArray<SpecialResultInput> specialResults = input.SpecialResults.IsDefault
            ? []
            : input.SpecialResults;
        Dictionary<string, SpecialResultInput> byId = new(StringComparer.Ordinal);
        foreach (SpecialResultInput? result in specialResults)
        {
            if (result is null || string.IsNullOrWhiteSpace(result.SpecialEvaluationId))
            {
                AddError(errors, "SPECIAL_RESULT_ID_REQUIRED", sourceRow, "SpecialEvaluation", "<blank>", "<blank>", "SpecialEvaluationId", "blank");
                continue;
            }

            if (!byId.TryAdd(result.SpecialEvaluationId, result))
            {
                AddError(errors, "DUPLICATE_SPECIAL_RESULT", sourceRow, "SpecialEvaluation", result.SpecialEvaluationId, result.SpecialEvaluationId, "SpecialEvaluationId", "duplicate");
                continue;
            }

            SpecialLayout? expected = layout.Specials.SingleOrDefault(candidate => string.Equals(
                candidate.Definition.Id,
                result.SpecialEvaluationId,
                StringComparison.Ordinal));
            if (expected is null)
            {
                AddError(errors, "SPECIAL_RESULT_NOT_ENABLED", sourceRow, "SpecialEvaluation", result.SpecialEvaluationId, result.SpecialEvaluationId, "SpecialEvaluationId", "unexpected");
                continue;
            }

            if (result.AiRaw is decimal raw && raw is < 0m or > 1m)
            {
                AddError(errors, "SPECIAL_SCORE_OUT_OF_RANGE", sourceRow, "SpecialEvaluation", expected.Definition.Id, expected.Definition.DisplayName, "AiRaw", raw.ToString("G29", CultureInfo.InvariantCulture));
            }

            ValidateAuxiliaryStrings(
                sourceRow,
                "SpecialEvaluation",
                expected.Definition.Id,
                expected.Definition.DisplayName,
                result.Status,
                result.Reason,
                result.Evidence,
                result.EvidenceSource,
                result.EvidenceSourceColumnId,
                errors);
        }

        SimilarityResultInput? similarity = input.Similarity;
        if (similarity is not null)
        {
            if (similarity.AiRaw is decimal raw && raw is < 0m or > 1m)
            {
                AddError(errors, "SIMILARITY_OUT_OF_RANGE", sourceRow, "Question", layout.Definition.Id, layout.Definition.DisplayName, "Similarity.AiRaw", raw.ToString("G29", CultureInfo.InvariantCulture));
            }

            if (similarity.PeerMax is decimal peerMax && peerMax is < 0m or > 1m)
            {
                AddError(errors, "SIMILARITY_PEER_OUT_OF_RANGE", sourceRow, "Question", layout.Definition.Id, layout.Definition.DisplayName, "Similarity.PeerMax", peerMax.ToString("G29", CultureInfo.InvariantCulture));
            }

            if (similarity.PeerRow is <= 0)
            {
                AddError(errors, "SIMILARITY_PEER_ROW_INVALID", sourceRow, "Question", layout.Definition.Id, layout.Definition.DisplayName, "Similarity.PeerRow", similarity.PeerRow.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (!ResultsStatusCodes.IsDefined(similarity.Status))
            {
                AddError(errors, "STATUS_INVALID", sourceRow, "Question", layout.Definition.Id, layout.Definition.DisplayName, "Similarity.Status", "unknown");
            }

            ValidateString(similarity.Status, sourceRow, "Question", layout.Definition.Id, layout.Definition.DisplayName, "Similarity.Status", errors);
            ValidateString(similarity.Reason, sourceRow, "Question", layout.Definition.Id, layout.Definition.DisplayName, "Similarity.Reason", errors);
        }
    }

    private static void ValidateAuxiliaryStrings(
        int sourceRow,
        string nodeKind,
        string nodeId,
        string displayName,
        string status,
        string reason,
        string evidence,
        string evidenceSource,
        string evidenceSourceColumn,
        List<ResultsSheetValidationError> errors)
    {
        if (!ResultsStatusCodes.IsDefined(status))
        {
            AddError(errors, "STATUS_INVALID", sourceRow, nodeKind, nodeId, displayName, "Status", "unknown");
        }

        ValidateString(status, sourceRow, nodeKind, nodeId, displayName, "Status", errors);
        ValidateString(reason, sourceRow, nodeKind, nodeId, displayName, "Reason", errors);
        ValidateString(evidence, sourceRow, nodeKind, nodeId, displayName, "Evidence", errors);
        ValidateString(evidenceSource, sourceRow, nodeKind, nodeId, displayName, "EvidenceSource", errors);
        ValidateString(evidenceSourceColumn, sourceRow, nodeKind, nodeId, displayName, "EvidenceSourceColumnId", errors);
    }

    private static void ValidateEvaluatorInputs(
        int sourceRow,
        QuestionResultInput questionInput,
        QuestionLayout questionLayout,
        List<ResultsSheetValidationError> errors)
    {
        ImmutableArray<EvaluatorResultInput> evaluators = questionInput.Evaluators.IsDefault ? [] : questionInput.Evaluators;
        Dictionary<string, EvaluatorResultInput> byId = new(StringComparer.Ordinal);
        foreach (EvaluatorResultInput? evaluator in evaluators)
        {
            if (evaluator is null || string.IsNullOrWhiteSpace(evaluator.EvaluatorId))
            {
                AddError(errors, "EVALUATOR_RESULT_ID_REQUIRED", sourceRow, "Evaluator", "<blank>", "<blank>", "EvaluatorId", "blank");
                continue;
            }

            if (!byId.TryAdd(evaluator.EvaluatorId, evaluator))
            {
                AddError(errors, "DUPLICATE_EVALUATOR_RESULT", sourceRow, "Evaluator", evaluator.EvaluatorId, evaluator.EvaluatorId, "EvaluatorId", "duplicate");
                continue;
            }

            EvaluatorLayout? expected = questionLayout.Evaluators.SingleOrDefault(candidate => string.Equals(candidate.Definition.Id, evaluator.EvaluatorId, StringComparison.Ordinal));
            if (expected is null)
            {
                AddError(errors, "EVALUATOR_RESULT_NOT_ENABLED", sourceRow, "Evaluator", evaluator.EvaluatorId, evaluator.EvaluatorId, "EvaluatorId", "unexpected");
                continue;
            }

            ValidateEvaluatorResult(sourceRow, questionInput, evaluator, expected, errors);
        }

        foreach (EvaluatorLayout expected in questionLayout.Evaluators.Where(expected => !byId.ContainsKey(expected.Definition.Id)))
        {
            AddError(errors, "EVALUATOR_RESULT_MISSING", sourceRow, "Evaluator", expected.Definition.Id, expected.Definition.DisplayName, "Evaluators", "missing");
        }
    }

    private static void ValidateEvaluatorResult(
        int sourceRow,
        QuestionResultInput questionInput,
        EvaluatorResultInput evaluatorInput,
        EvaluatorLayout evaluatorLayout,
        List<ResultsSheetValidationError> errors)
    {
        if (!ResultsStatusCodes.IsDefined(evaluatorInput.Status))
        {
            AddError(errors, "STATUS_INVALID", sourceRow, "Evaluator", evaluatorLayout.Definition.Id, evaluatorLayout.Definition.DisplayName, "Status", "unknown");
        }

        ValidateString(evaluatorInput.Status, sourceRow, "Evaluator", evaluatorLayout.Definition.Id, evaluatorLayout.Definition.DisplayName, "Status", errors);
        if (!questionInput.Scorable && evaluatorInput.AiResult is not null)
        {
            AddError(errors, "AI_RESULT_NOT_ALLOWED", sourceRow, "Evaluator", evaluatorLayout.Definition.Id, evaluatorLayout.Definition.DisplayName, "AiResult", "unscorable");
        }

        Dictionary<string, CriterionOverrideInput> overrides = new(StringComparer.Ordinal);
        foreach (CriterionOverrideInput? item in evaluatorInput.Overrides.IsDefault ? [] : evaluatorInput.Overrides)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.CriterionId))
            {
                AddError(errors, "OVERRIDE_CRITERION_ID_REQUIRED", sourceRow, "Criterion", "<blank>", "<blank>", "Override", "blank");
                continue;
            }

            if (!overrides.TryAdd(item.CriterionId, item))
            {
                AddError(errors, "DUPLICATE_OVERRIDE", sourceRow, "Criterion", item.CriterionId, item.CriterionId, "Override", "duplicate");
            }
        }

        foreach ((string criterionId, CriterionOverrideInput item) in overrides)
        {
            CriterionLayout? criterion = evaluatorLayout.Criteria.SingleOrDefault(candidate => string.Equals(candidate.Definition.Id, criterionId, StringComparison.Ordinal));
            if (criterion is null)
            {
                AddError(errors, "OVERRIDE_CRITERION_NOT_ENABLED", sourceRow, "Criterion", criterionId, criterionId, "Override", "unexpected");
                continue;
            }

            _ = ParseOverride(sourceRow, questionInput.Scorable, criterion, item.Value, errors);
        }

        if (evaluatorInput.AiResult is not QuantificationResult result)
        {
            return;
        }

        if (!string.Equals(result.EvaluatorId, evaluatorLayout.Definition.Id, StringComparison.Ordinal))
        {
            AddError(errors, "AI_EVALUATOR_ID_MISMATCH", sourceRow, "Evaluator", evaluatorLayout.Definition.Id, evaluatorLayout.Definition.DisplayName, "AiResult.EvaluatorId", "mismatch");
        }

        ImmutableArray<CriterionQuantificationResult> criteria = result.Criteria.IsDefault ? [] : result.Criteria;
        Dictionary<string, CriterionQuantificationResult> resultById = new(StringComparer.Ordinal);
        foreach (CriterionQuantificationResult? resultCriterion in criteria)
        {
            if (resultCriterion is null || string.IsNullOrWhiteSpace(resultCriterion.CriterionId))
            {
                AddError(errors, "AI_CRITERION_ID_REQUIRED", sourceRow, "Criterion", "<blank>", "<blank>", "AiResult.Criteria", "blank");
                continue;
            }

            if (!resultById.TryAdd(resultCriterion.CriterionId, resultCriterion))
            {
                AddError(errors, "DUPLICATE_AI_CRITERION", sourceRow, "Criterion", resultCriterion.CriterionId, resultCriterion.CriterionId, "AiResult.Criteria", "duplicate");
                continue;
            }

            CriterionLayout? expected = evaluatorLayout.Criteria.SingleOrDefault(candidate => string.Equals(candidate.Definition.Id, resultCriterion.CriterionId, StringComparison.Ordinal));
            if (expected is null)
            {
                AddError(errors, "AI_CRITERION_NOT_ENABLED", sourceRow, "Criterion", resultCriterion.CriterionId, resultCriterion.CriterionId, "AiResult.Criteria", "unexpected");
                continue;
            }

            ValidateString(resultCriterion.Reason, sourceRow, "Criterion", expected.Definition.Id, expected.Definition.DisplayName, "Reason", errors);
            ValidateString(resultCriterion.Evidence, sourceRow, "Criterion", expected.Definition.Id, expected.Definition.DisplayName, "Evidence", errors);
            ValidateString(resultCriterion.EvidenceSourceColumnId, sourceRow, "Criterion", expected.Definition.Id, expected.Definition.DisplayName, "EvidenceSourceColumnId", errors);
            if (!Enum.IsDefined(typeof(EvidenceSourceKind), resultCriterion.EvidenceSource))
            {
                AddError(errors, "EVIDENCE_SOURCE_INVALID", sourceRow, "Criterion", expected.Definition.Id, expected.Definition.DisplayName, "EvidenceSource", "unknown");
            }
        }

        foreach (CriterionLayout expected in evaluatorLayout.Criteria.Where(expected => !resultById.ContainsKey(expected.Definition.Id)))
        {
            AddError(errors, "AI_CRITERION_MISSING", sourceRow, "Criterion", expected.Definition.Id, expected.Definition.DisplayName, "AiResult.Criteria", "missing");
        }
    }

    private static decimal? ParseOverride(
        int sourceRow,
        bool scorable,
        CriterionLayout criterion,
        string? value,
        List<ResultsSheetValidationError>? errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (!scorable)
        {
            AddError(errors, "OVERRIDE_NOT_ALLOWED", sourceRow, "Criterion", criterion.Definition.Id, criterion.Definition.DisplayName, "Override", "unscorable");
            return null;
        }

        if (value.Length > UntrustedStringCellWriter.MaximumCellCharacters)
        {
            AddError(errors, "OVERRIDE_TEXT_LIMIT_EXCEEDED", sourceRow, "Criterion", criterion.Definition.Id, criterion.Definition.DisplayName, "Override", value.Length.ToString(CultureInfo.InvariantCulture));
            return null;
        }

        if (!decimal.TryParse(value, OverrideNumberStyles, CultureInfo.InvariantCulture, out decimal parsed))
        {
            AddError(errors, "OVERRIDE_NOT_NUMERIC", sourceRow, "Criterion", criterion.Definition.Id, criterion.Definition.DisplayName, "Override", "non-numeric");
            return null;
        }

        ScoreRange range = EffectiveRange(criterion.Definition, criterion.ParentEvaluator);
        if (parsed < range.Minimum || parsed > range.Maximum)
        {
            AddError(errors, "OVERRIDE_OUT_OF_RANGE", sourceRow, "Criterion", criterion.Definition.Id, criterion.Definition.DisplayName, "Override", "out-of-range");
            return null;
        }

        return parsed;
    }

    private static void ValidateString(
        string? value,
        int sourceRow,
        string nodeKind,
        string nodeId,
        string displayName,
        string field,
        List<ResultsSheetValidationError> errors)
    {
        if (value is null)
        {
            AddError(errors, "STRING_VALUE_REQUIRED", sourceRow, nodeKind, nodeId, displayName, field, "null");
        }
        else if (value.Length > UntrustedStringCellWriter.MaximumCellCharacters)
        {
            AddError(errors, "STRING_CELL_LIMIT_EXCEEDED", sourceRow, nodeKind, nodeId, displayName, field, value.Length.ToString(CultureInfo.InvariantCulture));
        }
    }

    private ImmutableArray<PreparedRow> PrepareRows(
        QuantificationDefinition definition,
        ResultsLayout layout,
        ImmutableArray<ResultsSheetRowInput> rows)
    {
        Dictionary<int, ResultsSheetRowInput> rowsByNumber = rows.ToDictionary(row => row.SourceRowNumber);
        ImmutableArray<PreparedRow>.Builder preparedRows = ImmutableArray.CreateBuilder<PreparedRow>();
        for (int sourceRow = definition.FirstDataRow; sourceRow <= definition.LastDataRow; sourceRow++)
        {
            ResultsSheetRowInput rowInput = rowsByNumber[sourceRow];
            Dictionary<string, QuestionResultInput> questionInputs = (rowInput.Questions.IsDefault ? [] : rowInput.Questions)
                .ToDictionary(question => question.QuestionId, StringComparer.Ordinal);
            ImmutableArray<PreparedQuestion>.Builder questions = ImmutableArray.CreateBuilder<PreparedQuestion>();
            foreach (QuestionLayout questionLayout in layout.Questions)
            {
                QuestionResultInput questionInput = questionInputs[questionLayout.Definition.Id];
                Dictionary<string, EvaluatorResultInput> evaluatorInputs = (questionInput.Evaluators.IsDefault ? [] : questionInput.Evaluators)
                    .ToDictionary(evaluator => evaluator.EvaluatorId, StringComparer.Ordinal);
                ImmutableArray<PreparedEvaluator>.Builder evaluators = ImmutableArray.CreateBuilder<PreparedEvaluator>();
                foreach (EvaluatorLayout evaluatorLayout in questionLayout.Evaluators)
                {
                    EvaluatorResultInput evaluatorInput = evaluatorInputs[evaluatorLayout.Definition.Id];
                    Dictionary<string, CriterionQuantificationResult> aiResults = evaluatorInput.AiResult is null
                        ? new Dictionary<string, CriterionQuantificationResult>(StringComparer.Ordinal)
                        : evaluatorInput.AiResult.Criteria.ToDictionary(criterion => criterion.CriterionId, StringComparer.Ordinal);
                    Dictionary<string, CriterionOverrideInput> overrideInputs = (evaluatorInput.Overrides.IsDefault ? [] : evaluatorInput.Overrides)
                        .ToDictionary(item => item.CriterionId, StringComparer.Ordinal);
                    ImmutableArray<PreparedCriterion>.Builder criteria = ImmutableArray.CreateBuilder<PreparedCriterion>();
                    foreach (CriterionLayout criterionLayout in evaluatorLayout.Criteria)
                    {
                        aiResults.TryGetValue(criterionLayout.Definition.Id, out CriterionQuantificationResult? aiResult);
                        overrideInputs.TryGetValue(criterionLayout.Definition.Id, out CriterionOverrideInput? overrideInput);
                        decimal? overrideValue = ParseOverride(
                            sourceRow,
                            questionInput.Scorable,
                            criterionLayout,
                            overrideInput?.Value,
                            errors: null);
                        decimal? aiRaw = questionInput.Scorable ? aiResult?.RawScore : null;
                        ScoreRange range = EffectiveRange(criterionLayout.Definition, evaluatorLayout.Definition);
                        EffectiveRawSelection effective = scoreCalculator.SelectEffectiveRaw(
                            questionInput.Scorable,
                            aiRaw,
                            overrideValue,
                            range);
                        decimal? normalized = scoreCalculator.Normalize(
                            effective.Value,
                            range,
                            definition.RoundingDigits);
                        criteria.Add(new PreparedCriterion(
                            criterionLayout,
                            questionInput.Scorable,
                            aiRaw,
                            overrideValue,
                            effective.Value,
                            normalized,
                            aiResult?.Reason ?? string.Empty,
                            aiResult?.Evidence ?? string.Empty,
                            aiResult is null ? string.Empty : EvidenceSourceName(aiResult.EvidenceSource),
                            aiResult?.EvidenceSourceColumnId ?? string.Empty,
                            evaluatorInput.Status));
                    }

                    ImmutableArray<PreparedCriterion> preparedCriteria = criteria.ToImmutable();
                    decimal? evaluatorScore = scoreCalculator.Aggregate(
                        preparedCriteria.Select(criterion => new WeightedScoreInput(
                            criterion.Normalized,
                            criterion.Layout.Definition.Weight)),
                        definition.RoundingDigits);
                    evaluators.Add(new PreparedEvaluator(evaluatorLayout, preparedCriteria, evaluatorScore));
                }

                ImmutableArray<PreparedEvaluator> preparedEvaluators = evaluators.ToImmutable();
                decimal? questionNormalized = scoreCalculator.Aggregate(
                    preparedEvaluators.Select(evaluator => new WeightedScoreInput(
                        evaluator.Score,
                        evaluator.Layout.Definition.Weight)),
                    definition.RoundingDigits);

                Dictionary<string, SpecialResultInput> specialInputs =
                    (questionInput.SpecialResults.IsDefault ? [] : questionInput.SpecialResults)
                    .ToDictionary(item => item.SpecialEvaluationId, StringComparer.Ordinal);
                ImmutableArray<PreparedSpecial>.Builder specials = ImmutableArray.CreateBuilder<PreparedSpecial>();
                foreach (SpecialLayout specialLayout in questionLayout.Specials)
                {
                    specialInputs.TryGetValue(specialLayout.Definition.Id, out SpecialResultInput? specialInput);
                    specials.Add(new PreparedSpecial(
                        specialLayout,
                        specialInput?.AiRaw,
                        specialInput?.Reason ?? string.Empty,
                        specialInput?.Evidence ?? string.Empty,
                        specialInput?.EvidenceSource ?? string.Empty,
                        specialInput?.EvidenceSourceColumnId ?? string.Empty,
                        specialInput?.Status ?? (definition.SpecialPoints == 0m
                            ? ResultsStatusCodes.NotRunZeroBudget
                            : ResultsStatusCodes.AiRuntimeFailed)));
                }

                ImmutableArray<PreparedSpecial> preparedSpecials = specials.ToImmutable();
                decimal? specialQuestionRate = definition.SpecialPoints == 0m || preparedSpecials.IsEmpty
                    ? null
                    : scoreCalculator.SpecialQuestionRate(
                        preparedSpecials.Select(item => item.AiRaw),
                        definition.RoundingDigits);
                SimilarityResultInput similarityInput = questionInput.Similarity
                    ?? new SimilarityResultInput
                    {
                        AiRaw = questionInput.Scorable ? null : 0m,
                        Status = questionInput.Scorable
                            ? ResultsStatusCodes.AiRuntimeFailed
                            : ResultsStatusCodes.Empty,
                    };
                decimal? questionRate = scoreCalculator.QuestionRate(
                    questionInput.Scorable,
                    questionNormalized);
                decimal? questionEarned = scoreCalculator.QuestionEarned(
                    questionRate,
                    questionLayout.Definition.Points,
                    definition.RoundingDigits);
                decimal? similarityPenalty = scoreCalculator.SimilarityPenalty(
                    questionLayout.Definition.Points,
                    similarityInput.AiRaw,
                    definition.SimilarityPenaltyWeight,
                    definition.RoundingDigits);
                questions.Add(new PreparedQuestion(
                    questionLayout,
                    preparedEvaluators,
                    questionInput.Scorable,
                    questionNormalized,
                    questionRate,
                    questionEarned,
                    preparedSpecials,
                    specialQuestionRate,
                    new PreparedSimilarity(
                        questionLayout.Similarity,
                        similarityInput.AiRaw,
                        similarityInput.Reason,
                        similarityInput.Status,
                        similarityPenalty,
                        similarityInput.PeerMax,
                        similarityInput.PeerRow)));
            }

            ImmutableArray<PreparedQuestion> preparedQuestions = questions.ToImmutable();
            decimal? specialEarned = scoreCalculator.SpecialEarned(
                definition.SpecialPoints,
                preparedQuestions
                    .Where(question => question.Layout.SpecialQuestionRate is not null)
                    .Select(question => question.SpecialQuestionRate),
                definition.RoundingDigits);
            decimal? finalRaw = scoreCalculator.FinalRaw(
                definition.BasePoints,
                preparedQuestions.Select(question => question.Earned),
                specialEarned,
                preparedQuestions.Select(question => question.Similarity.Penalty),
                definition.RoundingDigits);
            preparedRows.Add(new PreparedRow(
                sourceRow,
                preparedQuestions,
                definition.BasePoints,
                specialEarned,
                finalRaw,
                WeightedScoreCalculator.FinalScore(finalRaw)));
        }

        return preparedRows.ToImmutable();
    }

    private static ImmutableArray<FormulaCellDefinition> BuildFormulas(
        QuantificationDefinition definition,
        ResultsLayout layout,
        ImmutableArray<int> sourceRows,
        AppOwnedSheetNames sheetNames,
        ConfigCellAddressMap configCells)
    {
        ImmutableArray<FormulaCellDefinition>.Builder formulas = ImmutableArray.CreateBuilder<FormulaCellDefinition>();
        foreach (int sourceRow in sourceRows)
        {
            foreach (QuestionLayout question in layout.Questions)
            {
                foreach (EvaluatorLayout evaluator in question.Evaluators)
                {
                    foreach (CriterionLayout criterion in evaluator.Criteria)
                    {
                        FormulaIdentity effectiveIdentity = new(
                            "Criterion",
                            criterion.Definition.Id,
                            criterion.Definition.DisplayName,
                            EffectiveRawSuffix);
                        FormulaCellAddress effectiveTarget = Address(sheetNames.ResultsSheetName, criterion.EffectiveRaw, sourceRow);
                        formulas.Add(new FormulaCellDefinition(
                            effectiveTarget,
                            effectiveIdentity,
                            FormulaExpressions.EffectiveRaw(
                                Ref(Address(sheetNames.ResultsSheetName, criterion.Scorable, sourceRow)),
                                Ref(Address(sheetNames.ResultsSheetName, criterion.AiRaw, sourceRow)),
                                Ref(Address(sheetNames.ResultsSheetName, criterion.Override, sourceRow)),
                                Ref(configCells.CriterionMinimumCells[criterion.Definition.Id], absolute: true),
                                Ref(configCells.CriterionMaximumCells[criterion.Definition.Id], absolute: true))));
                        formulas.Add(new FormulaCellDefinition(
                            Address(sheetNames.ResultsSheetName, criterion.Normalized, sourceRow),
                            effectiveIdentity with { Field = NormalizedSuffix },
                            FormulaExpressions.Normalized(
                                Ref(effectiveTarget),
                                Ref(configCells.CriterionMinimumCells[criterion.Definition.Id], absolute: true),
                                Ref(configCells.CriterionMaximumCells[criterion.Definition.Id], absolute: true),
                                Ref(configCells.RoundingDigitsCell, absolute: true))));
                    }

                    formulas.Add(new FormulaCellDefinition(
                        Address(sheetNames.ResultsSheetName, evaluator.Score, sourceRow),
                        new FormulaIdentity(
                            "Evaluator",
                            evaluator.Definition.Id,
                            evaluator.Definition.DisplayName,
                            EvaluatorScoreSuffix),
                        FormulaExpressions.Aggregate(
                            evaluator.Criteria.Select(criterion => new WeightedFormulaChild(
                                Ref(Address(sheetNames.ResultsSheetName, criterion.Normalized, sourceRow)),
                                Ref(configCells.CriterionWeightCells[criterion.Definition.Id], absolute: true))),
                            Ref(configCells.RoundingDigitsCell, absolute: true))));
                }

                formulas.Add(new FormulaCellDefinition(
                    Address(sheetNames.ResultsSheetName, question.Normalized, sourceRow),
                    new FormulaIdentity(
                        "Question",
                        question.Definition.Id,
                        question.Definition.DisplayName,
                        QuestionNormalizedSuffix),
                    FormulaExpressions.Aggregate(
                        question.Evaluators.Select(evaluator => new WeightedFormulaChild(
                            Ref(Address(sheetNames.ResultsSheetName, evaluator.Score, sourceRow)),
                            Ref(configCells.EvaluatorWeightCells[evaluator.Definition.Id], absolute: true))),
                        Ref(configCells.RoundingDigitsCell, absolute: true))));

                formulas.Add(new FormulaCellDefinition(
                    Address(sheetNames.ResultsSheetName, question.Rate, sourceRow),
                    new FormulaIdentity("Question", question.Definition.Id, question.Definition.DisplayName, QuestionRateSuffix),
                    FormulaExpressions.QuestionRate(
                        Ref(Address(sheetNames.ResultsSheetName, question.AnswerPresent, sourceRow)),
                        Ref(Address(sheetNames.ResultsSheetName, question.Normalized, sourceRow)))));
                formulas.Add(new FormulaCellDefinition(
                    Address(sheetNames.ResultsSheetName, question.Earned, sourceRow),
                    new FormulaIdentity("Question", question.Definition.Id, question.Definition.DisplayName, QuestionEarnedSuffix),
                    FormulaExpressions.QuestionEarned(
                        Ref(Address(sheetNames.ResultsSheetName, question.Rate, sourceRow)),
                        Ref(configCells.QuestionPointsCells[question.Definition.Id], absolute: true),
                        Ref(configCells.RoundingDigitsCell, absolute: true))));

                if (question.SpecialQuestionRate is ColumnLayout specialQuestionRate)
                {
                    formulas.Add(new FormulaCellDefinition(
                        Address(sheetNames.ResultsSheetName, specialQuestionRate, sourceRow),
                        new FormulaIdentity("Question", question.Definition.Id, question.Definition.DisplayName, SpecialQuestionRateSuffix),
                        FormulaExpressions.SpecialQuestionRate(
                            question.Specials.Select(special => Ref(Address(
                                sheetNames.ResultsSheetName,
                                special.AiRaw,
                                sourceRow))),
                            Ref(configCells.SpecialPointsCell, absolute: true),
                            Ref(configCells.RoundingDigitsCell, absolute: true))));
                }

                formulas.Add(new FormulaCellDefinition(
                    Address(sheetNames.ResultsSheetName, question.Similarity.Penalty, sourceRow),
                    new FormulaIdentity("Question", question.Definition.Id, question.Definition.DisplayName, SimilarityPenaltySuffix),
                    FormulaExpressions.SimilarityPenalty(
                        Ref(configCells.QuestionPointsCells[question.Definition.Id], absolute: true),
                        Ref(Address(sheetNames.ResultsSheetName, question.Similarity.AiRaw, sourceRow)),
                        Ref(configCells.SimilarityPenaltyWeightCell, absolute: true),
                        Ref(configCells.RoundingDigitsCell, absolute: true))));
            }

            formulas.Add(new FormulaCellDefinition(
                Address(sheetNames.ResultsSheetName, layout.BasePoints, sourceRow),
                new FormulaIdentity("Definition", definition.Id, definition.Name, BasePointsHeader),
                new FormulaCell(Ref(configCells.BasePointsCell, absolute: true))));
            formulas.Add(new FormulaCellDefinition(
                Address(sheetNames.ResultsSheetName, layout.SpecialEarned, sourceRow),
                new FormulaIdentity("Definition", definition.Id, definition.Name, SpecialEarnedHeader),
                FormulaExpressions.SpecialEarned(
                    layout.Questions
                        .Where(question => question.SpecialQuestionRate is not null)
                        .Select(question => Ref(Address(
                            sheetNames.ResultsSheetName,
                            question.SpecialQuestionRate!,
                            sourceRow))),
                    Ref(configCells.SpecialPointsCell, absolute: true),
                    Ref(configCells.RoundingDigitsCell, absolute: true))));
            formulas.Add(new FormulaCellDefinition(
                Address(sheetNames.ResultsSheetName, layout.FinalRaw, sourceRow),
                new FormulaIdentity("Definition", definition.Id, definition.Name, FinalRawHeader),
                FormulaExpressions.FinalRaw(
                    Ref(configCells.AllocationValidCell, absolute: true),
                    Ref(configCells.BasePointsCell, absolute: true),
                    layout.Questions.Select(question => Ref(Address(
                        sheetNames.ResultsSheetName,
                        question.Earned,
                        sourceRow))),
                    Ref(Address(sheetNames.ResultsSheetName, layout.SpecialEarned, sourceRow)),
                    layout.Questions.Select(question => Ref(Address(
                        sheetNames.ResultsSheetName,
                        question.Similarity.Penalty,
                        sourceRow))),
                    Ref(configCells.RoundingDigitsCell, absolute: true))));
            formulas.Add(new FormulaCellDefinition(
                Address(sheetNames.ResultsSheetName, layout.FinalScore, sourceRow),
                new FormulaIdentity("Definition", definition.Id, definition.Name, FinalScoreHeader),
                FormulaExpressions.FinalScore(Ref(Address(
                    sheetNames.ResultsSheetName,
                    layout.FinalRaw,
                    sourceRow)))));
        }

        return formulas.ToImmutable();
    }

    private static void ValidateFormulas(
        ResultsLayout layout,
        ImmutableArray<int> sourceRows,
        ImmutableArray<FormulaCellDefinition> formulas,
        AppOwnedSheetNames sheetNames,
        ConfigCellAddressMap configCells,
        List<ResultsSheetValidationError> errors)
    {
        HashSet<FormulaCellAddress> verified = configCells.VerifiedCells.ToHashSet();
        foreach (int sourceRow in sourceRows)
        {
            foreach (ColumnLayout column in layout.AllColumns)
            {
                verified.Add(Address(sheetNames.ResultsSheetName, column, sourceRow));
            }
        }

        FormulaPreflightResult result = new FormulaPreflightValidator().Validate(
            formulas,
            new FormulaPreflightContext(sheetNames.AllSheetNames, verified));
        foreach (FormulaPreflightError error in result.Errors)
        {
            AddError(
                errors,
                error.Code,
                error.Identity.NodeKind == "Definition" ? null : TryFindFormulaRow(formulas, error),
                error.Identity.NodeKind,
                error.Identity.NodeId,
                error.Identity.DisplayName,
                error.Identity.Field,
                error.ActualDimension,
                error.Limit);
        }
    }

    private Row CreateHeaderRow(int headerRow, ResultsLayout layout)
    {
        Row row = new() { RowIndex = checked((uint)headerRow) };
        foreach (ColumnLayout column in layout.AllColumns)
        {
            stringCellWriter.Write(row, CellReference(column, headerRow), column.Header);
        }

        return row;
    }

    private Row CreateDataRow(
        PreparedRow prepared,
        IReadOnlyDictionary<FormulaCellAddress, FormulaCellDefinition> formulas,
        AppOwnedSheetNames sheetNames,
        ResultsLayout layout,
        ImmutableArray<WrittenFormulaCell>.Builder writtenFormulaCells)
    {
        Row row = new() { RowIndex = checked((uint)prepared.SourceRowNumber) };
        row.Append(SpreadsheetLiteral.CreateNumberCell(
            CellReference(layout.SourceRow, prepared.SourceRowNumber),
            prepared.SourceRowNumber));
        foreach (PreparedQuestion question in prepared.Questions)
        {
            foreach (PreparedEvaluator evaluator in question.Evaluators)
            {
                foreach (PreparedCriterion criterion in evaluator.Criteria)
                {
                    row.Append(SpreadsheetLiteral.CreateNumberCell(
                        CellReference(criterion.Layout.Scorable, prepared.SourceRowNumber),
                        criterion.Scorable ? 1 : 0));
                    row.Append(CreateOptionalNumberCell(
                        criterion.Layout.AiRaw,
                        prepared.SourceRowNumber,
                        criterion.AiRaw));
                    row.Append(CreateOptionalNumberCell(
                        criterion.Layout.Override,
                        prepared.SourceRowNumber,
                        criterion.Override));
                    AppendFormula(row, formulas, sheetNames, criterion.Layout.EffectiveRaw, prepared.SourceRowNumber, criterion.EffectiveRaw, writtenFormulaCells);
                    AppendFormula(row, formulas, sheetNames, criterion.Layout.Normalized, prepared.SourceRowNumber, criterion.Normalized, writtenFormulaCells);
                    stringCellWriter.Write(row, CellReference(criterion.Layout.Reason, prepared.SourceRowNumber), criterion.Reason);
                    stringCellWriter.Write(row, CellReference(criterion.Layout.Evidence, prepared.SourceRowNumber), criterion.Evidence);
                    stringCellWriter.Write(row, CellReference(criterion.Layout.EvidenceSource, prepared.SourceRowNumber), criterion.EvidenceSource);
                    stringCellWriter.Write(row, CellReference(criterion.Layout.EvidenceSourceColumn, prepared.SourceRowNumber), criterion.EvidenceSourceColumn);
                    stringCellWriter.Write(row, CellReference(criterion.Layout.Status, prepared.SourceRowNumber), criterion.Status);
                }

                AppendFormula(row, formulas, sheetNames, evaluator.Layout.Score, prepared.SourceRowNumber, evaluator.Score, writtenFormulaCells);
            }

            row.Append(SpreadsheetLiteral.CreateNumberCell(
                CellReference(question.Layout.AnswerPresent, prepared.SourceRowNumber),
                question.AnswerPresent ? 1 : 0));
            AppendFormula(row, formulas, sheetNames, question.Layout.Normalized, prepared.SourceRowNumber, question.Normalized, writtenFormulaCells);
            AppendFormula(row, formulas, sheetNames, question.Layout.Rate, prepared.SourceRowNumber, question.Rate, writtenFormulaCells);
            AppendFormula(row, formulas, sheetNames, question.Layout.Earned, prepared.SourceRowNumber, question.Earned, writtenFormulaCells);
            foreach (PreparedSpecial special in question.Specials)
            {
                row.Append(CreateOptionalNumberCell(special.Layout.AiRaw, prepared.SourceRowNumber, special.AiRaw));
                stringCellWriter.Write(row, CellReference(special.Layout.Reason, prepared.SourceRowNumber), special.Reason);
                stringCellWriter.Write(row, CellReference(special.Layout.Evidence, prepared.SourceRowNumber), special.Evidence);
                stringCellWriter.Write(row, CellReference(special.Layout.EvidenceSource, prepared.SourceRowNumber), special.EvidenceSource);
                stringCellWriter.Write(row, CellReference(special.Layout.EvidenceSourceColumn, prepared.SourceRowNumber), special.EvidenceSourceColumn);
                stringCellWriter.Write(row, CellReference(special.Layout.Status, prepared.SourceRowNumber), special.Status);
            }

            if (question.Layout.SpecialQuestionRate is ColumnLayout specialRate)
            {
                AppendFormula(row, formulas, sheetNames, specialRate, prepared.SourceRowNumber, question.SpecialQuestionRate, writtenFormulaCells);
            }

            row.Append(CreateOptionalNumberCell(
                question.Similarity.Layout.AiRaw,
                prepared.SourceRowNumber,
                question.Similarity.AiRaw));
            stringCellWriter.Write(row, CellReference(question.Similarity.Layout.Reason, prepared.SourceRowNumber), question.Similarity.Reason);
            stringCellWriter.Write(row, CellReference(question.Similarity.Layout.Status, prepared.SourceRowNumber), question.Similarity.Status);
            AppendFormula(row, formulas, sheetNames, question.Similarity.Layout.Penalty, prepared.SourceRowNumber, question.Similarity.Penalty, writtenFormulaCells);
            row.Append(CreateOptionalNumberCell(
                question.Similarity.Layout.PeerMax,
                prepared.SourceRowNumber,
                question.Similarity.PeerMax));
            row.Append(question.Similarity.PeerRow is int peerRow
                ? SpreadsheetLiteral.CreateNumberCell(CellReference(question.Similarity.Layout.PeerRow, prepared.SourceRowNumber), peerRow)
                : new Cell { CellReference = CellReference(question.Similarity.Layout.PeerRow, prepared.SourceRowNumber) });
        }

        AppendFormula(row, formulas, sheetNames, layout.BasePoints, prepared.SourceRowNumber, prepared.BasePoints, writtenFormulaCells);
        AppendFormula(row, formulas, sheetNames, layout.SpecialEarned, prepared.SourceRowNumber, prepared.SpecialEarned, writtenFormulaCells);
        AppendFormula(row, formulas, sheetNames, layout.FinalRaw, prepared.SourceRowNumber, prepared.FinalRaw, writtenFormulaCells);
        AppendFormula(row, formulas, sheetNames, layout.FinalScore, prepared.SourceRowNumber, prepared.FinalScore, writtenFormulaCells);
        return row;
    }

    private void AppendFormula(
        Row row,
        IReadOnlyDictionary<FormulaCellAddress, FormulaCellDefinition> formulas,
        AppOwnedSheetNames sheetNames,
        ColumnLayout column,
        int sourceRow,
        decimal? cachedValue,
        ImmutableArray<WrittenFormulaCell>.Builder writtenFormulaCells)
    {
        FormulaCellAddress target = Address(sheetNames.ResultsSheetName, column, sourceRow);
        FormulaCellDefinition definition = formulas[target];
        formulaCellWriter.Write(row, definition, cachedValue);
        writtenFormulaCells.Add(new WrittenFormulaCell(definition, cachedValue));
    }

    private DataValidations CreateDataValidations(
        ResultsLayout layout,
        ImmutableArray<PreparedRow> rows,
        AppOwnedSheetNames sheetNames,
        ConfigCellAddressMap configCells)
    {
        DataValidations validations = new();
        uint count = 0;
        foreach (PreparedRow row in rows)
        {
            foreach (PreparedCriterion criterion in row.Questions.SelectMany(question => question.Evaluators).SelectMany(evaluator => evaluator.Criteria))
            {
                FormulaCellAddress overrideAddress = Address(
                    sheetNames.ResultsSheetName,
                    criterion.Layout.Override,
                    row.SourceRowNumber);
                DataValidation validation = new()
                {
                    AllowBlank = true,
                    ShowErrorMessage = true,
                    ErrorStyle = DataValidationErrorStyleValues.Stop,
                    ErrorTitle = "Invalid override",
                    Error = criterion.Scorable
                        ? "Enter a number within the configured range or leave the cell blank."
                        : "This override must remain blank because the source answer is empty.",
                    SequenceOfReferences = new ListValue<StringValue>
                    {
                        InnerText = CellReference(criterion.Layout.Override, row.SourceRowNumber),
                    },
                };
                if (criterion.Scorable)
                {
                    validation.Type = DataValidationValues.Decimal;
                    validation.Operator = DataValidationOperatorValues.Between;
                    validation.Append(
                        new Formula1(SerializeReference(configCells.CriterionMinimumCells[criterion.Layout.Definition.Id])),
                        new Formula2(SerializeReference(configCells.CriterionMaximumCells[criterion.Layout.Definition.Id])));
                }
                else
                {
                    validation.Type = DataValidationValues.Custom;
                    FormulaExpression mustRemainBlank = FormulaExpressions.Binary(
                        new FormulaCell(Ref(overrideAddress)),
                        FormulaBinaryOperator.Equal,
                        FormulaBlank.Value);
                    validation.Append(new Formula1(SerializeWithoutLeadingEquals(mustRemainBlank)));
                }

                validations.Append(validation);
                count++;
            }
        }

        validations.Count = count;
        return validations;
    }

    private static Cell CreateOptionalNumberCell(ColumnLayout column, int row, decimal? value) =>
        value is decimal number
            ? SpreadsheetLiteral.CreateNumberCell(CellReference(column, row), number)
            : new Cell { CellReference = CellReference(column, row) };

    private static string EvidenceSourceName(EvidenceSourceKind source) => source switch
    {
        EvidenceSourceKind.PrimaryAnswer => "PRIMARY_ANSWER",
        EvidenceSourceKind.SupportingColumn => "SUPPORTING_COLUMN",
        EvidenceSourceKind.None => "NONE",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unsupported evidence source."),
    };

    private static ScoreRange EffectiveRange(CriterionDefinition criterion, EvaluatorDefinition evaluator) =>
        criterion.Range ?? evaluator.Range;

    private static ColumnLayout Column(int number, string header) =>
        new(number, ColumnName(number), header);

    private static string ColumnName(int number)
    {
        if (number is < 1 or > FormulaPreflightValidator.MaximumExcelColumn)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        Span<char> buffer = stackalloc char[3];
        int position = buffer.Length;
        int remaining = number;
        while (remaining > 0)
        {
            remaining--;
            buffer[--position] = (char)('A' + (remaining % 26));
            remaining /= 26;
        }

        return new string(buffer[position..]);
    }

    private static FormulaCellAddress Address(string sheetName, ColumnLayout column, int row) =>
        new(sheetName, column.Name, row);

    private static FormulaCellReference Ref(FormulaCellAddress address, bool absolute = false) =>
        new(address, absolute, absolute);

    private static string CellReference(ColumnLayout column, int row) =>
        column.Name + row.ToString(CultureInfo.InvariantCulture);

    private string SerializeReference(FormulaCellAddress address) =>
        SerializeWithoutLeadingEquals(new FormulaCell(Ref(address, absolute: true)));

    private string SerializeWithoutLeadingEquals(FormulaExpression expression)
    {
        string value = formulaSerializer.Serialize(expression);
        return value[1..];
    }

    private static int? TryFindFormulaRow(
        ImmutableArray<FormulaCellDefinition> formulas,
        FormulaPreflightError error) =>
        formulas.FirstOrDefault(formula => formula.Identity == error.Identity)?.Target.RowNumber;

    private static void ThrowIfErrors(List<ResultsSheetValidationError> errors)
    {
        if (errors.Count > 0)
        {
            throw new ResultsSheetValidationException(errors);
        }
    }

    private static void AddError(
        List<ResultsSheetValidationError>? errors,
        string code,
        int? sourceRow,
        string nodeKind,
        string nodeId,
        string displayName,
        string field,
        string safeOffendingValue,
        string? limit = null)
    {
        errors?.Add(new ResultsSheetValidationError(
            code,
            sourceRow,
            nodeKind,
            nodeId,
            displayName,
            field,
                safeOffendingValue,
                limit));
    }

    private sealed record ColumnLayout(int Number, string Name, string Header);

    private sealed record CriterionLayout(
        CriterionDefinition Definition,
        EvaluatorDefinition ParentEvaluator,
        ColumnLayout Scorable,
        ColumnLayout AiRaw,
        ColumnLayout Override,
        ColumnLayout EffectiveRaw,
        ColumnLayout Normalized,
        ColumnLayout Reason,
        ColumnLayout Evidence,
        ColumnLayout EvidenceSource,
        ColumnLayout EvidenceSourceColumn,
        ColumnLayout Status)
    {
        internal IEnumerable<ColumnLayout> AllColumns =>
        [Scorable, AiRaw, Override, EffectiveRaw, Normalized, Reason, Evidence, EvidenceSource, EvidenceSourceColumn, Status];
    }

    private sealed record EvaluatorLayout(
        EvaluatorDefinition Definition,
        ImmutableArray<CriterionLayout> Criteria,
        ColumnLayout Score)
    {
        internal IEnumerable<ColumnLayout> AllColumns =>
            Criteria.SelectMany(criterion => criterion.AllColumns).Append(Score);
    }

    private sealed record SpecialLayout(
        SpecialEvaluationDefinition Definition,
        ColumnLayout AiRaw,
        ColumnLayout Reason,
        ColumnLayout Evidence,
        ColumnLayout EvidenceSource,
        ColumnLayout EvidenceSourceColumn,
        ColumnLayout Status)
    {
        internal IEnumerable<ColumnLayout> AllColumns =>
        [AiRaw, Reason, Evidence, EvidenceSource, EvidenceSourceColumn, Status];
    }

    private sealed record SimilarityLayout(
        ColumnLayout AiRaw,
        ColumnLayout Reason,
        ColumnLayout Status,
        ColumnLayout Penalty,
        ColumnLayout PeerMax,
        ColumnLayout PeerRow)
    {
        internal IEnumerable<ColumnLayout> AllColumns => [AiRaw, Reason, Status, Penalty, PeerMax, PeerRow];
    }

    private sealed record QuestionLayout(
        QuestionDefinition Definition,
        ImmutableArray<EvaluatorLayout> Evaluators,
        ColumnLayout AnswerPresent,
        ColumnLayout Normalized,
        ColumnLayout Rate,
        ColumnLayout Earned,
        ImmutableArray<SpecialLayout> Specials,
        ColumnLayout? SpecialQuestionRate,
        SimilarityLayout Similarity)
    {
        internal IEnumerable<ColumnLayout> AllColumns
        {
            get
            {
                IEnumerable<ColumnLayout> columns = Evaluators
                    .SelectMany(evaluator => evaluator.AllColumns)
                    .Concat([AnswerPresent, Normalized, Rate, Earned])
                    .Concat(Specials.SelectMany(special => special.AllColumns));
                if (SpecialQuestionRate is not null)
                {
                    columns = columns.Append(SpecialQuestionRate);
                }

                return columns.Concat(Similarity.AllColumns);
            }
        }
    }

    private sealed record ResultsLayout(
        ColumnLayout SourceRow,
        ImmutableArray<QuestionLayout> Questions,
        ColumnLayout BasePoints,
        ColumnLayout SpecialEarned,
        ColumnLayout FinalRaw,
        ColumnLayout FinalScore,
        int ColumnCount)
    {
        internal IEnumerable<ColumnLayout> AllColumns =>
            new[] { SourceRow }
                .Concat(Questions.SelectMany(question => question.AllColumns))
                .Concat([BasePoints, SpecialEarned, FinalRaw, FinalScore]);
    }

    private sealed record PreparedCriterion(
        CriterionLayout Layout,
        bool Scorable,
        decimal? AiRaw,
        decimal? Override,
        decimal? EffectiveRaw,
        decimal? Normalized,
        string Reason,
        string Evidence,
        string EvidenceSource,
        string EvidenceSourceColumn,
        string Status);

    private sealed record PreparedEvaluator(
        EvaluatorLayout Layout,
        ImmutableArray<PreparedCriterion> Criteria,
        decimal? Score);

    private sealed record PreparedSpecial(
        SpecialLayout Layout,
        decimal? AiRaw,
        string Reason,
        string Evidence,
        string EvidenceSource,
        string EvidenceSourceColumn,
        string Status);

    private sealed record PreparedSimilarity(
        SimilarityLayout Layout,
        decimal? AiRaw,
        string Reason,
        string Status,
        decimal? Penalty,
        decimal? PeerMax,
        int? PeerRow);

    private sealed record PreparedQuestion(
        QuestionLayout Layout,
        ImmutableArray<PreparedEvaluator> Evaluators,
        bool AnswerPresent,
        decimal? Normalized,
        decimal? Rate,
        decimal? Earned,
        ImmutableArray<PreparedSpecial> Specials,
        decimal? SpecialQuestionRate,
        PreparedSimilarity Similarity);

    private sealed record PreparedRow(
        int SourceRowNumber,
        ImmutableArray<PreparedQuestion> Questions,
        decimal BasePoints,
        decimal? SpecialEarned,
        decimal? FinalRaw,
        decimal? FinalScore);

    private sealed record PreparedSheet(
        Worksheet Worksheet,
        ResultsLayout Layout,
        ImmutableArray<PreparedRow> Rows,
        ImmutableArray<WrittenFormulaCell> FormulaCells);
}
