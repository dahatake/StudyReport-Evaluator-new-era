#Requires -Version 7.4
#Requires -PSEdition Core

<#
.SYNOPSIS
Runs the seven P06 tests against the parent's already-packaged EXE; emits closed JSON.
.DESCRIPTION
Use pwsh -NoLogo -NoProfile. The parent owns the Release build, P02 publish and P05
packaging. This script never restores, builds, republishes, signs or authenticates.
P06 needs an interactive Windows x64 desktop, PowerShell Core and a WindowsDesktop
10.0.x UIA observer. PATH isolation is NOT evidence of an OS without dependencies.
Run serially for each evidence destination; source and package must remain unchanged.
.PARAMETER ResultsDirectory
Optional parent under repository TestResults or artifacts/test (including those roots).
Defaults to TestResults/windows-singlefile. A new GUID child is always created; no
existing TRX is reused and no directory is recursively cleaned. Raw TRX stays ignored.
.PARAMETER DevelopmentOnly
Allows an unchanged dirty checkout, always returns PASS_DEVELOPMENT, never publishable.
Writes artifacts/test/singlefile/StudyReportEvaluator-win-x64.exe.evidence.json without
touching artifacts/package/StudyReportEvaluator-win-x64.exe.evidence.json. Without this
switch, a clean checkout is required and success is PASS_REQUIRED, not clean-host PASS.
.NOTES
C01 schema v1: exactly schemaVersion, evidenceKind, status, sourceCommit,
sourceStatusEntryCount, sourceStatusSha256, sourceContentSha256, productVersion,
host, package, runtime, tests, checks, limitations. No raw observations are exported.
sourceStatusSha256 hashes Git porcelain-v1 -z bytes, including final NUL. Entry count
counts a rename/copy as ONE entry (its extra pathname is not another status entry).
sourceContentSha256 hashes the ordinal-sorted git ls-files --cached --others
--exclude-standard -z inventory: UTF-8 records F NUL path NUL decimalBytes NUL
uppercaseFileSHA256 NUL, or M NUL path NUL for a missing tracked file. Paths use '/'.
Root artifacts/TestResults and bin/obj/.git/.vs/.venv/node_modules path segments are
excluded; other tracked files, including tracked ignored files, are still hashed.
All SHA-256 values are uppercase; sourceCommit is lowercase, 40 hex characters.
Success stdout is the evidence JSON; failure stderr is a fixed P07 code and stage,
with exit code 1 and no replacement evidence. Never echo child logs or source paths.
#>
[CmdletBinding()]
param(
    [string] $ResultsDirectory,
    [switch] $DevelopmentOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$ExecutableName = 'StudyReportEvaluator-win-x64.exe'
$EvidenceName = "$ExecutableName.evidence.json"
$TrxName = 'windows-singlefile.trx'
$TestClass = 'StudyReportEvaluator.App.Tests.Packaging.WindowsSingleFilePackageTests'
$Utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$Invariant = [System.Globalization.CultureInfo]::InvariantCulture

# Exact P06 methods and their exact observation ownership. Native failures emit no JSON.
$Scenarios = [ordered]@{
    Single_file_cold_and_warm_start_validate_payload_and_show_input = @('cold', 'warm')
    Single_file_missing_cached_CLI_is_recovered_by_the_standard_host = @('before-file-loss', 'missing-file-recovered')
    Single_file_deleted_cache_is_reextracted_after_moving_the_EXE = @('before-cache-loss', 'reextracted-after-move')
    Single_file_two_instances_share_cache_and_close_independently = @('concurrent-first', 'concurrent-second')
    Single_file_relative_input_and_two_prompts_are_observed_in_GUI_order = @('relative-input-prompts')
    Single_file_file_valued_extraction_base_fails_without_changing_data = @()
    Single_file_truncated_bundle_reports_a_host_error_without_changing_data = @()
}

function Resolve-SafePath {
    param([string] $Path, [string] $Base)

    if ([string]::IsNullOrWhiteSpace($Path) -or
        ([System.IO.Path]::IsPathRooted($Path) -and -not [System.IO.Path]::IsPathFullyQualified($Path))) {
        throw 'P07_UNSAFE_PATH'
    }

    $root = [System.IO.Path]::GetPathRoot($Path)
    foreach ($segment in $Path.Substring($root.Length).Replace('/', '\').Split('\', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        # Reject before normalization: traversal, ADS, DOS/device aliases and trailing dots/spaces.
        if ($segment -eq '.') { continue }
        if ($segment -eq '..' -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or
            $segment -match '~[0-9]+(?:\.|$)' -or
            $segment -match '^(CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)' -or
            $segment.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
            throw 'P07_UNSAFE_PATH'
        }
    }

    $full = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($Path, $Base))
    if ([System.IO.Path]::GetPathRoot($full) -notmatch '^[A-Za-z]:\\$') { throw 'P07_UNSAFE_PATH' }
    return $full
}

function Test-WithinRoot {
    param([string] $Root, [string] $Path)

    return $Path.Equals($Root, [System.StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($Root + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-PlainParents {
    param([string] $Directory)

    # Include every existing ancestor, even above the repository; never follow a junction.
    while (-not [string]::IsNullOrEmpty($Directory)) {
        $item = $null
        try { $item = Get-Item -LiteralPath $Directory -Force -ErrorAction Stop }
        catch [System.Management.Automation.ItemNotFoundException] { }
        if ($null -ne $item -and (-not $item.PSIsContainer -or
                ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'P07_UNSAFE_PARENT'
        }
        $Directory = [System.IO.Path]::GetDirectoryName($Directory)
    }
}

function Get-PlainFile {
    param([string] $Path, [switch] $AllowMissing)

    Assert-PlainParents -Directory ([System.IO.Path]::GetDirectoryName($Path))
    try { $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop }
    catch [System.Management.Automation.ItemNotFoundException] {
        if ($AllowMissing) { return $null }
        throw 'P07_MISSING_FILE'
    }
    if ($item.PSIsContainer -or [string]$item.LinkType -ceq 'HardLink' -or
        ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'P07_UNSAFE_FILE'
    }
    return $item
}

function Get-BytesHash {
    param([AllowEmptyCollection()] [byte[]] $Bytes)
    return [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($Bytes))
}

function Get-FileProof {
    param([string] $Path)

    [void](Get-PlainFile -Path $Path)
    $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'Read')
    try {
        return [pscustomobject]@{
            Bytes = $stream.Length
            Sha256 = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream))
        }
    }
    finally { $stream.Dispose() }
}

function Invoke-LocalProcess {
    param(
        [string] $Executable, [string[]] $Arguments,
        [switch] $Git, [switch] $PackageTests, [switch] $DiscardOutput,
        [int] $TimeoutMilliseconds = 60000
    )

    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Executable
    $info.WorkingDirectory = $repositoryRoot
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    if ($Git) {
        # -C alone does not override inherited GIT_DIR/GIT_INDEX_FILE/config overrides.
        foreach ($name in @($info.Environment.Keys)) {
            if ($name.StartsWith('GIT_', [System.StringComparison]::OrdinalIgnoreCase)) {
                [void]$info.Environment.Remove($name)
            }
        }
        $info.Environment['GIT_OPTIONAL_LOCKS'] = '0'
        $info.Environment['GIT_TERMINAL_PROMPT'] = '0'
    }

    $optIn = 'RUN_WINDOWS_SINGLEFILE_PACKAGE_TESTS'
    $hadOptIn = $info.Environment.ContainsKey($optIn)
    $previousOptIn = if ($hadOptIn) { $info.Environment[$optIn] } else { $null }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    $capture = [System.IO.MemoryStream]::new()
    $started = $false
    try {
        # Only the test child receives the override. The caller's environment is never changed.
        if ($PackageTests) { $info.Environment[$optIn] = '1' }
        $started = $process.Start()
        if (-not $started) { throw 'P07_CHILD_START' }
        $destination = if ($DiscardOutput) { [System.IO.Stream]::Null } else { $capture }
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($destination)
        $stderr = $process.StandardError.BaseStream.CopyToAsync([System.IO.Stream]::Null)
        if (-not $process.WaitForExit($TimeoutMilliseconds)) { throw 'P07_CHILD_TIMEOUT' }
        $drain = [System.Threading.Tasks.Task]::WhenAll([System.Threading.Tasks.Task[]]@($stdout, $stderr))
        if (-not $drain.Wait(10000)) { throw 'P07_CHILD_OUTPUT_TIMEOUT' }
        if ($capture.Length -gt 16MB) { throw 'P07_CHILD_OUTPUT_LIMIT' }
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Bytes = $capture.ToArray() }
    }
    finally {
        try {
            if ($started -and -not $process.HasExited) {
                $process.Kill($true) # Only this invocation's child tree, never a process-name search.
                if (-not $process.WaitForExit(10000)) { throw 'P07_CHILD_STOP' }
            }
        }
        finally {
            if ($PackageTests) {
                if ($hadOptIn) { $info.Environment[$optIn] = $previousOptIn }
                else { [void]$info.Environment.Remove($optIn) }
            }
            $process.Dispose()
            $capture.Dispose()
        }
    }
}

function Invoke-ReadOnlyGit {
    param([string[]] $Arguments, [switch] $AllowExitOne)

    $result = Invoke-LocalProcess -Executable $gitPath -Git -Arguments (
        @('--no-optional-locks', '-c', 'core.fsmonitor=false', '-c', 'core.untrackedCache=false', '-C', $repositoryRoot) + $Arguments)
    if ($result.ExitCode -ne 0 -and -not ($AllowExitOne -and $result.ExitCode -eq 1)) {
        throw 'P07_GIT_FAILED'
    }
    return $result
}

function Assert-IgnoredOutput {
    param([string] $Path)

    $relative = [System.IO.Path]::GetRelativePath($repositoryRoot, $Path).Replace('\', '/')
    # Without --no-index, tracked output files are NOT accepted as ignored.
    $result = Invoke-ReadOnlyGit -Arguments @('check-ignore', '--quiet', '--', $relative) -AllowExitOne
    if ($result.ExitCode -ne 0) { throw 'P07_OUTPUT_NOT_IGNORED' }
}

function Get-SourceSnapshot {
    $commit = $Utf8.GetString((Invoke-ReadOnlyGit -Arguments @('rev-parse', '--verify', 'HEAD^{commit}')).Bytes).Trim()
    if ($commit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'P07_SOURCE_COMMIT' }
    $statusArguments = @('status', '--porcelain=v1', '-z', '--untracked-files=all', '--ignore-submodules=none')
    $inventoryArguments = @('ls-files', '--cached', '--others', '--exclude-standard', '-z')
    $status = (Invoke-ReadOnlyGit -Arguments $statusArguments).Bytes
    $inventory = (Invoke-ReadOnlyGit -Arguments $inventoryArguments).Bytes
    $statusHash = Get-BytesHash -Bytes $status
    $inventoryHash = Get-BytesHash -Bytes $inventory
    $statusText = $Utf8.GetString($status)
    $entryCount = 0
    if ($statusText.Length -gt 0) {
        if (-not $statusText.EndsWith([string][char]0)) { throw 'P07_GIT_STATUS_FORMAT' }
        $records = $statusText.Split([char]0)
        for ($index = 0; $index -lt $records.Length - 1; $index++) {
            $record = $records[$index]
            if ($record.Length -lt 4 -or $record[2] -cne ' ') { throw 'P07_GIT_STATUS_FORMAT' }
            $entryCount++
            if ($record.Substring(0, 2).IndexOfAny([char[]]'RC') -ge 0) {
                $index++
                if ($index -ge $records.Length - 1 -or $records[$index].Length -eq 0) { throw 'P07_GIT_STATUS_FORMAT' }
            }
        }
    }

    $inventoryText = $Utf8.GetString($inventory)
    if (-not $inventoryText.EndsWith([string][char]0)) { throw 'P07_GIT_INVENTORY_FORMAT' }
    [string[]] $paths = $inventoryText.Split([char]0, [System.StringSplitOptions]::RemoveEmptyEntries)
    [System.Array]::Sort($paths, [System.StringComparer]::Ordinal)
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $content = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        foreach ($relative in $paths) {
            if ($relative -match '^(artifacts|TestResults)/|(^|/)(bin|obj|\.git|\.vs|\.venv|node_modules)(/|$)') { continue }
            if (-not $seen.Add($relative)) { throw 'P07_AMBIGUOUS_SOURCE_ENTRY' }
            $path = Resolve-SafePath -Path $relative -Base $repositoryRoot
            if (-not (Test-WithinRoot -Root $repositoryRoot -Path $path)) { throw 'P07_UNSAFE_SOURCE_ENTRY' }
            $item = Get-PlainFile -Path $path -AllowMissing
            if ($null -eq $item) { $record = "M`0$relative`0" }
            else {
                $proof = Get-FileProof -Path $path
                $record = "F`0$relative`0$($proof.Bytes.ToString($Invariant))`0$($proof.Sha256)`0"
            }
            $content.AppendData($Utf8.GetBytes($record))
        }
        if ($seen.Count -eq 0) { throw 'P07_EMPTY_SOURCE_INVENTORY' }
        $contentHash = [System.Convert]::ToHexString($content.GetHashAndReset())
    }
    finally { $content.Dispose() }

    # Bracket hashing too; never report a mixed inventory/HEAD/status snapshot.
    if ((Get-BytesHash -Bytes (Invoke-ReadOnlyGit -Arguments $inventoryArguments).Bytes) -cne $inventoryHash -or
        (Get-BytesHash -Bytes (Invoke-ReadOnlyGit -Arguments $statusArguments).Bytes) -cne $statusHash -or
        $Utf8.GetString((Invoke-ReadOnlyGit -Arguments @('rev-parse', '--verify', 'HEAD^{commit}')).Bytes).Trim() -cne $commit) {
        throw 'P07_SOURCE_CHANGED'
    }
    return [pscustomobject]@{ Commit = $commit; EntryCount = $entryCount; StatusSha256 = $statusHash; ContentSha256 = $contentHash }
}

function Get-ProductVersion {
    [void](Get-PlainFile -Path (Join-Path $repositoryRoot 'Directory.Build.props'))
    $tool = Join-Path $repositoryRoot 'dev\version.ps1'
    [void](Get-PlainFile -Path $tool)
    # 'show' is read-only and validates the canonical props, including SemVer components.
    $version = (& $tool show -RepositoryRoot $repositoryRoot -Json | Out-String) | ConvertFrom-Json -AsHashtable
    if ($version.Status -cne 'PASS' -or $version.Command -cne 'show' -or
        $version.Version -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z') {
        throw 'P07_PRODUCT_VERSION'
    }
    return [string]$version.Version
}

function Assert-PackageVersion {
    param([string] $Path, [string] $Version)
    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if (([string]$info.ProductVersion).Split('+', 2)[0] -cne $Version -or
        [string]$info.FileVersion -cne ($Version.Split('-', 2)[0] + '.0')) { throw 'P07_PACKAGE_VERSION' }
}

function Assert-ExactSidecar {
    param([string] $Path, [string] $Hash)
    $file = Get-PlainFile -Path $Path
    $expected = $Utf8.GetBytes("$Hash  $ExecutableName`n")
    if ($Hash -cnotmatch '\A[0-9A-F]{64}\z' -or $file.Length -ne $expected.Length -or
        [System.Convert]::ToHexString([System.IO.File]::ReadAllBytes($Path)) -cne [System.Convert]::ToHexString($expected)) {
        throw 'P07_SIDECAR_MISMATCH'
    }
}

function Assert-TrxInterval {
    param([string] $Start, [string] $Finish, [System.DateTimeOffset] $RunStart, [System.DateTimeOffset] $RunFinish)
    $startTime = [System.Xml.XmlConvert]::ToDateTimeOffset($Start)
    $finishTime = [System.Xml.XmlConvert]::ToDateTimeOffset($Finish)
    # Allow filesystem/clock rounding, not a prior run. TRX creation may be AFTER start.
    if ($startTime -lt $RunStart.AddSeconds(-2) -or $finishTime -gt $RunFinish.AddSeconds(2) -or $finishTime -lt $startTime) {
        throw 'P07_STALE_TRX'
    }
}

function Read-P06Observation {
    param([string] $Json, [string] $Method, [psobject] $Package, [System.Collections.Generic.HashSet[string]] $Seen)

    $document = [System.Text.Json.JsonDocument]::Parse($Json)
    try {
        $value = $document.RootElement
        if ($value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw 'P07_OBSERVATION_SCHEMA' }
        $allowed = @('scenario', 'artifactName', 'artifactSha256', 'exeBytes', 'observationMilliseconds',
            'extractedFileCount', 'extractedBytes', 'cliSha256', 'inputScreenObserved', 'importedPromptCountObserved',
            'childProcessObservation', 'cleanHost', 'networkIsolation', 'authentication', 'liveAi')
        $keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($property in $value.EnumerateObject()) {
            if ($property.Name -cnotin $allowed -or -not $keys.Add($property.Name)) { throw 'P07_OBSERVATION_SCHEMA' }
        }
        if ($keys.Count -ne $allowed.Count) { throw 'P07_OBSERVATION_SCHEMA' }
        $scenario = $value.GetProperty('scenario').GetString()
        if ($scenario -cnotin $Scenarios[$Method] -or -not $Seen.Add($scenario)) { throw 'P07_OBSERVATION_SCENARIO' }
        if ($value.GetProperty('artifactName').GetString() -cne $ExecutableName -or
            $value.GetProperty('artifactSha256').GetString() -cne $Package.Sha256 -or
            $value.GetProperty('exeBytes').GetInt64() -ne $Package.Bytes) { throw 'P07_OBSERVATION_ARTIFACT' }
        $cliHash = $value.GetProperty('cliSha256').GetString()
        if ($cliHash -cnotmatch '\A[0-9A-F]{64}\z' -or -not $value.GetProperty('inputScreenObserved').GetBoolean() -or
            $value.GetProperty('observationMilliseconds').GetInt64() -lt 0 -or
            $value.GetProperty('extractedFileCount').GetInt64() -le 0 -or
            $value.GetProperty('extractedBytes').GetInt64() -le 0 -or
            $value.GetProperty('childProcessObservation').GetString() -cne 'two-bounded-snapshots-not-an-event-audit') {
            throw 'P07_OBSERVATION_VALUE'
        }
        $prompts = $value.GetProperty('importedPromptCountObserved')
        if ($scenario -ceq 'relative-input-prompts') {
            if ($prompts.GetInt32() -ne 2) { throw 'P07_OBSERVATION_PROMPTS' }
        }
        elseif ($prompts.ValueKind -ne [System.Text.Json.JsonValueKind]::Null) { throw 'P07_OBSERVATION_PROMPTS' }
        foreach ($name in @('cleanHost', 'networkIsolation', 'authentication', 'liveAi')) {
            if ($value.GetProperty($name).GetString() -cne 'NOT_RUN') { throw 'P07_OBSERVATION_SCOPE' }
        }
        return $cliHash
    }
    finally { $document.Dispose() }
}

function Read-P06Trx {
    param([string] $Path, [psobject] $Package, [System.DateTimeOffset] $RunStart, [System.DateTimeOffset] $RunFinish)

    $file = Get-PlainFile -Path $Path
    if ($file.Length -le 0 -or $file.Length -gt 16MB -or
        $file.LastWriteTimeUtc -lt $RunStart.UtcDateTime.AddSeconds(-2) -or
        $file.LastWriteTimeUtc -gt $RunFinish.UtcDateTime.AddSeconds(2)) { throw 'P07_STALE_OR_INVALID_TRX' }
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = 16MB
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try {
        $trx = [System.Xml.XmlDocument]::new()
        $trx.XmlResolver = $null
        $trx.Load($reader)
    }
    finally { $reader.Dispose() }
    $ns = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $results = @($trx.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
    $definitions = @($trx.SelectNodes('/t:TestRun/t:TestDefinitions/t:UnitTest', $ns))
    $times = @($trx.SelectNodes('/t:TestRun/t:Times', $ns))
    $summary = @($trx.SelectNodes('/t:TestRun/t:ResultSummary', $ns))
    if ($results.Count -ne 7 -or $definitions.Count -ne 7 -or $times.Count -ne 1 -or $summary.Count -ne 1 -or
        $trx.SelectNodes('/t:TestRun/t:Results/*', $ns).Count -ne 7 -or
        $trx.SelectNodes('//t:UnitTestResult', $ns).Count -ne 7) { throw 'P07_TRX_TEST_COUNT' }
    Assert-TrxInterval -Start $times[0].GetAttribute('start') -Finish $times[0].GetAttribute('finish') -RunStart $RunStart -RunFinish $RunFinish

    $byId = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::Ordinal)
    $definedNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $executionIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $guidPattern = '\A[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\z'
    foreach ($definition in $definitions) {
        $methods = @($definition.SelectNodes('t:TestMethod', $ns))
        $executions = @($definition.SelectNodes('t:Execution', $ns))
        if ($methods.Count -ne 1 -or $executions.Count -ne 1) { throw 'P07_TRX_DEFINITION' }
        $method = $methods[0].GetAttribute('name')
        $id = $definition.GetAttribute('id')
        $execution = $executions[0].GetAttribute('id')
        if ($methods[0].GetAttribute('className') -cne $TestClass -or $method -cnotin $Scenarios.Keys -or
            $id -cnotmatch $guidPattern -or $execution -cnotmatch $guidPattern -or
            $byId.ContainsKey($id) -or -not $definedNames.Add($method) -or -not $executionIds.Add($execution)) {
            throw 'P07_TRX_UNKNOWN_OR_DUPLICATE_TEST'
        }
        $byId.Add($id, [pscustomobject]@{ Method = $method; Execution = $execution })
    }

    $passed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $observed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $cliHash = $null
    foreach ($result in $results) {
        $id = $result.GetAttribute('testId')
        if (-not $byId.ContainsKey($id)) { throw 'P07_TRX_UNKNOWN_OR_DUPLICATE_TEST' }
        $definition = $byId[$id]
        $fullName = "$TestClass.$($definition.Method)"
        if ($result.GetAttribute('testName') -cne $fullName -or
            $result.GetAttribute('executionId') -cne $definition.Execution -or -not $passed.Add($fullName)) {
            throw 'P07_TRX_UNKNOWN_OR_DUPLICATE_TEST'
        }
        if ($result.GetAttribute('outcome') -cne 'Passed') { throw 'P07_TRX_TEST_NOT_PASSED' }
        Assert-TrxInterval -Start $result.GetAttribute('startTime') -Finish $result.GetAttribute('endTime') -RunStart $RunStart -RunFinish $RunFinish
        $beforeCount = $observed.Count
        foreach ($output in $result.SelectNodes('t:Output/t:StdOut', $ns)) {
            foreach ($line in ($output.InnerText -split '\r?\n')) {
                if (-not $line.StartsWith('P06_OBSERVATION', [System.StringComparison]::Ordinal)) { continue }
                if (-not $line.StartsWith('P06_OBSERVATION ', [System.StringComparison]::Ordinal)) { throw 'P07_OBSERVATION_SCHEMA' }
                $hash = Read-P06Observation -Json $line.Substring('P06_OBSERVATION '.Length) -Method $definition.Method -Package $Package -Seen $observed
                if ($null -ne $cliHash -and $hash -cne $cliHash) { throw 'P07_OBSERVATION_CLI_MISMATCH' }
                $cliHash = $hash
            }
        }
        if ($observed.Count - $beforeCount -ne $Scenarios[$definition.Method].Count) { throw 'P07_OBSERVATION_COUNT' }
    }
    if ($passed.Count -ne 7 -or $observed.Count -ne 9) { throw 'P07_OBSERVATION_COUNT' }
    $counters = @($summary[0].SelectNodes('t:Counters', $ns))
    if ($summary[0].GetAttribute('outcome') -cnotin @('Completed', 'Passed') -or $counters.Count -ne 1) { throw 'P07_TRX_SUMMARY' }
    foreach ($name in @('total', 'executed', 'passed', 'failed', 'notExecuted')) {
        if (-not $counters[0].HasAttribute($name)) { throw 'P07_TRX_COUNTERS' }
    }
    foreach ($counter in $counters[0].Attributes) {
        $expected = if ($counter.Name -cin @('total', 'executed', 'passed')) { '7' } else { '0' }
        if ($counter.Value -cne $expected) { throw 'P07_TRX_COUNTERS' }
    }
    $proof = Get-FileProof -Path $Path
    return [pscustomobject]@{
        Passed = $passed
        Observed = $observed
        CliSha256 = $cliHash
        Tests = [ordered]@{
            total = $results.Count
            passed = $passed.Count
            failed = [int]$counters[0].GetAttribute('failed')
            skipped = [int]$counters[0].GetAttribute('notExecuted')
            results = @(foreach ($method in $Scenarios.Keys) { [ordered]@{ id = "$TestClass.$method"; status = 'PASS' } })
            trx = [ordered]@{ fileName = $TrxName; bytes = $proof.Bytes; sha256 = $proof.Sha256 }
        }
    }
}

function Write-AtomicEvidence {
    param([string] $Path, [string] $Json)

    $temporary = "$Path.$([System.Guid]::NewGuid().ToString('N')).tmp"
    $created = $false
    try {
        Assert-PlainParents -Directory ([System.IO.Path]::GetDirectoryName($Path))
        $stream = [System.IO.File]::Open($temporary, 'CreateNew', 'Write', 'None')
        $created = $true
        try {
            $bytes = $Utf8.GetBytes($Json)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        }
        finally { $stream.Dispose() }
        [void](Get-PlainFile -Path $temporary)
        if ($null -ne (Get-PlainFile -Path $Path -AllowMissing)) { throw 'P07_EVIDENCE_WRITER_COLLISION' }
        [System.IO.File]::Move($temporary, $Path, $false)
        $created = $false
    }
    finally {
        if ($created) {
            [void](Get-PlainFile -Path $temporary)
            [System.IO.File]::Delete($temporary) # Only our CreateNew file; never clean a directory.
        }
    }
}

$stage = 'paths'
$exeHold = $null
$sidecarHold = $null
$trxHold = $null
try {
    if (-not $IsWindows) { throw 'P07_UNSUPPORTED_HOST' }
    $repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $repositoryRoot = Resolve-SafePath -Path $repositoryRoot -Base $repositoryRoot
    $packageDirectory = Join-Path $repositoryRoot 'artifacts\package'
    $evidenceDirectory = if ($DevelopmentOnly) { Join-Path $repositoryRoot 'artifacts\test\singlefile' } else { $packageDirectory }
    $evidencePath = Join-Path $evidenceDirectory $EvidenceName
    # Invalidate ONLY the selected fixed proof, before host/input/source/results validation.
    # If the target is unsafe or cannot be removed, fail without following/replacing it.
    $oldEvidence = Get-PlainFile -Path $evidencePath -AllowMissing
    if ($null -ne $oldEvidence) { [System.IO.File]::Delete($evidencePath) }

    $stage = 'host'
    if ([System.Environment]::OSVersion.Version.Build -lt 22000 -or
        [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64 -or
        [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
        throw 'P07_UNSUPPORTED_HOST'
    }
    $gitPath = (Get-Command git.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $dotnetPath = (Get-Command dotnet.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $stage = 'source-before'
    $top = $Utf8.GetString((Invoke-ReadOnlyGit -Arguments @('rev-parse', '--show-toplevel')).Bytes).TrimEnd([char[]]"`r`n")
    if (-not [System.IO.Path]::GetFullPath($top).Equals($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'P07_SOURCE_ROOT'
    }
    Assert-IgnoredOutput -Path $evidencePath
    $sourceBefore = Get-SourceSnapshot
    if (-not $DevelopmentOnly -and $sourceBefore.EntryCount -ne 0) { throw 'P07_CLEAN_SOURCE_REQUIRED' }

    $stage = 'package-before'
    $exePath = Join-Path $packageDirectory $ExecutableName
    $sidecarPath = "$exePath.sha256"
    [void](Get-PlainFile -Path $exePath)
    [void](Get-PlainFile -Path $sidecarPath)
    # Deny input write/delete for the entire run. P06 mutates only its own copies.
    $exeHold = [System.IO.File]::Open($exePath, 'Open', 'Read', 'Read')
    $sidecarHold = [System.IO.File]::Open($sidecarPath, 'Open', 'Read', 'Read')
    $packageBefore = Get-FileProof -Path $exePath
    $sidecarBefore = Get-FileProof -Path $sidecarPath
    if ($packageBefore.Bytes -le 0) { throw 'P07_EMPTY_PACKAGE' }
    Assert-ExactSidecar -Path $sidecarPath -Hash $packageBefore.Sha256
    $productVersion = Get-ProductVersion
    Assert-PackageVersion -Path $exePath -Version $productVersion
    $sdkResult = Invoke-LocalProcess -Executable $dotnetPath -Arguments @('--version')
    $sdkVersion = $Utf8.GetString($sdkResult.Bytes).Trim()
    if ($sdkResult.ExitCode -ne 0 -or $sdkVersion -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z') { throw 'P07_DOTNET_SDK' }

    $stage = 'results-directory'
    if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) { $ResultsDirectory = 'TestResults\windows-singlefile' }
    $resultsParent = Resolve-SafePath -Path $ResultsDirectory -Base $repositoryRoot
    if (-not (Test-WithinRoot -Root (Join-Path $repositoryRoot 'TestResults') -Path $resultsParent) -and
        -not (Test-WithinRoot -Root (Join-Path $repositoryRoot 'artifacts\test') -Path $resultsParent)) { throw 'P07_RESULTS_OUTSIDE_OWNED_ROOTS' }
    $runDirectory = Join-Path $resultsParent ([System.Guid]::NewGuid().ToString('N'))
    Assert-PlainParents -Directory $runDirectory
    if (Test-Path -LiteralPath $runDirectory) { throw 'P07_RESULTS_NOT_FRESH' }
    $trxPath = Join-Path $runDirectory $TrxName
    Assert-IgnoredOutput -Path $trxPath
    [void][System.IO.Directory]::CreateDirectory($runDirectory)
    Assert-PlainParents -Directory $runDirectory
    if (@(Get-ChildItem -LiteralPath $runDirectory -Force).Count -ne 0) { throw 'P07_RESULTS_NOT_FRESH' }
    $testProject = Join-Path $repositoryRoot 'tests\StudyReportEvaluator.App.Tests\StudyReportEvaluator.App.Tests.csproj'
    [void](Get-PlainFile -Path $testProject)
    $filter = (@(foreach ($method in $Scenarios.Keys) { "FullyQualifiedName=$TestClass.$method" }) -join '|')
    $stage = 'p06-tests'
    $runStart = [System.DateTimeOffset]::UtcNow
    $testRun = Invoke-LocalProcess -Executable $dotnetPath -PackageTests -DiscardOutput -TimeoutMilliseconds 1200000 -Arguments @(
        'test', $testProject, '--configuration', 'Release', '--no-build', '--no-restore',
        '--filter', $filter, '--results-directory', $runDirectory,
        '--logger', "trx;LogFileName=$TrxName", '--logger', 'console;verbosity=quiet')
    $runFinish = [System.DateTimeOffset]::UtcNow

    $stage = 'p06-trx'
    [void](Get-PlainFile -Path $trxPath)
    $trxHold = [System.IO.File]::Open($trxPath, 'Open', 'Read', 'Read')
    $testProof = Read-P06Trx -Path $trxPath -Package $packageBefore -RunStart $runStart -RunFinish $runFinish
    if ($testRun.ExitCode -ne 0) { throw 'P07_TEST_PROCESS_FAILED' }
    $stage = 'inputs-after'
    $versionAfter = Get-ProductVersion
    $packageAfter = Get-FileProof -Path $exePath
    $sidecarAfter = Get-FileProof -Path $sidecarPath
    Assert-ExactSidecar -Path $sidecarPath -Hash $packageAfter.Sha256
    Assert-PackageVersion -Path $exePath -Version $versionAfter
    $inputsUnchanged = $versionAfter -ceq $productVersion -and
        $packageBefore.Bytes -eq $packageAfter.Bytes -and $packageBefore.Sha256 -ceq $packageAfter.Sha256 -and
        $sidecarBefore.Bytes -eq $sidecarAfter.Bytes -and $sidecarBefore.Sha256 -ceq $sidecarAfter.Sha256
    if (-not $inputsUnchanged) { throw 'P07_PACKAGE_OR_VERSION_CHANGED' }
    $stage = 'source-after'
    Assert-IgnoredOutput -Path $trxPath
    Assert-IgnoredOutput -Path $evidencePath
    $sourceAfter = Get-SourceSnapshot
    $sourceUnchanged = $sourceBefore.Commit -ceq $sourceAfter.Commit -and $sourceBefore.EntryCount -eq $sourceAfter.EntryCount -and
        $sourceBefore.StatusSha256 -ceq $sourceAfter.StatusSha256 -and $sourceBefore.ContentSha256 -ceq $sourceAfter.ContentSha256
    if (-not $sourceUnchanged) { throw 'P07_SOURCE_CHANGED' }
    if (-not $DevelopmentOnly -and $sourceAfter.EntryCount -ne 0) { throw 'P07_CLEAN_SOURCE_REQUIRED' }

    $allPassed = $testProof.Passed.Count -eq 7
    $payloadVerified = $allPassed -and $testProof.Observed.Count -eq 9
    $restartVerified = $allPassed -and @($Scenarios.Keys | Select-Object -First 4 | Where-Object {
        -not $testProof.Passed.Contains("$TestClass.$_")
    }).Count -eq 0
    # These fixed package versions were ASSERTED by P06/P02 on the extracted payload,
    # including runtimeconfig, deps, SDK/CLI binary versions and actual CLI hash.
    # They are not inferred from the observer's installed runtime or host DLLs.
    $evidence = [ordered]@{
        schemaVersion = 1
        evidenceKind = 'windows-singlefile-required'
        status = if ($DevelopmentOnly) { 'PASS_DEVELOPMENT' } else { 'PASS_REQUIRED' }
        sourceCommit = $sourceBefore.Commit
        sourceStatusEntryCount = $sourceBefore.EntryCount
        sourceStatusSha256 = $sourceBefore.StatusSha256
        sourceContentSha256 = $sourceBefore.ContentSha256
        productVersion = $productVersion
        host = [ordered]@{
            osName = 'Windows'
            osVersion = [System.Environment]::OSVersion.Version.ToString()
            osBuild = [System.Environment]::OSVersion.Version.Build
            osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        }
        package = [ordered]@{ fileName = $ExecutableName; bytes = $packageBefore.Bytes; sha256 = $packageBefore.Sha256 }
        runtime = [ordered]@{
            dotnetSdk = $sdkVersion
            dotnetRuntime = '10.0.11'
            copilotSdk = '1.0.11'
            cliVersion = '1.0.79'
            cliSha256 = $testProof.CliSha256
        }
        tests = $testProof.Tests
        checks = [ordered]@{
            singleFileVerified = $payloadVerified
            sidecarVerified = $inputsUnchanged
            safeLayoutVerified = $payloadVerified
            bundledCliVerified = $payloadVerified -and $testProof.CliSha256 -cmatch '\A[0-9A-F]{64}\z'
            documentationVerified = $payloadVerified
            guiLaunchVerified = $payloadVerified
            restartAndConcurrencyVerified = $restartVerified
            inputUnchangedVerified = $allPassed -and $inputsUnchanged -and $sourceUnchanged
        }
        limitations = @(
            'Development-host observations are not clean-host evidence.',
            'Network isolation, personal authentication and live AI were not run.',
            'Disk-full, interrupted extraction, directory ACL faults and cross-format checkpoint resume were not run.'
        )
    }
    if (@($evidence.checks.Values | Where-Object { $_ -isnot [bool] -or -not $_ }).Count -ne 0) { throw 'P07_CHECKS_INCOMPLETE' }
    $stage = 'evidence'
    Assert-PlainParents -Directory $evidenceDirectory
    [void][System.IO.Directory]::CreateDirectory($evidenceDirectory)
    $json = ($evidence | ConvertTo-Json -Depth 8) + "`n"
    Write-AtomicEvidence -Path $evidencePath -Json $json
    Write-Output $json
}
catch {
    $code = $_.Exception.Message
    if ($code -cnotmatch '\AP07_[A-Z0-9_]+\z') { $code = 'P07_IO_OR_FORMAT_ERROR' }
    [System.Console]::Error.WriteLine("P07_FAIL stage=$stage code=$code")
    exit 1
}
finally {
    foreach ($stream in @($trxHold, $sidecarHold, $exeHold)) {
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

exit 0