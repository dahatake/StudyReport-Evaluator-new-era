using System.Collections.Immutable;
using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workflow;

public sealed class EvaluationPlanBuilderTests
{
    [Fact]
    public void Plan_is_inclusive_rows_times_enabled_questions_times_enabled_evaluators_in_definition_order()
    {
        QuestionDefinition first = U01TestSupport.Question(
            "Q1",
            "A",
            ["B"],
            enabled: true,
            U01TestSupport.Evaluator("E1", "C1"),
            U01TestSupport.Evaluator("E-DISABLED", "C-DISABLED", enabled: false));
        QuestionDefinition disabled = U01TestSupport.Question(
            "Q-DISABLED",
            "C",
            [],
            enabled: false,
            U01TestSupport.Evaluator("E-DESCENDANT", "C-DESCENDANT"));
        QuestionDefinition second = U01TestSupport.Question(
            "Q2",
            "C",
            ["A"],
            enabled: true,
            U01TestSupport.Evaluator("E2", "C2"),
            U01TestSupport.Evaluator("E3", "C3"));
        QuantificationDefinition definition = U01TestSupport.Definition(
            2,
            4,
            first,
            disabled,
            second);
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);
        ValidatedColumnMapping mapping = U01TestSupport.ValidateMapping(snapshot.Definition).Mapping;

        EvaluationPlan plan = new EvaluationPlanBuilder().Build(snapshot, mapping);

