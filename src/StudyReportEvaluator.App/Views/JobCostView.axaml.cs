using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using StudyReportEvaluator.App.ViewModels;

namespace StudyReportEvaluator.App.Views;

public sealed partial class JobCostView : UserControl
{
    public static readonly StyledProperty<string> AutomationScopeProperty =
        AvaloniaProperty.Register<JobCostView, string>(nameof(AutomationScope), "JobCost");

    private readonly TextBox logTextBox;
    private readonly ScrollViewer logScrollViewer;
    private JobCostViewModel? observed;
    private Guid? displayedJobId;
    private bool attached;
    private bool followPending;
    private bool hasDisplayedLog;

    public JobCostView()
    {
        InitializeComponent();
        logTextBox = this.FindControl<TextBox>("LogTextBox")!;
        logScrollViewer = this.FindControl<ScrollViewer>("LogScrollViewer")!;
        DataContextChanged += (_, _) => Observe();
        LayoutUpdated += (_, _) => FollowAfterLayout();
        ApplyAutomationScope();
    }

    /// <summary>The host supplies a stable unique scope (ExecutionCost or ResultsCost).</summary>
    public string AutomationScope
    {
        get => GetValue(AutomationScopeProperty);
        set => SetValue(AutomationScopeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AutomationScopeProperty)
        {
            ApplyAutomationScope();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        Observe();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        if (observed is not null)
        {
            observed.PropertyChanged -= HandleCostChanged;
        }

        followPending = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void Observe()
    {
        if (observed is not null)
        {
            observed.PropertyChanged -= HandleCostChanged;
        }

        bool changed = !ReferenceEquals(observed, DataContext);
        observed = DataContext as JobCostViewModel;
        if (attached && observed is not null)
        {
            observed.PropertyChanged += HandleCostChanged;
        }

        UpdateLog(force: changed || !hasDisplayedLog);
    }

    private void HandleCostChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (attached && ReferenceEquals(sender, observed)
            && e.PropertyName is nameof(JobCostViewModel.Snapshot) or nameof(JobCostViewModel.AutoFollow))
        {
            UpdateLog(force: false);
        }
    }

    private void UpdateLog(bool force)
    {
        Guid? jobId = observed?.Snapshot?.JobId;
        bool differentJob = displayedJobId != jobId;
        if (force || differentJob || observed?.AutoFollow != false)
        {
            string text = observed?.LogText ?? JobCostViewModel.NoDataText;
            if (logTextBox.Text != text)
            {
                logTextBox.Text = text;
            }

            displayedJobId = jobId;
            hasDisplayedLog = true;
            if (force || differentJob)
            {
                logTextBox.SelectionStart = logTextBox.SelectionEnd = 0;
                logScrollViewer.Offset = default;
            }
        }

        followPending = observed?.AutoFollow == true;
        InvalidateArrange();
    }

    private void FollowAfterLayout()
    {
        if (!attached || !followPending || observed?.AutoFollow != true
            || !logScrollViewer.IsEffectivelyVisible || logScrollViewer.Viewport.Height <= 0)
        {
            return;
        }

        followPending = false;
        logScrollViewer.ScrollToEnd();
    }

    private void ApplyAutomationScope()
    {
        AutomationProperties.SetAutomationId(this, AutomationScope);
        foreach (string name in new[] { "CostTabs", "DetailsTab", "LogTab", "CostDetailsTextBox",
            "AutoFollowCheckBox", "LogScrollViewer", "LogTextBox", "LogStatusText", "OpenLogButton",
            "OpenDirectoryButton", "OpenStatusText" })
        {
            if (this.FindControl<Control>(name) is { } control)
            {
                AutomationProperties.SetAutomationId(control, $"{AutomationScope}-{name}");
            }
        }
    }
}