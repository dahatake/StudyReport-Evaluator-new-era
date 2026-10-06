using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Workbooks.Intake;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Intake;

// Requirements: FR-002 (AC-002)
public sealed class FileFormatClassifierTests
{
    private readonly FileFormatClassifier classifier = new();

    [Fact]
    public void Valid_synthetic_xlsx_is_accepted_read_only_without_mutation()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        byte[] hashBefore = SHA256.HashData(File.ReadAllBytes(workbook.Path));
        long lengthBefore = new FileInfo(workbook.Path).Length;
        DateTime lastWriteBefore = File.GetLastWriteTimeUtc(workbook.Path);

        FileFormatClassificationResult result = classifier.Classify(workbook.Path);

        Assert.True(result.IsAccepted);
        Assert.Equal(FileFormatClassification.StandardXlsx, result.Classification);
        Assert.True(result.PackagePartCount >= 6);
        Assert.True(result.RelationshipCount >= 3);
        Assert.Equal(lengthBefore, new FileInfo(workbook.Path).Length);
        Assert.Equal(lastWriteBefore.Ticks, File.GetLastWriteTimeUtc(workbook.Path).Ticks);
        Assert.Equal(hashBefore, SHA256.HashData(File.ReadAllBytes(workbook.Path)));
        Assert.Contains("<redacted>", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(workbook.Path, result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("input.xls", FileFormatClassification.LegacyBinaryWorkbook)]
    [InlineData("input.xlsb", FileFormatClassification.LegacyBinaryWorkbook)]
    [InlineData("input.csv", FileFormatClassification.CommaSeparatedValues)]
    [InlineData("input.pdf", FileFormatClassification.PortableDocumentFormat)]
    [InlineData("input.xlsm", FileFormatClassification.MacroEnabledWorkbook)]
    [InlineData("input.txt", FileFormatClassification.UnsupportedExtension)]
    public void Unsupported_extensions_are_rejected_without_opening_content(
        string fileName,
        FileFormatClassification expected)
    {
        FileFormatClassificationResult result = classifier.Classify(fileName);

        Assert.False(result.IsAccepted);
        Assert.Equal(expected, result.Classification);
    }

    [Fact]
    public void Non_zip_content_with_xlsx_extension_is_rejected()
    {
        using TemporaryWorkbook file = X01SyntheticWorkbookFactory.CreateRaw(
            ".xlsx",
            Encoding.UTF8.GetBytes("column-a,column-b\nvalue-a,value-b"));

        FileFormatClassificationResult result = classifier.Classify(file.Path);

        Assert.Equal(FileFormatClassification.InvalidZipSignature, result.Classification);
    }

    [Fact]
    public void Compound_file_with_xlsx_extension_is_classified_as_protected()
    {
        byte[] compoundFile =
        [
            0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1,
            0x00, 0x00, 0x00, 0x00,
        ];
        using TemporaryWorkbook file = X01SyntheticWorkbookFactory.CreateRaw(".xlsx", compoundFile);

        FileFormatClassificationResult result = classifier.Classify(file.Path);

        Assert.Equal(FileFormatClassification.EncryptedOrRightsProtected, result.Classification);
    }

    [Fact]
    public void Macro_enabled_package_is_rejected_even_when_named_xlsx()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create(
            documentType: SpreadsheetDocumentType.MacroEnabledWorkbook);

        FileFormatClassificationResult result = classifier.Classify(workbook.Path);

        Assert.Equal(FileFormatClassification.MacroEnabledWorkbook, result.Classification);
    }

    [Fact]
    public void Missing_required_workbook_relationship_part_is_rejected()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        X01SyntheticWorkbookFactory.DeleteEntry(workbook.Path, "xl/_rels/workbook.xml.rels");

        FileFormatClassificationResult result = classifier.Classify(workbook.Path);

