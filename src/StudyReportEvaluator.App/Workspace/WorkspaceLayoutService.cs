using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StudyReportEvaluator.App.Workspace;

public sealed class WorkspaceLayoutChangedEventArgs(string? screenKey) : EventArgs
{
    /// <summary>The changed screen, or null when every screen may have changed (load, persona change).</summary>
    public string? ScreenKey { get; } = screenKey;
}

/// <summary>
/// Owns the per-screen layouts and the persona selection of one OS user (NFR-UX-004, FR-065).
/// Every change is saved to layout.json immediately when the file was readable (or absent) at
/// startup; an unreadable or unknown file is never overwritten in that session.
/// </summary>
public sealed class WorkspaceLayoutService : INotifyPropertyChanged
{
    private readonly LayoutFileStore? store;
    private readonly bool personaAware;
    private readonly Dictionary<string, WorkspaceLayout> customLayouts = new(StringComparer.Ordinal);
    private ImmutableArray<Persona> personas;
    private LayoutLoadStatus? loadStatus;
    private bool loaded;
    private bool lastSaveFailed;

    /// <summary>Application service: the first-run persona is P-01 (AS-009).</summary>
    public WorkspaceLayoutService(LayoutFileStore? store = null)
        : this(store, personaAware: true)
    {
    }

    private WorkspaceLayoutService(LayoutFileStore? store, bool personaAware)
    {
        this.store = store;
        this.personaAware = personaAware;
        personas = personaAware ? [Persona.Grader] : [];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<WorkspaceLayoutChangedEventArgs>? LayoutChanged;

    /// <summary>A host without a user context (a view shown on its own) uses the generic layouts in memory.</summary>
    public static WorkspaceLayoutService CreateGeneric() => new(null, personaAware: false);

    public string FilePath => store?.FilePath ?? string.Empty;

    public LayoutLoadStatus? LoadStatus => loadStatus;

    /// <summary>Saving is enabled only when the existing file was read successfully or did not exist.</summary>
    public bool CanPersist => store is not null && loaded
        && loadStatus is LayoutLoadStatus.Loaded or LayoutLoadStatus.Missing;

    public int SaveCount { get; private set; }

    public ImmutableArray<Persona> Personas => personas;

    public string StatusText => store is null
        ? "レイアウトの保存先が構成されていないため、レイアウトの変更はこの起動中だけ有効です。"
        : !loaded
            ? "レイアウトの読み込み前です。"
            : loadStatus is LayoutLoadStatus.Loaded or LayoutLoadStatus.Missing
                ? lastSaveFailed
                    ? "layout.json に保存できませんでした。レイアウトの変更はこの起動中だけ有効です。"
                    : "パネルの配置とペルソナは layout.json に自動で保存されます（学生の情報・パス・回答・Prompt は保存しません）。"
                : "layout.json を読み込めなかったため、既定のレイアウトで表示しています。ファイルは変更していません。この起動中のレイアウトの変更は保存されません。";

    public bool IsGraderSelected
    {
        get => IsSelected(Persona.Grader);
        set => SetPersonaSelected(Persona.Grader, value);
    }

    public bool IsAssessmentDesignerSelected
    {
        get => IsSelected(Persona.AssessmentDesigner);
        set => SetPersonaSelected(Persona.AssessmentDesigner, value);
    }

    public bool IsMaintenanceEngineerSelected
    {
        get => IsSelected(Persona.MaintenanceEngineer);
        set => SetPersonaSelected(Persona.MaintenanceEngineer, value);
    }

    /// <summary>The last selected persona cannot be cleared: at least one stays selected.</summary>
    public bool CanChangeGrader => CanChange(Persona.Grader);

    public bool CanChangeAssessmentDesigner => CanChange(Persona.AssessmentDesigner);

    public bool CanChangeMaintenanceEngineer => CanChange(Persona.MaintenanceEngineer);

    /// <summary>Reads layout.json once. Never creates, repairs or rewrites the file.</summary>
    public void Load()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        if (store is not null)
        {
            LayoutLoadResult result = store.Load();
            loadStatus = result.Status;
            if (result is { Status: LayoutLoadStatus.Loaded, Document: { } document })
            {
                personas = document.Personas;
                foreach ((string screen, WorkspaceLayout layout) in document.Screens)
                {
                    ImmutableArray<string> panels = WorkspaceDefaults.PanelsFor(screen);
                    if (!panels.IsEmpty)
                    {
                        customLayouts[screen] = layout.Normalize(panels);
                    }
                }
            }
        }

        RaisePersonaProperties();
        OnPropertyChanged(nameof(LoadStatus));
        OnPropertyChanged(nameof(CanPersist));
        OnPropertyChanged(nameof(StatusText));
        LayoutChanged?.Invoke(this, new WorkspaceLayoutChangedEventArgs(null));
    }

