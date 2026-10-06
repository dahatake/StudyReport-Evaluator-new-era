using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Formulas;

namespace StudyReportEvaluator.App.Workbooks.Validation;

public sealed record ExpectedFormulaCell(
    FormulaCellDefinition Definition,
    decimal? CachedValue)
{
    public override string ToString() =>
        $"{nameof(ExpectedFormulaCell)} {{ Content = <redacted> }}";
}

public sealed class OutputPackageValidationPlan
{
    private OutputPackageValidationPlan(
        AppOwnedSheetNames sheetNames,
        ImmutableArray<PreservedWorksheetIdentity> preservedWorksheets,
        ImmutableArray<PreservedPartIdentity> preservedParts,
        string definedNamesSha256,
        ImmutableArray<ExpectedFormulaCell> expectedFormulaCells,
        ImmutableArray<FormulaCellAddress> verifiedCells,
        ImmutableArray<FormulaRangeAddress> verifiedRanges)
    {
        SheetNames = sheetNames;
        PreservedWorksheets = preservedWorksheets;
        PreservedParts = preservedParts;
        DefinedNamesSha256 = definedNamesSha256;
        ExpectedFormulaCells = expectedFormulaCells;
        VerifiedCells = verifiedCells;
        VerifiedRanges = verifiedRanges;
    }

    public AppOwnedSheetNames SheetNames { get; }

    public ImmutableArray<ExpectedFormulaCell> ExpectedFormulaCells { get; }

    public ImmutableArray<FormulaCellAddress> VerifiedCells { get; }

    public ImmutableArray<FormulaRangeAddress> VerifiedRanges { get; }

    internal ImmutableArray<PreservedWorksheetIdentity> PreservedWorksheets { get; }

    internal ImmutableArray<PreservedPartIdentity> PreservedParts { get; }

    internal string DefinedNamesSha256 { get; }

    public static OutputPackageValidationPlan Capture(
        string originalInputPath,
        AppOwnedSheetNames sheetNames,
        IEnumerable<ExpectedFormulaCell> expectedFormulaCells,
        IEnumerable<FormulaCellAddress>? verifiedCells = null,
        IEnumerable<FormulaRangeAddress>? verifiedRanges = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalInputPath);
        ArgumentNullException.ThrowIfNull(sheetNames);
        ArgumentNullException.ThrowIfNull(expectedFormulaCells);

        ImmutableArray<ExpectedFormulaCell> formulas = expectedFormulaCells.ToImmutableArray();
        if (formulas.Any(formula => formula is null
            || formula.Definition is null
            || formula.Definition.Expression is null))
        {
            throw new ArgumentException("Expected formula cells cannot contain null values.", nameof(expectedFormulaCells));
        }

        string canonicalInputPath = Path.GetFullPath(originalInputPath);
        using FileStream stream = new(
            canonicalInputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using SpreadsheetDocument document = SpreadsheetDocument.Open(
            stream,
            false,
            new OpenSettings
            {
                AutoSave = false,
                MaxCharactersInPart = FileFormatClassifier.MaxCharactersInPart,
            });

        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The original workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("The original workbook root is missing.");
        Sheets sheets = workbook.GetFirstChild<Sheets>()
            ?? throw new InvalidDataException("The original workbook sheet collection is missing.");

        string[] boundNames =
        [
            sheetNames.ConfigSheetName,
            sheetNames.ReferencesSheetName,
            sheetNames.ResultsSheetName,
            sheetNames.RunSheetName,
        ];
        if (boundNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != boundNames.Length)
        {
            throw new ArgumentException("App-owned worksheet bindings must be unique.", nameof(sheetNames));
        }

        ImmutableArray<PreservedWorksheetIdentity>.Builder preserved =
            ImmutableArray.CreateBuilder<PreservedWorksheetIdentity>();
        int ordinal = 0;
        foreach (Sheet sheet in sheets.Elements<Sheet>())
        {
            ordinal++;
            string name = sheet.Name?.Value
                ?? throw new InvalidDataException("An original worksheet name is missing.");
            if (boundNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "An app-owned worksheet binding collides with an original worksheet.",
                    nameof(sheetNames));
            }

            string relationshipId = sheet.Id?.Value
                ?? throw new InvalidDataException("An original worksheet relationship is missing.");
            if (workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart
                || worksheetPart.Worksheet is null)
            {
                throw new InvalidDataException("An original worksheet part is missing.");
            }

            preserved.Add(new PreservedWorksheetIdentity(
                ordinal,
                name,
                sheet.SheetId?.Value
                    ?? throw new InvalidDataException("An original worksheet ID is missing."),
                sheet.State?.InnerText ?? string.Empty,
                relationshipId,
                HashText(worksheetPart.Worksheet.OuterXml)));
        }

