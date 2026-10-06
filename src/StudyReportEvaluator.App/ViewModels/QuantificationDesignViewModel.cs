using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Visualization;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Scoring;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.App.ViewModels;

public sealed class DesignValidationError
{
    public DesignValidationError(
        string code,
        string nodeKind,
        string nodeId,
        string field,
        string message)
    {
        Code = code;
        NodeKind = nodeKind;
        NodeId = nodeId;
        Field = field;
        Message = message;
    }

    public string Code { get; }

    public string NodeKind { get; }

    public string NodeId { get; }

    public string Field { get; }

    public string Message { get; }

    public string AccessibleText => $"{NodeKind} {NodeId}、{Field}。{Message}";

    public override string ToString() =>
        $"{nameof(DesignValidationError)} {{ Code = {Code}, NodeKind = {NodeKind}, NodeId = {NodeId}, Field = {Field}, Content = <redacted> }}";
}

public sealed class QuantificationDesignValidationException : Exception
{
    public QuantificationDesignValidationException(int errorCount)
        : base($"The quantification draft has {errorCount.ToString(CultureInfo.InvariantCulture)} technical validation error(s).")
    {
        ErrorCount = errorCount;
    }

    public int ErrorCount { get; }
}

public sealed record EvaluatorTypeChoice(
    EvaluatorType Type,
    string DisplayName,
    string ContractName);

public enum ImportedPromptTarget
{
    CustomEvaluator,
    SpecialEvaluation,
}

public sealed record ImportedPromptTargetChoice(
    ImportedPromptTarget Target,
    string DisplayName);

public sealed class ImportedPromptViewModel
{
    internal ImportedPromptViewModel(ImportedPrompt source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    internal ImportedPrompt Source { get; }

    public string DisplayName => Source.DisplayName;

    public string Content => Source.Content;

    public override string ToString() =>
        $"{nameof(ImportedPromptViewModel)} {{ DisplayName = {DisplayName}, Content = <redacted> }}";
}

public sealed class QuestionDesignItemViewModel : UiObservableObject
{
    private readonly QuantificationDesignViewModel owner;
    private QuestionDefinition definition;
    private readonly ViewModelCommand duplicateCommand;
    private readonly ViewModelCommand moveUpCommand;
    private readonly ViewModelCommand moveDownCommand;
    private readonly ViewModelCommand deleteCommand;
    private readonly ViewModelCommand addKnowledgeEvaluatorCommand;
    private readonly ViewModelCommand addCustomEvaluatorCommand;
    private readonly ViewModelCommand addSpecialEvaluationCommand;
    private EvaluatorDesignItemViewModel? selectedEvaluator;
    private SpecialEvaluationDesignItemViewModel? selectedSpecialEvaluation;

    internal QuestionDesignItemViewModel(
        QuantificationDesignViewModel owner,
        QuestionDefinition definition)
    {
        this.owner = owner;
        this.definition = definition;
        Evaluators = [];
        SpecialEvaluations = [];
        duplicateCommand = new ViewModelCommand(_ => owner.DuplicateQuestion(Id));
        moveUpCommand = new ViewModelCommand(_ => owner.MoveQuestionUp(Id), _ => owner.CanMoveQuestionUp(Id));
        moveDownCommand = new ViewModelCommand(_ => owner.MoveQuestionDown(Id), _ => owner.CanMoveQuestionDown(Id));
        deleteCommand = new ViewModelCommand(_ => owner.DeleteQuestion(Id));
        addKnowledgeEvaluatorCommand = new ViewModelCommand(_ => owner.AddEvaluator(Id, EvaluatorType.KnowledgeCoverage));
        addCustomEvaluatorCommand = new ViewModelCommand(_ => owner.AddEvaluator(Id, EvaluatorType.CustomPrompt));
        addSpecialEvaluationCommand = new ViewModelCommand(_ => owner.AddSpecialEvaluation(Id));
        SynchronizeEvaluators();
        SynchronizeSpecialEvaluations();
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

    public string PrimarySourceColumn => definition.PrimarySourceColumn;

    public string SupportingColumnsText => definition.SupportingSourceColumns.IsEmpty
        ? "補助列なし"
        : string.Join(", ", definition.SupportingSourceColumns);

    public decimal Weight
    {
        get => definition.Points;
        set => owner.UpdateQuestion(Id, question => question with { Points = value });
    }

    public decimal Points
    {
        get => Weight;
        set => Weight = value;
    }

    public bool Enabled
    {
        get => definition.Enabled;
        set => owner.UpdateQuestion(Id, question => question with { Enabled = value });
    }

    public decimal EffectiveWeightPercentage => owner.GetQuestionWeightPercentage(Id);

    public string EffectiveWeightText => QuantificationDesignViewModel.FormatPercentage(EffectiveWeightPercentage);

    public ObservableCollection<EvaluatorDesignItemViewModel> Evaluators { get; }

    public ObservableCollection<SpecialEvaluationDesignItemViewModel> SpecialEvaluations { get; }

    public EvaluatorDesignItemViewModel? SelectedEvaluator
    {
        get => selectedEvaluator;
        set
        {
            if (SetProperty(ref selectedEvaluator, value))
            {
                owner.NotifyPromptTargetChanged();
            }
        }
    }

    public SpecialEvaluationDesignItemViewModel? SelectedSpecialEvaluation
    {
        get => selectedSpecialEvaluation;
        set
        {
            if (SetProperty(ref selectedSpecialEvaluation, value))
            {
                owner.NotifyPromptTargetChanged();
            }
        }
    }

    public bool HasErrors => owner.HasErrorsFor(Id);

    public string ValidationText => owner.ValidationTextFor(Id);

    public string CardAutomationId => $"DesignQuestion-{Id}";

    public string NameAutomationId => $"DesignQuestion-{Id}-Name";

    public string WeightAutomationId => $"DesignQuestion-{Id}-Weight";

    public ICommand DuplicateCommand => duplicateCommand;

    public ICommand MoveUpCommand => moveUpCommand;

    public ICommand MoveDownCommand => moveDownCommand;

    public ICommand DeleteCommand => deleteCommand;

    public ICommand AddKnowledgeEvaluatorCommand => addKnowledgeEvaluatorCommand;

    public ICommand AddCustomEvaluatorCommand => addCustomEvaluatorCommand;

    public ICommand AddSpecialEvaluationCommand => addSpecialEvaluationCommand;

    internal void Synchronize(QuestionDefinition updated)
    {
        definition = updated;
        SynchronizeEvaluators();
        SynchronizeSpecialEvaluations();
        RefreshAll();
    }

    internal void RefreshAll()
    {
        OnPropertiesChanged(
            nameof(Id),
            nameof(DisplayName),
            nameof(QuestionText),
            nameof(PrimarySourceColumn),
            nameof(SupportingColumnsText),
            nameof(Weight),
            nameof(Points),
            nameof(Enabled),
            nameof(EffectiveWeightPercentage),
            nameof(EffectiveWeightText),
            nameof(HasErrors),
            nameof(ValidationText),
            nameof(CardAutomationId),
            nameof(NameAutomationId),
            nameof(WeightAutomationId));
        moveUpCommand.RaiseCanExecuteChanged();
        moveDownCommand.RaiseCanExecuteChanged();
        foreach (EvaluatorDesignItemViewModel evaluator in Evaluators)
        {
            evaluator.RefreshAll();
        }

        foreach (SpecialEvaluationDesignItemViewModel special in SpecialEvaluations)
        {
            special.RefreshAll();
        }
    }

    private void SynchronizeEvaluators()
    {
        EvaluatorDesignItemViewModel? previousSelection = selectedEvaluator;
        Dictionary<string, EvaluatorDesignItemViewModel> existing = Evaluators
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        List<EvaluatorDesignItemViewModel> ordered = [];
        foreach (EvaluatorDefinition evaluator in definition.Evaluators)
        {
            if (!existing.Remove(evaluator.Id, out EvaluatorDesignItemViewModel? item))
            {
                item = new EvaluatorDesignItemViewModel(owner, Id, evaluator);
            }
            else
            {
                item.Synchronize(evaluator);
            }

            ordered.Add(item);
        }

        SelectedEvaluator = SynchronizeCollection(Evaluators, ordered, previousSelection);
    }

    private void SynchronizeSpecialEvaluations()
    {
        SpecialEvaluationDesignItemViewModel? previousSelection = selectedSpecialEvaluation;
        Dictionary<string, SpecialEvaluationDesignItemViewModel> existing = SpecialEvaluations
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        List<SpecialEvaluationDesignItemViewModel> ordered = [];
        foreach (SpecialEvaluationDefinition special in definition.SpecialEvaluations)
        {
            if (!existing.Remove(special.Id, out SpecialEvaluationDesignItemViewModel? item))
            {
                item = new SpecialEvaluationDesignItemViewModel(owner, Id, special);
            }
            else
            {
                item.Synchronize(special);
            }

            ordered.Add(item);
        }

        SelectedSpecialEvaluation = SynchronizeCollection(SpecialEvaluations, ordered, previousSelection);
    }

    internal static T? SynchronizeCollection<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> ordered,
        T? selected)
        where T : class
    {
        // The callers reuse each VM by stable ID. Resolve the selection before
        // collection notifications can clear a bound SelectedItem during a move.
        T? nextSelection = selected;
        if (target.Count == 0)
        {
            nextSelection = ordered.FirstOrDefault();
        }
        else if (selected is not null && !ordered.Contains(selected))
        {
            int previousIndex = target.IndexOf(selected);
            nextSelection = target.Skip(previousIndex + 1)
                .FirstOrDefault(item => ordered.Contains(item))
                ?? target.Take(previousIndex).LastOrDefault(item => ordered.Contains(item))
                ?? ordered.FirstOrDefault();
        }

        SynchronizeCollection(target, ordered);
        return nextSelection;
    }

    internal static void SynchronizeCollection<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> ordered)
        where T : class
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
            T item = ordered[index];
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

    public override string ToString() =>
        $"{nameof(QuestionDesignItemViewModel)} {{ Id = {Id}, Content = <redacted> }}";
}

public sealed class SpecialSupportingColumnSelectionViewModel : UiObservableObject
{
    private readonly SpecialEvaluationDesignItemViewModel owner;
    private bool isSelected;

    internal SpecialSupportingColumnSelectionViewModel(
        SpecialEvaluationDesignItemViewModel owner,
        string columnName,
        bool selected)
    {
        this.owner = owner;
        ColumnName = columnName;
        isSelected = selected;
    }

    public string ColumnName { get; }

    public bool CanSelect => !string.Equals(
        owner.PrimarySourceColumn,
        ColumnName,
        StringComparison.OrdinalIgnoreCase);

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (value && !CanSelect)
            {
                return;
            }

            owner.SetSupportingColumn(ColumnName, value);
        }
    }

    internal void Synchronize(bool selected)
    {
        SetProperty(ref isSelected, selected, nameof(IsSelected));
        OnPropertyChanged(nameof(CanSelect));
    }
}

