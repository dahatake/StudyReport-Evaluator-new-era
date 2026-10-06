using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using DocumentFormat.OpenXml.Packaging;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Usage;
using StudyReportEvaluator.App.Visualization;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Validation;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Scoring;

namespace StudyReportEvaluator.App.ViewModels;

public static class ResultsOutputStatusCodes
{
    public const string Ready = "READY";
    public const string Success = AtomicOutputStatusCodes.Success;
    public const string InputChanged = AtomicOutputStatusCodes.InputChanged;
    public const string OutputInvalid = AtomicOutputStatusCodes.OutputInvalid;
    public const string TargetExists = AtomicOutputStatusCodes.TargetExists;
    public const string CommitFailed = AtomicOutputStatusCodes.CommitFailed;
    public const string Cancelled = AtomicOutputStatusCodes.Cancelled;
    public const string CleanupFailed = AtomicOutputStatusCodes.CleanupFailed;
    public const string OverrideInvalid = "OVERRIDE_INVALID";
    public const string OutputPathInvalid = "OUTPUT_PATH_INVALID";
    public const string ExportFailed = "EXPORT_FAILED";
}

public sealed class ResultsOutputPathAssessment
{
    public ResultsOutputPathAssessment(string code, bool isValid, bool targetExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (isValid && targetExists)
        {
            throw new ArgumentException("An existing target is not a valid output destination.");
        }

        Code = code;
        IsValid = isValid;
        TargetExists = targetExists;
    }

    public string Code { get; }

    public bool IsValid { get; }

    public bool TargetExists { get; }

    public static ResultsOutputPathAssessment Valid { get; } =
        new(ResultsOutputStatusCodes.Ready, isValid: true, targetExists: false);

    public override string ToString() =>
        $"{nameof(ResultsOutputPathAssessment)} {{ Code = {Code}, IsValid = {IsValid}, TargetExists = {TargetExists}, Content = <redacted> }}";
}

public sealed class ResultsOutputRequest
{
    public ResultsOutputRequest(
        ExecutionRunContext context,
        string outputPath,
        IEnumerable<RunCriterionOverride>? overrides = null)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        OutputPath = outputPath;
        Overrides = (overrides ?? []).ToImmutableArray();
        if (Overrides.Any(item => item is null))
        {
            throw new ArgumentException("Overrides cannot contain null values.", nameof(overrides));
        }
    }

    public ExecutionRunContext Context { get; }

    public string OutputPath { get; }

    public ImmutableArray<RunCriterionOverride> Overrides { get; }

    public override string ToString() =>
        $"{nameof(ResultsOutputRequest)} {{ OverrideCount = {Overrides.Length.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";
}

public sealed class ResultsOutputResult
{
    public ResultsOutputResult(string code, string? finalPath = null, string? causeCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        FinalPath = finalPath;
        CauseCode = causeCode;
    }

    public string Code { get; }

    public string? FinalPath { get; }

    public string? CauseCode { get; }

    public bool IsSuccess => string.Equals(
        Code,
        ResultsOutputStatusCodes.Success,
        StringComparison.Ordinal);

    public override string ToString() =>
        $"{nameof(ResultsOutputResult)} {{ Code = {Code}, CauseCode = {CauseCode ?? "<none>"}, Content = <redacted> }}";
}

// Presentation categories, not run/output status codes or conclusions inferred from scores.
// Missing durable rows are Unprocessed; otherwise technical errors precede cancellation,
// all-empty (ignoring zero-budget skips) precedes Success. Mixed successful/empty work is Success.
public enum ResultsRowStatus
{
    Success,
    Empty,
    Cancelled,
    // No committed durable row, or only undispatched legacy cancellation records.
    // A discarded in-progress durable row may have attempted work; it is not a saved result.
    Unprocessed,
    TechnicalError,
}

public sealed class ResultsRowScoreViewModel
{
    internal ResultsRowScoreViewModel(
        int sourceRowNumber,
        ResultsRowStatus status,
        string questionEarnedText,
        decimal? specialEarned,
        decimal? similarityPenalty,
        decimal? finalRaw,
        decimal? finalScore,
        IEnumerable<ResultChartQuestionValue>? chartValues = null)
    {
        SourceRowNumber = sourceRowNumber;
        Status = status;
        QuestionEarnedText = questionEarnedText;
        SpecialEarned = specialEarned;
        SimilarityPenalty = similarityPenalty;
        FinalRaw = finalRaw;
        FinalScore = finalScore;
        ChartValues = [.. chartValues ?? []];
    }

    // Per enabled snapshot question: the numeric values that the charts of FR-066 show.
    internal ImmutableArray<ResultChartQuestionValue> ChartValues { get; }

    public int SourceRowNumber { get; }

    public ResultsRowStatus Status { get; }

    public string StatusText => Status switch
    {
        ResultsRowStatus.Success => "成功",
        ResultsRowStatus.Empty => "回答空欄",
        ResultsRowStatus.Cancelled => "取消",
        ResultsRowStatus.Unprocessed => "未処理・未確定",
        ResultsRowStatus.TechnicalError => "技術エラー",
        _ => throw new InvalidOperationException("The result row status is invalid."),
    };

    public string QuestionEarnedText { get; }

    public decimal? SpecialEarned { get; }

    public decimal? SimilarityPenalty { get; }

    public decimal? FinalRaw { get; }

    public decimal? FinalScore { get; }

    public string SpecialEarnedText => Format(SpecialEarned);

    public string SimilarityPenaltyText => Format(SimilarityPenalty);

    public string FinalRawText => Format(FinalRaw);

    public string FinalScoreText => Format(FinalScore);

    public override string ToString() =>
        $"{nameof(ResultsRowScoreViewModel)} {{ SourceRowNumber = {SourceRowNumber.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";

    private static string Format(decimal? value) => value is decimal number
        ? number.ToString("G29", CultureInfo.InvariantCulture)
        : "—";
}

public interface IResultsOutputBoundary
{
    ResultsOutputPathAssessment AssessPath(string inputPath, string outputPath);

    // SUCCESS is the boundary's commit receipt. Prefer its nonblank FinalPath; for legacy
    // boundaries a null/blank FinalPath means the captured request.OutputPath, not a later edit.
    // Presentation consumes this receipt without probing whether a file exists.
    Task<ResultsOutputResult> ExportAsync(
        ResultsOutputRequest request,
        CancellationToken cancellationToken);
}

public sealed class ResultsOutputBoundary : IResultsOutputBoundary
{
    private readonly AtomicOutputCommitter committer;

    public ResultsOutputBoundary()
        : this(new AtomicOutputCommitter())
    {
    }

    public ResultsOutputBoundary(AtomicOutputCommitter committer)
    {
        this.committer = committer ?? throw new ArgumentNullException(nameof(committer));
    }

    public ResultsOutputPathAssessment AssessPath(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            return InvalidPath();
        }

        try
        {
            string canonicalInput = Path.GetFullPath(inputPath);
            string canonicalOutput = Path.GetFullPath(outputPath);
            if (!string.Equals(Path.GetExtension(canonicalOutput), ".xlsx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    canonicalInput,
                    canonicalOutput,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                return InvalidPath();
            }

            string? targetDirectory = Path.GetDirectoryName(canonicalOutput);
            if (targetDirectory is null || !Directory.Exists(targetDirectory))
            {
                return InvalidPath();
            }

            if (File.Exists(canonicalOutput) || Directory.Exists(canonicalOutput))
            {
                return new ResultsOutputPathAssessment(
                    ResultsOutputStatusCodes.TargetExists,
                    isValid: false,
                    targetExists: true);
            }

            return ResultsOutputPathAssessment.Valid;
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or IOException
            or UnauthorizedAccessException)
        {
            return InvalidPath();
        }
    }

    public Task<ResultsOutputResult> ExportAsync(
        ResultsOutputRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(
            () => ExportCore(request, cancellationToken),
            CancellationToken.None);
    }

    public override string ToString() =>
        $"{nameof(ResultsOutputBoundary)} {{ Content = <redacted> }}";

