using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudyReportEvaluator.App.Logging;
using StudyReportEvaluator.App.Usage;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Logging;

// Requirements: FR-043 (AC-044)
public sealed class JobCostBackendTests
{
    [Fact]
    public async Task Reader_keeps_last_200_safe_lines_and_marks_partial_tail_without_echoing_fields()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows handle-based log reading.");
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(logDirectory: temp.Path);
        await tracker.CompleteAsync("SUCCESS");
        string path = Assert.IsType<string>(tracker.Snapshot.LogPath);
        string[] originals = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
        JsonObject header = JsonNode.Parse(originals[0])!.AsObject();
        JsonObject final = JsonNode.Parse(originals[^1])!.AsObject();
        var records = new List<string> { originals[0] };
        for (int i = 2; i <= 220; i++)
        {
            JsonObject entry = header.DeepClone().AsObject();
            entry["Event"] = 2;
            entry["Revision"] = i;
            entry["Prompt"] = "PRIVATE_ghp_SECRET\nforged-log-line";
            records.Add(entry.ToJsonString());
        }
        final["Revision"] = 221;
        final["DroppedEntries"] = 7;
        records.Add(final.ToJsonString());
        await File.WriteAllTextAsync(path, string.Join('\n', records) + "\n", TestContext.Current.CancellationToken);
        JobCostLogReadResult complete = await tracker.ReadLogAsync();
        Assert.False(complete.ReadFailed);
        Assert.False(complete.IsIncomplete);
        Assert.False(complete.HasInvalidRecords);
        Assert.True(complete.IsTruncated);
        Assert.True(complete.HasDroppedEntries);
        Assert.Equal(200, complete.Lines.Length);
        Assert.DoesNotContain("PRIVATE_", string.Join('\n', complete.Lines), StringComparison.Ordinal);
        Assert.DoesNotContain("forged-log-line", string.Join('\n', complete.Lines), StringComparison.Ordinal);
        await File.AppendAllTextAsync(path, "{\"Prompt\":\"PRIVATE_UNFINISHED", TestContext.Current.CancellationToken);
        JobCostLogReadResult partial = await tracker.ReadLogAsync();
        Assert.True(partial.IsIncomplete);
        Assert.True(partial.HasInvalidRecords);
        Assert.Equal(complete.Lines.ToArray(), partial.Lines.ToArray());
        Assert.DoesNotContain("PRIVATE_", partial.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reader_rejects_foreign_identity_invalid_enum_and_oversized_line()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows handle-based log reading.");
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(logDirectory: temp.Path);
        await tracker.CompleteAsync("SUCCESS");
        string path = Assert.IsType<string>(tracker.Snapshot.LogPath);
        string header = (await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken))[0];
        JsonObject foreign = JsonNode.Parse(header)!.AsObject();
        foreign["JobId"] = Guid.NewGuid();
        await File.WriteAllTextAsync(path, foreign.ToJsonString() + "\n", TestContext.Current.CancellationToken);
        Assert.True((await tracker.ReadLogAsync()).HasInvalidRecords);
        JsonObject invalid = JsonNode.Parse(header)!.AsObject();
        invalid["Event"] = 999;
        await File.WriteAllTextAsync(path, invalid.ToJsonString() + "\n", TestContext.Current.CancellationToken);
        Assert.True((await tracker.ReadLogAsync()).HasInvalidRecords);
        await File.WriteAllTextAsync(path, header + "\n" + new string('x', 65537), TestContext.Current.CancellationToken);
        JobCostLogReadResult oversized = await tracker.ReadLogAsync();
        Assert.Single(oversized.Lines);
        Assert.True(oversized.HasInvalidRecords);
        Assert.True(oversized.IsIncomplete);
        Assert.True(oversized.IsTruncated);
        await using (var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            file.SetLength(32L * 1024 * 1024 + 1);
        }
        Assert.True((await tracker.ReadLogAsync()).IsTruncated);
    }

    [Fact]
    public async Task Retention_deletes_only_old_owned_unlocked_logs_and_warns_independently_on_failure()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows handle-based log retention.");
        using var temp = new TemporaryDirectory();
        await using var seed = new JobUsageTracker(logDirectory: temp.Path);
        await seed.CompleteAsync("SUCCESS");
        string template = (await File.ReadAllLinesAsync(Assert.IsType<string>(seed.Snapshot.LogPath), TestContext.Current.CancellationToken))[0];
        string old = await WriteOldLogAsync(temp.Path, template);
        string active = await WriteOldLogAsync(temp.Path, template);
        string readOnly = await WriteOldLogAsync(temp.Path, template);
        string arbitrary = Path.Combine(temp.Path, Guid.NewGuid().ToString("N") + ".jsonl");
        string unrelated = Path.Combine(temp.Path, "notes.jsonl");
        await File.WriteAllTextAsync(arbitrary, "PRIVATE_ARBITRARY_FILE", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(unrelated, template, TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(arbitrary, DateTime.UtcNow.AddDays(-40));
        File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-40));
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        try
        {
            using var held = new FileStream(active, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            await using var current = new JobUsageTracker(logDirectory: temp.Path);
            await current.CompleteAsync("SUCCESS");
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(active));
            Assert.True(File.Exists(readOnly));
            Assert.True(File.Exists(arbitrary));
            Assert.True(File.Exists(unrelated));
            Assert.True(File.Exists(seed.Snapshot.LogPath));
            Assert.True(File.Exists(current.Snapshot.LogPath));
            Assert.StartsWith("保存済み", current.Snapshot.LogStatusText, StringComparison.Ordinal);
            Assert.Contains("保持整理", current.Snapshot.LogStatusText, StringComparison.Ordinal);
        }
        finally { File.SetAttributes(readOnly, FileAttributes.Normal); }
    }

    [Fact]
    public async Task Symlink_is_not_followed_by_reader_or_retention_when_creation_is_available()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows reparse-point protection.");
        using var temp = new TemporaryDirectory();
        await using var tracker = new JobUsageTracker(logDirectory: temp.Path);
        await tracker.CompleteAsync("SUCCESS");
        string path = Assert.IsType<string>(tracker.Snapshot.LogPath);
        string target = Path.Combine(temp.Path, "outside.txt");
        File.Move(path, target);
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddDays(-40));
        try { File.CreateSymbolicLink(path, target); }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            || exception is IOException && (exception.HResult & 0xffff) == 1314)
        {
            Assert.Skip("Symbolic-link creation requires an unavailable Windows privilege (no elevation attempted).");
        }
        Assert.True((await tracker.ReadLogAsync()).ReadFailed);
        await using var current = new JobUsageTracker(logDirectory: temp.Path);
        await current.CompleteAsync("SUCCESS");
        Assert.True(File.Exists(target));
        Assert.NotNull(new FileInfo(path).LinkTarget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bounded_writer_counts_queue_capacity_and_io_drops_without_callback_interference(bool failWriter)
    {
        using var temp = new TemporaryDirectory();
        await using var seed = new JobUsageTracker(logDirectory: temp.Path);
        await seed.CompleteAsync("SUCCESS");
        string[] template = await File.ReadAllLinesAsync(Assert.IsType<string>(seed.Snapshot.LogPath), TestContext.Current.CancellationToken);
        // Internal writer test seam without widening production visibility or touching assembly configuration.
        Assembly assembly = typeof(JobUsageTracker).Assembly;
        Type loggerType = assembly.GetType("StudyReportEvaluator.App.Logging.JobCostLogger", true)!;
        Type entryType = assembly.GetType("StudyReportEvaluator.App.Logging.JobCostLogEntry", true)!;
        Guid jobId = Guid.NewGuid();
        object Entry(string json)
        {
            JsonObject node = JsonNode.Parse(json)!.AsObject();
            node["JobId"] = jobId;
            return JsonSerializer.Deserialize(node.ToJsonString(), entryType)!;
        }
        string directory = Path.Combine(temp.Path, "writer");
        if (failWriter) { await File.WriteAllTextAsync(directory, "PRIVATE_BLOCKER", TestContext.Current.CancellationToken); }
        object logger = Activator.CreateInstance(loggerType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, [jobId, directory, (Action)(() => throw new InvalidOperationException("PRIVATE_CALLBACK")), 131072L], null)!;
        object? Invoke(string name, params object[] args) => loggerType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(logger, args);
        object started = Entry(template[0]);
        for (int i = 0; i < 300; i++) { Invoke("Append", started); }
        long Dropped() => (long)loggerType.GetProperty("DroppedEntries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(logger)!;
        Assert.Equal(44, Dropped()); // 256 slots; writer has not started.
        Invoke("Start");
        await (Task)Invoke("CompleteAsync", Entry(template[^1]))!;
        string status = (string)loggerType.GetProperty("StatusText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(logger)!;
        if (failWriter)
        {
            Assert.Contains("保存失敗", status, StringComparison.Ordinal);
            Assert.Equal(301, Dropped()); // Includes the reserved final record exactly once.
        }
        else
        {
            string path = Path.Combine(directory, $"{jobId:N}.jsonl");
            string[] lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(new FileInfo(path).Length <= 131072);
            Assert.True(Dropped() > 44); // Disk cap, not just channel saturation.
            Assert.Equal(300 - (lines.Length - 1), Dropped());
            using JsonDocument final = JsonDocument.Parse(lines[^1]);
            Assert.Equal(Dropped(), final.RootElement.GetProperty("DroppedEntries").GetInt64());
            Assert.StartsWith("保存済み", status, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("PRIVATE_", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parallel_trackers_create_unique_complete_logs_with_terminal_totals()
    {
        using var temp = new TemporaryDirectory();
        const int count = 3;
        var trackers = Enumerable.Range(1, count)
            .Select(_ => new JobUsageTracker(logDirectory: temp.Path))
            .ToArray();
        try
        {
            using Barrier start = new(count);
            await Task.WhenAll(trackers.Select((tracker, index) => Task.Run(async () =>
            {
                Guid id = tracker.BeginAttempt(UsageOperation.Normal);
                Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
                tracker.ReplaceAttempt(new(id, UsageOperation.Normal, 1,
                    new(InputTokens: index + 1), UsageSource.Events));
                await tracker.CompleteAsync("SUCCESS");
            })));

            string[] paths = trackers.Select(tracker => Assert.IsType<string>(tracker.Snapshot.LogPath)).ToArray();
            Assert.Equal(count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(paths, path => Assert.True(File.Exists(path)));
            for (int i = 0; i < trackers.Length; i++)
            {
                string[] lines = await File.ReadAllLinesAsync(paths[i], TestContext.Current.CancellationToken);
                long previousRevision = 0;
                foreach (string line in lines)
                {
                    using JsonDocument entry = JsonDocument.Parse(line);
                    JsonElement root = entry.RootElement;
                    Assert.Equal(trackers[i].Snapshot.JobId, root.GetProperty("JobId").GetGuid());
                    long revision = root.GetProperty("Revision").GetInt64();
                    Assert.True(revision > previousRevision);
                    previousRevision = revision;
                    Assert.Equal(JsonValueKind.Null, root.GetProperty("Metrics").GetProperty("OutputTokens").ValueKind);
                }
                using JsonDocument final = JsonDocument.Parse(lines[^1]);
                Assert.Equal(1, final.RootElement.GetProperty("Completion").GetInt32());
                Assert.Equal(i + 1, final.RootElement.GetProperty("Metrics").GetProperty("InputTokens").GetInt64());
                Assert.Equal(trackers[i].Snapshot.JobId, final.RootElement.GetProperty("JobId").GetGuid());
            }
        }
        finally
        {
            foreach (JobUsageTracker tracker in trackers)
            {
                await tracker.DisposeAsync();
            }
        }
    }

    private static async Task<string> WriteOldLogAsync(string directory, string template)
    {
        Guid id = Guid.NewGuid();
        string path = Path.Combine(directory, $"{id:N}.jsonl");
        JsonObject header = JsonNode.Parse(template)!.AsObject();
        header["JobId"] = id;
        header["Timestamp"] = DateTimeOffset.UtcNow.AddDays(-40);
        await File.WriteAllTextAsync(path, header.ToJsonString() + "\n", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-40));
        return path;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "job-cost-tests", Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Path)) { Directory.Delete(Path, true); } }
    }
}