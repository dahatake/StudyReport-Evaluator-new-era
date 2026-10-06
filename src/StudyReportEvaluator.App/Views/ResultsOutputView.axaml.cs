using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workspace;

namespace StudyReportEvaluator.App.Views;

public sealed partial class ResultsOutputView : UserControl
{
    private const double MinimumContentHeight = 416d;
    private const double MinimumRowHeight = 44d;
    private readonly Grid resultsLayout;
    private readonly ListBox rowScoreList;
    private readonly ListBox resultsList;
    private readonly ContentControl criterionEditor;
    private readonly TextBox goToRowTextBox;
    private readonly PanelWorkspace workspace;
    private readonly WorkspacePanelFlagSync costSync;
    private ResultsOutputViewModel? observedViewModel;
    private (ResultsOutputViewModel ViewModel, ResultsCriterionViewModel? FirstResult, int? RowNumber)? detachedGoToRowSource;
    private long lifetime;
    private bool attached;
    private bool refreshQueued;
    private bool focusRequested;
    private bool scrollCriterionRequested;
    private bool updatingGoToRow;
    private bool synchronizingDetail;
    private bool focusDetail;

    public ResultsOutputView()
        : this(new ResultsOutputViewModel())
    {
    }

    public ResultsOutputView(ResultsOutputViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        resultsLayout = this.FindControl<Grid>("ResultsLayout")!;
        rowScoreList = this.FindControl<ListBox>("RowScoreList")!;
        resultsList = this.FindControl<ListBox>("ResultsList")!;
        criterionEditor = this.FindControl<ContentControl>("CriterionEditor")!;
        goToRowTextBox = this.FindControl<TextBox>("GoToRowTextBox")!;
        workspace = this.FindControl<PanelWorkspace>("ResultsWorkspace")!;
        // The fixed cost toggle selects the cost/log panel when it shares a tab group.
        costSync = new WorkspacePanelFlagSync(workspace, WorkspacePanelIds.ResultsCost,
            () => (DataContext as ResultsOutputViewModel)?.Cost.IsExpanded,
            value =>
            {
                if (DataContext is ResultsOutputViewModel current)
                {
                    current.Cost.IsExpanded = value;
                }
            });
        workspace.PresentationChanged += HandleWorkspacePresentationChanged;
        DataContext = viewModel;
        AttachedToVisualTree += HandleAttached;
        DetachedFromVisualTree += HandleDetached;
        DataContextChanged += HandleDataContextChanged;
        SizeChanged += HandleSizeChanged;
        LayoutUpdated += HandleLayoutUpdated;
        resultsList.SelectionChanged += HandleCriterionSelectionChanged;
        // ListBoxItem handles Enter for selection before it bubbles to the list.
        rowScoreList.AddHandler(KeyDownEvent, HandleRowKeyDown, handledEventsToo: true);
        rowScoreList.DoubleTapped += HandleRowDoubleTapped;
        goToRowTextBox.PropertyChanged += HandleGoToRowPropertyChanged;
        goToRowTextBox.KeyDown += HandleGoToRowKeyDown;
    }

    public ResultsOutputViewModel ViewModel => DataContext as ResultsOutputViewModel
        ?? throw new InvalidOperationException("ResultsOutputView requires a ResultsOutputViewModel data context.");

