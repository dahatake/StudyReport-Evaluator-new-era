#Requires -Version 7.4
#Requires -PSEdition Core

<#
.SYNOPSIS
Read-only v2 final-matrix or pre-clean-host candidate validation.
.DESCRIPTION
Use the latest pwsh on PATH with -NoLogo -NoProfile. MatrixPath and
CandidateRecordPath are mutually exclusive. V2/candidate requires explicit
ExpectedRepository and ExpectedCandidateRunId; neither is inferred from JSON.
Product version/source defaults remain the repository version tool and Git HEAD.

CandidateRecordPath must be ArtifactDirectory/release-candidate-record.json.
Candidate validation requires local EXE and ZIP bytes, their sidecars and package
evidence; it never reads or invents clean-host evidence. Final v2 additionally
binds the two fixed root descriptors candidateRecord and cleanHostEvidence.

C01's complete draft-2020-12 $defs validate each separate document. Control JSON
is limited to 1 MiB, clean-host JSON to 64 KiB, with strict UTF-8/no BOM, duplicate
member rejection and DateKind String (v2 requires PowerShell 7.5+). All v2 input
files and ancestors must be plain, non-hardlinked paths. Read handles deny writes
and deletes until validation completes. No file is created, changed or cleaned.

Success is one JSON object: schemaVersion=2, productVersion, sourceCommit,
candidate, candidateRecord, cleanHostEvidence (null for candidate), runtime,
rowCount=2, publishableAssets (the fixed EXE/ZIP and two sidecars), status,
cleanHostVerification, candidateRunVerification,
exeVersionVerification and limitations. PASS_CANDIDATE is NOT publication PASS.
Final PASS reports CANDIDATE_RECORD_ONLY and HUMAN_RECORDED, not execution proof.
The upstream workflow must independently verify the successful trusted
.github/workflows/release.yml run and its tag/commit. TRX/observation descriptors
are upstream/human records, not local raw-file requirements or public assets.
EXE internal version/layout rely on the P07 record bound to the actual EXE hash;
this validator does not independently inspect PE resources or execute binaries.
V2 errors are fixed C02 codes; received JSON, paths and exception bodies are not
printed. Retired schema-1 matrices are rejected with C02_SCHEMA_VERSION.
#>
[CmdletBinding(DefaultParameterSetName = 'Matrix')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Matrix')]
    [string] $MatrixPath,

    [Parameter(Mandatory, ParameterSetName = 'Candidate')]
    [string] $CandidateRecordPath,

    [Parameter(Mandatory)]
    [string] $ArtifactDirectory,

    [string] $ExpectedProductVersion,

    [string] $ExpectedSourceCommit,

    [string] $ExpectedRepository,

    [string] $ExpectedCandidateRunId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Utf8NoBom = [System.Text.UTF8Encoding]::new($false, $true)
$EmptyUtf8Sha256 = 'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855'

function Resolve-RepositoryRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    if ([System.IO.Path]::IsPathFullyQualified($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath($Path, $RepositoryRoot)
}

function Assert-NotReparsePoint {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileSystemInfo] $Item,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if (($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Description cannot be a reparse point."
    }
}

function Test-ByteArrayEqual {
    param(
        [Parameter(Mandatory)]
        [byte[]] $Left,

        [Parameter(Mandatory)]
        [byte[]] $Right
    )

    if ($Left.Length -ne $Right.Length) {
        return $false
    }

    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) {
            return $false
        }
    }

    return $true
}

function Assert-ExactPropertySet {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary] $Value,

        [Parameter(Mandatory)]
        [string[]] $ExpectedNames,

        [Parameter(Mandatory)]
        [string] $Description
    )

    # Bypass PowerShell's dictionary adapter so an untrusted JSON property
    # named "Keys" cannot replace the dictionary's real key collection.
    $actualNames = @($Value.psbase.Keys | ForEach-Object { [string]$_ })
    if ($actualNames.Count -ne $ExpectedNames.Count -or
        @($actualNames | Where-Object { $_ -cnotin $ExpectedNames }).Count -ne 0 -or
        @($ExpectedNames | Where-Object { $_ -cnotin $actualNames }).Count -ne 0) {
        throw "$Description property set is not closed."
    }
}

function Test-IsJsonInteger {
    param([AllowNull()] $Value)

    return $Value -is [byte] -or
        $Value -is [sbyte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int32] -or
        $Value -is [uint32] -or
        $Value -is [int64] -or
        $Value -is [uint64]
}

