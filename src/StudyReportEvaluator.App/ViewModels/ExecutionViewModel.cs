using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Usage;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.App.ViewModels;

public enum ExecutionAuthenticationState
{
    NotChecked,
    Checking,
    Available,
    AuthRequired,
    CliUnavailable,
    RuntimeFailed,
    Cancelled,
}

public sealed class ExecutionAuthenticationSnapshot
{
    public ExecutionAuthenticationSnapshot(
        ExecutionAuthenticationState state,
        IEnumerable<string>? modelIds,
        CopilotRuntimeIdentity? runtimeIdentity = null)
        : this(
            state,
            modelIds?.Select(id => new CopilotModelAvailability(id, null, null)),
            runtimeIdentity)
    {
    }

    public ExecutionAuthenticationSnapshot(
        ExecutionAuthenticationState state,
        IEnumerable<CopilotModelAvailability>? models = null,
        CopilotRuntimeIdentity? runtimeIdentity = null)
    {
        if (state is ExecutionAuthenticationState.NotChecked or ExecutionAuthenticationState.Checking)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        List<CopilotModelAvailability> normalized = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (CopilotModelAvailability? model in models ?? [])
        {
            if (model is null || !seen.Add(model.Id))
            {
                continue;
            }

            normalized.Add(model);
        }

        if (state == ExecutionAuthenticationState.Available && runtimeIdentity is null)
        {
            throw new ArgumentException(
                "An available authentication snapshot requires a runtime identity.",
                nameof(runtimeIdentity));
        }

        State = state;
        Models = normalized.AsReadOnly();
        ModelIds = Array.AsReadOnly(normalized.Select(model => model.Id).ToArray());
        RuntimeIdentity = runtimeIdentity;
    }

    public ExecutionAuthenticationState State { get; }

    public IReadOnlyList<string> ModelIds { get; }

    public IReadOnlyList<CopilotModelAvailability> Models { get; }

    public CopilotRuntimeIdentity? RuntimeIdentity { get; }

    public override string ToString() =>
        $"{nameof(ExecutionAuthenticationSnapshot)} {{ State = {State}, ModelCount = {ModelIds.Count.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public interface IExecutionAuthenticationBoundary
{
    Task<ExecutionAuthenticationSnapshot> CheckAsync(CancellationToken cancellationToken);
}

public sealed class CopilotExecutionAuthenticationBoundary : IExecutionAuthenticationBoundary
{
    private readonly CopilotAuthenticationService service;

    public CopilotExecutionAuthenticationBoundary()
        : this(new CopilotAuthenticationService())
    {
    }

    public CopilotExecutionAuthenticationBoundary(CopilotAuthenticationService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<ExecutionAuthenticationSnapshot> CheckAsync(
        CancellationToken cancellationToken)
    {
        CopilotAuthenticationResult result = await service
            .CheckAsync(cancellationToken)
            .ConfigureAwait(false);
        return new ExecutionAuthenticationSnapshot(
            result.Status switch
            {
                CopilotAuthenticationStatus.Available => ExecutionAuthenticationState.Available,
                CopilotAuthenticationStatus.AuthRequired => ExecutionAuthenticationState.AuthRequired,
                CopilotAuthenticationStatus.CliUnavailable => ExecutionAuthenticationState.CliUnavailable,
                CopilotAuthenticationStatus.RuntimeFailed => ExecutionAuthenticationState.RuntimeFailed,
                CopilotAuthenticationStatus.Cancelled => ExecutionAuthenticationState.Cancelled,
                _ => ExecutionAuthenticationState.RuntimeFailed,
            },
            result.AvailableModels,
            result.Identity);
    }

    public override string ToString() =>
        $"{nameof(CopilotExecutionAuthenticationBoundary)} {{ Content = <redacted> }}";
}

public interface IQuantificationRunBoundary
{
    Task<RunSummary> RunAsync(
        QuantificationRunRequest request,
        Action<EvaluationProgress>? progress,
        CancellationToken cancellationToken);

    // Existing fake/legacy boundaries can run without claiming cost observations.
    Task<RunSummary> RunAsync(
        QuantificationRunRequest request,
        Action<EvaluationProgress>? progress,
        Action<JobCostSnapshot>? costChanged,
        CancellationToken cancellationToken) => RunAsync(request, progress, cancellationToken);

    // Boundaries without a live preview simply never report one.
    Task<RunSummary> RunAsync(
        QuantificationRunRequest request,
        Action<EvaluationProgress>? progress,
        Action<JobCostSnapshot>? costChanged,
        Action<LivePreviewUpdate>? livePreview,
        CancellationToken cancellationToken) => RunAsync(request, progress, costChanged, cancellationToken);
}

public sealed class QuantificationRunBoundary : IQuantificationRunBoundary
{
    public Task<RunSummary> RunAsync(
        QuantificationRunRequest request,
        Action<EvaluationProgress>? progress,
        CancellationToken cancellationToken) => RunAsync(request, progress, null, null, cancellationToken);

    public Task<RunSummary> RunAsync(
        QuantificationRunRequest request,
        Action<EvaluationProgress>? progress,
        Action<JobCostSnapshot>? costChanged,
        CancellationToken cancellationToken) => RunAsync(request, progress, costChanged, null, cancellationToken);

    public Task<RunSummary> RunAsync(
        QuantificationRunRequest request,
        Action<EvaluationProgress>? progress,
        Action<JobCostSnapshot>? costChanged,
        Action<LivePreviewUpdate>? livePreview,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Job setup, log IO and final drain must not run on the UI thread.
        return Task.Run(async () =>
        {
            var assembly = typeof(QuantificationRunBoundary).Assembly;
            var usageContext = new JobUsageContext(
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                request.RuntimeIdentity?.SdkInformationalVersion, request.RuntimeIdentity?.CliVersion,
                request.ModelId, request.MaxConcurrency, request.ResumePartialPath is not null,
                requestedReasoningEffort: request.ReasoningEffort);
            await using JobUsageTracker usage = new(costChanged, context: usageContext);
            string status = "RUN_FAILED";
            try
            {
                RunSummary summary = await RunCoreAsync(request, progress, livePreview, usage, cancellationToken)
                    .ConfigureAwait(false);
                status = summary.StatusCode;
                return summary;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                status = "CANCELLED";
                throw;
            }
            finally
            {
                await usage.CompleteAsync(status).ConfigureAwait(false);
                // A final delivery after the bounded log drain also covers summary-less errors.
                try { costChanged?.Invoke(usage.Snapshot); }
                catch { /* Presentation cannot replace an evaluation result. */ }
            }
        }, CancellationToken.None);
    }

    private static async Task<RunSummary> RunCoreAsync(
        QuantificationRunRequest request,
        Action<EvaluationProgress>? progress,
        Action<LivePreviewUpdate>? livePreview,
        JobUsageTracker usage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        OpenXmlEvaluationRowSource rowSource = new(request.InputPath);
        AdaptiveEvaluationConcurrencyLimiter concurrencyLimiter = new(request.MaxConcurrency);
        EphemeralEvaluationRunnerOptions runnerOptions = new(
            maxConcurrency: request.MaxConcurrency,
            concurrencyObserver: concurrencyLimiter,
            contextTier: request.ContextTier);
        await using SharedCopilotClientPool sharedClient = new(new CopilotClientFactory());
        EphemeralEvaluationRunner runner = new(
            new SdkEphemeralCopilotTransportFactory(sharedClient, usage, UsageOperation.Normal),
            runnerOptions);
        EphemeralEvaluationRunnerAdapter normalRunner = new(runner, request.ReasoningEffort);
        if (!request.UseDurableWorkflow)
        {
            QuantificationOrchestrator orchestrator = new(rowSource, normalRunner);
            return await orchestrator.RunAsync(request, progress, cancellationToken).ConfigureAwait(false);
        }

        CopilotRuntimeIdentity identity = request.RuntimeIdentity
            ?? throw new QuantificationRunException("RUNTIME_IDENTITY_REQUIRED");
        DurableQuantificationOrchestrator durable = new(
            rowSource,
            normalRunner,
            new ReferenceAnswerOperationRunnerAdapter(new ReferenceAnswerEvaluationRunner(
                new SdkEphemeralCopilotTransportFactory(sharedClient, usage, UsageOperation.Reference),
                runnerOptions)),
            new SpecialEvaluationOperationRunnerAdapter(new SpecialEvaluationRunner(
                new SdkEphemeralCopilotTransportFactory(sharedClient, usage, UsageOperation.Special),
                runnerOptions)));
        return await durable.RunAsync(
            new DurableQuantificationRunRequest
            {
                Run = request,
                Runtime = new CheckpointRuntimeIdentity
                {
                    ApplicationIdentity = ApplicationIdentity(),
                    CliVersion = identity.CliVersion,
                    CliSha256 = identity.CliSha256,
                    SdkInformationalVersion = identity.SdkInformationalVersion,
                },
                OutputDirectory = request.OutputDirectory,
                ResumePartialPath = request.ResumePartialPath,
            },
            value => progress?.Invoke(ToLegacyProgress(value)),
            cancellationToken,
            concurrencyLimiter,
            livePreview).ConfigureAwait(false);
    }

    public override string ToString() =>
        $"{nameof(QuantificationRunBoundary)} {{ Content = <redacted> }}";

    private static EvaluationProgress ToLegacyProgress(DurableEvaluationProgress value) =>
        new(
            value.OperationTotal,
            value.OperationCompleted,
            value.InFlight,
            value.Stage switch
            {
                DurableEvaluationStage.Cancelling => EvaluationProgressStatus.Cancelling,
                DurableEvaluationStage.Completed when string.Equals(
                    value.StatusCode,
                    QuantificationRunStatusCodes.Success,
                    StringComparison.Ordinal) => EvaluationProgressStatus.Completed,
                DurableEvaluationStage.Completed => EvaluationProgressStatus.Cancelled,
                _ => EvaluationProgressStatus.Running,
            },
            value.Stage,
            value.ReferenceCompleted,
            value.ReferenceTotal,
            value.RowCompleted,
            value.RowTotal,
            value.StatusCode,
            value.FinalPath,
            value.PartialPath);