    private void HandleAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        attached = true;
        costSync.Reset();
        ObserveCurrentViewModel();
        focusRequested = TopLevel.GetTopLevel(this) is Window window
            && ReferenceEquals(window.Content, this);
    }

    private void HandleDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        detachedGoToRowSource = observedViewModel is { } current
            ? (current, current.Results.FirstOrDefault(), current.GoToRowNumber)
            : null;
        attached = false;
        StopObserving();
    }

    private void HandleDataContextChanged(object? sender, EventArgs e)
    {
        detachedGoToRowSource = null;
        ObserveCurrentViewModel();
    }

    private void ObserveCurrentViewModel()
    {
        StopObserving();
        if (attached && DataContext is ResultsOutputViewModel current)
        {
            // Criteria survive score recomputation, but Load replaces them even for identical run metadata.
            bool sameGoToRowSource = detachedGoToRowSource is { } previous
                && ReferenceEquals(previous.ViewModel, current)
                && ReferenceEquals(previous.FirstResult, current.Results.FirstOrDefault())
                && (previous.FirstResult is not null || !current.IsLoaded)
                && previous.RowNumber == current.GoToRowNumber;
            detachedGoToRowSource = null;
            observedViewModel = current;
            current.PropertyChanged += HandleResultsPropertyChanged;
            current.Cost.PropertyChanged += HandleCostPropertyChanged;
            scrollCriterionRequested = true;
            if (!sameGoToRowSource)
            {
                UpdateGoToRowText(current);
            }

            QueuePresentationRefresh();
        }
    }

    private void StopObserving()
    {
        if (observedViewModel is { } previous)
        {
            previous.PropertyChanged -= HandleResultsPropertyChanged;
            previous.Cost.PropertyChanged -= HandleCostPropertyChanged;
        }

        observedViewModel = null;
        lifetime++;
        refreshQueued = false;
        focusRequested = false;
        scrollCriterionRequested = false;
        // The parent caches this view. Keep the selector, not a detached editor or VM subscription.
        criterionEditor.Content = null;
    }

    private bool IsCurrent(ResultsOutputViewModel viewModel) => attached
        && ReferenceEquals(viewModel, observedViewModel)
        && ReferenceEquals(viewModel, DataContext);

    private void HandleResultsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ResultsOutputViewModel current || !IsCurrent(current))
        {
            return;
        }

        if (e.PropertyName == nameof(ResultsOutputViewModel.IsLoaded))
        {
            // Load may reuse the same VM. Invalidate work posted for the previous run as well.
            lifetime++;
            refreshQueued = false;
            focusRequested = false;
            scrollCriterionRequested = false;
            criterionEditor.Content = null;
            UpdateGoToRowText(current);
        }
        else if (e.PropertyName == nameof(ResultsOutputViewModel.GoToRowNumber) && !updatingGoToRow)
        {
            UpdateGoToRowText(current);
        }
        else if (e.PropertyName == nameof(ResultsOutputViewModel.SelectedRow))
        {
            // A page reset can clear SelectedItem without changing the source reference.
            // Reassert it while the VM still guards collection-induced TwoWay feedback.
            BindingOperations.GetBindingExpressionBase(rowScoreList, ListBox.SelectedItemProperty)?.UpdateTarget();
        }
        else if (e.PropertyName == nameof(ResultsOutputViewModel.SelectedCriterion))
        {
            scrollCriterionRequested = true;
        }
        else if (e.PropertyName == nameof(ResultsOutputViewModel.IsDetailVisible))
        {
            scrollCriterionRequested = current.IsDetailVisible;
            if (!current.IsDetailVisible)
            {
                criterionEditor.Content = null;
            }

            if (!synchronizingDetail)
            {
                // A request to see the detail or the list brings that panel to the front.
                focusRequested = true;
                focusDetail = current.IsDetailVisible;
                if (current.IsDetailVisible)
                {
                    workspace.Reveal(WorkspacePanelIds.ResultsDetail);
                }
                else
                {
                    workspace.Select(WorkspacePanelIds.ResultsList);
                }
            }
        }

        if (e.PropertyName is nameof(ResultsOutputViewModel.SelectedRow)
            or nameof(ResultsOutputViewModel.SelectedCriterion)
            or nameof(ResultsOutputViewModel.IsDetailVisible)
            or nameof(ResultsOutputViewModel.IsLoaded))
        {
            QueuePresentationRefresh();
        }
    }

    private void HandleSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (double.IsFinite(Bounds.Height) && Bounds.Height > 0d)
        {
            resultsLayout.Height = Math.Max(MinimumContentHeight, Bounds.Height - resultsLayout.Margin.Top - resultsLayout.Margin.Bottom);
        }

        QueuePresentationRefresh();
    }

    private void HandleLayoutUpdated(object? sender, EventArgs e) => QueuePresentationRefresh();

    private void HandleCostPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JobCostViewModel.IsExpanded)
            && observedViewModel is { } current && ReferenceEquals(sender, current.Cost))
        {
            // An explicit panel switch keeps focus on its checkbox. Do not replay
            // an earlier detail/list focus request after the reader closes cost.
            focusRequested = false;
            costSync.FlagChanged();
            QueuePresentationRefresh();
        }
    }

    private void QueuePresentationRefresh()
    {
        if (refreshQueued || observedViewModel is not { } current || !IsCurrent(current))
        {
            return;
        }

        refreshQueued = true;
        long scheduledLifetime = lifetime;
        Dispatcher.UIThread.Post(() =>
        {
            if (scheduledLifetime != lifetime || !IsCurrent(current))
            {
                return;
            }

            refreshQueued = false;
            if (current.Cost.IsExpanded)
            {
                focusRequested = false;
            }

            SynchronizeDetailPresentation(current);
            ResizePage(current);
            SynchronizeCriterion(current);
            if (focusRequested && IsEffectivelyVisible)
            {
                focusRequested = false;
                ListBox activeList = focusDetail && resultsList.IsEffectivelyVisible ? resultsList : PreferredFocusList();
                Control target = activeList.SelectedIndex >= 0
                    ? activeList.ContainerFromIndex(activeList.SelectedIndex) ?? activeList
                    : activeList;
                if (target.IsEffectivelyVisible)
                {
                    target.Focus(NavigationMethod.Tab, KeyModifiers.None);
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private void ResizePage(ResultsOutputViewModel current)
    {
        if (!rowScoreList.IsEffectivelyVisible)
        {
            return;
        }

        ScrollViewer? scroll = rowScoreList.GetVisualDescendants().OfType<ScrollViewer>()
            .FirstOrDefault(item => ReferenceEquals(item.TemplatedParent, rowScoreList));
        double height = scroll?.Viewport.Height ?? 0d;
        if (!double.IsFinite(height) || height < MinimumRowHeight)
        {
            return;
        }

        double measuredRowHeight = rowScoreList.ContainerFromIndex(0)?.Bounds.Height ?? MinimumRowHeight;
        double rowHeight = double.IsFinite(measuredRowHeight)
            ? Math.Max(MinimumRowHeight, measuredRowHeight)
            : MinimumRowHeight;
        int pageSize = (int)Math.Min(int.MaxValue, Math.Max(1d, Math.Floor(height / rowHeight)));
        if (current.PageSize != pageSize)
        {
            current.PageSize = pageSize;
        }
    }

    /// <summary>The row list when it is shown (side by side or as the selected tab), otherwise the criteria list.</summary>
    internal ListBox PreferredFocusList() => rowScoreList.IsEffectivelyVisible || !resultsList.IsEffectivelyVisible
        ? rowScoreList
        : resultsList;

    private void HandleWorkspacePresentationChanged(object? sender, EventArgs e)
    {
        costSync.PresentationChanged();
        QueuePresentationRefresh();
    }

    // The view model reads student answers only while the detail is shown. Keep that state equal
    // to what the workspace presents: side by side, as the selected tab, or not at all.
    private void SynchronizeDetailPresentation(ResultsOutputViewModel current)
    {
        bool presented = workspace.IsPresented(WorkspacePanelIds.ResultsDetail);
        synchronizingDetail = true;
        try
        {
            if (presented && !current.IsDetailVisible && current.ShowDetailCommand.CanExecute(null))
            {
                current.ShowDetailCommand.Execute(null);
            }
            else if (!presented && current.IsDetailVisible && current.ShowListCommand.CanExecute(null))
            {
                current.ShowListCommand.Execute(null);
            }
        }
        finally
        {
            synchronizingDetail = false;
        }
    }

    private void SynchronizeCriterion(ResultsOutputViewModel current)
    {
        if (current.SelectedCriterion is not { } selected
            || !current.SelectedRowCriteria.Contains(selected))
        {
            current.SelectedCriterion = current.SelectedRowCriteria.FirstOrDefault();
        }

        resultsList.SelectedItem = current.SelectedCriterion;
        if (scrollCriterionRequested && current.IsDetailVisible && current.SelectedCriterion is { } target)
        {
            scrollCriterionRequested = false;
            resultsList.ScrollIntoView(target);
        }

        UpdateCriterionEditor(current);
    }

    private void HandleCriterionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (observedViewModel is { } current && IsCurrent(current))
        {
            UpdateCriterionEditor(current);
            QueuePresentationRefresh();
        }
    }

    private void UpdateCriterionEditor(ResultsOutputViewModel current)
    {
        object? selected = current.IsDetailVisible
            && resultsList.SelectedItem is ResultsCriterionViewModel criterion
            && current.SelectedRowCriteria.Contains(criterion)
                ? criterion
                : null;
        if (!ReferenceEquals(criterionEditor.Content, selected))
        {
            criterionEditor.Content = selected;
        }
    }

    private void UpdateGoToRowText(ResultsOutputViewModel current)
    {
        updatingGoToRow = true;
        try
        {
            goToRowTextBox.Text = current.GoToRowNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }
        finally
        {
            updatingGoToRow = false;
        }
    }

    private void HandleGoToRowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
        {
            UpdateGoToRowNumber();
        }
    }

    private void UpdateGoToRowNumber()
    {
        if (updatingGoToRow || observedViewModel is not { } current || !IsCurrent(current))
        {
            return;
        }

        // Unlike a failed string-to-int binding, invalid text cannot leave a previous valid target armed.
        updatingGoToRow = true;
        try
        {
            current.GoToRowNumber = int.TryParse(goToRowTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int row)
                ? row
                : null;
        }
        finally
        {
            updatingGoToRow = false;
        }
    }

    private void HandleGoToRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && observedViewModel is { } current && IsCurrent(current))
        {
            UpdateGoToRowNumber();
            if (current.GoToRowCommand.CanExecute(null))
            {
                current.GoToRowCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void HandleRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ShowCurrentDetail();
            e.Handled = true;
        }
    }

    private void HandleRowDoubleTapped(object? sender, TappedEventArgs e) => ShowCurrentDetail();

    private void ShowCurrentDetail()
    {
        if (observedViewModel is { } current && IsCurrent(current) && current.ShowDetailCommand.CanExecute(null))
        {
            current.ShowDetailCommand.Execute(null);
        }
    }
}