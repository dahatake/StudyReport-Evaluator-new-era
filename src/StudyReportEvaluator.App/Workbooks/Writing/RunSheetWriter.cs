using System.Globalization;
using System.Reflection;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workbooks.Intake;

namespace StudyReportEvaluator.App.Workbooks.Writing;

public sealed record RunSheetMetadata
{
    public required InputSnapshot InputIdentity { get; init; }

    public required string DefinitionSha256 { get; init; }

    public required string ApplicationIdentity { get; init; }

    public required string CopilotSdkIdentity { get; init; }

    public required string CopilotCliIdentity { get; init; }

    public required string ModelIdentity { get; init; }

    public string? ReasoningEffort { get; init; }

    public string? ContextTier { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset EndedAtUtc { get; init; }

    public int PlannedEvaluationCount { get; init; }

    public int CompletedEvaluationCount { get; init; }

    public int ErrorCount { get; init; }

    public int UsageObservedUnitCount { get; init; }

    public long InputTokenCount { get; init; }

    public long OutputTokenCount { get; init; }

    public long ReasoningTokenCount { get; init; }

    public long CacheReadTokenCount { get; init; }

    public long CacheWriteTokenCount { get; init; }

    public required AppOwnedSheetNames SheetNames { get; init; }

    public override string ToString() =>
        $"{nameof(RunSheetMetadata)} {{ PlannedEvaluationCount = {PlannedEvaluationCount}, CompletedEvaluationCount = {CompletedEvaluationCount}, ErrorCount = {ErrorCount}, Content = <redacted> }}";
}

public sealed class RunSheetWriter
{
    public void Write(SpreadsheetDocument document, RunSheetMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(document);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The workbook part is missing.");
        Write(workbookPart, metadata);
    }

    public void Write(WorkbookPart workbookPart, RunSheetMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(workbookPart);
        ArgumentNullException.ThrowIfNull(metadata);
        Validate(metadata);

        SheetData sheetData = new();
        sheetData.Append(CreateHeaderRow());
        uint rowNumber = 2;
        AppendInlineRecord(sheetData, ref rowNumber, "InputSha256", metadata.InputIdentity.Sha256);
        AppendNumberRecord(sheetData, ref rowNumber, "InputSizeBytes", metadata.InputIdentity.SizeBytes);
        AppendInlineRecord(
            sheetData,
            ref rowNumber,
            "InputLastWriteTimeUtc",
            metadata.InputIdentity.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture));
        AppendInlineRecord(sheetData, ref rowNumber, "DefinitionSha256", metadata.DefinitionSha256.ToUpperInvariant());
        AppendInlineRecord(sheetData, ref rowNumber, "ApplicationIdentity", metadata.ApplicationIdentity);
        AppendInlineRecord(sheetData, ref rowNumber, "CopilotSdkIdentity", metadata.CopilotSdkIdentity);
        AppendInlineRecord(sheetData, ref rowNumber, "CopilotCliIdentity", metadata.CopilotCliIdentity);
        AppendInlineRecord(sheetData, ref rowNumber, "ModelIdentity", metadata.ModelIdentity);
        AppendInlineRecord(sheetData, ref rowNumber, "ReasoningEffort", metadata.ReasoningEffort ?? "未指定");
        AppendInlineRecord(sheetData, ref rowNumber, "ContextTier", metadata.ContextTier ?? ModelOptionPolicy.DefaultContextTier);
        AppendInlineRecord(sheetData, ref rowNumber, "OpenXmlSdkIdentity", GetOpenXmlSdkIdentity());
        AppendInlineRecord(
            sheetData,
            ref rowNumber,
            "StartedAtUtc",
            metadata.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        AppendInlineRecord(
            sheetData,
            ref rowNumber,
            "EndedAtUtc",
            metadata.EndedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        AppendNumberRecord(sheetData, ref rowNumber, "PlannedEvaluationCount", metadata.PlannedEvaluationCount);
        AppendNumberRecord(sheetData, ref rowNumber, "CompletedEvaluationCount", metadata.CompletedEvaluationCount);
        AppendNumberRecord(sheetData, ref rowNumber, "ErrorCount", metadata.ErrorCount);
        AppendNumberRecord(sheetData, ref rowNumber, "UsageObservedUnitCount", metadata.UsageObservedUnitCount);
        AppendNumberRecord(sheetData, ref rowNumber, "InputTokenCount", metadata.InputTokenCount);
        AppendNumberRecord(sheetData, ref rowNumber, "OutputTokenCount", metadata.OutputTokenCount);
        AppendNumberRecord(sheetData, ref rowNumber, "ReasoningTokenCount", metadata.ReasoningTokenCount);
        AppendNumberRecord(sheetData, ref rowNumber, "CacheReadTokenCount", metadata.CacheReadTokenCount);
        AppendNumberRecord(sheetData, ref rowNumber, "CacheWriteTokenCount", metadata.CacheWriteTokenCount);
        AppendInlineRecord(sheetData, ref rowNumber, "ConfigSheetName", metadata.SheetNames.ConfigSheetName);
        AppendInlineRecord(sheetData, ref rowNumber, "ReferencesSheetName", metadata.SheetNames.ReferencesSheetName);
        AppendInlineRecord(sheetData, ref rowNumber, "ResultsSheetName", metadata.SheetNames.ResultsSheetName);
        AppendInlineRecord(sheetData, ref rowNumber, "RunSheetName", metadata.SheetNames.RunSheetName);

        WorkbookSheetWriter.AddWorksheet(
            workbookPart,
            metadata.SheetNames.RunSheetName,
            new Worksheet(sheetData));
    }

    private static void Validate(RunSheetMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata.InputIdentity);
        ArgumentNullException.ThrowIfNull(metadata.SheetNames);
        ValidateSha256(metadata.DefinitionSha256, nameof(metadata.DefinitionSha256));
        ValidateIdentity(metadata.ApplicationIdentity, nameof(metadata.ApplicationIdentity));
        ValidateIdentity(metadata.CopilotSdkIdentity, nameof(metadata.CopilotSdkIdentity));
        ValidateIdentity(metadata.CopilotCliIdentity, nameof(metadata.CopilotCliIdentity));
        ValidateIdentity(metadata.ModelIdentity, nameof(metadata.ModelIdentity));
        if (metadata.ReasoningEffort is not null
            && !ReasoningEffortPolicy.IsSafeReasoningEffort(metadata.ReasoningEffort))
        {
            throw new ArgumentException("The reasoning effort is invalid.", nameof(metadata));
        }

        ValidateUtc(metadata.StartedAtUtc, nameof(metadata.StartedAtUtc));
        _ = ModelOptionPolicy.ToSdkContextTier(metadata.ContextTier);
        ValidateUtc(metadata.EndedAtUtc, nameof(metadata.EndedAtUtc));

        if (metadata.EndedAtUtc < metadata.StartedAtUtc)
        {
            throw new ArgumentException("The run end time must not precede its start time.", nameof(metadata));
        }

        if (metadata.PlannedEvaluationCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(metadata.PlannedEvaluationCount));
        }

