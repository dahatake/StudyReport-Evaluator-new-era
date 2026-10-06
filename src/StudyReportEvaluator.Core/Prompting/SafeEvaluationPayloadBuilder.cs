using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.Core.Prompting;

public sealed class SafeEvaluationPayloadBuilder
{
    private readonly PromptTemplateRenderer _renderer = new();

    public SafeEvaluationPayload Build(
        QuantificationSnapshot snapshot,
        string questionId,
        string evaluatorId,
        IReadOnlyDictionary<string, string?> sameRowCells)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(questionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(evaluatorId);
        ArgumentNullException.ThrowIfNull(sameRowCells);

        QuestionDefinition question = snapshot.Definition.Questions.SingleOrDefault(
            item => string.Equals(item.Id, questionId, StringComparison.Ordinal))
            ?? throw new PromptConfigurationException("QUESTION_NOT_FOUND", "The selected question does not exist in the run snapshot.");
        if (!question.Enabled)
        {
            throw new PromptConfigurationException("QUESTION_DISABLED", "A disabled question cannot be dispatched.");
        }

        EvaluatorDefinition evaluator = question.Evaluators.SingleOrDefault(
            item => string.Equals(item.Id, evaluatorId, StringComparison.Ordinal))
            ?? throw new PromptConfigurationException("EVALUATOR_NOT_FOUND", "The selected evaluator does not exist under the selected question.");
        if (!evaluator.Enabled)
        {
            throw new PromptConfigurationException("EVALUATOR_DISABLED", "A disabled evaluator cannot be dispatched.");
        }

        string primaryValue = ReadSelectedCell(sameRowCells, question.PrimarySourceColumn);
        if (string.IsNullOrWhiteSpace(primaryValue))
        {
            throw new PromptConfigurationException("EMPTY_PRIMARY", "An empty primary answer must not be dispatched.");
        }

        EvaluationSourceCell primary = new(
            EvaluationSourceKind.PrimaryAnswer,
            question.PrimarySourceColumn,
            primaryValue);
        ImmutableArray<EvaluationSourceCell> supporting = question.SupportingSourceColumns
            .Select(column => new EvaluationSourceCell(
                EvaluationSourceKind.SupportingColumn,
                column,
                ReadSelectedCell(sameRowCells, column)))
            .ToImmutableArray();
        ImmutableArray<ExpectedCriterion> criteria = evaluator.Criteria
            .Where(criterion => criterion.Enabled)
            .Select(criterion => new ExpectedCriterion(
                criterion.Id,
                criterion.DisplayName,
                criterion.Range ?? evaluator.Range))
            .ToImmutableArray();

        PromptRenderContext context = new(
            question.QuestionText,
            primary.Value,
            FormatSupportingInformation(supporting),
            FormatCriteria(evaluator, criteria),
            Invariant(evaluator.Range.Minimum),
            Invariant(evaluator.Range.Maximum));
        string template = evaluator.Type switch
        {
            EvaluatorType.KnowledgeCoverage => BuiltInPromptTemplates.GetKnowledgeTemplate(evaluator.BuiltInTemplateVersion),
            EvaluatorType.CustomPrompt => evaluator.CustomPromptTemplate
                ?? throw new PromptConfigurationException("TEMPLATE_REQUIRED", "A Custom Prompt template must not be blank."),
            _ => throw new PromptConfigurationException("INVALID_EVALUATOR_TYPE", "The evaluator type is not supported."),
        };
        string rendered = _renderer.Render(template, context);
        string completePrompt = AppendAppOwnedContract(rendered, evaluator.Id, primary, supporting, criteria);

        return new SafeEvaluationPayload(
            question.Id,
            evaluator.Id,
            completePrompt,
            primary,
            supporting,
            criteria);
    }

    public SafeReferenceAnswerPayload BuildReferenceAnswer(
        QuantificationSnapshot snapshot,
        string questionId)
    {
        QuestionDefinition question = FindEnabledQuestion(snapshot, questionId);
        string prompt = BuiltInPromptTemplates.ReferenceAnswerTemplate.Replace(
            "{設問}",
            question.QuestionText,
            StringComparison.Ordinal);
        return new SafeReferenceAnswerPayload(question.Id, prompt);
    }

    public SafeSpecialEvaluationPayload BuildSpecialEvaluation(
        QuantificationSnapshot snapshot,
        string questionId,
        string specialEvaluationId,
        IReadOnlyDictionary<string, string?> sameRowCells)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(specialEvaluationId);
        ArgumentNullException.ThrowIfNull(sameRowCells);
        QuestionDefinition question = FindEnabledQuestion(snapshot, questionId);
        SpecialEvaluationDefinition special = question.SpecialEvaluations.SingleOrDefault(
            item => string.Equals(item.Id, specialEvaluationId, StringComparison.Ordinal))
            ?? throw new PromptConfigurationException(
                "SPECIAL_EVALUATION_NOT_FOUND",
                "The selected special evaluation does not exist under the selected question.");
        if (!special.Enabled)
        {
            throw new PromptConfigurationException(
                "SPECIAL_EVALUATION_DISABLED",
                "A disabled special evaluation cannot be dispatched.");
        }

