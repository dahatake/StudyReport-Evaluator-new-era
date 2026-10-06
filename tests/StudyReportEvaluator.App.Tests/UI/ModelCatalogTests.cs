using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Text;
using System.Text.Json;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-015 (AC-016)
public sealed class ModelCatalogTests
{
    private static readonly TimeSpan TestWait = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static bool IsWindows => OperatingSystem.IsWindows();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(512)]
    [InlineData(4096)]
    public async Task Cache_round_trip_preserves_order_ids_and_nullable_limits_at_count_boundaries(int count)
    {
        using CatalogHarness harness = new();
        ImmutableArray<CachedCopilotModel> catalog = [.. Enumerable.Range(0, count).Select(index =>
            new CachedCopilotModel(index == 0 ? new string('m', 256) : $"model-{index}",
                index % 2 == 0 ? null : int.MaxValue, index % 2 == 0 ? 1 : null))];
        ApplicationSettings settings = new() { PreferredModelId = "not-yet-confirmed", CachedModels = catalog };

        await harness.SeedAsync(settings);
        ApplicationSettings restored = await harness.ReadAsync();

        AssertCatalog(catalog, restored.CachedModels);
        Assert.Equal(settings.PreferredModelId, restored.PreferredModelId);
        Assert.Equal(1, restored.SchemaVersion);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(harness.Path));
        Assert.Equal(count, json.RootElement.GetProperty("cachedModels").GetArrayLength());
        if (count > 0)
        {
            JsonElement first = json.RootElement.GetProperty("cachedModels")[0];
            Assert.Equal(new[] { "id", "maximumContextWindowTokens", "maximumPromptTokens" },
                first.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal(JsonValueKind.Null, first.GetProperty("maximumPromptTokens").ValueKind);
            Assert.Equal(1, first.GetProperty("maximumContextWindowTokens").GetInt32());
        }

        Assert.Equal(0, harness.Authentication.CallCount);
        harness.AssertNoLoginOrRun();
    }

    [Theory]
    [InlineData("missing-file")]
    [InlineData("old-settings")]
    [InlineData("null-cache")]
    public async Task Old_or_missing_settings_remain_passive_and_null_cache_is_omitted_on_save(string kind)
    {
        using CatalogHarness harness = new();
        if (kind != "missing-file")
        {
            harness.WriteJson(kind == "old-settings" ? """{"schemaVersion":1,"maxConcurrency":2}"""
                : """{"schemaVersion":1,"maxConcurrency":2,"cachedModels":null}""");
        }

        byte[]? original = File.Exists(harness.Path) ? File.ReadAllBytes(harness.Path) : null;
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Null(harness.Execution.CachedModels);
        Assert.Empty(harness.Execution.AvailableModelIds);
        Assert.Equal(ExecutionAuthenticationState.NotChecked, harness.Execution.AuthenticationState);
        Assert.Equal(0, harness.Authentication.CallCount);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Null(harness.Settings.LastSaveTask);
        if (original is null) Assert.False(Directory.Exists(harness.Root));
        else Assert.Equal(original, File.ReadAllBytes(harness.Path));

        await harness.Settings.SaveAsync(TestContext.Current.CancellationToken);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(harness.Path));
        Assert.False(json.RootElement.TryGetProperty("cachedModels", out _));
        harness.AssertNoLoginOrRun();
    }

    [Theory]
    [InlineData("default")]
    [InlineData("null-item")]
    [InlineData("null-id")]
    [InlineData("empty-id")]
    [InlineData("blank-id")]
    [InlineData("leading-space")]
    [InlineData("trailing-space")]
    [InlineData("control-id")]
    [InlineData("long-id")]
    [InlineData("surrogate-id")]
    [InlineData("duplicate")]
    [InlineData("prompt-zero")]
    [InlineData("prompt-negative")]
    [InlineData("context-zero")]
    [InlineData("context-negative")]
    [InlineData("too-many")]
    public async Task Invalid_cache_save_rejects_before_io_and_preserves_existing_bytes_and_mtime(string kind)
    {
        using CatalogHarness existing = new();
        using CatalogHarness absent = new();
        await existing.SeedAsync(new ApplicationSettings { CachedModels = Catalog("model-a", "auto") });
        var original = existing.Stamp();
        ImmutableArray<CachedCopilotModel> invalid = kind switch
        {
            "default" => default,
            "null-item" => [null!],
            "null-id" => [new(null!, 1, 1)],
            "empty-id" => [new("", 1, 1)],
            "blank-id" => [new(" \t ", 1, 1)],
            "leading-space" => [new(" model-a", 1, 1)],
            "trailing-space" => [new("model-a ", 1, 1)],
            "control-id" => [new("model\u007F", 1, 1)],
            "long-id" => [new(new string('m', 257), 1, 1)],
            "surrogate-id" => [new("model-\uD800", 1, 1)],
            "duplicate" => [new("model-a", 1, 1), new("model-a", 2, 2)],
            "prompt-zero" => [new("model-a", 0, 1)],
            "prompt-negative" => [new("model-a", -1, 1)],
            "context-zero" => [new("model-a", 1, 0)],
            "context-negative" => [new("model-a", 1, -1)],
            "too-many" => [.. Enumerable.Range(0, 4097).Select(index => new CachedCopilotModel($"model-{index}", 1, 1))],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        foreach (CatalogHarness harness in new[] { existing, absent })
        {
            SettingsSaveResult result = await harness.Store.SaveAsync(
                new ApplicationSettings { CachedModels = invalid }, TestContext.Current.CancellationToken);
            Assert.Equal(SettingsSaveStatus.InvalidSettings, result.Status);
            Assert.False(result.IsSuccess);
        }

        existing.AssertUnchanged(original);
        Assert.False(Directory.Exists(absent.Root));
        Assert.Equal(existing.Path, Assert.Single(Directory.GetFiles(existing.Root)));
        if (kind is not ("default" or "surrogate-id"))
        {
            // Default arrays have no JSON representation; the default serializer replaces
            // lone surrogates. All other malformed values must also fail the read boundary.
            absent.WriteJson(JsonSerializer.Serialize(new ApplicationSettings { CachedModels = invalid }, JsonOptions));
            var invalidFile = absent.Stamp();
            SettingsLoadResult loaded = await absent.Store.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(SettingsLoadStatus.JsonInvalid, loaded.Status);
            Assert.Null(loaded.Settings);
            absent.AssertUnchanged(invalidFile);
        }
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("{}")]
    [InlineData("\"models\"")]
    [InlineData("[{\"id\":null,\"maximumPromptTokens\":1,\"maximumContextWindowTokens\":1}]")]
    [InlineData("[{\"id\":\"\",\"maximumPromptTokens\":1,\"maximumContextWindowTokens\":1}]")]
    [InlineData("[{\"id\":\"model-a\"}]")]
    [InlineData("[{\"id\":\"model-\\uD800\",\"maximumPromptTokens\":1,\"maximumContextWindowTokens\":1}]")]
    [InlineData("[{\"id\":\"model-a\",\"maximumPromptTokens\":0,\"maximumContextWindowTokens\":1}]")]
    [InlineData("[{\"id\":\"model-a\",\"maximumPromptTokens\":1,\"maximumContextWindowTokens\":-1}]")]
    [InlineData("[{\"id\":\"model-a\",\"maximumPromptTokens\":\"1\",\"maximumContextWindowTokens\":1}]")]
    [InlineData("[{\"id\":\"model-a\",\"maximumPromptTokens\":2147483648,\"maximumContextWindowTokens\":1}]")]
    [InlineData("[{\"id\":\"model-a\",\"maximumPromptTokens\":1,\"maximumContextWindowTokens\":1,\"unknown\":true}]")]
    [InlineData("[{\"id\":\"model-a\",\"id\":\"model-b\",\"maximumPromptTokens\":1,\"maximumContextWindowTokens\":1}]")]
    [InlineData("[{\"id\":\"model-a\",\"maximumPromptTokens\":1,\"maximumContextWindowTokens\":1},{\"id\":\"model-a\",\"maximumPromptTokens\":2,\"maximumContextWindowTokens\":2}]")]
    public async Task Invalid_cache_json_is_rejected_without_partial_restore_or_repair(string catalogJson)
    {
        using CatalogHarness harness = new();
        harness.WriteJson("{\"schemaVersion\":1,\"maxConcurrency\":3,\"cachedModels\":" + catalogJson + "}");
        var original = harness.Stamp();

        SettingsLoadResult loaded = await harness.Store.LoadAsync(TestContext.Current.CancellationToken);
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SettingsLoadStatus.JsonInvalid, loaded.Status);
        Assert.Null(loaded.Settings);
        Assert.Null(harness.Execution.CachedModels);
        Assert.Equal(8, harness.Execution.MaxConcurrency);
        Assert.Equal(0, harness.Authentication.CallCount);
        harness.AssertUnchanged(original);
        harness.AssertNoLoginOrRun();
    }

    [Fact]
    public async Task Case_distinct_model_ids_are_not_duplicate_cache_entries()
    {
        using CatalogHarness harness = new();
        ImmutableArray<CachedCopilotModel> catalog = Catalog("model-a", "MODEL-A", "auto", "Auto");
        await harness.SeedAsync(new ApplicationSettings { CachedModels = catalog });
        AssertCatalog(catalog, (await harness.ReadAsync()).CachedModels);
    }

    [Fact]
    public async Task Applying_cache_is_display_only_even_with_known_limits_and_never_resets_the_collection()
    {
        using CatalogHarness harness = new();
        var collection = harness.Execution.AvailableModelIds;
        List<NotifyCollectionChangedAction> changes = [];
        ((INotifyCollectionChanged)collection).CollectionChanged += (_, args) => changes.Add(args.Action);
        ImmutableArray<CachedCopilotModel> cache = Catalog("model-a", "auto");

        harness.Execution.ApplySettings(new ApplicationSettings { PreferredModelId = "model-a", CachedModels = cache });

        Assert.Equal(new[] { "model-a", "auto" }, collection);
        AssertCatalog(cache, harness.Execution.CachedModels);
        Assert.Equal(ExecutionAuthenticationState.NotChecked, harness.Execution.AuthenticationState);
        AssertUnauthorized(harness);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, changes);
        Assert.All(changes, action => Assert.Equal(NotifyCollectionChangedAction.Add, action));
        Assert.Equal(2, changes.Count);
        changes.Clear();
        harness.Execution.ApplySettings(new ApplicationSettings { PreferredModelId = "model-a", CachedModels = cache });
        Assert.Same(collection, harness.Execution.AvailableModelIds);
        Assert.Empty(changes);
        await harness.Execution.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, harness.Authentication.CallCount);
        Assert.False(Directory.Exists(harness.Root));
        harness.AssertNoLoginOrRun();
    }

    [Theory]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("reorder")]
    [InlineData("limits")]
    [InlineData("combined")]
    [InlineData("empty")]
    public async Task Startup_restores_display_before_async_differential_refresh_and_saves_fresh_catalog(string change)
    {
        using CatalogHarness harness = new();
        ImmutableArray<CachedCopilotModel> old = Catalog("model-a", "model-b", "auto");
        ImmutableArray<CachedCopilotModel> next = change switch
        {
            "add" => Catalog("model-a", "model-new", "model-b", "auto"),
            "delete" => Catalog("model-a", "auto"),
            "reorder" => Catalog("model-b", "model-a", "auto"),
            "limits" => old.SetItem(0, new("model-a", null, 256_000)),
            "combined" => Catalog("auto", "model-new", "model-a").SetItem(2, new("model-a", 32_000, 256_000)),
            "empty" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        await harness.SeedAsync(new ApplicationSettings { PreferredModelId = "model-a", CachedModels = old });
        var original = harness.Stamp();
        TaskCompletionSource<ExecutionAuthenticationSnapshot> pending = NewPending();
        harness.Authentication.Handler = _ => pending.Task;
        var collection = harness.Execution.AvailableModelIds;
        Task startup = harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        List<NotifyCollectionChangedAction> changes = [];
        try
        {
            await harness.Authentication.Entered.Task.WaitAsync(TestWait, TestContext.Current.CancellationToken);
            Assert.False(startup.IsCompleted);
            Assert.True(harness.Settings.IsInitialized);
            Assert.Equal(old.Select(model => model.Id), collection);
            AssertCatalog(old, harness.Execution.CachedModels);
            AssertUnauthorized(harness);
            harness.AssertUnchanged(original);
            ((INotifyCollectionChanged)collection).CollectionChanged += (_, args) =>
            {
                changes.Add(args.Action);
                harness.Execution.SelectedModelId = null; // Two-way binding feedback is not an edit.
            };
            await harness.Execution.StartAsync(TestContext.Current.CancellationToken);
            await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, harness.Authentication.CallCount);
        }
        finally
        {
            pending.TrySetResult(Available(next));
            await startup.WaitAsync(TestWait, TestContext.Current.CancellationToken);
        }

        Assert.Same(collection, harness.Execution.AvailableModelIds);
        Assert.Equal(next.Select(model => model.Id), collection);
        AssertCatalog(next, harness.Execution.CachedModels);
        AssertCatalog(next, (await harness.ReadAsync()).CachedModels);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, changes);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Replace, changes);
        if (change == "limits") Assert.Empty(changes);
        else Assert.NotEmpty(changes);
        if (change is "add" or "combined") Assert.Contains(NotifyCollectionChangedAction.Add, changes);
        if (change is "delete" or "combined" or "empty") Assert.Contains(NotifyCollectionChangedAction.Remove, changes);
        if (change is "reorder" or "combined") Assert.Contains(NotifyCollectionChangedAction.Move, changes);
        Assert.Equal("model-a", harness.Execution.PreferredModelId);
        Assert.Equal(change == "empty" ? null : "model-a", harness.Execution.SelectedModelId);
        Assert.Equal(change != "empty", harness.Execution.CanStart);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Null(harness.Settings.LastSaveTask);
        harness.AssertNoLoginOrRun();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unchanged_cache_does_not_rewrite_bytes_or_mtime_or_emit_collection_changes(bool empty)
    {
        using CatalogHarness harness = new();
        ImmutableArray<CachedCopilotModel> catalog = empty ? [] : Catalog("model-b", "model-a", "auto");
        await harness.SeedAsync(new ApplicationSettings { PreferredModelId = "model-a", CachedModels = catalog });
        var original = harness.Stamp();
        TaskCompletionSource<ExecutionAuthenticationSnapshot> pending = NewPending();
        harness.Authentication.Handler = _ => pending.Task;
        Task startup = harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        List<NotifyCollectionChangedAction> changes = [];
        int cacheNotifications = 0;
        try
        {
            await harness.Authentication.Entered.Task.WaitAsync(TestWait, TestContext.Current.CancellationToken);
            ((INotifyCollectionChanged)harness.Execution.AvailableModelIds).CollectionChanged += (_, args) => changes.Add(args.Action);
            harness.Execution.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ExecutionViewModel.CachedModels)) cacheNotifications++;
            };
        }
        finally
        {
            pending.TrySetResult(Available(catalog));
            await startup.WaitAsync(TestWait, TestContext.Current.CancellationToken);
        }

        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, harness.Authentication.CallCount);
        Assert.Empty(changes);
        Assert.Equal(0, cacheNotifications);
        harness.AssertUnchanged(original);
        Assert.Contains("変更はありません", harness.Settings.ModelCatalogPersistenceText, StringComparison.Ordinal);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Null(harness.Settings.LastSaveTask);
        harness.AssertNoLoginOrRun();
    }

    [Theory]
    [InlineData(ExecutionAuthenticationState.AuthRequired)]
    [InlineData(ExecutionAuthenticationState.CliUnavailable)]
    [InlineData(ExecutionAuthenticationState.RuntimeFailed)]
    [InlineData(ExecutionAuthenticationState.Cancelled)]
    public async Task Failed_startup_refresh_keeps_visible_cache_but_grants_no_authentication_or_run(ExecutionAuthenticationState state)
    {
        using CatalogHarness harness = new();
        ImmutableArray<CachedCopilotModel> old = Catalog("model-a", "auto");
        await harness.SeedAsync(new ApplicationSettings { PreferredModelId = "model-a", CachedModels = old });
        var original = harness.Stamp();
        // Even a failed response carrying models must not replace the last successful catalog.
        harness.Authentication.Handler = _ => Task.FromResult(new ExecutionAuthenticationSnapshot(state,
            [U04TestSupport.Model("untrusted")], runtimeIdentity: null));

        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(state, harness.Execution.AuthenticationState);
        Assert.Equal(old.Select(model => model.Id), harness.Execution.AvailableModelIds);
        AssertCatalog(old, harness.Execution.CachedModels);
        AssertUnauthorized(harness);
        await harness.Execution.StartAsync(TestContext.Current.CancellationToken);
        harness.AssertUnchanged(original);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Equal(1, harness.Authentication.CallCount);
        harness.AssertNoLoginOrRun();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("dispose-settings")]
    [InlineData("dispose-execution")]
    public async Task Pending_startup_check_ignores_late_success_after_cancellation_or_disposal(string interruption)
    {
        using CatalogHarness harness = new();
        ImmutableArray<CachedCopilotModel> old = Catalog("model-a", "auto");
        await harness.SeedAsync(new ApplicationSettings { PreferredModelId = "model-a", CachedModels = old });
        var original = harness.Stamp();
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<ExecutionAuthenticationSnapshot> pending = NewPending();
        harness.Authentication.Handler = _ => pending.Task; // Deliberately ignores cancellation.
        Task startup = harness.Settings.InitializeAsync(cancellation.Token);
        int lateNotifications = 0;
        try
        {
            await harness.Authentication.Entered.Task.WaitAsync(TestWait, TestContext.Current.CancellationToken);
            if (interruption == "cancel") cancellation.Cancel();
            else if (interruption == "dispose-settings") harness.Settings.Dispose();
            else harness.Execution.Dispose();
            Assert.True(harness.Authentication.LastToken.IsCancellationRequested);
            if (interruption == "dispose-settings") harness.Settings.PropertyChanged += (_, _) => lateNotifications++;
            if (interruption == "dispose-execution") harness.Execution.PropertyChanged += (_, _) => lateNotifications++;
        }
        finally
        {
            pending.TrySetResult(Available(Catalog("model-new", "auto")));
            await startup.WaitAsync(TestWait, TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, lateNotifications);
        AssertUnauthorized(harness);
        AssertCatalog(old, harness.Execution.CachedModels);
        Assert.Equal(old.Select(model => model.Id), harness.Execution.AvailableModelIds);
        harness.AssertUnchanged(original);
        Assert.Equal(1, harness.Authentication.CallCount);
        harness.AssertNoLoginOrRun();
        if (interruption == "cancel")
        {
            Assert.Equal(ExecutionAuthenticationState.Cancelled, harness.Execution.AuthenticationState);
            harness.Authentication.Handler = _ => Task.FromResult(Available(old));
            await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
            Assert.True(harness.Execution.CanStart);
            harness.AssertUnchanged(original);
        }
    }

    [Fact(SkipUnless = nameof(IsWindows), Skip = "Requires Windows read-only replacement semantics.")]
    public async Task Catalog_save_failure_does_not_fail_authentication_and_same_catalog_retries_next_check()
    {
        using CatalogHarness harness = new();
        await harness.SeedAsync(new ApplicationSettings { PreferredModelId = "model-a", MaxConcurrency = 2 });
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        var original = harness.Stamp();
        FileAttributes attributes = File.GetAttributes(harness.Path);
        try
        {
            File.SetAttributes(harness.Path, attributes | FileAttributes.ReadOnly);
            await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
            Assert.True(harness.Execution.IsAuthenticationAvailable);
            Assert.True(harness.Execution.CanStart);
            Assert.Contains("失敗", harness.Settings.ModelCatalogPersistenceText, StringComparison.Ordinal);
            harness.AssertUnchanged(original);
            Assert.True((File.GetAttributes(harness.Path) & FileAttributes.ReadOnly) != 0);
            Assert.Equal(harness.Path, Assert.Single(Directory.GetFiles(harness.Root)));
        }
        finally
        {
            File.SetAttributes(harness.Path, attributes);
        }

        List<NotifyCollectionChangedAction> changes = [];
        ((INotifyCollectionChanged)harness.Execution.AvailableModelIds).CollectionChanged += (_, args) => changes.Add(args.Action);
        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.Empty(changes);
        AssertCatalog(Catalog("model-a", "auto"), (await harness.ReadAsync()).CachedModels);
        Assert.Contains("自動保存しました", harness.Settings.ModelCatalogPersistenceText, StringComparison.Ordinal);
        Assert.Equal(2, harness.Authentication.CallCount);
        Assert.False(harness.Settings.HasUnsavedChanges);
        Assert.Null(harness.Settings.LastSaveTask);
        harness.AssertNoLoginOrRun();
    }

    [Theory]
    [InlineData("{broken", SettingsLoadStatus.JsonInvalid)]
    [InlineData("{\"schemaVersion\":999,\"future\":true}", SettingsLoadStatus.UnsupportedVersion)]
    public async Task Successful_check_never_automatically_repairs_corrupt_or_future_settings(string json, SettingsLoadStatus expected)
    {
        using CatalogHarness harness = new();
        harness.WriteJson(json);
        var original = harness.Stamp();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expected, harness.Settings.LoadStatus);
        Assert.Equal(0, harness.Authentication.CallCount);

        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.True(harness.Execution.IsAuthenticationAvailable);
        AssertCatalog(Catalog("model-a", "auto"), harness.Execution.CachedModels);
        harness.AssertUnchanged(original);
        Assert.Contains("既存の設定ファイルは保持", harness.Settings.ModelCatalogPersistenceText, StringComparison.Ordinal);
        Assert.Equal(expected, harness.Settings.LoadStatus);
        Assert.Null(harness.Settings.LastSaveTask);
        harness.AssertNoLoginOrRun();
    }

    [Fact]
    public async Task Automatic_catalog_save_preserves_persisted_preferences_and_definition_not_unsaved_edits()
    {
        using CatalogHarness harness = new();
        ApplicationSettings baseline = new()
        {
            PreferredModelId = "model-a", MaxConcurrency = 2,
            OutputDirectoryOverride = Path.Combine(harness.Root, "saved-output"),
            Definition = harness.Definition,
        };
        await harness.SeedAsync(baseline);
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await harness.LoadInputAsync();
        Assert.False(harness.Settings.HasUnsavedChanges);
        harness.Execution.SelectedModelId = "model-b";
        harness.Execution.MaxConcurrency = 3;
        harness.Execution.OutputDirectoryOverride = Path.Combine(harness.Root, "unsaved-output");
        harness.Design.DefinitionName = "UNSAVED-CATALOG-DEFINITION";
        QuantificationDefinition inputBefore = harness.Input.DefinitionDraft;
        QuantificationDefinition draftBefore = harness.Design.Draft;
        QuantificationDefinition? storedBefore = harness.Settings.StoredDefinition;
        Assert.True(harness.Settings.HasUnsavedChanges);
        harness.Authentication.Handler = _ => Task.FromResult(Available(Catalog("model-b", "model-a", "auto")));

        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        ApplicationSettings actual = await harness.ReadAsync();
        Assert.Equal(baseline.PreferredModelId, actual.PreferredModelId);
        Assert.Equal(baseline.MaxConcurrency, actual.MaxConcurrency);
        Assert.Equal(baseline.OutputDirectoryOverride, actual.OutputDirectoryOverride);
        CanonicalDefinitionSerializer canonical = new();
        Assert.Equal(canonical.Serialize(baseline.Definition!), canonical.Serialize(actual.Definition!));
        AssertCatalog(Catalog("model-b", "model-a", "auto"), actual.CachedModels);
        Assert.Same(inputBefore, harness.Input.DefinitionDraft);
        Assert.Same(draftBefore, harness.Design.Draft);
        Assert.Same(storedBefore, harness.Settings.StoredDefinition);
        Assert.Equal("model-b", harness.Execution.PreferredModelId);
        Assert.Equal("model-b", harness.Execution.SelectedModelId);
        Assert.Equal(3, harness.Execution.MaxConcurrency);
        Assert.True(harness.Settings.HasUnsavedChanges);
        Assert.Null(harness.Settings.SaveStatus);
        Assert.Null(harness.Settings.LastSaveTask);
        Assert.Null(harness.Settings.LastApplySavedDefinitionTask);
        Assert.False(Directory.Exists(baseline.OutputDirectoryOverride));
        Assert.False(Directory.Exists(harness.Execution.OutputDirectoryOverride));
        harness.AssertNoLoginOrRun();
    }

    [Fact]
    public async Task Startup_authentication_exception_preserves_cache_and_redacts_details()
    {
        using CatalogHarness harness = new();
        ImmutableArray<CachedCopilotModel> old = Catalog("model-a", "auto");
        await harness.SeedAsync(new ApplicationSettings { PreferredModelId = "model-a", CachedModels = old });
        var original = harness.Stamp();
        harness.Authentication.Handler = _ => throw new IOException("PRIVATE-CATALOG-AUTH-CANARY");

        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionAuthenticationState.RuntimeFailed, harness.Execution.AuthenticationState);
        AssertUnauthorized(harness);
        AssertCatalog(old, harness.Execution.CachedModels);
        Assert.Equal(old.Select(model => model.Id), harness.Execution.AvailableModelIds);
        Assert.DoesNotContain("PRIVATE-CATALOG-AUTH-CANARY", harness.Execution.AuthenticationStatusText, StringComparison.Ordinal);
        Assert.All(harness.Execution.TechnicalErrors, error =>
            Assert.DoesNotContain("PRIVATE-CATALOG-AUTH-CANARY", error.Message, StringComparison.Ordinal));
        harness.AssertUnchanged(original);
        harness.AssertNoLoginOrRun();
    }

    [Fact]
    public async Task First_successful_check_creates_only_cache_and_defaults_not_unsaved_preferences()
    {
        using CatalogHarness harness = new();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        harness.Execution.SelectedModelId = "model-a";
        harness.Execution.MaxConcurrency = 3;
        harness.Execution.OutputDirectoryOverride = Path.Combine(harness.Root, "unsaved-output");
        Assert.False(Directory.Exists(harness.Root));

        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        ApplicationSettings saved = await harness.ReadAsync();
        Assert.Equal(new ApplicationSettings(), saved with { CachedModels = null });
        AssertCatalog(Catalog("model-a", "auto"), saved.CachedModels);
        Assert.Equal(SettingsLoadStatus.Loaded, harness.Settings.LoadStatus);
        Assert.True(harness.Settings.HasUnsavedChanges);
        Assert.Equal(3, harness.Execution.MaxConcurrency);
        Assert.Equal("model-a", harness.Execution.PreferredModelId);
        Assert.Equal(harness.Path, Assert.Single(Directory.GetFileSystemEntries(harness.Root)));
        Assert.Null(harness.Settings.LastSaveTask);
        harness.AssertNoLoginOrRun();
    }

    [Fact]
    public async Task Applying_old_settings_after_fresh_authentication_never_replaces_the_live_catalog()
    {
        using CatalogHarness harness = new();
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        List<NotifyCollectionChangedAction> changes = [];
        ((INotifyCollectionChanged)harness.Execution.AvailableModelIds).CollectionChanged += (_, args) => changes.Add(args.Action);

        harness.Execution.ApplySettings(new ApplicationSettings
        {
            PreferredModelId = "model-a", CachedModels = Catalog("stale-model"),
        });

        Assert.Equal(new[] { "model-a", "auto" }, harness.Execution.AvailableModelIds);
        AssertCatalog(Catalog("model-a", "auto"), harness.Execution.CachedModels);
        Assert.True(harness.Execution.CanStart);
        Assert.Empty(changes);
        Assert.Equal(1, harness.Authentication.CallCount);
        harness.AssertNoLoginOrRun();
    }

    [Fact]
    public async Task Catalog_over_the_cache_limit_stays_fully_selectable_but_is_not_cached()
    {
        using CatalogHarness harness = new();
        int count = ApplicationSettings.MaximumCachedModels + 1;
        string[] ids = [.. Enumerable.Range(0, count).Select(index => $"model-{index}")];
        await harness.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        harness.Authentication.Handler = _ => Task.FromResult(Available(Catalog(ids)));

        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        harness.Execution.SelectedModelId = ids[^1];

        Assert.Equal(ids, harness.Execution.AvailableModelIds);
        Assert.Equal(ids[^1], harness.Execution.SelectedModelId);
        Assert.Null(harness.Execution.CachedModels);
        Assert.False(Directory.Exists(harness.Root));
        harness.AssertNoLoginOrRun();
    }

    private static ImmutableArray<CachedCopilotModel> Catalog(params string[] ids) =>
        [.. ids.Select(id => new CachedCopilotModel(id, 64_000, 128_000))];

    private static ExecutionAuthenticationSnapshot Available(ImmutableArray<CachedCopilotModel> models) =>
        new(ExecutionAuthenticationState.Available,
            models.Select(model => new CopilotModelAvailability(model.Id, model.MaximumPromptTokens, model.MaximumContextWindowTokens)),
            U04TestSupport.RuntimeIdentity());

    private static TaskCompletionSource<ExecutionAuthenticationSnapshot> NewPending() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void AssertCatalog(ImmutableArray<CachedCopilotModel> expected, ImmutableArray<CachedCopilotModel>? actual)
    {
        Assert.True(actual.HasValue);
        Assert.Equal(expected.ToArray(), actual.GetValueOrDefault().ToArray());
    }

    private static void AssertUnauthorized(CatalogHarness harness)
    {
        Assert.False(harness.Execution.IsAuthenticationAvailable);
        Assert.Null(harness.Execution.SelectedModelId);
        Assert.False(harness.Execution.CanStart);
        Assert.False(harness.Execution.StartCommand.CanExecute(null));
    }

    private sealed class CatalogHarness : IDisposable
    {
        public CatalogHarness()
        {
            Store = new SettingsFileStore(Path);
            Input = new InputViewModel(new SyntheticInputLoader(Definition));
            Design = new QuantificationDesignViewModel();
            Execution = new ExecutionViewModel(Authentication, Runner,
                new BundledCopilotLoginService(LoginResolver, _ => throw new InvalidOperationException("No login authorized.")));
            Execution.Configure(Definition, U01TestSupport.ValidateMapping(Definition).Metadata,
                System.IO.Path.Combine(Root, "synthetic-input.xlsx"));
            Settings = new SettingsViewModel(Input, Design, Execution, Store);
        }

        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StudyReportEvaluator-Catalog-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root, "setting.txt");
        public QuantificationDefinition Definition { get; } = U04TestSupport.Definition(2, 3);
        public ControlledAuthentication Authentication { get; } = new();
        public RejectingLoginResolver LoginResolver { get; } = new();
        public RecordingRunBoundary Runner { get; } = new((_, _, _) => throw new InvalidOperationException("No run authorized."));
        public SettingsFileStore Store { get; }
        public InputViewModel Input { get; }
        public QuantificationDesignViewModel Design { get; }
        public ExecutionViewModel Execution { get; }
        public SettingsViewModel Settings { get; }

        public async Task SeedAsync(ApplicationSettings settings) =>
            Assert.Equal(SettingsSaveStatus.Saved, (await Store.SaveAsync(settings, TestContext.Current.CancellationToken)).Status);

        public void WriteJson(string json)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path, json, new UTF8Encoding(false));
        }

        public (byte[] Bytes, DateTime Modified) Stamp()
        {
            // Backdate instead of sleeping: an unintended replacement cannot pass on timestamp granularity.
            File.SetLastWriteTimeUtc(Path, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            return (File.ReadAllBytes(Path), File.GetLastWriteTimeUtc(Path));
        }

        public void AssertUnchanged((byte[] Bytes, DateTime Modified) original)
        {
            Assert.Equal(original.Bytes, File.ReadAllBytes(Path));
            Assert.Equal(original.Modified, File.GetLastWriteTimeUtc(Path));
        }

        public async Task<ApplicationSettings> ReadAsync()
        {
            SettingsLoadResult result = await Store.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
            return Assert.IsType<ApplicationSettings>(result.Settings);
        }

        public async Task LoadInputAsync()
        {
            await Input.SetFilePathAsync(System.IO.Path.Combine(Root, "synthetic-input.xlsx"), TestContext.Current.CancellationToken);
            Assert.True(await Input.ApplySavedDefinitionAsync(Definition, TestContext.Current.CancellationToken));
            Settings.SynchronizeDrafts();
        }

        public void AssertNoLoginOrRun()
        {
            Assert.Null(Execution.LastLoginTask);
            Assert.Equal(0, LoginResolver.CallCount);
            Assert.False(Execution.IsLoggingIn);
            Assert.False(Execution.IsRunning);
            Assert.Null(Execution.LastRunContext);
            Assert.Equal(0, Runner.CallCount);
        }

        public void Dispose()
        {
            Settings.Dispose();
            Execution.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class ControlledAuthentication : IExecutionAuthenticationBoundary
    {
        public Func<CancellationToken, Task<ExecutionAuthenticationSnapshot>> Handler { get; set; } =
            _ => Task.FromResult(Available(Catalog("model-a", "auto")));
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken LastToken { get; private set; }
        public int CallCount { get; private set; }

        public Task<ExecutionAuthenticationSnapshot> CheckAsync(CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            CallCount++;
            Entered.TrySetResult();
            return Handler(cancellationToken);
        }
    }

    private sealed class RejectingLoginResolver : ICopilotCliPathResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult<string?>(null);
        }
    }

    private sealed class SyntheticInputLoader(QuantificationDefinition definition) : IInputWorkbookLoader
    {
        public Task<InputWorkbookLoadResult> LoadAsync(string filePath, uint headerRow, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = U01TestSupport.ValidateMapping(definition).Metadata;
            return Task.FromResult(new InputWorkbookLoadResult(U01TestSupport.InputSnapshot(), metadata,
                new ColumnMappingSuggester().Suggest(metadata)));
        }
    }
}
