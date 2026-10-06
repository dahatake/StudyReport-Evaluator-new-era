using System.Text.Json;

namespace StudyReportEvaluator.App.Tests.Evidence;

internal static class AdvisoryEvidenceWriter
{
    internal static void Write(
        string fileName,
        string advisory,
        string status,
        string rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(advisory);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        if (Path.GetFileName(fileName) != fileName
            || !fileName.EndsWith(".json", StringComparison.Ordinal))
        {
            throw new ArgumentException("The advisory evidence file name is invalid.", nameof(fileName));
        }

        string repositoryRoot = FindRepositoryRoot();
        string directory = Path.Combine(repositoryRoot, "artifacts", "test");
        Directory.CreateDirectory(directory);
        string finalPath = Path.Combine(directory, fileName);
        string temporaryPath = Path.Combine(
            directory,
            $".{fileName}.{Guid.NewGuid():N}.tmp");
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            new AdvisoryEvidence(
                "study-report-evaluator/advisory-evidence/v1",
                advisory,
                status,
                rationale,
                DateTimeOffset.UtcNow,
                ContentDataIncluded: false,
                GeneratedEvidenceTrackedByGit: false),
            new JsonSerializerOptions { WriteIndented = true });
        try
        {
            using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(json);
                stream.WriteByte((byte)'\n');
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "StudyReportEvaluator.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }

    private sealed record AdvisoryEvidence(
        string Schema,
        string Advisory,
        string Status,
        string Rationale,
        DateTimeOffset EvaluatedAtUtc,
        bool ContentDataIncluded,
        bool GeneratedEvidenceTrackedByGit);
}
