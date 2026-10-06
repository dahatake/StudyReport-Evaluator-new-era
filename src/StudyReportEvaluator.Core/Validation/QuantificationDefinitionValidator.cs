using System.Collections.Immutable;
using System.Globalization;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Scoring;

namespace StudyReportEvaluator.Core.Validation;

public sealed record DefinitionValidationError(
    string Code,
    string Path,
    string NodeKind,
    string NodeId,
    string DisplayName,
    string Field,
    string SafeOffendingValue);

public sealed class DefinitionValidationResult
{
    internal DefinitionValidationResult(ImmutableArray<DefinitionValidationError> errors)
    {
        Errors = errors;
    }

    public ImmutableArray<DefinitionValidationError> Errors { get; }

    public bool IsValid => Errors.IsEmpty;
}

public sealed class QuantificationDefinitionValidationException : Exception
{
    public QuantificationDefinitionValidationException(ImmutableArray<DefinitionValidationError> errors)
        : base($"The quantification definition has {errors.Length.ToString(CultureInfo.InvariantCulture)} validation error(s).")
    {
        Errors = errors;
    }

    public ImmutableArray<DefinitionValidationError> Errors { get; }
}

public sealed class QuantificationDefinitionValidator
{
    public const int MaximumSelectedRows = 20_000;
    public const int MaximumExcelRow = 1_048_576;
    public const int MaximumExcelColumn = 16_384;
    public const int MaximumCellCharacters = 32_767;

    private static readonly PromptRenderContext PromptValidationContext = new(
        "question",
        "answer",
        "support",
        "criteria",
        "0",
        "1");

    private readonly PromptTemplateRenderer promptRenderer = new();
    private readonly ScoringAllocationCalculator allocationCalculator = new();

    public DefinitionValidationResult Validate(QuantificationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        ImmutableArray<DefinitionValidationError>.Builder errors = ImmutableArray.CreateBuilder<DefinitionValidationError>();
        Dictionary<string, string> knownIds = new(StringComparer.Ordinal);

        ValidateRequiredText(errors, "$", "Definition", definition.Id, definition.Name, "Id", definition.Id, body: false);
        ValidateRequiredText(errors, "$", "Definition", definition.Id, definition.Name, "Name", definition.Name, body: false);
        ValidateRequiredText(errors, "$", "Definition", definition.Id, definition.Name, "Revision", definition.Revision, body: false);
        ValidateRequiredText(errors, "$", "Definition", definition.Id, definition.Name, "SourceSheet", definition.SourceSheet, body: false);
        RegisterId(errors, knownIds, "$", "Definition", definition.Id, definition.Name);
        ValidateRows(definition, errors);
        ValidateRootAllocationValues(definition, errors);

        if (definition.RoundingDigits is < 0 or > 6)
        {
            Add(errors, "ROUNDING_OUT_OF_RANGE", "$", "Definition", definition.Id, definition.Name, "RoundingDigits", Invariant(definition.RoundingDigits));
        }

        ImmutableArray<QuestionDefinition> questions = DefinitionCollectionOperations.Normalize(definition.Questions);
        if (questions.IsEmpty)
        {
            Add(errors, "QUESTION_REQUIRED", "$.questions", "Definition", definition.Id, definition.Name, "Questions", "0");
        }
        else if (!questions.Any(question => question is not null && question.Enabled))
        {
            Add(errors, "ENABLED_QUESTION_REQUIRED", "$.questions", "Definition", definition.Id, definition.Name, "Questions.Enabled", "0");
        }

        for (int questionIndex = 0; questionIndex < questions.Length; questionIndex++)
        {
            string questionPath = $"$.questions[{questionIndex.ToString(CultureInfo.InvariantCulture)}]";
            QuestionDefinition? question = questions[questionIndex];
            if (question is null)
            {
                Add(errors, "NULL_NODE", questionPath, "Question", "<null>", "<null>", "Question", "<null>");
                continue;
            }

            ValidateQuestion(question, questionPath, errors, knownIds);
        }

        ValidateAllocation(definition, questions, errors);

        return new DefinitionValidationResult(errors.ToImmutable());
    }

