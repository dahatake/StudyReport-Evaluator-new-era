using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Writing;
using SpreadsheetText = DocumentFormat.OpenXml.Spreadsheet.Text;

namespace StudyReportEvaluator.App.Workbooks.Checkpoint;

public static class CheckpointStatusCodes
{
    public const string Success = "SUCCESS";
    public const string Invalid = "CHECKPOINT_INVALID";
    public const string SchemaUnsupported = "CHECKPOINT_SCHEMA_UNSUPPORTED";
    public const string HashMismatch = "CHECKPOINT_HASH_MISMATCH";
    public const string InputChanged = "INPUT_CHANGED";
    public const string TargetExists = "TARGET_EXISTS";
    public const string SaveFailed = "CHECKPOINT_SAVE_FAILED";
    public const string Cancelled = "CANCELLED";
}

public sealed class CheckpointSaveResult
{
    internal CheckpointSaveResult(string code, bool createdNew)
    {
        Code = code;
        CreatedNew = createdNew;
    }

    public string Code { get; }

    public bool CreatedNew { get; }

    public bool IsSuccess => string.Equals(Code, CheckpointStatusCodes.Success, StringComparison.Ordinal);

    public static CheckpointSaveResult Succeeded(bool createdNew) =>
        new(CheckpointStatusCodes.Success, createdNew);

    public static CheckpointSaveResult Failed(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new CheckpointSaveResult(code, createdNew: false);
    }

    public override string ToString() =>
        $"{nameof(CheckpointSaveResult)} {{ Code = {Code}, CreatedNew = {CreatedNew}, Content = <redacted> }}";
}

public sealed class CheckpointLoadResult
{
    internal CheckpointLoadResult(
        string code,
        CheckpointEnvelope? envelope,
        string? payloadSha256)
    {
        Code = code;
        Envelope = envelope;
        PayloadSha256 = payloadSha256;
    }

    public string Code { get; }

    public CheckpointEnvelope? Envelope { get; }

    public string? PayloadSha256 { get; }

    public bool IsSuccess => string.Equals(Code, CheckpointStatusCodes.Success, StringComparison.Ordinal);

    public static CheckpointLoadResult Succeeded(CheckpointEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new CheckpointLoadResult(CheckpointStatusCodes.Success, envelope, null);
    }

    public static CheckpointLoadResult Failed(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new CheckpointLoadResult(code, null, null);
    }

    public override string ToString() =>
        $"{nameof(CheckpointLoadResult)} {{ Code = {Code}, Content = <redacted> }}";
}

public interface ICheckpointFileOperations
{
    bool Exists(string path);

    void FlushToDisk(string path);

    void MoveNoOverwrite(string sourcePath, string destinationPath);

    void Replace(string sourcePath, string destinationPath);

    void Delete(string path);
}

public sealed class PhysicalCheckpointFileOperations : ICheckpointFileOperations
{
    public bool Exists(string path) => File.Exists(path);

    public void FlushToDisk(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.WriteThrough);
        stream.Flush(flushToDisk: true);
    }

    public void MoveNoOverwrite(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, overwrite: false);

    public void Replace(string sourcePath, string destinationPath) =>
        File.Replace(sourcePath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);

    public void Delete(string path) => File.Delete(path);

    public override string ToString() =>
        $"{nameof(PhysicalCheckpointFileOperations)} {{ Content = <redacted> }}";
}

public interface ICheckpointStore
{
    CheckpointSaveResult Create(
        CheckpointEnvelope envelope,
        CancellationToken cancellationToken = default);

    CheckpointSaveResult Update(
        CheckpointEnvelope envelope,
        CancellationToken cancellationToken = default);

    CheckpointLoadResult Load(
        string partialPath,
        CancellationToken cancellationToken = default);
}

public sealed class CheckpointStore : ICheckpointStore
{
    public const string CheckpointSheetName = "Quantification_Checkpoint";

    private const int CopyBufferSize = 128 * 1024;
    private const int MaximumTempNameAttempts = 64;

    private readonly ICheckpointFileOperations fileOperations;
    private readonly InputSnapshotService inputSnapshots;

    public CheckpointStore()
        : this(new PhysicalCheckpointFileOperations())
    {
    }

    public CheckpointStore(ICheckpointFileOperations fileOperations)
        : this(fileOperations, new InputSnapshotService())
    {
    }

