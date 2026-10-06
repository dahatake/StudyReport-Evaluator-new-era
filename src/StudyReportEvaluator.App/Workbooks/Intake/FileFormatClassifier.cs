using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace StudyReportEvaluator.App.Workbooks.Intake;

public enum FileFormatClassification
{
    StandardXlsx,
    UnsupportedExtension,
    LegacyBinaryWorkbook,
    CommaSeparatedValues,
    PortableDocumentFormat,
    MacroEnabledWorkbook,
    EncryptedOrRightsProtected,
    InvalidZipSignature,
    CorruptPackage,
    UnsafePackage,
    MissingRequiredPart,
    InvalidContentType,
    InvalidRelationship,
    UnsupportedSpreadsheetType,
}

public sealed class FileFormatClassificationResult
{
    internal FileFormatClassificationResult(
        FileFormatClassification classification,
        int packagePartCount = 0,
        int relationshipCount = 0)
    {
        Classification = classification;
        PackagePartCount = packagePartCount;
        RelationshipCount = relationshipCount;
    }

    public FileFormatClassification Classification { get; }

    public bool IsAccepted => Classification == FileFormatClassification.StandardXlsx;

    public int PackagePartCount { get; }

    public int RelationshipCount { get; }

    public override string ToString() =>
        $"{nameof(FileFormatClassificationResult)} {{ Classification = {Classification}, IsAccepted = {IsAccepted}, Content = <redacted> }}";
}

public sealed class FileFormatClassifier
{
    public const long MaxInputFileBytes = 100L * 1024 * 1024;
    public const long MaxExpandedPackageBytes = 1024L * 1024 * 1024;
    public const long MaxSingleExpandedEntryBytes = 256L * 1024 * 1024;
    public const int MaxZipEntryCount = 10_000;
    public const int MaxCompressionRatio = 100;
    public const int MaxRelationshipsPerPart = 4_096;
    public const int MaxTotalRelationships = 10_000;
    public const long MaxCharactersInPart = 64L * 1024 * 1024;

    private const long MaxStructuralXmlCharacters = 8L * 1024 * 1024;
    private const string ContentTypesPartName = "[Content_Types].xml";
    private const string RootRelationshipsPartName = "_rels/.rels";
    private const string WorkbookPartName = "xl/workbook.xml";
    private const string WorkbookRelationshipsPartName = "xl/_rels/workbook.xml.rels";
    private const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string PackageRelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string TransitionalSpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string StrictSpreadsheetNamespace = "http://purl.oclc.org/ooxml/spreadsheetml/main";
    private const string TransitionalOfficeRelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string StrictOfficeRelationshipNamespace = "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string TransitionalOfficeDocumentRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string StrictOfficeDocumentRelationship = "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument";
    private const string StandardWorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string WorksheetContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
    private const string RelationshipsContentType = "application/vnd.openxmlformats-package.relationships+xml";
    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly uint[] Crc32Table = CreateCrc32Table();

    public FileFormatClassificationResult Classify(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        FileFormatClassification? extensionClassification = ClassifyExtension(Path.GetExtension(filePath));
        if (extensionClassification is not null)
        {
            return new FileFormatClassificationResult(extensionClassification.Value);
        }

        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        return ClassifyOpenedStream(stream);
    }

    internal FileFormatClassificationResult ClassifyOpenedStream(FileStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek || stream.CanWrite)
        {
            throw new ArgumentException("A seekable read-only file stream is required.", nameof(stream));
        }

