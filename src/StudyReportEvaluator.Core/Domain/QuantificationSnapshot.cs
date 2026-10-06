using System.Collections.Immutable;
using StudyReportEvaluator.Core.Serialization;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.Core.Domain;

public sealed class QuantificationSnapshot
{
    private QuantificationSnapshot(
        QuantificationDefinition definition,
        string canonicalJson,
        string sha256)
    {
        Definition = definition;
        CanonicalJson = canonicalJson;
        Sha256 = sha256;
    }

    public QuantificationDefinition Definition { get; }

    public string CanonicalJson { get; }

    public string Sha256 { get; }

    public static QuantificationSnapshot Create(QuantificationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        QuantificationDefinitionValidator validator = new();
        DefinitionValidationResult validation = validator.Validate(definition);
        if (!validation.IsValid)
        {
            throw new QuantificationDefinitionValidationException(validation.Errors);
        }

        QuantificationDefinition frozenDefinition = Freeze(definition);
        CanonicalDefinitionSerializer serializer = new();
        string canonicalJson = serializer.Serialize(frozenDefinition);
        string hash = serializer.ComputeSha256(frozenDefinition);
        return new QuantificationSnapshot(frozenDefinition, canonicalJson, hash);
    }

    public bool HasValidHash()
    {
        CanonicalDefinitionSerializer serializer = new();
        return string.Equals(Sha256, serializer.ComputeSha256(Definition), StringComparison.Ordinal);
    }

    private static QuantificationDefinition Freeze(QuantificationDefinition definition) => definition with
    {
        Questions = DefinitionCollectionOperations.Normalize(definition.Questions)
            .Select(FreezeQuestion)
            .ToImmutableArray(),
    };

    private static QuestionDefinition FreezeQuestion(QuestionDefinition question) => question with
    {
        SupportingSourceColumns = DefinitionCollectionOperations.Normalize(question.SupportingSourceColumns).ToImmutableArray(),
        Evaluators = DefinitionCollectionOperations.Normalize(question.Evaluators)
            .Select(FreezeEvaluator)
            .ToImmutableArray(),
        SpecialEvaluations = DefinitionCollectionOperations.Normalize(question.SpecialEvaluations)
            .Select(FreezeSpecialEvaluation)
            .ToImmutableArray(),
    };

    private static EvaluatorDefinition FreezeEvaluator(EvaluatorDefinition evaluator) => evaluator with
    {
        Criteria = DefinitionCollectionOperations.Normalize(evaluator.Criteria)
            .Select(criterion => criterion with { })
            .ToImmutableArray(),
    };

    private static SpecialEvaluationDefinition FreezeSpecialEvaluation(
        SpecialEvaluationDefinition specialEvaluation) => specialEvaluation with
        {
            SupportingSourceColumns = DefinitionCollectionOperations.Normalize(
                    specialEvaluation.SupportingSourceColumns)
                .ToImmutableArray(),
        };
}
