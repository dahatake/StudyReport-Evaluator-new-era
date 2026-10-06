using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Scoring;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.App.ViewModels;

public abstract class UiObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(
        ref T storage,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected void OnPropertiesChanged(params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }
}

internal sealed class ViewModelCommand(
    Action<object?> execute,
    Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            execute(parameter);
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public interface IInputWorkbookLoader
{
    Task<InputWorkbookLoadResult> LoadAsync(
        string filePath,
        uint headerRow,
        CancellationToken cancellationToken = default);
}

public sealed class InputWorkbookLoadResult
{
    public InputWorkbookLoadResult(
        InputSnapshot snapshot,
        WorkbookMetadata metadata,
        ColumnMappingSuggestionResult suggestions)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Suggestions = suggestions ?? throw new ArgumentNullException(nameof(suggestions));
    }

    public InputSnapshot Snapshot { get; }

    public WorkbookMetadata Metadata { get; }

    public ColumnMappingSuggestionResult Suggestions { get; }

    public override string ToString() =>
        $"{nameof(InputWorkbookLoadResult)} {{ Content = <redacted> }}";
}

public sealed class InputWorkbookLoadException : Exception
{
    public InputWorkbookLoadException(string code)
        : base("The workbook could not be loaded through the technical input boundary.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}

public sealed class InputWorkbookLoader : IInputWorkbookLoader
{
    private readonly FileFormatClassifier classifier;
    private readonly InputSnapshotService snapshotService;
    private readonly WorkbookMetadataReader metadataReader;
    private readonly ColumnMappingSuggester mappingSuggester;

    public InputWorkbookLoader()
        : this(
            new FileFormatClassifier(),
            new InputSnapshotService(),
            new WorkbookMetadataReader(),
            new ColumnMappingSuggester())
    {
    }

    public InputWorkbookLoader(
        FileFormatClassifier classifier,
        InputSnapshotService snapshotService,
        WorkbookMetadataReader metadataReader,
        ColumnMappingSuggester mappingSuggester)
    {
        this.classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        this.snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        this.metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        this.mappingSuggester = mappingSuggester ?? throw new ArgumentNullException(nameof(mappingSuggester));
    }

    public Task<InputWorkbookLoadResult> LoadAsync(
        string filePath,
        uint headerRow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (headerRow is 0 or > WorkbookMetadataReader.MaxWorksheetRows)
        {
            throw new ArgumentOutOfRangeException(nameof(headerRow));
        }

        return Task.Run(
            () => Load(filePath, headerRow, cancellationToken),
            cancellationToken);
    }

    private InputWorkbookLoadResult Load(
        string filePath,
        uint headerRow,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileFormatClassificationResult classification = classifier.Classify(filePath);
        if (!classification.IsAccepted)
        {
            throw new InputWorkbookLoadException(classification.Classification.ToString());
        }

        cancellationToken.ThrowIfCancellationRequested();
        InputSnapshot snapshot = snapshotService.Capture(filePath);
        cancellationToken.ThrowIfCancellationRequested();
        WorkbookMetadata metadata = metadataReader.Read(filePath, headerRow);
        cancellationToken.ThrowIfCancellationRequested();
        InputSnapshotComparison comparison = snapshotService.Recheck(filePath, snapshot);
        if (!comparison.IsMatch)
        {
            throw new InputWorkbookLoadException("INPUT_CHANGED");
        }

        ColumnMappingSuggestionResult suggestions = mappingSuggester.Suggest(metadata);
        return new InputWorkbookLoadResult(snapshot, metadata, suggestions);
    }
}

public sealed class InputValidationError
{
    public InputValidationError(
        string code,
        string nodeId,
        string field,
        string message)
    {
        Code = code;
        NodeId = nodeId;
        Field = field;
        Message = message;
    }

    public string Code { get; }

    public string NodeId { get; }

    public string Field { get; }

    public string Message { get; }

    public override string ToString() =>
        $"{nameof(InputValidationError)} {{ Code = {Code}, Content = <redacted> }}";
}

public sealed class WorksheetChoiceViewModel
{
    internal WorksheetChoiceViewModel(WorksheetMetadata worksheet)
    {
        Name = worksheet.Name;
        StateText = worksheet.State switch
        {
            WorkbookSheetState.Visible => "表示",
            WorkbookSheetState.Hidden => "非表示",
            WorkbookSheetState.VeryHidden => "強制非表示",
            _ => "不明",
        };
        Dimension = worksheet.DimensionReference;
    }

    public string Name { get; }

    public string StateText { get; }

    public string Dimension { get; }

    public string DisplayText => $"{Name} · {Dimension} · {StateText}";

    public override string ToString() =>
        $"{nameof(WorksheetChoiceViewModel)} {{ Content = <redacted> }}";
}

public sealed class SourceColumnOption
{
    internal SourceColumnOption(string columnName, string headerText)
    {
        ColumnName = columnName;
        HeaderText = headerText;
    }

    public string ColumnName { get; }

    public string HeaderText { get; }

    public string DisplayText => string.IsNullOrWhiteSpace(HeaderText)
        ? ColumnName
        : $"{ColumnName} · {HeaderText}";

    public override string ToString() =>
        $"{nameof(SourceColumnOption)} {{ Content = <redacted> }}";
}

public sealed class MappingSuggestionViewModel
{
    internal MappingSuggestionViewModel(
        ColumnMappingCandidate candidate,
        string headerText)
    {
        ColumnName = candidate.ColumnName;
        HeaderText = headerText;
        IsPrimaryCandidate = candidate.IsPrimaryCandidate;
        IsStudentPromptPrimaryCandidate = candidate.IsStudentPromptPrimaryCandidate;
        IsSupportingCandidate = candidate.IsSupportingCandidate;
        SuggestedSupportingColumns = string.Join(", ", candidate.SuggestedSupportingColumns);
    }

    public string ColumnName { get; }

    public string HeaderText { get; }

    public bool IsPrimaryCandidate { get; }

    public bool IsStudentPromptPrimaryCandidate { get; }

    public bool IsSupportingCandidate { get; }

    public string SuggestedSupportingColumns { get; }

    public string RoleText
    {
        get
        {
            List<string> roles = [];
            if (IsPrimaryCandidate)
            {
                roles.Add("主回答候補");
            }

            if (IsStudentPromptPrimaryCandidate)
            {
                roles.Add("学生 Prompt 候補");
            }

            if (IsSupportingCandidate)
            {
                roles.Add("補助列候補");
            }

            return string.Join(" / ", roles);
        }
    }

    public string SupportText => string.IsNullOrEmpty(SuggestedSupportingColumns)
        ? "補助列の自動提案なし"
        : $"補助列: {SuggestedSupportingColumns}";

    public override string ToString() =>
        $"{nameof(MappingSuggestionViewModel)} {{ Content = <redacted> }}";
}

public sealed class SupportingColumnSelectionViewModel : UiObservableObject
{
    private readonly InputQuestionMappingViewModel owner;
    private bool isSelected;

    internal SupportingColumnSelectionViewModel(
        InputQuestionMappingViewModel owner,
        SourceColumnOption option,
        bool selected)
    {
        this.owner = owner;
        ColumnName = option.ColumnName;
        DisplayText = option.DisplayText;
        isSelected = selected;
    }

    public string ColumnName { get; }

    public string DisplayText { get; }

    public bool CanSelect => !string.Equals(
        ColumnName,
        owner.PrimarySourceColumn,
        StringComparison.OrdinalIgnoreCase);

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (SetProperty(ref isSelected, value))
            {
                owner.SetSupportingColumn(ColumnName, value);
            }
        }
    }

    internal void RefreshAvailability() => OnPropertyChanged(nameof(CanSelect));

    public override string ToString() =>
        $"{nameof(SupportingColumnSelectionViewModel)} {{ Content = <redacted> }}";
}

public sealed class InputQuestionMappingViewModel : UiObservableObject
{
    private readonly InputViewModel owner;
    private QuestionDefinition definition;
    private readonly ViewModelCommand duplicateCommand;
    private readonly ViewModelCommand moveUpCommand;
    private readonly ViewModelCommand moveDownCommand;
    private readonly ViewModelCommand deleteCommand;

