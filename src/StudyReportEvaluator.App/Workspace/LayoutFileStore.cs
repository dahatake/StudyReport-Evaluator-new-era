using System.Collections.Immutable;
using System.Security;
using System.Text;
using System.Text.Json;

namespace StudyReportEvaluator.App.Workspace;

public enum LayoutLoadStatus
{
    Loaded,
    Missing,
    Invalid,
    UnsupportedVersion,
    ReadFailed,
}

/// <summary>Everything layout.json may contain: persona codes and per-screen panel layouts.</summary>
public sealed record LayoutDocument(
    ImmutableArray<Persona> Personas,
    ImmutableDictionary<string, WorkspaceLayout> Screens)
{
    public override string ToString() => $"{nameof(LayoutDocument)} {{ Screens = {Screens.Count} }}";
}

public sealed record LayoutLoadResult(LayoutLoadStatus Status, LayoutDocument? Document = null);

/// <summary>
/// NFR-UX-004: the workspace layout file <c>layout.json</c>, separate from <c>setting.txt</c>.
/// Construction and loading never create or change files. A file that cannot be read, is not valid
/// JSON, or has an unknown schema is reported and left untouched. Saving writes a temporary file in
/// the same directory and replaces the old file only after the new bytes are complete.
/// </summary>
public sealed class LayoutFileStore
{
    public const string FileName = "layout.json";
    public const int CurrentSchemaVersion = 1;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public LayoutFileStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!Path.IsPathFullyQualified(filePath))
        {
            throw new ArgumentException("The layout file path must be fully qualified.", nameof(filePath));
        }

        try
        {
            _ = StrictUtf8.GetByteCount(filePath);
            FilePath = Path.GetFullPath(filePath);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            throw new ArgumentException("The layout file path is invalid.", nameof(filePath));
        }

        if (Path.GetDirectoryName(FilePath) is null || Path.EndsInDirectorySeparator(FilePath))
        {
            throw new ArgumentException("The layout path must include a file name.", nameof(filePath));
        }
    }

    public string FilePath { get; }

    public LayoutLoadResult Load()
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(FilePath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(LayoutLoadStatus.Missing);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return new(LayoutLoadStatus.ReadFailed);
        }

        return Parse(bytes);
    }

    public bool Save(LayoutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        byte[] bytes = Serialize(document);
        string directory = Path.GetDirectoryName(FilePath)!;
        string temporaryPath = Path.Combine(directory, $".layout-{Guid.NewGuid():N}.tmp");
        bool ownsTemporaryFile = false;
        try
        {
            Directory.CreateDirectory(directory);
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporaryFile = true;
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
            ownsTemporaryFile = false;
            return true;
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            return false;
        }
        finally
        {
            if (ownsTemporaryFile)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (IsFileFailure(exception))
                {
                    // Best effort for this invocation's own temporary file only.
                }
            }
        }
    }

    public override string ToString() => $"{nameof(LayoutFileStore)} {{ FilePath = <redacted> }}";

    internal static byte[] Serialize(LayoutDocument document)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteStartArray("personas");
            foreach (Persona persona in PersonaCodes.Ordered.Where(document.Personas.Contains))
            {
                writer.WriteStringValue(PersonaCodes.Code(persona));
            }

            writer.WriteEndArray();
            writer.WriteStartObject("screens");
            foreach (string screen in WorkspaceScreens.All.Where(document.Screens.ContainsKey))
            {
                WorkspaceLayout layout = document.Screens[screen];
                writer.WriteStartObject(screen);
                writer.WriteString("orientation",
                    layout.Orientation == WorkspaceOrientation.Horizontal ? "horizontal" : "vertical");
                writer.WriteStartArray("groups");
                foreach (WorkspaceGroup group in layout.Groups)
                {
                    writer.WriteStartObject();
                    writer.WriteStartArray("panels");
                    foreach (string id in group.PanelIds)
                    {
                        writer.WriteStringValue(id);
                    }

                    writer.WriteEndArray();
                    writer.WriteNumber("size", Math.Round(group.Size, 4));
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteStartArray("hidden");
                foreach (string id in layout.HiddenPanelIds)
                {
                    writer.WriteStringValue(id);
                }

                writer.WriteEndArray();
                if (layout.MaximizedPanelId is null)
                {
                    writer.WriteNull("maximized");
                }
                else
                {
                    writer.WriteString("maximized", layout.MaximizedPanelId);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    internal static LayoutLoadResult Parse(byte[] bytes)
    {
        try
        {
            int offset = bytes.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0;
            string json = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out JsonElement schema)
                || schema.ValueKind != JsonValueKind.Number
                || schema.GetRawText().AsSpan().IndexOfAny('.', 'e', 'E') >= 0)
            {
                return new(LayoutLoadStatus.Invalid);
            }

            if (!schema.TryGetInt32(out int version) || version != CurrentSchemaVersion)
            {
                return new(LayoutLoadStatus.UnsupportedVersion);
            }

            if (!HasOnly(root, "schemaVersion", "personas", "screens")
                || !root.TryGetProperty("personas", out JsonElement personasElement)
                || personasElement.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("screens", out JsonElement screensElement)
                || screensElement.ValueKind != JsonValueKind.Object)
            {
                return new(LayoutLoadStatus.Invalid);
            }

            HashSet<Persona> personas = [];
            foreach (JsonElement item in personasElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || !PersonaCodes.TryParse(item.GetString(), out Persona persona)
                    || !personas.Add(persona))
                {
                    return new(LayoutLoadStatus.Invalid);
                }
            }

            if (personas.Count == 0)
            {
                return new(LayoutLoadStatus.Invalid);
            }

            ImmutableDictionary<string, WorkspaceLayout>.Builder screens =
                ImmutableDictionary.CreateBuilder<string, WorkspaceLayout>(StringComparer.Ordinal);
            foreach (JsonProperty screen in screensElement.EnumerateObject())
            {
                if (!WorkspaceScreens.All.Contains(screen.Name, StringComparer.Ordinal)
                    || ParseLayout(screen.Value, WorkspaceDefaults.PanelsFor(screen.Name)) is not { } layout)
                {
                    return new(LayoutLoadStatus.Invalid);
                }

                screens[screen.Name] = layout;
            }

            return new(LayoutLoadStatus.Loaded, new LayoutDocument(
                [.. PersonaCodes.Ordered.Where(personas.Contains)], screens.ToImmutable()));
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            return new(LayoutLoadStatus.Invalid);
        }
    }

    private static WorkspaceLayout? ParseLayout(JsonElement element, ImmutableArray<string> knownPanels)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !HasOnly(element, "orientation", "groups", "hidden", "maximized")
            || !element.TryGetProperty("orientation", out JsonElement orientationElement)
            || !element.TryGetProperty("groups", out JsonElement groupsElement)
            || groupsElement.ValueKind != JsonValueKind.Array
            || !element.TryGetProperty("hidden", out JsonElement hiddenElement)
            || hiddenElement.ValueKind != JsonValueKind.Array
            || !element.TryGetProperty("maximized", out JsonElement maximizedElement))
        {
            return null;
        }

        WorkspaceOrientation orientation;
        switch (orientationElement.ValueKind == JsonValueKind.String ? orientationElement.GetString() : null)
        {
            case "horizontal":
                orientation = WorkspaceOrientation.Horizontal;
                break;
            case "vertical":
                orientation = WorkspaceOrientation.Vertical;
                break;
            default:
                return null;
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        List<WorkspaceGroup> groups = [];
        foreach (JsonElement groupElement in groupsElement.EnumerateArray())
        {
            if (groupElement.ValueKind != JsonValueKind.Object
                || !HasOnly(groupElement, "panels", "size")
                || !groupElement.TryGetProperty("panels", out JsonElement panelsElement)
                || panelsElement.ValueKind != JsonValueKind.Array
                || !groupElement.TryGetProperty("size", out JsonElement sizeElement)
                || sizeElement.ValueKind != JsonValueKind.Number
                || !sizeElement.TryGetDouble(out double size)
                || !double.IsFinite(size) || size < WorkspaceGroup.MinimumSize || size > WorkspaceGroup.MaximumSize)
            {
                return null;
            }

            List<string> ids = [];
            foreach (JsonElement idElement in panelsElement.EnumerateArray())
            {
                string? id = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
                if (id is null || !knownPanels.Contains(id, StringComparer.Ordinal) || !seen.Add(id))
                {
                    return null;
                }

                ids.Add(id);
            }

            if (ids.Count == 0)
            {
                return null;
            }

            groups.Add(new WorkspaceGroup(ids, size));
        }

        List<string> hidden = [];
        foreach (JsonElement idElement in hiddenElement.EnumerateArray())
        {
            string? id = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
            if (id is null || !seen.Contains(id))
            {
                return null;
            }

            hidden.Add(id);
        }

        string? maximized = maximizedElement.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String when maximizedElement.GetString() is { } id && seen.Contains(id) => id,
            _ => string.Empty,
        };
        if (maximized == string.Empty || seen.Count != knownPanels.Length)
        {
            return null;
        }

        WorkspaceLayout layout = new(orientation, groups, hidden, maximized);
        return layout.VisiblePanelIds.Any() ? layout : null;
    }

    private static bool HasOnly(JsonElement element, params string[] names)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFileFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or NotSupportedException;
}
