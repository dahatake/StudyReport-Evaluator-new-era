using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using StudyReportEvaluator.App.Usage;

namespace StudyReportEvaluator.App.Logging;

internal sealed class JobCostLogger
{
    internal const long MaximumBytes = 32 * 1024 * 1024;
    internal const int MaximumLineBytes = 64 * 1024;
    private readonly long _maximumBytes;
    private readonly Channel<JobCostLogEntry> _entries = Channel.CreateBounded<JobCostLogEntry>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Action _changed;
    private readonly CancellationTokenSource _stop = new();
    private readonly string? _directory;
    private readonly Guid _jobId;
    private Task? _writer;
    private JobCostLogEntry? _final;
    private string? _path;
    private long _dropped;
    // 0 writing, 1 saved, 2 failed, 3 flush deadline exceeded.
    private int _state;
    private int _retentionFailed;
    private int _finalDropped;

    internal JobCostLogger(Guid jobId, string? directory, Action changed, long maximumBytes = MaximumBytes)
    {
        if (maximumBytes < 2 * MaximumLineBytes || maximumBytes > MaximumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }
        _maximumBytes = maximumBytes;
        _jobId = jobId;
        _directory = directory;
        _changed = changed;
    }

    internal string? LogPath => Volatile.Read(ref _path);
    internal long DroppedEntries => Interlocked.Read(ref _dropped);
    internal string StatusText => (Volatile.Read(ref _state) switch
    {
        1 => "保存済み",
        2 => "保存失敗（使用量の画面表示は保持）",
        3 => "保存の終了待機期限超過（完全保存は未確認）",
        _ => "保存中",
    }) + (DroppedEntries > 0 ? $"／詳細記録欠落 {DroppedEntries} 件" : string.Empty)
        + (!OperatingSystem.IsWindows() ? "／保持整理はWindowsのみ対応（自動削除なし）"
            : Volatile.Read(ref _retentionFailed) != 0 ? "／保持整理を一部スキップ（保存処理とは別）" : string.Empty);

    internal Task<JobCostLogReadResult> ReadAsync()
    {
        string? path = LogPath;
        return Task.Run(() => JobCostLogReader.Read(path, _jobId));
    }

    internal void Start() => _writer = Task.Run(WriteAsync);