    internal static string ApplicationIdentity()
    {
        Assembly assembly = typeof(QuantificationRunBoundary).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        string version = informationalVersion?.Split('+', 2)[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "unknown";
        return (assembly.GetName().Name ?? "StudyReportEvaluator.App") + "/" + version;
    }
}

public sealed class ExecutionTechnicalError
{
    public ExecutionTechnicalError(
        string code,
        string field,
        string message,
        string? nodeId = null,
        string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Field = field;
        Message = message;
        NodeId = string.IsNullOrWhiteSpace(nodeId) ? null : nodeId;
        Path = string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public string Code { get; }

    public string Field { get; }

    public string Message { get; }

    public string? NodeId { get; }

    /// <summary>A validator-owned definition location, not a workbook or output path.</summary>
    public string? Path { get; }

    public string TargetText => string.Join(" · ", new[] { NodeId, Path, Field }
        .Where(value => value is not null));

    internal (string Code, string? NodeId, string? Path, string Field) Key => (Code, NodeId, Path, Field);

    public string AccessibleText => $"{TargetText}。{Message}";

    public override string ToString() =>
        $"{nameof(ExecutionTechnicalError)} {{ Code = {Code}, Field = {Field}, Content = <redacted> }}";
}

public sealed class ExecutionRunContext
{
    public ExecutionRunContext(
        RunSummary summary,
        string inputPath,
        string modelId,
        CopilotRuntimeIdentity runtimeIdentity,
        JobCostSnapshot? cost = null,
        string? reasoningEffort = null,
        string? contextTier = null)
    {
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (modelId.Length > 256
            || !string.Equals(modelId, modelId.Trim(), StringComparison.Ordinal)
            || modelId.Any(char.IsControl))
        {
            throw new ArgumentException("The model identity is invalid.", nameof(modelId));
        }

        if (reasoningEffort is not null && !ReasoningEffortPolicy.IsSafeReasoningEffort(reasoningEffort))
        {
            throw new ArgumentException("The reasoning effort is invalid.", nameof(reasoningEffort));
        }

        InputPath = Path.GetFullPath(inputPath);
        ModelId = modelId;
        ReasoningEffort = reasoningEffort;
        _ = ModelOptionPolicy.ToSdkContextTier(contextTier);
        ContextTier = contextTier;
        Cost = cost;
    }

    public RunSummary Summary { get; }

    public string InputPath { get; }

    public string ModelId { get; }

    public string? ReasoningEffort { get; }

    public string? ContextTier { get; }

    public CopilotRuntimeIdentity RuntimeIdentity { get; }

    public JobCostSnapshot? Cost { get; }

    public override string ToString() =>
        $"{nameof(ExecutionRunContext)} {{ StatusCode = {Summary.StatusCode}, Content = <redacted> }}";
}

public sealed class ExecutionRunCompletedEventArgs : EventArgs
{
    public ExecutionRunCompletedEventArgs(ExecutionRunContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public ExecutionRunContext Context { get; }
}

public sealed partial class ExecutionViewModel : UiObservableObject, IDisposable
{
    private const string BundledCliUnavailableMessage =
        "同梱 Copilot CLI を確認できません。配布物を再取得し、ZIP 版は再展開してください。";
    private const string LoginExitUnconfirmedMessage =
        "ログイン処理の終了を確認できませんでした。終了を待って再試行するか、アプリを終了して開き直してください。";

    private static readonly IReadOnlyList<int> ClosedConcurrencyOptions =
        Array.AsReadOnly(Enumerable.Range(1, EvaluationSchedulerOptions.MaximumMaxConcurrency).ToArray());

    private readonly IExecutionAuthenticationBoundary authenticationBoundary;
    private readonly IQuantificationRunBoundary runBoundary;
    private readonly BundledCopilotLoginService loginService;
    private readonly QuantificationDefinitionValidator definitionValidator = new();
    private readonly CanonicalDefinitionSerializer definitionSerializer = new();
    private readonly ColumnMappingValidator mappingValidator = new();
    private readonly WorkbookExecutionPreflight workbookPreflight = new();
    private readonly ObservableCollection<string> modelItems = [];
    private readonly Dictionary<string, CopilotModelAvailability> modelsById = new(StringComparer.Ordinal);
    private readonly ObservableCollection<ExecutionTechnicalError> technicalErrorItems = [];
    private readonly ViewModelCommand checkAuthenticationCommand;
    private readonly ViewModelCommand loginCommand;
    private readonly ViewModelCommand cancelLoginCommand;
    private readonly ViewModelCommand startCommand;
    private readonly ViewModelCommand cancelCommand;
    private QuantificationDefinition? definition;
    private WorkbookMetadata? workbookMetadata;
    private string inputPath = string.Empty;
    private (string InputPath, long EvaluationCount)? nextDraftSummary;
    private string? selectedModelId;
    private string? preferredModelId;
    private string? initialModelId;
    private bool initialModelSelectionApplied;
    private bool updatingModelSelection;
    private CopilotRuntimeIdentity? runtimeIdentity;
    private ExecutionAuthenticationState authenticationState = ExecutionAuthenticationState.NotChecked;
    private string? runtimeErrorCode;
    private ImmutableArray<ExecutionTechnicalError> runPreflightErrors = [];
    private int maxConcurrency = EvaluationSchedulerOptions.DefaultMaxConcurrency;
    private bool isCheckingAuthentication;
    private bool isLoggingIn;
    private bool loginExitUnconfirmed;
    private bool loginServiceDisposed;
    private bool startupAuthenticationStarted;
    private string loginStatusText = "GitHub へのログインは開始していません。";
    private bool isRunning;
    private bool isCancelling;
    private int progressTotal;
    private int progressCompleted;
    private int progressInFlight;
    private EvaluationProgressStatus? progressStatus;
    private DurableEvaluationStage? progressStage;
    private int referenceCompleted;
    private int referenceTotal;
    private int rowCompleted;
    private int rowTotal;
    private string reservedFinalPath = string.Empty;
    private string partialPath = string.Empty;
    private string? outputDirectoryOverride;
    private string resumePartialPath = string.Empty;
    private bool isResumeMode;
    private string resumeResetReason = string.Empty;
    private ExecutionRunContext? lastRunContext;
    private QuantificationRunRequest? currentRunRequest;
    private CancellationTokenSource? authenticationCancellation;
    private CancellationTokenSource? loginCancellation;
    private CancellationTokenSource? runCancellation;
    private long authenticationSequence;
    private long costSequence;
    private long progressSequence;
    private bool disposed;

    public JobCostViewModel Cost { get; } = new();

    public LiveQuantificationPreviewViewModel LivePreview { get; } = new();
    public ExecutionViewModel()
        : this(
            new CopilotExecutionAuthenticationBoundary(),
            new QuantificationRunBoundary())
    {
    }

    public ExecutionViewModel(
        IExecutionAuthenticationBoundary authenticationBoundary,
        IQuantificationRunBoundary runBoundary,
        BundledCopilotLoginService? loginService = null)
        : this(authenticationBoundary, runBoundary, loginService, resumeInspectionBoundary: null)
    {
    }

    public ExecutionViewModel(
        IExecutionAuthenticationBoundary authenticationBoundary,
        IQuantificationRunBoundary runBoundary,
        BundledCopilotLoginService? loginService,
        IResumeInspectionBoundary? resumeInspectionBoundary)
    {
        this.authenticationBoundary = authenticationBoundary
            ?? throw new ArgumentNullException(nameof(authenticationBoundary));
        this.runBoundary = runBoundary
            ?? throw new ArgumentNullException(nameof(runBoundary));
        // The view model owns the service, including an explicitly supplied instance.
        this.loginService = loginService ?? new BundledCopilotLoginService();
        this.resumeInspectionBoundary = resumeInspectionBoundary ?? new ResumeInspectionBoundary();
        AvailableModelIds = new ReadOnlyObservableCollection<string>(modelItems);
        TechnicalErrors = new ReadOnlyObservableCollection<ExecutionTechnicalError>(technicalErrorItems);
        ResumeFindings = new ReadOnlyObservableCollection<ResumeAdmissionFinding>(resumeFindingItems);
        checkAuthenticationCommand = new ViewModelCommand(
            _ => _ = CheckAuthenticationAsync(),
            _ => CanCheckAuthentication);
        loginCommand = new ViewModelCommand(
            _ => _ = LoginAsync(),
            _ => CanLogin);
        cancelLoginCommand = new ViewModelCommand(
            _ => CancelLogin(),
            _ => CanCancelLogin);
        startCommand = new ViewModelCommand(
            _ => _ = StartAsync(),
            _ => CanStart);
        cancelCommand = new ViewModelCommand(
            _ => Cancel(),
            _ => CanCancel);
        resumeInterruptedRunCommand = new ViewModelCommand(
            _ => _ = SelectInterruptedRunAsync(), _ => CanEditResume && HasInterruptedRun);
        validateResumeCheckpointCommand = new ViewModelCommand(
            _ => _ = PrepareResumeAsync(), _ => CanPrepareResume);
        applyCheckpointInputCommand = new ViewModelCommand(
            _ => _ = ApplyCheckpointInputAsync(), _ => CanEditResume && inspectedCheckpoint is not null);
        applyCheckpointModelCommand = new ViewModelCommand(
            _ => ApplyCheckpointModel(), _ => CanEditResume && inspectedCheckpoint is not null);
        Revalidate();
    }

    public event EventHandler<ExecutionRunCompletedEventArgs>? RunCompleted;

    internal event Func<CancellationToken, Task>? ModelCatalogRefreshed;

    public ImmutableArray<CachedCopilotModel>? CachedModels { get; private set; }

    private bool hasRefreshedModels;

    public ReadOnlyObservableCollection<string> AvailableModelIds { get; }

    public ReadOnlyObservableCollection<ExecutionTechnicalError> TechnicalErrors { get; }

    public IReadOnlyList<int> ConcurrencyOptions => ClosedConcurrencyOptions;

    public bool IsConfigured => definition is not null
        && workbookMetadata is not null
        && !string.IsNullOrWhiteSpace(inputPath);

