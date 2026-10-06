using System.Collections.Immutable;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.App.Settings;

public enum SettingsLoadStatus
{
    Loaded,
    Missing,
    JsonInvalid,
    UnsupportedVersion,
    ReadFailed,
}

/// <summary>Missing is a successful default startup; failed loads expose no partial settings.</summary>
public sealed record SettingsLoadResult(SettingsLoadStatus Status, ApplicationSettings? Settings = null)
{
    public bool IsSuccess => Status is SettingsLoadStatus.Loaded or SettingsLoadStatus.Missing;

    public override string ToString() =>
        $"{nameof(SettingsLoadResult)} {{ Status = {Status}, Settings = <redacted> }}";
}

public enum SettingsSaveStatus
{
    Saved,
    InvalidSettings,
    WriteFailed,
}

public sealed record SettingsSaveResult(SettingsSaveStatus Status)
{
    public bool IsSuccess => Status == SettingsSaveStatus.Saved;
}

/// <summary>Explicit persistence of one settings file at an injected, fully qualified path.</summary>
/// <remarks>
/// Construction and loading never create directories or files. There is no repair, history,
/// or cross-process merge: the last successful same-directory replacement wins.
/// Cancellation throws OperationCanceledException before replacement and leaves existing bytes
/// unchanged. Once the synchronous replacement succeeds, saving returns Saved even if cancellation
/// is subsequently requested. This does not promise durability across power loss or arbitrary
/// network filesystems. Failures never expose exception messages or settings content.
/// </remarks>
public sealed class SettingsFileStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public SettingsFileStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!Path.IsPathFullyQualified(filePath))
        {
            throw new ArgumentException("The settings file path must be fully qualified.", nameof(filePath));
        }

        try
        {
            ValidateUtf8Text(filePath);
            FilePath = Path.GetFullPath(filePath);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            throw new ArgumentException("The settings file path is invalid.", nameof(filePath));
        }

        if (Path.GetDirectoryName(FilePath) is null || Path.EndsInDirectorySeparator(FilePath))
        {
            throw new ArgumentException("The settings path must include a file name.", nameof(filePath));
        }
    }

    public string FilePath { get; }

    public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes;
        try
        {
            // Do not use File.Exists: it can turn access failures into a false Missing result.
            bytes = await File.ReadAllBytesAsync(FilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SettingsLoadResult missing = ClassifyMissingPath();
            cancellationToken.ThrowIfCancellationRequested();
            return missing;
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(SettingsLoadStatus.ReadFailed);
        }

        cancellationToken.ThrowIfCancellationRequested();
        SettingsLoadResult result = Deserialize(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<SettingsSaveResult> SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes;
        try
        {
            if (!IsValid(settings))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new(SettingsSaveStatus.InvalidSettings);
            }

            // Freeze the immutable record graph into bytes before the first await or any IO.
            // The canonical serializer is a hash oracle, not the settings file format.
            bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(SettingsSaveStatus.InvalidSettings);
        }

        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.GetDirectoryName(FilePath)!;
        string temporaryPath = Path.Combine(directory, $".setting-{Guid.NewGuid():N}.tmp");
        bool ownsTemporaryFile = false;
        try
        {
            Directory.CreateDirectory(directory);
            cancellationToken.ThrowIfCancellationRequested();
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                ownsTemporaryFile = true;
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Same directory, closed handle, and no delete/truncate of the old file first.
            // Do not observe cancellation after the commit point and report a false cancellation.
            File.Move(temporaryPath, FilePath, overwrite: true);
            ownsTemporaryFile = false;
            return new(SettingsSaveStatus.Saved);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(SettingsSaveStatus.WriteFailed);
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
                    // Best effort for this invocation's file only; preserve the failure/cancellation.
                    // Never delete other writers' files, change attributes, or expose exception text.
                }
            }
        }
    }

    public override string ToString() => $"{nameof(SettingsFileStore)} {{ FilePath = <redacted> }}";

    private static SettingsLoadResult Deserialize(byte[] bytes)
    {
        try
        {
            int offset = bytes.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0;
            string json = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
            using JsonDocument document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { AllowDuplicateProperties = false });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out JsonElement schema)
                || schema.ValueKind != JsonValueKind.Number
                || schema.GetRawText().AsSpan().IndexOfAny('.', 'e', 'E') >= 0)
            {
                return new(SettingsLoadStatus.JsonInvalid);
            }

            // An integer outside Int32 is also an unknown version, not a coerced current one.
            if (!schema.TryGetInt32(out int version) || version != ApplicationSettings.CurrentSchemaVersion)
            {
                return new(SettingsLoadStatus.UnsupportedVersion);
            }

            ApplicationSettings? settings = root.Deserialize<ApplicationSettings>(JsonOptions);
            return IsValid(settings)
                ? new(SettingsLoadStatus.Loaded, settings)
                : new(SettingsLoadStatus.JsonInvalid);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return new(SettingsLoadStatus.JsonInvalid);
        }
    }

    private SettingsLoadResult ClassifyMissingPath()
    {
        // A normal file in the parent chain can also raise DirectoryNotFoundException.
        // Walk only ancestors, without creating them, and distinguish that from a first launch.
        for (string? directory = Path.GetDirectoryName(FilePath);
             directory is not null;
             directory = Path.GetDirectoryName(directory))
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(directory);
                return (attributes & FileAttributes.Directory) != 0
                    ? new(SettingsLoadStatus.Missing, new ApplicationSettings())
                    : new(SettingsLoadStatus.ReadFailed);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // Continue to the nearest existing ancestor.
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
                return new(SettingsLoadStatus.ReadFailed);
            }
        }

        return new(SettingsLoadStatus.ReadFailed);
    }

    private static bool IsValid(ApplicationSettings? settings)
    {
        if (settings is null
            || settings.SchemaVersion != ApplicationSettings.CurrentSchemaVersion
            || settings.MaxConcurrency is < EphemeralEvaluationRunnerOptions.MinimumMaxConcurrency
                or > EphemeralEvaluationRunnerOptions.MaximumMaxConcurrency)
        {
            return false;
        }

        try
        {
            if (settings.ModelPreferences is { } preferences)
            {
                if (preferences.IsDefault || preferences.Length > ApplicationSettings.MaximumCachedModels)
                    return false;
                HashSet<string> preferenceIds = new(StringComparer.Ordinal);
                foreach (ModelSelectionPreference? preference in preferences)
                {
                    if (preference is null || !preferenceIds.Add(preference.ModelId)
                        || preference.ContextTier is not (ModelOptionPolicy.DefaultContextTier or ModelOptionPolicy.LongContextTier))
                        return false;
                    EphemeralEvaluationRunner.ValidateModelId(preference.ModelId);
                    EphemeralEvaluationRunner.ValidateReasoningEffort(preference.ReasoningEffort);
                    ValidateUtf8Text(preference.ModelId, preference.ReasoningEffort);
                }
            }

            if (settings.CachedModels is { } models)
            {
                if (models.IsDefault || models.Length > ApplicationSettings.MaximumCachedModels)
                {
                    return false;
                }

                HashSet<string> ids = new(StringComparer.Ordinal);
                foreach (CachedCopilotModel? model in models)
                {
                    if (model is null || !ids.Add(model.Id)
                        || model.MaximumPromptTokens is <= 0 || model.MaximumContextWindowTokens is <= 0)
                    {
                        return false;
                    }

                    EphemeralEvaluationRunner.ValidateModelId(model.Id);
                    ValidateUtf8Text(model.Id);
                }
            }

            if (settings.PreferredModelId is string modelId)
            {
                EphemeralEvaluationRunner.ValidateModelId(modelId);
            }

            if (settings.OutputDirectoryOverride is string outputDirectory)
            {
                if (string.IsNullOrWhiteSpace(outputDirectory) || !Path.IsPathFullyQualified(outputDirectory))
                {
                    return false;
                }

                _ = Path.GetFullPath(outputDirectory);
            }

            ValidateUtf8Text(settings.PreferredModelId, settings.OutputDirectoryOverride);
            return settings.Definition is not QuantificationDefinition definition
                || (HasPersistableShape(definition)
                    && new QuantificationDefinitionValidator().Validate(definition).IsValid);
        }
        catch (Exception exception) when (IsFileFailure(exception) || exception is OverflowException)
        {
            return false;
        }
    }

    private static bool HasPersistableShape(QuantificationDefinition definition)
    {
        if (!HasItems(definition.Questions))
        {
            return false;
        }

        ValidateUtf8Text(definition.Id, definition.Name, definition.Revision, definition.SourceSheet);
        foreach (QuestionDefinition question in definition.Questions)
        {
            if (!HasColumns(question.SupportingSourceColumns)
                || !HasItems(question.Evaluators)
                || !HasItems(question.SpecialEvaluations))
            {
                return false;
            }

            ValidateUtf8Text(question.Id, question.DisplayName, question.QuestionText, question.PrimarySourceColumn);
            foreach (EvaluatorDefinition evaluator in question.Evaluators)
            {
                if (!HasItems(evaluator.Criteria))
                {
                    return false;
                }

                ValidateUtf8Text(
                    evaluator.Id, evaluator.DisplayName, evaluator.BuiltInTemplateVersion, evaluator.CustomPromptTemplate);
                foreach (CriterionDefinition criterion in evaluator.Criteria)
                {
                    ValidateUtf8Text(criterion.Id, criterion.DisplayName, criterion.Description);
                }
            }

            foreach (SpecialEvaluationDefinition special in question.SpecialEvaluations)
            {
                if (!HasColumns(special.SupportingSourceColumns))
                {
                    return false;
                }

                ValidateUtf8Text(special.Id, special.DisplayName, special.PrimarySourceColumn, special.PromptTemplate);
            }
        }

        return true;
    }

    private static bool HasItems<T>(ImmutableArray<T> items) where T : class =>
        !items.IsDefault && items.All(item => item is not null);

    private static bool HasColumns(ImmutableArray<string> columns)
    {
        if (!HasItems(columns))
        {
            return false;
        }

        foreach (string column in columns)
        {
            ValidateUtf8Text(column);
        }

        return true;
    }

    private static void ValidateUtf8Text(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (value is not null)
            {
                // Reject unpaired UTF-16 surrogates instead of serializing replacement characters.
                _ = StrictUtf8.GetByteCount(value);
            }
        }
    }

    private static bool IsFileFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or NotSupportedException;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        DefaultJsonTypeInfoResolver resolver = new();
        resolver.Modifiers.Add(typeInfo =>
        {
            if (typeInfo.Type == typeof(ScoreRange))
            {
                // The Core record struct also has a default constructor. Do not let a missing
                // endpoint silently become zero, even when that would form a valid range.
                foreach (JsonPropertyInfo property in typeInfo.Properties)
                {
                    property.IsRequired = true;
                }
            }
        });

        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = resolver,
        };
    }
}