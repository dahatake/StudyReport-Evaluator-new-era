using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.ViewModels;

namespace StudyReportEvaluator.App.Views;

public interface IInputWorkbookPicker
{
    Task<string?> PickAsync(TopLevel topLevel);
}

public sealed class InputWorkbookPickerPathUnavailableException : Exception
{
    public InputWorkbookPickerPathUnavailableException()
        : base("The selected workbook does not expose a local path.")
    {
    }
}

public sealed class NativeInputWorkbookPicker : IInputWorkbookPicker
{
    private static readonly FilePickerFileType StandardXlsx = new("標準 Excel workbook (.xlsx)")
    {
        Patterns = ["*.xlsx"],
        AppleUniformTypeIdentifiers = ["org.openxmlformats.spreadsheetml.sheet"],
        MimeTypes = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
    };

    public async Task<string?> PickAsync(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "評価する標準 .xlsx を選択",
                AllowMultiple = false,
                FileTypeFilter = [StandardXlsx],
                SuggestedFileType = StandardXlsx,
            });
        if (files.Count == 0)
        {
            return null;
        }

        return files[0].TryGetLocalPath()
            ?? throw new InputWorkbookPickerPathUnavailableException();
    }

    public override string ToString() =>
        $"{nameof(NativeInputWorkbookPicker)} {{ Content = <redacted> }}";
}

public sealed partial class InputView : UserControl
{
    // Includes ListBoxItem padding/border; kept in sync with the local item style.
    private const double QuestionRowHeight = 44d;
    private static readonly string[] RowEditorNames = ["FirstDataRowTextBox", "LastDataRowTextBox"];
    private readonly IInputWorkbookPicker picker;
    private InputViewModel? observedInput;
    private InputValidationError? selectedValidationError;
    private bool validationRefreshQueued;
    private bool refreshingValidation;
    private bool isAttached;
    private bool updatingDataContext;
    private bool restoringQuestionSelection;
    private bool updatingPageSize;
    private bool isWideRange;
    private bool isPicking;

    public InputView()
        : this(new InputViewModel(), new NativeInputWorkbookPicker())
    {
    }

    public InputView(InputViewModel viewModel)
        : this(viewModel, new NativeInputWorkbookPicker())
    {
    }