public sealed class SpecialEvaluationDesignItemViewModel : UiObservableObject
{
    private readonly QuantificationDesignViewModel owner;
    private readonly string questionId;
    private SpecialEvaluationDefinition definition;
    private string primarySourceColumn;
    private bool isRefreshingPrimarySourceColumn;
    private readonly ViewModelCommand duplicateCommand;
    private readonly ViewModelCommand moveUpCommand;
    private readonly ViewModelCommand moveDownCommand;
    private readonly ViewModelCommand deleteCommand;

    internal SpecialEvaluationDesignItemViewModel(
        QuantificationDesignViewModel owner,
        string questionId,
        SpecialEvaluationDefinition definition)
    {
        this.owner = owner;
        this.questionId = questionId;
        this.definition = definition;
        primarySourceColumn = definition.PrimarySourceColumn;
        SupportingColumns = [];
        duplicateCommand = new ViewModelCommand(_ => owner.DuplicateSpecialEvaluation(questionId, Id));
        moveUpCommand = new ViewModelCommand(
            _ => owner.MoveSpecialEvaluationUp(questionId, Id),
            _ => owner.CanMoveSpecialEvaluationUp(questionId, Id));
        moveDownCommand = new ViewModelCommand(
            _ => owner.MoveSpecialEvaluationDown(questionId, Id),
            _ => owner.CanMoveSpecialEvaluationDown(questionId, Id));
        deleteCommand = new ViewModelCommand(_ => owner.DeleteSpecialEvaluation(questionId, Id));
        SynchronizeSupportingColumns();
    }

    public string Id => definition.Id;

    public string DisplayName
    {
        get => definition.DisplayName;
        set => owner.UpdateSpecialEvaluation(questionId, Id, special => special with
        {
            DisplayName = value ?? string.Empty,
        });
    }

    public string PrimarySourceColumn
    {
        get => primarySourceColumn;
        set
        {
            // Candidate replacement can write back null or the previous selection.
            // Neither is a user edit while incoming state is being published.
            if (owner.IsSynchronizingFromInput || isRefreshingPrimarySourceColumn)
            {
                return;
            }

            string next = value ?? string.Empty;
            owner.UpdateSpecialEvaluation(questionId, Id, special => special with
            {
                PrimarySourceColumn = next,
                SupportingSourceColumns = special.SupportingSourceColumns
                    .Where(column => !string.Equals(column, next, StringComparison.OrdinalIgnoreCase))
                    .ToImmutableArray(),
            });
        }
    }

    public IReadOnlyList<string> AvailableColumnNames => owner.AvailableColumnNames;

    public ObservableCollection<SpecialSupportingColumnSelectionViewModel> SupportingColumns { get; }

    public string PromptTemplate
    {
        get => definition.PromptTemplate;
        set => owner.UpdateSpecialEvaluation(questionId, Id, special => special with
        {
            PromptTemplate = value ?? string.Empty,
        });
    }

    public bool Enabled
    {
        get => definition.Enabled;
        set => owner.UpdateSpecialEvaluation(questionId, Id, special => special with { Enabled = value });
    }

    public bool HasErrors => owner.HasErrorsFor(Id);

    public string ValidationText => owner.ValidationTextFor(Id);

    public string CardAutomationId => $"DesignSpecial-{Id}";

    public string PromptAutomationId => $"DesignSpecial-{Id}-Prompt";

    public ICommand DuplicateCommand => duplicateCommand;

    public ICommand MoveUpCommand => moveUpCommand;

    public ICommand MoveDownCommand => moveDownCommand;

    public ICommand DeleteCommand => deleteCommand;

    internal void SetSupportingColumn(string columnName, bool selected)
    {
        ImmutableArray<string> current = definition.SupportingSourceColumns.IsDefault
            ? []
            : definition.SupportingSourceColumns;
        int existing = -1;
        for (int index = 0; index < current.Length; index++)
        {
            if (string.Equals(current[index], columnName, StringComparison.OrdinalIgnoreCase))
            {
                existing = index;
                break;
            }
        }
        ImmutableArray<string> next = selected
            ? existing >= 0 ? current : current.Add(columnName)
            : existing >= 0 ? current.RemoveAt(existing) : current;
        owner.UpdateSpecialEvaluation(questionId, Id, special => special with
        {
            SupportingSourceColumns = next,
        });
    }

    internal void Synchronize(SpecialEvaluationDefinition updated)
    {
        definition = updated;
        SynchronizeSupportingColumns();
        RefreshAll();
    }

    internal void RefreshAll()
    {
        RefreshPrimarySourceColumn();
        OnPropertiesChanged(
            nameof(Id),
            nameof(DisplayName),
            nameof(PromptTemplate),
            nameof(Enabled),
            nameof(HasErrors),
            nameof(ValidationText),
            nameof(CardAutomationId),
            nameof(PromptAutomationId));
        moveUpCommand.RaiseCanExecuteChanged();
        moveDownCommand.RaiseCanExecuteChanged();
        SynchronizeSupportingColumns();
    }

    public override string ToString() =>
        $"{nameof(SpecialEvaluationDesignItemViewModel)} {{ Id = {Id}, Content = <redacted> }}";

    private void RefreshPrimarySourceColumn()
    {
        bool wasRefreshingPrimarySourceColumn = isRefreshingPrimarySourceColumn;
        isRefreshingPrimarySourceColumn = true;
        try
        {
            // TwoWay binding rereads the getter after a rejected writeback. Keep
            // the previously published value until the new candidates are applied.
            OnPropertyChanged(nameof(AvailableColumnNames));
            primarySourceColumn = definition.PrimarySourceColumn;
            OnPropertyChanged(nameof(PrimarySourceColumn));
        }
        finally
        {
            primarySourceColumn = definition.PrimarySourceColumn;
            isRefreshingPrimarySourceColumn = wasRefreshingPrimarySourceColumn;
        }
    }

    private void SynchronizeSupportingColumns()
    {
        Dictionary<string, SpecialSupportingColumnSelectionViewModel> existing = SupportingColumns
            .ToDictionary(item => item.ColumnName, StringComparer.OrdinalIgnoreCase);
        HashSet<string> selected = definition.SupportingSourceColumns
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<SpecialSupportingColumnSelectionViewModel> ordered = [];
        foreach (string column in owner.AvailableColumnNames)
        {
            if (!existing.Remove(column, out SpecialSupportingColumnSelectionViewModel? item))
            {
                item = new SpecialSupportingColumnSelectionViewModel(this, column, selected.Contains(column));
            }
            else
            {
                item.Synchronize(selected.Contains(column));
            }

            ordered.Add(item);
        }

        QuestionDesignItemViewModel.SynchronizeCollection(SupportingColumns, ordered);
    }
}

public sealed class EvaluatorDesignItemViewModel : UiObservableObject
{
    private readonly QuantificationDesignViewModel owner;
    private readonly string questionId;
    private EvaluatorDefinition definition;
    private readonly ViewModelCommand duplicateCommand;
    private readonly ViewModelCommand moveUpCommand;
    private readonly ViewModelCommand moveDownCommand;
    private readonly ViewModelCommand deleteCommand;
    private readonly ViewModelCommand addCriterionCommand;
    private CriterionDesignItemViewModel? selectedCriterion;

    internal EvaluatorDesignItemViewModel(
        QuantificationDesignViewModel owner,
        string questionId,
        EvaluatorDefinition definition)
    {
        this.owner = owner;
        this.questionId = questionId;
        this.definition = definition;
        Criteria = [];
        duplicateCommand = new ViewModelCommand(_ => owner.DuplicateEvaluator(questionId, Id));
        moveUpCommand = new ViewModelCommand(
            _ => owner.MoveEvaluatorUp(questionId, Id),
            _ => owner.CanMoveEvaluatorUp(questionId, Id));
        moveDownCommand = new ViewModelCommand(
            _ => owner.MoveEvaluatorDown(questionId, Id),
            _ => owner.CanMoveEvaluatorDown(questionId, Id));
        deleteCommand = new ViewModelCommand(_ => owner.DeleteEvaluator(questionId, Id));
        addCriterionCommand = new ViewModelCommand(_ => owner.AddCriterion(questionId, Id));
        SynchronizeCriteria();
    }

    public string Id => definition.Id;

    public string DisplayName
    {
        get => definition.DisplayName;
        set => owner.UpdateEvaluator(questionId, Id, evaluator => evaluator with { DisplayName = value ?? string.Empty });
    }

    public EvaluatorType Type
    {
        get => definition.Type;
        set => owner.ChangeEvaluatorType(questionId, Id, value);
    }

    public IReadOnlyList<EvaluatorTypeChoice> AvailableTypes => QuantificationDesignViewModel.EvaluatorTypes;

    public EvaluatorTypeChoice SelectedTypeChoice
    {
        get => QuantificationDesignViewModel.EvaluatorTypes.Single(choice => choice.Type == Type);
        set
        {
            if (value is not null)
            {
                Type = value.Type;
            }
        }
    }

    public string TypeDisplayName => Type switch
    {
        EvaluatorType.KnowledgeCoverage => "Knowledge",
        EvaluatorType.CustomPrompt => "Custom",
        _ => "Unknown",
    };

    public string TypeContractName => Type switch
    {
        EvaluatorType.KnowledgeCoverage => "KNOWLEDGE_COVERAGE",
        EvaluatorType.CustomPrompt => "CUSTOM_PROMPT",
        _ => "INVALID",
    };

    public bool IsKnowledge => Type == EvaluatorType.KnowledgeCoverage;

    public bool IsCustom => Type == EvaluatorType.CustomPrompt;

    public decimal Weight
    {
        get => definition.Weight;
        set => owner.UpdateEvaluator(questionId, Id, evaluator => evaluator with { Weight = value });
    }

    public decimal Minimum
    {
        get => definition.Range.Minimum;
        set => owner.UpdateEvaluator(questionId, Id, evaluator => evaluator with
        {
            Range = new ScoreRange(value, evaluator.Range.Maximum),
        });
    }

    public decimal Maximum
    {
        get => definition.Range.Maximum;
        set => owner.UpdateEvaluator(questionId, Id, evaluator => evaluator with
        {
            Range = new ScoreRange(evaluator.Range.Minimum, value),
        });
    }

    public bool Enabled
    {
        get => definition.Enabled;
        set => owner.UpdateEvaluator(questionId, Id, evaluator => evaluator with { Enabled = value });
    }

    public decimal EffectiveWeightPercentage => owner.GetEvaluatorWeightPercentage(questionId, Id);

    public string EffectiveWeightText => QuantificationDesignViewModel.FormatPercentage(EffectiveWeightPercentage);

    public string? CustomPromptTemplate
    {
        get => definition.CustomPromptTemplate;
        set
        {
            if (IsCustom)
            {
                owner.UpdateEvaluator(questionId, Id, evaluator => evaluator with
                {
                    CustomPromptTemplate = value ?? string.Empty,
                });
            }
        }
    }

    public bool IsPromptReadOnly => IsKnowledge;

    public string PromptPreview => owner.CreatePromptPreview(definition);

    public string PromptSurfaceLabel => IsKnowledge
        ? "App-owned semantic Prompt preview（読取専用）"
        : "Custom Prompt template（編集可能）";

