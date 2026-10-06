using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.Core.Prompting;

namespace StudyReportEvaluator.App.Views;

public sealed partial class SpecialEvaluationSettingsView : UserControl
{
    private static readonly PromptTemplateRenderer PreviewRenderer = new();
    private static readonly PromptRenderContext PreviewContext = new(
        "【設問 preview】", "【回答 preview】", "【補助情報 preview】", string.Empty, "0", "1");

    private QuantificationDesignViewModel? observedDesign;
    private bool isAttached;

    public SpecialEvaluationSettingsView()
    {
        InitializeComponent();
        Loaded += HandleLoaded;
    }

    public SpecialEvaluationSettingsView(QuantificationDesignViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
    }

    public QuantificationDesignViewModel ViewModel => DataContext as QuantificationDesignViewModel
        ?? throw new InvalidOperationException("SpecialEvaluationSettingsView requires a QuantificationDesignViewModel data context.");

    // Presentation only: no additional draft, workbook access, or AI boundary.
    public static FuncValueConverter<string?, string> PromptPreviewConverter { get; } = new(CreatePromptPreview);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        isAttached = true;
        ObserveDesign(DataContext as QuantificationDesignViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        isAttached = false;
        ObserveDesign(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (isAttached && change.Property == DataContextProperty)
        {
            ObserveDesign(change.NewValue as QuantificationDesignViewModel);
        }
    }

    private void ObserveDesign(QuantificationDesignViewModel? next)
    {
        if (ReferenceEquals(observedDesign, next))
        {
            return;
        }

        if (observedDesign is not null)
        {
            observedDesign.PropertyChanged -= HandleDesignPropertyChanged;
        }

        observedDesign = next;
        if (observedDesign is not null)
        {
            observedDesign.PropertyChanged += HandleDesignPropertyChanged;
        }
    }

    private void HandleDesignPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!isAttached || !ReferenceEquals(sender, DataContext)
            || e.PropertyName != nameof(QuantificationDesignViewModel.SelectedQuestion))
        {
            return;
        }

        // T09 publishes this after collection changes while its selection guard is
        // active. Refresh targets only; never manufacture a null source selection.
        RefreshSelection("QuestionSelector");
        RefreshSelection("SpecialSelector");
    }

    private void RefreshSelection(string name)
    {
        if (this.FindControl<ComboBox>(name) is { } selector)
        {
            BindingOperations.GetBindingExpressionBase(selector, ComboBox.SelectedItemProperty)?.UpdateTarget();
        }
    }

    private void HandleLoaded(object? sender, RoutedEventArgs e)
    {
        // Also reconcile same-reference selections after a detached view is reused.
        // Items and inherited DataContext have settled by Loaded.
        RefreshSelection("QuestionSelector");
        RefreshSelection("SpecialSelector");
        if (TopLevel.GetTopLevel(this) is not Window window || !ReferenceEquals(window.Content, this))
        {
            return;
        }

        // The settings container owns focus when embedded. A queued standalone
        // focus must not affect a replacement view after detach/reparent.
        Dispatcher.UIThread.Post(() =>
        {
            if (isAttached && ReferenceEquals(window.Content, this))
            {
                this.FindControl<ComboBox>("QuestionSelector")?.Focus(NavigationMethod.Tab, KeyModifiers.None);
            }
        });
    }

    private static string CreatePromptPreview(string? template)
    {
        try
        {
            return PreviewRenderer.RenderSpecial(template ?? string.Empty, PreviewContext)
                + Environment.NewLine + Environment.NewLine
                + BuiltInPromptTemplates.SpecialOutputInstruction;
        }
        catch (PromptConfigurationException exception)
        {
            return $"Promptの設定エラーを解消するとプレビューを表示します（{exception.Code}）。";
        }
    }
}