        Assert.Equal(FileFormatClassification.MissingRequiredPart, result.Classification);
    }

    [Fact]
    public void Workbook_relationship_to_missing_part_is_rejected()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        X01SyntheticWorkbookFactory.RewriteXml(
            workbook.Path,
            "xl/_rels/workbook.xml.rels",
            document =>
            {
                XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
                XElement worksheetRelationship = document.Root!
                    .Elements(relationships + "Relationship")
                    .First(element => ((string?)element.Attribute("Type"))?.EndsWith("/worksheet", StringComparison.Ordinal) == true);
                worksheetRelationship.SetAttributeValue("Target", "worksheets/missing.xml");
            });

        FileFormatClassificationResult result = classifier.Classify(workbook.Path);

        Assert.Equal(FileFormatClassification.InvalidRelationship, result.Classification);
    }

    [Fact]
    public void External_relationship_is_rejected_fail_closed()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        X01SyntheticWorkbookFactory.RewriteXml(
            workbook.Path,
            "xl/_rels/workbook.xml.rels",
            document =>
            {
                XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
                XElement worksheetRelationship = document.Root!
                    .Elements(relationships + "Relationship")
                    .First(element => ((string?)element.Attribute("Type"))?.EndsWith("/worksheet", StringComparison.Ordinal) == true);
                worksheetRelationship.SetAttributeValue("TargetMode", "External");
            });

        FileFormatClassificationResult result = classifier.Classify(workbook.Path);

        Assert.Equal(FileFormatClassification.InvalidRelationship, result.Classification);
    }

    [Fact]
    public void Relationship_count_above_the_finite_per_part_limit_is_rejected()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        X01SyntheticWorkbookFactory.RewriteXml(
            workbook.Path,
            "xl/_rels/workbook.xml.rels",
            document =>
            {
                XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
                XElement root = document.Root!;
                string existingTarget = (string?)root
                    .Elements(relationships + "Relationship")
                    .First(element => ((string?)element.Attribute("Type"))?.EndsWith("/worksheet", StringComparison.Ordinal) == true)
                    .Attribute("Target")
                    ?? throw new InvalidDataException("Synthetic worksheet relationship target is missing.");
                int additions = FileFormatClassifier.MaxRelationshipsPerPart + 1 - root.Elements().Count();
                for (int index = 0; index < additions; index++)
                {
                    root.Add(new XElement(
                        relationships + "Relationship",
                        new XAttribute("Id", "rLimit" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new XAttribute("Type", "https://synthetic.invalid/relationship/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new XAttribute("Target", existingTarget)));
                }
            });

        FileFormatClassificationResult result = classifier.Classify(workbook.Path);

        Assert.Equal(FileFormatClassification.UnsafePackage, result.Classification);
    }

    [Fact]
    public void Wrong_workbook_content_type_is_rejected()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        X01SyntheticWorkbookFactory.RewriteXml(
            workbook.Path,
            "[Content_Types].xml",
            document =>
            {
                XElement workbookOverride = document.Root!
                    .Elements()
                    .Single(element => string.Equals(
                        (string?)element.Attribute("ContentType"),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
                        StringComparison.OrdinalIgnoreCase));
                workbookOverride.SetAttributeValue(
                    "ContentType",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml");
            });

        FileFormatClassificationResult result = classifier.Classify(workbook.Path);

        Assert.Equal(FileFormatClassification.InvalidContentType, result.Classification);
    }

    [Fact]
    public void Truncated_zip_is_rejected_as_corrupt()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        byte[] bytes = File.ReadAllBytes(workbook.Path);
        File.WriteAllBytes(workbook.Path, bytes[..^22]);

        FileFormatClassificationResult result = classifier.Classify(workbook.Path);

        Assert.Equal(FileFormatClassification.CorruptPackage, result.Classification);
    }

    [Fact]
    public void Excessive_compression_ratio_is_rejected_before_package_parsing()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.CreateZipBombLike();

        FileFormatClassificationResult result = classifier.Classify(workbook.Path);

        Assert.Equal(FileFormatClassification.UnsafePackage, result.Classification);
    }

    [Fact]
    public void Percent_encoded_backslash_in_a_package_part_name_is_rejected()
    {
        using TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        X01SyntheticWorkbookFactory.AddEntry(
            workbook.Path,
            "xl/%5Cunsafe.xml",
            "<synthetic />"u8.ToArray());

        FileFormatClassificationResult result = classifier.Classify(workbook.Path);

        Assert.Equal(FileFormatClassification.UnsafePackage, result.Classification);
    }

    [Fact]
    public void Security_limits_are_finite_and_match_the_fixed_boundaries()
    {
        Assert.Equal(100L * 1024 * 1024, FileFormatClassifier.MaxInputFileBytes);
        Assert.Equal(1024L * 1024 * 1024, FileFormatClassifier.MaxExpandedPackageBytes);
        Assert.Equal(256L * 1024 * 1024, FileFormatClassifier.MaxSingleExpandedEntryBytes);
        Assert.Equal(10_000, FileFormatClassifier.MaxZipEntryCount);
        Assert.Equal(100, FileFormatClassifier.MaxCompressionRatio);
        Assert.InRange(FileFormatClassifier.MaxRelationshipsPerPart, 1, int.MaxValue);
        Assert.InRange(FileFormatClassifier.MaxTotalRelationships, 1, int.MaxValue);
        Assert.InRange(FileFormatClassifier.MaxCharactersInPart, 1, long.MaxValue);
    }
}

internal sealed class TemporaryWorkbook : IDisposable
{
    public TemporaryWorkbook(string directory, string path)
    {
        Directory = directory;
        Path = path;
    }

    public string Directory { get; }

    public string Path { get; }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}

internal static class X01SyntheticWorkbookFactory
{
    public const string SharedHeader = "Synthetic shared header";
    public const string InlineHeader = "Synthetic inline header";

