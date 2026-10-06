namespace StudyReportEvaluator.Core.Prompting;

public static class BuiltInPromptTemplates
{
    public const string KnowledgeTemplateVersion = "knowledge-v1";

    public const string KnowledgeSemanticTemplate = """
        次の設問への回答について、指定された各知識ポイントが回答内でどの程度説明されているかを評価してください。
        単語が存在するだけで満点にせず、内容上の説明、他の概念との関係、具体的な適用が確認できる程度を評価してください。
        各評価項目について指定range内の数値、短い理由、回答内の根拠を返してください。

        ### 設問
        {設問}

        ### 評価対象回答
        {回答}

        ### 補助情報
        {補助情報}

        ### 知識ポイントと評価項目
        {評価項目}
        """;

    public const string CustomPromptPreset = """
        次の設問に対して作成されたPromptを分析してください。
        評価項目ごとに、そのPromptが必要な視点を引き出せる具体性、論理性、実行可能性を指定range内で数値化してください。
        補助情報にPrompt作成時の工夫・観点・論点がある場合は、それを重要な評価情報として使い、実際のPromptへ反映されているかを評価してください。
        各評価項目について短い理由と、評価対象回答または補助情報内の根拠を返してください。

        ### 設問
        {設問}

        ### 評価対象Prompt
        {回答}

        ### 補助情報
        {補助情報}

        ### 評価項目
        {評価項目}
        """;

    public const string StructuredOutputInstruction = """
        [APP-OWNED STRUCTURED OUTPUT CONTRACT]
        Call the submit_quantification tool exactly once and do not substitute normal assistant text.
        Return only the requested evaluator ID and every expected criterion exactly once.
        For each criterion return only its raw score, a short reason, exact contiguous evidence, evidence source kind, and source column ID.
        Never return an evaluator score, question score, overall score, weight, pass/fail decision, or additional field.
        Evidence must come from the identified same-row primary or supporting source. If no evidence exists, return an empty evidence string, NONE, and an empty source column ID.
        """;

    public const string ReferenceAnswerTemplate = """
        次の設問に、設問文だけを根拠として回答してください。
        学生の回答、採点情報、過去の回答は使用しないでください。
        比較用の一つの回答本文だけを返してください。

        ### 設問
        {設問}

        [APP-OWNED REFERENCE OUTPUT CONTRACT]
        Call the submit_reference_answer tool exactly once and do not substitute normal assistant text.
        Return only the expected question ID and one non-empty reference answer.
        """;

    public const string SpecialOutputInstruction = """
        [APP-OWNED SPECIAL OUTPUT CONTRACT]
        Evaluate only the supplied same-row sources. Zero means the criterion is not met and one means it is fully met.
        Call the submit_special_quantification tool exactly once and do not substitute normal assistant text.
        Return only the expected special evaluation ID, one finite score from 0 through 1, a short reason, exact contiguous evidence, evidence source kind, and source column ID.
        Do not return question points, an aggregate, a final score, pass/fail, misconduct, or an additional field.
        """;

    public static string GetKnowledgeTemplate(string? version)
    {
        if (!string.Equals(version, KnowledgeTemplateVersion, StringComparison.Ordinal))
        {
            throw new PromptConfigurationException(
                "UNSUPPORTED_KNOWLEDGE_TEMPLATE_VERSION",
                "The configured Knowledge template version is not supported.");
        }

        return KnowledgeSemanticTemplate;
    }
}