    internal InputQuestionMappingViewModel(
        InputViewModel owner,
        QuestionDefinition definition)
    {
        this.owner = owner;
        this.definition = definition;
        SupportingColumns = [];
        duplicateCommand = new ViewModelCommand(_ => owner.DuplicateQuestion(Id));
        moveUpCommand = new ViewModelCommand(_ => owner.MoveQuestionUp(Id), _ => owner.CanMoveQuestionUp(Id));
        moveDownCommand = new ViewModelCommand(_ => owner.MoveQuestionDown(Id), _ => owner.CanMoveQuestionDown(Id));
        deleteCommand = new ViewModelCommand(_ => owner.DeleteQuestion(Id));
        SynchronizeSupportingColumns();
    }

    public string Id => definition.Id;

    public string DisplayName
    {
        get => definition.DisplayName;
        set => owner.UpdateQuestion(Id, question => question with { DisplayName = value ?? string.Empty });
    }

    public string QuestionText
    {
        get => definition.QuestionText;
        set => owner.UpdateQuestion(Id, question => question with { QuestionText = value ?? string.Empty });
    }

    public string PrimarySourceColumn
    {
        get => definition.PrimarySourceColumn;
        set => owner.SetPrimaryColumn(Id, value ?? string.Empty);
    }

    public bool Enabled
    {
        get => definition.Enabled;
        set => owner.UpdateQuestion(Id, question => question with { Enabled = value });
    }

    public decimal Weight
    {
        get => definition.Points;
        set => owner.UpdateQuestion(Id, question => question with { Points = value });
    }

    public ReadOnlyObservableCollection<string> AvailableColumnNames => owner.AvailableColumnNames;

    public ObservableCollection<SupportingColumnSelectionViewModel> SupportingColumns { get; }

    public string SupportingSummary => definition.SupportingSourceColumns.IsEmpty
        ? "補助列なし"
        : string.Join(", ", definition.SupportingSourceColumns);

    public string NameAutomationId => $"InputQuestion-{Id}-Name";

    public string PrimaryAutomationId => $"InputQuestion-{Id}-Primary";

    public string SupportingAutomationId => $"InputQuestion-{Id}-Supporting";

    public ICommand DuplicateCommand => duplicateCommand;

    public ICommand MoveUpCommand => moveUpCommand;

    public ICommand MoveDownCommand => moveDownCommand;

    public ICommand DeleteCommand => deleteCommand;

    public void SetSupportingColumn(string columnName, bool selected) =>
        owner.SetSupportingColumn(Id, columnName, selected);

    internal void Synchronize(QuestionDefinition updated)
    {
        definition = updated;
        SynchronizeSupportingColumns();
        OnPropertiesChanged(
            nameof(Id),
            nameof(DisplayName),
            nameof(QuestionText),
            nameof(PrimarySourceColumn),
            nameof(Enabled),
            nameof(Weight),
            nameof(SupportingSummary),
            nameof(NameAutomationId),
            nameof(PrimaryAutomationId),
            nameof(SupportingAutomationId));
        RefreshCommands();
    }

    internal void RefreshCommands()
    {
        moveUpCommand.RaiseCanExecuteChanged();
        moveDownCommand.RaiseCanExecuteChanged();
        foreach (SupportingColumnSelectionViewModel supportingColumn in SupportingColumns)
        {
            supportingColumn.RefreshAvailability();
        }
    }

    private void SynchronizeSupportingColumns()
    {
        SupportingColumns.Clear();
        HashSet<string> selected = definition.SupportingSourceColumns
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (SourceColumnOption option in owner.AvailableColumns)
        {
            SupportingColumns.Add(new SupportingColumnSelectionViewModel(
                this,
                option,
                selected.Contains(option.ColumnName)));
        }
    }

    public override string ToString() =>
        $"{nameof(InputQuestionMappingViewModel)} {{ Id = {Id}, Content = <redacted> }}";
}

public sealed class InputViewModel : UiObservableObject
{
    private static readonly IReadOnlyList<int> ClosedHeaderRowOptions = Array.AsReadOnly([1, 2]);

    private readonly IInputWorkbookLoader loader;
    private readonly ColumnMappingValidator mappingValidator = new();
    private readonly QuantificationDefinitionValidator definitionValidator = new();
    private readonly ObservableCollection<WorksheetChoiceViewModel> worksheetItems = [];
    private readonly ObservableCollection<SourceColumnOption> availableColumnItems = [];
    private readonly ObservableCollection<string> availableColumnNameItems = [];
    private readonly ObservableCollection<MappingSuggestionViewModel> suggestionItems = [];
    private readonly ObservableCollection<InputQuestionMappingViewModel> questionItems = [];
    private readonly ObservableCollection<InputQuestionMappingViewModel> visibleQuestionItems = [];
    private readonly ObservableCollection<InputValidationError> validationErrorItems = [];
    private readonly ViewModelCommand loadFileCommand;
    private readonly ViewModelCommand refreshHeaderCommand;
    private readonly ViewModelCommand applySuggestionsCommand;
    private readonly ViewModelCommand addQuestionCommand;
    private readonly ViewModelCommand previousPageCommand;
    private readonly ViewModelCommand nextPageCommand;
    private QuantificationDefinition definitionDraft;
    private InputQuestionMappingViewModel? selectedQuestion;
    private int pageSize = 4;
    private int pageIndex;
    private bool updatingPresentation;
    private WorkbookMetadata? metadata;
    private InputSnapshot? snapshot;
    private ColumnMappingSuggestionResult? suggestions;
    private InputValidationError? loadError;
    private InputValidationError? savedDefinitionApplicationError;
    private string filePath = string.Empty;
    private string selectedSheet = string.Empty;
    private int headerRow = 1;
    private int firstDataRow = 2;
    private int lastDataRow = 2;
    private bool isBusy;
    private bool isUsingSuggestedMapping;
    private bool launchAutoLoadPending;
    private bool updatingInputChoices;
    private long loadSequence;

    public InputViewModel()
        : this(new InputWorkbookLoader())
    {
    }

    public InputViewModel(IInputWorkbookLoader loader)
    {
        this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        Worksheets = new ReadOnlyObservableCollection<WorksheetChoiceViewModel>(worksheetItems);
        AvailableColumns = new ReadOnlyObservableCollection<SourceColumnOption>(availableColumnItems);
        AvailableColumnNames = new ReadOnlyObservableCollection<string>(availableColumnNameItems);
        MappingSuggestions = new ReadOnlyObservableCollection<MappingSuggestionViewModel>(suggestionItems);
        Questions = new ReadOnlyObservableCollection<InputQuestionMappingViewModel>(questionItems);
        VisibleQuestions = new ReadOnlyObservableCollection<InputQuestionMappingViewModel>(visibleQuestionItems);
        ValidationErrors = new ReadOnlyObservableCollection<InputValidationError>(validationErrorItems);
        definitionDraft = CreateEmptyDefinition();
        loadFileCommand = new ViewModelCommand(
            _ => _ = LoadAsync(),
            _ => !IsBusy && !string.IsNullOrWhiteSpace(FilePath));
        refreshHeaderCommand = new ViewModelCommand(
            _ => _ = RefreshHeaderAsync(),
            _ => !IsBusy && HasLoadedWorkbook && HeaderRow is 1 or 2);
        applySuggestionsCommand = new ViewModelCommand(
            _ => ApplySuggestedMapping(),
            _ => !IsBusy && CurrentWorksheetSuggestion is not null);
        addQuestionCommand = new ViewModelCommand(
            _ => AddQuestion(),
            _ => availableColumnNameItems.Count > 0);
        previousPageCommand = new ViewModelCommand(_ => PageIndex--, _ => pageIndex > 0);
        nextPageCommand = new ViewModelCommand(_ => PageIndex++, _ => pageIndex < LastPageIndex);
        Revalidate();
    }

    public ReadOnlyObservableCollection<WorksheetChoiceViewModel> Worksheets { get; }

