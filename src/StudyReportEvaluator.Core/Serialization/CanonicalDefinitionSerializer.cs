using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.Core.Serialization;

public sealed class CanonicalDefinitionSerializer
{
    public const string SchemaVersion = "4.0";

    public string Serialize(QuantificationDefinition definition) =>
        Encoding.UTF8.GetString(SerializeToUtf8Bytes(definition));

    public byte[] SerializeToUtf8Bytes(QuantificationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false });
        WriteDefinition(writer, definition);
        writer.Flush();
        return stream.ToArray();
    }

    public string ComputeSha256(QuantificationDefinition definition) =>
        Convert.ToHexString(SHA256.HashData(SerializeToUtf8Bytes(definition)));

    private static void WriteDefinition(Utf8JsonWriter writer, QuantificationDefinition definition)
    {
        writer.WriteStartObject();
        writer.WriteString("schemaVersion", SchemaVersion);
        writer.WriteString("id", definition.Id);
        writer.WriteString("name", definition.Name);
        writer.WriteString("revision", definition.Revision);
        writer.WriteString("sourceSheet", definition.SourceSheet);
        writer.WriteNumber("headerRow", definition.HeaderRow);
        writer.WriteNumber("firstDataRow", definition.FirstDataRow);
        writer.WriteNumber("lastDataRow", definition.LastDataRow);
        WriteDecimal(writer, "basePoints", definition.BasePoints);
        WriteDecimal(writer, "specialPoints", definition.SpecialPoints);
        WriteDecimal(writer, "similarityPenaltyWeight", definition.SimilarityPenaltyWeight);
        writer.WriteNumber("roundingDigits", definition.RoundingDigits);
        writer.WriteStartArray("questions");
        foreach (QuestionDefinition question in DefinitionCollectionOperations.Normalize(definition.Questions))
        {
            WriteQuestion(writer, question);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteQuestion(Utf8JsonWriter writer, QuestionDefinition question)
    {
        writer.WriteStartObject();
        writer.WriteString("id", question.Id);
        writer.WriteString("displayName", question.DisplayName);
        writer.WriteString("questionText", question.QuestionText);
        writer.WriteString("primarySourceColumn", question.PrimarySourceColumn);
        writer.WriteStartArray("supportingSourceColumns");
        foreach (string column in DefinitionCollectionOperations.Normalize(question.SupportingSourceColumns))
        {
            writer.WriteStringValue(column);
        }

        writer.WriteEndArray();
        WriteDecimal(writer, "points", question.Points);
        writer.WriteBoolean("enabled", question.Enabled);
        writer.WriteStartArray("evaluators");
        foreach (EvaluatorDefinition evaluator in DefinitionCollectionOperations.Normalize(question.Evaluators))
        {
            WriteEvaluator(writer, evaluator);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("specialEvaluations");
        foreach (SpecialEvaluationDefinition special in DefinitionCollectionOperations.Normalize(question.SpecialEvaluations))
        {
            WriteSpecialEvaluation(writer, special);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSpecialEvaluation(
        Utf8JsonWriter writer,
        SpecialEvaluationDefinition special)
    {
        writer.WriteStartObject();
        writer.WriteString("id", special.Id);
        writer.WriteString("displayName", special.DisplayName);
        writer.WriteString("primarySourceColumn", special.PrimarySourceColumn);
        writer.WriteStartArray("supportingSourceColumns");
        foreach (string column in DefinitionCollectionOperations.Normalize(special.SupportingSourceColumns))
        {
            writer.WriteStringValue(column);
        }

        writer.WriteEndArray();
        writer.WriteString("promptTemplate", special.PromptTemplate);
        writer.WriteBoolean("enabled", special.Enabled);
        writer.WriteEndObject();
    }

    private static void WriteEvaluator(Utf8JsonWriter writer, EvaluatorDefinition evaluator)
    {
        writer.WriteStartObject();
        writer.WriteString("id", evaluator.Id);
        writer.WriteString("displayName", evaluator.DisplayName);
        writer.WriteString("type", GetEvaluatorTypeName(evaluator.Type));
        WriteDecimal(writer, "weight", evaluator.Weight);
        WriteRange(writer, "range", evaluator.Range);
        writer.WriteString("builtInTemplateVersion", evaluator.BuiltInTemplateVersion);
        writer.WriteString("customPromptTemplate", evaluator.CustomPromptTemplate);
        writer.WriteBoolean("enabled", evaluator.Enabled);
        writer.WriteStartArray("criteria");
        foreach (CriterionDefinition criterion in DefinitionCollectionOperations.Normalize(evaluator.Criteria))
        {
            WriteCriterion(writer, criterion);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCriterion(Utf8JsonWriter writer, CriterionDefinition criterion)
    {
        writer.WriteStartObject();
        writer.WriteString("id", criterion.Id);
        writer.WriteString("displayName", criterion.DisplayName);
        writer.WriteString("description", criterion.Description);
        WriteDecimal(writer, "weight", criterion.Weight);
        if (criterion.Range is ScoreRange range)
        {
            WriteRange(writer, "range", range);
        }
        else
        {
            writer.WriteNull("range");
        }

        writer.WriteBoolean("enabled", criterion.Enabled);
        writer.WriteEndObject();
    }

    private static void WriteRange(Utf8JsonWriter writer, string propertyName, ScoreRange range)
    {
        writer.WriteStartObject(propertyName);
        WriteDecimal(writer, "minimum", range.Minimum);
        WriteDecimal(writer, "maximum", range.Maximum);
        writer.WriteEndObject();
    }

    private static void WriteDecimal(Utf8JsonWriter writer, string propertyName, decimal value)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture), skipInputValidation: false);
    }

    private static string GetEvaluatorTypeName(EvaluatorType type) => type switch
    {
        EvaluatorType.KnowledgeCoverage => "KNOWLEDGE_COVERAGE",
        EvaluatorType.CustomPrompt => "CUSTOM_PROMPT",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported evaluator type."),
    };
}