        string primaryValue = ReadSelectedCell(sameRowCells, special.PrimarySourceColumn);
        if (string.IsNullOrWhiteSpace(primaryValue))
        {
            throw new PromptConfigurationException(
                "EMPTY_SPECIAL_PRIMARY",
                "An empty special-evaluation primary value must not be dispatched.");
        }

        EvaluationSourceCell primary = new(
            EvaluationSourceKind.PrimaryAnswer,
            special.PrimarySourceColumn,
            primaryValue);
        ImmutableArray<EvaluationSourceCell> supporting = special.SupportingSourceColumns
            .Select(column => new EvaluationSourceCell(
                EvaluationSourceKind.SupportingColumn,
                column,
                ReadSelectedCell(sameRowCells, column)))
            .ToImmutableArray();
        PromptRenderContext context = new(
            question.QuestionText,
            primary.Value,
            FormatSupportingInformation(supporting),
            string.Empty,
            "0",
            "1");
        string rendered = _renderer.RenderSpecial(special.PromptTemplate, context);
        StringBuilder prompt = new(rendered.Length + 768);
        prompt.AppendLine(rendered.TrimEnd());
        prompt.AppendLine();
        prompt.AppendLine(BuiltInPromptTemplates.SpecialOutputInstruction.Trim());
        prompt.Append("ExpectedSpecialEvaluationId: ").AppendLine(special.Id);
        prompt.Append("PrimarySourceColumnId: ").AppendLine(primary.SourceColumnId);
        prompt.Append("AllowedSupportingSourceColumnIds: ")
            .AppendLine(supporting.IsEmpty ? "(none)" : string.Join(",", supporting.Select(item => item.SourceColumnId)));
        return new SafeSpecialEvaluationPayload(
            question.Id,
            special.Id,
            prompt.ToString().TrimEnd(),
            primary,
            supporting);
    }

    private static QuestionDefinition FindEnabledQuestion(
        QuantificationSnapshot snapshot,
        string questionId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(questionId);
        QuestionDefinition question = snapshot.Definition.Questions.SingleOrDefault(
            item => string.Equals(item.Id, questionId, StringComparison.Ordinal))
            ?? throw new PromptConfigurationException(
                "QUESTION_NOT_FOUND",
                "The selected question does not exist in the run snapshot.");
        if (!question.Enabled)
        {
            throw new PromptConfigurationException(
                "QUESTION_DISABLED",
                "A disabled question cannot be dispatched.");
        }

        return question;
    }

    private static string ReadSelectedCell(IReadOnlyDictionary<string, string?> sameRowCells, string selectedColumn)
    {
        if (sameRowCells.TryGetValue(selectedColumn, out string? exactValue))
        {
            return exactValue ?? string.Empty;
        }

        foreach ((string column, string? value) in sameRowCells)
        {
            if (string.Equals(column, selectedColumn, StringComparison.OrdinalIgnoreCase))
            {
                return value ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string FormatSupportingInformation(ImmutableArray<EvaluationSourceCell> supporting)
    {
        if (supporting.IsEmpty)
        {
            return "(none)";
        }

        StringBuilder builder = new();
        foreach (EvaluationSourceCell source in supporting)
        {
            builder.Append("SourceColumnId: ").AppendLine(source.SourceColumnId);
            builder.AppendLine("Value:");
            builder.AppendLine(source.Value);
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatCriteria(
        EvaluatorDefinition evaluator,
        ImmutableArray<ExpectedCriterion> criteria)
    {
        StringBuilder builder = new();
        foreach (ExpectedCriterion expected in criteria)
        {
            CriterionDefinition criterion = evaluator.Criteria.Single(item => item.Id == expected.CriterionId);
            builder.Append("CriterionId: ").AppendLine(expected.CriterionId);
            builder.Append("DisplayName: ").AppendLine(expected.DisplayName);
            builder.Append("Description: ").AppendLine(criterion.Description);
            builder.Append("EffectiveRange: ")
                .Append(Invariant(expected.Range.Minimum))
                .Append("..")
                .AppendLine(Invariant(expected.Range.Maximum));
        }

        return builder.ToString().TrimEnd();
    }

    private static string AppendAppOwnedContract(
        string rendered,
        string evaluatorId,
        EvaluationSourceCell primary,
        ImmutableArray<EvaluationSourceCell> supporting,
        ImmutableArray<ExpectedCriterion> criteria)
    {
        StringBuilder builder = new(rendered.Length + 1024);
        builder.AppendLine(rendered.TrimEnd());
        builder.AppendLine();
        builder.AppendLine(BuiltInPromptTemplates.StructuredOutputInstruction.Trim());
        builder.Append("ExpectedEvaluatorId: ").AppendLine(evaluatorId);
        builder.Append("PrimarySourceColumnId: ").AppendLine(primary.SourceColumnId);
        builder.Append("AllowedSupportingSourceColumnIds: ")
            .AppendLine(supporting.IsEmpty ? "(none)" : string.Join(",", supporting.Select(item => item.SourceColumnId)));
        builder.Append("ExpectedCriterionIds: ")
            .AppendLine(string.Join(",", criteria.Select(item => item.CriterionId)));
        return builder.ToString().TrimEnd();
    }

    private static string Invariant(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
