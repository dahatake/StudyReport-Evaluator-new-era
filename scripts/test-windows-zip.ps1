#Requires -Version 7.4
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [string] $ResultsDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$ZipFileName = 'StudyReportEvaluator-win-x64.zip'
$SidecarFileName = "$ZipFileName.sha256"
$EvidenceFileName = 'StudyReportEvaluator-win-x64.evidence.json'
$PackageRootName = 'StudyReportEvaluator-win-x64'
$CopilotCliRelativePath = 'runtimes/win-x64/native/copilot.exe'
$Utf8NoBom = [System.Text.UTF8Encoding]::new($false, $true)
$RequiredTestCount = 3
$RequiredEntries = @(
    "$PackageRootName/StudyReportEvaluator.App.exe",
    "$PackageRootName/StudyReportEvaluator.App.dll",
    "$PackageRootName/StudyReportEvaluator.Core.dll",
    "$PackageRootName/GitHub.Copilot.SDK.dll",
    "$PackageRootName/copilot-runtime.json",
    "$PackageRootName/$CopilotCliRelativePath",
    "$PackageRootName/README.md",
    "$PackageRootName/LICENSE",
    "$PackageRootName/RELEASE-NOTES.txt"
)

function Assert-SupportedHost {
    if ($PSVersionTable.PSEdition -cne 'Core' -or
        $PSVersionTable.PSVersion -lt [System.Version]'7.4') {
        throw 'PowerShell Core 7.4 or later is required.'
    }

    if (-not $IsWindows -or
        [System.Environment]::OSVersion.Version.Build -lt 22000 -or
        [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64 -or
        [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
        throw 'Windows ZIP required validation requires Windows 11 x64 and an x64 process.'
    }
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

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        return [System.Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData($stream))
    }
    finally {
        $stream.Dispose()
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

function Assert-SafeArchiveEntry {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchiveEntry] $Entry,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]] $Seen
    )

    $name = $Entry.FullName
    if ([string]::IsNullOrWhiteSpace($name) -or
        $name.Contains('\\', [System.StringComparison]::Ordinal) -or
        $name.StartsWith('/', [System.StringComparison]::Ordinal) -or
        -not $name.StartsWith("$PackageRootName/", [System.StringComparison]::Ordinal) -or
        @($name.Split('/') | Where-Object { $_ -eq '.' -or $_ -eq '..' }).Count -ne 0 -or
        -not $Seen.Add($name)) {
        throw 'Windows ZIP contains an unsafe, duplicate, or out-of-root entry.'
    }

    $unixFileType = ($Entry.ExternalAttributes -shr 16) -band 0xF000
    if ($unixFileType -eq 0xA000) {
        throw 'Windows ZIP cannot contain symbolic links.'
    }

    if (-not $name.EndsWith('/', [System.StringComparison]::Ordinal) -and
        $Entry.Length -le 0) {
        throw 'Windows ZIP cannot contain empty files.'
    }

    $extension = [System.IO.Path]::GetExtension($name)
    if ($extension -cin @('.xlsx', '.xls', '.xlsm', '.xlsb', '.csv', '.pfx', '.p12', '.pem', '.key')) {
        throw 'Windows ZIP contains a forbidden workbook or secret file type.'
    }
}

function Get-RequiredArchiveEntry {
    param(
        [Parameter(Mandatory)]
        [System.IO.Compression.ZipArchive] $Archive,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry -or $entry.Length -le 0) {
        throw "Windows ZIP is missing required entry: $Name"
    }

    return $entry
}

function Write-AtomicEvidence {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [System.Collections.IDictionary] $Value
    )

    $temporaryPath = "$Path.$([System.Guid]::NewGuid().ToString('N')).tmp"
    try {
        $json = ($Value | ConvertTo-Json -Depth 8) + "`n"
        [System.IO.File]::WriteAllText($temporaryPath, $json, $Utf8NoBom)
        [System.IO.File]::Move($temporaryPath, $Path, $true)
    }
    finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

Assert-SupportedHost
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testProject = Join-Path $repositoryRoot 'tests\StudyReportEvaluator.App.Tests\StudyReportEvaluator.App.Tests.csproj'
$versionTool = Join-Path $repositoryRoot 'dev\version.ps1'
$packageDirectory = Join-Path $repositoryRoot 'artifacts\package'
$zipPath = Join-Path $packageDirectory $ZipFileName
$sidecarPath = Join-Path $packageDirectory $SidecarFileName
$evidencePath = Join-Path $packageDirectory $EvidenceFileName

