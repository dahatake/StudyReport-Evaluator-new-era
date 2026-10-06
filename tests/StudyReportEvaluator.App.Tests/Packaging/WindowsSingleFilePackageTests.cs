using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Packaging;

// P06: consume the parent's final artifact, never restore/publish/package or start Copilot.
// UIA needs an interactive development/CI desktop, installed pwsh 7+ and WindowsDesktop 10.0.x.
// These are OBSERVER prerequisites, not application dependencies or clean-host evidence.
// Child-process checks are bounded snapshots, NOT a complete process-event/AI-send audit.
// Keep LaunchOptionsTests (parser, explicit application, no-auto-run) as required regressions.
// Disk-full, interrupted extraction, directory ACLs and CH-01..06 are not claimed here.
// Requirements: FR-055 (AC-056)
[Collection<WindowsPublishPackageCollection>]
[Trait("Category", "WindowsSingleFilePackageIntegration")]
public sealed class WindowsSingleFilePackageTests(ITestOutputHelper output)
{
    private const string ArtifactName = "StudyReportEvaluator-win-x64.exe";
    private const string ApplicationName = "StudyReportEvaluator.App";
    private const string CliRelativePath = "runtimes/win-x64/native/copilot.exe";
    private const string SkipReason = "Set RUN_WINDOWS_SINGLEFILE_PACKAGE_TESTS=1 after parent publish/package; missing artifacts then FAIL.";
    private const int StartupTimeoutMilliseconds = 40_000; // Finite CI allowance, not a product SLA.
    private const string FirstPromptName = "評価 02 first.txt"; // Intentionally not alphabetical order.
    private const string SecondPromptName = "評価 01 second.txt";
    private const string FirstPrompt = "P06 synthetic first {回答} {評価項目}";
    private const string SecondPrompt = "P06 synthetic second {回答} {評価項目}";

    private static readonly string[] DocumentationFiles =
    [
        "README.md", "LICENSE", "docs/README.md", "docs/getting-started.md",
        "docs/features.md", "docs/custom-evaluator-guide.md", "docs/prompt-launch.md",
        "docs/privacy-and-data-handling.md", "docs/troubleshooting.md",
        "docs/settings.md", "docs/third-party-notices.md", "docs/result-excel-description.md", "docs/technical-guid.md", "images/README.md",
        "images/architecture-overview.svg", "images/technical-architecture.svg",
        "images/evaluation-message-flow.svg",
        "images/01-input-workbook.png", "images/02-input-mapping.png",
        "images/03-design-knowledge.png", "images/04-design-custom-prompt.png",
        "images/05-execution-auto.png", "images/06-results-review.png", "images/07-output-export.png",
        "images/08-settings.png",
    ];

    // Import ONLY these already-tested P02 validators, never the script's entry point/launcher.
    private static readonly string[] ValidationFunctions =
    [
        "Assert-NotReparsePoint", "Get-Sha256Hex", "Assert-Amd64PortableExecutable",
        "Assert-BundledCopilotRuntime", "Get-SingleFileDocumentationPaths", "Assert-SafePublishLayout",
        "Get-PublishProductVersion", "Assert-PublishBinaryVersion", "Assert-NoStartupChildProcesses",
    ];

    private static readonly string[] NativeFiles = ["av_libglesv2.dll", "libHarfBuzzSharp.dll", "libSkiaSharp.dll"];

    public static bool RunWindowsSingleFilePackageTests =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_WINDOWS_SINGLEFILE_PACKAGE_TESTS"), "1", StringComparison.Ordinal);

    [Fact(SkipUnless = nameof(RunWindowsSingleFilePackageTests), Skip = SkipReason)]
    public async Task Single_file_cold_and_warm_start_validate_payload_and_show_input()
    {
        await using PackageRun run = new(output);
        Assert.False(Directory.Exists(run.CacheDirectory));
        AppInstance cold = run.Start("cold");
        string coldBase = await run.ObserveAsync(cold);
        Assert.Contains("Starting new extraction of application bundle.", cold.Trace, StringComparison.Ordinal);
        await run.CloseAsync(cold);
        Dictionary<string, FileSnapshot> cache = CaptureFiles(coldBase);

        AppInstance warm = run.Start("warm");
        Assert.Equal(coldBase, await run.ObserveAsync(warm));
        Assert.Contains("Reusing existing extraction of application bundle.", warm.Trace, StringComparison.Ordinal);
        Assert.DoesNotContain("Starting new extraction of application bundle.", warm.Trace, StringComparison.Ordinal);
        AssertFilesUnchanged(cache, coldBase);
        await run.CloseAsync(warm);
        AssertFilesUnchanged(cache, coldBase);
    }

    [Fact(SkipUnless = nameof(RunWindowsSingleFilePackageTests), Skip = SkipReason)]
    public async Task Single_file_missing_cached_CLI_is_recovered_by_the_standard_host()
    {
        await using PackageRun run = new(output);
        AppInstance first = run.Start("before-file-loss");
        string appBase = await run.ObserveAsync(first);
        await run.CloseAsync(first);
        string cli = Path.Combine(appBase, CliRelativePath);
        FileSnapshot originalCli = FileSnapshot.Read(cli);
        string retained = Path.Combine(appBase, "LICENSE");
        FileSnapshot originalRetained = FileSnapshot.Read(retained);
        File.Delete(cli); // Only this run's extraction, after its process exited.
        Assert.False(File.Exists(cli));
        run.AssertDataUnchanged();

        AppInstance recovered = run.Start("missing-file-recovered");
        Assert.Equal(appBase, await run.ObserveAsync(recovered));
        Assert.Contains("Extraction recovered [" + CliRelativePath + "]", recovered.Trace.Replace('\\', '/'), StringComparison.Ordinal);
        FileSnapshot restoredCli = FileSnapshot.Read(cli);
        Assert.Equal(originalCli.Sha256, restoredCli.Sha256);
        Assert.Equal(originalCli.Length, restoredCli.Length); // A recovered file may have a new write time.
        Assert.Equal(originalRetained, FileSnapshot.Read(retained));
        await run.CloseAsync(recovered);
    }

