using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Serialization;
using StudyReportEvaluator.Core.Validation;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Settings;

// Requirements: FR-040 (AC-041)
public sealed class SettingsFileStoreTests
{
    private const string ModelCanary = "PRIVATE-T03-MODEL";
    private const string TextCanary = "PRIVATE-T03-TEXT 日本語 🌸 e\u0301\r\n\"引用\"と\\と{brace}\n末尾  ";
    private const string PromptCanary =
        "PRIVATE-T03-PROMPT 日本語 🌸\r\n{{brace}} \\\"引用\\\"\n{設問} {回答} {補助情報}\n{評価項目} {最小点} {最大点}\n末尾  ";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions FixtureJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static bool IsWindows => OperatingSystem.IsWindows();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("setting.txt")]
    [InlineData("relative/setting.txt")]
    public void Constructor_rejects_blank_and_relative_paths(string path)
    {
        Assert.Throws<ArgumentException>(() => new SettingsFileStore(path));
    }

    [Fact]
    public void Constructor_rejects_null_invalid_and_root_paths_without_exposing_the_path()
    {
        using TemporarySettingsDirectory fixture = new();
        Assert.Throws<ArgumentNullException>(() => new SettingsFileStore(null!));
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new SettingsFileStore(fixture.FilePath + "\0PRIVATE-T03-INVALID-PATH"));
        Assert.DoesNotContain("PRIVATE-T03-INVALID-PATH", exception.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new SettingsFileStore(Path.GetPathRoot(fixture.FilePath)!));
        Assert.False(Directory.Exists(fixture.Root));
    }

    [Fact]
    public void Constructor_normalizes_the_injected_absolute_path_without_creating_anything()
    {
        using TemporarySettingsDirectory fixture = new();
        string path = Path.Combine(fixture.Root, "unused", "..", "設定", "setting.txt");

        SettingsFileStore store = new(path);

        Assert.Equal(Path.GetFullPath(path), store.FilePath);
        Assert.False(Directory.Exists(fixture.Root));
        Assert.DoesNotContain(fixture.Root, store.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_file_returns_successful_defaults_without_creating_file_or_parent(bool parentExists)
    {
        using TemporarySettingsDirectory fixture = new();
        if (parentExists)
        {
            Directory.CreateDirectory(fixture.SettingsDirectory);
        }

        SettingsLoadResult result = await new SettingsFileStore(fixture.FilePath)
            .LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.Missing, result.Status);
        Assert.True(result.IsSuccess);
        Assert.Equal(new ApplicationSettings(), result.Settings);
        Assert.False(File.Exists(fixture.FilePath));
        Assert.Equal(parentExists, Directory.Exists(fixture.SettingsDirectory));
        Assert.Equal(parentExists, Directory.Exists(fixture.Root));
        if (parentExists)
        {
            Assert.Empty(Directory.GetFileSystemEntries(fixture.SettingsDirectory));
        }
    }

    [Fact]
    public async Task First_explicit_save_creates_only_settings_and_round_trips_defaults()
    {
        using TemporarySettingsDirectory fixture = new();
        SettingsFileStore store = new(fixture.FilePath);
        Assert.False(Directory.Exists(fixture.Root));

        SettingsSaveResult saved = await store.SaveAsync(new ApplicationSettings(), TestContext.Current.CancellationToken);
        SettingsLoadResult loaded = await new SettingsFileStore(fixture.FilePath)
            .LoadAsync(TestContext.Current.CancellationToken);

        AssertSaved(saved);
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(new ApplicationSettings(), loaded.Settings);
        byte[] bytes = File.ReadAllBytes(fixture.FilePath);
        Assert.False(bytes.AsSpan().StartsWith("\uFEFF"u8));
        using JsonDocument document = JsonDocument.Parse(StrictUtf8.GetString(bytes));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            ["definition", "maxConcurrency", "outputDirectoryOverride", "preferredModelId", "schemaVersion"],
            document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        fixture.AssertOnlySettingsFile();
        Assert.False(Directory.Exists(fixture.OutputDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Load_accepts_strict_utf8_with_or_without_bom_without_rewriting(bool includeBom)
    {
        using TemporarySettingsDirectory fixture = new();
        const string json = """{"schemaVersion":1,"preferredModelId":"合成-model-🌸","maxConcurrency":2}""";
        byte[] original = fixture.WriteJson(json, includeBom);

        SettingsLoadResult result = await new SettingsFileStore(fixture.FilePath)
            .LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.True(result.IsSuccess);
        ApplicationSettings settings = Assert.IsType<ApplicationSettings>(result.Settings);
        Assert.Equal("合成-model-🌸", settings.PreferredModelId);
        Assert.Equal(2, settings.MaxConcurrency);
        Assert.Null(settings.Definition);
        fixture.AssertUnchanged(original);
        fixture.AssertOnlySettingsFile();
    }

    [Theory]
    [InlineData("continuation")]
    [InlineData("overlong")]
    [InlineData("surrogate")]
    [InlineData("truncated")]
    [InlineData("out-of-unicode-range")]
    [InlineData("utf16-bom")]
    public async Task Load_rejects_invalid_utf8_instead_of_replacing_characters(string caseName)
    {
        using TemporarySettingsDirectory fixture = new();
        byte[] invalid = caseName switch
        {
            "continuation" => [0x80],
            "overlong" => [0xC0, 0xAF],
            "surrogate" => [0xED, 0xA0, 0x80],
            "truncated" => [0xF0, 0x9F, 0x8C],
            "out-of-unicode-range" => [0xF4, 0x90, 0x80, 0x80],
            "utf16-bom" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
        };
        byte[] original = caseName == "utf16-bom"
            ? [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("""{"schemaVersion":1}""")]
            : [.. "{\"schemaVersion\":1,\"preferredModelId\":\""u8, .. invalid, .. "\"}"u8];
        fixture.WriteBytes(original);

        SettingsLoadResult result = await new SettingsFileStore(fixture.FilePath)
            .LoadAsync(TestContext.Current.CancellationToken);

        AssertLoadFailed(result, SettingsLoadStatus.JsonInvalid);
        fixture.AssertUnchanged(original);
        fixture.AssertOnlySettingsFile();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Valid_common_preferences_preserve_model_and_path_without_resolving_or_creating_output(int concurrency)
    {
        using TemporarySettingsDirectory fixture = new();
        ApplicationSettings expected = new()
        {
            PreferredModelId = new string('m', 256),
            MaxConcurrency = concurrency,
            OutputDirectoryOverride = fixture.OutputDirectory,
        };
        SettingsFileStore store = new(fixture.FilePath);

        AssertSaved(await store.SaveAsync(expected, TestContext.Current.CancellationToken));
        SettingsLoadResult result = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(expected, result.Settings);
        Assert.False(Directory.Exists(fixture.OutputDirectory));
        fixture.AssertOnlySettingsFile();
    }

    [Fact]
    public async Task Unicode_nested_definition_round_trip_preserves_every_field_order_and_canonical_hash()
    {
        using TemporarySettingsDirectory fixture = new();
        ApplicationSettings original = CreateSettings(fixture);
        QuantificationDefinition definition = Assert.IsType<QuantificationDefinition>(original.Definition);
        Assert.True(new QuantificationDefinitionValidator().Validate(definition).IsValid);
        CanonicalDefinitionSerializer canonical = new();
        string originalHash = canonical.ComputeSha256(definition);
        SettingsFileStore store = new(fixture.FilePath);

        AssertSaved(await store.SaveAsync(original, TestContext.Current.CancellationToken));
        byte[] savedBytes = File.ReadAllBytes(fixture.FilePath);
        SettingsLoadResult result = await new SettingsFileStore(fixture.FilePath)
            .LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.True(result.IsSuccess);
        ApplicationSettings restored = Assert.IsType<ApplicationSettings>(result.Settings);
        AssertSettingsEqual(original, restored);
        Assert.NotSame(original, restored);
        Assert.NotSame(definition, restored.Definition);
        Assert.Equal(originalHash, canonical.ComputeSha256(restored.Definition!));
        Assert.Equal(originalHash, canonical.ComputeSha256(definition));
        Assert.True(new QuantificationDefinitionValidator().Validate(restored.Definition!).IsValid);
        fixture.AssertUnchanged(savedBytes);
        Assert.False(Directory.Exists(fixture.OutputDirectory));

        using JsonDocument document = JsonDocument.Parse(savedBytes);
        JsonElement jsonDefinition = document.RootElement.GetProperty("definition");
        Assert.Equal(definition.BasePoints, jsonDefinition.GetProperty("basePoints").GetDecimal());
        Assert.Equal(definition.SimilarityPenaltyWeight, jsonDefinition.GetProperty("similarityPenaltyWeight").GetDecimal());
        JsonElement question = jsonDefinition.GetProperty("questions")[0];
        Assert.Equal(TextCanary, question.GetProperty("questionText").GetString());
        JsonElement evaluator = question.GetProperty("evaluators")[0];
        Assert.Equal(PromptCanary, evaluator.GetProperty("customPromptTemplate").GetString());
        Assert.Equal(-1.125m, evaluator.GetProperty("range").GetProperty("minimum").GetDecimal());

        // A second settings serialization is not used as the canonical-hash oracle.
        AssertSaved(await new SettingsFileStore(fixture.FilePath).SaveAsync(restored, TestContext.Current.CancellationToken));
        SettingsLoadResult second = await store.LoadAsync(TestContext.Current.CancellationToken);
        AssertSettingsEqual(original, Assert.IsType<ApplicationSettings>(second.Settings));
        Assert.Equal(originalHash, canonical.ComputeSha256(second.Settings!.Definition!));
        fixture.AssertOnlySettingsFile();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("""{"maxConcurrency":1}""")]
    [InlineData("""{"SchemaVersion":1}""")]
    [InlineData("""{"schemaVersion":null}""")]
    [InlineData("""{"schemaVersion":"1"}""")]
    [InlineData("""{"schemaVersion":true}""")]
    [InlineData("""{"schemaVersion":[]}""")]
    [InlineData("""{"schemaVersion":{}}""")]
    [InlineData("""{"schemaVersion":1.0}""")]
    [InlineData("""{"schemaVersion":1e0}""")]
    [InlineData("""{"schemaVersion":1,}""")]
    [InlineData("""{/*comment*/"schemaVersion":1}""")]
    [InlineData("""{"schemaVersion":1} {}""")]
    public async Task Missing_wrong_schema_and_invalid_json_are_JsonInvalid_and_preserved(string json)
    {
        using TemporarySettingsDirectory fixture = new();
        await AssertRejectedJsonAsync(fixture, json);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2")]
    [InlineData("2147483648")]
    [InlineData("999999999999999999999999999999999999999")]
    public async Task Other_integer_schemas_are_UnsupportedVersion_without_interpreting_or_repairing_them(string version)
    {
        using TemporarySettingsDirectory fixture = new();
        string json = "{\"schemaVersion\":" + version + ",\"futureSetting\":{\"value\":true}}";

        await AssertRejectedJsonAsync(fixture, json, SettingsLoadStatus.UnsupportedVersion);
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"schemaVersion":1}""")]
    [InlineData("""{"schemaVersion":1,"schemaVersion":2}""")]
    [InlineData("""{"schemaVersion":2,"schemaVersion":1}""")]
    [InlineData("""{"schemaVersion":2,"schemaVersion":3}""")]
    [InlineData("""{"schemaVersion":1,"\u0073chemaVersion":1}""")]
    [InlineData("""{"schemaVersion":2,"future":{"value":1,"value":2}}""")]
    public async Task Duplicate_properties_are_rejected_before_schema_dispatch_including_escaped_names(string json)
    {
        using TemporarySettingsDirectory fixture = new();
        await AssertRejectedJsonAsync(fixture, json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("definition")]
    [InlineData("definition.questions.0")]
    [InlineData("definition.questions.0.evaluators.0")]
    [InlineData("definition.questions.0.evaluators.0.range")]
    [InlineData("definition.questions.0.evaluators.0.criteria.0")]
    [InlineData("definition.questions.0.evaluators.0.criteria.0.range")]
    [InlineData("definition.questions.0.specialEvaluations.0")]
    public async Task Unknown_and_duplicate_members_are_rejected_at_every_object_type(string objectPath)
    {
        using TemporarySettingsDirectory fixture = new();
        JsonObject root = CreateJson(fixture);
        JsonObject target = NodeAtPath(root, objectPath).AsObject();
        KeyValuePair<string, JsonNode?> first = target.First();
        string targetJson = target.ToJsonString();
        string rootJson = root.ToJsonString();
        Assert.Equal(2, rootJson.Split(targetJson, StringSplitOptions.None).Length);
        string repeatedProperty = JsonSerializer.Serialize(first.Key) + ":" + first.Value!.ToJsonString();
        string duplicateJson = targetJson[..^1] + "," + repeatedProperty + "}";

        await AssertRejectedJsonAsync(
            fixture,
            rootJson.Replace(targetJson, duplicateJson, StringComparison.Ordinal));

        target["PRIVATE-T03-UNKNOWN-MEMBER"] = "PRIVATE-T03-UNKNOWN-VALUE";
        await AssertRejectedJsonAsync(fixture, root.ToJsonString());
    }

    [Theory]
    [InlineData("maxConcurrency", "0")]
    [InlineData("maxConcurrency", "17")]
    [InlineData("maxConcurrency", "null")]
    [InlineData("maxConcurrency", "\"2\"")]
    [InlineData("maxConcurrency", "1.5")]
    [InlineData("maxConcurrency", "2147483648")]
    [InlineData("preferredModelId", "\"\"")]
    [InlineData("preferredModelId", "\" model\"")]
    [InlineData("preferredModelId", "\"model\\n\"")]
    [InlineData("outputDirectoryOverride", "\" \"")]
    [InlineData("outputDirectoryOverride", "\"relative/output\"")]
    [InlineData("definition", "[]")]
    [InlineData("definition.id", "null")]
    [InlineData("definition.name", "\"\"")]
    [InlineData("definition.headerRow", "3")]
    [InlineData("definition.lastDataRow", "1")]
    [InlineData("definition.basePoints", "-1")]
    [InlineData("definition.basePoints", "101")]
    [InlineData("definition.specialPoints", "-1")]
    [InlineData("definition.similarityPenaltyWeight", "1.1")]
    [InlineData("definition.roundingDigits", "7")]
    [InlineData("definition.questions", "null")]
    [InlineData("definition.questions", "[]")]
    [InlineData("definition.questions", "[null]")]
    [InlineData("definition.questions.0.questionText", "null")]
    [InlineData("definition.questions.0.supportingSourceColumns", "null")]
    [InlineData("definition.questions.0.supportingSourceColumns", "[null]")]
    [InlineData("definition.questions.0.points", "34.500001")]
    [InlineData("definition.questions.0.evaluators", "null")]
    [InlineData("definition.questions.0.evaluators", "[null]")]
    [InlineData("definition.questions.0.evaluators.0.type", "-1")]
    [InlineData("definition.questions.0.evaluators.0.type", "99")]
    [InlineData("definition.questions.0.evaluators.0.type", "\"NO_SUCH_TYPE\"")]
    [InlineData("definition.questions.0.evaluators.0.type", "null")]
    [InlineData("definition.questions.0.evaluators.0.weight", "0")]
    [InlineData("definition.questions.0.evaluators.0.weight", "\"1\"")]
    [InlineData("definition.questions.0.evaluators.0.weight", "1e1000")]
    [InlineData("definition.questions.0.evaluators.0.range", "null")]
    [InlineData("definition.questions.0.evaluators.0.range", "{}")]
    [InlineData("definition.questions.0.evaluators.0.range", """{"minimum":-1}""")]
    [InlineData("definition.questions.0.evaluators.0.range", """{"maximum":1}""")]
    [InlineData("definition.questions.0.evaluators.0.range", """{"minimum":0,"maximum":0}""")]
    [InlineData("definition.questions.0.evaluators.0.criteria", "null")]
    [InlineData("definition.questions.0.evaluators.0.criteria", "[null]")]
    [InlineData("definition.questions.0.evaluators.0.criteria.0.description", "null")]
    [InlineData("definition.questions.0.evaluators.0.criteria.0.range", "{}")]
    [InlineData("definition.questions.0.evaluators.0.criteria.0.range", """{"maximum":1}""")]
    [InlineData("definition.questions.0.evaluators.0.criteria.0.range.minimum", "\"0\"")]
    [InlineData("definition.questions.0.evaluators.0.criteria.0.enabled", "\"true\"")]
    [InlineData("definition.questions.0.evaluators.0.customPromptTemplate", "null")]
    [InlineData("definition.questions.0.evaluators.0.customPromptTemplate", "\"{回答} {未知}\"")]
    [InlineData("definition.questions.0.specialEvaluations", "null")]
    [InlineData("definition.questions.0.specialEvaluations", "[null]")]
    [InlineData("definition.questions.0.specialEvaluations.0.supportingSourceColumns", "null")]
    [InlineData("definition.questions.0.specialEvaluations.0.supportingSourceColumns", "[null]")]
    [InlineData("definition.questions.0.specialEvaluations.0.promptTemplate", "null")]
    [InlineData("definition.questions.0.specialEvaluations.0.promptTemplate", "\"{評価項目}\"")]
    public async Task Load_rejects_invalid_types_values_nulls_ranges_and_prompts(string propertyPath, string replacementJson)
    {
        using TemporarySettingsDirectory fixture = new();
        JsonObject root = CreateJson(fixture);
        (JsonObject parent, string name) = PropertyAtPath(root, propertyPath);
        parent[name] = JsonNode.Parse(replacementJson);

        await AssertRejectedJsonAsync(fixture, root.ToJsonString());
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("definition.id")]
    [InlineData("definition.name")]
    [InlineData("definition.revision")]
    [InlineData("definition.sourceSheet")]
    [InlineData("definition.questions.0.questionText")]
    [InlineData("definition.questions.0.primarySourceColumn")]
    [InlineData("definition.questions.0.evaluators.0.id")]
    [InlineData("definition.questions.0.evaluators.0.range.minimum")]
    [InlineData("definition.questions.0.evaluators.0.range.maximum")]
    [InlineData("definition.questions.0.evaluators.0.criteria.0.description")]
    [InlineData("definition.questions.0.evaluators.0.criteria.0.range.minimum")]
    [InlineData("definition.questions.0.specialEvaluations.0.promptTemplate")]
    public async Task Load_rejects_missing_required_values_including_either_range_endpoint(string propertyPath)
    {
        using TemporarySettingsDirectory fixture = new();
        JsonObject root = CreateJson(fixture);
        (JsonObject parent, string name) = PropertyAtPath(root, propertyPath);
        Assert.True(parent.Remove(name));

        await AssertRejectedJsonAsync(fixture, root.ToJsonString());
    }

    [Theory]
    [InlineData("null-settings")]
    [InlineData("schema")]
    [InlineData("concurrency-low")]
    [InlineData("concurrency-high")]
    [InlineData("model-empty")]
    [InlineData("model-space")]
    [InlineData("model-control")]
    [InlineData("model-long")]
    [InlineData("output-empty")]
    [InlineData("output-space")]
    [InlineData("output-relative")]
    [InlineData("output-invalid")]
    [InlineData("definition-null-id")]
    [InlineData("base-points")]
    [InlineData("allocation-total")]
    [InlineData("similarity-weight")]
    [InlineData("rows")]
    [InlineData("rounding")]
    [InlineData("question-points")]
    [InlineData("question-null-text")]
    [InlineData("invalid-enum")]
    [InlineData("disabled-invalid-enum")]
    [InlineData("evaluator-weight")]
    [InlineData("evaluator-range")]
    [InlineData("criterion-weight")]
    [InlineData("criterion-range")]
    [InlineData("disabled-criterion-range")]
    [InlineData("criterion-null-description")]
    [InlineData("prompt-null")]
    [InlineData("prompt-unknown")]
    [InlineData("prompt-brace")]
    [InlineData("special-prompt-null")]
    [InlineData("special-prompt-invalid")]
    [InlineData("disabled-special-prompt-invalid")]
    [InlineData("default-questions")]
    [InlineData("default-evaluators")]
    [InlineData("default-criteria")]
    [InlineData("default-specials")]
    [InlineData("default-question-columns")]
    [InlineData("default-special-columns")]
    [InlineData("null-question")]
    [InlineData("null-evaluator")]
    [InlineData("null-criterion")]
    [InlineData("null-special")]
    [InlineData("null-question-column")]
    [InlineData("null-special-column")]
    [InlineData("unpaired-surrogate")]
    public async Task Save_rejects_invalid_settings_before_io_and_keeps_existing_bytes(string caseName)
    {
        using TemporarySettingsDirectory fixture = new();
        ApplicationSettings original = CreateSettings(fixture);
        ApplicationSettings invalid = InvalidSettings(original, caseName);
        byte[] originalBytes = fixture.WriteJson(JsonSerializer.Serialize(original, FixtureJsonOptions), includeBom: true);

        SettingsSaveResult result = await new SettingsFileStore(fixture.FilePath)
            .SaveAsync(invalid, TestContext.Current.CancellationToken);

        Assert.Equal(SettingsSaveStatus.InvalidSettings, result.Status);
        Assert.False(result.IsSuccess);
        fixture.AssertUnchanged(originalBytes);
        fixture.AssertOnlySettingsFile();
        Assert.DoesNotContain("PRIVATE-T03", result.ToString(), StringComparison.Ordinal);

        using TemporarySettingsDirectory absent = new();
        SettingsSaveResult firstSave = await new SettingsFileStore(absent.FilePath)
            .SaveAsync(invalid, TestContext.Current.CancellationToken);
        Assert.Equal(SettingsSaveStatus.InvalidSettings, firstSave.Status);
        Assert.False(Directory.Exists(absent.Root));
    }

    [Fact]
    public async Task Long_model_and_invalid_absolute_output_are_also_rejected_on_load()
    {
        using TemporarySettingsDirectory fixture = new();
        JsonObject root = CreateJson(fixture);
        root["preferredModelId"] = new string('m', 257);
        await AssertRejectedJsonAsync(fixture, root.ToJsonString());

        root = CreateJson(fixture);
        root["outputDirectoryOverride"] = fixture.OutputDirectory + "\0PRIVATE-T03-PATH";
        await AssertRejectedJsonAsync(fixture, root.ToJsonString());
    }

    [Fact(SkipUnless = nameof(IsWindows), Skip = "Requires Windows file-sharing semantics.")]
    public async Task Exclusive_handle_refuses_read_and_replace_keeps_old_bytes_and_cleans_only_own_temp()
    {
        using TemporarySettingsDirectory fixture = new();
        SettingsFileStore store = new(fixture.FilePath);
        AssertSaved(await store.SaveAsync(CreateSettings(fixture), TestContext.Current.CancellationToken));
        byte[] original = File.ReadAllBytes(fixture.FilePath);
        string foreignTemporaryPath = Path.Combine(fixture.SettingsDirectory, ".setting-foreign.tmp");
        byte[] foreignBytes = "PRIVATE-T03-OTHER-WRITER"u8.ToArray();
        File.WriteAllBytes(foreignTemporaryPath, foreignBytes);
        FileStream? held = null;
        try
        {
            held = new FileStream(fixture.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            SettingsLoadResult load = await store.LoadAsync(TestContext.Current.CancellationToken);
            SettingsSaveResult save = await store.SaveAsync(
                new ApplicationSettings { PreferredModelId = "new-model" },
                TestContext.Current.CancellationToken);

            AssertLoadFailed(load, SettingsLoadStatus.ReadFailed);
            Assert.Equal(SettingsSaveStatus.WriteFailed, save.Status);
            Assert.False(save.IsSuccess);
            Assert.Equal(
                [".setting-foreign.tmp", "setting.txt"],
                Directory.GetFiles(fixture.SettingsDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            Assert.DoesNotContain(fixture.FilePath, save.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            held?.Dispose();
        }

        fixture.AssertUnchanged(original);
        Assert.Equal(foreignBytes, File.ReadAllBytes(foreignTemporaryPath));
        // Releasing the handle is enough to recover; no repair or attribute change is needed.
        AssertSaved(await store.SaveAsync(new ApplicationSettings(), TestContext.Current.CancellationToken));
        Assert.Equal(foreignBytes, File.ReadAllBytes(foreignTemporaryPath));
    }

    [Fact(SkipUnless = nameof(IsWindows), Skip = "Requires Windows read-only file replacement semantics.")]
    public async Task Read_only_file_can_be_loaded_but_cannot_be_replaced_and_attributes_are_restored()
    {
        using TemporarySettingsDirectory fixture = new();
        ApplicationSettings expected = new() { PreferredModelId = ModelCanary };
        SettingsFileStore store = new(fixture.FilePath);
        AssertSaved(await store.SaveAsync(expected, TestContext.Current.CancellationToken));
        byte[] original = File.ReadAllBytes(fixture.FilePath);
        FileAttributes attributes = File.GetAttributes(fixture.FilePath);
        try
        {
            File.SetAttributes(fixture.FilePath, attributes | FileAttributes.ReadOnly);
            Assert.True((File.GetAttributes(fixture.FilePath) & FileAttributes.ReadOnly) != 0);

            SettingsLoadResult load = await store.LoadAsync(TestContext.Current.CancellationToken);
            SettingsSaveResult save = await store.SaveAsync(new ApplicationSettings(), TestContext.Current.CancellationToken);

            Assert.Equal(SettingsLoadStatus.Loaded, load.Status);
            Assert.Equal(expected, load.Settings);
            Assert.Equal(SettingsSaveStatus.WriteFailed, save.Status);
            Assert.False(save.IsSuccess);
            fixture.AssertUnchanged(original);
            fixture.AssertOnlySettingsFile();
            Assert.True((File.GetAttributes(fixture.FilePath) & FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            File.SetAttributes(fixture.FilePath, attributes);
        }

        fixture.AssertUnchanged(original);
        AssertSaved(await store.SaveAsync(new ApplicationSettings(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Normal_file_in_parent_chain_is_ReadFailed_and_WriteFailed_not_Missing()
    {
        using TemporarySettingsDirectory fixture = new();
        Directory.CreateDirectory(fixture.Root);
        string parentFile = Path.Combine(fixture.Root, "PRIVATE-T03-NOT-A-DIRECTORY");
        byte[] sentinel = "PRIVATE-T03-PARENT-BYTES"u8.ToArray();
        File.WriteAllBytes(parentFile, sentinel);
        SettingsFileStore store = new(Path.Combine(parentFile, "child", "setting.txt"));

        SettingsLoadResult load = await store.LoadAsync(TestContext.Current.CancellationToken);
        SettingsSaveResult save = await store.SaveAsync(new ApplicationSettings(), TestContext.Current.CancellationToken);

        AssertLoadFailed(load, SettingsLoadStatus.ReadFailed);
        Assert.Equal(SettingsSaveStatus.WriteFailed, save.Status);
        Assert.False(save.IsSuccess);
        Assert.Equal(sentinel, File.ReadAllBytes(parentFile));
        Assert.Equal(parentFile, Assert.Single(Directory.GetFileSystemEntries(fixture.Root)));
        Assert.DoesNotContain(parentFile, save.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Directory_at_file_path_is_not_deleted_or_treated_as_a_missing_file()
    {
        using TemporarySettingsDirectory fixture = new();
        Directory.CreateDirectory(fixture.FilePath);
        string sentinelPath = Path.Combine(fixture.FilePath, "sentinel.txt");
        byte[] sentinel = "PRIVATE-T03-DIRECTORY-CONTENT"u8.ToArray();
        File.WriteAllBytes(sentinelPath, sentinel);
        SettingsFileStore store = new(fixture.FilePath);

        AssertLoadFailed(await store.LoadAsync(TestContext.Current.CancellationToken), SettingsLoadStatus.ReadFailed);
        SettingsSaveResult result = await store.SaveAsync(new ApplicationSettings(), TestContext.Current.CancellationToken);

        Assert.Equal(SettingsSaveStatus.WriteFailed, result.Status);
        Assert.False(result.IsSuccess);
        Assert.True(Directory.Exists(fixture.FilePath));
        Assert.Equal(sentinel, File.ReadAllBytes(sentinelPath));
        Assert.Equal(fixture.FilePath, Assert.Single(Directory.GetFileSystemEntries(fixture.SettingsDirectory)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_throws_consistently_without_creating_or_changing_files(bool fileExists)
    {
        using TemporarySettingsDirectory fixture = new();
        byte[]? original = fileExists ? fixture.WriteJson("""{"schemaVersion":1,"maxConcurrency":2}""") : null;
        SettingsFileStore store = new(fixture.FilePath);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync(new ApplicationSettings(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(null!, cancellation.Token));

        if (original is not null)
        {
            fixture.AssertUnchanged(original);
            fixture.AssertOnlySettingsFile();
        }
        else
        {
            Assert.False(Directory.Exists(fixture.Root));
        }
    }

    [Fact]
    public async Task Save_uses_the_supplied_record_and_last_successful_save_wins_across_store_instances()
    {
        using TemporarySettingsDirectory fixture = new();
        SettingsFileStore firstStore = new(fixture.FilePath);
        SettingsFileStore secondStore = new(fixture.FilePath);
        ApplicationSettings draft = CreateSettings(fixture);
        ApplicationSettings savedSnapshot = draft;

        Task<SettingsSaveResult> saving = firstStore.SaveAsync(draft, TestContext.Current.CancellationToken);
        draft = draft with { PreferredModelId = "next-model", MaxConcurrency = 1, Definition = null };
        AssertSaved(await saving);
        SettingsLoadResult first = await secondStore.LoadAsync(TestContext.Current.CancellationToken);
        AssertSettingsEqual(savedSnapshot, Assert.IsType<ApplicationSettings>(first.Settings));

        AssertSaved(await secondStore.SaveAsync(draft, TestContext.Current.CancellationToken));
        byte[] lastSuccessfulBytes = File.ReadAllBytes(fixture.FilePath);
        SettingsSaveResult rejected = await firstStore.SaveAsync(
            savedSnapshot with { MaxConcurrency = 17 },
            TestContext.Current.CancellationToken);
        Assert.Equal(SettingsSaveStatus.InvalidSettings, rejected.Status);
        fixture.AssertUnchanged(lastSuccessfulBytes);

        SettingsLoadResult final = await firstStore.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(draft, final.Settings);
        fixture.AssertOnlySettingsFile();
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("""{"schemaVersion":2,"future":true}""")]
    public async Task Rejected_file_is_only_replaced_by_a_subsequent_explicit_valid_save(string originalJson)
    {
        using TemporarySettingsDirectory fixture = new();
        byte[] original = fixture.WriteJson(originalJson);
        SettingsFileStore store = new(fixture.FilePath);
        SettingsLoadResult rejected = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.False(rejected.IsSuccess);
        fixture.AssertUnchanged(original);

        AssertSaved(await store.SaveAsync(new ApplicationSettings(), TestContext.Current.CancellationToken));
        SettingsLoadResult loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.Equal(new ApplicationSettings(), loaded.Settings);
        fixture.AssertOnlySettingsFile();
    }

    [Fact]
    public void Results_expose_only_status_settings_and_success_and_never_print_content()
    {
        using TemporarySettingsDirectory fixture = new();
        ApplicationSettings settings = CreateSettings(fixture);
        Assert.Equal(
            ["IsSuccess", "Settings", "Status"],
            typeof(SettingsLoadResult).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["IsSuccess", "Status"],
            typeof(SettingsSaveResult).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));

        foreach (SettingsLoadStatus status in Enum.GetValues<SettingsLoadStatus>())
        {
            SettingsLoadResult result = new(status, settings);
            Assert.Equal(status is SettingsLoadStatus.Loaded or SettingsLoadStatus.Missing, result.IsSuccess);
            Assert.DoesNotContain(ModelCanary, result.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(TextCanary, result.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(PromptCanary, result.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Root, result.ToString(), StringComparison.Ordinal);
        }

        foreach (SettingsSaveStatus status in Enum.GetValues<SettingsSaveStatus>())
        {
            SettingsSaveResult result = new(status);
            Assert.Equal(status == SettingsSaveStatus.Saved, result.IsSuccess);
            Assert.DoesNotContain("PRIVATE-T03", result.ToString(), StringComparison.Ordinal);
        }

        Assert.False(Directory.Exists(fixture.Root));
    }

    private static async Task AssertRejectedJsonAsync(
        TemporarySettingsDirectory fixture,
        string json,
        SettingsLoadStatus expectedStatus = SettingsLoadStatus.JsonInvalid)
    {
        byte[] original = fixture.WriteJson(json);
        SettingsLoadResult result = await new SettingsFileStore(fixture.FilePath)
            .LoadAsync(TestContext.Current.CancellationToken);
        AssertLoadFailed(result, expectedStatus);
        Assert.DoesNotContain("PRIVATE-T03", result.ToString(), StringComparison.Ordinal);
        fixture.AssertUnchanged(original);
        fixture.AssertOnlySettingsFile();
    }

    private static void AssertLoadFailed(SettingsLoadResult result, SettingsLoadStatus status)
    {
        Assert.Equal(status, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Settings);
    }

    private static void AssertSaved(SettingsSaveResult result)
    {
        Assert.Equal(SettingsSaveStatus.Saved, result.Status);
        Assert.True(result.IsSuccess);
    }

    private static JsonObject CreateJson(TemporarySettingsDirectory fixture) =>
        JsonSerializer.SerializeToNode(CreateSettings(fixture), FixtureJsonOptions)!.AsObject();

    private static JsonNode NodeAtPath(JsonNode root, string path)
    {
        JsonNode node = root;
        foreach (string segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node is JsonArray array
                ? array[int.Parse(segment, CultureInfo.InvariantCulture)]!
                : node[segment]!;
        }

        return node;
    }

    private static (JsonObject Parent, string Name) PropertyAtPath(JsonObject root, string path)
    {
        int separator = path.LastIndexOf('.');
        return separator < 0
            ? (root, path)
            : (NodeAtPath(root, path[..separator]).AsObject(), path[(separator + 1)..]);
    }

    private static ApplicationSettings InvalidSettings(ApplicationSettings settings, string caseName) => caseName switch
    {
        "null-settings" => null!,
        "schema" => settings with { SchemaVersion = 2 },
        "concurrency-low" => settings with { MaxConcurrency = 0 },
        "concurrency-high" => settings with { MaxConcurrency = 17 },
        "model-empty" => settings with { PreferredModelId = string.Empty },
        "model-space" => settings with { PreferredModelId = " model " },
        "model-control" => settings with { PreferredModelId = "model\u007F" },
        "model-long" => settings with { PreferredModelId = new string('m', 257) },
        "output-empty" => settings with { OutputDirectoryOverride = string.Empty },
        "output-space" => settings with { OutputDirectoryOverride = "  " },
        "output-relative" => settings with { OutputDirectoryOverride = "relative/output" },
        "output-invalid" => settings with { OutputDirectoryOverride = settings.OutputDirectoryOverride + "\0" },
        "definition-null-id" => ChangeDefinition(settings, definition => definition with { Id = null! }),
        "base-points" => ChangeDefinition(settings, definition => definition with { BasePoints = -1m }),
        "allocation-total" => ChangeDefinition(settings, definition => definition with { BasePoints = 55.126m }),
        "similarity-weight" => ChangeDefinition(settings, definition => definition with { SimilarityPenaltyWeight = 2m }),
        "rows" => ChangeDefinition(settings, definition => definition with { FirstDataRow = 1 }),
        "rounding" => ChangeDefinition(settings, definition => definition with { RoundingDigits = 7 }),
        "question-points" => ChangeQuestion(settings, question => question with { Points = -1m }),
        "question-null-text" => ChangeQuestion(settings, question => question with { QuestionText = null! }),
        "invalid-enum" => ChangeEvaluator(settings, evaluator => evaluator with { Type = (EvaluatorType)99 }),
        "disabled-invalid-enum" => ChangeEvaluator(settings, evaluator => evaluator with { Type = (EvaluatorType)99 }, 1),
        "evaluator-weight" => ChangeEvaluator(settings, evaluator => evaluator with { Weight = 0m }),
        "evaluator-range" => ChangeEvaluator(settings, evaluator => evaluator with { Range = new ScoreRange(2m, 1m) }),
        "criterion-weight" => ChangeCriterion(settings, criterion => criterion with { Weight = -1m }),
        "criterion-range" => ChangeCriterion(settings, criterion => criterion with { Range = new ScoreRange(0m, 0m) }),
        "disabled-criterion-range" => ChangeCriterion(settings, criterion => criterion with { Range = new ScoreRange(0m, 0m) }, 1),
        "criterion-null-description" => ChangeCriterion(settings, criterion => criterion with { Description = null! }),
        "prompt-null" => ChangeEvaluator(settings, evaluator => evaluator with { CustomPromptTemplate = null }),
        "prompt-unknown" => ChangeEvaluator(settings, evaluator => evaluator with { CustomPromptTemplate = "{回答} {未知}" }),
        "prompt-brace" => ChangeEvaluator(settings, evaluator => evaluator with { CustomPromptTemplate = "{回答} {評価項目} }" }),
        "special-prompt-null" => ChangeSpecial(settings, special => special with { PromptTemplate = null! }),
        "special-prompt-invalid" => ChangeSpecial(settings, special => special with { PromptTemplate = "{評価項目}" }),
        "disabled-special-prompt-invalid" => ChangeSpecial(settings, special => special with { PromptTemplate = "{未知}" }, 1),
        "default-questions" => ChangeDefinition(settings, definition => definition with { Questions = default }),
        "default-evaluators" => ChangeQuestion(settings, question => question with { Evaluators = default }),
        "default-criteria" => ChangeEvaluator(settings, evaluator => evaluator with { Criteria = default }),
        "default-specials" => ChangeQuestion(settings, question => question with { SpecialEvaluations = default }),
        "default-question-columns" => ChangeQuestion(settings, question => question with { SupportingSourceColumns = default }),
        "default-special-columns" => ChangeSpecial(settings, special => special with { SupportingSourceColumns = default }),
        "null-question" => ChangeDefinition(settings, definition => definition with { Questions = [null!] }),
        "null-evaluator" => ChangeQuestion(settings, question => question with { Evaluators = [null!] }),
        "null-criterion" => ChangeEvaluator(settings, evaluator => evaluator with { Criteria = [null!] }),
        "null-special" => ChangeQuestion(settings, question => question with { SpecialEvaluations = [null!] }),
        "null-question-column" => ChangeQuestion(settings, question => question with { SupportingSourceColumns = [null!] }),
        "null-special-column" => ChangeSpecial(settings, special => special with { SupportingSourceColumns = [null!] }),
        "unpaired-surrogate" => ChangeQuestion(settings, question => question with { QuestionText = "PRIVATE-T03-\uD800" }),
        _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
    };

    private static ApplicationSettings ChangeDefinition(
        ApplicationSettings settings,
        Func<QuantificationDefinition, QuantificationDefinition> change) =>
        settings with { Definition = change(settings.Definition!) };

    private static ApplicationSettings ChangeQuestion(
        ApplicationSettings settings,
        Func<QuestionDefinition, QuestionDefinition> change) =>
        ChangeDefinition(settings, definition => definition with
        {
            Questions = definition.Questions.SetItem(0, change(definition.Questions[0])),
        });

    private static ApplicationSettings ChangeEvaluator(
        ApplicationSettings settings,
        Func<EvaluatorDefinition, EvaluatorDefinition> change,
        int index = 0) =>
        ChangeQuestion(settings, question => question with
        {
            Evaluators = question.Evaluators.SetItem(index, change(question.Evaluators[index])),
        });

    private static ApplicationSettings ChangeCriterion(
        ApplicationSettings settings,
        Func<CriterionDefinition, CriterionDefinition> change,
        int index = 0) =>
        ChangeEvaluator(settings, evaluator => evaluator with
        {
            Criteria = evaluator.Criteria.SetItem(index, change(evaluator.Criteria[index])),
        });

    private static ApplicationSettings ChangeSpecial(
        ApplicationSettings settings,
        Func<SpecialEvaluationDefinition, SpecialEvaluationDefinition> change,
        int index = 0) =>
        ChangeQuestion(settings, question => question with
        {
            SpecialEvaluations = question.SpecialEvaluations.SetItem(index, change(question.SpecialEvaluations[index])),
        });

    private static ApplicationSettings CreateSettings(TemporarySettingsDirectory fixture)
    {
        EvaluatorDefinition custom = U01TestSupport.Evaluator(
            "E-Z", "C-Z",
            evaluatorRange: new ScoreRange(-1.125m, 8.875m),
            criterionRange: new ScoreRange(-0.125m, 4.625m),
            customPrompt: PromptCanary,
            evaluatorWeight: 1.375m,
            criterionWeight: 0.875m);
        custom = custom with
        {
            DisplayName = TextCanary,
            Criteria =
            [
                custom.Criteria[0] with { DisplayName = TextCanary, Description = TextCanary },
                custom.Criteria[0] with { Id = "C-A", Weight = 0.0625m, Range = null, Enabled = false },
            ],
        };
        EvaluatorDefinition knowledge = U01TestSupport.Evaluator(
            "E-A", "C-KNOWLEDGE", enabled: false,
            evaluatorRange: new ScoreRange(0m, 1m), evaluatorWeight: 0.125m) with
        {
            Type = EvaluatorType.KnowledgeCoverage,
            BuiltInTemplateVersion = BuiltInPromptTemplates.KnowledgeTemplateVersion,
            CustomPromptTemplate = null,
        };
        SpecialEvaluationDefinition special = new()
        {
            Id = "S-Z",
            DisplayName = TextCanary,
            PrimarySourceColumn = "C",
            SupportingSourceColumns = ["D", "B"],
            PromptTemplate = PromptCanary,
        };
        QuestionDefinition first = U01TestSupport.Question("Q-Z", "B", ["D", "C"], true, custom, knowledge) with
        {
            DisplayName = TextCanary,
            QuestionText = TextCanary,
            Points = 34.5m,
            SpecialEvaluations = [special, special with { Id = "S-A", SupportingSourceColumns = [], Enabled = false }],
        };
        QuestionDefinition disabled = U01TestSupport.Question(
            "Q-A", "E", [], false, U01TestSupport.Evaluator("E-DISABLED", "C-DISABLED")) with
        {
            Points = 2.375m,
        };
        QuantificationDefinition definition = U01TestSupport.Definition(3, 23, first, disabled) with
        {
            Id = "DEF-T03",
            Name = TextCanary,
            Revision = "revision-3",
            SourceSheet = "合成 回答",
            HeaderRow = 2,
            BasePoints = 55.125m,
            SpecialPoints = 10.375m,
            SimilarityPenaltyWeight = 0.1234567890123456789012345678m,
            RoundingDigits = 3,
        };
        return new ApplicationSettings
        {
            PreferredModelId = ModelCanary,
            MaxConcurrency = 3,
            OutputDirectoryOverride = fixture.OutputDirectory,
            Definition = definition,
        };
    }

    private static void AssertSettingsEqual(ApplicationSettings expected, ApplicationSettings actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.PreferredModelId, actual.PreferredModelId);
        Assert.Equal(expected.MaxConcurrency, actual.MaxConcurrency);
        Assert.Equal(expected.OutputDirectoryOverride, actual.OutputDirectoryOverride);
        QuantificationDefinition expectedDefinition = Assert.IsType<QuantificationDefinition>(expected.Definition);
        QuantificationDefinition actualDefinition = Assert.IsType<QuantificationDefinition>(actual.Definition);
        Assert.Equal(expectedDefinition.Id, actualDefinition.Id);
        Assert.Equal(expectedDefinition.Name, actualDefinition.Name);
        Assert.Equal(expectedDefinition.Revision, actualDefinition.Revision);
        Assert.Equal(expectedDefinition.SourceSheet, actualDefinition.SourceSheet);
        Assert.Equal(expectedDefinition.HeaderRow, actualDefinition.HeaderRow);
        Assert.Equal(expectedDefinition.FirstDataRow, actualDefinition.FirstDataRow);
        Assert.Equal(expectedDefinition.LastDataRow, actualDefinition.LastDataRow);
        Assert.Equal(expectedDefinition.BasePoints, actualDefinition.BasePoints);
        Assert.Equal(expectedDefinition.SpecialPoints, actualDefinition.SpecialPoints);
        Assert.Equal(expectedDefinition.SimilarityPenaltyWeight, actualDefinition.SimilarityPenaltyWeight);
        Assert.Equal(expectedDefinition.RoundingDigits, actualDefinition.RoundingDigits);
        Assert.Equal(expectedDefinition.Questions.Length, actualDefinition.Questions.Length);
        for (int questionIndex = 0; questionIndex < expectedDefinition.Questions.Length; questionIndex++)
        {
            QuestionDefinition expectedQuestion = expectedDefinition.Questions[questionIndex];
            QuestionDefinition actualQuestion = actualDefinition.Questions[questionIndex];
            Assert.Equal(expectedQuestion.Id, actualQuestion.Id);
            Assert.Equal(expectedQuestion.DisplayName, actualQuestion.DisplayName);
            Assert.Equal(expectedQuestion.QuestionText, actualQuestion.QuestionText);
            Assert.Equal(expectedQuestion.PrimarySourceColumn, actualQuestion.PrimarySourceColumn);
            Assert.Equal(expectedQuestion.SupportingSourceColumns.ToArray(), actualQuestion.SupportingSourceColumns.ToArray());
            Assert.Equal(expectedQuestion.Points, actualQuestion.Points);
            Assert.Equal(expectedQuestion.Enabled, actualQuestion.Enabled);
            Assert.Equal(expectedQuestion.Evaluators.Length, actualQuestion.Evaluators.Length);
            for (int evaluatorIndex = 0; evaluatorIndex < expectedQuestion.Evaluators.Length; evaluatorIndex++)
            {
                EvaluatorDefinition expectedEvaluator = expectedQuestion.Evaluators[evaluatorIndex];
                EvaluatorDefinition actualEvaluator = actualQuestion.Evaluators[evaluatorIndex];
                Assert.Equal(expectedEvaluator.Id, actualEvaluator.Id);
                Assert.Equal(expectedEvaluator.DisplayName, actualEvaluator.DisplayName);
                Assert.Equal(expectedEvaluator.Type, actualEvaluator.Type);
                Assert.Equal(expectedEvaluator.Weight, actualEvaluator.Weight);
                Assert.Equal(expectedEvaluator.Range, actualEvaluator.Range);
                Assert.Equal(expectedEvaluator.BuiltInTemplateVersion, actualEvaluator.BuiltInTemplateVersion);
                Assert.Equal(expectedEvaluator.CustomPromptTemplate, actualEvaluator.CustomPromptTemplate);
                Assert.Equal(expectedEvaluator.Enabled, actualEvaluator.Enabled);
                Assert.Equal(expectedEvaluator.Criteria.ToArray(), actualEvaluator.Criteria.ToArray());
            }

            Assert.Equal(expectedQuestion.SpecialEvaluations.Length, actualQuestion.SpecialEvaluations.Length);
            for (int specialIndex = 0; specialIndex < expectedQuestion.SpecialEvaluations.Length; specialIndex++)
            {
                SpecialEvaluationDefinition expectedSpecial = expectedQuestion.SpecialEvaluations[specialIndex];
                SpecialEvaluationDefinition actualSpecial = actualQuestion.SpecialEvaluations[specialIndex];
                Assert.Equal(expectedSpecial.Id, actualSpecial.Id);
                Assert.Equal(expectedSpecial.DisplayName, actualSpecial.DisplayName);
                Assert.Equal(expectedSpecial.PrimarySourceColumn, actualSpecial.PrimarySourceColumn);
                Assert.Equal(expectedSpecial.SupportingSourceColumns.ToArray(), actualSpecial.SupportingSourceColumns.ToArray());
                Assert.Equal(expectedSpecial.PromptTemplate, actualSpecial.PromptTemplate);
                Assert.Equal(expectedSpecial.Enabled, actualSpecial.Enabled);
            }
        }
    }

    private sealed class TemporarySettingsDirectory : IDisposable
    {
        // No constructor IO: first-load tests can assert the entire directory remains absent.
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"StudyReportEvaluator-T03-{Guid.NewGuid():N}");

        public string SettingsDirectory => Path.Combine(Root, "合成 設定");

        public string FilePath => Path.Combine(SettingsDirectory, "setting.txt");

        public string OutputDirectory => Path.Combine(Root, "PRIVATE-T03-OUTPUT 未作成");

        public byte[] WriteJson(string json, bool includeBom = false)
        {
            byte[] bytes = includeBom
                ? [.. "\uFEFF"u8, .. StrictUtf8.GetBytes(json)]
                : StrictUtf8.GetBytes(json);
            WriteBytes(bytes);
            return bytes;
        }

        public void WriteBytes(byte[] bytes)
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllBytes(FilePath, bytes);
        }

        public void AssertUnchanged(byte[] expected)
        {
            byte[] actual = File.ReadAllBytes(FilePath);
            Assert.Equal(expected, actual);
            Assert.Equal(SHA256.HashData(expected), SHA256.HashData(actual));
        }

        public void AssertOnlySettingsFile() =>
            Assert.Equal(FilePath, Assert.Single(Directory.GetFileSystemEntries(SettingsDirectory)));

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}