    public bool IsSelected(Persona persona) => personas.Contains(persona);

    public bool SetPersonaSelected(Persona persona, bool selected)
    {
        if (!personaAware || !Enum.IsDefined(persona) || IsSelected(persona) == selected)
        {
            return false;
        }

        if (!selected && personas.Length == 1)
        {
            // Refuse and re-publish so a two-way check box returns to the selected state.
            RaisePersonaProperties();
            return false;
        }

        personas = [.. PersonaCodes.Ordered.Where(candidate => candidate == persona ? selected : personas.Contains(candidate))];
        Save();
        RaisePersonaProperties();
        LayoutChanged?.Invoke(this, new WorkspaceLayoutChangedEventArgs(null));
        return true;
    }

    public WorkspaceLayout GetLayout(string screenKey)
    {
        ArgumentNullException.ThrowIfNull(screenKey);
        return customLayouts.TryGetValue(screenKey, out WorkspaceLayout? layout) ? layout : GetDefaultLayout(screenKey);
    }

    public WorkspaceLayout GetDefaultLayout(string screenKey)
    {
        ArgumentNullException.ThrowIfNull(screenKey);
        return WorkspaceDefaults.Resolve(screenKey, personas);
    }

    public bool HasCustomLayout(string screenKey) => customLayouts.ContainsKey(screenKey);

    public void SetLayout(string screenKey, WorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(screenKey);
        ArgumentNullException.ThrowIfNull(layout);
        ImmutableArray<string> panels = WorkspaceDefaults.PanelsFor(screenKey);
        if (panels.IsEmpty)
        {
            throw new ArgumentException("The screen has no workspace panels.", nameof(screenKey));
        }

        WorkspaceLayout normalized = layout.Normalize(panels);
        if (normalized.Equals(GetLayout(screenKey)))
        {
            return;
        }

        customLayouts[screenKey] = normalized;
        Save();
        LayoutChanged?.Invoke(this, new WorkspaceLayoutChangedEventArgs(screenKey));
    }

    /// <summary>NFR-UX-005: forget the screen's own layout and show the persona default again.</summary>
    public void ResetLayout(string screenKey)
    {
        ArgumentNullException.ThrowIfNull(screenKey);
        if (customLayouts.Remove(screenKey))
        {
            Save();
        }

        LayoutChanged?.Invoke(this, new WorkspaceLayoutChangedEventArgs(screenKey));
    }

    private bool CanChange(Persona persona) => personaAware && !(IsSelected(persona) && personas.Length == 1);

    private void Save()
    {
        if (!CanPersist)
        {
            return;
        }

        bool saved = store!.Save(new LayoutDocument(personas,
            customLayouts.ToImmutableDictionary(StringComparer.Ordinal)));
        SaveCount += saved ? 1 : 0;
        if (lastSaveFailed == saved)
        {
            lastSaveFailed = !saved;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private void RaisePersonaProperties()
    {
        OnPropertyChanged(nameof(Personas));
        OnPropertyChanged(nameof(IsGraderSelected));
        OnPropertyChanged(nameof(IsAssessmentDesignerSelected));
        OnPropertyChanged(nameof(IsMaintenanceEngineerSelected));
        OnPropertyChanged(nameof(CanChangeGrader));
        OnPropertyChanged(nameof(CanChangeAssessmentDesigner));
        OnPropertyChanged(nameof(CanChangeMaintenanceEngineer));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