    public string PromptGuidance => IsKnowledge
        ? "単語一致ではなく、説明・関係・適用の程度を評価します。"
        : "{回答} と {評価項目} が必須です。literal brace は {{ と }} で記述します。";

    public ObservableCollection<CriterionDesignItemViewModel> Criteria { get; }

    public CriterionDesignItemViewModel? SelectedCriterion
    {
        get => selectedCriterion;
        set => SetProperty(ref selectedCriterion, value);
    }

    public bool HasErrors => owner.HasErrorsFor(Id);

    public string ValidationText => owner.ValidationTextFor(Id);

    public string CardAutomationId => $"DesignEvaluator-{Id}";

    public string TypeAutomationId => $"DesignEvaluator-{Id}-Type";

    public string PromptAutomationId => $"DesignEvaluator-{Id}-Prompt";

    public string KnowledgePromptPreviewAutomationId => $"DesignEvaluator-{Id}-KnowledgePromptPreview";

    public string CustomPromptPreviewAutomationId => $"DesignEvaluator-{Id}-CustomPromptPreview";

    public string PromptPreviewAutomationId => IsKnowledge
        ? KnowledgePromptPreviewAutomationId
        : CustomPromptPreviewAutomationId;

    public ICommand DuplicateCommand => duplicateCommand;

    public ICommand MoveUpCommand => moveUpCommand;

    public ICommand MoveDownCommand => moveDownCommand;

    public ICommand DeleteCommand => deleteCommand;

    public ICommand AddCriterionCommand => addCriterionCommand;

    internal ScoreRange Range => definition.Range;

    internal void Synchronize(EvaluatorDefinition updated)
    {
        definition = updated;
        SynchronizeCriteria();
        RefreshAll();
    }

    internal void RefreshAll()
    {
        OnPropertiesChanged(
            nameof(Id),
            nameof(DisplayName),
            nameof(Type),
            nameof(SelectedTypeChoice),
            nameof(TypeDisplayName),
            nameof(TypeContractName),
            nameof(IsKnowledge),
            nameof(IsCustom),
            nameof(Weight),
            nameof(Minimum),
            nameof(Maximum),
            nameof(Enabled),
            nameof(EffectiveWeightPercentage),
            nameof(EffectiveWeightText),
            nameof(CustomPromptTemplate),
            nameof(IsPromptReadOnly),
            nameof(PromptPreview),
            nameof(PromptSurfaceLabel),
            nameof(PromptGuidance),
            nameof(HasErrors),
            nameof(ValidationText),
            nameof(CardAutomationId),
            nameof(TypeAutomationId),
            nameof(PromptAutomationId),
            nameof(KnowledgePromptPreviewAutomationId),
            nameof(CustomPromptPreviewAutomationId),
            nameof(PromptPreviewAutomationId));
        moveUpCommand.RaiseCanExecuteChanged();
        moveDownCommand.RaiseCanExecuteChanged();
        foreach (CriterionDesignItemViewModel criterion in Criteria)
        {
            criterion.RefreshAll();
        }
    }

    private void SynchronizeCriteria()
    {
        CriterionDesignItemViewModel? previousSelection = selectedCriterion;
        Dictionary<string, CriterionDesignItemViewModel> existing = Criteria
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        List<CriterionDesignItemViewModel> ordered = [];
        foreach (CriterionDefinition criterion in definition.Criteria)
        {
            if (!existing.Remove(criterion.Id, out CriterionDesignItemViewModel? item))
            {
                item = new CriterionDesignItemViewModel(owner, questionId, Id, criterion);
            }
            else
            {
                item.Synchronize(criterion);
            }

            ordered.Add(item);
        }

        SelectedCriterion = QuestionDesignItemViewModel.SynchronizeCollection(Criteria, ordered, previousSelection);
    }

    public override string ToString() =>
        $"{nameof(EvaluatorDesignItemViewModel)} {{ Id = {Id}, Type = {TypeContractName}, Content = <redacted> }}";
}

public sealed class CriterionDesignItemViewModel : UiObservableObject
{
    private readonly QuantificationDesignViewModel owner;
    private readonly string questionId;
    private readonly string evaluatorId;
    private CriterionDefinition definition;
    private readonly ViewModelCommand duplicateCommand;
    private readonly ViewModelCommand moveUpCommand;
    private readonly ViewModelCommand moveDownCommand;
    private readonly ViewModelCommand deleteCommand;

    internal CriterionDesignItemViewModel(
        QuantificationDesignViewModel owner,
        string questionId,
        string evaluatorId,
        CriterionDefinition definition)
    {
        this.owner = owner;
        this.questionId = questionId;
        this.evaluatorId = evaluatorId;
        this.definition = definition;
        duplicateCommand = new ViewModelCommand(_ => owner.DuplicateCriterion(questionId, evaluatorId, Id));
        moveUpCommand = new ViewModelCommand(
            _ => owner.MoveCriterionUp(questionId, evaluatorId, Id),
            _ => owner.CanMoveCriterionUp(questionId, evaluatorId, Id));
        moveDownCommand = new ViewModelCommand(
            _ => owner.MoveCriterionDown(questionId, evaluatorId, Id),
            _ => owner.CanMoveCriterionDown(questionId, evaluatorId, Id));
        deleteCommand = new ViewModelCommand(_ => owner.DeleteCriterion(questionId, evaluatorId, Id));
    }

    public string Id => definition.Id;

    public string DisplayName
    {
        get => definition.DisplayName;
        set => owner.UpdateCriterion(questionId, evaluatorId, Id, criterion => criterion with
        {
            DisplayName = value ?? string.Empty,
        });
    }

    public string Description
    {
        get => definition.Description;
        set => owner.UpdateCriterion(questionId, evaluatorId, Id, criterion => criterion with
        {
            Description = value ?? string.Empty,
        });
    }

    public decimal Weight
    {
        get => definition.Weight;
        set => owner.UpdateCriterion(questionId, evaluatorId, Id, criterion => criterion with { Weight = value });
    }

    public bool Enabled
    {
        get => definition.Enabled;
        set => owner.UpdateCriterion(questionId, evaluatorId, Id, criterion => criterion with { Enabled = value });
    }

    public bool HasCustomRange
    {
        get => definition.Range is not null;
        set => owner.SetCriterionCustomRangeEnabled(questionId, evaluatorId, Id, value);
    }

    public decimal Minimum
    {
        get => EffectiveRange.Minimum;
        set => owner.SetCriterionRangeMinimum(questionId, evaluatorId, Id, value);
    }

    public decimal Maximum
    {
        get => EffectiveRange.Maximum;
        set => owner.SetCriterionRangeMaximum(questionId, evaluatorId, Id, value);
    }

    public ScoreRange EffectiveRange => definition.Range ?? owner.GetEvaluatorRange(questionId, evaluatorId);

    public string EffectiveRangeText => definition.Range is null
        ? $"親 range を使用: {FormatRange(EffectiveRange)}"
        : $"個別 range: {FormatRange(EffectiveRange)}";

    public decimal EffectiveWeightPercentage => owner.GetCriterionWeightPercentage(questionId, evaluatorId, Id);

    public string EffectiveWeightText => QuantificationDesignViewModel.FormatPercentage(EffectiveWeightPercentage);

    public bool HasErrors => owner.HasErrorsFor(Id);

    public string ValidationText => owner.ValidationTextFor(Id);

    public string CardAutomationId => $"DesignCriterion-{Id}";

    public string NameAutomationId => $"DesignCriterion-{Id}-Name";

    public string RangeAutomationId => $"DesignCriterion-{Id}-Range";

    public ICommand DuplicateCommand => duplicateCommand;

    public ICommand MoveUpCommand => moveUpCommand;

    public ICommand MoveDownCommand => moveDownCommand;

    public ICommand DeleteCommand => deleteCommand;

    internal void Synchronize(CriterionDefinition updated)
    {
        definition = updated;
        RefreshAll();
    }

    internal void RefreshAll()
    {
        OnPropertiesChanged(
            nameof(Id),
            nameof(DisplayName),
            nameof(Description),
            nameof(Weight),
            nameof(Enabled),
            nameof(HasCustomRange),
            nameof(Minimum),
            nameof(Maximum),
            nameof(EffectiveRange),
            nameof(EffectiveRangeText),
            nameof(EffectiveWeightPercentage),
            nameof(EffectiveWeightText),
            nameof(HasErrors),
            nameof(ValidationText),
            nameof(CardAutomationId),
            nameof(NameAutomationId),
            nameof(RangeAutomationId));
        moveUpCommand.RaiseCanExecuteChanged();
        moveDownCommand.RaiseCanExecuteChanged();
    }

    private static string FormatRange(ScoreRange range) =>
        $"{range.Minimum.ToString("G29", CultureInfo.InvariantCulture)} ～ {range.Maximum.ToString("G29", CultureInfo.InvariantCulture)}";

    public override string ToString() =>
        $"{nameof(CriterionDesignItemViewModel)} {{ Id = {Id}, Content = <redacted> }}";
}

public sealed class QuantificationDesignViewModel : UiObservableObject
{
    private object? allocationVisualization;
    private static readonly IReadOnlyList<EvaluatorTypeChoice> ClosedEvaluatorTypes = Array.AsReadOnly(
    [
        new EvaluatorTypeChoice(EvaluatorType.KnowledgeCoverage, "Knowledge", "KNOWLEDGE_COVERAGE"),
        new EvaluatorTypeChoice(EvaluatorType.CustomPrompt, "Custom", "CUSTOM_PROMPT"),
    ]);
    private static readonly IReadOnlyList<ImportedPromptTargetChoice> ClosedPromptTargets = Array.AsReadOnly(
    [
        new ImportedPromptTargetChoice(ImportedPromptTarget.CustomEvaluator, "選択中のCustom evaluator"),
        new ImportedPromptTargetChoice(ImportedPromptTarget.SpecialEvaluation, "選択中の固有評価"),
    ]);

    private static readonly PromptRenderContext PreviewContext = new(
        "【設問 preview】",
        "【回答 preview】",
        "【補助情報 preview】",
        "【評価項目 preview】",
        "0",
        "10");

    private readonly QuantificationDefinitionValidator definitionValidator = new();
    private readonly ScoringAllocationCalculator allocationCalculator = new();
    private readonly PromptTemplateRenderer promptRenderer = new();
    private IReadOnlyList<string> availableColumnNames;
    private readonly ImmutableArray<ImportedPrompt> importedPromptSources;
    private readonly ObservableCollection<QuestionDesignItemViewModel> questionItems = [];
    private readonly ObservableCollection<QuestionDesignItemViewModel> visibleQuestionItems = [];
    private readonly ObservableCollection<ImportedPromptViewModel> importedPromptItems = [];
    private readonly ObservableCollection<DesignValidationError> validationErrorItems = [];
    private readonly ViewModelCommand addQuestionCommand;
    private readonly ViewModelCommand equalizeQuestionPointsCommand;
    private readonly ViewModelCommand applyImportedPromptCommand;
    private readonly ViewModelCommand validateCommand;
    private readonly ViewModelCommand previousPageCommand;
    private readonly ViewModelCommand nextPageCommand;
    private QuantificationDefinition draft;
    private QuestionDesignItemViewModel? selectedQuestion;
    private int pageSize = 4;
    private int pageIndex;
    private bool updatingPresentation;
    private ImportedPromptViewModel? selectedImportedPrompt;
    private ImportedPromptTarget selectedPromptTarget = ImportedPromptTarget.CustomEvaluator;