    private ResultsOutputResult ExportCore(
        ResultsOutputRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunSummary summary = request.Context.Summary;
            RunOutputPreparation preparation = summary.PrepareOutput(request.Overrides);
            if (!preparation.InputUnchanged)
            {
                return new ResultsOutputResult(ResultsOutputStatusCodes.InputChanged);
            }

            if (!preparation.OverrideErrors.IsEmpty)
            {
                return new ResultsOutputResult(ResultsOutputStatusCodes.OverrideInvalid);
            }

            ResultsOutputPathAssessment path = AssessPath(
                request.Context.InputPath,
                request.OutputPath);
            if (!path.IsValid)
            {
                return new ResultsOutputResult(path.Code);
            }

            using WorkingPackage package = WorkingPackage.Create(
                request.Context.InputPath,
                request.OutputPath);
            cancellationToken.ThrowIfCancellationRequested();
            AppOwnedSheetNames sheetNames;
            ConfigCellAddressMap config;
            ResultsSheetWriteResult results;
            using (SpreadsheetDocument document = package.OpenForEditing())
            {
                sheetNames = new AppOwnedSheetNameResolver().Resolve(document);
                config = new ConfigSheetWriter().Write(
                    document,
                    preparation.Snapshot,
                    sheetNames);
                new ReferenceAnswersSheetWriter().Write(
                    document,
                    preparation.Snapshot,
                    sheetNames,
                    CreateReferenceRows(request.Context));
                results = new ResultsSheetWriter().Write(
                    document,
                    preparation.Snapshot,
                    sheetNames,
                    config,
                    preparation.Rows);
                new RunSheetWriter().Write(
                    document,
                    CreateRunMetadata(request.Context, sheetNames));
                new CalculationPropertiesWriter().Write(document);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ImmutableArray<ExpectedFormulaCell> expectedFormulas = config.FormulaCells
                .Concat(results.FormulaCells)
                .Select(item => new ExpectedFormulaCell(item.Definition, item.CachedValue))
                .ToImmutableArray();
            OutputPackageValidationPlan validationPlan = OutputPackageValidationPlan.Capture(
                request.Context.InputPath,
                sheetNames,
                expectedFormulas);
            AtomicOutputCommitResult commit = committer.Commit(
                package,
                request.OutputPath,
                request.Context.InputPath,
                preparation.InputSnapshot,
                validationPlan,
                cancellationToken);
            return new ResultsOutputResult(
                commit.Code,
                commit.FinalPath,
                commit.CauseCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ResultsOutputResult(ResultsOutputStatusCodes.Cancelled);
        }
        catch
        {
            return new ResultsOutputResult(ResultsOutputStatusCodes.ExportFailed);
        }
    }

    private static RunSheetMetadata CreateRunMetadata(
        ExecutionRunContext context,
        AppOwnedSheetNames sheetNames)
    {
        RunSummary summary = context.Summary;
        EvaluationTokenUsage usage = summary.OperationTokenUsage;
        return new RunSheetMetadata
        {
            InputIdentity = summary.InputSnapshot,
            DefinitionSha256 = summary.DefinitionSha256,
            ApplicationIdentity = ApplicationIdentity(),
            CopilotSdkIdentity = "GitHub.Copilot.SDK/" + context.RuntimeIdentity.SdkInformationalVersion,
            CopilotCliIdentity = "copilot/" + context.RuntimeIdentity.CliVersion
                + ";sha256=" + context.RuntimeIdentity.CliSha256,
            ModelIdentity = context.ModelId,
            ReasoningEffort = context.ReasoningEffort,
            ContextTier = context.ContextTier,
            StartedAtUtc = summary.StartedAtUtc,
            EndedAtUtc = summary.EndedAtUtc,
            PlannedEvaluationCount = summary.PlannedOperationCount,
            CompletedEvaluationCount = summary.CompletedOperationCount,
            ErrorCount = summary.OperationFailureCount,
            UsageObservedUnitCount = summary.OperationUsageObservedCount,
            InputTokenCount = usage.InputTokens,
            OutputTokenCount = usage.OutputTokens,
            ReasoningTokenCount = usage.ReasoningTokens,
            CacheReadTokenCount = usage.CacheReadTokens,
            CacheWriteTokenCount = usage.CacheWriteTokens,
            SheetNames = sheetNames,
        };
    }

    private static IEnumerable<ReferenceAnswerSheetRow> CreateReferenceRows(ExecutionRunContext context)
    {
        RunSummary summary = context.Summary;
        Dictionary<string, CheckpointReference> references = summary.References
            .ToDictionary(reference => reference.QuestionId, StringComparer.Ordinal);
        foreach (QuestionDefinition question in summary.Snapshot.Definition.Questions.Where(question => question.Enabled))
        {
            references.TryGetValue(question.Id, out CheckpointReference? reference);
            yield return new ReferenceAnswerSheetRow
            {
                QuestionId = question.Id,
                // Reference answers use the run model and run-level effort (requirements §7.1).
                ModelId = context.ModelId,
                ReasoningEffort = context.ReasoningEffort,
                Answer = reference?.Answer,
                StatusCode = reference?.StatusCode ?? ResultsStatusCodes.AiRuntimeFailed,
                GeneratedAtUtc = reference?.GeneratedAtUtc ?? summary.EndedAtUtc,
            };
        }
    }

    private static string ApplicationIdentity()
    {
        Assembly assembly = typeof(ResultsOutputBoundary).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        string version = informationalVersion?.Split('+', 2)[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "unknown";
        return (assembly.GetName().Name ?? "StudyReportEvaluator.App") + "/" + version;
    }

    private static ResultsOutputPathAssessment InvalidPath() =>
        new(
            ResultsOutputStatusCodes.OutputPathInvalid,
            isValid: false,
            targetExists: false);
}

public sealed class ResultsCriterionViewModel : UiObservableObject
{
    internal const string AnswerLoadingText = "学生の回答を読み込んでいます…";
    internal const string AnswerInputChangedText =
        "入力Excelが実行時から変更されているため、学生の回答を表示できません。元のExcelまたは結果Excelで確認してください。";
    internal const string AnswerUnavailableText =
        "入力Excelを読み取れないため、学生の回答を表示できません（移動・削除・ほかのアプリで使用中など）。元のExcelまたは結果Excelで確認してください。";
    internal const string BlankCellText = "（空欄）";

    private readonly Action<ResultsCriterionViewModel> overrideChanged;
    private readonly CriterionQuantificationResult? acceptedResult;
    private string overrideText = string.Empty;
    private string? overrideError;
    private decimal? effectiveRaw;
    private decimal? normalizedScore;
    private decimal? evaluatorScore;
    private decimal? questionScore;
    private decimal? overallScore;
    private ResultsAnswerState studentAnswerState = ResultsAnswerState.Loading;
    private string studentAnswerText = AnswerLoadingText;

    internal ResultsCriterionViewModel(
        int sourceRowNumber,
        QuestionDefinition question,
        EvaluatorDefinition evaluator,
        CriterionDefinition criterion,
        bool canOverride,
        decimal? aiRawScore,
        string statusCode,
        Action<ResultsCriterionViewModel> overrideChanged,
        CriterionQuantificationResult? acceptedResult = null)
    {
        if (sourceRowNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRowNumber));
        }

        Question = question ?? throw new ArgumentNullException(nameof(question));
        Evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        Criterion = criterion ?? throw new ArgumentNullException(nameof(criterion));
        ArgumentException.ThrowIfNullOrWhiteSpace(statusCode);
        this.overrideChanged = overrideChanged
            ?? throw new ArgumentNullException(nameof(overrideChanged));
        SourceRowNumber = sourceRowNumber;
        CanOverride = canOverride;
        AiRawScore = aiRawScore;
        StatusCode = statusCode;
        Range = criterion.Range ?? evaluator.Range;
        this.acceptedResult = acceptedResult;
    }

    internal QuestionDefinition Question { get; }

    internal EvaluatorDefinition Evaluator { get; }

    internal CriterionDefinition Criterion { get; }

    public int SourceRowNumber { get; }

    public string QuestionId => Question.Id;

    public string EvaluatorId => Evaluator.Id;

    public string CriterionId => Criterion.Id;

    public string QuestionName => Question.DisplayName;

    public string QuestionText => Question.QuestionText;

    public string EvaluatorName => Evaluator.DisplayName;

    public string CriterionName => Criterion.DisplayName;

    public bool CanOverride { get; }

    public decimal? AiRawScore { get; }

    public string AiRawText => FormatScore(AiRawScore);

    public string StatusCode { get; }

    public ScoreRange Range { get; }

    public string RangeText =>
        $"{Range.Minimum.ToString(CultureInfo.InvariantCulture)} ～ {Range.Maximum.ToString(CultureInfo.InvariantCulture)}";

    public ResultsAnswerState StudentAnswerState => studentAnswerState;

    public string StudentAnswerText => studentAnswerText;

    // FR-RV-05: the same accepted values that the Results sheet writes to .Reason/.Evidence/.Evidence_Source.
    public string ReasonText => acceptedResult is { } result
        ? string.IsNullOrWhiteSpace(result.Reason) ? "（理由は空欄です）" : result.Reason
        : StatusCode switch
        {
            ResultsStatusCodes.Empty => "回答が空欄のため、AIで評価していません。",
            ResultsStatusCodes.Cancelled => "取消または未処理のため、評価していません。",
            _ => $"技術的な失敗（{StatusCode}）のため、評価結果はありません。",
        };

    public string EvidenceText => acceptedResult is { } result
        ? string.IsNullOrEmpty(result.Evidence) ? "（引用なし）" : result.Evidence
        : "—";

    public string EvidenceSourceText => acceptedResult is { } result
        ? result.EvidenceSource switch
        {
            EvidenceSourceKind.PrimaryAnswer => WithColumn("主回答", result.EvidenceSourceColumnId),
            EvidenceSourceKind.SupportingColumn => WithColumn("補助", result.EvidenceSourceColumnId),
            _ => "なし",
        }
        : "—";

    public string CriterionDescriptionText => string.IsNullOrWhiteSpace(Criterion.Description)
        ? "（説明は未設定です）"
        : Criterion.Description;

    internal IEnumerable<string> AnswerColumns => QuestionAnswerColumns(Question);

    public string OverrideText
    {
        get => overrideText;
        set
        {
            string next = value ?? string.Empty;
            if (!CanOverride && next.Length > 0)
            {
                return;
            }

            if (SetProperty(ref overrideText, next))
            {
                overrideChanged(this);
            }
        }
    }

    public string? OverrideError
    {
        get => overrideError;
        internal set
        {
            if (SetProperty(ref overrideError, value))
            {
                OnPropertyChanged(nameof(HasOverrideError));
            }
        }
    }

    public bool HasOverrideError => OverrideError is not null;

    public decimal? EffectiveRaw => effectiveRaw;

    public string EffectiveRawText => FormatScore(EffectiveRaw);

    public decimal? NormalizedScore => normalizedScore;