function Assert-ZipEvidenceBinding {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary] $Evidence,

        [Parameter(Mandatory)]
        [System.Collections.IDictionary] $Matrix,

        [Parameter(Mandatory)]
        [System.Collections.IDictionary] $Row
    )

    Assert-ExactPropertySet `
        -Value $Evidence `
        -ExpectedNames @(
            'schemaVersion',
            'evidenceKind',
            'status',
            'sourceCommit',
            'sourceStatusEntryCount',
            'sourceStatusSha256',
            'productVersion',
            'host',
            'package',
            'checks') `
        -Description 'Windows ZIP evidence'
    Assert-ExactPropertySet `
        -Value $Evidence.host `
        -ExpectedNames @(
            'osName',
            'osVersion',
            'osBuild',
            'osArchitecture',
            'processArchitecture') `
        -Description 'Windows ZIP evidence host'
    Assert-ExactPropertySet `
        -Value $Evidence.package `
        -ExpectedNames @('fileName', 'bytes', 'sha256') `
        -Description 'Windows ZIP evidence package'
    $requiredChecks = @(
        'sidecarVerified',
        'safeLayoutVerified',
        'bundledCliVerified',
        'cleanExtractVerified',
        'apphostLaunchVerified',
        'externalRuntimeAbsentVerified',
        'userWorkbookExcluded',
        'inputUnchangedVerified')
    Assert-ExactPropertySet `
        -Value $Evidence.checks `
        -ExpectedNames $requiredChecks `
        -Description 'Windows ZIP evidence checks'

    $checksPass = @($requiredChecks | Where-Object {
            $Evidence.checks[$_] -isnot [bool] -or
            -not [bool]$Evidence.checks[$_]
        }).Count -eq 0
    if (-not (Test-IsJsonInteger $Evidence.schemaVersion) -or
        [int]$Evidence.schemaVersion -ne 1 -or
        [string]$Evidence.evidenceKind -cne 'windows-zip-required' -or
        [string]$Evidence.status -cne 'PASS_REQUIRED' -or
        [string]$Evidence.sourceCommit -cne [string]$Matrix.sourceCommit -or
        -not (Test-IsJsonInteger $Evidence.sourceStatusEntryCount) -or
        [int]$Evidence.sourceStatusEntryCount -ne 0 -or
        [string]$Evidence.sourceStatusSha256 -cne $EmptyUtf8Sha256 -or
        [string]$Evidence.productVersion -cne [string]$Matrix.productVersion -or
        [string]$Evidence.host.osName -cne [string]$Row.verification.osName -or
        [string]$Evidence.host.osVersion -cne [string]$Row.verification.osVersion -or
        -not (Test-IsJsonInteger $Evidence.host.osBuild) -or
        [int]$Evidence.host.osBuild -ne [int]$Row.verification.osBuild -or
        [string]$Evidence.host.osArchitecture -cne [string]$Row.verification.osArchitecture -or
        [string]$Evidence.host.processArchitecture -cne [string]$Row.verification.processArchitecture -or
        [string]$Evidence.package.fileName -cne [string]$Row.artifact.fileName -or
        -not (Test-IsJsonInteger $Evidence.package.bytes) -or
        [long]$Evidence.package.bytes -ne [long]$Row.artifact.bytes -or
        [string]$Evidence.package.sha256 -cne [string]$Row.artifact.sha256 -or
        -not $checksPass) {
        throw 'Windows ZIP evidence is not bound to the matrix, package, environment, and required checks.'
    }
}

function Resolve-C02Path {
    param([string] $Path, [string] $Base)

    if ([string]::IsNullOrWhiteSpace($Path) -or
        ([System.IO.Path]::IsPathRooted($Path) -and -not [System.IO.Path]::IsPathFullyQualified($Path))) {
        throw 'C02_UNSAFE_PATH'
    }
    $root = [System.IO.Path]::GetPathRoot($Path)
    foreach ($segment in $Path.Substring($root.Length).Replace('\', '/').Split('/')) {
        if ($segment -ceq '' -or $segment -ceq '.') { continue }
        if ($segment -ceq '..' -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or
            $segment.Contains(':') -or $segment -match '~[0-9]+(?:\.|$)' -or
            $segment -match '^(CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)' -or
            $segment.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
            throw 'C02_UNSAFE_PATH'
        }
    }
    $full = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($Path, $Base))
    # Local filesystem only: no UNC/device namespace or network retrieval.
    if ($IsWindows -and [System.IO.Path]::GetPathRoot($full) -notmatch '^[A-Za-z]:\\$') {
        throw 'C02_UNSAFE_PATH'
    }
    return $full
}

function Assert-C02PlainDirectory {
    param([string] $Path)

    while (-not [string]::IsNullOrEmpty($Path)) {
        $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
        if (-not $item.PSIsContainer -or
            ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'C02_UNSAFE_PARENT'
        }
        $Path = [System.IO.Path]::GetDirectoryName($Path)
    }
}