    public QuantificationDesignViewModel()
        : this(null, null, null)
    {
    }

    public QuantificationDesignViewModel(QuantificationDefinition? initialDefinition)
        : this(initialDefinition, null, null)
    {
    }

    public QuantificationDesignViewModel(
        QuantificationDefinition? initialDefinition,
        IEnumerable<string>? availableColumnNames,
        IEnumerable<ImportedPrompt>? importedPrompts = null)
    {
        QuantificationDefinition seed = initialDefinition ?? CreateSafeDefault();
        this.availableColumnNames = NormalizeAvailableColumns(seed, availableColumnNames);
        importedPromptSources = (importedPrompts ?? []).ToImmutableArray();
        if (importedPromptSources.Any(prompt => prompt is null))
        {
            throw new ArgumentException("Imported prompts cannot contain null values.", nameof(importedPrompts));
        }

        Questions = new ReadOnlyObservableCollection<QuestionDesignItemViewModel>(questionItems);
        VisibleQuestions = new ReadOnlyObservableCollection<QuestionDesignItemViewModel>(visibleQuestionItems);
        ImportedPrompts = new ReadOnlyObservableCollection<ImportedPromptViewModel>(importedPromptItems);
        ValidationErrors = new ReadOnlyObservableCollection<DesignValidationError>(validationErrorItems);
        AllocationChart = new AllocationChartViewModel(questionId =>
        {
            if (questionItems.FirstOrDefault(item => string.Equals(item.Id, questionId, StringComparison.Ordinal)) is { } question)
            {
                SelectedQuestion = question;
            }
        });
        allocationVisualization = AllocationChart;
        draft = CloneDefinition(seed);
        addQuestionCommand = new ViewModelCommand(_ => AddQuestion());
        equalizeQuestionPointsCommand = new ViewModelCommand(
            _ => EqualizeQuestionPoints(),
            _ => draft.Questions.Any(question => question.Enabled)
                && draft.BasePoints + draft.SpecialPoints <= 100m);
        applyImportedPromptCommand = new ViewModelCommand(
            _ => ApplyImportedPrompt(),
            _ => CanApplyImportedPrompt);
        validateCommand = new ViewModelCommand(_ => Revalidate());
        previousPageCommand = new ViewModelCommand(_ => PageIndex--, _ => pageIndex > 0);
        nextPageCommand = new ViewModelCommand(_ => PageIndex++, _ => pageIndex < LastPageIndex);
        foreach (ImportedPrompt prompt in importedPromptSources)
        {
            importedPromptItems.Add(new ImportedPromptViewModel(prompt));
        }

        selectedImportedPrompt = importedPromptItems.FirstOrDefault();
        SynchronizeQuestions();
        Revalidate();
    }

    public static IReadOnlyList<EvaluatorTypeChoice> EvaluatorTypes => ClosedEvaluatorTypes;

    public static IReadOnlyList<ImportedPromptTargetChoice> PromptTargets => ClosedPromptTargets;

    public IReadOnlyList<ImportedPromptTargetChoice> AvailablePromptTargets => ClosedPromptTargets;

    public ReadOnlyObservableCollection<QuestionDesignItemViewModel> Questions { get; }

    /// <summary>The current page contains the original editors from Questions, not copies.</summary>
    public ReadOnlyObservableCollection<QuestionDesignItemViewModel> VisibleQuestions { get; }

    /// <summary>The logical editing target; a separately browsed page need not contain it.</summary>
    public QuestionDesignItemViewModel? SelectedQuestion
    {
        get => selectedQuestion;
        set
        {
            // List changes can write back null (or an old item) through SelectedItem.
            // This presentation guard is independent of the input/primary-column guards.
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

    /// <summary>Zero-based and clamped to the available pages. Browsing does not change selection.</summary>
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

            int start = pageIndex * pageSize;
            int end = start + Math.Min(pageSize, questionItems.Count - start);
            return string.Format(CultureInfo.InvariantCulture,
                "{0:N0}–{1:N0} / {2:N0} 件", start + 1, end, questionItems.Count);
        }
    }

    public ReadOnlyObservableCollection<ImportedPromptViewModel> ImportedPrompts { get; }

    public IReadOnlyList<ImportedPrompt> ImportedPromptSources => importedPromptSources;