    public InputView(
        InputViewModel viewModel,
        IInputWorkbookPicker picker)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this.picker = picker ?? throw new ArgumentNullException(nameof(picker));
        // Per-view, installed before XAML resolves the SelectedItem converters.
        Resources["InputSelectionWriteback"] = new SelectionWritebackConverter(this);
        InitializeComponent();
        DataContext = viewModel;
        Loaded += HandleLoaded;
        foreach (TextBox editor in RowEditors())
        {
            editor.PropertyChanged += HandleRowEditorChanged;
        }
    }

    public InputViewModel ViewModel => DataContext as InputViewModel
        ?? throw new InvalidOperationException("InputView requires an InputViewModel data context.");

    /// <summary>
    /// Offers the run-preparation parts (Copilot login, resume, technical errors) bound to the shell's
    /// execution view model; passing null removes them and restores the standalone layout.
    /// </summary>
    public void AttachExecutionPreparation(ExecutionViewModel? execution)
    {
        PreparationPanel.DataContext = execution;
        this.FindControl<Button>("PreparationToggleButton")!.IsVisible = execution is not null;
        SetPreparationOpen(false);
    }

    public ExecutionPreparationPanel PreparationPanel =>
        this.FindControl<ExecutionPreparationPanel>("ExecutionPreparation")!;

    public bool IsPreparationOpen => this.FindControl<Control>("ExecutionPreparationHost")!.IsVisible;

    public void SetPreparationOpen(bool open)
    {
        this.FindControl<Control>("ExecutionPreparationHost")!.IsVisible = open;
        foreach (string name in new[] { "InputRangeCard", "InputMappingHost", "InputValidationSummary" })
        {
            this.FindControl<Control>(name)!.IsVisible = !open;
        }

        this.FindControl<Button>("PreparationToggleButton")!.Content = open ? "入力に戻る" : "実行の準備を開く";
    }

    private void HandlePreparationToggleClick(object? sender, RoutedEventArgs e) =>
        SetPreparationOpen(!IsPreparationOpen);
    /// <summary>Standalone hosts can open Mapping settings using this view's current selection.</summary>
    public event EventHandler? SettingsRequested;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        isAttached = true;
        if (!updatingDataContext)
        {
            ObserveInput(DataContext as InputViewModel);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        isAttached = false;
        ObserveInput(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextBeginUpdate()
    {
        // SelectingItemsControl can publish null while replacing its ItemsSource,
        // before applying the new SelectedItem. A worksheet writeback here would
        // reapply suggestions and replace the incoming question identities.
        updatingDataContext = true;
        ObserveInput(null);
        base.OnDataContextBeginUpdate();
    }

    protected override void OnDataContextEndUpdate()
    {
        base.OnDataContextEndUpdate();
        updatingDataContext = false;
        // OnPropertyChanged/DataContextChanged precede inherited child updates.
        // Subscribe only after their TwoWay binding sources have all switched.
        if (isAttached)
        {
            ObserveInput(DataContext as InputViewModel);
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
        selectedValidationError = null;
        if (next is not null)
        {
            next.PropertyChanged += HandleInputPropertyChanged;
        }

        // A cached/rebound view must never apply queued layout work to the old VM.
        if (isAttached)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (isAttached && !updatingDataContext
                    && ReferenceEquals(observedInput, next) && ReferenceEquals(DataContext, next))
                {
                    RestoreQuestionSelection();
                    RefreshValidation();
                    UpdatePageCapacity();
                }
            });
        }
    }

    private void HandleInputPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!isAttached || updatingDataContext
            || !ReferenceEquals(sender, observedInput) || !ReferenceEquals(sender, DataContext))
        {
            return;
        }

        if (e.PropertyName == nameof(InputViewModel.SelectedQuestion))
        {
            // T09: collections have settled while the VM guards null/stale writebacks.
            // Keep the compiled bindings; do not assign a transient logical selection.
            RestoreQuestionSelection();
            RefreshQuestionTextError();
        }
        else if (e.PropertyName == nameof(InputViewModel.ValidationSummary))
        {
            RefreshValidation();
        }
    }

    private void RestoreQuestionSelection()
    {
        if (updatingDataContext || restoringQuestionSelection)
        {
            return;
        }

        // Queued attach/rebind work runs outside the VM's presentation guard.
        // An off-page target may display null, but must not clear the logical target.
        restoringQuestionSelection = true;
        try
        {
            if (this.FindControl<ListBox>("InputQuestionList") is { } questions)
            {
                BindingOperations.GetBindingExpressionBase(questions, ListBox.SelectedItemProperty)?.UpdateTarget();
            }

            if (this.FindControl<ComboBox>("QuestionSelector") is { } selector)
            {
                BindingOperations.GetBindingExpressionBase(selector, ComboBox.SelectedItemProperty)?.UpdateTarget();
            }
        }
        finally
        {
            restoringQuestionSelection = false;
        }
    }

    private sealed class SelectionWritebackConverter(InputView owner) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            owner.updatingDataContext || owner.restoringQuestionSelection ? BindingOperations.DoNothing : value;
    }

    private void HandleInputContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid content || !double.IsFinite(content.Bounds.Width)
            || this.FindControl<Grid>("InputRangeFields") is not { } range)
        {
            return;
        }

        bool wide = content.Bounds.Width >= 840d;
        if (isWideRange == wide)
        {
            return;
        }

        // Grid.ColumnDefinitions is a CLR property, not a Style Setter target.
        // Change this local layout only on a width-category transition, never on edits.
        isWideRange = wide;
        range.ColumnDefinitions = new ColumnDefinitions(wide ? "2*,*,*,*,Auto" : "*,*,*,*");
        range.RowSpacing = wide ? 0d : 6d;
        Grid.SetColumnSpan(this.FindControl<StackPanel>("WorksheetField")!, wide ? 1 : 2);
        Grid.SetColumn(this.FindControl<StackPanel>("HeaderRowField")!, wide ? 1 : 2);
        StackPanel first = this.FindControl<StackPanel>("FirstDataRowField")!;
        Grid.SetRow(first, wide ? 0 : 1);
        Grid.SetColumn(first, wide ? 2 : 0);
        Grid.SetColumnSpan(first, wide ? 1 : 2);
        StackPanel last = this.FindControl<StackPanel>("LastDataRowField")!;
        Grid.SetRow(last, wide ? 0 : 1);
        Grid.SetColumn(last, wide ? 3 : 2);
        Grid.SetColumnSpan(last, wide ? 1 : 2);
        Grid.SetColumn(this.FindControl<Button>("RefreshHeaderButton")!, wide ? 4 : 3);
    }

    private void HandleQuestionViewportSizeChanged(object? sender, SizeChangedEventArgs e) => UpdatePageCapacity();

    private void UpdatePageCapacity()
    {
        if (!isAttached || updatingDataContext || updatingPageSize || DataContext is not InputViewModel input
            || !ReferenceEquals(input, observedInput)
            || this.FindControl<Grid>("QuestionPageViewport") is not { } viewport)
        {
            return;
        }

        // This is the arranged star row AFTER file/range/summary/pager layout and reflow.
        // No window-height subtraction, question-count cap, or infinite measure estimate.
        double height = viewport.Bounds.Height;
        if (!double.IsFinite(height) || height <= 0d)
        {
            return;
        }

        int capacity = (int)Math.Clamp(Math.Floor(height / QuestionRowHeight), 1d, int.MaxValue);
        if (input.PageSize == capacity)
        {
            return;
        }

        updatingPageSize = true;
        try
        {
            input.PageSize = capacity;
        }
        finally
        {
            updatingPageSize = false;
        }
    }

    private void HandleMappingDetailsClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? shell = this.GetVisualAncestors().OfType<Window>()
            .Select(window => window.DataContext).OfType<MainWindowViewModel>().FirstOrDefault();
        if (shell is not null && ReferenceEquals(shell.InputViewModel, DataContext))
        {
            // Settings.Input is this same VM: SelectedQuestion already carries the stable ID.
            shell.OpenSettings(SettingsCategory.Mapping);
        }
        else
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private IEnumerable<TextBox> RowEditors() => RowEditorNames
        .Select(name => this.FindControl<TextBox>(name)).OfType<TextBox>();

    private InputValidationError[] RowConversionErrors() => RowEditors()
        .Where(DataValidationErrors.GetHasErrors)
        .Select(editor => new InputValidationError(
            "NUMERIC_INPUT_UNCOMMITTED", "<input>",
            editor.Name == "FirstDataRowTextBox" ? nameof(InputViewModel.FirstDataRow) : nameof(InputViewModel.LastDataRow),
            editor.Name == "FirstDataRowTextBox" ? "回答開始行を整数で入力してください。" : "回答終了行を整数で入力してください。"))
        .ToArray();

    private void HandleRowEditorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!isAttached || validationRefreshQueued
            || (e.Property != TextBox.TextProperty && e.Property != DataValidationErrors.HasErrorsProperty))
        {
            return;
        }

        // Conversion failures do not notify the VM. Read the binding's settled
        // error state, including corrections back to an unchanged numeric value.
        validationRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            validationRefreshQueued = false;
            RefreshValidation();
        });
    }

    private void RefreshValidation()
    {
        if (!isAttached || updatingDataContext || refreshingValidation
            || this.FindControl<ComboBox>("InputValidationErrorList") is not { } selector)
        {
            return;
        }

        refreshingValidation = true;
        try
        {
            InputViewModel? input = DataContext as InputViewModel;
            InputValidationError[] errors = [.. RowConversionErrors(), .. input?.ValidationErrors.AsEnumerable() ?? []];
            selectedValidationError = errors.FirstOrDefault(error =>
                error.Code == selectedValidationError?.Code
                && error.NodeId == selectedValidationError?.NodeId
                && error.Field == selectedValidationError?.Field) ?? errors.FirstOrDefault();
            selector.ItemsSource = errors;
            selector.IsEnabled = errors.Length > 0;
            selector.SetCurrentValue(ComboBox.SelectedItemProperty, selectedValidationError);
            RefreshValidationDetail();
            RefreshQuestionTextError();
        }
        finally
        {
            refreshingValidation = false;
        }
    }

    private void HandleValidationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!refreshingValidation && sender is ComboBox { SelectedItem: InputValidationError error })
        {
            selectedValidationError = error;
            RefreshValidationDetail();
        }
    }

    private void RefreshValidationDetail()
    {
        InputValidationError[] conversionErrors = RowConversionErrors();
        string pending = conversionErrors.Length == 0 ? string.Empty
            : $"数値として未反映の入力 {conversionErrors.Length} 件: {string.Join("、", conversionErrors.Select(conversion => conversion.Field))}。回答行・件数は確定値です。";
        string detail = selectedValidationError is { } error
            ? $"{error.Field}: {error.Message} ({error.NodeId})"
            : conversionErrors.Length == 0 ? (DataContext as InputViewModel)?.ValidationSummary ?? string.Empty : string.Empty;
        string text = string.Join(Environment.NewLine, new[] { pending, detail }.Where(value => value.Length > 0));
        this.FindControl<TextBox>("InputValidationDetail")?.SetCurrentValue(TextBox.TextProperty, text);
        ToolTip.SetTip(this.FindControl<Border>("InputValidationSummary")!, text);
        this.FindControl<Button>("InputGoToProblemButton")!.IsEnabled = ValidationEditorName() is not null;
    }

    private string? ValidationEditorName() => selectedValidationError?.Field switch
    {
        "FilePath" => "FilePathTextBox",
        "SourceSheet" => "WorksheetComboBox",
        "HeaderRow" => "HeaderRowComboBox",
        "FirstDataRow" => "FirstDataRowTextBox",
        "LastDataRow" or "SelectedRowCount" => "LastDataRowTextBox",
        "QuestionText" => "QuestionTextEditor",
        "PrimarySourceColumn" => "PrimaryColumnComboBox",
        _ => null,
    };

    private void HandleGoToProblem(object? sender, RoutedEventArgs e)
    {
        if (ValidationEditorName() is not { } name || DataContext is not InputViewModel input)
        {
            return;
        }

        if (input.Questions.FirstOrDefault(question => question.Id == selectedValidationError?.NodeId) is { } target)
        {
            input.SelectedQuestion = target;
        }

        if (this.FindControl<Control>(name) is { IsEffectivelyEnabled: true } editor)
        {
            editor.BringIntoView();
            editor.Focus(NavigationMethod.Tab);
        }
    }

    private void RefreshQuestionTextError()
    {
        if (this.FindControl<TextBlock>("QuestionTextRequiredError") is not { } message)
        {
            return;
        }

        InputViewModel? input = DataContext as InputViewModel;
        bool required = input?.SelectedQuestion is { } selected && input.ValidationErrors.Any(error =>
            error.NodeId == selected.Id && error.Code == "REQUIRED"
            && error.Field == nameof(InputQuestionMappingViewModel.QuestionText));
        message.Text = required ? "設問文は必須です。評価する本文を入力してください。" : string.Empty;
        message.IsVisible = required;
    }

    public async Task PickFileAsync()
    {
        if (isPicking || TopLevel.GetTopLevel(this) is not TopLevel topLevel)
        {
            return;
        }

        isPicking = true;
        Button? button = this.FindControl<Button>("PickFileButton");
        if (button is not null)
        {
            button.IsEnabled = false;
        }

        try
        {
            string? selectedPath = await picker.PickAsync(topLevel);
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                SetPickerStatus(string.Empty);
                await ViewModel.SetFilePathAsync(selectedPath);
            }
        }
        catch (InputWorkbookPickerPathUnavailableException)
        {
            SetPickerStatus("選択したファイルのlocal pathを取得できません。pathを直接入力してください。");
        }
        catch
        {
            SetPickerStatus("native pickerを利用できません。pathを直接入力してください。");
        }
        finally
        {
            isPicking = false;
            if (button is not null)
            {
                button.IsEnabled = true;
            }
        }
    }

    private async void HandlePickFileClick(object? sender, RoutedEventArgs e) =>
        await PickFileAsync();

    private void SetPickerStatus(string message)
    {
        TextBlock? status = this.FindControl<TextBlock>("PickerStatusMessage");
        if (status is not null)
        {
            status.Text = message;
            status.IsVisible = message.Length > 0;
        }
    }

    private async void HandleLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= HandleLoaded;
        if (DataContext is not InputViewModel input)
        {
            return;
        }

        await input.LoadLaunchInputIfRequestedAsync();
        if (TopLevel.GetTopLevel(this) is not Window window
            || !ReferenceEquals(window.Content, this))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (isAttached && ReferenceEquals(DataContext, input) && ReferenceEquals(window.Content, this))
            {
                this.FindControl<TextBox>("FilePathTextBox")?.Focus(NavigationMethod.Tab, KeyModifiers.None);
            }
        });
    }
}