        ImmutableArray<FormulaCellAddress> cells = verifiedCells is null
            ? CollectReferencedCells(formulas).ToImmutableArray()
            : verifiedCells.ToImmutableArray();
        ImmutableArray<FormulaRangeAddress> ranges = verifiedRanges is null
            ? CollectReferencedRanges(formulas).ToImmutableArray()
            : verifiedRanges.ToImmutableArray();
        string definedNames = workbook.GetFirstChild<DefinedNames>()?.OuterXml ?? string.Empty;
        ImmutableArray<PreservedPartIdentity> preservedParts = EnumerateParts(document)
            .Where(part => !ReferenceEquals(part, workbookPart))
            .OrderBy(part => part.Uri.OriginalString, StringComparer.Ordinal)
            .Select((part, index) => new PreservedPartIdentity(
                index + 1,
                part.Uri.OriginalString,
                part.ContentType,
                HashPart(part)))
            .ToImmutableArray();

        return new OutputPackageValidationPlan(
            sheetNames,
            preserved.ToImmutable(),
            preservedParts,
            HashText(definedNames),
            formulas,
            cells,
            ranges);
    }

    public override string ToString() =>
        $"{nameof(OutputPackageValidationPlan)} {{ PreservedSheetCount = {PreservedWorksheets.Length}, PreservedPartCount = {PreservedParts.Length}, ExpectedFormulaCount = {ExpectedFormulaCells.Length}, Content = <redacted> }}";

    private static IEnumerable<FormulaCellAddress> CollectReferencedCells(
        IEnumerable<ExpectedFormulaCell> formulas)
    {
        HashSet<FormulaCellAddress> cells = [];
        foreach (ExpectedFormulaCell expected in formulas)
        {
            cells.Add(Normalize(expected.Definition.Target));
            foreach (FormulaCellAddress reference in EnumerateCellReferences(expected.Definition.Expression))
            {
                cells.Add(Normalize(reference));
            }
        }

        return cells;
    }

    private static IEnumerable<FormulaRangeAddress> CollectReferencedRanges(
        IEnumerable<ExpectedFormulaCell> formulas)
    {
        HashSet<FormulaRangeAddress> ranges = [];
        foreach (ExpectedFormulaCell expected in formulas)
        {
            foreach (FormulaRangeAddress reference in EnumerateRangeReferences(expected.Definition.Expression))
            {
                ranges.Add(Normalize(reference));
            }
        }

        return ranges;
    }

    internal static IEnumerable<FormulaCellAddress> EnumerateCellReferences(FormulaExpression root)
    {
        Stack<FormulaExpression> pending = new();
        pending.Push(root);
        while (pending.TryPop(out FormulaExpression? expression))
        {
            switch (expression)
            {
                case FormulaCell cell:
                    yield return cell.Reference.Address;
                    break;
                case FormulaBinary binary:
                    pending.Push(binary.Right);
                    pending.Push(binary.Left);
                    break;
                case FormulaFunction function:
                    for (int index = function.Arguments.Length - 1; index >= 0; index--)
                    {
                        pending.Push(function.Arguments[index]);
                    }

                    break;
            }
        }
    }

    internal static IEnumerable<FormulaRangeAddress> EnumerateRangeReferences(FormulaExpression root)
    {
        Stack<FormulaExpression> pending = new();
        pending.Push(root);
        while (pending.TryPop(out FormulaExpression? expression))
        {
            switch (expression)
            {
                case FormulaRange range:
                    yield return range.Reference.Address;
                    break;
                case FormulaBinary binary:
                    pending.Push(binary.Right);
                    pending.Push(binary.Left);
                    break;
                case FormulaFunction function:
                    for (int index = function.Arguments.Length - 1; index >= 0; index--)
                    {
                        pending.Push(function.Arguments[index]);
                    }

                    break;
            }
        }
    }

    internal static FormulaCellAddress Normalize(FormulaCellAddress address) =>
        address with { ColumnName = address.NormalizedColumnName };

    private static FormulaRangeAddress Normalize(FormulaRangeAddress address) => address with
    {
        StartColumnName = address.StartColumnName.ToUpperInvariant(),
        EndColumnName = address.EndColumnName.ToUpperInvariant(),
    };

    internal static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string HashPart(OpenXmlPart part)
    {
        using Stream stream = part.GetStream(FileMode.Open, FileAccess.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static IEnumerable<OpenXmlPart> EnumerateParts(OpenXmlPartContainer root)
    {
        HashSet<Uri> visited = [];
        Stack<OpenXmlPartContainer> pending = new();
        pending.Push(root);
        while (pending.TryPop(out OpenXmlPartContainer? container))
        {
            foreach (IdPartPair pair in container.Parts)
            {
                OpenXmlPart part = pair.OpenXmlPart;
                if (!visited.Add(part.Uri))
                {
                    continue;
                }

                yield return part;
                pending.Push(part);
            }
        }
    }
}

internal sealed record PreservedWorksheetIdentity(
    int Ordinal,
    string Name,
    uint SheetId,
    string State,
    string RelationshipId,
    string WorksheetSha256);

internal sealed record PreservedPartIdentity(
    int Ordinal,
    string Uri,
    string ContentType,
    string Sha256);

public sealed record OutputPackageValidationError(
    string Code,
    FormulaIdentity Identity,
    string ActualDimension,
    string Limit)
{
    public override string ToString() =>
        $"{nameof(OutputPackageValidationError)} {{ Code = {Code}, NodeKind = {Identity.NodeKind}, NodeId = {Identity.NodeId}, Field = {Identity.Field}, Content = <redacted> }}";
}

public sealed class OutputPackageValidationResult
{
    public OutputPackageValidationResult(IEnumerable<OutputPackageValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors.ToImmutableArray();
    }

    public ImmutableArray<OutputPackageValidationError> Errors { get; }

    public bool IsValid => Errors.IsEmpty;

    public static OutputPackageValidationResult Valid { get; } = new([]);

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new OutputPackageValidationException(Errors);
        }
    }

    public override string ToString() =>
        $"{nameof(OutputPackageValidationResult)} {{ IsValid = {IsValid}, ErrorCount = {Errors.Length}, Content = <redacted> }}";
}

