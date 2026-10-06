using System.Collections.Immutable;

namespace StudyReportEvaluator.App.Workspace;

/// <summary>Personas of §5 (FR-065). A presentation preference only, never access control (NFR-SEC-006).</summary>
public enum Persona
{
    /// <summary>P-01 採点担当教員.</summary>
    Grader,

    /// <summary>P-02 評価設計担当.</summary>
    AssessmentDesigner,

    /// <summary>P-03 保守エンジニア.</summary>
    MaintenanceEngineer,
}

public static class PersonaCodes
{
    /// <summary>Selection order of FR-065: the first selected persona with a default for a screen wins.</summary>
    public static ImmutableArray<Persona> Ordered { get; } =
        [Persona.Grader, Persona.AssessmentDesigner, Persona.MaintenanceEngineer];

    public static string Code(Persona persona) => persona switch
    {
        Persona.Grader => "P-01",
        Persona.AssessmentDesigner => "P-02",
        Persona.MaintenanceEngineer => "P-03",
        _ => throw new ArgumentOutOfRangeException(nameof(persona)),
    };

    public static string DisplayName(Persona persona) => persona switch
    {
        Persona.Grader => "P-01 採点担当教員",
        Persona.AssessmentDesigner => "P-02 評価設計担当",
        Persona.MaintenanceEngineer => "P-03 保守エンジニア",
        _ => throw new ArgumentOutOfRangeException(nameof(persona)),
    };

    public static bool TryParse(string? code, out Persona persona)
    {
        foreach (Persona candidate in Ordered)
        {
            if (string.Equals(Code(candidate), code, StringComparison.Ordinal))
            {
                persona = candidate;
                return true;
            }
        }

        persona = default;
        return false;
    }
}

/// <summary>Screen keys: the persistence unit of NFR-UX-004 is OS user (the file location) × screen.</summary>
public static class WorkspaceScreens
{
    public const string Input = "Step.Input";
    public const string Design = "Step.Design";
    public const string Execution = "Step.Execution";
    public const string Results = "Step.Results";
    public const string SettingsCommon = "Settings.Common";
    public const string SettingsMapping = "Settings.Mapping";
    public const string SettingsEvaluation = "Settings.Evaluation";
    public const string SettingsSpecial = "Settings.Special";
    public const string SettingsImportedPrompts = "Settings.ImportedPrompts";

    public static ImmutableArray<string> All { get; } =
    [
        Input, Design, Execution, Results,
        SettingsCommon, SettingsMapping, SettingsEvaluation, SettingsSpecial, SettingsImportedPrompts,
    ];

    public static string DisplayName(string screenKey) => screenKey switch
    {
        Input => "1 入力",
        Design => "2 定量化設計",
        Execution => "3 実行",
        Results => "4 結果・出力",
        SettingsCommon => "設定・共通",
        SettingsMapping => "設定・入力詳細",
        SettingsEvaluation => "設定・通常評価",
        SettingsSpecial => "設定・固有評価",
        SettingsImportedPrompts => "設定・読込Prompt",
        _ => screenKey,
    };
}

/// <summary>Panel identifiers. They are also the stem of the panels' Automation IDs.</summary>
public static class WorkspacePanelIds
{
    public const string DesignForm = "Design.Form";
    public const string DesignAllocation = "Design.Allocation";
    public const string ExecutionConditions = "Execution.Conditions";
    public const string ExecutionProgress = "Execution.Progress";
    public const string ExecutionCost = "Execution.Cost";
    public const string ResultsList = "Results.List";
    public const string ResultsDetail = "Results.Detail";
    public const string ResultsChart = "Results.Chart";
    public const string ResultsCost = "Results.Cost";
}

/// <summary>Generic defaults (the pre-workspace arrangement) and the §5.1 persona defaults (NFR-UX-009).</summary>
public static class WorkspaceDefaults
{
    private static readonly ImmutableDictionary<string, ImmutableArray<string>> PanelsByScreen =
        new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
        {
            [WorkspaceScreens.Design] = [WorkspacePanelIds.DesignForm, WorkspacePanelIds.DesignAllocation],
            [WorkspaceScreens.Execution] =
                [WorkspacePanelIds.ExecutionConditions, WorkspacePanelIds.ExecutionProgress, WorkspacePanelIds.ExecutionCost],
            [WorkspaceScreens.Results] =
            [
                WorkspacePanelIds.ResultsList, WorkspacePanelIds.ResultsDetail,
                WorkspacePanelIds.ResultsChart, WorkspacePanelIds.ResultsCost,
            ],
        }.ToImmutableDictionary(StringComparer.Ordinal);

