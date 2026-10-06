using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Scoring;
using StudyReportEvaluator.Core.Validation;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Fixtures;

public sealed class SyntheticSpecificationContractTests
{
    private const int ExpectedSeed = 20260901;
    private readonly string _fixtureRoot = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "v3");

    [Fact]
    public void Manifest_hashes_every_fixed_seed_fixture_and_declares_no_content_data()
    {
        using JsonDocument manifest = Load("manifest.json");
        JsonElement root = manifest.RootElement;

        Assert.Equal("3.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(ExpectedSeed, root.GetProperty("seed").GetInt32());
        Assert.False(root.GetProperty("contentDataIncluded").GetBoolean());
        JsonElement[] files = root.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(4, files.Length);
        foreach (JsonElement file in files)
        {
            string relativePath = Assert.IsType<string>(file.GetProperty("path").GetString());
            string expectedHash = Assert.IsType<string>(file.GetProperty("sha256").GetString());
            string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_fixtureRoot, relativePath))));
            Assert.Equal(expectedHash, actualHash);
        }
    }

    [Fact]
    public void Definition_matrix_builds_all_27_dynamic_shapes_as_valid_deterministic_snapshots()
    {
        using JsonDocument matrix = Load("definition-matrix.json");
        JsonElement root = matrix.RootElement;
        int[] questionCounts = ReadIntArray(root, "questionCounts");
        int[] evaluatorCounts = ReadIntArray(root, "evaluatorCountsPerQuestion");
        int[] criterionCounts = ReadIntArray(root, "criterionCountsPerEvaluator");
        int scenarios = 0;

        foreach (int questionCount in questionCounts)
        {
            foreach (int evaluatorCount in evaluatorCounts)
            {
                foreach (int criterionCount in criterionCounts)
                {
                    QuantificationDefinition definition = BuildDefinition(questionCount, evaluatorCount, criterionCount);
                    QuantificationSnapshot first = QuantificationSnapshot.Create(definition);
                    QuantificationSnapshot second = QuantificationSnapshot.Create(BuildDefinition(questionCount, evaluatorCount, criterionCount));

                    Assert.Equal(questionCount, first.Definition.Questions.Length);
                    Assert.All(first.Definition.Questions, question => Assert.Equal(evaluatorCount, question.Evaluators.Length));
                    Assert.All(first.Definition.Questions.SelectMany(question => question.Evaluators), evaluator => Assert.Equal(criterionCount, evaluator.Criteria.Length));
                    Assert.Equal(first.Sha256, second.Sha256);
                    scenarios++;
                }
            }
        }

        Assert.Equal([1, 2, 10], questionCounts);
        Assert.Equal([1, 2, 5], evaluatorCounts);
        Assert.Equal([1, 4, 20], criterionCounts);
        Assert.Equal(root.GetProperty("expectedScenarioCount").GetInt32(), scenarios);
    }

    [Fact]
    public void Minimal_definition_fixture_has_one_complete_dynamic_hierarchy()
    {
        using JsonDocument minimal = Load("definition-minimal.json");
        JsonElement root = minimal.RootElement;
        JsonElement definition = root.GetProperty("definition");
        JsonElement question = Assert.Single(definition.GetProperty("questions").EnumerateArray());
        JsonElement evaluator = Assert.Single(question.GetProperty("evaluators").EnumerateArray());
        JsonElement criterion = Assert.Single(evaluator.GetProperty("criteria").EnumerateArray());

        Assert.Equal(ExpectedSeed, root.GetProperty("seed").GetInt32());
        Assert.Equal("KNOWLEDGE_COVERAGE", evaluator.GetProperty("type").GetString());
        Assert.Equal("SYN-C-001-001-001", criterion.GetProperty("id").GetString());
    }

    [Fact]
    public void Result_oracles_match_the_independent_hand_calculation_contract()
    {
        using JsonDocument oracles = Load("result-oracles.json");
        JsonElement root = oracles.RootElement;
        int digits = root.GetProperty("roundingDigits").GetInt32();
        WeightedScoreCalculator calculator = new();
        List<WeightedScoreInput> children = [];
        foreach (JsonElement criterion in root.GetProperty("criterionOracles").EnumerateArray())
        {
            decimal score = calculator.Normalize(
                criterion.GetProperty("raw").GetDecimal(),
                new ScoreRange(
                    criterion.GetProperty("minimum").GetDecimal(),
                    criterion.GetProperty("maximum").GetDecimal()),
                digits)!.Value;
            Assert.Equal(criterion.GetProperty("expectedNormalized").GetDecimal(), score);
            children.Add(new WeightedScoreInput(score, criterion.GetProperty("weight").GetDecimal()));
        }

        Assert.Equal(root.GetProperty("expectedEvaluatorScore").GetDecimal(), calculator.Aggregate(children, digits));
        foreach (JsonElement midpoint in root.GetProperty("midpointOracles").EnumerateArray())
        {
            Assert.Equal(
                midpoint.GetProperty("expected").GetDecimal(),
                calculator.Round(midpoint.GetProperty("input").GetDecimal(), digits));
        }

        JsonElement[] blankOracles = root.GetProperty("blankOracles").EnumerateArray().ToArray();
        Assert.Equal(4, blankOracles.Length);
        foreach (JsonElement oracle in blankOracles)
        {
            decimal minimum = oracle.TryGetProperty("minimum", out JsonElement minimumElement)
                ? minimumElement.GetDecimal()
                : 0m;
            decimal maximum = oracle.TryGetProperty("maximum", out JsonElement maximumElement)
                ? maximumElement.GetDecimal()
                : 10m;
            ScoreRange range = new(minimum, maximum);
            EffectiveRawSelection selection = calculator.SelectEffectiveRaw(
                oracle.GetProperty("scorable").GetBoolean(),
                ReadNullableDecimal(oracle.GetProperty("aiRaw")),
                ReadNullableDecimal(oracle.GetProperty("override")),
                range);
            decimal? normalized = calculator.Normalize(selection.Value, range, digits);

            Assert.Equal(ReadNullableDecimal(oracle.GetProperty("expectedEffectiveRaw")), selection.Value);
            Assert.Equal(ReadNullableDecimal(oracle.GetProperty("expectedNormalized")), normalized);
        }
    }

    [Fact]
    public void Sample_like_workbook_spec_has_531_rows_and_only_synthetic_metadata()
    {
        using JsonDocument scenarios = Load("workbook-scenarios.json");
        JsonElement root = scenarios.RootElement;
        JsonElement sampleLike = root.GetProperty("sampleLike");
        JsonElement policy = root.GetProperty("contentPolicy");

        Assert.Equal(531, sampleLike.GetProperty("rowsIncludingHeader").GetInt32());
        Assert.Equal("A1:L531", sampleLike.GetProperty("dimension").GetString());
        Assert.Equal(12, sampleLike.GetProperty("columns").GetInt32());
        Assert.Equal(["F", "G", "H", "I", "J", "K"], ReadStringArray(sampleLike, "primarySuggestionColumns"));
        Assert.All(policy.EnumerateObject(), property => Assert.False(property.Value.GetBoolean()));

        string allFixtures = string.Join('\n', Directory.EnumerateFiles(_fixtureRoot, "*.json").Select(File.ReadAllText));
        Assert.DoesNotContain("73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA", allFixtures, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_negative_fixture_introduces_exactly_one_expected_definition_error()
    {
        using JsonDocument scenarios = Load("workbook-scenarios.json");
        foreach (JsonElement negative in scenarios.RootElement.GetProperty("singleCauseDefinitionNegatives").EnumerateArray())
        {
            string mutation = Assert.IsType<string>(negative.GetProperty("mutation").GetString());
            string expectedCode = Assert.IsType<string>(negative.GetProperty("expectedCode").GetString());
            QuantificationDefinition definition = ApplySingleMutation(BuildDefinition(1, 1, 1), mutation);

            DefinitionValidationResult validation = new QuantificationDefinitionValidator().Validate(definition);

            DefinitionValidationError error = Assert.Single(validation.Errors);
            Assert.Equal(expectedCode, error.Code);
        }
    }

    private JsonDocument Load(string fileName) =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_fixtureRoot, fileName)));

    private static int[] ReadIntArray(JsonElement parent, string propertyName) =>
        parent.GetProperty(propertyName).EnumerateArray().Select(value => value.GetInt32()).ToArray();

    private static string[] ReadStringArray(JsonElement parent, string propertyName) =>
        parent.GetProperty(propertyName).EnumerateArray().Select(value => Assert.IsType<string>(value.GetString())).ToArray();

    private static decimal? ReadNullableDecimal(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();

    private static QuantificationDefinition BuildDefinition(int questions, int evaluators, int criteria)
    {
        ImmutableArray<decimal> questionPoints = new ScoringAllocationCalculator().Equalize(
            60m,
            0m,
            questions);
        ImmutableArray<QuestionDefinition>.Builder questionBuilder = ImmutableArray.CreateBuilder<QuestionDefinition>(questions);
        for (int questionIndex = 1; questionIndex <= questions; questionIndex++)
        {
            ImmutableArray<EvaluatorDefinition>.Builder evaluatorBuilder = ImmutableArray.CreateBuilder<EvaluatorDefinition>(evaluators);
            for (int evaluatorIndex = 1; evaluatorIndex <= evaluators; evaluatorIndex++)
            {
                ImmutableArray<CriterionDefinition>.Builder criterionBuilder = ImmutableArray.CreateBuilder<CriterionDefinition>(criteria);
                for (int criterionIndex = 1; criterionIndex <= criteria; criterionIndex++)
                {
                    criterionBuilder.Add(new CriterionDefinition
                    {
                        Id = $"SYN-C-{questionIndex:D2}-{evaluatorIndex:D2}-{criterionIndex:D2}",
                        DisplayName = $"Synthetic criterion {criterionIndex}",
                        Description = "Fixed synthetic concept",
                        Weight = criterionIndex,
                        Range = criterionIndex % 2 == 0 ? new ScoreRange(1m, 10m) : null,
                    });
                }

                bool knowledge = evaluatorIndex % 2 == 1;
                evaluatorBuilder.Add(new EvaluatorDefinition
                {
                    Id = $"SYN-E-{questionIndex:D2}-{evaluatorIndex:D2}",
                    DisplayName = $"Synthetic evaluator {evaluatorIndex}",
                    Type = knowledge ? EvaluatorType.KnowledgeCoverage : EvaluatorType.CustomPrompt,
                    Weight = evaluatorIndex,
                    Range = new ScoreRange(0m, 10m),
                    BuiltInTemplateVersion = knowledge ? "knowledge-v1" : null,
                    CustomPromptTemplate = knowledge ? null : "Assess {回答} using {評価項目}",
                    Criteria = criterionBuilder.MoveToImmutable(),
                });
            }

            questionBuilder.Add(new QuestionDefinition
            {
                Id = $"SYN-Q-{questionIndex:D2}",
                DisplayName = $"Synthetic question {questionIndex}",
                QuestionText = "Fixed synthetic question",
                PrimarySourceColumn = "F",
                SupportingSourceColumns = ["K", "L"],
                Points = questionPoints[questionIndex - 1],
                Evaluators = evaluatorBuilder.MoveToImmutable(),
            });
        }

        return new QuantificationDefinition
        {
            Id = $"SYN-DEF-{questions}-{evaluators}-{criteria}",
            Name = "Fixed-seed synthetic matrix",
            Revision = "1",
            SourceSheet = "Synthetic_Input",
            HeaderRow = 1,
            FirstDataRow = 2,
            LastDataRow = 531,
            RoundingDigits = 1,
            Questions = questionBuilder.MoveToImmutable(),
        };
    }

    private static QuantificationDefinition ApplySingleMutation(
        QuantificationDefinition source,
        string mutation)
    {
        QuestionDefinition question = source.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        CriterionDefinition criterion = evaluator.Criteria[0];
        return mutation switch
        {
            "blankQuestionId" => source with { Questions = [question with { Id = " " }] },
            "duplicateGlobalId" => source with
            {
                Questions = [question with { Evaluators = [evaluator with { Criteria = [criterion with { Id = question.Id }] }] }],
            },
            "equalRange" => source with
            {
                Questions = [question with { Evaluators = [evaluator with { Range = new ScoreRange(5m, 5m) }] }],
            },
            "reversedRange" => source with
            {
                Questions = [question with { Evaluators = [evaluator with { Range = new ScoreRange(5m, 1m) }] }],
            },
            "zeroWeight" => source with
            {
                Questions = [question with { Evaluators = [evaluator with { Weight = 0m }] }],
            },
            "negativeWeight" => source with
            {
                Questions = [question with { Evaluators = [evaluator with { Weight = -1m }] }],
            },
            "noEnabledQuestion" => source with { Questions = [question with { Enabled = false }] },
            "noEnabledEvaluator" => source with
            {
                Questions = [question with { Evaluators = [evaluator with { Enabled = false }] }],
            },
            "noEnabledCriterion" => source with
            {
                Questions = [question with { Evaluators = [evaluator with { Criteria = [criterion with { Enabled = false }] }] }],
            },
            "duplicateSupport" => source with { Questions = [question with { SupportingSourceColumns = ["K", "k"] }] },
            "supportEqualsPrimary" => source with { Questions = [question with { SupportingSourceColumns = [question.PrimarySourceColumn] }] },
            "rowLimit" => source with { FirstDataRow = 2, LastDataRow = 20_002 },
            _ => throw new InvalidOperationException($"Unknown fixed fixture mutation: {mutation}"),
        };
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "StudyReportEvaluator.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
