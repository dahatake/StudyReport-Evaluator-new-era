using System.Collections.Immutable;
using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.App.Workbooks.Writing;

public sealed record ReferenceAnswerSheetRow
{
    public required string QuestionId { get; init; }

    public required string ModelId { get; init; }

    public string? ReasoningEffort { get; init; }

    public string? Answer { get; init; }

    public required string StatusCode { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }

    public override string ToString() =>
        $"{nameof(ReferenceAnswerSheetRow)} {{ QuestionId = {QuestionId}, StatusCode = {StatusCode}, Content = <redacted> }}";
}

public sealed record ReferenceAnswersSheetWriteResult(
    string SheetName,
    int RowCount);

public sealed class ReferenceAnswersSheetWriter
{
    public ReferenceAnswersSheetWriteResult Write(
        SpreadsheetDocument document,
        QuantificationSnapshot snapshot,
        AppOwnedSheetNames sheetNames,
        IEnumerable<ReferenceAnswerSheetRow> rows)
    {
        ArgumentNullException.ThrowIfNull(document);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The workbook part is missing.");
        return Write(workbookPart, snapshot, sheetNames.ReferencesSheetName, rows);
    }

    public ReferenceAnswersSheetWriteResult Write(
        WorkbookPart workbookPart,
        QuantificationSnapshot snapshot,
        string sheetName,
        IEnumerable<ReferenceAnswerSheetRow> rows)
    {
        ArgumentNullException.ThrowIfNull(workbookPart);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        ArgumentNullException.ThrowIfNull(rows);
        if (!snapshot.HasValidHash())
        {
            throw new InvalidDataException("The quantification snapshot hash is invalid.");
        }

        ImmutableArray<ReferenceAnswerSheetRow> materialized = rows.ToImmutableArray();
        ImmutableArray<QuestionDefinition> expected = snapshot.Definition.Questions
            .Where(question => question.Enabled)
            .ToImmutableArray();
        Validate(expected, materialized);

        SheetData data = new();
        data.Append(Header());
        Dictionary<string, ReferenceAnswerSheetRow> byQuestion = materialized.ToDictionary(
            row => row.QuestionId,
            StringComparer.Ordinal);
        uint rowNumber = 2;
        foreach (QuestionDefinition question in expected)
        {
            ReferenceAnswerSheetRow source = byQuestion[question.Id];
            string rowText = rowNumber.ToString(CultureInfo.InvariantCulture);
            Row row = new() { RowIndex = rowNumber };
            row.Append(
                SpreadsheetLiteral.CreateInlineStringCell("A" + rowText, question.Id),
                SpreadsheetLiteral.CreateInlineStringCell("B" + rowText, question.DisplayName),
                SpreadsheetLiteral.CreateInlineStringCell("C" + rowText, question.QuestionText),
                SpreadsheetLiteral.CreateInlineStringCell("D" + rowText, source.ModelId),
                SpreadsheetLiteral.CreateInlineStringCell("E" + rowText, source.Answer ?? string.Empty),
                SpreadsheetLiteral.CreateInlineStringCell("F" + rowText, source.StatusCode),
                SpreadsheetLiteral.CreateInlineStringCell(
                    "G" + rowText,
                    source.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                SpreadsheetLiteral.CreateInlineStringCell("H" + rowText, source.ReasoningEffort ?? "未指定"));
            data.Append(row);
            rowNumber++;
        }

        WorkbookSheetWriter.AddWorksheet(workbookPart, sheetName, new Worksheet(data));
        return new ReferenceAnswersSheetWriteResult(sheetName, materialized.Length);
    }

    private static void Validate(
        ImmutableArray<QuestionDefinition> expected,
        ImmutableArray<ReferenceAnswerSheetRow> rows)
    {
        Dictionary<string, ReferenceAnswerSheetRow> byQuestion = new(StringComparer.Ordinal);
        foreach (ReferenceAnswerSheetRow? row in rows)
        {
            if (row is null
                || string.IsNullOrWhiteSpace(row.QuestionId)
                || !byQuestion.TryAdd(row.QuestionId, row))
            {
                throw new ArgumentException("Reference rows must have unique question IDs.", nameof(rows));
            }

            try
            {
                EphemeralEvaluationRunner.ValidateModelId(row.ModelId);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException("The reference model ID is invalid.", nameof(rows), exception);
            }

            if (row.ReasoningEffort is not null
                && !ReasoningEffortPolicy.IsSafeReasoningEffort(row.ReasoningEffort))
            {
                throw new ArgumentException("The reference reasoning effort is invalid.", nameof(rows));
            }

            if (string.IsNullOrWhiteSpace(row.StatusCode))
            {
                throw new ArgumentException("The reference status code is required.", nameof(rows));
            }

            if (row.GeneratedAtUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException("The reference timestamp must use UTC.", nameof(rows));
            }

            if (string.Equals(row.StatusCode, "SUCCESS", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(row.Answer))
            {
                throw new ArgumentException("A successful reference row requires an answer.", nameof(rows));
            }

            if (row.Answer is { Length: > ConfigSheetWriter.MaximumCellCharacters })
            {
                throw new ArgumentException("A reference answer exceeds the Excel cell limit.", nameof(rows));
            }
        }

        string[] expectedIds = expected.Select(question => question.Id).ToArray();
        if (rows.Length != expectedIds.Length
            || expectedIds.Any(id => !byQuestion.ContainsKey(id))
            || byQuestion.Keys.Any(id => !expectedIds.Contains(id, StringComparer.Ordinal)))
        {
            throw new ArgumentException("Reference rows must match enabled questions exactly.", nameof(rows));
        }
    }

    private static Row Header() => new(
        SpreadsheetLiteral.CreateInlineStringCell("A1", "QuestionId"),
        SpreadsheetLiteral.CreateInlineStringCell("B1", "DisplayName"),
        SpreadsheetLiteral.CreateInlineStringCell("C1", "QuestionText"),
        SpreadsheetLiteral.CreateInlineStringCell("D1", "ModelId"),
        SpreadsheetLiteral.CreateInlineStringCell("E1", "ReferenceAnswer"),
        SpreadsheetLiteral.CreateInlineStringCell("F1", "Status"),
        SpreadsheetLiteral.CreateInlineStringCell("G1", "GeneratedAtUtc"),
        SpreadsheetLiteral.CreateInlineStringCell("H1", "ReasoningEffort"))
    {
        RowIndex = 1,
    };
}
