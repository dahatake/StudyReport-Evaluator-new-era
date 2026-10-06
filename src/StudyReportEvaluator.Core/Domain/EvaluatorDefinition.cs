using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace StudyReportEvaluator.Core.Domain;

public enum EvaluatorType
{
    [JsonStringEnumMemberName("KNOWLEDGE_COVERAGE")]
    KnowledgeCoverage,

    [JsonStringEnumMemberName("CUSTOM_PROMPT")]
    CustomPrompt,
}

public sealed record EvaluatorDefinition
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public EvaluatorType Type { get; init; }

    public decimal Weight { get; init; }

    public ScoreRange Range { get; init; }

    public ImmutableArray<CriterionDefinition> Criteria { get; init; } = [];

    public string? BuiltInTemplateVersion { get; init; }

    public string? CustomPromptTemplate { get; init; }

    public bool Enabled { get; init; } = true;

    public EvaluatorDefinition AddCriterion(CriterionDefinition criterion) =>
        this with { Criteria = DefinitionCollectionOperations.Normalize(Criteria).Add(criterion) };

    public EvaluatorDefinition DuplicateCriterion(int sourceIndex, string newId)
    {
        ImmutableArray<CriterionDefinition> items = DefinitionCollectionOperations.Normalize(Criteria);
        CriterionDefinition duplicate = items[DefinitionCollectionOperations.RequireIndex(items, sourceIndex)].Duplicate(newId);
        return this with { Criteria = items.Insert(sourceIndex + 1, duplicate) };
    }

    public EvaluatorDefinition MoveCriterion(int sourceIndex, int destinationIndex) =>
        this with { Criteria = DefinitionCollectionOperations.Move(Criteria, sourceIndex, destinationIndex) };

    public EvaluatorDefinition SetCriterionEnabled(int index, bool enabled)
    {
        ImmutableArray<CriterionDefinition> items = DefinitionCollectionOperations.Normalize(Criteria);
        DefinitionCollectionOperations.RequireIndex(items, index);
        return this with { Criteria = DefinitionCollectionOperations.Replace(items, index, items[index] with { Enabled = enabled }) };
    }

    public EvaluatorDefinition RemoveCriterion(int index) =>
        this with { Criteria = DefinitionCollectionOperations.RemoveAt(Criteria, index) };

    public EvaluatorDefinition Duplicate(
        string newId,
        Func<CriterionDefinition, string> criterionIdFactory)
    {
        ArgumentNullException.ThrowIfNull(criterionIdFactory);
        return this with
        {
            Id = newId,
            Criteria = DefinitionCollectionOperations.Normalize(Criteria)
                .Select(criterion => criterion.Duplicate(criterionIdFactory(criterion)))
                .ToImmutableArray(),
        };
    }
}
