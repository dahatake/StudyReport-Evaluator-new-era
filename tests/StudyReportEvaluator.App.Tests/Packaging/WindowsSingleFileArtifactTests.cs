using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Packaging;

// P05 only. Required tests import named PowerShell functions from the AST, not the
// publisher or script entry point. Synthetic PE bytes exercise rejection and I/O
// contracts, never bundle validity. Only the opt-in test uses the real P02 artifact.
// Requirements: FR-055 (AC-056)
[Collection<WindowsPublishPackageCollection>]
public sealed class WindowsSingleFileArtifactTests
{
    private const string ArtifactName = "StudyReportEvaluator-win-x64.exe";
    private const string HashName = ArtifactName + ".sha256";
    private const string OptInVariable = "RUN_WINDOWS_SINGLEFILE_ARTIFACT_TESTS";

    private static readonly string[] Functions =
    [
        "Assert-SupportedHost", "Resolve-PackageDirectory", "Test-PackagePathWithinRoot",
        "Assert-PlainDirectoryAncestors", "Get-PlainPackageFile", "Resolve-SingleFilePackagePaths",
        "Get-SingleFilePackageInput", "Assert-Amd64PortableExecutable", "Get-SingleFilePackageVersion",
        "Assert-SingleFileBinaryVersion", "Get-Sha256Hex", "Assert-ExactSingleFileSidecar",
        "Get-ExistingSingleFilePackageHash", "Write-SingleFileArtifactPair", "Invoke-SingleFilePackaging",
    ];

    // Not a compiled application and NEVER executed. No version resources or bundle.
    private const string Fixture = """
        $fixtureRepo = Join-Path $TestRoot 'repository'
        $publish = [IO.Path]::GetFullPath((Join-Path $fixtureRepo 'artifacts/package/publish/win-x64-singlefile'))
        $output = [IO.Path]::GetFullPath((Join-Path $fixtureRepo 'artifacts/package/release'))
        [void][IO.Directory]::CreateDirectory($publish)
        [IO.File]::Copy((Join-Path $RepositoryRoot 'Directory.Build.props'), (Join-Path $fixtureRepo 'Directory.Build.props'))
        $exe = Join-Path $publish 'StudyReportEvaluator.App.exe'
        $bytes = [byte[]]::new(512)
        $bytes[0] = 0x4D; $bytes[1] = 0x5A
        [BitConverter]::GetBytes([int]64).CopyTo($bytes, 0x3C)
        $bytes[64] = 0x50; $bytes[65] = 0x45
        $bytes[68] = 0x64; $bytes[69] = 0x86
        $bytes[84] = 0xF0; $bytes[86] = 0x22
        $bytes[88] = 0x0B; $bytes[89] = 0x02
        $bytes[511] = 1
        [IO.File]::WriteAllBytes($exe, $bytes)
        $knownHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
        $knownSidecar = [Text.UTF8Encoding]::new($false).GetBytes("$knownHash  StudyReportEvaluator-win-x64.exe`n")
        $final = Join-Path $output 'StudyReportEvaluator-win-x64.exe'
        $sidecar = $final + '.sha256'
        function Write-FixturePair {
            # Intentionally isolate the copy/transaction unit from PE/version admission.
            $source = [IO.File]::Open($exe, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            try { return Write-SingleFileArtifactPair -SourceStream $source -OutputDirectory $output }
            finally { $source.Dispose() }
        }
        """;

