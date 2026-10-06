using System.Collections.Immutable;

namespace StudyReportEvaluator.Core.Domain;

public sealed record QuestionDefinition
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string QuestionText { get; init; }

    public required string PrimarySourceColumn { get; init; }

    public ImmutableArray<string> SupportingSourceColumns { get; init; } = [];

    public decimal Points { get; init; }

    public ImmutableArray<EvaluatorDefinition> Evaluators { get; init; } = [];

    public ImmutableArray<SpecialEvaluationDefinition> SpecialEvaluations { get; init; } = [];

    public bool Enabled { get; init; } = true;

    public QuestionDefinition AddEvaluator(EvaluatorDefinition evaluator) =>
        this with { Evaluators = DefinitionCollectionOperations.Normalize(Evaluators).Add(evaluator) };

    public QuestionDefinition DuplicateEvaluator(
        int sourceIndex,
        string newId,
        Func<CriterionDefinition, string> criterionIdFactory)
    {
        ImmutableArray<EvaluatorDefinition> items = DefinitionCollectionOperations.Normalize(Evaluators);
        EvaluatorDefinition duplicate = items[DefinitionCollectionOperations.RequireIndex(items, sourceIndex)]
            .Duplicate(newId, criterionIdFactory);
        return this with { Evaluators = items.Insert(sourceIndex + 1, duplicate) };
    }

    public QuestionDefinition MoveEvaluator(int sourceIndex, int destinationIndex) =>
        this with { Evaluators = DefinitionCollectionOperations.Move(Evaluators, sourceIndex, destinationIndex) };

    public QuestionDefinition SetEvaluatorEnabled(int index, bool enabled)
    {
        ImmutableArray<EvaluatorDefinition> items = DefinitionCollectionOperations.Normalize(Evaluators);
        DefinitionCollectionOperations.RequireIndex(items, index);
        return this with { Evaluators = DefinitionCollectionOperations.Replace(items, index, items[index] with { Enabled = enabled }) };
    }

    public QuestionDefinition RemoveEvaluator(int index) =>
        this with { Evaluators = DefinitionCollectionOperations.RemoveAt(Evaluators, index) };

    public QuestionDefinition AddSpecialEvaluation(SpecialEvaluationDefinition specialEvaluation) =>
        this with
        {
            SpecialEvaluations = DefinitionCollectionOperations.Normalize(SpecialEvaluations).Add(specialEvaluation),
        };

    public QuestionDefinition DuplicateSpecialEvaluation(int sourceIndex, string newId)
    {
        ImmutableArray<SpecialEvaluationDefinition> items =
            DefinitionCollectionOperations.Normalize(SpecialEvaluations);
        SpecialEvaluationDefinition duplicate =
            items[DefinitionCollectionOperations.RequireIndex(items, sourceIndex)].Duplicate(newId);
        return this with { SpecialEvaluations = items.Insert(sourceIndex + 1, duplicate) };
    }

    public QuestionDefinition MoveSpecialEvaluation(int sourceIndex, int destinationIndex) =>
        this with
        {
            SpecialEvaluations = DefinitionCollectionOperations.Move(
                SpecialEvaluations,
                sourceIndex,
                destinationIndex),
        };

    public QuestionDefinition SetSpecialEvaluationEnabled(int index, bool enabled)
    {
        ImmutableArray<SpecialEvaluationDefinition> items =
            DefinitionCollectionOperations.Normalize(SpecialEvaluations);
        DefinitionCollectionOperations.RequireIndex(items, index);
        return this with
        {
            SpecialEvaluations = DefinitionCollectionOperations.Replace(
                items,
                index,
                items[index] with { Enabled = enabled }),
        };
    }

    public QuestionDefinition RemoveSpecialEvaluation(int index) =>
        this with
        {
            SpecialEvaluations = DefinitionCollectionOperations.RemoveAt(SpecialEvaluations, index),
        };

    public QuestionDefinition Duplicate(
        string newId,
        Func<EvaluatorDefinition, string> evaluatorIdFactory,
        Func<CriterionDefinition, string> criterionIdFactory,
        Func<SpecialEvaluationDefinition, string> specialEvaluationIdFactory)
    {
        ArgumentNullException.ThrowIfNull(evaluatorIdFactory);
        ArgumentNullException.ThrowIfNull(criterionIdFactory);
        ArgumentNullException.ThrowIfNull(specialEvaluationIdFactory);
        return this with
        {
            Id = newId,
            Evaluators = DefinitionCollectionOperations.Normalize(Evaluators)
                .Select(evaluator => evaluator.Duplicate(evaluatorIdFactory(evaluator), criterionIdFactory))
                .ToImmutableArray(),
            SpecialEvaluations = DefinitionCollectionOperations.Normalize(SpecialEvaluations)
                .Select(specialEvaluation => specialEvaluation.Duplicate(
                    specialEvaluationIdFactory(specialEvaluation)))
                .ToImmutableArray(),
        };
    }
}