    /// <summary>Panels of a screen; empty for screens without a workspace.</summary>
    public static ImmutableArray<string> PanelsFor(string screenKey) =>
        PanelsByScreen.TryGetValue(screenKey, out ImmutableArray<string> panels) ? panels : [];

    public static WorkspaceLayout Generic(string screenKey) => screenKey switch
    {
        // The form alone, as before; the allocation chart is available from the panel menu.
        WorkspaceScreens.Design => new(WorkspaceOrientation.Horizontal,
            [Group(3d, WorkspacePanelIds.DesignForm), Group(1.3d, WorkspacePanelIds.DesignAllocation)],
            [WorkspacePanelIds.DesignAllocation]),
        WorkspaceScreens.Execution => new(WorkspaceOrientation.Horizontal,
            [
                Group(2d, WorkspacePanelIds.ExecutionConditions),
                Group(3d, WorkspacePanelIds.ExecutionProgress, WorkspacePanelIds.ExecutionCost),
            ]),
        // One area that switches between list, detail, chart and cost, as before.
        WorkspaceScreens.Results => new(WorkspaceOrientation.Horizontal,
            [Group(1d, WorkspacePanelIds.ResultsList, WorkspacePanelIds.ResultsDetail,
                WorkspacePanelIds.ResultsChart, WorkspacePanelIds.ResultsCost)]),
        _ => new(WorkspaceOrientation.Horizontal, PanelsFor(screenKey).Select(id => Group(1d, id))),
    };

    /// <summary>The §5.1 default of one persona, or null when the persona has none for the screen.</summary>
    public static WorkspaceLayout? ForPersona(Persona persona, string screenKey) => (persona, screenKey) switch
    {
        // P-01: left list, centre detail, right chart tabs.
        (Persona.Grader, WorkspaceScreens.Results) => new(WorkspaceOrientation.Horizontal,
            [
                Group(4d, WorkspacePanelIds.ResultsList),
                Group(5.5d, WorkspacePanelIds.ResultsDetail),
                Group(2.5d, WorkspacePanelIds.ResultsChart, WorkspacePanelIds.ResultsCost),
            ],
            [WorkspacePanelIds.ResultsCost]),
        // P-02: left/centre design form, right allocation composition.
        (Persona.AssessmentDesigner, WorkspaceScreens.Design) => new(WorkspaceOrientation.Horizontal,
            [Group(3d, WorkspacePanelIds.DesignForm), Group(1.3d, WorkspacePanelIds.DesignAllocation)]),
        // P-03: top progress (with the cost summary of the fixed bar), bottom cost and job log.
        (Persona.MaintenanceEngineer, WorkspaceScreens.Execution) => new(WorkspaceOrientation.Vertical,
            [
                Group(3d, WorkspacePanelIds.ExecutionProgress, WorkspacePanelIds.ExecutionConditions),
                Group(2d, WorkspacePanelIds.ExecutionCost),
            ]),
        (Persona.MaintenanceEngineer, WorkspaceScreens.Results) => new(WorkspaceOrientation.Vertical,
            [
                Group(3d, WorkspacePanelIds.ResultsList, WorkspacePanelIds.ResultsDetail, WorkspacePanelIds.ResultsChart),
                Group(2d, WorkspacePanelIds.ResultsCost),
            ]),
        _ => null,
    };

    /// <summary>FR-065: the first selected persona (P-01, P-02, P-03 order) with a default, else the generic one.</summary>
    public static WorkspaceLayout Resolve(string screenKey, IEnumerable<Persona> selectedPersonas)
    {
        ArgumentNullException.ThrowIfNull(selectedPersonas);
        HashSet<Persona> selected = [.. selectedPersonas];
        foreach (Persona persona in PersonaCodes.Ordered.Where(selected.Contains))
        {
            if (ForPersona(persona, screenKey) is { } layout)
            {
                return layout.Normalize(PanelsFor(screenKey));
            }
        }

        return Generic(screenKey).Normalize(PanelsFor(screenKey));
    }

    private static WorkspaceGroup Group(double size, params string[] panelIds) => new(panelIds, size);
}
