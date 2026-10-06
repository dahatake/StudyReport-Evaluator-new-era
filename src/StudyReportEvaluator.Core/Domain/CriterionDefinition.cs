namespace StudyReportEvaluator.Core.Domain;

public readonly record struct ScoreRange(decimal Minimum, decimal Maximum);

public sealed record CriterionDefinition
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public decimal Weight { get; init; }

    public ScoreRange? Range { get; init; }

    public bool Enabled { get; init; } = true;

    public CriterionDefinition Duplicate(string newId) => this with { Id = newId };
}
