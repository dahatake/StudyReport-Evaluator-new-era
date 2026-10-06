using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitHub.Copilot;
using StudyReportEvaluator.App.Copilot;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Packaging;

[CollectionDefinition(DisableParallelization = true)]
public sealed class WindowsPublishPackageCollection
{
}

// Requirements: FR-056 (AC-057)
[Collection<WindowsPublishPackageCollection>]
public sealed class WindowsPublishPackageTests
{
    private const string PackageRootName = "StudyReportEvaluator-win-x64";
    private const string ZipFileName = "StudyReportEvaluator-win-x64.zip";
    private const string HashFileName = "StudyReportEvaluator-win-x64.zip.sha256";
    private const string CopilotCliRelativePath = "runtimes/win-x64/native/copilot.exe";
    private static readonly DateTime FixedZipTimestamp = new(2000, 1, 1, 0, 0, 0);

    private static readonly string[] RequiredApplicationFiles =
    [
        "StudyReportEvaluator.App.exe",
        "StudyReportEvaluator.App.dll",
        "StudyReportEvaluator.App.runtimeconfig.json",
        "StudyReportEvaluator.App.deps.json",
        "StudyReportEvaluator.Core.dll",
        "DocumentFormat.OpenXml.dll",
        "DocumentFormat.OpenXml.Framework.dll",
        "GitHub.Copilot.SDK.dll",
        "copilot-runtime.json",
        CopilotCliRelativePath,
        "Avalonia.dll",
        "Avalonia.Win32.dll",
        "coreclr.dll",
        "hostfxr.dll",
        "hostpolicy.dll",
        "System.Private.CoreLib.dll",
    ];

    private static readonly string[] RequiredDocumentationFiles =
    [
        "LICENSE",
        "README.md",
        "docs/README.md",
        "docs/getting-started.md",
        "docs/features.md",
        "docs/custom-evaluator-guide.md",
        "docs/technical-guid.md",
        "docs/prompt-launch.md",
        "docs/privacy-and-data-handling.md",
        "docs/troubleshooting.md",
        "docs/settings.md",
        "docs/third-party-notices.md",
        "docs/result-excel-description.md",
        "images/README.md",
        "images/architecture-overview.svg",
        "images/technical-architecture.svg",
        "images/evaluation-message-flow.svg",
        "images/01-input-workbook.png",
        "images/02-input-mapping.png",
        "images/03-design-knowledge.png",
        "images/04-design-custom-prompt.png",
        "images/05-execution-auto.png",
        "images/06-results-review.png",
        "images/07-output-export.png",
        "images/08-settings.png",
    ];

    private static readonly string[] RequiredDependencyPrefixes =
    [
        "StudyReportEvaluator.App/",
        "StudyReportEvaluator.Core/",
        "DocumentFormat.OpenXml/",
        "GitHub.Copilot.SDK/",
        "Avalonia/",
        "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/",
    ];

    private static readonly string[] ForbiddenDependencyMarkers =
    [
        "Microsoft.Office",
        "Office.Interop",
        "Interop.Excel",
        "LibreOffice",
        "soffice",
        "Microsoft.NET.Test.Sdk",
        "xunit",
        "testhost",
        "Avalonia.Headless",
        "StudyReportEvaluator.App.Tests",
        "StudyReportEvaluator.Core.Tests",
    ];

    private static readonly HashSet<string> ForbiddenSegments = new(
        [
            "sample",
            "samples",
            "input",
            "inputs",
            "artifact",
            "artifacts",
            "test",
            "tests",
            "src",
            "source",
            "sources",
            "secret",
            "secrets",
            ".git",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ForbiddenExtensions = new(
        [
            ".cs",
            ".csproj",
            ".fs",
            ".fsproj",
            ".vb",
            ".vbproj",
            ".sln",
            ".slnx",
            ".ps1",
            ".pdb",
            ".pfx",
            ".p12",
            ".pem",
            ".key",
            ".snk",
            ".xlsx",
            ".xls",
            ".xlsm",
            ".csv",
        ],
        StringComparer.OrdinalIgnoreCase);

    [Fact]
    public async Task Windows_publish_and_unsigned_package_are_safe_launchable_and_reproducible()
    {
        RequireWindows11X64();
        AssertDocumentationSet(
            RequiredDocumentationFiles.Concat(RequiredApplicationFiles).Append("RELEASE-NOTES.txt"));
        foreach (string unexpectedPath in new[] { "DOCS/extra.md", "Images/extra.png", "docs/settings.md" })
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                AssertDocumentationSet(RequiredDocumentationFiles.Append(unexpectedPath)));
        }

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            AssertDocumentationSet(RequiredDocumentationFiles.Select(path =>
                path == "docs/settings.md" ? "DOCS/settings.md" : path)));

