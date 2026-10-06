using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.App.ViewModels;

public sealed partial class ExecutionViewModel
{
    private readonly IResumeInspectionBoundary resumeInspectionBoundary;
    private readonly ObservableCollection<ResumeAdmissionFinding> resumeFindingItems = [];
    private readonly ViewModelCommand resumeInterruptedRunCommand;
    private readonly ViewModelCommand validateResumeCheckpointCommand;
    private readonly ViewModelCommand applyCheckpointInputCommand;
    private readonly ViewModelCommand applyCheckpointModelCommand;
    private CheckpointEnvelope? inspectedCheckpoint;
    private ResumeAdmissionReport? resumeReport;
    private ResumeValidationIdentity? validatedResumeIdentity;
    private CancellationTokenSource? resumeInspectionCancellation;
    private CancellationTokenSource? checkpointInputCancellation;
    private long resumeInspectionSequence;
    private long resumeSelectionSequence;
    private bool isPreparingResume;
    private bool isApplyingCheckpointInput;
    private bool closing;
    private bool runStarting;
    private string? interruptedPartialPath;
    private string interruptedRunSummaryText = string.Empty;
    private string resumeValidationText = "再開元を指定して「再開元を確認」を押してください。";

    internal event Func<string, CancellationToken, Task<bool>>? CheckpointInputRequested;

    public ReadOnlyObservableCollection<ResumeAdmissionFinding> ResumeFindings { get; }
    public ResumeAdmissionReport? ResumeReport => resumeReport;
    public string ResumeValidationText => resumeValidationText;
    public bool IsPreparingResume => isPreparingResume || isApplyingCheckpointInput;
    public bool CanEditResume => !disposed && !closing && !IsRunning && !runStarting && !IsPreparingResume;
    public bool CanPrepareResume => CanEditResume && !string.IsNullOrWhiteSpace(ResumePartialPath);
    public bool HasInterruptedRun => !string.IsNullOrWhiteSpace(interruptedPartialPath);
    public string InterruptedRunSummaryText => interruptedRunSummaryText;
    public Task? LastRunTask { get; private set; }
    public ICommand ResumeInterruptedRunCommand => resumeInterruptedRunCommand;
    public ICommand ValidateResumeCheckpointCommand => validateResumeCheckpointCommand;
    public ICommand ApplyCheckpointInputCommand => applyCheckpointInputCommand;
    public ICommand ApplyCheckpointModelCommand => applyCheckpointModelCommand;

    private bool HasMatchingResumePreflight => resumeReport?.CanResume == true
        && validatedResumeIdentity == CaptureResumeIdentity();

    // Immutable references are intentional: a fresh input observation must require a new check.
    private sealed record ResumeValidationIdentity(
        QuantificationDefinition? Definition, WorkbookMetadata? Metadata, string InputPath,
        string? ModelId, CopilotRuntimeIdentity? Runtime, ExecutionAuthenticationState Authentication,
        string PartialPath, bool ResumeMode, int Concurrency, string? OutputDirectory,
        string? ReasoningEffort, string? ContextTier);

    private ResumeValidationIdentity CaptureResumeIdentity() => new(
        definition, workbookMetadata, inputPath, selectedModelId, runtimeIdentity, AuthenticationState,
        resumePartialPath, isResumeMode, maxConcurrency, outputDirectoryOverride, SelectedModelReasoningEffort,
        SelectedContextTier);