function Open-C02File {
    param(
        [string] $Path,
        [System.Collections.Generic.List[System.IO.Stream]] $Holds,
        [long] $MaximumBytes = 9007199254740991
    )

    Assert-C02PlainDirectory -Path ([System.IO.Path]::GetDirectoryName($Path))
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'C02_MISSING_FILE' }
    $item = Get-Item -LiteralPath $Path -Force
    if ([string]$item.LinkType -ceq 'HardLink' -or
        ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'C02_UNSAFE_FILE'
    }
    $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'Read')
    $Holds.Add($stream)
    if ($stream.Length -le 0 -or $stream.Length -gt $MaximumBytes) { throw 'C02_FILE_SIZE_LIMIT' }
    $hash = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream))
    $stream.Position = 0
    return [pscustomobject]@{
        Stream = $stream
        Descriptor = [ordered]@{ fileName = $item.Name; bytes = $stream.Length; sha256 = $hash }
    }
}

function Read-C02BoundedBytes {
    param([System.IO.Stream] $Stream, [long] $Length, [int] $MaximumBytes = 1MB)

    if ($Length -le 0 -or $Length -gt $MaximumBytes) { throw 'C02_FILE_SIZE_LIMIT' }
    $bytes = [byte[]]::new([int]$Length)
    $Stream.ReadExactly($bytes, 0, $bytes.Length)
    if ($Stream.ReadByte() -ne -1) { throw 'C02_FILE_SIZE_LIMIT' }
    return ,$bytes
}

function Assert-C02JsonTokens {
    param([System.Text.Json.JsonElement] $Element)

    switch ($Element.ValueKind) {
        ([System.Text.Json.JsonValueKind]::Object) {
            $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            foreach ($property in $Element.EnumerateObject()) {
                if (-not $names.Add($property.Name)) { throw 'C02_JSON_DUPLICATE_PROPERTY' }
                Assert-C02JsonTokens -Element $property.Value
            }
        }
        ([System.Text.Json.JsonValueKind]::Array) {
            foreach ($item in $Element.EnumerateArray()) { Assert-C02JsonTokens -Element $item }
        }
        ([System.Text.Json.JsonValueKind]::Number) {
            # All numbers in these contracts are integers. Do not round floats,
            # exponents or oversized numbers into a passing counter/descriptor.
            $integer = [long]0
            if (-not $Element.TryGetInt64([ref]$integer)) { throw 'C02_JSON_INTEGER' }
        }
    }
}

function Get-C02JsonText {
    param([byte[]] $Bytes)

    if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF) {
        throw 'C02_UTF8'
    }
    try { $json = $Utf8NoBom.GetString($Bytes) }
    catch { throw 'C02_UTF8' }
    try { $document = [System.Text.Json.JsonDocument]::Parse($json) }
    catch { throw 'C02_JSON_INVALID' }
    try {
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw 'C02_JSON_INVALID'
        }
        Assert-C02JsonTokens -Element $document.RootElement
    }
    finally { $document.Dispose() }
    return $json
}

function ConvertFrom-C02Json {
    param([string] $Json, [string] $Schema, [string] $SchemaError = 'C02_SCHEMA_INVALID')

    if (-not [string]::IsNullOrEmpty($Schema) -and
        -not (Test-Json -Json $Json -Schema $Schema -ErrorAction SilentlyContinue -WarningAction SilentlyContinue)) {
        throw $SchemaError
    }
    # Get-C02JsonText must precede this call; ConvertFrom-Json alone accepts
    # comments/duplicate properties and can silently convert calendar strings.
    return ConvertFrom-Json -InputObject $Json -AsHashtable -DateKind String -Depth 64
}

function Read-C02Document {
    param([psobject] $File, [string] $Schema, [string] $SchemaError, [int] $MaximumBytes = 1MB)

    $File.Stream.Position = 0
    $bytes = Read-C02BoundedBytes -Stream $File.Stream -Length $File.Descriptor.bytes -MaximumBytes $MaximumBytes
    $json = Get-C02JsonText -Bytes $bytes
    return ConvertFrom-C02Json -Json $json -Schema $Schema -SchemaError $SchemaError
}