    [Fact]
    public async Task Script_parses_has_only_two_directory_parameters_and_never_publishes_or_launches()
    {
        string source = File.ReadAllText(ScriptPath);
        Assert.Contains("#Requires -Version 7.0", source, StringComparison.Ordinal);
        Assert.Contains("#Requires -PSEdition Core", source, StringComparison.Ordinal);
        Assert.Contains("P02 and P06", source, StringComparison.Ordinal);
        foreach (string forbidden in new[]
        {
            "publish-windows.ps1", "dotnet publish", "dotnet restore", "powershell.exe",
            "Import-Module", "Add-Type", "Start-Process", "Process]::Start", "Assembly]::Load",
            "Set-AuthenticodeSignature", "signtool", "-Recurse", "DOTNET_BUNDLE_EXTRACT_BASE_DIR",
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }

        await AssertFunctionsSucceedAsync("""
            $parameters = @($ast.ParamBlock.Parameters)
            if ($parameters.Count -ne 2 -or
                $parameters[0].Name.VariablePath.UserPath -cne 'PublishedDirectory' -or
                $parameters[1].Name.VariablePath.UserPath -cne 'OutputDirectory' -or
                $parameters[0].StaticType -ne [string] -or $parameters[1].StaticType -ne [string]) {
                throw 'Unexpected public packaging API.'
            }
            $entryCalls = @($ast.EndBlock.Statements | Where-Object {
                $_ -is [System.Management.Automation.Language.PipelineAst] -and
                $_.Extent.Text.StartsWith('Invoke-SingleFilePackaging ')
            })
            if ($entryCalls.Count -ne 1) { throw 'Missing single packaging entry point.' }
            """);
    }

    [Fact]
    public async Task Defaults_are_final_singlefile_publish_and_owned_artifacts_with_repository_relative_overrides()
    {
        await AssertFunctionsSucceedAsync(Fixture + "\n" + """
            $paths = Resolve-SingleFilePackagePaths -RepositoryRoot $fixtureRepo
            if ($paths.PublishedDirectory -cne $publish -or
                $paths.OutputDirectory -cne (Join-Path $fixtureRepo 'artifacts/package') -or
                $paths.ExecutablePath -cne (Join-Path $paths.OutputDirectory 'StudyReportEvaluator-win-x64.exe') -or
                $paths.HashPath -cne ($paths.ExecutablePath + '.sha256')) { throw 'Wrong default artifact paths.' }
            $relative = Resolve-SingleFilePackagePaths -RepositoryRoot $fixtureRepo `
                -PublishedDirectory 'artifacts/package/publish/win-x64-singlefile' -OutputDirectory 'artifacts/package/release'
            if ($relative.PublishedDirectory -cne $publish -or $relative.OutputDirectory -cne $output) {
                throw 'Relative paths depend on the caller cwd.'
            }
            $externalInput = Join-Path $TestRoot 'caller-owned-input'
            foreach ($driveLetter in @($externalInput.Substring(0, 1).ToLowerInvariant(), $externalInput.Substring(0, 1).ToUpperInvariant())) {
                $external = Resolve-SingleFilePackagePaths -RepositoryRoot $fixtureRepo `
                    -PublishedDirectory ($driveLetter + $externalInput.Substring(1)) -OutputDirectory $output
                if (-not $external.PublishedDirectory.Equals($externalInput, [StringComparison]::OrdinalIgnoreCase) -or
                    $external.OutputDirectory -cne $output -or (Test-Path -LiteralPath $external.PublishedDirectory)) {
                    throw 'Same-drive caller-owned source was changed or not preserved.'
                }
            }
            if (Test-Path -LiteralPath $output) { throw 'Path resolution wrote output.' }
            """);
    }

    [Fact]
    public async Task Paths_reject_different_drive_sources_before_filesystem_probes_without_writes()
    {
        // Parent reproduced SUBST Z: -> artifacts/package/publish/win-x64-singlefile:
        // source Z:\ with output set to the actual publish directory was accepted; no packaging writes.
        // Exercise the root mismatch without creating a mapping or assuming any drive is free.
        await AssertFunctionsSucceedAsync(Fixture + "\n" + """
            $repositoryDrive = [IO.Path]::GetPathRoot($fixtureRepo)
            $differentDrive = if ($repositoryDrive -ieq 'A:\') { 'B:\' } else { 'A:\' }
            function Assert-PlainDirectoryAncestors {
                param([string] $Directory)
                throw 'Filesystem alias probes ran before different-drive rejection.'
            }
            foreach ($source in @($differentDrive, ($differentDrive + 'missing-' + [Guid]::NewGuid().ToString('N')))) {
                foreach ($destination in @($output, $publish)) {
                    Assert-Rejected {
                        Resolve-SingleFilePackagePaths -RepositoryRoot $fixtureRepo `
                            -PublishedDirectory $source -OutputDirectory $destination
                    } 'same logical drive'
                }
            }
            if ((Test-Path -LiteralPath $output) -or
                (Get-Sha256Hex -Path $exe) -cne $knownHash -or
                @(Get-ChildItem -LiteralPath $publish -Force).Count -ne 1) {
                throw 'Rejected different-drive paths created output or changed the source.'
            }
            """);
    }

    [Fact]
    public async Task Paths_reject_output_escape_input_aliases_and_noncanonical_names_without_writes()
    {
        await AssertFunctionsSucceedAsync(Fixture + "\n" + """
            foreach ($destination in @($publish, $publish.ToUpperInvariant(), (Join-Path $publish 'child'))) {
                Assert-Rejected { Resolve-SingleFilePackagePaths -RepositoryRoot $fixtureRepo -PublishedDirectory $publish -OutputDirectory $destination } 'must not alias'
            }
            foreach ($destination in @((Join-Path $TestRoot 'outside'), (Join-Path $fixtureRepo 'artifacts/package-other'),
                    (Join-Path $fixtureRepo 'artifacts/package/../../work'))) {
                Assert-Rejected { Resolve-SingleFilePackagePaths -RepositoryRoot $fixtureRepo -OutputDirectory $destination } 'repository-owned'
            }
            foreach ($path in @(($publish + '.'), ($publish + ' '), (Join-Path $TestRoot 'PUBLIS~1'))) {
                Assert-Rejected { Resolve-SingleFilePackagePaths -RepositoryRoot $fixtureRepo -PublishedDirectory $path } 'canonical directory names'
            }
            if (Test-Path -LiteralPath $output) { throw 'Rejected paths created output.' }
            if ([Convert]::ToHexString([IO.File]::ReadAllBytes($exe)) -cne [Convert]::ToHexString($bytes)) {
                throw 'Rejected paths changed the source.'
            }
            """);
    }

    [Fact]
    public async Task Bad_input_layout_PE_and_missing_versions_fail_before_creating_or_replacing_outputs()
    {
        await AssertFunctionsSucceedAsync(Fixture + "\n" + """
            $existingOutput = Join-Path $fixtureRepo 'artifacts/package/previous'
            [void][IO.Directory]::CreateDirectory($existingOutput)
            $oldExe = Join-Path $existingOutput 'StudyReportEvaluator-win-x64.exe'
            $oldHash = $oldExe + '.sha256'
            [IO.File]::WriteAllBytes($oldExe, $bytes)
            [IO.File]::WriteAllBytes($oldHash, $knownSidecar)
            $oldTime = [IO.File]::GetLastWriteTimeUtc($oldExe)
            $cases = @(
                @{ Name = 'missing'; Change = { [IO.Directory]::Delete($publish, $true) }; Error = 'does not exist' }
                @{ Name = 'empty'; Change = { [IO.File]::Delete($exe) }; Error = 'exactly one' }
                @{ Name = 'zero'; Change = { [IO.File]::WriteAllBytes($exe, [byte[]]@()) }; Error = 'nonzero' }
                @{ Name = 'duplicate'; Change = { [IO.File]::WriteAllBytes((Join-Path $publish 'second.exe'), $bytes) }; Error = 'exactly one' }
                @{ Name = 'sidecar'; Change = { [IO.File]::WriteAllText(($exe + '.sha256'), 'extra') }; Error = 'exactly one' }
                @{ Name = 'symbols'; Change = { [IO.File]::WriteAllText((Join-Path $publish 'app.pdb'), 'extra') }; Error = 'exactly one' }
                @{ Name = 'hidden'; Change = { $p = Join-Path $publish '.env'; [IO.File]::WriteAllText($p, 'synthetic'); [IO.File]::SetAttributes($p, [IO.FileAttributes]::Hidden) }; Error = 'exactly one' }
                @{ Name = 'workbook'; Change = { [IO.File]::WriteAllText((Join-Path $publish 'input.xlsx'), 'synthetic') }; Error = 'exactly one' }
                @{ Name = 'subdirectory'; Change = { [void][IO.Directory]::CreateDirectory((Join-Path $publish 'runtimes')) }; Error = 'exactly one' }
                @{ Name = 'name'; Change = { [IO.File]::Move($exe, (Join-Path $publish 'other.exe')) }; Error = 'exactly one' }
                @{ Name = 'dos'; Change = { [IO.File]::WriteAllText($exe, 'not a PE') }; Error = 'PE DOS header' }
                @{ Name = 'offset'; Change = { $b = $bytes.Clone(); [BitConverter]::GetBytes([int]::MaxValue).CopyTo($b, 0x3C); [IO.File]::WriteAllBytes($exe, $b) }; Error = 'PE header offset' }
                @{ Name = 'signature'; Change = { $b = $bytes.Clone(); $b[64] = 0; [IO.File]::WriteAllBytes($exe, $b) }; Error = 'PE signature' }
                @{ Name = 'architecture'; Change = { $b = $bytes.Clone(); $b[68] = 0x4C; $b[69] = 0x01; [IO.File]::WriteAllBytes($exe, $b) }; Error = 'AMD64' }
                @{ Name = 'dll'; Change = { $b = $bytes.Clone(); $b[87] = 0x20; [IO.File]::WriteAllBytes($exe, $b) }; Error = 'not a DLL' }
                @{ Name = 'no-version'; Change = { }; Error = 'ProductVersion/FileVersion' }
            )
            foreach ($case in $cases) {
                if (Test-Path -LiteralPath $publish) { [IO.Directory]::Delete($publish, $true) }
                [void][IO.Directory]::CreateDirectory($publish)
                [IO.File]::WriteAllBytes($exe, $bytes)
                & $case.Change
                foreach ($destination in @($output, $existingOutput)) {
                    Assert-Rejected { Invoke-SingleFilePackaging -RepositoryRoot $fixtureRepo -PublishedDirectory $publish -OutputDirectory $destination } $case.Error
                }
                if (Test-Path -LiteralPath $output) { throw ('Bad input created output: ' + $case.Name) }
                if ([Convert]::ToHexString([IO.File]::ReadAllBytes($oldExe)) -cne [Convert]::ToHexString($bytes) -or
                    [Convert]::ToHexString([IO.File]::ReadAllBytes($oldHash)) -cne [Convert]::ToHexString($knownSidecar) -or
                    [IO.File]::GetLastWriteTimeUtc($oldExe) -ne $oldTime -or
                    @(Get-ChildItem -LiteralPath $existingOutput -Force).Count -ne 2) {
                    throw ('Bad input modified the previous pair: ' + $case.Name)
                }
            }
            """);
    }

    [Fact]
    public async Task Reparse_parents_and_hardlink_aliases_are_rejected_without_touching_source_or_targets()
    {
        await AssertFunctionsSucceedAsync(Fixture + "\n" + """
            $external = Join-Path $TestRoot 'external'
            [void][IO.Directory]::CreateDirectory($external)
            $workbook = Join-Path $external 'keep.xlsx'
            [IO.File]::WriteAllText($workbook, 'synthetic workbook sentinel')
            $workbookHash = Get-Sha256Hex -Path $workbook
            $sourceLink = Join-Path $TestRoot 'source-junction'
            $outputLink = Join-Path $fixtureRepo 'artifacts/package/output-junction'
            try {
                [void](New-Item -ItemType Junction -Path $sourceLink -Target (Split-Path $publish -Parent))
                [void](New-Item -ItemType Junction -Path $outputLink -Target $external)
                Assert-Rejected { Invoke-SingleFilePackaging -RepositoryRoot $fixtureRepo `
                    -PublishedDirectory (Join-Path $sourceLink 'win-x64-singlefile') -OutputDirectory $output } 'Reparse points'
                Assert-Rejected { Invoke-SingleFilePackaging -RepositoryRoot $fixtureRepo `
                    -PublishedDirectory $publish -OutputDirectory (Join-Path $outputLink 'new-output') } 'Reparse points'
            }
            finally {
                if (Test-Path -LiteralPath $sourceLink) { [IO.Directory]::Delete($sourceLink) }
                if (Test-Path -LiteralPath $outputLink) { [IO.Directory]::Delete($outputLink) }
            }
            $alias = Join-Path $TestRoot 'hardlink.exe'
            try {
                [void](New-Item -ItemType HardLink -Path $alias -Target $exe)
                Assert-Rejected { Invoke-SingleFilePackaging -RepositoryRoot $fixtureRepo -PublishedDirectory $publish -OutputDirectory $output } 'Hard links'
                Assert-Rejected { Get-PlainPackageFile -Path $alias } 'Hard links'
            }
            finally { [IO.File]::Delete($alias) }
            if ((Get-Sha256Hex -Path $exe) -cne $knownHash -or (Get-Sha256Hex -Path $workbook) -cne $workbookHash -or
                @(Get-ChildItem -LiteralPath $external -Force).Count -ne 1 -or (Test-Path -LiteralPath $output)) {
                throw 'Rejected link changed caller-owned content.'
            }
            """);
    }

    [Fact]
    public async Task Product_and_file_version_defaults_follow_props_including_suffix_and_reject_ambiguity()
    {
        await AssertFunctionsSucceedAsync("""
            $props = Join-Path $TestRoot 'Directory.Build.props'
            foreach ($case in @(@{ Prefix = '7.8.9'; Suffix = ''; Product = '7.8.9' },
                    @{ Prefix = '7.8.10'; Suffix = 'rc.2'; Product = '7.8.10-rc.2' })) {
                [IO.File]::WriteAllText($props, "<Project><PropertyGroup><VersionPrefix>$($case.Prefix)</VersionPrefix><VersionSuffix>$($case.Suffix)</VersionSuffix></PropertyGroup></Project>")
                $version = Get-SingleFilePackageVersion -RepositoryRoot $TestRoot
                if ($version.ProductVersion -cne $case.Product -or $version.FileVersion -cne ($case.Prefix + '.0')) {
                    throw 'Canonical version is hard-coded or suffix handling drifted.'
                }
            }
            [IO.File]::WriteAllText($props, '<Project><PropertyGroup><VersionPrefix>7.8.9</VersionPrefix><VersionPrefix>7.8.10</VersionPrefix><VersionSuffix /></PropertyGroup></Project>')
            Assert-Rejected { Get-SingleFilePackageVersion -RepositoryRoot $TestRoot } 'canonical product version'
            """);
    }

    [Fact]
    public async Task Version_validator_reads_real_compiled_metadata_and_rejects_each_field_independently()
    {
        // A managed assembly is used ONLY for the FileVersionInfo unit, not as an EXE/bundle.
        string assemblyPath = Path.Combine(AppContext.BaseDirectory, "StudyReportEvaluator.App.dll");
        await AssertFunctionsSucceedAsync($"$assembly = {Quote(assemblyPath)}\n" + """
            $version = Get-SingleFilePackageVersion -RepositoryRoot $RepositoryRoot
            Assert-SingleFileBinaryVersion -Path $assembly -ProductVersion $version.ProductVersion -FileVersion $version.FileVersion
            Assert-Rejected { Assert-SingleFileBinaryVersion -Path $assembly -ProductVersion '99.98.97' -FileVersion $version.FileVersion } 'ProductVersion/FileVersion'
            Assert-Rejected { Assert-SingleFileBinaryVersion -Path $assembly -ProductVersion $version.ProductVersion -FileVersion '99.98.97.0' } 'ProductVersion/FileVersion'
            """);
    }

    [Fact]
    public async Task Existing_output_requires_a_complete_PE_pair_and_exact_sidecar_before_staging()
    {
        await AssertFunctionsSucceedAsync(Fixture + "\n" + """
            [void][IO.Directory]::CreateDirectory($output)
            # A typed list keeps each byte[] intact instead of pipeline-unrolling its bytes.
            $invalid = [Collections.Generic.List[byte[]]]::new()
            $invalid.Add([byte[]]([Text.UTF8Encoding]::new($true).GetPreamble() + $knownSidecar))
            $invalid.Add([Text.Encoding]::UTF8.GetBytes("$knownHash  StudyReportEvaluator-win-x64.exe`r`n"))
            $invalid.Add([Text.Encoding]::UTF8.GetBytes("$($knownHash.ToLowerInvariant())  StudyReportEvaluator-win-x64.exe`n"))
            $invalid.Add([Text.Encoding]::UTF8.GetBytes("$knownHash StudyReportEvaluator-win-x64.exe`n"))
            $invalid.Add([Text.Encoding]::UTF8.GetBytes("$knownHash  StudyReportEvaluator.App.exe`n"))
            $invalid.Add([Text.Encoding]::UTF8.GetBytes("$knownHash  StudyReportEvaluator-win-x64.exe`nextra`n"))
            $invalid.Add([Text.Encoding]::UTF8.GetBytes("$('0' * 64)  StudyReportEvaluator-win-x64.exe`n"))
            if ($invalid.Count -ne 7) { throw 'Malformed sidecar cases were lost.' }
            foreach ($badBytes in $invalid) {
                [IO.File]::WriteAllBytes($final, $bytes)
                [IO.File]::WriteAllBytes($sidecar, $badBytes)
                Assert-Rejected { Write-FixturePair } 'SHA-256 sidecar must be exact'
                if ([Convert]::ToHexString([IO.File]::ReadAllBytes($sidecar)) -cne [Convert]::ToHexString([byte[]]$badBytes) -or
                    (Get-Sha256Hex -Path $final) -cne $knownHash -or @(Get-ChildItem -LiteralPath $output -Force).Count -ne 2) {
                    throw 'Invalid existing pair was overwritten or staged.'
                }
            }
            [IO.File]::Delete($sidecar)
            Assert-Rejected { Write-FixturePair } 'incomplete existing'
            [IO.File]::Delete($final)
            [IO.File]::WriteAllBytes($sidecar, $knownSidecar)
            Assert-Rejected { Write-FixturePair } 'incomplete existing'
            [void][IO.Directory]::CreateDirectory($final)
            Assert-Rejected { Write-FixturePair } 'directory at a package file path'
            [IO.Directory]::Delete($final)
            [IO.File]::WriteAllText($final, 'arbitrary workbook bytes, not a PE')
            $arbitraryHash = Get-Sha256Hex -Path $final
            [IO.File]::WriteAllText($sidecar, "$arbitraryHash  StudyReportEvaluator-win-x64.exe`n", [Text.UTF8Encoding]::new($false))
            Assert-Rejected { Write-FixturePair } 'invalid PE'
            if ((Get-Sha256Hex -Path $final) -cne $arbitraryHash) { throw 'Arbitrary file was overwritten.' }
            """);
    }

    [Fact]
    public async Task Copy_unit_preserves_exact_bytes_generates_exact_hash_and_repackages_without_touching_workbooks()
    {
        await AssertFunctionsSucceedAsync(Fixture + "\n" + """
            [void][IO.Directory]::CreateDirectory($output)
            $workbook = Join-Path $output 'keep.partial.xlsx'
            [IO.File]::WriteAllText($workbook, 'synthetic caller-owned workbook sentinel')
            $workbookHash = Get-Sha256Hex -Path $workbook
            $workbookTime = [IO.File]::GetLastWriteTimeUtc($workbook)
            $inputTime = [IO.File]::GetLastWriteTimeUtc($exe)
            foreach ($iteration in @(1, 2)) {
                if ((Write-FixturePair) -cne $knownHash -or
                    [Convert]::ToHexString([IO.File]::ReadAllBytes($final)) -cne [Convert]::ToHexString($bytes) -or
                    [Convert]::ToHexString([IO.File]::ReadAllBytes($sidecar)) -cne [Convert]::ToHexString($knownSidecar)) {
                    throw 'Copy/rename/checksum bytes are not exact and reproducible.'
                }
                if ([IO.File]::GetLastWriteTimeUtc($exe) -ne $inputTime -or (Get-Sha256Hex -Path $exe) -cne $knownHash -or
                    (Get-Sha256Hex -Path $workbook) -cne $workbookHash -or [IO.File]::GetLastWriteTimeUtc($workbook) -ne $workbookTime -or
                    @(Get-ChildItem -LiteralPath $output -Force).Count -ne 3 -or
                    @(Get-ChildItem -LiteralPath $publish -Force).Count -ne 1) { throw 'Packaging changed input/data or leaked temporary files.' }
            }
            # A changed input is also replaced as a pair, without retaining stale hashes.
            $bytes[511] = 2
            [IO.File]::WriteAllBytes($exe, $bytes)
            $newHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
            if ((Write-FixturePair) -cne $newHash -or (Get-Sha256Hex -Path $final) -cne $newHash) {
                throw 'Updated byte copy was not committed.'
            }
            Assert-ExactSingleFileSidecar -Path $sidecar -Hash $newHash
            """);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_commit_failure_restores_previous_pair_and_cleans_only_owned_staging(bool blockSecondCommit)
    {
        await AssertFunctionsSucceedAsync(Fixture + $"\n$blockSecondCommit = {(blockSecondCommit ? "$true" : "$false")}\n" + """
            [void](Write-FixturePair)
            $oldTime = [IO.File]::GetLastWriteTimeUtc($final)
            $workbook = Join-Path $output 'keep.xlsx'
            [IO.File]::WriteAllText($workbook, 'synthetic untouched workbook')
            $workbookHash = Get-Sha256Hex -Path $workbook
            $unrelatedTemp = Join-Path $output '.singlefile-not-owned-by-this-run'
            [void][IO.Directory]::CreateDirectory($unrelatedTemp)
            [IO.File]::WriteAllText((Join-Path $unrelatedTemp 'keep.txt'), 'untouched')
            $bytes[511] = 2
            [IO.File]::WriteAllBytes($exe, $bytes)
            $inputHash = Get-Sha256Hex -Path $exe
            $inputTime = [IO.File]::GetLastWriteTimeUtc($exe)
            $blocked = if ($blockSecondCommit) { $sidecar } else { $final }
            # Read-sharing permits validation and backup, but denies the rename itself.
            $handle = [IO.File]::Open($blocked, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            try {
                $message = 'commit ' + [IO.Path]::GetFileName($blocked) + '; original output state restored'
                Assert-Rejected { Write-FixturePair } $message
            }
            finally { $handle.Dispose() }
            if ((Get-Sha256Hex -Path $final) -cne $knownHash -or
                [Convert]::ToHexString([IO.File]::ReadAllBytes($sidecar)) -cne [Convert]::ToHexString($knownSidecar) -or
                [IO.File]::GetLastWriteTimeUtc($final) -ne $oldTime -or
                (Get-Sha256Hex -Path $exe) -cne $inputHash -or [IO.File]::GetLastWriteTimeUtc($exe) -ne $inputTime -or
                (Get-Sha256Hex -Path $workbook) -cne $workbookHash -or
                [IO.File]::ReadAllText((Join-Path $unrelatedTemp 'keep.txt')) -cne 'untouched' -or
                @(Get-ChildItem -LiteralPath $output -Force).Count -ne 4) {
                throw 'Failed commit lost old files, modified source/workbooks, or cleaned unrelated paths.'
            }
            """);
    }

    public static bool RunPublishedSingleFileArtifactTest =>
        string.Equals(Environment.GetEnvironmentVariable(OptInVariable), "1", StringComparison.Ordinal);

    [Fact(SkipUnless = nameof(RunPublishedSingleFileArtifactTest),
        Skip = "Set RUN_WINDOWS_SINGLEFILE_ARTIFACT_TESTS=1 after parent P02 publish; never auto-publishes or launches.")]
    [Trait("Category", "WindowsSingleFileArtifactIntegration")]
    public async Task Opted_in_P02_artifact_is_packaged_by_the_actual_script_with_identical_bytes_and_versions()
    {
        Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000));
        string repositoryRoot = FindRepositoryRoot();
        string publishDirectory = Path.Combine(repositoryRoot, "artifacts", "package", "publish", "win-x64-singlefile");
        string sourcePath = Path.Combine(publishDirectory, "StudyReportEvaluator.App.exe");
        Assert.True(File.Exists(sourcePath), "Opt-in requires the actual P02 publish; a missing artifact is a failure, not a skip.");
        Assert.Equal(sourcePath, Assert.Single(Directory.GetFileSystemEntries(publishDirectory)));
        string outputDirectory = Path.Combine(repositoryRoot, "artifacts", "package", ".p05-artifact-test-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(outputDirectory));
        DateTime sourceTime = File.GetLastWriteTimeUtc(sourcePath);
        string sourceHash = ComputeSha256(sourcePath);
        string finalPath = Path.Combine(outputDirectory, ArtifactName);
        XDocument props = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));
        string prefix = Assert.Single(props.Descendants("VersionPrefix")).Value;
        string suffix = Assert.Single(props.Descendants("VersionSuffix")).Value;
        string productVersion = suffix.Length == 0 ? prefix : $"{prefix}-{suffix}";