    internal CheckpointStore(
        ICheckpointFileOperations fileOperations,
        InputSnapshotService inputSnapshots)
    {
        this.fileOperations = fileOperations
            ?? throw new ArgumentNullException(nameof(fileOperations));
        this.inputSnapshots = inputSnapshots
            ?? throw new ArgumentNullException(nameof(inputSnapshots));
    }

    public CheckpointSaveResult Create(
        CheckpointEnvelope envelope,
        CancellationToken cancellationToken = default) =>
        Save(envelope, update: false, cancellationToken);

    public CheckpointSaveResult Update(
        CheckpointEnvelope envelope,
        CancellationToken cancellationToken = default) =>
        Save(envelope, update: true, cancellationToken);

    public CheckpointLoadResult Load(
        string partialPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(partialPath))
        {
            return LoadFailure(CheckpointStatusCodes.Invalid);
        }

        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(partialPath);
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return LoadFailure(CheckpointStatusCodes.Invalid);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return LoadFailure(CheckpointStatusCodes.Cancelled);
        }

        try
        {
            if (!fileOperations.Exists(canonicalPath))
            {
                return LoadFailure(CheckpointStatusCodes.Invalid);
            }

            CheckpointLoadResult structural = InspectPackage(
                canonicalPath,
                canonicalPath,
                baseline: null,
                expectedPayloadSha256: null,
                cancellationToken);
            if (structural.IsSuccess
                && structural.Envelope is CheckpointEnvelope envelope
                && InputMatches(envelope))
            {
                SourceWorkbookBaseline baseline = CaptureSourceBaseline(
                    envelope.InputPath,
                    cancellationToken);
                return InspectPackage(
                    canonicalPath,
                    canonicalPath,
                    baseline,
                    structural.PayloadSha256,
                    cancellationToken);
            }

            return structural;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return LoadFailure(CheckpointStatusCodes.Cancelled);
        }
        catch (CheckpointFormatException exception)
        {
            return LoadFailure(exception.Code);
        }
        catch (Exception exception) when (IsOperationalException(exception))
        {
            return LoadFailure(CheckpointStatusCodes.Invalid);
        }
    }

    public override string ToString() =>
        $"{nameof(CheckpointStore)} {{ Content = <redacted> }}";

    private CheckpointSaveResult Save(
        CheckpointEnvelope envelope,
        bool update,
        CancellationToken cancellationToken)
    {
        if (envelope is null)
        {
            return SaveFailure(CheckpointStatusCodes.Invalid);
        }

        CheckpointPayloadEncoding encoding;
        try
        {
            encoding = CheckpointPayloadCodec.Encode(envelope);
        }
        catch (CheckpointFormatException exception)
        {
            return SaveFailure(exception.Code);
        }

        string temporaryPath = string.Empty;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string partialPath = envelope.PartialPath;
            string targetDirectory = Path.GetDirectoryName(partialPath)
                ?? throw new InvalidDataException("The checkpoint path has no parent directory.");
            if (!Directory.Exists(targetDirectory))
            {
                return SaveFailure(CheckpointStatusCodes.SaveFailed);
            }

            bool targetExists = fileOperations.Exists(partialPath);
            CheckpointLoadResult? previous = null;
            if (!update && targetExists)
            {
                return SaveFailure(CheckpointStatusCodes.TargetExists);
            }

            if (update)
            {
                if (!targetExists)
                {
                    return SaveFailure(CheckpointStatusCodes.Invalid);
                }

                previous = Load(partialPath, cancellationToken);
                if (!previous.IsSuccess
                    || previous.Envelope is null
                    || !CanAdvance(previous.Envelope, envelope))
                {
                    return SaveFailure(previous.IsSuccess
                        ? CheckpointStatusCodes.Invalid
                        : previous.Code);
                }
            }

            if (!InputMatches(envelope))
            {
                return SaveFailure(CheckpointStatusCodes.InputChanged);
            }

            SourceWorkbookBaseline baseline = CaptureSourceBaseline(
                envelope.InputPath,
                cancellationToken);
            temporaryPath = CopyInputToUniqueTemp(
                envelope.InputPath,
                targetDirectory,
                envelope.Input,
                cancellationToken);
            using (SpreadsheetDocument document = SpreadsheetDocument.Open(
                       temporaryPath,
                       true,
                       new OpenSettings { AutoSave = true }))
            {
                WorkbookPart workbookPart = document.WorkbookPart
                    ?? throw new InvalidDataException("The workbook part is missing.");
                WorkbookSheetWriter.AddWorksheet(
                    workbookPart,
                    CheckpointSheetName,
                    CreateWorksheet(encoding));
            }

            fileOperations.FlushToDisk(temporaryPath);
            CheckpointLoadResult prepared = InspectPackage(
                temporaryPath,
                envelope.PartialPath,
                baseline,
                encoding.Sha256,
                cancellationToken);
            if (!prepared.IsSuccess)
            {
                return SaveFailureWithCleanup(prepared.Code, temporaryPath);
            }

            if (!InputMatches(envelope))
            {
                return SaveFailureWithCleanup(CheckpointStatusCodes.InputChanged, temporaryPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (update)
            {
                CheckpointLoadResult current = Load(envelope.PartialPath, cancellationToken);
                if (!current.IsSuccess
                    || current.Envelope is null
                    || previous?.PayloadSha256 is null
                    || !string.Equals(
                        current.PayloadSha256,
                        previous.PayloadSha256,
                        StringComparison.Ordinal)
                    || !CanAdvance(current.Envelope, envelope))
                {
                    return SaveFailureWithCleanup(CheckpointStatusCodes.Invalid, temporaryPath);
                }

                fileOperations.Replace(temporaryPath, envelope.PartialPath);
            }
            else
            {
                fileOperations.MoveNoOverwrite(temporaryPath, envelope.PartialPath);
            }

            temporaryPath = string.Empty;
            CheckpointLoadResult installed = InspectPackage(
                envelope.PartialPath,
                envelope.PartialPath,
                baseline,
                encoding.Sha256,
                CancellationToken.None);
            return installed.IsSuccess
                ? new CheckpointSaveResult(CheckpointStatusCodes.Success, createdNew: !update)
                : SaveFailure(installed.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SaveFailureWithCleanup(
                CheckpointStatusCodes.Cancelled,
                temporaryPath);
        }
        catch (CheckpointFormatException exception)
        {
            return SaveFailureWithCleanup(exception.Code, temporaryPath);
        }
        catch (Exception exception) when (IsOperationalException(exception))
        {
            if (InstalledPayloadMatches(envelope.PartialPath, encoding.Sha256))
            {
                return new CheckpointSaveResult(CheckpointStatusCodes.Success, createdNew: !update);
            }

            string code = !update && SafeExists(envelope.PartialPath)
                ? CheckpointStatusCodes.TargetExists
                : CheckpointStatusCodes.SaveFailed;
            return SaveFailureWithCleanup(code, temporaryPath);
        }
    }

    private bool InstalledPayloadMatches(string partialPath, string expectedPayloadSha256)
    {
        try
        {
            CheckpointLoadResult result = InspectPackage(
                partialPath,
                partialPath,
                baseline: null,
                expectedPayloadSha256,
                CancellationToken.None);
            return result.IsSuccess;
        }
        catch (Exception exception) when (IsOperationalException(exception)
            || exception is CheckpointFormatException)
        {
            return false;
        }
    }

    private bool InputMatches(CheckpointEnvelope envelope)
    {
        try
        {
            return inputSnapshots.Recheck(envelope.InputPath, envelope.Input).IsMatch;
        }
        catch (Exception exception) when (IsOperationalException(exception))
        {
            return false;
        }
    }

    private static bool CanAdvance(
        CheckpointEnvelope previous,
        CheckpointEnvelope next)
    {
        if (previous.SchemaVersion != next.SchemaVersion
            || !PathsEqual(previous.InputPath, next.InputPath)
            || !previous.Input.Equals(next.Input)
            || !string.Equals(previous.DefinitionCanonicalJson, next.DefinitionCanonicalJson, StringComparison.Ordinal)
            || !string.Equals(previous.DefinitionSha256, next.DefinitionSha256, StringComparison.Ordinal)
            || !string.Equals(previous.NormalModelId, next.NormalModelId, StringComparison.Ordinal)
            || !string.Equals(previous.ContextTier, next.ContextTier, StringComparison.Ordinal)
            || !string.Equals(previous.ReferenceModelId, next.ReferenceModelId, StringComparison.Ordinal)
            || !string.Equals(previous.ReasoningEffort, next.ReasoningEffort, StringComparison.Ordinal)
            || !string.Equals(
                CheckpointPayloadCodec.SerializeValue(previous.Runtime),
                CheckpointPayloadCodec.SerializeValue(next.Runtime),
                StringComparison.Ordinal)
            || !PathsEqual(previous.FinalPath, next.FinalPath)
            || !PathsEqual(previous.PartialPath, next.PartialPath)
            || previous.StartedAtUtc != next.StartedAtUtc
            || next.SavedAtUtc < previous.SavedAtUtc
            || !IsCanonicalPrefix(previous.References, next.References)
            || !IsCanonicalPrefix(previous.CompletedRows, next.CompletedRows))
        {
            return false;
        }

        return true;
    }

    private static bool IsCanonicalPrefix<T>(
        ImmutableArray<T> prefix,
        ImmutableArray<T> candidate)
    {
        if (prefix.Length > candidate.Length)
        {
            return false;
        }

        for (int index = 0; index < prefix.Length; index++)
        {
            if (!string.Equals(
                    CheckpointPayloadCodec.SerializeValue(prefix[index]),
                    CheckpointPayloadCodec.SerializeValue(candidate[index]),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static SourceWorkbookBaseline CaptureSourceBaseline(
        string inputPath,
        CancellationToken cancellationToken)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(inputPath, false);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The input workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("The input workbook root is missing.");
        ImmutableArray<SourceSheetIdentity>.Builder sheets =
            ImmutableArray.CreateBuilder<SourceSheetIdentity>();
        foreach (Sheet sheet in workbook.Descendants<Sheet>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = sheet.Name?.Value
                ?? throw new InvalidDataException("An input worksheet name is missing.");
            if (string.Equals(name, CheckpointSheetName, StringComparison.OrdinalIgnoreCase))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            string relationshipId = sheet.Id?.Value
                ?? throw new InvalidDataException("An input worksheet relationship is missing.");
            OpenXmlPart part = workbookPart.GetPartById(relationshipId);
            sheets.Add(new SourceSheetIdentity(
                name,
                sheet.SheetId?.Value
                    ?? throw new InvalidDataException("An input worksheet ID is missing."),
                relationshipId,
                part.Uri.ToString(),
                part.ContentType,
                HashPart(part)));
        }

        return new SourceWorkbookBaseline(sheets.ToImmutable());
    }

    private static string CopyInputToUniqueTemp(
        string inputPath,
        string targetDirectory,
        InputSnapshot expectedInput,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaximumTempNameAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string temporaryPath = Path.Combine(
                targetDirectory,
                $".study-report-evaluator-{Guid.NewGuid():N}.checkpoint.xlsx");
            FileStream output;
            try
            {
                output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    CopyBufferSize,
                    FileOptions.SequentialScan | FileOptions.WriteThrough);
            }
            catch (IOException) when (File.Exists(temporaryPath) || Directory.Exists(temporaryPath))
            {
                continue;
            }

            try
            {
                using (output)
                using (FileStream input = new(
                           inputPath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read,
                           CopyBufferSize,
                           FileOptions.SequentialScan))
                using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    byte[] buffer = new byte[CopyBufferSize];
                    long copied = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        hash.AppendData(buffer, 0, read);
                        copied = checked(copied + read);
                    }

                    output.Flush(flushToDisk: true);
                    if (copied != expectedInput.SizeBytes
                        || !string.Equals(
                            Convert.ToHexString(hash.GetHashAndReset()),
                            expectedInput.Sha256,
                            StringComparison.Ordinal))
                    {
                        throw new IOException("The input changed while the checkpoint copy was being created.");
                    }
                }

                return temporaryPath;
            }
            catch
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception cleanupException) when (IsOperationalException(cleanupException))
                {
                    // Preserve the original copy failure. The app-owned temp name is never a resume source.
                }

                throw;
            }
        }

        throw new IOException("A unique checkpoint temp path could not be allocated.");
    }

    private static Worksheet CreateWorksheet(CheckpointPayloadEncoding encoding)
    {
        SheetData data = new();
        data.Append(CreateStringRow(1, "RecordType", "KeyOrChunkIndex", "Value"));
        data.Append(CreateStringRow(2, "META", "SchemaVersion", CheckpointEnvelope.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)));
        data.Append(CreateStringRow(3, "META", "PayloadSha256", encoding.Sha256));
        data.Append(CreateStringRow(4, "META", "ChunkCount", encoding.Chunks.Length.ToString(CultureInfo.InvariantCulture)));
        for (int index = 0; index < encoding.Chunks.Length; index++)
        {
            uint rowNumber = checked((uint)(index + 5));
            string rowText = rowNumber.ToString(CultureInfo.InvariantCulture);
            Row row = new() { RowIndex = rowNumber };
            row.Append(
                StringCell("A" + rowText, "PAYLOAD"),
                SpreadsheetLiteral.CreateNumberCell("B" + rowText, index),
                StringCell("C" + rowText, encoding.Chunks[index]));
            data.Append(row);
        }

        return new Worksheet(data);
    }

    private static Row CreateStringRow(
        uint rowNumber,
        string first,
        string second,
        string third)
    {
        string rowText = rowNumber.ToString(CultureInfo.InvariantCulture);
        return new Row(
            StringCell("A" + rowText, first),
            StringCell("B" + rowText, second),
            StringCell("C" + rowText, third))
        {
            RowIndex = rowNumber,
        };
    }

    private static Cell StringCell(string reference, string value) =>
        SpreadsheetLiteral.CreateInlineStringCell(reference, value);

    private static CheckpointLoadResult InspectPackage(
        string packagePath,
        string boundPartialPath,
        SourceWorkbookBaseline? baseline,
        string? expectedPayloadSha256,
        CancellationToken cancellationToken)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(packagePath, false);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        Workbook workbook = workbookPart.Workbook
            ?? throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        Sheet[] sheets = workbook.Descendants<Sheet>().ToArray();
        Sheet[] checkpointSheets = sheets.Where(sheet => string.Equals(
            sheet.Name?.Value,
            CheckpointSheetName,
            StringComparison.Ordinal)).ToArray();
        if (checkpointSheets.Length != 1)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        if (baseline is not null)
        {
            ValidatePreservedSheets(workbookPart, sheets, baseline);
        }

        string relationshipId = checkpointSheets[0].Id?.Value
            ?? throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        if (workbookPart.GetPartById(relationshipId) is not WorksheetPart checkpointPart
            || checkpointPart.Worksheet is null)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        (string payload, string payloadSha256) = ReadCheckpointWorksheet(checkpointPart.Worksheet);
        if (expectedPayloadSha256 is not null
            && !string.Equals(payloadSha256, expectedPayloadSha256, StringComparison.Ordinal))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.HashMismatch);
        }

        CheckpointEnvelope envelope = CheckpointPayloadCodec.Decode(payload, payloadSha256);
        if (!PathsEqual(envelope.PartialPath, Path.GetFullPath(boundPartialPath)))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        if (new OpenXmlValidator().Validate(document, cancellationToken).Any())
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        return new CheckpointLoadResult(
            CheckpointStatusCodes.Success,
            envelope,
            payloadSha256);
    }

    private static void ValidatePreservedSheets(
        WorkbookPart workbookPart,
        IReadOnlyList<Sheet> actualSheets,
        SourceWorkbookBaseline baseline)
    {
        if (actualSheets.Count != baseline.Sheets.Length + 1
            || !string.Equals(
                actualSheets[^1].Name?.Value,
                CheckpointSheetName,
                StringComparison.Ordinal))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        for (int index = 0; index < baseline.Sheets.Length; index++)
        {
            SourceSheetIdentity expected = baseline.Sheets[index];
            Sheet actual = actualSheets[index];
            string relationshipId = actual.Id?.Value
                ?? throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            OpenXmlPart part = workbookPart.GetPartById(relationshipId);
            if (!string.Equals(actual.Name?.Value, expected.Name, StringComparison.Ordinal)
                || actual.SheetId?.Value != expected.SheetId
                || !string.Equals(relationshipId, expected.RelationshipId, StringComparison.Ordinal)
                || !string.Equals(part.Uri.ToString(), expected.PartUri, StringComparison.Ordinal)
                || !string.Equals(part.ContentType, expected.ContentType, StringComparison.Ordinal)
                || !string.Equals(HashPart(part), expected.Sha256, StringComparison.Ordinal))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }
        }
    }

    private static (string Payload, string PayloadSha256) ReadCheckpointWorksheet(Worksheet worksheet)
    {
        if (worksheet.ChildElements.Count != 1
            || worksheet.GetFirstChild<SheetData>() is not SheetData data)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        Row[] rows = data.Elements<Row>()
            .Take(ConfigSheetWriter.MaximumExcelRows + 1)
            .ToArray();
        if (rows.Length < 5 || rows.Length > ConfigSheetWriter.MaximumExcelRows)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        AssertStringRow(rows[0], 1, "RecordType", "KeyOrChunkIndex", "Value");
        AssertStringRow(rows[1], 2, "META", "SchemaVersion", ReadInline(rows[1], "C2"));
        string schemaVersion = ReadInline(rows[1], "C2");
        if (!string.Equals(
            schemaVersion,
            CheckpointEnvelope.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.SchemaUnsupported);
        }

        AssertStringRow(rows[2], 3, "META", "PayloadSha256", ReadInline(rows[2], "C3"));
        string payloadSha256 = ReadInline(rows[2], "C3");
        AssertStringRow(rows[3], 4, "META", "ChunkCount", ReadInline(rows[3], "C4"));
        string chunkCountText = ReadInline(rows[3], "C4");
        if (!int.TryParse(chunkCountText, NumberStyles.None, CultureInfo.InvariantCulture, out int chunkCount)
            || chunkCount < 1
            || chunkCount > ConfigSheetWriter.MaximumExcelRows - 4
            || !string.Equals(chunkCountText, chunkCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            || rows.Length != chunkCount + 4)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        StringBuilder payload = new();
        for (int index = 0; index < chunkCount; index++)
        {
            int rowNumber = index + 5;
            Row row = rows[index + 4];
            AssertRowShape(row, rowNumber);
            if (!string.Equals(ReadInline(row, "A" + rowNumber), "PAYLOAD", StringComparison.Ordinal))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            Cell indexCell = CellAt(row, "B" + rowNumber);
            string? indexText = indexCell.CellValue?.Text;
            if (indexCell.DataType?.Value != CellValues.Number
                || !int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedIndex)
                || parsedIndex != index
                || !string.Equals(indexText, index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            string chunk = ReadInline(row, "C" + rowNumber);
            if (chunk.Length > CheckpointPayloadCodec.MaximumChunkCharacters)
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            payload.Append(chunk);
        }

        return (payload.ToString(), payloadSha256);
    }

    private static void AssertStringRow(
        Row row,
        int rowNumber,
        string first,
        string second,
        string third)
    {
        AssertRowShape(row, rowNumber);
        if (!string.Equals(ReadInline(row, "A" + rowNumber), first, StringComparison.Ordinal)
            || !string.Equals(ReadInline(row, "B" + rowNumber), second, StringComparison.Ordinal)
            || !string.Equals(ReadInline(row, "C" + rowNumber), third, StringComparison.Ordinal))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
    }

    private static void AssertRowShape(Row row, int rowNumber)
    {
        if (row.RowIndex?.Value != (uint)rowNumber
            || row.ChildElements.Count != 3
            || row.Elements<Cell>().Count() != 3)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        string[] expected = ["A" + rowNumber, "B" + rowNumber, "C" + rowNumber];
        if (!row.Elements<Cell>().Select(cell => cell.CellReference?.Value).SequenceEqual(expected))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
    }

    private static string ReadInline(Row row, string reference)
    {
        Cell cell = CellAt(row, reference);
        if (cell.DataType?.Value != CellValues.InlineString
            || cell.InlineString is null
            || cell.InlineString.ChildElements.Count != 1
            || cell.InlineString.GetFirstChild<SpreadsheetText>() is not SpreadsheetText text)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        return text.Text ?? string.Empty;
    }

    private static Cell CellAt(Row row, string reference) =>
        row.Elements<Cell>().SingleOrDefault(cell => string.Equals(
            cell.CellReference?.Value,
            reference,
            StringComparison.Ordinal))
        ?? throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);

    private CheckpointSaveResult SaveFailureWithCleanup(string code, string temporaryPath)
    {
        if (string.IsNullOrEmpty(temporaryPath))
        {
            return SaveFailure(code);
        }

        try
        {
            if (fileOperations.Exists(temporaryPath))
            {
                fileOperations.Delete(temporaryPath);
            }
        }
        catch (Exception exception) when (IsOperationalException(exception))
        {
            return SaveFailure(code);
        }

        return SaveFailure(code);
    }

    private bool SafeExists(string path)
    {
        try
        {
            return fileOperations.Exists(path);
        }
        catch (Exception exception) when (IsOperationalException(exception))
        {
            return false;
        }
    }

    private static string HashPart(OpenXmlPart part)
    {
        using Stream stream = part.GetStream(FileMode.Open, FileAccess.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsOperationalException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or OpenXmlPackageException
            or OperationCanceledException
            or ArgumentException
            or NotSupportedException;

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            first,
            second,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static CheckpointSaveResult SaveFailure(string code) => new(code, createdNew: false);

    private static CheckpointLoadResult LoadFailure(string code) => new(code, null, null);

    private sealed record SourceWorkbookBaseline(ImmutableArray<SourceSheetIdentity> Sheets);

    private sealed record SourceSheetIdentity(
        string Name,
        uint SheetId,
        string RelationshipId,
        string PartUri,
        string ContentType,
        string Sha256);
}
