using StudyReportEvaluator.Core.Similarity;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Similarity;

// Requirements: FR-021 (AC-022)
public sealed class SimilarityPeerAnalysisTests
{
    [Fact]
    public void Calculates_best_other_row_per_question()
    {
        SurfaceTextSimilarityCalculator calculator = new();

        SimilarityPeerAnalysisResult result = calculator.CalculatePeerMaximums([
            new SimilarityPeerInput(2, "Q1", "生成AIの利用では透明性が重要です。"),
            new SimilarityPeerInput(3, "Q1", "生成AIの利用では透明性が重要です!"),
            new SimilarityPeerInput(4, "Q1", "まったく別の内容です。"),
            new SimilarityPeerInput(2, "Q2", "短い回答"),
            new SimilarityPeerInput(3, "Q2", "別回答"),
        ]);

        SimilarityPeerResult row2 = result.Find(2, "Q1")!;
        Assert.NotNull(row2);
        Assert.Equal(1m, row2.PeerMax);
        Assert.Equal(3, row2.PeerRow);

        SimilarityPeerResult row4 = result.Find(4, "Q1")!;
        Assert.NotNull(row4);
        Assert.Equal(2, row4.PeerRow);
        Assert.True(row4.PeerMax < 0.5m);
    }

    [Fact]
    public void Empty_answers_have_no_peer_row()
    {
        SurfaceTextSimilarityCalculator calculator = new();

        SimilarityPeerAnalysisResult result = calculator.CalculatePeerMaximums([
            new SimilarityPeerInput(2, "Q1", ""),
            new SimilarityPeerInput(3, "Q1", "内容"),
        ]);

        SimilarityPeerResult row2 = result.Find(2, "Q1")!;
        Assert.NotNull(row2);
        Assert.Equal(0m, row2.PeerMax);
        Assert.Null(row2.PeerRow);
    }
}