    public ImportedPromptViewModel? SelectedImportedPrompt
    {
        get => selectedImportedPrompt;
        set
        {
            if (SetProperty(ref selectedImportedPrompt, value))
            {
                OnPropertiesChanged(nameof(ImportedPromptPreview), nameof(CanApplyImportedPrompt));
                applyImportedPromptCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ImportedPromptTarget SelectedPromptTarget
    {
        get => selectedPromptTarget;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (SetProperty(ref selectedPromptTarget, value))
            {
                OnPropertiesChanged(
                    nameof(SelectedPromptTargetChoice),
                    nameof(SelectedPromptTargetSummary),
                    nameof(CanApplyImportedPrompt));
                applyImportedPromptCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ImportedPromptTargetChoice SelectedPromptTargetChoice
    {
        get => ClosedPromptTargets.Single(choice => choice.Target == SelectedPromptTarget);
        set
        {
            if (value is not null)
            {
                SelectedPromptTarget = value.Target;
            }
        }
    }

    public string ImportedPromptPreview => SelectedImportedPrompt?.Content
        ?? "command lineで --prompt を指定するとここに表示されます。";

    public string SelectedPromptTargetSummary => SelectedQuestion is not { } question
        ? "設問を選択してください"
        : SelectedPromptTarget switch
        {
            ImportedPromptTarget.CustomEvaluator when question.SelectedEvaluator?.IsCustom == true =>
                $"{question.DisplayName} → Custom: {question.SelectedEvaluator.DisplayName}",
            ImportedPromptTarget.CustomEvaluator =>
                $"{question.DisplayName} → Custom評価方法を選択してください",
            ImportedPromptTarget.SpecialEvaluation when question.SelectedSpecialEvaluation is not null =>
                $"{question.DisplayName} → 固有評価: {question.SelectedSpecialEvaluation.DisplayName}",
            ImportedPromptTarget.SpecialEvaluation =>
                $"{question.DisplayName} → 固有評価を選択してください",
            _ => throw new ArgumentOutOfRangeException(nameof(SelectedPromptTarget)),
        };

    public bool CanApplyImportedPrompt => SelectedImportedPrompt is not null
        && SelectedQuestion is not null
        && (SelectedPromptTarget switch
        {
            ImportedPromptTarget.CustomEvaluator => SelectedQuestion.SelectedEvaluator?.IsCustom == true,
            ImportedPromptTarget.SpecialEvaluation => SelectedQuestion.SelectedSpecialEvaluation is not null,
            _ => false,
        });

    public ReadOnlyObservableCollection<DesignValidationError> ValidationErrors { get; }

    public IReadOnlyList<string> AvailableColumnNames => availableColumnNames;

    public QuantificationDefinition Draft => draft;

    /// <summary>
    /// Content of the 「配点構成」 panel (FR-067): <see cref="AllocationChart"/> by default, or its empty state while it is null.
    /// </summary>
    public object? AllocationVisualization
    {
        get => allocationVisualization;
        set
        {
            if (SetProperty(ref allocationVisualization, value))
            {
                OnPropertyChanged(nameof(HasAllocationVisualization));
            }
        }
    }

    public bool HasAllocationVisualization => allocationVisualization is not null;

    /// <summary>The allocation stacked bar of the current draft (FR-067).</summary>
    public AllocationChartViewModel AllocationChart { get; }

    public string DefinitionName
    {
        get => draft.Name;
        set => Commit(draft with { Name = value ?? string.Empty });
    }

    public string Revision
    {
        get => draft.Revision;
        set => Commit(draft with { Revision = value ?? string.Empty });
    }

    public string SourceSummary => $"{draft.SourceSheet} · header {draft.HeaderRow.ToString(CultureInfo.InvariantCulture)} · rows {draft.FirstDataRow.ToString(CultureInfo.InvariantCulture)}–{draft.LastDataRow.ToString(CultureInfo.InvariantCulture)}";

    public int RoundingDigits
    {
        get => draft.RoundingDigits;
        set => Commit(draft with { RoundingDigits = value });
    }

    public decimal BasePoints
    {
        get => draft.BasePoints;
        set => Commit(draft with { BasePoints = value });
    }

    public decimal SpecialPoints
    {
        get => draft.SpecialPoints;
        set => Commit(draft with { SpecialPoints = value });
    }

    public decimal SimilarityPenaltyWeight
    {
        get => draft.SimilarityPenaltyWeight;
        set => Commit(draft with { SimilarityPenaltyWeight = value });
    }

    public decimal? AllocationTotal => allocationCalculator.Validate(
        draft.BasePoints,
        draft.SpecialPoints,
        draft.Questions.Where(question => question.Enabled).Select(question => question.Points)).Total;

    public decimal? AllocationRemaining => AllocationTotal is decimal total ? 100m - total : null;

    public bool IsAllocationValid => allocationCalculator.Validate(
        draft.BasePoints,
        draft.SpecialPoints,
        draft.Questions.Where(question => question.Enabled).Select(question => question.Points)).IsValid;

    public string AllocationSummary => AllocationTotal is decimal total
        ? $"配点合計 {total.ToString("G29", CultureInfo.InvariantCulture)} / 100 · 残り {(100m - total).ToString("G29", CultureInfo.InvariantCulture)}"
        : "配点合計を計算できません。";

    public bool IsValid => validationErrorItems.Count == 0;

    public bool CanBuildSnapshot => IsValid;

    public bool HasTechnicalErrors => !IsValid;

    public string ValidationSummary => IsValid
        ? "設計は有効です。現在の draft から immutable snapshot を作成できます。"
        : $"技術的な設定エラーが {validationErrorItems.Count.ToString(CultureInfo.InvariantCulture)} 件あります。";

    public string HierarchySummary => $"{draft.Questions.Length.ToString(CultureInfo.InvariantCulture)} questions · {draft.Questions.Sum(question => question.Evaluators.Length).ToString(CultureInfo.InvariantCulture)} evaluators · {draft.Questions.Sum(question => question.Evaluators.Sum(evaluator => evaluator.Criteria.Length)).ToString(CultureInfo.InvariantCulture)} criteria";

    public ICommand AddQuestionCommand => addQuestionCommand;

    public ICommand EqualizeQuestionPointsCommand => equalizeQuestionPointsCommand;

    public ICommand ApplyImportedPromptCommand => applyImportedPromptCommand;

    public ICommand ValidateCommand => validateCommand;

    public ICommand PreviousPageCommand => previousPageCommand;

    public ICommand NextPageCommand => nextPageCommand;

    private int LastPageIndex => questionItems.Count == 0 ? 0 : (questionItems.Count - 1) / pageSize;

    internal bool IsSynchronizingFromInput { get; private set; }

    /// <summary>
    /// Synchronizes the current input draft without replacing surviving editor VMs
    /// or applying imported prompts. The caller owns the Input/Design edit boundary.
    /// </summary>
    public void SynchronizeFromInput(
        QuantificationDefinition definition,
        IReadOnlyList<string> availableColumns)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(availableColumns);
        IReadOnlyList<string> columns = NormalizeAvailableColumns(definition, availableColumns);
        bool columnsChanged = !availableColumnNames.SequenceEqual(columns, StringComparer.Ordinal);
        bool definitionChanged = !HasSameContent(draft, definition);
        if (!columnsChanged && !definitionChanged)
        {
            return;
        }

        bool wasSynchronizingFromInput = IsSynchronizingFromInput;
        IsSynchronizingFromInput = true;
        try
        {
            if (columnsChanged)
            {
                availableColumnNames = columns;
            }

            if (definitionChanged)
            {
                Commit(definition);
                NotifyPromptTargetChanged();
            }
            else
            {
                foreach (QuestionDesignItemViewModel question in questionItems)
                {
                    question.RefreshAll();
                }
            }

            if (columnsChanged)
            {
                OnPropertyChanged(nameof(AvailableColumnNames));
            }
        }
        finally
        {
            IsSynchronizingFromInput = wasSynchronizingFromInput;
        }
    }

    public void ApplyImportedPrompt()
    {
        if (!CanApplyImportedPrompt
            || SelectedImportedPrompt is null
            || SelectedQuestion is null)
        {
            return;
        }

        string template = SelectedImportedPrompt.Content;
        switch (SelectedPromptTarget)
        {
            case ImportedPromptTarget.CustomEvaluator:
                EvaluatorDesignItemViewModel evaluator = SelectedQuestion.SelectedEvaluator!;
                evaluator.CustomPromptTemplate = template;
                break;
            case ImportedPromptTarget.SpecialEvaluation:
                SpecialEvaluationDesignItemViewModel special = SelectedQuestion.SelectedSpecialEvaluation!;
                special.PromptTemplate = template;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(SelectedPromptTarget));
        }

        NotifyPromptTargetChanged();
    }

    public void EqualizeQuestionPoints()
    {
        QuestionDefinition[] enabled = draft.Questions.Where(question => question.Enabled).ToArray();
        ImmutableArray<decimal> points = allocationCalculator.Equalize(
            draft.BasePoints,
            draft.SpecialPoints,
            enabled.Length);
        int pointIndex = 0;
        ImmutableArray<QuestionDefinition> questions = draft.Questions
            .Select(question => question.Enabled
                ? question with { Points = points[pointIndex++] }
                : question)
            .ToImmutableArray();
        Commit(draft with { Questions = questions });
    }

    public QuestionDesignItemViewModel AddQuestion()
    {
        string primary = draft.Questions.FirstOrDefault()?.PrimarySourceColumn ?? "A";
        QuestionDefinition question = CreateDefaultQuestion(
            NewId("question"),
            questionItems.Count + 1,
            primary);
        string id = question.Id;
        Commit(draft.AddQuestion(question));
        return questionItems.Single(item => string.Equals(item.Id, id, StringComparison.Ordinal));
    }

    public QuestionDesignItemViewModel DuplicateQuestion(string questionId)
    {
        int index = FindQuestionIndex(questionId);
        QuantificationDefinition next = draft.DuplicateQuestion(
            index,
            NewId("question"),
            _ => NewId("evaluator"),
            _ => NewId("criterion"),
            _ => NewId("special"));
        string duplicateId = next.Questions[index + 1].Id;
        Commit(next);
        return questionItems.Single(item => string.Equals(item.Id, duplicateId, StringComparison.Ordinal));
    }

    public bool CanMoveQuestionUp(string questionId) => FindQuestionIndex(questionId, false) > 0;

    public bool CanMoveQuestionDown(string questionId)
    {
        int index = FindQuestionIndex(questionId, false);
        return index >= 0 && index < draft.Questions.Length - 1;
    }

    public void MoveQuestionUp(string questionId)
    {
        int index = FindQuestionIndex(questionId);
        if (index > 0)
        {
            Commit(draft.MoveQuestion(index, index - 1));
        }
    }

    public void MoveQuestionDown(string questionId)
    {
        int index = FindQuestionIndex(questionId);
        if (index < draft.Questions.Length - 1)
        {
            Commit(draft.MoveQuestion(index, index + 1));
        }
    }

    public void DeleteQuestion(string questionId) =>
        Commit(draft.RemoveQuestion(FindQuestionIndex(questionId)));

    public EvaluatorDesignItemViewModel AddEvaluator(string questionId, EvaluatorType type)
    {
        EnsureEvaluatorType(type);
        string evaluatorId = NewId("evaluator");
        EvaluatorDefinition evaluator = CreateDefaultEvaluator(evaluatorId, type, 1);
        UpdateQuestion(questionId, question => question.AddEvaluator(evaluator));
        return FindQuestionItem(questionId).Evaluators.Single(item => item.Id == evaluatorId);
    }

    public EvaluatorDesignItemViewModel DuplicateEvaluator(string questionId, string evaluatorId)
    {
        int evaluatorIndex = FindEvaluatorIndex(questionId, evaluatorId);
        string duplicateId = NewId("evaluator");
        UpdateQuestion(questionId, question => question.DuplicateEvaluator(
            evaluatorIndex,
            duplicateId,
            _ => NewId("criterion")));
        return FindQuestionItem(questionId).Evaluators.Single(item => item.Id == duplicateId);
    }

    public bool CanMoveEvaluatorUp(string questionId, string evaluatorId) =>
        FindEvaluatorIndex(questionId, evaluatorId, false) > 0;

    public bool CanMoveEvaluatorDown(string questionId, string evaluatorId)
    {
        int questionIndex = FindQuestionIndex(questionId, false);
        int evaluatorIndex = FindEvaluatorIndex(questionId, evaluatorId, false);
        return questionIndex >= 0
            && evaluatorIndex >= 0
            && evaluatorIndex < draft.Questions[questionIndex].Evaluators.Length - 1;
    }

    public void MoveEvaluatorUp(string questionId, string evaluatorId)
    {
        int index = FindEvaluatorIndex(questionId, evaluatorId);
        if (index > 0)
        {
            UpdateQuestion(questionId, question => question with
            {
                Evaluators = question.MoveEvaluator(index, index - 1).Evaluators,
            });
        }
    }

    public void MoveEvaluatorDown(string questionId, string evaluatorId)
    {
        int questionIndex = FindQuestionIndex(questionId);
        int evaluatorIndex = FindEvaluatorIndex(questionId, evaluatorId);
        if (evaluatorIndex < draft.Questions[questionIndex].Evaluators.Length - 1)
        {
            UpdateQuestion(questionId, question => question with
            {
                Evaluators = question.MoveEvaluator(evaluatorIndex, evaluatorIndex + 1).Evaluators,
            });
        }
    }

    public void DeleteEvaluator(string questionId, string evaluatorId)
    {
        int evaluatorIndex = FindEvaluatorIndex(questionId, evaluatorId);
        UpdateQuestion(questionId, question => question.RemoveEvaluator(evaluatorIndex));
    }

    public SpecialEvaluationDesignItemViewModel AddSpecialEvaluation(string questionId)
    {
        QuestionDefinition question = GetQuestion(questionId);
        string specialId = NewId("special");
        string primaryColumn = availableColumnNames.FirstOrDefault()
            ?? question.PrimarySourceColumn;
        SpecialEvaluationDefinition special = new()
        {
            Id = specialId,
            DisplayName = $"固有評価 {question.SpecialEvaluations.Length + 1}",
            PrimarySourceColumn = primaryColumn,
            SupportingSourceColumns = [],
            PromptTemplate = "次の内容を固有観点で評価してください。\n{回答}",
            Enabled = true,
        };
        UpdateQuestion(questionId, item => item.AddSpecialEvaluation(special));
        return FindQuestionItem(questionId).SpecialEvaluations.Single(item => item.Id == specialId);
    }

    public SpecialEvaluationDesignItemViewModel DuplicateSpecialEvaluation(
        string questionId,
        string specialEvaluationId)
    {
        int index = FindSpecialEvaluationIndex(questionId, specialEvaluationId);
        string duplicateId = NewId("special");
        UpdateQuestion(questionId, question => question.DuplicateSpecialEvaluation(index, duplicateId));
        return FindQuestionItem(questionId).SpecialEvaluations.Single(item => item.Id == duplicateId);
    }

    public bool CanMoveSpecialEvaluationUp(string questionId, string specialEvaluationId) =>
        FindSpecialEvaluationIndex(questionId, specialEvaluationId, throwIfMissing: false) > 0;

    public bool CanMoveSpecialEvaluationDown(string questionId, string specialEvaluationId)
    {
        QuestionDefinition? question = TryGetQuestion(questionId);
        int index = FindSpecialEvaluationIndex(questionId, specialEvaluationId, throwIfMissing: false);
        return question is not null && index >= 0 && index < question.SpecialEvaluations.Length - 1;
    }

    public void MoveSpecialEvaluationUp(string questionId, string specialEvaluationId)
    {
        int index = FindSpecialEvaluationIndex(questionId, specialEvaluationId);
        if (index > 0)
        {
            UpdateQuestion(questionId, question => question.MoveSpecialEvaluation(index, index - 1));
        }
    }

    public void MoveSpecialEvaluationDown(string questionId, string specialEvaluationId)
    {
        QuestionDefinition question = GetQuestion(questionId);
        int index = FindSpecialEvaluationIndex(questionId, specialEvaluationId);
        if (index < question.SpecialEvaluations.Length - 1)
        {
            UpdateQuestion(questionId, item => item.MoveSpecialEvaluation(index, index + 1));
        }
    }

    public void DeleteSpecialEvaluation(string questionId, string specialEvaluationId)
    {
        int index = FindSpecialEvaluationIndex(questionId, specialEvaluationId);
        UpdateQuestion(questionId, question => question.RemoveSpecialEvaluation(index));
    }

    internal void UpdateSpecialEvaluation(
        string questionId,
        string specialEvaluationId,
        Func<SpecialEvaluationDefinition, SpecialEvaluationDefinition> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        int index = FindSpecialEvaluationIndex(questionId, specialEvaluationId);
        QuestionDefinition question = GetQuestion(questionId);
        SpecialEvaluationDefinition current = question.SpecialEvaluations[index];
        SpecialEvaluationDefinition updated = update(current);
        if (updated == current)
        {
            return;
        }

        UpdateQuestion(questionId, item => item with
        {
            SpecialEvaluations = item.SpecialEvaluations.SetItem(index, updated),
        });
    }

    public CriterionDesignItemViewModel AddCriterion(string questionId, string evaluatorId)
    {
        int count = GetEvaluator(questionId, evaluatorId).Criteria.Length;
        string criterionId = NewId("criterion");
        CriterionDefinition criterion = CreateDefaultCriterion(
            criterionId,
            count + 1,
            GetEvaluator(questionId, evaluatorId).Type);
        UpdateEvaluator(questionId, evaluatorId, evaluator => evaluator.AddCriterion(criterion));
        return FindEvaluatorItem(questionId, evaluatorId).Criteria.Single(item => item.Id == criterionId);
    }

    public CriterionDesignItemViewModel DuplicateCriterion(
        string questionId,
        string evaluatorId,
        string criterionId)
    {
        int criterionIndex = FindCriterionIndex(questionId, evaluatorId, criterionId);
        string duplicateId = NewId("criterion");
        UpdateEvaluator(questionId, evaluatorId, evaluator => evaluator.DuplicateCriterion(
            criterionIndex,
            duplicateId));
        return FindEvaluatorItem(questionId, evaluatorId).Criteria.Single(item => item.Id == duplicateId);
    }

    public bool CanMoveCriterionUp(string questionId, string evaluatorId, string criterionId) =>
        FindCriterionIndex(questionId, evaluatorId, criterionId, false) > 0;

    public bool CanMoveCriterionDown(string questionId, string evaluatorId, string criterionId)
    {
        EvaluatorDefinition? evaluator = TryGetEvaluator(questionId, evaluatorId);
        int index = FindCriterionIndex(questionId, evaluatorId, criterionId, false);
        return evaluator is not null && index >= 0 && index < evaluator.Criteria.Length - 1;
    }

    public void MoveCriterionUp(string questionId, string evaluatorId, string criterionId)
    {
        int index = FindCriterionIndex(questionId, evaluatorId, criterionId);
        if (index > 0)
        {
            UpdateEvaluator(questionId, evaluatorId, evaluator => evaluator.MoveCriterion(index, index - 1));
        }
    }

    public void MoveCriterionDown(string questionId, string evaluatorId, string criterionId)
    {
        EvaluatorDefinition evaluator = GetEvaluator(questionId, evaluatorId);
        int index = FindCriterionIndex(questionId, evaluatorId, criterionId);
        if (index < evaluator.Criteria.Length - 1)
        {
            UpdateEvaluator(questionId, evaluatorId, item => item.MoveCriterion(index, index + 1));
        }
    }

    public void DeleteCriterion(string questionId, string evaluatorId, string criterionId)
    {
        int criterionIndex = FindCriterionIndex(questionId, evaluatorId, criterionId);
        UpdateEvaluator(questionId, evaluatorId, evaluator => evaluator.RemoveCriterion(criterionIndex));
    }

    public void SetCriterionCustomRangeEnabled(
        string questionId,
        string evaluatorId,
        string criterionId,
        bool enabled)
    {
        ScoreRange parentRange = GetEvaluatorRange(questionId, evaluatorId);
        UpdateCriterion(questionId, evaluatorId, criterionId, criterion => criterion with
        {
            Range = enabled ? criterion.Range ?? parentRange : null,
        });
    }

    public void SetCriterionRangeMinimum(
        string questionId,
        string evaluatorId,
        string criterionId,
        decimal minimum)
    {
        ScoreRange range = GetCriterion(questionId, evaluatorId, criterionId).Range
            ?? GetEvaluatorRange(questionId, evaluatorId);
        UpdateCriterion(questionId, evaluatorId, criterionId, criterion => criterion with
        {
            Range = new ScoreRange(minimum, range.Maximum),
        });
    }

    public void SetCriterionRangeMaximum(
        string questionId,
        string evaluatorId,
        string criterionId,
        decimal maximum)
    {
        ScoreRange range = GetCriterion(questionId, evaluatorId, criterionId).Range
            ?? GetEvaluatorRange(questionId, evaluatorId);
        UpdateCriterion(questionId, evaluatorId, criterionId, criterion => criterion with
        {
            Range = new ScoreRange(range.Minimum, maximum),
        });
    }

    public void ChangeEvaluatorType(string questionId, string evaluatorId, EvaluatorType type)
    {
        EnsureEvaluatorType(type);
        UpdateEvaluator(questionId, evaluatorId, evaluator => type switch
        {
            EvaluatorType.KnowledgeCoverage => evaluator with
            {
                Type = type,
                BuiltInTemplateVersion = BuiltInPromptTemplates.KnowledgeTemplateVersion,
                CustomPromptTemplate = null,
            },
            EvaluatorType.CustomPrompt => evaluator with
            {
                Type = type,
                BuiltInTemplateVersion = null,
                CustomPromptTemplate = string.IsNullOrWhiteSpace(evaluator.CustomPromptTemplate)
                    ? BuiltInPromptTemplates.CustomPromptPreset
                    : evaluator.CustomPromptTemplate,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        });
        NotifyPromptTargetChanged();
    }

    public bool TryBuildSnapshot(out QuantificationSnapshot? snapshot)
    {
        Revalidate();
        if (!IsValid)
        {
            snapshot = null;
            return false;
        }

        snapshot = QuantificationSnapshot.Create(CloneDefinition(draft));
        return true;
    }

    public QuantificationSnapshot BuildSnapshot()
    {
        if (!TryBuildSnapshot(out QuantificationSnapshot? snapshot))
        {
            throw new QuantificationDesignValidationException(validationErrorItems.Count);
        }

        return snapshot!;
    }

    internal void UpdateQuestion(
        string questionId,
        Func<QuestionDefinition, QuestionDefinition> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        int questionIndex = FindQuestionIndex(questionId);
        QuestionDefinition current = draft.Questions[questionIndex];
        QuestionDefinition updated = update(current);
        if (updated == current)
        {
            return;
        }

        Commit(draft with { Questions = draft.Questions.SetItem(questionIndex, updated) });
    }

    internal void NotifyPromptTargetChanged()
    {
        OnPropertiesChanged(nameof(SelectedPromptTargetSummary), nameof(CanApplyImportedPrompt));
        applyImportedPromptCommand.RaiseCanExecuteChanged();
    }

    internal void UpdateEvaluator(
        string questionId,
        string evaluatorId,
        Func<EvaluatorDefinition, EvaluatorDefinition> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        int questionIndex = FindQuestionIndex(questionId);
        int evaluatorIndex = FindEvaluatorIndex(questionId, evaluatorId);
        QuestionDefinition question = draft.Questions[questionIndex];
        EvaluatorDefinition current = question.Evaluators[evaluatorIndex];
        EvaluatorDefinition updated = update(current);
        if (updated == current)
        {
            return;
        }

        UpdateQuestion(questionId, item => item with
        {
            Evaluators = item.Evaluators.SetItem(evaluatorIndex, updated),
        });
    }

    internal void UpdateCriterion(
        string questionId,
        string evaluatorId,
        string criterionId,
        Func<CriterionDefinition, CriterionDefinition> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        int criterionIndex = FindCriterionIndex(questionId, evaluatorId, criterionId);
        CriterionDefinition current = GetEvaluator(questionId, evaluatorId).Criteria[criterionIndex];
        CriterionDefinition updated = update(current);
        if (updated == current)
        {
            return;
        }

        UpdateEvaluator(questionId, evaluatorId, evaluator => evaluator with
        {
            Criteria = evaluator.Criteria.SetItem(
                criterionIndex,
                updated),
        });
    }

    internal decimal GetQuestionWeightPercentage(string questionId) =>
        Percentage(
            draft.Questions,
            question => question.Id,
            question => question.Points,
            question => question.Enabled,
            questionId);

    internal decimal GetEvaluatorWeightPercentage(string questionId, string evaluatorId) =>
        Percentage(
            GetQuestion(questionId).Evaluators,
            evaluator => evaluator.Id,
            evaluator => evaluator.Weight,
            evaluator => evaluator.Enabled,
            evaluatorId);

    internal decimal GetCriterionWeightPercentage(
        string questionId,
        string evaluatorId,
        string criterionId) =>
        Percentage(
            GetEvaluator(questionId, evaluatorId).Criteria,
            criterion => criterion.Id,
            criterion => criterion.Weight,
            criterion => criterion.Enabled,
            criterionId);

    internal ScoreRange GetEvaluatorRange(string questionId, string evaluatorId) =>
        GetEvaluator(questionId, evaluatorId).Range;

    internal bool HasErrorsFor(string nodeId) => validationErrorItems.Any(error => string.Equals(
        error.NodeId,
        nodeId,
        StringComparison.Ordinal));

    internal string ValidationTextFor(string nodeId)
    {
        string[] messages = validationErrorItems
            .Where(error => string.Equals(error.NodeId, nodeId, StringComparison.Ordinal))
            .Select(error => $"{error.Field}: {error.Message}")
            .ToArray();
        return string.Join(Environment.NewLine, messages);
    }

    internal string CreatePromptPreview(EvaluatorDefinition evaluator)
    {
        if (evaluator.Type == EvaluatorType.KnowledgeCoverage)
        {
            return BuiltInPromptTemplates.KnowledgeSemanticTemplate
                + Environment.NewLine
                + Environment.NewLine
                + BuiltInPromptTemplates.StructuredOutputInstruction;
        }

        if (evaluator.Type != EvaluatorType.CustomPrompt
            || string.IsNullOrWhiteSpace(evaluator.CustomPromptTemplate))
        {
            return "Preview は technical validation の解消後に表示されます。";
        }

        try
        {
            return promptRenderer.Render(evaluator.CustomPromptTemplate, PreviewContext)
                + Environment.NewLine
                + Environment.NewLine
                + BuiltInPromptTemplates.StructuredOutputInstruction;
        }
        catch (PromptConfigurationException)
        {
            return "Preview は technical validation の解消後に表示されます。";
        }
    }

    private void Commit(QuantificationDefinition next)
    {
        if (next == draft)
        {
            return;
        }

        draft = CloneDefinition(next);
        SynchronizeQuestions();
        Revalidate();
        OnPropertiesChanged(
            nameof(Draft),
            nameof(DefinitionName),
            nameof(Revision),
            nameof(SourceSummary),
            nameof(RoundingDigits),
            nameof(BasePoints),
            nameof(SpecialPoints),
            nameof(SimilarityPenaltyWeight),
            nameof(AllocationTotal),
            nameof(AllocationRemaining),
            nameof(IsAllocationValid),
            nameof(AllocationSummary),
            nameof(SelectedPromptTargetSummary),
            nameof(HierarchySummary));
        equalizeQuestionPointsCommand.RaiseCanExecuteChanged();
    }

    private void SynchronizeQuestions()
    {
        bool wasUpdatingPresentation = updatingPresentation;
        updatingPresentation = true;
        try
        {
            QuestionDesignItemViewModel? previousSelection = selectedQuestion;
            Dictionary<string, QuestionDesignItemViewModel> existing = questionItems
                .ToDictionary(item => item.Id, StringComparer.Ordinal);
            List<QuestionDesignItemViewModel> ordered = [];
            foreach (QuestionDefinition question in draft.Questions)
            {
                if (!existing.Remove(question.Id, out QuestionDesignItemViewModel? item))
                {
                    item = new QuestionDesignItemViewModel(this, question);
                }
                else
                {
                    item.Synchronize(question);
                }

                ordered.Add(item);
            }

            bool orderChanged = !questionItems.SequenceEqual(ordered);
            QuestionDesignItemViewModel? nextSelection = QuestionDesignItemViewModel.SynchronizeCollection(
                questionItems, ordered, previousSelection);
            // Structural edits follow the surviving selection or T05's adjacent fallback.
            // Value-only edits leave a separately browsed page in place.
            int nextPageIndex = orderChanged && nextSelection is not null
                ? questionItems.IndexOf(nextSelection) / pageSize
                : pageIndex;
            UpdatePresentation(nextSelection, nextPageIndex, pageSize);
        }
        finally
        {
            updatingPresentation = wasUpdatingPresentation;
        }
    }

    private void UpdatePresentation(
        QuestionDesignItemViewModel? selection,
        int requestedPageIndex,
        int requestedPageSize)
    {
        bool wasUpdatingPresentation = updatingPresentation;
        updatingPresentation = true;
        try
        {
            bool sizeChanged = pageSize != requestedPageSize;
            bool selectionChanged = !ReferenceEquals(selectedQuestion, selection);
            pageSize = requestedPageSize;
            int nextPageIndex = Math.Clamp(requestedPageIndex, 0, LastPageIndex);
            bool pageChanged = pageIndex != nextPageIndex;
            pageIndex = nextPageIndex;
            QuestionDesignItemViewModel[] visible = questionItems.Skip(pageIndex * pageSize).Take(pageSize).ToArray();
            QuestionDesignItemViewModel.SynchronizeCollection(visibleQuestionItems, visible);
            selectedQuestion = selection;
            AllocationChart.SetSelectedQuestion(selection?.Id);

            if (sizeChanged)
            {
                OnPropertyChanged(nameof(PageSize));
            }

            if (pageChanged)
            {
                OnPropertyChanged(nameof(PageIndex));
            }

            // Signal settled items while writebacks are still guarded. The view uses
            // UpdateTarget: a compiled TwoWay binding suppresses same-reference notifications.
            OnPropertiesChanged(nameof(PageSummary), nameof(SelectedQuestion));
            if (selectionChanged)
            {
                NotifyPromptTargetChanged();
            }

            previousPageCommand.RaiseCanExecuteChanged();
            nextPageCommand.RaiseCanExecuteChanged();
        }
        finally
        {
            updatingPresentation = wasUpdatingPresentation;
        }
    }

    private void Revalidate()
    {
        Dictionary<string, DesignValidationError> errors = new(StringComparer.Ordinal);
        DefinitionValidationResult definitionResult = definitionValidator.Validate(draft);
        foreach (DefinitionValidationError error in definitionResult.Errors)
        {
            AddError(errors, new DesignValidationError(
                error.Code,
                error.NodeKind,
                error.NodeId,
                error.Field,
                ValidationMessage(error)));
        }

        foreach (QuestionDefinition question in draft.Questions)
        {
            foreach (EvaluatorDefinition evaluator in question.Evaluators)
            {
                ValidateEvaluatorPrompt(evaluator, errors);
            }
        }

        validationErrorItems.Clear();
        foreach (DesignValidationError error in errors.Values)
        {
            validationErrorItems.Add(error);
        }

        OnPropertiesChanged(
            nameof(IsValid),
            nameof(CanBuildSnapshot),
            nameof(HasTechnicalErrors),
            nameof(ValidationSummary));
        foreach (QuestionDesignItemViewModel question in questionItems)
        {
            question.RefreshAll();
        }

        AllocationChart.Update(draft, allocationCalculator);
    }

    private void ValidateEvaluatorPrompt(
        EvaluatorDefinition evaluator,
        IDictionary<string, DesignValidationError> errors)
    {
        if (evaluator.Type == EvaluatorType.KnowledgeCoverage)
        {
            if (!string.Equals(
                evaluator.BuiltInTemplateVersion,
                BuiltInPromptTemplates.KnowledgeTemplateVersion,
                StringComparison.Ordinal))
            {
                AddError(errors, PromptError(
                    "KNOWLEDGE_TEMPLATE_VERSION_INVALID",
                    evaluator.Id,
                    "BuiltInTemplateVersion",
                    "Knowledge は app-owned template version を使用する必要があります。"));
            }

            if (!string.IsNullOrEmpty(evaluator.CustomPromptTemplate))
            {
                AddError(errors, PromptError(
                    "KNOWLEDGE_CUSTOM_TEMPLATE_FORBIDDEN",
                    evaluator.Id,
                    "CustomPromptTemplate",
                    "Knowledge の semantic Prompt は編集できません。別の分析には Custom を使用してください。"));
            }

            return;
        }

        if (evaluator.Type != EvaluatorType.CustomPrompt)
        {
            return;
        }

        if (!string.IsNullOrEmpty(evaluator.BuiltInTemplateVersion))
        {
            AddError(errors, PromptError(
                "CUSTOM_BUILT_IN_TEMPLATE_FORBIDDEN",
                evaluator.Id,
                "BuiltInTemplateVersion",
                "Custom は利用者が編集する Prompt template を使用します。"));
        }

        try
        {
            _ = promptRenderer.Render(evaluator.CustomPromptTemplate ?? string.Empty, PreviewContext);
        }
        catch (PromptConfigurationException exception)
        {
            AddError(errors, PromptError(
                exception.Code,
                evaluator.Id,
                "CustomPromptTemplate",
                PromptValidationMessage(exception.Code)));
        }
    }

    private static DesignValidationError PromptError(
        string code,
        string evaluatorId,
        string field,
        string message) => new(code, "Evaluator", evaluatorId, field, message);

    private static void AddError(
        IDictionary<string, DesignValidationError> errors,
        DesignValidationError error)
    {
        string key = $"{error.Code}|{error.NodeKind}|{error.NodeId}|{error.Field}";
        errors.TryAdd(key, error);
    }

    private int FindQuestionIndex(string questionId, bool throwIfMissing = true)
    {
        int index = -1;
        for (int candidateIndex = 0; candidateIndex < draft.Questions.Length; candidateIndex++)
        {
            if (string.Equals(
                draft.Questions[candidateIndex].Id,
                questionId,
                StringComparison.Ordinal))
            {
                index = candidateIndex;
                break;
            }
        }

        if (index < 0 && throwIfMissing)
        {
            throw new ArgumentException("The question identity does not exist in the design draft.", nameof(questionId));
        }

        return index;
    }

    private int FindEvaluatorIndex(
        string questionId,
        string evaluatorId,
        bool throwIfMissing = true)
    {
        int questionIndex = FindQuestionIndex(questionId, throwIfMissing);
        if (questionIndex < 0)
        {
            return -1;
        }

        ImmutableArray<EvaluatorDefinition> evaluators = draft.Questions[questionIndex].Evaluators;
        int index = -1;
        for (int candidateIndex = 0; candidateIndex < evaluators.Length; candidateIndex++)
        {
            if (string.Equals(evaluators[candidateIndex].Id, evaluatorId, StringComparison.Ordinal))
            {
                index = candidateIndex;
                break;
            }
        }

        if (index < 0 && throwIfMissing)
        {
            throw new ArgumentException("The evaluator identity does not exist in the design draft.", nameof(evaluatorId));
        }

        return index;
    }

    private int FindCriterionIndex(
        string questionId,
        string evaluatorId,
        string criterionId,
        bool throwIfMissing = true)
    {
        EvaluatorDefinition? evaluator = TryGetEvaluator(questionId, evaluatorId);
        int index = -1;
        if (evaluator is not null)
        {
            for (int candidateIndex = 0; candidateIndex < evaluator.Criteria.Length; candidateIndex++)
            {
                if (string.Equals(
                    evaluator.Criteria[candidateIndex].Id,
                    criterionId,
                    StringComparison.Ordinal))
                {
                    index = candidateIndex;
                    break;
                }
            }
        }

        if (index < 0 && throwIfMissing)
        {
            throw new ArgumentException("The criterion identity does not exist in the design draft.", nameof(criterionId));
        }

        return index;
    }

    private int FindSpecialEvaluationIndex(
        string questionId,
        string specialEvaluationId,
        bool throwIfMissing = true)
    {
        QuestionDefinition? question = TryGetQuestion(questionId);
        int index = -1;
        if (question is not null)
        {
            for (int candidateIndex = 0;
                 candidateIndex < question.SpecialEvaluations.Length;
                 candidateIndex++)
            {
                if (string.Equals(
                    question.SpecialEvaluations[candidateIndex].Id,
                    specialEvaluationId,
                    StringComparison.Ordinal))
                {
                    index = candidateIndex;
                    break;
                }
            }
        }

        if (index < 0 && throwIfMissing)
        {
            throw new ArgumentException(
                "The special-evaluation identity does not exist in the design draft.",
                nameof(specialEvaluationId));
        }

        return index;
    }

    private QuestionDefinition GetQuestion(string questionId) =>
        draft.Questions[FindQuestionIndex(questionId)];

    private QuestionDefinition? TryGetQuestion(string questionId)
    {
        int index = FindQuestionIndex(questionId, throwIfMissing: false);
        return index < 0 ? null : draft.Questions[index];
    }

    private EvaluatorDefinition GetEvaluator(string questionId, string evaluatorId) =>
        GetQuestion(questionId).Evaluators[FindEvaluatorIndex(questionId, evaluatorId)];

    private EvaluatorDefinition? TryGetEvaluator(string questionId, string evaluatorId)
    {
        int questionIndex = FindQuestionIndex(questionId, false);
        if (questionIndex < 0)
        {
            return null;
        }

        int evaluatorIndex = FindEvaluatorIndex(questionId, evaluatorId, false);
        return evaluatorIndex < 0 ? null : draft.Questions[questionIndex].Evaluators[evaluatorIndex];
    }

    private CriterionDefinition GetCriterion(
        string questionId,
        string evaluatorId,
        string criterionId) =>
        GetEvaluator(questionId, evaluatorId).Criteria[FindCriterionIndex(
            questionId,
            evaluatorId,
            criterionId)];

    private QuestionDesignItemViewModel FindQuestionItem(string questionId) =>
        questionItems.Single(item => string.Equals(item.Id, questionId, StringComparison.Ordinal));

    private EvaluatorDesignItemViewModel FindEvaluatorItem(string questionId, string evaluatorId) =>
        FindQuestionItem(questionId).Evaluators.Single(item => string.Equals(
            item.Id,
            evaluatorId,
            StringComparison.Ordinal));

    private static decimal Percentage<T>(
        IEnumerable<T> items,
        Func<T, string> id,
        Func<T, decimal> weight,
        Func<T, bool> enabled,
        string targetId)
    {
        T? target = items.FirstOrDefault(item => string.Equals(
            id(item),
            targetId,
            StringComparison.Ordinal));
        if (target is null || !enabled(target) || weight(target) <= 0m)
        {
            return 0m;
        }

        T[] included = items
            .Where(item => enabled(item) && weight(item) > 0m)
            .ToArray();
        if (included.Length == 0)
        {
            return 0m;
        }

        try
        {
            decimal total = included.Sum(weight);
            return weight(target) * 100m / total;
        }
        catch (OverflowException)
        {
            // Scaling preserves a usable percentage when otherwise-valid decimal weights
            // overflow only while summing or multiplying the intermediate values.
        }

        decimal maximumWeight = included.Max(weight);
        decimal scaledTotal = included.Sum(item => weight(item) / maximumWeight);
        decimal scaledTarget = weight(target) / maximumWeight;
        return scaledTarget * 100m / scaledTotal;
    }

    private static void EnsureEvaluatorType(EvaluatorType type)
    {
        if (type is not EvaluatorType.KnowledgeCoverage and not EvaluatorType.CustomPrompt)
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Only Knowledge and Custom evaluator types are supported.");
        }
    }

    private static QuantificationDefinition CreateSafeDefault()
    {
        string questionId = NewId("question");
        return new QuantificationDefinition
        {
            Id = NewId("definition"),
            Name = "新しい定量化設計",
            Revision = "1",
            SourceSheet = "Original",
            HeaderRow = 1,
            FirstDataRow = 2,
            LastDataRow = 2,
            BasePoints = 60m,
            SpecialPoints = 0m,
            SimilarityPenaltyWeight = 0.1m,
            RoundingDigits = 1,
            Questions = [CreateDefaultQuestion(questionId, 1, "A") with { Points = 40m }],
        };
    }

    private static QuestionDefinition CreateDefaultQuestion(
        string questionId,
        int ordinal,
        string primaryColumn) => new()
        {
            Id = questionId,
            DisplayName = $"質問 {ordinal.ToString(CultureInfo.InvariantCulture)}",
            QuestionText = "評価する設問を入力してください。",
            PrimarySourceColumn = primaryColumn,
            SupportingSourceColumns = [],
            Points = 0m,
            Evaluators = [CreateDefaultEvaluator(NewId("evaluator"), EvaluatorType.KnowledgeCoverage, 1)],
            Enabled = true,
        };

    private static EvaluatorDefinition CreateDefaultEvaluator(
        string evaluatorId,
        EvaluatorType type,
        int ordinal) => new()
        {
            Id = evaluatorId,
            DisplayName = type == EvaluatorType.KnowledgeCoverage
                ? $"Knowledge {ordinal.ToString(CultureInfo.InvariantCulture)}"
                : $"Custom {ordinal.ToString(CultureInfo.InvariantCulture)}",
            Type = type,
            Weight = 1m,
            Range = new ScoreRange(0m, 10m),
            Criteria = [CreateDefaultCriterion(NewId("criterion"), 1, type)],
            BuiltInTemplateVersion = type == EvaluatorType.KnowledgeCoverage
                ? BuiltInPromptTemplates.KnowledgeTemplateVersion
                : null,
            CustomPromptTemplate = type == EvaluatorType.CustomPrompt
                ? BuiltInPromptTemplates.CustomPromptPreset
                : null,
            Enabled = true,
        };

    private static CriterionDefinition CreateDefaultCriterion(
        string criterionId,
        int ordinal,
        EvaluatorType evaluatorType) => new()
    {
        Id = criterionId,
        DisplayName = $"評価項目 {ordinal.ToString(CultureInfo.InvariantCulture)}",
        Description = DefaultCriterionDescriptions.For(evaluatorType),
        Weight = 1m,
        Range = null,
        Enabled = true,
    };

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static bool HasSameContent(
        QuantificationDefinition current,
        QuantificationDefinition next)
    {
        if (current == next)
        {
            return true;
        }

        // Input clones the immutable arrays even on a plain round trip, so record
        // equality alone cannot distinguish an edit from an unchanged draft.
        ImmutableArray<QuestionDefinition> questions = next.Questions.IsDefault ? [] : next.Questions;
        if (current != (next with { Questions = current.Questions })
            || current.Questions.Length != questions.Length)
        {
            return false;
        }

        for (int questionIndex = 0; questionIndex < questions.Length; questionIndex++)
        {
            QuestionDefinition question = current.Questions[questionIndex];
            QuestionDefinition candidate = questions[questionIndex];
            ImmutableArray<string> supportingColumns = candidate.SupportingSourceColumns.IsDefault
                ? [] : candidate.SupportingSourceColumns;
            ImmutableArray<EvaluatorDefinition> evaluators = candidate.Evaluators.IsDefault
                ? [] : candidate.Evaluators;
            ImmutableArray<SpecialEvaluationDefinition> specials = candidate.SpecialEvaluations.IsDefault
                ? [] : candidate.SpecialEvaluations;
            if (question != (candidate with
                {
                    SupportingSourceColumns = question.SupportingSourceColumns,
                    Evaluators = question.Evaluators,
                    SpecialEvaluations = question.SpecialEvaluations,
                })
                || !question.SupportingSourceColumns.SequenceEqual(supportingColumns, StringComparer.Ordinal)
                || question.Evaluators.Length != evaluators.Length
                || question.SpecialEvaluations.Length != specials.Length)
            {
                return false;
            }

            for (int evaluatorIndex = 0; evaluatorIndex < evaluators.Length; evaluatorIndex++)
            {
                EvaluatorDefinition evaluator = question.Evaluators[evaluatorIndex];
                EvaluatorDefinition candidateEvaluator = evaluators[evaluatorIndex];
                ImmutableArray<CriterionDefinition> criteria = candidateEvaluator.Criteria.IsDefault
                    ? [] : candidateEvaluator.Criteria;
                if (evaluator != (candidateEvaluator with { Criteria = evaluator.Criteria })
                    || !evaluator.Criteria.SequenceEqual(criteria))
                {
                    return false;
                }
            }

            for (int specialIndex = 0; specialIndex < specials.Length; specialIndex++)
            {
                SpecialEvaluationDefinition special = question.SpecialEvaluations[specialIndex];
                SpecialEvaluationDefinition candidateSpecial = specials[specialIndex];
                ImmutableArray<string> specialColumns = candidateSpecial.SupportingSourceColumns.IsDefault
                    ? [] : candidateSpecial.SupportingSourceColumns;
                if (special != (candidateSpecial with { SupportingSourceColumns = special.SupportingSourceColumns })
                    || !special.SupportingSourceColumns.SequenceEqual(specialColumns, StringComparer.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static QuantificationDefinition CloneDefinition(QuantificationDefinition definition)
    {
        ImmutableArray<QuestionDefinition> questions = definition.Questions.IsDefault
            ? []
            : [.. definition.Questions.Select(question => question with
            {
                SupportingSourceColumns = question.SupportingSourceColumns.IsDefault
                    ? []
                    : [.. question.SupportingSourceColumns],
                Evaluators = question.Evaluators.IsDefault
                    ? []
                    : [.. question.Evaluators.Select(evaluator => evaluator with
                    {
                        Criteria = evaluator.Criteria.IsDefault
                            ? []
                            : [.. evaluator.Criteria.Select(criterion => criterion with { })],
                    })],
                SpecialEvaluations = question.SpecialEvaluations.IsDefault
                    ? []
                    : [.. question.SpecialEvaluations.Select(special => special with
                    {
                        SupportingSourceColumns = special.SupportingSourceColumns.IsDefault
                            ? []
                            : [.. special.SupportingSourceColumns],
                    })],
            })];
        return definition with { Questions = questions };
    }

    private static IReadOnlyList<string> NormalizeAvailableColumns(
        QuantificationDefinition definition,
        IEnumerable<string>? provided)
    {
        IEnumerable<string> source = provided ?? definition.Questions
            .SelectMany(question => question.SupportingSourceColumns
                .Prepend(question.PrimarySourceColumn)
                .Concat(question.SpecialEvaluations.SelectMany(special =>
                    special.SupportingSourceColumns.Prepend(special.PrimarySourceColumn))));
        string[] columns = source
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Array.AsReadOnly(columns.Length == 0 ? ["A"] : columns);
    }

    internal static string FormatPercentage(decimal value) =>
        $"{value.ToString("0.##", CultureInfo.InvariantCulture)}%";

    private static string PromptValidationMessage(string code) => code switch
    {
        "TEMPLATE_REQUIRED" => "Custom Prompt template を入力してください。",
        "ANSWER_PLACEHOLDER_REQUIRED" => "Custom Prompt には {回答} が必要です。",
        "CRITERIA_PLACEHOLDER_REQUIRED" => "Custom Prompt には {評価項目} が必要です。",
        "UNKNOWN_PLACEHOLDER" => "許可されていない placeholder があります。",
        "UNCLOSED_PLACEHOLDER" => "閉じていない placeholder があります。",
        "MALFORMED_PLACEHOLDER" => "brace の構造が不正です。literal brace は {{ と }} で記述してください。",
        "UNMATCHED_CLOSING_BRACE" => "閉じ brace は }} として escape してください。",
        _ => "Custom Prompt template を修正してください。",
    };

    // FR-012/FR-067: the allocation error states the validator's own total and its difference from 100,
    // the value that the 配点構成 bar shows.
    private static string ValidationMessage(DefinitionValidationError error)
    {
        string message = ValidationMessage(error.Code);
        if (error.Code == "ALLOCATION_TOTAL_INVALID"
            && decimal.TryParse(error.SafeOffendingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal total))
        {
            message += $"（合計 {ChartFormat.Exact(total)}・差分 {ChartFormat.SignedDifference(total - AllocationComposition.Target)}）";
        }

        return message;
    }

    private static string ValidationMessage(string code) => code switch
    {
        "REQUIRED" => "必須項目を入力してください。",
        "DUPLICATE_ID" => "階層内の identity は一意である必要があります。",
        "QUESTION_REQUIRED" or "ENABLED_QUESTION_REQUIRED" => "1 件以上の有効な質問が必要です。",
        "ENABLED_EVALUATOR_REQUIRED" => "有効な質問には 1 件以上の有効な評価方法が必要です。",
        "ENABLED_CRITERION_REQUIRED" => "有効な評価方法には 1 件以上の有効な評価項目が必要です。",
        "WEIGHT_MUST_BE_POSITIVE" => "重みは有限かつ 0 より大きい値にしてください。",
        "SCORE_RANGE_INVALID" => "最小点は最大点より小さくしてください。",
        "ROUNDING_OUT_OF_RANGE" => "丸め桁数は 0～6 にしてください。",
        "BASE_POINTS_OUT_OF_RANGE" => "Base points は 0～100 にしてください。",
        "SPECIAL_POINTS_OUT_OF_RANGE" => "Special points は 0～100 にしてください。",
        "SIMILARITY_WEIGHT_OUT_OF_RANGE" => "類似度減点係数は 0～1 にしてください。",
        "QUESTION_POINTS_OUT_OF_RANGE" => "質問配点は 0 以上にしてください。",
        "ALLOCATION_TOTAL_INVALID" => "Base、Special、有効質問の配点合計を正確に100にしてください。",
        "SPECIAL_ITEMS_REQUIRED" => "Special points が正の場合は1件以上の有効な固有評価が必要です。",
        "INVALID_EVALUATOR_TYPE" => "評価方法は Knowledge または Custom にしてください。",
        "PRIMARY_COLUMN_REUSED" => "主回答列と補助列は同一質問内で重複できません。",
        "DUPLICATE_SUPPORTING_COLUMN" => "補助列は同一質問内で重複できません。",
        _ => "定量化 definition の技術的な設定を確認してください。",
    };

    public override string ToString() =>
        $"{nameof(QuantificationDesignViewModel)} {{ QuestionCount = {Questions.Count.ToString(CultureInfo.InvariantCulture)}, IsValid = {IsValid}, Content = <redacted> }}";
}