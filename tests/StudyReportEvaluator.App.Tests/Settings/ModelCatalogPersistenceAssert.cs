using System.Text.Json;
using StudyReportEvaluator.App.Settings;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Settings;

internal static class ModelCatalogPersistenceAssert
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Null means the file did not exist: only default settings plus the catalog may be created.
    // Compare raw values, not deserialized records, so nested content, number spelling,
    // escaping, property order, missing fields and unexpected fields remain observable.
    internal static byte[] OnlyCatalogChanged(
        byte[]? before, byte[] after, IReadOnlyList<CachedCopilotModel> expectedCatalog)
    {
        using JsonDocument baseline = JsonDocument.Parse(before ?? JsonSerializer.SerializeToUtf8Bytes(new ApplicationSettings()));
        using JsonDocument actual = JsonDocument.Parse(after);
        Assert.Equal(JsonValueKind.Object, baseline.RootElement.ValueKind);
        Assert.Equal(JsonValueKind.Object, actual.RootElement.ValueKind);
        JsonProperty catalog = Assert.Single(actual.RootElement.EnumerateObject(), property => property.Name == "cachedModels");
        Assert.Equal(JsonSerializer.Serialize(expectedCatalog, JsonOptions), catalog.Value.GetRawText());
        Assert.Equal(
            baseline.RootElement.EnumerateObject().Where(property => property.Name != "cachedModels")
                .Select(property => (property.Name, property.Value.GetRawText())).ToArray(),
            actual.RootElement.EnumerateObject().Where(property => property.Name != "cachedModels")
                .Select(property => (property.Name, property.Value.GetRawText())).ToArray());
        return after;
    }
}