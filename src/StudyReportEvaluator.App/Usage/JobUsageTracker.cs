using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Logging;

namespace StudyReportEvaluator.App.Usage;

/// <summary>Job-local observations, independent of accepted results and persisted workbook rows.</summary>
public sealed class JobUsageTracker : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, AttemptUsageSnapshot> _attempts = [];
    private readonly HashSet<Guid> _reportedOutcomes = [];
    private readonly Queue<string> _recent = new();
    private readonly Guid _jobId = Guid.NewGuid();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private DateTimeOffset? _observedAtUtc;
    private DateTimeOffset? _endedAtUtc;
    private readonly Action<JobCostSnapshot>? _observer;
    private readonly Channel<bool> _notifications = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly JobCostLogger _logger;
    private readonly JobUsageContext _context;
    private long _revision;
    private bool _finished;
    private JobCostCompletion _completion;
    private Task? _completeTask;

    public JobUsageTracker(Action<JobCostSnapshot>? observer = null, string? logDirectory = null,
        JobUsageContext? context = null)
    {
        _observer = observer;
        _context = context ?? new JobUsageContext();
        _logger = new JobCostLogger(_jobId, logDirectory, LogChanged);
        lock (_gate)
        {
            Record(JobCostEvent.JobStarted);
        }

        _logger.Start();
        _ = Task.Run(NotifyAsync);
    }

    public JobCostSnapshot Snapshot
    {
        get { lock (_gate) { return CreateSnapshot(); } }
    }

    /// <summary>Reads only this tracker's generated file, never a caller-supplied path.</summary>
    public Task<JobCostLogReadResult> ReadLogAsync() => _logger.ReadAsync();

    /// <summary>Called immediately before send; each retry gets a new generated identity.</summary>
    public Guid BeginAttempt(UsageOperation operation)
    {
        lock (_gate)
        {
            if (_finished) { return Guid.Empty; }
            if (!Enum.IsDefined(operation)) { operation = UsageOperation.Normal; }
            Guid id = Guid.NewGuid();
            var attempt = new AttemptUsageSnapshot(id, operation, 0, new UsageMetrics(), UsageSource.None);
            _attempts.Add(id, attempt);
            Record(JobCostEvent.AttemptStarted, attempt);
            return id;
        }
    }

    /// <summary>Replaces an attempt's cumulative observation; never adds old and new snapshots.</summary>
    public void ReplaceAttempt(AttemptUsageSnapshot observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(observation.Metrics);
        lock (_gate)
        {
            if (_finished || !_attempts.TryGetValue(observation.AttemptId, out var previous)
                || previous.IsFinished || observation.Revision <= previous.Revision) { return; }
            UsageMetrics metrics = UsageMath.Sanitize(observation.Metrics, out bool invalid);
            var models = ImmutableArray.CreateBuilder<ModelUsageSnapshot>();
            bool modelsTruncated = observation.ModelsTruncated;
            if (!observation.Models.IsDefaultOrEmpty)
            {
                modelsTruncated |= observation.Models.Length > ModelUsageSnapshot.MaximumModels;
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var model in observation.Models.Take(ModelUsageSnapshot.MaximumModels))
                {
                    if (model?.Metrics is null) { modelsTruncated = true; continue; }
                    string key = ModelUsageSnapshot.SafeKey(model.ModelKey);
                    if (!keys.Add(key)) { modelsTruncated = true; continue; }
                    UsageMetrics modelMetrics = UsageMath.Sanitize(model.Metrics, out bool modelInvalid);
                    models.Add(model with
                    {
                        ModelKey = key,
                        Metrics = modelMetrics,
                        IsPartial = model.IsPartial || modelInvalid || !observation.IsFinished
                            || observation.Source != UsageSource.FinalRpc,
                    });
                }
            }
            observation = observation with
            {
                HasDownwardCorrection = previous.HasDownwardCorrection
                    || UsageMath.HasDecrease(previous.Metrics, metrics),
                Operation = previous.Operation,
                Metrics = metrics,
                Source = Enum.IsDefined(observation.Source) ? observation.Source : UsageSource.None,
                Status = Enum.IsDefined(observation.Status) ? observation.Status : UsageObservationStatus.Pending,
                HasInvalidValues = observation.HasInvalidValues || invalid,
                Models = models.ToImmutable(),
                ModelsTruncated = modelsTruncated,
                RequestedModelKey = observation.RequestedModelKey is null ? null
                    : ModelUsageSnapshot.SafeKey(observation.RequestedModelKey),
                // Persist only safe effort identifiers the app sends, never arbitrary caller text.
                RequestedReasoningEffort = ReasoningEffortPolicy.IsSafeReasoningEffort(observation.RequestedReasoningEffort)
                    ? observation.RequestedReasoningEffort
                    : null,
                // Null stays null: an absent map identifies a legacy caller, not a complete one.
                MetricProvenance = observation.MetricProvenance is null ? null
                    : UsageProvenance.Sanitize(metrics, observation.MetricProvenance),
            };
            observation = observation with { ModelCostComparison = UsageProvenance.SanitizeComparison(observation) };
            _attempts[observation.AttemptId] = observation;
            _observedAtUtc = DateTimeOffset.UtcNow;
            Record(JobCostEvent.UsageUpdated, observation);
        }
    }

    /// <summary>Records evaluation completion independently of numeric observation completion.</summary>
    public void RecordAttemptOutcome(Guid attemptId, int attemptNumber, Guid operationId, UsageAttemptOutcome outcome)
    {
        lock (_gate)
        {
            if (_finished || attemptNumber < 1 || operationId == Guid.Empty
                || !_attempts.TryGetValue(attemptId, out var attempt)
                || !_reportedOutcomes.Add(attemptId)) { return; }
            // Do not advance the observation revision, timestamp, status or numeric values.
            Record(JobCostEvent.AttemptFinished, attempt, attemptNumber: attemptNumber,
                operationId: operationId, outcome: Enum.IsDefined(outcome) ? outcome : UsageAttemptOutcome.Unknown);
        }
    }

    public Task CompleteAsync(string statusCode)
    {
        lock (_gate)
        {
            if (_completeTask is not null) { return _completeTask; }
            _finished = true;
            _endedAtUtc = DateTimeOffset.UtcNow;
            // Never retain or log caller text, even if it is an exception/path/credential.
            _completion = statusCode switch
            {
                "Completed" or "completed" or "Succeeded" or "Success" or "SUCCESS" or "COMPLETED" => JobCostCompletion.Completed,
                "Cancelled" or "Canceled" or "cancelled" or "canceled" or "CANCELLED" or "CANCELED" => JobCostCompletion.Cancelled,
                "TimedOut" or "Timeout" or "timeout" or "AI_TIMEOUT" or "TIMEOUT" => JobCostCompletion.TimedOut,
                "Failed" or "failed" or "FAILED" or "AI_RUNTIME_FAILED" or "OUTPUT_FAILED" or "CLEANUP_FAILED"
                    or "NETWORK_FAILED" or "RATE_LIMITED" or "QUOTA_EXHAUSTED"
                    or "AUTH_REQUIRED" or "AI_OUTPUT_INVALID"
                    or "RUN_FAILED" or "OUTPUT_INVALID" or "CHECKPOINT_SAVE_FAILED" or "INPUT_CHANGED" => JobCostCompletion.Failed,
                "Disposed" => JobCostCompletion.Disposed,
                _ => JobCostCompletion.Unknown,
            };
            JobCostLogEntry final = Record(JobCostEvent.JobFinished, enqueue: false);
            // Avoid running completion/observers inline under the state lock.
            _completeTask = Task.Run(() => FinishAsync(final));
            return _completeTask;
        }
    }

    public ValueTask DisposeAsync() => new(CompleteAsync("Disposed"));

    private async Task FinishAsync(JobCostLogEntry final)
    {
        await _logger.CompleteAsync(final).ConfigureAwait(false);
        lock (_gate)
        {
            _revision++;
            _notifications.Writer.TryWrite(true);
            _notifications.Writer.TryComplete();
        }
    }

    private JobCostLogEntry Record(JobCostEvent kind, AttemptUsageSnapshot? attempt = null, bool enqueue = true,
        int? attemptNumber = null, Guid? operationId = null, UsageAttemptOutcome? outcome = null)
    {
        _revision++;
        UsageMetrics totals = Aggregate(out bool invalid, out int[] counts);
        bool partial = _attempts.Values.Any(IsPartial)
            || counts.Any(c => c < _attempts.Count);
        var entry = new JobCostLogEntry(1, _jobId, _revision, DateTimeOffset.UtcNow, kind,
            _completion, attempt, totals, _attempts.Count, counts, partial, invalid, _logger.DroppedEntries)
        {
            Context = kind is JobCostEvent.JobStarted or JobCostEvent.JobFinished ? _context : null,
            StartedAtUtc = _startedAtUtc,
            ObservedAtUtc = _observedAtUtc,
            EndedAtUtc = _endedAtUtc,
            MetricObservations = Observations(_attempts.Values, totals, counts),
            AttemptStatuses = Statuses(),
            OperationBreakdown = kind == JobCostEvent.JobFinished ? Operations() : [],
            ModelCostMismatchCount = MismatchCount(),
            AttemptNumber = attemptNumber,
            OperationId = operationId,
            AttemptOutcome = outcome,
        };
        _recent.Enqueue(JobCostLogReader.Render(entry));
        while (_recent.Count > 200) { _recent.Dequeue(); }
        if (enqueue) { _logger.Append(entry); }
        _notifications.Writer.TryWrite(true);
        return entry;
    }

    private UsageMetrics Aggregate(out bool invalid, out int[] counts)
    {
        UsageMetrics result = UsageMath.Sum(_attempts.Values.Select(a => a.Metrics), out invalid, out counts);
        invalid |= _attempts.Values.Any(a => a.HasInvalidValues);
        return result;
    }

    private JobCostSnapshot CreateSnapshot()
    {
        UsageMetrics total = Aggregate(out bool invalid, out int[] counts);
        var observations = Observations(_attempts.Values, total, counts);
        var operations = Operations();
        var models = Models(out bool modelsTruncated);
        int count = _attempts.Count;
        int mismatch = MismatchCount();
        string status = count == 0 ? "未送信（このジョブのAI送信なし）"
            : invalid ? "異常値あり（不明として扱います）"
            : counts.All(c => c == 0) ? "未取得（SDK未提供・取得失敗・送信状況不明）"
            : observations.Values.Any(m => m.IsPartial)
                ? "一部取得（観測できた値のみ）" : "観測完了";
        string Metric(decimal? value, int index) => value is null ? "—（未取得）"
            : $"{Number(value)}（観測 {counts[index]}/{count} 試行{(observations[(UsageMetric)index].IsPartial ? "・部分取得" : "・観測完了")}）";
        string Credits() => total.TotalNanoAiu is not { } nanoAiu ? "—（未取得）"
            : $"{RunMetricsFormatter.FormatAiCredits(nanoAiu)}（SDK報告値から換算・観測 {counts[(int)UsageMetric.TotalNanoAiu]}/{count} 試行"
                + $"{(observations[UsageMetric.TotalNanoAiu].IsPartial ? "・部分取得" : "・観測完了")}）";
        string summary = $"入力 {Metric(total.InputTokens, 0)}／出力 {Metric(total.OutputTokens, 1)}\n"
            + $"AIクレジット {Credits()}\n{(_finished ? "終了" : "実行中")}・{status}";
        var details = new StringBuilder()
            .AppendLine("GitHubから取得できた使用量です。請求確定額・アカウント全体の利用量ではありません。")
            .AppendLine("今回のジョブのみ。再試行・失敗・未保存行を含む観測値。未報告の消費は含みません。")
            .AppendLine($"送信試行数 {count}（送信受付確認数ではありません）")
            .AppendLine($"観測値の下方訂正あり: {_attempts.Values.Count(a => a.HasDownwardCorrection)} 試行（新しい観測値へ置換済み）")
            .AppendLine($"開始UTC {_startedAtUtc:O}／最終観測UTC {_observedAtUtc:O}／終了UTC {_endedAtUtc:O}")
            .AppendLine($"入力トークン {Metric(total.InputTokens, 0)}")
            .AppendLine($"出力トークン {Metric(total.OutputTokens, 1)}")
            .AppendLine($"推論トークン {Metric(total.ReasoningTokens, 2)}")
            .AppendLine($"キャッシュ読み取り {Metric(total.CacheReadTokens, 3)}")
            .AppendLine($"キャッシュ書き込み {Metric(total.CacheWriteTokens, 4)}")
            .AppendLine($"nano-AI units（SDK報告値） {Metric(total.TotalNanoAiu, 5)}")
            .AppendLine($"AIクレジット（nano-AI units ÷ 1,000,000,000。SDK報告値からの換算で請求確定額ではありません） {Credits()}")
            .AppendLine($"プレミアムリクエスト消費量 {Metric(total.PremiumRequests, 6)}")
            .AppendLine("非nullable項目の0は欠落と区別できないため未取得。最終RPCの取得項目はイベント累計を置換。")
            .AppendLine("最後の呼び出しのみの値・イベント補完値は部分取得。推論・キャッシュは入力/出力へ加算しません。");
        foreach (UsageMetric metric in Enum.GetValues<UsageMetric>())
        {
            var sources = observations[metric].SourceCounts;
            if (sources.IsEmpty) { continue; }
            details.AppendLine($"{MetricLabel(metric)}の取得元 " + string.Join("／", sources.OrderBy(p => p.Key)
                .Select(p => $"{SourceLabel(p.Key)} {p.Value} 試行")));
        }

        details.AppendLine($"モデル内訳とセッション総量の不一致: {mismatch} 試行"
            + "（同一の最終RPCで全モデルの費用を取得できた場合のみ比較。配賦・補正はしません）");
        details.AppendLine("報告モデル識別子（匿名化）：SDKのモデル名をSHA-256の先頭16桁へ変換。実際のモデル名は表示・記録しません。")
            .AppendLine("要求モデルと報告モデルは別です。autoは自動選択の要求であり、実際に使用されたモデルを保証しません。")
            .AppendLine("モデル内訳は独立した観測値です。総量への加算・総量との一致の推定はしません。")
            .AppendLine($"モデル内訳は最大{ModelUsageSnapshot.MaximumModels}識別子。{(modelsTruncated ? "保持上限・内訳欠落あり（部分取得）" : "未報告項目は未取得")}");
        var requested = _attempts.Values.Select(a => (a.RequestedModelKey, a.RequestedModelIsAuto))
            .Distinct().Take(ModelUsageSnapshot.MaximumModels + 1).ToArray();
        foreach (var request in requested.Take(ModelUsageSnapshot.MaximumModels))
        {
            string mode = request.RequestedModelIsAuto switch { true => "auto", false => "非auto", _ => "未確認" };
            details.AppendLine($"要求モデル（匿名化） {request.RequestedModelKey ?? "未確認"}／{mode}");
        }
        if (requested.Length > ModelUsageSnapshot.MaximumModels) { details.AppendLine("要求モデル表示は保持上限により省略あり。"); }
        if (models.IsEmpty) { details.AppendLine("報告モデル内訳 —（未取得）"); }
        foreach (var model in models)
        {
            details.AppendLine($"{model.ModelKey}（{(model.IsPartial ? "部分取得" : "観測完了")}）／入力 {Number(model.Metrics.InputTokens)}／出力 {Number(model.Metrics.OutputTokens)}")
                .AppendLine($"推論 {Number(model.Metrics.ReasoningTokens)}／キャッシュ読み取り {Number(model.Metrics.CacheReadTokens)}／キャッシュ書き込み {Number(model.Metrics.CacheWriteTokens)}")
                .AppendLine($"nano-AI units（SDK報告値） {Number(model.Metrics.TotalNanoAiu)}／プレミアムリクエスト消費量 {Number(model.Metrics.PremiumRequests)}");
        }
        foreach (var operation in operations)
        {
            string OperationMetric(decimal? value, UsageMetric metric) => value is null ? "—（未取得）"
                : $"{Number(value)}（{(operation.MetricObservations[metric].IsPartial ? "部分取得" : "観測完了")}）";
            details.AppendLine($"{operation.Operation}: {operation.AttemptCount} 試行／入力 {OperationMetric(operation.Metrics.InputTokens, UsageMetric.InputTokens)}／出力 {OperationMetric(operation.Metrics.OutputTokens, UsageMetric.OutputTokens)}");
        }

        foreach (var group in _attempts.Values.GroupBy(a => a.Status).OrderBy(g => g.Key))
        {
            string label = group.Key switch
            {
                UsageObservationStatus.EventObserved => "イベント観測（部分取得）",
                UsageObservationStatus.FinalObserved => "最終RPC観測（欠落項目はイベントで補完）",
                UsageObservationStatus.RpcUnavailable => "最終RPC取得失敗・非対応",
                UsageObservationStatus.RpcTimedOut => "最終RPC取得期限超過",
                UsageObservationStatus.SendPending => "送信未収束のため最終RPC省略",
                UsageObservationStatus.AbortPending => "中断処理未収束のため最終RPC省略",
                UsageObservationStatus.EventsIncomplete => "イベント識別子欠落・保持上限による部分取得",
                UsageObservationStatus.EventsUnavailable => "イベント購読不可",
                _ => "観測待ち・SDK未提供",
            };
            details.AppendLine($"{label}: {group.Count()} 試行");
        }

        return new JobCostSnapshot(_jobId, _revision, _finished, summary, details.ToString(),
            string.Join(Environment.NewLine, _recent), _logger.LogPath, _logger.StatusText)
        {
            Metrics = total,
            MetricObservations = observations,
            AttemptCount = count,
            AttemptStatuses = Statuses(),
            OperationBreakdown = operations,
            Models = models,
            ModelsTruncated = modelsTruncated,
            ModelCostMismatchCount = mismatch,
            StartedAtUtc = _startedAtUtc,
            ObservedAtUtc = _observedAtUtc,
            EndedAtUtc = _endedAtUtc,
        };
    }

    private int MismatchCount() =>
        _attempts.Values.Count(a => a.ModelCostComparison == ModelCostComparison.Mismatch);

    private static string MetricLabel(UsageMetric metric) => metric switch
    {
        UsageMetric.InputTokens => "入力トークン",
        UsageMetric.OutputTokens => "出力トークン",
        UsageMetric.ReasoningTokens => "推論トークン",
        UsageMetric.CacheReadTokens => "キャッシュ読み取り",
        UsageMetric.CacheWriteTokens => "キャッシュ書き込み",
        UsageMetric.TotalNanoAiu => "nano-AI units",
        _ => "プレミアムリクエスト消費量",
    };

    private static string SourceLabel(UsageSource source) => source switch
    {
        UsageSource.Events => "イベント",
        UsageSource.FinalRpc => "最終RPC",
        UsageSource.LastCall => "最後の呼び出しのみ",
        _ => "未取得",
    };

    // The adapter has attempt-wide provenance today. Conservatively keep all metrics partial
    // when any fallback is partial; never promote events/LastCall solely because counts match.
    private static bool IsPartial(AttemptUsageSnapshot attempt) => attempt.IsPartial || !attempt.IsFinished
        || attempt.HasInvalidValues || attempt.Source != UsageSource.FinalRpc;

    private static ImmutableDictionary<UsageMetric, MetricObservation> Observations(
        IEnumerable<AttemptUsageSnapshot> attempts, UsageMetrics total, int[] counts)
    {
        AttemptUsageSnapshot[] values = attempts.ToArray();
        return Enum.GetValues<UsageMetric>().ToImmutableDictionary(m => m, m =>
        {
            int complete = 0;
            var sources = ImmutableDictionary.CreateBuilder<UsageSource, int>();
            foreach (AttemptUsageSnapshot attempt in values)
            {
                MetricProvenance item = UsageProvenance.ForAttempt(attempt, m);
                if (item.Source == UsageSource.None) { continue; }
                sources[item.Source] = sources.GetValueOrDefault(item.Source) + 1;
                if (!item.IsPartial) { complete++; }
            }

            return new MetricObservation(counts[(int)m],
                UsageProvenance.Value(total, m) is null ? 0 : complete, values.Length)
            {
                SourceCounts = sources.ToImmutable(),
            };
        });
    }

    private ImmutableDictionary<UsageObservationStatus, int> Statuses() => _attempts.Values
        .GroupBy(a => a.Status).ToImmutableDictionary(g => g.Key, g => g.Count());

    private ImmutableArray<ModelUsageSnapshot> Models(out bool truncated)
    {
        string[] keys = _attempts.Values.SelectMany(a => a.Models).Select(m => m.ModelKey)
            .Distinct(StringComparer.Ordinal).Take(ModelUsageSnapshot.MaximumModels + 1).ToArray();
        truncated = keys.Length > ModelUsageSnapshot.MaximumModels || _attempts.Values.Any(a => a.ModelsTruncated);
        bool incomplete = truncated || _attempts.Values.Any(a => a.Models.IsEmpty || !a.IsFinished);
        return keys.Take(ModelUsageSnapshot.MaximumModels).OrderBy(k => k, StringComparer.Ordinal).Select(key =>
        {
            var values = _attempts.Values.SelectMany(a => a.Models).Where(m => m.ModelKey == key).ToArray();
            UsageMetrics metrics = UsageMath.Sum(values.Select(m => m.Metrics), out bool invalid, out int[] counts);
            return new ModelUsageSnapshot(key, metrics, incomplete || invalid || values.Any(m => m.IsPartial)
                || counts.Any(c => c < values.Length));
        }).ToImmutableArray();
    }

    private ImmutableArray<OperationUsageSnapshot> Operations() => _attempts.Values.GroupBy(a => a.Operation)
        .OrderBy(g => g.Key).Select(g =>
        {
            UsageMetrics metrics = UsageMath.Sum(g.Select(a => a.Metrics), out _, out int[] counts);
            return new OperationUsageSnapshot(g.Key, g.Count(), metrics, Observations(g, metrics, counts));
        }).ToImmutableArray();

    private void LogChanged()
    {
        lock (_gate)
        {
            _revision++;
            _notifications.Writer.TryWrite(true);
        }
    }

    private async Task NotifyAsync()
    {
        // Single background consumer: observer exceptions/blocking never affect SDK or disk writes.
        await foreach (bool unused in _notifications.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            JobCostSnapshot snapshot = Snapshot;
            try { _observer?.Invoke(snapshot); }
            catch { /* An observer is not part of the evaluation's success contract. */ }
            if (!snapshot.IsFinished) { await Task.Delay(250).ConfigureAwait(false); }
        }
    }

    private static string Number(decimal? value) => value?.ToString("0.############################", CultureInfo.InvariantCulture)
        ?? "—（未取得）";
}