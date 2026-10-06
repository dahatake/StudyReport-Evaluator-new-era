using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.Core.Prompting;

public static class DefaultCriterionDescriptions
{
    public const string Knowledge =
        "学生が作成したレポートを高度に分析・解析をしてそれぞれの{論点}について説明がされているかどうかの評価を行ってください。\n"
        + "論点:\n"
        + "- xxx\n"
        + "- xxx\n"
        + "- xxx";

    public const string PromptAnalysis =
        "学生が作成したPromptについて高度に分析・解析をして{論点}を導き出そうとしているかの評価を行ってください。\n"
        + "論点:\n"
        + "- xxx\n"
        + "- xxx\n"
        + "- xxx";

    public static string For(EvaluatorType type) => type switch
    {
        EvaluatorType.KnowledgeCoverage => Knowledge,
        EvaluatorType.CustomPrompt => PromptAnalysis,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Only Knowledge and Custom evaluator types are supported."),
    };
}
