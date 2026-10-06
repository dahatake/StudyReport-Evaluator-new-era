using System.Collections.Immutable;

namespace StudyReportEvaluator.Core.Domain;

public sealed record SpecialEvaluationDefinition
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string PrimarySourceColumn { get; init; }

    public ImmutableArray<string> SupportingSourceColumns { get; init; } = [];

    public required string PromptTemplate { get; init; }

    public bool Enabled { get; init; } = true;

    public SpecialEvaluationDefinition Duplicate(string newId) => this with { Id = newId };
}