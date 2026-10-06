using StudyReportEvaluator.Core.Formulas;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Formulas;

// Requirements: FR-024 (AC-025)
public sealed class FormulaSerializerTests
{
    private readonly FormulaSerializer _serializer = new();

    [Fact]
    public void Effective_raw_formula_is_blank_safe_range_safe_and_override_first()
    {
        FormulaExpression formula = FormulaExpressions.EffectiveRaw(
            Ref("Results", "A", 2),
            Ref("Results", "B", 2),
            Ref("Results", "C", 2),
            Ref("Config", "D", 2, absolute: true),
            Ref("Config", "E", 2, absolute: true));

        string serialized = _serializer.Serialize(formula);

        Assert.Equal(
            "=IF(('Results'!A2<>1),\"\",IF(('Results'!C2=\"\"),IF(ISNUMBER('Results'!B2),IF(('Results'!B2<'Config'!$D$2),\"\",IF(('Results'!B2>'Config'!$E$2),\"\",'Results'!B2)),\"\"),IF(ISNUMBER('Results'!C2),IF(('Results'!C2<'Config'!$D$2),\"\",IF(('Results'!C2>'Config'!$E$2),\"\",'Results'!C2)),\"\")))",
            serialized);
        Assert.DoesNotContain("5", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalized_formula_uses_only_config_refs_and_blank_propagation()
    {
        string serialized = _serializer.Serialize(FormulaExpressions.Normalized(
            Ref("Results", "D", 2),
            Ref("Config", "A", 2, absolute: true),
            Ref("Config", "B", 2, absolute: true),
            Ref("Config", "C", 1, absolute: true)));

        Assert.Equal(
            "=IF(ISNUMBER('Results'!D2),IFERROR(ROUND(((('Results'!D2-'Config'!$A$2)/('Config'!$B$2-'Config'!$A$2))*100),'Config'!$C$1),\"\"),\"\")",
            serialized);
    }

    [Fact]
    public void Aggregate_uses_pairwise_score_weight_terms_and_count_blank_guard()
    {
        string serialized = _serializer.Serialize(FormulaExpressions.Aggregate(
            [
                new WeightedFormulaChild(Ref("Results", "A", 2), Ref("Config", "B", 2, true)),
                new WeightedFormulaChild(Ref("Results", "C", 2), Ref("Config", "D", 2, true)),
            ],
            Ref("Config", "E", 1, true)));

        Assert.Equal(
            "=IF((COUNT('Results'!A2,'Results'!C2)=2),IFERROR(ROUND((SUM(('Results'!A2*'Config'!$B$2),('Results'!C2*'Config'!$D$2))/SUM('Config'!$B$2,'Config'!$D$2)),'Config'!$E$1),\"\"),\"\")",
            serialized);
    }

    [Fact]
    public void Aggregate_excludes_disabled_children_from_count_numerator_and_denominator()
    {
        string serialized = _serializer.Serialize(FormulaExpressions.Aggregate(
            [
                new WeightedFormulaChild(Ref("Results", "A", 2), Ref("Config", "B", 2, true)),
                new WeightedFormulaChild(Ref("Results", "C", 2), Ref("Config", "D", 2, true), Enabled: false),
            ],
            Ref("Config", "E", 1, true)));

        Assert.Contains("COUNT('Results'!A2)=1", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("'Results'!C2", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("'Config'!$D$2", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Sheet_apostrophes_are_escaped_as_Excel_sheet_references()
    {
        string serialized = _serializer.Serialize(new FormulaCell(Ref("Run's Data", "a", 1)));

        Assert.Equal("='Run''s Data'!A1", serialized);
    }

    [Fact]
    public void V4_question_rate_and_earned_formulas_preserve_empty_zero_and_technical_blank()
    {
        string rate = _serializer.Serialize(FormulaExpressions.QuestionRate(
            Ref("Results", "A", 2),
            Ref("Results", "B", 2)));
        string earned = _serializer.Serialize(FormulaExpressions.QuestionEarned(
            Ref("Results", "C", 2),
            Ref("Config", "D", 2, true),
            Ref("Config", "E", 1, true)));

        Assert.Equal(
            "=IF(('Results'!A2=0),0,IF(ISNUMBER('Results'!B2),('Results'!B2/100),\"\"))",
            rate);
        Assert.Equal(
            "=IF(ISNUMBER('Results'!C2),ROUND(('Results'!C2*'Config'!$D$2),'Config'!$E$1),\"\")",
            earned);
    }

    [Fact]
    public void V4_special_formulas_require_numeric_completeness_and_handle_zero_budget()
    {
        string questionRate = _serializer.Serialize(FormulaExpressions.SpecialQuestionRate(
            [Ref("Results", "A", 2), Ref("Results", "B", 2)],
            Ref("Config", "C", 1, true),
            Ref("Config", "D", 1, true)));
        string earned = _serializer.Serialize(FormulaExpressions.SpecialEarned(
            [Ref("Results", "E", 2), Ref("Results", "F", 2)],
            Ref("Config", "C", 1, true),
            Ref("Config", "D", 1, true)));

        Assert.Equal(
            "=IF(('Config'!$C$1=0),\"\",IF((COUNT('Results'!A2,'Results'!B2)=2),ROUND((SUM('Results'!A2,'Results'!B2)/2),'Config'!$D$1),\"\"))",
            questionRate);
        Assert.Equal(
            "=IF(('Config'!$C$1=0),0,IF((COUNT('Results'!E2,'Results'!F2)=2),ROUND(('Config'!$C$1*(SUM('Results'!E2,'Results'!F2)/2)),'Config'!$D$1),\"\"))",
            earned);
    }

    [Fact]
    public void V4_similarity_final_raw_and_clamp_use_only_verified_cell_references()
    {
        string penalty = _serializer.Serialize(FormulaExpressions.SimilarityPenalty(
            Ref("Config", "A", 2, true),
            Ref("Results", "B", 2),
            Ref("Config", "C", 1, true),
            Ref("Config", "D", 1, true)));
        string finalRaw = _serializer.Serialize(FormulaExpressions.FinalRaw(
            Ref("Config", "E", 1, true),
            Ref("Config", "F", 1, true),
            [Ref("Results", "G", 2), Ref("Results", "H", 2)],
            Ref("Results", "I", 2),
            [Ref("Results", "J", 2), Ref("Results", "K", 2)],
            Ref("Config", "D", 1, true)));
        string finalScore = _serializer.Serialize(FormulaExpressions.FinalScore(
            Ref("Results", "L", 2)));

        Assert.Equal(
            "=IF(ISNUMBER('Results'!B2),ROUND((('Config'!$A$2*'Results'!B2)*'Config'!$C$1),'Config'!$D$1),\"\")",
            penalty);
        Assert.Contains("('Config'!$E$1<>1)", finalRaw, StringComparison.Ordinal);
        Assert.Contains("COUNT('Results'!G2,'Results'!H2,'Results'!I2,'Results'!J2,'Results'!K2)=5", finalRaw, StringComparison.Ordinal);
        Assert.Contains("('Config'!$F$1+SUM('Results'!G2,'Results'!H2))", finalRaw, StringComparison.Ordinal);
        Assert.Contains("-SUM('Results'!J2,'Results'!K2)", finalRaw, StringComparison.Ordinal);
        Assert.Equal(
            "=IF(ISNUMBER('Results'!L2),IF(('Results'!L2<0),0,IF(('Results'!L2>100),100,'Results'!L2)),\"\")",
            finalScore);
    }

    [Fact]
    public void V4_collection_formulas_reject_empty_or_mismatched_inputs()
    {
        FormulaCellReference config = Ref("Config", "A", 1, true);

        Assert.Throws<ArgumentException>(() => FormulaExpressions.SpecialQuestionRate([], config, config));
        Assert.Equal(
            "=IF(('Config'!$A$1=0),0,\"\")",
            _serializer.Serialize(FormulaExpressions.SpecialEarned([], config, config)));
        Assert.Throws<ArgumentException>(() => FormulaExpressions.FinalRaw(
            config,
            config,
            [Ref("Results", "A", 2)],
            Ref("Results", "B", 2),
            [],
            config));
    }

    private static FormulaCellReference Ref(
        string sheet,
        string column,
        int row,
        bool absolute = false) =>
        new(new FormulaCellAddress(sheet, column, row), absolute, absolute);
}
