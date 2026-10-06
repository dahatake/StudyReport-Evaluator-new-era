using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Prompting;

// Requirements: FR-011 (AC-012)
public sealed class PromptTemplateRendererTests
{
    private readonly PromptTemplateRenderer _renderer = new();
    private readonly PromptRenderContext _context = new(
        "QUESTION",
        "ANSWER",
        "SUPPORT",
        "CRITERIA",
        "1",
        "10");

    [Fact]
    public void Exactly_six_placeholders_are_allowed_and_rendered()
    {
        string result = _renderer.Render(
            "{設問}|{回答}|{補助情報}|{評価項目}|{最小点}|{最大点}",
            _context);

        Assert.Equal("QUESTION|ANSWER|SUPPORT|CRITERIA|1|10", result);
        Assert.Equal(6, PromptTemplateRenderer.Placeholders.Count);
    }

    [Fact]
    public void Literal_braces_are_escaped_without_counting_as_required_placeholders()
    {
        string result = _renderer.Render("{{literal}} {回答} {評価項目}", _context);

        Assert.Equal("{literal} ANSWER CRITERIA", result);
        PromptConfigurationException exception = Assert.Throws<PromptConfigurationException>(
            () => _renderer.Render("{{回答}} {評価項目}", _context));
        Assert.Equal("ANSWER_PLACEHOLDER_REQUIRED", exception.Code);
    }

    [Fact]
    public void Inserted_values_are_opaque_and_never_rescanned()
    {
        PromptRenderContext recursiveLike = _context with
        {
            Answer = "student text {設問} {{bad}} }",
            EvaluationCriteria = "criterion {回答}",
        };

        string result = _renderer.Render("A={回答}; C={評価項目}", recursiveLike);

        Assert.Equal("A=student text {設問} {{bad}} }; C=criterion {回答}", result);
    }

    [Theory]
    [InlineData("{未知} {回答} {評価項目}", "UNKNOWN_PLACEHOLDER")]
    [InlineData("{回答 {評価項目}", "MALFORMED_PLACEHOLDER")]
    [InlineData("{回答} {評価項目", "UNCLOSED_PLACEHOLDER")]
    [InlineData("{回答} {評価項目} }", "UNMATCHED_CLOSING_BRACE")]
    [InlineData("   ", "TEMPLATE_REQUIRED")]
    public void Unknown_unclosed_and_malformed_braces_fail_before_render(
        string template,
        string expectedCode)
    {
        PromptConfigurationException exception = Assert.Throws<PromptConfigurationException>(
            () => _renderer.Render(template, _context));

        Assert.Equal(expectedCode, exception.Code);
        Assert.DoesNotContain(template, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{評価項目}", "ANSWER_PLACEHOLDER_REQUIRED")]
    [InlineData("{回答}", "CRITERIA_PLACEHOLDER_REQUIRED")]
    public void Custom_required_placeholders_must_each_appear(string template, string expectedCode)
    {
        PromptConfigurationException exception = Assert.Throws<PromptConfigurationException>(
            () => _renderer.Render(template, _context));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void Special_prompt_requires_answer_but_does_not_require_criteria()
    {
        string result = _renderer.RenderSpecial("Evaluate {回答} for {設問}", _context);

        Assert.Equal("Evaluate ANSWER for QUESTION", result);
        PromptConfigurationException exception = Assert.Throws<PromptConfigurationException>(
            () => _renderer.RenderSpecial("Evaluate {設問}", _context));
        Assert.Equal("ANSWER_PLACEHOLDER_REQUIRED", exception.Code);
    }

    [Fact]
    public void Special_prompt_keeps_the_same_closed_placeholder_and_brace_rules()
    {
        Assert.Equal(
            "{literal} ANSWER",
            _renderer.RenderSpecial("{{literal}} {回答}", _context));
        PromptConfigurationException exception = Assert.Throws<PromptConfigurationException>(
            () => _renderer.RenderSpecial("{回答} {unknown}", _context));
        Assert.Equal("UNKNOWN_PLACEHOLDER", exception.Code);
    }
}
