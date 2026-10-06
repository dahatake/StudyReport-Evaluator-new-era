using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.ViewModels;

namespace StudyReportEvaluator.App.Views;

/// <summary>
/// T17's passive settings container. The host owns the SettingsViewModel, its
/// optional store, initialization, and CloseRequested subscription (T23).
/// </summary>
public sealed partial class SettingsView : UserControl
{
    private readonly Dictionary<SettingsCategory, Control> categoryViews = [];
    private SettingsViewModel? owner;
    private bool attached;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += HandleDataContextChanged;
    }

    public SettingsView(SettingsViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
    }

    public SettingsViewModel ViewModel => DataContext as SettingsViewModel
        ?? throw new InvalidOperationException("SettingsView requires a SettingsViewModel data context.");

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        if (owner is not null)
        {
            owner.PropertyChanged += HandleSettingsPropertyChanged;
        }

        ShowCategory(moveFocus: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        if (owner is not null)
        {
            owner.PropertyChanged -= HandleSettingsPropertyChanged;
        }

        // Keep the controls and their unfinished text, not a second editable draft.
        // Neither the parent's VM nor any of its editors is disposed here.
        base.OnDetachedFromVisualTree(e);
    }

    private void HandleDataContextChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(owner, DataContext))
        {
            return;
        }

        if (attached && owner is not null)
        {
            owner.PropertyChanged -= HandleSettingsPropertyChanged;
        }

        this.FindControl<ContentControl>("CurrentSettingsContent")!.Content = null;
        categoryViews.Clear();
        owner = DataContext as SettingsViewModel;
        if (attached && owner is not null)
        {
            owner.PropertyChanged += HandleSettingsPropertyChanged;
        }

        ShowCategory(moveFocus: attached);
    }

    private void HandleSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!attached || !ReferenceEquals(sender, owner))
        {
            return;
        }

        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.SelectedCategory))
        {
            // The VM has already synchronized the latest editor at this boundary.
            ShowCategory(moveFocus: true);
        }
        else
        {
            RefreshAvailability();
        }
    }

    private void ShowCategory(bool moveFocus)
    {
        ContentControl content = this.FindControl<ContentControl>("CurrentSettingsContent")!;
        if (owner is null)
        {
            content.Content = null;
            RefreshAvailability();
            return;
        }

        SettingsCategory category = owner.SelectedCategory;
        if (!categoryViews.TryGetValue(category, out Control? view))
        {
            view = CreateCategory(category, owner);
            categoryViews.Add(category, view);
        }

        if (!ReferenceEquals(content.Content, view))
        {
            // Remove the old logical child before attaching the new one. Cached
            // categories are not hidden siblings with duplicate automation IDs.
            content.Content = null;
            content.Content = view;
        }

        foreach (Button button in this.FindControl<Grid>("SettingsCategoryBar")!.Children.OfType<Button>())
        {
            bool selected = Equals(button.CommandParameter, category);
            button.Classes.Set("selected", selected);
            AutomationProperties.SetHelpText(button, selected ? "選択中の設定カテゴリです。" : "この設定カテゴリへ移動します。");
        }

        RefreshAvailability();
        if (moveFocus)
        {
            FocusCategoryAfterLayout(owner, category, view);
        }
    }

    private Control CreateCategory(SettingsCategory category, SettingsViewModel settings)
    {
        Control view = category switch
        {
            SettingsCategory.Common => ((IDataTemplate)Resources["CommonSettingsTemplate"]!).Build(settings)
                ?? throw new InvalidOperationException("The common settings template must produce a control."),
            SettingsCategory.Mapping => new MappingSettingsView(settings.Input),
            SettingsCategory.Evaluation => new EvaluatorSettingsView(settings.Design),
            SettingsCategory.Special => new SpecialEvaluationSettingsView(settings.Design),
            SettingsCategory.ImportedPrompts => new ImportedPromptSettingsView(settings.Design),
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

        if (category == SettingsCategory.Common)
        {
            view.DataContext = settings;
        }

        if (view is UserControl { Content: Grid layout })
        {
            // Adapt only these newly-created embedded instances. Never impose the
            // standalone tests' 450-DIP height on the smaller settings body.
            layout.Margin = new Thickness(4, 0);
            layout.RowSpacing = 4;
        }

        if (view is EvaluatorSettingsView evaluator)
        {
            CompactEvaluatorHeader(evaluator);
        }

        if (view is EvaluatorSettingsView or SpecialEvaluationSettingsView)
        {
            // An ancestor style disappears while a cached category is off-tree.
            // Changing TabStripPlacement rebuilds containers and resets pending text.
            foreach (TabControl tabs in view.GetLogicalDescendants().OfType<TabControl>())
            {
                tabs.TabStripPlacement = Dock.Left;
            }
        }

        if (view is ImportedPromptSettingsView prompts)
        {
            // Viewing the original prompts is valid before loading an Excel file.
            // Button.Command still supplies CanApplyImportedPrompt (including the
            // Knowledge/missing-target guard); IsEnabled adds the Settings gate.
            prompts.FindControl<Button>("ApplyImportedPromptButton")!.Bind(IsEnabledProperty,
                new Binding(nameof(SettingsViewModel.CanEditDefinition))
                {
                    Source = settings,
                    Mode = BindingMode.OneWay,
                    FallbackValue = false,
                });
        }

        return view;
    }

    private static void CompactEvaluatorHeader(EvaluatorSettingsView view)
    {
        // These three existing labelled selectors become one 44-DIP row. The
        // selection bindings and handlers stay intact; no editor is recreated.
        if (view.Content is Grid layout && layout.Children[0] is Grid selectors)
        {
            selectors.ColumnSpacing = 8;
            foreach (Grid field in selectors.Children.OfType<Grid>())
            {
                field.RowDefinitions = new RowDefinitions("44");
                field.ColumnDefinitions = new ColumnDefinitions(field.Children.OfType<Button>().Any()
                    ? "Auto,*,Auto" : "Auto,*");
                field.ColumnSpacing = 4;
                foreach (Control control in field.Children)
                {
                    Grid.SetRow(control, 0);
                    Grid.SetColumnSpan(control, 1);
                    Grid.SetColumn(control, control is TextBlock ? 0 : control is ComboBox ? 1 : 2);
                }
            }
        }

        foreach (Border pane in view.GetLogicalDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("settings-pane")))
        {
            pane.Padding = new Thickness(4);
        }

        foreach (TabItem tab in view.GetLogicalDescendants().OfType<TabItem>())
        {
            if (tab.Content is Grid page)
            {
                page.Margin = new Thickness(0);
                page.ColumnSpacing = 8;
            }
        }
    }

    private void RefreshAvailability()
    {
        bool inspectable = owner is not null && (owner.CanEditDefinition
            || owner.SelectedCategory is SettingsCategory.Common or SettingsCategory.ImportedPrompts);
        this.FindControl<Grid>("SettingsCategoryBar")!.IsEnabled = owner is not null;
        this.FindControl<ContentControl>("CurrentSettingsContent")!.IsEnabled = inspectable;
        this.FindControl<TextBlock>("SettingsDefinitionAvailability")!.IsVisible = owner is null
            || (!owner.CanEditDefinition && owner.SelectedCategory != SettingsCategory.Common);
        this.FindControl<Button>("SettingsLoadRetry")!.IsEnabled = owner is not null
            && !string.IsNullOrEmpty(owner.FilePath) && !owner.IsLoading && !owner.IsSaving && !owner.IsApplying;
        this.FindControl<Button>("SettingsRequestClose")!.IsEnabled = owner is not null;
    }

    private void HandleBodySizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // ScrollViewer otherwise measures its child with infinite height. Give
        // star rows the measured remaining space; only a narrow/short body gets
        // a minimum canvas and actual scroll. The footer is never in this scroll.
        ContentControl content = this.FindControl<ContentControl>("CurrentSettingsContent")!;
        content.Width = Math.Max(720, e.NewSize.Width);
        content.Height = Math.Max(340, e.NewSize.Height);
    }

    private void FocusCategoryAfterLayout(SettingsViewModel settings, SettingsCategory category, Control view)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!attached || !ReferenceEquals(owner, settings) || owner.SelectedCategory != category
                || !ReferenceEquals(this.FindControl<ContentControl>("CurrentSettingsContent")!.Content, view))
            {
                return;
            }

            string id = category switch
            {
                SettingsCategory.Common => "ExecutionModel",
                SettingsCategory.Mapping => "MappingSettingsQuestions",
                SettingsCategory.Evaluation => "EvaluatorSettingsQuestions",
                SettingsCategory.Special => "SpecialSettingsQuestions",
                _ => "ImportedPromptPreview", // ListBox itself is not a Tab target.
            };
            Control? target = view.GetVisualDescendants().OfType<Control>().FirstOrDefault(control =>
                AutomationProperties.GetAutomationId(control) == id && control.IsEffectivelyEnabled && control.IsEffectivelyVisible);
            if (target?.Focus(NavigationMethod.Tab) != true)
            {
                this.FindControl<Grid>("SettingsCategoryBar")!.Children.OfType<Button>()
                    .First(button => Equals(button.CommandParameter, category)).Focus(NavigationMethod.Tab);
            }
        }, DispatcherPriority.Loaded);
    }

    private async void HandleLoadRetry(object? sender, RoutedEventArgs e)
    {
        if (owner is not { } settings || !this.FindControl<Button>("SettingsLoadRetry")!.IsEffectivelyEnabled)
        {
            return;
        }

        try
        {
            await settings.LoadAsync();
        }
        catch
        {
            // IO failures normally become VM status. A failing event subscriber
            // must not escape async void or expose paths/content in an exception.
            if (attached && ReferenceEquals(owner, settings))
            {
                this.FindControl<TextBlock>("SettingsStatus")!.SetCurrentValue(TextBlock.TextProperty,
                    "設定の再読込に失敗しました。保存先を確認してください。");
            }
        }
    }
}