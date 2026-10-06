using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Packaging;

// P02 contracts: parse the script, then import only named function definitions from its AST.
// Never dot-source/invoke the full publisher, restore/build, acquire CLI, or launch the app.
// Requirements: FR-055 (AC-056)
[Collection<WindowsPublishPackageCollection>]
public sealed class WindowsSingleFilePublishTests
{
    private static readonly string[] LockFunctions =
    [
        "Assert-NotReparsePoint",
        "Get-Sha256Hex",
        "Assert-JsonEquivalent",
        "Assert-RidLockMatchesCanonicalLock",
        "Assert-SingleFileRidLockMatchesDedicatedLock",
        "Assert-SingleFileLockHashes",
    ];

    private static readonly string[] LayoutFunctions =
    [
        "Assert-PathWithinRoot",
        "Assert-NotReparsePoint",
        "Assert-OwnedDirectoryPath",
        "Assert-Amd64PortableExecutable",
        "Assert-SingleFilePublishLayout",
        "Get-SingleFileDocumentationPaths",
        "Assert-SafePublishLayout",
        "Remove-PublishSymbols",
        "Get-SingleFileExtractionDirectory",
    ];

    // Header-only fixture for the existing PE-machine validator. It is NEVER executed.
    private const string MinimalExeSetup = """
        $publish = Join-Path $TestRoot 'publish'
        [void][IO.Directory]::CreateDirectory($publish)
        $exe = Join-Path $publish "$ApplicationName.exe"
        $bytes = [byte[]]::new(128)
        $bytes[0] = 0x4D; $bytes[1] = 0x5A
        [BitConverter]::GetBytes([int]64).CopyTo($bytes, 0x3C)
        $bytes[64] = 0x50; $bytes[65] = 0x45
        $bytes[68] = 0x64; $bytes[69] = 0x86
        [IO.File]::WriteAllBytes($exe, $bytes)
        """;

    private const string LockSetup = """
        $canonicalPath = Join-Path $RepositoryRoot 'src/StudyReportEvaluator.App/packages.lock.json'
        $dedicatedPath = Join-Path $RepositoryRoot 'src/StudyReportEvaluator.App/packages.win-x64-singlefile.lock.json'
        $actualPath = Join-Path $TestRoot 'rid.lock.json'
        $dedicated = Get-Content -LiteralPath $dedicatedPath -Raw | ConvertFrom-Json -AsHashtable
        """;

    private const string CheckSingleFileLock = """
        Assert-SingleFileRidLockMatchesDedicatedLock -CanonicalLockPath $canonicalPath -DedicatedLockPath $dedicatedPath -RidLockPath $actualPath
        """;

    // Isolate layout/runtime/deps rules from the separately tested real CLI integrity validator.
    private const string ExtractedLayoutSetup = """
        $publish = Join-Path $TestRoot 'extracted'
        [void][IO.Directory]::CreateDirectory($publish)
        $required = @(
            "$ApplicationName.dll", 'StudyReportEvaluator.Core.dll',
            'DocumentFormat.OpenXml.dll', 'DocumentFormat.OpenXml.Framework.dll',
            'GitHub.Copilot.SDK.dll', 'Avalonia.dll', 'Avalonia.Win32.dll',
            'System.Private.CoreLib.dll', 'runtimes/win-x64/native/copilot.exe'
        ) + @(Get-SingleFileDocumentationPaths)
        foreach ($relative in $required) {
            $path = Join-Path $publish $relative
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
            [IO.File]::WriteAllBytes($path, [byte[]]@(1))
        }
        $configPath = Join-Path $publish "$ApplicationName.runtimeconfig.json"
        $config = @{ runtimeOptions = @{ tfm = 'net10.0'; includedFrameworks = @(
            @{ name = 'Microsoft.NETCore.App'; version = '10.0.11' }
        ) } }
        $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath
        [IO.File]::WriteAllText((Join-Path $publish 'copilot-runtime.json'), '{"sdkVersion":"1.0.11"}')
        $depsPath = Join-Path $publish "$ApplicationName.deps.json"
        $deps = @{ runtimeTarget = @{ name = '.NETCoreApp,Version=v10.0/win-x64' }; libraries = [ordered]@{
            'StudyReportEvaluator.App/7.8.9' = @{}; 'StudyReportEvaluator.Core/7.8.9' = @{}
            'DocumentFormat.OpenXml/3.5.1' = @{}; 'GitHub.Copilot.SDK/1.0.11' = @{}
            'Avalonia/12.1.1' = @{}; 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.11' = @{}
        } }
        $deps | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $depsPath
        $script:cliLayoutChecked = $false
        function Assert-BundledCopilotRuntime { param([string] $PublishDirectory) $script:cliLayoutChecked = $true }
        """;

