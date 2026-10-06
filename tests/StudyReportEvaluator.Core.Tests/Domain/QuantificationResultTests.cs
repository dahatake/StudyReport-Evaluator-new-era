using System.Text.Json;
using System.Text.Json.Serialization;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Domain;

// Requirements: FR-023 (AC-024)
public sealed class QuantificationResultTests
{
    [Fact]
    public void Evidence_source_names_serialize_to_the_exact_contract()
    {
        JsonSerializerOptions options = new() { Converters = { new JsonStringEnumConverter() } };

        Assert.Equal("\"PRIMARY_ANSWER\"", JsonSerializer.Serialize(EvidenceSourceKind.PrimaryAnswer, options));
        Assert.Equal("\"SUPPORTING_COLUMN\"", JsonSerializer.Serialize(EvidenceSourceKind.SupportingColumn, options));
        Assert.Equal("\"NONE\"", JsonSerializer.Serialize(EvidenceSourceKind.None, options));
    }

    [Fact]
    public void Result_model_contains_no_aggregate_or_weight_surface()
    {
        string[] resultProperties = typeof(QuantificationResult).GetProperties().Select(property => property.Name).ToArray();
        string[] criterionProperties = typeof(CriterionQuantificationResult).GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(["EvaluatorId", "Criteria"], resultProperties);
        Assert.Equal(
            ["CriterionId", "RawScore", "Reason", "Evidence", "EvidenceSource", "EvidenceSourceColumnId"],
            criterionProperties);
        Assert.DoesNotContain(resultProperties.Concat(criterionProperties), name => name.Contains("Weight", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resultProperties.Concat(criterionProperties), name => name.Contains("Overall", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Content_bearing_result_string_representations_are_redacted()
    {
        const string canary = "PRIVATE-CONTENT-CANARY";
        CriterionQuantificationResult criterion = ResultTestData.Criterion() with
        {
            Reason = canary,
            Evidence = canary,
        };
        QuantificationResult result = new() { EvaluatorId = "E1", Criteria = [criterion] };

        Assert.DoesNotContain(canary, criterion.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(canary, result.ToString(), StringComparison.Ordinal);
    }
}

internal static class ResultTestData
{
    internal static CriterionQuantificationResult Criterion(
        string id = "C1",
        decimal rawScore = 5m,
        EvidenceSourceKind source = EvidenceSourceKind.PrimaryAnswer,
        string sourceColumn = "G",
        string evidence = "primary evidence") => new()
        {
            CriterionId = id,
            RawScore = rawScore,
            Reason = "short reason",
            Evidence = evidence,
            EvidenceSource = source,
            EvidenceSourceColumnId = sourceColumn,
        };
}