    private void ValidateQuestion(
        QuestionDefinition question,
        string path,
        ImmutableArray<DefinitionValidationError>.Builder errors,
        Dictionary<string, string> knownIds)
    {
        ValidateRequiredText(errors, path, "Question", question.Id, question.DisplayName, "Id", question.Id, body: false);
        ValidateRequiredText(errors, path, "Question", question.Id, question.DisplayName, "DisplayName", question.DisplayName, body: false);
        ValidateRequiredText(errors, path, "Question", question.Id, question.DisplayName, "QuestionText", question.QuestionText, body: true);
        ValidateRequiredText(errors, path, "Question", question.Id, question.DisplayName, "PrimarySourceColumn", question.PrimarySourceColumn, body: false);
        RegisterId(errors, knownIds, path, "Question", question.Id, question.DisplayName);
        if (question.Points < 0m)
        {
            Add(errors, "QUESTION_POINTS_OUT_OF_RANGE", path, "Question", question.Id, question.DisplayName, "Points", Invariant(question.Points));
        }

        ValidateColumns(question, path, errors);

        ImmutableArray<EvaluatorDefinition> evaluators = DefinitionCollectionOperations.Normalize(question.Evaluators);
        if (question.Enabled && !evaluators.Any(evaluator => evaluator is not null && evaluator.Enabled))
        {
            Add(errors, "ENABLED_EVALUATOR_REQUIRED", $"{path}.evaluators", "Question", question.Id, question.DisplayName, "Evaluators.Enabled", "0");
        }

        for (int evaluatorIndex = 0; evaluatorIndex < evaluators.Length; evaluatorIndex++)
        {
            string evaluatorPath = $"{path}.evaluators[{evaluatorIndex.ToString(CultureInfo.InvariantCulture)}]";
            EvaluatorDefinition? evaluator = evaluators[evaluatorIndex];
            if (evaluator is null)
            {
                Add(errors, "NULL_NODE", evaluatorPath, "Evaluator", "<null>", "<null>", "Evaluator", "<null>");
                continue;
            }

            ValidateEvaluator(evaluator, evaluatorPath, errors, knownIds);
        }

        ImmutableArray<SpecialEvaluationDefinition> specialEvaluations =
            DefinitionCollectionOperations.Normalize(question.SpecialEvaluations);
        for (int specialIndex = 0; specialIndex < specialEvaluations.Length; specialIndex++)
        {
            string specialPath = $"{path}.specialEvaluations[{specialIndex.ToString(CultureInfo.InvariantCulture)}]";
            SpecialEvaluationDefinition? specialEvaluation = specialEvaluations[specialIndex];
            if (specialEvaluation is null)
            {
                Add(errors, "NULL_NODE", specialPath, "SpecialEvaluation", "<null>", "<null>", "SpecialEvaluation", "<null>");
                continue;
            }

            ValidateSpecialEvaluation(specialEvaluation, specialPath, errors, knownIds);
        }
    }

    private void ValidateSpecialEvaluation(
        SpecialEvaluationDefinition specialEvaluation,
        string path,
        ImmutableArray<DefinitionValidationError>.Builder errors,
        Dictionary<string, string> knownIds)
    {
        ValidateRequiredText(errors, path, "SpecialEvaluation", specialEvaluation.Id, specialEvaluation.DisplayName, "Id", specialEvaluation.Id, body: false);
        ValidateRequiredText(errors, path, "SpecialEvaluation", specialEvaluation.Id, specialEvaluation.DisplayName, "DisplayName", specialEvaluation.DisplayName, body: false);
        ValidateRequiredText(errors, path, "SpecialEvaluation", specialEvaluation.Id, specialEvaluation.DisplayName, "PrimarySourceColumn", specialEvaluation.PrimarySourceColumn, body: false);
        ValidateRequiredText(errors, path, "SpecialEvaluation", specialEvaluation.Id, specialEvaluation.DisplayName, "PromptTemplate", specialEvaluation.PromptTemplate, body: true);
        RegisterId(errors, knownIds, path, "SpecialEvaluation", specialEvaluation.Id, specialEvaluation.DisplayName);
        ValidateSourceColumns(
            path,
            "SpecialEvaluation",
            specialEvaluation.Id,
            specialEvaluation.DisplayName,
            specialEvaluation.PrimarySourceColumn,
            specialEvaluation.SupportingSourceColumns,
            errors);

        if (string.IsNullOrWhiteSpace(specialEvaluation.PromptTemplate)
            || specialEvaluation.PromptTemplate.Length > MaximumCellCharacters)
        {
            return;
        }

        try
        {
            _ = promptRenderer.RenderSpecial(
                specialEvaluation.PromptTemplate,
                PromptValidationContext);
        }
        catch (PromptConfigurationException exception)
        {
            Add(
                errors,
                exception.Code,
                path,
                "SpecialEvaluation",
                specialEvaluation.Id,
                specialEvaluation.DisplayName,
                "PromptTemplate",
                exception.Position is int position
                    ? $"position={position.ToString(CultureInfo.InvariantCulture)}"
                    : SafeLength(specialEvaluation.PromptTemplate));
        }
    }