public sealed class OutputPackageValidationException : Exception
{
    public OutputPackageValidationException(IEnumerable<OutputPackageValidationError> errors)
        : this(errors.ToImmutableArray())
    {
    }

    private OutputPackageValidationException(ImmutableArray<OutputPackageValidationError> errors)
        : base($"The output workbook has {errors.Length.ToString(CultureInfo.InvariantCulture)} validation error(s).")
    {
        Errors = errors;
    }

    public ImmutableArray<OutputPackageValidationError> Errors { get; }

    public override string ToString() =>
        $"{nameof(OutputPackageValidationException)} {{ ErrorCount = {Errors.Length}, Content = <redacted> }}";
}

public interface IOutputPackageValidator
{
    OutputPackageValidationResult Validate(
        string workingPackagePath,
        OutputPackageValidationPlan plan,
        CancellationToken cancellationToken = default);
}

public sealed class OutputPackageValidator : IOutputPackageValidator
{
    private static readonly FormulaIdentity WorkbookIdentity =
        new("Workbook", "<workbook>", "<redacted>", "Package");

    private readonly FormulaSerializer serializer = new();

    public OutputPackageValidationResult Validate(
        string workingPackagePath,
        OutputPackageValidationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingPackagePath);
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        List<OutputPackageValidationError> packageErrors = [];
        try
        {
            string canonicalPath = Path.GetFullPath(workingPackagePath);
            FileFormatClassificationResult classification = new FileFormatClassifier().Classify(canonicalPath);
            if (!classification.IsAccepted)
            {
                Add(
                    packageErrors,
                    "PACKAGE_CLASSIFICATION_INVALID",
                    WorkbookIdentity,
                    classification.Classification.ToString(),
                    FileFormatClassification.StandardXlsx.ToString());
            }

            cancellationToken.ThrowIfCancellationRequested();
            using FileStream stream = new(
                canonicalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            using SpreadsheetDocument document = SpreadsheetDocument.Open(
                stream,
                false,
                new OpenSettings
                {
                    AutoSave = false,
                    MaxCharactersInPart = FileFormatClassifier.MaxCharactersInPart,
                });
            OutputPackageValidationResult opened = ValidateOpenedDocument(
                document,
                plan,
                cancellationToken);
            return packageErrors.Count == 0
                ? opened
                : new OutputPackageValidationResult(packageErrors.Concat(opened.Errors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or OpenXmlPackageException
            or XmlException
            or FormatException
            or OverflowException)
        {
            packageErrors.Add(new OutputPackageValidationError(
                "PACKAGE_REOPEN_FAILED",
                WorkbookIdentity,
                "unreadable",
                "read-only reopen"));
            return new OutputPackageValidationResult(packageErrors);
        }
    }

    public override string ToString() =>
        $"{nameof(OutputPackageValidator)} {{ Content = <redacted> }}";

    private OutputPackageValidationResult ValidateOpenedDocument(
        SpreadsheetDocument document,
        OutputPackageValidationPlan plan,
        CancellationToken cancellationToken)
    {
        List<OutputPackageValidationError> errors = [];
        ValidateSchema(document, errors, cancellationToken);
        ValidatePackageSafety(document, errors, cancellationToken);

        WorkbookPart? workbookPart = document.WorkbookPart;
        Workbook? workbook = workbookPart?.Workbook;
        Sheets? sheets = workbook?.GetFirstChild<Sheets>();
        if (workbookPart is null || workbook is null || sheets is null)
        {
            Add(errors, "WORKBOOK_STRUCTURE_MISSING", WorkbookIdentity, "missing", "workbook/sheets");
            return new OutputPackageValidationResult(errors);
        }

        Dictionary<string, WorksheetPart> boundWorksheets = ValidateSheetSet(
            workbookPart,
            sheets,
            plan,
            errors,
            cancellationToken);
        ValidatePreservedParts(document, plan, errors, cancellationToken);
        ValidateDefinedNames(workbook, plan, errors);
        ValidateCalculationProperties(workbook, errors);
        ValidateExpectedFormulaPlan(plan, errors);

        if (boundWorksheets.TryGetValue(plan.SheetNames.ConfigSheetName, out WorksheetPart? configPart))
        {
            ValidateExpectedFormulas(
                configPart.Worksheet!,
                plan.SheetNames.ConfigSheetName,
                plan,
                errors,
                cancellationToken);
        }

        if (boundWorksheets.TryGetValue(plan.SheetNames.ReferencesSheetName, out WorksheetPart? referencesPart))
        {
            ValidateFormulaFreeSheet(referencesPart.Worksheet!, "References", errors);
        }

        if (boundWorksheets.TryGetValue(plan.SheetNames.RunSheetName, out WorksheetPart? runPart))
        {
            ValidateFormulaFreeSheet(runPart.Worksheet!, "Run", errors);
            ValidateRunBindings(runPart.Worksheet!, plan.SheetNames, errors);
        }

        if (boundWorksheets.TryGetValue(plan.SheetNames.ResultsSheetName, out WorksheetPart? resultsPart))
        {
            ValidateExpectedFormulas(
                resultsPart.Worksheet!,
                plan.SheetNames.ResultsSheetName,
                plan,
                errors,
                cancellationToken);
        }

        return new OutputPackageValidationResult(errors);
    }

    private static void ValidateSchema(
        SpreadsheetDocument document,
        List<OutputPackageValidationError> errors,
        CancellationToken cancellationToken)
    {
        OpenXmlValidator validator = new()
        {
            MaxNumberOfErrors = 1_000,
        };
        int count = 0;
        foreach (ValidationErrorInfo _ in validator.Validate(document, cancellationToken))
        {
            count++;
        }

        if (count > 0)
        {
            Add(
                errors,
                "OPEN_XML_SCHEMA_INVALID",
                WorkbookIdentity,
                count.ToString(CultureInfo.InvariantCulture),
                "0");
        }
    }

    private static void ValidatePackageSafety(
        SpreadsheetDocument document,
        List<OutputPackageValidationError> errors,
        CancellationToken cancellationToken)
    {
        if (document.DocumentType != SpreadsheetDocumentType.Workbook)
        {
            Add(errors, "WORKBOOK_TYPE_INVALID", WorkbookIdentity, "non-standard", "xlsx workbook");
        }

        int externalRelationshipCount = document.ExternalRelationships.Count()
            + document.HyperlinkRelationships.Count();
        bool unsafePartFound = false;
        bool macroPartFound = false;
        foreach (OpenXmlPart part in OutputPackageValidationPlan.EnumerateParts(document))
        {
            cancellationToken.ThrowIfCancellationRequested();
            externalRelationshipCount += part.ExternalRelationships.Count()
                + part.HyperlinkRelationships.Count();
            string marker = part.ContentType + "|" + part.Uri.OriginalString;
            macroPartFound |= marker.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase)
                || marker.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)
                || marker.Contains("activeX", StringComparison.OrdinalIgnoreCase);
            unsafePartFound |= marker.Contains("externalLink", StringComparison.OrdinalIgnoreCase)
                || marker.Contains("connections", StringComparison.OrdinalIgnoreCase)
                || marker.Contains("queryTable", StringComparison.OrdinalIgnoreCase)
                || marker.Contains("oleObject", StringComparison.OrdinalIgnoreCase);
        }

        if (externalRelationshipCount > 0)
        {
            Add(
                errors,
                "EXTERNAL_RELATIONSHIP_PRESENT",
                WorkbookIdentity,
                externalRelationshipCount.ToString(CultureInfo.InvariantCulture),
                "0");
        }

        if (macroPartFound)
        {
            Add(errors, "MACRO_PART_PRESENT", WorkbookIdentity, "present", "absent");
        }

        if (unsafePartFound)
        {
            Add(errors, "EXTERNAL_OR_ACTIVE_PART_PRESENT", WorkbookIdentity, "present", "absent");
        }
    }