function Test-C02SameValue {
    param([AllowNull()] $Left, [AllowNull()] $Right)

    if ($null -eq $Left -or $null -eq $Right) { return $null -eq $Left -and $null -eq $Right }
    if ($Left -is [string] -or $Right -is [string]) {
        return $Left -is [string] -and $Right -is [string] -and
            [string]::Equals($Left, $Right, [System.StringComparison]::Ordinal)
    }
    if ($Left -is [bool] -or $Right -is [bool]) {
        return $Left -is [bool] -and $Right -is [bool] -and $Left -eq $Right
    }
    if ((Test-IsJsonInteger $Left) -or (Test-IsJsonInteger $Right)) {
        return (Test-IsJsonInteger $Left) -and (Test-IsJsonInteger $Right) -and [long]$Left -eq [long]$Right
    }
    if ($Left -is [System.Collections.IDictionary] -and $Right -is [System.Collections.IDictionary]) {
        # JSON keys named Count/Keys must not replace dictionary metadata.
        if ($Left.psbase.Count -ne $Right.psbase.Count) { return $false }
        foreach ($key in $Left.psbase.Keys) {
            if ($key -cnotin @($Right.psbase.Keys) -or -not (Test-C02SameValue -Left $Left[$key] -Right $Right[$key])) {
                return $false
            }
        }
        return $true
    }
    if ($Left -is [System.Collections.IList] -and $Right -is [System.Collections.IList]) {
        if ($Left.Count -ne $Right.Count) { return $false }
        for ($index = 0; $index -lt $Left.Count; $index++) {
            if (-not (Test-C02SameValue -Left $Left[$index] -Right $Right[$index])) { return $false }
        }
        return $true
    }
    return $false
}