    public async Task PrepareResumeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanPrepareResume) return;
        InvalidateResumePreflight(clearCheckpoint: true);
        long sequence = resumeInspectionSequence;
        ResumeValidationIdentity identity = CaptureResumeIdentity();
        using CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        resumeInspectionCancellation = operation;
        CancellationToken token = operation.Token;
        isPreparingResume = true;
        SetResumeValidationText("再開元を読み取り専用で確認しています…");
        RaiseResumeStates();
        try
        {
            CheckpointLoadResult loaded = await resumeInspectionBoundary.LoadAsync(identity.PartialPath, token);
            token.ThrowIfCancellationRequested();
            if (!IsCurrentResumeInspection(sequence, identity)) return;
            if (!loaded.IsSuccess || loaded.Envelope is null)
            {
                SetResumeLoadFailure(loaded.Code);
                return;
            }

            inspectedCheckpoint = loaded.Envelope;
            InputSnapshot? input = null;
            if (!string.IsNullOrWhiteSpace(identity.InputPath))
            {
                try
                {
                    input = await resumeInspectionBoundary.CaptureInputAsync(identity.InputPath, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { /* Missing/unreadable input is an item failure, not a failed checkpoint load. */ }
            }

            token.ThrowIfCancellationRequested();
            if (!IsCurrentResumeInspection(sequence, identity)) return;
            QuantificationSnapshot? snapshot = null;
            EvaluationPlan? plan = null;
            if (identity.Definition is not null && definitionValidator.Validate(identity.Definition).IsValid)
            {
                snapshot = QuantificationSnapshot.Create(identity.Definition);
                if (identity.Metadata is not null)
                {
                    var mapping = mappingValidator.Validate(identity.Metadata, snapshot.Definition);
                    if (mapping.IsValid && mapping.Mapping is not null
                        && workbookPreflight.Validate(snapshot, identity.Metadata).IsValid)
                    {
                        plan = new EvaluationPlanBuilder().Build(snapshot, mapping.Mapping);
                    }
                }
            }

            CheckpointRuntimeIdentity? runtime = identity.Authentication == ExecutionAuthenticationState.Available
                && identity.Runtime is { } currentRuntime
                ? new CheckpointRuntimeIdentity
                {
                    ApplicationIdentity = QuantificationRunBoundary.ApplicationIdentity(),
                    CliVersion = currentRuntime.CliVersion,
                    CliSha256 = currentRuntime.CliSha256,
                    SdkInformationalVersion = currentRuntime.SdkInformationalVersion,
                }
                : null;
            ResumeAdmissionReport report = ResumeAdmissionEvaluator.Evaluate(loaded.Envelope,
                identity.PartialPath, snapshot, plan, input, identity.InputPath, identity.ModelId,
                identity.ReasoningEffort, runtime, identity.ContextTier);
            if (!IsCurrentResumeInspection(sequence, identity)) return;
            resumeReport = report;
            validatedResumeIdentity = identity;
            resumeFindingItems.Clear();
            foreach (ResumeAdmissionFinding finding in report.Findings)
            {
                if (!IsCurrentResumeInspection(sequence, identity)) return;
                resumeFindingItems.Add(finding);
            }
            if (!IsCurrentResumeInspection(sequence, identity)) return;
            SetResumeValidationText(report.CanResume
                ? "再開条件を確認しました。実行開始時に入力を再確認し、完了行の内容も検証します。"
                : "再開条件が一致しません。各項目を確認してから、再度「再開元を確認」を押してください。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (IsCurrentResumeInspection(sequence, identity))
                SetResumeValidationText("再開元の確認を中断しました。再度確認してください。");
        }
        catch
        {
            if (IsCurrentResumeInspection(sequence, identity)) SetResumeLoadFailure(CheckpointStatusCodes.Invalid);
        }
        finally
        {
            if (ReferenceEquals(resumeInspectionCancellation, operation))
            {
                resumeInspectionCancellation = null;
                isPreparingResume = false;
                if (!disposed)
                {
                    RaiseResumeStates();
                    Revalidate();
                }
            }
        }
    }

    private bool IsCurrentResumeInspection(long sequence, ResumeValidationIdentity identity) =>
        !disposed && !closing && sequence == resumeInspectionSequence && identity == CaptureResumeIdentity();

    private void SetResumeLoadFailure(string code)
    {
        // Never reflect arbitrary boundary codes or exception messages into presentation.
        string safeCode = code is CheckpointStatusCodes.SchemaUnsupported or CheckpointStatusCodes.HashMismatch
            or CheckpointStatusCodes.Cancelled ? code : CheckpointStatusCodes.Invalid;
        string message = safeCode switch
        {
            CheckpointStatusCodes.SchemaUnsupported => "この checkpoint 形式には対応していません。中断時の版を使用してください。",
            CheckpointStatusCodes.HashMismatch => "checkpoint の整合性を確認できません。変更せずに中断時のファイルを確認してください。",
            CheckpointStatusCodes.Cancelled => "再開元の確認を中断しました。再度確認してください。",
            _ => "checkpoint を読み取れません。作成時の場所にある .partial.xlsx を指定してください。",
        };
        inspectedCheckpoint = null;
        validatedResumeIdentity = null;
        ResumeAdmissionFinding finding = new(ResumeAdmissionItem.CheckpointShape, false, safeCode, message);
        resumeReport = new ResumeAdmissionReport([finding], safeCode);
        resumeFindingItems.Clear();
        resumeFindingItems.Add(finding);
        SetResumeValidationText(message);
    }

    private void InvalidateResumePreflight(bool clearCheckpoint = false, bool notifyValidationSummary = false)
    {
        resumeInspectionSequence++;
        CancellationTokenSource? previous = resumeInspectionCancellation;
        resumeInspectionCancellation = null;
        isPreparingResume = false;
        resumeReport = null;
        validatedResumeIdentity = null;
        if (clearCheckpoint) inspectedCheckpoint = null;
        TryCancel(previous);
        resumeFindingItems.Clear();
        SetResumeValidationText("再開元と現在の条件を確認してください。条件の変更後は再確認が必要です。");
        RaiseResumeStates(notifyValidationSummary);
    }

    private void SetResumeValidationText(string value)
    {
        resumeValidationText = value;
        OnPropertiesChanged(nameof(ResumeValidationText), nameof(ResumeReport));
        if (IsResumeMode && !HasMatchingResumePreflight)
        {
            OnPropertyChanged(nameof(ValidationSummary));
        }
    }

    private void RaiseResumeStates(bool notifyValidationSummary = false)
    {
        OnPropertiesChanged(nameof(IsPreparingResume), nameof(CanEditResume), nameof(CanPrepareResume),
            nameof(CanStart), nameof(HasInterruptedRun), nameof(InterruptedRunSummaryText));
        if (notifyValidationSummary)
        {
            OnPropertyChanged(nameof(ValidationSummary));
        }

        resumeInterruptedRunCommand.RaiseCanExecuteChanged();
        validateResumeCheckpointCommand.RaiseCanExecuteChanged();
        applyCheckpointInputCommand.RaiseCanExecuteChanged();
        applyCheckpointModelCommand.RaiseCanExecuteChanged();
        startCommand.RaiseCanExecuteChanged();
    }

    private async Task SelectInterruptedRunAsync()
    {
        if (!CanEditResume || interruptedPartialPath is not { } path) return;
        IsResumeMode = true;
        ResumePartialPath = path;
        await PrepareResumeAsync(); // Selection + inspection only. Never dispatch a run.
    }

    private void ClearInterruptedRun()
    {
        interruptedPartialPath = null;
        interruptedRunSummaryText = string.Empty;
        RaiseResumeStates();
    }

    private void RememberInterruptedRun(RunSummary summary)
    {
        ClearInterruptedRun();
        if (!summary.IsDurable || summary.StatusCode != QuantificationRunStatusCodes.Cancelled
            || string.IsNullOrWhiteSpace(summary.PartialPath)) return;
        // Only a returned durable summary proves persistence; progress contains reservations.
        interruptedPartialPath = summary.PartialPath;
        interruptedRunSummaryText = string.Format(CultureInfo.InvariantCulture,
            "中断しました。参照 {0}/{1}・行 {2}/{3} を保存済みです。途中の行は再開時に最初から評価します。",
            summary.References.Length, summary.Snapshot.Definition.Questions.Count(question => question.Enabled),
            summary.CompletedRows.Length, summary.Mapping.SelectedRowCount);
        RaiseResumeStates();
    }

    public async Task ApplyCheckpointInputAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEditResume || inspectedCheckpoint is not { } checkpoint) return;
        var handler = CheckpointInputRequested;
        if (handler is null || handler.GetInvocationList().Length != 1)
        {
            SetResumeValidationText("入力の読み込み先を確認できません。入力画面から読み込んでください。");
            return;
        }

        string selectedPath = ResumePartialPath;
        bool selectedMode = IsResumeMode;
        long selection = resumeSelectionSequence;
        InvalidateResumePreflight();
        using CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        checkpointInputCancellation = operation;
        isApplyingCheckpointInput = true;
        RaiseResumeStates();
        bool applied = false;
        try
        {
            // Parent owns atomic input loading against the expected/current draft. No definition auto-apply.
            applied = await handler(checkpoint.InputPath, operation.Token);
            if (disposed || closing || operation.IsCancellationRequested || selection != resumeSelectionSequence) return;
            if (applied)
            {
                resumePartialPath = selectedPath;
                isResumeMode = selectedMode;
                resumeResetReason = string.Empty;
                inspectedCheckpoint = checkpoint;
                OnPropertiesChanged(nameof(ResumePartialPath), nameof(IsResumeMode), nameof(ResumeResetReason), nameof(OutputModeText));
                SetResumeValidationText("入力を読み込みました。採点設計は自動変更していません。再開条件を再確認してください。");
            }
            else SetResumeValidationText("入力を読み込めませんでした。現在の入力を確認してください。");
        }
        catch
        {
            if (!disposed && !closing && selection == resumeSelectionSequence)
                SetResumeValidationText("入力の読み込みを完了できませんでした。現在の入力を確認してください。");
        }
        finally
        {
            checkpointInputCancellation = null;
            isApplyingCheckpointInput = false;
            if (!disposed) { RaiseResumeStates(); Revalidate(); }
        }
        if (applied && !disposed && !closing && !operation.IsCancellationRequested && selection == resumeSelectionSequence)
            await PrepareResumeAsync(cancellationToken);
    }

    private void ApplyCheckpointModel()
    {
        if (!CanEditResume || inspectedCheckpoint is not { } checkpoint) return;
        if (!IsAuthenticationAvailable || !modelsById.ContainsKey(checkpoint.NormalModelId))
        {
            SetResumeValidationText("中断時のモデルを現在の一覧で利用できません。Copilot 状態を再確認してください。別モデルへの自動切替は行いません。");
            return;
        }
        SelectedModelId = checkpoint.NormalModelId;
        string tier = checkpoint.ContextTier ?? ModelOptionPolicy.DefaultContextTier;
        if (checkpoint.ReasoningEffort is { } effort
                && !ReasoningEffortOptions.Any(option => option.Value == effort)
            || !ContextSizeOptions.Any(option => option.Tier == tier))
        {
            SetResumeValidationText("中断時の思考レベルまたは Context Size は現在利用できません。共通設定と Copilot 状態を確認してください。");
            return;
        }
        SaveModelOptions(checkpoint.ReasoningEffort, tier);
        SetResumeValidationText("中断時のモデル・思考レベル・Context Size を選択しました。再開条件を再確認してください。");
    }

    public async Task StopAndDrainAsync(TimeSpan timeout)
    {
        closing = true; // No new start, including reentrant IsRunning notifications.
        InvalidateResumePreflight(clearCheckpoint: true);
        TryCancel(checkpointInputCancellation);
        TryCancel(runCancellation);
        if (IsRunning) IsCancelling = true;
        RaiseCommandStates();
        Task? run = LastRunTask;
        if (run is null) return;
        // A finite bound is mandatory even if a caller supplies InfiniteTimeSpan.
        TimeSpan bound = timeout < TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30)
            ? TimeSpan.FromSeconds(10) : timeout;
        try { await run.WaitAsync(bound); }
        catch (TimeoutException) { /* Last atomic checkpoint remains the only resume source. */ }
        catch { /* Start owns observation; close must never depend on success. */ }
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        try { cancellation?.Cancel(); }
        catch { /* Cancellation callbacks cannot escape into commands or prevent draining. */ }
    }
}