using System.Collections.Immutable;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

// Requirements: FR-017 (AC-018), NFR-PERF-002 (AC-072)
public sealed class EvaluationRequestCapacityValidatorTests
{
    [Fact]
    public void Measures_actual_prompt_schema_and_tool_contract_without_claiming_token_usage()
    {
        SafeEvaluationPayload payload = Payload("A😀日");

        EvaluationRequestCapacityResult result = new EvaluationRequestCapacityValidator().Validate(
            payload,
            maximumPromptTokens: 64_000,
            maximumContextWindowTokens: 128_000);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.PromptUnicodeScalarCount);
        Assert.Equal(8, result.PromptUtf8ByteCount);
        Assert.True(result.SchemaUtf8ByteCount > 0);
        Assert.True(result.AppOwnedRequestUtf8ByteCount > result.PromptUtf8ByteCount + result.SchemaUtf8ByteCount);
        Assert.Equal(51_200, result.ModelContextBudget);
        Assert.Empty(result.Errors);
        Assert.Contains("<redacted>", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("A😀日", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void App_owned_request_over_the_sdk_limit_is_rejected_with_numeric_dimensions_only()
    {
        const string promptCanary = "PRIVATE-PROMPT-CANARY-日本語-😀";
        EvaluationRequestCapacityValidator validator = new();
        SafeEvaluationPayload payload = Payload(promptCanary);
        EvaluationRequestCapacityResult measured = validator.Validate(
            payload,
            maximumPromptTokens: int.MaxValue,
            maximumContextWindowTokens: int.MaxValue);
        int belowMeasuredBudget = checked((int)measured.AppOwnedRequestUtf8ByteCount - 1);
        int sdkLimit = checked((int)Math.Ceiling(belowMeasuredBudget / 0.8d));

        EvaluationRequestCapacityResult result = validator.Validate(
            payload,
            maximumPromptTokens: sdkLimit,
            maximumContextWindowTokens: sdkLimit + 10);

        EvaluationRequestCapacityError error = Assert.Single(result.Errors);
        Assert.False(result.IsValid);
        Assert.Equal("REQUEST_CONTEXT_BUDGET_EXCEEDED", error.Code);
        Assert.Equal("AppOwnedRequestUtf8Bytes", error.Field);
        Assert.Equal(measured.AppOwnedRequestUtf8ByteCount, error.ActualDimension);
        Assert.True(error.Limit < measured.AppOwnedRequestUtf8ByteCount);
        Assert.DoesNotContain(promptCanary, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_or_nonpositive_sdk_limits_are_not_invented()
    {
        EvaluationRequestCapacityValidator validator = new();
        SafeEvaluationPayload payload = Payload("synthetic");

        Assert.Throws<ArgumentOutOfRangeException>(() => validator.Validate(payload, 0, 128_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => validator.Validate(payload, 64_000, 0));
    }

    [Fact]
    public void Unpublished_sdk_limits_skip_the_model_budget_without_inventing_one()
    {
        EvaluationRequestCapacityValidator validator = new();
        SafeEvaluationPayload payload = Payload("synthetic");

        EvaluationRequestCapacityResult result = validator.Validate(
            payload,
            maximumPromptTokens: null,
            maximumContextWindowTokens: null);

        Assert.True(result.IsValid);
        Assert.Null(result.ModelContextBudget);
        Assert.True(result.AppOwnedRequestUtf8ByteCount > 0);
        Assert.Contains("unknown", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Unpublished_sdk_limits_still_enforce_the_model_independent_scalar_ceiling()
    {
        string prompt = string.Concat(
            Enumerable.Repeat("😀", EvaluationRequestCapacityValidator.MaximumRequestUnicodeScalars));

        EvaluationRequestCapacityResult result = new EvaluationRequestCapacityValidator().Validate(
            Payload(prompt),
            maximumPromptTokens: null,
            maximumContextWindowTokens: null);

        EvaluationRequestCapacityError error = Assert.Single(
            result.Errors,
            item => item.Code == "REQUEST_SCALAR_LIMIT_EXCEEDED");
        Assert.False(result.IsValid);
        Assert.Null(result.ModelContextBudget);
        Assert.Equal(EvaluationRequestCapacityValidator.MaximumRequestUnicodeScalars, error.Limit);
        Assert.DoesNotContain(
            result.Errors,
            item => item.Code == "REQUEST_CONTEXT_BUDGET_EXCEEDED");
    }

    [Fact]
    public void Request_scalar_limit_counts_unicode_scalars_not_utf16_code_units()
    {
        string prompt = string.Concat(
            Enumerable.Repeat("😀", EvaluationRequestCapacityValidator.MaximumRequestUnicodeScalars));

        EvaluationRequestCapacityResult result = new EvaluationRequestCapacityValidator().Validate(
            Payload(prompt),
            maximumPromptTokens: int.MaxValue,
            maximumContextWindowTokens: int.MaxValue);

        EvaluationRequestCapacityError error = Assert.Single(
            result.Errors,
            item => item.Code == "REQUEST_SCALAR_LIMIT_EXCEEDED");
        Assert.True(error.ActualDimension > EvaluationRequestCapacityValidator.MaximumRequestUnicodeScalars);
        Assert.Equal(EvaluationRequestCapacityValidator.MaximumRequestUnicodeScalars, error.Limit);
        Assert.Equal(EvaluationRequestCapacityValidator.MaximumRequestUnicodeScalars, result.PromptUnicodeScalarCount);
        Assert.Equal(EvaluationRequestCapacityValidator.MaximumRequestUnicodeScalars * 4L, result.PromptUtf8ByteCount);
    }

    private static SafeEvaluationPayload Payload(string prompt) =>
        new(
            "Q1",
            "E1",
            prompt,
            new EvaluationSourceCell(EvaluationSourceKind.PrimaryAnswer, "A", "synthetic"),
            ImmutableArray<EvaluationSourceCell>.Empty,
            [new ExpectedCriterion("C1", "Criterion", new ScoreRange(0m, 10m))]);
}