        Assert.Same(snapshot, plan.Snapshot);
        Assert.Equal(snapshot.Sha256, plan.DefinitionSha256);
        Assert.Equal(9, plan.TotalCount);
        Assert.Equal(
            [
                "1:2:Q1:E1",
                "2:2:Q2:E2",
                "3:2:Q2:E3",
                "4:3:Q1:E1",
                "5:3:Q2:E2",
                "6:3:Q2:E3",
                "7:4:Q1:E1",
                "8:4:Q2:E2",
                "9:4:Q2:E3",
            ],
            plan.Items.Select(item =>
                $"{item.SequenceNumber}:{item.SourceRowNumber}:{item.QuestionId}:{item.EvaluatorId}"));
        Assert.Equal(["A", "B"], plan.Items[0].SelectedSourceColumns);
        Assert.Equal(["C", "A"], plan.Items[1].SelectedSourceColumns);
        Assert.DoesNotContain(plan.Items, item => item.QuestionId == "Q-DISABLED");
        Assert.DoesNotContain(plan.Items, item => item.EvaluatorId == "E-DISABLED");
    }

    [Fact]
    public void Plan_rejects_a_validated_mapping_from_a_different_snapshot()
    {
        QuantificationDefinition original = U01TestSupport.Definition(
            2,
            3,
            U01TestSupport.Question(
                "Q1",
                "A",
                ["B"],
                enabled: true,
                U01TestSupport.Evaluator("E1", "C1")));
        ValidatedColumnMapping staleMapping = U01TestSupport.ValidateMapping(original).Mapping;
        QuantificationDefinition changed = original with
        {
            Questions = [original.Questions[0] with { PrimarySourceColumn = "C" }],
        };
        QuantificationSnapshot changedSnapshot = QuantificationSnapshot.Create(changed);

        Assert.Throws<ArgumentException>(() =>
            new EvaluationPlanBuilder().Build(changedSnapshot, staleMapping));
    }

    [Fact]
    public void Plan_collections_are_copy_safe_and_string_forms_contain_only_safe_identity_dimensions()
    {
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(
            U01TestSupport.Definition(
                2,
                2,
                U01TestSupport.Question(
                    "Q-SAFE",
                    "A",
                    ["B"],
                    enabled: true,
                    U01TestSupport.Evaluator("E-SAFE", "C-SAFE"))));
        EvaluationPlan plan = new EvaluationPlanBuilder().Build(
            snapshot,
            U01TestSupport.ValidateMapping(snapshot.Definition).Mapping);
        EvaluationPlanItem item = Assert.Single(plan.Items);

        IList<EvaluationPlanItem> itemList = Assert.IsAssignableFrom<IList<EvaluationPlanItem>>(plan.Items);
        Assert.True(itemList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => itemList.Add(item));
        IList<string> columnList = Assert.IsAssignableFrom<IList<string>>(item.SelectedSourceColumns);
        Assert.True(columnList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => columnList.Add("C"));
        Assert.Contains("Q-SAFE", item.ToString(), StringComparison.Ordinal);
        Assert.Contains("E-SAFE", item.ToString(), StringComparison.Ordinal);
        Assert.Contains("<redacted>", item.ToString(), StringComparison.Ordinal);
        Assert.Contains("<redacted>", plan.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(snapshot.CanonicalJson, plan.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(snapshot.Sha256, plan.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenXml_row_source_reads_exactly_one_requested_row_and_selected_columns_without_exposing_a_path()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        OpenXmlEvaluationRowSource source = new(workbook.Path);
        EvaluationRowRequest request = new("Original", 2, ["a", "C"]);

        EvaluationRowData row = await source.ReadAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, row.SourceRowNumber);
        Assert.Equal(["A", "C"], request.SelectedColumns);
        Assert.Equal(2, row.Cells.Count);
        Assert.Equal("1", row.Cells["A"]);
        Assert.Equal("2", row.Cells["C"]);
        Assert.False(row.Cells.ContainsKey("B"));
        Assert.DoesNotContain("Synthetic body", row.Cells.Values, StringComparer.Ordinal);
        Assert.DoesNotContain(workbook.Path, request.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(workbook.Path, row.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(workbook.Path, source.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            typeof(EvaluationRowRequest).GetProperties(),
            property => property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(IEvaluationRowSource).GetMethods().SelectMany(method => method.GetParameters()),
            parameter => parameter.Name?.Contains("path", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task OpenXml_row_source_resolves_selected_shared_and_inline_strings_and_blanks_missing_cells()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        OpenXmlEvaluationRowSource source = new(workbook.Path);

        EvaluationRowData header = await source.ReadAsync(
            new EvaluationRowRequest("Original", 1, ["A", "B"]),
            TestContext.Current.CancellationToken);
        EvaluationRowData body = await source.ReadAsync(
            new EvaluationRowRequest("Original", 3, ["A", "B", "C"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(X01SyntheticWorkbookFactory.SharedHeader, header.Cells["A"]);
        Assert.Equal(X01SyntheticWorkbookFactory.InlineHeader, header.Cells["B"]);
        Assert.Equal(string.Empty, body.Cells["A"]);
        Assert.Equal("Synthetic body", body.Cells["B"]);
        Assert.Equal(string.Empty, body.Cells["C"]);
    }
}

internal static class U01TestSupport
{
    internal static QuantificationDefinition Definition(
        int firstDataRow,
        int lastDataRow,
        params QuestionDefinition[] questions)
    {
        decimal enabledPoints = questions.Where(question => question.Enabled).Sum(question => question.Points);
        return new QuantificationDefinition
        {
            Id = "DEF-U01",
            Name = "Synthetic U-01 definition",
            Revision = "1",
            SourceSheet = "Original",
            HeaderRow = 1,
            FirstDataRow = firstDataRow,
            LastDataRow = lastDataRow,
            BasePoints = 100m - enabledPoints,
            RoundingDigits = 1,
            Questions = [.. questions],
        };
    }

    internal static QuestionDefinition Question(
        string id,
        string primary,
        string[] supporting,
        bool enabled,
        params EvaluatorDefinition[] evaluators) =>
        new()
        {
            Id = id,
            DisplayName = "Question " + id,
            QuestionText = "Question text " + id,
            PrimarySourceColumn = primary,
            SupportingSourceColumns = [.. supporting],
            Points = 2m,
            Enabled = enabled,
            Evaluators = [.. evaluators],
        };

    internal static EvaluatorDefinition Evaluator(
        string id,
        string criterionId,
        bool enabled = true,
        ScoreRange? evaluatorRange = null,
        ScoreRange? criterionRange = null,
        string? customPrompt = null,
        decimal evaluatorWeight = 3m,
        decimal criterionWeight = 4m) =>
        new()
        {
            Id = id,
            DisplayName = "Evaluator " + id,
            Type = EvaluatorType.CustomPrompt,
            Weight = evaluatorWeight,
            Range = evaluatorRange ?? new ScoreRange(0m, 10m),
            CustomPromptTemplate = customPrompt ?? "Prompt {回答} {評価項目}",
            Enabled = enabled,
            Criteria =
            [
                new CriterionDefinition
                {
                    Id = criterionId,
                    DisplayName = "Criterion " + criterionId,
                    Description = "Description " + criterionId,
                    Weight = criterionWeight,
                    Range = criterionRange,
                    Enabled = true,
                },
            ],
        };

    internal static (WorkbookMetadata Metadata, ValidatedColumnMapping Mapping) ValidateMapping(
        QuantificationDefinition definition)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original",
            1,
            checked((uint)definition.LastDataRow),
            3,
            new X02Header(1, "Primary A"),
            new X02Header(2, "Support B"),
            new X02Header(3, "Primary C"));
        WorkbookMetadata metadata = new WorkbookMetadataReader().Read(workbook.Path);
        ColumnMappingValidationResult validation = new ColumnMappingValidator().Validate(
            metadata,
            definition);
        Assert.True(validation.IsValid, string.Join(',', validation.Errors.Select(error => error.Code)));
        return (metadata, Assert.IsType<ValidatedColumnMapping>(validation.Mapping));
    }

    internal static EvaluationPlan Plan(QuantificationDefinition definition)
    {
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);
        return new EvaluationPlanBuilder().Build(
            snapshot,
            ValidateMapping(snapshot.Definition).Mapping);
    }

    internal static QuantificationResult ValidResult(
        SafeEvaluationPayload payload,
        Func<ExpectedCriterion, decimal>? score = null) =>
        new()
        {
            EvaluatorId = payload.EvaluatorId,
            Criteria = payload.ExpectedCriteria
                .Select(criterion => new CriterionQuantificationResult
                {
                    CriterionId = criterion.CriterionId,
                    RawScore = score?.Invoke(criterion) ?? criterion.Range.Minimum,
                    Reason = "Synthetic reason",
                    Evidence = string.Empty,
                    EvidenceSource = EvidenceSourceKind.None,
                    EvidenceSourceColumnId = string.Empty,
                })
                .ToImmutableArray(),
        };

    internal static InputSnapshot InputSnapshot() =>
        new(new string('A', 64), 123, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
}