function Open-C02Descriptor {
    param(
        [string] $ArtifactRoot, [System.Collections.IDictionary] $Descriptor,
        [System.Collections.Generic.List[System.IO.Stream]] $Holds,
        [long] $MaximumBytes = 9007199254740991
    )

    # Call only after the document's complete C01 schema: every fileName is a
    # fixed basename, not an extensible artifact or arbitrary observation path.
    $name = $Descriptor.fileName
    if ($name -isnot [string] -or $name -cne [System.IO.Path]::GetFileName($name) -or
        $name.Contains('/') -or $name.Contains('\')) { throw 'C02_UNSAFE_PATH' }
    $path = Resolve-C02Path -Path $name -Base $ArtifactRoot
    $file = Open-C02File -Path $path -Holds $Holds -MaximumBytes $MaximumBytes
    if (-not (Test-C02SameValue -Left $Descriptor.bytes -Right $file.Descriptor.bytes)) {
        throw 'C02_DESCRIPTOR_SIZE'
    }
    if (-not (Test-C02SameValue -Left $Descriptor.sha256 -Right $file.Descriptor.sha256)) {
        throw 'C02_DESCRIPTOR_HASH'
    }
    if ($file.Descriptor.fileName -cne $name) { throw 'C02_DESCRIPTOR_NAME' }
    return $file
}

function Assert-C02Identity {
    param(
        [System.Collections.IDictionary] $Value,
        [string] $Version, [string] $Commit, [string] $Repository, [string] $RunId
    )

    if (-not (Test-C02SameValue -Left $Value.productVersion -Right $Version) -or
        -not (Test-C02SameValue -Left $Value.sourceCommit -Right $Commit)) { throw 'C02_SOURCE_VERSION_BINDING' }
    if (-not (Test-C02SameValue -Left $Value.candidate.repository -Right $Repository) -or
        -not (Test-C02SameValue -Left $Value.candidate.runId -Right $RunId) -or
        $Value.candidate.workflow -cne '.github/workflows/release.yml') { throw 'C02_CANDIDATE_IDENTITY' }
}

function Assert-C02WindowsVersion {
    param([System.Collections.IDictionary] $Value)

    $version = $null
    if (-not [System.Version]::TryParse($Value.osVersion, [ref]$version) -or
        -not (Test-C02SameValue -Left $version.Build -Right $Value.osBuild)) {
        throw 'C02_OS_VERSION_BUILD'
    }
}

function Assert-C02StringFields {
    param([System.Collections.IDictionary] $Value, [string[]] $Names)

    # Keys is not a legacy evidence field. Reject it before the unchanged
    # legacy closed-set check accesses dictionary.Keys through the adapter.
    if (@($Value.psbase.Keys) -icontains 'Keys') { throw 'C02_EVIDENCE_TYPE' }
    foreach ($name in $Names) {
        if ($Value[$name] -isnot [string]) { throw 'C02_EVIDENCE_TYPE' }
    }
}

function Assert-C02LegacyEvidenceTypes {
    param([System.Collections.IDictionary] $Evidence)

    # Legacy checks below remain authoritative for their closed field sets and
    # values. Guard their string casts on v2: a one-element JSON array must not
    # masquerade as a string. Legacy integer and bool checks already test types.
    Assert-C02StringFields -Value $Evidence -Names @('evidenceKind', 'status', 'sourceCommit', 'sourceStatusSha256')
    Assert-C02StringFields -Value $Evidence.host -Names @('osArchitecture', 'processArchitecture')
    Assert-C02StringFields -Value $Evidence.package -Names @('fileName', 'sha256')
    Assert-C02StringFields -Value $Evidence -Names @('productVersion')
    Assert-C02StringFields -Value $Evidence.host -Names @('osName', 'osVersion')
    Assert-C02StringFields -Value $Evidence.checks -Names @()
}

function Assert-C02ZipRuntime {
    param([System.IO.Stream] $Stream, [System.Collections.IDictionary] $Runtime)

    # No extraction, PE parser, native execution, or reading workbook contents.
    # The ZIP test owns the rest of the package layout/runtime-version proof.
    $Stream.Position = 0
    $archive = [System.IO.Compression.ZipArchive]::new($Stream, [System.IO.Compression.ZipArchiveMode]::Read, $true)
    try {
        if ($archive.Entries.Count -gt 10000) { throw 'C02_ZIP_LAYOUT' }
        $root = 'StudyReportEvaluator-win-x64/'
        $cliPath = 'runtimes/win-x64/native/copilot.exe'
        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            if (-not $entry.FullName.StartsWith($root, [System.StringComparison]::Ordinal) -or
                $entry.FullName.Contains('\') -or -not $seen.Add($entry.FullName) -or
                @($entry.FullName.Split('/') | Where-Object { $_ -cin @('.', '..') }).Count -ne 0 -or
                (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { throw 'C02_ZIP_LAYOUT' }
        }
        $manifestEntry = $archive.GetEntry($root + 'copilot-runtime.json')
        $cliEntry = $archive.GetEntry($root + $cliPath)
        if ($null -eq $manifestEntry -or $null -eq $cliEntry) { throw 'C02_ZIP_RUNTIME' }
        $manifestStream = $manifestEntry.Open()
        try {
            $bytes = Read-C02BoundedBytes -Stream $manifestStream -Length $manifestEntry.Length
            # The bundled MSBuild manifest is UTF-8 with BOM. This compatibility
            # applies ONLY to that manifest, never candidate/CH/package evidence.
            if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
                $withoutBom = [byte[]]::new($bytes.Length - 3)
                [System.Array]::Copy($bytes, 3, $withoutBom, 0, $withoutBom.Length)
                $bytes = $withoutBom
            }
            $manifest = ConvertFrom-C02Json -Json (Get-C02JsonText -Bytes $bytes)
        }
        finally { $manifestStream.Dispose() }
        $expectedManifest = [ordered]@{
            schemaVersion = 1
            runtimeIdentifier = 'win-x64'
            cliVersion = $Runtime.cliVersion
            cliSha256 = $Runtime.cliSha256
            sdkVersion = $Runtime.copilotSdk
            cliRelativePath = $cliPath
        }
        if (-not (Test-C02SameValue -Left $manifest -Right $expectedManifest)) { throw 'C02_ZIP_RUNTIME' }
        if ($cliEntry.Length -le 0 -or $cliEntry.Length -gt 512MB) { throw 'C02_ZIP_CLI_SIZE' }
        $cliStream = $cliEntry.Open()
        $hasher = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
        try {
            $buffer = [byte[]]::new(65536)
            $count = [long]0
            while (($read = $cliStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $count += $read
                if ($count -gt $cliEntry.Length) { throw 'C02_ZIP_CLI_SIZE' }
                $hasher.AppendData($buffer, 0, $read)
            }
            $hash = [System.Convert]::ToHexString($hasher.GetHashAndReset())
            if ($count -ne $cliEntry.Length -or $hash -cne $Runtime.cliSha256) { throw 'C02_ZIP_CLI_HASH' }
        }
        finally { $hasher.Dispose(); $cliStream.Dispose() }
    }
    finally { $archive.Dispose() }
}

function Assert-C02CleanHost {
    param([System.Collections.IDictionary] $Evidence, [System.Collections.IDictionary] $SingleFileEvidence)

    if (-not (Test-C02SameValue -Left $Evidence.package -Right $SingleFileEvidence.package) -or
        -not (Test-C02SameValue -Left $Evidence.runtime -Right $SingleFileEvidence.runtime)) {
        throw 'C02_CLEAN_HOST_BINDING'
    }
    Assert-C02WindowsVersion -Value $Evidence.host
    # C01 permits up to 19 fractional digits. Parse the calendar at seconds,
    # then round any sub-tick remainder UP solely for the future-time bound.
    $timestampMatch = [System.Text.RegularExpressions.Regex]::Match(
        $Evidence.measuredAtUtc, '\A([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2})(?:\.([0-9]{1,19}))?Z\z')
    $time = [System.DateTimeOffset]::MinValue
    if (-not $timestampMatch.Success -or -not [System.DateTimeOffset]::TryParseExact(
            $timestampMatch.Groups[1].Value + 'Z', "yyyy-MM-dd'T'HH:mm:ss'Z'",
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::AssumeUniversal -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal,
            [ref]$time)) { throw 'C02_CLEAN_HOST_TIME' }
    $fraction = $timestampMatch.Groups[2].Value
    $ticks = [long]::Parse($fraction.PadRight(7, '0').Substring(0, 7), [System.Globalization.CultureInfo]::InvariantCulture)
    if ($fraction.Length -gt 7 -and $fraction.Substring(7).Trim('0').Length -ne 0) { $ticks++ }
    if ($time.AddTicks($ticks) -gt [System.DateTimeOffset]::UtcNow.AddMinutes(5)) { throw 'C02_CLEAN_HOST_TIME' }
    # The full C01 schema already requires CH-01..06 PASS/non-null records and
    # the nine absent dependencies. There is no JSON switch to downgrade them.
    $protection = $Evidence.protection
    if (($protection.launchOutcome -ceq 'Allowed' -and $protection.warningActions -ne 0) -or
        ($protection.launchOutcome -ceq 'WarnedThenAllowed' -and $protection.warningActions -le 0)) {
        throw 'C02_PROTECTION_CONSISTENCY'
    }
}

function Invoke-C02Validation {
    param([string] $InputPath, [string] $RepositoryRoot, [switch] $CandidateOnly)

    if ($PSVersionTable.PSVersion -lt [System.Version]'7.5') { throw 'C02_POWERSHELL_75_REQUIRED' }
    if ([string]::IsNullOrWhiteSpace($ExpectedRepository) -or [string]::IsNullOrWhiteSpace($ExpectedCandidateRunId)) {
        throw 'C02_EXPECTED_IDENTITY_REQUIRED'
    }
    # Preserve the existing repository defaults, never defaults from a record.
    $version = $ExpectedProductVersion
    if ([string]::IsNullOrWhiteSpace($version)) {
        $versionJson = (& (Join-Path $RepositoryRoot 'dev\version.ps1') show -Json | Out-String)
        $versionValue = ConvertFrom-C02Json -Json (Get-C02JsonText -Bytes $Utf8NoBom.GetBytes($versionJson))
        if ($versionValue.Status -isnot [string] -or $versionValue.Status -cne 'PASS' -or
            $versionValue.Version -isnot [string]) { throw 'C02_EXPECTED_VERSION' }
        $version = $versionValue.Version
    }
    $commit = $ExpectedSourceCommit
    if ([string]::IsNullOrWhiteSpace($commit)) {
        $commit = (@(git -C $RepositoryRoot rev-parse HEAD 2>$null) -join '').Trim()
        if ($LASTEXITCODE -ne 0) { throw 'C02_EXPECTED_COMMIT' }
    }
    if ($version.Length -gt 64 -or $version -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') {
        throw 'C02_EXPECTED_VERSION'
    }
    if ($commit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'C02_EXPECTED_COMMIT' }

    $holds = [System.Collections.Generic.List[System.IO.Stream]]::new()
    try {
        $artifactRoot = Resolve-C02Path -Path $ArtifactDirectory -Base $RepositoryRoot
        Assert-C02PlainDirectory -Path $artifactRoot
        $inputFullPath = Resolve-C02Path -Path $InputPath -Base $RepositoryRoot
        $candidatePath = Join-Path $artifactRoot 'release-candidate-record.json'
        $pathComparison = if ($IsWindows) { [System.StringComparison]::OrdinalIgnoreCase } else { [System.StringComparison]::Ordinal }
        if ($CandidateOnly -and
            (-not [string]::Equals($inputFullPath, $candidatePath, $pathComparison) -or
                [System.IO.Path]::GetFileName($inputFullPath) -cne 'release-candidate-record.json')) {
            throw 'C02_CANDIDATE_PATH'
        }
        $schemaFile = Open-C02File -Path (Join-Path $RepositoryRoot 'eng\schemas\platform-release-matrix-v2.schema.json') -Holds $holds -MaximumBytes 1MB
        $schemaText = Get-C02JsonText -Bytes (Read-C02BoundedBytes -Stream $schemaFile.Stream -Length $schemaFile.Descriptor.bytes)
        $schema = ConvertFrom-C02Json -Json $schemaText
        $schemas = @{}
        foreach ($definition in @('candidateRecord', 'cleanHostEvidence', 'singleFilePackageEvidence')) {
            # Never validate a detached nested definition: its refs need ALL defs.
            $schemas[$definition] = [ordered]@{
                '$schema' = $schema['$schema']
                '$defs' = $schema['$defs']
                '$ref' = '#/$defs/' + $definition
            } | ConvertTo-Json -Depth 64 -Compress
        }
        $inputFile = Open-C02File -Path $inputFullPath -Holds $holds -MaximumBytes 1MB
        $matrix = $null
        if ($CandidateOnly) {
            $candidateFile = $inputFile
        }
        else {
            $matrix = Read-C02Document -File $inputFile -Schema $schemaText -SchemaError 'C02_MATRIX_SCHEMA'
            Assert-C02Identity -Value $matrix -Version $version -Commit $commit -Repository $ExpectedRepository -RunId $ExpectedCandidateRunId
            $candidateFile = Open-C02Descriptor -ArtifactRoot $artifactRoot -Descriptor $matrix.candidateRecord -Holds $holds -MaximumBytes 1MB
        }
        $candidate = Read-C02Document -File $candidateFile -Schema $schemas.candidateRecord -SchemaError 'C02_CANDIDATE_SCHEMA'
        Assert-C02Identity -Value $candidate -Version $version -Commit $commit -Repository $ExpectedRepository -RunId $ExpectedCandidateRunId
        $byKind = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::Ordinal)
        foreach ($row in $candidate.rows) { $byKind.Add($row.artifactKind, $row) }
        if ($null -ne $matrix) {
            foreach ($row in $matrix.rows) {
                $candidateRow = $byKind[$row.artifactKind]
                foreach ($field in @('artifactKind', 'verification', 'artifact', 'sidecar', 'evidence')) {
                    if (-not (Test-C02SameValue -Left $row[$field] -Right $candidateRow[$field])) { throw 'C02_ROW_BINDING' }
                }
            }
        }

        $evidenceByKind = @{}
        $zipFile = $null
        $packageHost = $byKind['windows-singlefile-exe'].verification
        foreach ($kind in @('windows-singlefile-exe', 'windows-zip')) {
            $row = $byKind[$kind]
            Assert-C02WindowsVersion -Value $row.verification
            foreach ($field in @('osVersion', 'osBuild', 'osArchitecture', 'processArchitecture')) {
                # P07's Windows vs legacy Windows 11 osName is intentional.
                if (-not (Test-C02SameValue -Left $row.verification[$field] -Right $packageHost[$field])) {
                    throw 'C02_PACKAGE_HOST_BINDING'
                }
            }
            $artifactFile = Open-C02Descriptor -ArtifactRoot $artifactRoot -Descriptor $row.artifact -Holds $holds
            if ($kind -ceq 'windows-zip') { $zipFile = $artifactFile }
            $sidecarFile = Open-C02Descriptor -ArtifactRoot $artifactRoot -Descriptor $row.sidecar -Holds $holds -MaximumBytes 64KB
            $expectedBytes = $Utf8NoBom.GetBytes("$($row.artifact.sha256)  $($row.artifact.fileName)`n")
            $actualBytes = Read-C02BoundedBytes -Stream $sidecarFile.Stream -Length $sidecarFile.Descriptor.bytes -MaximumBytes 64KB
            if (-not (Test-ByteArrayEqual -Left $actualBytes -Right $expectedBytes)) { throw 'C02_SIDECAR_CONTENT' }
            $evidenceFile = Open-C02Descriptor -ArtifactRoot $artifactRoot -Descriptor $row.evidence -Holds $holds -MaximumBytes 1MB
            if ($kind -ceq 'windows-singlefile-exe') {
                $evidence = Read-C02Document -File $evidenceFile -Schema $schemas.singleFilePackageEvidence -SchemaError 'C02_P07_SCHEMA'
                if (-not (Test-C02SameValue -Left $evidence.productVersion -Right $version) -or
                    -not (Test-C02SameValue -Left $evidence.sourceCommit -Right $commit) -or
                    -not (Test-C02SameValue -Left $evidence.host -Right $row.verification) -or
                    -not (Test-C02SameValue -Left $evidence.package -Right $row.artifact)) { throw 'C02_P07_BINDING' }
            }
            else {
                $evidence = Read-C02Document -File $evidenceFile
                # Preserve, rather than approximate, the legacy closed ZIP evidence
                # checks; sanitize their diagnostics on the v2 path.
                try {
                    Assert-C02LegacyEvidenceTypes -Evidence $evidence
                    Assert-ZipEvidenceBinding -Evidence $evidence -Matrix $candidate -Row $row
                }
                catch { throw 'C02_ZIP_EVIDENCE' }
            }
            $evidenceByKind[$kind] = $evidence
        }
        $singleFile = $evidenceByKind['windows-singlefile-exe']
        Assert-C02ZipRuntime -Stream $zipFile.Stream -Runtime $singleFile.runtime
        $cleanHostDescriptor = $null
        if (-not $CandidateOnly) {
            $cleanHostFile = Open-C02Descriptor -ArtifactRoot $artifactRoot -Descriptor $matrix.cleanHostEvidence -Holds $holds -MaximumBytes 64KB
            $cleanHost = Read-C02Document -File $cleanHostFile -Schema $schemas.cleanHostEvidence -SchemaError 'C02_CLEAN_HOST_SCHEMA' -MaximumBytes 64KB
            Assert-C02Identity -Value $cleanHost -Version $version -Commit $commit -Repository $ExpectedRepository -RunId $ExpectedCandidateRunId
            Assert-C02CleanHost -Evidence $cleanHost -SingleFileEvidence $singleFile
            $cleanHostDescriptor = $cleanHostFile.Descriptor
        }
        [ordered]@{
            schemaVersion = 2
            productVersion = $version
            sourceCommit = $commit
            candidate = $candidate.candidate
            candidateRecord = $candidateFile.Descriptor
            cleanHostEvidence = $cleanHostDescriptor
            runtime = $singleFile.runtime
            rowCount = 2
            publishableAssets = @(
                'StudyReportEvaluator-win-x64.exe', 'StudyReportEvaluator-win-x64.exe.sha256',
                'StudyReportEvaluator-win-x64.zip', 'StudyReportEvaluator-win-x64.zip.sha256')
            status = if ($CandidateOnly) { 'PASS_CANDIDATE' } else { 'PASS' }
            cleanHostVerification = if ($CandidateOnly) { 'NOT_RUN' } else { 'HUMAN_RECORDED' }
            candidateRunVerification = 'CALLER_BOUND_UPSTREAM_REQUIRED'
            exeVersionVerification = 'P07_RECORD_AND_PACKAGE_HASH_BOUND'
            limitations = @(
                'Offline validation does not verify GitHub run success, workflow trust or tag identity; the upstream workflow must verify them.',
                'EXE internal version/layout and TRX execution are upstream P07 records bound to package hashes, not independent PE inspection or test execution here.',
                'Clean-host evidence and observation descriptors are human records, not execution proof; raw records are not fetched or published.',
                'PASS_CANDIDATE is not publication eligibility; unsigned hashes do not establish publisher identity or production trust.')
        } | ConvertTo-Json -Depth 8
    }
    finally {
        foreach ($stream in $holds) { $stream.Dispose() }
    }
}

function Get-ReleaseMatrixSchemaVersion {
    param([string] $Path)

    # Even the bounded version probe must not follow a parent junction/hardlink
    # or a UNC path before the v2 reader can enforce its file-handle boundary.
    if ($IsWindows -and [System.IO.Path]::GetPathRoot($Path) -notmatch '^[A-Za-z]:\\$') { throw 'C02_UNSAFE_PATH' }
    Assert-C02PlainDirectory -Path ([System.IO.Path]::GetDirectoryName($Path))
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Release matrix file is missing.' }
    $item = Get-Item -LiteralPath $Path -Force
    Assert-NotReparsePoint -Item $item -Description 'Release matrix'
    if ([string]$item.LinkType -ceq 'HardLink') { throw 'C02_UNSAFE_FILE' }
    $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'Read')
    try { $bytes = Read-C02BoundedBytes -Stream $stream -Length $stream.Length }
    finally { $stream.Dispose() }
    try { $json = $Utf8NoBom.GetString($bytes) }
    catch { throw 'Release matrix must be strict UTF-8.' }
    try { $document = [System.Text.Json.JsonDocument]::Parse($json) }
    catch { throw 'Release matrix JSON is invalid: C02_JSON_INVALID' }
    try {
        $property = [System.Text.Json.JsonElement]::new()
        $version = 0
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object -or
            -not $document.RootElement.TryGetProperty('schemaVersion', [ref]$property) -or
            $property.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
            -not $property.TryGetInt32([ref]$version) -or $version -ne 2) { throw 'C02_SCHEMA_VERSION' }
        Assert-C02JsonTokens -Element $document.RootElement
        return $version
    }
    finally { $document.Dispose() }
}

# Unsupported/ill-typed schema versions, including the retired schema 1, are
# rejected. C03 calls these normal CLI sets, not extracted private functions or
# a separate helper-module contract.
try {
    $dispatchRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    if ($PSCmdlet.ParameterSetName -ceq 'Candidate') {
        Invoke-C02Validation -InputPath $CandidateRecordPath -RepositoryRoot $dispatchRoot -CandidateOnly
        return
    }
    $dispatchPath = Resolve-RepositoryRelativePath -Path $MatrixPath -RepositoryRoot $dispatchRoot
    [void](Get-ReleaseMatrixSchemaVersion -Path $dispatchPath)
    Invoke-C02Validation -InputPath $MatrixPath -RepositoryRoot $dispatchRoot
    return
}
catch {
    $code = $_.Exception.Message
    # Preserve the fixed matrix probe diagnostics, without echoing JSON bodies.
    if ($code.StartsWith('Release matrix ', [System.StringComparison]::Ordinal)) { throw }
    if ($code -cnotmatch '\AC02_[A-Z0-9_]+\z') { $code = 'C02_IO_OR_FORMAT' }
    [System.Console]::Error.WriteLine("C02_FAIL code=$code")
    exit 1
}
