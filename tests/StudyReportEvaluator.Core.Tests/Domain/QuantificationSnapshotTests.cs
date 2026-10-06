using System.Collections.Immutable;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using StudyReportEvaluator.Core.Tests.Validation;
using StudyReportEvaluator.Core.Validation;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Domain;

public sealed class QuantificationSnapshotTests
{
    [Fact]
    public void Snapshot_is_a_deep_copy_with_a_valid_canonical_hash()
    {
        QuantificationDefinition draft = C02TestDefinitions.CreateValid();

        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(draft);

        Assert.NotSame(draft, snapshot.Definition);
        Assert.NotSame(draft.Questions[0], snapshot.Definition.Questions[0]);
        Assert.NotSame(draft.Questions[0].Evaluators[0], snapshot.Definition.Questions[0].Evaluators[0]);
        Assert.NotSame(draft.Questions[0].Evaluators[0].Criteria[0], snapshot.Definition.Questions[0].Evaluators[0].Criteria[0]);
        Assert.True(snapshot.HasValidHash());
        Assert.Equal(new CanonicalDefinitionSerializer().Serialize(snapshot.Definition), snapshot.CanonicalJson);
        Assert.Matches("^[0-9A-F]{64}$", snapshot.Sha256);
    }

    [Fact]
    public void Editing_a_draft_during_a_run_cannot_change_the_snapshot()
    {
        QuantificationDefinition draft = C02TestDefinitions.CreateValid();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(draft);
        string originalJson = snapshot.CanonicalJson;
        string originalHash = snapshot.Sha256;

        QuestionDefinition changedQuestion = draft.Questions[0] with
        {
            PrimarySourceColumn = "L",
            SupportingSourceColumns = ["A"],
            Points = 99m,
            Enabled = false,
            Evaluators =
            [
                draft.Questions[0].Evaluators[0] with
                {
                    Range = new ScoreRange(10m, 20m),
                    Weight = 42m,
                    Criteria =
                    [
                        draft.Questions[0].Evaluators[0].Criteria[0] with { Weight = 17m, Enabled = false },
                    ],
                },
            ],
        };
        draft = draft with { Questions = [changedQuestion, draft.Questions[1]] };

        Assert.NotEqual(draft, snapshot.Definition);
        Assert.Equal("G", snapshot.Definition.Questions[0].PrimarySourceColumn);
        Assert.Equal(2m, snapshot.Definition.Questions[0].Evaluators[0].Criteria[0].Weight);
        Assert.True(snapshot.Definition.Questions[0].Evaluators[0].Criteria[0].Enabled);
        Assert.Equal(originalJson, snapshot.CanonicalJson);
        Assert.Equal(originalHash, snapshot.Sha256);
        Assert.True(snapshot.HasValidHash());
    }

    [Fact]
    public void Invalid_definition_is_rejected_before_snapshot_creation()
    {
        QuantificationDefinition invalid = C02TestDefinitions.CreateValid() with
        {
            Questions = C02TestDefinitions.CreateValid().Questions.Select(question => question with { Enabled = false }).ToImmutableArray(),
        };

        QuantificationDefinitionValidationException exception = Assert.Throws<QuantificationDefinitionValidationException>(
            () => QuantificationSnapshot.Create(invalid));

        Assert.Contains(exception.Errors, error => error.Code == "ENABLED_QUESTION_REQUIRED");
        Assert.DoesNotContain("Prompt", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disabled_nodes_with_default_child_collections_are_normalized_in_snapshot()
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuestionDefinition disabled = source.Questions[1] with
        {
            Evaluators = default,
            SupportingSourceColumns = default,
        };
        QuantificationDefinition definition = source with { Questions = [source.Questions[0], disabled] };

        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);

        Assert.False(snapshot.Definition.Questions[1].Evaluators.IsDefault);
        Assert.Empty(snapshot.Definition.Questions[1].Evaluators);
        Assert.False(snapshot.Definition.Questions[1].SupportingSourceColumns.IsDefault);
        Assert.Empty(snapshot.Definition.Questions[1].SupportingSourceColumns);
    }

    [Fact]
    public void Snapshot_deep_copies_special_definitions_and_binds_them_to_the_hash()
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        SpecialEvaluationDefinition special = new()
        {
            Id = "S1",
            DisplayName = "Prompt quality",
            PrimarySourceColumn = "G",
            SupportingSourceColumns = ["K"],
            PromptTemplate = "Evaluate {回答}",
        };
        QuantificationDefinition definition = source with
        {
            SpecialPoints = 10m,
            Questions =
            [
                source.Questions[0] with { Points = 30m, SpecialEvaluations = [special] },
                source.Questions[1],
            ],
        };

        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);

        SpecialEvaluationDefinition frozen = Assert.Single(snapshot.Definition.Questions[0].SpecialEvaluations);
        Assert.NotSame(special, frozen);
        Assert.Equal(["K"], frozen.SupportingSourceColumns);
        Assert.True(snapshot.HasValidHash());
        Assert.NotEqual(
            snapshot.Sha256,
            new CanonicalDefinitionSerializer().ComputeSha256(snapshot.Definition with
            {
                Questions =
                [
                    snapshot.Definition.Questions[0] with
                    {
                        SpecialEvaluations = [frozen with { PromptTemplate = "Changed {回答}" }],
                    },
                    snapshot.Definition.Questions[1],
                ],
            }));
    }
}
