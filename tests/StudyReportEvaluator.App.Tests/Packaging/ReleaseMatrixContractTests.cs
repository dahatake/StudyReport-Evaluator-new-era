using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Packaging;

// Requirements: FR-058 (AC-059)
public sealed class ReleaseMatrixContractTests
{
    // These are structural/byte-binding fixtures, not launch, PE integrity,
    // trusted GitHub-run, personal authentication or clean-host execution proof.
    // Every invocation runs the real validator via its public pwsh CLI contract.
    [Fact]
    public async Task V2_final_matrix_binds_four_assets_without_raw_records()
    {
        using V2Fixture fixture = new();
        Assert.False(File.Exists(fixture.Resolve("windows-singlefile.trx")));
        Assert.False(File.Exists(fixture.Resolve("CH-01.txt")));
        string[] before = fixture.Snapshot();

        ProcessResult result = await fixture.ValidateAsync(TestContext.Current.CancellationToken);

        AssertV2Succeeded(result, fixture, candidateOnly: false);
        Assert.Equal(before, fixture.Snapshot());
    }

    [Fact]
    public async Task V2_candidate_requires_packages_but_not_matrix_or_clean_host()
    {
        using V2Fixture fixture = new();
        File.Delete(fixture.Resolve("matrix.json"));
        File.Delete(fixture.Resolve(V2Fixture.CleanHostFileName));
        string[] before = fixture.Snapshot();

        ProcessResult result = await fixture.ValidateAsync(
            TestContext.Current.CancellationToken, candidateOnly: true);

        AssertV2Succeeded(result, fixture, candidateOnly: true);
        Assert.Equal(before, fixture.Snapshot());
    }

    [Fact]
    public async Task V2_candidate_rejects_a_missing_local_zip_binary()
    {
        using V2Fixture fixture = new();
        File.Delete(fixture.Resolve(MatrixFixture.ZipFileName));

        ProcessResult result = await fixture.ValidateAsync(
            TestContext.Current.CancellationToken, candidateOnly: true);

        AssertV2Failed(result, "C02_MISSING_FILE");
    }

    [Fact]
    public async Task V2_accepts_the_MSBuild_UTF8_BOM_in_the_bundled_manifest_only()
    {
        using V2Fixture fixture = new();
        fixture.ReplaceZip("bom-manifest");

        ProcessResult result = await fixture.ValidateAsync(TestContext.Current.CancellationToken);

        AssertV2Succeeded(result, fixture, candidateOnly: false);
    }

    [Theory]
    [InlineData("NOT_RUN")]
    [InlineData("FAIL")]
    public async Task V2_advisory_results_and_recorded_warning_actions_do_not_block_required_pass(string status)
    {
        using V2Fixture fixture = new();
        foreach (JsonNode? test in fixture.CleanHost["tests"]!.AsArray().Skip(6))
        {
            test!["status"] = status;
            test["operations"] = status == "NOT_RUN" ? 0 : 1;
            test["record"] = status == "NOT_RUN" ? null : V2Fixture.Observation("advisory.txt");
        }
        fixture.CleanHost["protection"]!["launchOutcome"] = "WarnedThenAllowed";
        fixture.CleanHost["protection"]!["warningActions"] = 2;
        // C01 permits fractional precision beyond DateTime's seven digits.
        fixture.CleanHost["measuredAtUtc"] = "2024-02-29T12:34:56.1234567890123456789Z";
        fixture.SaveCleanHost();
        fixture.SaveMatrix();

        ProcessResult result = await fixture.ValidateAsync(TestContext.Current.CancellationToken);

        AssertV2Succeeded(result, fixture, candidateOnly: false);
    }