        try
        {
            for (int iteration = 0; iteration < 2; iteration++)
            {
                ProcessStartInfo startInfo = CreatePowerShellStartInfo(Path.GetTempPath());
                foreach (string argument in new[] { "-File", ScriptPath, "-OutputDirectory", outputDirectory })
                {
                    startInfo.ArgumentList.Add(argument);
                }

                AssertSucceeded(await RunPowerShellAsync(startInfo, TimeSpan.FromSeconds(90)));
                Assert.Equal(new[] { ArtifactName, HashName }, Directory.GetFileSystemEntries(outputDirectory)
                    .Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToArray());
                AssertSameBytes(sourcePath, finalPath);
                Assert.Equal(sourceHash, ComputeSha256(finalPath));
                Assert.Equal(new UTF8Encoding(false).GetBytes($"{sourceHash}  {ArtifactName}\n"),
                    File.ReadAllBytes(Path.Combine(outputDirectory, HashName)));
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(finalPath);
                Assert.Equal(productVersion, version.ProductVersion?.Split('+', 2)[0]);
                Assert.Equal(prefix + ".0", version.FileVersion);
                Assert.Equal(sourceTime, File.GetLastWriteTimeUtc(sourcePath));
                Assert.Equal(sourceHash, ComputeSha256(sourcePath));
                Assert.Equal(sourcePath, Assert.Single(Directory.GetFileSystemEntries(publishDirectory)));
            }
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
        // No launch/extraction/clean-host claim. Internal bundle validation remains P02/P06.
    }