    public string NormalizedScoreText => FormatScore(NormalizedScore);

    public decimal? EvaluatorScore => evaluatorScore;

    public string EvaluatorScoreText => FormatScore(EvaluatorScore);

    public decimal? QuestionScore => questionScore;

    public string QuestionScoreText => FormatScore(QuestionScore);

    public decimal? OverallScore => overallScore;

    public string OverallScoreText => FormatScore(OverallScore);

    public string AutomationId =>
        $"Result-{SourceRowNumber.ToString(CultureInfo.InvariantCulture)}-{QuestionId}-{EvaluatorId}-{CriterionId}";

    public string AccessibleSummary =>
        $"row {SourceRowNumber.ToString(CultureInfo.InvariantCulture)}、{QuestionName}、{EvaluatorName}、{CriterionName}、status {StatusCode}";

    internal void SetPreview(
        decimal? nextEffectiveRaw,
        decimal? nextNormalizedScore,
        decimal? nextEvaluatorScore,
        decimal? nextQuestionScore,
        decimal? nextOverallScore)
    {
        SetPreviewProperty(ref effectiveRaw, nextEffectiveRaw, nameof(EffectiveRaw), nameof(EffectiveRawText));
        SetPreviewProperty(ref normalizedScore, nextNormalizedScore, nameof(NormalizedScore), nameof(NormalizedScoreText));
        SetPreviewProperty(ref evaluatorScore, nextEvaluatorScore, nameof(EvaluatorScore), nameof(EvaluatorScoreText));
        SetPreviewProperty(ref questionScore, nextQuestionScore, nameof(QuestionScore), nameof(QuestionScoreText));
        SetPreviewProperty(ref overallScore, nextOverallScore, nameof(OverallScore), nameof(OverallScoreText));
    }

    internal void SetStudentAnswer(
        ResultsAnswerState state,
        IReadOnlyDictionary<string, string?>? cells = null)
    {
        string text = state switch
        {
            ResultsAnswerState.Loaded => FormatAnswer(cells ?? throw new ArgumentNullException(nameof(cells))),
            ResultsAnswerState.InputChanged => AnswerInputChangedText,
            ResultsAnswerState.Unavailable => AnswerUnavailableText,
            _ => AnswerLoadingText,
        };
        SetProperty(ref studentAnswerState, state, nameof(StudentAnswerState));
        SetProperty(ref studentAnswerText, text, nameof(StudentAnswerText));
    }

    internal static IEnumerable<string> QuestionAnswerColumns(QuestionDefinition question) =>
        question.SupportingSourceColumns
            .Prepend(question.PrimarySourceColumn)
            .Select(column => column.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal);

    public override string ToString() =>
        $"{nameof(ResultsCriterionViewModel)} {{ SourceRowNumber = {SourceRowNumber.ToString(CultureInfo.InvariantCulture)}, StatusCode = {StatusCode}, Content = <redacted> }}";

    // FR-RV-02: one block per answer column, primary first, original text, blank as （空欄）.
    private string FormatAnswer(IReadOnlyDictionary<string, string?> cells)
    {
        string primary = Question.PrimarySourceColumn.ToUpperInvariant();
        return string.Join(
            "\n\n",
            AnswerColumns.Select(column =>
            {
                string kind = string.Equals(column, primary, StringComparison.Ordinal) ? "主回答" : "補助";
                string? value = cells.TryGetValue(column, out string? found) ? found : null;
                return $"[列 {column} · {kind}]\n{(string.IsNullOrWhiteSpace(value) ? BlankCellText : value)}";
            }));
    }

    private static string WithColumn(string label, string? column) =>
        string.IsNullOrWhiteSpace(column) ? label : $"{label}（列 {column}）";

    private void SetPreviewProperty(
        ref decimal? field,
        decimal? value,
        string propertyName,
        string textPropertyName)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(textPropertyName);
        }
    }

    private static string FormatScore(decimal? value) => value is decimal score
        ? score.ToString(CultureInfo.InvariantCulture)
        : "—";
}

public sealed class ResultsOutputViewModel : UiObservableObject, IDisposable
{
    public JobCostViewModel Cost { get; } = new();

    private const NumberStyles OverrideNumberStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    private readonly IResultsOutputBoundary outputBoundary;
    private readonly IResultsAnswerSource answerSource;
    private readonly Dictionary<int, ImmutableDictionary<string, string?>> answerCache = [];
    private readonly WeightedScoreCalculator scoreCalculator = new();
    private readonly ObservableCollection<ResultsCriterionViewModel> resultItems = [];
    private readonly ObservableCollection<ResultsRowScoreViewModel> rowScoreItems = [];
    private readonly ObservableCollection<ResultsRowScoreViewModel> visibleRowScoreItems = [];
    private readonly ObservableCollection<ResultsCriterionViewModel> selectedRowCriterionItems = [];
    private readonly ViewModelCommand exportCommand;
    private object? visualization;
    private readonly ViewModelCommand cancelExportCommand;
    private readonly ViewModelCommand previousPageCommand;
    private readonly ViewModelCommand nextPageCommand;
    private readonly ViewModelCommand goToRowCommand;
    private readonly ViewModelCommand nextOverrideErrorCommand;
    private readonly ViewModelCommand showDetailCommand;
    private readonly ViewModelCommand showListCommand;
    private readonly ViewModelCommand clearRowFilterCommand;
    private readonly List<ResultsRowScoreViewModel> filteredRowItems = [];
    private ResultsRowFilter? rowFilter;
    private ExecutionRunContext? context;
    private ResultsRowScoreViewModel? selectedRow;
    private ResultsCriterionViewModel? selectedCriterion;
    private int pageSize = 4;
    private int pageIndex;
    private int? goToRowNumber;
    private bool isDetailVisible;
    private bool updatingPresentation;
    private bool hasUnsavedOverrides;
    private long overrideRevision;
    private string outputPath = string.Empty;
    private string lastSuccessfulExportPath = string.Empty;
    private ResultsOutputPathAssessment pathAssessment = InvalidInitialPath();
    private int overrideErrorCount;
    private bool isExporting;
    private bool isExportCancelling;
    private string lastExportCode = ResultsOutputStatusCodes.Ready;
    private CancellationTokenSource? exportCancellation;
    private long exportSequence;
    private CancellationTokenSource? answerCancellation;
    private int? answerRequestRow;
    private long answerSequence;
    private Task pendingStudentAnswerTask = Task.CompletedTask;
    private bool disposed;

    public ResultsOutputViewModel()
        : this(new ResultsOutputBoundary())
    {
    }

    public ResultsOutputViewModel(
        IResultsOutputBoundary outputBoundary,
        ExecutionRunContext? context = null,
        IResultsAnswerSource? answerSource = null)
    {
        this.outputBoundary = outputBoundary
            ?? throw new ArgumentNullException(nameof(outputBoundary));
        this.answerSource = answerSource ?? new ResultsAnswerSource();
        Results = new ReadOnlyObservableCollection<ResultsCriterionViewModel>(resultItems);
        RowScores = new ReadOnlyObservableCollection<ResultsRowScoreViewModel>(rowScoreItems);
        VisibleRowScores = new ReadOnlyObservableCollection<ResultsRowScoreViewModel>(visibleRowScoreItems);
        SelectedRowCriteria = new ReadOnlyObservableCollection<ResultsCriterionViewModel>(selectedRowCriterionItems);
        exportCommand = new ViewModelCommand(
            _ => _ = ExportAsync(),
            _ => CanExport);
        cancelExportCommand = new ViewModelCommand(
            _ => CancelExport(),
            _ => CanCancelExport);
        previousPageCommand = new ViewModelCommand(
            _ => PageIndex--,
            _ => !disposed && PageIndex > 0);
        nextPageCommand = new ViewModelCommand(
            _ => PageIndex++,
            _ => !disposed && PageIndex < LastPageIndex);
        goToRowCommand = new ViewModelCommand(
            _ => SelectRowAcrossFilter(FindRow(GoToRowNumber)),
            _ => !disposed && FindRow(GoToRowNumber) is not null);
        nextOverrideErrorCommand = new ViewModelCommand(
            _ => SelectNextOverrideError(),
            _ => !disposed && HasOverrideErrors);
        showDetailCommand = new ViewModelCommand(
            _ => IsDetailVisible = true,
            _ => !disposed && SelectedRow is not null && !IsDetailVisible);
        showListCommand = new ViewModelCommand(
            _ => IsDetailVisible = false,
            _ => !disposed && IsDetailVisible);
        clearRowFilterCommand = new ViewModelCommand(
            _ => ClearRowFilter(),
            _ => !disposed && rowFilter is not null);
        Charts = new ResultsChartsViewModel(new ChartActions(this));
        visualization = Charts;
        UpdateCharts();
        if (context is not null)
        {
            Load(context);
        }
    }

    public ReadOnlyObservableCollection<ResultsCriterionViewModel> Results { get; }

    public ReadOnlyObservableCollection<ResultsRowScoreViewModel> RowScores { get; }

    public ReadOnlyObservableCollection<ResultsRowScoreViewModel> VisibleRowScores { get; }

    public ReadOnlyObservableCollection<ResultsCriterionViewModel> SelectedRowCriteria { get; }

    public ResultsCriterionViewModel? SelectedCriterion
    {
        get => selectedCriterion;
        set
        {
            if (updatingPresentation
                || (value is not null && !selectedRowCriterionItems.Contains(value)))
            {
                return;
            }

            SetProperty(ref selectedCriterion, value);
        }
    }

