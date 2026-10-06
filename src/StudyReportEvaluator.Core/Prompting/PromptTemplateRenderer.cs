using System.Collections.Frozen;
using System.Text;

namespace StudyReportEvaluator.Core.Prompting;

public sealed class PromptTemplateRenderer
{
    private static readonly FrozenSet<string> AllowedPlaceholders = new[]
    {
        "設問",
        "回答",
        "補助情報",
        "評価項目",
        "最小点",
        "最大点",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> Placeholders => AllowedPlaceholders;

    public string Render(string template, PromptRenderContext context) =>
        RenderCore(template, context, requireCriteria: true);

    public string RenderSpecial(string template, PromptRenderContext context) =>
        RenderCore(template, context, requireCriteria: false);

    private static string RenderCore(
        string template,
        PromptRenderContext context,
        bool requireCriteria)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new PromptConfigurationException("TEMPLATE_REQUIRED", "The Prompt template must not be blank.");
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            ["設問"] = context.Question,
            ["回答"] = context.Answer,
            ["補助情報"] = context.SupportingInformation,
            ["評価項目"] = context.EvaluationCriteria,
            ["最小点"] = context.MinimumScore,
            ["最大点"] = context.MaximumScore,
        };
        HashSet<string> usedPlaceholders = new(StringComparer.Ordinal);
        StringBuilder output = new(template.Length + 256);

        for (int position = 0; position < template.Length; position++)
        {
            char current = template[position];
            if (current == '{')
            {
                if (position + 1 < template.Length && template[position + 1] == '{')
                {
                    output.Append('{');
                    position++;
                    continue;
                }

                int closePosition = template.IndexOf('}', position + 1);
                if (closePosition < 0)
                {
                    throw At("UNCLOSED_PLACEHOLDER", "A Prompt placeholder is not closed.", position);
                }

                int nestedOpen = template.IndexOf('{', position + 1, closePosition - position - 1);
                if (nestedOpen >= 0)
                {
                    throw At("MALFORMED_PLACEHOLDER", "Nested opening braces are not allowed in a Prompt placeholder.", nestedOpen);
                }

                string placeholder = template[(position + 1)..closePosition];
                if (!AllowedPlaceholders.Contains(placeholder))
                {
                    throw At("UNKNOWN_PLACEHOLDER", "The Prompt template contains an unknown or empty placeholder.", position);
                }

                output.Append(values[placeholder]);
                usedPlaceholders.Add(placeholder);
                position = closePosition;
                continue;
            }

            if (current == '}')
            {
                if (position + 1 < template.Length && template[position + 1] == '}')
                {
                    output.Append('}');
                    position++;
                    continue;
                }

                throw At("UNMATCHED_CLOSING_BRACE", "A closing brace must be escaped as }}.", position);
            }

            output.Append(current);
        }

        if (!usedPlaceholders.Contains("回答"))
        {
            throw new PromptConfigurationException("ANSWER_PLACEHOLDER_REQUIRED", "The Prompt template must contain the answer placeholder.");
        }

        if (requireCriteria && !usedPlaceholders.Contains("評価項目"))
        {
            throw new PromptConfigurationException("CRITERIA_PLACEHOLDER_REQUIRED", "The Prompt template must contain the evaluation-criteria placeholder.");
        }

        return output.ToString();
    }

    private static PromptConfigurationException At(string code, string message, int position) =>
        new(code, message, position);
}
