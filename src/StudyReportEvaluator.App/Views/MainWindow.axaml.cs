using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Composition;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workspace;

namespace StudyReportEvaluator.App.Views;

public sealed partial class MainWindow : Window
{
    // One control per existing editor owner; no hidden sibling views or second draft.
    private readonly Dictionary<UiObservableObject, Control> editorViews = new(ReferenceEqualityComparer.Instance);
    private UiObservableObject? currentEditor;
    private UiObservableObject? settingsReturnEditor;
    private Control? settingsReturnTarget;
    private long focusVersion;
    private bool opened;
    private bool closed;
    private bool closePending;
    private bool finalCloseAllowed;
    private Control? paletteReturnFocus;
    private IReadOnlyList<CommandPaletteEntry> paletteEntries = [];

    public MainWindow()
        : this(new ServiceRegistration().CreateMainWindowViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = ViewModel;
        // Every step workspace below this window uses the user's layouts and personas.
        PanelWorkspace.SetLayoutService(this, ViewModel.Workspace);
        // NFR-UX-007: Ctrl+Shift+P opens command search from anywhere in the window.
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Shift),
            Command = new WindowActionCommand(OpenCommandPalette),
        });
        TextBox search = this.FindControl<TextBox>("CommandPaletteSearch")!;
        search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                RefreshCommandPaletteResults();
            }
        };
        search.AddHandler(KeyDownEvent, HandlePaletteKeyDown, RoutingStrategies.Tunnel);
        ListBox results = this.FindControl<ListBox>("CommandPaletteResults")!;
        results.AddHandler(KeyDownEvent, HandlePaletteKeyDown, RoutingStrategies.Tunnel);
        results.DoubleTapped += (_, _) => ExecuteSelectedCommand();
        ViewModel.PropertyChanged += HandleViewModelPropertyChanged;
        Opened += HandleOpened;
        Closing += HandleClosing;
        Closed += HandleClosed;
        ShowCurrentEditor();
    }

    public MainWindowViewModel ViewModel { get; }

    public bool IsCommandPaletteOpen => this.FindControl<Border>("CommandPalette")!.IsVisible;

    /// <summary>Commands currently offered by command search, before filtering.</summary>
    internal IReadOnlyList<CommandPaletteEntry> CommandPaletteEntries => paletteEntries;

    public void OpenCommandPalette()
    {
        if (closed)
        {
            return;
        }

        Border palette = this.FindControl<Border>("CommandPalette")!;
        if (!palette.IsVisible)
        {
            paletteReturnFocus = FocusManager?.GetFocusedElement() as Control;
        }

        paletteEntries = BuildCommands();
        palette.IsVisible = true;
        TextBox search = this.FindControl<TextBox>("CommandPaletteSearch")!;
        search.Text = string.Empty;
        RefreshCommandPaletteResults();
        search.Focus(NavigationMethod.Tab, KeyModifiers.None);
    }

    public void CloseCommandPalette()
    {
        Border palette = this.FindControl<Border>("CommandPalette")!;
        if (!palette.IsVisible)
        {
            return;
        }

        palette.IsVisible = false;
        Control? target = paletteReturnFocus;
        paletteReturnFocus = null;
        if (!TryFocus(target) && this.FindControl<ContentControl>("CurrentStepContent")!.Content is Control view)
        {
            FocusEditorAfterLayout(view);
        }
    }

    /// <summary>
    /// Step moves, settings categories, panel display and layout operations of the current screen,
    /// layout reset and the Copilot status check. AI evaluation and GitHub login are deliberately absent.
    /// </summary>
    internal IReadOnlyList<CommandPaletteEntry> BuildCommands()
    {
        List<CommandPaletteEntry> entries = [];
        foreach (WorkflowStepPresentation step in ViewModel.Steps)
        {
            if (ViewModel.NavigateCommand.CanExecute(step.Step))
            {
                WorkflowStep target = step.Step;
                entries.Add(new($"Navigate.{target}", $"{step.Number} {step.Title}へ移動（Alt+{step.Number}）", "ステップ",
                    () => ViewModel.NavigateCommand.Execute(target)));
            }
        }

        foreach (SettingsCategory category in Enum.GetValues<SettingsCategory>())
        {
            entries.Add(new($"Settings.{category}", $"設定「{SettingsCategoryTitle(category)}」を開く", "設定",
                () => ViewModel.OpenSettings(category)));
        }

        if (CurrentWorkspace() is { } workspace)
        {
            string screen = WorkspaceScreens.DisplayName(workspace.ScreenKey);
            WorkspaceLayout layout = workspace.Layout;
            foreach (WorkspacePanel panel in workspace.Panels)
            {
                string id = panel.PanelId;
                foreach ((WorkspacePanelOperation operation, string label) in PanelCommandLabels)
                {
                    if (workspace.CanExecute(operation, id))
                    {
                        entries.Add(new($"Panel.{id}.{operation}", string.Format(CultureInfo.InvariantCulture, label, panel.Title),
                            screen, () => workspace.Execute(operation, id)));
                    }
                }
            }

            if (layout.MaximizedPanelId is { } maximized)
            {
                entries.Add(new("Layout.Restore", "最大化を元に戻す", screen,
                    () => workspace.Execute(WorkspacePanelOperation.Restore, maximized)));
            }
            else if (layout.VisiblePanelIds.FirstOrDefault() is { } first)
            {
                entries.Add(new("Layout.ToggleOrientation",
                    layout.Orientation == WorkspaceOrientation.Horizontal ? "パネルを上下に並べる" : "パネルを左右に並べる",
                    screen, () => workspace.Execute(WorkspacePanelOperation.ToggleOrientation, first)));
            }

            entries.Add(new("Layout.Reset", "既定のレイアウトに戻す", screen, workspace.ResetLayout));
        }

        if (ViewModel.ExecutionViewModel.CheckAuthenticationCommand.CanExecute(null))
        {
            entries.Add(new("Copilot.CheckStatus", "Copilot 状態を確認", "Copilot",
                () => ViewModel.ExecutionViewModel.CheckAuthenticationCommand.Execute(null)));
        }

        return entries;
    }

    private static readonly (WorkspacePanelOperation Operation, string Label)[] PanelCommandLabels =
    [
        (WorkspacePanelOperation.Show, "パネル「{0}」を表示"),
        (WorkspacePanelOperation.Hide, "パネル「{0}」を隠す"),
        (WorkspacePanelOperation.MoveEarlier, "パネル「{0}」を前へ移動"),
        (WorkspacePanelOperation.MoveLater, "パネル「{0}」を後ろへ移動"),
        (WorkspacePanelOperation.Grow, "パネル「{0}」を広げる"),
        (WorkspacePanelOperation.Shrink, "パネル「{0}」を狭める"),
        (WorkspacePanelOperation.MergeIntoTabs, "パネル「{0}」をタブにまとめる"),
        (WorkspacePanelOperation.SeparateFromTabs, "パネル「{0}」をタブから出す"),
        (WorkspacePanelOperation.Maximize, "パネル「{0}」を最大化"),
    ];

    private static string SettingsCategoryTitle(SettingsCategory category) => category switch
    {
        SettingsCategory.Common => "共通",
        SettingsCategory.Mapping => "入力詳細",
        SettingsCategory.Evaluation => "通常評価",
        SettingsCategory.Special => "固有評価",
        SettingsCategory.ImportedPrompts => "読込Prompt",
        _ => category.ToString(),
    };

    private PanelWorkspace? CurrentWorkspace() =>
        this.FindControl<ContentControl>("CurrentStepContent")!.Content is Control view
            ? view.GetVisualDescendants().OfType<PanelWorkspace>().FirstOrDefault()
            : null;

    private void RefreshCommandPaletteResults()
    {
        ListBox results = this.FindControl<ListBox>("CommandPaletteResults")!;
        IReadOnlyList<CommandPaletteEntry> matches = CommandPaletteFilter.Filter(
            paletteEntries, this.FindControl<TextBox>("CommandPaletteSearch")!.Text);
        results.ItemsSource = matches;
        results.SelectedIndex = matches.Count > 0 ? 0 : -1;
    }

    private void HandlePaletteKeyDown(object? sender, KeyEventArgs e)
    {
        ListBox results = this.FindControl<ListBox>("CommandPaletteResults")!;
        int count = results.ItemCount;
        switch (e.Key)
        {
            case Key.Escape:
                CloseCommandPalette();
                e.Handled = true;
                break;
            case Key.Enter:
                ExecuteSelectedCommand();
                e.Handled = true;
                break;
            case Key.Down when sender is TextBox && count > 0:
                results.SelectedIndex = Math.Min(count - 1, results.SelectedIndex + 1);
                results.ScrollIntoView(results.SelectedIndex);
                e.Handled = true;
                break;
            case Key.Up when sender is TextBox && count > 0:
                results.SelectedIndex = Math.Max(0, results.SelectedIndex - 1);
                results.ScrollIntoView(results.SelectedIndex);
                e.Handled = true;
                break;
        }
    }

    private void ExecuteSelectedCommand()
    {
        if (this.FindControl<ListBox>("CommandPaletteResults")!.SelectedItem is not CommandPaletteEntry entry)
        {
            return;
        }

        CloseCommandPalette();
        entry.Execute();
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or "" or nameof(MainWindowViewModel.CurrentEditorViewModel)
            or nameof(MainWindowViewModel.IsSettingsOpen)))
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowCurrentEditor();
        }
        else
        {
            Dispatcher.UIThread.Post(ShowCurrentEditor);
        }
    }

    private void ShowCurrentEditor()
    {
        UiObservableObject? editor = ViewModel.CurrentEditorViewModel;
        if (closed || ReferenceEquals(currentEditor, editor))
        {
            return;
        }

        Control? returnTarget = null;
        if (editor is SettingsViewModel && currentEditor is not SettingsViewModel)
        {
            // Capture before detaching the invoking view, not from a control name
            // after it has been recreated. Category changes do not replace this target.
            settingsReturnEditor = currentEditor;
            settingsReturnTarget = FocusManager?.GetFocusedElement() as Control;
        }
        else if (currentEditor is SettingsViewModel)
        {
            if (ReferenceEquals(editor, settingsReturnEditor))
            {
                returnTarget = settingsReturnTarget;
            }

            settingsReturnEditor = null;
            settingsReturnTarget = null;
        }

        ContentControl content = this.FindControl<ContentControl>("CurrentStepContent")!;
        Control? view = null;
        if (editor is not null && !editorViews.TryGetValue(editor, out view))
        {
            IDataTemplate template = content.DataTemplates.First(candidate => candidate.Match(editor));
            view = template.Build(editor)
                ?? throw new InvalidOperationException("The current editor template must produce a control.");
            // Build does not set DataContext. A template-root binding would read
            // the host's inherited shell context when it is applied. Set this local
            // owner once; cache hits must not rebind or reset unfinished editors.
            view.DataContext = editor;
            if (view is InputView inputView)
            {
                inputView.AttachExecutionPreparation(ViewModel.ExecutionViewModel);
            }
            editorViews.Add(editor, view);
        }

        CloseCommandPalette();
        content.Content = null;
        currentEditor = editor;
        content.Content = view;
        focusVersion++;
        this.FindControl<ScrollViewer>("ShellScrollViewer")!.Offset = default;
        // Settings owns its five categories and their initial focus. The shell
        // must not reset the cached Settings DataContext or initialize its store.
        if (opened && view is not null && editor is not SettingsViewModel)
        {
            FocusEditorAfterLayout(view, returnTarget);
        }
    }

    private void HandleBodySizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!closed && double.IsFinite(e.NewSize.Height) && e.NewSize.Height > 0d)
        {
            // Normal editors already fit a 450-DIP slot. Narrow Input reflows its
            // range fields into two rows; only that smaller viewport needs more
            // canvas. No header/footer is measured at infinity or scrolled away.
            double minimumHeight = e.NewSize.Width < 856d ? 520d : 450d;
            this.FindControl<Grid>("ShellBody")!.Height = Math.Max(minimumHeight, e.NewSize.Height);
        }
    }

    private void FocusEditorAfterLayout(Control view, Control? returnTarget = null)
    {
        long scheduledVersion = focusVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (closed || !opened || scheduledVersion != focusVersion
                || !ReferenceEquals(this.FindControl<ContentControl>("CurrentStepContent")!.Content, view))
            {
                return;
            }

            if (TryFocus(returnTarget))
            {
                return;
            }

            // GoToProblem posts a specific field focus at Loaded priority. Run
            // afterwards and keep that focus instead of stealing it for the default.
            if (FocusManager?.GetFocusedElement() is Control focused
                && focused.IsEffectivelyVisible && focused.IsEffectivelyEnabled
                && focused.GetVisualAncestors().Contains(view))
            {
                return;
            }

            Control? target = view switch
            {
                InputView input => input.FindControl<TextBox>("FilePathTextBox"),
                QuantificationDesignView design => design.FindControl<TextBox>("BasePointsTextBox"),
                ExecutionView execution => execution.FindControl<Button>("ChangeExecutionSettingsButton"),
                ResultsOutputView results => ResultsFocusTarget(results),
                _ => null,
            };
            if (!TryFocus(target))
            {
                TryFocus(view.GetVisualDescendants().OfType<Control>().FirstOrDefault(control =>
                    control.Focusable && control.IsTabStop && control.IsEffectivelyVisible && control.IsEffectivelyEnabled));
            }
        }, DispatcherPriority.Background);
    }

    private static Control? ResultsFocusTarget(ResultsOutputView view)
    {
        ListBox list = view.PreferredFocusList();
        return list is { SelectedIndex: >= 0 } ? list.ContainerFromIndex(list.SelectedIndex) ?? list : list;
    }

    private bool TryFocus(Control? target)
    {
        if (target is not { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true }
            || !ReferenceEquals(TopLevel.GetTopLevel(target), this))
        {
            return false;
        }

        target.BringIntoView();
        return target.Focus(NavigationMethod.Tab, KeyModifiers.None);
    }

    private void HandleOpened(object? sender, EventArgs e)
    {
        Opened -= HandleOpened;
        opened = true;
        if (!ViewModel.IsSettingsOpen && ViewModel.CurrentStep == WorkflowStep.Input)
        {
            TryFocus(this.FindControl<Button>("InputStepButton"));
        }
        else if (this.FindControl<ContentControl>("CurrentStepContent")!.Content is Control view
            && view is not SettingsView)
        {
            FocusEditorAfterLayout(view);
        }
    }

    private void HandleClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closed || finalCloseAllowed) return;

        if (e.CloseReason == WindowCloseReason.OSShutdown)
        {
            // OS termination cannot be delayed reliably. Request cooperative
            // interruption, but do not veto shutdown or kill any process.
            if (!closePending)
            {
                closePending = true;
                _ = DrainBeforeCloseAsync(closeAfterDrain: false);
            }
            return;
        }

        // Repeated user/programmatic requests must not skip the pending drain,
        // including the interval between IsRunning=false and task completion.
        if (closePending)
        {
            e.Cancel = true;
            return;
        }

        ExecutionViewModel execution = ViewModel.ExecutionViewModel;
        if (!execution.IsRunning && execution.LastRunTask is not { IsCompleted: false }) return;
        e.Cancel = true;
        closePending = true;
        _ = DrainBeforeCloseAsync(closeAfterDrain: true);
    }

    private async Task DrainBeforeCloseAsync(bool closeAfterDrain)
    {
        try
        {
            await ViewModel.ExecutionViewModel.StopAndDrainAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Shutdown is finite and never depends on successful evaluation.
            // Do not expose exception details or interfere with external tools.
        }
        finally
        {
            if (closeAfterDrain)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (closed) return;
                    finalCloseAllowed = true; // Only this internal Close bypasses the guard.
                    Close();
                });
            }
        }
    }

    private void HandleClosed(object? sender, EventArgs e)
    {
        closed = true;
        opened = false;
        focusVersion++;
        Opened -= HandleOpened;
        Closing -= HandleClosing;
        Closed -= HandleClosed;
        ViewModel.PropertyChanged -= HandleViewModelPropertyChanged;
        this.FindControl<ContentControl>("CurrentStepContent")!.Content = null;
        currentEditor = null;
        settingsReturnEditor = null;
        settingsReturnTarget = null;
        editorViews.Clear();
        ViewModel.Dispose();
    }

    private sealed class WindowActionCommand(Action action) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => action();
    }
}
