using System.Collections.Immutable;

namespace StudyReportEvaluator.Core.Domain;

public sealed record QuantificationDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Revision { get; init; }

    public required string SourceSheet { get; init; }

    public int HeaderRow { get; init; }

    public int FirstDataRow { get; init; }

    public int LastDataRow { get; init; }

    public decimal BasePoints { get; init; } = 60m;

    public decimal SpecialPoints { get; init; }

    public decimal SimilarityPenaltyWeight { get; init; } = 0.1m;

    public int RoundingDigits { get; init; } = 1;

    public ImmutableArray<QuestionDefinition> Questions { get; init; } = [];

    public QuantificationDefinition AddQuestion(QuestionDefinition question) =>
        this with { Questions = DefinitionCollectionOperations.Normalize(Questions).Add(question) };

    public QuantificationDefinition DuplicateQuestion(
        int sourceIndex,
        string newId,
        Func<EvaluatorDefinition, string> evaluatorIdFactory,
        Func<CriterionDefinition, string> criterionIdFactory,
        Func<SpecialEvaluationDefinition, string> specialEvaluationIdFactory)
    {
        ImmutableArray<QuestionDefinition> items = DefinitionCollectionOperations.Normalize(Questions);
        QuestionDefinition duplicate = items[DefinitionCollectionOperations.RequireIndex(items, sourceIndex)]
            .Duplicate(newId, evaluatorIdFactory, criterionIdFactory, specialEvaluationIdFactory);
        return this with { Questions = items.Insert(sourceIndex + 1, duplicate) };
    }

    public QuantificationDefinition MoveQuestion(int sourceIndex, int destinationIndex) =>
        this with { Questions = DefinitionCollectionOperations.Move(Questions, sourceIndex, destinationIndex) };

    public QuantificationDefinition SetQuestionEnabled(int index, bool enabled)
    {
        ImmutableArray<QuestionDefinition> items = DefinitionCollectionOperations.Normalize(Questions);
        DefinitionCollectionOperations.RequireIndex(items, index);
        return this with { Questions = DefinitionCollectionOperations.Replace(items, index, items[index] with { Enabled = enabled }) };
    }

    public QuantificationDefinition RemoveQuestion(int index) =>
        this with { Questions = DefinitionCollectionOperations.RemoveAt(Questions, index) };
}

internal static class DefinitionCollectionOperations
{
    internal static ImmutableArray<T> Normalize<T>(ImmutableArray<T> items) =>
        items.IsDefault ? [] : items;

    internal static int RequireIndex<T>(ImmutableArray<T> items, int index)
    {
        if ((uint)index >= (uint)items.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index must identify an existing definition item.");
        }

        return index;
    }

    internal static ImmutableArray<T> Move<T>(ImmutableArray<T> source, int sourceIndex, int destinationIndex)
    {
        ImmutableArray<T> items = Normalize(source);
        RequireIndex(items, sourceIndex);
        RequireIndex(items, destinationIndex);
        if (sourceIndex == destinationIndex)
        {
            return items;
        }

        T item = items[sourceIndex];
        return items.RemoveAt(sourceIndex).Insert(destinationIndex, item);
    }

    internal static ImmutableArray<T> Replace<T>(ImmutableArray<T> source, int index, T value)
    {
        ImmutableArray<T> items = Normalize(source);
        RequireIndex(items, index);
        return items.SetItem(index, value);
    }

    internal static ImmutableArray<T> RemoveAt<T>(ImmutableArray<T> source, int index)
    {
        ImmutableArray<T> items = Normalize(source);
        RequireIndex(items, index);
        return items.RemoveAt(index);
    }
}