    [Fact(SkipUnless = nameof(RunWindowsSingleFilePackageTests), Skip = SkipReason)]
    public async Task Single_file_deleted_cache_is_reextracted_after_moving_the_EXE()
    {
        await using PackageRun run = new(output);
        AppInstance first = run.Start("before-cache-loss");
        string appBase = await run.ObserveAsync(first);
        await run.CloseAsync(first);
        Dictionary<string, FileSnapshot> original = CaptureFiles(appBase);
        run.DeleteOwnedCacheAndMoveExecutable();
        Assert.False(Directory.Exists(run.CacheDirectory));
        run.AssertDataUnchanged();

        AppInstance reextracted = run.Start("reextracted-after-move");
        Assert.Equal(appBase, await run.ObserveAsync(reextracted));
        Assert.Contains("Starting new extraction of application bundle.", reextracted.Trace, StringComparison.Ordinal);
        Dictionary<string, FileSnapshot> restored = CaptureFiles(appBase);
        Assert.Equal(original.Keys.Order(StringComparer.Ordinal), restored.Keys.Order(StringComparer.Ordinal));
        foreach ((string relative, FileSnapshot expected) in original)
        {
            Assert.Equal(expected.Sha256, restored[relative].Sha256);
            Assert.Equal(expected.Length, restored[relative].Length);
        }

        await run.CloseAsync(reextracted);
    }

    [Fact(SkipUnless = nameof(RunWindowsSingleFilePackageTests), Skip = SkipReason)]
    public async Task Single_file_two_instances_share_cache_and_close_independently()
    {
        await using PackageRun run = new(output);
        AppInstance first = run.Start("concurrent-first");
        AppInstance second = run.Start("concurrent-second"); // Both start before either readiness wait.
        await run.WaitForStartupAsync(first);
        await run.WaitForStartupAsync(second); // Do not inspect a peer's still-in-progress extraction.
        string appBase = await run.ObserveAsync(first);
        Assert.Equal(appBase, await run.ObserveAsync(second));
        Assert.NotEqual(first.Process.Id, second.Process.Id);
        Assert.NotEqual(first.Process.MainWindowHandle, second.Process.MainWindowHandle);
        Dictionary<string, FileSnapshot> cache = CaptureFiles(appBase);

        await run.CloseAsync(first);
        Assert.False(await WaitForExitAsync(second.Process, TimeSpan.FromMilliseconds(1_500), TestContext.Current.CancellationToken));
        await run.CheckGuiAndPayloadAsync(second, appBase, withPrompts: false);
        AssertFilesUnchanged(cache, appBase);
        await run.CloseAsync(second);
        AssertFilesUnchanged(cache, appBase); // Application exit must not sweep shared cache/data.
    }

    [Fact(SkipUnless = nameof(RunWindowsSingleFilePackageTests), Skip = SkipReason)]
    public async Task Single_file_relative_input_and_two_prompts_are_observed_in_GUI_order()
    {
        await using PackageRun run = new(output);
        AppInstance app = run.Start("relative-input-prompts",
            "--input", Path.GetRelativePath(run.WorkingDirectory, run.InputPath),
            "--prompt", Path.GetRelativePath(run.WorkingDirectory, Path.Combine(run.DataDirectory, FirstPromptName)),
            "--prompt", Path.GetRelativePath(run.WorkingDirectory, Path.Combine(run.DataDirectory, SecondPromptName)));

        // UIA reads InputFilePath and loaded metadata, then Design -> Settings list order and both previews.
        // It returns to Design without login, authentication checks, Prompt application, saving or evaluation.
        await run.ObserveAsync(app, withPrompts: true);
        await run.CloseAsync(app);
    }

    [Fact(SkipUnless = nameof(RunWindowsSingleFilePackageTests), Skip = SkipReason)]
    public async Task Single_file_file_valued_extraction_base_fails_without_changing_data()
    {
        await using PackageRun run = new(output);
        File.WriteAllText(run.CacheDirectory, "P06 owned extraction barrier", new UTF8Encoding(false));
        FileSnapshot barrier = FileSnapshot.Read(run.CacheDirectory);
        AppInstance app = run.Start("extraction-base-is-file");
        await run.AssertHostFailureAsync(app, "Failed to create directory [");
        Assert.Equal(barrier, FileSnapshot.Read(run.CacheDirectory));
        Assert.False(Directory.Exists(run.CacheDirectory));
    }

    [Fact(SkipUnless = nameof(RunWindowsSingleFilePackageTests), Skip = SkipReason)]
    public async Task Single_file_truncated_bundle_reports_a_host_error_without_changing_data()
    {
        await using PackageRun run = new(output);
        run.TruncateOwnedExecutable();
        AppInstance app = run.Start("truncated-bundle");
        await run.AssertHostFailureAsync(app, "possible file corruption");
        Assert.False(Directory.Exists(run.CacheDirectory));
        Assert.False(File.Exists(run.CacheDirectory));
    }

    private sealed class PackageRun : IAsyncDisposable
    {
        private readonly ITestOutputHelper output;
        private readonly List<AppInstance> applications = [];
        private readonly string sourcePath;
        private readonly FileSnapshot sourceSnapshot;
        private readonly FileSnapshot sidecarSnapshot;
        private readonly Dictionary<string, FileSnapshot> dataFiles;
        private readonly Dictionary<string, FileSnapshot> cwdFiles;
        private readonly string[] dataEntries;
        private readonly string[] cwdEntries;
        private FileSnapshot expectedExecutable;

        internal PackageRun(ITestOutputHelper output)
        {
            this.output = output;
            Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000), "P06 requires Windows 11 x64.");
            Assert.Equal(Architecture.X64, RuntimeInformation.OSArchitecture);
            Assert.Equal(Architecture.X64, RuntimeInformation.ProcessArchitecture);
            RepositoryRoot = FindRepositoryRoot();
            sourcePath = Path.Combine(RepositoryRoot, "artifacts", "package", ArtifactName);
            Assert.True(File.Exists(sourcePath) && File.Exists(sourcePath + ".sha256"),
                "Opt-in requires artifacts/package/StudyReportEvaluator-win-x64.exe and its .sha256; missing is FAIL, not SKIP.");
            sourceSnapshot = FileSnapshot.Read(sourcePath);
            sidecarSnapshot = FileSnapshot.Read(sourcePath + ".sha256");
            Assert.True(sourceSnapshot.Length > 0);
            Assert.Equal(Encoding.UTF8.GetBytes($"{sourceSnapshot.Sha256}  {ArtifactName}\n"), File.ReadAllBytes(sourcePath + ".sha256"));
            AssertAmd64(sourcePath);
            XDocument props = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
            string prefix = Assert.Single(props.Descendants("VersionPrefix")).Value;
            string suffix = Assert.Single(props.Descendants("VersionSuffix")).Value;
            string version = suffix.Length == 0 ? prefix : $"{prefix}-{suffix}";
            FileVersionInfo binaryVersion = FileVersionInfo.GetVersionInfo(sourcePath);
            Assert.Equal(version, binaryVersion.ProductVersion?.Split('+', 2)[0]);
            Assert.Equal(prefix + ".0", binaryVersion.FileVersion);