    private static Dictionary<string, WorksheetPart> ValidateSheetSet(
        WorkbookPart workbookPart,
        Sheets sheets,
        OutputPackageValidationPlan plan,
        List<OutputPackageValidationError> errors,
        CancellationToken cancellationToken)
    {
        Sheet[] actual = sheets.Elements<Sheet>().ToArray();
        int expectedCount = plan.PreservedWorksheets.Length + 4;
        if (actual.Length != expectedCount)
        {
            Add(
                errors,
                "WORKSHEET_COUNT_MISMATCH",
                WorkbookIdentity with { Field = "Sheets" },
                actual.Length.ToString(CultureInfo.InvariantCulture),
                expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        if (actual.Select(sheet => sheet.Name?.Value ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != actual.Length)
        {
            Add(errors, "WORKSHEET_NAME_DUPLICATE", WorkbookIdentity with { Field = "Sheets" }, "duplicate", "unique");
        }

        for (int index = 0; index < plan.PreservedWorksheets.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreservedWorksheetIdentity expected = plan.PreservedWorksheets[index];
            FormulaIdentity identity = new(
                "PreservedSheet",
                expected.Ordinal.ToString(CultureInfo.InvariantCulture),
                "<redacted>",
                "Sheet");
            if (index >= actual.Length)
            {
                Add(errors, "PRESERVED_SHEET_MISSING", identity, "missing", "present");
                continue;
            }

            Sheet sheet = actual[index];
            string actualName = sheet.Name?.Value ?? string.Empty;
            string actualRelationshipId = sheet.Id?.Value ?? string.Empty;
            bool metadataMatches = string.Equals(actualName, expected.Name, StringComparison.Ordinal)
                && sheet.SheetId?.Value == expected.SheetId
                && string.Equals(sheet.State?.InnerText ?? string.Empty, expected.State, StringComparison.Ordinal)
                && string.Equals(actualRelationshipId, expected.RelationshipId, StringComparison.Ordinal);
            if (!metadataMatches)
            {
                Add(errors, "PRESERVED_SHEET_METADATA_CHANGED", identity, "changed", "exact original identity");
                continue;
            }

            if (!TryGetWorksheetPart(workbookPart, sheet, out WorksheetPart? worksheetPart)
                || worksheetPart?.Worksheet is null)
            {
                Add(errors, "PRESERVED_SHEET_PART_MISSING", identity, "missing", "worksheet part");
                continue;
            }

            string actualHash = OutputPackageValidationPlan.HashText(worksheetPart.Worksheet.OuterXml);
            if (!string.Equals(actualHash, expected.WorksheetSha256, StringComparison.Ordinal))
            {
                Add(errors, "PRESERVED_SHEET_CHANGED", identity, "changed", "exact original worksheet");
            }
        }

        HashSet<string> expectedAdded = new(
            plan.SheetNames.AllSheetNames,
            StringComparer.Ordinal);
        string[] actualAdded = actual
            .Skip(plan.PreservedWorksheets.Length)
            .Select(sheet => sheet.Name?.Value ?? string.Empty)
            .ToArray();
        if (actualAdded.Length != expectedAdded.Count
            || !actualAdded.ToHashSet(StringComparer.Ordinal).SetEquals(expectedAdded))
        {
            Add(errors, "APP_OWNED_SHEET_BINDING_MISMATCH", WorkbookIdentity with { Field = "Sheets" }, "mismatch", "exact bindings");
        }

        Dictionary<string, WorksheetPart> bound = new(StringComparer.Ordinal);
        foreach (string expectedName in plan.SheetNames.AllSheetNames)
        {
            Sheet[] matches = actual
                .Where(sheet => string.Equals(sheet.Name?.Value, expectedName, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1
                || !TryGetWorksheetPart(workbookPart, matches.SingleOrDefault(), out WorksheetPart? worksheetPart)
                || worksheetPart?.Worksheet is null)
            {
                Add(
                    errors,
                    "APP_OWNED_SHEET_MISSING",
                    BindingIdentity(expectedName, plan.SheetNames),
                    matches.Length.ToString(CultureInfo.InvariantCulture),
                    "1");
                continue;
            }

            bound.Add(expectedName, worksheetPart);
        }

        return bound;
    }

    private static void ValidateDefinedNames(
        Workbook workbook,
        OutputPackageValidationPlan plan,
        List<OutputPackageValidationError> errors)
    {
        string actual = workbook.GetFirstChild<DefinedNames>()?.OuterXml ?? string.Empty;
        if (!string.Equals(
            OutputPackageValidationPlan.HashText(actual),
            plan.DefinedNamesSha256,
            StringComparison.Ordinal))
        {
            Add(errors, "DEFINED_NAMES_CHANGED", WorkbookIdentity with { Field = "DefinedNames" }, "changed", "exact original set");
        }
    }

    private static void ValidatePreservedParts(
        SpreadsheetDocument document,
        OutputPackageValidationPlan plan,
        List<OutputPackageValidationError> errors,
        CancellationToken cancellationToken)
    {
        Dictionary<string, OpenXmlPart> actualParts = OutputPackageValidationPlan.EnumerateParts(document)
            .ToDictionary(part => part.Uri.OriginalString, StringComparer.Ordinal);
        foreach (PreservedPartIdentity expected in plan.PreservedParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FormulaIdentity identity = new(
                "PreservedPart",
                expected.Ordinal.ToString(CultureInfo.InvariantCulture),
                "<redacted>",
                "PackagePart");
            if (!actualParts.TryGetValue(expected.Uri, out OpenXmlPart? actual))
            {
                Add(errors, "PRESERVED_PART_MISSING", identity, "missing", "present");
                continue;
            }

            if (!string.Equals(actual.ContentType, expected.ContentType, StringComparison.Ordinal)
                || !string.Equals(
                    OutputPackageValidationPlan.HashPart(actual),
                    expected.Sha256,
                    StringComparison.Ordinal))
            {
                Add(errors, "PRESERVED_PART_CHANGED", identity, "changed", "exact original part");
            }
        }
    }

    private static void ValidateCalculationProperties(
        Workbook workbook,
        List<OutputPackageValidationError> errors)
    {
        CalculationProperties? properties = workbook.CalculationProperties;
        if (properties?.CalculationMode?.Value != CalculateModeValues.Auto)
        {
            Add(errors, "CALCULATION_MODE_INVALID", WorkbookIdentity with { Field = "CalculationMode" }, "invalid", "Auto");
        }

        if (properties?.FullCalculationOnLoad?.Value != true)
        {
            Add(errors, "FULL_CALCULATION_ON_LOAD_REQUIRED", WorkbookIdentity with { Field = "FullCalculationOnLoad" }, "false", "true");
        }

        if (properties?.ForceFullCalculation?.Value != true)
        {
            Add(errors, "FORCE_FULL_CALCULATION_REQUIRED", WorkbookIdentity with { Field = "ForceFullCalculation" }, "false", "true");
        }
    }

    private static void ValidateExpectedFormulaPlan(
        OutputPackageValidationPlan plan,
        List<OutputPackageValidationError> errors)
    {
        if (plan.ExpectedFormulaCells.IsEmpty)
        {
            Add(errors, "EXPECTED_FORMULA_SET_EMPTY", WorkbookIdentity with { Field = "Formulas" }, "0", ">=1");
            return;
        }

        foreach (ExpectedFormulaCell expected in plan.ExpectedFormulaCells)
        {
            FormulaCellDefinition definition = expected.Definition;
            if (!string.Equals(definition.Target.SheetName, plan.SheetNames.ConfigSheetName, StringComparison.Ordinal)
                && !string.Equals(definition.Target.SheetName, plan.SheetNames.ResultsSheetName, StringComparison.Ordinal))
            {
                Add(errors, "FORMULA_TARGET_SHEET_INVALID", definition.Identity, "mismatch", "bound Config/Results sheet");
            }

            foreach (FormulaCellAddress reference in OutputPackageValidationPlan.EnumerateCellReferences(definition.Expression))
            {
                if (!string.Equals(reference.SheetName, plan.SheetNames.ConfigSheetName, StringComparison.Ordinal)
                    && !string.Equals(reference.SheetName, plan.SheetNames.ResultsSheetName, StringComparison.Ordinal))
                {
                    Add(errors, "FORMULA_REFERENCE_SHEET_INVALID", definition.Identity, "mismatch", "bound Config/Results sheet");
                }
            }

            foreach (FormulaRangeAddress reference in OutputPackageValidationPlan.EnumerateRangeReferences(definition.Expression))
            {
                if (!string.Equals(reference.SheetName, plan.SheetNames.ConfigSheetName, StringComparison.Ordinal)
                    && !string.Equals(reference.SheetName, plan.SheetNames.ResultsSheetName, StringComparison.Ordinal))
                {
                    Add(errors, "FORMULA_REFERENCE_SHEET_INVALID", definition.Identity, "mismatch", "bound Config/Results sheet");
                }
            }
        }

        FormulaPreflightResult preflight = new FormulaPreflightValidator().Validate(
            plan.ExpectedFormulaCells.Select(expected => expected.Definition),
            new FormulaPreflightContext(
                plan.SheetNames.AllSheetNames,
                plan.VerifiedCells,
                plan.VerifiedRanges));
        foreach (FormulaPreflightError error in preflight.Errors)
        {
            Add(errors, error.Code, error.Identity, error.ActualDimension, error.Limit);
        }
    }

    private static void ValidateFormulaFreeSheet(
        Worksheet worksheet,
        string bindingKind,
        List<OutputPackageValidationError> errors)
    {
        int formulaCount = worksheet.Descendants<CellFormula>().Count();
        if (formulaCount > 0)
        {
            Add(
                errors,
                "FORMULA_OUTSIDE_RESULTS",
                new FormulaIdentity(bindingKind, $"<{bindingKind.ToLowerInvariant()}>", "<redacted>", "Formula"),
                formulaCount.ToString(CultureInfo.InvariantCulture),
                "0");
        }
    }

    private static void ValidateRunBindings(
        Worksheet worksheet,
        AppOwnedSheetNames sheetNames,
        List<OutputPackageValidationError> errors)
    {
        Dictionary<string, string> expected = new(StringComparer.Ordinal)
        {
            ["ConfigSheetName"] = sheetNames.ConfigSheetName,
            ["ReferencesSheetName"] = sheetNames.ReferencesSheetName,
            ["ResultsSheetName"] = sheetNames.ResultsSheetName,
            ["RunSheetName"] = sheetNames.RunSheetName,
        };
        Dictionary<string, List<string>> actual = new(StringComparer.Ordinal);
        foreach (Row row in worksheet.Descendants<Row>())
        {
            string field = CellText(row, "A");
            if (!expected.ContainsKey(field))
            {
                continue;
            }

            if (!actual.TryGetValue(field, out List<string>? values))
            {
                values = [];
                actual.Add(field, values);
            }

            values.Add(CellText(row, "B"));
        }

        foreach ((string field, string expectedValue) in expected)
        {
            if (!actual.TryGetValue(field, out List<string>? values)
                || values.Count != 1
                || !string.Equals(values[0], expectedValue, StringComparison.Ordinal))
            {
                Add(
                    errors,
                    "RUN_SHEET_BINDING_MISMATCH",
                    new FormulaIdentity("Run", "<run>", "<redacted>", field),
                    values?.Count.ToString(CultureInfo.InvariantCulture) ?? "0",
                    "1 exact binding");
            }
        }
    }

    private void ValidateExpectedFormulas(
        Worksheet worksheet,
        string sheetName,
        OutputPackageValidationPlan plan,
        List<OutputPackageValidationError> errors,
        CancellationToken cancellationToken)
    {
        Dictionary<FormulaCellAddress, ExpectedFormulaCell> expectedByTarget = [];
        foreach (ExpectedFormulaCell expected in plan.ExpectedFormulaCells.Where(expected => string.Equals(
                     expected.Definition.Target.SheetName,
                     sheetName,
                     StringComparison.Ordinal)))
        {
            FormulaCellAddress target = OutputPackageValidationPlan.Normalize(expected.Definition.Target);
            if (!expectedByTarget.TryAdd(target, expected))
            {
                Add(errors, "DUPLICATE_FORMULA_TARGET", expected.Definition.Identity, "duplicate", "unique");
            }
        }

        HashSet<FormulaCellAddress> found = [];
        foreach (Cell cell in worksheet.Descendants<Cell>().Where(cell => cell.CellFormula is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryParseCellReference(cell.CellReference?.Value, out string columnName, out int rowNumber))
            {
                Add(errors, "FORMULA_TARGET_INVALID", WorkbookIdentity with { Field = "FormulaTarget" }, "invalid", "A1 reference");
                continue;
            }

            FormulaCellAddress target = new(sheetName, columnName, rowNumber);
            target = OutputPackageValidationPlan.Normalize(target);
            if (!expectedByTarget.TryGetValue(target, out ExpectedFormulaCell? expected))
            {
                Add(
                    errors,
                    "UNEXPECTED_FORMULA_CELL",
                    new FormulaIdentity("Formula", columnName + rowNumber.ToString(CultureInfo.InvariantCulture), "<redacted>", "Formula"),
                    "unexpected",
                    "expected target");
                continue;
            }

            found.Add(target);
            string actualFormula = cell.CellFormula?.Text ?? string.Empty;
            if (actualFormula.StartsWith("=", StringComparison.Ordinal))
            {
                Add(errors, "FORMULA_HAS_LEADING_EQUALS", expected.Definition.Identity, "present", "absent");
            }

            int serializedLength = checked(actualFormula.Length + 1);
            if (serializedLength > FormulaPreflightValidator.MaximumFormulaLength)
            {
                Add(
                    errors,
                    "FORMULA_LENGTH_EXCEEDED",
                    expected.Definition.Identity,
                    serializedLength.ToString(CultureInfo.InvariantCulture),
                    FormulaPreflightValidator.MaximumFormulaLength.ToString(CultureInfo.InvariantCulture));
            }

            if (LooksExternalOrDde(actualFormula))
            {
                Add(errors, "FORMULA_EXTERNAL_OR_DDE_REFERENCE", expected.Definition.Identity, "present", "absent");
            }

            string? expectedFormula = null;
            try
            {
                string serialized = serializer.Serialize(expected.Definition.Expression);
                expectedFormula = serialized[1..];
            }
            catch (Exception exception) when (exception is ArgumentOutOfRangeException
                or NullReferenceException)
            {
                Add(errors, "UNSUPPORTED_FORMULA_EXPRESSION", expected.Definition.Identity, "unsupported", "closed AST");
            }

            if (expectedFormula is not null
                && !string.Equals(actualFormula, expectedFormula, StringComparison.Ordinal))
            {
                Add(errors, "FORMULA_SERIALIZATION_MISMATCH", expected.Definition.Identity, "mismatch", "exact expected formula");
            }

            ValidateCachedValue(cell, expected, errors);
        }

        foreach ((FormulaCellAddress target, ExpectedFormulaCell expected) in expectedByTarget)
        {
            if (!found.Contains(target))
            {
                Add(errors, "EXPECTED_FORMULA_CELL_MISSING", expected.Definition.Identity, "missing", "present");
            }
        }
    }

    private static void ValidateCachedValue(
        Cell cell,
        ExpectedFormulaCell expected,
        List<OutputPackageValidationError> errors)
    {
        if (expected.CachedValue is decimal expectedValue)
        {
            bool isValid = cell.DataType?.Value == CellValues.Number
                && decimal.TryParse(
                    cell.CellValue?.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out decimal actualValue)
                && actualValue == expectedValue;
            if (!isValid)
            {
                Add(errors, "FORMULA_CACHE_MISMATCH", expected.Definition.Identity, "mismatch", "exact numeric preview");
            }

            return;
        }

        if (cell.CellValue is not null || cell.DataType is not null)
        {
            Add(errors, "FORMULA_BLANK_CACHE_INVALID", expected.Definition.Identity, "nonblank", "blank");
        }
    }

    private static bool TryGetWorksheetPart(
        WorkbookPart workbookPart,
        Sheet? sheet,
        out WorksheetPart? worksheetPart)
    {
        worksheetPart = null;
        string relationshipId = sheet?.Id?.Value ?? string.Empty;
        if (relationshipId.Length == 0)
        {
            return false;
        }

        try
        {
            worksheetPart = workbookPart.GetPartById(relationshipId) as WorksheetPart;
            return worksheetPart is not null;
        }
        catch (Exception exception) when (exception is ArgumentException
            or KeyNotFoundException
            or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryParseCellReference(
        string? reference,
        out string columnName,
        out int rowNumber)
    {
        columnName = string.Empty;
        rowNumber = 0;
        if (string.IsNullOrEmpty(reference))
        {
            return false;
        }

        int separator = 0;
        while (separator < reference.Length && char.IsAsciiLetter(reference[separator]))
        {
            separator++;
        }

        if (separator is < 1 or > 3
            || separator == reference.Length
            || !int.TryParse(reference.AsSpan(separator), NumberStyles.None, CultureInfo.InvariantCulture, out rowNumber)
            || rowNumber is < 1 or > FormulaPreflightValidator.MaximumExcelRow)
        {
            return false;
        }

        columnName = reference[..separator].ToUpperInvariant();
        int columnNumber = 0;
        foreach (char character in columnName)
        {
            columnNumber = checked((columnNumber * 26) + (character - 'A' + 1));
        }

        return columnNumber <= FormulaPreflightValidator.MaximumExcelColumn;
    }

    private static bool LooksExternalOrDde(string formula) =>
        formula.IndexOfAny(['[', ']', '|']) >= 0
        || formula.Contains("://", StringComparison.Ordinal)
        || formula.Contains("file:", StringComparison.OrdinalIgnoreCase)
        || formula.Contains("\\\\", StringComparison.Ordinal);

    private static FormulaIdentity BindingIdentity(
        string sheetName,
        AppOwnedSheetNames names) =>
        string.Equals(sheetName, names.ConfigSheetName, StringComparison.Ordinal)
            ? new FormulaIdentity("Config", "<config>", "<redacted>", "Sheet")
            : string.Equals(sheetName, names.ResultsSheetName, StringComparison.Ordinal)
                ? new FormulaIdentity("Results", "<results>", "<redacted>", "Sheet")
                : new FormulaIdentity("Run", "<run>", "<redacted>", "Sheet");

    private static string CellText(Row row, string columnName)
    {
        Cell? cell = row.Elements<Cell>().FirstOrDefault(candidate => string.Equals(
            CellColumn(candidate.CellReference?.Value),
            columnName,
            StringComparison.Ordinal));
        return cell?.InnerText ?? string.Empty;
    }

    private static string CellColumn(string? reference) =>
        new((reference ?? string.Empty).TakeWhile(char.IsAsciiLetter).ToArray());

    private static OutputPackageValidationResult Failure(
        string code,
        FormulaIdentity identity,
        string actual,
        string limit) =>
        new([new OutputPackageValidationError(code, identity, actual, limit)]);

    private static void Add(
        List<OutputPackageValidationError> errors,
        string code,
        FormulaIdentity identity,
        string actual,
        string limit) =>
        errors.Add(new OutputPackageValidationError(code, identity, actual, limit));
}