    private static async Task AssertFunctionsSucceedAsync(string body)
    {
        Assert.True(OperatingSystem.IsWindows(), "P05 PowerShell contracts require Windows and PowerShell Core 7+.");
        string testRoot = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-P05-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            string command = $$"""
                $ErrorActionPreference = 'Stop'
                Set-StrictMode -Version Latest
                try {
                    if ($PSVersionTable.PSEdition -cne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell Core 7+ required.' }
                    $RepositoryRoot = {{Quote(FindRepositoryRoot())}}
                    $TestRoot = {{Quote(testRoot)}}
                    $tokens = $null; $parseErrors = $null
                    $ast = [System.Management.Automation.Language.Parser]::ParseFile({{Quote(ScriptPath)}}, [ref]$tokens, [ref]$parseErrors)
                    if (@($parseErrors).Count -ne 0) { throw ('Packaging script parse errors: ' + ($parseErrors.Message -join '; ')) }
                    foreach ($name in @({{string.Join(",", Functions.Select(Quote))}})) {
                        $definitions = @($ast.EndBlock.Statements | Where-Object {
                            $_ -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $_.Name -ceq $name
                        })
                        if ($definitions.Count -ne 1) { throw "Expected one function definition: $name" }
                        . ([scriptblock]::Create($definitions[0].Extent.Text))
                    }
                    function Assert-Rejected {
                        param([scriptblock] $Action, [string] $Message)
                        $rejected = $false
                        try { & $Action | Out-Null }
                        catch {
                            if (-not $_.Exception.Message.Contains($Message, [StringComparison]::Ordinal)) {
                                throw ("Expected rejection containing '$Message', received: " + $_.Exception.Message)
                            }
                            $rejected = $true
                        }
                        if (-not $rejected) { throw "Expected rejection containing '$Message'." }
                    }
                    {{body}}
                }
                catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
                """;
            ProcessStartInfo startInfo = CreatePowerShellStartInfo(testRoot);
            startInfo.ArgumentList.Add("-EncodedCommand");
            startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
            AssertSucceeded(await RunPowerShellAsync(startInfo, TimeSpan.FromSeconds(45)));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static ProcessStartInfo CreatePowerShellStartInfo(string workingDirectory)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = FindPowerShellCoreExecutable(),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task<ProcessResult> RunPowerShellAsync(ProcessStartInfo startInfo, TimeSpan maximumDuration)
    {
        using Process process = new() { StartInfo = startInfo };
        bool started = false;
        try
        {
            started = process.Start();
            Assert.True(started);
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(maximumDuration);
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanup.Token);
            }
        }
    }

    private static string FindPowerShellCoreExecutable()
    {
        HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.Add(Path.Combine(directory.Trim('"'), "pwsh.exe"));
        }

        string installedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell");
        if (Directory.Exists(installedRoot))
        {
            foreach (string directory in Directory.EnumerateDirectories(installedRoot))
            {
                candidates.Add(Path.Combine(directory, "pwsh.exe"));
            }
        }

        // Prefer the newest installed pwsh, never Windows PowerShell 5.1.
        return candidates.Where(File.Exists)
            .Select(path => (Path: path, Info: FileVersionInfo.GetVersionInfo(path)))
            .Where(candidate => candidate.Info.FileMajorPart >= 7)
            .OrderByDescending(candidate => new Version(candidate.Info.FileMajorPart, candidate.Info.FileMinorPart,
                candidate.Info.FileBuildPart, candidate.Info.FilePrivatePart))
            .Select(candidate => Path.GetFullPath(candidate.Path)).FirstOrDefault()
            ?? throw new FileNotFoundException("PowerShell Core 7+ executable pwsh.exe was not found.");
    }

    private static void AssertSameBytes(string expectedPath, string actualPath)
    {
        using FileStream expected = File.OpenRead(expectedPath);
        using FileStream actual = File.OpenRead(actualPath);
        Assert.Equal(expected.Length, actual.Length);
        byte[] expectedBuffer = new byte[65536];
        byte[] actualBuffer = new byte[65536];
        int count;
        while ((count = expected.Read(expectedBuffer)) != 0)
        {
            actual.ReadExactly(actualBuffer.AsSpan(0, count));
            Assert.True(expectedBuffer.AsSpan(0, count).SequenceEqual(actualBuffer.AsSpan(0, count)), "Packaged EXE bytes changed.");
        }

        Assert.Equal(-1, actual.ReadByte());
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void AssertSucceeded(ProcessResult result) =>
        Assert.True(result.ExitCode == 0, $"P05 PowerShell contract failed.\n{result.StandardOutput}\n{result.StandardError}");

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string ScriptPath => Path.Combine(FindRepositoryRoot(), "scripts", "package-windows-singlefile.ps1");

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