            Root = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-P06-" + Guid.NewGuid().ToString("N"));
            PackageDirectory = Path.Combine(Root, "日本語 package");
            DataDirectory = Path.Combine(Root, "日本語 data");
            WorkingDirectory = Path.Combine(Root, "別の cwd");
            Assert.False(Directory.Exists(Root) || File.Exists(Root));
            Directory.CreateDirectory(Root);
            try
            {
                foreach (string directory in new[] { PackageDirectory, DataDirectory, WorkingDirectory, Path.Combine(Root, "traces") })
                {
                    Directory.CreateDirectory(directory);
                }

                File.Copy(sourcePath, ExecutablePath, overwrite: false); // No sidecar/DLL/manifest beside the EXE.
                expectedExecutable = FileSnapshot.Read(ExecutablePath);
                Assert.Equal(sourceSnapshot.Sha256, expectedExecutable.Sha256);
                Assert.Equal(sourceSnapshot.Length, expectedExecutable.Length);
                CreateSyntheticWorkbook(InputPath);
                File.WriteAllText(Path.Combine(DataDirectory, FirstPromptName), FirstPrompt, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(DataDirectory, SecondPromptName), SecondPrompt, new UTF8Encoding(false));
                File.Copy(InputPath, Path.Combine(WorkingDirectory, "cwd input.xlsx"));
                foreach (string directory in new[] { DataDirectory, WorkingDirectory })
                {
                    string result = Path.Combine(directory, "result");
                    Directory.CreateDirectory(result);
                    // Valid synthetic workbooks used as preservation sentinels, not claimed resume checkpoints.
                    File.Copy(InputPath, Path.Combine(result, "keep.final.xlsx"));
                    File.Copy(InputPath, Path.Combine(result, "keep.partial.xlsx"));
                }

                dataFiles = CaptureFiles(DataDirectory);
                cwdFiles = CaptureFiles(WorkingDirectory);
                dataEntries = RelativeEntries(DataDirectory);
                cwdEntries = RelativeEntries(WorkingDirectory);
                SetIsolatedEnvironment(new ProcessStartInfo()); // Create only owned homes/TEMP, not .net roots/cache.
                AssertDataUnchanged();
            }
            catch
            {
                DeletePlainDirectory(Root);
                throw;
            }
        }

        internal string RepositoryRoot { get; }
        internal string Root { get; }
        internal string PackageDirectory { get; private set; }
        internal string ExecutablePath => Path.Combine(PackageDirectory, ArtifactName);
        internal string DataDirectory { get; }
        internal string WorkingDirectory { get; }
        internal string InputPath => Path.Combine(DataDirectory, "入力 report.xlsx");
        internal string CacheDirectory => Path.Combine(Root, "TEMP", ".net");

        internal AppInstance Start(string scenario, params string[] arguments)
        {
            AssertDataUnchanged();
            string trace = Path.Combine(Root, "traces", scenario + ".host.log");
            Assert.False(File.Exists(trace));
            ProcessStartInfo info = CreateStartInfo(ExecutablePath, createNoWindow: false);
            info.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = CacheDirectory;
            info.Environment["DOTNET_HOST_TRACE"] = "1";
            info.Environment["DOTNET_HOST_TRACE_VERBOSITY"] = "4";
            info.Environment["DOTNET_HOST_TRACEFILE"] = trace;
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            Process process = new() { StartInfo = info };
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                Assert.True(process.Start()); // Win32 start failure is NOT accepted as a native-host negative pass.
            }
            catch
            {
                process.Dispose();
                throw;
            }

            AppInstance instance = new(process, scenario, trace, watch,
                process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
            applications.Add(instance);
            return instance;
        }

        internal async Task WaitForStartupAsync(AppInstance app)
        {
            if (!app.StartupObserved)
            {
                Stopwatch startup = Stopwatch.StartNew();
                Assert.True(app.Process.WaitForInputIdle(StartupTimeoutMilliseconds), $"{app.Scenario}: input-idle timed out.");
                // Input-idle can precede creation of the main window; share the same startup deadline.
                while (true)
                {
                    app.Process.Refresh();
                    Assert.False(app.Process.HasExited,
                        $"{app.Scenario}: application exited during startup; no GUI observation was made.");
                    if (app.Process.MainWindowHandle != IntPtr.Zero) { break; }
                    long remaining = StartupTimeoutMilliseconds - startup.ElapsedMilliseconds;
                    Assert.True(remaining > 0, $"{app.Scenario}: main window timed out.");
                    Assert.False(await WaitForExitAsync(app.Process, TimeSpan.FromMilliseconds(Math.Min(250, remaining)), TestContext.Current.CancellationToken),
                        $"{app.Scenario}: application exited during startup; no GUI observation was made.");
                }

                app.StartupObserved = true;
            }

            app.Process.Refresh();
            Assert.False(app.Process.HasExited);
            Assert.NotEqual(IntPtr.Zero, app.Process.MainWindowHandle); // Necessary, but UIA below is the screen oracle.
        }

        internal async Task<string> ObserveAsync(AppInstance app, bool withPrompts = false)
        {
            await WaitForStartupAsync(app);
            string appBase = GetExtractedBase(app);
            await CheckGuiAndPayloadAsync(app, appBase, withPrompts);
            foreach (string native in NativeFiles)
            {
                AssertAmd64(Path.Combine(appBase, native));
            }

            Assert.Equal(Path.GetFullPath(ExecutablePath), Path.GetFullPath(app.Process.MainModule!.FileName), ignoreCase: true);
            foreach (ProcessModule module in app.Process.Modules)
            {
                if (NativeFiles.Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase)
                    || new[] { "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll" }
                        .Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase))
                {
                    AssertInside(appBase, module.FileName); // No installed .NET/native fallback; static host components may be absent.
                }
            }

            app.Watch.Stop();
            string[] files = PlainEntries(appBase).Where(File.Exists).ToArray();
            // MSBuild WriteLinesToFile emits a UTF-8 BOM; text decoding consumes it.
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(appBase, "copilot-runtime.json"), Encoding.UTF8));
            output.WriteLine("P06_OBSERVATION " + JsonSerializer.Serialize(new
            {
                scenario = app.Scenario,
                artifactName = ArtifactName,
                artifactSha256 = sourceSnapshot.Sha256,
                exeBytes = sourceSnapshot.Length,
                observationMilliseconds = app.Watch.ElapsedMilliseconds, // Includes liveness/UIA/hash/peer waits, not pure startup time.
                extractedFileCount = files.Length,
                extractedBytes = files.Sum(path => new FileInfo(path).Length),
                cliSha256 = manifest.RootElement.GetProperty("cliSha256").GetString(),
                inputScreenObserved = true,
                importedPromptCountObserved = withPrompts ? 2 : (int?)null,
                childProcessObservation = "two-bounded-snapshots-not-an-event-audit",
                cleanHost = "NOT_RUN",
                networkIsolation = "NOT_RUN",
                authentication = "NOT_RUN",
                liveAi = "NOT_RUN",
            }));
            AssertDataUnchanged();
            return appBase;
        }

        private string GetExtractedBase(AppInstance app)
        {
            Assert.True(File.Exists(app.TracePath), "Standard DOTNET_HOST_TRACEFILE is missing.");
            const string prefix = "Property APP_CONTEXT_BASE_DIRECTORY = ";
            string line = Assert.Single(app.Trace.Split('\n'), value => value.StartsWith(prefix, StringComparison.Ordinal));
            string value = line[prefix.Length..].Trim();
            Assert.True(Path.IsPathFullyQualified(value));
            string appBase = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
            AssertInside(CacheDirectory, appBase);
            string[] segments = Path.GetRelativePath(CacheDirectory, appBase).Replace('\\', '/').Split('/');
            Assert.Equal(2, segments.Length);
            Assert.Equal(Path.GetFileNameWithoutExtension(ArtifactName), segments[0]); // Final EXE basename, NOT ApplicationName.
            Assert.False(string.IsNullOrWhiteSpace(segments[1]));
            string application = Assert.Single(Directory.GetFileSystemEntries(CacheDirectory));
            Assert.Equal(Path.Combine(CacheDirectory, segments[0]), application, ignoreCase: true);
            Assert.Equal(appBase, Assert.Single(Directory.GetFileSystemEntries(application)), ignoreCase: true);
            Assert.NotEmpty(PlainEntries(appBase));
            return appBase;
        }

        internal async Task CheckGuiAndPayloadAsync(AppInstance app, string appBase, bool withPrompts)
        {
            string desktopRuntime = FindWindowsDesktopRuntime();
            string command = $$"""
                $RepositoryRoot = {{Quote(RepositoryRoot)}}
                $ApplicationName = '{{ApplicationName}}'
                $RuntimeIdentifier = 'win-x64'
                $TargetFramework = 'net10.0'
                $base = {{Quote(appBase)}}
                $tokens = $null; $parseErrors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                    (Join-Path $RepositoryRoot 'scripts/publish-windows.ps1'), [ref]$tokens, [ref]$parseErrors)
                if (@($parseErrors).Count -ne 0) { throw 'P02 validation script has parse errors.' }
                foreach ($name in @({{string.Join(",", ValidationFunctions.Select(Quote))}})) {
                    $definitions = @($ast.EndBlock.Statements | Where-Object {
                        $_ -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $_.Name -ceq $name
                    })
                    if ($definitions.Count -ne 1) { throw "Expected one read-only validator: $name" }
                    . ([scriptblock]::Create($definitions[0].Extent.Text))
                }
                Assert-SafePublishLayout -PublishDirectory $base -ExtractedBundle
                $version = Get-PublishProductVersion -RepositoryRoot $RepositoryRoot
                foreach ($assembly in @('StudyReportEvaluator.App.dll', 'StudyReportEvaluator.Core.dll')) {
                    Assert-PublishBinaryVersion -Path (Join-Path $base $assembly) -ExpectedVersion $version -Managed
                }
                $manifest = Get-Content -LiteralPath (Join-Path $base 'copilot-runtime.json') -Raw | ConvertFrom-Json
                if ($manifest.cliVersion -cne '1.0.79' -or $manifest.sdkVersion -cne '1.0.11') {
                    throw 'Extracted CLI/SDK is not the approved fixed version.'
                }
                $deps = Get-Content -LiteralPath (Join-Path $base 'StudyReportEvaluator.App.deps.json') -Raw | ConvertFrom-Json
                foreach ($library in @("StudyReportEvaluator.App/$version", "StudyReportEvaluator.Core/$version",
                        'GitHub.Copilot.SDK/1.0.11', 'Avalonia/12.1.1', 'DocumentFormat.OpenXml/3.5.1',
                        'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.11')) {
                    if ($library -cnotin @($deps.libraries.PSObject.Properties.Name)) { throw "Wrong dependency identity: $library" }
                }
                $expectedDocs = @({{string.Join(",", DocumentationFiles.Select(Quote))}})
                $actualDocs = @(Get-SingleFileDocumentationPaths)
                $actualDocSet = [Collections.Generic.HashSet[string]]::new([string[]]$actualDocs, [StringComparer]::Ordinal)
                if ($expectedDocs.Count -ne 25 -or $actualDocs.Count -ne 25 -or $actualDocSet.Count -ne 25 -or
                    -not $actualDocSet.SetEquals([string[]]$expectedDocs)) {
                    throw 'P01 public documentation allowlist must contain exactly the 25 approved paths without duplicates.'
                }
                foreach ($relative in $expectedDocs) {
                    if ($relative -cnotin $actualDocs -or
                        (Get-Sha256Hex -Path (Join-Path $base $relative)) -cne
                        (Get-Sha256Hex -Path (Join-Path $RepositoryRoot $relative))) {
                        throw "Bundled public document differs from its allowed source: $relative"
                    }
                }
                Add-Type -Path {{Quote(Path.Combine(desktopRuntime, "UIAutomationTypes.dll"))}}
                Add-Type -Path {{Quote(Path.Combine(desktopRuntime, "UIAutomationClient.dll"))}}
                $appProcess = [Diagnostics.Process]::GetProcessById({{app.Process.Id}})
                try {
                    $childDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
                    while ($true) {
                        try {
                            Assert-NoStartupChildProcesses -ProcessId {{app.Process.Id}}
                            break
                        }
                        catch {
                            if ([DateTimeOffset]::UtcNow -ge $childDeadline) { throw }
                            Start-Sleep -Milliseconds 100
                        }
                    }
                    $appProcess.Refresh()
                    if ($appProcess.HasExited -or $appProcess.MainWindowHandle -eq [IntPtr]::Zero) { throw 'P06 GUI is not alive.' }
                    $window = [Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
                    if ($window.Current.ProcessId -ne {{app.Process.Id}}) { throw 'UIA window belongs to another process.' }
                    function Find-Control([string] $Id, [Windows.Automation.AutomationElement] $Root = $window) {
                        $condition = [Windows.Automation.PropertyCondition]::new(
                            [Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
                        $control = $Root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
                        if ($null -eq $control) { throw "Required UIA control was not found: $Id" }
                        return $control
                    }
                    function Read-Value([Windows.Automation.AutomationElement] $Control) {
                        return ([Windows.Automation.ValuePattern]$Control.GetCurrentPattern(
                            [Windows.Automation.ValuePattern]::Pattern)).Current.Value
                    }
                    function Assert-GuiStillLive {
                        if (-not $appProcess.WaitForInputIdle(20000) -or $appProcess.WaitForExit(1500)) {
                            throw 'GUI did not remain responsive after the UIA operation.'
                        }
                    }
                    $pathBox = Find-Control 'InputFilePath'
                    if ((Read-Value $pathBox) -cne {{Quote(withPrompts ? InputPath : string.Empty)}}) {
                        throw 'UIA InputFilePath does not match the launch cwd (no path values logged).'
                    }
                    if ({{(withPrompts ? "$true" : "$false")}}) {
                        # InputView now puts StatusText in Name and WorkbookSummary in ToolTip/HelpText.
                        # Require both the completed read/snapshot and the exact metadata, not argv prefill.
                        $loadDeadline = [Diagnostics.Stopwatch]::StartNew()
                        do {
                            $loadStatus = (Find-Control 'InputLoadStatus').Current
                            $metadataLoaded = $loadStatus.Name -ceq 'read-only 読込と入力 snapshot の取得が完了しました。' -and
                                $loadStatus.HelpText -ceq '1 sheets · 5 package parts'
                            $headerReady = (Find-Control 'RefreshInputHeader').Current.IsEnabled
                            if ($metadataLoaded -and $headerReady) { break }
                            if ($loadDeadline.Elapsed.TotalSeconds -ge 10) {
                                throw "Synthetic workbook load observation timed out (metadata=$metadataLoaded; headerReady=$headerReady)."
                            }
                            if ($appProcess.WaitForExit(250)) { throw 'Application exited before workbook loading completed.' }
                        } while ($true)
                        $sheet = Find-Control 'InputWorksheet'
                        # The pinned Avalonia UIA bridge exposes no selection items here, but
                        # the rendered TextBlock contains the exact sheet/dimension/state.
                        $expand = [Windows.Automation.ExpandCollapsePattern]$sheet.GetCurrentPattern(
                            [Windows.Automation.ExpandCollapsePattern]::Pattern)
                        $expand.Expand()
                        Assert-GuiStillLive
                        $displayCondition = [Windows.Automation.PropertyCondition]::new(
                            [Windows.Automation.AutomationElement]::NameProperty, 'Synthetic · A1:A2 · 表示')
                        if ($null -eq $sheet.FindFirst([Windows.Automation.TreeScope]::Descendants, $displayCondition)) {
                            throw 'The actual synthetic sheet display was absent from the Input GUI.'
                        }
                        $expand.Collapse()
                        $design = Find-Control 'WorkflowStepDesign'
                        ([Windows.Automation.InvokePattern]$design.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
                        Assert-GuiStillLive
                        $designStepName = (Find-Control 'WorkflowStepDesign').Current.Name
                        $openPrompts = Find-Control 'DesignOpenImportedPrompts'
                        if ($openPrompts.Current.ControlType -ne [Windows.Automation.ControlType]::Button -or
                            $openPrompts.Current.Name -cne '同じ設問を対象に読込Promptの設定を開く' -or
                            -not $openPrompts.Current.IsEnabled) {
                            throw 'Design did not expose the labelled, enabled imported-Prompt settings button.'
                        }
                        ([Windows.Automation.InvokePattern]$openPrompts.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
                        Assert-GuiStillLive
                        $settings = Find-Control 'SettingsView'
                        $promptSettings = Find-Control 'ImportedPromptSettingsView' $settings
                        $list = Find-Control 'ImportedPrompts' $promptSettings
                        $condition = [Windows.Automation.PropertyCondition]::new(
                            [Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)
                        $items = @($list.FindAll([Windows.Automation.TreeScope]::Descendants, $condition))
                        if ($items.Count -ne 2) { throw 'Settings did not expose exactly two imported Prompt items.' }
                        $names = @({{Quote(FirstPromptName)}}, {{Quote(SecondPromptName)}})
                        $contents = @({{Quote(FirstPrompt)}}, {{Quote(SecondPrompt)}})
                        $textCondition = [Windows.Automation.PropertyCondition]::new(
                            [Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text)
                        for ($index = 0; $index -lt 2; $index++) {
                            $text = $items[$index].FindFirst([Windows.Automation.TreeScope]::Descendants, $textCondition)
                            if ($null -eq $text -or $text.Current.Name -cne $names[$index]) {
                                throw "Actual Settings Prompt order mismatch at index $index."
                            }
                            ([Windows.Automation.SelectionItemPattern]$items[$index].GetCurrentPattern(
                                [Windows.Automation.SelectionItemPattern]::Pattern)).Select()
                            Assert-GuiStillLive
                            $preview = Find-Control 'ImportedPromptPreview' $promptSettings
                            if ((Read-Value $preview) -cne $contents[$index]) {
                                throw "Actual Settings Prompt preview mismatch at index $index (content not logged)."
                            }
                        }
                        $back = Find-Control 'SettingsRequestClose' $settings
                        if ($back.Current.Name -cne '設定から元のステップへ戻る' -or -not $back.Current.IsEnabled) {
                            throw 'Settings did not expose the labelled return-to-step button.'
                        }
                        ([Windows.Automation.InvokePattern]$back.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
                        Assert-GuiStillLive
                        [void](Find-Control 'QuantificationDesignView')
                        if ((Find-Control 'WorkflowStepDesign').Current.Name -cne $designStepName) {
                            throw 'Closing Settings did not preserve the original Design step.'
                        }
                    }
                    Assert-NoStartupChildProcesses -ProcessId {{app.Process.Id}}
                    [Console]::WriteLine('P06_PROBE_OK')
                }
                finally { $appProcess.Dispose() }
                """;
            await RunPowerShellAsync(command);
        }

        internal async Task CloseAsync(AppInstance app)
        {
            Assert.False(app.Process.HasExited, $"{app.Scenario}: application exited before an explicit close.");
            Assert.True(app.Process.CloseMainWindow(), $"{app.Scenario}: main window could not be closed.");
            Assert.True(await WaitForExitAsync(app.Process, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken),
                $"{app.Scenario}: graceful close timed out.");
            Assert.Equal(0, app.Process.ExitCode);
            WaitForOwnedProcessesToExit();
            AssertDataUnchanged();
        }

        private void WaitForOwnedProcessesToExit()
        {
            // The startup status-check CLI can outlive the app briefly while holding inherited run handles.
            string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root)) + Path.DirectorySeparatorChar;
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (true)
            {
                bool running = false;
                foreach (Process candidate in Process.GetProcessesByName("copilot"))
                {
                    using (candidate)
                    {
                        try
                        {
                            running |= candidate.MainModule?.FileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true;
                        }
                        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
                        {
                            // Exited or inaccessible processes are not owned by this run.
                        }
                    }
                }

                if (!running)
                {
                    return;
                }

                Assert.True(DateTimeOffset.UtcNow < deadline, "The bundled CLI started from this run did not exit after the application closed.");
                Thread.Sleep(250);
            }
        }

        internal async Task AssertHostFailureAsync(AppInstance app, string expectedHostMessage)
        {
            Assert.True(await WaitForExitAsync(app.Process, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken),
                $"{app.Scenario}: native host did not fail within the bounded timeout.");
            Assert.NotEqual(0, app.Process.ExitCode);
            Assert.True(File.Exists(app.TracePath), "A failed native-host launch must produce a standard host trace.");
            string error = await app.StandardError.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            string diagnostics = app.Trace + "\n" + error;
            // Do not echo raw traces/paths. A loader rejection or managed startup error is not this fault.
            Assert.True(diagnostics.Contains("Failure processing application bundle", StringComparison.Ordinal),
                $"{app.Scenario}: no native bundle failure diagnostic; exit=0x{app.Process.ExitCode:X8}.");
            Assert.True(diagnostics.Contains(expectedHostMessage, StringComparison.Ordinal),
                $"{app.Scenario}: expected native diagnostic '{expectedHostMessage}' was absent.");
            Assert.DoesNotContain("Property APP_CONTEXT_BASE_DIRECTORY = ", app.Trace, StringComparison.Ordinal);
            output.WriteLine($"P06_NATIVE_FAILURE scenario={app.Scenario} exit=0x{app.Process.ExitCode:X8} hostDiagnosticMatched=true");
            AssertDataUnchanged();
        }

        internal void DeleteOwnedCacheAndMoveExecutable()
        {
            Assert.All(applications, app => Assert.True(app.Process.HasExited));
            AssertInside(Root, CacheDirectory);
            RetryTransientIo(() => DeletePlainDirectory(CacheDirectory), () => Directory.Exists(CacheDirectory));
            string destination = Path.Combine(Root, "移動した package");
            Assert.False(Directory.Exists(destination));
            string source = PackageDirectory;
            RetryTransientIo(() => Directory.Move(source, destination), () => Directory.Exists(source) && !Directory.Exists(destination));
            PackageDirectory = destination;
        }

        private static void RetryTransientIo(Action operation, Func<bool> canRetry)
        {
            // Antivirus scans can briefly hold just-exited files; only an unchanged operation is retried.
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (true)
            {
                try
                {
                    operation();
                    return;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    && DateTimeOffset.UtcNow < deadline && canRetry())
                {
                    Thread.Sleep(250);
                }
            }
        }

        internal void TruncateOwnedExecutable()
        {
            Assert.Empty(applications);
            long peEnd;
            using (FileStream source = File.OpenRead(ExecutablePath))
            using (PEReader pe = new(source))
            {
                // Standard PE section bounds only, NOT a proprietary bundle/header parser.
                peEnd = pe.PEHeaders.SectionHeaders.Max(section => (long)section.PointerToRawData + section.SizeOfRawData);
            }

            long shortenedLength = Math.Max(peEnd, expectedExecutable.Length / 2);
            Assert.True(shortenedLength < expectedExecutable.Length, "No removable bundle tail outside the native PE sections.");
            using (FileStream stream = new(ExecutablePath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(shortenedLength);
                stream.Flush(flushToDisk: true);
            }

            expectedExecutable = FileSnapshot.Read(ExecutablePath); // Only this intentional local fault changes the expected copy.
            Assert.NotEqual(sourceSnapshot.Sha256, expectedExecutable.Sha256);
            AssertAmd64(ExecutablePath);
        }

        internal void AssertDataUnchanged()
        {
            Assert.Equal(sourceSnapshot, FileSnapshot.Read(sourcePath));
            Assert.Equal(sidecarSnapshot, FileSnapshot.Read(sourcePath + ".sha256"));
            Assert.Equal(ExecutablePath, Assert.Single(Directory.GetFileSystemEntries(PackageDirectory)));
            Assert.Equal(expectedExecutable, FileSnapshot.Read(ExecutablePath));
            Assert.Equal(dataEntries, RelativeEntries(DataDirectory));
            Assert.Equal(cwdEntries, RelativeEntries(WorkingDirectory));
            AssertFilesUnchanged(dataFiles, DataDirectory);
            AssertFilesUnchanged(cwdFiles, WorkingDirectory);
        }

        private void AssertWorkbookLocationsAfterExit()
        {
            // A peer can rename its extraction staging directory while Start is being called.
            // Inspect the complete run tree only after every owned process has stopped.
            string[] expectedWorkbooks = dataFiles.Keys.Where(IsWorkbook).Select(path => Path.Combine(DataDirectory, path))
                .Concat(cwdFiles.Keys.Where(IsWorkbook).Select(path => Path.Combine(WorkingDirectory, path)))
                .Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expectedWorkbooks, PlainEntries(Root).Where(File.Exists).Where(IsWorkbook).Order(StringComparer.Ordinal).ToArray());
        }

        public async ValueTask DisposeAsync()
        {
            List<Exception> failures = [];
            bool stopped = true;
            foreach (AppInstance app in applications.AsEnumerable().Reverse())
            {
                try { await StopOwnedProcessAsync(app.Process); }
                catch (Exception exception) { stopped = false; failures.Add(exception); }
                finally { app.Process.Dispose(); }
            }

            try { AssertDataUnchanged(); }
            catch (Exception exception) { failures.Add(exception); }
            if (stopped)
            {
                try { AssertWorkbookLocationsAfterExit(); }
                catch (Exception exception) { failures.Add(exception); }
                try { RetryTransientIo(() => DeletePlainDirectory(Root), () => Directory.Exists(Root)); }
                catch (Exception exception) { failures.Add(exception); }
            }

            if (failures.Count != 0)
            {
                throw new AggregateException("P06 owned-process cleanup or data-preservation checks failed.", failures);
            }
        }

        private ProcessStartInfo CreateStartInfo(string executable, bool createNoWindow)
        {
            ProcessStartInfo info = new()
            {
                FileName = executable,
                WorkingDirectory = WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = createNoWindow,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            SetIsolatedEnvironment(info);
            return info;
        }

        private void SetIsolatedEnvironment(ProcessStartInfo info)
        {
            info.Environment.Clear(); // No inherited credentials, startup hooks, profilers or CLI settings.
            foreach (string name in new[] { "SystemRoot", "WINDIR", "SystemDrive", "ComSpec" })
            {
                if (Environment.GetEnvironmentVariable(name) is { } value)
                {
                    info.Environment[name] = value;
                }
            }

            foreach (string name in new[] { "TEMP", "TMP", "USERPROFILE", "HOME", "LOCALAPPDATA", "APPDATA", "COPILOT_HOME" })
            {
                string owned = Path.Combine(Root, name);
                Directory.CreateDirectory(owned);
                info.Environment[name] = owned;
            }

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Assert.False(string.IsNullOrWhiteSpace(windows));
            info.Environment["PATH"] = Path.Combine(windows, "System32");
            info.Environment["DOTNET_ROOT"] = Path.Combine(Root, "absent-dotnet");
            info.Environment["DOTNET_ROOT_X64"] = Path.Combine(Root, "absent-dotnet-x64");
            info.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            info.Environment["DOTNET_DISABLE_GUI_ERRORS"] = "1";
            info.Environment["STUDY_REPORT_EVALUATOR_AUTO_COPILOT_LOGIN"] = "0";
            foreach (string name in new[] { "DOTNET_ROOT", "DOTNET_ROOT_X64" })
            {
                Assert.False(Directory.Exists(info.Environment[name]) || File.Exists(info.Environment[name]));
            }
        }

        private async Task RunPowerShellAsync(string body)
        {
            string command = $$"""
                $ErrorActionPreference = 'Stop'
                $ProgressPreference = 'SilentlyContinue'
                Set-StrictMode -Version Latest
                try {
                    if ($PSVersionTable.PSEdition -cne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell Core 7+ required.' }
                    if ([int][char]'·' -ne 183) { throw 'Observer script Unicode was changed in transit.' }
                    {{body}}
                }
                catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
                """;
            ProcessStartInfo info = CreateStartInfo(FindPowerShellCoreExecutable(), createNoWindow: true);
            // Keep the command line short; the observer-only script travels over a private stdin pipe.
            info.RedirectStandardInput = true;
            // VSTest can default to CP932, whose best-fit conversion changes U+00B7 to U+30FB.
            // Specify UTF-8 at both ends, independently of either process's console code page.
            info.StandardInputEncoding = new UTF8Encoding(false, true);
            foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                "& ([scriptblock]::Create([IO.StreamReader]::new([Console]::OpenStandardInput(), [Text.UTF8Encoding]::new($false, $true)).ReadToEnd()))" })
            {
                info.ArgumentList.Add(argument);
            }

            using Process observer = new() { StartInfo = info };
            bool started = false;
            try
            {
                started = observer.Start();
                Assert.True(started);
                Assert.Equal(Encoding.UTF8.CodePage, observer.StandardInput.Encoding.CodePage);
                Task<string> stdout = observer.StandardOutput.ReadToEndAsync();
                Task<string> stderr = observer.StandardError.ReadToEndAsync();
                await observer.StandardInput.WriteLineAsync(command).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                observer.StandardInput.Close();
                Assert.True(await WaitForExitAsync(observer, TimeSpan.FromSeconds(40), TestContext.Current.CancellationToken),
                    "P06 UIA/payload observer timed out; a window handle/host trace alone is not an Input-screen PASS.");
                string result = await stdout.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                string error = await stderr.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                error = error.Replace(Root, "<owned-run>", StringComparison.OrdinalIgnoreCase)
                    .Replace(RepositoryRoot, "<repository>", StringComparison.OrdinalIgnoreCase);
                Assert.True(observer.ExitCode == 0, $"P06 read-only UIA/payload observer failed. {error}");
                Assert.Equal("P06_PROBE_OK", result.Trim());
            }
            finally
            {
                if (started) { await StopOwnedProcessAsync(observer); }
            }
        }
    }

    private sealed record AppInstance(Process Process, string Scenario, string TracePath, Stopwatch Watch,
        Task<string> StandardOutput, Task<string> StandardError)
    {
        internal bool StartupObserved { get; set; }
        internal string Trace
        {
            get
            {
                using FileStream stream = new(TracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader reader = new(stream);
                return reader.ReadToEnd();
            }
        }
    }

    private sealed record FileSnapshot(long Length, DateTime LastWriteTimeUtc, string Sha256)
    {
        internal static FileSnapshot Read(string path)
        {
            FileInfo file = new(path);
            Assert.True(file.Exists, $"Expected file is missing: {Path.GetFileName(path)}");
            Assert.Equal(0, (int)(file.Attributes & FileAttributes.ReparsePoint));
            using FileStream stream = File.OpenRead(path);
            return new(file.Length, file.LastWriteTimeUtc, Convert.ToHexString(SHA256.HashData(stream)));
        }
    }

    private static Dictionary<string, FileSnapshot> CaptureFiles(string root) => PlainEntries(root).Where(File.Exists)
        .ToDictionary(path => Path.GetRelativePath(root, path), FileSnapshot.Read, StringComparer.Ordinal);

    private static void AssertFilesUnchanged(Dictionary<string, FileSnapshot> expected, string root)
    {
        Dictionary<string, FileSnapshot> actual = CaptureFiles(root);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach ((string relative, FileSnapshot snapshot) in expected)
        {
            Assert.Equal(snapshot, actual[relative]);
        }
    }

    private static string[] RelativeEntries(string root) => PlainEntries(root)
        .Select(path => Path.GetRelativePath(root, path)).Order(StringComparer.Ordinal).ToArray();

    private static string[] PlainEntries(string root)
    {
        List<string> entries = [];
        Stack<string> directories = new();
        directories.Push(root);
        while (directories.TryPop(out string? directory))
        {
            Assert.Equal(0, (int)(File.GetAttributes(directory) & FileAttributes.ReparsePoint));
            foreach (string path in Directory.GetFileSystemEntries(directory))
            {
                Assert.Equal(0, (int)(File.GetAttributes(path) & FileAttributes.ReparsePoint));
                entries.Add(path);
                if (Directory.Exists(path)) { directories.Push(path); }
            }
        }

        return entries.ToArray();
    }

    private static void DeletePlainDirectory(string path)
    {
        _ = PlainEntries(path); // Refuse links before recursive deletion; callers supply only newly owned run/cache roots.
        Directory.Delete(path, recursive: true);
        Assert.False(Directory.Exists(path));
    }

    private static bool IsWorkbook(string path) => Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

    private static void AssertInside(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        Assert.False(Path.IsPathRooted(relative) || relative is "." or ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Path is outside the owned extraction directory.");
    }

    private static void AssertAmd64(string path)
    {
        using FileStream stream = File.OpenRead(path);
        Assert.True(stream.Length > 0, $"Empty PE file: {Path.GetFileName(path)}");
        using PEReader reader = new(stream);
        Assert.Equal(Machine.Amd64, reader.PEHeaders.CoffHeader.Machine);
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan duration, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration);
        try { await process.WaitForExitAsync(timeout.Token); return true; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    private static async Task StopOwnedProcessAsync(Process process)
    {
        if (process.HasExited) { return; }
        if (process.CloseMainWindow() && await WaitForExitAsync(process, TimeSpan.FromSeconds(2), CancellationToken.None)) { return; }
        if (!process.HasExited) { process.Kill(); } // This exact owned handle only; never by name or entireProcessTree.
        Assert.True(await WaitForExitAsync(process, TimeSpan.FromSeconds(10), CancellationToken.None), "Owned P06 process did not stop.");
    }

    private static string FindPowerShellCoreExecutable()
    {
        HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.Add(Path.Combine(directory.Trim('"'), "pwsh.exe"));
        }

        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell");
        if (Directory.Exists(installed))
        {
            foreach (string directory in Directory.EnumerateDirectories(installed)) { candidates.Add(Path.Combine(directory, "pwsh.exe")); }
        }

        return candidates.Where(File.Exists).Select(path => (Path: path, Info: FileVersionInfo.GetVersionInfo(path)))
            .Where(candidate => candidate.Info.FileMajorPart >= 7)
            .OrderByDescending(candidate => new Version(candidate.Info.FileMajorPart, candidate.Info.FileMinorPart,
                candidate.Info.FileBuildPart, candidate.Info.FilePrivatePart))
            .Select(candidate => Path.GetFullPath(candidate.Path)).FirstOrDefault()
            ?? throw new FileNotFoundException("P06 observer requires installed PowerShell Core 7+; no Windows PowerShell fallback.");
    }

    private static string FindWindowsDesktopRuntime()
    {
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
        };
        string? runtimeRoot = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent?.Parent?.Parent?.FullName;
        if (runtimeRoot is not null) { roots.Add(runtimeRoot); }
        foreach (string name in new[] { "DOTNET_ROOT_X64", "DOTNET_ROOT" })
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } root) { roots.Add(root); }
        }

        return roots.Select(root => Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App"))
            .Where(Directory.Exists).SelectMany(root => Directory.EnumerateDirectories(root))
            .Select(path => (Path: path, Version: Version.TryParse(Path.GetFileName(path), out Version? version) ? version : null))
            .Where(candidate => candidate.Version is { Major: 10, Minor: 0 }
                && File.Exists(Path.Combine(candidate.Path, "UIAutomationClient.dll"))
                && File.Exists(Path.Combine(candidate.Path, "UIAutomationTypes.dll")))
            .OrderByDescending(candidate => candidate.Version).Select(candidate => candidate.Path).FirstOrDefault()
            ?? throw new DirectoryNotFoundException("P06 UIA observer requires an installed WindowsDesktop 10.0.x runtime; GUI validation cannot be silently omitted.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "StudyReportEvaluator.slnx"))) { return current.FullName; }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("The repository containing the parent-built P06 artifact was not found.");
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static void CreateSyntheticWorkbook(string path)
    {
        // Minimal standard OOXML, identical structure to the approved S01 probe; never opens sample/user workbooks.
        Dictionary<string, string> parts = new(StringComparer.Ordinal)
        {
            ["[Content_Types].xml"] = """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""",
            ["_rels/.rels"] = """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""",
            ["xl/workbook.xml"] = """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Synthetic" sheetId="1" r:id="r1"/></sheets></workbook>""",
            ["xl/_rels/workbook.xml.rels"] = """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""",
            ["xl/worksheets/sheet1.xml"] = """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><dimension ref="A1:A2"/><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Synthetic question</t></is></c></row><row r="2"><c r="A2" t="inlineStr"><is><t>Synthetic answer</t></is></c></row></sheetData></worksheet>""",
        };
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string name, string content) in parts)
        {
            using StreamWriter writer = new(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
    }
}