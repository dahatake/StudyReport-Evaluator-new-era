using System.Globalization;
using System.Text.Json;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using StudyReportEvaluator.Core.Tests.Validation;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Serialization;

public sealed class CanonicalDefinitionSerializerTests
{
    private readonly CanonicalDefinitionSerializer _serializer = new();

    [Fact]
    public void Serialization_is_compact_parseable_ordered_and_uses_normative_type_names()
    {
        QuantificationDefinition definition = C02TestDefinitions.CreateValid();

        string json = _serializer.Serialize(definition);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.DoesNotContain("\r", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", json, StringComparison.Ordinal);
        Assert.Equal("4.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(60m, document.RootElement.GetProperty("basePoints").GetDecimal());
        Assert.Equal(0m, document.RootElement.GetProperty("specialPoints").GetDecimal());
        Assert.Equal(0.1m, document.RootElement.GetProperty("similarityPenaltyWeight").GetDecimal());
        Assert.Equal(40m, document.RootElement.GetProperty("questions")[0].GetProperty("points").GetDecimal());
        Assert.Empty(document.RootElement.GetProperty("questions")[0].GetProperty("specialEvaluations").EnumerateArray());
        Assert.Equal(["Q1", "Q2"], document.RootElement.GetProperty("questions").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.Equal("KNOWLEDGE_COVERAGE", document.RootElement.GetProperty("questions")[0].GetProperty("evaluators")[0].GetProperty("type").GetString());
        Assert.Equal("CUSTOM_PROMPT", document.RootElement.GetProperty("questions")[0].GetProperty("evaluators")[1].GetProperty("type").GetString());
    }

    [Fact]
    public void Equal_decimal_values_have_the_same_canonical_hash_regardless_of_scale_or_culture()
    {
        QuantificationDefinition first = C02TestDefinitions.CreateValid();
        QuantificationDefinition second = first with
        {
            Questions = [first.Questions[0] with { Points = 40.000m }],
        };
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string firstHash = _serializer.ComputeSha256(first with { Questions = [first.Questions[0]] });
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");
            string secondHash = _serializer.ComputeSha256(second);

            Assert.Equal(firstHash, secondHash);
            Assert.Matches("^[0-9A-F]{64}$", firstHash);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void Ordered_collection_changes_change_the_hash()
    {
        QuantificationDefinition first = C02TestDefinitions.CreateValid();
        QuantificationDefinition reordered = first.MoveQuestion(1, 0);

        Assert.NotEqual(_serializer.ComputeSha256(first), _serializer.ComputeSha256(reordered));
    }

    [Fact]
    public void Every_execution_relevant_value_changes_the_hash()
    {
        QuantificationDefinition first = C02TestDefinitions.CreateValid();
        QuestionDefinition changedQuestion = first.Questions[0] with
        {
            PrimarySourceColumn = "H",
            Points = 9m,
            Enabled = false,
        };
        QuantificationDefinition changed = first with { Questions = [changedQuestion, first.Questions[1]] };

        Assert.NotEqual(_serializer.ComputeSha256(first), _serializer.ComputeSha256(changed));
        Assert.Contains("\"primarySourceColumn\":\"H\"", _serializer.Serialize(changed), StringComparison.Ordinal);
        Assert.Contains("\"enabled\":false", _serializer.Serialize(changed), StringComparison.Ordinal);
    }

    [Fact]
    public void V4_root_allocation_and_special_definition_are_canonical_and_hash_bound()
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

        string json = _serializer.Serialize(definition);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement serializedSpecial = Assert.Single(
            document.RootElement.GetProperty("questions")[0].GetProperty("specialEvaluations").EnumerateArray());

        Assert.Equal("S1", serializedSpecial.GetProperty("id").GetString());
        Assert.Equal("G", serializedSpecial.GetProperty("primarySourceColumn").GetString());
        Assert.Equal(["K"], serializedSpecial.GetProperty("supportingSourceColumns").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("Evaluate {回答}", serializedSpecial.GetProperty("promptTemplate").GetString());
        Assert.NotEqual(
            _serializer.ComputeSha256(definition),
            _serializer.ComputeSha256(definition with { SimilarityPenaltyWeight = 0.2m }));
        Assert.NotEqual(
            _serializer.ComputeSha256(definition),
            _serializer.ComputeSha256(definition with
            {
                Questions = [definition.Questions[0] with { SpecialEvaluations = [special with { PromptTemplate = "Changed {回答}" }] }, definition.Questions[1]],
            }));
    }
}
