using System.Collections.Immutable;
using System.Windows.Input;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Settings;

namespace StudyReportEvaluator.App.ViewModels;

public sealed partial class ExecutionViewModel
{
    private ImmutableArray<ModelSelectionPreference>? modelPreferences;
    private IReadOnlyList<ReasoningEffortOption> reasoningEffortOptions = [];
    private IReadOnlyList<ContextSizeOption> contextSizeOptions = [];
    private ReasoningEffortOption? selectedReasoningEffortOption;
    private ContextSizeOption? selectedContextSizeOption;
    private bool updatingModelOptions;
    private bool reasoningPreferenceUnavailable;
    private bool contextPreferenceUnavailable;
    private ViewModelCommand? resetModelOptionsCommand;

    public ImmutableArray<ModelSelectionPreference>? ModelPreferences => modelPreferences;
    public IReadOnlyList<ReasoningEffortOption> ReasoningEffortOptions => reasoningEffortOptions;
    public IReadOnlyList<ContextSizeOption> ContextSizeOptions => contextSizeOptions;
    public bool CanSelectReasoningEffort => reasoningEffortOptions.Count > 0;
    public bool CanSelectContextSize => contextSizeOptions.Count > 0;
    public bool CanResetModelOptions => !disposed && CurrentModelPreference() is not null;
    public ICommand ResetModelOptionsCommand => resetModelOptionsCommand ??= new ViewModelCommand(
        _ => ResetModelOptions(), _ => CanResetModelOptions);

    public ReasoningEffortOption? SelectedReasoningEffortOption
    {
        get => selectedReasoningEffortOption;
        set
        {
            if (updatingModelOptions || value is null) return;
            if (!reasoningEffortOptions.Contains(value))
                throw new ArgumentException("Select an available reasoning effort.", nameof(value));
            SaveModelOptions(value.Value, RequestedContextTier());
        }
    }

    public ContextSizeOption? SelectedContextSizeOption
    {
        get => selectedContextSizeOption;
        set
        {
            if (updatingModelOptions || value is null) return;
            if (!contextSizeOptions.Contains(value))
                throw new ArgumentException("Select an available context size.", nameof(value));
            SaveModelOptions(RequestedReasoningEffort(), value.Tier);
        }
    }

    public string? SelectedContextTier => selectedContextSizeOption?.Tier == ModelOptionPolicy.LongContextTier
        ? ModelOptionPolicy.LongContextTier : null;

    public string SelectedContextSizeText => SelectedModelId is null ? "未選択"
        : contextPreferenceUnavailable ? "選択した Context Size は現在利用できません"
        : selectedContextSizeOption?.DisplayName ?? "SDK未公開";

    public string? CurrentRunContextTierText => currentRunRequest is null ? null
        : currentRunRequest.ContextTier ?? ModelOptionPolicy.DefaultContextTier;

    private ModelSelectionPreference? CurrentModelPreference() =>
        modelPreferences?.FirstOrDefault(preference => preference.ModelId == SelectedModelId);

    private string? RequestedReasoningEffort() =>
        CurrentModelPreference()?.ReasoningEffort ?? selectedReasoningEffortOption?.Value;

    private string RequestedContextTier() =>
        CurrentModelPreference()?.ContextTier ?? selectedContextSizeOption?.Tier ?? ModelOptionPolicy.DefaultContextTier;

    private void SaveModelOptions(string? effort, string tier)
    {
        if (SelectedModelId is not { } id) return;
        ImmutableArray<ModelSelectionPreference> preferences = modelPreferences ?? [];
        ModelSelectionPreference next = new(id, effort, tier);
        int index = -1;
        for (int candidate = 0; candidate < preferences.Length; candidate++)
        {
            if (preferences[candidate].ModelId == id) { index = candidate; break; }
        }
        if (index >= 0 && preferences[index] == next) return;
        modelPreferences = index < 0 ? preferences.Add(next) : preferences.SetItem(index, next);
        OnPropertyChanged(nameof(ModelPreferences));
        RefreshModelOptions();
        InvalidateResumePreflight();
        runtimeErrorCode = null;
        runPreflightErrors = [];
        Revalidate();
    }

    private void ResetModelOptions()
    {
        if (!CanResetModelOptions || modelPreferences is not { } preferences) return;
        modelPreferences = [.. preferences.Where(preference => preference.ModelId != SelectedModelId)];
        OnPropertyChanged(nameof(ModelPreferences));
        RefreshModelOptions();
        InvalidateResumePreflight();
        runtimeErrorCode = null;
        runPreflightErrors = [];
        Revalidate();
    }

    private void RefreshModelOptions()
    {
        updatingModelOptions = true;
        try
        {
            reasoningPreferenceUnavailable = false;
            contextPreferenceUnavailable = false;
            reasoningEffortOptions = [];
            contextSizeOptions = [];
            selectedReasoningEffortOption = null;
            selectedContextSizeOption = null;
            if (SelectedModelId is { } id && modelsById.TryGetValue(id, out CopilotModelAvailability? model))
            {
                reasoningEffortOptions = ModelOptionPolicy.ReasoningOptions(model);
                contextSizeOptions = ModelOptionPolicy.ContextOptions(model);
                ModelSelectionPreference? preference = CurrentModelPreference();
                string? effort = preference is null
                    ? ReasoningEffortPolicy.ResolveReasoningEffort([model.ToModelInfo()], id)
                    : preference.ReasoningEffort;
                selectedReasoningEffortOption = reasoningEffortOptions.FirstOrDefault(option => option.Value == effort);
                reasoningPreferenceUnavailable = preference?.ReasoningEffort is not null
                    && selectedReasoningEffortOption is null;
                string tier = preference?.ContextTier ?? ModelOptionPolicy.DefaultContextTier;
                selectedContextSizeOption = contextSizeOptions.FirstOrDefault(option => option.Tier == tier);
                contextPreferenceUnavailable = selectedContextSizeOption is null;
            }

            OnPropertiesChanged(nameof(ReasoningEffortOptions), nameof(ContextSizeOptions),
                nameof(SelectedReasoningEffortOption), nameof(SelectedContextSizeOption),
                nameof(CanSelectReasoningEffort), nameof(CanSelectContextSize),
                nameof(SelectedModelReasoningEffort), nameof(SelectedModelReasoningEffortText),
                nameof(SelectedModelPromptTokenLimit), nameof(SelectedModelLimitText),
                nameof(SelectedContextTier), nameof(SelectedContextSizeText), nameof(CanResetModelOptions));
            resetModelOptionsCommand?.RaiseCanExecuteChanged();
        }
        finally
        {
            updatingModelOptions = false;
        }
    }
}
