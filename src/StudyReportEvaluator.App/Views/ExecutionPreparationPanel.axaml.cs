using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workflow;

namespace StudyReportEvaluator.App.Views;

/// <summary>Copilot login, checkpoint resume and technical-error parts of run preparation, hosted by the Input step.</summary>
public sealed partial class ExecutionPreparationPanel : UserControl
{
    private readonly IResumeCheckpointPicker picker;
    private ExecutionViewModel? owner;
    private (string Code, string? NodeId, string? Path, string Field)? selectedErrorKey;
    private ResumeAdmissionItem? selectedResumeItem;
    private long pickerVersion;
    private bool isPicking;
    private bool attached;

    public ExecutionPreparationPanel()
        : this(new NativeResumeCheckpointPicker())
    {
    }

    public ExecutionPreparationPanel(IResumeCheckpointPicker picker)
    {
        this.picker = picker ?? throw new ArgumentNullException(nameof(picker));
        InitializeComponent();
        DataContextChanged += HandleDataContextChanged;
    }

    public ExecutionViewModel? ViewModel => DataContext as ExecutionViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        Subscribe();
        RefreshPresentation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        pickerVersion++;
        Unsubscribe();
        // The host, not a temporary view detachment, owns login/run cancellation.
        base.OnDetachedFromVisualTree(e);
    }

    private void Subscribe()
    {
        if (owner is not null)
        {
            owner.PropertyChanged += HandleExecutionPropertyChanged;
            ((INotifyCollectionChanged)owner.ResumeFindings).CollectionChanged += HandleResumeFindingsChanged;
        }
    }

    private void Unsubscribe()
    {
        if (owner is not null)
        {
            owner.PropertyChanged -= HandleExecutionPropertyChanged;
            ((INotifyCollectionChanged)owner.ResumeFindings).CollectionChanged -= HandleResumeFindingsChanged;
        }
    }

    private void HandleDataContextChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(owner, DataContext))
        {
            return;
        }

        if (attached)
        {
            Unsubscribe();
        }

        pickerVersion++;
        owner = DataContext as ExecutionViewModel;
        selectedErrorKey = null;
        selectedResumeItem = null;
        SetResumePickerStatus(string.Empty);
        if (attached)
        {
            Subscribe();
        }

        RefreshPresentation();
    }

    private void HandleExecutionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, owner)
            && (string.IsNullOrEmpty(e.PropertyName)
                || e.PropertyName is nameof(ExecutionViewModel.IsRunning) or nameof(ExecutionViewModel.LastRunTask)
                    or nameof(ExecutionViewModel.IsResumeMode) or nameof(ExecutionViewModel.ResumePartialPath)
                    or nameof(ExecutionViewModel.ResumeResetReason)
                || (e.PropertyName == nameof(ExecutionViewModel.CanEditResume) && owner?.CanEditResume != true)))
        {
            // Even a run that starts and finishes while the native dialog is open
            // invalidates the selection. Rebinding/detachment also increments this.
            pickerVersion++;
        }

        if (!attached || !ReferenceEquals(sender, owner)
            || (!string.IsNullOrEmpty(e.PropertyName)
                && e.PropertyName is not (nameof(ExecutionViewModel.IsRunning)
                    or nameof(ExecutionViewModel.CanEditResume)
                    or nameof(ExecutionViewModel.HasTechnicalErrors))))
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (attached && ReferenceEquals(sender, owner))
                {
                    RefreshPresentation();
                }
            });
            return;
        }

        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        ListBox errors = this.FindControl<ListBox>("TechnicalErrorsList")!;
        if (owner is null || owner.TechnicalErrors.Count == 0)
        {
            selectedErrorKey = null;
            errors.SelectedItem = null;
        }
        else
        {
            // Revalidation replaces the items. Retain the logical selection, not
            // a stale instance; collection-reset null feedback is not a user edit.
            errors.SelectedItem = owner.TechnicalErrors.FirstOrDefault(error =>
                selectedErrorKey == error.Key) ?? owner.TechnicalErrors[0];
        }

        RefreshSelectedErrorDetail();
        RefreshResumeFindings();
        RefreshResumePickerAvailability();
    }
    private void HandleResumeFindingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!attached || !ReferenceEquals(sender, owner?.ResumeFindings)) return;
        if (Dispatcher.UIThread.CheckAccess()) RefreshResumeFindings();
        else Dispatcher.UIThread.Post(() =>
        {
            if (attached && ReferenceEquals(sender, owner?.ResumeFindings)) RefreshResumeFindings();
        });
    }

    private void RefreshResumeFindings()
    {
        if (this.FindControl<ListBox>("ResumeFindingsList") is not { } list) return;
        list.SelectedItem = owner?.ResumeFindings.FirstOrDefault(finding => finding.Item == selectedResumeItem)
            ?? owner?.ResumeFindings.FirstOrDefault();
        RefreshResumeFindingDetail();
    }

    private void HandleResumeFindingSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        RefreshResumeFindingDetail();

    private void RefreshResumeFindingDetail()
    {
        ResumeAdmissionFinding? finding = this.FindControl<ListBox>("ResumeFindingsList")?.SelectedItem as ResumeAdmissionFinding;
        if (finding is not null) selectedResumeItem = finding.Item;
        if (this.FindControl<TextBox>("ResumeFindingDetail") is not { } detail) return;
        string itemName = finding?.Item switch
        {
            ResumeAdmissionItem.PartialPath => "再開元の場所",
            ResumeAdmissionItem.InputIdentity => "入力ファイル",
            ResumeAdmissionItem.Definition => "採点設計",
            ResumeAdmissionItem.NormalModel => "通常評価モデル",
            ResumeAdmissionItem.Runtime => "アプリ・CLI・SDK の版",
            ResumeAdmissionItem.CheckpointShape => "checkpoint の形式・整合性",
            _ => "再開条件",
        };
        detail.Text = finding is null ? string.Empty
            : $"{itemName}: {(finding.IsSatisfied ? "一致" : "要確認")}{Environment.NewLine}{finding.StatusCode}{Environment.NewLine}{finding.Description}";
    }

    public async Task PickResumeCheckpointAsync()
    {
        if (isPicking || !attached || owner is not { CanEditResume: true } execution
            || TopLevel.GetTopLevel(this) is not { } topLevel) return;

        long version = pickerVersion;
        Task? run = execution.LastRunTask;
        bool IsCurrentSelection() => attached && version == pickerVersion
            && ReferenceEquals(owner, execution) && ReferenceEquals(DataContext, execution)
            && ReferenceEquals(TopLevel.GetTopLevel(this), topLevel)
            && execution.CanEditResume && ReferenceEquals(run, execution.LastRunTask);

        isPicking = true;
        RefreshResumePickerAvailability();
        try
        {
            string? path = await picker.PickAsync(topLevel);
            if (!IsCurrentSelection() || string.IsNullOrWhiteSpace(path)) return;
            if (!Path.IsPathFullyQualified(path)) throw new ResumeCheckpointPickerPathUnavailableException();

            SetResumePickerStatus(string.Empty);
            execution.IsResumeMode = true;
            execution.ResumePartialPath = path;
            // Selection only: the user still explicitly starts the run.
            await execution.PrepareResumeAsync();
        }
        catch (ResumeCheckpointPickerPathUnavailableException)
        {
            if (IsCurrentSelection())
                SetResumePickerStatus("選択したファイルのローカルパスを取得できません。パスを直接入力してください。");
        }
        catch
        {
            if (IsCurrentSelection())
                SetResumePickerStatus("再開元の選択を完了できません。パスを直接入力して「再開元を確認」を押してください。");
        }
        finally
        {
            isPicking = false;
            RefreshResumePickerAvailability();
        }
    }

    private async void HandlePickResumeCheckpointClick(object? sender, RoutedEventArgs e) =>
        await PickResumeCheckpointAsync();

    private void RefreshResumePickerAvailability()
    {
        if (this.FindControl<Button>("PickResumeCheckpointButton") is { } button)
            button.IsEnabled = !isPicking && owner?.CanEditResume == true;
    }

    private void SetResumePickerStatus(string text)
    {
        if (this.FindControl<TextBlock>("ResumePickerStatusMessage") is not { } status) return;
        status.Text = text;
        status.IsVisible = text.Length > 0;
    }

    private void HandleTechnicalErrorSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        RefreshSelectedErrorDetail();

    private void RefreshSelectedErrorDetail()
    {
        ExecutionTechnicalError? error = this.FindControl<ListBox>("TechnicalErrorsList")?.SelectedItem as ExecutionTechnicalError;
        if (error is not null)
        {
            selectedErrorKey = error.Key;
        }

        if (this.FindControl<TextBox>("SelectedTechnicalErrorDetail") is { } detail)
        {
            detail.Text = error is null ? string.Empty
                : $"{error.Code}{Environment.NewLine}{error.TargetText}{Environment.NewLine}{error.Message}";
        }
    }
}
