using System.Security.Cryptography;

namespace StudyReportEvaluator.App.Workbooks.Intake;

public sealed class InputSnapshot : IEquatable<InputSnapshot>
{
    public InputSnapshot(string sha256, long sizeBytes, DateTimeOffset lastWriteTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.", nameof(sha256));
        }

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        }

        if (lastWriteTimeUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Last-write time must use the UTC offset.", nameof(lastWriteTimeUtc));
        }

        Sha256 = sha256.ToUpperInvariant();
        SizeBytes = sizeBytes;
        LastWriteTimeUtc = lastWriteTimeUtc;
    }

    public string Sha256 { get; }

    public long SizeBytes { get; }

    public DateTimeOffset LastWriteTimeUtc { get; }

    public bool Equals(InputSnapshot? other) =>
        other is not null
        && string.Equals(Sha256, other.Sha256, StringComparison.Ordinal)
        && SizeBytes == other.SizeBytes
        && LastWriteTimeUtc.UtcTicks == other.LastWriteTimeUtc.UtcTicks;

    public override bool Equals(object? obj) => Equals(obj as InputSnapshot);

    public override int GetHashCode() => HashCode.Combine(Sha256, SizeBytes, LastWriteTimeUtc.UtcTicks);

    public override string ToString() =>
        $"{nameof(InputSnapshot)} {{ Content = <redacted> }}";
}

public sealed class InputSnapshotComparison
{
    internal InputSnapshotComparison(
        bool sha256Matches,
        bool sizeMatches,
        bool lastWriteTimeUtcMatches)
    {
        Sha256Matches = sha256Matches;
        SizeMatches = sizeMatches;
        LastWriteTimeUtcMatches = lastWriteTimeUtcMatches;
    }

    public bool Sha256Matches { get; }

    public bool SizeMatches { get; }

    public bool LastWriteTimeUtcMatches { get; }

    public bool IsMatch => Sha256Matches && SizeMatches && LastWriteTimeUtcMatches;

    public override string ToString() =>
        $"{nameof(InputSnapshotComparison)} {{ IsMatch = {IsMatch}, Content = <redacted> }}";
}

public sealed class InputSnapshotService
{
    public InputSnapshot Capture(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        long sizeBefore = stream.Length;
            DateTimeOffset lastWriteBefore = ReadLastWriteTimeUtc(stream);
        byte[] hash = SHA256.HashData(stream);
        long sizeAfter = stream.Length;
            DateTimeOffset lastWriteAfter = ReadLastWriteTimeUtc(stream);

        if (sizeBefore != sizeAfter
            || lastWriteBefore.UtcTicks != lastWriteAfter.UtcTicks)
        {
            throw new IOException("Input changed while its snapshot was being captured.");
        }

        return new InputSnapshot(
            Convert.ToHexString(hash),
            sizeAfter,
            lastWriteAfter);
    }

    public InputSnapshotComparison Recheck(string filePath, InputSnapshot expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        InputSnapshot actual = Capture(filePath);
        return new InputSnapshotComparison(
            string.Equals(expected.Sha256, actual.Sha256, StringComparison.Ordinal),
            expected.SizeBytes == actual.SizeBytes,
            expected.LastWriteTimeUtc.UtcTicks == actual.LastWriteTimeUtc.UtcTicks);
    }

    private static DateTimeOffset ReadLastWriteTimeUtc(FileStream stream)
    {
        DateTime value = File.GetLastWriteTimeUtc(stream.SafeFileHandle);
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}