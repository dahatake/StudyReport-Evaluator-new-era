using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Utilities;
using StudyReportEvaluator.App.ViewModels;

namespace StudyReportEvaluator.App.Views;

public sealed partial class ImportedPromptSettingsView : UserControl
{
    public ImportedPromptSettingsView()
    {
        InitializeComponent();
        RefreshApplyStatus();
    }

    public ImportedPromptSettingsView(QuantificationDesignViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
    }

    public QuantificationDesignViewModel ViewModel => DataContext as QuantificationDesignViewModel
        ?? throw new InvalidOperationException("ImportedPromptSettingsView requires a QuantificationDesignViewModel data context.");

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != DataContextProperty)
        {
            return;
        }

        if (change.OldValue is QuantificationDesignViewModel previous)
        {
            WeakEventHandlerManager.Unsubscribe<PropertyChangedEventArgs, ImportedPromptSettingsView>(
                previous, nameof(previous.PropertyChanged), HandleDesignPropertyChanged);
        }

        if (change.NewValue is QuantificationDesignViewModel current)
        {
            WeakEventHandlerManager.Subscribe<QuantificationDesignViewModel, PropertyChangedEventArgs, ImportedPromptSettingsView>(
                current, nameof(current.PropertyChanged), HandleDesignPropertyChanged);
        }

        RefreshApplyStatus();
    }

    private void HandleDesignPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, DataContext))
        {
            return;
        }

        if (e.PropertyName == nameof(QuantificationDesignViewModel.SelectedQuestion)
            && this.FindControl<ComboBox>("ImportedPromptQuestionSelector") is { } questions)
        {
            // Reuse the Design view's settled-selection refresh after a collection move.
            // The existing VM still guards collection-induced TwoWay writebacks here.
            BindingOperations.GetBindingExpressionBase(questions, ComboBox.SelectedItemProperty)?.UpdateTarget();
        }

        if (e.PropertyName is null
            or nameof(QuantificationDesignViewModel.SelectedQuestion)
            or nameof(QuantificationDesignViewModel.ImportedPromptPreview)
            or nameof(QuantificationDesignViewModel.SelectedPromptTargetSummary)
            or nameof(QuantificationDesignViewModel.CanApplyImportedPrompt)
            or nameof(QuantificationDesignViewModel.Draft))
        {
            RefreshApplyStatus();
        }
    }

    private void RefreshApplyStatus()
    {
        if (this.FindControl<TextBlock>("ImportedPromptApplyStatus") is not { } status)
        {
            return;
        }

        status.Text = GetApplyStatus();
        AutomationProperties.SetName(status, status.Text);
    }

    private string GetApplyStatus()
    {
        if (DataContext is not QuantificationDesignViewModel viewModel)
        {
            return "採点設計がありません。";
        }

        if (viewModel.ImportedPrompts.Count == 0)
        {
            return "読込Promptはありません。起動時に --prompt を指定してください。";
        }

        if (viewModel.SelectedImportedPrompt is not { } prompt)
        {
            return "一覧から適用するPromptを選択してください。";
        }

        if (viewModel.SelectedQuestion is not { } question)
        {
            return "適用先の設問を選択してください。";
        }

        if (viewModel.SelectedPromptTarget == ImportedPromptTarget.CustomEvaluator
            && question.SelectedEvaluator?.IsKnowledge == true)
        {
            return "Knowledgeは読取専用です。Custom評価方法を選択してください。";
        }

        if (!viewModel.CanApplyImportedPrompt)
        {
            return viewModel.SelectedPromptTarget == ImportedPromptTarget.CustomEvaluator
                ? "適用先のCustom評価方法を選択してください。"
                : "適用先の固有評価を選択してください。";
        }

        string? template = viewModel.SelectedPromptTarget == ImportedPromptTarget.CustomEvaluator
            ? question.SelectedEvaluator?.CustomPromptTemplate
            : question.SelectedSpecialEvaluation?.PromptTemplate;

        // This is current content equality, not an apply history or a saved-file claim.
        // Only the button's existing ApplyImportedPromptCommand changes the draft.
        return string.Equals(template, prompt.Content, StringComparison.Ordinal)
            ? "適用先と本文が一致しています（保存状態とは別です）。"
            : "この適用先には未反映です。「Promptを適用」で本文をコピーします。";
    }
}