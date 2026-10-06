using System.Diagnostics;
using StudyReportEvaluator.Core.Similarity;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Similarity;

// Requirements: FR-021 (AC-022)
public sealed class SurfaceTextSimilarityCalculatorTests
{
    private readonly SurfaceTextSimilarityCalculator calculator = new();

    [Fact]
    public void Identical_text_scores_one()
    {
        SurfaceSimilarityResult result = calculator.Calculate("生成AIの利用では透明性が重要です。", "生成AIの利用では透明性が重要です。");

        Assert.Equal(1m, result.Score);
        Assert.Equal(1m, result.NGramContainment);
        Assert.Equal(1m, result.LongestCommonSubstringCoverage);
    }

    [Fact]
    public void Unrelated_text_scores_near_zero()
    {
        SurfaceSimilarityResult result = calculator.Calculate("りんごを食べた。", "量子コンピュータは量子ビットを用いる。");

        Assert.InRange(result.Score, 0m, 0.05m);
    }

    [Fact]
    public void Partial_paste_is_captured_by_asymmetric_containment()
    {
        string student = "結論として、透明性を確保するために利用した生成AIの名称と用途を記録する必要がある。";
        string referenceText = "授業レポートでは、透明性を確保するために利用した生成AIの名称と用途を記録する必要がある。さらに自分の考察を区別する。";

        SurfaceSimilarityResult result = calculator.Calculate(student, referenceText);

        Assert.InRange(result.Score, 0.75m, 1m);
        Assert.True(result.NGramContainment > result.JaccardIndex);
    }

    [Fact]
    public void Normalization_ignores_case_width_whitespace_and_punctuation()
    {
        SurfaceSimilarityResult result = calculator.Calculate("ＡＩ レポート！", "aiレポート");

        Assert.Equal(1m, result.Score);
    }

    [Theory]
    [InlineData("AI", "ＡＩ", 2, 1)]
    [InlineData("A", "a", 1, 1)]
    [InlineData("AI", "AX", 2, 0)]
    public void Short_text_uses_smaller_ngrams(string student, string referenceText, int expectedN, double minimum)
    {
        SurfaceSimilarityResult result = calculator.Calculate(student, referenceText);

        Assert.Equal(expectedN, result.NGramSize);
        Assert.True(result.Score >= (decimal)minimum);
    }

    [Theory]
    [InlineData(null, "reference")]
    [InlineData("", "reference")]
    [InlineData(" 。 ！ ", "reference")]
    [InlineData("student", "")]
    public void Empty_after_normalization_scores_zero(string? student, string? referenceText)
    {
        SurfaceSimilarityResult result = calculator.Calculate(student, referenceText);

        Assert.Equal(0m, result.Score);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Calculation_is_deterministic()
    {
        SurfaceSimilarityResult first = calculator.Calculate("同じ文章の一部をコピーしました。", "これは同じ文章の一部をコピーしましたという参照です。");
        SurfaceSimilarityResult second = calculator.Calculate("同じ文章の一部をコピーしました。", "これは同じ文章の一部をコピーしましたという参照です。");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Long_input_stays_fast_and_bounded()
    {
        string student = new string('あ', SurfaceTextSimilarityCalculator.MaximumInputCharacters) + "末尾";
        string referenceText = new string('あ', SurfaceTextSimilarityCalculator.MaximumInputCharacters);
        Stopwatch stopwatch = Stopwatch.StartNew();

        SurfaceSimilarityResult result = calculator.Calculate(student, referenceText);

        stopwatch.Stop();
        Assert.Equal(1m, result.Score);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }
}