        string repositoryRoot = FindRepositoryRoot();
        string publishScript = Path.Combine(repositoryRoot, "scripts", "publish-windows.ps1");
        string packageScript = Path.Combine(repositoryRoot, "scripts", "package-windows.ps1");
        string publishDirectory = Path.Combine(
            repositoryRoot,
            "artifacts",
            "package",
            "publish",
            "win-x64");
        string zipPath = Path.Combine(repositoryRoot, "artifacts", "package", ZipFileName);
        string hashPath = Path.Combine(repositoryRoot, "artifacts", "package", HashFileName);
        string powerShellPath = FindPowerShellCoreExecutable();

        AssertSdkAndGlobalJsonContract(repositoryRoot);
        AssertPowerShellContracts(publishScript, packageScript);
        string[] lockPaths =
        [
            Path.Combine(repositoryRoot, "src", "StudyReportEvaluator.Core", "packages.lock.json"),
            Path.Combine(repositoryRoot, "src", "StudyReportEvaluator.App", "packages.lock.json"),
            Path.Combine(repositoryRoot, "tests", "StudyReportEvaluator.Core.Tests", "packages.lock.json"),
            Path.Combine(repositoryRoot, "tests", "StudyReportEvaluator.App.Tests", "packages.lock.json"),
        ];
        Dictionary<string, string> originalLockHashes = lockPaths.ToDictionary(
            path => path,
            ComputeSha256,
            StringComparer.OrdinalIgnoreCase);

        ProcessResult publishResult = await RunPowerShellScriptAsync(
            powerShellPath,
            publishScript,
            [],
            TestContext.Current.CancellationToken);
        AssertProcessSucceeded(publishResult, "publish-windows.ps1");
        AssertLockFilesUnchanged(originalLockHashes);
        AssertPublishLayout(publishDirectory);
        await AssertApplicationStartsAndStopsAsync(
            publishDirectory,
            TestContext.Current.CancellationToken);

        ProcessResult firstPackageResult = await RunPowerShellScriptAsync(
            powerShellPath,
            packageScript,
            ["-PublishedDirectory", publishDirectory],
            TestContext.Current.CancellationToken);
        AssertProcessSucceeded(firstPackageResult, "package-windows.ps1 (first run)");
        AssertLockFilesUnchanged(originalLockHashes);
        Assert.True(File.Exists(zipPath));
        Assert.True(File.Exists(hashPath));