    [Theory]
    [InlineData("missing-row", "C02_MATRIX_SCHEMA")]
    [InlineData("missing-exe-row", "C02_MATRIX_SCHEMA")]
    [InlineData("unknown-row", "C02_MATRIX_SCHEMA")]
    [InlineData("duplicate-row", "C02_MATRIX_SCHEMA")]
    [InlineData("retired-third-row", "C02_MATRIX_SCHEMA")]
    [InlineData("changed-row", "C02_ROW_BINDING")]
    [InlineData("zip-not-published", "C02_MATRIX_SCHEMA")]
    [InlineData("zip-status-fail", "C02_MATRIX_SCHEMA")]
    [InlineData("zip-wrong-basename", "C02_MATRIX_SCHEMA")]
    [InlineData("candidate-extra-row", "C02_CANDIDATE_SCHEMA")]
    [InlineData("candidate-run", "C02_CANDIDATE_IDENTITY")]
    [InlineData("candidate-repository", "C02_CANDIDATE_IDENTITY")]
    [InlineData("candidate-commit", "C02_SOURCE_VERSION_BINDING")]
    [InlineData("candidate-workflow", "C02_CANDIDATE_SCHEMA")]
    [InlineData("p07-development", "C02_P07_SCHEMA")]
    [InlineData("p07-credential", "C02_P07_SCHEMA")]
    [InlineData("p07-source", "C02_P07_BINDING")]
    [InlineData("missing-clean-host", "C02_MISSING_FILE")]
    [InlineData("ch06-missing", "C02_CLEAN_HOST_SCHEMA")]
    [InlineData("ch06-not-run", "C02_CLEAN_HOST_SCHEMA")]
    [InlineData("ch06-na", "C02_CLEAN_HOST_SCHEMA")]
    [InlineData("ch06-fail", "C02_CLEAN_HOST_SCHEMA")]
    [InlineData("clean-host-hash", "C02_CLEAN_HOST_BINDING")]
    [InlineData("clean-host-version", "C02_SOURCE_VERSION_BINDING")]
    [InlineData("clean-host-runtime", "C02_CLEAN_HOST_BINDING")]
    [InlineData("clean-host-calendar", "C02_CLEAN_HOST_")]
    [InlineData("clean-host-future", "C02_CLEAN_HOST_TIME")]
    [InlineData("clean-host-candidate", "C02_CANDIDATE_IDENTITY")]
    [InlineData("clean-host-source", "C02_SOURCE_VERSION_BINDING")]
    [InlineData("clean-host-dependency", "C02_CLEAN_HOST_SCHEMA")]
    [InlineData("protection-actions", "C02_CLEAN_HOST_SCHEMA")]
    [InlineData("advisory-record", "C02_CLEAN_HOST_SCHEMA")]
    [InlineData("sidecar-content", "C02_SIDECAR_CONTENT")]
    [InlineData("zip-sidecar-content", "C02_SIDECAR_CONTENT")]
    [InlineData("candidate-size", "C02_DESCRIPTOR_SIZE")]
    [InlineData("candidate-hash", "C02_DESCRIPTOR_HASH")]
    [InlineData("exe-bytes", "C02_DESCRIPTOR_HASH")]
    [InlineData("os-build", "C02_OS_VERSION_BUILD")]
    [InlineData("package-host", "C02_PACKAGE_HOST_BINDING")]
    [InlineData("zip-closed-evidence", "C02_ZIP_EVIDENCE")]
    [InlineData("zip-evidence-status", "C02_ZIP_EVIDENCE")]
    [InlineData("zip-evidence-case-variant", "C02_ZIP_EVIDENCE")]
    [InlineData("zip-key-shadow", "C02_ZIP_EVIDENCE")]
    [InlineData("zip-string-type", "C02_ZIP_EVIDENCE")]
    [InlineData("numeric-string", "C02_MATRIX_SCHEMA")]
    [InlineData("unknown-schema", "C02_SCHEMA_VERSION")]
    [InlineData("retired-schema-1", "C02_SCHEMA_VERSION")]
    public async Task V2_rejects_broken_schema_or_semantic_closure(string mutation, string expectedCode)
    {
        using V2Fixture fixture = new();
        fixture.Mutate(mutation);
        string[] before = fixture.Snapshot();

        ProcessResult result = await fixture.ValidateAsync(TestContext.Current.CancellationToken);

        AssertV2Failed(result, expectedCode);
        Assert.Equal(before, fixture.Snapshot());
    }

    [Theory]
    [InlineData(false, "repository")]
    [InlineData(false, "run")]
    [InlineData(true, "repository")]
    [InlineData(true, "run")]
    public async Task V2_requires_explicit_repository_and_run_in_both_parameter_sets(bool candidateOnly, string omitted)
    {
        using V2Fixture fixture = new();

        ProcessResult result = await fixture.ValidateAsync(
            TestContext.Current.CancellationToken,
            candidateOnly: candidateOnly,
            expectedRepository: omitted == "repository" ? null : V2Fixture.Repository,
            expectedRunId: omitted == "run" ? null : V2Fixture.RunId);

        AssertV2Failed(result, "C02_EXPECTED_IDENTITY_REQUIRED");
    }

    [Theory]
    [InlineData("repository", "C02_CANDIDATE_IDENTITY")]
    [InlineData("run", "C02_CANDIDATE_IDENTITY")]
    [InlineData("version", "C02_SOURCE_VERSION_BINDING")]
    [InlineData("commit", "C02_SOURCE_VERSION_BINDING")]
    public async Task V2_binds_caller_expectations_instead_of_inferred_record_values(string changed, string expectedCode)
    {
        using V2Fixture fixture = new();

        ProcessResult result = await fixture.ValidateAsync(
            TestContext.Current.CancellationToken,
            expectedRepository: changed == "repository" ? "other/repository" : V2Fixture.Repository,
            expectedRunId: changed == "run" ? "999" : V2Fixture.RunId,
            expectedVersion: changed == "version" ? "0.8.1" : MatrixFixture.ProductVersion,
            expectedCommit: changed == "commit" ? new string('b', 40) : MatrixFixture.SourceCommit);

        AssertV2Failed(result, expectedCode);
    }