    public ExecutionAuthenticationState AuthenticationState
    {
        get => authenticationState;
        private set
        {
            if (SetProperty(ref authenticationState, value))
            {
                OnPropertiesChanged(
                    nameof(AuthenticationStatusText),
                    nameof(IsAuthenticationAvailable),
                    nameof(ValidationSummary));
            }
        }
    }

    public bool IsAuthenticationAvailable =>
        AuthenticationState == ExecutionAuthenticationState.Available;


    /// null は SDK が選択中 model の上限を公開していないことを表す。
    public int? SelectedModelPromptTokenLimit =>
        SelectedModelId is string id && modelsById.TryGetValue(id, out CopilotModelAvailability? model)
            ? SelectedContextTier == ModelOptionPolicy.LongContextTier
                ? model.LongContextPromptTokens : model.EffectivePromptTokenLimit
            : null;

    public string SelectedModelLimitText => SelectedModelId is null
        ? "未選択"
        : SelectedModelPromptTokenLimit is int limit
            ? $"{limit.ToString("N0", CultureInfo.InvariantCulture)} tokens"
            : "SDK未公開・事前検証なし";

    public string? SelectedModelReasoningEffort => selectedReasoningEffortOption?.Value;

    public string SelectedModelReasoningEffortText => SelectedModelId is null
        ? "未選択"
        : reasoningPreferenceUnavailable ? "選択した思考レベルは現在利用できません"
        : SelectedModelReasoningEffort ?? "未指定（model非対応またはauto）";

    public string AuthenticationStatusText => AuthenticationState switch
    {
        ExecutionAuthenticationState.NotChecked => "Copilot CLI の login 状態は未確認です。",
        ExecutionAuthenticationState.Checking => "Copilot CLI と利用可能 model を確認しています…",
        ExecutionAuthenticationState.Available => "既存の Copilot CLI login を利用できます。",
        ExecutionAuthenticationState.AuthRequired => "Copilot CLI で login してから再確認してください。",
        ExecutionAuthenticationState.CliUnavailable => BundledCliUnavailableMessage,
        ExecutionAuthenticationState.RuntimeFailed => "Copilot runtime の確認に失敗しました。",
        ExecutionAuthenticationState.Cancelled => "Copilot 状態の確認を取り消しました。",
        _ => "Copilot runtime の状態を確認できません。",
    };

    public string RuntimeIdentityText => runtimeIdentity is null
        ? "runtime identity はまだありません。"
        : $"CLI {runtimeIdentity.CliVersion} · SHA-256 {runtimeIdentity.CliSha256[..12]}… · SDK {runtimeIdentity.SdkInformationalVersion}";

    /// <summary>The saved or explicitly edited preference, independent of authentication state.</summary>
    public string? PreferredModelId => preferredModelId;

    public string? SelectedModelId
    {
        get => selectedModelId;
        set
        {
            string? next = string.IsNullOrWhiteSpace(value) ? null : value;
            // Collection resets and two-way binding feedback are not explicit preference edits.
            if (updatingModelSelection
                || (next is null && (selectedModelId is null || IsCheckingAuthentication || IsLoggingIn)))
            {
                return;
            }

            SetModelPreference(next);
        }
    }

    public int MaxConcurrency
    {
        get => maxConcurrency;
        set
        {
            if (SetProperty(ref maxConcurrency, value))
            {
                InvalidateResumePreflight();
                runtimeErrorCode = null;
                runPreflightErrors = [];
                Revalidate();
                OnPropertyChanged(nameof(ConcurrencyText));
            }
        }
    }

    public string ConcurrencyText =>
        $"最大 {MaxConcurrency.ToString(CultureInfo.InvariantCulture)} 件を並列実行（許可範囲 1～16）";

    /// <summary>An explicit next-run directory, or null to derive it from the current input.</summary>
    public string? OutputDirectoryOverride
    {
        get => outputDirectoryOverride;
        set
        {
            string? next = string.IsNullOrWhiteSpace(value) ? null : value;
            if (SetProperty(ref outputDirectoryOverride, next))
            {
                InvalidateResumePreflight();
                runtimeErrorCode = null;
                OnPropertyChanged(nameof(OutputDirectory));
                Revalidate();
            }
        }
    }

    public string OutputDirectory
    {
        get => OutputDirectoryOverride ?? (string.IsNullOrWhiteSpace(NextDraftInputPath)
            ? string.Empty
            : Path.Combine(Path.GetDirectoryName(NextDraftInputPath) ?? string.Empty, "result"));
        set => OutputDirectoryOverride = value;
    }

    private string NextDraftInputPath => nextDraftSummary?.InputPath ?? inputPath;

    public string ResumePartialPath
    {
        get => resumePartialPath;
        set
        {
            if (SetProperty(ref resumePartialPath, value ?? string.Empty))
            {
                resumeSelectionSequence++;
                InvalidateResumePreflight(clearCheckpoint: true);
                runtimeErrorCode = null;
                resumeResetReason = string.Empty;
                OnPropertiesChanged(nameof(ResumeResetReason), nameof(OutputModeText));
                Revalidate();
            }
        }
    }

    public bool IsResumeMode
    {
        get => isResumeMode;
        set
        {
            if (SetProperty(ref isResumeMode, value))
            {
                resumeSelectionSequence++;
                InvalidateResumePreflight();
                runtimeErrorCode = null;
                resumeResetReason = string.Empty;
                OnPropertiesChanged(nameof(ResumeResetReason), nameof(OutputModeText), nameof(CanStart));
                Revalidate();
            }
        }
    }

    public string ResumeResetReason => resumeResetReason;

    public string OutputModeText => IsResumeMode
        ? "既存の .partial.xlsx をread-only検証して再開します。"
        : "新規runとしてfinal/partial名を同時予約します。"
            + (string.IsNullOrEmpty(ResumeResetReason) ? string.Empty : " " + ResumeResetReason);

    public long PlannedEvaluationCount => nextDraftSummary?.EvaluationCount
        ?? CountPlannedEvaluations(definition);

    public string PlanSummary => !string.IsNullOrWhiteSpace(NextDraftInputPath)
        ? $"{PlannedEvaluationCount.ToString("N0", CultureInfo.InvariantCulture)} evaluation units · retry込み最大 {WorstCaseAttemptCount.ToString("N0", CultureInfo.InvariantCulture)} attempts · snapshot は実行開始時に固定"
        : "入力と定量化設計を完了してください。";

    public long WorstCaseAttemptCount
    {
        get
        {
            try
            {
                return checked(PlannedEvaluationCount * RetryAndCleanupCoordinator.MaximumTransientAttempts);
            }
            catch (OverflowException)
            {
                return long.MaxValue;
            }
        }
    }

    public bool IsCheckingAuthentication
    {
        get => isCheckingAuthentication;
        private set
        {
            if (SetProperty(ref isCheckingAuthentication, value))
            {
                OnPropertiesChanged(
                    nameof(CanLogin),
                    nameof(CanCheckAuthentication),
                    nameof(CanStart),
                    nameof(ValidationSummary));
                RaiseCommandStates();
            }
        }
    }

    public bool IsLoggingIn
    {
        get => isLoggingIn;
        private set
        {
            if (SetProperty(ref isLoggingIn, value))
            {
                OnPropertiesChanged(
                    nameof(CanLogin),
                    nameof(CanCancelLogin),
                    nameof(CanCheckAuthentication),
                    nameof(CanStart),
                    nameof(ValidationSummary));
                RaiseCommandStates();
            }
        }
    }

    public string LoginStatusText
    {
        get => loginStatusText;
        private set => SetProperty(ref loginStatusText, value);
    }

    public Task? LastLoginTask { get; private set; }

    public Task? LastStartupAuthenticationTask { get; private set; }

    public bool IsRunning
    {
        get => isRunning;
        private set
        {
            if (SetProperty(ref isRunning, value))
            {
                OnPropertiesChanged(
                    nameof(CanLogin),
                    nameof(CanCheckAuthentication),
                    nameof(CanStart),
                    nameof(CanCancel),
                    nameof(RunStatusText),
                    nameof(ValidationSummary),
                    nameof(CurrentRunOutputSummary));
                RaiseCommandStates();
            }
        }
    }

    public bool IsCancelling
    {
        get => isCancelling;
        private set
        {
            if (SetProperty(ref isCancelling, value))
            {
                OnPropertiesChanged(nameof(CanCancel), nameof(RunStatusText), nameof(ValidationSummary));
                RaiseCommandStates();
            }
        }
    }

    public int ProgressTotal => progressTotal;

    public int ProgressCompleted => progressCompleted;

    public int ProgressInFlight => progressInFlight;

    public DurableEvaluationStage? ProgressStage => progressStage;

    public int ReferenceCompleted => referenceCompleted;

    public int ReferenceTotal => referenceTotal;

    public int RowCompleted => rowCompleted;

    public int RowTotal => rowTotal;

    public string ReservedFinalPath => reservedFinalPath;

    public string PartialPath => partialPath;

    public string StageText => progressStage switch
    {
        DurableEvaluationStage.Preparing => "準備中",
        DurableEvaluationStage.GeneratingReferences => "参照回答を生成中",
        DurableEvaluationStage.EvaluatingRows => "学生行を評価中",
        DurableEvaluationStage.SavingCheckpoint => "checkpointを保存中",
        DurableEvaluationStage.FinalizingWorkbook => "final workbookを検証中",
        DurableEvaluationStage.Completed => "完了",
        DurableEvaluationStage.Cancelling => "中断処理中",
        _ => "未開始",
    };

    public string DurableProgressText =>
        $"参照 {ReferenceCompleted.ToString(CultureInfo.InvariantCulture)} / {ReferenceTotal.ToString(CultureInfo.InvariantCulture)} · 行 {RowCompleted.ToString(CultureInfo.InvariantCulture)} / {RowTotal.ToString(CultureInfo.InvariantCulture)}";

    public string OutputIdentityText => string.IsNullOrWhiteSpace(PartialPath)
        ? "run開始時にfinal/partial pathを予約します。"
        : $"final: {(string.IsNullOrWhiteSpace(ReservedFinalPath) ? "—" : ReservedFinalPath)}{Environment.NewLine}partial: {PartialPath}";