    private void ValidateEvaluator(
        EvaluatorDefinition evaluator,
        string path,
        ImmutableArray<DefinitionValidationError>.Builder errors,
        Dictionary<string, string> knownIds)
    {
        ValidateRequiredText(errors, path, "Evaluator", evaluator.Id, evaluator.DisplayName, "Id", evaluator.Id, body: false);
        ValidateRequiredText(errors, path, "Evaluator", evaluator.Id, evaluator.DisplayName, "DisplayName", evaluator.DisplayName, body: false);
        RegisterId(errors, knownIds, path, "Evaluator", evaluator.Id, evaluator.DisplayName);
        ValidateWeight(errors, path, "Evaluator", evaluator.Id, evaluator.DisplayName, evaluator.Weight);
        ValidateRange(errors, path, "Evaluator", evaluator.Id, evaluator.DisplayName, evaluator.Range);

        if (!Enum.IsDefined(typeof(EvaluatorType), evaluator.Type))
        {
            Add(errors, "INVALID_EVALUATOR_TYPE", path, "Evaluator", evaluator.Id, evaluator.DisplayName, "Type", Invariant((int)evaluator.Type));
        }
        else
        {
            ValidatePromptConfiguration(evaluator, path, errors);
        }

        ImmutableArray<CriterionDefinition> criteria = DefinitionCollectionOperations.Normalize(evaluator.Criteria);
        if (evaluator.Enabled && !criteria.Any(criterion => criterion is not null && criterion.Enabled))
        {
            Add(errors, "ENABLED_CRITERION_REQUIRED", $"{path}.criteria", "Evaluator", evaluator.Id, evaluator.DisplayName, "Criteria.Enabled", "0");
        }

        for (int criterionIndex = 0; criterionIndex < criteria.Length; criterionIndex++)
        {
            string criterionPath = $"{path}.criteria[{criterionIndex.ToString(CultureInfo.InvariantCulture)}]";
            CriterionDefinition? criterion = criteria[criterionIndex];
            if (criterion is null)
            {
                Add(errors, "NULL_NODE", criterionPath, "Criterion", "<null>", "<null>", "Criterion", "<null>");
                continue;
            }

            ValidateCriterion(criterion, criterionPath, errors, knownIds);
        }
    }

    private void ValidatePromptConfiguration(
        EvaluatorDefinition evaluator,
        string path,
        ImmutableArray<DefinitionValidationError>.Builder errors)
    {
        bool customPromptWithinCellLimit = ValidateOptionalTextCapacity(
            errors,
            path,
            "Evaluator",
            evaluator.Id,
            evaluator.DisplayName,
            "CustomPromptTemplate",
            evaluator.CustomPromptTemplate);
        _ = ValidateOptionalTextCapacity(
            errors,
            path,
            "Evaluator",
            evaluator.Id,
            evaluator.DisplayName,
            "BuiltInTemplateVersion",
            evaluator.BuiltInTemplateVersion);

        if (evaluator.Type == EvaluatorType.KnowledgeCoverage)
        {
            if (!string.Equals(
                evaluator.BuiltInTemplateVersion,
                BuiltInPromptTemplates.KnowledgeTemplateVersion,
                StringComparison.Ordinal))
            {
                Add(
                    errors,
                    "KNOWLEDGE_TEMPLATE_VERSION_INVALID",
                    path,
                    "Evaluator",
                    evaluator.Id,
                    evaluator.DisplayName,
                    "BuiltInTemplateVersion",
                    SafeScalar(evaluator.BuiltInTemplateVersion ?? string.Empty));
            }

            if (!string.IsNullOrEmpty(evaluator.CustomPromptTemplate))
            {
                Add(
                    errors,
                    "KNOWLEDGE_CUSTOM_TEMPLATE_FORBIDDEN",
                    path,
                    "Evaluator",
                    evaluator.Id,
                    evaluator.DisplayName,
                    "CustomPromptTemplate",
                    SafeLength(evaluator.CustomPromptTemplate));
            }

            return;
        }

        if (!string.IsNullOrEmpty(evaluator.BuiltInTemplateVersion))
        {
            Add(
                errors,
                "CUSTOM_BUILT_IN_TEMPLATE_FORBIDDEN",
                path,
                "Evaluator",
                evaluator.Id,
                evaluator.DisplayName,
                "BuiltInTemplateVersion",
                SafeScalar(evaluator.BuiltInTemplateVersion));
        }

        if (!customPromptWithinCellLimit)
        {
            return;
        }

        try
        {
            _ = promptRenderer.Render(
                evaluator.CustomPromptTemplate ?? string.Empty,
                PromptValidationContext);
        }
        catch (PromptConfigurationException exception)
        {
            Add(
                errors,
                exception.Code,
                path,
                "Evaluator",
                evaluator.Id,
                evaluator.DisplayName,
                "CustomPromptTemplate",
                exception.Position is int position
                    ? $"position={position.ToString(CultureInfo.InvariantCulture)}"
                    : SafeLength(evaluator.CustomPromptTemplate));
        }
    }

