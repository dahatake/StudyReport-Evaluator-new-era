using System.Security.Cryptography;
using System.Text;
using StudyReportEvaluator.App.Workbooks.Intake;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Intake;

// Requirements: FR-007 (AC-008)
public sealed class InputSnapshotServiceTests
{
    private readonly InputSnapshotService service = new();

    [Fact]
    public void Capture_returns_exact_sha256_size_and_utc_last_write_time()
    {
        byte[] content = Encoding.UTF8.GetBytes("fixed synthetic snapshot payload");
        using TemporaryWorkbook file = X01SyntheticWorkbookFactory.CreateRaw(".bin", content);
        DateTime requestedLastWrite = new(2026, 9, 1, 10, 11, 12, 345, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file.Path, requestedLastWrite);
        DateTime actualLastWrite = File.GetLastWriteTimeUtc(file.Path);

        InputSnapshot snapshot = service.Capture(file.Path);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), snapshot.Sha256);
        Assert.Equal(content.LongLength, snapshot.SizeBytes);
        Assert.Equal(TimeSpan.Zero, snapshot.LastWriteTimeUtc.Offset);
        Assert.Equal(actualLastWrite.Ticks, snapshot.LastWriteTimeUtc.UtcTicks);
        Assert.Contains("<redacted>", snapshot.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(snapshot.Sha256, snapshot.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(file.Path, snapshot.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Recheck_reports_all_three_exact_matches_for_unchanged_input()
    {
        using TemporaryWorkbook file = X01SyntheticWorkbookFactory.CreateRaw(
            ".bin",
            Encoding.UTF8.GetBytes("unchanged synthetic payload"));
        InputSnapshot expected = service.Capture(file.Path);

        InputSnapshotComparison comparison = service.Recheck(file.Path, expected);

        Assert.True(comparison.Sha256Matches);
        Assert.True(comparison.SizeMatches);
        Assert.True(comparison.LastWriteTimeUtcMatches);
        Assert.True(comparison.IsMatch);
        Assert.Contains("<redacted>", comparison.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Recheck_detects_hash_drift_even_when_size_and_timestamp_are_restored()
    {
        using TemporaryWorkbook file = X01SyntheticWorkbookFactory.CreateRaw(
            ".bin",
            Encoding.ASCII.GetBytes("AAAA"));
        InputSnapshot expected = service.Capture(file.Path);
        File.WriteAllBytes(file.Path, Encoding.ASCII.GetBytes("BBBB"));
        File.SetLastWriteTimeUtc(file.Path, expected.LastWriteTimeUtc.UtcDateTime);

        InputSnapshotComparison comparison = service.Recheck(file.Path, expected);

        Assert.False(comparison.Sha256Matches);
        Assert.True(comparison.SizeMatches);
        Assert.True(comparison.LastWriteTimeUtcMatches);
        Assert.False(comparison.IsMatch);
    }

    [Fact]
    public void Recheck_detects_size_drift()
    {
        using TemporaryWorkbook file = X01SyntheticWorkbookFactory.CreateRaw(
            ".bin",
            Encoding.ASCII.GetBytes("size"));
        InputSnapshot expected = service.Capture(file.Path);
        File.AppendAllText(file.Path, "-changed", Encoding.ASCII);

        InputSnapshotComparison comparison = service.Recheck(file.Path, expected);

        Assert.False(comparison.SizeMatches);
        Assert.False(comparison.IsMatch);
    }

    [Fact]
    public void Recheck_detects_utc_last_write_drift_with_identical_content()
    {
        using TemporaryWorkbook file = X01SyntheticWorkbookFactory.CreateRaw(
            ".bin",
            Encoding.ASCII.GetBytes("timestamp"));
        InputSnapshot expected = service.Capture(file.Path);
        File.SetLastWriteTimeUtc(
            file.Path,
            expected.LastWriteTimeUtc.UtcDateTime.AddSeconds(2));

        InputSnapshotComparison comparison = service.Recheck(file.Path, expected);

        Assert.True(comparison.Sha256Matches);
        Assert.True(comparison.SizeMatches);
        Assert.False(comparison.LastWriteTimeUtcMatches);
        Assert.False(comparison.IsMatch);
    }

    [Fact]
    public void Capture_works_for_a_read_only_file_and_releases_its_handle()
    {
        using TemporaryWorkbook file = X01SyntheticWorkbookFactory.CreateRaw(
            ".bin",
            Encoding.ASCII.GetBytes("read-only"));
        File.SetAttributes(file.Path, FileAttributes.ReadOnly);
        try
        {
            InputSnapshot snapshot = service.Capture(file.Path);

            Assert.Equal(9, snapshot.SizeBytes);
        }
        finally
        {
            File.SetAttributes(file.Path, FileAttributes.Normal);
        }

        using FileStream writer = new(file.Path, FileMode.Open, FileAccess.Write, FileShare.None);
        Assert.True(writer.CanWrite);
    }

    [Fact]
    public void Snapshot_value_equality_uses_all_three_canonical_fields()
    {
        DateTimeOffset timestamp = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        InputSnapshot first = new(new string('a', 64), 12, timestamp);
        InputSnapshot same = new(new string('A', 64), 12, timestamp);
        InputSnapshot differentTime = new(new string('A', 64), 12, timestamp.AddTicks(1));

        Assert.Equal(first, same);
        Assert.NotEqual(first, differentTime);
    }
}