    public ResultsRowScoreViewModel? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (updatingPresentation || ReferenceEquals(selectedRow, value))
            {
                return;
            }

            if (value is null)
            {
                SetSelectedRow(null);
                return;
            }

            int index = CurrentRows.IndexOf(value);
            if (index < 0)
            {
                return;
            }

            if (SetProperty(ref pageIndex, index / PageSize, nameof(PageIndex)))
            {
                RefreshPresentation(value);
            }
            else
            {
                SetSelectedRow(value);
            }
        }
    }

    // The view may replace this provisional size with the number of rows that fit.
    public int PageSize
    {
        get => pageSize;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The page size must be positive.");
            }

            if (SetProperty(ref pageSize, value))
            {
                int selectedIndex = selectedRow is null ? -1 : CurrentRows.IndexOf(selectedRow);
                if (selectedIndex >= 0)
                {
                    SetProperty(ref pageIndex, selectedIndex / value, nameof(PageIndex));
                }

                RefreshPresentation(selectedRow);
            }
        }
    }

    public int PageIndex
    {
        get => pageIndex;
        set
        {
            if (SetProperty(ref pageIndex, Math.Clamp(value, 0, LastPageIndex)))
            {
                RefreshPresentation(selectedRow);
            }
        }
    }

    public string PageSummary => CurrentRows.Count == 0
        ? "結果はありません（0 行）"
        : rowFilter is null
            ? $"{(PageIndex * PageSize + 1).ToString("N0", CultureInfo.InvariantCulture)}–{(PageIndex * PageSize + VisibleRowScores.Count).ToString("N0", CultureInfo.InvariantCulture)} / {RowScores.Count.ToString("N0", CultureInfo.InvariantCulture)} 行"
            : $"{(PageIndex * PageSize + 1).ToString("N0", CultureInfo.InvariantCulture)}–{(PageIndex * PageSize + VisibleRowScores.Count).ToString("N0", CultureInfo.InvariantCulture)} / {CurrentRows.Count.ToString("N0", CultureInfo.InvariantCulture)} 行（絞り込み中・全 {RowScores.Count.ToString("N0", CultureInfo.InvariantCulture)} 行）";

    /// <summary>The 「4 結果」 charts (FR-066). Also the default <see cref="Visualization"/>.</summary>
    public ResultsChartsViewModel Charts { get; }

    /// <summary>The chart filter of the list (FR-069); paging uses the filtered rows; overrides are kept.</summary>
    public ResultsRowFilter? RowFilter => rowFilter;

    public bool IsRowFilterActive => rowFilter is not null;

    public string RowFilterText => rowFilter is { } filter
        ? $"絞り込み中: {filter.Label}（{filter.Count.ToString("N0", CultureInfo.InvariantCulture)} 行）"
        : string.Empty;

    /// <summary>Rows shown by the list pages: all rows, or the filtered rows.</summary>
    public IReadOnlyList<ResultsRowScoreViewModel> FilteredRowScores => rowFilter is null
        ? rowScoreItems
        : filteredRowItems;

    public ICommand ClearRowFilterCommand => clearRowFilterCommand;

    public int? GoToRowNumber
    {
        get => goToRowNumber;
        set
        {
            if (SetProperty(ref goToRowNumber, value))
            {
                goToRowCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsDetailVisible
    {
        get => isDetailVisible;
        private set
        {
            if (SetProperty(ref isDetailVisible, value))
            {
                RefreshStudentAnswers();
                RaiseCommandStates();
            }
        }
    }

    // Completes when the latest student-answer read has been applied or discarded.
    internal Task PendingStudentAnswerTask => pendingStudentAnswerTask;

    public bool HasUnsavedOverrides
    {
        get => hasUnsavedOverrides;
        private set => SetProperty(ref hasUnsavedOverrides, value);
    }

    public ICommand PreviousPageCommand => previousPageCommand;

    public ICommand NextPageCommand => nextPageCommand;

    public ICommand GoToRowCommand => goToRowCommand;

    public ICommand NextOverrideErrorCommand => nextOverrideErrorCommand;

    public ICommand ShowDetailCommand => showDetailCommand;

    public ICommand ShowListCommand => showListCommand;

    public bool IsLoaded => context is not null;

    /// <summary>
    /// Content of the 「図」 panel (FR-066): <see cref="Charts"/> by default, or its empty state while it is null.
    /// It must be built only from the loaded run (NFR-SEC-006).
    /// </summary>
    public object? Visualization
    {
        get => visualization;
        set
        {
            if (SetProperty(ref visualization, value))
            {
                OnPropertyChanged(nameof(HasVisualization));
            }
        }
    }

    public bool HasVisualization => visualization is not null;

    public bool IsPartial => context?.Summary.IsPartial == true;

    public bool IsAutomaticOutput => context?.Summary.IsDurable == true;

    public string FinalPath => context?.Summary.FinalPath ?? string.Empty;

    public string PartialPath => context?.Summary.PartialPath ?? string.Empty;

    // Empty until this loaded run receives an explicit export SUCCESS; Load clears the receipt.
    public string LastSuccessfulExportPath => lastSuccessfulExportPath;

    public bool HasSuccessfulExport => LastSuccessfulExportPath.Length > 0;

    public string LastSuccessfulExportText => HasSuccessfulExport
        ? $"保存済み修正版: {LastSuccessfulExportPath}"
        : string.Empty;

    public bool PartialCleanupFailed => context?.Summary.PartialCleanupFailed == true;

    public string DurableOutputText => context?.Summary switch
    {
        null => "run結果はありません。",
        { IsDurable: false } => "legacy manual export modeです。",
        { FinalPath: not null, PartialCleanupFailed: true } =>
            $"final: {FinalPath}{Environment.NewLine}partial cleanup warning: {PartialPath}",
        { FinalPath: not null } => $"final: {FinalPath}",
        _ => $"partial: {PartialPath}",
    };

    // The loaded run has an input snapshot and has not reported INPUT_CHANGED.
    // This is neither export readiness nor proof of a fresh/end-of-run filesystem check;
    // InputStateText states which verification stage the summary actually establishes.
    public bool IsInputUnchanged => context is { } run
        && run.Summary.StatusCode != QuantificationRunStatusCodes.InputChanged
        && run.Summary.FinalizationCode != ResultsOutputStatusCodes.InputChanged;

    public string RunIdentityText => context is { } run
        ? $"入力: {Path.GetFileName(run.InputPath)} · 開始: {run.Summary.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)} · 終了: {run.Summary.EndedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}"
        : string.Empty;

    // Display-only projection of the loaded job's cost record (§11.21); null cost is reported, not zeroed.
    public string RunMetricsText => context is { } run ? RunMetricsFormatter.DescribeRun(run.Cost) : string.Empty;

    public string RunMetricsToolTip => context is null
        ? string.Empty
        : RunMetricsText + Environment.NewLine + RunMetricsFormatter.AiCreditNote;

    public int PlannedEvaluationCount => context?.Summary.PlannedOperationCount ?? 0;

    public int CompletedEvaluationCount => context?.Summary.CompletedOperationCount ?? 0;

    public int FailureCount => context?.Summary.OperationFailureCount ?? 0;

    public int CancelledCount => context?.Summary.OperationCancelledCount ?? 0;

    public string RunSummaryText => context is null
        ? "完了または cancel 済みの run はありません。"
        : $"{CompletedEvaluationCount.ToString("N0", CultureInfo.InvariantCulture)} / {PlannedEvaluationCount.ToString("N0", CultureInfo.InvariantCulture)} 完了 · error {FailureCount.ToString("N0", CultureInfo.InvariantCulture)} · cancelled {CancelledCount.ToString("N0", CultureInfo.InvariantCulture)}";

    public string PartialResultText => IsPartial
        ? "部分結果です。完了済み学生行だけをpartial checkpointへ保存しています。"
        : "全operationの実行とfinalizationが終了しています。";

    public string InputStateText => context?.Summary switch
    {
        null => "入力不変性は未確認です。",
        _ when !IsInputUnchanged => "入力変更を検出しました（INPUT_CHANGED）。出力は停止されています。",
        { IsDurable: false } =>
            "run 終了時の exact hash / size / mtime は一致しています。final commit 直前にも再確認します。",
        { StatusCode: QuantificationRunStatusCodes.Success, FinalPath: not null,
            FinalizationCode: ResultsOutputStatusCodes.Success } =>
            "run終了時とfinal commit直前のexact hash / size / mtimeを確認しました。",
        { StatusCode: QuantificationRunStatusCodes.CheckpointFailed } =>
            "run開始時の入力snapshotは取得済みです。checkpoint保存に失敗したため、run終了時・final commit直前の不変性は未確認です。",
        { StatusCode: QuantificationRunStatusCodes.Cancelled, FinalizationCode: null } =>
            "run開始時の入力snapshotは取得済みです。取消による部分結果で、run終了時・final commit直前の不変性確認は未実施です。",
        { StatusCode: QuantificationRunStatusCodes.Cancelled } =>
            "run終了時のexact hash / size / mtimeは一致しています。final出力を取り消したため、commit直前の再確認・保存成功は未確認です。",
        { StatusCode: QuantificationRunStatusCodes.OutputInvalid } =>
            "run終了時のexact hash / size / mtimeは一致しています。final出力に失敗したため、commit直前の再確認・保存成功は未確認です。",
        _ => "run終了時のexact hash / size / mtimeは一致しています。final commit直前の再確認・保存成功は未確認です。",
    };

    public string OutputPath
    {
        get => outputPath;
        set
        {
            string next = value ?? string.Empty;
            if (SetProperty(ref outputPath, next))
            {
                LastExportCode = ResultsOutputStatusCodes.Ready;
                RefreshPathAssessment();
            }
        }
    }

    public int OverrideErrorCount
    {
        get => overrideErrorCount;
        private set
        {
            if (SetProperty(ref overrideErrorCount, value))
            {
                OnPropertiesChanged(
                    nameof(HasOverrideErrors),
                    nameof(OverrideErrorNavigationText),
                    nameof(OverrideValidationText),
                    nameof(CanExport));
                RaiseCommandStates();
            }
        }
    }

    public bool HasOverrideErrors => OverrideErrorCount > 0;

    public string OverrideErrorNavigationText =>
        $"エラー {OverrideErrorCount.ToString("N0", CultureInfo.InvariantCulture)} 件・次へ";

    public string OverrideValidationText => HasOverrideErrors
        ? $"override の技術検証エラー {OverrideErrorCount.ToString("N0", CultureInfo.InvariantCulture)} 件を修正してください。出力は停止されています。"
        : "override は snapshot の scorable flag と effective range に適合しています。";

    public bool IsOutputPathValid => pathAssessment.IsValid;

    public bool OutputTargetExists => pathAssessment.TargetExists;

    public string OutputPathStatusText => pathAssessment.Code switch
    {
        ResultsOutputStatusCodes.Ready => "新規 .xlsx として atomic commit できます。",
        ResultsOutputStatusCodes.TargetExists => "完成名が既に存在します。上書きせず、別の名前を指定してください。",
        _ => "既存 directory 内の、入力とは異なる新規 .xlsx path を指定してください。",
    };

    public bool IsExporting
    {
        get => isExporting;
        private set
        {
            if (SetProperty(ref isExporting, value))
            {
                OnPropertiesChanged(
                    nameof(CanExport),
                    nameof(CanCancelExport),
                    nameof(ExportStatusText));
                RaiseCommandStates();
            }
        }
    }

    public bool IsExportCancelling
    {
        get => isExportCancelling;
        private set
        {
            if (SetProperty(ref isExportCancelling, value))
            {
                OnPropertiesChanged(nameof(CanCancelExport), nameof(ExportStatusText));
                RaiseCommandStates();
            }
        }
    }

    public string LastExportCode
    {
        get => lastExportCode;
        private set
        {
            if (SetProperty(ref lastExportCode, value))
            {
                OnPropertyChanged(nameof(ExportStatusText));
            }
        }
    }

    public string ExportStatusText
    {
        get
        {
            if (IsAutomaticOutput
                && !IsExporting
                && !IsExportCancelling
                && string.Equals(LastExportCode, ResultsOutputStatusCodes.Ready, StringComparison.Ordinal))
            {
                return context?.Summary.FinalPath is not null
                    ? PartialCleanupFailed
                        ? "final workbookは有効です。partial cleanup warningを確認してください。"
                        : "検証済みfinal workbookを自動commitしました。"
                    : "final workbookは作成されていません。partial checkpointを確認してください。";
            }

            if (IsExportCancelling)
            {
                return "cancel を受け付けました。atomic rename の安全点を待っています。";
            }

            if (IsExporting)
            {
                return "working copy を write・flush・close・reopen・validate しています…";
            }

            return LastExportCode switch
            {
                ResultsOutputStatusCodes.Ready => "出力準備を確認してください。",
                ResultsOutputStatusCodes.Success => "検証済み workbook を完成名へ atomic commit しました。",
                ResultsOutputStatusCodes.InputChanged => "入力変更を検出したため、完成名は作成していません。",
                ResultsOutputStatusCodes.OutputInvalid => "再オープン検証に失敗したため、完成名は作成していません。",
                ResultsOutputStatusCodes.TargetExists => "完成名が既に存在するため、上書きしていません。",
                ResultsOutputStatusCodes.OverrideInvalid => "override が無効なため、出力していません。",
                ResultsOutputStatusCodes.Cancelled => "final commit 前に取り消しました。完成名は作成していません。",
                ResultsOutputStatusCodes.CleanupFailed => "一時 workbook の cleanup に失敗しました。状態を確認してください。",
                _ => "workbook を安全に出力できませんでした。状態を確認して再実行してください。",
            };
        }
    }

    public bool CanExport => !disposed
        && context is not null
        && context.Summary.PrepareOutput(CreateOverrides()).IsExportReady
        && !HasOverrideErrors
        && pathAssessment.IsValid
        && !IsExporting;

    public bool CanCancelExport => IsExporting && !IsExportCancelling;

    public ICommand ExportCommand => exportCommand;

    public ICommand CancelExportCommand => cancelExportCommand;

    public void Load(ExecutionRunContext runContext)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(runContext);
        Interlocked.Increment(ref exportSequence);
        CancelAnswerRead();
        answerCache.Clear();
        context = runContext;
        Cost.Reset();
        if (runContext.Cost is { } cost) Cost.Apply(cost);
        exportCancellation?.Cancel();
        exportCancellation?.Dispose();
        exportCancellation = null;
        IsExporting = false;
        IsExportCancelling = false;
        LastExportCode = ResultsOutputStatusCodes.Ready;
        SetLastSuccessfulExportPath(string.Empty);
        overrideRevision = 0;
        HasUnsavedOverrides = false;
        SetSelectedRow(null);
        SetProperty(ref pageIndex, 0, nameof(PageIndex));
        GoToRowNumber = null;
        rowFilter = null;
        filteredRowItems.Clear();
        resultItems.Clear();
        rowScoreItems.Clear();
        BuildResultItems(runContext.Summary);
        outputPath = runContext.Summary.IsDurable
            ? runContext.Summary.FinalPath ?? runContext.Summary.PartialPath ?? string.Empty
            : CreateDefaultOutputPath(runContext);
        OnPropertiesChanged(
            nameof(IsLoaded),
            nameof(IsPartial),
            nameof(IsAutomaticOutput),
            nameof(FinalPath),
            nameof(PartialPath),
            nameof(PartialCleanupFailed),
            nameof(DurableOutputText),
            nameof(IsInputUnchanged),
            nameof(RunIdentityText),
            nameof(RunMetricsText),
            nameof(RunMetricsToolTip),
            nameof(PlannedEvaluationCount),
            nameof(CompletedEvaluationCount),
            nameof(FailureCount),
            nameof(CancelledCount),
            nameof(RunSummaryText),
            nameof(PartialResultText),
            nameof(InputStateText),
            nameof(OutputPath),
            nameof(ExportStatusText),
            nameof(CanExport),
            nameof(RowFilter),
            nameof(IsRowFilterActive),
            nameof(RowFilterText));
        RevalidateOverridesAndPreview();
        RefreshPathAssessment();
    }

    public async Task ExportAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        RevalidateOverridesAndPreview();
        RefreshPathAssessment();
        if (!CanExport || context is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref exportSequence);
        ExecutionRunContext exportContext = context;
        string exportPath = OutputPath;
        ImmutableArray<RunCriterionOverride> exportOverrides = CreateOverrides();
        long exportOverrideRevision = overrideRevision;
        exportCancellation?.Cancel();
        exportCancellation?.Dispose();
        CancellationTokenSource currentCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        exportCancellation = currentCancellation;
        IsExportCancelling = false;
        IsExporting = true;
        LastExportCode = ResultsOutputStatusCodes.Ready;
        try
        {
            ResultsOutputResult result = await outputBoundary.ExportAsync(
                new ResultsOutputRequest(exportContext, exportPath, exportOverrides),
                currentCancellation.Token);
            if (sequence == Volatile.Read(ref exportSequence) && !disposed)
            {
                LastExportCode = result.Code;
                if (result.IsSuccess)
                {
                    SetLastSuccessfulExportPath(string.IsNullOrWhiteSpace(result.FinalPath)
                        ? exportPath
                        : result.FinalPath);
                    if (exportOverrideRevision == overrideRevision)
                    {
                        HasUnsavedOverrides = false;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (currentCancellation.IsCancellationRequested)
        {
            if (sequence == Volatile.Read(ref exportSequence) && !disposed)
            {
                LastExportCode = ResultsOutputStatusCodes.Cancelled;
            }
        }
        catch
        {
            if (sequence == Volatile.Read(ref exportSequence) && !disposed)
            {
                LastExportCode = ResultsOutputStatusCodes.ExportFailed;
            }
        }
        finally
        {
            currentCancellation.Dispose();
            if (sequence == Volatile.Read(ref exportSequence) && !disposed)
            {
                exportCancellation = null;
                IsExporting = false;
                IsExportCancelling = false;
                RefreshPathAssessment();
            }
        }
    }

    public void CancelExport()
    {
        if (!CanCancelExport)
        {
            return;
        }

        IsExportCancelling = true;
        exportCancellation?.Cancel();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Interlocked.Increment(ref exportSequence);
        exportCancellation?.Cancel();
        exportCancellation?.Dispose();
        CancelAnswerRead();
        answerCache.Clear();
        RaiseCommandStates();
    }

    public override string ToString() =>
        $"{nameof(ResultsOutputViewModel)} {{ IsLoaded = {IsLoaded}, ResultCount = {Results.Count.ToString(CultureInfo.InvariantCulture)}, LastExportCode = {LastExportCode}, Content = <redacted> }}";

    private void BuildResultItems(RunSummary summary)
    {
        foreach (EvaluationUnitResult unit in summary.Units.OrderBy(item => item.Item.SequenceNumber))
        {
            QuestionDefinition question = summary.Snapshot.Definition.Questions.Single(item =>
                item.Enabled
                && string.Equals(item.Id, unit.Item.QuestionId, StringComparison.Ordinal));
            EvaluatorDefinition evaluator = question.Evaluators.Single(item =>
                item.Enabled
                && string.Equals(item.Id, unit.Item.EvaluatorId, StringComparison.Ordinal));
            bool canOverride = QuestionIsScorable(
                summary,
                unit.Item.SourceRowNumber,
                question.Id);
            foreach (CriterionDefinition criterion in evaluator.Criteria.Where(item => item.Enabled))
            {
                CriterionQuantificationResult? criterionResult = unit.AcceptedResult?.Criteria.SingleOrDefault(
                    item => string.Equals(item.CriterionId, criterion.Id, StringComparison.Ordinal));
                resultItems.Add(new ResultsCriterionViewModel(
                    unit.Item.SourceRowNumber,
                    question,
                    evaluator,
                    criterion,
                    canOverride,
                    criterionResult?.RawScore,
                    unit.StatusCode,
                    OverrideChanged,
                    criterionResult));
            }
        }
    }

    private void OverrideChanged(ResultsCriterionViewModel item)
    {
        if (disposed || !resultItems.Contains(item))
        {
            return;
        }

        overrideRevision++;
        HasUnsavedOverrides = true;
        RevalidateOverridesAndPreview();
    }

    private void RevalidateOverridesAndPreview()
    {
        if (context is null)
        {
            OverrideErrorCount = 0;
            return;
        }

        ImmutableArray<RunCriterionOverride> overrides = CreateOverrides();
        RunOverrideValidationResult validation = context.Summary.ValidateOverrides(overrides);
        Dictionary<ResultKey, RunOverrideValidationError> errors = validation.Errors
            .Where(error => error.SourceRowNumber is not null)
            .GroupBy(error => new ResultKey(
                error.SourceRowNumber!.Value,
                error.QuestionId,
                error.EvaluatorId,
                error.CriterionId))
            .ToDictionary(group => group.Key, group => group.First());
        foreach (ResultsCriterionViewModel item in resultItems)
        {
            ResultKey key = Key(item);
            item.OverrideError = errors.TryGetValue(key, out RunOverrideValidationError? error)
                ? OverrideErrorMessage(error.Code)
                : null;
        }

        OverrideErrorCount = validation.Errors.Length;
        RecomputePreview();
        OnPropertyChanged(nameof(CanExport));
        RaiseCommandStates();
    }

    private void RecomputePreview()
    {
        if (context is null)
        {
            return;
        }

        RunSummary summary = context.Summary;
        QuantificationDefinition definition = summary.Snapshot.Definition;
        ILookup<int, EvaluationUnitResult> unitsByRow = summary.Units.ToLookup(
            unit => unit.Item.SourceRowNumber);
        Dictionary<int, CheckpointCompletedRow> completedRows = summary.CompletedRows.ToDictionary(
            row => row.SourceRowNumber);
        rowScoreItems.Clear();
        foreach (IGrouping<int, ResultsCriterionViewModel> sourceRow in resultItems.GroupBy(item => item.SourceRowNumber))
        {
            CheckpointCompletedRow? completedRow = completedRows.GetValueOrDefault(sourceRow.Key);
            ResultsRowStatus rowStatus = GetRowStatus(
                summary.IsDurable,
                unitsByRow[sourceRow.Key],
                completedRow);
            Dictionary<ResultKey, ResultsCriterionViewModel> rowItems = sourceRow.ToDictionary(Key);
            Dictionary<string, decimal?> evaluatorScores = new(StringComparer.Ordinal);
            Dictionary<string, decimal?> questionScores = new(StringComparer.Ordinal);
            foreach (QuestionDefinition question in definition.Questions.Where(item => item.Enabled))
            {
                foreach (EvaluatorDefinition evaluator in question.Evaluators.Where(item => item.Enabled))
                {
                    List<WeightedScoreInput> criterionInputs = [];
                    foreach (CriterionDefinition criterion in evaluator.Criteria.Where(item => item.Enabled))
                    {
                        ResultKey key = new(sourceRow.Key, question.Id, evaluator.Id, criterion.Id);
                        ResultsCriterionViewModel item = rowItems[key];
                        decimal? parsedOverride = ParseValidOverride(item);
                        EffectiveRawSelection effective = item.OverrideText.Length > 0
                            && item.HasOverrideError
                            ? new EffectiveRawSelection(null, EffectiveRawStatus.InvalidOverride)
                            : scoreCalculator.SelectEffectiveRaw(
                                item.CanOverride,
                                item.AiRawScore,
                                parsedOverride,
                                item.Range);
                        decimal? normalized = scoreCalculator.Normalize(
                            effective.Value,
                            item.Range,
                            definition.RoundingDigits);
                        item.SetPreview(effective.Value, normalized, null, null, null);
                        criterionInputs.Add(new WeightedScoreInput(normalized, criterion.Weight));
                    }

                    evaluatorScores[evaluator.Id] = scoreCalculator.Aggregate(
                        criterionInputs,
                        definition.RoundingDigits);
                }

                questionScores[question.Id] = scoreCalculator.Aggregate(
                    question.Evaluators
                        .Where(item => item.Enabled)
                        .Select(evaluator => new WeightedScoreInput(
                            evaluatorScores[evaluator.Id],
                            evaluator.Weight)),
                    definition.RoundingDigits);
            }

            decimal? legacyOverall = summary.IsDurable ? null : scoreCalculator.Aggregate(
                definition.Questions
                    .Where(item => item.Enabled && item.Points > 0m)
                    .Select(question => new WeightedScoreInput(
                        questionScores[question.Id],
                        question.Points)),
                definition.RoundingDigits);
            List<(string Label, decimal? Earned)> questionEarned = [];
            List<decimal?> similarityPenalties = [];
            List<decimal?> specialQuestionRates = [];
            List<ResultChartQuestionValue> chartValues = [];
            foreach (QuestionDefinition question in definition.Questions.Where(item => item.Enabled))
            {
                QuestionResultInput questionInput = CreateDisplayQuestionInput(
                    summary, sourceRow.Key, question, completedRow);
                EvaluationUnitResult[] questionUnits = unitsByRow[sourceRow.Key]
                    .Where(unit => string.Equals(unit.Item.QuestionId, question.Id, StringComparison.Ordinal))
                    .ToArray();
                // Workbook Answer_Present=0 also covers unknown/undispatched input. The UI
                // must not call that an earned zero; only completed, known input has a score.
                bool questionComplete = (!summary.IsDurable || completedRow is not null)
                    && questionUnits.Any(unit => unit.ScorableKnown)
                    && questionUnits.All(unit => unit.StatusCode != ResultsStatusCodes.Cancelled);
                decimal? rate = questionComplete
                    ? scoreCalculator.QuestionRate(questionInput.Scorable, questionScores[question.Id])
                    : null;
                decimal? earned = scoreCalculator.QuestionEarned(
                    rate,
                    question.Points,
                    definition.RoundingDigits);
                questionEarned.Add((QuestionLabel(question), earned));

                // Match ResultsSheetWriter: only a missing legacy object gets a default.
                // A durable CANCELLED/error object with AiRaw=null must stay unevaluated.
                decimal? similarity = questionInput.Similarity is { } similarityInput
                    ? similarityInput.AiRaw
                    : questionInput.Scorable ? null : 0m;
                similarityPenalties.Add(scoreCalculator.SimilarityPenalty(
                    question.Points,
                    similarity,
                    definition.SimilarityPenaltyWeight,
                    definition.RoundingDigits));
                chartValues.Add(new ResultChartQuestionValue(
                    earned,
                    questionInput.Similarity?.AiRaw,
                    similarityPenalties[^1],
                    questionInput.Similarity?.PeerMax,
                    questionInput.Similarity?.PeerRow));

                SpecialEvaluationDefinition[] enabledSpecials = question.SpecialEvaluations
                    .Where(special => special.Enabled)
                    .ToArray();
                if (definition.SpecialPoints > 0m && enabledSpecials.Length > 0)
                {
                    specialQuestionRates.Add(scoreCalculator.SpecialQuestionRate(
                        enabledSpecials.Select(special => questionInput.SpecialResults
                            .SingleOrDefault(result => string.Equals(
                                result.SpecialEvaluationId,
                                special.Id,
                                StringComparison.Ordinal))?.AiRaw),
                        definition.RoundingDigits));
                }
            }

            decimal? specialEarned = scoreCalculator.SpecialEarned(
                definition.SpecialPoints,
                specialQuestionRates,
                definition.RoundingDigits);
            decimal? finalRaw = scoreCalculator.FinalRaw(
                definition.BasePoints,
                questionEarned.Select(item => item.Earned),
                specialEarned,
                similarityPenalties,
                definition.RoundingDigits);
            decimal? finalScore = WeightedScoreCalculator.FinalScore(finalRaw);
            decimal? totalPenalty = similarityPenalties.Any(value => value is null)
                ? null
                : similarityPenalties.Sum(value => value!.Value);
            rowScoreItems.Add(new ResultsRowScoreViewModel(
                sourceRow.Key,
                rowStatus,
                string.Join(
                    " · ",
                    questionEarned.Select(item =>
                        $"{item.Label}: {(item.Earned?.ToString("G29", CultureInfo.InvariantCulture) ?? "—")}")),
                specialEarned,
                totalPenalty,
                finalRaw,
                finalScore,
                chartValues));

            decimal? overall = summary.IsDurable ? finalScore : legacyOverall;
            foreach (ResultsCriterionViewModel item in sourceRow)
            {
                item.SetPreview(
                    item.EffectiveRaw,
                    item.NormalizedScore,
                    evaluatorScores[item.EvaluatorId],
                    questionScores[item.QuestionId],
                    overall);
            }
        }

        RebuildFilteredRows();
        RefreshPresentation(selectedRow);
        UpdateCharts();
    }

    // FR-RS-02/03: the snapshot question text (Excel header cell) with whitespace collapsed;
    // blank text falls back to the display name, then the ID.
    internal static string QuestionLabel(QuestionDefinition question)
    {
        foreach (string candidate in new[] { question.QuestionText, question.DisplayName })
        {
            string collapsed = string.Join(
                ' ',
                (candidate ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (collapsed.Length > 0)
            {
                return collapsed;
            }
        }

        return question.Id;
    }

    private static QuestionResultInput CreateDisplayQuestionInput(
        RunSummary summary,
        int sourceRowNumber,
        QuestionDefinition question,
        CheckpointCompletedRow? completedRow)
    {
        // Project only score-bearing fields from the same inputs as RunSummary.BuildRows.
        // Do not change the run status or borrow export readiness for display; all arithmetic
        // still uses the ResultsSheetWriter's WeightedScoreCalculator path above.
        CheckpointSimilarityResult? similarity = completedRow?.SimilarityResults.SingleOrDefault(result =>
            string.Equals(result.QuestionId, question.Id, StringComparison.Ordinal));
        RunPeerSimilarityResult? peer = summary.PeerSimilarities.SingleOrDefault(result =>
            result.SourceRowNumber == sourceRowNumber
            && string.Equals(result.QuestionId, question.Id, StringComparison.Ordinal));
        return new QuestionResultInput
        {
            QuestionId = question.Id,
            Scorable = QuestionIsScorable(summary, sourceRowNumber, question.Id),
            SpecialResults = summary.IsDurable
                ? question.SpecialEvaluations.Where(special => special.Enabled).Select(special =>
                {
                    CheckpointSpecialResult? result = completedRow?.SpecialResults.SingleOrDefault(result =>
                        string.Equals(result.QuestionId, question.Id, StringComparison.Ordinal)
                        && string.Equals(result.SpecialEvaluationId, special.Id, StringComparison.Ordinal));
                    return new SpecialResultInput
                    {
                        SpecialEvaluationId = special.Id,
                        AiRaw = result?.AcceptedResult?.Score
                            ?? (result?.StatusCode == ResultsStatusCodes.Empty ? 0m : null),
                        Status = result?.StatusCode ?? ResultsStatusCodes.Cancelled,
                    };
                }).ToImmutableArray()
                : [],
            Similarity = summary.IsDurable
                ? new SimilarityResultInput
                {
                    AiRaw = similarity?.AcceptedResult?.Similarity
                        ?? (similarity?.StatusCode == ResultsStatusCodes.Empty ? 0m : null),
                    Status = similarity?.StatusCode ?? ResultsStatusCodes.Cancelled,
                    PeerMax = peer?.PeerMax,
                    PeerRow = peer?.PeerRow,
                }
                : null,
        };
    }

    private static ResultsRowStatus GetRowStatus(
        bool isDurable,
        IEnumerable<EvaluationUnitResult> sourceUnits,
        CheckpointCompletedRow? completedRow)
    {
        if (isDurable && completedRow is null)
        {
            return ResultsRowStatus.Unprocessed;
        }

        EvaluationUnitResult[] units = sourceUnits.ToArray();
        string[] statuses = completedRow is null
            ? units.Select(unit => unit.StatusCode).ToArray()
            : completedRow.NormalResults.Select(result => result.StatusCode)
                .Concat(completedRow.SpecialResults.Select(result => result.StatusCode))
                .Concat(completedRow.SimilarityResults.Select(result => result.StatusCode))
                .ToArray();
        if (statuses.Length == 0)
        {
            return ResultsRowStatus.Unprocessed;
        }

        if (statuses.Any(status => status is not ResultsStatusCodes.Success
            and not ResultsStatusCodes.Empty
            and not ResultsStatusCodes.Cancelled
            and not ResultsStatusCodes.NotRunZeroBudget))
        {
            return ResultsRowStatus.TechnicalError;
        }

        if (statuses.Contains(ResultsStatusCodes.Cancelled, StringComparer.Ordinal))
        {
            return completedRow is null && units.All(unit =>
                unit.StatusCode == ResultsStatusCodes.Cancelled
                && unit.AttemptCount == 0 && !unit.ScorableKnown)
                ? ResultsRowStatus.Unprocessed
                : ResultsRowStatus.Cancelled;
        }

        return statuses.Contains(ResultsStatusCodes.Empty, StringComparer.Ordinal)
            && statuses.All(status => status is ResultsStatusCodes.Empty or ResultsStatusCodes.NotRunZeroBudget)
                ? ResultsRowStatus.Empty
                : ResultsRowStatus.Success;
    }

    private void SetLastSuccessfulExportPath(string value)
    {
        if (SetProperty(ref lastSuccessfulExportPath, value, nameof(LastSuccessfulExportPath)))
        {
            OnPropertiesChanged(nameof(HasSuccessfulExport), nameof(LastSuccessfulExportText));
        }
    }

    private int LastPageIndex => CurrentRows.Count == 0 ? 0 : (CurrentRows.Count - 1) / PageSize;

    private IList<ResultsRowScoreViewModel> CurrentRows => rowFilter is null ? rowScoreItems : filteredRowItems;

    private ResultsRowScoreViewModel? FindRow(int? sourceRowNumber) => sourceRowNumber is > 0
        ? rowScoreItems.FirstOrDefault(row => row.SourceRowNumber == sourceRowNumber.Value)
        : null;

    // Explicit moves to a row (go to row, next override error) reach every row: a chart filter
    // that hides the target is cleared first, so the move never silently fails.
    private void SelectRowAcrossFilter(ResultsRowScoreViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (rowFilter is { } filter && !filter.Contains(row.SourceRowNumber))
        {
            ClearRowFilter();
            row = FindRow(row.SourceRowNumber);
        }

        SelectedRow = row;
    }

    private void RebuildFilteredRows()
    {
        filteredRowItems.Clear();
        if (rowFilter is { } filter)
        {
            filteredRowItems.AddRange(rowScoreItems.Where(row => filter.Contains(row.SourceRowNumber)));
        }
    }

    internal void ApplyRowFilter(ResultsRowFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (disposed || context is null)
        {
            return;
        }

        rowFilter = filter;
        RebuildFilteredRows();
        ChangeRowSet();
    }

    internal void ClearRowFilter()
    {
        if (disposed || rowFilter is null)
        {
            return;
        }

        rowFilter = null;
        filteredRowItems.Clear();
        ChangeRowSet();
    }

    // Keep the selected row when it is still listed (on its page), otherwise select the first row.
    private void ChangeRowSet()
    {
        ResultsRowScoreViewModel? keep = selectedRow is { } current
            ? CurrentRows.FirstOrDefault(row => row.SourceRowNumber == current.SourceRowNumber)
            : null;
        int index = keep is null ? 0 : CurrentRows.IndexOf(keep);
        SetProperty(ref pageIndex, index / PageSize, nameof(PageIndex));
        RefreshPresentation(keep);
        OnPropertiesChanged(nameof(RowFilter), nameof(IsRowFilterActive), nameof(RowFilterText), nameof(FilteredRowScores));
        UpdateCharts();
        RaiseCommandStates();
    }

    private void UpdateCharts()
    {
        if (context is null)
        {
            Charts.Update(null, null, null);
            return;
        }

        ResultChartDataSet data = new(
            context.Summary.Snapshot.Definition.Questions
                .Where(question => question.Enabled)
                .Select(question => new ResultChartQuestion(question.Id, QuestionLabel(question), question.Points)),
            rowScoreItems.Select(row => new ResultChartRow(row.SourceRowNumber, row.FinalScore, row.ChartValues)),
            // The result screen shows completed or partial runs only, never live preview values (FR-046).
            isProvisional: false);
        Charts.Update(data, rowFilter, selectedRow?.SourceRowNumber);
    }

    private void OpenRowFromChart(int sourceRow, string? questionId)
    {
        if (disposed || CurrentRows.FirstOrDefault(row => row.SourceRowNumber == sourceRow) is not { } row)
        {
            return;
        }

        SelectedRow = row;
        if (questionId is not null
            && selectedRowCriterionItems.FirstOrDefault(item =>
                string.Equals(item.QuestionId, questionId, StringComparison.Ordinal)) is { } criterion)
        {
            SelectedCriterion = criterion;
        }

        if (showDetailCommand.CanExecute(null))
        {
            showDetailCommand.Execute(null);
        }
    }

    private sealed class ChartActions(ResultsOutputViewModel owner) : IResultsChartActions
    {
        public void ApplyRowFilter(ResultsRowFilter filter) => owner.ApplyRowFilter(filter);

        public void ClearRowFilter() => owner.ClearRowFilter();

        public void OpenRow(int sourceRow, string? questionId) => owner.OpenRowFromChart(sourceRow, questionId);
    }

    private void SelectNextOverrideError()
    {
        int start = selectedCriterion is null ? -1 : resultItems.IndexOf(selectedCriterion);
        ResultsCriterionViewModel? target = resultItems.Skip(start + 1)
            .Concat(resultItems.Take(start + 1))
            .FirstOrDefault(item => item.HasOverrideError);
        if (target is null)
        {
            return;
        }

        SelectRowAcrossFilter(FindRow(target.SourceRowNumber));
        SelectedCriterion = target;
        IsDetailVisible = true;
        // Revisit a single remaining error even if its criterion is already selected.
        OnPropertyChanged(nameof(SelectedCriterion));
    }

    private void RefreshPresentation(ResultsRowScoreViewModel? preferredSelection)
    {
        updatingPresentation = true;
        try
        {
            SetProperty(ref pageIndex, Math.Clamp(pageIndex, 0, LastPageIndex), nameof(PageIndex));
            visibleRowScoreItems.Clear();
            foreach (ResultsRowScoreViewModel row in CurrentRows.Skip(PageIndex * PageSize).Take(PageSize))
            {
                visibleRowScoreItems.Add(row);
            }

            SetSelectedRow(visibleRowScoreItems.FirstOrDefault(row =>
                row.SourceRowNumber == preferredSelection?.SourceRowNumber)
                ?? visibleRowScoreItems.FirstOrDefault());
        }
        finally
        {
            updatingPresentation = false;
        }

        OnPropertyChanged(nameof(PageSummary));
        RaiseCommandStates();
    }

    private void SetSelectedRow(ResultsRowScoreViewModel? value)
    {
        bool rowChanged = selectedRow?.SourceRowNumber != value?.SourceRowNumber;
        selectedRow = value;
        if (rowChanged)
        {
            selectedRowCriterionItems.Clear();
            foreach (ResultsCriterionViewModel item in resultItems.Where(item =>
                         item.SourceRowNumber == value?.SourceRowNumber))
            {
                selectedRowCriterionItems.Add(item);
            }

            SetProperty(ref selectedCriterion, selectedRowCriterionItems.FirstOrDefault(), nameof(SelectedCriterion));
        }

        if (value is null)
        {
            IsDetailVisible = false;
        }

        RefreshStudentAnswers();
        Charts.SetSelection(value?.SourceRowNumber);

        // Reassert selection after the visible collection resets, even when the row reference survives.
        // Do not reset the criterion editors for a score refresh on the same source row.
        OnPropertyChanged(nameof(SelectedRow));
        RaiseCommandStates();
    }

    // FR-RV-03/04, NFR-RV-01: read only the selected row while the detail is visible;
    // remember successful rows for this result and apply only the latest request.
    private void RefreshStudentAnswers()
    {
        if (disposed || context is null || !isDetailVisible || selectedRow is null)
        {
            CancelAnswerRead();
            return;
        }

        int rowNumber = selectedRow.SourceRowNumber;
        if (answerCache.TryGetValue(rowNumber, out ImmutableDictionary<string, string?>? cached))
        {
            CancelAnswerRead();
            ApplyStudentAnswers(rowNumber, ResultsAnswerState.Loaded, cached);
            return;
        }

        if (answerCancellation is not null && answerRequestRow == rowNumber)
        {
            return;
        }

        CancelAnswerRead();
        ApplyStudentAnswers(rowNumber, ResultsAnswerState.Loading, null);
        string[] columns = context.Summary.Snapshot.Definition.Questions
            .Where(question => question.Enabled)
            .SelectMany(ResultsCriterionViewModel.QuestionAnswerColumns)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (columns.Length == 0)
        {
            ApplyStudentAnswers(rowNumber, ResultsAnswerState.Unavailable, null);
            return;
        }

        ResultsAnswerRequest request = new(
            context.InputPath,
            context.Summary.InputSnapshot,
            context.Summary.Snapshot.Definition.SourceSheet,
            rowNumber,
            columns);
        CancellationTokenSource cancellation = new();
        long sequence = Interlocked.Increment(ref answerSequence);
        answerCancellation = cancellation;
        answerRequestRow = rowNumber;
        pendingStudentAnswerTask = ReadStudentAnswersAsync(request, sequence, cancellation);
    }

    private async Task ReadStudentAnswersAsync(
        ResultsAnswerRequest request,
        long sequence,
        CancellationTokenSource cancellation)
    {
        ResultsAnswerReadResult result;
        try
        {
            result = await answerSource.ReadAsync(request, cancellation.Token)
                ?? ResultsAnswerReadResult.Unavailable;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            result = ResultsAnswerReadResult.Unavailable;
        }

        if (disposed || sequence != Interlocked.Read(ref answerSequence))
        {
            return;
        }

        answerCancellation = null;
        answerRequestRow = null;
        if (result.State == ResultsAnswerState.Loaded)
        {
            answerCache[request.SourceRowNumber] = result.Cells;
            ApplyStudentAnswers(request.SourceRowNumber, ResultsAnswerState.Loaded, result.Cells);
        }
        else
        {
            ApplyStudentAnswers(
                request.SourceRowNumber,
                result.State == ResultsAnswerState.InputChanged
                    ? ResultsAnswerState.InputChanged
                    : ResultsAnswerState.Unavailable,
                null);
        }
    }

    private void CancelAnswerRead()
    {
        Interlocked.Increment(ref answerSequence);
        CancellationTokenSource? previous = answerCancellation;
        answerCancellation = null;
        answerRequestRow = null;
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ApplyStudentAnswers(
        int sourceRowNumber,
        ResultsAnswerState state,
        IReadOnlyDictionary<string, string?>? cells)
    {
        foreach (ResultsCriterionViewModel item in resultItems.Where(item => item.SourceRowNumber == sourceRowNumber))
        {
            item.SetStudentAnswer(state, cells);
        }
    }

    private void RefreshPathAssessment()
    {
        pathAssessment = context is null
            ? InvalidInitialPath()
            : SafeAssessPath(context.InputPath, OutputPath);
        OnPropertiesChanged(
            nameof(IsOutputPathValid),
            nameof(OutputTargetExists),
            nameof(OutputPathStatusText),
            nameof(CanExport));
        RaiseCommandStates();
    }

    private ResultsOutputPathAssessment SafeAssessPath(string inputPath, string candidateOutputPath)
    {
        try
        {
            return outputBoundary.AssessPath(inputPath, candidateOutputPath)
                ?? InvalidInitialPath();
        }
        catch
        {
            return InvalidInitialPath();
        }
    }

    private ImmutableArray<RunCriterionOverride> CreateOverrides() => resultItems
        .Where(item => item.OverrideText.Length > 0)
        .Select(item => new RunCriterionOverride
        {
            SourceRowNumber = item.SourceRowNumber,
            QuestionId = item.QuestionId,
            EvaluatorId = item.EvaluatorId,
            CriterionId = item.CriterionId,
            Value = item.OverrideText,
        })
        .ToImmutableArray();

    private static bool QuestionIsScorable(
        RunSummary summary,
        int sourceRowNumber,
        string questionId)
    {
        bool? known = null;
        foreach (EvaluationUnitResult unit in summary.Units.Where(item =>
                     item.Item.SourceRowNumber == sourceRowNumber
                     && string.Equals(item.Item.QuestionId, questionId, StringComparison.Ordinal)
                     && item.ScorableKnown))
        {
            known ??= unit.Scorable;
            if (known.Value != unit.Scorable)
            {
                return false;
            }
        }

        return known ?? false;
    }

    private static decimal? ParseValidOverride(ResultsCriterionViewModel item)
    {
        if (item.OverrideText.Length == 0 || item.HasOverrideError)
        {
            return null;
        }

        return decimal.TryParse(
            item.OverrideText,
            OverrideNumberStyles,
            CultureInfo.InvariantCulture,
            out decimal value)
            ? value
            : null;
    }

    private static string OverrideErrorMessage(string code) => code switch
    {
        "OVERRIDE_NOT_NUMERIC" => "数値を入力するか空欄にしてください。",
        "OVERRIDE_OUT_OF_RANGE" => "snapshot の effective range 内で入力してください。",
        "OVERRIDE_NOT_ALLOWED" => "主回答が空欄または未完了のため override できません。",
        _ => "この override は技術検証を通過できません。",
    };

    private static string CreateDefaultOutputPath(ExecutionRunContext runContext)
    {
        string directory = Path.GetDirectoryName(runContext.InputPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(runContext.InputPath);
        string timestamp = runContext.Summary.EndedAtUtc.ToLocalTime()
            .ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(directory, $"{stem}_quantified_{timestamp}.xlsx");
    }

    private static ResultKey Key(ResultsCriterionViewModel item) =>
        new(
            item.SourceRowNumber,
            item.QuestionId,
            item.EvaluatorId,
            item.CriterionId);

    private static ResultsOutputPathAssessment InvalidInitialPath() =>
        new(
            ResultsOutputStatusCodes.OutputPathInvalid,
            isValid: false,
            targetExists: false);

    private void RaiseCommandStates()
    {
        exportCommand.RaiseCanExecuteChanged();
        cancelExportCommand.RaiseCanExecuteChanged();
        previousPageCommand.RaiseCanExecuteChanged();
        nextPageCommand.RaiseCanExecuteChanged();
        goToRowCommand.RaiseCanExecuteChanged();
        nextOverrideErrorCommand.RaiseCanExecuteChanged();
        showDetailCommand.RaiseCanExecuteChanged();
        showListCommand.RaiseCanExecuteChanged();
        clearRowFilterCommand.RaiseCanExecuteChanged();
    }

    private readonly record struct ResultKey(
        int SourceRowNumber,
        string QuestionId,
        string EvaluatorId,
        string CriterionId);
}