    private static void ValidateCriterion(
        CriterionDefinition criterion,
        string path,
        ImmutableArray<DefinitionValidationError>.Builder errors,
        Dictionary<string, string> knownIds)
    {
        ValidateRequiredText(errors, path, "Criterion", criterion.Id, criterion.DisplayName, "Id", criterion.Id, body: false);
        ValidateRequiredText(errors, path, "Criterion", criterion.Id, criterion.DisplayName, "DisplayName", criterion.DisplayName, body: false);
        ValidateRequiredText(errors, path, "Criterion", criterion.Id, criterion.DisplayName, "Description", criterion.Description, body: true);
        RegisterId(errors, knownIds, path, "Criterion", criterion.Id, criterion.DisplayName);
        ValidateWeight(errors, path, "Criterion", criterion.Id, criterion.DisplayName, criterion.Weight);
        if (criterion.Range is ScoreRange range)
        {
            ValidateRange(errors, path, "Criterion", criterion.Id, criterion.DisplayName, range);
        }
    }

    private static void ValidateRows(
        QuantificationDefinition definition,
        ImmutableArray<DefinitionValidationError>.Builder errors)
    {
        ValidateExcelRow(errors, definition, "HeaderRow", definition.HeaderRow);
        ValidateExcelRow(errors, definition, "FirstDataRow", definition.FirstDataRow);
        ValidateExcelRow(errors, definition, "LastDataRow", definition.LastDataRow);

        if (definition.HeaderRow is not (1 or 2))
        {
            Add(errors, "QUESTION_TEXT_ROW_INVALID", "$", "Definition", definition.Id, definition.Name, "HeaderRow", Invariant(definition.HeaderRow));
        }

        bool rowsInExcelRange = IsExcelRow(definition.HeaderRow)
            && IsExcelRow(definition.FirstDataRow)
            && IsExcelRow(definition.LastDataRow);
        if (!rowsInExcelRange)
        {
            return;
        }

        if (definition.FirstDataRow <= definition.HeaderRow)
        {
            Add(errors, "DATA_ROW_NOT_AFTER_HEADER", "$", "Definition", definition.Id, definition.Name, "FirstDataRow", Invariant(definition.FirstDataRow));
        }

        if (definition.LastDataRow < definition.FirstDataRow)
        {
            Add(errors, "DATA_ROW_RANGE_REVERSED", "$", "Definition", definition.Id, definition.Name, "LastDataRow", Invariant(definition.LastDataRow));
            return;
        }

        long selectedRows = (long)definition.LastDataRow - definition.FirstDataRow + 1;
        if (selectedRows > MaximumSelectedRows)
        {
            Add(errors, "SELECTED_ROW_LIMIT_EXCEEDED", "$", "Definition", definition.Id, definition.Name, "SelectedRowCount", Invariant(selectedRows));
        }
    }

    private static void ValidateExcelRow(
        ImmutableArray<DefinitionValidationError>.Builder errors,
        QuantificationDefinition definition,
        string field,
        int value)
    {
        if (!IsExcelRow(value))
        {
            Add(errors, "ROW_OUT_OF_RANGE", "$", "Definition", definition.Id, definition.Name, field, Invariant(value));
        }
    }