    internal void Append(JobCostLogEntry entry)
    {
        if (Volatile.Read(ref _state) != 0 || !_entries.Writer.TryWrite(entry))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    internal async Task CompleteAsync(JobCostLogEntry final)
    {
        // Reserved terminal slot: queue saturation never drops the final aggregate.
        Volatile.Write(ref _final, final);
        _entries.Writer.TryComplete();
        try
        {
            await (_writer ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Interlocked.CompareExchange(ref _state, 3, 0);
            // Cancellation callbacks must not hold up the caller's finite flush deadline.
            _ = _stop.CancelAsync();
        }
        catch
        {
            Interlocked.CompareExchange(ref _state, 2, 0);
        }

        if (_writer?.IsCompleted == true && Volatile.Read(ref _state) == 2) { CountFinalDrop(); }
        Changed();
    }

    private async Task WriteAsync()
    {
        bool pendingEntry = false;
        bool finalWritten = false;
        try
        {
            string directory = _directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StudyReportEvaluator", "jobs");
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{_jobId:N}.jsonl");
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.Read, 4096, FileOptions.Asynchronous))
            {
                Volatile.Write(ref _path, path);
                Changed();
                RetainRecentLogs(directory);
                long bytes = 0;
                await foreach (JobCostLogEntry entry in _entries.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
                {
                    pendingEntry = true;
                    byte[] line = Serialize(entry);
                    if (line.Length > MaximumLineBytes || bytes + line.Length > _maximumBytes - MaximumLineBytes)
                    {
                        Interlocked.Increment(ref _dropped);
                        pendingEntry = false;
                        Changed();
                        continue;
                    }

                    await stream.WriteAsync(line, _stop.Token).ConfigureAwait(false);
                    await stream.FlushAsync(_stop.Token).ConfigureAwait(false);
                    bytes += line.Length;
                    pendingEntry = false;
                }

                if (Volatile.Read(ref _final) is { } final)
                {
                    byte[] line = Serialize(final with { DroppedEntries = DroppedEntries });
                    if (line.Length > MaximumLineBytes || bytes + line.Length > _maximumBytes)
                    {
                        throw new IOException();
                    }
                    await stream.WriteAsync(line, _stop.Token).ConfigureAwait(false);
                }

                await stream.FlushAsync(_stop.Token).ConfigureAwait(false);
                finalWritten = true;
            }

            Interlocked.CompareExchange(ref _state, 1, 0);
        }
        catch
        {
            // Do not disclose exception messages (which can contain paths/credentials).
            Interlocked.CompareExchange(ref _state, 2, 0);
        }
        finally
        {
            _entries.Writer.TryComplete();
            if (pendingEntry) { Interlocked.Increment(ref _dropped); }
            while (_entries.Reader.TryRead(out _)) { Interlocked.Increment(ref _dropped); }
            if (!finalWritten) { CountFinalDrop(); }
            Changed();
        }
    }

    private void CountFinalDrop()
    {
        if (Volatile.Read(ref _final) is not null && Interlocked.Exchange(ref _finalDropped, 1) == 0)
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    private void Changed()
    {
        try { _changed(); }
        catch { /* Presentation must not fault the writer or completion. */ }
    }

    private void RetainRecentLogs(string directory)
    {
        try
        {
            // Managed DeleteOnClose cannot be enabled after validating an already-open handle.
            // Do not fall back to a racy close/check/path-delete on platforms without this helper.
            if (!OperatingSystem.IsWindows()) { Interlocked.Exchange(ref _retentionFailed, 1); return; }
            using var parents = JobCostFileAccess.PinDirectories(directory);
            DateTime cutoff = DateTime.UtcNow.AddDays(-30);
            foreach (string candidate in Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                if (_stop.IsCancellationRequested) { break; }
                string name = Path.GetFileNameWithoutExtension(candidate);
                if (!Guid.TryParseExact(name, "N", out Guid id) || name != id.ToString("N") || id == _jobId) { continue; }
                try
                {
                    if ((File.GetAttributes(candidate) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0
                        || File.GetLastWriteTimeUtc(candidate) >= cutoff) { continue; }
                    using SafeFileHandle handle = JobCostFileAccess.Open(candidate, delete: true);
                    // Recheck age/type on the exclusively held object, not the pathname.
                    if ((File.GetAttributes(handle) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0
                        || File.GetLastWriteTimeUtc(handle) >= cutoff) { continue; }
                    using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: false);
                    byte[] header = new byte[MaximumLineBytes];
                    int length = 0;
                    int next;
                    while (length < header.Length && (next = stream.ReadByte()) >= 0)
                    {
                        if (next == '\n') { break; }
                        header[length++] = (byte)next;
                    }
                    if (length == header.Length) { continue; }
                    JobCostLogEntry? entry = JobCostLogReader.Parse(header.AsSpan(0, length), id);
                    // A GUID-like arbitrary file is not sufficient evidence of ownership.
                    if (entry is not { Event: JobCostEvent.JobStarted, Revision: 1, AttemptCount: 0 }
                        || entry.Timestamp.UtcDateTime >= cutoff) { continue; }
                    JobCostFileAccess.DeleteWhenClosed(handle);
                }
                catch (FileNotFoundException) { /* Another retention pass won. */ }
                catch (IOException ex) when ((ex.HResult & 0xffff) is 2 or 3 or 32 or 33) { /* Gone or active; skip. */ }
                catch { Interlocked.Exchange(ref _retentionFailed, 1); }
            }
        }
        catch { Interlocked.Exchange(ref _retentionFailed, 1); }
        finally { Changed(); }
    }

    private static byte[] Serialize(JobCostLogEntry entry) => Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(entry, JobCostJsonContext.Default.JobCostLogEntry) + "\n");
}

/// <summary>Bounded, safely rendered view. No original JSON or untrusted text is returned.</summary>
public sealed record JobCostLogReadResult(
    ImmutableArray<string> Lines, bool IsIncomplete, bool HasInvalidRecords,
    bool HasDroppedEntries, bool IsTruncated, bool ReadFailed)
{
    public string StatusText => (ReadFailed ? "ログ読込失敗" : IsIncomplete ? "未完了のログ" : "終端記録あり")
        + (HasInvalidRecords ? "／破損・不正または途中行あり" : string.Empty)
        + (HasDroppedEntries ? "／詳細記録欠落あり" : string.Empty)
        + (IsTruncated ? "／表示上限（最新200件・読込32MiB）" : string.Empty);
}

internal static class JobCostLogReader
{
    internal static JobCostLogReadResult Read(string? path, Guid jobId)
    {
        var lines = new Queue<string>();
        bool terminal = false, invalid = false, dropped = false, truncated = false;
        try
        {
            if (path is null || Path.GetFileName(path) != $"{jobId:N}.jsonl") { throw new IOException(); }
            using var parents = JobCostFileAccess.PinDirectories(Path.GetDirectoryName(path)!);
            using SafeFileHandle handle = JobCostFileAccess.Open(path, delete: false);
            if ((File.GetAttributes(handle) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) { throw new IOException(); }
            using var stream = new FileStream(handle, FileAccess.Read, 4096, isAsync: false);
            long limit = Math.Min(stream.Length, JobCostLogger.MaximumBytes);
            truncated = stream.Length > limit;
            byte[] buffer = new byte[JobCostLogger.MaximumLineBytes];
            int length = 0;
            long revision = 0;
            for (long read = 0; read < limit; read++)
            {
                int next = stream.ReadByte();
                if (next < 0) { invalid = true; break; }
                if (next != '\n')
                {
                    if (length == buffer.Length) { invalid = truncated = true; break; }
                    buffer[length++] = (byte)next;
                    continue;
                }
                JobCostLogEntry? entry = Parse(buffer.AsSpan(0, length), jobId);
                if (entry is null || terminal || entry.Revision <= revision
                    || (revision == 0 && (entry.Event != JobCostEvent.JobStarted || entry.Revision != 1))
                    || (revision != 0 && entry.Event == JobCostEvent.JobStarted))
                {
                    invalid = true;
                    break;
                }
                revision = entry.Revision;
                terminal = entry.Event == JobCostEvent.JobFinished;
                dropped |= entry.DroppedEntries > 0;
                lines.Enqueue(Render(entry));
                if (lines.Count > 200) { lines.Dequeue(); truncated = true; }
                length = 0;
            }
            invalid |= length != 0;
            return new(lines.ToImmutableArray(), !terminal || invalid || stream.Length > limit,
                invalid, dropped, truncated, false);
        }
        catch
        {
            return new(lines.ToImmutableArray(), true, invalid, dropped, truncated, true);
        }
    }

    internal static JobCostLogEntry? Parse(ReadOnlySpan<byte> json, Guid jobId)
    {
        try
        {
            JobCostLogEntry? entry = JsonSerializer.Deserialize(json, JobCostJsonContext.Default.JobCostLogEntry);
            if (entry is null || entry.SchemaVersion != 1 || entry.JobId != jobId || entry.Revision <= 0
                || entry.Timestamp == default || entry.Timestamp.Offset != TimeSpan.Zero
                || !Enum.IsDefined(entry.Event) || !Enum.IsDefined(entry.Completion)
                || entry.AttemptCount < 0 || entry.DroppedEntries < 0 || entry.Metrics is null
                || entry.ObservedAttemptCounts is not { Length: 7 }
                || entry.ObservedAttemptCounts.Any(c => c < 0 || c > entry.AttemptCount)
                || (entry.Event == JobCostEvent.JobFinished) == (entry.Completion == JobCostCompletion.Running)) { return null; }
            UsageMath.Sanitize(entry.Metrics, out bool invalid);
            if (invalid) { return null; }
            if (entry.AttemptOutcome is { } outcome && !Enum.IsDefined(outcome)) { return null; }
            if (entry.Event == JobCostEvent.AttemptFinished
                && (entry.Attempt is null || entry.AttemptNumber is null or < 1
                    || entry.OperationId is null || entry.OperationId == Guid.Empty
                    || entry.AttemptOutcome is null)) { return null; }
            if (entry.Attempt is { } attempt)
            {
                if (attempt.AttemptId == Guid.Empty || attempt.Revision < 0 || attempt.Metrics is null
                    || !Enum.IsDefined(attempt.Operation) || !Enum.IsDefined(attempt.Source)
                    || !Enum.IsDefined(attempt.Status)
                    || !Enum.IsDefined(attempt.ModelCostComparison)
                    || attempt.MetricProvenance?.Any(pair => !Enum.IsDefined(pair.Key)
                        || pair.Value is null || !Enum.IsDefined(pair.Value.Source)) == true) { return null; }
                UsageMath.Sanitize(attempt.Metrics, out invalid);
                if (invalid) { return null; }
            }
            if (entry.ModelCostMismatchCount < 0 || entry.ModelCostMismatchCount > entry.AttemptCount) { return null; }
            if (entry.MetricObservations is { } observations && (observations.Count != 7
                || observations.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value is null
                    || pair.Value.AttemptCount != entry.AttemptCount || pair.Value.CompleteAttemptCount < 0
                    || pair.Value.CompleteAttemptCount > pair.Value.ObservedAttemptCount
                    || pair.Value.ObservedAttemptCount != entry.ObservedAttemptCounts[(int)pair.Key]
                    || pair.Value.SourceCounts is null
                    || pair.Value.SourceCounts.Any(source => !Enum.IsDefined(source.Key) || source.Value < 0)
                    || pair.Value.SourceCounts.Values.Sum() > pair.Value.ObservedAttemptCount))) { return null; }
            return entry;
        }
        catch (JsonException) { return null; }
    }

    internal static string Render(JobCostLogEntry entry)
    {
        string label = entry.Event switch
        {
            JobCostEvent.JobStarted => "ジョブ開始",
            JobCostEvent.AttemptStarted => "送信試行開始（受付は未確認）",
            JobCostEvent.AttemptFinished => $"試行終了（{entry.AttemptOutcome}／試行番号 {entry.AttemptNumber}／操作ID {entry.OperationId}）",
            JobCostEvent.JobFinished => $"ジョブ終了（{entry.Completion}）",
            _ => "使用量更新（累計を置換）",
        };
        string Metric(decimal? value, UsageMetric metric) => value is null ? "—（未取得）"
            : value.Value.ToString("0.############################", CultureInfo.InvariantCulture)
                + (IsPartial(metric) ? "（部分取得）" : "（観測完了）");
        bool IsPartial(UsageMetric metric) =>
            entry.HasInvalidValues || entry.Attempt is { Source: UsageSource.Events or UsageSource.LastCall }
            || entry.MetricObservations?.GetValueOrDefault(metric)?.IsPartial != false;
        string credits = entry.Metrics.TotalNanoAiu is { } nanoAiu && nanoAiu >= 0m
            ? $"{RunMetricsFormatter.FormatAiCredits(nanoAiu)}（SDK報告値から換算・{(IsPartial(UsageMetric.TotalNanoAiu) ? "部分取得" : "観測完了")}）"
            : "—（未取得）";
        return $"{entry.Timestamp:HH:mm:ss} UTC {label}：入力 {Metric(entry.Metrics.InputTokens, UsageMetric.InputTokens)}／出力 {Metric(entry.Metrics.OutputTokens, UsageMetric.OutputTokens)}"
            + $"／推論 {Metric(entry.Metrics.ReasoningTokens, UsageMetric.ReasoningTokens)}"
            + $"／キャッシュ読み取り {Metric(entry.Metrics.CacheReadTokens, UsageMetric.CacheReadTokens)}"
            + $"／キャッシュ書き込み {Metric(entry.Metrics.CacheWriteTokens, UsageMetric.CacheWriteTokens)}"
            + $"／nano-AI units（SDK報告値） {Metric(entry.Metrics.TotalNanoAiu, UsageMetric.TotalNanoAiu)}"
            + $"／プレミアムリクエスト消費量 {Metric(entry.Metrics.PremiumRequests, UsageMetric.PremiumRequests)}"
            + $"／AIクレジット {credits}"
            + (entry.Attempt?.HasDownwardCorrection == true ? "／観測値の下方訂正あり" : string.Empty)
            + (entry.ModelCostMismatchCount > 0
                ? $"／モデル内訳と総量の不一致 {entry.ModelCostMismatchCount} 試行" : string.Empty);
    }
}

// Pin directory components against rename/reparse replacement; open the final file without
// following reparse points. Deletion is marked on the validated exclusive handle itself.
internal static class JobCostFileAccess
{
    internal static IDisposable PinDirectories(string directory)
    {
        if (!OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException(); }
        var pinned = new DirectoryHandles();
        try
        {
            var names = new Stack<string>();
            for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent) { names.Push(current.FullName); }
            foreach (string name in names)
            {
                SafeFileHandle handle = CreateFile(name, 0x80, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                pinned.Handles.Add(handle);
                if (handle.IsInvalid || (File.GetAttributes(handle) & FileAttributes.ReparsePoint) != 0) { throw new IOException(); }
            }
            return pinned;
        }
        catch { pinned.Dispose(); throw; }
    }

    internal static SafeFileHandle Open(string path, bool delete)
    {
        if (!OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException(); }
        SafeFileHandle handle = CreateFile(path, delete ? 0x80010000u : 0x80000000u,
            delete ? 0u : 3u, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (!handle.IsInvalid) { return handle; }
        int error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        throw new IOException("Log file unavailable.", unchecked((int)(0x80070000u | (uint)error)));
    }

    internal static void DeleteWhenClosed(SafeFileHandle handle)
    {
        // FILE_DISPOSITION_INFO contains a one-byte BOOLEAN. Unlike opening DeleteOnClose,
        // marking it here cannot delete a replacement before identity/age/type validation.
        byte delete = 1;
        if (!SetFileInformationByHandle(handle, 4, ref delete, 1)) { throw new IOException(); }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, ref byte info, uint size);

    private sealed class DirectoryHandles : IDisposable
    {
        internal List<SafeFileHandle> Handles { get; } = [];
        public void Dispose()
        {
            for (int i = Handles.Count - 1; i >= 0; i--) { Handles[i].Dispose(); }
        }
    }
}