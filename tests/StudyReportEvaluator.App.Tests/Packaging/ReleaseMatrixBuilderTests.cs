using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Packaging;

// Candidate construction launches nested tools and flushes artifacts to disk.
// Isolate its wall-clock deadline from the large durable workbook E2E load.
// The explicit two-builder concurrency test below still runs both processes.
// Requirements: FR-058 (AC-059)
[Collection<WindowsPublishPackageCollection>]
public sealed class ReleaseMatrixBuilderTests
{
    private const string SkipReason =
        "C03 process tests require Windows, git.exe, and PowerShell Core 7.5 or later.";
    private static readonly Lazy<HostTools?> AvailableTools = new(FindHostTools);

    public static bool RunC03ProcessTests => AvailableTools.Value is not null;

    [Fact]
    public void Builder_fails_closed_on_non_Windows_before_source_or_output_access()
    {
        string script = File.ReadAllText(Path.Combine(
            FindRepositoryRootForTest(),
            "scripts",
            "build-platform-release-matrix.ps1"));
        int guard = script.IndexOf(
            "if (-not $IsWindows) { throw 'C03_UNSUPPORTED_HOST' }",
            StringComparison.Ordinal);
        int sourceAccess = script.IndexOf("$stage = 'source-before'", StringComparison.Ordinal);
        int outputAccess = script.IndexOf("$stage = 'output-lock'", StringComparison.Ordinal);

        Assert.True(guard >= 0, "The explicit Windows-only guard is missing.");
        Assert.True(sourceAccess > guard, "Source access must follow the Windows-only guard.");
        Assert.True(outputAccess > guard, "Output access must follow the Windows-only guard.");
    }