    public ReadOnlyObservableCollection<SourceColumnOption> AvailableColumns { get; }

    public ReadOnlyObservableCollection<string> AvailableColumnNames { get; }

    public ReadOnlyObservableCollection<MappingSuggestionViewModel> MappingSuggestions { get; }

    public ReadOnlyObservableCollection<InputQuestionMappingViewModel> Questions { get; }

    /// <summary>The current page contains the original mapping editors, not copies.</summary>
    public ReadOnlyObservableCollection<InputQuestionMappingViewModel> VisibleQuestions { get; }

    /// <summary>The logical editing target, independent of a separately browsed page.</summary>
    public InputQuestionMappingViewModel? SelectedQuestion
    {
        get => selectedQuestion;
        set
        {
            // Collection changes can write back null or an old SelectedItem.
            // Keep this guard separate from T04's input/primary-column guard.
            if (updatingPresentation)
            {
                return;
            }

            int selectedIndex = value is null ? -1 : questionItems.IndexOf(value);
            if (value is not null && selectedIndex < 0)
            {
                return;
            }

            int nextPageIndex = selectedIndex >= 0 ? selectedIndex / pageSize : pageIndex;
            if (!ReferenceEquals(selectedQuestion, value) || pageIndex != nextPageIndex)
            {
                UpdatePresentation(value, nextPageIndex, pageSize);
            }
        }
    }

    /// <summary>A positive layout capacity; four is provisional until the view measures its space.</summary>
    public int PageSize
    {
        get => pageSize;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Page size must be positive.");
            }