    public static TemporaryWorkbook Create(
        string sharedHeader = SharedHeader,
        string inlineHeader = InlineHeader,
        string dimension = "A1:C3",
        SpreadsheetDocumentType documentType = SpreadsheetDocumentType.Workbook)
    {
        TemporaryWorkbook temporary = CreateLocation(".xlsx");
        using (SpreadsheetDocument document = SpreadsheetDocument.Create(temporary.Path, documentType))
        {
            WorkbookPart workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            SharedStringTablePart sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedStringPart.SharedStringTable = new SharedStringTable(
                new SharedStringItem(new DocumentFormat.OpenXml.Spreadsheet.Text(sharedHeader)));

            WorksheetPart originalPart = workbookPart.AddNewPart<WorksheetPart>();
            originalPart.Worksheet = CreateOriginalWorksheet(dimension, inlineHeader);

            WorksheetPart hiddenPart = workbookPart.AddNewPart<WorksheetPart>();
            hiddenPart.Worksheet = new Worksheet(
                new SheetDimension { Reference = "D4:E5" },
                new SheetData(
                    new Row(
                        InlineCell("D4", "Synthetic hidden header"),
                        NumberCell("E4", "7"))
                    {
                        RowIndex = 4,
                    },
                    new Row(NumberCell("D5", "8"))
                    {
                        RowIndex = 5,
                    }));

            Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(originalPart),
                    SheetId = 1,
                    Name = "Original",
                    State = SheetStateValues.Visible,
                },
                new Sheet
                {
                    Id = workbookPart.GetIdOfPart(hiddenPart),
                    SheetId = 2,
                    Name = "Hidden synthetic",
                    State = SheetStateValues.VeryHidden,
                });
            workbookPart.Workbook.Save();
        }

        return temporary;
    }

    public static TemporaryWorkbook CreateRaw(string extension, ReadOnlySpan<byte> content)
    {
        TemporaryWorkbook temporary = CreateLocation(extension);
        File.WriteAllBytes(temporary.Path, content);
        return temporary;
    }

    public static TemporaryWorkbook CreateZipBombLike()
    {
        TemporaryWorkbook temporary = CreateLocation(".xlsx");
        using FileStream stream = new(temporary.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(stream, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("large.xml", CompressionLevel.SmallestSize);
        using Stream entryStream = entry.Open();
        byte[] block = new byte[10_000];
        for (int index = 0; index < 101; index++)
        {
            entryStream.Write(block);
        }

        return temporary;
    }

    public static void DeleteEntry(string path, string entryName)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using ZipArchive archive = new(stream, ZipArchiveMode.Update);
        archive.GetEntry(entryName)?.Delete();
    }

    public static void AddEntry(string path, string entryName, byte[] content)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using ZipArchive archive = new(stream, ZipArchiveMode.Update);
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
        using Stream entryStream = entry.Open();
        entryStream.Write(content);
    }

    public static void RewriteXml(string path, string entryName, Action<XDocument> mutation)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using ZipArchive archive = new(stream, ZipArchiveMode.Update);
        ZipArchiveEntry original = archive.GetEntry(entryName)
            ?? throw new InvalidDataException("Synthetic package entry is missing.");
        XDocument document;
        using (Stream originalStream = original.Open())
        {
            document = XDocument.Load(originalStream, LoadOptions.PreserveWhitespace);
        }

        mutation(document);
        original.Delete();
        ZipArchiveEntry replacement = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream replacementStream = replacement.Open();
        document.Save(replacementStream, SaveOptions.DisableFormatting);
    }

    private static Worksheet CreateOriginalWorksheet(string dimension, string inlineHeader)
    {
        Row header = new(
            new Cell
            {
                CellReference = "A1",
                DataType = CellValues.SharedString,
                CellValue = new CellValue("0"),
            },
            InlineCell("B1", inlineHeader),
            new Cell
            {
                CellReference = "C1",
                DataType = CellValues.String,
                CellValue = new CellValue("Synthetic plain header"),
            })
        {
            RowIndex = 1,
        };
        Row second = new(NumberCell("A2", "1"), NumberCell("C2", "2"))
        {
            RowIndex = 2,
        };
        Row third = new(InlineCell("B3", "Synthetic body"))
        {
            RowIndex = 3,
        };
        return new Worksheet(
            new SheetDimension { Reference = dimension },
            new SheetData(header, second, third));
    }

    private static Cell InlineCell(string reference, string value) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(value)),
        };

    private static Cell NumberCell(string reference, string value) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.Number,
            CellValue = new CellValue(value),
        };

    private static TemporaryWorkbook CreateLocation(string extension)
    {
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "StudyReportEvaluator-X01-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, "synthetic" + extension);
        return new TemporaryWorkbook(directory, path);
    }
}