    private static bool IsExcelRow(int value) => value is >= 1 and <= MaximumExcelRow;

    private static void ValidateColumns(
        QuestionDefinition question,
        string path,
        ImmutableArray<DefinitionValidationError>.Builder errors) =>
        ValidateSourceColumns(
            path,
            "Question",
            question.Id,
            question.DisplayName,
            question.PrimarySourceColumn,
            question.SupportingSourceColumns,
            errors);

    private static void ValidateSourceColumns(
        string path,
        string nodeKind,
        string nodeId,
        string displayName,
        string primarySourceColumn,
        ImmutableArray<string> sourceSupportingColumns,
        ImmutableArray<DefinitionValidationError>.Builder errors)
    {
        if (!string.IsNullOrWhiteSpace(primarySourceColumn)
            && !TryGetExcelColumnNumber(primarySourceColumn, out _))
        {
            Add(errors, "INVALID_SOURCE_COLUMN", path, nodeKind, nodeId, displayName, "PrimarySourceColumn", SafeScalar(primarySourceColumn));
        }

        HashSet<string> supportingColumns = new(StringComparer.OrdinalIgnoreCase);
        ImmutableArray<string> columns = DefinitionCollectionOperations.Normalize(sourceSupportingColumns);
        for (int index = 0; index < columns.Length; index++)
        {
            string field = $"SupportingSourceColumns[{index.ToString(CultureInfo.InvariantCulture)}]";
            string? column = columns[index];
            if (string.IsNullOrWhiteSpace(column))
            {
                Add(errors, "SOURCE_COLUMN_REQUIRED", path, nodeKind, nodeId, displayName, field, "<blank>");
                continue;
            }

            if (!TryGetExcelColumnNumber(column, out _))
            {
                Add(errors, "INVALID_SOURCE_COLUMN", path, nodeKind, nodeId, displayName, field, SafeScalar(column));
            }

            if (!supportingColumns.Add(column))
            {
                Add(errors, "DUPLICATE_SUPPORTING_COLUMN", path, nodeKind, nodeId, displayName, field, SafeScalar(column));
            }

            if (string.Equals(primarySourceColumn, column, StringComparison.OrdinalIgnoreCase))
            {
                Add(errors, "PRIMARY_COLUMN_REUSED", path, nodeKind, nodeId, displayName, field, SafeScalar(column));
            }
        }
    }

    private void ValidateAllocation(
        QuantificationDefinition definition,
        ImmutableArray<QuestionDefinition> questions,
        ImmutableArray<DefinitionValidationError>.Builder errors)
    {
        decimal[] enabledPoints = questions
            .Where(question => question is not null && question.Enabled)
            .Select(question => question.Points)
            .ToArray();
        if (enabledPoints.Length == 0)
        {
            return;
        }

        ScoringAllocationValidationResult allocation = allocationCalculator.Validate(
            definition.BasePoints,
            definition.SpecialPoints,
            enabledPoints);
        if (!allocation.IsValid)
        {
            Add(
                errors,
                "ALLOCATION_TOTAL_INVALID",
                "$",
                "Definition",
                definition.Id,
                definition.Name,
                "AllocationTotal",
                allocation.Total is decimal total ? Invariant(total) : "invalid");
        }

        bool hasEnabledSpecial = questions
            .Where(question => question is not null && question.Enabled)
            .SelectMany(question => DefinitionCollectionOperations.Normalize(question.SpecialEvaluations))
            .Any(special => special is not null && special.Enabled);
        if (definition.SpecialPoints > 0m && !hasEnabledSpecial)
        {
            Add(errors, "SPECIAL_ITEMS_REQUIRED", "$", "Definition", definition.Id, definition.Name, "SpecialEvaluations.Enabled", "0");
        }
    }

    private static void ValidateRootAllocationValues(
        QuantificationDefinition definition,
        ImmutableArray<DefinitionValidationError>.Builder errors)
    {
        ValidateInclusiveRange(errors, definition, "BASE_POINTS_OUT_OF_RANGE", "BasePoints", definition.BasePoints, 0m, 100m);
        ValidateInclusiveRange(errors, definition, "SPECIAL_POINTS_OUT_OF_RANGE", "SpecialPoints", definition.SpecialPoints, 0m, 100m);
        ValidateInclusiveRange(errors, definition, "SIMILARITY_WEIGHT_OUT_OF_RANGE", "SimilarityPenaltyWeight", definition.SimilarityPenaltyWeight, 0m, 1m);
    }

