#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [string] $PublishedDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$PackageRootName = 'StudyReportEvaluator-win-x64'
$ZipFileName = "$PackageRootName.zip"
$HashFileName = "$ZipFileName.sha256"
$RuntimeIdentifier = 'win-x64'
$CopilotCliRelativePath = 'runtimes/win-x64/native/copilot.exe'
$FixedTimestamp = [System.DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
$Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$ReleaseNotes = @(
    'StudyReport Evaluator - Windows x64 local package',
    'Target: Windows 11 x64',
    'Deployment: .NET 10 self-contained folder',
    'Archive: local ZIP',
    'Signing: UNSIGNED',
    'Symbols: EXCLUDED',
    'Office dependency: NONE',
    'GitHub Copilot CLI runtime: BUNDLED',
    'GitHub Copilot CLI login is required only for AI evaluation.',
    'User guide: README.md and docs/getting-started.md'
) -join "`n"
$ReleaseNotes += "`n"

function Assert-SupportedHost {
    if ($PSVersionTable.PSEdition -cne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
        throw 'PowerShell Core 7 or later is required. Windows PowerShell is not supported.'
    }

    if (-not $IsWindows -or [System.Environment]::OSVersion.Version.Build -lt 22000) {
        throw 'P-01 packaging is supported only on Windows 11 x64.'
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64 -or
        [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
        throw 'P-01 packaging requires an x64 operating system and x64 PowerShell process.'
    }
}

function Assert-NotReparsePoint {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileSystemInfo] $Item
    )

    if (($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Reparse points are not allowed in the package input or outputs: $($Item.FullName)"
    }
}

function Assert-SafeRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [System.IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath -eq '..' -or
        $RelativePath.StartsWith('..\', [System.StringComparison]::Ordinal) -or
        $RelativePath.StartsWith('../', [System.StringComparison]::Ordinal)) {
        throw "Unsafe package-relative path: $RelativePath"
    }

    $normalized = $RelativePath.Replace('\', '/')
    foreach ($segment in $normalized.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        if ($segment -eq '.' -or $segment -eq '..' -or $segment.Contains(':', [System.StringComparison]::Ordinal)) {
            throw "Unsafe package-relative path: $RelativePath"
        }
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

function Assert-BundledCopilotRuntime {
    param(
        [Parameter(Mandatory)]
        [string] $Directory
    )

    $manifestPath = Join-Path $Directory 'copilot-runtime.json'
    $cliPath = Join-Path $Directory $CopilotCliRelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $cliPath -PathType Leaf)) {
        throw 'The bundled Copilot CLI manifest or binary is missing from the package input.'
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        throw 'The bundled Copilot CLI manifest in the package input is invalid JSON.'
    }

    $propertyNames = @($manifest.PSObject.Properties.Name)
    $expectedPropertyNames = @(
        'schemaVersion',
        'runtimeIdentifier',
        'cliVersion',
        'cliSha256',
        'sdkVersion',
        'cliRelativePath'
    )
    if ($propertyNames.Count -ne $expectedPropertyNames.Count -or
        @($propertyNames | Where-Object { $_ -cnotin $expectedPropertyNames }).Count -ne 0 -or
        [int]$manifest.schemaVersion -ne 1 -or
        [string]$manifest.runtimeIdentifier -cne $RuntimeIdentifier -or
        [string]$manifest.cliRelativePath -cne $CopilotCliRelativePath -or
        [string]$manifest.cliVersion -notmatch '^[0-9][A-Za-z0-9._+-]{0,127}$' -or
        [string]$manifest.sdkVersion -notmatch '^[0-9][A-Za-z0-9._+-]{0,127}$' -or
        [string]$manifest.cliSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'The bundled Copilot CLI manifest violates the package contract.'
    }

    $manifestItem = Get-Item -LiteralPath $manifestPath -Force
    $cliItem = Get-Item -LiteralPath $cliPath -Force
    Assert-NotReparsePoint -Item $manifestItem
    Assert-NotReparsePoint -Item $cliItem
    if ($manifestItem.Length -le 0 -or $cliItem.Length -le 0 -or
        (Get-Sha256Hex -Path $cliPath) -cne ([string]$manifest.cliSha256).ToUpperInvariant()) {
        throw 'The bundled Copilot CLI does not match its package manifest hash.'
    }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($cliPath)
    if ([string]$versionInfo.ProductVersion -cne [string]$manifest.cliVersion -and
        [string]$versionInfo.FileVersion -cne [string]$manifest.cliVersion) {
        throw 'The bundled Copilot CLI does not match its package manifest version.'
    }

    $sdkPath = Join-Path $Directory 'GitHub.Copilot.SDK.dll'
    $sdkVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($sdkPath)
    $sdkPackageVersion = ([string]$sdkVersionInfo.ProductVersion).Split('+', 2)[0]
    if ($sdkPackageVersion -cne [string]$manifest.sdkVersion) {
        throw 'The bundled Copilot manifest does not match the packaged SDK version.'
    }
}

function Assert-OwnedOutputTarget {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $ExpectedFullPath
    )

    if (-not [string]::Equals(
            [System.IO.Path]::GetFullPath($Path),
            [System.IO.Path]::GetFullPath($ExpectedFullPath),
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace a non-owned package output: $Path"
    }

    if (Test-Path -LiteralPath $Path) {
        $item = Get-Item -LiteralPath $Path -Force
        Assert-NotReparsePoint -Item $item
        if ($item.PSIsContainer) {
            throw "Refusing to replace a directory at the owned file path: $Path"
        }
    }
}

function Assert-PublishInput {
    param(
        [Parameter(Mandatory)]
        [string] $Directory
    )

    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw "Published directory does not exist: $Directory"
    }

    $rootItem = Get-Item -LiteralPath $Directory -Force
    Assert-NotReparsePoint -Item $rootItem

    $items = @(Get-ChildItem -LiteralPath $Directory -Force -Recurse)
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    if ($files.Count -eq 0) {
        throw 'Published directory is empty.'
    }

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $forbiddenSegments = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@('sample', 'samples', 'input', 'inputs', 'artifact', 'artifacts', 'test', 'tests', 'src', 'source', 'sources', 'secret', 'secrets', '.git'),
        [System.StringComparer]::OrdinalIgnoreCase)
    $forbiddenExtensions = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@('.cs', '.csproj', '.fs', '.fsproj', '.vb', '.vbproj', '.sln', '.slnx', '.ps1', '.pdb', '.pfx', '.p12', '.pem', '.key', '.snk', '.xlsx', '.xls', '.xlsm', '.csv'),
        [System.StringComparer]::OrdinalIgnoreCase)
    $forbiddenMarkers = @(
        'Microsoft.Office',
        'Office.Interop',
        'Interop.Excel',
        'LibreOffice',
        'soffice',
        'Microsoft.NET.Test.Sdk',
        'xunit',
        'testhost',
        'Avalonia.Headless',
        'StudyReportEvaluator.App.Tests',
        'StudyReportEvaluator.Core.Tests'
    )

    foreach ($item in $items) {
        Assert-NotReparsePoint -Item $item
        $relative = [System.IO.Path]::GetRelativePath($Directory, $item.FullName)
        Assert-SafeRelativePath -RelativePath $relative
        $normalized = $relative.Replace('\', '/')

        if (-not $seen.Add($normalized)) {
            throw "Duplicate or case-colliding package input: $normalized"
        }

        foreach ($segment in $normalized.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries)) {
            if ($forbiddenSegments.Contains($segment)) {
                throw "Forbidden package input segment: $normalized"
            }
        }

        foreach ($marker in $forbiddenMarkers) {
            if ($normalized.Contains($marker, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Forbidden test or Office dependency marker in package input: $normalized"
            }
        }

        if (-not $item.PSIsContainer) {
            if ($item.Length -le 0) {
                throw "Zero-byte package input is not allowed: $normalized"
            }

            if ($forbiddenExtensions.Contains([System.IO.Path]::GetExtension($item.Name)) -or
                $item.Name -ieq '.env' -or
                $item.Name -ieq 'setting.txt' -or
                [System.Text.RegularExpressions.Regex]::IsMatch(
                    $item.Name,
                    '(^|[._-])(secret|password|credential|token)([._-]|$)',
                    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                throw "Forbidden source, symbol, input, or secret file in package input: $normalized"
            }
        }
    }

    foreach ($requiredFile in @(
        'StudyReportEvaluator.App.exe',
        'StudyReportEvaluator.App.dll',
        'StudyReportEvaluator.App.runtimeconfig.json',
        'StudyReportEvaluator.App.deps.json',
        'StudyReportEvaluator.Core.dll',
        'DocumentFormat.OpenXml.dll',
        'DocumentFormat.OpenXml.Framework.dll',
        'GitHub.Copilot.SDK.dll',
        'copilot-runtime.json',
        'runtimes\win-x64\native\copilot.exe',
        'Avalonia.dll',
        'Avalonia.Win32.dll',
        'coreclr.dll',
        'hostfxr.dll',
        'hostpolicy.dll',
        'System.Private.CoreLib.dll')) {
        $requiredPath = Join-Path $Directory $requiredFile
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf) -or
            (Get-Item -LiteralPath $requiredPath).Length -le 0) {
            throw "Required publish file is missing or empty: $requiredFile"
        }
    }

    Assert-BundledCopilotRuntime -Directory $Directory

    if (Test-Path -LiteralPath (Join-Path $Directory 'copilot.exe')) {
        throw 'The Copilot CLI must be stored only under the pinned RID runtime directory.'
    }

    $dependencyContextPath = Join-Path $Directory 'StudyReportEvaluator.App.deps.json'
    $dependencyContextText = Get-Content -LiteralPath $dependencyContextPath -Raw
    foreach ($marker in $forbiddenMarkers) {
        if ($dependencyContextText.Contains($marker, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Forbidden test or Office dependency marker in package deps.json: $marker"
        }
    }
}

