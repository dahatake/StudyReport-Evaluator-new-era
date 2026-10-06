using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace StudyReportEvaluator.App.Workbooks.Writing;

public sealed class AppOwnedSheetNames
{
    internal AppOwnedSheetNames(
        string configSheetName,
        string referencesSheetName,
        string resultsSheetName,
        string runSheetName)
    {
        ConfigSheetName = configSheetName;
        ReferencesSheetName = referencesSheetName;
        ResultsSheetName = resultsSheetName;
        RunSheetName = runSheetName;
        AllSheetNames = Array.AsReadOnly(
            [ConfigSheetName, ReferencesSheetName, ResultsSheetName, RunSheetName]);
    }

    public string ConfigSheetName { get; }

    public string ReferencesSheetName { get; }

    public string ResultsSheetName { get; }

    public string RunSheetName { get; }

    public IReadOnlyList<string> AllSheetNames { get; }

    public override string ToString() =>
        $"{nameof(AppOwnedSheetNames)} {{ Count = {AllSheetNames.Count}, Content = <redacted> }}";
}

public sealed class AppOwnedSheetNameResolver
{
    public const int MaximumSheetNameLength = 31;
    public const string ConfigBaseName = "Quantification_Config";
    public const string ReferencesBaseName = "Quantification_References";
    public const string ResultsBaseName = "Quantification_Results";
    public const string RunBaseName = "Quantification_Run";

    private static readonly char[] InvalidWorksheetNameCharacters = ['[', ']', ':', '*', '?', '/', '\\'];

    public AppOwnedSheetNames Resolve(SpreadsheetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The workbook part is missing.");
        return Resolve(workbookPart);
    }

    public AppOwnedSheetNames Resolve(WorkbookPart workbookPart)
    {
        ArgumentNullException.ThrowIfNull(workbookPart);
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("The workbook root is missing.");
        string[] existingNames = workbook.Descendants<Sheet>()
            .Select(sheet => sheet.Name?.Value ?? throw new InvalidDataException("A worksheet name is missing."))
            .ToArray();
        return Resolve(existingNames);
    }

    public AppOwnedSheetNames Resolve(IEnumerable<string> existingSheetNames)
    {
        ArgumentNullException.ThrowIfNull(existingSheetNames);
        HashSet<string> allocated = new(existingSheetNames, StringComparer.OrdinalIgnoreCase);

        string config = ResolveAndReserve(ConfigBaseName, allocated);
        string references = ResolveAndReserve(ReferencesBaseName, allocated);
        string results = ResolveAndReserve(ResultsBaseName, allocated);
        string run = ResolveAndReserve(RunBaseName, allocated);
        return new AppOwnedSheetNames(config, references, results, run);
    }

    public string ResolveUniqueName(string baseName, IEnumerable<string> existingSheetNames)
    {
        ArgumentNullException.ThrowIfNull(existingSheetNames);
        HashSet<string> allocated = new(existingSheetNames, StringComparer.OrdinalIgnoreCase);
        return ResolveAndReserve(baseName, allocated);
    }

    public static bool IsValidWorksheetName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaximumSheetNameLength
        && value.IndexOfAny(InvalidWorksheetNameCharacters) < 0
        && !value.Any(char.IsControl)
        && value[0] != '\''
        && value[^1] != '\''
        && !string.Equals(value, "History", StringComparison.OrdinalIgnoreCase);

    private static string ResolveAndReserve(string baseName, HashSet<string> allocated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        if (!IsValidWorksheetName(baseName))
        {
            throw new ArgumentException("The worksheet base name is invalid or reserved.", nameof(baseName));
        }

        if (allocated.Add(baseName))
        {
            return baseName;
        }

        for (long suffixNumber = 2; suffixNumber < long.MaxValue; suffixNumber++)
        {
            string suffix = $" ({suffixNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            int availableLength = MaximumSheetNameLength - suffix.Length;
            if (availableLength <= 0)
            {
                break;
            }

            string prefix = TruncateWithoutSplittingSurrogate(baseName, availableLength);
            string candidate = prefix + suffix;
            if (IsValidWorksheetName(candidate) && allocated.Add(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("A unique valid worksheet name could not be allocated.");
    }

    private static string TruncateWithoutSplittingSurrogate(string value, int maximumLength)
    {
        int length = Math.Min(value.Length, maximumLength);
        if (length < value.Length && length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }
}