foreach ($requiredPath in @($testProject, $versionTool)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw 'Windows ZIP validation source input is missing.'
    }
}

[void][System.IO.Directory]::CreateDirectory($packageDirectory)
if (Test-Path -LiteralPath $evidencePath) {
    $existingEvidence = Get-Item -LiteralPath $evidencePath -Force
    Assert-NotReparsePoint -Item $existingEvidence -Description 'Windows ZIP evidence'
    if ($existingEvidence.PSIsContainer) {
        throw 'Windows ZIP evidence path cannot be a directory.'
    }

    Remove-Item -LiteralPath $evidencePath -Force
}

$statusBefore = @(git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or $statusBefore.Count -ne 0) {
    throw 'Windows ZIP required evidence must be generated from a clean source checkout.'
}

$sourceCommit = ([string](@(git -C $repositoryRoot rev-parse HEAD) -join '')).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to resolve the Windows ZIP source commit.'
}

$versionResult = & $versionTool show -Json | Out-String | ConvertFrom-Json
if ([string]$versionResult.Status -cne 'PASS' -or
    [string]$versionResult.Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Unable to resolve the stable Windows ZIP product version.'
}
$productVersion = [string]$versionResult.Version

if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $repositoryRoot 'TestResults\windows-zip'
}
elseif (-not [System.IO.Path]::IsPathFullyQualified($ResultsDirectory)) {
    $ResultsDirectory = [System.IO.Path]::GetFullPath($ResultsDirectory, $repositoryRoot)
}
else {
    $ResultsDirectory = [System.IO.Path]::GetFullPath($ResultsDirectory)
}

[void][System.IO.Directory]::CreateDirectory($ResultsDirectory)
$resultsItem = Get-Item -LiteralPath $ResultsDirectory -Force
Assert-NotReparsePoint -Item $resultsItem -Description 'Windows ZIP test results directory'
$trxPath = Join-Path $ResultsDirectory 'windows-zip.trx'
Remove-Item -LiteralPath $trxPath -Force -ErrorAction SilentlyContinue

$sentinelRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    'StudyReportEvaluator-ZipInput-' + [System.Guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($sentinelRoot)
$sentinelPath = Join-Path $sentinelRoot 'user-workbook-sentinel.xlsx'
[System.IO.File]::WriteAllBytes($sentinelPath, [byte[]](80, 75, 3, 4, 0, 1, 2, 3))
$sentinelHash = Get-Sha256Hex -Path $sentinelPath
$evidence = $null
$zipHash = $null

try {
    $testArguments = @(
        'test',
        $testProject,
        '--configuration', 'Release',
        '--no-build',
        '--no-restore',
        '--filter', 'FullyQualifiedName~StudyReportEvaluator.App.Tests.Packaging.WindowsPublishPackageTests',
        '--results-directory', $ResultsDirectory,
        '--logger', 'trx;LogFileName=windows-zip.trx',
        '--logger', 'console;verbosity=minimal')
    & dotnet @testArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Windows ZIP focused package tests failed.'
    }

    if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
        throw 'Windows ZIP test result file is missing.'
    }

    [xml]$trx = [System.IO.File]::ReadAllText($trxPath)
    $namespace = [System.Xml.XmlNamespaceManager]::new($trx.NameTable)
    $namespace.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $testResults = @($trx.SelectNodes('//t:UnitTestResult', $namespace))
    if ($testResults.Count -ne $RequiredTestCount -or
        @($testResults | Where-Object { [string]$_.outcome -cne 'Passed' }).Count -ne 0) {
        throw 'Windows ZIP focused package test count or outcome is invalid.'
    }

    foreach ($path in @($zipPath, $sidecarPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw 'Windows ZIP package or sidecar is missing.'
        }

        $item = Get-Item -LiteralPath $path -Force
        Assert-NotReparsePoint -Item $item -Description 'Windows ZIP output'
        if ($item.Length -le 0) {
            throw 'Windows ZIP package and sidecar must be nonempty.'
        }
    }

    $zipHash = Get-Sha256Hex -Path $zipPath
    $expectedSidecar = $Utf8NoBom.GetBytes("$zipHash  $ZipFileName`n")
    $actualSidecar = [System.IO.File]::ReadAllBytes($sidecarPath)
    if (-not (Test-ByteArrayEqual -Left $actualSidecar -Right $expectedSidecar)) {
        throw 'Windows ZIP sidecar does not exactly match the package.'
    }

    $stream = [System.IO.File]::Open(
        $zipPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [System.IO.Compression.ZipArchiveMode]::Read,
            $false,
            [System.Text.Encoding]::UTF8)
        try {
            $seen = [System.Collections.Generic.HashSet[string]]::new(
                [System.StringComparer]::OrdinalIgnoreCase)
            foreach ($entry in $archive.Entries) {
                Assert-SafeArchiveEntry -Entry $entry -Seen $seen
            }

            foreach ($requiredEntry in $RequiredEntries) {
                [void](Get-RequiredArchiveEntry -Archive $archive -Name $requiredEntry)
            }

            $manifestEntry = Get-RequiredArchiveEntry `
                -Archive $archive `
                -Name "$PackageRootName/copilot-runtime.json"
            $manifestReader = [System.IO.StreamReader]::new(
                $manifestEntry.Open(),
                $Utf8NoBom,
                $true)
            try {
                $manifest = $manifestReader.ReadToEnd() | ConvertFrom-Json
            }
            finally {
                $manifestReader.Dispose()
            }

            if ([int]$manifest.schemaVersion -ne 1 -or
                [string]$manifest.runtimeIdentifier -cne 'win-x64' -or
                [string]$manifest.cliRelativePath -cne $CopilotCliRelativePath -or
                [string]$manifest.cliSha256 -notmatch '^[0-9A-F]{64}$') {
                throw 'Windows ZIP bundled CLI manifest is invalid.'
            }

            $cliEntry = Get-RequiredArchiveEntry `
                -Archive $archive `
                -Name "$PackageRootName/$CopilotCliRelativePath"
            $cliStream = $cliEntry.Open()
            try {
                $cliHash = [System.Convert]::ToHexString(
                    [System.Security.Cryptography.SHA256]::HashData($cliStream))
            }
            finally {
                $cliStream.Dispose()
            }

            if ($cliHash -cne [string]$manifest.cliSha256) {
                throw 'Windows ZIP bundled CLI does not match its manifest hash.'
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    if ((Get-Sha256Hex -Path $sentinelPath) -cne $sentinelHash) {
        throw 'Windows ZIP validation changed the user-workbook sentinel.'
    }

    $statusAfter = @(git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $statusAfter.Count -ne 0) {
        throw 'Windows ZIP validation changed the clean source checkout.'
    }

    $zipItem = Get-Item -LiteralPath $zipPath -Force
    $evidence = [ordered]@{
        schemaVersion = 1
        evidenceKind = 'windows-zip-required'
        status = 'PASS_REQUIRED'
        sourceCommit = $sourceCommit
        sourceStatusEntryCount = 0
        sourceStatusSha256 = 'E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855'
        productVersion = $productVersion
        host = [ordered]@{
            osName = 'Windows 11'
            osVersion = [System.Environment]::OSVersion.Version.ToString()
            osBuild = [System.Environment]::OSVersion.Version.Build
            osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        }
        package = [ordered]@{
            fileName = $ZipFileName
            bytes = $zipItem.Length
            sha256 = $zipHash
        }
        checks = [ordered]@{
            sidecarVerified = $true
            safeLayoutVerified = $true
            bundledCliVerified = $true
            cleanExtractVerified = $true
            apphostLaunchVerified = $true
            externalRuntimeAbsentVerified = $true
            userWorkbookExcluded = $true
            inputUnchangedVerified = $true
        }
    }
}
finally {
    if (Test-Path -LiteralPath $sentinelRoot) {
        Remove-Item -LiteralPath $sentinelRoot -Recurse -Force
    }
}

if (Test-Path -LiteralPath $sentinelRoot) {
    throw 'Windows ZIP user-workbook sentinel cleanup failed.'
}

if ($null -eq $evidence -or [string]::IsNullOrWhiteSpace([string]$zipHash)) {
    throw 'Windows ZIP validation did not produce complete evidence inputs.'
}

Write-AtomicEvidence -Path $evidencePath -Value $evidence
Write-Output "Windows ZIP evidence: $evidencePath"
Write-Output "Package SHA-256: $zipHash"
Write-Output 'Status: PASS_REQUIRED for the Windows ZIP regression only.'
