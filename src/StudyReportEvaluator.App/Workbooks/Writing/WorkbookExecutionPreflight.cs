using System.Collections.Immutable;
using System.Globalization;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.App.Workbooks.Writing;

public sealed record ExecutionCapacityError(
    string Code,
    string NodeKind,
    string NodeId,
    string DisplayName,
    string Field,
    string ActualDimension,
    string Limit)
{
    public override string ToString() =>
        $"{nameof(ExecutionCapacityError)} {{ Code = {Code}, NodeKind = {NodeKind}, NodeId = {NodeId}, Field = {Field}, ActualDimension = {ActualDimension}, Limit = {Limit}, Content = <redacted> }}";
}

public sealed class WorkbookExecutionPreflightResult
{
    internal WorkbookExecutionPreflightResult(
        long configRowCount,
        int resultsColumnCount,
        int representativeFormulaCount,
        ImmutableArray<ExecutionCapacityError> errors)
    {
        ConfigRowCount = configRowCount;
        ResultsColumnCount = resultsColumnCount;
        RepresentativeFormulaCount = representativeFormulaCount;
        Errors = errors;
    }

    public long ConfigRowCount { get; }

    public int ResultsColumnCount { get; }

    public int RepresentativeFormulaCount { get; }

    public ImmutableArray<ExecutionCapacityError> Errors { get; }

    public bool IsValid => Errors.IsEmpty;

    public override string ToString() =>
        $"{nameof(WorkbookExecutionPreflightResult)} {{ ConfigRowCount = {ConfigRowCount.ToString(CultureInfo.InvariantCulture)}, ResultsColumnCount = {ResultsColumnCount.ToString(CultureInfo.InvariantCulture)}, RepresentativeFormulaCount = {RepresentativeFormulaCount.ToString(CultureInfo.InvariantCulture)}, ErrorCount = {Errors.Length.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed class WorkbookExecutionPreflight
{
    private readonly AppOwnedSheetNameResolver sheetNameResolver = new();
    private readonly ConfigSheetWriter configWriter = new();
    private readonly ResultsSheetWriter resultsWriter = new();

    public WorkbookExecutionPreflightResult Validate(
        QuantificationSnapshot snapshot,
        WorkbookMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(metadata);

        ImmutableArray<ExecutionCapacityError>.Builder errors =
            ImmutableArray.CreateBuilder<ExecutionCapacityError>();
        long configRowCount;
        try
        {
            configRowCount = configWriter.CalculateRequiredRowCount(snapshot);
        }
        catch (OverflowException)
        {
            configRowCount = long.MaxValue;
        }

        if (configRowCount > ConfigSheetWriter.MaximumExcelRows)
        {
            errors.Add(new ExecutionCapacityError(
                "CONFIG_ROW_LIMIT_EXCEEDED",
                "Definition",
                snapshot.Definition.Id,
                snapshot.Definition.Name,
                "ConfigRows",
                configRowCount.ToString(CultureInfo.InvariantCulture),
                ConfigSheetWriter.MaximumExcelRows.ToString(CultureInfo.InvariantCulture)));
            return new WorkbookExecutionPreflightResult(
                configRowCount,
                0,
                0,
                errors.ToImmutable());
        }

        AppOwnedSheetNames sheetNames;
        ConfigCellAddressMap configCells;
        try
        {
            sheetNames = sheetNameResolver.Resolve(
                metadata.Worksheets.Select(worksheet => worksheet.Name));
            configCells = configWriter.CreateAddressMap(snapshot, sheetNames.ConfigSheetName);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidDataException
            or IOException
            or OverflowException)
        {
            errors.Add(new ExecutionCapacityError(
                "WORKBOOK_LAYOUT_INVALID",
                "Definition",
                snapshot.Definition.Id,
                snapshot.Definition.Name,
                "WorkbookLayout",
                "invalid",
                "valid app-owned layout"));
            return new WorkbookExecutionPreflightResult(
                configRowCount,
                0,
                0,
                errors.ToImmutable());
        }

        ResultsSheetPreflightResult results = resultsWriter.Preflight(
            snapshot,
            sheetNames,
            configCells);
        foreach (ResultsSheetValidationError error in results.Errors)
        {
            errors.Add(new ExecutionCapacityError(
                error.Code,
                error.NodeKind,
                error.NodeId,
                error.DisplayName,
                error.Field,
                error.SafeOffendingValue,
                error.Limit ?? "valid Excel capacity"));
        }

        return new WorkbookExecutionPreflightResult(
            configRowCount,
            results.ColumnCount,
            results.FormulaCount,
            errors.ToImmutable());
    }
}
