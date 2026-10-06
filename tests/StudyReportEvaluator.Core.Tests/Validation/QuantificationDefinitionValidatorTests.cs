using System.Collections.Immutable;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Validation;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Validation;

// Requirements: FR-008 (AC-009), FR-012 (AC-013), FR-020 (AC-021)
public sealed class QuantificationDefinitionValidatorTests
{
    private readonly QuantificationDefinitionValidator _validator = new();

    [Fact]
    public void Valid_dynamic_definition_is_accepted()
    {
        DefinitionValidationResult result = _validator.Validate(C02TestDefinitions.CreateValid());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Duplicate_and_blank_ids_are_rejected_with_exact_identity()
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuantificationDefinition definition = source with
        {
            Questions =
            [
                source.Questions[0],
                source.Questions[1] with
                {
                    Id = " ",
                    Evaluators =
                    [
                        source.Questions[1].Evaluators[0] with { Id = "C1" },
                    ],
                },
            ],
        };

        DefinitionValidationResult result = _validator.Validate(definition);

        Assert.Contains(result.Errors, error => error.Code == "REQUIRED" && error.NodeKind == "Question" && error.Field == "Id");
        DefinitionValidationError duplicate = Assert.Single(result.Errors, error => error.Code == "DUPLICATE_ID");
        Assert.Equal("Evaluator", duplicate.NodeKind);
        Assert.Equal("C1", duplicate.NodeId);
        Assert.Equal("Id", duplicate.Field);
    }

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(2, 20001, true)]
    [InlineData(2, 20002, false)]
    [InlineData(3, 2, false)]
    public void Selected_row_boundaries_are_enforced(int firstRow, int lastRow, bool expectedValid)
    {
        QuantificationDefinition definition = C02TestDefinitions.CreateValid() with
        {
            HeaderRow = 1,
            FirstDataRow = firstRow,
            LastDataRow = lastRow,
        };

        DefinitionValidationResult result = _validator.Validate(definition);

        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            Assert.Contains(result.Errors, error => error.Code is "SELECTED_ROW_LIMIT_EXCEEDED" or "DATA_ROW_RANGE_REVERSED");
        }
    }

    [Fact]
    public void Data_rows_must_follow_the_header_and_fit_Excel()
    {
        QuantificationDefinition definition = C02TestDefinitions.CreateValid() with
        {
            HeaderRow = 2,
            FirstDataRow = 2,
            LastDataRow = 3,
        };
        QuantificationDefinition rowOverflow = C02TestDefinitions.CreateValid() with
        {
            LastDataRow = QuantificationDefinitionValidator.MaximumExcelRow + 1,
        };

        DefinitionValidationResult result = _validator.Validate(definition);
        DefinitionValidationResult overflowResult = _validator.Validate(rowOverflow);

        Assert.Contains(result.Errors, error => error.Code == "DATA_ROW_NOT_AFTER_HEADER" && error.Field == "FirstDataRow");
        Assert.Contains(overflowResult.Errors, error => error.Code == "ROW_OUT_OF_RANGE" && error.Field == "LastDataRow");
    }

    [Fact]
    public void Columns_must_be_valid_unique_and_different_from_primary()
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuantificationDefinition definition = source with
        {
            Questions =
            [
                source.Questions[0] with
                {
                    PrimarySourceColumn = "XFE",
                    SupportingSourceColumns = ["G", "g", "XFE", "1", ""],
                },
            ],
        };

        DefinitionValidationResult result = _validator.Validate(definition);

        Assert.Contains(result.Errors, error => error.Code == "INVALID_SOURCE_COLUMN" && error.Field == "PrimarySourceColumn");
        Assert.Contains(result.Errors, error => error.Code == "DUPLICATE_SUPPORTING_COLUMN");
        Assert.Contains(result.Errors, error => error.Code == "PRIMARY_COLUMN_REUSED");
        Assert.Contains(result.Errors, error => error.Code == "INVALID_SOURCE_COLUMN" && error.SafeOffendingValue == "1");
        Assert.Contains(result.Errors, error => error.Code == "SOURCE_COLUMN_REQUIRED");
    }

    [Theory]
    [InlineData("A", true)]
    [InlineData("z", true)]
    [InlineData("AA", true)]
    [InlineData("XFD", true)]
    [InlineData("XFE", false)]
    [InlineData("A1", false)]
    public void Primary_column_accepts_the_full_Excel_column_boundary(string column, bool expectedValid)
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuantificationDefinition definition = source with
        {
            Questions = [source.Questions[0] with { PrimarySourceColumn = column, SupportingSourceColumns = [] }],
        };

        DefinitionValidationResult result = _validator.Validate(definition);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Equal_reversed_ranges_negative_points_and_nonpositive_internal_weights_are_rejected()
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuestionDefinition question = source.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        QuantificationDefinition definition = source with
        {
            Questions =
            [
                question with
                {
                    Points = -1m,
                    Evaluators =
                    [
                        evaluator with
                        {
                            Weight = -1m,
                            Range = new ScoreRange(10m, 10m),
                            Criteria =
                            [
                                evaluator.Criteria[0] with
                                {
                                    Weight = 0m,
                                    Range = new ScoreRange(5m, 1m),
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        DefinitionValidationResult result = _validator.Validate(definition);

        Assert.Equal(2, result.Errors.Count(error => error.Code == "WEIGHT_MUST_BE_POSITIVE"));
        Assert.Equal(2, result.Errors.Count(error => error.Code == "SCORE_RANGE_INVALID"));
        Assert.Contains(result.Errors, error => error.Code == "QUESTION_POINTS_OUT_OF_RANGE" && error.NodeId == "Q1" && error.Field == "Points");
        Assert.Contains(result.Errors, error => error.NodeId == "E1" && error.Field == "Range");
        Assert.Contains(result.Errors, error => error.NodeId == "C1" && error.Field == "Range");
    }

    [Fact]
    public void Executable_snapshot_requires_an_enabled_child_at_every_level()
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuestionDefinition question = source.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];

        DefinitionValidationResult noQuestions = _validator.Validate(source with
        {
            Questions = source.Questions.Select(item => item with { Enabled = false }).ToImmutableArray(),
        });
        DefinitionValidationResult noEvaluators = _validator.Validate(source with
        {
            Questions = [question with { Evaluators = question.Evaluators.Select(item => item with { Enabled = false }).ToImmutableArray() }],
        });
        DefinitionValidationResult noCriteria = _validator.Validate(source with
        {
            Questions =
            [
                question with
                {
                    Evaluators = [evaluator with { Criteria = evaluator.Criteria.Select(item => item with { Enabled = false }).ToImmutableArray() }],
                },
            ],
        });

        Assert.Contains(noQuestions.Errors, error => error.Code == "ENABLED_QUESTION_REQUIRED");
        Assert.Contains(noEvaluators.Errors, error => error.Code == "ENABLED_EVALUATOR_REQUIRED" && error.NodeId == "Q1");
        Assert.Contains(noCriteria.Errors, error => error.Code == "ENABLED_CRITERION_REQUIRED" && error.NodeId == "E1");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void Rounding_digits_outside_zero_through_six_are_rejected(int roundingDigits)
    {
        DefinitionValidationResult result = _validator.Validate(C02TestDefinitions.CreateValid() with { RoundingDigits = roundingDigits });

        DefinitionValidationError error = Assert.Single(result.Errors, item => item.Code == "ROUNDING_OUT_OF_RANGE");
        Assert.Equal("RoundingDigits", error.Field);
        Assert.Equal(roundingDigits.ToString(System.Globalization.CultureInfo.InvariantCulture), error.SafeOffendingValue);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(3, false)]
    public void Question_text_row_is_limited_to_one_or_two(int headerRow, bool expectedValid)
    {
        QuantificationDefinition definition = C02TestDefinitions.CreateValid() with
        {
            HeaderRow = headerRow,
            FirstDataRow = Math.Max(2, headerRow + 1),
        };

        DefinitionValidationResult result = _validator.Validate(definition);

        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            Assert.Contains(result.Errors, error => error.Code == "QUESTION_TEXT_ROW_INVALID");
        }
    }

    [Theory]
    [InlineData("BasePoints", "-0.1", "BASE_POINTS_OUT_OF_RANGE")]
    [InlineData("BasePoints", "100.1", "BASE_POINTS_OUT_OF_RANGE")]
    [InlineData("SpecialPoints", "-0.1", "SPECIAL_POINTS_OUT_OF_RANGE")]
    [InlineData("SpecialPoints", "100.1", "SPECIAL_POINTS_OUT_OF_RANGE")]
    [InlineData("SimilarityPenaltyWeight", "-0.1", "SIMILARITY_WEIGHT_OUT_OF_RANGE")]
    [InlineData("SimilarityPenaltyWeight", "1.1", "SIMILARITY_WEIGHT_OUT_OF_RANGE")]
    public void Root_allocation_values_enforce_their_inclusive_ranges(
        string field,
        string valueText,
        string expectedCode)
    {
        decimal value = decimal.Parse(valueText, System.Globalization.CultureInfo.InvariantCulture);
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuantificationDefinition definition = field switch
        {
            "BasePoints" => source with { BasePoints = value },
            "SpecialPoints" => source with { SpecialPoints = value },
            "SimilarityPenaltyWeight" => source with { SimilarityPenaltyWeight = value },
            _ => throw new InvalidOperationException(),
        };

        DefinitionValidationResult result = _validator.Validate(definition);

        Assert.Contains(result.Errors, error => error.Code == expectedCode && error.Field == field);
    }

    [Fact]
    public void Allocation_must_equal_exactly_100_and_zero_question_points_are_allowed()
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        DefinitionValidationResult invalid = _validator.Validate(source with
        {
            Questions = [source.Questions[0] with { Points = 39.999999m }, source.Questions[1]],
        });
        DefinitionValidationResult validZero = _validator.Validate(source with
        {
            BasePoints = 100m,
            Questions = [source.Questions[0] with { Points = 0m }, source.Questions[1]],
        });

        DefinitionValidationError error = Assert.Single(
            invalid.Errors,
            item => item.Code == "ALLOCATION_TOTAL_INVALID");
        Assert.Equal("99.999999", error.SafeOffendingValue);
        Assert.True(validZero.IsValid);
    }

    [Fact]
    public void Positive_special_budget_requires_an_enabled_special_item()
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuantificationDefinition missing = source with
        {
            SpecialPoints = 10m,
            Questions = [source.Questions[0] with { Points = 30m }, source.Questions[1]],
        };
        SpecialEvaluationDefinition special = CreateValidSpecial();
        QuantificationDefinition present = missing with
        {
            Questions = [missing.Questions[0] with { SpecialEvaluations = [special] }, missing.Questions[1]],
        };

        DefinitionValidationResult missingResult = _validator.Validate(missing);
        DefinitionValidationResult presentResult = _validator.Validate(present);

        Assert.Contains(missingResult.Errors, error => error.Code == "SPECIAL_ITEMS_REQUIRED");
        Assert.True(presentResult.IsValid);
    }

    [Fact]
    public void Special_definition_validates_identity_columns_and_answer_placeholder_without_requiring_criteria()
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        SpecialEvaluationDefinition valid = CreateValidSpecial();
        DefinitionValidationResult validResult = _validator.Validate(source with
        {
            SpecialPoints = 10m,
            Questions =
            [
                source.Questions[0] with { Points = 30m, SpecialEvaluations = [valid] },
                source.Questions[1],
            ],
        });
        DefinitionValidationResult invalidResult = _validator.Validate(source with
        {
            Questions =
            [
                source.Questions[0] with
                {
                    SpecialEvaluations =
                    [
                        valid with
                        {
                            Id = source.Questions[0].Id,
                            PrimarySourceColumn = "XFE",
                            SupportingSourceColumns = ["G", "g", "XFE"],
                            PromptTemplate = "{未知}",
                        },
                    ],
                },
                source.Questions[1],
            ],
        });

        Assert.True(validResult.IsValid);
        Assert.Contains(invalidResult.Errors, error => error.Code == "DUPLICATE_ID" && error.NodeKind == "SpecialEvaluation");
        Assert.Contains(invalidResult.Errors, error => error.Code == "INVALID_SOURCE_COLUMN" && error.NodeKind == "SpecialEvaluation");
        Assert.Contains(invalidResult.Errors, error => error.Code == "DUPLICATE_SUPPORTING_COLUMN" && error.NodeKind == "SpecialEvaluation");
        Assert.Contains(invalidResult.Errors, error => error.Code == "PRIMARY_COLUMN_REUSED" && error.NodeKind == "SpecialEvaluation");
        Assert.Contains(invalidResult.Errors, error => error.Code == "UNKNOWN_PLACEHOLDER" && error.NodeKind == "SpecialEvaluation");
    }

    [Theory]
    [InlineData("{回答} {評価項目", "UNCLOSED_PLACEHOLDER")]
    [InlineData("{回答 {評価項目}", "MALFORMED_PLACEHOLDER")]
    [InlineData("{回答} {未知}", "UNKNOWN_PLACEHOLDER")]
    [InlineData("{回答} {評価項目} }", "UNMATCHED_CLOSING_BRACE")]
    [InlineData("{{回答}} {評価項目}", "ANSWER_PLACEHOLDER_REQUIRED")]
    [InlineData("{回答} {{評価項目}}", "CRITERIA_PLACEHOLDER_REQUIRED")]
    public void Custom_prompt_syntax_is_rejected_by_snapshot_validation(
        string template,
        string expectedCode)
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuestionDefinition question = source.Questions[0];
        EvaluatorDefinition custom = question.Evaluators.Single(
            evaluator => evaluator.Type == EvaluatorType.CustomPrompt);
        QuantificationDefinition definition = source with
        {
            Questions =
            [
                question with
                {
                    Evaluators = question.Evaluators
                        .Select(evaluator => evaluator.Id == custom.Id
                            ? evaluator with { CustomPromptTemplate = template }
                            : evaluator)
                        .ToImmutableArray(),
                },
            ],
        };

        DefinitionValidationResult result = _validator.Validate(definition);

        DefinitionValidationError error = Assert.Single(
            result.Errors,
            item => item.Code == expectedCode);
        Assert.Equal(custom.Id, error.NodeId);
        Assert.Equal("CustomPromptTemplate", error.Field);
        Assert.DoesNotContain(template, error.ToString(), StringComparison.Ordinal);
        Assert.Throws<QuantificationDefinitionValidationException>(
            () => QuantificationSnapshot.Create(definition));
    }

    [Fact]
    public void Evaluator_type_specific_prompt_ownership_is_validated_in_core()
    {
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuestionDefinition question = source.Questions[0];
        EvaluatorDefinition knowledge = question.Evaluators.Single(
            evaluator => evaluator.Type == EvaluatorType.KnowledgeCoverage);
        EvaluatorDefinition custom = question.Evaluators.Single(
            evaluator => evaluator.Type == EvaluatorType.CustomPrompt);
        QuantificationDefinition definition = source with
        {
            Questions =
            [
                question with
                {
                    Evaluators =
                    [
                        knowledge with
                        {
                            BuiltInTemplateVersion = "unsupported",
                            CustomPromptTemplate = "PRIVATE-KNOWLEDGE-PROMPT-CANARY",
                        },
                        custom with { BuiltInTemplateVersion = "knowledge-v1" },
                    ],
                },
            ],
        };

        DefinitionValidationResult result = _validator.Validate(definition);

        Assert.Contains(result.Errors, error => error.Code == "KNOWLEDGE_TEMPLATE_VERSION_INVALID");
        Assert.Contains(result.Errors, error => error.Code == "KNOWLEDGE_CUSTOM_TEMPLATE_FORBIDDEN");
        Assert.Contains(result.Errors, error => error.Code == "CUSTOM_BUILT_IN_TEMPLATE_FORBIDDEN");
        Assert.DoesNotContain(
            "PRIVATE-KNOWLEDGE-PROMPT-CANARY",
            string.Concat(result.Errors),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("QuestionText")]
    [InlineData("CustomPromptTemplate")]
    [InlineData("Description")]
    public void Excel_cell_text_limit_is_rejected_before_snapshot_creation(string field)
    {
        string oversized = new('X', QuantificationDefinitionValidator.MaximumCellCharacters + 1);
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuestionDefinition question = source.Questions[0];
        EvaluatorDefinition custom = question.Evaluators.Single(
            evaluator => evaluator.Type == EvaluatorType.CustomPrompt);
        QuantificationDefinition definition = source with
        {
            Questions =
            [
                question with
                {
                    QuestionText = field == "QuestionText" ? oversized : question.QuestionText,
                    Evaluators = question.Evaluators
                        .Select(evaluator => evaluator.Id == custom.Id
                            ? evaluator with
                            {
                                CustomPromptTemplate = field == "CustomPromptTemplate"
                                    ? oversized
                                    : evaluator.CustomPromptTemplate,
                                Criteria = evaluator.Criteria
                                    .Select(criterion => criterion with
                                    {
                                        Description = field == "Description"
                                            ? oversized
                                            : criterion.Description,
                                    })
                                    .ToImmutableArray(),
                            }
                            : evaluator)
                        .ToImmutableArray(),
                },
            ],
        };

        DefinitionValidationResult result = _validator.Validate(definition);

        DefinitionValidationError error = Assert.Single(
            result.Errors,
            item => item.Code == "CELL_TEXT_LIMIT_EXCEEDED" && item.Field == field);
        Assert.Equal(
            oversized.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            error.SafeOffendingValue);
        Assert.DoesNotContain(oversized, error.ToString(), StringComparison.Ordinal);
        Assert.Throws<QuantificationDefinitionValidationException>(
            () => QuantificationSnapshot.Create(definition));
    }

    [Fact]
    public void Validation_error_never_echoes_question_or_prompt_bodies()
    {
        const string questionCanary = "QUESTION-BODY-MUST-NOT-APPEAR";
        const string promptCanary = "PROMPT-BODY-MUST-NOT-APPEAR";
        QuantificationDefinition source = C02TestDefinitions.CreateValid();
        QuantificationDefinition definition = source with
        {
            Questions =
            [
                source.Questions[0] with
                {
                    QuestionText = questionCanary,
                    Points = -1m,
                    Evaluators =
                    [
                        source.Questions[0].Evaluators[1] with
                        {
                            CustomPromptTemplate = promptCanary,
                            Range = new ScoreRange(2m, 1m),
                        },
                    ],
                },
            ],
        };

        DefinitionValidationResult result = _validator.Validate(definition);
        string renderedErrors = string.Join("|", result.Errors.Select(error => error.ToString()));

        Assert.DoesNotContain(questionCanary, renderedErrors, StringComparison.Ordinal);
        Assert.DoesNotContain(promptCanary, renderedErrors, StringComparison.Ordinal);
    }

    private static SpecialEvaluationDefinition CreateValidSpecial() => new()
    {
        Id = "S1",
        DisplayName = "Student prompt quality",
        PrimarySourceColumn = "G",
        SupportingSourceColumns = ["K"],
        PromptTemplate = "Evaluate {回答}",
    };
}

internal static class C02TestDefinitions
{
    internal static QuantificationDefinition CreateValid()
    {
        QuantificationDefinition source = Domain.TestDefinitions.Create();
        QuestionDefinition second = source.Questions[1] with
        {
            Evaluators = source.Questions[1].Evaluators
                .Select(evaluator => evaluator with
                {
                    Id = $"{evaluator.Id}-Q2",
                    Criteria = evaluator.Criteria
                        .Select(criterion => criterion with { Id = $"{criterion.Id}-Q2" })
                        .ToImmutableArray(),
                })
                .ToImmutableArray(),
        };

        return source with { Questions = [source.Questions[0], second] };
    }
}