    public double ProgressPercent => progressTotal == 0
        ? 0d
        : (double)progressCompleted / progressTotal * 100d;

    public string ProgressText => progressTotal == 0
        ? "まだ実行していません。"
        : $"{progressCompleted.ToString("N0", CultureInfo.InvariantCulture)} / {progressTotal.ToString("N0", CultureInfo.InvariantCulture)} 完了 · {progressInFlight.ToString(CultureInfo.InvariantCulture)} 実行中 · {ProgressPercent.ToString("0.0", CultureInfo.InvariantCulture)}%";

    public string RunStatusText
    {
        get
        {
            if (IsCancelling)
            {
                return "中断を受け付けました。処理中の行を中止し、保存済みの行を保持します。途中の行は再開時に最初から評価します。";
            }

            if (IsRunning)
            {
                return "同一 immutable snapshot で評価しています。";
            }

            if (lastRunContext is null)
            {
                return "実行準備中です。";
            }

            RunSummary summary = lastRunContext.Summary;
            return summary.StatusCode switch
            {
                QuantificationRunStatusCodes.Success when summary.PartialCleanupFailed =>
                    "final workbookは有効ですが、partial cleanupに失敗しました。",
                QuantificationRunStatusCodes.Success => "検証済みfinal workbookを自動作成しました。",
                QuantificationRunStatusCodes.Cancelled when HasInterruptedRun => "中断しました。.partial.xlsx に保存済みの結果を保持しています。「中断した処理を再開準備」で再開元を設定できます。",
                QuantificationRunStatusCodes.Cancelled => "中断しました。再開元として確認できる保存済み checkpoint はありません。",
                QuantificationRunStatusCodes.InputChanged => "入力変更を検出したため、出力は停止されています。",
                QuantificationRunStatusCodes.CheckpointFailed => "checkpointを安全に保存できなかったため停止しました。",
                QuantificationRunStatusCodes.OutputInvalid => "final検証に失敗したためpartialを保持しました。",
                _ => "実行状態を確認してください。",
            };
        }
    }

    public ExecutionRunContext? LastRunContext
    {
        get => lastRunContext;
        private set => SetProperty(ref lastRunContext, value);
    }

    /// <summary>UI-only metadata from the latest dispatched request, not the next-run draft.</summary>
    public bool HasCurrentRun => currentRunRequest is not null;

    public string? CurrentRunModelId => currentRunRequest?.ModelId;

    public int? CurrentRunMaxConcurrency => currentRunRequest?.MaxConcurrency;

    public string? CurrentRunReasoningEffortText => currentRunRequest is null
        ? null
        : currentRunRequest.ReasoningEffort ?? "未指定（model非対応またはauto）";

    public string? CurrentRunLimitText => currentRunRequest is null
        ? null
        : currentRunRequest.MaximumPromptTokens is int limit
            ? $"{limit.ToString("N0", CultureInfo.InvariantCulture)} tokens"
            : "SDK未公開・事前検証なし";

    public string CurrentRunOutputSummary
    {
        get
        {
            if (currentRunRequest is not { } request)
            {
                return string.Empty;
            }

            string label = IsRunning ? "今回run" : "前回run";
            return request.ResumePartialPath is { } resumePath
                ? $"{label} 再開: {resumePath}"
                : $"{label} 新規出力先: {request.OutputDirectory}";
        }
    }

    public bool HasTechnicalErrors => technicalErrorItems.Count > 0;

    public bool IsTechnicallyValid => !HasTechnicalErrors;

    public string ValidationSummary
    {
        get
        {
            // Error presence stays independent of this presentation and CanStart.
            if (CanStart)
            {
                return "技術検証を通過しました。run を開始できます。";
            }

            if (disposed || loginServiceDisposed)
            {
                return "アプリを開き直すまで run は開始できません。";
            }

            if (closing) return "終了処理中です。新しい実行は開始できません。";
            if (IsPreparingResume) return "再開条件を確認中です。実行はまだ開始できません。";

            if (IsRunning)
            {
                return IsCancelling
                    ? "取消処理中です。次回の run はまだ開始できません。"
                    : "実行中です。次回の run はまだ開始できません。";
            }

            if (IsLoggingIn)
            {
                return "GitHub にログイン中です。run はまだ開始できません。";
            }

            if (IsCheckingAuthentication || AuthenticationState == ExecutionAuthenticationState.Checking)
            {
                return "Copilot 状態を確認中です。run はまだ開始できません。";
            }

            if (loginExitUnconfirmed)
            {
                return "ログイン処理の終了が未確認です。run は開始できません。";
            }

            if (!IsConfigured)
            {
                return "入力と定量化設計を完了してください。run はまだ開始できません。";
            }

            if (nextDraftSummary is not null)
            {
                return "次回の入力と定量化設計を実行画面で確認してください。run はまだ開始できません。";
            }

            if (HasTechnicalErrors)
            {
                return $"実行前に解消する技術的な問題が {technicalErrorItems.Count.ToString(CultureInfo.InvariantCulture)} 件あります。";
            }

            if (IsResumeMode && !HasMatchingResumePreflight) return ResumeValidationText;

            return IsAuthenticationAvailable
                ? "実行条件を確認してください。run はまだ開始できません。"
                : AuthenticationStatusText;
        }
    }

    public bool CanCheckAuthentication => !disposed
        && !closing
        && !IsCheckingAuthentication
        && !IsLoggingIn
        && !loginExitUnconfirmed
        && !loginServiceDisposed
        && !IsRunning;

    public bool CanLogin => !disposed
        && !closing
        && !loginServiceDisposed
        && !IsLoggingIn
        && !IsCheckingAuthentication
        && !IsRunning;

    public bool CanCancelLogin => !disposed
        && IsLoggingIn
        && loginCancellation is { IsCancellationRequested: false };

    public bool CanStart => !disposed
        && !closing
        && !runStarting
        && !IsPreparingResume
        && (!IsResumeMode || HasMatchingResumePreflight)
        && IsConfigured
        && nextDraftSummary is null
        && !IsCheckingAuthentication
        && !IsLoggingIn
        && !loginExitUnconfirmed
        && !loginServiceDisposed
        && !IsRunning
        && AuthenticationState == ExecutionAuthenticationState.Available
        && runtimeIdentity is not null
        && SelectedModelId is not null
        && modelItems.Contains(SelectedModelId)
        && MaxConcurrency is >= EvaluationSchedulerOptions.MinimumMaxConcurrency
            and <= EvaluationSchedulerOptions.MaximumMaxConcurrency
        && !HasTechnicalErrors;

    public bool CanCancel => IsRunning && !IsCancelling;

    public ICommand CheckAuthenticationCommand => checkAuthenticationCommand;

    public ICommand LoginCommand => loginCommand;

    public ICommand CancelLoginCommand => cancelLoginCommand;

    public ICommand StartCommand => startCommand;

    public ICommand CancelCommand => cancelCommand;

    public void ApplySettings(ApplicationSettings settings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(settings);

        string? preference = string.IsNullOrWhiteSpace(settings.PreferredModelId)
            ? null
            : settings.PreferredModelId;
        if (!string.Equals(preferredModelId, preference, StringComparison.Ordinal))
        {
            SetModelPreference(preference);
        }

        MaxConcurrency = settings.MaxConcurrency;
        OutputDirectoryOverride = settings.OutputDirectoryOverride;
        modelPreferences = settings.ModelPreferences;
        OnPropertyChanged(nameof(ModelPreferences));
        RefreshModelOptions();
        InvalidateResumePreflight();
        Revalidate();
        if (!hasRefreshedModels && settings.CachedModels is { } cached)
        {
            CachedModels = cached;
            updatingModelSelection = true;
            try
            {
                SynchronizeModelIds(cached.Select(model => model.Id).ToArray());
            }
            finally
            {
                updatingModelSelection = false;
            }
        }
        // Definitions require the separate explicit Input/Design application boundary.
        // Restoring preferences never checks authentication, logs in, or starts a run.
    }

    /// <summary>
    /// Refresh only next-run presentation on any screen. No draft clone, preflight,
    /// progress reset or request replacement; Configure is still required before the next start.
    /// </summary>
    internal void UpdateNextDraftSummary(
        QuantificationDefinition? draftDefinition,
        string? configuredInputPath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        string nextInputPath = draftDefinition is null || string.IsNullOrWhiteSpace(configuredInputPath)
            ? string.Empty
            : Path.GetFullPath(configuredInputPath);
        // Summary counts alone cannot detect a changed criterion or prompt.
        if (draftDefinition is null || workbookMetadata is null
            || !IsSameConfiguration(draftDefinition, workbookMetadata, nextInputPath))
        {
            InvalidateResumePreflight(notifyValidationSummary: IsResumeMode);
            if (!isApplyingCheckpointInput) ClearInterruptedRun();
        }
        (string InputPath, long EvaluationCount) next = (
            nextInputPath,
            nextInputPath.Length == 0 ? 0 : CountPlannedEvaluations(draftDefinition));
        var previous = nextDraftSummary ?? (inputPath, CountPlannedEvaluations(definition));
        // Opening unchanged settings must not invalidate a ready configuration.
        // During a run, retain the existing pending-draft gate even when only non-summary fields changed.
        if (nextDraftSummary == next || (!IsRunning && previous == next))
        {
            return;
        }

        bool becamePending = nextDraftSummary is null;
        nextDraftSummary = next;
        if (previous != next)
        {
            OnPropertiesChanged(
                nameof(OutputDirectory),
                nameof(PlannedEvaluationCount),
                nameof(WorstCaseAttemptCount),
                nameof(PlanSummary));
        }

        if (becamePending && !IsRunning)
        {
            // Readiness presentation only: leave configured fields and validation errors intact.
            OnPropertiesChanged(nameof(CanStart), nameof(ValidationSummary));
            startCommand.RaiseCanExecuteChanged();
        }
    }

