using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;

namespace StudyReportEvaluator.App.Workbooks.Checkpoint;

internal sealed class CheckpointFormatException : Exception
{
    internal CheckpointFormatException(string code)
        : base("The checkpoint payload is invalid.")
    {
        Code = code;
    }

    internal string Code { get; }
}

internal sealed record CheckpointPayloadEncoding(
    string Payload,
    string Sha256,
    ImmutableArray<string> Chunks);

internal static class CheckpointPayloadCodec
{
    internal const int MaximumChunkCharacters = 30_000;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    internal static CheckpointPayloadEncoding Encode(CheckpointEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateEnvelope(envelope);
        string payload = JsonSerializer.Serialize(envelope, SerializerOptions);
        string sha256 = ComputeSha256(payload);
        return new CheckpointPayloadEncoding(payload, sha256, Chunk(payload));
    }

    internal static CheckpointEnvelope Decode(string payload, string expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!IsUpperHexSha256(expectedSha256)
            || !string.Equals(ComputeSha256(payload), expectedSha256, StringComparison.Ordinal))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.HashMismatch);
        }

        CheckpointEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CheckpointEnvelope>(payload, SerializerOptions)
                ?? throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
        catch (CheckpointFormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
            or NotSupportedException
            or ArgumentException)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        if (envelope.SchemaVersion != CheckpointEnvelope.CurrentSchemaVersion)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.SchemaUnsupported);
        }

        ValidateEnvelope(envelope);
        if (!string.Equals(
                JsonSerializer.Serialize(envelope, SerializerOptions),
                payload,
                StringComparison.Ordinal))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        return envelope;
    }

    internal static string SerializeValue<T>(T value) =>
        JsonSerializer.Serialize(value, SerializerOptions);

    internal static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void ValidateEnvelope(CheckpointEnvelope envelope)
    {
        if (envelope.SchemaVersion != CheckpointEnvelope.CurrentSchemaVersion)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.SchemaUnsupported);
        }

        ValidateCanonicalPath(envelope.InputPath, ".xlsx");
        ValidateInput(envelope.Input);
        ValidateDefinition(envelope.DefinitionCanonicalJson, envelope.DefinitionSha256);
        ValidateBoundedText(envelope.NormalModelId, 256);
        ValidateBoundedText(envelope.ReferenceModelId, 256);
        if (envelope.ReasoningEffort is not null
            && !ReasoningEffortPolicy.IsSafeReasoningEffort(envelope.ReasoningEffort))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        ValidateRuntime(envelope.Runtime);
        if (envelope.ContextTier is not (null or ModelOptionPolicy.LongContextTier))
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        ValidateCanonicalPath(envelope.FinalPath, ".xlsx");
        ValidateCanonicalPath(envelope.PartialPath, ".partial.xlsx");
        if (envelope.FinalPath.EndsWith(".partial.xlsx", StringComparison.OrdinalIgnoreCase)
            || !SameDirectory(envelope.FinalPath, envelope.PartialPath)
            || PathsEqual(envelope.InputPath, envelope.FinalPath)
            || PathsEqual(envelope.InputPath, envelope.PartialPath)
            || !NamesShareStem(envelope.FinalPath, envelope.PartialPath))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        ValidateUtc(envelope.StartedAtUtc);
        ValidateUtc(envelope.SavedAtUtc);
        if (envelope.StartedAtUtc == default
            || envelope.SavedAtUtc == default
            || envelope.SavedAtUtc < envelope.StartedAtUtc
            || envelope.References.IsDefault
            || envelope.CompletedRows.IsDefault)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        HashSet<string> referenceIds = new(StringComparer.Ordinal);
        foreach (CheckpointReference? reference in envelope.References)
        {
            if (reference is null || !referenceIds.Add(reference.QuestionId))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            ValidateIdentifier(reference.QuestionId);
            ValidateStatus(reference.StatusCode);
            ValidateAttemptCount(reference.AttemptCount);
            ValidateUtc(reference.GeneratedAtUtc);
            if (reference.GeneratedAtUtc < envelope.StartedAtUtc
                || reference.GeneratedAtUtc > envelope.SavedAtUtc
                || reference.Answer is { Length: > ConfigSheetWriter.MaximumCellCharacters }
                || (IsSuccess(reference.StatusCode) != !string.IsNullOrWhiteSpace(reference.Answer)))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            ValidateTokenUsage(reference.TokenUsage);
        }

        HashSet<int> completedRows = [];
        int priorRow = 0;
        foreach (CheckpointCompletedRow? row in envelope.CompletedRows)
        {
            if (row is null
                || row.SourceRowNumber <= priorRow
                || !completedRows.Add(row.SourceRowNumber)
                || row.NormalResults.IsDefault
                || row.SpecialResults.IsDefault
                || row.SimilarityResults.IsDefault)
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            priorRow = row.SourceRowNumber;
            ValidateNormalResults(row.NormalResults);
            ValidateSpecialResults(row.SpecialResults);
            ValidateSimilarityResults(row.SimilarityResults);
        }
    }

    private static void ValidateInput(Workbooks.Intake.InputSnapshot input)
    {
        if (input is null
            || !IsUpperHexSha256(input.Sha256)
            || input.SizeBytes < 0
            || input.LastWriteTimeUtc.Offset != TimeSpan.Zero)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
    }

    private static void ValidateDefinition(string canonicalJson, string sha256)
    {
        if (string.IsNullOrEmpty(canonicalJson)
            || !IsUpperHexSha256(sha256)
            || !string.Equals(ComputeSha256(canonicalJson), sha256, StringComparison.Ordinal))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.HashMismatch);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(canonicalJson);
            JsonElement root = document.RootElement;
            JsonProperty first = root.ValueKind == JsonValueKind.Object
                ? root.EnumerateObject().FirstOrDefault()
                : default;
            if (!string.Equals(first.Name, "schemaVersion", StringComparison.Ordinal)
                || first.Value.ValueKind != JsonValueKind.String
                || !string.Equals(
                    first.Value.GetString(),
                    CanonicalDefinitionSerializer.SchemaVersion,
                    StringComparison.Ordinal))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }
        }
        catch (CheckpointFormatException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
    }

    private static void ValidateRuntime(CheckpointRuntimeIdentity runtime)
    {
        if (runtime is null)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        ValidateBoundedText(runtime.ApplicationIdentity, 256);
        if (!CopilotRuntimeIdentity.IsSafeVersion(runtime.CliVersion)
            || !IsUpperHexSha256(runtime.CliSha256)
            || !CopilotRuntimeIdentity.IsSafeVersion(runtime.SdkInformationalVersion))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
    }

    private static void ValidateNormalResults(ImmutableArray<CheckpointNormalResult> results)
    {
        HashSet<(string QuestionId, string EvaluatorId)> keys = [];
        foreach (CheckpointNormalResult? result in results)
        {
            if (result is null
                || !keys.Add((result.QuestionId, result.EvaluatorId)))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            ValidateIdentifier(result.QuestionId);
            ValidateIdentifier(result.EvaluatorId);
            ValidateStatus(result.StatusCode);
            ValidateAttemptCount(result.AttemptCount);
            if (!result.ScorableKnown && result.Scorable
                || (IsSuccess(result.StatusCode) != (result.AcceptedResult is not null)))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            if (result.AcceptedResult is QuantificationResult accepted)
            {
                if (!string.Equals(accepted.EvaluatorId, result.EvaluatorId, StringComparison.Ordinal)
                    || accepted.Criteria.IsDefault)
                {
                    throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
                }

                HashSet<string> criterionIds = new(StringComparer.Ordinal);
                foreach (CriterionQuantificationResult? criterion in accepted.Criteria)
                {
                    if (criterion is null || !criterionIds.Add(criterion.CriterionId))
                    {
                        throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
                    }

                    ValidateIdentifier(criterion.CriterionId);
                    ValidateCellText(criterion.Reason);
                    ValidateCellText(criterion.Evidence);
                    ValidateCellText(criterion.EvidenceSourceColumnId);
                }
            }

            ValidateTokenUsage(result.TokenUsage);
        }
    }

    private static void ValidateSpecialResults(ImmutableArray<CheckpointSpecialResult> results)
    {
        HashSet<(string QuestionId, string SpecialId)> keys = [];
        foreach (CheckpointSpecialResult? result in results)
        {
            if (result is null
                || !keys.Add((result.QuestionId, result.SpecialEvaluationId)))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            ValidateIdentifier(result.QuestionId);
            ValidateIdentifier(result.SpecialEvaluationId);
            ValidateStatus(result.StatusCode);
            ValidateAttemptCount(result.AttemptCount);
            if (IsSuccess(result.StatusCode) != (result.AcceptedResult is not null))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            if (result.AcceptedResult is SpecialQuantificationResult accepted)
            {
                if (!string.Equals(
                        accepted.SpecialEvaluationId,
                        result.SpecialEvaluationId,
                        StringComparison.Ordinal)
                    || accepted.Score is < 0m or > 1m)
                {
                    throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
                }

                ValidateCellText(accepted.Reason);
                ValidateCellText(accepted.Evidence);
                ValidateCellText(accepted.EvidenceSourceColumnId);
            }

            ValidateTokenUsage(result.TokenUsage);
        }
    }

    private static void ValidateSimilarityResults(ImmutableArray<CheckpointSimilarityResult> results)
    {
        HashSet<string> questionIds = new(StringComparer.Ordinal);
        foreach (CheckpointSimilarityResult? result in results)
        {
            if (result is null || !questionIds.Add(result.QuestionId))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            ValidateIdentifier(result.QuestionId);
            ValidateStatus(result.StatusCode);
            ValidateAttemptCount(result.AttemptCount);
            if (IsSuccess(result.StatusCode) != (result.AcceptedResult is not null))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }

            if (result.AcceptedResult is SimilarityQuantificationResult accepted)
            {
                if (!string.Equals(accepted.QuestionId, result.QuestionId, StringComparison.Ordinal)
                    || accepted.Similarity is < 0m or > 1m)
                {
                    throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
                }

                ValidateCellText(accepted.Reason);
            }

            ValidateTokenUsage(result.TokenUsage);
        }
    }

    private static void ValidateTokenUsage(CheckpointTokenUsage usage)
    {
        if (usage is null
            || usage.InputTokens < 0
            || usage.OutputTokens < 0
            || usage.ReasoningTokens < 0
            || usage.CacheReadTokens < 0
            || usage.CacheWriteTokens < 0
            || (!usage.IsAvailable
                && (usage.InputTokens != 0
                    || usage.OutputTokens != 0
                    || usage.ReasoningTokens != 0
                    || usage.CacheReadTokens != 0
                    || usage.CacheWriteTokens != 0)))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
    }

    private static void ValidateCanonicalPath(string path, string requiredSuffix)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }

        try
        {
            string canonical = Path.GetFullPath(path);
            if (!PathsEqual(canonical, path)
                || !path.EndsWith(requiredSuffix, StringComparison.OrdinalIgnoreCase))
            {
                throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
            }
        }
        catch (CheckpointFormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
    }

    private static void ValidateIdentifier(string value) => ValidateBoundedText(value, 256);

    private static void ValidateStatus(string value)
    {
        if (!ResultsStatusCodes.IsDefined(value))
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
    }

    private static void ValidateCellText(string value)
    {
        if (value is null || value.Length > ConfigSheetWriter.MaximumCellCharacters)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
    }

    private static void ValidateBoundedText(string value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
    }

    private static void ValidateAttemptCount(int attemptCount)
    {
        if (attemptCount < 0)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
    }

    private static void ValidateUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new CheckpointFormatException(CheckpointStatusCodes.Invalid);
        }
    }

    private static bool IsSuccess(string statusCode) =>
        string.Equals(statusCode, ResultsStatusCodes.Success, StringComparison.Ordinal);

    private static bool IsUpperHexSha256(string value) =>
        value is { Length: 64 }
        && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F');

    private static bool SameDirectory(string first, string second) =>
        PathsEqual(
            Path.GetDirectoryName(first) ?? string.Empty,
            Path.GetDirectoryName(second) ?? string.Empty);

    private static bool NamesShareStem(string finalPath, string partialPath)
    {
        const string finalSuffix = ".xlsx";
        const string partialSuffix = ".partial.xlsx";
        string finalName = Path.GetFileName(finalPath);
        string partialName = Path.GetFileName(partialPath);
        return finalName.Length > finalSuffix.Length
            && partialName.Length > partialSuffix.Length
            && string.Equals(
                finalName[..^finalSuffix.Length],
                partialName[..^partialSuffix.Length],
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            first,
            second,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static ImmutableArray<string> Chunk(string value)
    {
        ImmutableArray<string>.Builder chunks = ImmutableArray.CreateBuilder<string>();
        for (int offset = 0; offset < value.Length;)
        {
            int length = Math.Min(MaximumChunkCharacters, value.Length - offset);
            if (offset + length < value.Length && char.IsHighSurrogate(value[offset + length - 1]))
            {
                length--;
            }

            chunks.Add(value.Substring(offset, length));
            offset += length;
        }

        if (chunks.Count == 0)
        {
            chunks.Add(string.Empty);
        }

        return chunks.ToImmutable();
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
