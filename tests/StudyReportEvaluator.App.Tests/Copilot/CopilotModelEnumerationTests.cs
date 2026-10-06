using GitHub.Copilot;
using StudyReportEvaluator.App.Copilot;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

// Requirements: FR-015 (AC-016)
public sealed class CopilotModelEnumerationTests
{
    [Theory]
    [InlineData(272_000L, 1_000_000L, 272_000, 1_000_000)]
    [InlineData(null, 1_000_000L, 100_000, 1_000_000)]
    [InlineData(0L, 1_000_000L, 100_000, 1_000_000)]
    [InlineData(272_000L, 272_000L, 272_000, null)]
    [InlineData(272_000L, -1L, 272_000, null)]
    [InlineData(272_000L, 2_147_483_648L, 272_000, null)]
    public void Tier_budgets_come_from_pinned_sdk_metadata_without_inventing_capacities(
        long? standard, long? extended, int expectedStandard, int? expectedExtended)
    {
        ModelInfo model = Model("fresh-model", prompt: 100_000, context: 400_000);
#pragma warning disable GHCP001
        model.Billing = new ModelBilling
        {
            TokenPrices = new GitHub.Copilot.Rpc.ModelBillingTokenPrices
            {
                MaxPromptTokens = standard,
                LongContext = new GitHub.Copilot.Rpc.ModelBillingTokenPricesLongContext { MaxPromptTokens = extended },
            },
        };
#pragma warning restore GHCP001
        CopilotModelAvailability result = Assert.Single(SdkCopilotAuthenticationRuntime.MapAvailableModels([model]));
        Assert.Equal(expectedStandard, result.MaximumPromptTokens);
        Assert.Equal(expectedExtended, result.LongContextPromptTokens);
        Assert.Equal(400_000, result.MaximumContextWindowTokens);
    }

    [Fact]
    public void Legacy_budget_metadata_is_used_only_when_new_budget_is_absent_and_false_support_flag_wins()
    {
        ModelInfo model = Model("fresh-model", efforts: ["low", "high"]);
        model.Capabilities.Supports.ReasoningEffort = false;
#pragma warning disable GHCP001
        model.Billing = new ModelBilling
        {
            TokenPrices = new GitHub.Copilot.Rpc.ModelBillingTokenPrices
            {
                ContextMax = 272_000,
                LongContext = new GitHub.Copilot.Rpc.ModelBillingTokenPricesLongContext { ContextMax = 1_000_000 },
            },
        };
#pragma warning restore GHCP001
        CopilotModelAvailability result = Assert.Single(SdkCopilotAuthenticationRuntime.MapAvailableModels([model]));
        Assert.False(result.SupportsReasoningEffort);
        Assert.Equal(272_000, result.MaximumPromptTokens);
        Assert.Equal(1_000_000, result.LongContextPromptTokens);
    }

    [Fact]
    public void Every_enumerated_model_is_selectable_in_sdk_order_including_auto_and_unknown_policy()
    {
        ModelInfo[] enumerated =
        [
            Model("auto"),
            Model("claude-sonnet-5", "enabled", 936_000, 1_000_000, ["low", "high"], "low"),
            Model("gpt-5.3-codex", policyState: null, prompt: 272_000),
            Model("brand-new-model", "unconfigured"),
            Model("gpt-5.6-sol-fast", "ENABLED"),
        ];

        IReadOnlyList<CopilotModelAvailability> available = SdkCopilotAuthenticationRuntime.MapAvailableModels(enumerated);

        Assert.Equal(
            ["auto", "claude-sonnet-5", "gpt-5.3-codex", "brand-new-model", "gpt-5.6-sol-fast"],
            available.Select(model => model.Id));
        CopilotModelAvailability sonnet = available[1];
        Assert.Equal(936_000, sonnet.MaximumPromptTokens);
        Assert.Equal(1_000_000, sonnet.MaximumContextWindowTokens);
        Assert.Equal(["low", "high"], sonnet.SupportedReasoningEfforts);
        Assert.Equal("low", sonnet.DefaultReasoningEffort);
        Assert.Null(available[0].MaximumPromptTokens);
    }

    [Fact]
    public void Large_catalogs_are_not_truncated()
    {
        ModelInfo[] enumerated = [.. Enumerable.Range(0, 1000).Select(index => Model($"model-{index}", "enabled"))];

        IReadOnlyList<CopilotModelAvailability> available = SdkCopilotAuthenticationRuntime.MapAvailableModels(enumerated);

        Assert.Equal(enumerated.Select(model => model.Id), available.Select(model => model.Id));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("Disabled")]
    public void Models_disabled_by_policy_are_not_offered_and_do_not_hide_the_others(string state)
    {
        IReadOnlyList<CopilotModelAvailability> available = SdkCopilotAuthenticationRuntime.MapAvailableModels(
            [Model("model-a", "enabled"), Model("model-b", state), Model("model-c", "enabled")]);

        Assert.Equal(["model-a", "model-c"], available.Select(model => model.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" model-x")]
    [InlineData("model-x ")]
    [InlineData("model\u0007x")]
    public void One_malformed_model_id_is_skipped_without_failing_the_whole_enumeration(string? badId)
    {
        ModelInfo[] enumerated = [Model("model-a"), new ModelInfo { Id = badId! }, Model("model-b")];

        IReadOnlyList<CopilotModelAvailability> available = SdkCopilotAuthenticationRuntime.MapAvailableModels(enumerated);

        Assert.Equal(["model-a", "model-b"], available.Select(model => model.Id));
    }

    [Fact]
    public void Over_long_model_id_is_skipped_and_the_boundary_length_is_kept()
    {
        IReadOnlyList<CopilotModelAvailability> available = SdkCopilotAuthenticationRuntime.MapAvailableModels(
            [Model(new string('m', 257)), Model(new string('n', 256))]);

        Assert.Equal([new string('n', 256)], available.Select(model => model.Id));
    }

    [Fact]
    public void Null_or_empty_enumeration_yields_an_empty_list()
    {
        Assert.Empty(SdkCopilotAuthenticationRuntime.MapAvailableModels(null));
        Assert.Empty(SdkCopilotAuthenticationRuntime.MapAvailableModels([]));
    }

    private static ModelInfo Model(
        string id,
        string? policyState = "enabled",
        int? prompt = null,
        int? context = null,
        string[]? efforts = null,
        string? defaultEffort = null) =>
        new()
        {
            Id = id,
            Policy = policyState is null ? null : new ModelPolicy { State = policyState },
            Capabilities = new ModelCapabilities
            {
                Limits = new ModelLimits { MaxPromptTokens = prompt ?? 0, MaxContextWindowTokens = context ?? 0 },
                Supports = new ModelSupports { ReasoningEffort = efforts is { Length: > 0 } },
            },
            SupportedReasoningEfforts = efforts is null ? null : [.. efforts],
            DefaultReasoningEffort = defaultEffort,
        };
}