    public void Configure(
        QuantificationDefinition draftDefinition,
        WorkbookMetadata metadata,
        string configuredInputPath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(draftDefinition);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredInputPath);
        if (IsRunning)
        {
            throw new InvalidOperationException("An active run cannot be reconfigured.");
        }

        string nextInputPath = Path.GetFullPath(configuredInputPath);
        bool hadNextDraftSummary = nextDraftSummary is not null;
        nextDraftSummary = null;
        if (IsSameConfiguration(draftDefinition, metadata, nextInputPath))
        {
            if (hadNextDraftSummary)
            {
                OnPropertiesChanged(
                    nameof(OutputDirectory),
                    nameof(PlannedEvaluationCount),
                    nameof(WorstCaseAttemptCount),
                    nameof(PlanSummary));
            }

            Revalidate();
            return;
        }

        definition = InputViewModel.CloneDefinition(draftDefinition);
        workbookMetadata = metadata;
        inputPath = nextInputPath;
        resumeResetReason = isResumeMode || !string.IsNullOrWhiteSpace(resumePartialPath) || HasInterruptedRun
            ? "入力または採点定義が変更されたため、再開指定を解除しました。再開する場合はcheckpointを指定し直してください。"
            : string.Empty;
        resumePartialPath = string.Empty;
        isResumeMode = false;
        InvalidateResumePreflight(clearCheckpoint: true);
        ClearInterruptedRun();
        runtimeErrorCode = null;
        runPreflightErrors = [];
        LastRunContext = null;
        ResetProgress();
        OnPropertiesChanged(
            nameof(IsConfigured),
            nameof(OutputDirectory),
            nameof(ResumePartialPath),
            nameof(IsResumeMode),
            nameof(ResumeResetReason),
            nameof(OutputModeText),
            nameof(PlannedEvaluationCount),
            nameof(WorstCaseAttemptCount),
            nameof(PlanSummary),
            nameof(RunStatusText));
        Revalidate();
    }

    internal void ClearConfiguration()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsRunning)
        {
            throw new InvalidOperationException("An active run configuration cannot be cleared.");
        }

        definition = null;
        workbookMetadata = null;
        inputPath = string.Empty;
        nextDraftSummary = null;
        resumeResetReason = isResumeMode || !string.IsNullOrWhiteSpace(resumePartialPath) || HasInterruptedRun
            ? "入力が未選択になったため、再開指定を解除しました。入力を読み込み、checkpointを指定し直してください。"
            : string.Empty;
        resumePartialPath = string.Empty;
        isResumeMode = false;
        InvalidateResumePreflight(clearCheckpoint: true);
        ClearInterruptedRun();
        runtimeErrorCode = null;
        runPreflightErrors = [];
        LastRunContext = null;
        ResetProgress();
        OnPropertiesChanged(
            nameof(IsConfigured),
            nameof(OutputDirectory),
            nameof(ResumePartialPath),
            nameof(IsResumeMode),
            nameof(ResumeResetReason),
            nameof(OutputModeText),
            nameof(PlannedEvaluationCount),
            nameof(WorstCaseAttemptCount),
            nameof(PlanSummary),
            nameof(RunStatusText));
        Revalidate();
    }

    public Task LoginAsync(CancellationToken cancellationToken = default)
    {
        if (!CanLogin)
        {
            return Task.CompletedTask;
        }

        LastLoginTask = LoginCoreAsync(cancellationToken);
        OnPropertyChanged(nameof(LastLoginTask));
        return LastLoginTask;
    }

    private async Task LoginCoreAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = operationCancellation.Token;
        loginCancellation = operationCancellation;
        IsLoggingIn = true;
        bool refreshModels = false;

        try
        {
            if (disposed)
            {
                return;
            }

            // Login may switch accounts. Only a fresh authentication check can restore availability.
            Interlocked.Increment(ref authenticationSequence);
            ClearAuthenticationModels();
            AuthenticationState = ExecutionAuthenticationState.NotChecked;
            LoginStatusText = "GitHub へのログイン中です。Copilot CLI とブラウザーの案内に従ってください。";
            Revalidate();

            CopilotLoginResult result = await loginService.LoginAsync(token);
            if (!disposed)
            {
                // A01 retains ownership when exit cannot be confirmed. A retry may
                // observe that exit, but authentication/run must not race that process.
                // A cancelled retry can return before A01 even inspects its retained process.
                if (result.Status != CopilotLoginStatus.Cancelled)
                {
                    loginExitUnconfirmed = result.Status == CopilotLoginStatus.AlreadyRunning
                        || result.ErrorCategory is CopilotLoginErrorCategory.ProcessWaitFailed
                            or CopilotLoginErrorCategory.CleanupFailed;
                }

                loginServiceDisposed = result.Status == CopilotLoginStatus.Disposed;
                LoginStatusText = loginExitUnconfirmed
                    ? LoginExitUnconfirmedMessage
                    : LoginResultMessage(result);
                refreshModels = result.Status == CopilotLoginStatus.Completed && !loginExitUnconfirmed;
            }
        }
        catch
        {
            if (!disposed)
            {
                loginExitUnconfirmed = true;
                LoginStatusText = LoginExitUnconfirmedMessage;
            }
        }
        finally
        {
            if (ReferenceEquals(loginCancellation, operationCancellation))
            {
                loginCancellation = null;
                if (!disposed)
                {
                    IsLoggingIn = false;
                    Revalidate();
                }
            }
        }

        if (refreshModels && !disposed && !token.IsCancellationRequested)
        {
            await CheckAuthenticationAsync(token);
            if (!disposed)
            {
                LoginStatusText = IsAuthenticationAvailable
                    ? "ログイン後の認証確認とモデル一覧の更新が完了しました。"
                    : "ログイン処理は終了しましたが、モデル一覧を更新できませんでした。「Copilot 状態を確認」で再試行してください。";
            }
        }
    }

    public void CancelLogin()
    {
        if (!CanCancelLogin)
        {
            return;
        }

        LoginStatusText = "GitHub へのログインを取り消しています…";
        try
        {
            loginCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The attempt finished or the application closed before cancellation.
        }
        catch
        {
            if (!disposed)
            {
                LoginStatusText = "ログインの取消処理で問題が発生しました。終了を確認できない場合はアプリを開き直してください。";
            }
        }

        if (!disposed)
        {
            OnPropertyChanged(nameof(CanCancelLogin));
            RaiseCommandStates();
        }
    }

