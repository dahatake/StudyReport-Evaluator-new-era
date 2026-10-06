using System.Text.Json;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Settings;

// Requirements: FR-040 (AC-041)
public sealed class ApplicationSettingsTests
{
    private const string ModelCanary = "PRIVATE-T02-MODEL-not-yet-verified";
    private const string TextCanary = "PRIVATE-T02-TEXT 日本語 🌸 e\u0301\r\n\"引用\"と\\と{brace}\n末尾  ";
    private const string PromptCanary = "PRIVATE-T02-PROMPT 日本語 🌸\r\n\"引用\"と\\\n{回答}\n{評価項目}\n末尾  ";

    private static readonly string OutputDirectoryCanary = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "PRIVATE-T02-OUTPUT", "合成 {設定}"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void Defaults_round_trip_with_explicit_integer_schema_one_and_only_persisted_members()
    {
        ApplicationSettings original = new();

        string json = JsonSerializer.Serialize(original);
        ApplicationSettings restored = Assert.IsType<ApplicationSettings>(
            JsonSerializer.Deserialize<ApplicationSettings>(json));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal(1, ApplicationSettings.CurrentSchemaVersion);
        Assert.Equal(1, original.SchemaVersion);
        Assert.Equal(8, original.MaxConcurrency);
        Assert.Null(original.PreferredModelId);
        Assert.Null(original.OutputDirectoryOverride);
        Assert.Null(original.Definition);
        Assert.Equal(original, restored);
        Assert.Equal(
            ["definition", "maxConcurrency", "outputDirectoryOverride", "preferredModelId", "schemaVersion"],
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(8, root.GetProperty("maxConcurrency").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("preferredModelId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("outputDirectoryOverride").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("definition").ValueKind);
    }

    [Fact]
    public void Schema_only_json_restores_optional_defaults()
    {
        ApplicationSettings restored = Assert.IsType<ApplicationSettings>(
            JsonSerializer.Deserialize<ApplicationSettings>("""{"schemaVersion":1}""", JsonOptions));

        Assert.Equal(new ApplicationSettings(), restored);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"preferredModelId":null,"maxConcurrency":3,"outputDirectoryOverride":null,"definition":null}""")]
    public void Deserialization_rejects_missing_schema_version(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Explicit_preferences_round_trip_without_resolving_effective_values(int maxConcurrency)
    {
        ApplicationSettings original = new()
        {
            SchemaVersion = 1,
            PreferredModelId = ModelCanary,
            MaxConcurrency = maxConcurrency,
            OutputDirectoryOverride = OutputDirectoryCanary,
            Definition = null,
        };

        string json = JsonSerializer.Serialize(original, JsonOptions);
        ApplicationSettings restored = Assert.IsType<ApplicationSettings>(
            JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions));
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(ModelCanary, restored.PreferredModelId);
        Assert.Equal(maxConcurrency, restored.MaxConcurrency);
        Assert.Equal(OutputDirectoryCanary, restored.OutputDirectoryOverride);
        Assert.Null(restored.Definition);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Definition_round_trip_preserves_all_fields_and_canonical_hash()
    {
        QuantificationDefinition definition = CreateDefinition();
        ApplicationSettings original = new()
        {
            PreferredModelId = ModelCanary,
            MaxConcurrency = 3,
            OutputDirectoryOverride = OutputDirectoryCanary,
            Definition = definition,
        };

        string json = JsonSerializer.Serialize(original, JsonOptions);
        ApplicationSettings restored = Assert.IsType<ApplicationSettings>(
            JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions));
        QuantificationDefinition actual = Assert.IsType<QuantificationDefinition>(restored.Definition);

        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(original.PreferredModelId, restored.PreferredModelId);
        Assert.Equal(original.MaxConcurrency, restored.MaxConcurrency);
        Assert.Equal(original.OutputDirectoryOverride, restored.OutputDirectoryOverride);
        Assert.NotSame(definition, actual);
        Assert.Equal(definition.Id, actual.Id);
        Assert.Equal(definition.Name, actual.Name);
        Assert.Equal(definition.Revision, actual.Revision);
        Assert.Equal(definition.SourceSheet, actual.SourceSheet);
        Assert.Equal(definition.HeaderRow, actual.HeaderRow);
        Assert.Equal(definition.FirstDataRow, actual.FirstDataRow);
        Assert.Equal(definition.LastDataRow, actual.LastDataRow);
        Assert.Equal(definition.BasePoints, actual.BasePoints);
        Assert.Equal(definition.SpecialPoints, actual.SpecialPoints);
        Assert.Equal(definition.SimilarityPenaltyWeight, actual.SimilarityPenaltyWeight);
        Assert.Equal(definition.RoundingDigits, actual.RoundingDigits);
        Assert.Equal(
            definition.Questions.Select(question => question.Id),
            actual.Questions.Select(question => question.Id));

        for (int questionIndex = 0; questionIndex < definition.Questions.Length; questionIndex++)
        {
            QuestionDefinition expectedQuestion = definition.Questions[questionIndex];
            QuestionDefinition actualQuestion = actual.Questions[questionIndex];
            Assert.Equal(expectedQuestion.DisplayName, actualQuestion.DisplayName);
            Assert.Equal(expectedQuestion.QuestionText, actualQuestion.QuestionText);
            Assert.Equal(expectedQuestion.PrimarySourceColumn, actualQuestion.PrimarySourceColumn);
            Assert.Equal(expectedQuestion.SupportingSourceColumns.ToArray(), actualQuestion.SupportingSourceColumns.ToArray());
            Assert.Equal(expectedQuestion.Points, actualQuestion.Points);
            Assert.Equal(expectedQuestion.Enabled, actualQuestion.Enabled);
            Assert.Equal(
                expectedQuestion.Evaluators.Select(evaluator => evaluator.Id),
                actualQuestion.Evaluators.Select(evaluator => evaluator.Id));

            for (int evaluatorIndex = 0; evaluatorIndex < expectedQuestion.Evaluators.Length; evaluatorIndex++)
            {
                EvaluatorDefinition expectedEvaluator = expectedQuestion.Evaluators[evaluatorIndex];
                EvaluatorDefinition actualEvaluator = actualQuestion.Evaluators[evaluatorIndex];
                Assert.Equal(expectedEvaluator.DisplayName, actualEvaluator.DisplayName);
                Assert.Equal(expectedEvaluator.Type, actualEvaluator.Type);
                Assert.Equal(expectedEvaluator.Weight, actualEvaluator.Weight);
                Assert.Equal(expectedEvaluator.Range, actualEvaluator.Range);
                Assert.Equal(expectedEvaluator.BuiltInTemplateVersion, actualEvaluator.BuiltInTemplateVersion);
                Assert.Equal(expectedEvaluator.CustomPromptTemplate, actualEvaluator.CustomPromptTemplate);
                Assert.Equal(expectedEvaluator.Enabled, actualEvaluator.Enabled);
                Assert.Equal(expectedEvaluator.Criteria.ToArray(), actualEvaluator.Criteria.ToArray());
            }

            Assert.Equal(
                expectedQuestion.SpecialEvaluations.Select(special => special.Id),
                actualQuestion.SpecialEvaluations.Select(special => special.Id));
            for (int specialIndex = 0; specialIndex < expectedQuestion.SpecialEvaluations.Length; specialIndex++)
            {
                SpecialEvaluationDefinition expectedSpecial = expectedQuestion.SpecialEvaluations[specialIndex];
                SpecialEvaluationDefinition actualSpecial = actualQuestion.SpecialEvaluations[specialIndex];
                Assert.Equal(expectedSpecial.DisplayName, actualSpecial.DisplayName);
                Assert.Equal(expectedSpecial.PrimarySourceColumn, actualSpecial.PrimarySourceColumn);
                Assert.Equal(expectedSpecial.SupportingSourceColumns.ToArray(), actualSpecial.SupportingSourceColumns.ToArray());
                Assert.Equal(expectedSpecial.PromptTemplate, actualSpecial.PromptTemplate);
                Assert.Equal(expectedSpecial.Enabled, actualSpecial.Enabled);
            }
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement jsonDefinition = document.RootElement.GetProperty("definition");
        Assert.Equal(definition.BasePoints, jsonDefinition.GetProperty("basePoints").GetDecimal());
        Assert.Equal(definition.SpecialPoints, jsonDefinition.GetProperty("specialPoints").GetDecimal());
        Assert.Equal(definition.SimilarityPenaltyWeight, jsonDefinition.GetProperty("similarityPenaltyWeight").GetDecimal());
        JsonElement jsonQuestion = jsonDefinition.GetProperty("questions")[0];
        Assert.Equal(TextCanary, jsonQuestion.GetProperty("questionText").GetString());
        JsonElement jsonEvaluator = jsonQuestion.GetProperty("evaluators")[0];
        Assert.Equal(PromptCanary, jsonEvaluator.GetProperty("customPromptTemplate").GetString());
        Assert.Equal(-1.125m, jsonEvaluator.GetProperty("range").GetProperty("minimum").GetDecimal());
        Assert.Equal(TextCanary, jsonEvaluator.GetProperty("criteria")[0].GetProperty("description").GetString());
        Assert.Equal(
            PromptCanary,
            jsonQuestion.GetProperty("specialEvaluations")[0].GetProperty("promptTemplate").GetString());

        CanonicalDefinitionSerializer canonical = new();
        Assert.Equal(canonical.ComputeSha256(definition), canonical.ComputeSha256(actual));
    }

    [Fact]
    public void With_expression_can_clear_preferences_without_changing_original()
    {
        QuantificationDefinition definition = CreateDefinition();
        ApplicationSettings original = new()
        {
            PreferredModelId = ModelCanary,
            MaxConcurrency = 3,
            OutputDirectoryOverride = OutputDirectoryCanary,
            Definition = definition,
        };

        ApplicationSettings cleared = original with
        {
            PreferredModelId = null,
            MaxConcurrency = 1,
            OutputDirectoryOverride = null,
        };
        string json = JsonSerializer.Serialize(cleared, JsonOptions);
        ApplicationSettings restored = Assert.IsType<ApplicationSettings>(
            JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions));

        Assert.NotSame(original, cleared);
        Assert.Equal(ModelCanary, original.PreferredModelId);
        Assert.Equal(3, original.MaxConcurrency);
        Assert.Equal(OutputDirectoryCanary, original.OutputDirectoryOverride);
        Assert.Same(definition, original.Definition);
        Assert.Same(definition, cleared.Definition);
        Assert.Equal(1, restored.SchemaVersion);
        Assert.Equal(1, restored.MaxConcurrency);
        Assert.Null(restored.PreferredModelId);
        Assert.Null(restored.OutputDirectoryOverride);
        CanonicalDefinitionSerializer canonical = new();
        Assert.Equal(
            canonical.ComputeSha256(definition),
            canonical.ComputeSha256(Assert.IsType<QuantificationDefinition>(restored.Definition)));
    }

    [Fact]
    public void ToString_redacts_model_path_and_definition_content()
    {
        ApplicationSettings settings = new()
        {
            PreferredModelId = ModelCanary,
            OutputDirectoryOverride = OutputDirectoryCanary,
            Definition = CreateDefinition(),
        };

        Assert.Equal("ApplicationSettings { Content = <redacted> }", settings.ToString());
        Assert.Equal("ApplicationSettings { Content = <redacted> }", new ApplicationSettings().ToString());
    }

    private static QuantificationDefinition CreateDefinition()
    {
        EvaluatorDefinition custom = U01TestSupport.Evaluator(
            "E-Z",
            "C-Z",
            evaluatorRange: new ScoreRange(-1.125m, 8.875m),
            criterionRange: new ScoreRange(-0.125m, 4.625m),
            customPrompt: PromptCanary,
            evaluatorWeight: 1.375m,
            criterionWeight: 0.875m);
        custom = custom with
        {
            DisplayName = TextCanary,
            Criteria =
            [
                custom.Criteria[0] with { DisplayName = TextCanary, Description = TextCanary },
                custom.Criteria[0] with { Id = "C-A", Weight = 0.0625m, Range = null, Enabled = false },
            ],
        };
        EvaluatorDefinition knowledge = U01TestSupport.Evaluator(
            "E-A",
            "C-KNOWLEDGE",
            enabled: false,
            evaluatorRange: new ScoreRange(0m, 1m),
            evaluatorWeight: 0.125m) with
        {
            Type = EvaluatorType.KnowledgeCoverage,
            BuiltInTemplateVersion = BuiltInPromptTemplates.KnowledgeTemplateVersion,
            CustomPromptTemplate = null,
        };
        SpecialEvaluationDefinition special = new()
        {
            Id = "S-Z",
            DisplayName = TextCanary,
            PrimarySourceColumn = "C",
            SupportingSourceColumns = ["D", "B"],
            PromptTemplate = PromptCanary,
        };
        QuestionDefinition question = U01TestSupport.Question(
            "Q-Z", "B", ["D", "C"], true, custom, knowledge) with
        {
            DisplayName = TextCanary,
            QuestionText = TextCanary,
            Points = 34.5m,
            SpecialEvaluations =
            [
                special,
                special with { Id = "S-A", SupportingSourceColumns = [], Enabled = false },
            ],
        };
        QuestionDefinition disabled = U01TestSupport.Question(
            "Q-A", "E", [], false, U01TestSupport.Evaluator("E-DISABLED", "C-DISABLED")) with
        {
            Points = 2.375m,
        };

        return U01TestSupport.Definition(3, 23, question, disabled) with
        {
            Id = "DEF-T02",
            Name = TextCanary,
            Revision = "revision-2",
            SourceSheet = "合成 回答",
            HeaderRow = 2,
            BasePoints = 55.125m,
            SpecialPoints = 10.375m,
            SimilarityPenaltyWeight = 0.1234567890123456789012345678m,
            RoundingDigits = 3,
        };
    }
}