    [Fact(SkipUnless = nameof(RunC03ProcessTests), Skip = SkipReason)]
    public async Task Candidate_generation_is_exact_and_passes_the_public_C02_candidate_CLI()
    {
        using BuilderFixture fixture = new(RequiredTools);

        ProcessResult result = await fixture.BuildCandidateAsync(TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        Assert.Contains("status=PASS_CANDIDATE", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("publicationEligible=false", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("not publication eligible", result.StandardOutput, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError));
        Assert.Equal(
            BuilderFixture.CandidateOutputNames.Order(StringComparer.Ordinal).ToArray(),
            fixture.GetOutputNames());
        Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, BuilderFixture.MatrixFileName)));
        Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, BuilderFixture.CleanHostFileName)));
        AssertStrictUtf8WithoutBom(Path.Combine(fixture.OutputDirectory, BuilderFixture.CandidateFileName));

        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(fixture.OutputDirectory, BuilderFixture.CandidateFileName)));
        JsonElement candidate = document.RootElement;
        Assert.Equal(1, candidate.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("windows-release-candidate", candidate.GetProperty("evidenceKind").GetString());
        Assert.Equal(BuilderFixture.ProductVersion, candidate.GetProperty("productVersion").GetString());
        Assert.Equal(fixture.SourceCommit, candidate.GetProperty("sourceCommit").GetString());
        Assert.Equal(BuilderFixture.Repository, candidate.GetProperty("candidate").GetProperty("repository").GetString());
        Assert.Equal(".github/workflows/release.yml", candidate.GetProperty("candidate").GetProperty("workflow").GetString());
        Assert.Equal(BuilderFixture.RunId, candidate.GetProperty("candidate").GetProperty("runId").GetString());
        Assert.Equal("PASS", candidate.GetProperty("requiredTests").GetString());
        JsonElement[] rows = candidate.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(
            ["windows-singlefile-exe", "windows-zip"],
            rows.Select(row => row.GetProperty("artifactKind").GetString()
                ?? throw new InvalidDataException("Candidate artifact kind is null.")).ToArray());
        Assert.All(rows, row =>
        {
            Assert.False(row.TryGetProperty("platform", out _));
            Assert.False(row.TryGetProperty("runtimeIdentifier", out _));
            Assert.False(row.TryGetProperty("publish", out _));
            Assert.False(row.TryGetProperty("status", out _));
        });

        ProcessResult validation = await fixture.ValidateCandidateAsync(TestContext.Current.CancellationToken);
        AssertSucceeded(validation);
        using JsonDocument validationOutput = JsonDocument.Parse(validation.StandardOutput);
        Assert.Equal("PASS_CANDIDATE", validationOutput.RootElement.GetProperty("status").GetString());
        Assert.Equal("NOT_RUN", validationOutput.RootElement.GetProperty("cleanHostVerification").GetString());
        Assert.Equal(string.Empty, fixture.GetGitStatus());
        fixture.AssertNoTransientDirectories();
    }

    [Theory(SkipUnless = nameof(RunC03ProcessTests), Skip = SkipReason)]
    [InlineData("pass-development")]
    [InlineData("unknown-property")]
    public async Task Candidate_rejects_non_required_or_non_closed_P07_evidence_without_a_usable_output(
        string mutation)
    {
        using BuilderFixture fixture = new(RequiredTools);
        fixture.MutateP07(mutation);

        ProcessResult result = await fixture.BuildCandidateAsync(TestContext.Current.CancellationToken);

        AssertFailed(result);
        Assert.False(Directory.Exists(fixture.OutputDirectory));
        Assert.DoesNotContain(BuilderFixture.SensitiveSentinel, result.StandardError, StringComparison.Ordinal);
        Assert.Equal(string.Empty, fixture.GetGitStatus());
        fixture.AssertNoTransientDirectories();
    }

    [Theory(SkipUnless = nameof(RunC03ProcessTests), Skip = SkipReason)]
    [InlineData("bom-evidence")]
    [InlineData("sidecar-crlf")]
    public async Task Candidate_rejects_non_strict_UTF8_or_sidecar_bytes(string mutation)
    {
        using BuilderFixture fixture = new(RequiredTools);
        fixture.MutateCandidateInputBytes(mutation);

        ProcessResult result = await fixture.BuildCandidateAsync(TestContext.Current.CancellationToken);

        AssertFailed(result);
        Assert.False(Directory.Exists(fixture.OutputDirectory));
        Assert.Equal(string.Empty, fixture.GetGitStatus());
        fixture.AssertNoTransientDirectories();
    }

    [Theory(SkipUnless = nameof(RunC03ProcessTests), Skip = SkipReason)]
    [InlineData("source")]
    [InlineData("repository")]
    [InlineData("run")]
    public async Task Candidate_binds_the_clean_source_and_explicit_identity(string mutation)
    {
        using BuilderFixture fixture = new(RequiredTools);
        string repository = BuilderFixture.Repository;
        string runId = BuilderFixture.RunId;
        if (mutation == "source")
        {
            fixture.MutateP07("source");
        }
        else if (mutation == "repository")
        {
            repository = "https://invalid.example/repository";
        }
        else
        {
            runId = "0";
        }

        ProcessResult result = await fixture.BuildCandidateAsync(
            TestContext.Current.CancellationToken,
            repository,
            runId);

        AssertFailed(result);
        Assert.False(Directory.Exists(fixture.OutputDirectory));
        fixture.AssertNoTransientDirectories();
    }

    [Fact(SkipUnless = nameof(RunC03ProcessTests), Skip = SkipReason)]
    public async Task Candidate_rejects_a_dirty_source_checkout_before_creating_output()
    {
        using BuilderFixture fixture = new(RequiredTools);
        File.AppendAllText(Path.Combine(fixture.Root, "fixture.txt"), "dirty", BuilderFixture.Utf8NoBom);

        ProcessResult result = await fixture.BuildCandidateAsync(TestContext.Current.CancellationToken);

        AssertFailed(result);
        Assert.Contains("C03_SOURCE_NOT_CLEAN", result.StandardError, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.OutputDirectory));
        fixture.AssertNoTransientDirectories();
    }

    [Fact(SkipUnless = nameof(RunC03ProcessTests), Skip = SkipReason)]
    public async Task Concurrent_candidate_builders_for_one_output_are_serialized_and_remain_valid()
    {
        using BuilderFixture fixture = new(RequiredTools);

        Task<ProcessResult> first = fixture.BuildCandidateAsync(TestContext.Current.CancellationToken);
        Task<ProcessResult> second = fixture.BuildCandidateAsync(TestContext.Current.CancellationToken);
        ProcessResult[] results = await Task.WhenAll(first, second);

        Assert.All(results, AssertSucceeded);
        Assert.All(results, result =>
            Assert.Contains("status=PASS_CANDIDATE", result.StandardOutput, StringComparison.Ordinal));
        AssertSucceeded(await fixture.ValidateCandidateAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            BuilderFixture.CandidateOutputNames.Order(StringComparer.Ordinal).ToArray(),
            fixture.GetOutputNames());
        Assert.Equal(string.Empty, fixture.GetGitStatus());
        fixture.AssertNoTransientDirectories();
    }

    [Fact(SkipUnless = nameof(RunC03ProcessTests), Skip = SkipReason)]
    public async Task Final_generation_projects_exact_rows_and_passes_C02()
    {
        using BuilderFixture fixture = new(RequiredTools);
        AssertSucceeded(await fixture.BuildCandidateAsync(TestContext.Current.CancellationToken));
        byte[] candidateBytes = File.ReadAllBytes(
            Path.Combine(fixture.OutputDirectory, BuilderFixture.CandidateFileName));
        fixture.WriteCleanHostEvidence();
        byte[] cleanHostBytes = File.ReadAllBytes(fixture.CleanHostEvidencePath);

        ProcessResult result = await fixture.BuildFinalAsync(TestContext.Current.CancellationToken);

        AssertSucceeded(result);
        Assert.Contains("status=PASS", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("upstreamCandidateRunVerificationRequired=true", result.StandardOutput, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError));
        Assert.Equal(
            BuilderFixture.FinalOutputNames.Order(StringComparer.Ordinal).ToArray(),
            fixture.GetOutputNames());
        Assert.Equal(candidateBytes, File.ReadAllBytes(
            Path.Combine(fixture.OutputDirectory, BuilderFixture.CandidateFileName)));
        Assert.Equal(cleanHostBytes, File.ReadAllBytes(
            Path.Combine(fixture.OutputDirectory, BuilderFixture.CleanHostFileName)));
        AssertStrictUtf8WithoutBom(Path.Combine(fixture.OutputDirectory, BuilderFixture.MatrixFileName));

        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(fixture.OutputDirectory, BuilderFixture.MatrixFileName)));
        JsonElement matrix = document.RootElement;
        Assert.Equal(2, matrix.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(fixture.SourceCommit, matrix.GetProperty("sourceCommit").GetString());
        AssertDescriptor(
            matrix.GetProperty("candidateRecord"),
            Path.Combine(fixture.OutputDirectory, BuilderFixture.CandidateFileName));
        AssertDescriptor(
            matrix.GetProperty("cleanHostEvidence"),
            Path.Combine(fixture.OutputDirectory, BuilderFixture.CleanHostFileName));
        JsonElement[] rows = matrix.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        AssertRow(rows[0], "windows-singlefile-exe", publish: true, "PASS_REQUIRED");
        AssertRow(rows[1], "windows-zip", publish: true, "PASS_REQUIRED");

        ProcessResult validation = await fixture.ValidateFinalAsync(TestContext.Current.CancellationToken);
        AssertSucceeded(validation);
        using JsonDocument validationOutput = JsonDocument.Parse(validation.StandardOutput);
        Assert.Equal("PASS", validationOutput.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, validationOutput.RootElement.GetProperty("rowCount").GetInt32());
        Assert.False(validationOutput.RootElement.TryGetProperty("developmentMsixVerification", out _));
        Assert.Equal("HUMAN_RECORDED", validationOutput.RootElement.GetProperty("cleanHostVerification").GetString());
        Assert.Equal(string.Empty, fixture.GetGitStatus());
        fixture.AssertNoTransientDirectories();
    }

    [Theory(SkipUnless = nameof(RunC03ProcessTests), Skip = SkipReason)]
    [InlineData("NOT_RUN")]
    [InlineData("FAIL")]
    public async Task Final_rejects_required_clean_host_non_pass_and_preserves_prior_candidate_output(
        string status)
    {
        using BuilderFixture fixture = new(RequiredTools);
        AssertSucceeded(await fixture.BuildCandidateAsync(TestContext.Current.CancellationToken));
        fixture.WriteCleanHostEvidence(status);
        string[] before = fixture.SnapshotOutput();

        ProcessResult result = await fixture.BuildFinalAsync(TestContext.Current.CancellationToken);

        AssertFailed(result);
        Assert.Equal(before, fixture.SnapshotOutput());
        Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, BuilderFixture.MatrixFileName)));
        Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, BuilderFixture.CleanHostFileName)));
        Assert.Equal(string.Empty, fixture.GetGitStatus());
        fixture.AssertNoTransientDirectories();
    }

    [Theory(SkipUnless = nameof(RunC03ProcessTests), Skip = SkipReason)]
    [InlineData("repository")]
    [InlineData("run")]
    public async Task Final_rejects_caller_identity_different_from_the_candidate_and_preserves_output(
        string changed)
    {
        using BuilderFixture fixture = new(RequiredTools);
        AssertSucceeded(await fixture.BuildCandidateAsync(TestContext.Current.CancellationToken));
        fixture.WriteCleanHostEvidence();
        string[] before = fixture.SnapshotOutput();

        ProcessResult result = await fixture.BuildFinalAsync(
            TestContext.Current.CancellationToken,
            changed == "repository" ? "other-owner/other-repository" : BuilderFixture.Repository,
            changed == "run" ? "123" : BuilderFixture.RunId);

        AssertFailed(result);
        Assert.Equal(before, fixture.SnapshotOutput());
        fixture.AssertNoTransientDirectories();
    }

    [Fact(SkipUnless = nameof(RunC03ProcessTests), Skip = SkipReason)]
    public async Task Candidate_never_synthesizes_clean_host_and_final_requires_the_received_record()
    {
        using BuilderFixture fixture = new(RequiredTools);
        AssertSucceeded(await fixture.BuildCandidateAsync(TestContext.Current.CancellationToken));
        string[] before = fixture.SnapshotOutput();

        ProcessResult result = await fixture.BuildFinalWithoutCleanHostAsync(
            TestContext.Current.CancellationToken);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.False(string.IsNullOrWhiteSpace(result.StandardError));
        Assert.Equal(before, fixture.SnapshotOutput());
        Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, BuilderFixture.CleanHostFileName)));
        Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, BuilderFixture.MatrixFileName)));
        fixture.AssertNoTransientDirectories();
    }

    [Theory(SkipUnless = nameof(RunC03ProcessTests), Skip = SkipReason)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PowerShell_process_preserves_UTF8_output_and_parameter_binding_errors(
        bool rejectParameter)
    {
        using BuilderFixture fixture = new(RequiredTools);
        const string message = "あいう 日本語 'quoted'; $literal";
        string scriptPath = Path.Combine(fixture.PackageDirectory, "日本語 probe's.ps1");
        File.WriteAllText(scriptPath, """
            [CmdletBinding()]
            param(
                [ValidateScript({
                    if ($_ -eq 'reject') { throw 'あいう 引数検証エラー' }
                    $true
                })]
                [string] $Message
            )
            if ([Console]::OutputEncoding.CodePage -ne 65001) { throw 'EXPECTED_UTF8' }
            [Console]::Out.Write($Message)
            [Console]::Error.Write($Message)
            exit 0
            """, BuilderFixture.Utf8NoBom);

        ProcessResult result = await fixture.RunPowerShellAsync(
            scriptPath,
            ["-Message", rejectParameter ? "reject" : message],
            TestContext.Current.CancellationToken);

        if (rejectParameter)
        {
            Assert.NotEqual(0, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
            Assert.Contains("あいう 引数検証エラー", result.StandardError, StringComparison.Ordinal);
        }
        else
        {
            AssertSucceeded(result);
            Assert.Equal(message, result.StandardOutput);
            Assert.Equal(message, result.StandardError);
        }
        Assert.Equal(string.Empty, fixture.GetGitStatus());
        fixture.AssertNoTransientDirectories();
    }

    [Fact(SkipUnless = nameof(RunC03ProcessTests), Skip = SkipReason)]
    public async Task Final_committed_validation_failure_restores_the_exact_prior_candidate_output()
    {
        using BuilderFixture fixture = new(RequiredTools, failFinalAfterInstall: true);
        AssertSucceeded(await fixture.BuildCandidateAsync(TestContext.Current.CancellationToken));
        fixture.WriteCleanHostEvidence();
        string[] before = fixture.SnapshotOutput();

        ProcessResult result = await fixture.BuildFinalAsync(TestContext.Current.CancellationToken);

        AssertFailed(result);
        Assert.Contains("stage=committed-validation", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("code=C03_TEST_COMMITTED_VALIDATION_FAULT", result.StandardError, StringComparison.Ordinal);
        Assert.Equal(before, fixture.SnapshotOutput());
        Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, BuilderFixture.MatrixFileName)));
        Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, BuilderFixture.CleanHostFileName)));
        Assert.Equal(string.Empty, fixture.GetGitStatus());
        fixture.AssertNoTransientDirectories();
    }

    private static HostTools RequiredTools =>
        AvailableTools.Value ?? throw new InvalidOperationException(SkipReason);

    private static string FindRepositoryRootForTest()
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
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static void AssertRow(
        JsonElement row,
        string kind,
        bool publish,
        string status)
    {
        Assert.Equal(kind, row.GetProperty("artifactKind").GetString());
        Assert.Equal("windows", row.GetProperty("platform").GetString());
        Assert.Equal("win-x64", row.GetProperty("runtimeIdentifier").GetString());
        Assert.Equal(publish, row.GetProperty("publish").GetBoolean());
        Assert.Equal(status, row.GetProperty("status").GetString());
    }

    private static void AssertDescriptor(JsonElement descriptor, string path)
    {
        FileInfo file = new(path);
        Assert.Equal(file.Name, descriptor.GetProperty("fileName").GetString());
        Assert.Equal(file.Length, descriptor.GetProperty("bytes").GetInt64());
        Assert.Equal(ComputeSha256(path), descriptor.GetProperty("sha256").GetString());
    }

    private static void AssertStrictUtf8WithoutBom(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        _ = BuilderFixture.Utf8NoBom.GetString(bytes);
    }

    private static void AssertSucceeded(ProcessResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            $"Process failed with exit code {result.ExitCode}.\nSTDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
    }

    private static void AssertFailed(ProcessResult result)
    {
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Contains("C03_", result.StandardError, StringComparison.Ordinal);
    }

    private static HostTools? FindHostTools()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string? powerShell = FindOnPath("pwsh.exe");
        string? git = FindOnPath("git.exe");
        if (powerShell is null || git is null)
        {
            return null;
        }

        try
        {
            ProcessStartInfo info = new()
            {
                FileName = powerShell,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add("-NoLogo");
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("$PSVersionTable.PSVersion.ToString()");
            using Process process = new() { StartInfo = info };
            if (!process.Start())
            {
                return null;
            }
            Stopwatch watch = Stopwatch.StartNew();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                KillIfRunning(process);
                _ = process.WaitForExit(5_000);
                _ = Task.WaitAll([outputTask, errorTask], TimeSpan.FromSeconds(5));
                return null;
            }

            int remainingMilliseconds = Math.Max(
                0,
                10_000 - checked((int)Math.Min(watch.ElapsedMilliseconds, int.MaxValue)));
            if (!Task.WaitAll([outputTask, errorTask], remainingMilliseconds) ||
                process.ExitCode != 0 ||
                outputTask.IsFaulted ||
                errorTask.IsFaulted ||
                !Version.TryParse(outputTask.Result.Trim(), out Version? version) ||
                version < new Version(7, 5))
            {
                KillIfRunning(process);
                _ = process.WaitForExit(5_000);
                _ = Task.WaitAll([outputTask, errorTask], TimeSpan.FromSeconds(5));
                return null;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or AggregateException
                or IOException
                or ObjectDisposedException)
        {
            return null;
        }

        return new HostTools(powerShell, git);
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string? FindOnPath(string executableName)
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate = Path.Combine(directory.Trim('"'), executableName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class BuilderFixture : IDisposable
    {
        internal const string ProductVersion = "0.8.0";
        internal const string Repository = "fixture-owner/fixture-repository";
        internal const string RunId = "98765432109876543210";
        internal const string CandidateFileName = "release-candidate-record.json";
        internal const string CleanHostFileName = "windows-singlefile-clean-host.evidence.json";
        internal const string MatrixFileName = "platform-release-matrix.json";
        internal const string ExeFileName = "StudyReportEvaluator-win-x64.exe";
        internal const string ExeSidecarFileName = ExeFileName + ".sha256";
        internal const string ExeEvidenceFileName = ExeFileName + ".evidence.json";
        internal const string ZipFileName = "StudyReportEvaluator-win-x64.zip";
        internal const string ZipSidecarFileName = ZipFileName + ".sha256";
        internal const string ZipEvidenceFileName = "StudyReportEvaluator-win-x64.evidence.json";
        internal const string SensitiveSentinel = "SYNTHETIC_CREDENTIAL_MUST_NOT_BE_PRINTED";
        internal const string EmptyUtf8Sha256 =
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
        private const string CliEntryName = "runtimes/win-x64/native/copilot.exe";
        private static readonly byte[] CliBytes = [0x43, 0x4C, 0x49, 0x31];
        internal static readonly UTF8Encoding Utf8NoBom = new(false, true);
        internal static readonly string[] CandidateOutputNames =
        [
            CandidateFileName,
            ExeEvidenceFileName,
            ExeFileName,
            ExeSidecarFileName,
            ZipEvidenceFileName,
            ZipFileName,
            ZipSidecarFileName,
        ];
        internal static readonly string[] FinalOutputNames =
        [
            CandidateFileName,
            CleanHostFileName,
            ExeEvidenceFileName,
            ExeFileName,
            ExeSidecarFileName,
            MatrixFileName,
            ZipEvidenceFileName,
            ZipFileName,
            ZipSidecarFileName,
        ];

        private readonly HostTools tools;
        private readonly string builderPath;
        private readonly string validatorPath;
        private bool disposed;

        internal BuilderFixture(HostTools tools, bool failFinalAfterInstall = false)
        {
            this.tools = tools;
            Root = Path.Combine(
                Path.GetTempPath(),
                "StudyReportEvaluator-C03-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Root);
                string sourceRoot = FindRepositoryRoot();
                CopyRepositoryFile(sourceRoot, "scripts", "build-platform-release-matrix.ps1");
                CopyRepositoryFile(sourceRoot, "scripts", "validate-platform-release-matrix.ps1");
                CopyRepositoryFile(sourceRoot, "eng", "schemas", "platform-release-matrix-v2.schema.json");
                CopyRepositoryFile(sourceRoot, "dev", "version.ps1");
                File.WriteAllText(
                    Path.Combine(Root, "Directory.Build.props"),
                    $"<Project><PropertyGroup><VersionPrefix>{ProductVersion}</VersionPrefix>" +
                    "<VersionSuffix></VersionSuffix></PropertyGroup></Project>\n",
                    Utf8NoBom);
                File.WriteAllText(Path.Combine(Root, ".gitignore"), "/artifacts/\n", Utf8NoBom);
                File.WriteAllText(Path.Combine(Root, "fixture.txt"), "tracked\n", Utf8NoBom);
                builderPath = Path.Combine(Root, "scripts", "build-platform-release-matrix.ps1");
                validatorPath = Path.Combine(Root, "scripts", "validate-platform-release-matrix.ps1");
                if (failFinalAfterInstall)
                {
                    InjectFinalCommittedValidationFault();
                }

                RunGit("init", "--quiet");
                RunGit("config", "user.name", "C03 Fixture");
                RunGit("config", "user.email", "c03-fixture@example.invalid");
                RunGit("config", "commit.gpgsign", "false");
                RunGit("config", "core.autocrlf", "false");
                RunGit("config", "core.hooksPath", ".git/no-hooks");
                RunGit("add", "--all");
                RunGit("commit", "--quiet", "-m", "C03 fixture");
                SourceCommit = RunGit("rev-parse", "HEAD").StandardOutput.Trim();
                Assert.Matches("^[0-9a-f]{40}$", SourceCommit);

                PackageDirectory = Path.Combine(Root, "artifacts", "package");
                OutputDirectory = Path.Combine(PackageDirectory, "matrix");
                string cleanDirectory = Path.Combine(Root, "artifacts", "clean-host");
                Directory.CreateDirectory(PackageDirectory);
                Directory.CreateDirectory(cleanDirectory);
                CleanHostEvidencePath = Path.Combine(cleanDirectory, CleanHostFileName);
                WritePackageInputs();
                Assert.Equal(string.Empty, GetGitStatus());
            }
            catch
            {
                TryDeleteRoot();
                throw;
            }
        }

        internal string Root { get; }
        internal string SourceCommit { get; }
        internal string PackageDirectory { get; }
        internal string OutputDirectory { get; }
        internal string CleanHostEvidencePath { get; }

        internal Task<ProcessResult> BuildCandidateAsync(
            CancellationToken cancellationToken,
            string repository = Repository,
            string runId = RunId) =>
            RunPowerShellAsync(
                builderPath,
                [
                    "-Mode", "Candidate",
                    "-ExpectedRepository", repository,
                    "-CandidateRunId", runId,
                    "-SingleFileDirectory", PackageDirectory,
                    "-ZipDirectory", PackageDirectory,
                    "-OutputDirectory", OutputDirectory,
                ],
                cancellationToken);

        internal Task<ProcessResult> BuildFinalAsync(
            CancellationToken cancellationToken,
            string repository = Repository,
            string runId = RunId) =>
            RunPowerShellAsync(
                builderPath,
                [
                    "-Mode", "Final",
                    "-ExpectedRepository", repository,
                    "-CandidateRunId", runId,
                    "-CandidateRecordPath", Path.Combine(OutputDirectory, CandidateFileName),
                    "-CleanHostEvidencePath", CleanHostEvidencePath,
                    "-OutputDirectory", OutputDirectory,
                ],
                cancellationToken);

        internal Task<ProcessResult> BuildFinalWithoutCleanHostAsync(CancellationToken cancellationToken) =>
            RunPowerShellAsync(
                builderPath,
                [
                    "-Mode", "Final",
                    "-ExpectedRepository", Repository,
                    "-CandidateRunId", RunId,
                    "-CandidateRecordPath", Path.Combine(OutputDirectory, CandidateFileName),
                    "-OutputDirectory", OutputDirectory,
                ],
                cancellationToken);

        internal Task<ProcessResult> ValidateCandidateAsync(CancellationToken cancellationToken) =>
            RunPowerShellAsync(
                validatorPath,
                [
                    "-CandidateRecordPath", Path.Combine(OutputDirectory, CandidateFileName),
                    "-ArtifactDirectory", OutputDirectory,
                    "-ExpectedProductVersion", ProductVersion,
                    "-ExpectedSourceCommit", SourceCommit,
                    "-ExpectedRepository", Repository,
                    "-ExpectedCandidateRunId", RunId,
                ],
                cancellationToken);

        internal Task<ProcessResult> ValidateFinalAsync(CancellationToken cancellationToken) =>
            RunPowerShellAsync(
                validatorPath,
                [
                    "-MatrixPath", Path.Combine(OutputDirectory, MatrixFileName),
                    "-ArtifactDirectory", OutputDirectory,
                    "-ExpectedProductVersion", ProductVersion,
                    "-ExpectedSourceCommit", SourceCommit,
                    "-ExpectedRepository", Repository,
                    "-ExpectedCandidateRunId", RunId,
                ],
                cancellationToken);

        internal string[] GetOutputNames() => Directory.GetFileSystemEntries(OutputDirectory)
            .Select(path => Path.GetFileName(path)
                ?? throw new InvalidDataException("Output entry has no basename."))
            .Order(StringComparer.Ordinal)
            .ToArray();

        internal string[] SnapshotOutput() => Directory.GetFiles(OutputDirectory)
            .Order(StringComparer.Ordinal)
            .Select(path =>
            {
                FileInfo file = new(path);
                return $"{file.Name}:{file.Length.ToString(CultureInfo.InvariantCulture)}:" +
                    $"{file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)}:{ComputeSha256(path)}";
            })
            .ToArray();

        internal string GetGitStatus() => RunGit(
            "status",
            "--porcelain=v1",
            "--untracked-files=all").StandardOutput;

        internal void AssertNoTransientDirectories()
        {
            string[] transient = Directory.GetDirectories(PackageDirectory)
                .Where(path => Path.GetFileName(path).StartsWith(".matrix.c03-", StringComparison.Ordinal))
                .ToArray();
            Assert.Empty(transient);
        }

        internal void MutateP07(string mutation)
        {
            string path = Path.Combine(PackageDirectory, ExeEvidenceFileName);
            JsonObject evidence = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
            switch (mutation)
            {
                case "pass-development":
                    evidence["status"] = "PASS_DEVELOPMENT";
                    break;
                case "unknown-property":
                    evidence["accessToken"] = SensitiveSentinel;
                    break;
                case "source":
                    evidence["sourceCommit"] = new string('b', 40);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            WriteJson(path, evidence);
        }

        internal void MutateCandidateInputBytes(string mutation)
        {
            switch (mutation)
            {
                case "bom-evidence":
                    string evidencePath = Path.Combine(PackageDirectory, ExeEvidenceFileName);
                    File.WriteAllBytes(evidencePath, [0xEF, 0xBB, 0xBF, .. File.ReadAllBytes(evidencePath)]);
                    break;
                case "sidecar-crlf":
                    string sidecarPath = Path.Combine(PackageDirectory, ExeSidecarFileName);
                    File.WriteAllText(
                        sidecarPath,
                        $"{ComputeSha256(Path.Combine(PackageDirectory, ExeFileName))}  {ExeFileName}\r\n",
                        Utf8NoBom);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }
        }

        internal void WriteCleanHostEvidence(string requiredStatus = "PASS")
        {
            JsonArray tests = [];
            for (int index = 1; index <= 6; index++)
            {
                string id = "CH-" + index.ToString("D2", CultureInfo.InvariantCulture);
                bool mutate = index == 3 && requiredStatus != "PASS";
                tests.Add(new JsonObject
                {
                    ["id"] = id,
                    ["status"] = mutate ? requiredStatus : "PASS",
                    ["operations"] = mutate && requiredStatus == "NOT_RUN" ? 0 : 1,
                    ["record"] = mutate && requiredStatus == "NOT_RUN" ? null : Observation(id + ".txt"),
                });
            }
            tests.Add(new JsonObject
            {
                ["id"] = "ADV-01", ["status"] = "NOT_RUN", ["operations"] = 0, ["record"] = null,
            });
            tests.Add(new JsonObject
            {
                ["id"] = "ADV-02", ["status"] = "NOT_RUN", ["operations"] = 0, ["record"] = null,
            });

            JsonObject cleanHost = new()
            {
                ["schemaVersion"] = 1,
                ["evidenceKind"] = "windows-singlefile-clean-host",
                ["measuredAtUtc"] = DateTime.UtcNow.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture),
                ["productVersion"] = ProductVersion,
                ["sourceCommit"] = SourceCommit,
                ["candidate"] = Identity(),
                ["package"] = Descriptor(Path.Combine(OutputDirectory, ExeFileName)),
                ["runtime"] = Runtime(),
                ["host"] = new JsonObject
                {
                    ["osName"] = "Windows 11",
                    ["edition"] = "Pro",
                    ["osVersion"] = "10.0.26200.0",
                    ["osBuild"] = 26200,
                    ["architecture"] = "X64",
                    ["standardUser"] = true,
                    ["fresh"] = true,
                    ["additionalDependencies"] = new JsonObject
                    {
                        ["dotnetSdk"] = false,
                        ["dotnetRuntime"] = false,
                        ["powerShell6Plus"] = false,
                        ["nodeNpm"] = false,
                        ["git"] = false,
                        ["githubCli"] = false,
                        ["copilotCli"] = false,
                        ["office"] = false,
                        ["ide"] = false,
                    },
                },
                ["protection"] = new JsonObject
                {
                    ["motw"] = true,
                    ["smartScreen"] = "Enabled",
                    ["smartAppControl"] = "Off",
                    ["enterprisePolicy"] = "NotConfigured",
                    ["settingsUnchanged"] = true,
                    ["launchOutcome"] = "Allowed",
                    ["launchGestures"] = 1,
                    ["warningActions"] = 0,
                    ["withinApprovedScope"] = true,
                },
                ["measurements"] = new JsonObject
                {
                    ["extractedBytes"] = 1234,
                    ["coldStartMilliseconds"] = 20,
                    ["warmStartMilliseconds"] = 10,
                },
                ["tests"] = tests,
            };
            WriteJson(CleanHostEvidencePath, cleanHost);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            DeleteRoot();
            Assert.False(Directory.Exists(Root));
        }

        private void WritePackageInputs()
        {
            File.WriteAllBytes(Path.Combine(PackageDirectory, ExeFileName),
                [0x4D, 0x5A, 0x43, 0x30, 0x33, 0x46, 0x49, 0x58, 0x54, 0x55, 0x52, 0x45]);
            WriteSidecar(PackageDirectory, ExeFileName);
            WriteZip();
            WriteSidecar(PackageDirectory, ZipFileName);

            JsonObject packageHost = new()
            {
                ["osName"] = "Windows",
                ["osVersion"] = "10.0.26100.0",
                ["osBuild"] = 26100,
                ["osArchitecture"] = "X64",
                ["processArchitecture"] = "X64",
            };
            WriteJson(
                Path.Combine(PackageDirectory, ExeEvidenceFileName),
                CreateP07Evidence(packageHost));
            WriteJson(
                Path.Combine(PackageDirectory, ZipEvidenceFileName),
                CreateZipEvidence());
        }

        private JsonObject CreateP07Evidence(JsonObject host)
        {
            string[] methods =
            [
                "Single_file_cold_and_warm_start_validate_payload_and_show_input",
                "Single_file_missing_cached_CLI_is_recovered_by_the_standard_host",
                "Single_file_deleted_cache_is_reextracted_after_moving_the_EXE",
                "Single_file_two_instances_share_cache_and_close_independently",
                "Single_file_relative_input_and_two_prompts_are_observed_in_GUI_order",
                "Single_file_file_valued_extraction_base_fails_without_changing_data",
                "Single_file_truncated_bundle_reports_a_host_error_without_changing_data",
            ];
            return new JsonObject
            {
                ["schemaVersion"] = 1,
                ["evidenceKind"] = "windows-singlefile-required",
                ["status"] = "PASS_REQUIRED",
                ["sourceCommit"] = SourceCommit,
                ["sourceStatusEntryCount"] = 0,
                ["sourceStatusSha256"] = EmptyUtf8Sha256,
                ["sourceContentSha256"] = new string('B', 64),
                ["productVersion"] = ProductVersion,
                ["host"] = host,
                ["package"] = Descriptor(Path.Combine(PackageDirectory, ExeFileName)),
                ["runtime"] = Runtime(),
                ["tests"] = new JsonObject
                {
                    ["total"] = 7,
                    ["passed"] = 7,
                    ["failed"] = 0,
                    ["skipped"] = 0,
                    ["results"] = new JsonArray(methods.Select(method => (JsonNode)new JsonObject
                    {
                        ["id"] = "StudyReportEvaluator.App.Tests.Packaging.WindowsSingleFilePackageTests." + method,
                        ["status"] = "PASS",
                    }).ToArray()),
                    ["trx"] = Observation("windows-singlefile.trx"),
                },
                ["checks"] = new JsonObject
                {
                    ["singleFileVerified"] = true,
                    ["sidecarVerified"] = true,
                    ["safeLayoutVerified"] = true,
                    ["bundledCliVerified"] = true,
                    ["documentationVerified"] = true,
                    ["guiLaunchVerified"] = true,
                    ["restartAndConcurrencyVerified"] = true,
                    ["inputUnchangedVerified"] = true,
                },
                ["limitations"] = new JsonArray(
                    "Development-host observations are not clean-host evidence.",
                    "Network isolation, personal authentication and live AI were not run.",
                    "Disk-full, interrupted extraction, directory ACL faults and cross-format checkpoint resume were not run."),
            };
        }

        private JsonObject CreateZipEvidence()
        {
            return new JsonObject
            {
                ["schemaVersion"] = 1,
                ["evidenceKind"] = "windows-zip-required",
                ["status"] = "PASS_REQUIRED",
                ["sourceCommit"] = SourceCommit,
                ["sourceStatusEntryCount"] = 0,
                ["sourceStatusSha256"] = EmptyUtf8Sha256,
                ["productVersion"] = ProductVersion,
                ["host"] = new JsonObject
                {
                    ["osName"] = "Windows 11",
                    ["osVersion"] = "10.0.26100.0",
                    ["osBuild"] = 26100,
                    ["osArchitecture"] = "X64",
                    ["processArchitecture"] = "X64",
                },
                ["package"] = Descriptor(Path.Combine(PackageDirectory, ZipFileName)),
                ["checks"] = new JsonObject
                {
                    ["sidecarVerified"] = true,
                    ["safeLayoutVerified"] = true,
                    ["bundledCliVerified"] = true,
                    ["cleanExtractVerified"] = true,
                    ["apphostLaunchVerified"] = true,
                    ["externalRuntimeAbsentVerified"] = true,
                    ["userWorkbookExcluded"] = true,
                    ["inputUnchangedVerified"] = true,
                },
            };
        }

        private void WriteZip()
        {
            using FileStream file = File.Create(Path.Combine(PackageDirectory, ZipFileName));
            using ZipArchive archive = new(file, ZipArchiveMode.Create);
            JsonObject manifest = new()
            {
                ["schemaVersion"] = 1,
                ["runtimeIdentifier"] = "win-x64",
                ["cliVersion"] = "1.0.79",
                ["cliSha256"] = CliHash,
                ["sdkVersion"] = "1.0.11",
                ["cliRelativePath"] = CliEntryName,
            };
            using (Stream entry = archive.CreateEntry(
                       "StudyReportEvaluator-win-x64/copilot-runtime.json",
                       CompressionLevel.NoCompression).Open())
            {
                entry.Write(Utf8NoBom.GetBytes(manifest.ToJsonString()));
            }
            using (Stream entry = archive.CreateEntry(
                       "StudyReportEvaluator-win-x64/" + CliEntryName,
                       CompressionLevel.NoCompression).Open())
            {
                entry.Write(CliBytes);
            }
        }

        private void WriteSidecar(string directory, string artifactName)
        {
            string artifactPath = Path.Combine(directory, artifactName);
            File.WriteAllText(
                Path.Combine(directory, artifactName + ".sha256"),
                $"{ComputeSha256(artifactPath)}  {artifactName}\n",
                Utf8NoBom);
        }

        private void WriteJson(string path, JsonNode value)
        {
            File.WriteAllText(
                path,
                value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
                Utf8NoBom);
        }

        private static JsonObject Descriptor(string path)
        {
            FileInfo file = new(path);
            return new JsonObject
            {
                ["fileName"] = file.Name,
                ["bytes"] = file.Length,
                ["sha256"] = ComputeSha256(path),
            };
        }

        private static JsonObject Runtime() => new()
        {
            ["dotnetSdk"] = "10.0.400",
            ["dotnetRuntime"] = "10.0.11",
            ["copilotSdk"] = "1.0.11",
            ["cliVersion"] = "1.0.79",
            ["cliSha256"] = CliHash,
        };

        private static JsonObject Identity() => new()
        {
            ["repository"] = Repository,
            ["workflow"] = ".github/workflows/release.yml",
            ["runId"] = RunId,
        };

        private static JsonObject Observation(string fileName) => new()
        {
            ["fileName"] = fileName,
            ["bytes"] = 42,
            ["sha256"] = new string('A', 64),
        };

        private static string CliHash => Convert.ToHexString(SHA256.HashData(CliBytes));

        private void CopyRepositoryFile(string sourceRoot, params string[] parts)
        {
            string relative = Path.Combine(parts);
            string source = Path.Combine(sourceRoot, relative);
            string destination = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)
                ?? throw new InvalidDataException("Fixture destination has no parent."));
            File.Copy(source, destination);
        }

        private void InjectFinalCommittedValidationFault()
        {
            const string target = "        $stage = 'committed-validation'";
            const string replacement =
                "        $stage = 'committed-validation'\r\n" +
                "        if ($Mode -ceq 'Final') { throw 'C03_TEST_COMMITTED_VALIDATION_FAULT' }";
            string script = File.ReadAllText(builderPath);
            Assert.Equal(script.IndexOf(target, StringComparison.Ordinal),
                script.LastIndexOf(target, StringComparison.Ordinal));
            Assert.Contains(target, script, StringComparison.Ordinal);
            File.WriteAllText(
                builderPath,
                script.Replace(target, replacement, StringComparison.Ordinal),
                Utf8NoBom);
        }

        private ProcessResult RunGit(params string[] arguments)
        {
            ProcessStartInfo info = new()
            {
                FileName = tools.GitPath,
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };
            foreach (string name in info.Environment.Keys
                         .Where(name => name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                info.Environment.Remove(name);
            }
            info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            info.ArgumentList.Add("-C");
            info.ArgumentList.Add(Root);
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }
            using Process process = new() { StartInfo = info };
            Assert.True(process.Start());
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(30_000), "git fixture command timed out.");
            Task.WaitAll([output, error], TimeSpan.FromSeconds(10));
            ProcessResult result = new(process.ExitCode, output.Result, error.Result);
            Assert.True(
                result.ExitCode == 0,
                $"git fixture command failed.\nSTDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
            return result;
        }

        internal async Task<ProcessResult> RunPowerShellAsync(
            string scriptPath,
            IEnumerable<string> arguments,
            CancellationToken cancellationToken)
        {
            ProcessStartInfo info = new()
            {
                FileName = tools.PowerShellPath,
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };
            foreach (string argument in new[]
                     {
                         "-NoLogo", "-NoProfile", "-NonInteractive", "-CommandWithArgs",
                         // Configure the producer before script parameter binding can emit a
                         // localized error. ProcessStartInfo only configures our strict readers.
                         // Keep paths and values as arguments, never interpolated PowerShell code.
                         "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false, $true); " +
                         "$ErrorActionPreference = 'Stop'; $scriptPath = $args[0]; $parameters = @{}; " +
                         "for ($i = 1; $i -lt $args.Count; $i += 2) { " +
                         "$parameters.Add($args[$i].TrimStart('-'), $args[$i + 1]) }; " +
                         "& $scriptPath @parameters; if (-not $?) { exit 1 }",
                         scriptPath,
                     })
            {
                info.ArgumentList.Add(argument);
            }
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = info };
            Assert.True(process.Start());
            Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("C03 process test timed out.");
            }
            finally
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException) when (process.HasExited)
                    {
                    }
                    using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(10));
                    await process.WaitForExitAsync(cleanup.Token);
                }
                await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }
            return new ProcessResult(process.ExitCode, await output, await error);
        }

        private void TryDeleteRoot()
        {
            try
            {
                DeleteRoot();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void DeleteRoot()
        {
            if (!Directory.Exists(Root))
            {
                return;
            }
            foreach (string entry in Directory.EnumerateFileSystemEntries(
                         Root,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(entry, FileAttributes.Normal);
            }
            File.SetAttributes(Root, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
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
            throw new DirectoryNotFoundException("Repository root was not found.");
        }
    }

    private sealed record HostTools(string PowerShellPath, string GitPath);

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