    public async Task CheckAuthenticationAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!CanCheckAuthentication)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref authenticationSequence);
        authenticationCancellation?.Cancel();
        authenticationCancellation?.Dispose();
        authenticationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = authenticationCancellation.Token;
        IsCheckingAuthentication = true;
        AuthenticationState = ExecutionAuthenticationState.Checking;
        ClearAuthenticationModels();
        runtimeErrorCode = null;
        runPreflightErrors = [];
        Revalidate();

        try
        {
            ExecutionAuthenticationSnapshot result = await authenticationBoundary
                .CheckAsync(token);
            token.ThrowIfCancellationRequested();
            if (sequence != Volatile.Read(ref authenticationSequence))
            {
                return;
            }

            ApplyAuthentication(result);
            if (result.State == ExecutionAuthenticationState.Available && ModelCatalogRefreshed is { } handlers)
            {
                foreach (Func<CancellationToken, Task> handler in handlers.GetInvocationList())
                {
                    await handler(token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (sequence == Volatile.Read(ref authenticationSequence))
            {
                AuthenticationState = ExecutionAuthenticationState.Cancelled;
            }
        }
        catch
        {
            if (sequence == Volatile.Read(ref authenticationSequence))
            {
                AuthenticationState = ExecutionAuthenticationState.RuntimeFailed;
            }
        }
        finally
        {
            if (sequence == Volatile.Read(ref authenticationSequence))
            {
                IsCheckingAuthentication = false;
                Revalidate();
            }
        }
    }

    /// <summary>
    /// Runs the application-start sequence once: check the credentials the bundled CLI
    /// already resolves for this OS user and, only when none are usable, start the
    /// bundled login once. Never throws; never selects a model or starts evaluation.
    /// </summary>
    public Task RunStartupAuthenticationAsync(
        bool allowAutomaticLogin,
        CancellationToken cancellationToken = default)
    {
        if (startupAuthenticationStarted || disposed || closing)
        {
            return Task.CompletedTask;
        }

        startupAuthenticationStarted = true;
        LastStartupAuthenticationTask = StartupAuthenticationCoreAsync(allowAutomaticLogin, cancellationToken);
        OnPropertyChanged(nameof(LastStartupAuthenticationTask));
        return LastStartupAuthenticationTask;
    }

    private async Task StartupAuthenticationCoreAsync(
        bool allowAutomaticLogin,
        CancellationToken cancellationToken)
    {
        try
        {
            // The settings restore may already have started a check; join it instead of racing it.
            await WaitForAuthenticationCheckAsync(cancellationToken);
            if (disposed || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (AuthenticationState == ExecutionAuthenticationState.NotChecked)
            {
                if (!CanCheckAuthentication)
                {
                    return;
                }

                LoginStatusText = "起動時に、この PC で利用中の GitHub アカウントのログイン状態を自動確認しています…";
                await CheckAuthenticationAsync(cancellationToken);
                if (disposed || cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }

            switch (AuthenticationState)
            {
                case ExecutionAuthenticationState.Available:
                    LoginStatusText = "この PC で利用中の GitHub アカウントで自動的にログインしました。";
                    break;
                case ExecutionAuthenticationState.AuthRequired when allowAutomaticLogin && CanLogin:
                    LoginStatusText = "利用できる GitHub ログインが見つからないため、自動ログインを開始します…";
                    await LoginAsync(cancellationToken);
                    break;
                case ExecutionAuthenticationState.AuthRequired:
                    LoginStatusText = "利用できる GitHub ログインが見つかりませんでした。「GitHubにログイン」を押してください。";
                    break;
                default:
                    LoginStatusText = "起動時の自動ログイン確認を完了できませんでした。「Copilot 状態を確認」で再試行してください。";
                    break;
            }
        }
        catch
        {
            // Startup convenience must not fault the app or expose exception content.
            if (!disposed)
            {
                LoginStatusText = "起動時の自動ログイン確認を完了できませんでした。「Copilot 状態を確認」で再試行してください。";
            }
        }
    }

    private async Task WaitForAuthenticationCheckAsync(CancellationToken cancellationToken)
    {
        if (!IsCheckingAuthentication)
        {
            return;
        }

        TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void HandleChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IsCheckingAuthentication) && !IsCheckingAuthentication)
            {
                finished.TrySetResult();
            }
        }

        PropertyChanged += HandleChanged;
        try
        {
            if (IsCheckingAuthentication)
            {
                await finished.Task.WaitAsync(cancellationToken);
            }
        }
        finally
        {
            PropertyChanged -= HandleChanged;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Revalidate();
        if (!CanStart
            || definition is null
            || workbookMetadata is null
            || selectedModelId is null
            || runtimeIdentity is null)
        {
            return;
        }

        QuantificationDefinition runDefinition = InputViewModel.CloneDefinition(definition);
        string runInputPath = inputPath;
        string runModelId = selectedModelId;
        CopilotModelAvailability runModel = modelsById[runModelId];
        CopilotRuntimeIdentity runRuntimeIdentity = runtimeIdentity;
        QuantificationRunRequest request = new()
        {
            DraftDefinition = runDefinition,
            WorkbookMetadata = workbookMetadata,
            InputPath = runInputPath,
            ModelId = runModelId,
            ReasoningEffort = SelectedModelReasoningEffort,
            ContextTier = SelectedContextTier,
            MaximumPromptTokens = SelectedModelPromptTokenLimit,
            MaximumContextWindowTokens = SelectedContextTier == ModelOptionPolicy.LongContextTier
                || runModel.EffectivePromptTokenLimit is null
                ? null
                : runModel.MaximumContextWindowTokens ?? runModel.EffectivePromptTokenLimit,
            MaxConcurrency = MaxConcurrency,
            RuntimeIdentity = runRuntimeIdentity,
            OutputDirectory = IsResumeMode ? null : OutputDirectory,
            ResumePartialPath = IsResumeMode ? ResumePartialPath : null,
            UseDurableWorkflow = true,
        };

        runCancellation?.Cancel();
        runCancellation?.Dispose();
        runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = runCancellation.Token;
        SynchronizationContext? observerContext = SynchronizationContext.Current;
        long currentProgressSequence = Interlocked.Increment(ref progressSequence);
        long jobSequence = Interlocked.Increment(ref costSequence);
        JobCostSnapshot? latestCost = null;
        object costGate = new();
        Cost.Reset();
        void OnCostChanged(JobCostSnapshot value)
        {
            lock (costGate)
            {
                if (latestCost is { } previous
                    && (previous.JobId != value.JobId || previous.Revision >= value.Revision)) return;
                latestCost = value;
            }

            void Apply()
            {
                if (!disposed && jobSequence == Volatile.Read(ref costSequence)) Cost.Apply(value);
            }
            if (observerContext is null || ReferenceEquals(observerContext, SynchronizationContext.Current)) Apply();
            else observerContext.Post(_ => Apply(), null);
        }
        // Publish a completion promise before any IsRunning observer can reenter close.
        // Keep the start gate closed through all completion notifications as well.
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runStarting = true;
        LastRunTask = completion.Task;
        runtimeErrorCode = null;
        runPreflightErrors = [];
        // Freeze before start-state observers can edit settings. The same immutable
        // request goes to the boundary; no draft-derived snapshot or path is invented.
        currentRunRequest = request;
        try
        {
            InvalidateResumePreflight();
            long runResumeSequence = resumeInspectionSequence;
            ClearInterruptedRun();
            IsCancelling = false;
            IsRunning = true;
            OnPropertyChanged(nameof(LastRunTask));
            LastRunContext = null;
            ResetProgress();
            OnPropertiesChanged(
                nameof(HasCurrentRun),
                nameof(CurrentRunModelId),
                nameof(CurrentRunMaxConcurrency),
                nameof(CurrentRunReasoningEffortText),
                nameof(CurrentRunContextTierText),
                nameof(CurrentRunLimitText),
                nameof(CurrentRunOutputSummary));
            Revalidate();
            RunSummary summary = await runBoundary.RunAsync(
                request,
                progress => ReportProgress(progress, observerContext, currentProgressSequence),
                OnCostChanged,
                preview => ReportPreview(preview, observerContext, currentProgressSequence),
                token);
            if (disposed) return;
            Interlocked.Increment(ref progressSequence);
            ApplyCompletedSummary(summary);
            if (runResumeSequence == resumeInspectionSequence) RememberInterruptedRun(summary);
            ExecutionRunContext context = new(
                summary,
                runInputPath,
                runModelId,
                runRuntimeIdentity,
                latestCost,
                request.ReasoningEffort,
                request.ContextTier);
            LastRunContext = context;
            RaiseRunCompleted(context);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            runtimeErrorCode = "RUN_CANCELLED_WITHOUT_SUMMARY";
            progressStatus = EvaluationProgressStatus.Cancelled;
        }
        catch (QuantificationMappingValidationException)
        {
            runtimeErrorCode = "MAPPING_INVALID";
        }
        catch (QuantificationDefinitionValidationException)
        {
            runtimeErrorCode = "DEFINITION_INVALID";
        }
        catch (QuantificationRunPreflightException exception)
        {
            runtimeErrorCode = null;
            runPreflightErrors = exception.Errors
                .Select(CapacityTechnicalError)
                .ToImmutableArray();
        }
        catch (QuantificationRunException exception)
        {
            runtimeErrorCode = exception.Code;
        }
        catch
        {
            runtimeErrorCode = "RUN_FAILED";
        }
        finally
        {
            try
            {
                if (!disposed)
                {
                    LivePreview.Finish();
                }

                if (!disposed && jobSequence == Volatile.Read(ref costSequence) && latestCost is { } finalCost)
                {
                    Cost.Apply(finalCost);
                }
                IsRunning = false;
                IsCancelling = false;
                OnPropertiesChanged(
                    nameof(ProgressText),
                    nameof(ProgressPercent),
                    nameof(RunStatusText));
            }
            finally
            {
                runStarting = false;
                completion.TrySetResult();
                if (!disposed) Revalidate();
            }
        }
    }

    public void Cancel()
    {
        if (!CanCancel)
        {
            return;
        }

        IsCancelling = true;
        progressStatus = EvaluationProgressStatus.Cancelling;
        TryCancel(runCancellation);
        OnPropertiesChanged(nameof(ProgressText), nameof(RunStatusText));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        closing = true;
        LivePreview.Reset();
        InvalidateResumePreflight(clearCheckpoint: true);
        TryCancel(checkpointInputCancellation);
        Interlocked.Increment(ref authenticationSequence);
        CancellationTokenSource? cancellation = loginCancellation;
        loginCancellation = null;
        try
        {
            cancellation?.Cancel();
        }
        catch
        {
            // Cancellation callbacks cannot skip owned-process cleanup or expose content.
        }
        finally
        {
            // Do not wait on LastLoginTask: its continuation may need the closing UI.
            // A01 performs synchronous owned-process cleanup without that continuation.
            loginService.Dispose();
            cancellation?.Dispose();
        }

        TryCancel(authenticationCancellation);
        authenticationCancellation?.Dispose();
        TryCancel(runCancellation);
        runCancellation?.Dispose();
        LoginStatusText = "アプリを終了したため、ログインは開始できません。";
        IsLoggingIn = false;
        OnPropertiesChanged(
            nameof(CanLogin),
            nameof(CanCancelLogin),
            nameof(CanCheckAuthentication),
            nameof(CanStart),
            nameof(ValidationSummary));
        RaiseCommandStates();
    }

    public override string ToString() =>
        $"{nameof(ExecutionViewModel)} {{ AuthenticationState = {AuthenticationState}, IsRunning = {IsRunning}, PlannedEvaluationCount = {PlannedEvaluationCount.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";

    private static string LoginResultMessage(CopilotLoginResult result) => result.Status switch
    {
        CopilotLoginStatus.Completed => "ログイン処理が終了しました。認証状態とモデル一覧を更新します…",
        CopilotLoginStatus.CliUnavailable => BundledCliUnavailableMessage,
        CopilotLoginStatus.RuntimeFailed when result.ErrorCategory is CopilotLoginErrorCategory.ProcessWaitFailed
            or CopilotLoginErrorCategory.CleanupFailed => LoginExitUnconfirmedMessage,
        CopilotLoginStatus.RuntimeFailed => "GitHub へのログインに失敗しました。再試行するか、「Copilot 状態を確認」を押してください。",
        CopilotLoginStatus.Cancelled => "GitHub へのログインを取り消しました。再試行するか、「Copilot 状態を確認」を押してください。",
        CopilotLoginStatus.AlreadyRunning => LoginExitUnconfirmedMessage,
        CopilotLoginStatus.Disposed => "ログイン処理は終了しています。アプリを開き直してください。",
        _ => LoginExitUnconfirmedMessage,
    };

    private static long CountPlannedEvaluations(QuantificationDefinition? definition)
    {
        if (definition is null)
        {
            return 0;
        }

        long selectedRows = Math.Max(
            0L,
            ((long)definition.LastDataRow - definition.FirstDataRow) + 1L);
        long referenceOperations = definition.Questions.Count(question => question.Enabled);
        long normalOperations = definition.Questions
            .Where(question => question.Enabled)
            .Sum(question => (long)question.Evaluators.Count(evaluator => evaluator.Enabled));
        long specialOperations = definition.Questions
            .Where(question => question.Enabled)
            .Sum(question => (long)question.SpecialEvaluations.Count(special => special.Enabled));
        try
        {
            return checked(referenceOperations
                + selectedRows * (normalOperations + specialOperations + referenceOperations));
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private bool IsSameConfiguration(
        QuantificationDefinition incomingDefinition,
        WorkbookMetadata metadata,
        string normalizedInputPath)
    {
        // A freshly read metadata instance represents a new input observation, even at the same path.
        if (definition is null
            || !ReferenceEquals(workbookMetadata, metadata)
            || !string.Equals(inputPath, normalizedInputPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return string.Equals(
                definitionSerializer.Serialize(definition),
                definitionSerializer.Serialize(incomingDefinition),
                StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            // Unsupported draft values must still reach the existing definition validation.
            return false;
        }
    }

    private string? ResolveSelectedModelId()
    {
        string? requested = preferredModelId ?? initialModelId;
        return AuthenticationState == ExecutionAuthenticationState.Available
            && requested is not null
            && modelsById.ContainsKey(requested)
            ? requested
            : null;
    }

    private void SetModelPreference(string? preference)
    {
        bool preferenceChanged = !string.Equals(preferredModelId, preference, StringComparison.Ordinal);
        preferredModelId = preference;
        initialModelId = null;
        initialModelSelectionApplied = true;
        string? effective = ResolveSelectedModelId();
        bool selectionChanged = !string.Equals(selectedModelId, effective, StringComparison.Ordinal);
        selectedModelId = effective;
        if (!preferenceChanged && !selectionChanged)
        {
            return;
        }

        InvalidateResumePreflight();
        runtimeErrorCode = null;
        runPreflightErrors = [];
        updatingModelSelection = true;
        try
        {
            RefreshModelOptions();
            if (preferenceChanged)
            {
                OnPropertyChanged(nameof(PreferredModelId));
            }

            if (selectionChanged)
            {
                OnPropertyChanged(nameof(SelectedModelId));
                OnPropertiesChanged(
                    nameof(SelectedModelPromptTokenLimit),
                    nameof(SelectedModelLimitText),
                    nameof(SelectedModelReasoningEffort),
                    nameof(SelectedModelReasoningEffortText));
            }
        }
        finally
        {
            updatingModelSelection = false;
        }

        Revalidate();
    }

    private void ClearAuthenticationModels()
    {
        InvalidateResumePreflight();
        updatingModelSelection = true;
        try
        {
            runtimeIdentity = null;
            selectedModelId = null;
            // Keep the last catalog visible, but discard all effective authorization/capacity.
            modelsById.Clear();
            RefreshModelOptions();
            OnPropertiesChanged(
                nameof(AvailableModelIds),
                nameof(SelectedModelId),
                nameof(SelectedModelPromptTokenLimit),
                nameof(SelectedModelLimitText),
                nameof(SelectedModelReasoningEffort),
                nameof(SelectedModelReasoningEffortText),
                nameof(RuntimeIdentityText));
        }
        finally
        {
            updatingModelSelection = false;
        }
    }

    private void ApplyAuthentication(ExecutionAuthenticationSnapshot result)
    {
        InvalidateResumePreflight();
        updatingModelSelection = true;
        try
        {
            AuthenticationState = result.State;
            runtimeIdentity = result.RuntimeIdentity;
            selectedModelId = null;
            modelsById.Clear();
            if (result.State == ExecutionAuthenticationState.Available)
            {
                hasRefreshedModels = true;
                SynchronizeModelIds(result.ModelIds);
                foreach (CopilotModelAvailability model in result.Models)
                {
                    modelsById.Add(model.Id, model);
                }

                ImmutableArray<CachedCopilotModel> next = [.. result.Models.Select(model =>
                    new CachedCopilotModel(model.Id, model.MaximumPromptTokens, model.MaximumContextWindowTokens))];
                // An over-limit catalog stays selectable but is not cached, so it cannot invalidate the settings file.
                if (next.Length <= ApplicationSettings.MaximumCachedModels
                    && (CachedModels is not { } previous || !previous.SequenceEqual(next)))
                {
                    CachedModels = next;
                    OnPropertyChanged(nameof(CachedModels));
                }
            }

            if (result.State == ExecutionAuthenticationState.Available && !initialModelSelectionApplied)
            {
                // Preserve the legacy first selection once, without persisting an implicit preference.
                initialModelId = preferredModelId is null ? modelItems.FirstOrDefault() : null;
                initialModelSelectionApplied = true;
            }

            selectedModelId = ResolveSelectedModelId();
            RefreshModelOptions();
            OnPropertiesChanged(
                nameof(AvailableModelIds),
                nameof(SelectedModelId),
                nameof(SelectedModelPromptTokenLimit),
                nameof(SelectedModelLimitText),
                nameof(SelectedModelReasoningEffort),
                nameof(SelectedModelReasoningEffortText),
                nameof(RuntimeIdentityText));
        }
        finally
        {
            updatingModelSelection = false;
        }
    }

    private void SynchronizeModelIds(IReadOnlyList<string> ids)
    {
        HashSet<string> desired = new(ids, StringComparer.Ordinal);
        for (int index = modelItems.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(modelItems[index])) modelItems.RemoveAt(index);
        }

        for (int index = 0; index < ids.Count; index++)
        {
            if (index < modelItems.Count && modelItems[index] == ids[index]) continue;
            int existing = modelItems.IndexOf(ids[index]);
            if (existing >= 0) modelItems.Move(existing, index);
            else modelItems.Insert(index, ids[index]);
        }
    }

    private void RaiseRunCompleted(ExecutionRunContext context)
    {
        EventHandler<ExecutionRunCompletedEventArgs>? handlers = RunCompleted;
        if (handlers is null)
        {
            return;
        }

        ExecutionRunCompletedEventArgs eventArgs = new(context);
        foreach (EventHandler<ExecutionRunCompletedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch
            {
                // Navigation and presentation observers cannot corrupt completed run data.
            }
        }
    }

    private void ReportProgress(
        EvaluationProgress progress,
        SynchronizationContext? observerContext,
        long sequence)
    {
        if (observerContext is null || ReferenceEquals(observerContext, SynchronizationContext.Current))
        {
            ApplyProgress(progress, sequence);
            return;
        }

        observerContext.Post(_ => ApplyProgress(progress, sequence), null);
    }

    private void ReportPreview(
        LivePreviewUpdate update,
        SynchronizationContext? observerContext,
        long sequence)
    {
        if (observerContext is null || ReferenceEquals(observerContext, SynchronizationContext.Current))
        {
            ApplyPreview(update, sequence);
            return;
        }

        observerContext.Post(_ => ApplyPreview(update, sequence), null);
    }

    private void ApplyPreview(LivePreviewUpdate update, long sequence)
    {
        if (disposed || !IsRunning || sequence != Volatile.Read(ref progressSequence)) return;
        try
        {
            LivePreview.Apply(update);
        }
        catch
        {
            // The preview is presentation only and cannot fail a run.
        }
    }

    private void ApplyProgress(EvaluationProgress progress, long sequence)
    {
        if (disposed || !IsRunning || sequence != Volatile.Read(ref progressSequence)) return;
        progressTotal = progress.Total;
        progressCompleted = progress.Completed;
        progressInFlight = progress.InFlight;
        progressStatus = progress.Status;
        progressStage = progress.Stage;
        referenceCompleted = progress.ReferenceCompleted;
        referenceTotal = progress.ReferenceTotal;
        rowCompleted = progress.RowCompleted;
        rowTotal = progress.RowTotal;
        reservedFinalPath = progress.FinalPath ?? reservedFinalPath;
        partialPath = progress.PartialPath ?? partialPath;
        OnPropertiesChanged(
            nameof(ProgressTotal),
            nameof(ProgressCompleted),
            nameof(ProgressInFlight),
            nameof(ProgressPercent),
            nameof(ProgressText),
            nameof(ProgressStage),
            nameof(ReferenceCompleted),
            nameof(ReferenceTotal),
            nameof(RowCompleted),
            nameof(RowTotal),
            nameof(ReservedFinalPath),
            nameof(PartialPath),
            nameof(StageText),
            nameof(DurableProgressText),
            nameof(OutputIdentityText),
            nameof(RunStatusText));
    }

    private void ApplyCompletedSummary(RunSummary summary)
    {
        progressTotal = summary.PlannedOperationCount;
        progressCompleted = summary.CompletedOperationCount;
        progressInFlight = 0;
        progressStage = DurableEvaluationStage.Completed;
        if (summary.IsDurable)
        {
            referenceTotal = summary.Snapshot.Definition.Questions.Count(question => question.Enabled);
            referenceCompleted = summary.References.Length;
            rowTotal = summary.Plan.Mapping.SelectedRowCount;
            rowCompleted = summary.CompletedRows.Length;
        }

        reservedFinalPath = summary.FinalPath ?? reservedFinalPath;
        partialPath = summary.PartialPath ?? partialPath;
        progressStatus = summary.IsPartial
            ? EvaluationProgressStatus.Cancelled
            : EvaluationProgressStatus.Completed;
        OnPropertiesChanged(
            nameof(ProgressTotal),
            nameof(ProgressCompleted),
            nameof(ProgressInFlight),
            nameof(ProgressPercent),
            nameof(ProgressText),
            nameof(ProgressStage),
            nameof(ReferenceCompleted),
            nameof(ReferenceTotal),
            nameof(RowCompleted),
            nameof(RowTotal),
            nameof(ReservedFinalPath),
            nameof(PartialPath),
            nameof(StageText),
            nameof(DurableProgressText),
            nameof(OutputIdentityText));
    }

    private void ResetProgress()
    {
        progressTotal = 0;
        progressCompleted = 0;
        progressInFlight = 0;
        progressStatus = null;
        progressStage = null;
        referenceCompleted = 0;
        referenceTotal = 0;
        rowCompleted = 0;
        rowTotal = 0;
        reservedFinalPath = string.Empty;
        partialPath = string.Empty;
        LivePreview.Reset();
        OnPropertiesChanged(
            nameof(ProgressTotal),
            nameof(ProgressCompleted),
            nameof(ProgressInFlight),
            nameof(ProgressPercent),
            nameof(ProgressText),
            nameof(ProgressStage),
            nameof(ReferenceCompleted),
            nameof(ReferenceTotal),
            nameof(RowCompleted),
            nameof(RowTotal),
            nameof(ReservedFinalPath),
            nameof(PartialPath),
            nameof(StageText),
            nameof(DurableProgressText),
            nameof(OutputIdentityText));
    }

    private void Revalidate()
    {
        Dictionary<(string Code, string? NodeId, string? Path, string Field), ExecutionTechnicalError> errors = [];
        if (definition is null || workbookMetadata is null || string.IsNullOrWhiteSpace(inputPath))
        {
            AddError(errors, new ExecutionTechnicalError(
                "EXECUTION_CONFIGURATION_REQUIRED",
                "Input / Design",
                "入力 workbook と有効な定量化設計が必要です。"));
        }
        else
        {
            try
            {
                _ = Path.GetFullPath(inputPath);
            }
            catch (Exception exception) when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                AddError(errors, new ExecutionTechnicalError(
                    "INPUT_PATH_INVALID",
                    "Input",
                    "入力 workbook path を確認してください。"));
            }

            DefinitionValidationResult definitionResult = definitionValidator.Validate(definition);
            foreach (DefinitionValidationError error in definitionResult.Errors)
            {
                AddError(errors, new ExecutionTechnicalError(
                    error.Code,
                    error.Field,
                    "定量化設計の技術的な設定を修正してください。",
                    nodeId: error.NodeId,
                    path: error.Path));
            }

            ColumnMappingValidationResult mappingResult = mappingValidator.Validate(
                workbookMetadata,
                definition);
            foreach (ColumnMappingValidationError error in mappingResult.Errors)
            {
                AddError(errors, new ExecutionTechnicalError(
                    error.Code,
                    error.Field,
                    "入力 workbook と列 mapping を一致させてください。",
                    nodeId: error.QuestionId,
                    path: error.Path));
            }

            if (definitionResult.IsValid && mappingResult.IsValid)
            {
                QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);
                WorkbookExecutionPreflightResult capacity = workbookPreflight.Validate(
                    snapshot,
                    workbookMetadata);
                foreach (ExecutionCapacityError error in capacity.Errors)
                {
                    AddError(errors, CapacityTechnicalError(error));
                }
            }
        }

        if (PlannedEvaluationCount > int.MaxValue)
        {
            AddError(errors, new ExecutionTechnicalError(
                "EVALUATION_PLAN_TOO_LARGE",
                "Evaluation plan",
                "評価単位数が実行可能な上限を超えています。"));
        }

            if (WorstCaseAttemptCount > EvaluationRequestCapacityValidator.MaximumWorstCaseAttemptsPerRun)
            {
                AddError(errors, new ExecutionTechnicalError(
                "ATTEMPT_BUDGET_TOO_LARGE",
                "Evaluation plan",
                $"retry込み最大attempt数 {WorstCaseAttemptCount.ToString(CultureInfo.InvariantCulture)} がrun上限 {EvaluationRequestCapacityValidator.MaximumWorstCaseAttemptsPerRun.ToString(CultureInfo.InvariantCulture)} を超えています。"));
            }

        if (MaxConcurrency is < EvaluationSchedulerOptions.MinimumMaxConcurrency
            or > EvaluationSchedulerOptions.MaximumMaxConcurrency)
        {
            AddError(errors, new ExecutionTechnicalError(
                "CONCURRENCY_OUT_OF_RANGE",
                "Concurrency",
                "並列度は 1～16 にしてください。"));
        }

        if (IsResumeMode)
        {
            try
            {
                string resumePath = Path.GetFullPath(ResumePartialPath);
                if (!resumePath.EndsWith(".partial.xlsx", StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(resumePath))
                {
                    AddError(errors, new ExecutionTechnicalError(
                        "RESUME_PARTIAL_REQUIRED",
                        "Resume",
                        "既存の .partial.xlsx checkpointを指定してください。"));
                }
            }
            catch (Exception exception) when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                AddError(errors, new ExecutionTechnicalError(
                    "RESUME_PARTIAL_REQUIRED",
                    "Resume",
                    "既存の .partial.xlsx checkpointを指定してください。"));
            }
        }
        else if (OutputDirectoryOverride is { } explicitDirectory
            && !Path.IsPathFullyQualified(explicitDirectory))
        {
            AddError(errors, new ExecutionTechnicalError(
                "OUTPUT_DIRECTORY_INVALID",
                "Output directory",
                "明示する出力先は絶対パスで指定してください。空欄の場合は入力隣接の result を使用します。"));
        }
        else
        {
            try
            {
                string targetDirectory = Path.GetFullPath(OutputDirectory);
                if (File.Exists(targetDirectory))
                {
                    AddError(errors, new ExecutionTechnicalError(
                        "OUTPUT_DIRECTORY_INVALID",
                        "Output directory",
                        "既存directoryまたは作成可能なdirectoryを指定してください。"));
                }
            }
            catch (Exception exception) when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                AddError(errors, new ExecutionTechnicalError(
                    "OUTPUT_DIRECTORY_INVALID",
                    "Output directory",
                    "既存directoryまたは作成可能なdirectoryを指定してください。"));
            }
        }

        switch (AuthenticationState)
        {
            case ExecutionAuthenticationState.NotChecked:
                AddError(errors, new ExecutionTechnicalError(
                    "AUTH_CHECK_REQUIRED",
                    "Copilot",
                    "既存 Copilot CLI login の状態を確認してください。"));
                break;
            case ExecutionAuthenticationState.Available when runtimeIdentity is null:
                AddError(errors, new ExecutionTechnicalError(
                    "RUNTIME_IDENTITY_REQUIRED",
                    "Copilot",
                    "安全な CLI / SDK identity を確認できません。"));
                break;
            case ExecutionAuthenticationState.Available when modelItems.Count == 0:
                AddError(errors, new ExecutionTechnicalError(
                    "MODEL_REQUIRED",
                    "Model",
                    "利用可能な model がありません。"));
                break;
            case ExecutionAuthenticationState.Available when SelectedModelId is null
                || !modelItems.Contains(SelectedModelId)
                || !modelsById.ContainsKey(SelectedModelId):
                AddError(errors, new ExecutionTechnicalError(
                    "MODEL_SELECTION_REQUIRED",
                    "Model",
                    "利用可能な model を選択してください。"));
                break;
            case ExecutionAuthenticationState.AuthRequired:
                AddError(errors, new ExecutionTechnicalError(
                    ResultsStatusCodes.AuthRequired,
                    "Copilot",
                    "Copilot CLI で login してから再確認してください。"));
                break;
            case ExecutionAuthenticationState.CliUnavailable:
                AddError(errors, new ExecutionTechnicalError(
                    "COPILOT_CLI_UNAVAILABLE",
                    "Copilot",
                    BundledCliUnavailableMessage));
                break;
            case ExecutionAuthenticationState.RuntimeFailed:
                AddError(errors, new ExecutionTechnicalError(
                    "COPILOT_RUNTIME_FAILED",
                    "Copilot",
                    "Copilot runtime を安全に確認できませんでした。"));
                break;
        }


        if (reasoningPreferenceUnavailable)
            AddError(errors, new ExecutionTechnicalError("MODEL_REASONING_EFFORT_UNAVAILABLE", "ReasoningEffort",
                "保存した思考レベルは現在利用できません。共通設定で対応する値を選び直してください。"));
        if (contextPreferenceUnavailable)
            AddError(errors, new ExecutionTechnicalError("MODEL_CONTEXT_TIER_UNAVAILABLE", "ContextTier",
                "保存した Context Size は現在利用できません。共通設定で対応する値を選び直してください。"));

        if (runtimeErrorCode is not null)
        {
            AddError(errors, new ExecutionTechnicalError(
                runtimeErrorCode,
                "Run",
                RunFailureMessage(runtimeErrorCode)));
        }

            foreach (ExecutionTechnicalError error in runPreflightErrors)
            {
                AddError(errors, error);
            }

        technicalErrorItems.Clear();
        foreach (ExecutionTechnicalError error in errors.Values)
        {
            technicalErrorItems.Add(error);
        }

        OnPropertiesChanged(
            nameof(HasTechnicalErrors),
            nameof(IsTechnicallyValid),
            nameof(ValidationSummary),
            nameof(CanStart));
        RaiseCommandStates();
    }

    private static void AddError(
        IDictionary<(string Code, string? NodeId, string? Path, string Field), ExecutionTechnicalError> errors,
        ExecutionTechnicalError error) =>
        errors.TryAdd(error.Key, error);

    private static ExecutionTechnicalError CapacityTechnicalError(
        ExecutionCapacityError error) =>
        new(
            error.Code,
            error.Field,
            $"実測値 {error.ActualDimension} は上限 {error.Limit} を満たしません。定量化設計を縮小してください。",
            nodeId: error.NodeId);

    private static string RunFailureMessage(string code) => code switch
    {
        "INPUT_SNAPSHOT_FAILED" => "入力 workbook の immutable identity を取得できませんでした。",
        "MAPPING_INVALID" => "入力 workbook と列 mapping が一致しません。",
        "DEFINITION_INVALID" => "定量化設計に技術的な問題があります。",
        CheckpointAdmissionStatusCodes.InputMismatch => "checkpointと現在の入力identityが一致しません。",
        CheckpointAdmissionStatusCodes.DefinitionMismatch => "checkpointと現在の定量化設計が一致しません。",
        CheckpointAdmissionStatusCodes.ModelMismatch => "checkpointと選択modelが一致しません。",
        CheckpointAdmissionStatusCodes.RuntimeMismatch => "checkpointと現在のCLI/SDK runtime identityが一致しません。",
        CheckpointStatusCodes.Invalid or CheckpointStatusCodes.HashMismatch or CheckpointStatusCodes.SchemaUnsupported =>
            "checkpointをclosed validationできないため再開しませんでした。",
        CheckpointStatusCodes.SaveFailed => "checkpointを安全に保存できませんでした。",
        "RUN_CANCELLED_WITHOUT_SUMMARY" => "部分結果を確定する前に実行が中断されました。再実行してください。",
        _ => "実行を安全に完了できませんでした。状態を確認して再実行してください。",
    };

    private void RaiseCommandStates()
    {
        checkAuthenticationCommand.RaiseCanExecuteChanged();
        loginCommand.RaiseCanExecuteChanged();
        cancelLoginCommand.RaiseCanExecuteChanged();
        startCommand.RaiseCanExecuteChanged();
        cancelCommand.RaiseCanExecuteChanged();
        RaiseResumeStates();
    }
}