        try
        {
            return InspectPackage(stream);
        }
        catch (RejectedPackageException exception)
        {
            return new FileFormatClassificationResult(exception.Classification);
        }
        catch (Exception exception) when (exception is InvalidDataException
            or XmlException
            or OpenXmlPackageException
            or IOException
            or FormatException
            or OverflowException)
        {
            return new FileFormatClassificationResult(FileFormatClassification.CorruptPackage);
        }
    }

    private static FileFormatClassificationResult InspectPackage(FileStream stream)
    {
        if (stream.Length > MaxInputFileBytes)
        {
            Reject(FileFormatClassification.UnsafePackage);
        }

        Span<byte> signature = stackalloc byte[CompoundFileSignature.Length];
        stream.Position = 0;
        int signatureLength = ReadAvailable(stream, signature);
        if (signatureLength >= CompoundFileSignature.Length
            && signature.SequenceEqual(CompoundFileSignature))
        {
            Reject(FileFormatClassification.EncryptedOrRightsProtected);
        }

        if (signatureLength < 4
            || signature[0] != 0x50
            || signature[1] != 0x4B
            || signature[2] != 0x03
            || signature[3] != 0x04)
        {
            Reject(FileFormatClassification.InvalidZipSignature);
        }

        stream.Position = 0;
        using ZipArchive archive = new(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count is 0 or > MaxZipEntryCount)
        {
            Reject(FileFormatClassification.UnsafePackage);
        }

        Dictionary<string, ZipArchiveEntry> entries = ValidateZipEntries(archive);
        RequireEntry(entries, ContentTypesPartName);
        RequireEntry(entries, RootRelationshipsPartName);
        RequireEntry(entries, WorkbookPartName);
        RequireEntry(entries, WorkbookRelationshipsPartName);

        ContentTypeCatalog contentTypes = ReadContentTypes(entries[ContentTypesPartName]);
        ValidateAllPartContentTypes(entries, contentTypes);
        if (contentTypes.ContainsMacroContent
            || entries.Keys.Any(IsMacroPartName))
        {
            Reject(FileFormatClassification.MacroEnabledWorkbook);
        }

        if (!contentTypes.TryGet(WorkbookPartName, out string? workbookContentType)
            || !string.Equals(workbookContentType, StandardWorkbookContentType, StringComparison.OrdinalIgnoreCase))
        {
            Reject(FileFormatClassification.InvalidContentType);
        }

        RelationshipGraph relationships = ReadRelationshipGraph(entries, contentTypes);
        ValidateWorkbookGraph(entries, contentTypes, relationships);
        ValidateWithOpenXmlSdk(stream);

        return new FileFormatClassificationResult(
            FileFormatClassification.StandardXlsx,
            entries.Count,
            relationships.TotalCount);
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateZipEntries(ZipArchive archive)
    {
        Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
        HashSet<string> caseFoldedNames = new(StringComparer.OrdinalIgnoreCase);
        long expandedTotal = 0;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.IsEncrypted)
            {
                Reject(FileFormatClassification.EncryptedOrRightsProtected);
            }

            if (string.IsNullOrEmpty(entry.Name)
                || !TryCanonicalizePackagePath(entry.FullName, out string canonicalName)
                || !entries.TryAdd(canonicalName, entry)
                || !caseFoldedNames.Add(canonicalName))
            {
                Reject(FileFormatClassification.UnsafePackage);
            }

            if (entry.Length > MaxSingleExpandedEntryBytes)
            {
                Reject(FileFormatClassification.UnsafePackage);
            }

            expandedTotal = checked(expandedTotal + entry.Length);
            if (expandedTotal > MaxExpandedPackageBytes
                || ExceedsCompressionRatio(entry.Length, entry.CompressedLength))
            {
                Reject(FileFormatClassification.UnsafePackage);
            }

            ValidateEntryPayload(entry);
        }

        return entries;
    }

    private static bool ExceedsCompressionRatio(long expandedLength, long compressedLength)
    {
        if (expandedLength == 0)
        {
            return false;
        }

        if (compressedLength <= 0)
        {
            return true;
        }

        return expandedLength > compressedLength * MaxCompressionRatio;
    }

    private static void ValidateEntryPayload(ZipArchiveEntry entry)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            using Stream entryStream = entry.Open();
            long totalRead = 0;
            uint crc = uint.MaxValue;
            while (true)
            {
                int read = entryStream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                totalRead = checked(totalRead + read);
                if (totalRead > entry.Length || totalRead > MaxSingleExpandedEntryBytes)
                {
                    Reject(FileFormatClassification.CorruptPackage);
                }

                crc = UpdateCrc32(crc, buffer.AsSpan(0, read));
            }

            if (totalRead != entry.Length || ~crc != entry.Crc32)
            {
                Reject(FileFormatClassification.CorruptPackage);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static ContentTypeCatalog ReadContentTypes(ZipArchiveEntry entry)
    {
        XDocument document = ReadXml(entry);
        XNamespace contentTypesNamespace = ContentTypesNamespace;
        if (document.Root?.Name != contentTypesNamespace + "Types")
        {
            Reject(FileFormatClassification.InvalidContentType);
        }

        Dictionary<string, string> overrides = new(StringComparer.Ordinal);
        Dictionary<string, string> defaults = new(StringComparer.OrdinalIgnoreCase);
        bool containsMacroContent = false;
        int declarationCount = 0;

        foreach (XElement declaration in document.Root.Elements())
        {
            declarationCount++;
            if (declarationCount > MaxZipEntryCount)
            {
                Reject(FileFormatClassification.UnsafePackage);
            }

            string contentType = (string?)declaration.Attribute("ContentType") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(contentType))
            {
                Reject(FileFormatClassification.InvalidContentType);
            }

            containsMacroContent |= IsMacroContentType(contentType);
            if (declaration.Name == contentTypesNamespace + "Override")
            {
                string partName = (string?)declaration.Attribute("PartName") ?? string.Empty;
                if (!partName.StartsWith('/')
                    || !TryCanonicalizePackagePath(partName[1..], out string canonicalPartName)
                    || !overrides.TryAdd(canonicalPartName, contentType))
                {
                    Reject(FileFormatClassification.InvalidContentType);
                }
            }
            else if (declaration.Name == contentTypesNamespace + "Default")
            {
                string extension = (string?)declaration.Attribute("Extension") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(extension)
                    || extension.IndexOfAny(['/', '\\', '.']) >= 0
                    || !defaults.TryAdd(extension, contentType))
                {
                    Reject(FileFormatClassification.InvalidContentType);
                }
            }
            else
            {
                Reject(FileFormatClassification.InvalidContentType);
            }
        }

        return new ContentTypeCatalog(overrides, defaults, containsMacroContent);
    }

    private static void ValidateAllPartContentTypes(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        ContentTypeCatalog contentTypes)
    {
        foreach (string entryName in entries.Keys)
        {
            if (string.Equals(entryName, ContentTypesPartName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!contentTypes.TryGet(entryName, out string? contentType)
                || string.IsNullOrWhiteSpace(contentType))
            {
                Reject(FileFormatClassification.InvalidContentType);
            }

            if (IsRelationshipPart(entryName)
                && !string.Equals(contentType, RelationshipsContentType, StringComparison.OrdinalIgnoreCase))
            {
                Reject(FileFormatClassification.InvalidContentType);
            }
        }
    }

    private static RelationshipGraph ReadRelationshipGraph(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        ContentTypeCatalog contentTypes)
    {
        Dictionary<string, IReadOnlyDictionary<string, RelationshipRecord>> bySource = new(StringComparer.Ordinal);
        int totalCount = 0;

        foreach ((string relationshipPartName, ZipArchiveEntry relationshipEntry) in entries
            .Where(pair => IsRelationshipPart(pair.Key)))
        {
            if (!contentTypes.TryGet(relationshipPartName, out string? contentType)
                || !string.Equals(contentType, RelationshipsContentType, StringComparison.OrdinalIgnoreCase))
            {
                Reject(FileFormatClassification.InvalidRelationship);
            }

            if (!TryGetRelationshipSource(relationshipPartName, out string sourcePartName)
                || (sourcePartName.Length > 0 && !entries.ContainsKey(sourcePartName)))
            {
                Reject(FileFormatClassification.InvalidRelationship);
            }

            XDocument document = ReadXml(relationshipEntry);
            XNamespace relationshipNamespace = PackageRelationshipsNamespace;
            if (document.Root?.Name != relationshipNamespace + "Relationships"
                || bySource.ContainsKey(sourcePartName))
            {
                Reject(FileFormatClassification.InvalidRelationship);
            }

            Dictionary<string, RelationshipRecord> relationships = new(StringComparer.Ordinal);
            foreach (XElement relationship in document.Root.Elements())
            {
                if (relationship.Name != relationshipNamespace + "Relationship"
                    || relationships.Count >= MaxRelationshipsPerPart
                    || totalCount >= MaxTotalRelationships)
                {
                    Reject(FileFormatClassification.UnsafePackage);
                }

                string id = (string?)relationship.Attribute("Id") ?? string.Empty;
                string type = (string?)relationship.Attribute("Type") ?? string.Empty;
                string target = (string?)relationship.Attribute("Target") ?? string.Empty;
                string targetMode = (string?)relationship.Attribute("TargetMode") ?? "Internal";
                if (string.IsNullOrWhiteSpace(id)
                    || !Uri.TryCreate(type, UriKind.Absolute, out _)
                    || !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase)
                    || !TryResolveRelationshipTarget(sourcePartName, target, out string resolvedTarget)
                    || !entries.ContainsKey(resolvedTarget)
                    || !relationships.TryAdd(id, new RelationshipRecord(type, resolvedTarget)))
                {
                    Reject(FileFormatClassification.InvalidRelationship);
                }

                totalCount++;
            }

            bySource.Add(sourcePartName, relationships);
        }

        return new RelationshipGraph(bySource, totalCount);
    }

    private static void ValidateWorkbookGraph(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        ContentTypeCatalog contentTypes,
        RelationshipGraph relationships)
    {
        if (!relationships.BySource.TryGetValue(string.Empty, out IReadOnlyDictionary<string, RelationshipRecord>? rootRelationships)
            || rootRelationships is null)
        {
            Reject(FileFormatClassification.MissingRequiredPart);
        }

        if (!relationships.BySource.TryGetValue(WorkbookPartName, out IReadOnlyDictionary<string, RelationshipRecord>? workbookRelationships)
            || workbookRelationships is null)
        {
            Reject(FileFormatClassification.MissingRequiredPart);
        }

        RelationshipRecord[] officeDocumentRelationships = rootRelationships.Values
            .Where(relationship => string.Equals(relationship.Type, TransitionalOfficeDocumentRelationship, StringComparison.Ordinal)
                || string.Equals(relationship.Type, StrictOfficeDocumentRelationship, StringComparison.Ordinal))
            .ToArray();
        if (officeDocumentRelationships.Length != 1
            || !string.Equals(officeDocumentRelationships[0].Target, WorkbookPartName, StringComparison.Ordinal))
        {
            Reject(FileFormatClassification.InvalidRelationship);
        }

        XDocument workbookDocument = ReadXml(entries[WorkbookPartName]);
        string workbookNamespace = workbookDocument.Root?.Name.NamespaceName ?? string.Empty;
        string relationshipAttributeNamespace;
        string officeDocumentRelationshipType;
        string worksheetRelationshipType;
        if (string.Equals(workbookNamespace, TransitionalSpreadsheetNamespace, StringComparison.Ordinal))
        {
            relationshipAttributeNamespace = TransitionalOfficeRelationshipNamespace;
            officeDocumentRelationshipType = TransitionalOfficeDocumentRelationship;
            worksheetRelationshipType = TransitionalOfficeRelationshipNamespace + "/worksheet";
        }
        else if (string.Equals(workbookNamespace, StrictSpreadsheetNamespace, StringComparison.Ordinal))
        {
            relationshipAttributeNamespace = StrictOfficeRelationshipNamespace;
            officeDocumentRelationshipType = StrictOfficeDocumentRelationship;
            worksheetRelationshipType = StrictOfficeRelationshipNamespace + "/worksheet";
        }
        else
        {
            Reject(FileFormatClassification.UnsupportedSpreadsheetType);
            return;
        }

        if (workbookDocument.Root?.Name.LocalName != "workbook")
        {
            Reject(FileFormatClassification.UnsupportedSpreadsheetType);
        }

        if (!string.Equals(officeDocumentRelationships[0].Type, officeDocumentRelationshipType, StringComparison.Ordinal))
        {
            Reject(FileFormatClassification.InvalidRelationship);
        }

        XNamespace spreadsheetNamespace = workbookNamespace;
        XNamespace relationshipsNamespace = relationshipAttributeNamespace;
        XElement? sheets = workbookDocument.Root?.Element(spreadsheetNamespace + "sheets");
        if (sheets is null)
        {
            Reject(FileFormatClassification.MissingRequiredPart);
        }

        HashSet<string> sheetNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> sheetRelationshipIds = new(StringComparer.Ordinal);
        HashSet<uint> sheetIds = [];
        HashSet<string> worksheetTargets = new(StringComparer.Ordinal);
        int sheetCount = 0;
        foreach (XElement sheet in sheets.Elements(spreadsheetNamespace + "sheet"))
        {
            sheetCount++;
            string name = (string?)sheet.Attribute("name") ?? string.Empty;
            string relationshipId = (string?)sheet.Attribute(relationshipsNamespace + "id") ?? string.Empty;
            string sheetIdText = (string?)sheet.Attribute("sheetId") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)
                || name.Length > 31
                || !sheetNames.Add(name)
                || !sheetRelationshipIds.Add(relationshipId)
                || !uint.TryParse(sheetIdText, NumberStyles.None, CultureInfo.InvariantCulture, out uint sheetId)
                || sheetId == 0
                || !sheetIds.Add(sheetId)
                || !workbookRelationships.TryGetValue(relationshipId, out RelationshipRecord? relationship)
                || relationship is null
                || !string.Equals(relationship.Type, worksheetRelationshipType, StringComparison.Ordinal)
                || !worksheetTargets.Add(relationship.Target)
                || !contentTypes.TryGet(relationship.Target, out string? worksheetContentType)
                || !string.Equals(worksheetContentType, WorksheetContentType, StringComparison.OrdinalIgnoreCase))
            {
                Reject(FileFormatClassification.InvalidRelationship);
            }
        }

        if (sheetCount == 0 || sheets.Elements().Count() != sheetCount)
        {
            Reject(FileFormatClassification.MissingRequiredPart);
        }
    }

    private static void ValidateWithOpenXmlSdk(FileStream stream)
    {
        stream.Position = 0;
        OpenSettings settings = new()
        {
            AutoSave = false,
            MaxCharactersInPart = MaxCharactersInPart,
        };

        using SpreadsheetDocument document = SpreadsheetDocument.Open(stream, false, settings);
        if (document.DocumentType != SpreadsheetDocumentType.Workbook)
        {
            Reject(document.DocumentType == SpreadsheetDocumentType.MacroEnabledWorkbook
                ? FileFormatClassification.MacroEnabledWorkbook
                : FileFormatClassification.UnsupportedSpreadsheetType);
        }

        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new RejectedPackageException(FileFormatClassification.MissingRequiredPart);
        Workbook workbook = workbookPart.Workbook
            ?? throw new RejectedPackageException(FileFormatClassification.MissingRequiredPart);
        Sheet[] sheets = workbook.Descendants<Sheet>().ToArray();
        if (sheets.Length == 0)
        {
            Reject(FileFormatClassification.MissingRequiredPart);
        }

        foreach (Sheet sheet in sheets)
        {
            string relationshipId = sheet.Id?.Value ?? string.Empty;
            if (string.IsNullOrEmpty(relationshipId)
                || workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart
                || !string.Equals(worksheetPart.ContentType, WorksheetContentType, StringComparison.OrdinalIgnoreCase))
            {
                Reject(FileFormatClassification.InvalidRelationship);
            }
        }
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxStructuralXmlCharacters,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        using Stream stream = entry.Open();
        using XmlReader reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static FileFormatClassification? ClassifyExtension(string extension) =>
        extension.ToUpperInvariant() switch
        {
            ".XLSX" => null,
            ".XLS" or ".XLSB" => FileFormatClassification.LegacyBinaryWorkbook,
            ".CSV" => FileFormatClassification.CommaSeparatedValues,
            ".PDF" => FileFormatClassification.PortableDocumentFormat,
            ".XLSM" or ".XLTM" or ".XLAM" => FileFormatClassification.MacroEnabledWorkbook,
            _ => FileFormatClassification.UnsupportedExtension,
        };

    private static void RequireEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string entryName)
    {
        if (!entries.ContainsKey(entryName))
        {
            Reject(FileFormatClassification.MissingRequiredPart);
        }
    }

    private static bool IsMacroContentType(string contentType) =>
        contentType.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)
        || contentType.Contains("activeX", StringComparison.OrdinalIgnoreCase);

    private static bool IsMacroPartName(string partName) =>
        partName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)
        || partName.Contains("/activeX/", StringComparison.OrdinalIgnoreCase);

    private static bool IsRelationshipPart(string packagePath) =>
        string.Equals(packagePath, RootRelationshipsPartName, StringComparison.Ordinal)
        || (packagePath.Contains("/_rels/", StringComparison.Ordinal)
            && packagePath.EndsWith(".rels", StringComparison.OrdinalIgnoreCase));

    private static bool TryGetRelationshipSource(string relationshipPartName, out string sourcePartName)
    {
        if (string.Equals(relationshipPartName, RootRelationshipsPartName, StringComparison.Ordinal))
        {
            sourcePartName = string.Empty;
            return true;
        }

        int marker = relationshipPartName.LastIndexOf("/_rels/", StringComparison.Ordinal);
        if (marker <= 0)
        {
            sourcePartName = string.Empty;
            return false;
        }

        string relationshipFileName = relationshipPartName[(marker + "/_rels/".Length)..];
        if (!relationshipFileName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
            || relationshipFileName.Length == ".rels".Length)
        {
            sourcePartName = string.Empty;
            return false;
        }

        sourcePartName = relationshipPartName[..marker]
            + "/"
            + relationshipFileName[..^".rels".Length];
        return TryCanonicalizePackagePath(sourcePartName, out sourcePartName);
    }

    private static bool TryResolveRelationshipTarget(
        string sourcePartName,
        string target,
        out string resolvedTarget)
    {
        resolvedTarget = string.Empty;
        if (string.IsNullOrWhiteSpace(target)
            || target.Contains('\\')
            || target.Contains('?')
            || target.Contains('#')
            || Uri.TryCreate(target, UriKind.Absolute, out _))
        {
            return false;
        }

        string candidate;
        if (target.StartsWith('/'))
        {
            candidate = target[1..];
        }
        else
        {
            int lastSlash = sourcePartName.LastIndexOf('/');
            string sourceDirectory = lastSlash < 0 ? string.Empty : sourcePartName[..lastSlash];
            candidate = sourceDirectory.Length == 0 ? target : sourceDirectory + "/" + target;
        }

        return TryCanonicalizePackagePath(candidate, out resolvedTarget, allowParentSegments: true);
    }

    private static bool TryCanonicalizePackagePath(
        string path,
        out string canonicalPath,
        bool allowParentSegments = false)
    {
        canonicalPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith('/')
            || path.Contains('\\')
            || path.Contains('\0')
            || path.Contains('?')
            || path.Contains('#')
            || !HasValidPercentTriplets(path))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (decoded.IndexOfAny(['\\', '?', '#', '\0']) >= 0)
        {
            return false;
        }

        List<string> segments = [];
        foreach (string segment in decoded.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                return false;
            }

            if (segment == "..")
            {
                if (!allowParentSegments || segments.Count == 0)
                {
                    return false;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            if (segment.Any(char.IsControl))
            {
                return false;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            return false;
        }

        canonicalPath = string.Join('/', segments);
        return true;
    }

    private static bool HasValidPercentTriplets(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
            {
                continue;
            }

            if (index + 2 >= value.Length
                || !Uri.IsHexDigit(value[index + 1])
                || !Uri.IsHexDigit(value[index + 2]))
            {
                return false;
            }

            index += 2;
        }

        return true;
    }

    private static int ReadAvailable(Stream stream, Span<byte> buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer[totalRead..]);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }

    private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc = Crc32Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] CreateCrc32Table()
    {
        uint[] table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 0
                    ? value >> 1
                    : 0xEDB88320U ^ (value >> 1);
            }

            table[index] = value;
        }

        return table;
    }

    [DoesNotReturn]
    private static void Reject(FileFormatClassification classification) =>
        throw new RejectedPackageException(classification);

    private sealed class RejectedPackageException(FileFormatClassification classification) : Exception
    {
        public FileFormatClassification Classification { get; } = classification;
    }

    private sealed class ContentTypeCatalog(
        IReadOnlyDictionary<string, string> overrides,
        IReadOnlyDictionary<string, string> defaults,
        bool containsMacroContent)
    {
        public bool ContainsMacroContent { get; } = containsMacroContent;

        public bool TryGet(string packagePath, out string? contentType)
        {
            if (overrides.TryGetValue(packagePath, out contentType))
            {
                return true;
            }

            int lastSlash = packagePath.LastIndexOf('/');
            int lastDot = packagePath.LastIndexOf('.');
            if (lastDot <= lastSlash || lastDot == packagePath.Length - 1)
            {
                contentType = null;
                return false;
            }

            return defaults.TryGetValue(packagePath[(lastDot + 1)..], out contentType);
        }
    }

    private sealed record RelationshipRecord(string Type, string Target);

    private sealed class RelationshipGraph(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, RelationshipRecord>> bySource,
        int totalCount)
    {
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, RelationshipRecord>> BySource { get; } = bySource;

        public int TotalCount { get; } = totalCount;
    }
}