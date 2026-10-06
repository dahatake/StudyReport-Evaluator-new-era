using System.Collections.Immutable;
using System.Text.Json;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.App.Copilot;

internal static class AuxiliaryToolWireParser
{
    internal const int MaximumCellCharacters = 32_767;

    internal static Dictionary<string, JsonElement>? ReadClosedObject(
        JsonElement? arguments,
        IReadOnlyCollection<string> requiredProperties,
        ImmutableArray<string>.Builder errors)
    {
        if (arguments is not JsonElement root || root.ValueKind != JsonValueKind.Object)
        {
            errors.Add("ROOT_OBJECT_REQUIRED");
            return null;
        }

        Dictionary<string, JsonElement> values = new(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!requiredProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                errors.Add("ROOT_UNKNOWN_PROPERTY");
                continue;
            }

            if (!values.TryAdd(property.Name, property.Value))
            {
                errors.Add("ROOT_DUPLICATE_PROPERTY");
            }
        }

        if (requiredProperties.Any(property => !values.ContainsKey(property)))
        {
            errors.Add("ROOT_MISSING_PROPERTY");
        }

        return values;
    }

    internal static bool TryReadString(
        IReadOnlyDictionary<string, JsonElement> values,
        string propertyName,
        bool requireNonWhitespace,
        ImmutableArray<string>.Builder errors,
        out string value)
    {
        value = string.Empty;
        if (!values.TryGetValue(propertyName, out JsonElement element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add("PROPERTY_TYPE_INVALID");
            return false;
        }

        value = element.GetString() ?? string.Empty;
        if (value.Length > MaximumCellCharacters)
        {
            errors.Add("TEXT_LENGTH_INVALID");
            return false;
        }

        if (requireNonWhitespace && string.IsNullOrWhiteSpace(value))
        {
            errors.Add("TEXT_REQUIRED");
            return false;
        }

        return true;
    }

    internal static bool TryReadDecimal(
        IReadOnlyDictionary<string, JsonElement> values,
        string propertyName,
        ImmutableArray<string>.Builder errors,
        out decimal value)
    {
        value = default;
        if (!values.TryGetValue(propertyName, out JsonElement element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out value))
        {
            errors.Add("NUMBER_INVALID");
            return false;
        }

        return true;
    }

    internal static bool TryReadEvidenceSource(
        IReadOnlyDictionary<string, JsonElement> values,
        ImmutableArray<string>.Builder errors,
        out EvidenceSourceKind value)
    {
        value = default;
        if (!values.TryGetValue("EvidenceSource", out JsonElement element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add("PROPERTY_TYPE_INVALID");
            return false;
        }

        value = element.GetString() switch
        {
            "PRIMARY_ANSWER" => EvidenceSourceKind.PrimaryAnswer,
            "SUPPORTING_COLUMN" => EvidenceSourceKind.SupportingColumn,
            "NONE" => EvidenceSourceKind.None,
            _ => (EvidenceSourceKind)(-1),
        };
        if (!Enum.IsDefined(value))
        {
            errors.Add("EVIDENCE_SOURCE_INVALID");
            return false;
        }

        return true;
    }

    internal static ImmutableArray<string> NormalizeCodes(IEnumerable<string> codes) =>
        codes.Distinct(StringComparer.Ordinal).ToImmutableArray();
}
