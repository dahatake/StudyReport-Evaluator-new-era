using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StudyReportEvaluator.App.ViewModels;

namespace StudyReportEvaluator.App.Views;

public sealed partial class MappingSettingsView : UserControl
{
    private InputViewModel? observedInput;
    private bool isAttached;
    private bool refreshingSelection;
    private string? supportingQuestionId;
    private string? supportingColumnName;
    private string? candidateColumnName;

    public MappingSettingsView()
    {
        InitializeComponent();
    }

    public MappingSettingsView(InputViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
    }

    public InputViewModel ViewModel => DataContext as InputViewModel
        ?? throw new InvalidOperationException("MappingSettingsView requires an InputViewModel data context.");

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        isAttached = true;
        ObserveInput(DataContext as InputViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        isAttached = false;
        ObserveInput(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (isAttached && change.Property == DataContextProperty)
        {
            ObserveInput(change.NewValue as InputViewModel);
        }
    }

    private void ObserveInput(InputViewModel? next)
    {
        if (ReferenceEquals(observedInput, next))
        {
            return;
        }

        if (observedInput is not null)
        {
            observedInput.PropertyChanged -= HandleInputPropertyChanged;
        }

        observedInput = next;
        if (next is not null)
        {
            next.PropertyChanged += HandleInputPropertyChanged;
        }

        // DataContext inheritance/bindings settle after the parent changes.
        // Never let queued work update a detached view or a replacement context.
        if (isAttached)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (isAttached && ReferenceEquals(observedInput, next))
                {
                    RefreshSelection();
                    RefreshValidationSelection();
                }
            });
        }
    }

    private void HandleInputPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!isAttached || !ReferenceEquals(sender, DataContext))
        {
            return;
        }

        if (e.PropertyName == nameof(InputViewModel.SelectedQuestion))
        {
            // T08 publishes this synchronously after its collections settle,
            // while the VM's null/stale SelectedItem writeback guard is active.
            RefreshSelection();
        }
        else if (e.PropertyName == nameof(InputViewModel.ValidationSummary))
        {
            RefreshValidationSelection();
        }
    }

    private void RefreshSelection()
    {
        if (!isAttached || refreshingSelection || !ReferenceEquals(observedInput, DataContext))
        {
            return;
        }

        refreshingSelection = true;
        try
        {
            ComboBox questions = this.FindControl<ComboBox>("QuestionSelector")!;
            // Keep the real compiled TwoWay expression: no SelectedItem assignment
            // or replacement binding for the root's logical question selection.
            BindingOperations.GetBindingExpressionBase(questions, ComboBox.SelectedItemProperty)?.UpdateTarget();

            ListBox columns = this.FindControl<ListBox>("SupportingColumnList")!;
            BindingOperations.GetBindingExpressionBase(columns, ListBox.ItemsSourceProperty)?.UpdateTarget();
            InputQuestionMappingViewModel? question = observedInput?.SelectedQuestion;
            if (!string.Equals(supportingQuestionId, question?.Id, StringComparison.Ordinal))
            {
                supportingColumnName = null;
            }

            // Only these two inspection selections are view-local. Supporting
            // options are recreated on every commit, so retain the column key,
            // not an obsolete option/checkbox or an independent mapping draft.
            SupportingColumnSelectionViewModel? column = question?.SupportingColumns.FirstOrDefault(item =>
                string.Equals(item.ColumnName, supportingColumnName, StringComparison.OrdinalIgnoreCase))
                ?? question?.SupportingColumns.FirstOrDefault();
            columns.SetCurrentValue(ListBox.SelectedItemProperty, column);
            supportingQuestionId = question?.Id;
            supportingColumnName = column?.ColumnName;
            RefreshSupportingAvailability(column);
            if (question is not null && column is not null)
            {
                columns.ScrollIntoView(question.SupportingColumns.IndexOf(column));
            }

            ComboBox candidates = this.FindControl<ComboBox>("CandidateSelector")!;
            MappingSuggestionViewModel? candidate = observedInput?.MappingSuggestions.FirstOrDefault(item =>
                string.Equals(item.ColumnName, candidateColumnName, StringComparison.OrdinalIgnoreCase))
                ?? observedInput?.MappingSuggestions.FirstOrDefault();
            candidates.SetCurrentValue(ComboBox.SelectedItemProperty, candidate);
            candidateColumnName = candidate?.ColumnName;
        }
        finally
        {
            refreshingSelection = false;
        }
    }

    private void HandleSupportingSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!isAttached || refreshingSelection || sender is not ListBox columns)
        {
            return;
        }

        InputQuestionMappingViewModel? question = (DataContext as InputViewModel)?.SelectedQuestion;
        if (columns.SelectedItem is SupportingColumnSelectionViewModel column)
        {
            supportingQuestionId = question?.Id;
            supportingColumnName = column.ColumnName;
            RefreshSupportingAvailability(column);
        }
        else if (question?.SupportingColumns.Count > 0)
        {
            RefreshSupportingAvailability(null);
        }
    }

    private void RefreshSupportingAvailability(SupportingColumnSelectionViewModel? column)
    {
        // A commit clears/repopulates SupportingColumns synchronously. Do not
        // disable the focused checkbox for that transient null; use the VM's
        // settled CanSelect after refresh, or an explicit inspection selection.
        // IsChecked remains a real TwoWay binding to the current option.
        this.FindControl<CheckBox>("IncludeSupportingColumnCheckBox")!
            .SetCurrentValue(IsEnabledProperty, column?.CanSelect == true);
    }

    private void HandleCandidateSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (isAttached && !refreshingSelection && sender is ComboBox
            { SelectedItem: MappingSuggestionViewModel candidate })
        {
            candidateColumnName = candidate.ColumnName;
        }
    }

    private void RefreshValidationSelection()
    {
        if (isAttached && ReferenceEquals(observedInput, DataContext)
            && this.FindControl<ComboBox>("MappingValidationErrors") is { SelectedItem: null } errors)
        {
            errors.SetCurrentValue(ComboBox.SelectedItemProperty, observedInput?.ValidationErrors.FirstOrDefault());
        }
    }

    private void HandleLoaded(object? sender, RoutedEventArgs e)
    {
        RefreshSelection();
        RefreshValidationSelection();
        if (TopLevel.GetTopLevel(this) is not Window window || !ReferenceEquals(window.Content, this))
        {
            return;
        }

        // Embedded category focus belongs to T17, not this editing surface.
        Dispatcher.UIThread.Post(() =>
        {
            if (isAttached && ReferenceEquals(window.Content, this))
            {
                this.FindControl<ComboBox>("QuestionSelector")?.Focus(NavigationMethod.Tab, KeyModifiers.None);
            }
        });
    }
}