        if (metadata.CompletedEvaluationCount < 0
            || metadata.CompletedEvaluationCount > metadata.PlannedEvaluationCount)
        {
            throw new ArgumentOutOfRangeException(nameof(metadata.CompletedEvaluationCount));
        }

        if (metadata.ErrorCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(metadata.ErrorCount));
        }

        if (metadata.UsageObservedUnitCount < 0
            || metadata.UsageObservedUnitCount > metadata.CompletedEvaluationCount)
        {
            throw new ArgumentOutOfRangeException(nameof(metadata.UsageObservedUnitCount));
        }

        if (metadata.InputTokenCount < 0
            || metadata.OutputTokenCount < 0
            || metadata.ReasoningTokenCount < 0
            || metadata.CacheReadTokenCount < 0
            || metadata.CacheWriteTokenCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(metadata), "Token usage counts must be nonnegative.");
        }
    }

    private static void ValidateSha256(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.", parameterName);
        }
    }

    private static void ValidateIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > ConfigSheetWriter.MaximumCellCharacters)
        {
            throw new ArgumentException("A run identity exceeds the Excel cell limit.", parameterName);
        }
    }

    private static void ValidateUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Run timestamps must use the UTC offset.", parameterName);
        }
    }

    private static Row CreateHeaderRow()
    {
        Row row = new() { RowIndex = 1 };
        row.Append(
            SpreadsheetLiteral.CreateInlineStringCell("A1", "Field"),
            SpreadsheetLiteral.CreateInlineStringCell("B1", "Value"));
        return row;
    }

    private static void AppendInlineRecord(
        SheetData sheetData,
        ref uint rowNumber,
        string field,
        string value)
    {
        string rowText = rowNumber.ToString(CultureInfo.InvariantCulture);
        Row row = new(
            SpreadsheetLiteral.CreateInlineStringCell("A" + rowText, field),
            SpreadsheetLiteral.CreateInlineStringCell("B" + rowText, value))
        {
            RowIndex = rowNumber,
        };
        sheetData.Append(row);
        rowNumber++;
    }

    private static void AppendNumberRecord(
        SheetData sheetData,
        ref uint rowNumber,
        string field,
        int value)
    {
        string rowText = rowNumber.ToString(CultureInfo.InvariantCulture);
        Row row = new(
            SpreadsheetLiteral.CreateInlineStringCell("A" + rowText, field),
            SpreadsheetLiteral.CreateNumberCell("B" + rowText, value))
        {
            RowIndex = rowNumber,
        };
        sheetData.Append(row);
        rowNumber++;
    }

    private static void AppendNumberRecord(
        SheetData sheetData,
        ref uint rowNumber,
        string field,
        long value)
    {
        string rowText = rowNumber.ToString(CultureInfo.InvariantCulture);
        Row row = new(
            SpreadsheetLiteral.CreateInlineStringCell("A" + rowText, field),
            SpreadsheetLiteral.CreateNumberCell("B" + rowText, value))
        {
            RowIndex = rowNumber,
        };
        sheetData.Append(row);
        rowNumber++;
    }

    private static string GetOpenXmlSdkIdentity()
    {
        Assembly assembly = typeof(SpreadsheetDocument).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        string version = informationalVersion?.Split('+', 2)[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "unknown";
        return "DocumentFormat.OpenXml/" + version;
    }
}
