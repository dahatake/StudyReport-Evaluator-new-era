using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using StudyReportEvaluator.App.ViewModels;

namespace StudyReportEvaluator.App.Views;

/// <summary>
/// Edits only the existing owner's selected question/evaluator/criterion chain.
/// The host supplies the data context and a finite, stretching layout slot; this
/// view does not create a draft, navigate, save, apply imported prompts, or run AI.
/// </summary>
public sealed partial class EvaluatorSettingsView : UserControl
{
    private QuantificationDesignViewModel? observedDesign;
    private QuestionDesignItemViewModel? observedQuestion;
    private EvaluatorDesignItemViewModel? observedEvaluator;
    private bool attached;
    private bool refreshingSelection;

    public EvaluatorSettingsView()
    {
        InitializeComponent();
    }

    public EvaluatorSettingsView(QuantificationDesignViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
    }

    public QuantificationDesignViewModel ViewModel => DataContext as QuantificationDesignViewModel
        ?? throw new InvalidOperationException("EvaluatorSettingsView requires a QuantificationDesignViewModel data context.");

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        ObserveDesign(DataContext as QuantificationDesignViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        this.FindControl<Button>("AddEvaluatorButton")?.Flyout?.Hide();
        DetachObservers();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (attached && change.Property == DataContextProperty)
        {
            this.FindControl<Button>("AddEvaluatorButton")?.Flyout?.Hide();
            ObserveDesign(change.NewValue as QuantificationDesignViewModel);
        }
    }

    private void ObserveDesign(QuantificationDesignViewModel? design)
    {
        if (!ReferenceEquals(observedDesign, design))
        {
            DetachObservers();
            observedDesign = design;
            if (observedDesign is not null)
            {
                observedDesign.PropertyChanged += HandleDesignPropertyChanged;
            }
        }

        RefreshSelection();
    }

    private void DetachObservers()
    {
        if (observedDesign is not null)
        {
            observedDesign.PropertyChanged -= HandleDesignPropertyChanged;
        }

        if (observedQuestion is not null)
        {
            observedQuestion.PropertyChanged -= HandleQuestionPropertyChanged;
        }

        if (observedEvaluator is not null)
        {
            observedEvaluator.PropertyChanged -= HandleEvaluatorPropertyChanged;
        }

        observedDesign = null;
        observedQuestion = null;
        observedEvaluator = null;
    }

    private void HandleDesignPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, observedDesign)
            && e.PropertyName is null or "" or nameof(QuantificationDesignViewModel.SelectedQuestion)
                or nameof(QuantificationDesignViewModel.Draft))
        {
            RefreshSelection();
        }
    }

    private void HandleQuestionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, observedQuestion)
            && e.PropertyName is null or "" or nameof(QuestionDesignItemViewModel.SelectedEvaluator))
        {
            RefreshSelection();
        }
    }

    private void HandleEvaluatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, observedEvaluator)
            && e.PropertyName is null or "" or nameof(EvaluatorDesignItemViewModel.SelectedCriterion))
        {
            RefreshSelection();
        }
    }

    private void RefreshSelection()
    {
        if (!attached || refreshingSelection)
        {
            return;
        }

        refreshingSelection = true;
        try
        {
            QuestionDesignItemViewModel? question = observedDesign?.SelectedQuestion;
            if (!ReferenceEquals(observedQuestion, question))
            {
                if (observedQuestion is not null)
                {
                    observedQuestion.PropertyChanged -= HandleQuestionPropertyChanged;
                }

                observedQuestion = question;
                if (observedQuestion is not null)
                {
                    observedQuestion.PropertyChanged += HandleQuestionPropertyChanged;
                }
            }

            EvaluatorDesignItemViewModel? evaluator = question?.SelectedEvaluator;
            if (!ReferenceEquals(observedEvaluator, evaluator))
            {
                if (observedEvaluator is not null)
                {
                    observedEvaluator.PropertyChanged -= HandleEvaluatorPropertyChanged;
                }

                observedEvaluator = evaluator;
                if (observedEvaluator is not null)
                {
                    observedEvaluator.PropertyChanged += HandleEvaluatorPropertyChanged;
                }
            }

            foreach (string name in new[] { "QuestionSelector", "EvaluatorSelector", "CriterionSelector" })
            {
                if (this.FindControl<SelectingItemsControl>(name) is { } selector)
                {
                    // T09 pattern: reapply even an unchanged reference after items settle.
                    // Publish candidates first, then selection, without a temporary null
                    // in the owner or replacing the production compiled binding.
                    BindingOperations.GetBindingExpressionBase(selector, ItemsControl.ItemsSourceProperty)?.UpdateTarget();
                    BindingOperations.GetBindingExpressionBase(selector, SelectingItemsControl.SelectedItemProperty)?.UpdateTarget();
                }
            }
        }
        finally
        {
            refreshingSelection = false;
        }
    }

    private void HandleTargetSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!attached || refreshingSelection || sender is not SelectingItemsControl selector
            || DataContext is not QuantificationDesignViewModel design)
        {
            return;
        }

        // Dropdowns have no clear action. OneWay target bindings plus explicit,
        // non-null candidate writes prevent ItemsSource refresh from clearing an
        // old question's child selection. An explicit owner-side null still works.
        switch (selector.Name)
        {
            case "QuestionSelector" when selector.SelectedItem is QuestionDesignItemViewModel question
                && design.Questions.Contains(question):
                design.SelectedQuestion = question;
                break;
            case "EvaluatorSelector" when selector.SelectedItem is EvaluatorDesignItemViewModel evaluator
                && design.SelectedQuestion is { } parentQuestion
                && ReferenceEquals(selector.ItemsSource, parentQuestion.Evaluators)
                && parentQuestion.Evaluators.Contains(evaluator):
                parentQuestion.SelectedEvaluator = evaluator;
                break;
            case "CriterionSelector" when selector.SelectedItem is CriterionDesignItemViewModel criterion
                && design.SelectedQuestion?.SelectedEvaluator is { } parentEvaluator
                && ReferenceEquals(selector.ItemsSource, parentEvaluator.Criteria)
                && parentEvaluator.Criteria.Contains(criterion):
                parentEvaluator.SelectedCriterion = criterion;
                break;
        }
    }
}