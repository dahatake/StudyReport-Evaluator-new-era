using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Workspace;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.App.ViewModels;

public enum SettingsCategory
{
    Common,
    Mapping,
    Evaluation,
    Special,
    ImportedPrompts,
}

/// <summary>Editing boundaries and explicit persistence over the existing editor VMs.</summary>
/// <remarks>
/// The parent supplies initially synchronized editors and calls SynchronizeDrafts at
/// workflow/open boundaries. Their prior editing history cannot be inferred here.
/// Only production composition supplies a store and explicitly initializes it. A null
/// store never resolves or accesses a user-data path. This VM owns no editable definition.
/// </remarks>
public sealed class SettingsViewModel : UiObservableObject, IDisposable
{
    private static readonly JsonSerializerOptions ComparisonOptions = new()
    {
        Converters = { new DecimalComparisonConverter() },
    };

    private readonly SettingsFileStore? store;
    private readonly SemaphoreSlim persistenceGate = new(1, 1);
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly ViewModelCommand selectCategoryCommand;
    private readonly ViewModelCommand saveCommand;
    private readonly ViewModelCommand applySavedDefinitionCommand;
    private readonly ViewModelCommand requestCloseCommand;
    private SettingsCategory selectedCategory;
    private DefinitionEditor latestEditor = DefinitionEditor.Input;
    private bool synchronizing;
    private bool initialized;
    private bool loading;
    private bool restoringPreferences;
    private bool preferredModelEdited;
    private bool modelOptionsEdited;
    private bool maxConcurrencyEdited;
    private bool outputDirectoryEdited;
    private bool saving;
    private bool applying;
    private bool disposed;
    private bool hasUnsavedChanges;
    private byte[]? persistedValues = ComparisonBytes(new ApplicationSettings());
    private string? persistenceMessage;
    private string applyStatusText = string.Empty;
    private CancellationTokenSource? applicationCancellation;