    private static void ValidateInclusiveRange(
        ImmutableArray<DefinitionValidationError>.Builder errors,
        QuantificationDefinition definition,
        string code,
        string field,
        decimal value,
        decimal minimum,
        decimal maximum)
    {
        if (value < minimum || value > maximum)
        {
            Add(errors, code, "$", "Definition", definition.Id, definition.Name, field, Invariant(value));
        }
    }

    private static bool TryGetExcelColumnNumber(string column, out int number)
    {
        number = 0;
        if (column.Length is < 1 or > 3)
        {
            return false;
        }

        foreach (char value in column)
        {
            if (!char.IsAsciiLetter(value))
            {
                return false;
            }

            number = checked((number * 26) + (char.ToUpperInvariant(value) - 'A' + 1));
        }

        return number <= MaximumExcelColumn;
    }

    private static void ValidateWeight(
        ImmutableArray<DefinitionValidationError>.Builder errors,
        string path,
        string nodeKind,
        string? nodeId,
        string? displayName,
        decimal weight)
    {
        if (weight <= 0m)
        {
            Add(errors, "WEIGHT_MUST_BE_POSITIVE", path, nodeKind, nodeId, displayName, "Weight", Invariant(weight));
        }
    }

    private static void ValidateRange(
        ImmutableArray<DefinitionValidationError>.Builder errors,
        string path,
        string nodeKind,
        string? nodeId,
        string? displayName,
        ScoreRange range)
    {
        if (range.Minimum >= range.Maximum)
        {
            Add(
                errors,
                "SCORE_RANGE_INVALID",
                path,
                nodeKind,
                nodeId,
                displayName,
                "Range",
                $"{Invariant(range.Minimum)}..{Invariant(range.Maximum)}");
        }
    }

    private static void ValidateRequiredText(
        ImmutableArray<DefinitionValidationError>.Builder errors,
        string path,
        string nodeKind,
        string? nodeId,
        string? displayName,
        string field,
        string? value,
        bool body)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(errors, "REQUIRED", path, nodeKind, nodeId, displayName, field, body ? "<blank-body>" : "<blank>");
        }
        else if (value.Length > MaximumCellCharacters)
        {
            Add(
                errors,
                "CELL_TEXT_LIMIT_EXCEEDED",
                path,
                nodeKind,
                nodeId,
                displayName,
                field,
                value.Length.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool ValidateOptionalTextCapacity(
        ImmutableArray<DefinitionValidationError>.Builder errors,
        string path,
        string nodeKind,
        string? nodeId,
        string? displayName,
        string field,
        string? value)
    {
        if (value is null || value.Length <= MaximumCellCharacters)
        {
            return true;
        }

        Add(
            errors,
            "CELL_TEXT_LIMIT_EXCEEDED",
            path,
            nodeKind,
            nodeId,
            displayName,
            field,
            value.Length.ToString(CultureInfo.InvariantCulture));
        return false;
    }

    private static void RegisterId(
        ImmutableArray<DefinitionValidationError>.Builder errors,
        Dictionary<string, string> knownIds,
        string path,
        string nodeKind,
        string? id,
        string? displayName)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        if (!knownIds.TryAdd(id, path))
        {
            Add(errors, "DUPLICATE_ID", path, nodeKind, id, displayName, "Id", $"duplicate-of:{knownIds[id]}");
        }
    }

    private static void Add(
        ImmutableArray<DefinitionValidationError>.Builder errors,
        string code,
        string path,
        string nodeKind,
        string? nodeId,
        string? displayName,
        string field,
        string safeOffendingValue) =>
        errors.Add(new DefinitionValidationError(
            code,
            path,
            nodeKind,
            SafeIdentity(nodeId),
            SafeIdentity(displayName),
            field,
            safeOffendingValue));

    private static string SafeIdentity(string? value) => string.IsNullOrWhiteSpace(value) ? "<blank>" : value;

    private static string SafeScalar(string value) => value.Length <= 64
        ? value
        : $"{value[..64]}…(length={value.Length.ToString(CultureInfo.InvariantCulture)})";

    private static string SafeLength(string? value) =>
        $"length={(value?.Length ?? 0).ToString(CultureInfo.InvariantCulture)}";

    private static string Invariant(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);
}