function Assert-Archive {
    param(
        [Parameter(Mandatory)]
        [string] $ArchivePath,

        [Parameter(Mandatory)]
        [string[]] $ExpectedEntryNames
    )

    $stream = [System.IO.File]::Open(
        $ArchivePath,
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
            $entries = @($archive.Entries)
            if ($entries.Count -ne $ExpectedEntryNames.Count) {
                throw 'ZIP entry count does not match the deterministic input manifest.'
            }

            $seenOrdinal = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            $seenIgnoreCase = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
            for ($index = 0; $index -lt $entries.Count; $index++) {
                $entry = $entries[$index]
                if ($entry.FullName -cne $ExpectedEntryNames[$index] -or
                    -not $seenOrdinal.Add($entry.FullName) -or
                    -not $seenIgnoreCase.Add($entry.FullName)) {
                    throw "ZIP order or uniqueness validation failed at entry '$($entry.FullName)'."
                }

                Assert-SafeRelativePath -RelativePath $entry.FullName.TrimEnd('/')
                if (-not $entry.FullName.StartsWith("$PackageRootName/", [System.StringComparison]::Ordinal)) {
                    throw "ZIP entry is outside the single package root: $($entry.FullName)"
                }

                if ($entry.LastWriteTime.Year -ne $FixedTimestamp.Year -or
                    $entry.LastWriteTime.Month -ne $FixedTimestamp.Month -or
                    $entry.LastWriteTime.Day -ne $FixedTimestamp.Day -or
                    $entry.LastWriteTime.Hour -ne $FixedTimestamp.Hour -or
                    $entry.LastWriteTime.Minute -ne $FixedTimestamp.Minute -or
                    $entry.LastWriteTime.Second -ne $FixedTimestamp.Second) {
                    throw "ZIP entry timestamp is not normalized: $($entry.FullName)"
                }

                $unixFileType = ($entry.ExternalAttributes -shr 16) -band 0xF000
                if ($unixFileType -eq 0xA000) {
                    throw "ZIP symlink entry is not allowed: $($entry.FullName)"
                }

                if ($entry.FullName -ne "$PackageRootName/" -and $entry.Length -le 0) {
                    throw "ZIP contains an empty file: $($entry.FullName)"
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

Assert-SupportedHost

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$packageDirectory = Join-Path $repositoryRoot 'artifacts\package'
$defaultPublishDirectory = Join-Path $packageDirectory 'publish\win-x64'
$publishScriptPath = Join-Path $PSScriptRoot 'publish-windows.ps1'
$zipPath = Join-Path $packageDirectory $ZipFileName
$hashPath = Join-Path $packageDirectory $HashFileName

if ([string]::IsNullOrWhiteSpace($PublishedDirectory)) {
    & $publishScriptPath
    $PublishedDirectory = $defaultPublishDirectory
}
elseif (-not [System.IO.Path]::IsPathRooted($PublishedDirectory)) {
    $PublishedDirectory = [System.IO.Path]::GetFullPath($PublishedDirectory, $repositoryRoot)
}
else {
    $PublishedDirectory = [System.IO.Path]::GetFullPath($PublishedDirectory)
}

[void][System.IO.Directory]::CreateDirectory($packageDirectory)
$packageDirectoryItem = Get-Item -LiteralPath $packageDirectory -Force
Assert-NotReparsePoint -Item $packageDirectoryItem
Assert-PublishInput -Directory $PublishedDirectory

$zipRelativeToInput = [System.IO.Path]::GetRelativePath($PublishedDirectory, $zipPath)
if (-not ($zipRelativeToInput -eq '..' -or
          $zipRelativeToInput.StartsWith('..\', [System.StringComparison]::Ordinal) -or
          $zipRelativeToInput.StartsWith('../', [System.StringComparison]::Ordinal))) {
    throw 'Package outputs must not be located inside the published input directory.'
}

Assert-OwnedOutputTarget -Path $zipPath -ExpectedFullPath (Join-Path $packageDirectory 'StudyReportEvaluator-win-x64.zip')
Assert-OwnedOutputTarget -Path $hashPath -ExpectedFullPath (Join-Path $packageDirectory 'StudyReportEvaluator-win-x64.zip.sha256')

$fileMap = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
$fileSnapshots = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
$relativeNames = [System.Collections.Generic.List[string]]::new()
foreach ($file in @(Get-ChildItem -LiteralPath $PublishedDirectory -File -Force -Recurse)) {
    $relative = [System.IO.Path]::GetRelativePath($PublishedDirectory, $file.FullName)
    Assert-SafeRelativePath -RelativePath $relative
    $normalized = $relative.Replace('\', '/')
    if ($fileMap.ContainsKey($normalized)) {
        throw "Duplicate package input path: $normalized"
    }

    $fileMap.Add($normalized, $file.FullName)
    $fileSnapshots.Add(
        $normalized,
        [pscustomobject]@{
            Length = $file.Length
            LastWriteTimeUtc = $file.LastWriteTimeUtc
        })
    $relativeNames.Add($normalized)
}

$documentationItems = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
$documentationRelativePaths = @(
    'README.md',
    'LICENSE',
    'docs\README.md',
    'docs\getting-started.md',
    'docs\features.md',
    'docs\custom-evaluator-guide.md',
    'docs\technical-guid.md',
    'docs\prompt-launch.md',
    'docs\privacy-and-data-handling.md',
    'docs\troubleshooting.md',
    'docs\settings.md',
    'docs\third-party-notices.md',
    'docs\result-excel-description.md',
    'images\README.md',
    'images\architecture-overview.svg',
    'images\technical-architecture.svg',
    'images\evaluation-message-flow.svg',
    'images\01-input-workbook.png',
    'images\02-input-mapping.png',
    'images\03-design-knowledge.png',
    'images\04-design-custom-prompt.png',
    'images\05-execution-auto.png',
    'images\06-results-review.png',
    'images\07-output-export.png',
    'images\08-settings.png'
)
foreach ($documentationRelativePath in $documentationRelativePaths) {
    $documentationPath = Join-Path $repositoryRoot $documentationRelativePath
    if (-not (Test-Path -LiteralPath $documentationPath -PathType Leaf)) {
        throw "Required public documentation file is missing: $documentationRelativePath"
    }

    $documentationItems.Add((Get-Item -LiteralPath $documentationPath -Force))
}

foreach ($documentationFile in $documentationItems) {
    Assert-NotReparsePoint -Item $documentationFile
    $relative = [System.IO.Path]::GetRelativePath($repositoryRoot, $documentationFile.FullName)
    Assert-SafeRelativePath -RelativePath $relative
    $normalized = $relative.Replace('\', '/')
    $extension = [System.IO.Path]::GetExtension($documentationFile.Name)
    $isLicense = $normalized -ceq 'LICENSE'
    if ((-not $isLicense -and $extension -cne '.md' -and $extension -cne '.png' -and $extension -cne '.svg') -or
        $documentationFile.Length -le 0) {
        throw "Documentation package input must be LICENSE, Markdown, PNG, or SVG and nonempty: $normalized"
    }

    if ($fileMap.ContainsKey($normalized)) {
        throw "Documentation package path collides with publish input: $normalized"
    }

    $fileMap.Add($normalized, $documentationFile.FullName)
    $fileSnapshots.Add(
        $normalized,
        [pscustomobject]@{
            Length = $documentationFile.Length
            LastWriteTimeUtc = $documentationFile.LastWriteTimeUtc
        })
    $relativeNames.Add($normalized)
}

if ($fileMap.ContainsKey('RELEASE-NOTES.txt')) {
    throw 'Published input must not provide the package-owned RELEASE-NOTES.txt file.'
}

$relativeNames.Add('RELEASE-NOTES.txt')
$sortedRelativeNames = $relativeNames.ToArray()
[System.Array]::Sort($sortedRelativeNames, [System.StringComparer]::Ordinal)

$expectedEntryNames = [System.Collections.Generic.List[string]]::new()
$expectedEntryNames.Add("$PackageRootName/")
foreach ($relativeName in $sortedRelativeNames) {
    $expectedEntryNames.Add("$PackageRootName/$relativeName")
}

$runId = [System.Guid]::NewGuid().ToString('N')
$temporaryZipPath = Join-Path $packageDirectory ".$ZipFileName.$runId.tmp"
$temporaryHashPath = Join-Path $packageDirectory ".$HashFileName.$runId.tmp"
$zipCommitted = $false

try {
    $zipStream = [System.IO.FileStream]::new(
        $temporaryZipPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None,
        65536,
        [System.IO.FileOptions]::WriteThrough)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $zipStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true,
            [System.Text.Encoding]::UTF8)
        try {
            $rootEntry = $archive.CreateEntry("$PackageRootName/", [System.IO.Compression.CompressionLevel]::NoCompression)
            $rootEntry.LastWriteTime = $FixedTimestamp
            $rootEntry.ExternalAttributes = 0x10

            foreach ($relativeName in $sortedRelativeNames) {
                $entry = $archive.CreateEntry(
                    "$PackageRootName/$relativeName",
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $FixedTimestamp
                $entry.ExternalAttributes = 0
                $entryStream = $entry.Open()
                try {
                    if ($relativeName -ceq 'RELEASE-NOTES.txt') {
                        $releaseBytes = $Utf8NoBom.GetBytes($ReleaseNotes)
                        $entryStream.Write($releaseBytes, 0, $releaseBytes.Length)
                    }
                    else {
                        $sourcePath = $fileMap[$relativeName]
                        $sourceStream = [System.IO.File]::Open(
                            $sourcePath,
                            [System.IO.FileMode]::Open,
                            [System.IO.FileAccess]::Read,
                            [System.IO.FileShare]::Read)
                        try {
                            $sourceStream.CopyTo($entryStream)
                        }
                        finally {
                            $sourceStream.Dispose()
                        }

                        $current = Get-Item -LiteralPath $sourcePath
                        $snapshot = $fileSnapshots[$relativeName]
                        if ($current.Length -ne $snapshot.Length -or
                            $current.LastWriteTimeUtc -ne $snapshot.LastWriteTimeUtc) {
                            throw "Package input changed while being archived: $relativeName"
                        }
                    }
                }
                finally {
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }

        $zipStream.Flush($true)
    }
    finally {
        $zipStream.Dispose()
    }

    Assert-Archive -ArchivePath $temporaryZipPath -ExpectedEntryNames $expectedEntryNames.ToArray()

    $hash = Get-Sha256Hex -Path $temporaryZipPath
    $hashLine = "$hash  $ZipFileName`n"
    $hashBytes = $Utf8NoBom.GetBytes($hashLine)
    $hashStream = [System.IO.FileStream]::new(
        $temporaryHashPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None,
        4096,
        [System.IO.FileOptions]::WriteThrough)
    try {
        $hashStream.Write($hashBytes, 0, $hashBytes.Length)
        $hashStream.Flush($true)
    }
    finally {
        $hashStream.Dispose()
    }

    if ((Get-Content -LiteralPath $temporaryHashPath -Raw) -cne $hashLine) {
        throw 'Generated SHA-256 sidecar does not have the required exact format.'
    }

    Assert-OwnedOutputTarget -Path $zipPath -ExpectedFullPath (Join-Path $packageDirectory 'StudyReportEvaluator-win-x64.zip')
    Assert-OwnedOutputTarget -Path $hashPath -ExpectedFullPath (Join-Path $packageDirectory 'StudyReportEvaluator-win-x64.zip.sha256')
    [System.IO.File]::Move($temporaryZipPath, $zipPath, $true)
    $zipCommitted = $true
    [System.IO.File]::Move($temporaryHashPath, $hashPath, $true)

    if ((Get-Sha256Hex -Path $zipPath) -cne $hash -or
        (Get-Content -LiteralPath $hashPath -Raw) -cne $hashLine) {
        throw 'Final package and SHA-256 sidecar verification failed.'
    }

    Write-Output "Created unsigned package: $zipPath"
    Write-Output "Created SHA-256 sidecar: $hashPath"
}
catch {
    if ($zipCommitted) {
        Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $hashPath -Force -ErrorAction SilentlyContinue
    }

    throw
}
finally {
    Remove-Item -LiteralPath $temporaryZipPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryHashPath -Force -ErrorAction SilentlyContinue
}
