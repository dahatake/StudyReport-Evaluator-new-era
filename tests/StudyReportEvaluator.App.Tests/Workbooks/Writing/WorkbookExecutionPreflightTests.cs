using System.Collections.Immutable;
using System.Globalization;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Writing;

// Requirements: NFR-PERF-002 (AC-072)
public sealed class WorkbookExecutionPreflightTests
{
    [Fact]
    public void Valid_snapshot_builds_exact_representative_layout_without_workbook_mutation()
    {
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateDefinition(1));
        var metadata = U01TestSupport.ValidateMapping(snapshot.Definition).Metadata;

        WorkbookExecutionPreflightResult result = new WorkbookExecutionPreflight().Validate(
            snapshot,
            metadata);

        Assert.True(result.IsValid, string.Join(',', result.Errors.Select(error => error.Code)));
        Assert.Empty(result.Errors);
        Assert.Equal(26, result.ResultsColumnCount);
        Assert.Equal(11, result.RepresentativeFormulaCount);
        Assert.True(result.ConfigRowCount >= 7);
        Assert.Contains("<redacted>", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Excel_column_limit_is_rejected_by_run_preflight()
    {
        const int criterionCount = 1_639;
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateDefinition(criterionCount));
        var metadata = U01TestSupport.ValidateMapping(snapshot.Definition).Metadata;

        WorkbookExecutionPreflightResult result = new WorkbookExecutionPreflight().Validate(
            snapshot,
            metadata);

        ExecutionCapacityError error = Assert.Single(
            result.Errors,
            item => item.Code == "COLUMN_LIMIT_EXCEEDED");
        Assert.False(result.IsValid);
        Assert.Equal((criterionCount * 10 + 16).ToString(CultureInfo.InvariantCulture), error.ActualDimension);
        Assert.Equal("16384", error.Limit);
        Assert.Equal("ResultsColumns", error.Field);
    }

    [Fact]
    public void Formula_function_argument_limit_is_rejected_by_run_preflight()
    {
        const int criterionCount = 256;
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateDefinition(criterionCount));
        var metadata = U01TestSupport.ValidateMapping(snapshot.Definition).Metadata;

        WorkbookExecutionPreflightResult result = new WorkbookExecutionPreflight().Validate(
            snapshot,
            metadata);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == "FUNCTION_ARGUMENT_LIMIT_EXCEEDED"
                && error.NodeKind == "Evaluator"
                && error.NodeId == "E1"
                && error.ActualDimension == criterionCount.ToString(CultureInfo.InvariantCulture)
                && error.Limit == "255");
        Assert.Contains(
            result.Errors,
            error => error.Code == "FORMULA_LENGTH_EXCEEDED"
                && error.NodeKind == "Evaluator"
                && error.NodeId == "E1"
                && int.Parse(error.ActualDimension, CultureInfo.InvariantCulture) > 8_191
                && error.Limit == "8191");
    }

    [Fact]
    public void Oversized_results_header_is_rejected_without_echoing_identity_content()
    {
        string longId = new('X', 32_760);
        QuantificationDefinition definition = CreateDefinition(1);
        CriterionDefinition criterion = definition.Questions[0].Evaluators[0].Criteria[0] with
        {
            Id = longId,
        };
        definition = definition with
        {
            Questions =
            [
                definition.Questions[0] with
                {
                    Evaluators =
                    [
                        definition.Questions[0].Evaluators[0] with { Criteria = [criterion] },
                    ],
                },
            ],
        };
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);
        var metadata = U01TestSupport.ValidateMapping(snapshot.Definition).Metadata;

        WorkbookExecutionPreflightResult result = new WorkbookExecutionPreflight().Validate(
            snapshot,
            metadata);

        ExecutionCapacityError[] errors = result.Errors
            .Where(item => item.Code == "HEADER_CELL_LIMIT_EXCEEDED")
            .ToArray();
        Assert.Equal(10, errors.Length);
        Assert.All(errors, error =>
        {
            Assert.Equal("ResultsHeader", error.Field);
            Assert.Equal("32767", error.Limit);
            Assert.DoesNotContain(longId, error.ToString(), StringComparison.Ordinal);
        });
    }

    private static QuantificationDefinition CreateDefinition(int criterionCount)
    {
        ImmutableArray<CriterionDefinition> criteria = Enumerable.Range(1, criterionCount)
            .Select(index => new CriterionDefinition
            {
                Id = $"C{index.ToString(CultureInfo.InvariantCulture)}",
                DisplayName = $"Criterion {index.ToString(CultureInfo.InvariantCulture)}",
                Description = "Synthetic criterion",
                Weight = 1m,
                Enabled = true,
            })
            .ToImmutableArray();
        EvaluatorDefinition evaluator = U01TestSupport.Evaluator("E1", "placeholder") with
        {
            Criteria = criteria,
        };
        return U01TestSupport.Definition(
            2,
            2,
            U01TestSupport.Question("Q1", "A", ["B"], true, evaluator));
    }
}
