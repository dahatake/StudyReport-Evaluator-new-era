#Requires -Version 7.5
#Requires -PSEdition Core

<#
.SYNOPSIS
Creates a validated v2 release candidate record or final platform release matrix.
.DESCRIPTION
Candidate mode copies the two fixed Windows package kinds and their sidecars and
evidence into a private staging directory, writes only release-candidate-record.json,
and invokes the public C02 candidate CLI against those local bytes. PASS_CANDIDATE
is explicitly not publication eligibility and no clean-host result is generated.

Final mode receives a candidate record and independently supplied clean-host record.
It copies the fixed EXE/ZIP packages and package evidence. It projects the two
candidate rows into schemaVersion 2 and invokes the public C02 final CLI.

Both modes require an actual clean Git checkout before and after generation. Output
must be ignored when it is inside the checkout. Builders targeting the same output
are serialized. A sibling staging directory is validated first; an existing owned
output is replaced only after validation, then the committed files are held read-only
and validated again before PASS. A rollback rename is retained through that commit
point and the post-generation source check.
#>
[CmdletBinding(DefaultParameterSetName = 'Candidate')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Candidate')]
    [Parameter(Mandatory, ParameterSetName = 'Final')]
    [ValidateSet('Candidate', 'Final')]
    [string] $Mode,

    [Parameter(Mandatory, ParameterSetName = 'Candidate')]
    [Parameter(Mandatory, ParameterSetName = 'Final')]
    [ValidateNotNullOrEmpty()]
    [string] $ExpectedRepository,

    [Parameter(Mandatory, ParameterSetName = 'Candidate')]
    [Parameter(Mandatory, ParameterSetName = 'Final')]
    [ValidateNotNullOrEmpty()]
    [string] $CandidateRunId,

    [Parameter(ParameterSetName = 'Candidate')]
    [string] $SingleFileDirectory,

    [Parameter(ParameterSetName = 'Candidate')]
    [string] $ZipDirectory,

    [Parameter(Mandatory, ParameterSetName = 'Final')]
    [ValidateNotNullOrEmpty()]
    [string] $CandidateRecordPath,

    [Parameter(Mandatory, ParameterSetName = 'Final')]
    [ValidateNotNullOrEmpty()]
    [string] $CleanHostEvidencePath,

    [Parameter(ParameterSetName = 'Candidate')]
    [Parameter(ParameterSetName = 'Final')]
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Utf8NoBom = [System.Text.UTF8Encoding]::new($false, $true)
$MaximumArtifactBytes = 2GB
$MaximumJsonBytes = 1MB
$MaximumCleanHostBytes = 64KB
$MaximumSidecarBytes = 64KB
$CandidateFileName = 'release-candidate-record.json'
$CleanHostFileName = 'windows-singlefile-clean-host.evidence.json'
$MatrixFileName = 'platform-release-matrix.json'
$WorkflowPath = '.github/workflows/release.yml'
$FilesByKind = [ordered]@{
    'windows-singlefile-exe' = [ordered]@{
        Artifact = 'StudyReportEvaluator-win-x64.exe'
        Sidecar = 'StudyReportEvaluator-win-x64.exe.sha256'
        Evidence = 'StudyReportEvaluator-win-x64.exe.evidence.json'
    }
    'windows-zip' = [ordered]@{
        Artifact = 'StudyReportEvaluator-win-x64.zip'
        Sidecar = 'StudyReportEvaluator-win-x64.zip.sha256'
        Evidence = 'StudyReportEvaluator-win-x64.evidence.json'
    }
}

function Resolve-C03Path {
    param([string] $Path, [string] $Base)

    if ([string]::IsNullOrWhiteSpace($Path) -or
        ([System.IO.Path]::IsPathRooted($Path) -and -not [System.IO.Path]::IsPathFullyQualified($Path))) {
        throw 'C03_UNSAFE_PATH'
    }

    $root = [System.IO.Path]::GetPathRoot($Path)
    foreach ($segment in $Path.Substring($root.Length).Replace('/', '\').Split(
            '\', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        if ($segment -ceq '.') { continue }
        if ($segment -ceq '..' -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or
            $segment.Contains(':') -or $segment -match '~[0-9]+(?:\.|$)' -or
            $segment -match '^(CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)' -or
            $segment.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
            throw 'C03_UNSAFE_PATH'
        }
    }

    $full = [System.IO.Path]::TrimEndingDirectorySeparator(
        [System.IO.Path]::GetFullPath($Path, $Base))
    if ([System.IO.Path]::GetPathRoot($full) -notmatch '^[A-Za-z]:\\$') {
        throw 'C03_UNSAFE_PATH'
    }
    return $full
}

function Test-C03WithinRoot {
    param([string] $Root, [string] $Path)

    return $Path.Equals($Root, [System.StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($Root + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-C03PlainParents {
    param([string] $Directory)

    while (-not [string]::IsNullOrEmpty($Directory)) {
        $item = $null
        try { $item = Get-Item -LiteralPath $Directory -Force -ErrorAction Stop }
        catch [System.Management.Automation.ItemNotFoundException] { }
        if ($null -ne $item -and (-not $item.PSIsContainer -or
                ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'C03_UNSAFE_PARENT'
        }
        $Directory = [System.IO.Path]::GetDirectoryName($Directory)
    }
}

function Get-C03PlainDirectory {
    param([string] $Path)

    Assert-C03PlainParents -Directory $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw 'C03_MISSING_DIRECTORY'
    }
    $item = Get-Item -LiteralPath $Path -Force
    if (-not $item.PSIsContainer -or
        ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'C03_UNSAFE_DIRECTORY'
    }
    return $item
}

function Get-C03PlainFile {
    param([string] $Path, [long] $MaximumBytes)

    Assert-C03PlainParents -Directory ([System.IO.Path]::GetDirectoryName($Path))
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'C03_MISSING_FILE'
    }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or [string]$item.LinkType -ceq 'HardLink' -or
        ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'C03_UNSAFE_FILE'
    }
    if ($item.Length -le 0 -or $item.Length -gt $MaximumBytes) {
        throw 'C03_FILE_SIZE_LIMIT'
    }
    return $item
}

function Get-C03FileDescriptor {
    param([string] $Path, [long] $MaximumBytes)

    $item = Get-C03PlainFile -Path $Path -MaximumBytes $MaximumBytes
    $stream = [System.IO.File]::Open($item.FullName, 'Open', 'Read', 'Read')
    try {
        if ($stream.Length -ne $item.Length) { throw 'C03_FILE_CHANGED' }
        $hash = [System.Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData($stream))
        return [ordered]@{
            fileName = $item.Name
            bytes = $stream.Length
            sha256 = $hash
        }
    }
    finally { $stream.Dispose() }
}

function Copy-C03FixedFile {
    param(
        [string] $SourcePath,
        [string] $ExpectedFileName,
        [string] $DestinationDirectory,
        [long] $MaximumBytes
    )

    if ([System.IO.Path]::GetFileName($SourcePath) -cne $ExpectedFileName) {
        throw 'C03_FIXED_BASENAME'
    }
    $source = Get-C03PlainFile -Path $SourcePath -MaximumBytes $MaximumBytes
    $destinationPath = Join-Path $DestinationDirectory $ExpectedFileName
    if (Test-Path -LiteralPath $destinationPath) { throw 'C03_OUTPUT_COLLISION' }

    $sourceStream = [System.IO.File]::Open($source.FullName, 'Open', 'Read', 'Read')
    $output = $null
    $hasher = [System.Security.Cryptography.IncrementalHash]::CreateHash(
        [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        if ($sourceStream.Length -le 0 -or $sourceStream.Length -gt $MaximumBytes) {
            throw 'C03_FILE_SIZE_LIMIT'
        }
        $output = [System.IO.File]::Open($destinationPath, 'CreateNew', 'Write', 'None')
        $buffer = [byte[]]::new(65536)
        $copied = [long]0
        while (($read = $sourceStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $copied += $read
            if ($copied -gt $MaximumBytes -or $copied -gt $sourceStream.Length) {
                throw 'C03_FILE_SIZE_LIMIT'
            }
            $hasher.AppendData($buffer, 0, $read)
            $output.Write($buffer, 0, $read)
        }
        $output.Flush($true)
        $sourceHash = [System.Convert]::ToHexString($hasher.GetHashAndReset())
        if ($copied -ne $sourceStream.Length) { throw 'C03_FILE_CHANGED' }
    }
    finally {
        if ($null -ne $output) { $output.Dispose() }
        $hasher.Dispose()
        $sourceStream.Dispose()
    }

    $descriptor = Get-C03FileDescriptor -Path $destinationPath -MaximumBytes $MaximumBytes
    if ($descriptor.bytes -ne $copied -or $descriptor.sha256 -cne $sourceHash) {
        throw 'C03_COPY_MISMATCH'
    }
    return $descriptor
}

function Assert-C03JsonTokens {
    param([System.Text.Json.JsonElement] $Element)

    switch ($Element.ValueKind) {
        ([System.Text.Json.JsonValueKind]::Object) {
            $names = [System.Collections.Generic.HashSet[string]]::new(
                [System.StringComparer]::Ordinal)
            foreach ($property in $Element.EnumerateObject()) {
                if (-not $names.Add($property.Name)) { throw 'C03_JSON_DUPLICATE_PROPERTY' }
                Assert-C03JsonTokens -Element $property.Value
            }
        }
        ([System.Text.Json.JsonValueKind]::Array) {
            foreach ($item in $Element.EnumerateArray()) {
                Assert-C03JsonTokens -Element $item
            }
        }
    }
}

function Read-C03StrictJson {
    param([string] $Path, [int] $MaximumBytes)

    $item = Get-C03PlainFile -Path $Path -MaximumBytes $MaximumBytes
    $stream = [System.IO.File]::Open($item.FullName, 'Open', 'Read', 'Read')
    try {
        $bytes = [byte[]]::new([int]$stream.Length)
        $stream.ReadExactly($bytes, 0, $bytes.Length)
        if ($stream.ReadByte() -ne -1) { throw 'C03_FILE_CHANGED' }
    }
    finally { $stream.Dispose() }

    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw 'C03_UTF8'
    }
    try { $json = $Utf8NoBom.GetString($bytes) }
    catch { throw 'C03_UTF8' }

    $document = $null
    try {
        $document = [System.Text.Json.JsonDocument]::Parse($json)
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw 'C03_JSON_INVALID'
        }
        Assert-C03JsonTokens -Element $document.RootElement
    }
    catch {
        if ($_.Exception.Message -cmatch '\AC03_[A-Z0-9_]+\z') { throw }
        throw 'C03_JSON_INVALID'
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }

    try {
        return ConvertFrom-Json -InputObject $json -AsHashtable -DateKind String -Depth 64
    }
    catch { throw 'C03_JSON_INVALID' }
}

function Write-C03Json {
    param([string] $Path, [System.Collections.IDictionary] $Value)

    $json = ($Value | ConvertTo-Json -Depth 16) + "`n"
    $bytes = $Utf8NoBom.GetBytes($json)
    if ($bytes.Length -le 0 -or $bytes.Length -gt $MaximumJsonBytes) {
        throw 'C03_FILE_SIZE_LIMIT'
    }
    $stream = [System.IO.File]::Open($Path, 'CreateNew', 'Write', 'None')
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    }
    finally { $stream.Dispose() }
    [void](Get-C03PlainFile -Path $Path -MaximumBytes $MaximumJsonBytes)
}

function Invoke-C03Process {
    param(
        [string] $Executable,
        [string[]] $Arguments,
        [switch] $Git,
        [int] $TimeoutMilliseconds = 600000
    )

    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Executable
    $info.WorkingDirectory = $repositoryRoot
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = $Utf8NoBom
    $info.StandardErrorEncoding = $Utf8NoBom
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    if ($Git) {
        foreach ($name in @($info.Environment.Keys)) {
            if ($name.StartsWith('GIT_', [System.StringComparison]::OrdinalIgnoreCase)) {
                [void]$info.Environment.Remove($name)
            }
        }
        $info.Environment['GIT_OPTIONAL_LOCKS'] = '0'
        $info.Environment['GIT_TERMINAL_PROMPT'] = '0'
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    $started = $false
    try {
        $started = $process.Start()
        if (-not $started) { throw 'C03_CHILD_START' }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) { throw 'C03_CHILD_TIMEOUT' }
        $drain = [System.Threading.Tasks.Task]::WhenAll(
            [System.Threading.Tasks.Task[]]@($stdoutTask, $stderrTask))
        if (-not $drain.Wait(10000)) { throw 'C03_CHILD_OUTPUT_TIMEOUT' }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($Utf8NoBom.GetByteCount($stdout) -gt $MaximumJsonBytes -or
            $Utf8NoBom.GetByteCount($stderr) -gt $MaximumJsonBytes) {
            throw 'C03_CHILD_OUTPUT_LIMIT'
        }
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $stdout
            StandardError = $stderr
        }
    }
    finally {
        try {
            if ($started -and -not $process.HasExited) {
                $process.Kill($true)
                [void]$process.WaitForExit(10000)
            }
        }
        finally { $process.Dispose() }
    }
}

function Invoke-C03Git {
    param([string[]] $Arguments)

    return Invoke-C03Process -Executable $gitPath -Git -Arguments (
        @('--no-optional-locks', '-c', 'core.fsmonitor=false', '-c',
            'core.untrackedCache=false', '-C', $repositoryRoot) + $Arguments)
}

function Get-C03SourceState {
    $topResult = Invoke-C03Git -Arguments @('rev-parse', '--show-toplevel')
    if ($topResult.ExitCode -ne 0) { throw 'C03_GIT_FAILED' }
    $top = $topResult.StandardOutput.TrimEnd([char[]]"`r`n")
    if (-not [System.IO.Path]::GetFullPath($top).Equals(
            $repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'C03_SOURCE_ROOT'
    }

    $commitResult = Invoke-C03Git -Arguments @('rev-parse', '--verify', 'HEAD^{commit}')
    $commit = $commitResult.StandardOutput.Trim()
    if ($commitResult.ExitCode -ne 0 -or $commit -cnotmatch '\A[0-9a-f]{40}\z') {
        throw 'C03_SOURCE_COMMIT'
    }

    $statusResult = Invoke-C03Git -Arguments @(
        'status', '--porcelain=v1', '-z', '--untracked-files=all', '--ignore-submodules=none')
    if ($statusResult.ExitCode -ne 0) { throw 'C03_GIT_FAILED' }
    if ($statusResult.StandardOutput.Length -ne 0) { throw 'C03_SOURCE_NOT_CLEAN' }
    return [pscustomobject]@{ Commit = $commit }
}

function Assert-C03IgnoredOutput {
    param([string] $Path)

    if (-not (Test-C03WithinRoot -Root $repositoryRoot -Path $Path)) { return }
    $relative = [System.IO.Path]::GetRelativePath($repositoryRoot, $Path).Replace('\', '/')
    $result = Invoke-C03Git -Arguments @('check-ignore', '--quiet', '--', $relative)
    if ($result.ExitCode -ne 0) { throw 'C03_OUTPUT_NOT_IGNORED' }
}

function Get-C03ProductVersion {
    $command = '[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false, $true); ' +
        '$ErrorActionPreference = ''Stop''; try { $value = & $args[0] show -RepositoryRoot $args[1] -Json; ' +
        '$succeeded = $?; if (-not $succeeded) { exit 1 }; $value } catch { exit 1 }'
    $result = Invoke-C03Process -Executable $powerShellPath -Arguments @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-CommandWithArgs',
        $command, $versionTool, $repositoryRoot)
    if ($result.ExitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($result.StandardError)) {
        throw 'C03_VERSION_TOOL'
    }
    try {
        $value = ConvertFrom-Json -InputObject $result.StandardOutput -AsHashtable -DateKind String -Depth 8
    }
    catch { throw 'C03_VERSION_TOOL' }
    if ($value.Status -isnot [string] -or $value.Status -cne 'PASS' -or
        $value.Version -isnot [string] -or
        $value.Version -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') {
        throw 'C03_STABLE_VERSION_REQUIRED'
    }
    return [string]$value.Version
}

function Invoke-C03Validator {
    param(
        [ValidateSet('Candidate', 'Final')]
        [string] $ValidationMode,
        [string] $Directory,
        [string] $InputPath,
        [string] $ProductVersion,
        [string] $SourceCommit
    )

    # The child wrapper checks the script invocation's $? immediately. It does
    # not consult a stale LASTEXITCODE left by an unrelated native command.
    $command = '[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false, $true); ' +
        '$ErrorActionPreference = ''Stop''; try { $scriptPath = $args[0]; $parameters = @{}; ' +
        'for ($i = 1; $i -lt $args.Count; $i += 2) { $parameters.Add($args[$i], $args[$i + 1]) }; ' +
        '$value = & $scriptPath @parameters; $succeeded = $?; if (-not $succeeded) { exit 1 }; $value } catch { exit 1 }'
    $inputParameter = if ($ValidationMode -ceq 'Candidate') {
        'CandidateRecordPath'
    }
    else {
        'MatrixPath'
    }
    $arguments = @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-CommandWithArgs', $command,
        $validator,
        $inputParameter, $InputPath,
        'ArtifactDirectory', $Directory,
        'ExpectedProductVersion', $ProductVersion,
        'ExpectedSourceCommit', $SourceCommit,
        'ExpectedRepository', $ExpectedRepository,
        'ExpectedCandidateRunId', $CandidateRunId)
    $result = Invoke-C03Process -Executable $powerShellPath -Arguments $arguments
    if ($result.ExitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($result.StandardError)) {
        throw 'C03_C02_VALIDATION'
    }
    try {
        $value = ConvertFrom-Json -InputObject $result.StandardOutput -AsHashtable -DateKind String -Depth 16
    }
    catch { throw 'C03_C02_VALIDATION' }
    $expectedStatus = if ($ValidationMode -ceq 'Candidate') { 'PASS_CANDIDATE' } else { 'PASS' }
    if ($value.status -isnot [string] -or $value.status -cne $expectedStatus -or
        ($value.rowCount -isnot [long] -and $value.rowCount -isnot [int]) -or
        [long]$value.rowCount -ne 2 -or
        $value.productVersion -cne $ProductVersion -or
        $value.sourceCommit -cne $SourceCommit -or
        $value.candidate.repository -cne $ExpectedRepository -or
        $value.candidate.runId -cne $CandidateRunId) {
        throw 'C03_C02_VALIDATION'
    }
}

function ConvertTo-C03Verification {
    param([System.Collections.IDictionary] $Evidence)

    if ($Evidence.host -isnot [System.Collections.IDictionary]) {
        throw 'C03_EVIDENCE_SHAPE'
    }
    return [ordered]@{
        osName = $Evidence.host.osName
        osVersion = $Evidence.host.osVersion
        osBuild = $Evidence.host.osBuild
        osArchitecture = $Evidence.host.osArchitecture
        processArchitecture = $Evidence.host.processArchitecture
    }
}

function Assert-C03OwnedOutputDirectory {
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $directory = Get-C03PlainDirectory -Path $Path
    $allowed = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($files in $FilesByKind.Values) {
        foreach ($name in $files.Values) { [void]$allowed.Add([string]$name) }
    }
    foreach ($name in @($CandidateFileName, $CleanHostFileName, $MatrixFileName)) {
        [void]$allowed.Add($name)
    }

    $items = @(Get-ChildItem -LiteralPath $directory.FullName -Force)
    $hasOwnershipRecord = $items.Count -eq 0
    foreach ($item in $items) {
        if ($item.PSIsContainer -or -not $allowed.Contains($item.Name) -or
            [string]$item.LinkType -ceq 'HardLink' -or
            ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'C03_OUTPUT_NOT_OWNED'
        }
        if ($item.Name -cin @($CandidateFileName, $MatrixFileName)) {
            $hasOwnershipRecord = $true
        }
    }
    if (-not $hasOwnershipRecord) { throw 'C03_OUTPUT_NOT_OWNED' }
}

function Assert-C03ExactFileSet {
    param([string] $Directory, [string[]] $ExpectedNames)

    $expected = [System.Collections.Generic.HashSet[string]]::new(
        $ExpectedNames, [System.StringComparer]::OrdinalIgnoreCase)
    $actual = @(Get-ChildItem -LiteralPath $Directory -Force)
    if ($actual.Count -ne $expected.Count) { throw 'C03_OUTPUT_FILE_SET' }
    foreach ($item in $actual) {
        if ($item.PSIsContainer -or -not $expected.Contains($item.Name) -or
            [string]$item.LinkType -ceq 'HardLink' -or
            ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'C03_OUTPUT_FILE_SET'
        }
    }
}

function New-C03PrivateDirectory {
    param([string] $OutputRoot, [string] $Purpose)

    $parent = [System.IO.Path]::GetDirectoryName($OutputRoot)
    [void](Get-C03PlainDirectory -Path $parent)
    $leaf = [System.IO.Path]::GetFileName($OutputRoot)
    if ([string]::IsNullOrWhiteSpace($leaf)) { throw 'C03_UNSAFE_PATH' }
    $path = Join-Path $parent ".$leaf.c03-$Purpose-$([System.Guid]::NewGuid().ToString('N')).tmp"
    if (Test-Path -LiteralPath $path) { throw 'C03_OUTPUT_COLLISION' }
    Assert-C03IgnoredOutput -Path $path
    [void][System.IO.Directory]::CreateDirectory($path)
    [void](Get-C03PlainDirectory -Path $path)
    return $path
}

function Remove-C03PrivateDirectory {
    param([AllowNull()] [string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return }
    [void](Get-C03PlainDirectory -Path $Path)
    [System.IO.Directory]::Delete($Path, $true)
}

function New-C03OutputMutex {
    param([string] $OutputRoot)

    $normalized = $OutputRoot.ToUpperInvariant()
    $nameHash = [System.Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.Text.Encoding]::UTF8.GetBytes($normalized)))
    return [System.Threading.Mutex]::new(
        $false,
        "Local\StudyReportEvaluator.C03.$nameHash")
}

function Open-C03OutputSnapshot {
    param([string] $Directory, [string[]] $ExpectedNames)

    $holds = [System.Collections.Generic.List[System.IO.Stream]]::new()
    try {
        Assert-C03ExactFileSet -Directory $Directory -ExpectedNames $ExpectedNames
        foreach ($name in $ExpectedNames) {
            $path = Join-Path $Directory $name
            $item = Get-C03PlainFile -Path $path -MaximumBytes $MaximumArtifactBytes
            $stream = [System.IO.File]::Open(
                $item.FullName,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::Read)
            if ($stream.Length -ne $item.Length) {
                $stream.Dispose()
                throw 'C03_FILE_CHANGED'
            }
            $holds.Add($stream)
        }
        return $holds
    }
    catch {
        foreach ($stream in $holds) { $stream.Dispose() }
        throw
    }
}

function Copy-C03Kind {
    param(
        [string] $Kind,
        [string] $SourceDirectory,
        [string] $DestinationDirectory
    )

    $files = $FilesByKind[$Kind]
    $result = [ordered]@{}
    $result.Artifact = Copy-C03FixedFile `
        -SourcePath (Join-Path $SourceDirectory $files.Artifact) `
        -ExpectedFileName $files.Artifact `
        -DestinationDirectory $DestinationDirectory `
        -MaximumBytes $MaximumArtifactBytes
    $result.Sidecar = Copy-C03FixedFile `
        -SourcePath (Join-Path $SourceDirectory $files.Sidecar) `
        -ExpectedFileName $files.Sidecar `
        -DestinationDirectory $DestinationDirectory `
        -MaximumBytes $MaximumSidecarBytes
    $result.Evidence = Copy-C03FixedFile `
        -SourcePath (Join-Path $SourceDirectory $files.Evidence) `
        -ExpectedFileName $files.Evidence `
        -DestinationDirectory $DestinationDirectory `
        -MaximumBytes $MaximumJsonBytes
    return $result
}

if ($PSVersionTable.PSEdition -cne 'Core' -or
    $PSVersionTable.PSVersion -lt [System.Version]'7.5') {
    throw 'PowerShell Core 7.5 or later is required.'
}

$stage = 'parameters'
$stagingRoot = $null
$backupRoot = $null
$outputMutex = $null
$outputMutexHeld = $false
$outputInstalled = $false
$backupMoved = $false
$commitSucceeded = $false
$installedStagingPath = $null
try {
    if (-not $IsWindows) { throw 'C03_UNSUPPORTED_HOST' }
    if ($Mode -cne $PSCmdlet.ParameterSetName) { throw 'C03_MODE_PARAMETERS' }
    if ($ExpectedRepository -cnotmatch '\A[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?/[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9_-])?\z' -or
        $ExpectedRepository.Length -gt 200 -or
        $CandidateRunId -cnotmatch '\A[1-9][0-9]*\z' -or
        $CandidateRunId.Length -gt 20) {
        throw 'C03_EXPECTED_IDENTITY'
    }

    $repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $repositoryRoot = Resolve-C03Path -Path $repositoryRoot -Base $repositoryRoot
    [void](Get-C03PlainDirectory -Path $repositoryRoot)
    $validator = Join-Path $repositoryRoot 'scripts\validate-platform-release-matrix.ps1'
    $versionTool = Join-Path $repositoryRoot 'dev\version.ps1'
    [void](Get-C03PlainFile -Path $validator -MaximumBytes $MaximumJsonBytes)
    [void](Get-C03PlainFile -Path $versionTool -MaximumBytes $MaximumJsonBytes)
    $powerShellPath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    [void](Get-C03PlainFile -Path $powerShellPath -MaximumBytes $MaximumArtifactBytes)
    $gitCommand = Get-Command git.exe -CommandType Application -ErrorAction Stop |
        Select-Object -First 1
    if ($null -eq $gitCommand -or [string]::IsNullOrWhiteSpace([string]$gitCommand.Source)) {
        throw 'C03_GIT_FAILED'
    }
    $gitPath = [string]$gitCommand.Source

    $stage = 'source-before'
    $sourceBefore = Get-C03SourceState
    $productVersion = Get-C03ProductVersion

    $outputValue = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        'artifacts\package\matrix'
    }
    else { $OutputDirectory }
    $outputRoot = Resolve-C03Path -Path $outputValue -Base $repositoryRoot
    if ($outputRoot -ceq [System.IO.Path]::GetPathRoot($outputRoot) -or
        (Test-C03WithinRoot -Root $outputRoot -Path $repositoryRoot)) {
        throw 'C03_UNSAFE_OUTPUT'
    }
    [void](Get-C03PlainDirectory -Path ([System.IO.Path]::GetDirectoryName($outputRoot)))
    Assert-C03IgnoredOutput -Path $outputRoot

    $stage = 'output-lock'
    $outputMutex = New-C03OutputMutex -OutputRoot $outputRoot
    try {
        $outputMutexHeld = $outputMutex.WaitOne([System.TimeSpan]::FromMinutes(10))
    }
    catch [System.Threading.AbandonedMutexException] {
        $outputMutexHeld = $true
    }
    if (-not $outputMutexHeld) { throw 'C03_OUTPUT_LOCK_TIMEOUT' }
    Assert-C03OwnedOutputDirectory -Path $outputRoot

    $stage = 'staging'
    $stagingRoot = New-C03PrivateDirectory -OutputRoot $outputRoot -Purpose 'stage'
    if ($Mode -ceq 'Candidate') {
        $singleFileValue = if ([string]::IsNullOrWhiteSpace($SingleFileDirectory)) {
            'artifacts\package'
        }
        else { $SingleFileDirectory }
        $zipValue = if ([string]::IsNullOrWhiteSpace($ZipDirectory)) {
            'artifacts\package'
        }
        else { $ZipDirectory }
        $singleFileRoot = Resolve-C03Path -Path $singleFileValue -Base $repositoryRoot
        $zipRoot = Resolve-C03Path -Path $zipValue -Base $repositoryRoot
        foreach ($root in @($singleFileRoot, $zipRoot)) {
            [void](Get-C03PlainDirectory -Path $root)
            if (Test-C03WithinRoot -Root $outputRoot -Path $root) {
                throw 'C03_UNSAFE_OUTPUT'
            }
        }

        $stage = 'candidate-inputs'
        $descriptors = [ordered]@{
            'windows-singlefile-exe' = Copy-C03Kind `
                -Kind 'windows-singlefile-exe' `
                -SourceDirectory $singleFileRoot `
                -DestinationDirectory $stagingRoot
            'windows-zip' = Copy-C03Kind `
                -Kind 'windows-zip' `
                -SourceDirectory $zipRoot `
                -DestinationDirectory $stagingRoot
        }
        $singleFileEvidence = Read-C03StrictJson `
            -Path (Join-Path $stagingRoot $FilesByKind['windows-singlefile-exe'].Evidence) `
            -MaximumBytes $MaximumJsonBytes
        $zipEvidence = Read-C03StrictJson `
            -Path (Join-Path $stagingRoot $FilesByKind['windows-zip'].Evidence) `
            -MaximumBytes $MaximumJsonBytes
        $singleFileVerification = ConvertTo-C03Verification -Evidence $singleFileEvidence
        $zipVerification = ConvertTo-C03Verification -Evidence $zipEvidence
        $identity = [ordered]@{
            repository = $ExpectedRepository
            workflow = $WorkflowPath
            runId = $CandidateRunId
        }
        $candidate = [ordered]@{
            schemaVersion = 1
            evidenceKind = 'windows-release-candidate'
            productVersion = $productVersion
            sourceCommit = $sourceBefore.Commit
            candidate = $identity
            requiredTests = 'PASS'
            rows = @(
                [ordered]@{
                    artifactKind = 'windows-singlefile-exe'
                    verification = $singleFileVerification
                    artifact = $descriptors['windows-singlefile-exe'].Artifact
                    sidecar = $descriptors['windows-singlefile-exe'].Sidecar
                    evidence = $descriptors['windows-singlefile-exe'].Evidence
                },
                [ordered]@{
                    artifactKind = 'windows-zip'
                    verification = $zipVerification
                    artifact = $descriptors['windows-zip'].Artifact
                    sidecar = $descriptors['windows-zip'].Sidecar
                    evidence = $descriptors['windows-zip'].Evidence
                }
            )
        }
        $candidatePath = Join-Path $stagingRoot $CandidateFileName
        Write-C03Json -Path $candidatePath -Value $candidate
        $stage = 'candidate-validation'
        Invoke-C03Validator `
            -ValidationMode Candidate `
            -Directory $stagingRoot `
            -InputPath $candidatePath `
            -ProductVersion $productVersion `
            -SourceCommit $sourceBefore.Commit
        $expectedOutputFiles = @(
            $FilesByKind['windows-singlefile-exe'].Values
            $FilesByKind['windows-zip'].Values
            $CandidateFileName)
    }
    else {
        $stage = 'final-input-paths'
        $candidatePath = Resolve-C03Path -Path $CandidateRecordPath -Base $repositoryRoot
        $cleanHostPath = Resolve-C03Path -Path $CleanHostEvidencePath -Base $repositoryRoot
        if ([System.IO.Path]::GetFileName($candidatePath) -cne $CandidateFileName -or
            [System.IO.Path]::GetFileName($cleanHostPath) -cne $CleanHostFileName) {
            throw 'C03_FIXED_BASENAME'
        }
        $candidateRoot = [System.IO.Path]::GetDirectoryName($candidatePath)
        [void](Get-C03PlainDirectory -Path $candidateRoot)

        $stage = 'final-inputs'
        $candidateDescriptor = Copy-C03FixedFile `
            -SourcePath $candidatePath `
            -ExpectedFileName $CandidateFileName `
            -DestinationDirectory $stagingRoot `
            -MaximumBytes $MaximumJsonBytes
        $cleanHostDescriptor = Copy-C03FixedFile `
            -SourcePath $cleanHostPath `
            -ExpectedFileName $CleanHostFileName `
            -DestinationDirectory $stagingRoot `
            -MaximumBytes $MaximumCleanHostBytes
        [void](Copy-C03Kind `
                -Kind 'windows-singlefile-exe' `
                -SourceDirectory $candidateRoot `
                -DestinationDirectory $stagingRoot)
        [void](Copy-C03Kind `
                -Kind 'windows-zip' `
                -SourceDirectory $candidateRoot `
                -DestinationDirectory $stagingRoot)
        $candidate = Read-C03StrictJson `
            -Path (Join-Path $stagingRoot $CandidateFileName) `
            -MaximumBytes $MaximumJsonBytes
        [void](Read-C03StrictJson `
                -Path (Join-Path $stagingRoot $CleanHostFileName) `
                -MaximumBytes $MaximumCleanHostBytes)
        foreach ($kind in $FilesByKind.Keys) {
            [void](Read-C03StrictJson `
                    -Path (Join-Path $stagingRoot $FilesByKind[$kind].Evidence) `
                    -MaximumBytes $MaximumJsonBytes)
        }

        $candidateRows = [System.Collections.Generic.Dictionary[string,object]]::new(
            [System.StringComparer]::Ordinal)
        try {
            foreach ($row in @($candidate.rows)) {
                if ($row -isnot [System.Collections.IDictionary] -or
                    $row.artifactKind -isnot [string]) {
                    throw 'C03_CANDIDATE_SHAPE'
                }
                $candidateRows.Add([string]$row.artifactKind, $row)
            }
            foreach ($kind in $FilesByKind.Keys) {
                if (-not $candidateRows.ContainsKey($kind)) { throw 'C03_CANDIDATE_SHAPE' }
            }
        }
        catch {
            if ($_.Exception.Message -ceq 'C03_CANDIDATE_SHAPE') { throw }
            throw 'C03_CANDIDATE_SHAPE'
        }

        $rows = @()
        foreach ($kind in $FilesByKind.Keys) {
            $row = $candidateRows[$kind]
            $rows += [ordered]@{
                artifactKind = $kind
                platform = 'windows'
                runtimeIdentifier = 'win-x64'
                publish = $true
                status = 'PASS_REQUIRED'
                verification = $row.verification
                artifact = $row.artifact
                sidecar = $row.sidecar
                evidence = $row.evidence
            }
        }
        $matrix = [ordered]@{
            schemaVersion = 2
            productVersion = $productVersion
            sourceCommit = $sourceBefore.Commit
            candidate = [ordered]@{
                repository = $ExpectedRepository
                workflow = $WorkflowPath
                runId = $CandidateRunId
            }
            candidateRecord = $candidateDescriptor
            cleanHostEvidence = $cleanHostDescriptor
            rows = $rows
        }
        $matrixPath = Join-Path $stagingRoot $MatrixFileName
        Write-C03Json -Path $matrixPath -Value $matrix
        $stage = 'final-validation'
        Invoke-C03Validator `
            -ValidationMode Final `
            -Directory $stagingRoot `
            -InputPath $matrixPath `
            -ProductVersion $productVersion `
            -SourceCommit $sourceBefore.Commit
        $expectedOutputFiles = @(
            $CandidateFileName
            $CleanHostFileName
            $FilesByKind['windows-singlefile-exe'].Values
            $FilesByKind['windows-zip'].Values
            $MatrixFileName)
    }

    $stage = 'staged-output'
    Assert-C03ExactFileSet -Directory $stagingRoot -ExpectedNames $expectedOutputFiles
    $sourceAfterValidation = Get-C03SourceState
    $versionAfterValidation = Get-C03ProductVersion
    if ($sourceAfterValidation.Commit -cne $sourceBefore.Commit -or
        $versionAfterValidation -cne $productVersion) {
        throw 'C03_SOURCE_CHANGED'
    }

    $stage = 'output-commit'
    $hadExistingOutput = Test-Path -LiteralPath $outputRoot
    if ($hadExistingOutput) {
        Assert-C03OwnedOutputDirectory -Path $outputRoot
        $backupRoot = New-C03PrivateDirectory -OutputRoot $outputRoot -Purpose 'backup'
        [System.IO.Directory]::Delete($backupRoot, $false)
        [System.IO.Directory]::Move($outputRoot, $backupRoot)
        $backupMoved = $true
    }
    try {
        $installedStagingPath = $stagingRoot
        [System.IO.Directory]::Move($stagingRoot, $outputRoot)
        $outputInstalled = $true
        $stagingRoot = $null

        $stage = 'committed-validation'
        $outputHolds = Open-C03OutputSnapshot `
            -Directory $outputRoot `
            -ExpectedNames $expectedOutputFiles
        try {
            $committedInputPath = if ($Mode -ceq 'Candidate') {
                Join-Path $outputRoot $CandidateFileName
            }
            else {
                Join-Path $outputRoot $MatrixFileName
            }
            Invoke-C03Validator `
                -ValidationMode $Mode `
                -Directory $outputRoot `
                -InputPath $committedInputPath `
                -ProductVersion $productVersion `
                -SourceCommit $sourceBefore.Commit

            $stage = 'source-after'
            $sourceAfter = Get-C03SourceState
            $versionAfter = Get-C03ProductVersion
            if ($sourceAfter.Commit -cne $sourceBefore.Commit -or
                $versionAfter -cne $productVersion) {
                throw 'C03_SOURCE_CHANGED'
            }
            Assert-C03ExactFileSet -Directory $outputRoot -ExpectedNames $expectedOutputFiles
        }
        finally {
            foreach ($stream in $outputHolds) { $stream.Dispose() }
        }
        $commitSucceeded = $true
    }
    catch {
        $commitFailure = $_
        try {
            if ($outputInstalled -and (Test-Path -LiteralPath $outputRoot)) {
                [System.IO.Directory]::Move($outputRoot, $installedStagingPath)
                $stagingRoot = $installedStagingPath
                $outputInstalled = $false
            }
            if ($backupMoved -and -not (Test-Path -LiteralPath $outputRoot) -and
                $null -ne $backupRoot -and (Test-Path -LiteralPath $backupRoot)) {
                [System.IO.Directory]::Move($backupRoot, $outputRoot)
                $backupMoved = $false
                $backupRoot = $null
            }
        }
        catch { throw 'C03_OUTPUT_ROLLBACK' }
        throw $commitFailure
    }

    if ($commitSucceeded -and $backupMoved -and $null -ne $backupRoot) {
        $stage = 'backup-cleanup'
        Assert-C03OwnedOutputDirectory -Path $backupRoot
        try {
            [System.IO.Directory]::Delete($backupRoot, $true)
            $backupMoved = $false
            $backupRoot = $null
        }
        catch {
            # The validated output has crossed the commit point. A stale private
            # backup is safer than reporting failure while leaving new output.
        }
    }

    if ($Mode -ceq 'Candidate') {
        Write-Output 'C03_PASS status=PASS_CANDIDATE publicationEligible=false'
        Write-Output 'PASS_CANDIDATE is not publication eligible; clean-host evidence and a final matrix were not generated.'
    }
    else {
        Write-Output 'C03_PASS status=PASS upstreamCandidateRunVerificationRequired=true'
    }
}
catch {
    $code = $_.Exception.Message
    if ($code -cnotmatch '\AC03_[A-Z0-9_]+\z') { $code = 'C03_IO_OR_FORMAT' }
    [System.Console]::Error.WriteLine("C03_FAIL stage=$stage code=$code")
    exit 1
}
finally {
    try { Remove-C03PrivateDirectory -Path $stagingRoot }
    catch { }
    if ($null -ne $backupRoot -and (Test-Path -LiteralPath $backupRoot)) {
        # A backup is only left after an interrupted rename. Do not delete it:
        # preserving the prior validated output is safer than guessing recovery.
    }
    if ($outputMutexHeld -and $null -ne $outputMutex) {
        try { $outputMutex.ReleaseMutex() }
        catch { }
    }
    if ($null -ne $outputMutex) { $outputMutex.Dispose() }
}

exit 0