            if (pageSize != value)
            {
                int selectedIndex = selectedQuestion is null ? -1 : questionItems.IndexOf(selectedQuestion);
                UpdatePresentation(selectedQuestion, selectedIndex >= 0 ? selectedIndex / value : pageIndex, value);
            }
        }
    }

    /// <summary>Zero-based and clamped to available pages. Browsing does not change selection.</summary>
    public int PageIndex
    {
        get => pageIndex;
        set
        {
            int nextPageIndex = Math.Clamp(value, 0, LastPageIndex);
            if (pageIndex != nextPageIndex)
            {
                UpdatePresentation(selectedQuestion, nextPageIndex, pageSize);
            }
        }
    }

    public string PageSummary
    {
        get
        {
            if (questionItems.Count == 0)
            {
                return "設問はありません（0 件）";
            }

            var page = CalculateQuestionPage(questionItems.Count, pageIndex, pageSize);
            return string.Format(CultureInfo.InvariantCulture,
                "{0:N0}–{1:N0} / {2:N0} 件", page.Start + 1, page.Start + page.Count, questionItems.Count);
        }
    }

    public ReadOnlyObservableCollection<InputValidationError> ValidationErrors { get; }

    public IReadOnlyList<int> HeaderRowOptions => ClosedHeaderRowOptions;

    public QuantificationDefinition DefinitionDraft => definitionDraft;

    public WorkbookMetadata? Metadata => metadata;

    public InputSnapshot? Snapshot => snapshot;

    public bool HasLoadedWorkbook => metadata is not null && snapshot is not null;

    public InputValidationError? SavedDefinitionApplicationError
    {
        get => savedDefinitionApplicationError;
        private set => SetProperty(ref savedDefinitionApplicationError, value);
    }

    public string FilePath
    {
        get => filePath;
        set => SetFilePath(value);
    }

    public string SelectedSheet
    {
        get => selectedSheet;
        set
        {
            string next = value ?? string.Empty;
            if (!SetProperty(ref selectedSheet, next))
            {
                return;
            }

            SupersedeActiveLoad();
            OnPropertyChanged(nameof(SelectedWorksheetChoice));
            ApplyWorksheetSelection(applySuggestion: true);
        }
    }

    public WorksheetChoiceViewModel? SelectedWorksheetChoice
    {
        get => worksheetItems.FirstOrDefault(item => string.Equals(
            item.Name,
            SelectedSheet,
            StringComparison.OrdinalIgnoreCase));
        set
        {
            if (!updatingInputChoices)
            {
                SelectedSheet = value?.Name ?? string.Empty;
            }
        }
    }

    public int HeaderRow
    {
        get => headerRow;
        set
        {
            if (value is not 1 and not 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "The question-text row must be 1 or 2.");
            }

            if (SetProperty(ref headerRow, value))
            {
                SupersedeActiveLoad();
                isUsingSuggestedMapping = false;
                RebuildRootDraft();
            }
        }
    }

    public int FirstDataRow
    {
        get => firstDataRow;
        set
        {
            if (SetProperty(ref firstDataRow, value))
            {
                SupersedeActiveLoad();
                isUsingSuggestedMapping = false;
                RebuildRootDraft();
            }
        }
    }

    public int LastDataRow
    {
        get => lastDataRow;
        set
        {
            if (SetProperty(ref lastDataRow, value))
            {
                SupersedeActiveLoad();
                isUsingSuggestedMapping = false;
                RebuildRootDraft();
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(StatusText));
                RaiseCommandStates();
            }
        }
    }

    public bool IsUsingSuggestedMapping
    {
        get => isUsingSuggestedMapping;
        private set
        {
            if (SetProperty(ref isUsingSuggestedMapping, value))
            {
                OnPropertyChanged(nameof(SuggestionStatusText));
            }
        }
    }

    public bool CanContinue => HasLoadedWorkbook && !IsBusy && validationErrorItems.Count == 0;

    public bool HasTechnicalErrors => validationErrorItems.Count > 0;

    public bool IsTechnicallyValid => !HasTechnicalErrors;

    public string ValidationSummary => validationErrorItems.Count == 0
        ? "技術検証を通過しました。定量化設計へ進めます。"
        : $"技術的な問題が {validationErrorItems.Count.ToString(CultureInfo.InvariantCulture)} 件あります。入力を修正してください。";

    public string StatusText => IsBusy
        ? "Excel を read-only で確認しています…"
        : HasLoadedWorkbook
            ? "read-only 読込と入力 snapshot の取得が完了しました。"
            : "標準 .xlsx を選択してください。";

    public string SuggestionStatusText => IsUsingSuggestedMapping
        ? "X-02 の候補を適用中です。すべて上書きできます。"
        : "手動 mapping を使用中です。必要なら候補へ戻せます。";

    public string WorkbookSummary => metadata is null
        ? "Workbook metadata はまだありません。"
        : $"{metadata.Worksheets.Count.ToString(CultureInfo.InvariantCulture)} sheets · {metadata.PackagePartCount.ToString(CultureInfo.InvariantCulture)} package parts";

    public string InputSummary
    {
        get
        {
            if (metadata is null || snapshot is null)
            {
                return "入力は未読込です。";
            }

            string questionSummary = string.Format(CultureInfo.InvariantCulture,
                "設問 {0:N0} / {1:N0} 件有効",
                definitionDraft.Questions.Count(question => question.Enabled), definitionDraft.Questions.Length);
            WorksheetMetadata? worksheet = metadata.Worksheets.FirstOrDefault(item => string.Equals(
                item.Name, definitionDraft.SourceSheet, StringComparison.OrdinalIgnoreCase));
            if (worksheet is null)
            {
                return $"回答 sheet を選択してください。 · {questionSummary}";
            }

            string headerSummary = string.Format(CultureInfo.InvariantCulture, "質問行 {0}", definitionDraft.HeaderRow);
            if (metadata.HeaderRowNumber != definitionDraft.HeaderRow)
            {
                headerSummary += string.Format(CultureInfo.InvariantCulture,
                    "（読込済み {0}・再読込が必要）", metadata.HeaderRowNumber);
            }

            int first = definitionDraft.FirstDataRow;
            int last = definitionDraft.LastDataRow;
            string rowCount = first > definitionDraft.HeaderRow
                && first >= worksheet.FirstRowIndex
                && last >= first
                && last <= worksheet.LastRowIndex
                    ? string.Format(CultureInfo.InvariantCulture, "{0:N0} 行", (long)last - first + 1)
                    : "範囲を確認してください";
            return string.Format(CultureInfo.InvariantCulture,
                "{0} · {1} · 回答行 {2:N0}–{3:N0}（{4}） · {5}",
                worksheet.Name, headerSummary, first, last, rowCount, questionSummary);
        }
    }

    public ICommand LoadFileCommand => loadFileCommand;

    public ICommand RefreshHeaderCommand => refreshHeaderCommand;

    public ICommand ApplySuggestionsCommand => applySuggestionsCommand;

    public ICommand AddQuestionCommand => addQuestionCommand;

    public ICommand PreviousPageCommand => previousPageCommand;

    public ICommand NextPageCommand => nextPageCommand;

    private int LastPageIndex => CalculateQuestionPage(questionItems.Count, int.MaxValue, pageSize).PageIndex;

    private WorksheetMappingSuggestion? CurrentWorksheetSuggestion => suggestions?.WorksheetSuggestions
        .FirstOrDefault(item => string.Equals(
            item.WorksheetName,
            SelectedSheet,
            StringComparison.OrdinalIgnoreCase));

    public void SetFilePath(string? path)
    {
        string next = path ?? string.Empty;
        if (SetProperty(ref filePath, next, nameof(FilePath)))
        {
            Interlocked.Increment(ref loadSequence);
            loadError = null;
            ClearLoadedState();
            // Completion observers must not pair the new path with the old workbook.
            IsBusy = false;
            Revalidate();
        }
    }

    public void ApplyLaunchInput(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SetFilePath(path);
        launchAutoLoadPending = true;
    }

    public async Task LoadLaunchInputIfRequestedAsync(
        CancellationToken cancellationToken = default)
    {
        if (!launchAutoLoadPending)
        {
            return;
        }

        launchAutoLoadPending = false;
        await LoadAsync(cancellationToken);
    }

    public async Task SetFilePathAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        SetFilePath(path);
        await LoadAsync(cancellationToken);
    }

    /// <summary>Loads a checkpoint input without replacing the current input until successful.</summary>
    public async Task<bool> TryLoadCheckpointInputAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (IsBusy || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        string normalizedPath = Path.GetFullPath(path);
        QuantificationDefinition originalDraft = definitionDraft;
        bool preserveMapping = HasLoadedWorkbook;
        long sequence = Interlocked.Increment(ref loadSequence);
        IsBusy = true;
        try
        {
            InputWorkbookLoadResult result = await loader.LoadAsync(
                normalizedPath,
                checked((uint)HeaderRow),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (sequence != Volatile.Read(ref loadSequence)
                || !ReferenceEquals(originalDraft, definitionDraft))
            {
                return false;
            }

            if (preserveMapping && !mappingValidator.Validate(result.Metadata, originalDraft).IsValid)
            {
                return false;
            }

            filePath = normalizedPath;
            loadError = null;
            ApplyLoadResult(result, preserveMapping);
            OnPropertyChanged(nameof(FilePath));
            Revalidate();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (InputWorkbookLoadException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException)
        {
            return false;
        }
        finally
        {
            if (sequence == Volatile.Read(ref loadSequence))
            {
                IsBusy = false;
                Revalidate();
            }
        }
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        LoadCoreAsync(preserveMapping: false, cancellationToken);

    private async Task LoadCoreAsync(bool preserveMapping, CancellationToken cancellationToken)
    {
        long sequence = Interlocked.Increment(ref loadSequence);
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            loadError = CreateError(
                "INPUT_FILE_REQUIRED",
                "<input>",
                "FilePath",
                "標準 .xlsx ファイルを指定してください。");
            Revalidate();
            return;
        }

        if (HeaderRow is not 1 and not 2)
        {
            loadError = CreateError(
                "HEADER_ROW_OUT_OF_RANGE",
                "<input>",
                "HeaderRow",
                "質問文の行は 1 または 2 を選択してください。");
            Revalidate();
            return;
        }

        IsBusy = true;
        loadError = null;
        Revalidate();
        try
        {
            InputWorkbookLoadResult result = await loader.LoadAsync(
                FilePath,
                checked((uint)HeaderRow),
                cancellationToken);
            if (sequence != Volatile.Read(ref loadSequence))
            {
                return;
            }

            ApplyLoadResult(result, preserveMapping);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (sequence == Volatile.Read(ref loadSequence))
            {
                loadError = CreateError(
                    "INPUT_LOAD_CANCELLED",
                    "<input>",
                    "FilePath",
                    "Excel の読込は取り消されました。");
                ClearLoadedState();
            }
        }
        catch (InputWorkbookLoadException exception)
        {
            if (sequence == Volatile.Read(ref loadSequence))
            {
                loadError = CreateError(
                    exception.Code,
                    "<input>",
                    "FilePath",
                    ClassificationMessage(exception.Code));
                ClearLoadedState();
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException)
        {
            if (sequence == Volatile.Read(ref loadSequence))
            {
                loadError = CreateError(
                    "INPUT_LOAD_FAILED",
                    "<input>",
                    "FilePath",
                    "Excel を安全に読み込めませんでした。形式、アクセス権、破損の有無を確認してください。");
                ClearLoadedState();
            }
        }
        finally
        {
            if (sequence == Volatile.Read(ref loadSequence))
            {
                IsBusy = false;
                Revalidate();
            }
        }
    }

    public Task RefreshHeaderAsync(CancellationToken cancellationToken = default) =>
        LoadCoreAsync(preserveMapping: true, cancellationToken);

    public async Task<bool> ApplySavedDefinitionAsync(
        QuantificationDefinition saved,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (IsBusy || !HasLoadedWorkbook || string.IsNullOrWhiteSpace(FilePath))
        {
            SavedDefinitionApplicationError = CreateError(
                IsBusy ? "INPUT_LOAD_IN_PROGRESS" : "INPUT_WORKBOOK_REQUIRED",
                "<saved-definition>",
                nameof(FilePath),
                IsBusy
                    ? "Excel の読込完了後に、保存した採点定義を適用してください。"
                    : "保存した採点定義を適用する前に、標準 .xlsx を読み込んでください。");
            return false;
        }

        string loadedPath = FilePath;
        QuantificationDefinition originalDraft = definitionDraft;
        WorkbookMetadata originalMetadata = metadata!;
        InputSnapshot originalSnapshot = snapshot!;
        long sequence = Interlocked.Increment(ref loadSequence);
        try
        {
            SavedDefinitionApplicationError = null;
            IsBusy = true;
            OnPropertyChanged(nameof(CanContinue));

            QuantificationDefinition incoming;
            InputWorkbookLoadResult result;
            WorksheetChoiceViewModel[] worksheetChoices;
            SourceColumnOption[] columnChoices;
            MappingSuggestionViewModel[] candidateChoices;
            // Only preparation is caught here. No live input state is replaced before commit.
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                DefinitionValidationResult validation = definitionValidator.Validate(saved);
                if (!validation.IsValid)
                {
                    DefinitionValidationError error = validation.Errors[0];
                    return Reject(error.Code, error.Field, DefinitionValidationMessage(error.Code));
                }

                incoming = CloneDefinition(saved);
                if (!IsCurrentInput())
                {
                    return false;
                }

                result = await loader.LoadAsync(
                    loadedPath,
                    checked((uint)incoming.HeaderRow),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentInput())
                {
                    return false;
                }

                if (!originalSnapshot.Equals(result.Snapshot))
                {
                    return Reject("INPUT_CHANGED", nameof(FilePath), ClassificationMessage("INPUT_CHANGED"));
                }

                ColumnMappingValidationResult mapping = mappingValidator.Validate(result.Metadata, incoming);
                if (!mapping.IsValid)
                {
                    ColumnMappingValidationError error = mapping.Errors[0];
                    return Reject(error.Code, error.Field, MappingValidationMessage(error.Code));
                }

                WorksheetMetadata worksheet = result.Metadata.Worksheets.First(item => string.Equals(
                    item.Name,
                    incoming.SourceSheet,
                    StringComparison.OrdinalIgnoreCase));
                Dictionary<uint, string> headers = worksheet.HeaderCells.ToDictionary(
                    cell => cell.ColumnIndex,
                    cell => cell.Value);
                worksheetChoices = result.Metadata.Worksheets
                    .Select(item => new WorksheetChoiceViewModel(item))
                    .ToArray();
                columnChoices = Enumerable.Range(
                        checked((int)worksheet.FirstColumnIndex),
                        checked((int)worksheet.ColumnCount))
                    .Select(column => new SourceColumnOption(
                        GetColumnName((uint)column),
                        headers.GetValueOrDefault((uint)column, string.Empty)))
                    .ToArray();
                WorksheetMappingSuggestion? worksheetSuggestion = result.Suggestions.WorksheetSuggestions
                    .FirstOrDefault(item => string.Equals(
                        item.WorksheetName,
                        incoming.SourceSheet,
                        StringComparison.OrdinalIgnoreCase));
                candidateChoices = worksheetSuggestion?.Candidates
                    .Select(candidate => new MappingSuggestionViewModel(
                        candidate,
                        headers.GetValueOrDefault(candidate.ColumnIndex, string.Empty)))
                    .ToArray() ?? [];
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                return Reject(
                    "SAVED_DEFINITION_CANCELLED",
                    nameof(FilePath),
                    "保存した採点定義の適用は取り消されました。現在の入力は変更していません。");
            }
            catch (Exception)
            {
                return Reject(
                    "SAVED_DEFINITION_LOAD_FAILED",
                    nameof(FilePath),
                    "保存した採点定義を適用できませんでした。Excel の形式、アクセス権、破損の有無を確認してください。");
            }

            if (!IsCurrentInput())
            {
                return false;
            }

            updatingInputChoices = true;
            try
            {
                snapshot = result.Snapshot;
                metadata = result.Metadata;
                suggestions = result.Suggestions;
                selectedSheet = incoming.SourceSheet;
                headerRow = incoming.HeaderRow;
                firstDataRow = incoming.FirstDataRow;
                lastDataRow = incoming.LastDataRow;
                definitionDraft = incoming;
                isUsingSuggestedMapping = false;
                loadError = null;

                worksheetItems.Clear();
                foreach (WorksheetChoiceViewModel choice in worksheetChoices)
                {
                    worksheetItems.Add(choice);
                }

                availableColumnItems.Clear();
                availableColumnNameItems.Clear();
                foreach (SourceColumnOption choice in columnChoices)
                {
                    availableColumnItems.Add(choice);
                    availableColumnNameItems.Add(choice.ColumnName);
                }

                suggestionItems.Clear();
                foreach (MappingSuggestionViewModel choice in candidateChoices)
                {
                    suggestionItems.Add(choice);
                }

                SynchronizeQuestionItems();
            }
            finally
            {
                updatingInputChoices = false;
            }

            SavedDefinitionApplicationError = null;
            OnPropertiesChanged(
                nameof(Metadata),
                nameof(Snapshot),
                nameof(HasLoadedWorkbook),
                nameof(SelectedSheet),
                nameof(SelectedWorksheetChoice),
                nameof(HeaderRow),
                nameof(FirstDataRow),
                nameof(LastDataRow),
                nameof(DefinitionDraft),
                nameof(IsUsingSuggestedMapping),
                nameof(SuggestionStatusText),
                nameof(WorkbookSummary),
                nameof(StatusText));
            Revalidate();
            return true;
        }
        finally
        {
            if (sequence == Volatile.Read(ref loadSequence))
            {
                IsBusy = false;
                OnPropertyChanged(nameof(CanContinue));
            }
        }

        bool IsCurrentInput() => sequence == Volatile.Read(ref loadSequence)
            && string.Equals(loadedPath, FilePath, StringComparison.Ordinal)
            && ReferenceEquals(originalDraft, definitionDraft)
            && ReferenceEquals(originalMetadata, metadata)
            && ReferenceEquals(originalSnapshot, snapshot);

        bool Reject(string code, string field, string message)
        {
            if (IsCurrentInput())
            {
                SavedDefinitionApplicationError = CreateError(code, "<saved-definition>", field, message);
            }

            return false;
        }
    }

    public void ApplySuggestedMapping()
    {
        if (metadata is null || CurrentWorksheetSuggestion is null)
        {
            return;
        }

        ApplyWorksheetSelection(applySuggestion: true);
    }

    public InputQuestionMappingViewModel AddQuestion()
    {
        string primaryColumn = availableColumnNameItems.FirstOrDefault() ?? "A";
        string questionText = TryGetQuestionTextForPrimaryColumn(primaryColumn, out string headerText)
            ? headerText
            : string.Empty;
        QuestionDefinition question = CreateDefaultQuestion(
            NewId("question"),
            questionItems.Count + 1,
            primaryColumn,
            questionText: questionText,
            studentPrompt: false,
            supportingColumns: []);
        CommitDraft(definitionDraft.AddQuestion(question), suggested: false);
        return questionItems.Single(item => string.Equals(item.Id, question.Id, StringComparison.Ordinal));
    }

    public InputQuestionMappingViewModel DuplicateQuestion(string questionId)
    {
        int index = FindQuestionIndex(questionId);
        QuantificationDefinition next = definitionDraft.DuplicateQuestion(
            index,
            NewId("question"),
            _ => NewId("evaluator"),
            _ => NewId("criterion"),
            _ => NewId("special"));
        string duplicateId = next.Questions[index + 1].Id;
        CommitDraft(next, suggested: false);
        return questionItems.Single(item => string.Equals(item.Id, duplicateId, StringComparison.Ordinal));
    }

    public bool CanMoveQuestionUp(string questionId) => FindQuestionIndex(questionId, throwIfMissing: false) > 0;

    public bool CanMoveQuestionDown(string questionId)
    {
        int index = FindQuestionIndex(questionId, throwIfMissing: false);
        return index >= 0 && index < definitionDraft.Questions.Length - 1;
    }

    public void MoveQuestionUp(string questionId)
    {
        int index = FindQuestionIndex(questionId);
        if (index > 0)
        {
            CommitDraft(definitionDraft.MoveQuestion(index, index - 1), suggested: false);
        }
    }

    public void MoveQuestionDown(string questionId)
    {
        int index = FindQuestionIndex(questionId);
        if (index < definitionDraft.Questions.Length - 1)
        {
            CommitDraft(definitionDraft.MoveQuestion(index, index + 1), suggested: false);
        }
    }

    public void DeleteQuestion(string questionId)
    {
        int index = FindQuestionIndex(questionId);
        CommitDraft(definitionDraft.RemoveQuestion(index), suggested: false);
    }

    public void SetQuestionEnabled(string questionId, bool enabled) =>
        UpdateQuestion(questionId, question => question with { Enabled = enabled });

    public void SetPrimaryColumn(string questionId, string columnName)
    {
        if (updatingInputChoices)
        {
            return;
        }

        string primaryColumn = columnName ?? string.Empty;
        bool updateQuestionText = TryGetQuestionTextForPrimaryColumn(
            primaryColumn,
            out string questionText);
        UpdateQuestion(questionId, question =>
        {
            ImmutableArray<string> supportingColumns = question.SupportingSourceColumns.IsDefault
                ? []
                : question.SupportingSourceColumns;
            return question with
            {
                PrimarySourceColumn = primaryColumn,
                QuestionText = updateQuestionText ? questionText : question.QuestionText,
                SupportingSourceColumns =
                [
                    .. supportingColumns.Where(column => !string.Equals(
                        column,
                        primaryColumn,
                        StringComparison.OrdinalIgnoreCase)),
                ],
            };
        });
    }

    public void SetSupportingColumn(string questionId, string columnName, bool selected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        UpdateQuestion(questionId, question =>
        {
            ImmutableArray<string> columns = question.SupportingSourceColumns.IsDefault
                ? []
                : question.SupportingSourceColumns;
            int existingIndex = FindColumnIndex(columns, columnName);
            if (selected && existingIndex < 0)
            {
                columns = columns.Add(columnName);
            }
            else if (!selected && existingIndex >= 0)
            {
                columns = columns.RemoveAt(existingIndex);
            }

            return question with { SupportingSourceColumns = columns };
        });
    }

    public bool TryCreateDesignDefinition(out QuantificationDefinition? definition)
    {
        Revalidate();
        if (!CanContinue)
        {
            definition = null;
            return false;
        }

        definition = CloneDefinition(definitionDraft);
        return true;
    }

    public QuantificationDefinition CreateDesignDefinition()
    {
        if (!TryCreateDesignDefinition(out QuantificationDefinition? definition))
        {
            throw new InvalidOperationException(
                $"The input configuration has {validationErrorItems.Count.ToString(CultureInfo.InvariantCulture)} technical validation error(s).");
        }

        return definition!;
    }

    internal void SynchronizeFromDesignDraft(QuantificationDefinition designDraft)
    {
        ArgumentNullException.ThrowIfNull(designDraft);
        CommitDraft(designDraft, IsUsingSuggestedMapping);
    }

    internal void UpdateQuestion(
        string questionId,
        Func<QuestionDefinition, QuestionDefinition> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        int index = FindQuestionIndex(questionId);
        QuestionDefinition updated = update(definitionDraft.Questions[index]);
        QuantificationDefinition next = definitionDraft with
        {
            Questions = definitionDraft.Questions.SetItem(index, updated),
        };
        CommitDraft(next, suggested: false);
    }

    private void ApplyLoadResult(InputWorkbookLoadResult result, bool preserveMapping)
    {
        QuantificationDefinition? retained = preserveMapping && HasLoadedWorkbook
            ? definitionDraft
            : null;
        snapshot = result.Snapshot;
        metadata = result.Metadata;
        suggestions = result.Suggestions;
        updatingInputChoices = true;
        try
        {
            worksheetItems.Clear();
            foreach (WorksheetMetadata worksheet in metadata.Worksheets)
            {
                worksheetItems.Add(new WorksheetChoiceViewModel(worksheet));
            }

            selectedSheet = retained?.SourceSheet
                ?? result.Suggestions.SuggestedWorksheetName
                ?? metadata.Worksheets.FirstOrDefault(worksheet => worksheet.State == WorkbookSheetState.Visible)?.Name
                ?? metadata.Worksheets[0].Name;
            OnPropertyChanged(nameof(SelectedSheet));
            OnPropertyChanged(nameof(SelectedWorksheetChoice));
            OnPropertiesChanged(
                nameof(Metadata),
                nameof(Snapshot),
                nameof(HasLoadedWorkbook),
                nameof(WorkbookSummary),
                nameof(StatusText));
            ApplyWorksheetSelection(applySuggestion: retained is null);
            if (retained is not null)
            {
                SynchronizeQuestionItems();
            }
        }
        finally
        {
            updatingInputChoices = false;
        }
    }

    private void ApplyWorksheetSelection(bool applySuggestion)
    {
        if (metadata is null)
        {
            return;
        }

        WorksheetMetadata? worksheet = metadata.Worksheets.FirstOrDefault(item => string.Equals(
            item.Name,
            SelectedSheet,
            StringComparison.OrdinalIgnoreCase));
        if (worksheet is null)
        {
            availableColumnItems.Clear();
            availableColumnNameItems.Clear();
            suggestionItems.Clear();
            RebuildRootDraft();
            return;
        }

        Dictionary<uint, string> headers = worksheet.HeaderCells.ToDictionary(
            header => header.ColumnIndex,
            header => header.Value);
        availableColumnItems.Clear();
        availableColumnNameItems.Clear();
        for (uint column = worksheet.FirstColumnIndex; column <= worksheet.LastColumnIndex; column++)
        {
            string columnName = GetColumnName(column);
            availableColumnItems.Add(new SourceColumnOption(
                columnName,
                headers.GetValueOrDefault(column, string.Empty)));
            availableColumnNameItems.Add(columnName);
        }

        WorksheetMappingSuggestion? worksheetSuggestion = CurrentWorksheetSuggestion;
        suggestionItems.Clear();
        if (worksheetSuggestion is not null)
        {
            foreach (ColumnMappingCandidate candidate in worksheetSuggestion.Candidates)
            {
                suggestionItems.Add(new MappingSuggestionViewModel(
                    candidate,
                    headers.GetValueOrDefault(candidate.ColumnIndex, string.Empty)));
            }
        }

        if (applySuggestion)
        {
            uint suggestedHeaderRow = worksheetSuggestion?.HeaderRow ?? metadata.HeaderRowNumber;
            headerRow = suggestedHeaderRow is 1 or 2
                ? checked((int)suggestedHeaderRow)
                : 1;
            firstDataRow = checked((int)(worksheetSuggestion?.FirstDataRow
                ?? Math.Min(worksheet.LastRowIndex, metadata.HeaderRowNumber + 1)));
            lastDataRow = checked((int)(worksheetSuggestion?.LastDataRow ?? worksheet.LastRowIndex));
            OnPropertiesChanged(nameof(HeaderRow), nameof(FirstDataRow), nameof(LastDataRow));

            ImmutableArray<QuestionDefinition> questions = CreateSuggestedQuestions(
                worksheet,
                worksheetSuggestion);
            definitionDraft = definitionDraft with
            {
                SourceSheet = worksheet.Name,
                HeaderRow = headerRow,
                FirstDataRow = firstDataRow,
                LastDataRow = lastDataRow,
                Questions = questions,
            };
            IsUsingSuggestedMapping = worksheetSuggestion is not null;
            SynchronizeQuestionItems();
            OnPropertyChanged(nameof(DefinitionDraft));
        }
        else
        {
            RebuildRootDraft();
        }

        OnPropertyChanged(nameof(SuggestionStatusText));
        RaiseCommandStates();
        Revalidate();
    }

    private ImmutableArray<QuestionDefinition> CreateSuggestedQuestions(
        WorksheetMetadata worksheet,
        WorksheetMappingSuggestion? worksheetSuggestion)
    {
        Dictionary<string, string> headers = worksheet.HeaderCells.ToDictionary(
            header => header.ColumnName,
            header => header.Value,
            StringComparer.OrdinalIgnoreCase);
        ImmutableArray<QuestionDefinition>.Builder questions = ImmutableArray.CreateBuilder<QuestionDefinition>();
        if (worksheetSuggestion is not null)
        {
            foreach (ColumnMappingCandidate candidate in worksheetSuggestion.Candidates.Where(item => item.IsPrimaryCandidate))
            {
                questions.Add(CreateDefaultQuestion(
                    NewId("question"),
                    questions.Count + 1,
                    candidate.ColumnName,
                    headers.GetValueOrDefault(candidate.ColumnName, string.Empty),
                    candidate.IsStudentPromptPrimaryCandidate,
                    candidate.SuggestedSupportingColumns));
            }
        }

        if (questions.Count == 0 && availableColumnNameItems.Count > 0)
        {
            string primary = availableColumnNameItems[0];
            questions.Add(CreateDefaultQuestion(
                NewId("question"),
                1,
                primary,
                headers.GetValueOrDefault(primary, string.Empty),
                studentPrompt: false,
                supportingColumns: []));
        }

        ImmutableArray<QuestionDefinition> materialized = questions.ToImmutable();
        ImmutableArray<decimal> allocations = new ScoringAllocationCalculator().Equalize(
            definitionDraft.BasePoints,
            definitionDraft.SpecialPoints,
            materialized.Count(question => question.Enabled));
        int allocationIndex = 0;
        return materialized
            .Select(question => question.Enabled
                ? question with { Points = allocations[allocationIndex++] }
                : question)
            .ToImmutableArray();
    }

    private QuestionDefinition CreateDefaultQuestion(
        string id,
        int ordinal,
        string primaryColumn,
        string questionText,
        bool studentPrompt,
        IEnumerable<string> supportingColumns)
    {
        CriterionDefinition criterion = new()
        {
            Id = NewId("criterion"),
            DisplayName = studentPrompt ? "具体性と実行可能性" : "知識ポイント 1",
            Description = DefaultCriterionDescriptions.For(
                studentPrompt ? EvaluatorType.CustomPrompt : EvaluatorType.KnowledgeCoverage),
            Weight = 1m,
            Range = null,
            Enabled = true,
        };
        EvaluatorDefinition evaluator = new()
        {
            Id = NewId("evaluator"),
            DisplayName = studentPrompt ? "Prompt 分析" : "Knowledge coverage",
            Type = studentPrompt ? EvaluatorType.CustomPrompt : EvaluatorType.KnowledgeCoverage,
            Weight = 1m,
            Range = new ScoreRange(0m, 10m),
            Criteria = [criterion],
            BuiltInTemplateVersion = studentPrompt ? null : BuiltInPromptTemplates.KnowledgeTemplateVersion,
            CustomPromptTemplate = studentPrompt ? BuiltInPromptTemplates.CustomPromptPreset : null,
            Enabled = true,
        };
        return new QuestionDefinition
        {
            Id = id,
            DisplayName = $"質問 {ordinal.ToString(CultureInfo.InvariantCulture)}",
            QuestionText = questionText,
            PrimarySourceColumn = primaryColumn,
            SupportingSourceColumns = [.. supportingColumns],
            Points = 0m,
            Evaluators = [evaluator],
            Enabled = true,
        };
    }

    private void RebuildRootDraft()
    {
        definitionDraft = definitionDraft with
        {
            SourceSheet = SelectedSheet,
            HeaderRow = HeaderRow,
            FirstDataRow = FirstDataRow,
            LastDataRow = LastDataRow,
        };
        OnPropertyChanged(nameof(DefinitionDraft));
        OnPropertyChanged(nameof(IsUsingSuggestedMapping));
        OnPropertyChanged(nameof(SuggestionStatusText));
        Revalidate();
    }

    private void CommitDraft(QuantificationDefinition next, bool suggested)
    {
        definitionDraft = CloneDefinition(next);
        IsUsingSuggestedMapping = suggested;
        SynchronizeQuestionItems();
        OnPropertyChanged(nameof(DefinitionDraft));
        Revalidate();
    }

    private void SynchronizeQuestionItems()
    {
        bool wasUpdatingPresentation = updatingPresentation;
        updatingPresentation = true;
        try
        {
            InputQuestionMappingViewModel? previousSelection = selectedQuestion;
            Dictionary<string, InputQuestionMappingViewModel> existing = questionItems
                .ToDictionary(item => item.Id, StringComparer.Ordinal);
            List<InputQuestionMappingViewModel> ordered = [];
            foreach (QuestionDefinition question in definitionDraft.Questions)
            {
                if (!existing.Remove(question.Id, out InputQuestionMappingViewModel? item))
                {
                    item = new InputQuestionMappingViewModel(this, question);
                }
                else
                {
                    item.Synchronize(question);
                }

                ordered.Add(item);
            }

            bool orderChanged = !questionItems.SequenceEqual(ordered);
            InputQuestionMappingViewModel? nextSelection = previousSelection;
            if (questionItems.Count == 0)
            {
                nextSelection = ordered.FirstOrDefault();
            }
            else if (previousSelection is not null && !ordered.Contains(previousSelection))
            {
                int previousIndex = questionItems.IndexOf(previousSelection);
                nextSelection = questionItems.Skip(previousIndex + 1)
                    .FirstOrDefault(item => ordered.Contains(item))
                    ?? questionItems.Take(previousIndex).LastOrDefault(item => ordered.Contains(item))
                    ?? ordered.FirstOrDefault();
            }

            SynchronizeQuestionCollection(questionItems, ordered);
            // Only structural edits follow the stable selection or its old-order neighbor.
            // Value edits and header refreshes keep a separately browsed page in place.
            int nextPageIndex = orderChanged && nextSelection is not null
                ? questionItems.IndexOf(nextSelection) / pageSize
                : pageIndex;
            UpdatePresentation(nextSelection, nextPageIndex, pageSize);
            foreach (InputQuestionMappingViewModel item in questionItems)
            {
                item.RefreshCommands();
            }
        }
        finally
        {
            updatingPresentation = wasUpdatingPresentation;
        }
    }

    private static void SynchronizeQuestionCollection(
        ObservableCollection<InputQuestionMappingViewModel> target,
        IReadOnlyList<InputQuestionMappingViewModel> ordered)
    {
        for (int index = target.Count - 1; index >= 0; index--)
        {
            if (!ordered.Contains(target[index]))
            {
                target.RemoveAt(index);
            }
        }

        for (int index = 0; index < ordered.Count; index++)
        {
            InputQuestionMappingViewModel item = ordered[index];
            int currentIndex = target.IndexOf(item);
            if (currentIndex < 0)
            {
                target.Insert(index, item);
            }
            else if (currentIndex != index)
            {
                target.Move(currentIndex, index);
            }
        }
    }

    private void UpdatePresentation(
        InputQuestionMappingViewModel? selection,
        int requestedPageIndex,
        int requestedPageSize)
    {
        bool wasUpdatingPresentation = updatingPresentation;
        updatingPresentation = true;
        try
        {
            var page = CalculateQuestionPage(questionItems.Count, requestedPageIndex, requestedPageSize);
            bool sizeChanged = pageSize != requestedPageSize;
            bool pageChanged = pageIndex != page.PageIndex;
            pageSize = requestedPageSize;
            pageIndex = page.PageIndex;
            InputQuestionMappingViewModel[] visible = questionItems.Skip(page.Start).Take(page.Count).ToArray();
            SynchronizeQuestionCollection(visibleQuestionItems, visible);
            selectedQuestion = selection;

            if (sizeChanged)
            {
                OnPropertyChanged(nameof(PageSize));
            }

            if (pageChanged)
            {
                OnPropertyChanged(nameof(PageIndex));
            }

            // As in Design, notify after items settle while writebacks are guarded.
            // A view can UpdateTarget for same-reference compiled SelectedItem bindings.
            OnPropertiesChanged(nameof(PageSummary), nameof(SelectedQuestion));
            previousPageCommand.RaiseCanExecuteChanged();
            nextPageCommand.RaiseCanExecuteChanged();
        }
        finally
        {
            updatingPresentation = wasUpdatingPresentation;
        }
    }

    private static (int PageIndex, int Start, int Count) CalculateQuestionPage(
        int questionCount,
        int requestedPageIndex,
        int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(questionCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        int lastPageIndex = questionCount == 0 ? 0 : (questionCount - 1) / pageSize;
        int index = Math.Clamp(requestedPageIndex, 0, lastPageIndex);
        // Clamp first: both the product and the returned end remain within questionCount.
        int start = index * pageSize;
        return (index, start, Math.Min(pageSize, questionCount - start));
    }

    private void ClearLoadedState()
    {
        metadata = null;
        snapshot = null;
        suggestions = null;
        IsUsingSuggestedMapping = false;
        selectedSheet = string.Empty;
        worksheetItems.Clear();
        availableColumnItems.Clear();
        availableColumnNameItems.Clear();
        suggestionItems.Clear();
        definitionDraft = CreateEmptyDefinition();
        SynchronizeQuestionItems();
        OnPropertiesChanged(
            nameof(Metadata),
            nameof(Snapshot),
            nameof(HasLoadedWorkbook),
            nameof(SelectedSheet),
            nameof(SelectedWorksheetChoice),
            nameof(DefinitionDraft),
            nameof(WorkbookSummary),
            nameof(StatusText));
    }

    private void SupersedeActiveLoad()
    {
        if (!IsBusy)
        {
            return;
        }

        Interlocked.Increment(ref loadSequence);
        IsBusy = false;
    }

    private void Revalidate()
    {
        Dictionary<string, InputValidationError> errors = new(StringComparer.Ordinal);
        if (loadError is not null)
        {
            AddError(errors, loadError);
        }

        if (metadata is null)
        {
            if (loadError is null)
            {
                AddError(errors, CreateError(
                    "INPUT_WORKBOOK_REQUIRED",
                    "<input>",
                    "FilePath",
                    "read-only で確認済みの標準 .xlsx が必要です。"));
            }
        }
        else
        {
            ColumnMappingValidationResult mapping = mappingValidator.Validate(metadata, definitionDraft);
            foreach (ColumnMappingValidationError error in mapping.Errors)
            {
                AddError(errors, CreateError(
                    error.Code,
                    error.QuestionId,
                    error.Field,
                    MappingValidationMessage(error.Code)));
            }

            DefinitionValidationResult definition = definitionValidator.Validate(definitionDraft);
            foreach (DefinitionValidationError error in definition.Errors)
            {
                AddError(errors, CreateError(
                    error.Code,
                    error.NodeId,
                    error.Field,
                    DefinitionValidationMessage(error.Code)));
            }
        }

        validationErrorItems.Clear();
        foreach (InputValidationError error in errors.Values)
        {
            validationErrorItems.Add(error);
        }

        OnPropertiesChanged(
            nameof(CanContinue),
            nameof(HasTechnicalErrors),
            nameof(IsTechnicallyValid),
            nameof(ValidationSummary),
            nameof(InputSummary));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        loadFileCommand.RaiseCanExecuteChanged();
        refreshHeaderCommand.RaiseCanExecuteChanged();
        applySuggestionsCommand.RaiseCanExecuteChanged();
        addQuestionCommand.RaiseCanExecuteChanged();
        foreach (InputQuestionMappingViewModel question in questionItems)
        {
            question.RefreshCommands();
        }
    }

    private int FindQuestionIndex(string questionId, bool throwIfMissing = true)
    {
        int index = -1;
        for (int candidateIndex = 0; candidateIndex < definitionDraft.Questions.Length; candidateIndex++)
        {
            if (string.Equals(
                definitionDraft.Questions[candidateIndex].Id,
                questionId,
                StringComparison.Ordinal))
            {
                index = candidateIndex;
                break;
            }
        }

        if (index < 0 && throwIfMissing)
        {
            throw new ArgumentException("The question identity does not exist in the input draft.", nameof(questionId));
        }

        return index;
    }

    private bool TryGetQuestionTextForPrimaryColumn(
        string columnName,
        out string questionText)
    {
        questionText = string.Empty;
        if (metadata is null
            || metadata.HeaderRowNumber != (uint)HeaderRow
            || !metadata.Worksheets.Any(worksheet => string.Equals(
                worksheet.Name,
                SelectedSheet,
                StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        SourceColumnOption? option = availableColumnItems.FirstOrDefault(item => string.Equals(
            item.ColumnName,
            columnName,
            StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            return false;
        }

        questionText = option.HeaderText;
        return true;
    }

    private static int FindColumnIndex(ImmutableArray<string> columns, string columnName)
    {
        for (int index = 0; index < columns.Length; index++)
        {
            if (string.Equals(columns[index], columnName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static QuantificationDefinition CreateEmptyDefinition() => new()
    {
        Id = $"definition-{Guid.NewGuid():N}",
        Name = "StudyReport 定量化",
        Revision = "1",
        SourceSheet = string.Empty,
        HeaderRow = 1,
        FirstDataRow = 2,
        LastDataRow = 2,
        RoundingDigits = 1,
        Questions = [],
    };

    internal static QuantificationDefinition CloneDefinition(QuantificationDefinition definition) => definition with
    {
        Questions = definition.Questions.IsDefault ? [] : [.. definition.Questions.Select(question => question with
        {
            SupportingSourceColumns = question.SupportingSourceColumns.IsDefault ? [] : [.. question.SupportingSourceColumns],
            Evaluators = question.Evaluators.IsDefault ? [] : [.. question.Evaluators.Select(evaluator => evaluator with
            {
                Criteria = evaluator.Criteria.IsDefault ? [] : [.. evaluator.Criteria.Select(criterion => criterion with { })],
            })],
            SpecialEvaluations = question.SpecialEvaluations.IsDefault ? [] : [.. question.SpecialEvaluations.Select(special => special with
            {
                SupportingSourceColumns = special.SupportingSourceColumns.IsDefault ? [] : [.. special.SupportingSourceColumns],
            })],
        })],
    };

    private static void AddError(
        IDictionary<string, InputValidationError> errors,
        InputValidationError error)
    {
        string key = $"{error.Code}|{error.NodeId}|{error.Field}";
        errors.TryAdd(key, error);
    }

    private static InputValidationError CreateError(
        string code,
        string nodeId,
        string field,
        string message) => new(code, nodeId, field, message);

    private static string ClassificationMessage(string code) => code switch
    {
        nameof(FileFormatClassification.UnsupportedExtension) => "標準 .xlsx 以外の拡張子は対象外です。",
        nameof(FileFormatClassification.LegacyBinaryWorkbook) => "旧形式の Excel workbook は対象外です。標準 .xlsx を使用してください。",
        nameof(FileFormatClassification.CommaSeparatedValues) => "CSV は対象外です。標準 .xlsx を使用してください。",
        nameof(FileFormatClassification.PortableDocumentFormat) => "PDF は対象外です。標準 .xlsx を使用してください。",
        nameof(FileFormatClassification.MacroEnabledWorkbook) => "macro-enabled workbook は対象外です。",
        nameof(FileFormatClassification.EncryptedOrRightsProtected) => "password または rights protection のある workbook は対象外です。",
        nameof(FileFormatClassification.InvalidZipSignature) => "標準 Office Open XML package として認識できません。",
        nameof(FileFormatClassification.UnsafePackage) => "Workbook package が安全上の上限を超えています。",
        "INPUT_CHANGED" => "読込中に入力が変更されました。変更完了後に再読込してください。",
        _ => "Workbook package が破損しているか、対応する標準 .xlsx ではありません。",
    };

    private static string MappingValidationMessage(string code) => code switch
    {
        "SOURCE_SHEET_REQUIRED" or "SOURCE_SHEET_NOT_FOUND" => "回答 sheet を選択してください。",
        "HEADER_METADATA_MISMATCH" => "見出し行を再読込して metadata と一致させてください。",
        "ROW_OUT_OF_RANGE" or "HEADER_ROW_OUTSIDE_WORKSHEET" or "DATA_ROW_OUTSIDE_WORKSHEET" => "行番号を選択 sheet の有効範囲内に設定してください。",
        "DATA_ROW_NOT_AFTER_HEADER" => "開始行は見出し行より後に設定してください。",
        "DATA_ROW_RANGE_REVERSED" => "終了行は開始行以降に設定してください。",
        "SELECTED_ROW_LIMIT_EXCEEDED" => "選択できる回答行は 20,000 行までです。",
        "SOURCE_COLUMN_REQUIRED" or "SOURCE_COLUMN_NOT_FOUND" or "INVALID_SOURCE_COLUMN" => "主回答列と補助列を選択 sheet の列から指定してください。",
        "DUPLICATE_SUPPORTING_COLUMN" => "同じ補助列を 1 質問内で重複指定できません。",
        "PRIMARY_COLUMN_REUSED" => "主回答列を同じ質問の補助列へ重複指定できません。",
        _ => "列 mapping を確認してください。",
    };

    private static string DefinitionValidationMessage(string code) => code switch
    {
        "QUESTION_REQUIRED" or "ENABLED_QUESTION_REQUIRED" => "1 件以上の有効な質問 mapping が必要です。",
        "ENABLED_EVALUATOR_REQUIRED" => "有効な質問には 1 件以上の有効な評価方法が必要です。",
        "ENABLED_CRITERION_REQUIRED" => "有効な評価方法には 1 件以上の有効な評価項目が必要です。",
        "WEIGHT_MUST_BE_POSITIVE" => "重みは 0 より大きい値にしてください。",
        "SCORE_RANGE_INVALID" => "最小点は最大点より小さくしてください。",
        "REQUIRED" => "必須項目を入力してください。",
        _ => "定量化 definition の技術的な設定を確認してください。",
    };

    private static string GetColumnName(uint columnIndex)
    {
        Span<char> characters = stackalloc char[3];
        int position = characters.Length;
        uint value = columnIndex;
        while (value > 0)
        {
            value--;
            characters[--position] = (char)('A' + (value % 26));
            value /= 26;
        }

        return new string(characters[position..]);
    }

    public override string ToString() =>
        $"{nameof(InputViewModel)} {{ Loaded = {HasLoadedWorkbook}, QuestionCount = {Questions.Count.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}