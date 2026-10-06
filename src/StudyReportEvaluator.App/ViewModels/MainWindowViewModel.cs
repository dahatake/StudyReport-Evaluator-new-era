using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workspace;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.App.ViewModels;

public sealed record WorkflowStepPresentation(
    WorkflowStep Step,
    int Number,
    string NumberText,
    string Title,
    string EnglishTitle,
    string StatusIcon,
    string StatusText,
    bool IsCurrent,
    bool IsCompleted,
    bool IsUpcoming)
{
    public string AccessibleName =>
        $"ステップ {Number.ToString(CultureInfo.InvariantCulture)}、{Title}、{StatusText}";
}

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly WorkflowNavigator navigator;
    private readonly DelegateCommand navigateCommand;
    private readonly DelegateCommand nextCommand;
    private readonly DelegateCommand previousCommand;
    private readonly DelegateCommand openSettingsCommand;
    private readonly ExecutionViewModel executionViewModel;
    private readonly ResultsOutputViewModel resultsOutputViewModel;
    private readonly SettingsViewModel settingsViewModel;
    private ImmutableArray<WorkflowStepPresentation> stepPresentations;
    private readonly QuantificationDesignViewModel designViewModel;
    private bool synchronizingDrafts;
    private QuantificationDefinition? latestObservedDraft;
    private SettingsCategory observedSettingsCategory;
    private bool observedSettingsApplying;
    private bool isSettingsOpen;
    private bool disposed;

    public MainWindowViewModel(WorkflowNavigator navigator)
        : this(
            navigator,
            new InputViewModel(),
            new QuantificationDesignViewModel(),
            new ExecutionViewModel(),
            new ResultsOutputViewModel(),
            null)
    {
    }

    public MainWindowViewModel(
        WorkflowNavigator navigator,
        InputViewModel inputViewModel,
        QuantificationDesignViewModel designViewModel)
        : this(
            navigator,
            inputViewModel,
            designViewModel,
            new ExecutionViewModel(),
            new ResultsOutputViewModel(),
            null)
    {
    }

    public MainWindowViewModel(
        WorkflowNavigator navigator,
        InputViewModel inputViewModel,
        QuantificationDesignViewModel designViewModel,
        ExecutionViewModel executionViewModel,
        ResultsOutputViewModel resultsOutputViewModel,
        SettingsFileStore? settingsStore = null,
        WorkspaceLayoutService? workspace = null)
    {
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        Workspace = workspace ?? new WorkspaceLayoutService();
        InputViewModel = inputViewModel ?? throw new ArgumentNullException(nameof(inputViewModel));
        this.designViewModel = designViewModel ?? throw new ArgumentNullException(nameof(designViewModel));
        this.executionViewModel = executionViewModel
            ?? throw new ArgumentNullException(nameof(executionViewModel));
        this.resultsOutputViewModel = resultsOutputViewModel
            ?? throw new ArgumentNullException(nameof(resultsOutputViewModel));
        settingsViewModel = new SettingsViewModel(
            InputViewModel,
            this.designViewModel,
            this.executionViewModel,
            settingsStore,
            Workspace);
        observedSettingsCategory = settingsViewModel.SelectedCategory;
        stepPresentations = BuildStepPresentations();
        navigateCommand = new DelegateCommand(
            ExecuteNavigate,
            CanNavigate);
        nextCommand = new DelegateCommand(
            _ => ExecuteMoveNext(),
            _ => this.navigator.CanMoveNext);
        previousCommand = new DelegateCommand(
            _ => ExecuteMovePrevious(),
            _ => this.navigator.CanMovePrevious);
        openSettingsCommand = new DelegateCommand(
            ExecuteOpenSettings,
            CanOpenSettings);
        this.navigator.CurrentStepChanged += HandleCurrentStepChanged;
        InputViewModel.PropertyChanged += HandleInputViewModelPropertyChanged;
        this.designViewModel.PropertyChanged += HandleDesignViewModelPropertyChanged;
        this.executionViewModel.PropertyChanged += HandleExecutionViewModelPropertyChanged;
        this.executionViewModel.RunCompleted += HandleRunCompleted;
        this.executionViewModel.CheckpointInputRequested += HandleCheckpointInputRequestedAsync;
        settingsViewModel.PropertyChanged += HandleSettingsViewModelPropertyChanged;
        settingsViewModel.CloseRequested += HandleSettingsCloseRequested;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkflowNavigator Navigator => navigator;

    public InputViewModel InputViewModel { get; }

    public QuantificationDesignViewModel DesignViewModel => designViewModel;

    public ExecutionViewModel ExecutionViewModel => executionViewModel;

    public ResultsOutputViewModel ResultsOutputViewModel => resultsOutputViewModel;

    public SettingsViewModel Settings => settingsViewModel;

    /// <summary>Panel layouts and personas of this OS user (NFR-UX-004, FR-065). Not part of setting.txt.</summary>
    public WorkspaceLayoutService Workspace { get; }

    public WorkflowStep CurrentStep => navigator.CurrentStep;

    public bool IsSettingsOpen => isSettingsOpen;

    public UiObservableObject? CurrentEditorViewModel => IsSettingsOpen
        ? Settings
        : CurrentStep switch
        {
            WorkflowStep.Input => InputViewModel,
            WorkflowStep.Design => DesignViewModel,
            WorkflowStep.Execution => ExecutionViewModel,
            WorkflowStep.Results => ResultsOutputViewModel,
            _ => throw new ArgumentOutOfRangeException(nameof(CurrentStep), CurrentStep, "Unknown workflow step."),
        };

    public bool HasEditorContent => CurrentEditorViewModel is not null;

    public bool HasPlaceholderContent => !HasEditorContent;

    public ImmutableArray<WorkflowStepPresentation> Steps => stepPresentations;

    public WorkflowStepPresentation InputStep => stepPresentations[0];

    public WorkflowStepPresentation DesignStep => stepPresentations[1];

    public WorkflowStepPresentation ExecutionStep => stepPresentations[2];

    public WorkflowStepPresentation ResultsStep => stepPresentations[3];

    public ICommand NavigateCommand => navigateCommand;

    public ICommand NextCommand => nextCommand;

    public ICommand PreviousCommand => previousCommand;

    public ICommand OpenSettingsCommand => openSettingsCommand;

    public string CurrentStepKicker =>
        $"STEP {(navigator.CurrentIndex + 1).ToString("00", CultureInfo.InvariantCulture)} / 04 · {navigator.CurrentDefinition.EnglishTitle}";

    public string CurrentStepTitle => CurrentContent.Title;

    public string CurrentStepDescription => CurrentContent.Description;

    public string CurrentDetailOne => CurrentContent.DetailOne;

    public string CurrentDetailTwo => CurrentContent.DetailTwo;

    public string CurrentDetailThree => CurrentContent.DetailThree;

    public string CurrentStepStatusText =>
        $"{CurrentPresentation.StatusIcon} {CurrentPresentation.Title} — {CurrentPresentation.StatusText}";

    public string NavigationProgressText =>
        $"{(navigator.CurrentIndex + 1).ToString(CultureInfo.InvariantCulture)} / {WorkflowNavigator.StepCount.ToString(CultureInfo.InvariantCulture)} ステップ";

    public string NextButtonText => navigator.CanMoveNext
        ? $"{navigator.Steps[navigator.CurrentIndex + 1].Title}へ  →"
        : "最終ステップ";

    public string PreviousButtonText => navigator.CanMovePrevious
        ? $"←  {navigator.Steps[navigator.CurrentIndex - 1].Title}へ戻る"
        : "←  前へ";

    private WorkflowStepPresentation CurrentPresentation => stepPresentations[navigator.CurrentIndex];

    private StepContent CurrentContent => ContentFor(navigator.CurrentStep);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        navigator.CurrentStepChanged -= HandleCurrentStepChanged;
        InputViewModel.PropertyChanged -= HandleInputViewModelPropertyChanged;
        designViewModel.PropertyChanged -= HandleDesignViewModelPropertyChanged;
        executionViewModel.PropertyChanged -= HandleExecutionViewModelPropertyChanged;
        executionViewModel.RunCompleted -= HandleRunCompleted;
        executionViewModel.CheckpointInputRequested -= HandleCheckpointInputRequestedAsync;
        settingsViewModel.PropertyChanged -= HandleSettingsViewModelPropertyChanged;
        settingsViewModel.CloseRequested -= HandleSettingsCloseRequested;
        settingsViewModel.Dispose();
        executionViewModel.Dispose();
        resultsOutputViewModel.Dispose();
    }

    public void OpenSettings() => OpenSettings(settingsViewModel.SelectedCategory);

    public void OpenSettings(SettingsCategory category)
    {
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        if (disposed)
        {
            return;
        }

        // A category change already synchronizes. A same-category open must do so
        // explicitly, before showing editors that may still contain the old input.
        // Capture the source ID BEFORE draft synchronization can replace editors.
        // Common has no question target and must not align independent selections.
        string? sourceQuestionId = category == SettingsCategory.Common ? null
            : SettingsSourceQuestionId(IsSettingsOpen ? settingsViewModel.SelectedCategory : null);
        bool wasSynchronizingDrafts = synchronizingDrafts;
        synchronizingDrafts = true;
        try
        {
            if (settingsViewModel.SelectedCategory == category)
            {
                settingsViewModel.SynchronizeDrafts();
            }
            else
            {
                settingsViewModel.SelectedCategory = category;
            }

            SynchronizeSettingsQuestionSelection(sourceQuestionId, category);
        }
        finally
        {
            synchronizingDrafts = wasSynchronizingDrafts;
        }

        RefreshNextDraftSummary(InputViewModel.DefinitionDraft);
        SetSettingsOpen(true);
    }

    public void CloseSettings() => CloseSettings(synchronizeDrafts: true);

    private string? SettingsSourceQuestionId(SettingsCategory? sourceCategory) => sourceCategory switch
    {
        SettingsCategory.Mapping => InputViewModel.SelectedQuestion?.Id,
        SettingsCategory.Evaluation or SettingsCategory.Special or SettingsCategory.ImportedPrompts =>
            designViewModel.SelectedQuestion?.Id,
        _ => CurrentStep switch
        {
            WorkflowStep.Input => InputViewModel.SelectedQuestion?.Id,
            WorkflowStep.Design => designViewModel.SelectedQuestion?.Id,
            _ => null,
        },
    };

    private void SynchronizeSettingsQuestionSelection(string? sourceQuestionId, SettingsCategory targetCategory)
    {
        if (sourceQuestionId is null || targetCategory == SettingsCategory.Common
            || InputViewModel.IsBusy || settingsViewModel.IsApplying)
        {
            return;
        }

        // Match identities in each existing VM; never copy an editor, choose by
        // display name/index, or clear a selection when an ID no longer exists.
        InputQuestionMappingViewModel? inputQuestion = InputViewModel.Questions
            .FirstOrDefault(question => question.Id == sourceQuestionId);
        QuestionDesignItemViewModel? designQuestion = designViewModel.Questions
            .FirstOrDefault(question => question.Id == sourceQuestionId);
        if (inputQuestion is not null && !ReferenceEquals(InputViewModel.SelectedQuestion, inputQuestion))
        {
            InputViewModel.SelectedQuestion = inputQuestion;
        }

        if (designQuestion is not null && !ReferenceEquals(designViewModel.SelectedQuestion, designQuestion))
        {
            designViewModel.SelectedQuestion = designQuestion;
        }
    }

    private bool CanNavigate(object? parameter) =>
        parameter is WorkflowStep step && navigator.CanNavigateTo(step);

    private bool CanOpenSettings(object? parameter)
    {
        if (disposed)
        {
            return false;
        }

        return parameter is null
            || parameter is SettingsCategory category && Enum.IsDefined(category);
    }

    private void ExecuteNavigate(object? parameter)
    {
        if (parameter is WorkflowStep step)
        {
            CloseSettings(synchronizeDrafts: true);
            navigator.NavigateTo(step);
        }
    }

    private void ExecuteMoveNext()
    {
        CloseSettings(synchronizeDrafts: true);
        navigator.MoveNext();
    }

    private void ExecuteMovePrevious()
    {
        CloseSettings(synchronizeDrafts: true);
        navigator.MovePrevious();
    }

    private void ExecuteOpenSettings(object? parameter)
    {
        if (parameter is SettingsCategory category)
        {
            OpenSettings(category);
        }
        else if (parameter is null)
        {
            OpenSettings();
        }
    }

    private void HandleCurrentStepChanged(object? sender, EventArgs e)
    {
        if (disposed)
        {
            return;
        }

        RefreshExecutionConfiguration();
        stepPresentations = BuildStepPresentations();
        OnPropertyChanged(nameof(DesignViewModel));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(CurrentEditorViewModel));
        OnPropertyChanged(nameof(HasEditorContent));
        OnPropertyChanged(nameof(HasPlaceholderContent));
        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(InputStep));
        OnPropertyChanged(nameof(DesignStep));
        OnPropertyChanged(nameof(ExecutionStep));
        OnPropertyChanged(nameof(ResultsStep));
        OnPropertyChanged(nameof(CurrentStepKicker));
        OnPropertyChanged(nameof(CurrentStepTitle));
        OnPropertyChanged(nameof(CurrentStepDescription));
        OnPropertyChanged(nameof(CurrentDetailOne));
        OnPropertyChanged(nameof(CurrentDetailTwo));
        OnPropertyChanged(nameof(CurrentDetailThree));
        OnPropertyChanged(nameof(CurrentStepStatusText));
        OnPropertyChanged(nameof(NavigationProgressText));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(PreviousButtonText));
        navigateCommand.RaiseCanExecuteChanged();
        nextCommand.RaiseCanExecuteChanged();
        previousCommand.RaiseCanExecuteChanged();
        openSettingsCommand.RaiseCanExecuteChanged();
    }

    private void HandleRunCompleted(object? sender, ExecutionRunCompletedEventArgs e)
    {
        if (disposed)
        {
            return;
        }

        resultsOutputViewModel.Load(e.Context);
        if (!IsSettingsOpen && navigator.CurrentStep == WorkflowStep.Execution
            && e.Context.Summary.StatusCode == QuantificationRunStatusCodes.Success)
        {
            navigator.MoveNext();
        }
    }

    private void HandleInputViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed || synchronizingDrafts)
        {
            return;
        }

        // Loading publishes metadata before its draft is complete. Synchronize only
        // after IsBusy clears, including an already-open Common settings page.
        if (e.PropertyName == nameof(InputViewModel.IsBusy) && !InputViewModel.IsBusy)
        {
            RefreshExecutionConfiguration();
            return;
        }

        // FilePath is published before the old loaded state is cleared. Wait for
        // the matching cleared/completed draft instead of pairing a new path with old metadata.
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(InputViewModel.DefinitionDraft)
                or nameof(InputViewModel.Metadata)
                or nameof(InputViewModel.HasLoadedWorkbook))
        {
            RefreshExecutionConfiguration(InputViewModel.DefinitionDraft);
        }
    }

    private void HandleDesignViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(QuantificationDesignViewModel.Draft))
        {
            RefreshExecutionConfiguration(designViewModel.Draft);
        }
    }

    private void HandleExecutionViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // RunCompleted is raised while IsRunning is still true. A failed/cancelled
        // run can leave Execution visible after settings were closed during the run.
        if (e.PropertyName == nameof(ExecutionViewModel.IsRunning) && !executionViewModel.IsRunning)
        {
            RefreshExecutionConfiguration();
        }
    }

    private Task<bool> HandleCheckpointInputRequestedAsync(
        string inputPath,
        CancellationToken cancellationToken) =>
        InputViewModel.TryLoadCheckpointInputAsync(inputPath, cancellationToken);

    private void HandleSettingsCloseRequested(object? sender, EventArgs e) =>
        CloseSettings(synchronizeDrafts: false);

    private void HandleSettingsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedCategory))
        {
            SettingsCategory sourceCategory = observedSettingsCategory;
            observedSettingsCategory = settingsViewModel.SelectedCategory;
            // T17's category buttons use Settings.SelectCategoryCommand directly.
            // Its synchronization preserves surviving source IDs; OpenSettings
            // handles its own pre-sync capture and must not run this path twice.
            if (IsSettingsOpen && !synchronizingDrafts)
            {
                SynchronizeSettingsQuestionSelection(
                    SettingsSourceQuestionId(sourceCategory), observedSettingsCategory);
            }
        }

        // Navigation may close Settings while applying awaits the input loader.
        // Its final notification follows both editor commits; earlier changes are gated.
        // Startup loads/saves also notify IsApplying=false, without an editor sync boundary.
        if (e.PropertyName == nameof(SettingsViewModel.IsApplying))
        {
            bool wasApplying = observedSettingsApplying;
            observedSettingsApplying = settingsViewModel.IsApplying;
            if (wasApplying && !observedSettingsApplying)
            {
                // Apply owns both commits (or neither). Its completion is not an
                // edit boundary, even when navigation closed Settings during the await.
                RefreshExecutionConfiguration(synchronizeEditors: false);
            }
        }
    }

    private void CloseSettings(bool synchronizeDrafts)
    {
        if (disposed || !IsSettingsOpen)
        {
            return;
        }

        if (synchronizeDrafts)
        {
            settingsViewModel.SynchronizeDrafts();
        }

        SetSettingsOpen(false);
        if (navigator.CurrentStep == WorkflowStep.Execution)
        {
            RefreshExecutionConfiguration();
        }
    }

    private void RefreshNextDraftSummary(QuantificationDefinition latestDraft)
    {
        if (disposed || synchronizingDrafts
            || InputViewModel.IsBusy || settingsViewModel.IsApplying)
        {
            return;
        }

        latestObservedDraft = latestDraft;
        executionViewModel.UpdateNextDraftSummary(
            InputViewModel.HasLoadedWorkbook ? latestDraft : null,
            InputViewModel.HasLoadedWorkbook ? InputViewModel.FilePath : null);
    }

    private void RefreshExecutionConfiguration(
        QuantificationDefinition? latestDraft = null,
        bool synchronizeEditors = true)
    {
        if (disposed || synchronizingDrafts)
        {
            return;
        }

        // Remember deferred presentation only; Settings still owns peer synchronization.
        latestObservedDraft = latestDraft ?? latestObservedDraft;
        if (InputViewModel.IsBusy || settingsViewModel.IsApplying)
        {
            return;
        }

        bool previewOnly = IsSettingsOpen || navigator.CurrentStep != WorkflowStep.Execution;
        if (previewOnly && latestDraft is not null)
        {
            // Ordinary edits keep their peer untouched until the existing sync boundary.
            RefreshNextDraftSummary(latestDraft);
            return;
        }

        synchronizingDrafts = true;
        try
        {
            // Settings alone chooses the latest editor. Keep its peer notifications
            // inside this scope so they cannot recursively Configure a stale draft.
            if (synchronizeEditors)
            {
                settingsViewModel.SynchronizeDrafts();
                latestObservedDraft = InputViewModel.DefinitionDraft;
            }

            QuantificationDefinition executionDraft = latestObservedDraft ?? InputViewModel.DefinitionDraft;
            if (previewOnly || executionViewModel.IsRunning)
            {
                executionViewModel.UpdateNextDraftSummary(
                    InputViewModel.HasLoadedWorkbook ? executionDraft : null,
                    InputViewModel.HasLoadedWorkbook ? InputViewModel.FilePath : null);
            }
            else if (InputViewModel.HasLoadedWorkbook && InputViewModel.Metadata is { } metadata)
            {
                executionViewModel.Configure(
                    executionDraft,
                    metadata,
                    InputViewModel.FilePath);
            }
            else
            {
                executionViewModel.ClearConfiguration();
            }
        }
        finally
        {
            synchronizingDrafts = false;
        }
    }

    private void SetSettingsOpen(bool value)
    {
        if (isSettingsOpen == value)
        {
            return;
        }

        isSettingsOpen = value;
        OnPropertyChanged(nameof(IsSettingsOpen));
        OnPropertyChanged(nameof(CurrentEditorViewModel));
        OnPropertyChanged(nameof(HasEditorContent));
        OnPropertyChanged(nameof(HasPlaceholderContent));
    }

    private ImmutableArray<WorkflowStepPresentation> BuildStepPresentations() =>
        [
            .. navigator.Steps.Select(definition =>
            {
                WorkflowStepState state = navigator.GetState(definition.Step);
                return new WorkflowStepPresentation(
                    definition.Step,
                    definition.Number,
                    definition.Number.ToString("00", CultureInfo.InvariantCulture),
                    definition.Title,
                    definition.EnglishTitle,
                    state switch
                    {
                        WorkflowStepState.Visited => "◉",
                        WorkflowStepState.Current => "●",
                        WorkflowStepState.Upcoming => "○",
                        _ => throw new InvalidOperationException("Unknown workflow step state."),
                    },
                    state switch
                    {
                        WorkflowStepState.Visited => "設定済み",
                        WorkflowStepState.Current => "現在・選択中",
                        WorkflowStepState.Upcoming => "未着手",
                        _ => throw new InvalidOperationException("Unknown workflow step state."),
                    },
                    state == WorkflowStepState.Current,
                    state == WorkflowStepState.Visited,
                    state == WorkflowStepState.Upcoming);
            }),
        ];

    private static StepContent ContentFor(WorkflowStep step) => step switch
    {
        WorkflowStep.Input => new(
            "入力を準備する",
            "評価対象の Excel と範囲を選び、質問ごとの回答列を結び付けます。",
            "Excel ファイルとシートを選択",
            "見出し行・回答行を確認",
            "主回答列と補助列をマッピング"),
        WorkflowStep.Design => new(
            "定量化を設計する",
            "Knowledge / Custom の評価方法、評価項目、range、配点を組み立てます。",
            "質問と評価方法を構成",
            "Prompt・知識ポイントを編集",
            "range と重みを確認"),
        WorkflowStep.Execution => new(
            "定量化を実行する",
            "Copilot の状態とmodelを確認し、snapshotに固定した処理の進捗、cancel、技術的エラーを扱います。",
            "ログインと model を確認",
            "評価単位と進捗を表示",
            "cancel と技術的失敗を安全に処理"),
        WorkflowStep.Results => new(
            "結果を確認して出力する",
            "AI raw、任意override、Excel計算previewを確認し、入力を変えず別ファイルへ出力します。",
            "raw 値と式 preview を確認",
            "必要な値だけ任意で上書き",
            "入力とは別の Excel へ出力"),
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Unknown workflow step."),
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record StepContent(
        string Title,
        string Description,
        string DetailOne,
        string DetailTwo,
        string DetailThree);

    private sealed class DelegateCommand(
        Action<object?> execute,
        Predicate<object?> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute(parameter);

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                execute(parameter);
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}