        string firstZipHash = AssertHashSidecar(zipPath, hashPath);
        long firstZipLength = new FileInfo(zipPath).Length;
        AssertZipContract(zipPath);

        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "StudyReportEvaluator-P01-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string extractionDirectory = Path.Combine(temporaryDirectory, "extracted");
            Directory.CreateDirectory(extractionDirectory);
            ZipFile.ExtractToDirectory(zipPath, extractionDirectory);
            string[] topLevelEntries = Directory.GetFileSystemEntries(extractionDirectory);
            string extractedPackageRoot = Assert.Single(topLevelEntries);
            Assert.Equal(PackageRootName, Path.GetFileName(extractedPackageRoot));
            Assert.True(Directory.Exists(extractedPackageRoot));
            AssertPublishLayout(extractedPackageRoot, allowReleaseNotes: true);
            AssertExtractedDocumentationLinks(extractedPackageRoot);
            string? resolvedCliPath = await new BundledCopilotCliPathResolver(extractedPackageRoot)
                .ResolveAsync(TestContext.Current.CancellationToken);
            Assert.Equal(
                Path.Combine(extractedPackageRoot, CopilotCliRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                resolvedCliPath);
            await AssertApplicationStartsAndStopsAsync(
                extractedPackageRoot,
                TestContext.Current.CancellationToken);

            string tamperedPath = Path.Combine(temporaryDirectory, "tampered.zip");
            File.Copy(zipPath, tamperedPath);
            await using (FileStream tamperedStream = new(
                tamperedPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough))
            {
                tamperedStream.WriteByte(0xA5);
                await tamperedStream.FlushAsync(TestContext.Current.CancellationToken);
                tamperedStream.Flush(flushToDisk: true);
            }

            Assert.NotEqual(firstZipHash, ComputeSha256(tamperedPath));
            Assert.False(VerifyHashSidecar(tamperedPath, hashPath));
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                await DeleteDirectoryWithRetriesAsync(temporaryDirectory);
            }
        }

        ProcessResult secondPackageResult = await RunPowerShellScriptAsync(
            powerShellPath,
            packageScript,
            ["-PublishedDirectory", publishDirectory],
            TestContext.Current.CancellationToken);
        AssertProcessSucceeded(secondPackageResult, "package-windows.ps1 (second run)");
        Assert.Equal(firstZipLength, new FileInfo(zipPath).Length);
        Assert.Equal(firstZipHash, AssertHashSidecar(zipPath, hashPath));
        AssertZipContract(zipPath);
        AssertLockFilesUnchanged(originalLockHashes);
    }

    [Fact]
    public async Task Package_rejects_a_zero_byte_required_file_without_replacing_outputs()
    {
        RequireWindows11X64();
        string repositoryRoot = FindRepositoryRoot();
        string packageScript = Path.Combine(repositoryRoot, "scripts", "package-windows.ps1");
        string powerShellPath = FindPowerShellCoreExecutable();
        IReadOnlyDictionary<string, FileHashSnapshot> outputSnapshots =
            CapturePackageOutputSnapshots(repositoryRoot);
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "StudyReportEvaluator-P01-Zero-" + Guid.NewGuid().ToString("N"));
        string publishInput = Path.Combine(temporaryDirectory, "publish");
        Directory.CreateDirectory(publishInput);

        try
        {
            CreateMinimalPackageInput(publishInput);
            File.WriteAllBytes(
                Path.Combine(publishInput, "StudyReportEvaluator.App.exe"),
                []);

            ProcessResult result = await RunPowerShellScriptAsync(
                powerShellPath,
                packageScript,
                ["-PublishedDirectory", publishInput],
                TestContext.Current.CancellationToken);

            AssertProcessFailed(
                result,
                "package-windows.ps1 zero-byte input",
                "Zero-byte package input is not allowed");
            AssertPackageOutputsUnchanged(outputSnapshots);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                await DeleteDirectoryWithRetriesAsync(temporaryDirectory);
            }
        }
    }

    [Fact]
    public async Task Package_rejects_a_reparse_point_without_replacing_outputs()
    {
        RequireWindows11X64();
        string repositoryRoot = FindRepositoryRoot();
        string packageScript = Path.Combine(repositoryRoot, "scripts", "package-windows.ps1");
        string powerShellPath = FindPowerShellCoreExecutable();
        IReadOnlyDictionary<string, FileHashSnapshot> outputSnapshots =
            CapturePackageOutputSnapshots(repositoryRoot);
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "StudyReportEvaluator-P01-Reparse-" + Guid.NewGuid().ToString("N"));
        string publishInput = Path.Combine(temporaryDirectory, "publish");
        string junctionTarget = Path.Combine(temporaryDirectory, "junction-target");
        string junctionPath = Path.Combine(publishInput, "junction");
        Directory.CreateDirectory(publishInput);
        Directory.CreateDirectory(junctionTarget);

        try
        {
            CreateMinimalPackageInput(publishInput);
            string createJunctionCommand = string.Concat(
                "$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path ",
                QuotePowerShellLiteral(junctionPath),
                " -Target ",
                QuotePowerShellLiteral(junctionTarget),
                " | Out-Null");
            ProcessResult junctionResult = await RunPowerShellCommandAsync(
                powerShellPath,
                createJunctionCommand,
                TestContext.Current.CancellationToken);
            AssertProcessSucceeded(junctionResult, "PowerShell Core junction setup");
            Assert.NotEqual(
                0,
                (int)(File.GetAttributes(junctionPath) & FileAttributes.ReparsePoint));

            ProcessResult result = await RunPowerShellScriptAsync(
                powerShellPath,
                packageScript,
                ["-PublishedDirectory", publishInput],
                TestContext.Current.CancellationToken);

            AssertProcessFailed(
                result,
                "package-windows.ps1 reparse-point input",
                "Reparse points are not allowed");
            AssertPackageOutputsUnchanged(outputSnapshots);
        }
        finally
        {
            if (Directory.Exists(junctionPath))
            {
                Directory.Delete(junctionPath);
            }

            if (Directory.Exists(temporaryDirectory))
            {
                await DeleteDirectoryWithRetriesAsync(temporaryDirectory);
            }
        }
    }

    private static void AssertSdkAndGlobalJsonContract(string repositoryRoot)
    {
        string globalJsonPath = Path.Combine(repositoryRoot, "global.json");
        Assert.True(File.Exists(globalJsonPath));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(globalJsonPath));
        JsonElement sdk = document.RootElement.GetProperty("sdk");
        Assert.Equal("10.0.400", sdk.GetProperty("version").GetString());
        Assert.Equal("latestPatch", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());
    }

    private static void AssertPowerShellContracts(string publishScript, string packageScript)
    {
        Assert.True(File.Exists(publishScript));
        Assert.True(File.Exists(packageScript));
        string publishSource = File.ReadAllText(publishScript);
        string packageSource = File.ReadAllText(packageScript);

        foreach (string source in new[] { publishSource, packageSource })
        {
            Assert.True(source.Contains("#Requires -Version 7.0", StringComparison.Ordinal));
            Assert.True(source.Contains("#Requires -PSEdition Core", StringComparison.Ordinal));
            Assert.True(source.Contains("$PSVersionTable.PSEdition -cne 'Core'", StringComparison.Ordinal));
            Assert.True(source.Contains("$PSVersionTable.PSVersion.Major -lt 7", StringComparison.Ordinal));
            Assert.False(source.Contains("powershell.exe", StringComparison.OrdinalIgnoreCase));
            Assert.False(source.Contains("Get-AuthenticodeSignature", StringComparison.OrdinalIgnoreCase));
            Assert.False(source.Contains("Set-AuthenticodeSignature", StringComparison.OrdinalIgnoreCase));
            Assert.False(source.Contains("signtool", StringComparison.OrdinalIgnoreCase));
        }

        Assert.True(publishSource.Contains("--locked-mode", StringComparison.Ordinal));
        Assert.True(publishSource.Contains("--lock-file-path", StringComparison.Ordinal));
        Assert.True(publishSource.Contains("--no-restore", StringComparison.Ordinal));
        Assert.True(publishSource.Contains("--self-contained", StringComparison.Ordinal));
        Assert.True(publishSource.Contains("PublishSingleFile=false", StringComparison.Ordinal));
        Assert.True(publishSource.Contains("PublishTrimmed=false", StringComparison.Ordinal));
        Assert.True(publishSource.Contains("PublishReadyToRun=false", StringComparison.Ordinal));
        Assert.True(publishSource.Contains("SelfContained=true", StringComparison.Ordinal));
        Assert.True(publishSource.Contains("UseAppHost=true", StringComparison.Ordinal));
        Assert.True(packageSource.Contains("Signing: UNSIGNED", StringComparison.Ordinal));
        Assert.True(packageSource.Contains("GitHub Copilot CLI runtime: BUNDLED", StringComparison.Ordinal));
        Assert.True(packageSource.Contains("StringComparer]::Ordinal", StringComparison.Ordinal));
        Assert.True(packageSource.Contains("SHA256]::HashData", StringComparison.Ordinal));
        Assert.True(packageSource.Contains("Assert-SafeRelativePath", StringComparison.Ordinal));
        Assert.True(packageSource.Contains("FileAttributes]::ReparsePoint", StringComparison.Ordinal));
        Assert.True(packageSource.Contains("StringComparer]::OrdinalIgnoreCase", StringComparison.Ordinal));
        Assert.True(packageSource.Contains("Length -le 0", StringComparison.Ordinal));
        foreach (string marker in ForbiddenDependencyMarkers)
        {
            Assert.Contains(marker, publishSource, StringComparison.Ordinal);
            Assert.Contains(marker, packageSource, StringComparison.Ordinal);
        }
    }

    private static void AssertPublishLayout(
        string publishDirectory,
        bool allowReleaseNotes = false)
    {
        Assert.True(Directory.Exists(publishDirectory));
        AssertNoReparsePoint(publishDirectory);

        FileSystemInfo[] entries = new DirectoryInfo(publishDirectory)
            .EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
            .ToArray();
        FileInfo[] files = entries.OfType<FileInfo>().ToArray();
        Assert.NotEmpty(files);

        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (FileSystemInfo entry in entries)
        {
            AssertNoReparsePoint(entry.FullName);
            string relativePath = Path.GetRelativePath(publishDirectory, entry.FullName)
                .Replace('\\', '/');
            AssertSafeRelativePath(relativePath);
            Assert.True(paths.Add(relativePath), $"Duplicate or case-colliding path: {relativePath}");
            AssertNoForbiddenPath(relativePath, allowReleaseNotes);

            if (entry is FileInfo file)
            {
                Assert.True(file.Length > 0, $"Zero-byte publish file: {relativePath}");
            }
        }

        foreach (string requiredFile in RequiredApplicationFiles)
        {
            string requiredPath = Path.Combine(publishDirectory, requiredFile);
            Assert.True(File.Exists(requiredPath), $"Missing publish file: {requiredFile}");
            Assert.True(new FileInfo(requiredPath).Length > 0);
        }

        AssertBundledCopilotRuntime(publishDirectory);
        Assert.False(File.Exists(Path.Combine(publishDirectory, "copilot.exe")));
        Assert.DoesNotContain(
            files,
            file => file.Extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase));

        string runtimeConfigPath = Path.Combine(
            publishDirectory,
            "StudyReportEvaluator.App.runtimeconfig.json");
        using (JsonDocument runtimeConfig = JsonDocument.Parse(File.ReadAllBytes(runtimeConfigPath)))
        {
            JsonElement runtimeOptions = runtimeConfig.RootElement.GetProperty("runtimeOptions");
            Assert.Equal("net10.0", runtimeOptions.GetProperty("tfm").GetString());
            Assert.False(runtimeOptions.TryGetProperty("framework", out _));
            JsonElement[] includedFrameworks = runtimeOptions
                .GetProperty("includedFrameworks")
                .EnumerateArray()
                .ToArray();
            JsonElement netCoreFramework = Assert.Single(
                includedFrameworks,
                framework => framework.GetProperty("name").GetString() == "Microsoft.NETCore.App");
            Assert.StartsWith(
                "10.0.",
                netCoreFramework.GetProperty("version").GetString(),
                StringComparison.Ordinal);
        }

        string dependencyContextPath = Path.Combine(
            publishDirectory,
            "StudyReportEvaluator.App.deps.json");
        string dependencyContextText = File.ReadAllText(dependencyContextPath);
        foreach (string marker in ForbiddenDependencyMarkers)
        {
            Assert.DoesNotContain(marker, dependencyContextText, StringComparison.OrdinalIgnoreCase);
        }

        using (JsonDocument dependencyContext = JsonDocument.Parse(dependencyContextText))
        {
            Assert.Equal(
                ".NETCoreApp,Version=v10.0/win-x64",
                dependencyContext.RootElement
                    .GetProperty("runtimeTarget")
                    .GetProperty("name")
                    .GetString());
            string[] libraryNames = dependencyContext.RootElement
                .GetProperty("libraries")
                .EnumerateObject()
                .Select(library => library.Name)
                .ToArray();
            foreach (string prefix in RequiredDependencyPrefixes)
            {
                Assert.Contains(
                    libraryNames,
                    name => name.StartsWith(prefix, StringComparison.Ordinal));
            }
        }

        AssertAmd64AppHost(Path.Combine(publishDirectory, "StudyReportEvaluator.App.exe"));
        AssertNotReadyToRun(Path.Combine(publishDirectory, "StudyReportEvaluator.App.dll"));
    }

    private static void AssertZipContract(string zipPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry[] entries = archive.Entries.ToArray();
        Assert.NotEmpty(entries);
        Assert.Equal(PackageRootName + "/", entries[0].FullName);

        string[] entryNames = entries.Select(entry => entry.FullName).ToArray();
        Assert.Equal(
            entryNames.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            entryNames);
        Assert.Equal(entryNames.Length, entryNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            entryNames.Length,
            entryNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (ZipArchiveEntry entry in entries)
        {
            Assert.False(entry.FullName.Contains('\\'));
            Assert.StartsWith(PackageRootName + "/", entry.FullName, StringComparison.Ordinal);
            string relativePath = entry.FullName[(PackageRootName.Length + 1)..].TrimEnd('/');
            if (relativePath.Length > 0)
            {
                AssertSafeRelativePath(relativePath);
                AssertNoForbiddenPath(relativePath, allowReleaseNotes: true);
            }

            DateTime timestamp = entry.LastWriteTime.DateTime;
            Assert.Equal(FixedZipTimestamp, timestamp);
            int unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            Assert.NotEqual(0xA000, unixFileType);
            if (!entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Assert.True(entry.Length > 0, $"ZIP contains an empty file: {entry.FullName}");
            }
        }

        string[] topLevelNames = entryNames
            .Select(name => name.Split('/', StringSplitOptions.RemoveEmptyEntries)[0])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([PackageRootName], topLevelNames);

        foreach (string requiredFile in RequiredApplicationFiles)
        {
            Assert.Contains(PackageRootName + "/" + requiredFile, entryNames);
        }

        foreach (string requiredFile in RequiredDocumentationFiles)
        {
            Assert.Contains(PackageRootName + "/" + requiredFile, entryNames);
        }

        AssertDocumentationSet(entryNames.Select(name => name[(PackageRootName.Length + 1)..]));

        ZipArchiveEntry releaseNotesEntry = Assert.Single(
            entries,
            entry => entry.FullName == PackageRootName + "/RELEASE-NOTES.txt");
        using StreamReader reader = new(
            releaseNotesEntry.Open(),
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        string releaseNotes = reader.ReadToEnd();
        Assert.Contains("Signing: UNSIGNED\n", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Deployment: .NET 10 self-contained folder\n", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("Symbols: EXCLUDED\n", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("GitHub Copilot CLI runtime: BUNDLED\n", releaseNotes, StringComparison.Ordinal);
        Assert.Contains("User guide: README.md and docs/getting-started.md\n", releaseNotes, StringComparison.Ordinal);
        Assert.DoesNotContain("Signing: SIGNED", releaseNotes, StringComparison.Ordinal);
    }

    private static void AssertDocumentationSet(IEnumerable<string> relativePaths)
    {
        // Count only the public documents, license and images, not runtime/package metadata.
        string[] documentationEntries = relativePaths
            .Where(path => path.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(25, RequiredDocumentationFiles.Length);
        Assert.Equal(25, documentationEntries.Length);
        Assert.Equal(
            RequiredDocumentationFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            documentationEntries,
            StringComparer.Ordinal);
    }

    private static string AssertHashSidecar(string zipPath, string hashPath)
    {
        byte[] sidecarBytes = File.ReadAllBytes(hashPath);
        Assert.NotEmpty(sidecarBytes);
        Assert.Equal((byte)'\n', sidecarBytes[^1]);
        Assert.DoesNotContain((byte)'\r', sidecarBytes);

        string actual = Encoding.UTF8.GetString(sidecarBytes);
        string hash = ComputeSha256(zipPath);
        Assert.Equal($"{hash}  {ZipFileName}\n", actual);
        Assert.Matches("^[0-9A-F]{64}  StudyReportEvaluator-win-x64[.]zip\\n$", actual);
        return hash;
    }

    private static void AssertExtractedDocumentationLinks(string packageRoot)
    {
        string canonicalRoot = Path.GetFullPath(packageRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string[] markdownPaths = RequiredDocumentationFiles
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.Combine(packageRoot, path.Replace('/', Path.DirectorySeparatorChar)))
            .ToArray();
        Assert.NotEmpty(markdownPaths);
        int linkCount = 0;
        foreach (string markdownPath in markdownPaths)
        {
            string content = File.ReadAllText(markdownPath);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                         content,
                         @"!?\[[^\]]*\]\((?<target>[^)]+)\)",
                         System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            {
                string target = match.Groups["target"].Value.Trim().Trim('<', '>');
                if (target.StartsWith('#')
                    || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                linkCount++;
                target = Uri.UnescapeDataString(target.Split('#', 2)[0].Split('?', 2)[0]);
                string resolved = Path.GetFullPath(
                    target.Replace('/', Path.DirectorySeparatorChar),
                    Path.GetDirectoryName(markdownPath)!);
                Assert.StartsWith(canonicalRoot, resolved, StringComparison.OrdinalIgnoreCase);
                Assert.True(
                    File.Exists(resolved) || Directory.Exists(resolved),
                    $"Broken extracted documentation link: {Path.GetRelativePath(packageRoot, markdownPath)} -> {target}");
            }
        }

        Assert.True(linkCount >= 25, $"Expected at least 25 extracted documentation links, found {linkCount}.");
    }

    private static void AssertBundledCopilotRuntime(string applicationDirectory)
    {
        string manifestPath = Path.Combine(applicationDirectory, BundledCopilotCliPathResolver.ManifestFileName);
        string cliPath = Path.Combine(
            applicationDirectory,
            CopilotCliRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(cliPath));
        Assert.True(new FileInfo(manifestPath).Length > 0);
        Assert.True(new FileInfo(cliPath).Length > 0);

        string manifestJson = File.ReadAllText(
            manifestPath,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        using JsonDocument document = JsonDocument.Parse(manifestJson);
        JsonElement root = document.RootElement;
        string[] propertyNames = root.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(
            ["schemaVersion", "runtimeIdentifier", "cliVersion", "cliSha256", "sdkVersion", "cliRelativePath"],
            propertyNames);
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("win-x64", root.GetProperty("runtimeIdentifier").GetString());
        Assert.Equal(CopilotCliRelativePath, root.GetProperty("cliRelativePath").GetString());

        string cliVersion = Assert.IsType<string>(root.GetProperty("cliVersion").GetString());
        string cliSha256 = Assert.IsType<string>(root.GetProperty("cliSha256").GetString());
        string sdkVersion = Assert.IsType<string>(root.GetProperty("sdkVersion").GetString());
        Assert.Matches("^[0-9][A-Za-z0-9._+-]{0,127}$", cliVersion);
        Assert.Matches("^[0-9A-F]{64}$", cliSha256);
        Assert.Equal(ComputeSha256(cliPath), cliSha256);
        Assert.Equal(
            typeof(CopilotClient).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                .Split('+', 2)[0],
            sdkVersion);

        FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(cliPath);
        Assert.True(
            string.Equals(versionInfo.ProductVersion, cliVersion, StringComparison.Ordinal)
            || string.Equals(versionInfo.FileVersion, cliVersion, StringComparison.Ordinal));
        AssertAmd64AppHost(cliPath);
    }

    private static bool VerifyHashSidecar(string candidateZipPath, string hashPath)
    {
        string sidecar = File.ReadAllText(hashPath, Encoding.UTF8);
        if (sidecar.Length != 64 + 2 + ZipFileName.Length + 1 ||
            !sidecar.EndsWith($"  {ZipFileName}\n", StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(
            sidecar[..64],
            ComputeSha256(candidateZipPath),
            StringComparison.Ordinal);
    }

    private static async Task AssertApplicationStartsAndStopsAsync(
        string applicationDirectory,
        CancellationToken cancellationToken)
    {
        string executablePath = Path.Combine(
            applicationDirectory,
            "StudyReportEvaluator.App.exe");
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = applicationDirectory,
                UseShellExecute = false,
                CreateNoWindow = false,
            },
        };
        process.StartInfo.Environment["DOTNET_ROOT"] = Path.Combine(
            applicationDirectory,
            "__no-installed-dotnet__");
        process.StartInfo.Environment["DOTNET_ROOT_X64"] = Path.Combine(
            applicationDirectory,
            "__no-installed-dotnet-x64__");
        process.StartInfo.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";

        try
        {
            Assert.True(process.Start());
            bool exitedDuringStartup = await WaitForExitAsync(
                process,
                TimeSpan.FromMilliseconds(1_500),
                cancellationToken);
            Assert.False(
                exitedDuringStartup,
                exitedDuringStartup
                    ? $"Packaged application exited during startup with code {process.ExitCode}."
                    : "Packaged application must remain alive during startup.");

            process.Refresh();
            ProcessModule[] modules = process.Modules.Cast<ProcessModule>().ToArray();
            ProcessModule coreClr = Assert.Single(
                modules,
                module => module.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(
                Path.GetFullPath(applicationDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(Path.GetDirectoryName(coreClr.FileName)!)
                    .TrimEnd(Path.DirectorySeparatorChar),
                ignoreCase: true);
            Assert.DoesNotContain(
                modules,
                module => IsOfficeRuntimeModule(module.ModuleName));

            if (process.CloseMainWindow() &&
                await WaitForExitAsync(
                    process,
                    TimeSpan.FromSeconds(5),
                    cancellationToken))
            {
                Assert.Equal(0, process.ExitCode);
                return;
            }

            process.Kill(entireProcessTree: true);
            Assert.True(await WaitForExitAsync(
                process,
                TimeSpan.FromSeconds(5),
                CancellationToken.None));
        }
        finally
        {
            await EnsureStoppedAsync(process);
        }
    }

    private static async Task<ProcessResult> RunPowerShellScriptAsync(
        string powerShellPath,
        string scriptPath,
        IReadOnlyList<string> scriptArguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = CreatePowerShellStartInfo(powerShellPath);
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (string argument in scriptArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return await RunPowerShellProcessAsync(
            startInfo,
            scriptPath,
            cancellationToken);
    }

    private static async Task<ProcessResult> RunPowerShellCommandAsync(
        string powerShellPath,
        string command,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = CreatePowerShellStartInfo(powerShellPath);
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        return await RunPowerShellProcessAsync(
            startInfo,
            "PowerShell Core command",
            cancellationToken);
    }

    private static ProcessStartInfo CreatePowerShellStartInfo(string powerShellPath)
    {
        return new ProcessStartInfo
        {
            FileName = powerShellPath,
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
    }

    private static async Task<ProcessResult> RunPowerShellProcessAsync(
        ProcessStartInfo startInfo,
        string operation,
        CancellationToken cancellationToken)
    {
        using Process process = new() { StartInfo = startInfo };
        Assert.True(process.Start());
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw new TimeoutException($"PowerShell operation timed out: {operation}");
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string QuotePowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static void AssertProcessSucceeded(ProcessResult result, string operation)
    {
        Assert.True(
            result.ExitCode == 0,
            $"{operation} failed with exit code {result.ExitCode}.\nSTDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
    }

    private static void AssertProcessFailed(
        ProcessResult result,
        string operation,
        string expectedMessage)
    {
        Assert.True(
            result.ExitCode != 0,
            $"{operation} unexpectedly succeeded.\nSTDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
        Assert.Contains(
            expectedMessage,
            result.StandardOutput + "\n" + result.StandardError,
            StringComparison.Ordinal);
    }

    private static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task EnsureStoppedAsync(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            if (process.CloseMainWindow() &&
                await WaitForExitAsync(
                    process,
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None))
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        Assert.True(await WaitForExitAsync(
            process,
            TimeSpan.FromSeconds(5),
            CancellationToken.None));
    }

    private static async Task DeleteDirectoryWithRetriesAsync(string path)
    {
        const int maximumAttempts = 20;
        for (int attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                Assert.False(Directory.Exists(path));
                return;
            }
            catch (Exception exception) when (
                attempt < maximumAttempts &&
                exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }

        Directory.Delete(path, recursive: true);
        Assert.False(Directory.Exists(path));
    }

    private static void AssertNoForbiddenPath(string relativePath, bool allowReleaseNotes)
    {
        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain(segments, segment => segment is "." or "..");
        Assert.DoesNotContain(segments, ForbiddenSegments.Contains);
        Assert.DoesNotContain(
            ForbiddenDependencyMarkers,
            marker => relativePath.Contains(marker, StringComparison.OrdinalIgnoreCase));

        string fileName = segments[^1];
        if (allowReleaseNotes && fileName.Equals("RELEASE-NOTES.txt", StringComparison.Ordinal))
        {
            return;
        }

        Assert.DoesNotContain(Path.GetExtension(fileName), ForbiddenExtensions);
        Assert.False(fileName.Equals(".env", StringComparison.OrdinalIgnoreCase));
        Assert.False(fileName.Equals("setting.txt", StringComparison.OrdinalIgnoreCase));
        Assert.False(
            ContainsSensitiveFileNameToken(fileName),
            $"Potential secret file name is not allowed: {relativePath}");
    }

    private static bool ContainsSensitiveFileNameToken(string fileName)
    {
        string[] tokens = fileName.Split(
            ['.', '_', '-'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(token =>
            token.Equals("secret", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("password", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("credential", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("token", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertSafeRelativePath(string relativePath)
    {
        Assert.False(string.IsNullOrWhiteSpace(relativePath));
        Assert.False(Path.IsPathRooted(relativePath));
        Assert.DoesNotContain('\\', relativePath);
        Assert.DoesNotContain('\0', relativePath);
        Assert.DoesNotContain(
            relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries),
            segment => segment is "." or ".." || segment.Contains(':', StringComparison.Ordinal));
    }

    private static void AssertNoReparsePoint(string path)
    {
        Assert.Equal(
            0,
            (int)(File.GetAttributes(path) & FileAttributes.ReparsePoint));
    }

    private static void AssertAmd64AppHost(string executablePath)
    {
        using FileStream stream = new(
            executablePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using PEReader reader = new(stream);
        Assert.Equal(Machine.Amd64, reader.PEHeaders.CoffHeader.Machine);
    }

    private static void AssertNotReadyToRun(string applicationPath)
    {
        using FileStream stream = new(
            applicationPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using PEReader reader = new(stream);
        Assert.NotNull(reader.PEHeaders.CorHeader);
        Assert.Equal(0, reader.PEHeaders.CorHeader.ManagedNativeHeaderDirectory.Size);
    }

    private static bool IsOfficeRuntimeModule(string moduleName)
    {
        string name = Path.GetFileNameWithoutExtension(moduleName);
        return name.Equals("excel", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("soffice", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("libreoffice", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("office.interop", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Microsoft.Office", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void CreateMinimalPackageInput(string directory)
    {
        foreach (string requiredFile in RequiredApplicationFiles)
        {
            string path = Path.Combine(directory, requiredFile.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [0x01]);
        }
    }

    private static IReadOnlyDictionary<string, FileHashSnapshot> CapturePackageOutputSnapshots(
        string repositoryRoot)
    {
        string packageDirectory = Path.Combine(repositoryRoot, "artifacts", "package");
        string[] outputPaths =
        [
            Path.Combine(packageDirectory, ZipFileName),
            Path.Combine(packageDirectory, HashFileName),
        ];
        return outputPaths.ToDictionary(
            path => path,
            path => File.Exists(path)
                ? new FileHashSnapshot(true, ComputeSha256(path))
                : new FileHashSnapshot(false, null),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertPackageOutputsUnchanged(
        IReadOnlyDictionary<string, FileHashSnapshot> expectedSnapshots)
    {
        foreach ((string path, FileHashSnapshot expected) in expectedSnapshots)
        {
            Assert.Equal(expected.Exists, File.Exists(path));
            if (expected.Exists)
            {
                Assert.Equal(expected.Sha256, ComputeSha256(path));
            }
        }
    }

    private static void AssertLockFilesUnchanged(
        IReadOnlyDictionary<string, string> originalHashes)
    {
        foreach ((string path, string expectedHash) in originalHashes)
        {
            Assert.True(File.Exists(path));
            Assert.Equal(expectedHash, ComputeSha256(path));
        }
    }

    private static string FindPowerShellCoreExecutable()
    {
        string? currentProcess = Environment.ProcessPath;
        if (currentProcess is not null &&
            Path.GetFileName(currentProcess).Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(currentProcess);
        }

        string? pathEnvironment = Environment.GetEnvironmentVariable("PATH");
        foreach (string directory in (pathEnvironment ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidateDirectory = directory.Trim('"');
            string candidate = Path.Combine(candidateDirectory, "pwsh.exe");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            "PowerShell Core 7+ executable pwsh.exe was not found on PATH.");
    }

    private static void RequireWindows11X64()
    {
        Assert.True(OperatingSystem.IsWindows());
        Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000));
        Assert.Equal(Architecture.X64, RuntimeInformation.OSArchitecture);
        Assert.Equal(Architecture.X64, RuntimeInformation.ProcessArchitecture);
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

        throw new DirectoryNotFoundException(
            "The repository root containing StudyReportEvaluator.slnx was not found.");
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed record FileHashSnapshot(
        bool Exists,
        string? Sha256);
}