    [Fact]
    public async Task Single_file_publish_script_parses_and_preserves_the_default_folder_flow()
    {
        string source = File.ReadAllText(ScriptPath);
        foreach (string contract in new[]
        {
            "#Requires -Version 7.0", "#Requires -PSEdition Core",
            "--property:PublishSingleFile=false", "--property:PublishTrimmed=false",
            "--property:PublishReadyToRun=false", "--property:SelfContained=true",
            "--property:UseAppHost=true", "--lock-file-path", "--no-restore",
            "Published self-contained unsigned folder: $finalPublishDirectory",
            "Canonical package lock changed during publish:",
            "$publishVariant = if ($SingleFile) { \"$RuntimeIdentifier-singlefile\" } else { $RuntimeIdentifier }",
            "$lockScope = if ($SingleFile) { 'P02' } else { 'P01' }",
            "$ridRestoreArguments += $publishModeArguments",
            "$publishArguments += $publishModeArguments",
            "Get-NpmCopilotCliBinary", "--property:CopilotCliBinaryPath=$copilotCliPath",
            "Assert-SafePublishLayout -PublishDirectory $temporaryPublishDirectory",
        })
        {
            Assert.Contains(contract, source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("--property:PublishSingleFile=true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("--property:PublishProfile=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("powershell.exe", source, StringComparison.OrdinalIgnoreCase);

        await AssertFunctionsSucceedAsync("""
            if ($ast.ParamBlock.Parameters.Count -ne 1 -or
                $ast.ParamBlock.Parameters[0].Name.VariablePath.UserPath -cne 'SingleFile' -or
                $ast.ParamBlock.Parameters[0].StaticType -ne [switch]) { throw 'Unexpected public parameters.' }
            $calls = @($ast.FindAll({ param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and
                $node.GetCommandName() -ceq 'Invoke-DotNet'
            }, $true))
            if ($calls.Count -ne 3 -or
                -not $calls[0].Extent.Text.Contains('--locked-mode') -or
                $calls[0].Extent.Text.Contains('Profile') -or
                $calls[0].Extent.Text.Contains('SingleFile') -or
                -not $calls[1].Extent.Text.Contains('$ridRestoreArguments') -or
                -not $calls[2].Extent.Text.Contains('$publishArguments')) { throw 'Restore/publish sequencing changed.' }
            $main = (@($ast.EndBlock.Statements | Where-Object {
                $_ -isnot [System.Management.Automation.Language.FunctionDefinitionAst]
            } | ForEach-Object { $_.Extent.Text }) -join "`n")
            if ($main.IndexOf('Remove-PublishSymbols -PublishDirectory') -ge
                $main.IndexOf('Assert-SingleFileApplicationLaunch -RepositoryRoot') -or
                $main.IndexOf('Assert-SingleFileApplicationLaunch -RepositoryRoot') -ge
                $main.IndexOf('[System.IO.Directory]::Move(')) { throw 'Validation must precede output replacement.' }
            """);
    }

    [Fact]
    public async Task Publish_mode_uses_the_full_app_profile_only_when_opted_in()
    {
        await AssertFunctionsSucceedAsync("""
            $profile = Join-Path $RepositoryRoot 'src/StudyReportEvaluator.App/Properties/PublishProfiles/WindowsSingleFile.pubxml'
            $folder = @(Get-PublishModeArguments -PublishProfileFullPath 'missing.pubxml')
            if ($folder.Count -ne 1 -or $folder[0] -cne '--property:PublishSingleFile=false') { throw 'Default mode changed.' }
            $single = @(Get-PublishModeArguments -SingleFile -PublishProfileFullPath $profile)
            if ($single.Count -ne 1 -or $single[0] -cne "--property:PublishProfileFullPath=$profile") { throw 'App-only profile not selected.' }
            """, "Assert-NotReparsePoint", "Get-PublishModeArguments");
    }

    [Fact]
    public void Single_file_profile_pins_runtime_and_ILLink_only_for_the_App()
    {
        string path = Path.Combine(FindRepositoryRoot(), "src", "StudyReportEvaluator.App",
            "Properties", "PublishProfiles", "WindowsSingleFile.pubxml");
        XDocument profile = XDocument.Load(path);
        const string appCondition = "'$(MSBuildProjectName)' == 'StudyReportEvaluator.App'";
        XElement runtime = Assert.Single(profile.Descendants("RuntimeFrameworkVersion"));
        Assert.Equal("10.0.11", runtime.Value);
        Assert.Equal(appCondition, (string?)runtime.Parent!.Attribute("Condition"));
        XElement pack = Assert.Single(profile.Descendants("KnownILLinkPack"));
        Assert.Equal("Microsoft.NET.ILLink.Tasks", (string?)pack.Attribute("Update"));
        Assert.Equal(appCondition, (string?)pack.Parent!.Attribute("Condition"));
        Assert.Null(pack.Attribute("Condition")); // Item-level metadata conditions fail MSBuild evaluation.
        XElement version = Assert.Single(pack.Elements("ILLinkPackVersion"));
        Assert.Equal("10.0.11", version.Value);
        Assert.Equal("'%(KnownILLinkPack.TargetFramework)' == 'net10.0'", (string?)version.Attribute("Condition"));
        Assert.Equal("false", Assert.Single(profile.Descendants("PublishTrimmed")).Value);
        Assert.Empty(profile.Descendants("PackageReference")); // Keep canonical App/Core locks untouched.
    }

    [Theory]
    [InlineData("relative.pubxml")]
    [InlineData("")]
    public async Task Single_file_mode_rejects_missing_or_nonabsolute_profiles(string relativePath)
    {
        string expression = relativePath.Length == 0 ? "(Join-Path $TestRoot 'missing.pubxml')" : Quote(relativePath);
        await AssertFunctionsFailAsync(
            $"Get-PublishModeArguments -SingleFile -PublishProfileFullPath {expression}",
            "requires the full path", "Assert-NotReparsePoint", "Get-PublishModeArguments");
    }

    [Fact]
    public async Task Single_file_RID_lock_accepts_exact_dedicated_graph_without_mutating_inputs()
    {
        await AssertFunctionsSucceedAsync(LockSetup + "\n" + """
            $hashes = @{}
            foreach ($path in @($canonicalPath, $dedicatedPath)) { $hashes[$path] = Get-Sha256Hex -Path $path }
            # Whitespace is immaterial; JSON types, fields, values and package sets are not.
            $dedicated | ConvertTo-Json -Depth 100 -Compress | Set-Content -LiteralPath $actualPath
            """ + "\n" + CheckSingleFileLock + "\nAssert-SingleFileLockHashes -Hashes $hashes", LockFunctions);
    }

    [Theory]
    [InlineData("dedicatedPath", "Dedicated App single-file package lock is missing")]
    [InlineData("actualPath", "App single-file RID lock was not generated")]
    public async Task Single_file_RID_lock_rejects_missing_required_locks(string variable, string message)
    {
        await AssertFunctionsFailAsync(
            LockSetup + $"\n${variable} = Join-Path $TestRoot 'missing.lock.json'\n" + CheckSingleFileLock,
            message, LockFunctions);
    }

    [Theory]
    [InlineData("$dedicated['version'] = '2'")]
    [InlineData("$dedicated['dependencies']['net10.0']['Avalonia']['resolved'] = '99.0.0'")]
    [InlineData("$dedicated['dependencies']['net10.0']['Avalonia']['contentHash'] = 'changed'")]
    [InlineData("$dedicated['dependencies']['net10.0']['Avalonia']['requested'] = '[1.0.0, )'")]
    [InlineData("$dedicated['dependencies']['net10.0']['Avalonia']['dependencies']['MicroCom.Runtime'] = '99.0.0'")]
    [InlineData("$dedicated['dependencies']['net10.0']['Avalonia']['type'] = @('Direct')")]
    [InlineData("$dedicated['dependencies']['net10.0']['Unexpected.Package'] = @{}")]
    [InlineData("[void]$dedicated['dependencies']['net10.0'].Remove('Avalonia')")]
    [InlineData("[void]$dedicated['dependencies']['net10.0/win-x64'].Remove('Avalonia.Native')")]
    [InlineData("$dedicated['dependencies']['net10.0/linux-x64'] = @{}")]
    public async Task Single_file_RID_lock_rejects_generated_graph_field_set_and_type_drift(string mutation)
    {
        await AssertFunctionsFailAsync(
            LockSetup + "\n" + mutation + "\n" +
            "$dedicated | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $actualPath\n" + CheckSingleFileLock,
            "Lock", LockFunctions);
    }

    [Theory]
    [InlineData("$dedicated['dependencies']['net10.0']['Microsoft.NET.ILLink.Tasks']['contentHash'] = 'changed'", "Lock value")]
    [InlineData("$dedicated['dependencies']['net10.0']['Microsoft.NET.ILLink.Tasks']['resolved'] = '10.0.12'", "Lock value")]
    [InlineData("$dedicated['dependencies']['net10.0']['Microsoft.NET.ILLink.Tasks']['requested'] = '[10.0.0, )'", "Lock value")]
    [InlineData("$dedicated['dependencies']['net10.0']['Unexpected.Package'] = @{}", "Lock object")]
    [InlineData("[void]$dedicated['dependencies']['net10.0'].Remove('Avalonia')", "Lock object")]
    [InlineData("$dedicated['dependencies']['net10.0']['Avalonia']['dependencies']['MicroCom.Runtime'] = '99.0.0'", "Lock value")]
    [InlineData("$dedicated['dependencies']['net10.0/win-x64']['Avalonia.Native']['dependencies']['Avalonia'] = '99.0.0'", "Lock value")]
    [InlineData("$dedicated['dependencies']['net10.0/win-x64']['Microsoft.NET.ILLink.Tasks'] = @{}", "outside the canonical lock")]
    public async Task Single_file_RID_lock_rejects_joint_dedicated_and_generated_drift(string mutation, string message)
    {
        await AssertFunctionsFailAsync(LockSetup + "\n" + mutation + "\n" + """
            $dedicatedPath = Join-Path $TestRoot 'dedicated.lock.json'
            $json = $dedicated | ConvertTo-Json -Depth 100
            $json | Set-Content -LiteralPath $dedicatedPath
            $json | Set-Content -LiteralPath $actualPath
            """ + "\n" + CheckSingleFileLock, message, LockFunctions);
    }

    [Theory]
    [InlineData("canonical", false)]
    [InlineData("canonical", true)]
    [InlineData("dedicated", false)]
    [InlineData("dedicated", true)]
    public async Task Protected_single_file_locks_reject_byte_drift_or_deletion(string name, bool delete)
    {
        await AssertFunctionsFailAsync("""
            $hashes = @{}
            foreach ($name in @('canonical', 'dedicated')) {
                $path = Join-Path $TestRoot "$name.lock.json"
                [IO.File]::WriteAllText($path, '{}')
                $hashes[$path] = Get-Sha256Hex -Path $path
            }
            Assert-SingleFileLockHashes -Hashes $hashes
            """ + $"\n$path = Join-Path $TestRoot '{name}.lock.json'\n" +
            (delete ? "[IO.File]::Delete($path)\n" : "[IO.File]::AppendAllText($path, ' ')\n") +
            "Assert-SingleFileLockHashes -Hashes $hashes", "changed a protected package lock", LockFunctions);
    }

    [Fact]
    public async Task Core_RID_validation_stays_canonical_and_rejects_ILLink()
    {
        await AssertFunctionsSucceedAsync("""
            $core = Join-Path $RepositoryRoot 'src/StudyReportEvaluator.Core/packages.lock.json'
            $rid = Join-Path $TestRoot 'core-rid.lock.json'
            $graph = @{ version = 2; dependencies = @{ 'net10.0' = @{}; 'net10.0/win-x64' = @{} } }
            $graph | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $rid
            Assert-RidLockMatchesCanonicalLock -CanonicalLockPath $core -RidLockPath $rid -ProjectName 'StudyReportEvaluator.Core'
            $graph.dependencies['net10.0']['Microsoft.NET.ILLink.Tasks'] = @{ type = 'Direct'; resolved = '10.0.11' }
            $graph | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $rid
            $rejected = $false
            try { Assert-RidLockMatchesCanonicalLock -CanonicalLockPath $core -RidLockPath $rid -ProjectName 'StudyReportEvaluator.Core' }
            catch { $rejected = $_.Exception.Message.Contains('Lock object comparison failed') }
            if (-not $rejected) { throw 'Core accepted the App-only build dependency.' }
            if (Test-Path -LiteralPath (Join-Path (Split-Path $core -Parent) 'packages.win-x64-singlefile.lock.json')) {
                throw 'Core must not acquire a dedicated single-file lock.'
            }
            """, LockFunctions);
    }

    [Fact]
    public async Task Single_file_layout_accepts_only_a_nonempty_amd64_exe_after_symbol_removal()
    {
        await AssertFunctionsSucceedAsync(MinimalExeSetup + "\n" + """
            [IO.File]::WriteAllBytes((Join-Path $publish 'native.pdb'), [byte[]]@(1))
            Remove-PublishSymbols -PublishDirectory $publish
            Assert-SingleFilePublishLayout -PublishDirectory $publish
            """, LayoutFunctions);
    }

    [Theory]
    [InlineData("[IO.File]::Delete($exe)", "only the application EXE")]
    [InlineData("[IO.File]::WriteAllBytes($exe, [byte[]]@())", "must not be empty")]
    [InlineData("[IO.File]::WriteAllText((Join-Path $publish 'copilot-runtime.json'), '{}')", "only the application EXE")]
    [InlineData("[void][IO.Directory]::CreateDirectory((Join-Path $publish 'extra'))", "only the application EXE")]
    [InlineData("[IO.File]::Move($exe, (Join-Path $publish 'other.exe'))", "only the application EXE")]
    [InlineData("$publish = $exe", "must be a directory")]
    [InlineData("$bytes[68] = 0x4C; $bytes[69] = 0x01; [IO.File]::WriteAllBytes($exe, $bytes)", "not AMD64")]
    public async Task Single_file_layout_rejects_missing_empty_extra_or_wrong_architecture_entries(string mutation, string message)
    {
        await AssertFunctionsFailAsync(MinimalExeSetup + "\n" + mutation +
            "\nAssert-SingleFilePublishLayout -PublishDirectory $publish", message, LayoutFunctions);
    }

    [Fact]
    public async Task Extracted_bundle_layout_reuses_integrity_checks_without_requiring_static_host_files()
    {
        await AssertFunctionsSucceedAsync(ExtractedLayoutSetup + "\n" + """
            if (@(Get-SingleFileDocumentationPaths).Count -ne 25) { throw 'The extracted fixture must contain all 25 public documentation files.' }
            Assert-SafePublishLayout -PublishDirectory $publish -ExtractedBundle
            if (-not $script:cliLayoutChecked) { throw 'Bundled CLI validation was bypassed.' }
            foreach ($absent in @("$ApplicationName.exe", 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
                if (Test-Path -LiteralPath (Join-Path $publish $absent)) { throw 'Fixture unexpectedly has a folder-only host component.' }
            }
            $rejected = $false
            try { Assert-SafePublishLayout -PublishDirectory $publish }
            catch { $rejected = $_.Exception.Message.Contains('Required self-contained publish file is missing') }
            if (-not $rejected) { throw 'Default folder validation was weakened.' }
            """, LayoutFunctions);
    }

    [Theory]
    [InlineData("copilot-runtime.json")]
    [InlineData("runtimes/win-x64/native/copilot.exe")]
    [InlineData("GitHub.Copilot.SDK.dll")]
    [InlineData("StudyReportEvaluator.App.dll")]
    [InlineData("StudyReportEvaluator.Core.dll")]
    [InlineData("System.Private.CoreLib.dll")]
    [InlineData("docs/features.md")]
    [InlineData("docs/settings.md")]
    [InlineData("docs/third-party-notices.md")]
    [InlineData("docs/result-excel-description.md")]
    [InlineData("docs/technical-guid.md")]
    [InlineData("images/architecture-overview.svg")]
    [InlineData("images/technical-architecture.svg")]
    [InlineData("images/evaluation-message-flow.svg")]
    [InlineData("images/08-settings.png")]
    public async Task Extracted_bundle_layout_rejects_missing_required_content(string relativePath)
    {
        await AssertFunctionsSucceedAsync(ExtractedLayoutSetup +
            $"\n$missing = {Quote(relativePath)}\n" + """
            # A fixture/setup failure must not count as rejection of the missing entry.
            Assert-SafePublishLayout -PublishDirectory $publish -ExtractedBundle
            $missingPath = Join-Path $publish $missing
            if (-not (Test-Path -LiteralPath $missingPath -PathType Leaf)) { throw 'The missing-content fixture was not created.' }
            [IO.File]::Delete($missingPath)
            $rejected = $false
            try { Assert-SafePublishLayout -PublishDirectory $publish -ExtractedBundle }
            catch {
                $rejected = $_.Exception.Message.Replace('\', '/') -ceq "Required self-contained publish file is missing or empty: $missing"
            }
            if (-not $rejected) { throw "Missing required content was not rejected for the expected path: $missing" }
            """, LayoutFunctions);
    }

    [Theory]
    [InlineData("unexpected.cmd", "Unexpected file")]
    [InlineData("unexpected.sh", "Unexpected file")]
    [InlineData("unexpected.py", "Unexpected file")]
    [InlineData("unexpected.json", "Unexpected file")]
    [InlineData("source.cs", "Forbidden source")]
    [InlineData("input.xlsx", "Forbidden source")]
    [InlineData("unexpected.pdb", "Forbidden source")]
    [InlineData(".env", "Forbidden source")]
    [InlineData("setting.txt", "Forbidden source")]
    [InlineData("SETTING.TXT", "Forbidden source")]
    [InlineData("docs/setting.txt", "Forbidden source")]
    public async Task Extracted_bundle_layout_rejects_unexpected_scripts_sources_and_content(string name, string message)
    {
        await AssertFunctionsSucceedAsync(ExtractedLayoutSetup +
            $"\n$unexpected = {Quote(name)}\n$expectedPrefix = {Quote(message)}\n" + """
            Assert-SafePublishLayout -PublishDirectory $publish -ExtractedBundle
            [IO.File]::WriteAllText((Join-Path $publish $unexpected), 'synthetic')
            $rejected = $false
            try { Assert-SafePublishLayout -PublishDirectory $publish -ExtractedBundle }
            catch {
                $actualMessage = $_.Exception.Message.Replace('\', '/')
                $rejected = $actualMessage.StartsWith($expectedPrefix, [System.StringComparison]::Ordinal) -and
                    $actualMessage.EndsWith(": $unexpected", [System.StringComparison]::Ordinal)
            }
            if (-not $rejected) { throw "Unexpected content was not rejected for the expected reason and path: $unexpected" }
            """, LayoutFunctions);
    }

    [Theory]
    [InlineData("[IO.File]::WriteAllBytes((Join-Path $publish 'README.md'), [byte[]]@())", "Zero-byte")]
    [InlineData("$config.runtimeOptions.framework = @{ name = 'Microsoft.NETCore.App'; version = '10.0.11' }", "must not declare a framework")]
    [InlineData("$config.runtimeOptions.includedFrameworks[0].version = '10.0.12'", "pinned Microsoft.NETCore.App")]
    [InlineData("$deps.runtimeTarget.name = '.NETCoreApp,Version=v10.0/linux-x64'", "runtime target is not win-x64")]
    [InlineData("[void]$deps.libraries.Remove('GitHub.Copilot.SDK/1.0.11'); $deps.libraries['GitHub.Copilot.SDK/99.0.0'] = @{}", "identity does not match")]
    [InlineData("$deps.libraries['GitHub.Copilot.SDK/99.0.0'] = @{}", "ambiguous library identities")]
    [InlineData("$deps.libraries['xunit/3.0.0'] = @{}", "Forbidden test or Office")]
    public async Task Extracted_bundle_layout_rejects_zero_bytes_and_runtime_or_dependency_drift(string mutation, string message)
    {
        await AssertFunctionsFailAsync(ExtractedLayoutSetup + "\n" + mutation + "\n" + """
            $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath
            $deps | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $depsPath
            Assert-SafePublishLayout -PublishDirectory $publish -ExtractedBundle
            """, message, LayoutFunctions);
    }

    [Fact]
    public async Task Extracted_CLI_validator_rejects_manifest_hash_tampering_without_execution()
    {
        await AssertFunctionsFailAsync("""
            $cli = Join-Path $TestRoot 'runtimes/win-x64/native/copilot.exe'
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($cli))
            [IO.File]::WriteAllBytes($cli, [byte[]]@(1))
            $manifest = [ordered]@{
                schemaVersion = 1; runtimeIdentifier = 'win-x64'; cliVersion = '1.0.79'
                cliSha256 = ('0' * 64); sdkVersion = '1.0.11'; cliRelativePath = 'runtimes/win-x64/native/copilot.exe'
            }
            $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $TestRoot 'copilot-runtime.json')
            Assert-BundledCopilotRuntime -PublishDirectory $TestRoot
            """, "does not match its manifest hash", "Assert-NotReparsePoint", "Get-Sha256Hex", "Assert-BundledCopilotRuntime");
    }

    [Fact]
    public async Task Single_file_documentation_allowlist_matches_the_app_profile()
    {
        // Independent contract: neither the profile nor the script supplies the expected paths.
        string[] expected =
        [
            "README.md",
            "LICENSE",
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
        Assert.Equal(25, expected.Length);
        expected = expected.Order(StringComparer.Ordinal).ToArray();
        string profilePath = Path.Combine(FindRepositoryRoot(), "src", "StudyReportEvaluator.App", "Properties", "PublishProfiles", "WindowsSingleFile.pubxml");
        string[] profilePaths = XDocument.Load(profilePath).Descendants("Content")
            .Select(item => ((string)item.Attribute("Link")!).Replace('\\', '/'))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, profilePaths);

        ProcessResult result = await RunFunctionsAsync(
            "Get-SingleFileDocumentationPaths", "Get-SingleFileDocumentationPaths");
        AssertSucceeded(result);
        string[] actual = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(25, actual.Length);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owned_single_file_paths_reject_outside_roots_and_reparse_ancestors(bool reparse)
    {
        string body = reparse ? """
            $target = Join-Path $TestRoot 'target'
            [void][IO.Directory]::CreateDirectory($target)
            $link = Join-Path $TestRoot 'junction'
            [void](New-Item -ItemType Junction -Path $link -Target $target)
            try { Assert-OwnedDirectoryPath -Root $TestRoot -Path (Join-Path $link 'child') }
            finally { [IO.Directory]::Delete($link) }
            """ : "Assert-OwnedDirectoryPath -Root $TestRoot -Path ($TestRoot + '-outside')";
        await AssertFunctionsFailAsync(body, reparse ? "Reparse points" : "escapes the repository root", LayoutFunctions);
    }

    [Theory]
    [InlineData("valid", null)]
    [InlineData("missing", "host trace is missing")]
    [InlineData("duplicate", "exactly one absolute")]
    [InlineData("outside", "escapes the repository root")]
    [InlineData("wrong-app", "owned standard-host bundle directory")]
    [InlineData("two-bundles", "exactly one application and one bundle")]
    public async Task Single_file_trace_requires_one_app_base_inside_its_owned_cache(string scenario, string? error)
    {
        string body = """
            $cache = Join-Path $TestRoot 'TEMP/.net'
            $base = Join-Path $cache "$ApplicationName/bundle-id"
            [void][IO.Directory]::CreateDirectory($base)
            $trace = Join-Path $TestRoot 'host-trace.log'
            $line = "Property APP_CONTEXT_BASE_DIRECTORY = $base" + [IO.Path]::DirectorySeparatorChar
            """ + $"\n$scenario = {Quote(scenario)}\n" + """
            switch ($scenario) {
                'duplicate' { $line += "`r`n" + $line }
                'outside' { $line = "Property APP_CONTEXT_BASE_DIRECTORY = $cache-outside" }
                'wrong-app' { $line = "Property APP_CONTEXT_BASE_DIRECTORY = $cache/other/bundle-id" }
                'two-bundles' { [void][IO.Directory]::CreateDirectory((Join-Path $cache "$ApplicationName/second")) }
            }
            if ($scenario -cne 'missing') { [IO.File]::WriteAllText($trace, $line + "`r`n") }
            $actual = Get-SingleFileExtractionDirectory -ProbeDirectory $TestRoot
            if ($actual -cne $base) { throw 'Trace did not resolve the expected extracted directory.' }
            """;
        if (error is null)
        {
            await AssertFunctionsSucceedAsync(body, LayoutFunctions);
        }
        else
        {
            await AssertFunctionsFailAsync(body, error, LayoutFunctions);
        }
    }

    [Fact]
    public async Task Single_file_probe_environment_owns_cache_home_and_trace_without_inherited_secrets()
    {
        await AssertFunctionsSucceedAsync("""
            $info = [Diagnostics.ProcessStartInfo]::new()
            $info.Environment['GITHUB_TOKEN'] = 'synthetic-never-log'
            $info.Environment['DOTNET_STARTUP_HOOKS'] = 'synthetic-hook'
            Set-SingleFileProbeEnvironment -StartInfo $info -ProbeDirectory $TestRoot
            foreach ($name in @('GITHUB_TOKEN', 'DOTNET_STARTUP_HOOKS')) {
                if ($info.Environment.ContainsKey($name)) { throw 'Inherited environment was not cleared.' }
            }
            foreach ($name in @('TEMP', 'TMP', 'USERPROFILE', 'HOME', 'LOCALAPPDATA', 'APPDATA', 'COPILOT_HOME',
                    'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_BUNDLE_EXTRACT_BASE_DIR', 'DOTNET_HOST_TRACEFILE')) {
                Assert-PathWithinRoot -Root $TestRoot -Path $info.Environment[$name]
            }
            if ((Test-Path -LiteralPath $info.Environment['DOTNET_ROOT']) -or
                (Test-Path -LiteralPath $info.Environment['DOTNET_ROOT_X64'])) { throw 'The isolated .NET roots must be absent.' }
            if ($info.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] -cne (Join-Path $TestRoot 'TEMP/.net') -or
                $info.Environment['PATH'] -cne (Join-Path $env:SystemRoot 'System32') -or
                $info.Environment['DOTNET_HOST_TRACE'] -cne '1' -or
                $info.ArgumentList.Count -ne 0) { throw 'Unexpected launch environment or arguments.' }
            """, [.. LayoutFunctions, "Set-SingleFileProbeEnvironment"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Single_file_probe_cleans_owned_trace_and_cache_after_launch_or_extraction_failure(bool launchFails)
    {
        string failureFlag = launchFails ? "$true" : "$false";
        await AssertFunctionsSucceedAsync(MinimalExeSetup + $"\n$script:launchFails = {failureFlag}\n" + """
            function Get-PublishProductVersion { param($RepositoryRoot) return '7.8.9' }
            function Assert-PublishBinaryVersion { param($Path, $ExpectedVersion, [switch] $Managed) }
            function Assert-ApplicationLaunch {
                param($PublishDirectory, $SingleFileProbeDirectory)
                if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory "$ApplicationName.exe"))) {
                    throw 'Missing isolated EXE copy.'
                }
                $info = [Diagnostics.ProcessStartInfo]::new()
                Set-SingleFileProbeEnvironment -StartInfo $info -ProbeDirectory $SingleFileProbeDirectory
                [void][IO.Directory]::CreateDirectory($info.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'])
                [IO.File]::WriteAllText($info.Environment['DOTNET_HOST_TRACEFILE'], 'synthetic paths only')
                if ($script:launchFails) { throw 'synthetic launch failure' }
            }
            $hash = Get-Sha256Hex -Path $exe
            $probe = Join-Path $TestRoot 'owned-probe'
            $rejected = $false
            try { Assert-SingleFileApplicationLaunch -RepositoryRoot $TestRoot -PublishDirectory $publish -ProbeDirectory $probe }
            catch {
                $expected = if ($script:launchFails) { 'synthetic launch failure' } else { 'exactly one absolute APP_CONTEXT_BASE_DIRECTORY' }
                $rejected = $_.Exception.Message.Contains($expected)
            }
            if (-not $rejected -or (Test-Path -LiteralPath $probe)) { throw 'Failed probe did not clean its owned trace/cache.' }
            if ((Get-Sha256Hex -Path $exe) -cne $hash) { throw 'Publish input was changed by failed validation.' }
            """, [.. LayoutFunctions, "Get-Sha256Hex", "Set-SingleFileProbeEnvironment", "Wait-OwnedProbeProcessesExit", "Invoke-TransientFileOperation", "Assert-SingleFileApplicationLaunch"]);
    }

    [Fact]
    public async Task Single_file_probe_rejects_unexpected_startup_children_without_collecting_command_lines()
    {
        await AssertFunctionsFailAsync("""
            function Get-CimInstance {
                param($ClassName, $Filter, $Property, $OperationTimeoutSec, $ErrorAction)
                if ($ClassName -cne 'Win32_Process' -or $Filter -cne 'ParentProcessId = 123' -or
                    $Property -cne 'ProcessId' -or $OperationTimeoutSec -ne 5) { throw 'Unexpected process query.' }
                [pscustomobject]@{ ProcessId = 456 }
            }
            Assert-NoStartupChildProcesses -ProcessId 123
            """, "unexpectedly has child processes", "Assert-NoStartupChildProcesses");
    }

    [Fact]
    public async Task Single_file_product_version_comes_from_props_not_the_future_patch()
    {
        await AssertFunctionsSucceedAsync("""
            $props = Join-Path $TestRoot 'Directory.Build.props'
            foreach ($version in @('7.8.9', '7.8.10')) {
                [IO.File]::WriteAllText($props, "<Project><PropertyGroup><VersionPrefix>$version</VersionPrefix><VersionSuffix></VersionSuffix></PropertyGroup></Project>")
                if ((Get-PublishProductVersion -RepositoryRoot $TestRoot) -cne $version) { throw 'Product version is hard-coded.' }
            }
            """, "Get-PublishProductVersion");
    }

    [Fact]
    public async Task Managed_binary_versions_are_checked_from_metadata_without_loading_the_application()
    {
        string outputDirectory = Quote(AppContext.BaseDirectory);
        await AssertFunctionsSucceedAsync($"$assemblyDirectory = {outputDirectory}\n" + """
            $version = Get-PublishProductVersion -RepositoryRoot $RepositoryRoot
            foreach ($name in @('StudyReportEvaluator.App.dll', 'StudyReportEvaluator.Core.dll')) {
                $path = Join-Path $assemblyDirectory $name
                Assert-PublishBinaryVersion -Path $path -ExpectedVersion $version -Managed
                $rejected = $false
                try { Assert-PublishBinaryVersion -Path $path -ExpectedVersion '99.98.97' -Managed }
                catch { $rejected = $_.Exception.Message.Contains('canonical product version') }
                if (-not $rejected) { throw 'A different binary version was accepted.' }
            }
            $renamed = Join-Path $TestRoot 'DifferentAssembly.dll'
            [IO.File]::Copy((Join-Path $assemblyDirectory 'StudyReportEvaluator.App.dll'), $renamed)
            $rejected = $false
            try { Assert-PublishBinaryVersion -Path $renamed -ExpectedVersion $version -Managed }
            catch { $rejected = $_.Exception.Message.Contains('managed assembly identity') }
            if (-not $rejected) { throw 'A different managed assembly identity was accepted.' }
            """, "Get-PublishProductVersion", "Assert-PublishBinaryVersion");
    }

    private static async Task AssertFunctionsSucceedAsync(string body, params string[] functions) =>
        AssertSucceeded(await RunFunctionsAsync(body, functions));

    private static async Task AssertFunctionsFailAsync(string body, string message, params string[] functions)
    {
        ProcessResult result = await RunFunctionsAsync(body, functions);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(message, result.StandardError, StringComparison.Ordinal);
    }

    private static void AssertSucceeded(ProcessResult result) =>
        Assert.True(result.ExitCode == 0, $"Isolated PowerShell contract failed.\n{result.StandardOutput}\n{result.StandardError}");

    private static async Task<ProcessResult> RunFunctionsAsync(string body, params string[] functions)
    {
        Assert.True(OperatingSystem.IsWindows(), "These packaging function contracts require PowerShell Core on Windows.");
        string powerShellPath = FindPowerShellCoreExecutable();
        string repositoryRoot = FindRepositoryRoot();
        string testRoot = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-P02-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        string command = $$"""
            $ErrorActionPreference = 'Stop'
            Set-StrictMode -Version Latest
            try {
                if ($PSVersionTable.PSEdition -cne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell Core 7+ required.' }
                $RepositoryRoot = {{Quote(repositoryRoot)}}
                $TestRoot = {{Quote(testRoot)}}
                $TargetFramework = 'net10.0'
                $RuntimeIdentifier = 'win-x64'
                $ApplicationName = 'StudyReportEvaluator.App'
                $tokens = $null; $parseErrors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile({{Quote(ScriptPath)}}, [ref]$tokens, [ref]$parseErrors)
                if (@($parseErrors).Count -ne 0) { throw ('Publish script parse errors: ' + ($parseErrors.Message -join '; ')) }
                foreach ($name in @({{string.Join(",", functions.Select(Quote))}})) {
                    $definitions = @($ast.EndBlock.Statements | Where-Object {
                        $_ -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $_.Name -ceq $name
                    })
                    if ($definitions.Count -ne 1) { throw "Expected exactly one function definition: $name" }
                    . ([scriptblock]::Create($definitions[0].Extent.Text))
                }
                {{body}}
            }
            catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
            """;
        ProcessStartInfo startInfo = new()
        {
            FileName = powerShellPath,
            WorkingDirectory = testRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        bool started = false;
        try
        {
            started = process.Start();
            Assert.True(started);
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using CancellationTokenSource cleanupTimeout = new(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanupTimeout.Token);
            }

            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static string FindPowerShellCoreExecutable()
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate = Path.Combine(directory.Trim('"'), "pwsh.exe");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException("PowerShell Core 7+ executable pwsh.exe was not found on PATH.");
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string ScriptPath => Path.Combine(FindRepositoryRoot(), "scripts", "publish-windows.ps1");

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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}