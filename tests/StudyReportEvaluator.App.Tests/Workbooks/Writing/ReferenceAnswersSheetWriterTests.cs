using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Writing;

// Requirements: FR-018 (AC-019)
public sealed class ReferenceAnswersSheetWriterTests
{
    [Fact]
    public void Writes_one_ordered_literal_reference_per_enabled_question()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateDefinition());
        using SpreadsheetDocument document = SpreadsheetDocument.Open(workbook.Path, true);
        AppOwnedSheetNames names = new AppOwnedSheetNameResolver().Resolve(document);
        ReferenceAnswersSheetWriter writer = new();

        ReferenceAnswersSheetWriteResult result = writer.Write(
            document,
            snapshot,
            names,
            [
                new ReferenceAnswerSheetRow
                {
                    QuestionId = "Q1",
                    ModelId = "model-a",
                    ReasoningEffort = "low",
                    Answer = "=REFERENCE-CANARY",
                    StatusCode = "SUCCESS",
                    GeneratedAtUtc = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
                },
            ]);

        Assert.Equal(names.ReferencesSheetName, result.SheetName);
        Assert.Equal(1, result.RowCount);
        Worksheet sheet = Worksheet(document, names.ReferencesSheetName);
        Assert.Empty(sheet.Descendants<CellFormula>());
        Row row = sheet.Descendants<Row>().Single(item => item.RowIndex?.Value == 2);
        Assert.Equal("Q1", Text(row, "A"));
        Assert.Equal("model-a", Text(row, "D"));
        Assert.Equal("=REFERENCE-CANARY", Text(row, "E"));
        Assert.Equal("SUCCESS", Text(row, "F"));
        Assert.Equal("low", Text(row, "H"));
        Assert.All(row.Elements<Cell>(), cell => Assert.Equal(CellValues.InlineString, cell.DataType?.Value));
    }

    [Fact]
    public void Reference_rows_must_match_enabled_questions_exactly_and_success_requires_an_answer()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateDefinition());
        using SpreadsheetDocument document = SpreadsheetDocument.Open(workbook.Path, true);
        AppOwnedSheetNames names = new AppOwnedSheetNameResolver().Resolve(document);
        ReferenceAnswersSheetWriter writer = new();
        Workbook workbookRoot = document.WorkbookPart?.Workbook
            ?? throw new InvalidDataException("The synthetic workbook root is missing.");
        int before = workbookRoot.Descendants<Sheet>().Count();

        Assert.Throws<ArgumentException>(() => writer.Write(document, snapshot, names, []));
        Assert.Throws<ArgumentException>(() => writer.Write(
            document,
            snapshot,
            names,
            [
                new ReferenceAnswerSheetRow
                {
                    QuestionId = "Q1",
                    ModelId = "auto",
                    Answer = "",
                    StatusCode = "SUCCESS",
                    GeneratedAtUtc = DateTimeOffset.UnixEpoch,
                },
            ]));
        Assert.Throws<ArgumentException>(() => writer.Write(
            document,
            snapshot,
            names,
            [
                new ReferenceAnswerSheetRow
                {
                    QuestionId = "Q1",
                    ModelId = " model-a",
                    Answer = "answer",
                    StatusCode = "SUCCESS",
                    GeneratedAtUtc = DateTimeOffset.UnixEpoch,
                },
            ]));
        ArgumentException missingStatus = Assert.Throws<ArgumentException>(() => writer.Write(
            document,
            snapshot,
            names,
            [
                new ReferenceAnswerSheetRow
                {
                    QuestionId = "Q1",
                    ModelId = "auto",
                    Answer = null,
                    StatusCode = "",
                    GeneratedAtUtc = DateTimeOffset.UnixEpoch,
                },
            ]));
        ArgumentException nonUtc = Assert.Throws<ArgumentException>(() => writer.Write(
            document,
            snapshot,
            names,
            [
                new ReferenceAnswerSheetRow
                {
                    QuestionId = "Q1",
                    ModelId = "auto",
                    Answer = null,
                    StatusCode = "AI_TIMEOUT",
                    GeneratedAtUtc = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(9)),
                },
            ]));

        Assert.Equal(before, workbookRoot.Descendants<Sheet>().Count());
        Assert.Contains("status", missingStatus.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UTC", nonUtc.Message, StringComparison.Ordinal);
    }

    private static QuantificationDefinition CreateDefinition()
    {
        CriterionDefinition criterion = new()
        {
            Id = "C1",
            DisplayName = "Criterion",
            Description = "Description",
            Weight = 1m,
        };
        EvaluatorDefinition evaluator = new()
        {
            Id = "E1",
            DisplayName = "Knowledge",
            Type = EvaluatorType.KnowledgeCoverage,
            Weight = 1m,
            Range = new ScoreRange(0m, 10m),
            Criteria = [criterion],
            BuiltInTemplateVersion = "knowledge-v1",
        };
        return new QuantificationDefinition
        {
            Id = "DEF",
            Name = "Definition",
            Revision = "1",
            SourceSheet = "Original",
            HeaderRow = 1,
            FirstDataRow = 2,
            LastDataRow = 3,
            Questions =
            [
                new QuestionDefinition
                {
                    Id = "Q1",
                    DisplayName = "Question",
                    QuestionText = "Question text",
                    PrimarySourceColumn = "A",
                    Points = 40m,
                    Evaluators = [evaluator],
                },
            ],
        };
    }

    private static Worksheet Worksheet(SpreadsheetDocument document, string name)
    {
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("The workbook root is missing.");
        Sheet sheet = workbook.Descendants<Sheet>()
            .Single(item => string.Equals(item.Name?.Value, name, StringComparison.Ordinal));
        return ((WorksheetPart)workbookPart.GetPartById(
            sheet.Id?.Value ?? throw new InvalidDataException("The sheet relationship is missing."))).Worksheet
            ?? throw new InvalidDataException("The worksheet root is missing.");
    }

    private static string Text(Row row, string column) => row.Elements<Cell>()
        .FirstOrDefault(cell => (cell.CellReference?.Value ?? string.Empty).StartsWith(column, StringComparison.Ordinal))
        ?.InnerText ?? string.Empty;
}