    public SettingsViewModel(
        InputViewModel input,
        QuantificationDesignViewModel design,
        ExecutionViewModel execution,
        SettingsFileStore? store = null,
        WorkspaceLayoutService? workspace = null)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Workspace = workspace ?? new WorkspaceLayoutService();
        Design = design ?? throw new ArgumentNullException(nameof(design));
        Execution = execution ?? throw new ArgumentNullException(nameof(execution));
        this.store = store;
        initialized = store is null;
        // XAML supplies an enum via x:Static, never a display label or numeric string.
        selectCategoryCommand = new ViewModelCommand(
            parameter => SelectedCategory = (SettingsCategory)parameter!,
            parameter => !disposed && !IsApplying
                && parameter is SettingsCategory category && Enum.IsDefined(category));
        saveCommand = new ViewModelCommand(_ => _ = ObserveCommandAsync(SaveAsync()), _ => CanSave);
        applySavedDefinitionCommand = new ViewModelCommand(
            _ => _ = ObserveCommandAsync(ApplySavedDefinitionAsync()), _ => CanApplySavedDefinition);
        requestCloseCommand = new ViewModelCommand(_ => RequestClose(), _ => !disposed && !IsApplying);
        Input.PropertyChanged += InputChanged;
        Design.PropertyChanged += DesignChanged;
        Execution.PropertyChanged += ExecutionChanged;
        Execution.ModelCatalogRefreshed += PersistModelCatalogAsync;
        RefreshDirtyState();
    }

    public InputViewModel Input { get; }

    public QuantificationDesignViewModel Design { get; }

    /// <summary>Persona selection (FR-065). Saved immediately to layout.json, never to setting.txt.</summary>
    public WorkspaceLayoutService Workspace { get; }

    public ExecutionViewModel Execution { get; }

    public SettingsCategory SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (disposed || IsApplying || selectedCategory == value)
            {
                return;
            }

            SynchronizeDrafts();
            SetProperty(ref selectedCategory, value);
        }
    }

    /// <summary>For explicit UI display only; ToString never includes this path.</summary>
    public string FilePath => store?.FilePath ?? string.Empty;

    /// <summary>An immutable persisted snapshot, never automatically applied to an editor.</summary>
    public QuantificationDefinition? StoredDefinition { get; private set; }

    public string StoredDefinitionSummary => StoredDefinition is { } definition
        ? string.Format(CultureInfo.InvariantCulture, "保存定義: {0} · 設問 {1} 件 · 質問行 {2} · 回答行 {3}–{4}",
            definition.SourceSheet, definition.Questions.Length, definition.HeaderRow,
            definition.FirstDataRow, definition.LastDataRow)
        : "保存した採点定義はありません。";

    public bool IsInitialized => initialized;

    public bool IsLoadPending => !initialized;

    public bool IsLoading => loading;

    public bool IsSaving => saving;

    public bool IsApplying => applying;

    public bool HasUnsavedChanges => hasUnsavedChanges;

    public SettingsLoadStatus? LoadStatus { get; private set; }

    public SettingsSaveStatus? SaveStatus { get; private set; }

    public Task? LastLoadTask { get; private set; }

    public Task? LastSaveTask { get; private set; }

    public string ModelCatalogPersistenceText { get; private set; } = string.Empty;

    public Task<bool>? LastApplySavedDefinitionTask { get; private set; }

    public bool CanSave => !disposed && store is not null && IsInitialized
        && !IsLoading && !IsSaving && !IsApplying && !Input.IsBusy;

    /// <summary>Gate definition metadata as well as detail editors when no input exists.</summary>
    public bool CanEditDefinition => !disposed && Input.HasLoadedWorkbook && !Input.IsBusy && !IsApplying;

    public string DefinitionAvailabilityText => CanEditDefinition
        ? "採点定義の変更は現在の入力と設計へ反映されます。ファイル保存は明示操作です。"
        : Input.IsBusy || IsApplying
            ? "入力の読込・適用が終わるまでお待ちください。"
            : "採点定義を編集するには、先に Excel を読み込んでください。";

    public bool CanApplySavedDefinition => !disposed && IsInitialized && StoredDefinition is not null
        && Input.HasLoadedWorkbook && !Input.IsBusy && !Execution.IsRunning
        && !IsLoading && !IsSaving && !IsApplying;

    public string ApplyAvailabilityText => Execution.IsRunning
        ? "実行中は保存定義を一括適用できません。実行終了後に適用してください。"
        : StoredDefinition is null
            ? "適用できる保存定義はありません。"
            : !Input.HasLoadedWorkbook
                ? "現在の入力に適用する前に Excel を読み込んでください。"
                : !CanApplySavedDefinition
                    ? "読込・保存・適用が終わるまでお待ちください。"
                    : "保存定義の sheet、行範囲、列 mapping を確認して明示適用してください。";

    public string ApplyStatusText => applyStatusText;

    public string StatusText => store is null
        ? "保存先が構成されていません。ファイルへの読込・保存は行いません。"
        : IsLoading
            ? "設定を読み込んでいます…"
            : IsLoadPending
                ? persistenceMessage ?? "設定の読込待ちです。読込が終わるまで保存できません。"
                : IsSaving
                    ? "設定を保存中です…"
                    : persistenceMessage ?? (HasUnsavedChanges
                        ? "未保存の変更があります。"
                        : SaveStatus == SettingsSaveStatus.Saved || LoadStatus == SettingsLoadStatus.Loaded
                            ? "保存済みです。"
                            : "設定ファイルはまだありません。最初の明示保存まで作成しません。");

    public ICommand SelectCategoryCommand => selectCategoryCommand;

    public ICommand SaveCommand => saveCommand;

    public ICommand ApplySavedDefinitionCommand => applySavedDefinitionCommand;

    public ICommand RequestCloseCommand => requestCloseCommand;

    public event EventHandler? CloseRequested;

    /// <summary>Idempotent startup entry point; construction and navigation never call it.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) => IsInitialized
        ? Task.CompletedTask
        : LoadAsync(cancellationToken);

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading)
        {
            return LastLoadTask ?? Task.CompletedTask;
        }

        if (disposed || store is null || IsSaving || IsApplying)
        {
            return Task.CompletedTask;
        }

        LastLoadTask = LoadCoreAsync(cancellationToken);
        return LastLoadTask;
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (IsSaving)
        {
            return LastSaveTask ?? Task.CompletedTask;
        }

        if (!CanSave)
        {
            return Task.CompletedTask;
        }

        LastSaveTask = SaveCoreAsync(cancellationToken);
        return LastSaveTask;
    }

    public Task<bool> ApplySavedDefinitionAsync(CancellationToken cancellationToken = default)
    {
        if (IsApplying)
        {
            return LastApplySavedDefinitionTask ?? Task.FromResult(false);
        }

        if (!CanApplySavedDefinition)
        {
            return Task.FromResult(false);
        }

        LastApplySavedDefinitionTask = ApplySavedDefinitionCoreAsync(cancellationToken);
        return LastApplySavedDefinitionTask;
    }

    /// <summary>One edit boundary, latest definition editor to its peer, preserving VM instances.</summary>
    public void SynchronizeDrafts()
    {
        if (disposed || synchronizing || IsApplying || Input.IsBusy || !Input.HasLoadedWorkbook)
        {
            return;
        }

        synchronizing = true;
        try
        {
            switch (latestEditor)
            {
                case DefinitionEditor.Input:
                    Design.SynchronizeFromInput(Input.DefinitionDraft, Input.AvailableColumnNames);
                    break;
                case DefinitionEditor.Design:
                    Input.SynchronizeFromDesignDraft(Design.Draft);
                    break;
            }

            latestEditor = DefinitionEditor.Synchronized;
        }
        finally
        {
            synchronizing = false;
        }

        RefreshDirtyState();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Input.PropertyChanged -= InputChanged;
        Design.PropertyChanged -= DesignChanged;
        Execution.PropertyChanged -= ExecutionChanged;
        Execution.ModelCatalogRefreshed -= PersistModelCatalogAsync;
        CloseRequested = null;
        try
        {
            lifetimeCancellation.Cancel();
        }
        catch
        {
            // A cancellation observer must not prevent detachment or leak private content.
        }
        finally
        {
            lifetimeCancellation.Dispose();
        }

        RefreshAvailability();
        // The parent, not Settings, owns Input/Design/Execution and their lifetimes.
    }

    public override string ToString() => $"{nameof(SettingsViewModel)} {{ Content = <redacted> }}";

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        bool startup = !initialized;
        bool gateHeld = false;
        loading = true;
        try
        {
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, lifetimeCancellation.Token);
            NotifyOperationState();
            await persistenceGate.WaitAsync(cancellation.Token);
            gateHeld = true;
            SettingsLoadResult result = await store!.LoadAsync(cancellation.Token);
            if (disposed)
            {
                return;
            }

            LoadStatus = result.Status;
            SaveStatus = null;
            persistenceMessage = LoadFailureMessage(result.Status);
            if (result.IsSuccess && result.Settings is { } loaded)
            {
                byte[]? values = ComparisonBytes(loaded);
                if (values is null)
                {
                    LoadStatus = SettingsLoadStatus.JsonInvalid;
                    persistenceMessage = LoadFailureMessage(LoadStatus.Value);
                }
                else
                {
                    // Explicit edits win, including reversions and edits between cancelled
                    // startup attempts. Our own restore notifications are not user edits.
                    restoringPreferences = true;
                    try
                    {
                        Execution.ApplySettings(loaded with
                        {
                            PreferredModelId = preferredModelEdited ? Execution.PreferredModelId : loaded.PreferredModelId,
                            ModelPreferences = modelOptionsEdited ? Execution.ModelPreferences : loaded.ModelPreferences,
                            MaxConcurrency = maxConcurrencyEdited ? Execution.MaxConcurrency : loaded.MaxConcurrency,
                            OutputDirectoryOverride = outputDirectoryEdited
                                ? Execution.OutputDirectoryOverride : loaded.OutputDirectoryOverride,
                        });
                    }
                    finally
                    {
                        restoringPreferences = false;
                    }

                    StoredDefinition = loaded.Definition;
                    persistedValues = values;
                    OnPropertiesChanged(nameof(StoredDefinition), nameof(StoredDefinitionSummary));
                }
            }

            if (persistenceMessage is not null)
            {
                // Unknown/unreadable bytes are not a persisted baseline for current values.
                persistedValues = null;
            }

            initialized = true;
        }
        catch (OperationCanceledException)
        {
            if (!disposed)
            {
                persistenceMessage = "設定の読込を取り消しました。保存前にもう一度読み込んでください。";
            }
        }
        catch
        {
            if (!disposed)
            {
                initialized = true;
                LoadStatus = SettingsLoadStatus.ReadFailed;
                persistedValues = null;
                persistenceMessage = LoadFailureMessage(SettingsLoadStatus.ReadFailed);
            }
        }
        finally
        {
            if (gateHeld) persistenceGate.Release();
            loading = false;
            if (initialized)
            {
                // Later explicit reloads track only edits made during that read. A cancelled
                // initialization stays pending and must retain its edit history for retry.
                preferredModelEdited = false;
                modelOptionsEdited = false;
                maxConcurrencyEdited = false;
                outputDirectoryEdited = false;
            }

            if (!disposed)
            {
                RefreshDirtyState();
                NotifyOperationState();
            }
        }

        // Only a previously fetched catalog opts startup into a metadata/auth check.
        // Old settings and first launches remain passive until login or explicit check.
        if (startup && initialized && !disposed && !cancellationToken.IsCancellationRequested
            && Execution.CachedModels is not null
            && Execution.AuthenticationState == ExecutionAuthenticationState.NotChecked)
        {
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, lifetimeCancellation.Token);
            await Execution.CheckAuthenticationAsync(cancellation.Token);
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        bool gateHeld = false;
        saving = true;
        try
        {
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, lifetimeCancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            SynchronizeDrafts();
            // Domain records/arrays are immutable. Neither later editor commits nor preference
            // edits can change this graph, and only this value becomes the successful baseline.
            ApplicationSettings frozen = CaptureSettings();
            byte[]? frozenValues = ComparisonBytes(frozen);
            SaveStatus = null;
            persistenceMessage = null;
            NotifyOperationState();
            await persistenceGate.WaitAsync(cancellation.Token);
            gateHeld = true;
            // Cache updates queued before this write must not be undone by the frozen draft.
            frozen = frozen with { CachedModels = Execution.CachedModels };
            SettingsSaveResult result = await store!.SaveAsync(frozen, cancellation.Token);
            if (disposed)
            {
                return;
            }

            SaveStatus = result.Status;
            if (result.IsSuccess)
            {
                StoredDefinition = frozen.Definition;
                persistedValues = frozenValues;
                OnPropertiesChanged(nameof(StoredDefinition), nameof(StoredDefinitionSummary));
            }
            else
            {
                persistenceMessage = result.Status == SettingsSaveStatus.InvalidSettings
                    ? "保存失敗: 設定が不正です。現在の編集は未保存です。採点定義と共通設定を確認してください。"
                    : "保存失敗: 設定を書き込めませんでした。編集内容と既存ファイルを保持しています。保存先を確認してください。";
            }
        }
        catch (OperationCanceledException)
        {
            if (!disposed)
            {
                SaveStatus = null;
                persistenceMessage = "保存を取り消しました。編集内容と既存ファイルを保持しています。";
            }
        }
        catch
        {
            if (!disposed)
            {
                SaveStatus = SettingsSaveStatus.WriteFailed;
                persistenceMessage = "保存失敗: 設定を安全に保存できませんでした。編集内容と保存先を確認してください。";
            }
        }
        finally
        {
            if (gateHeld) persistenceGate.Release();
            saving = false;
            if (!disposed)
            {
                RefreshDirtyState();
                NotifyOperationState();
            }
        }
    }

    private async Task PersistModelCatalogAsync(CancellationToken cancellationToken)
    {
        if (disposed || store is null || Execution.CachedModels is null) return;
        bool gateHeld = false;
        try
        {
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, lifetimeCancellation.Token);
            await persistenceGate.WaitAsync(cancellation.Token);
            gateHeld = true;
            // Read-modify-write persisted values, never the user's unsaved editor draft.
            // Invalid/future/unreadable files must not be automatically repaired or replaced.
            SettingsLoadResult loaded = await store.LoadAsync(cancellation.Token);
            if (!loaded.IsSuccess || loaded.Settings is not { } baseline)
            {
                ModelCatalogPersistenceText = "モデル一覧を自動保存できません。既存の設定ファイルは保持しています。設定の読込状態を確認してください。";
                return;
            }

            var catalog = Execution.CachedModels!.Value;
            if (baseline.CachedModels is { } previous && previous.SequenceEqual(catalog))
            {
                ModelCatalogPersistenceText = "モデル一覧に変更はありません。保存済みの一覧を利用します。";
                return;
            }

            SettingsSaveResult saved = await store.SaveAsync(baseline with { CachedModels = catalog }, cancellation.Token);
            ModelCatalogPersistenceText = saved.IsSuccess
                ? "モデル一覧の差分を自動保存しました。編集中の設定は保存していません。"
                : "モデル一覧の自動保存に失敗しました。次の状態確認で再試行します。";
            if (saved.IsSuccess && LoadStatus == SettingsLoadStatus.Missing)
            {
                LoadStatus = SettingsLoadStatus.Loaded;
            }
        }
        catch (OperationCanceledException)
        {
            ModelCatalogPersistenceText = "モデル一覧の自動保存を取り消しました。次の状態確認で再試行します。";
        }
        catch
        {
            ModelCatalogPersistenceText = "モデル一覧の自動保存に失敗しました。既存の設定ファイルは保持しています。";
        }
        finally
        {
            if (gateHeld) persistenceGate.Release();
            if (!disposed)
            {
                OnPropertiesChanged(nameof(ModelCatalogPersistenceText), nameof(LoadStatus), nameof(StatusText));
            }
        }
    }

    private async Task<bool> ApplySavedDefinitionCoreAsync(CancellationToken cancellationToken)
    {
        applying = true;
        try
        {
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, lifetimeCancellation.Token);
            applicationCancellation = cancellation;
            applyStatusText = "保存定義を現在の入力に対して検証しています…";
            NotifyOperationState();
            // Do not synchronize first: a failed or cancelled apply must preserve both drafts.
            bool applied = await Input.ApplySavedDefinitionAsync(StoredDefinition!, cancellation.Token);
            if (disposed)
            {
                return false;
            }

            if (!applied)
            {
                applyStatusText = "保存定義を適用できませんでした。現在の編集は保持しています。入力の検証結果を確認してください。";
                return false;
            }

            synchronizing = true;
            try
            {
                Design.SynchronizeFromInput(Input.DefinitionDraft, Input.AvailableColumnNames);
                latestEditor = DefinitionEditor.Synchronized;
            }
            finally
            {
                synchronizing = false;
            }

            applyStatusText = "保存定義を現在の入力と設計に適用しました。";
            return true;
        }
        catch
        {
            if (!disposed)
            {
                applyStatusText = "保存定義を安全に適用できませんでした。入力と設計を確認してください。";
            }

            return false;
        }
        finally
        {
            applicationCancellation = null;
            applying = false;
            if (!disposed)
            {
                RefreshDirtyState();
                NotifyOperationState();
            }
        }
    }

    private void RequestClose()
    {
        SynchronizeDrafts();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void InputChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (disposed || synchronizing)
        {
            return;
        }

        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(InputViewModel.DefinitionDraft))
        {
            latestEditor = DefinitionEditor.Input;
            RefreshDirtyState();
        }

        if (string.IsNullOrEmpty(args.PropertyName)
            || args.PropertyName is nameof(InputViewModel.HasLoadedWorkbook) or nameof(InputViewModel.IsBusy))
        {
            RefreshDirtyState();
            RefreshAvailability();
        }
    }

    private void DesignChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (disposed || synchronizing
            || (!string.IsNullOrEmpty(args.PropertyName) && args.PropertyName != nameof(QuantificationDesignViewModel.Draft)))
        {
            return;
        }

        latestEditor = DefinitionEditor.Design;
        // Input guards its own edits; cancellation also protects Design edits during the read.
        CancelPendingApplication();
        RefreshDirtyState();
    }

    private void ExecutionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (disposed)
        {
            return;
        }

        if (string.IsNullOrEmpty(args.PropertyName)
            || args.PropertyName is nameof(ExecutionViewModel.PreferredModelId)
                or nameof(ExecutionViewModel.ModelPreferences)
                or nameof(ExecutionViewModel.MaxConcurrency) or nameof(ExecutionViewModel.OutputDirectoryOverride))
        {
            if (!restoringPreferences && (!initialized || loading))
            {
                // Only exact persisted-property notifications identify an explicit edit;
                // selection/authentication refreshes and unchanged setter calls do not.
                preferredModelEdited |= args.PropertyName == nameof(ExecutionViewModel.PreferredModelId);
                modelOptionsEdited |= args.PropertyName == nameof(ExecutionViewModel.ModelPreferences);
                maxConcurrencyEdited |= args.PropertyName == nameof(ExecutionViewModel.MaxConcurrency);
                outputDirectoryEdited |= args.PropertyName == nameof(ExecutionViewModel.OutputDirectoryOverride);
            }

            // A common preference change must never steal the latest definition editor.
            RefreshDirtyState();
        }

        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(ExecutionViewModel.IsRunning))
        {
            if (Execution.IsRunning)
            {
                CancelPendingApplication();
            }

            RefreshAvailability();
        }
    }

    private void CancelPendingApplication()
    {
        try
        {
            applicationCancellation?.Cancel();
        }
        catch
        {
            // Cancellation callbacks must not escape an editor notification.
        }
    }

    private ApplicationSettings CaptureSettings() => new()
    {
        PreferredModelId = Execution.PreferredModelId,
        ModelPreferences = Execution.ModelPreferences,
        MaxConcurrency = Execution.MaxConcurrency,
        OutputDirectoryOverride = Execution.OutputDirectoryOverride,
        Definition = !Input.HasLoadedWorkbook ? StoredDefinition
            : latestEditor == DefinitionEditor.Design ? Design.Draft : Input.DefinitionDraft,
        CachedModels = Execution.CachedModels,
    };

    private void RefreshDirtyState()
    {
        byte[]? current = ComparisonBytes(CaptureSettings());
        bool changed = persistedValues is null || current is null || !persistedValues.AsSpan().SequenceEqual(current);
        SetProperty(ref hasUnsavedChanges, changed, nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(StatusText));
    }

    private void NotifyOperationState()
    {
        OnPropertiesChanged(nameof(IsInitialized), nameof(IsLoadPending), nameof(IsLoading), nameof(IsSaving),
            nameof(IsApplying), nameof(LoadStatus), nameof(SaveStatus), nameof(StatusText), nameof(ApplyStatusText));
        RefreshAvailability();
    }

    private void RefreshAvailability()
    {
        OnPropertiesChanged(nameof(CanSave), nameof(CanApplySavedDefinition), nameof(CanEditDefinition),
            nameof(DefinitionAvailabilityText), nameof(ApplyAvailabilityText));
        saveCommand.RaiseCanExecuteChanged();
        applySavedDefinitionCommand.RaiseCanExecuteChanged();
        selectCategoryCommand.RaiseCanExecuteChanged();
        requestCloseCommand.RaiseCanExecuteChanged();
    }

    private static async Task ObserveCommandAsync(Task operation)
    {
        try
        {
            await operation;
        }
        catch
        {
            // IO/application failures are reported by the operation. Even a throwing UI
            // notification subscriber must not leave an unobserved fire-and-forget fault.
            // LastSaveTask/LastApplySavedDefinitionTask remain awaitable by their caller.
        }
    }

    private static byte[]? ComparisonBytes(ApplicationSettings settings)
    {
        try
        {
            // Comparison only, not the file format or another draft store. The real store
            // exclusively owns schema/definition validation and atomic persistence.
            // The automatic catalog is not an editable preference and does not dirty the draft.
            return JsonSerializer.SerializeToUtf8Bytes(settings with { CachedModels = null }, ComparisonOptions);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException
            or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed class DecimalComparisonConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            // Match canonical definition numeric values without altering drafts or file serialization.
            writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture), skipInputValidation: false);
    }

    private static string? LoadFailureMessage(SettingsLoadStatus status) => status switch
    {
        SettingsLoadStatus.JsonInvalid => "設定を読み込めません: ファイルの形式または値が不正です。元ファイルは変更していません。現在の編集は明示保存まで未保存です。",
        SettingsLoadStatus.UnsupportedVersion => "設定を読み込めません: 未対応の設定バージョンです。元ファイルは変更していません。現在の編集は明示保存まで未保存です。",
        SettingsLoadStatus.ReadFailed => "設定を読み込めません: 保存先とアクセス権を確認してください。元ファイルは変更していません。現在の編集は明示保存まで未保存です。",
        _ => null,
    };

    private enum DefinitionEditor { Synchronized, Input, Design }
}