    [Fact]
    public async Task V2_matrix_and_candidate_parameter_sets_are_mutually_exclusive()
    {
        using V2Fixture fixture = new();

        ProcessResult result = await fixture.ValidateAsync(
            TestContext.Current.CancellationToken, conflictingParameterSets: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
    }

    [Theory]
    [InlineData("duplicate-p07", "C02_JSON_DUPLICATE_PROPERTY")]
    [InlineData("bom-clean-host", "C02_UTF8")]
    [InlineData("invalid-utf8-clean-host", "C02_UTF8")]
    [InlineData("oversized-clean-host", "C02_FILE_SIZE_LIMIT")]
    [InlineData("oversized-candidate", "C02_FILE_SIZE_LIMIT")]
    public async Task V2_rejects_unsafe_json_before_conversion(string mutation, string expectedCode)
    {
        using V2Fixture fixture = new();
        fixture.MutateJsonBytes(mutation);

        ProcessResult result = await fixture.ValidateAsync(TestContext.Current.CancellationToken);

        AssertV2Failed(result, expectedCode);
    }

    [Theory]
    [InlineData("cli-version", "C02_ZIP_RUNTIME")]
    [InlineData("sdk-version", "C02_ZIP_RUNTIME")]
    [InlineData("cli-bytes", "C02_ZIP_CLI_HASH")]
    [InlineData("member-shadow", "C02_ZIP_RUNTIME")]
    public async Task V2_reads_the_actual_zip_cli_and_manifest_without_extracting(string mutation, string expectedCode)
    {
        using V2Fixture fixture = new();
        fixture.ReplaceZip(mutation);
        string[] before = fixture.Snapshot();

        ProcessResult result = await fixture.ValidateAsync(TestContext.Current.CancellationToken);

        AssertV2Failed(result, expectedCode);
        Assert.Equal(before, fixture.Snapshot());
    }

    private static void AssertV2Failed(ProcessResult result, string expectedCode)
    {
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Contains("C02_FAIL code=" + expectedCode, result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(V2Fixture.SensitiveSentinel, result.StandardError, StringComparison.Ordinal);
    }

    private static void AssertV2Succeeded(ProcessResult result, V2Fixture fixture, bool candidateOnly)
    {
        AssertProcessSucceeded(result);
        Assert.True(string.IsNullOrWhiteSpace(result.StandardError));
        using JsonDocument output = JsonDocument.Parse(result.StandardOutput);
        JsonElement value = output.RootElement;
        Assert.Equal(2, value.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(candidateOnly ? "PASS_CANDIDATE" : "PASS", value.GetProperty("status").GetString());
        Assert.Equal(2, value.GetProperty("rowCount").GetInt32());
        Assert.Equal(MatrixFixture.ProductVersion, value.GetProperty("productVersion").GetString());
        Assert.Equal(MatrixFixture.SourceCommit, value.GetProperty("sourceCommit").GetString());
        Assert.Equal(V2Fixture.Repository, value.GetProperty("candidate").GetProperty("repository").GetString());
        Assert.Equal(V2Fixture.RunId, value.GetProperty("candidate").GetProperty("runId").GetString());
        Assert.Equal(
            [V2Fixture.ExeFileName, V2Fixture.ExeSidecarFileName, MatrixFixture.ZipFileName, MatrixFixture.ZipSidecarFileName],
            value.GetProperty("publishableAssets").EnumerateArray()
                .Select(item => item.GetString() ?? throw new InvalidDataException("Public asset name is null.")).ToArray());
        Assert.False(value.TryGetProperty("developmentMsixVerification", out _));
        Assert.Equal(candidateOnly ? "NOT_RUN" : "HUMAN_RECORDED", value.GetProperty("cleanHostVerification").GetString());
        Assert.Equal("CALLER_BOUND_UPSTREAM_REQUIRED", value.GetProperty("candidateRunVerification").GetString());
        Assert.Equal("P07_RECORD_AND_PACKAGE_HASH_BOUND", value.GetProperty("exeVersionVerification").GetString());
        Assert.True(JsonNode.DeepEquals(
            fixture.Descriptor(V2Fixture.CandidateFileName), JsonNode.Parse(value.GetProperty("candidateRecord").GetRawText())));
        Assert.True(JsonNode.DeepEquals(fixture.P07["runtime"], JsonNode.Parse(value.GetProperty("runtime").GetRawText())));
        if (candidateOnly)
        {
            Assert.Equal(JsonValueKind.Null, value.GetProperty("cleanHostEvidence").ValueKind);
        }
        else
        {
            Assert.True(JsonNode.DeepEquals(
                fixture.Descriptor(V2Fixture.CleanHostFileName), JsonNode.Parse(value.GetProperty("cleanHostEvidence").GetRawText())));
        }
        Assert.Equal(4, value.GetProperty("limitations").GetArrayLength());
    }

    private static void AssertProcessSucceeded(ProcessResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            $"Matrix validator failed with exit code {result.ExitCode}.\nSTDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
    }

    private sealed class MatrixFixture : IDisposable
    {
        internal const string ZipFileName = "StudyReportEvaluator-win-x64.zip";
        internal const string ZipSidecarFileName = ZipFileName + ".sha256";
        internal const string ZipEvidenceFileName = "StudyReportEvaluator-win-x64.evidence.json";
        internal const string ProductVersion = "0.8.0";
        internal const string SourceCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        internal const string EmptyUtf8Sha256 =
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
        internal static readonly UTF8Encoding Utf8NoBom = new(false, true);

        private readonly string repositoryRoot;
        private readonly string validatorPath;
        private readonly string powerShellPath;
        private bool disposed;

        internal MatrixFixture()
        {
            repositoryRoot = FindRepositoryRoot();
            validatorPath = Path.Combine(
                repositoryRoot,
                "scripts",
                "validate-platform-release-matrix.ps1");
            powerShellPath = FindPowerShellCoreExecutable();
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "StudyReportEvaluator-ReleaseMatrix-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);

            File.WriteAllBytes(Resolve(ZipFileName), [1, 2, 3, 4]);
            File.WriteAllText(
                Resolve(ZipSidecarFileName),
                $"{ComputeSha256(Resolve(ZipFileName))}  {ZipFileName}\n",
                Utf8NoBom);

            JsonObject verification = Verification();
            JsonObject zipDescriptor = Descriptor(ZipFileName);
            ZipEvidence = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["evidenceKind"] = "windows-zip-required",
                ["status"] = "PASS_REQUIRED",
                ["sourceCommit"] = SourceCommit,
                ["sourceStatusEntryCount"] = 0,
                ["sourceStatusSha256"] = EmptyUtf8Sha256,
                ["productVersion"] = ProductVersion,
                ["host"] = verification.DeepClone(),
                ["package"] = zipDescriptor.DeepClone(),
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
            WriteJson(ZipEvidenceFileName, ZipEvidence);

            ZipRow = new JsonObject
            {
                ["artifactKind"] = "windows-zip",
                ["platform"] = "windows",
                ["runtimeIdentifier"] = "win-x64",
                ["publish"] = true,
                ["status"] = "PASS_REQUIRED",
                ["verification"] = verification.DeepClone(),
                ["artifact"] = zipDescriptor,
                ["sidecar"] = Descriptor(ZipSidecarFileName),
                ["evidence"] = Descriptor(ZipEvidenceFileName),
            };
        }

        internal string DirectoryPath { get; }

        internal JsonObject ZipRow { get; }

        internal JsonObject ZipEvidence { get; }

        internal string Resolve(string fileName) => Path.Combine(DirectoryPath, fileName);

        internal JsonObject Descriptor(string fileName)
        {
            string path = Resolve(fileName);
            return new JsonObject
            {
                ["fileName"] = fileName,
                ["bytes"] = new FileInfo(path).Length,
                ["sha256"] = ComputeSha256(path),
            };
        }

        internal void WriteJson(string fileName, JsonNode value)
        {
            File.WriteAllText(
                Resolve(fileName),
                value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                Utf8NoBom);
        }

        internal async Task<ProcessResult> RunValidatorAsync(CancellationToken cancellationToken, IEnumerable<string> arguments)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = powerShellPath,
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };
            foreach (string argument in new[]
                     {
                         "-NoLogo",
                         "-NoProfile",
                         "-NonInteractive",
                         "-CommandWithArgs",
                         // The child inherits VSTest's console code page unless set explicitly.
                         // Keep the bootstrap fixed and pass paths/values as arguments, not code.
                         "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false, $true); " +
                         "$ErrorActionPreference = 'Stop'; $scriptPath = $args[0]; $parameters = @{}; " +
                         "for ($i = 1; $i -lt $args.Count; $i += 2) { " +
                         "$parameters.Add($args[$i].TrimStart('-'), $args[$i + 1]) }; " +
                         "& $scriptPath @parameters; if (-not $?) { exit 1 }",
                         validatorPath,
                     })
            {
                startInfo.ArgumentList.Add(argument);
            }
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            Assert.True(process.Start());
            Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Release matrix validator timed out.");
            }
            finally
            {
                // Also clean up on caller cancellation, and only our own child.
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

            return new ProcessResult(
                process.ExitCode,
                await output,
                await error);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Directory.Delete(DirectoryPath, recursive: true);
            Assert.False(Directory.Exists(DirectoryPath));
        }

        private static JsonObject Verification() => new()
        {
            ["osName"] = "Windows 11",
            ["osVersion"] = "10.0.26100.0",
            ["osBuild"] = 26100,
            ["osArchitecture"] = "X64",
            ["processArchitecture"] = "X64",
        };

        private static string ComputeSha256(string path)
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static string FindPowerShellCoreExecutable()
        {
            string executableName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                         .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = Path.Combine(directory.Trim('"'), executableName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            throw new FileNotFoundException("PowerShell Core 7.4+ was not found on PATH.");
        }

        internal static string FindRepositoryRoot()
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

            throw new DirectoryNotFoundException(
                "The repository root containing StudyReportEvaluator.slnx was not found.");
        }
    }

    private sealed class V2Fixture : IDisposable
    {
        internal const string ExeFileName = "StudyReportEvaluator-win-x64.exe";
        internal const string ExeSidecarFileName = ExeFileName + ".sha256";
        internal const string ExeEvidenceFileName = ExeFileName + ".evidence.json";
        internal const string CandidateFileName = "release-candidate-record.json";
        internal const string CleanHostFileName = "windows-singlefile-clean-host.evidence.json";
        internal const string Repository = "fixture-owner/fixture-repository";
        // Intentionally exceeds Int64: identity is a decimal STRING, never coerced.
        internal const string RunId = "98765432109876543210";
        internal const string SensitiveSentinel = "SYNTHETIC_CREDENTIAL_MUST_NOT_BE_PRINTED";
        private const string CliEntryName = "runtimes/win-x64/native/copilot.exe";
        private static readonly byte[] CliBytes = [0x43, 0x4C, 0x49, 0x31];
        private readonly MatrixFixture legacy = new();

        internal V2Fixture()
        {
            // Reuse the closed ZIP evidence shape, but replace its raw ZIP bits
            // with a real tiny ZIP. Dummy EXE/CLI bytes are never executed.
            WriteZip();
            legacy.ZipRow["artifact"] = Descriptor(MatrixFixture.ZipFileName);
            legacy.ZipEvidence["package"] = Descriptor(MatrixFixture.ZipFileName);
            WriteSidecar(MatrixFixture.ZipFileName);
            legacy.ZipRow["sidecar"] = Descriptor(MatrixFixture.ZipSidecarFileName);
            legacy.WriteJson(MatrixFixture.ZipEvidenceFileName, legacy.ZipEvidence);
            legacy.ZipRow["evidence"] = Descriptor(MatrixFixture.ZipEvidenceFileName);

            File.WriteAllBytes(Resolve(ExeFileName), [0x4D, 0x5A, 0x46, 0x49, 0x58, 0x54, 0x55, 0x52, 0x45]);
            WriteSidecar(ExeFileName);
            JsonObject host = legacy.ZipRow["verification"]!.DeepClone().AsObject();
            host["osName"] = "Windows"; // Actual P07 vs legacy ZIP osName.
            P07 = CreateP07(host);
            legacy.WriteJson(ExeEvidenceFileName, P07);
            ExeRow = new JsonObject
            {
                ["artifactKind"] = "windows-singlefile-exe",
                ["platform"] = "windows",
                ["runtimeIdentifier"] = "win-x64",
                ["publish"] = true,
                ["status"] = "PASS_REQUIRED",
                ["verification"] = host,
                ["artifact"] = Descriptor(ExeFileName),
                ["sidecar"] = Descriptor(ExeSidecarFileName),
                ["evidence"] = Descriptor(ExeEvidenceFileName),
            };
            Rows = new JsonArray(ExeRow, legacy.ZipRow.DeepClone());
            Candidate = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["evidenceKind"] = "windows-release-candidate",
                ["productVersion"] = MatrixFixture.ProductVersion,
                ["sourceCommit"] = MatrixFixture.SourceCommit,
                ["candidate"] = Identity(),
                ["requiredTests"] = "PASS",
                ["rows"] = CandidateRows(),
            };
            legacy.WriteJson(CandidateFileName, Candidate);
            CleanHost = CreateCleanHost();
            legacy.WriteJson(CleanHostFileName, CleanHost);
            Matrix = new JsonObject
            {
                ["schemaVersion"] = 2,
                ["productVersion"] = MatrixFixture.ProductVersion,
                ["sourceCommit"] = MatrixFixture.SourceCommit,
                ["candidate"] = Identity(),
                ["candidateRecord"] = Descriptor(CandidateFileName),
                ["cleanHostEvidence"] = Descriptor(CleanHostFileName),
                ["rows"] = Rows,
            };
            SaveMatrix();
        }

        internal JsonObject Matrix { get; }
        internal JsonArray Rows { get; }
        internal JsonObject ExeRow { get; }
        internal JsonObject Candidate { get; }
        internal JsonObject P07 { get; }
        internal JsonObject CleanHost { get; }
        private static string CliHash => Convert.ToHexString(SHA256.HashData(CliBytes));

        internal string Resolve(string name) => legacy.Resolve(name);
        internal JsonObject Descriptor(string name) => legacy.Descriptor(name);
        internal void SaveMatrix() => legacy.WriteJson("matrix.json", Matrix);

        internal string[] Snapshot() => Directory.GetFileSystemEntries(legacy.DirectoryPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Directory.Exists(path)
                ? "D:" + Path.GetRelativePath(legacy.DirectoryPath, path)
                : "F:" + Path.GetRelativePath(legacy.DirectoryPath, path) + ":" +
                  File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture) + ":" +
                  Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
            .ToArray();

        internal Task<ProcessResult> ValidateAsync(
            CancellationToken cancellationToken,
            bool candidateOnly = false,
            string? expectedRepository = Repository,
            string? expectedRunId = RunId,
            string expectedVersion = MatrixFixture.ProductVersion,
            string expectedCommit = MatrixFixture.SourceCommit,
            bool conflictingParameterSets = false)
        {
            List<string> arguments =
            [
                candidateOnly ? "-CandidateRecordPath" : "-MatrixPath",
                Resolve(candidateOnly ? CandidateFileName : "matrix.json"),
                "-ArtifactDirectory", legacy.DirectoryPath,
                "-ExpectedProductVersion", expectedVersion,
                "-ExpectedSourceCommit", expectedCommit,
            ];
            if (expectedRepository is not null)
            {
                arguments.AddRange(["-ExpectedRepository", expectedRepository]);
            }
            if (expectedRunId is not null)
            {
                arguments.AddRange(["-ExpectedCandidateRunId", expectedRunId]);
            }
            if (conflictingParameterSets)
            {
                arguments.AddRange(["-CandidateRecordPath", Resolve(CandidateFileName)]);
            }
            return legacy.RunValidatorAsync(cancellationToken, arguments);
        }

        internal void SaveCleanHost()
        {
            legacy.WriteJson(CleanHostFileName, CleanHost);
            Matrix["cleanHostEvidence"] = Descriptor(CleanHostFileName);
        }

        private void SaveCandidate()
        {
            legacy.WriteJson(CandidateFileName, Candidate);
            Matrix["candidateRecord"] = Descriptor(CandidateFileName);
        }

        private void RebindCandidateRows()
        {
            Candidate["rows"] = CandidateRows();
            SaveCandidate();
        }

        private JsonArray CandidateRows() => new(Rows.Select(row =>
        {
            JsonObject projected = row!.DeepClone().AsObject();
            foreach (string name in new[] { "platform", "runtimeIdentifier", "publish", "status" })
            {
                projected.Remove(name);
            }
            return (JsonNode)projected;
        }).ToArray());

        private void SaveEvidence()
        {
            legacy.WriteJson(ExeEvidenceFileName, P07);
            legacy.WriteJson(MatrixFixture.ZipEvidenceFileName, legacy.ZipEvidence);
            ExeRow["evidence"] = Descriptor(ExeEvidenceFileName);
            Rows[1]!["evidence"] = Descriptor(MatrixFixture.ZipEvidenceFileName);
            RebindCandidateRows();
        }

        internal void Mutate(string mutation)
        {
            switch (mutation)
            {
                case "missing-row": Rows.RemoveAt(1); break;
                case "missing-exe-row": Rows.RemoveAt(0); break;
                case "unknown-row": Rows[1]!["artifactKind"] = "other-platform"; break;
                case "duplicate-row": Rows[1] = Rows[0]!.DeepClone(); break;
                case "retired-third-row":
                    // The retired development package row must not re-enter the closed two-row set.
                    JsonNode retired = Rows[1]!.DeepClone();
                    retired["artifactKind"] = "windows-development-msix";
                    retired["publish"] = false;
                    retired["status"] = "PASS_MECHANISM";
                    Rows.Add(retired);
                    break;
                case "changed-row": Rows[1]!["artifact"]!["sha256"] = new string('0', 64); break;
                case "zip-not-published": Rows[1]!["publish"] = false; break;
                case "zip-status-fail": Rows[1]!["status"] = "FAIL"; break;
                case "zip-wrong-basename": Rows[1]!["artifact"]!["fileName"] = "wrong.zip"; break;
                case "candidate-extra-row":
                    JsonNode extra = Candidate["rows"]![1]!.DeepClone();
                    extra["artifactKind"] = "windows-development-msix";
                    Candidate["rows"]!.AsArray().Add(extra);
                    SaveCandidate();
                    break;
                case "candidate-run":
                    Candidate["candidate"]!["runId"] = "123"; SaveCandidate(); break;
                case "candidate-repository":
                    Candidate["candidate"]!["repository"] = "foreign/repository"; SaveCandidate(); break;
                case "candidate-commit":
                    Candidate["sourceCommit"] = new string('b', 40); SaveCandidate(); break;
                case "candidate-workflow":
                    Candidate["candidate"]!["workflow"] = ".github/workflows/ci.yml"; SaveCandidate(); break;
                case "p07-development": P07["status"] = "PASS_DEVELOPMENT"; SaveEvidence(); break;
                case "p07-credential": P07["runtime"]!["accessToken"] = SensitiveSentinel; SaveEvidence(); break;
                case "p07-source": P07["sourceCommit"] = new string('b', 40); SaveEvidence(); break;
                case "missing-clean-host": File.Delete(Resolve(CleanHostFileName)); break;
                case "ch06-missing": CleanHost["tests"]!.AsArray().RemoveAt(5); SaveCleanHost(); break;
                case "ch06-not-run":
                case "ch06-na":
                case "ch06-fail":
                    JsonNode required = CleanHost["tests"]![5]!;
                    required["status"] = mutation == "ch06-na" ? "N/A" : mutation == "ch06-fail" ? "FAIL" : "NOT_RUN";
                    if (mutation == "ch06-not-run")
                    {
                        required["operations"] = 0;
                        required["record"] = null;
                    }
                    SaveCleanHost();
                    break;
                case "clean-host-hash": CleanHost["package"]!["sha256"] = new string('0', 64); SaveCleanHost(); break;
                case "clean-host-version": CleanHost["productVersion"] = "0.8.1"; SaveCleanHost(); break;
                case "clean-host-runtime": CleanHost["runtime"]!["cliSha256"] = new string('0', 64); SaveCleanHost(); break;
                case "clean-host-calendar": CleanHost["measuredAtUtc"] = "2024-02-30T12:00:00Z"; SaveCleanHost(); break;
                case "clean-host-future":
                    CleanHost["measuredAtUtc"] = DateTime.UtcNow.AddHours(1).ToString("O", CultureInfo.InvariantCulture);
                    SaveCleanHost(); break;
                case "clean-host-candidate": CleanHost["candidate"]!["runId"] = "123"; SaveCleanHost(); break;
                case "clean-host-source": CleanHost["sourceCommit"] = new string('b', 40); SaveCleanHost(); break;
                case "clean-host-dependency": CleanHost["host"]!["additionalDependencies"]!["nodeNpm"] = true; SaveCleanHost(); break;
                case "protection-actions": CleanHost["protection"]!["warningActions"] = 1; SaveCleanHost(); break;
                case "advisory-record": CleanHost["tests"]![6]!["record"] = Observation("advisory.txt"); SaveCleanHost(); break;
                case "sidecar-content":
                    File.WriteAllText(Resolve(ExeSidecarFileName),
                        $"{Descriptor(ExeFileName)["sha256"]!.GetValue<string>()}  {ExeFileName}\r\n", MatrixFixture.Utf8NoBom);
                    ExeRow["sidecar"] = Descriptor(ExeSidecarFileName);
                    RebindCandidateRows(); break;
                case "zip-sidecar-content":
                    File.WriteAllText(Resolve(MatrixFixture.ZipSidecarFileName),
                        $"{Descriptor(MatrixFixture.ZipFileName)["sha256"]!.GetValue<string>()}  {MatrixFixture.ZipFileName}\r\n",
                        MatrixFixture.Utf8NoBom);
                    Rows[1]!["sidecar"] = Descriptor(MatrixFixture.ZipSidecarFileName);
                    RebindCandidateRows(); break;
                case "candidate-size":
                    File.AppendAllText(Resolve(CandidateFileName), " ", MatrixFixture.Utf8NoBom); break;
                case "candidate-hash":
                    byte[] bytes = File.ReadAllBytes(Resolve(CandidateFileName));
                    int whitespace = Array.IndexOf(bytes, (byte)' ');
                    Assert.True(whitespace >= 0);
                    bytes[whitespace] = (byte)'\t'; // Same byte count, still JSON, different digest.
                    File.WriteAllBytes(Resolve(CandidateFileName), bytes); break;
                case "exe-bytes": File.WriteAllBytes(Resolve(ExeFileName), new byte[9]); break;
                case "os-build":
                    ExeRow["verification"]!["osBuild"] = 26200;
                    RebindCandidateRows(); break;
                case "package-host":
                    Rows[1]!["verification"]!["osBuild"] = 26200;
                    Rows[1]!["verification"]!["osVersion"] = "10.0.26200.0";
                    RebindCandidateRows(); break;
                case "zip-closed-evidence": legacy.ZipEvidence["accessToken"] = SensitiveSentinel; SaveEvidence(); break;
                case "zip-evidence-status": legacy.ZipEvidence["status"] = "FAIL"; SaveEvidence(); break;
                case "zip-evidence-case-variant": legacy.ZipEvidence["Status"] = "FAIL"; SaveEvidence(); break;
                case "zip-key-shadow":
                    legacy.ZipEvidence["Keys"] = new JsonArray(legacy.ZipEvidence
                        .Select(property => (JsonNode?)JsonValue.Create(property.Key)).ToArray());
                    legacy.ZipEvidence["accessToken"] = SensitiveSentinel;
                    SaveEvidence(); break;
                case "zip-string-type":
                    legacy.ZipEvidence["productVersion"] = new JsonArray(MatrixFixture.ProductVersion);
                    SaveEvidence(); break;
                case "numeric-string": ExeRow["verification"]!["osBuild"] = "26100"; break;
                case "unknown-schema": Matrix["schemaVersion"] = 3; break;
                case "retired-schema-1": Matrix["schemaVersion"] = 1; break;
                default: throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            SaveMatrix();
        }

        internal void MutateJsonBytes(string mutation)
        {
            switch (mutation)
            {
                case "duplicate-p07":
                    string json = P07.ToJsonString().Replace(
                        "\"status\":\"PASS_REQUIRED\"", "\"status\":\"PASS_REQUIRED\",\"status\":\"PASS_REQUIRED\"", StringComparison.Ordinal);
                    File.WriteAllText(Resolve(ExeEvidenceFileName), json, MatrixFixture.Utf8NoBom);
                    ExeRow["evidence"] = Descriptor(ExeEvidenceFileName);
                    RebindCandidateRows();
                    break;
                case "bom-clean-host":
                    File.WriteAllBytes(Resolve(CleanHostFileName), [0xEF, 0xBB, 0xBF, .. File.ReadAllBytes(Resolve(CleanHostFileName))]);
                    Matrix["cleanHostEvidence"] = Descriptor(CleanHostFileName);
                    break;
                case "invalid-utf8-clean-host":
                    File.WriteAllBytes(Resolve(CleanHostFileName), [0xC3, 0x28]);
                    Matrix["cleanHostEvidence"] = Descriptor(CleanHostFileName);
                    break;
                case "oversized-clean-host":
                    File.WriteAllBytes(Resolve(CleanHostFileName), new byte[(64 * 1024) + 1]);
                    Matrix["cleanHostEvidence"] = Descriptor(CleanHostFileName);
                    break;
                case "oversized-candidate":
                    File.WriteAllBytes(Resolve(CandidateFileName), new byte[(1024 * 1024) + 1]);
                    Matrix["candidateRecord"] = Descriptor(CandidateFileName);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            SaveMatrix();
        }

        internal void ReplaceZip(string mutation)
        {
            WriteZip(mutation);
            Rows[1]!["artifact"] = Descriptor(MatrixFixture.ZipFileName);
            legacy.ZipEvidence["package"] = Descriptor(MatrixFixture.ZipFileName);
            WriteSidecar(MatrixFixture.ZipFileName);
            Rows[1]!["sidecar"] = Descriptor(MatrixFixture.ZipSidecarFileName);
            SaveEvidence();
            SaveMatrix();
        }

        private void WriteZip(string? mutation = null)
        {
            JsonObject manifest = new()
            {
                ["schemaVersion"] = 1,
                ["runtimeIdentifier"] = "win-x64",
                ["cliVersion"] = mutation == "cli-version" ? "1.0.80" : "1.0.79",
                ["cliSha256"] = CliHash,
                ["sdkVersion"] = mutation == "sdk-version" ? "1.0.12" : "1.0.11",
                ["cliRelativePath"] = CliEntryName,
            };
            if (mutation == "member-shadow")
            {
                JsonArray keys = new(manifest.Select(property => (JsonNode?)JsonValue.Create(property.Key)).ToArray());
                manifest["Count"] = manifest.Count;
                manifest["Keys"] = keys;
                manifest["accessToken"] = SensitiveSentinel;
            }
            using FileStream file = File.Create(Resolve(MatrixFixture.ZipFileName));
            using ZipArchive archive = new(file, ZipArchiveMode.Create);
            using (Stream entry = archive.CreateEntry("StudyReportEvaluator-win-x64/copilot-runtime.json").Open())
            {
                if (mutation == "bom-manifest")
                {
                    entry.Write(new byte[] { 0xEF, 0xBB, 0xBF });
                }
                entry.Write(MatrixFixture.Utf8NoBom.GetBytes(manifest.ToJsonString()));
            }
            using (Stream entry = archive.CreateEntry("StudyReportEvaluator-win-x64/" + CliEntryName).Open())
            {
                entry.Write(mutation == "cli-bytes" ? new byte[] { 9, 8, 7, 6 } : CliBytes);
            }
        }

        private void WriteSidecar(string artifactName) => File.WriteAllText(
            Resolve(artifactName + ".sha256"),
            $"{Descriptor(artifactName)["sha256"]!.GetValue<string>()}  {artifactName}\n", MatrixFixture.Utf8NoBom);

        private static JsonObject Identity() => new()
        {
            ["repository"] = Repository,
            ["workflow"] = ".github/workflows/release.yml",
            ["runId"] = RunId,
        };

        private static JsonObject Runtime() => new()
        {
            ["dotnetSdk"] = "10.0.400",
            ["dotnetRuntime"] = "10.0.11",
            ["copilotSdk"] = "1.0.11",
            ["cliVersion"] = "1.0.79",
            ["cliSha256"] = CliHash,
        };

        internal static JsonObject Observation(string name) => new()
        {
            ["fileName"] = name,
            ["bytes"] = 42,
            ["sha256"] = new string('A', 64),
        };

        private JsonObject CreateP07(JsonObject host)
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
                ["sourceCommit"] = MatrixFixture.SourceCommit,
                ["sourceStatusEntryCount"] = 0,
                ["sourceStatusSha256"] = MatrixFixture.EmptyUtf8Sha256,
                ["sourceContentSha256"] = new string('B', 64),
                ["productVersion"] = MatrixFixture.ProductVersion,
                ["host"] = host.DeepClone(),
                ["package"] = Descriptor(ExeFileName),
                ["runtime"] = Runtime(),
                ["tests"] = new JsonObject
                {
                    ["total"] = 7, ["passed"] = 7, ["failed"] = 0, ["skipped"] = 0,
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

        private JsonObject CreateCleanHost()
        {
            JsonArray tests = [];
            for (int index = 1; index <= 6; index++)
            {
                string id = "CH-" + index.ToString("D2", CultureInfo.InvariantCulture);
                tests.Add(new JsonObject
                {
                    ["id"] = id, ["status"] = "PASS", ["operations"] = 0, ["record"] = Observation(id + ".txt"),
                });
            }
            foreach (string id in new[] { "ADV-01", "ADV-02" })
            {
                tests.Add(new JsonObject { ["id"] = id, ["status"] = "NOT_RUN", ["operations"] = 0, ["record"] = null });
            }
            return new JsonObject
            {
                ["schemaVersion"] = 1,
                ["evidenceKind"] = "windows-singlefile-clean-host",
                ["measuredAtUtc"] = DateTime.UtcNow.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture),
                ["productVersion"] = MatrixFixture.ProductVersion,
                ["sourceCommit"] = MatrixFixture.SourceCommit,
                ["candidate"] = Identity(),
                ["package"] = Descriptor(ExeFileName),
                ["runtime"] = Runtime(),
                ["host"] = new JsonObject
                {
                    ["osName"] = "Windows 11",
                    ["edition"] = "Pro",
                    ["osVersion"] = "10.0.26200.0", // A different fresh host is allowed.
                    ["osBuild"] = 26200,
                    ["architecture"] = "X64",
                    ["standardUser"] = true,
                    ["fresh"] = true,
                    ["additionalDependencies"] = new JsonObject
                    {
                        ["dotnetSdk"] = false, ["dotnetRuntime"] = false, ["powerShell6Plus"] = false,
                        ["nodeNpm"] = false, ["git"] = false, ["githubCli"] = false,
                        ["copilotCli"] = false, ["office"] = false, ["ide"] = false,
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
                    ["extractedBytes"] = 1234, ["coldStartMilliseconds"] = 20, ["warmStartMilliseconds"] = 10,
                },
                ["tests"] = tests,
            };
        }

        public void Dispose() => legacy.Dispose();
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}