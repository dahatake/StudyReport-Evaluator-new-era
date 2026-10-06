#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param(
    [switch] $SingleFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$TargetFramework = 'net10.0'
$RuntimeIdentifier = 'win-x64'
$ApplicationName = 'StudyReportEvaluator.App'

function Assert-SupportedHost {
    if ($PSVersionTable.PSEdition -cne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
        throw 'PowerShell Core 7 or later is required. Windows PowerShell is not supported.'
    }

    if (-not $IsWindows) {
        throw 'P-01 publish is supported only on Windows 11 x64.'
    }

    if ([System.Environment]::OSVersion.Version.Build -lt 22000) {
        throw 'P-01 publish requires Windows 11 or later.'
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64 -or
        [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
        throw 'P-01 publish requires an x64 operating system and x64 PowerShell process.'
    }
}

function Resolve-ApplicationPath {
    param(
        [Parameter(Mandatory)]
        [string] $Name
    )

    $command = Get-Command $Name -CommandType Application -ErrorAction Stop |
        Select-Object -First 1
    if ($null -eq $command -or [string]::IsNullOrWhiteSpace([string]$command.Source)) {
        throw "Unable to resolve application command: $Name"
    }

    return [string]$command.Source
}

function Assert-PathWithinRoot {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $Path
    )

    $relative = [System.IO.Path]::GetRelativePath($Root, $Path)
    $parentPrefix = '..' + [System.IO.Path]::DirectorySeparatorChar
    if ([System.IO.Path]::IsPathRooted($relative) -or
        $relative -eq '..' -or
        $relative.StartsWith($parentPrefix, [System.StringComparison]::Ordinal)) {
        throw "Path escapes the repository root: $Path"
    }
}

function Assert-NotReparsePoint {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileSystemInfo] $Item
    )

    if (($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Reparse points are not allowed in P-01 output: $($Item.FullName)"
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

function Assert-OwnedDirectoryPath {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $Path,

        [switch] $IncludeChildren
    )

    $rootPath = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($Root))
    $currentPath = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($Path))
    Assert-PathWithinRoot -Root $rootPath -Path $currentPath
    while ($true) {
        $item = Get-Item -LiteralPath $currentPath -Force -ErrorAction SilentlyContinue
        if ($null -ne $item) {
            Assert-NotReparsePoint -Item $item
            if (-not $item.PSIsContainer) {
                throw "Expected an owned publish directory but found a file: $currentPath"
            }
        }

        if ($currentPath.Equals($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $currentPath = [System.IO.Path]::GetDirectoryName($currentPath)
    }

    if ($IncludeChildren -and (Test-Path -LiteralPath $Path -PathType Container)) {
        # PowerShell does not follow directory links without -FollowSymlink.
        foreach ($item in @(Get-ChildItem -LiteralPath $Path -Force -Recurse)) {
            Assert-NotReparsePoint -Item $item
            Assert-PathWithinRoot -Root $Path -Path $item.FullName
        }
    }
}

function Get-PublishModeArguments {
    param(
        [switch] $SingleFile,
        [string] $PublishProfileFullPath
    )

    if ($SingleFile) {
        if ([string]::IsNullOrWhiteSpace($PublishProfileFullPath) -or
            -not [System.IO.Path]::IsPathFullyQualified($PublishProfileFullPath) -or
            -not (Test-Path -LiteralPath $PublishProfileFullPath -PathType Leaf)) {
            throw 'Single-file publish requires the full path to the App publish profile.'
        }

        Assert-NotReparsePoint -Item (Get-Item -LiteralPath $PublishProfileFullPath -Force)
        # The profile is App-scoped. A global true would add ILLink to Core as well.
        return "--property:PublishProfileFullPath=$PublishProfileFullPath"
    }

    return '--property:PublishSingleFile=false'
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string] $DotNetPath,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    & $DotNetPath @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "dotnet command failed with exit code $exitCode."
    }
}

function Assert-GlobalJsonSdk {
    param(
        [Parameter(Mandatory)]
        [string] $GlobalJsonPath,

        [Parameter(Mandatory)]
        [string] $ActualVersion
    )

    $globalJson = Get-Content -LiteralPath $GlobalJsonPath -Raw | ConvertFrom-Json
    if ([string]$globalJson.sdk.rollForward -cne 'latestPatch' -or
        [bool]$globalJson.sdk.allowPrerelease) {
        throw 'global.json must select a non-prerelease SDK with latestPatch roll-forward.'
    }

    try {
        $required = [System.Version]::Parse([string]$globalJson.sdk.version)
        $actual = [System.Version]::Parse($ActualVersion)
    }
    catch {
        throw "Unable to parse the SDK version selected by global.json: $ActualVersion"
    }

    $requiredFeatureBand = $required.Build - ($required.Build % 100)
    $actualFeatureBand = $actual.Build - ($actual.Build % 100)
    if ($actual.Major -ne $required.Major -or
        $actual.Minor -ne $required.Minor -or
        $actualFeatureBand -ne $requiredFeatureBand -or
        $actual -lt $required) {
        throw "dotnet selected SDK $actual, which is incompatible with global.json version $required and latestPatch roll-forward."
    }
}

function Assert-JsonEquivalent {
    param(
        [AllowNull()]
        [object] $Expected,

        [AllowNull()]
        [object] $Actual,

        [Parameter(Mandatory)]
        [string] $JsonPath
    )

    if ($null -eq $Expected -or $null -eq $Actual) {
        if ($null -ne $Expected -or $null -ne $Actual) {
            throw "Lock comparison failed at $JsonPath."
        }

        return
    }

    if ($Expected -is [System.Collections.IDictionary]) {
        if ($Actual -isnot [System.Collections.IDictionary] -or $Expected.Count -ne $Actual.Count) {
            throw "Lock object comparison failed at $JsonPath."
        }

        foreach ($key in $Expected.Keys) {
            if (-not $Actual.Contains($key)) {
                throw "RID lock is missing $JsonPath.$key."
            }

            Assert-JsonEquivalent -Expected $Expected[$key] -Actual $Actual[$key] -JsonPath "$JsonPath.$key"
        }

        return
    }

    if ($Expected -is [System.Collections.IList] -and $Expected -isnot [string]) {
        if ($Actual -isnot [System.Collections.IList] -or $Expected.Count -ne $Actual.Count) {
            throw "Lock array comparison failed at $JsonPath."
        }

        for ($index = 0; $index -lt $Expected.Count; $index++) {
            Assert-JsonEquivalent -Expected $Expected[$index] -Actual $Actual[$index] -JsonPath "$JsonPath[$index]"
        }

        return
    }

    if (-not [object]::Equals($Expected, $Actual)) {
        throw "Lock value comparison failed at $JsonPath."
    }
}

function Assert-RidLockMatchesCanonicalLock {
    param(
        [Parameter(Mandatory)]
        [string] $CanonicalLockPath,

        [Parameter(Mandatory)]
        [string] $RidLockPath,

        [Parameter(Mandatory)]
        [string] $ProjectName
    )

    if (-not (Test-Path -LiteralPath $RidLockPath -PathType Leaf)) {
        throw "RID lock was not generated for $ProjectName."
    }

    $canonical = Get-Content -LiteralPath $CanonicalLockPath -Raw | ConvertFrom-Json -AsHashtable
    $ridLock = Get-Content -LiteralPath $RidLockPath -Raw | ConvertFrom-Json -AsHashtable
    if ($canonical['version'] -ne 2 -or $ridLock['version'] -ne 2) {
        throw "Version 2 package locks are required for $ProjectName."
    }

    $canonicalTargets = $canonical['dependencies']
    $ridTargets = $ridLock['dependencies']
    if (-not $canonicalTargets.Contains($TargetFramework) -or
        -not $ridTargets.Contains($TargetFramework) -or
        -not $ridTargets.Contains("$TargetFramework/$RuntimeIdentifier") -or
        $ridTargets.Count -ne 2) {
        throw "RID lock targets are invalid for $ProjectName."
    }

    Assert-JsonEquivalent `
        -Expected $canonicalTargets[$TargetFramework] `
        -Actual $ridTargets[$TargetFramework] `
        -JsonPath "$ProjectName.dependencies.$TargetFramework"

    $canonicalPackages = $canonicalTargets[$TargetFramework]
    $ridPackages = $ridTargets["$TargetFramework/$RuntimeIdentifier"]
    foreach ($packageName in $ridPackages.Keys) {
        if (-not $canonicalPackages.Contains($packageName)) {
            throw "RID restore introduced package '$packageName' outside the canonical lock for $ProjectName."
        }

        $canonicalPackage = $canonicalPackages[$packageName]
        $ridPackage = $ridPackages[$packageName]
        foreach ($field in @('type', 'resolved', 'contentHash')) {
            if (-not $ridPackage.Contains($field) -or
                -not $canonicalPackage.Contains($field) -or
                -not [object]::Equals($ridPackage[$field], $canonicalPackage[$field])) {
                throw "RID restore changed $ProjectName package '$packageName' field '$field'."
            }
        }
    }
}

function Assert-SingleFileRidLockMatchesDedicatedLock {
    param(
        [Parameter(Mandatory)]
        [string] $CanonicalLockPath,

        [Parameter(Mandatory)]
        [string] $DedicatedLockPath,

        [Parameter(Mandatory)]
        [string] $RidLockPath
    )

    if (-not (Test-Path -LiteralPath $DedicatedLockPath -PathType Leaf)) {
        throw 'Dedicated App single-file package lock is missing.'
    }

    if (-not (Test-Path -LiteralPath $RidLockPath -PathType Leaf)) {
        throw 'App single-file RID lock was not generated.'
    }

    foreach ($path in @($CanonicalLockPath, $DedicatedLockPath, $RidLockPath)) {
        Assert-NotReparsePoint -Item (Get-Item -LiteralPath $path -Force)
    }

    $canonical = Get-Content -LiteralPath $CanonicalLockPath -Raw | ConvertFrom-Json -AsHashtable
    $dedicated = Get-Content -LiteralPath $DedicatedLockPath -Raw | ConvertFrom-Json -AsHashtable
    $ridLock = Get-Content -LiteralPath $RidLockPath -Raw | ConvertFrom-Json -AsHashtable

    # Compare the entire documents, including field types and the exact RID package set.
    Assert-JsonEquivalent -Expected $dedicated -Actual $ridLock -JsonPath "$ApplicationName.singleFileLock"
    if ($canonical['version'] -ne 2 -or $dedicated.Count -ne 2 -or
        $dedicated['dependencies'].Count -ne 2 -or
        -not $dedicated['dependencies'].Contains($TargetFramework) -or
        -not $dedicated['dependencies'].Contains("$TargetFramework/$RuntimeIdentifier")) {
        throw 'App single-file lock must have version 2 and only the net10.0 and win-x64 targets.'
    }

    Assert-JsonEquivalent -Expected $canonical['version'] -Actual $dedicated['version'] -JsonPath 'singleFileLock.version'
    $canonicalPackages = $canonical['dependencies'][$TargetFramework]
    $expectedPackages = [ordered]@{}
    foreach ($packageName in $canonicalPackages.Keys) {
        $expectedPackages[$packageName] = $canonicalPackages[$packageName]
    }

    $buildOnlyPackage = 'Microsoft.NET.ILLink.Tasks'
    if ($canonicalPackages.Contains($buildOnlyPackage)) {
        throw 'The single-file build-only ILLink package must not enter the canonical App lock.'
    }

    $expectedPackages[$buildOnlyPackage] = [ordered]@{
        type = 'Direct'
        requested = '[10.0.11, )'
        resolved = '10.0.11'
        contentHash = 'IBf7lbovvjGWVWXZX5cJ/cO0WXbId0Zq4BuSeT94mGZuOAP66oMeH9PTBZ9Jpp3Jb6jtK0qm/NyUbPRo1gC/wQ=='
    }
    Assert-JsonEquivalent -Expected $expectedPackages -Actual $dedicated['dependencies'][$TargetFramework] `
        -JsonPath "$ApplicationName.dependencies.$TargetFramework"

    foreach ($package in $dedicated['dependencies']["$TargetFramework/$RuntimeIdentifier"].GetEnumerator()) {
        if (-not $canonicalPackages.Contains($package.Key)) {
            throw "Single-file RID restore introduced a package outside the canonical lock: $($package.Key)"
        }

        Assert-JsonEquivalent -Expected $canonicalPackages[$package.Key] -Actual $package.Value `
            -JsonPath "$ApplicationName.dependencies.$RuntimeIdentifier.$($package.Key)"
    }
}

function Assert-SingleFileLockHashes {
    param(
        [Parameter(Mandatory)]
        [System.Collections.IDictionary] $Hashes
    )

    foreach ($path in $Hashes.Keys) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Single-file publish changed a protected package lock: $path"
        }

        Assert-NotReparsePoint -Item (Get-Item -LiteralPath $path -Force)
        if ((Get-Sha256Hex -Path $path) -cne $Hashes[$path]) {
            throw "Single-file publish changed a protected package lock: $path"
        }
    }
}

function Assert-Amd64PortableExecutable {
    param(
        [Parameter(Mandatory)]
        [string] $ExecutablePath
    )

    $bytes = [System.IO.File]::ReadAllBytes($ExecutablePath)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Application executable is not a valid PE file: $ExecutablePath"
    }

    $peOffset = [System.BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 6 -gt $bytes.Length -or
        $bytes[$peOffset] -ne 0x50 -or
        $bytes[$peOffset + 1] -ne 0x45 -or
        $bytes[$peOffset + 2] -ne 0 -or
        $bytes[$peOffset + 3] -ne 0) {
        throw "Application executable has an invalid PE header: $ExecutablePath"
    }

    $machine = [System.BitConverter]::ToUInt16($bytes, $peOffset + 4)
    if ($machine -ne 0x8664) {
        throw "Application executable is not AMD64 (machine 0x$($machine.ToString('X4')))."
    }
}

function Get-CopilotCliVersion {
    param(
        [Parameter(Mandatory)]
        [string] $DotNetPath,

        [Parameter(Mandatory)]
        [string] $ProjectPath
    )

    $output = & $DotNetPath msbuild $ProjectPath `
        -nologo `
        -getProperty:CopilotCliVersion `
        -property:RuntimeIdentifier=$RuntimeIdentifier
    $exitCode = $LASTEXITCODE
    $version = ([string](@($output) -join "`n")).Trim()
    if ($exitCode -ne 0 -or
        $version -notmatch '^[0-9][A-Za-z0-9._+-]{0,127}$') {
        throw 'Unable to resolve a safe Copilot CLI version from the pinned SDK package.'
    }

    return $version
}

function Get-NpmCopilotCliBinary {
    param(
        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [string] $DestinationDirectory
    )

    $npmCommand = Get-Command npm.cmd -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $npmCommand) {
        return $null
    }

    [void][System.IO.Directory]::CreateDirectory($DestinationDirectory)
    $packageName = '@github/copilot-win32-x64'
    $packageSpec = "$packageName@$Version"
    $expectedPackageShasum = '75265752e8f23150a17cd8f09c5e757bd9ff9374'
    $packOutput = & $npmCommand.Source pack $packageSpec `
        --ignore-scripts `
        --fetch-timeout=120000 `
        --fetch-retries=2 `
        --pack-destination $DestinationDirectory `
        --json
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "npm failed to acquire the pinned Copilot CLI package with exit code $exitCode."
    }

    try {
        $packRecords = @(($packOutput | Out-String | ConvertFrom-Json))
    }
    catch {
        throw 'npm returned an invalid package manifest for the Copilot CLI.'
    }

    if ($packRecords.Count -ne 1) {
        throw 'npm did not return exactly one Copilot CLI package record.'
    }

    $packRecord = $packRecords[0]
    if ([string]$packRecord.name -cne $packageName -or
        [string]$packRecord.version -cne $Version -or
        [string]$packRecord.integrity -notmatch '^sha512-[A-Za-z0-9+/]+={0,2}$' -or
        [string]$packRecord.shasum -cne $expectedPackageShasum) {
        throw 'npm returned Copilot CLI package metadata that does not match the pinned package.'
    }

    $archiveName = [string]$packRecord.filename
    if ([string]::IsNullOrWhiteSpace($archiveName) -or
        [System.IO.Path]::GetFileName($archiveName) -cne $archiveName -or
        [System.IO.Path]::GetExtension($archiveName) -cne '.tgz') {
        throw 'npm returned an unsafe Copilot CLI archive name.'
    }

    $archivePath = Join-Path $DestinationDirectory $archiveName
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        throw 'The pinned Copilot CLI archive was not created.'
    }

    $tarPath = Join-Path $env:SystemRoot 'System32\tar.exe'
    if (-not (Test-Path -LiteralPath $tarPath -PathType Leaf)) {
        throw 'Windows system tar is required to inspect the Copilot CLI package.'
    }

    $archiveEntries = @(& $tarPath -tzf $archivePath)
    if ($LASTEXITCODE -ne 0 -or $archiveEntries.Count -eq 0) {
        throw 'The Copilot CLI archive could not be listed.'
    }

    foreach ($archiveEntry in $archiveEntries) {
        $normalized = ([string]$archiveEntry).Replace('\', '/').TrimEnd('/')
        if ([string]::IsNullOrWhiteSpace($normalized) -or
            -not $normalized.StartsWith('package/', [System.StringComparison]::Ordinal)) {
            throw "Unsafe Copilot CLI archive entry: $archiveEntry"
        }

        foreach ($segment in $normalized.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries)) {
            if ($segment -eq '.' -or $segment -eq '..' -or
                $segment.Contains(':', [System.StringComparison]::Ordinal)) {
                throw "Unsafe Copilot CLI archive entry: $archiveEntry"
            }
        }
    }

    $extractionDirectory = Join-Path $DestinationDirectory 'extracted'
    [void][System.IO.Directory]::CreateDirectory($extractionDirectory)
    & $tarPath -xzf $archivePath -C $extractionDirectory
    if ($LASTEXITCODE -ne 0) {
        throw 'The Copilot CLI archive could not be extracted.'
    }

    foreach ($item in @(Get-ChildItem -LiteralPath $extractionDirectory -Force -Recurse)) {
        Assert-NotReparsePoint -Item $item
    }

    $cliPath = Join-Path $extractionDirectory 'package\copilot.exe'
    if (-not (Test-Path -LiteralPath $cliPath -PathType Leaf) -or
        (Get-Item -LiteralPath $cliPath).Length -le 0) {
        throw 'The pinned Copilot CLI binary is missing or empty.'
    }

    Assert-Amd64PortableExecutable -ExecutablePath $cliPath
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($cliPath)
    if ([string]$versionInfo.ProductVersion -cne $Version -and
        [string]$versionInfo.FileVersion -cne $Version) {
        throw 'The Copilot CLI binary version does not match the pinned SDK runtime version.'
    }

    return [System.IO.Path]::GetFullPath($cliPath)
}

function Assert-BundledCopilotRuntime {
    param(
        [Parameter(Mandatory)]
        [string] $PublishDirectory
    )

    $manifestPath = Join-Path $PublishDirectory 'copilot-runtime.json'
    $expectedRelativePath = 'runtimes/win-x64/native/copilot.exe'
    $cliPath = Join-Path $PublishDirectory $expectedRelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $cliPath -PathType Leaf)) {
        throw 'The bundled Copilot CLI manifest or binary is missing.'
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        throw 'The bundled Copilot CLI manifest is invalid JSON.'
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
        [string]$manifest.cliRelativePath -cne $expectedRelativePath -or
        [string]$manifest.cliVersion -notmatch '^[0-9][A-Za-z0-9._+-]{0,127}$' -or
        [string]$manifest.sdkVersion -notmatch '^[0-9][A-Za-z0-9._+-]{0,127}$' -or
        [string]$manifest.cliSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'The bundled Copilot CLI manifest violates the release contract.'
    }

    $cliItem = Get-Item -LiteralPath $cliPath -Force
    Assert-NotReparsePoint -Item $cliItem
    if ($cliItem.Length -le 0 -or
        (Get-Sha256Hex -Path $cliPath) -cne ([string]$manifest.cliSha256).ToUpperInvariant()) {
        throw 'The bundled Copilot CLI does not match its manifest hash.'
    }

    Assert-Amd64PortableExecutable -ExecutablePath $cliPath
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($cliPath)
    if ([string]$versionInfo.ProductVersion -cne [string]$manifest.cliVersion -and
        [string]$versionInfo.FileVersion -cne [string]$manifest.cliVersion) {
        throw 'The bundled Copilot CLI does not match its manifest version.'
    }

    $sdkPath = Join-Path $PublishDirectory 'GitHub.Copilot.SDK.dll'
    $sdkVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($sdkPath)
    $sdkPackageVersion = ([string]$sdkVersionInfo.ProductVersion).Split('+', 2)[0]
    if ($sdkPackageVersion -cne [string]$manifest.sdkVersion) {
        throw 'The bundled Copilot manifest does not match the published SDK package version.'
    }
}

function Get-SingleFileDocumentationPaths {
    # Same closed public allowlist as WindowsSingleFile.pubxml / package-windows.ps1.
    return @(
        'README.md',
        'LICENSE',
        'docs/README.md',
        'docs/getting-started.md',
        'docs/features.md',
        'docs/custom-evaluator-guide.md',
        'docs/technical-guid.md',
        'docs/prompt-launch.md',
        'docs/privacy-and-data-handling.md',
        'docs/troubleshooting.md',
        'docs/settings.md',
        'docs/third-party-notices.md',
        'docs/result-excel-description.md',
        'images/README.md',
        'images/architecture-overview.svg',
        'images/technical-architecture.svg',
        'images/evaluation-message-flow.svg',
        'images/01-input-workbook.png',
        'images/02-input-mapping.png',
        'images/03-design-knowledge.png',
        'images/04-design-custom-prompt.png',
        'images/05-execution-auto.png',
        'images/06-results-review.png',
        'images/07-output-export.png',
        'images/08-settings.png'
    )
}

function Assert-SafePublishLayout {
    param(
        [Parameter(Mandatory)]
        [string] $PublishDirectory,

        [switch] $ExtractedBundle
    )

    $rootItem = Get-Item -LiteralPath $PublishDirectory -Force
    Assert-NotReparsePoint -Item $rootItem

    $items = @(Get-ChildItem -LiteralPath $PublishDirectory -Force -Recurse)
    $files = @($items | Where-Object { -not $_.PSIsContainer })
    if ($files.Count -eq 0) {
        throw 'Publish output is empty.'
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

    $allowedExtractedContent = [System.Collections.Generic.HashSet[string]]::new(
        [string[]](@(Get-SingleFileDocumentationPaths) + @(
            "$ApplicationName.runtimeconfig.json",
            "$ApplicationName.deps.json",
            'copilot-runtime.json',
            'runtimes/win-x64/native/copilot.exe'
        )),
        [System.StringComparer]::Ordinal)

    foreach ($item in $items) {
        Assert-NotReparsePoint -Item $item
        $relative = [System.IO.Path]::GetRelativePath($PublishDirectory, $item.FullName)
        $parentPrefix = '..' + [System.IO.Path]::DirectorySeparatorChar
        if ([System.IO.Path]::IsPathRooted($relative) -or
            $relative -eq '..' -or
            $relative.StartsWith($parentPrefix, [System.StringComparison]::Ordinal)) {
            throw "Publish entry escapes its root: $relative"
        }

        $normalized = $relative.Replace('\', '/')
        if (-not $seen.Add($normalized)) {
            throw "Duplicate or case-colliding publish entry: $normalized"
        }

        foreach ($segment in $normalized.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries)) {
            if ($segment -eq '.' -or $segment -eq '..' -or $forbiddenSegments.Contains($segment)) {
                throw "Forbidden publish path segment: $normalized"
            }
        }

        foreach ($marker in $forbiddenMarkers) {
            if ($normalized.Contains($marker, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Forbidden test or Office dependency marker in publish output: $normalized"
            }
        }

        if (-not $item.PSIsContainer) {
            if ($item.Length -le 0) {
                throw "Zero-byte publish file is not allowed: $normalized"
            }

            if ($forbiddenExtensions.Contains([System.IO.Path]::GetExtension($item.Name)) -or
                $item.Name -ieq '.env' -or
                $item.Name -ieq 'setting.txt' -or
                [System.Text.RegularExpressions.Regex]::IsMatch(
                    $item.Name,
                    '(^|[._-])(secret|password|credential|token)([._-]|$)',
                    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                throw "Forbidden source, symbol, input, or secret file in publish output: $normalized"
            }

            if ($ExtractedBundle -and
                -not $allowedExtractedContent.Contains($normalized) -and
                $item.Extension -ine '.dll') {
                throw "Unexpected file in extracted single-file publish output: $normalized"
            }
        }
    }

    $requiredFiles = @(
        "$ApplicationName.exe",
        "$ApplicationName.dll",
        "$ApplicationName.runtimeconfig.json",
        "$ApplicationName.deps.json",
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
        'System.Private.CoreLib.dll'
    )
    if ($ExtractedBundle) {
        # The standard single-file host statically embeds these native host/runtime components.
        # Their absence from extraction is not a missing self-contained dependency.
        $requiredFiles = @($requiredFiles | Where-Object {
            $_ -cnotin @("$ApplicationName.exe", 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')
        }) + @(Get-SingleFileDocumentationPaths)
    }

    foreach ($requiredFile in $requiredFiles) {
        $requiredPath = Join-Path $PublishDirectory $requiredFile
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf) -or
            (Get-Item -LiteralPath $requiredPath).Length -le 0) {
            throw "Required self-contained publish file is missing or empty: $requiredFile"
        }
    }

    Assert-BundledCopilotRuntime -PublishDirectory $PublishDirectory

    if (Test-Path -LiteralPath (Join-Path $PublishDirectory 'copilot.exe')) {
        throw 'The Copilot CLI must be stored only under the pinned RID runtime directory.'
    }

    $runtimeConfigPath = Join-Path $PublishDirectory "$ApplicationName.runtimeconfig.json"
    $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
    $runtimeOptionsProperty = $runtimeConfig.PSObject.Properties['runtimeOptions']
    if ($null -eq $runtimeOptionsProperty) {
        throw 'runtimeconfig.json is missing runtimeOptions.'
    }

    $runtimeOptions = $runtimeOptionsProperty.Value
    if ($null -ne $runtimeOptions.PSObject.Properties['framework']) {
        throw 'Self-contained runtimeconfig.json must not declare a framework property.'
    }

    $includedFrameworksProperty = $runtimeOptions.PSObject.Properties['includedFrameworks']
    if ($null -eq $includedFrameworksProperty) {
        throw 'Self-contained runtimeconfig.json must declare includedFrameworks.'
    }

    $includedFrameworks = @($includedFrameworksProperty.Value)
    $netCoreFramework = @($includedFrameworks | Where-Object { $_.name -ceq 'Microsoft.NETCore.App' })
    if ($netCoreFramework.Count -ne 1 -or
        -not ([string]$netCoreFramework[0].version).StartsWith('10.0.', [System.StringComparison]::Ordinal)) {
        throw 'Self-contained runtimeconfig.json must include Microsoft.NETCore.App 10.0.x.'
    }

    if ($ExtractedBundle -and
        ($null -ne $runtimeOptions.PSObject.Properties['frameworks'] -or
            [string]$runtimeOptions.tfm -cne $TargetFramework -or
            $includedFrameworks.Count -ne 1 -or
            [string]$netCoreFramework[0].version -cne '10.0.11')) {
        throw 'Single-file runtimeconfig.json must include only the pinned Microsoft.NETCore.App 10.0.11 runtime.'
    }

    $depsPath = Join-Path $PublishDirectory "$ApplicationName.deps.json"
    $depsText = Get-Content -LiteralPath $depsPath -Raw
    foreach ($marker in $forbiddenMarkers) {
        if ($depsText.Contains($marker, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Forbidden test or Office dependency marker in deps.json: $marker"
        }
    }

    $deps = $depsText | ConvertFrom-Json
    if ([string]$deps.runtimeTarget.name -cne '.NETCoreApp,Version=v10.0/win-x64') {
        throw "deps.json runtime target is not win-x64: $($deps.runtimeTarget.name)"
    }

    $libraryNames = @($deps.libraries.PSObject.Properties.Name)
    foreach ($requiredPrefix in @(
        'StudyReportEvaluator.App/',
        'StudyReportEvaluator.Core/',
        'DocumentFormat.OpenXml/',
        'GitHub.Copilot.SDK/',
        'Avalonia/',
        'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/')) {
        $matchingLibraries = @($libraryNames | Where-Object { $_.StartsWith($requiredPrefix, [System.StringComparison]::Ordinal) })
        if ($matchingLibraries.Count -eq 0) {
            throw "deps.json is missing required library '$requiredPrefix'."
        }

        if ($ExtractedBundle -and $matchingLibraries.Count -ne 1) {
            throw "Single-file deps.json has ambiguous library identities: $requiredPrefix"
        }
    }

    if ($ExtractedBundle) {
        $manifest = Get-Content -LiteralPath (Join-Path $PublishDirectory 'copilot-runtime.json') -Raw | ConvertFrom-Json
        foreach ($library in @(
            "GitHub.Copilot.SDK/$($manifest.sdkVersion)",
            "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/$($netCoreFramework[0].version)")) {
            if ($library -cnotin $libraryNames) {
                throw "Single-file deps.json identity does not match extracted runtime metadata: $library"
            }
        }
    }
    else {
        Assert-Amd64PortableExecutable -ExecutablePath (Join-Path $PublishDirectory "$ApplicationName.exe")
    }
}

function Remove-PublishSymbols {
    param(
        [Parameter(Mandatory)]
        [string] $PublishDirectory
    )

    $items = @(Get-ChildItem -LiteralPath $PublishDirectory -Force -Recurse)
    foreach ($item in $items) {
        Assert-NotReparsePoint -Item $item
    }

    foreach ($symbol in @($items | Where-Object { -not $_.PSIsContainer -and $_.Extension -ieq '.pdb' })) {
        Remove-Item -LiteralPath $symbol.FullName -Force
    }
}

function Assert-SingleFilePublishLayout {
    param(
        [Parameter(Mandatory)]
        [string] $PublishDirectory
    )

    $rootItem = Get-Item -LiteralPath $PublishDirectory -Force
    Assert-NotReparsePoint -Item $rootItem
    if (-not $rootItem.PSIsContainer) {
        throw 'Single-file publish output must be a directory.'
    }

    $entries = @(Get-ChildItem -LiteralPath $PublishDirectory -Force)
    foreach ($entry in $entries) {
        Assert-NotReparsePoint -Item $entry
    }

    if ($entries.Count -ne 1 -or $entries[0].PSIsContainer -or
        $entries[0].Name -cne "$ApplicationName.exe") {
        throw 'Single-file publish output must contain only the application EXE, with no sidecars or directories.'
    }

    if ($entries[0].Length -le 0) {
        throw 'Single-file application EXE must not be empty.'
    }

    Assert-Amd64PortableExecutable -ExecutablePath $entries[0].FullName
}

function Get-PublishProductVersion {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot
    )

    $props = [xml](Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Directory.Build.props') -Raw)
    $prefixes = @($props.SelectNodes('/Project/PropertyGroup/VersionPrefix'))
    $suffixes = @($props.SelectNodes('/Project/PropertyGroup/VersionSuffix'))
    if ($prefixes.Count -ne 1 -or $suffixes.Count -ne 1 -or
        $prefixes[0].InnerText -notmatch '^\d+\.\d+\.\d+$' -or
        $suffixes[0].InnerText -notmatch '^[0-9A-Za-z.-]*$') {
        throw 'Unable to resolve the canonical product version for single-file validation.'
    }

    $version = $prefixes[0].InnerText
    if (-not [string]::IsNullOrEmpty($suffixes[0].InnerText)) {
        $version += '-' + $suffixes[0].InnerText
    }

    return $version
}

function Assert-PublishBinaryVersion {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $ExpectedVersion,

        [switch] $Managed
    )

    $fileVersion = $ExpectedVersion.Split('-', 2)[0] + '.0'
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if (([string]$versionInfo.ProductVersion).Split('+', 2)[0] -cne $ExpectedVersion -or
        [string]$versionInfo.FileVersion -cne $fileVersion) {
        throw "Single-file binary does not match the canonical product version: $([System.IO.Path]::GetFileName($Path))"
    }

    if ($Managed) {
        # Metadata inspection only; do not load or execute the published assembly.
        $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($Path)
        if ($assemblyName.Name -cne [System.IO.Path]::GetFileNameWithoutExtension($Path) -or
            $assemblyName.Version.ToString() -cne $fileVersion) {
            throw 'Extracted managed assembly identity does not match the canonical product version.'
        }
    }
}

function Set-SingleFileProbeEnvironment {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.ProcessStartInfo] $StartInfo,

        [Parameter(Mandatory)]
        [string] $ProbeDirectory
    )

    # Never inherit credentials, startup hooks, profiler settings, or user CLI configuration.
    $StartInfo.Environment.Clear()
    foreach ($name in @('SystemRoot', 'WINDIR', 'SystemDrive', 'ComSpec')) {
        $value = [System.Environment]::GetEnvironmentVariable($name)
        if ($null -ne $value) {
            $StartInfo.Environment[$name] = $value
        }
    }

    foreach ($name in @('TEMP', 'TMP', 'USERPROFILE', 'HOME', 'LOCALAPPDATA', 'APPDATA', 'COPILOT_HOME')) {
        $ownedPath = Join-Path $ProbeDirectory $name
        Assert-OwnedDirectoryPath -Root $ProbeDirectory -Path $ownedPath
        [void][System.IO.Directory]::CreateDirectory($ownedPath)
        $StartInfo.Environment[$name] = $ownedPath
    }

    $StartInfo.WorkingDirectory = Join-Path $ProbeDirectory 'cwd'
    Assert-OwnedDirectoryPath -Root $ProbeDirectory -Path $StartInfo.WorkingDirectory
    [void][System.IO.Directory]::CreateDirectory($StartInfo.WorkingDirectory)
    $StartInfo.Environment['PATH'] = Join-Path $env:SystemRoot 'System32'
    $StartInfo.Environment['DOTNET_ROOT'] = Join-Path $ProbeDirectory '__no-installed-dotnet__'
    $StartInfo.Environment['DOTNET_ROOT_X64'] = Join-Path $ProbeDirectory '__no-installed-dotnet-x64__'
    $StartInfo.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $StartInfo.Environment['DOTNET_DISABLE_GUI_ERRORS'] = '1'
    $StartInfo.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $ProbeDirectory 'TEMP\.net'
    $StartInfo.Environment['DOTNET_HOST_TRACE'] = '1'
    $StartInfo.Environment['DOTNET_HOST_TRACE_VERBOSITY'] = '4'
    $StartInfo.Environment['DOTNET_HOST_TRACEFILE'] = Join-Path $ProbeDirectory 'host-trace.log'
    $StartInfo.Environment['STUDY_REPORT_EVALUATOR_AUTO_COPILOT_LOGIN'] = '0'
}

function Get-SingleFileExtractionDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $ProbeDirectory
    )

    $cacheRoot = Join-Path $ProbeDirectory 'TEMP\.net'
    Assert-OwnedDirectoryPath -Root $ProbeDirectory -Path $cacheRoot -IncludeChildren
    $tracePath = Join-Path $ProbeDirectory 'host-trace.log'
    if (-not (Test-Path -LiteralPath $tracePath -PathType Leaf)) {
        throw 'Single-file host trace is missing.'
    }

    Assert-NotReparsePoint -Item (Get-Item -LiteralPath $tracePath -Force)
    $prefix = 'Property APP_CONTEXT_BASE_DIRECTORY = '
    $basePaths = @(foreach ($line in [System.IO.File]::ReadAllLines($tracePath)) {
        if ($line.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
            $line.Substring($prefix.Length).Trim()
        }
    })
    if ($basePaths.Count -ne 1 -or -not [System.IO.Path]::IsPathFullyQualified($basePaths[0])) {
        throw 'Single-file host trace must contain exactly one absolute APP_CONTEXT_BASE_DIRECTORY.'
    }

    $appBase = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($basePaths[0]))
    Assert-OwnedDirectoryPath -Root $cacheRoot -Path $appBase
    $segments = [System.IO.Path]::GetRelativePath($cacheRoot, $appBase).Replace('\', '/').Split('/')
    if ($segments.Count -ne 2 -or $segments[0] -cne $ApplicationName) {
        throw 'Single-file application base is not the owned standard-host bundle directory.'
    }

    $applications = @(Get-ChildItem -LiteralPath $cacheRoot -Force)
    $bundles = @(Get-ChildItem -LiteralPath (Join-Path $cacheRoot $ApplicationName) -Force)
    if ($applications.Count -ne 1 -or -not $applications[0].PSIsContainer -or
        $applications[0].Name -cne $ApplicationName -or
        $bundles.Count -ne 1 -or -not $bundles[0].PSIsContainer -or
        -not $bundles[0].FullName.Equals($appBase, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Single-file extraction must contain exactly one application and one bundle directory.'
    }

    return $appBase
}

function Assert-NoStartupChildProcesses {
    param(
        [Parameter(Mandatory)]
        [int] $ProcessId
    )

    # A bounded snapshot, not an event audit: short-lived children remain a P06 concern.
    # Unlike Win32_ProcessStartTrace subscription, this does not require elevation.
    $children = @(Get-CimInstance -ClassName Win32_Process -Filter "ParentProcessId = $ProcessId" `
        -Property ProcessId -OperationTimeoutSec 5 -ErrorAction Stop)
    if ($children.Count -ne 0) {
        throw 'Single-file startup unexpectedly has child processes; CLI, login and AI must not start automatically.'
    }
}

function Wait-NoStartupChildProcesses {
    param(
        [Parameter(Mandatory)]
        [int] $ProcessId,

        [int] $TimeoutSeconds = 30
    )

    # The startup login-status check briefly runs the bundled CLI; only a child that outlives it fails.
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        try {
            Assert-NoStartupChildProcesses -ProcessId $ProcessId
            return
        }
        catch {
            if ([DateTimeOffset]::UtcNow -ge $deadline) {
                throw
            }

            Start-Sleep -Milliseconds 100
        }
    }
}

function Wait-OwnedProbeProcessesExit {
    param(
        [Parameter(Mandatory)]
        [string] $ProbeDirectory,

        [int] $TimeoutSeconds = 30
    )

    # The bundled CLI started by the startup status check can outlive the app briefly and keeps
    # inherited probe handles (host trace) open; only processes running from this probe are awaited.
    $prefix = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($ProbeDirectory)) +
        [System.IO.Path]::DirectorySeparatorChar
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        $owned = @(Get-CimInstance -ClassName Win32_Process -Property ProcessId, ExecutablePath -OperationTimeoutSec 5 -ErrorAction Stop |
            Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) })
        if ($owned.Count -eq 0) {
            return
        }

        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            throw 'Processes started from the single-file probe did not exit after the application closed.'
        }

        Start-Sleep -Milliseconds 250
    }
}

function Invoke-TransientFileOperation {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Operation,

        [int] $TimeoutSeconds = 30
    )

    # Antivirus scans or an exiting child can briefly hold owned probe files after the app ends.
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        try {
            & $Operation
            return
        }
        catch {
            $transient = $_.Exception -is [System.IO.IOException] -or $_.Exception -is [System.UnauthorizedAccessException]
            if (-not $transient -or [DateTimeOffset]::UtcNow -ge $deadline) {
                throw
            }

            Start-Sleep -Milliseconds 250
        }
    }
}

function Assert-ApplicationLaunch {
    param(
        [Parameter(Mandatory)]
        [string] $PublishDirectory,

        [string] $SingleFileProbeDirectory
    )

    $executablePath = Join-Path $PublishDirectory "$ApplicationName.exe"
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $executablePath
    $startInfo.WorkingDirectory = $PublishDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $false
    $startInfo.Environment['DOTNET_ROOT'] = Join-Path $PublishDirectory '__no-installed-dotnet__'
    $startInfo.Environment['DOTNET_ROOT_X64'] = Join-Path $PublishDirectory '__no-installed-dotnet-x64__'
    $startInfo.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'

    if (-not [string]::IsNullOrWhiteSpace($SingleFileProbeDirectory)) {
        Set-SingleFileProbeEnvironment -StartInfo $startInfo -ProbeDirectory $SingleFileProbeDirectory
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $started = $false
    try {
        $started = $process.Start()
        if (-not $started) {
            throw 'The self-contained application process did not start.'
        }

        try {
            $idleTimeout = if ([string]::IsNullOrWhiteSpace($SingleFileProbeDirectory)) { 5000 } else { 20000 }
            [void]$process.WaitForInputIdle($idleTimeout)
        }
        catch [System.InvalidOperationException] {
            # A startup liveness probe below remains authoritative.
        }

        if ($process.WaitForExit(1500)) {
            throw "The self-contained application exited during startup with code $($process.ExitCode)."
        }

        if (-not [string]::IsNullOrWhiteSpace($SingleFileProbeDirectory)) {
            Wait-NoStartupChildProcesses -ProcessId $process.Id
            # No nonzero window handle requirement on headless CI; this proves liveness, not UI readiness.
            Write-Verbose 'Single-file startup liveness checked with an isolated environment; not GUI or clean-host evidence.'
        }

        $closedGracefully = $process.CloseMainWindow() -and $process.WaitForExit(5000)
        if ($closedGracefully) {
            if ($process.ExitCode -ne 0) {
                throw "The self-contained application exited with code $($process.ExitCode)."
            }

            return
        }

        $process.Kill($true)
        if (-not $process.WaitForExit(5000)) {
            throw 'The self-contained application did not stop during controlled cleanup.'
        }
    }
    finally {
        if ($started) {
            try {
                if (-not $process.HasExited) {
                    $process.Kill($true)
                    [void]$process.WaitForExit(5000)
                }
            }
            catch [System.InvalidOperationException] {
                # The process already ended between checks.
            }
        }

        $process.Dispose()
    }
}

function Assert-SingleFileApplicationLaunch {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRoot,

        [Parameter(Mandatory)]
        [string] $PublishDirectory,

        [Parameter(Mandatory)]
        [string] $ProbeDirectory
    )

    Assert-OwnedDirectoryPath -Root $RepositoryRoot -Path $PublishDirectory -IncludeChildren
    Assert-OwnedDirectoryPath -Root $RepositoryRoot -Path $ProbeDirectory
    if (Test-Path -LiteralPath $ProbeDirectory) {
        throw 'Single-file launch verification requires a fresh owned probe directory.'
    }

    # Only the freshly published EXE is launched. Contents are inspected AFTER standard-host
    # extraction, not authenticated by a proprietary bundle parser before execution.
    Assert-SingleFilePublishLayout -PublishDirectory $PublishDirectory
    $executablePath = Join-Path $PublishDirectory "$ApplicationName.exe"
    $expectedVersion = Get-PublishProductVersion -RepositoryRoot $RepositoryRoot
    Assert-PublishBinaryVersion -Path $executablePath -ExpectedVersion $expectedVersion
    $exeHash = Get-Sha256Hex -Path $executablePath
    [void][System.IO.Directory]::CreateDirectory($ProbeDirectory)
    try {
        $isolatedDirectory = Join-Path $ProbeDirectory 'application'
        [void][System.IO.Directory]::CreateDirectory($isolatedDirectory)
        $isolatedExe = Join-Path $isolatedDirectory "$ApplicationName.exe"
        [System.IO.File]::Copy($executablePath, $isolatedExe, $false)
        if ((Get-Sha256Hex -Path $isolatedExe) -cne $exeHash) {
            throw 'Isolated single-file EXE copy differs from the publish output.'
        }

        Assert-ApplicationLaunch -PublishDirectory $isolatedDirectory -SingleFileProbeDirectory $ProbeDirectory
        Wait-OwnedProbeProcessesExit -ProbeDirectory $ProbeDirectory
        $appBase = Get-SingleFileExtractionDirectory -ProbeDirectory $ProbeDirectory
        Assert-SafePublishLayout -PublishDirectory $appBase -ExtractedBundle
        foreach ($assembly in @("$ApplicationName.dll", 'StudyReportEvaluator.Core.dll')) {
            Assert-PublishBinaryVersion -Path (Join-Path $appBase $assembly) -ExpectedVersion $expectedVersion -Managed
        }

        $deps = Get-Content -LiteralPath (Join-Path $appBase "$ApplicationName.deps.json") -Raw | ConvertFrom-Json
        $canonical = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'src\StudyReportEvaluator.App\packages.lock.json') -Raw |
            ConvertFrom-Json -AsHashtable
        $sdkVersion = $canonical['dependencies'][$TargetFramework]['GitHub.Copilot.SDK']['resolved']
        foreach ($library in @("$ApplicationName/$expectedVersion", "StudyReportEvaluator.Core/$expectedVersion", "GitHub.Copilot.SDK/$sdkVersion")) {
            if ($library -cnotin @($deps.libraries.PSObject.Properties.Name)) {
                throw "Extracted deps.json does not match the canonical product/SDK version: $library"
            }
        }

        foreach ($document in @(Get-SingleFileDocumentationPaths)) {
            if ((Get-Sha256Hex -Path (Join-Path $appBase $document)) -cne
                (Get-Sha256Hex -Path (Join-Path $RepositoryRoot $document))) {
                throw "Single-file documentation differs from its allowed repository source: $document"
            }
        }

        Assert-SingleFilePublishLayout -PublishDirectory $PublishDirectory
        Assert-SingleFilePublishLayout -PublishDirectory $isolatedDirectory
        if ((Get-Sha256Hex -Path $executablePath) -cne $exeHash -or
            (Get-Sha256Hex -Path $isolatedExe) -cne $exeHash) {
            throw 'The single-file EXE changed during launch verification.'
        }
    }
    finally {
        # Includes host trace (paths only, no inherited secrets), .net cache and isolated home.
        # Never clean the real user cache or retain/upload the trace as release evidence.
        Assert-OwnedDirectoryPath -Root $RepositoryRoot -Path $ProbeDirectory
        $tracePath = Join-Path $ProbeDirectory 'host-trace.log'
        if (Test-Path -LiteralPath $tracePath -PathType Leaf) {
            # A nonrecursive removal also removes a file link itself, never its target.
            Invoke-TransientFileOperation { Remove-Item -LiteralPath $tracePath -Force -ErrorAction Stop }
        }

        Assert-OwnedDirectoryPath -Root $RepositoryRoot -Path $ProbeDirectory -IncludeChildren
        Invoke-TransientFileOperation { Remove-Item -LiteralPath $ProbeDirectory -Recurse -Force -ErrorAction Stop }
    }
}

Assert-SupportedHost

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'StudyReportEvaluator.slnx'
$globalJsonPath = Join-Path $repositoryRoot 'global.json'
$appProjectPath = Join-Path $repositoryRoot 'src\StudyReportEvaluator.App\StudyReportEvaluator.App.csproj'
$coreProjectDirectory = Join-Path $repositoryRoot 'src\StudyReportEvaluator.Core'
$appProjectDirectory = Join-Path $repositoryRoot 'src\StudyReportEvaluator.App'
$singleFileProfilePath = [System.IO.Path]::GetFullPath((Join-Path $appProjectDirectory 'Properties\PublishProfiles\WindowsSingleFile.pubxml'))
$dedicatedLockPath = Join-Path $appProjectDirectory 'packages.win-x64-singlefile.lock.json'
$packageDirectory = Join-Path $repositoryRoot 'artifacts\package'
$publishParent = Join-Path $repositoryRoot 'artifacts\package\publish'
$publishVariant = if ($SingleFile) { "$RuntimeIdentifier-singlefile" } else { $RuntimeIdentifier }
$lockScope = if ($SingleFile) { 'P02' } else { 'P01' }
$finalPublishDirectory = Join-Path $publishParent $publishVariant
$runId = [System.Guid]::NewGuid().ToString('N')
$temporaryPublishDirectory = Join-Path $publishParent ('.' + $publishVariant + '-' + $runId)
$temporaryLockRelativePath = "obj\$lockScope\$runId\packages.$RuntimeIdentifier.lock.json"
$temporaryCopilotDirectory = Join-Path $packageDirectory ('.copilot-cli-' + $runId)
$temporaryProbeDirectory = Join-Path $publishParent ('.win-x64-singlefile-probe-' + $runId)

foreach ($requiredPath in @($solutionPath, $globalJsonPath, $appProjectPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required repository file is missing: $requiredPath"
    }
}

if ($SingleFile) {
    if (-not (Test-Path -LiteralPath $dedicatedLockPath -PathType Leaf)) {
        throw 'Dedicated App single-file package lock is missing.'
    }

    foreach ($ownedPath in @($publishParent, $finalPublishDirectory, $temporaryPublishDirectory,
            $temporaryCopilotDirectory, $temporaryProbeDirectory, (Split-Path $singleFileProfilePath -Parent))) {
        Assert-OwnedDirectoryPath -Root $repositoryRoot -Path $ownedPath
    }

    foreach ($freshPath in @($temporaryPublishDirectory, $temporaryCopilotDirectory, $temporaryProbeDirectory)) {
        if (Test-Path -LiteralPath $freshPath) {
            throw "Single-file temporary output is already present and is not owned by this run: $freshPath"
        }
    }
}

$publishModeArguments = @(Get-PublishModeArguments -SingleFile:$SingleFile -PublishProfileFullPath $singleFileProfilePath)

Assert-PathWithinRoot -Root $repositoryRoot -Path $publishParent
Assert-PathWithinRoot -Root $repositoryRoot -Path $finalPublishDirectory
Assert-PathWithinRoot -Root $repositoryRoot -Path $temporaryPublishDirectory
Assert-PathWithinRoot -Root $repositoryRoot -Path $temporaryCopilotDirectory
[void][System.IO.Directory]::CreateDirectory($publishParent)

foreach ($existingPath in @($publishParent, $finalPublishDirectory)) {
    if (Test-Path -LiteralPath $existingPath) {
        $existingItem = Get-Item -LiteralPath $existingPath -Force
        Assert-NotReparsePoint -Item $existingItem
        if (-not $existingItem.PSIsContainer) {
            throw "Expected an owned publish directory but found a file: $existingPath"
        }
    }
}

$canonicalLockPaths = @(
    (Join-Path $coreProjectDirectory 'packages.lock.json'),
    (Join-Path $appProjectDirectory 'packages.lock.json')
)
$canonicalLockHashes = @{}
foreach ($lockPath in $canonicalLockPaths) {
    if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
        throw "Canonical package lock is missing: $lockPath"
    }

    if ($SingleFile) {
        Assert-OwnedDirectoryPath -Root $repositoryRoot -Path (Split-Path $lockPath -Parent)
        Assert-NotReparsePoint -Item (Get-Item -LiteralPath $lockPath -Force)
    }

    $canonicalLockHashes[$lockPath] = Get-Sha256Hex -Path $lockPath
}

$singleFileLockHashes = $canonicalLockHashes.Clone()
if ($SingleFile) {
    Assert-NotReparsePoint -Item (Get-Item -LiteralPath $dedicatedLockPath -Force)
    $singleFileLockHashes[$dedicatedLockPath] = Get-Sha256Hex -Path $dedicatedLockPath
}

$temporaryLockDirectories = @(
    (Join-Path $coreProjectDirectory "obj\$lockScope\$runId"),
    (Join-Path $appProjectDirectory "obj\$lockScope\$runId")
)
if ($SingleFile) {
    foreach ($temporaryLockDirectory in $temporaryLockDirectories) {
        Assert-OwnedDirectoryPath -Root $repositoryRoot -Path $temporaryLockDirectory
        if (Test-Path -LiteralPath $temporaryLockDirectory) {
            throw 'Single-file temporary RID lock directory is not owned by this run.'
        }
    }
}

$published = $false

try {
    Push-Location $repositoryRoot
    try {
        $dotNetPath = Resolve-ApplicationPath -Name 'dotnet'
        $actualSdkVersion = ((& $dotNetPath --version) | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($actualSdkVersion)) {
            throw 'Unable to resolve the dotnet SDK selected by global.json.'
        }

        Assert-GlobalJsonSdk -GlobalJsonPath $globalJsonPath -ActualVersion $actualSdkVersion

        Invoke-DotNet -DotNetPath $dotNetPath -Arguments @(
            'restore',
            $appProjectPath,
            '--locked-mode',
            '--property:CopilotSkipCliDownload=true',
            '--verbosity',
            'minimal'
        )

        $ridRestoreArguments = @(
            'restore',
            $appProjectPath,
            '--runtime',
            $RuntimeIdentifier,
            '--force-evaluate',
            '--lock-file-path',
            $temporaryLockRelativePath,
            '--property:CopilotSkipCliDownload=true',
            '--property:SelfContained=true',
            '--property:PublishTrimmed=false',
            '--property:PublishReadyToRun=false',
            '--verbosity',
            'minimal'
        )
        $ridRestoreArguments += $publishModeArguments
        Invoke-DotNet -DotNetPath $dotNetPath -Arguments $ridRestoreArguments

        Assert-RidLockMatchesCanonicalLock `
            -CanonicalLockPath (Join-Path $coreProjectDirectory 'packages.lock.json') `
            -RidLockPath (Join-Path $coreProjectDirectory $temporaryLockRelativePath) `
            -ProjectName 'StudyReportEvaluator.Core'
        if ($SingleFile) {
            Assert-SingleFileRidLockMatchesDedicatedLock `
                -CanonicalLockPath (Join-Path $appProjectDirectory 'packages.lock.json') `
                -DedicatedLockPath $dedicatedLockPath `
                -RidLockPath (Join-Path $appProjectDirectory $temporaryLockRelativePath)
            Assert-SingleFileLockHashes -Hashes $singleFileLockHashes
        }
        else {
            Assert-RidLockMatchesCanonicalLock `
                -CanonicalLockPath (Join-Path $appProjectDirectory 'packages.lock.json') `
                -RidLockPath (Join-Path $appProjectDirectory $temporaryLockRelativePath) `
                -ProjectName 'StudyReportEvaluator.App'
        }

        foreach ($lockPath in $canonicalLockPaths) {
            if ((Get-Sha256Hex -Path $lockPath) -cne $canonicalLockHashes[$lockPath]) {
                throw "RID restore changed canonical package lock: $lockPath"
            }
        }

        $copilotCliVersion = Get-CopilotCliVersion `
            -DotNetPath $dotNetPath `
            -ProjectPath $appProjectPath
        $copilotCliPath = Get-NpmCopilotCliBinary `
            -Version $copilotCliVersion `
            -DestinationDirectory $temporaryCopilotDirectory
        $publishArguments = @(
            'publish',
            $appProjectPath,
            '--configuration',
            'Release',
            '--framework',
            $TargetFramework,
            '--runtime',
            $RuntimeIdentifier,
            '--self-contained',
            'true',
            '--no-restore',
            '--output',
            $temporaryPublishDirectory,
            '--property:CopilotSkipCliDownload=false',
            '--property:PublishTrimmed=false',
            '--property:PublishReadyToRun=false',
            '--property:UseAppHost=true',
            '--property:DebugSymbols=false',
            '--property:DebugType=None',
            '--verbosity',
            'minimal'
        )
        $publishArguments += $publishModeArguments
        if ($null -ne $copilotCliPath) {
            $publishArguments += "--property:CopilotCliBinaryPath=$copilotCliPath"
        }

        Invoke-DotNet -DotNetPath $dotNetPath -Arguments $publishArguments
    }
    finally {
        Pop-Location
    }

    if ($SingleFile) {
        Assert-SingleFileLockHashes -Hashes $singleFileLockHashes
        Assert-OwnedDirectoryPath -Root $repositoryRoot -Path $temporaryPublishDirectory -IncludeChildren
    }

    Remove-PublishSymbols -PublishDirectory $temporaryPublishDirectory
    if ($SingleFile) {
        Assert-SingleFileApplicationLaunch -RepositoryRoot $repositoryRoot `
            -PublishDirectory $temporaryPublishDirectory -ProbeDirectory $temporaryProbeDirectory
        Assert-SingleFileLockHashes -Hashes $singleFileLockHashes
        Assert-OwnedDirectoryPath -Root $repositoryRoot -Path $finalPublishDirectory -IncludeChildren
    }
    else {
        Assert-SafePublishLayout -PublishDirectory $temporaryPublishDirectory
        Assert-ApplicationLaunch -PublishDirectory $temporaryPublishDirectory
    }

    if (Test-Path -LiteralPath $finalPublishDirectory) {
        Remove-Item -LiteralPath $finalPublishDirectory -Recurse -Force
    }

    [System.IO.Directory]::Move($temporaryPublishDirectory, $finalPublishDirectory)
    $published = $true
    if ($SingleFile) {
        Write-Output "Published self-contained unsigned single file: $(Join-Path $finalPublishDirectory "$ApplicationName.exe")"
    }
    else {
        Write-Output "Published self-contained unsigned folder: $finalPublishDirectory"
    }
}
finally {
    try {
        foreach ($temporaryLockDirectory in $temporaryLockDirectories) {
            if (Test-Path -LiteralPath $temporaryLockDirectory) {
                if ($SingleFile) {
                    Assert-OwnedDirectoryPath -Root $repositoryRoot -Path $temporaryLockDirectory -IncludeChildren
                }

                Remove-Item -LiteralPath $temporaryLockDirectory -Recurse -Force
            }
        }

        if (-not $published -and (Test-Path -LiteralPath $temporaryPublishDirectory)) {
            if ($SingleFile) {
                Assert-OwnedDirectoryPath -Root $repositoryRoot -Path $temporaryPublishDirectory -IncludeChildren
            }

            Remove-Item -LiteralPath $temporaryPublishDirectory -Recurse -Force
        }

        if (Test-Path -LiteralPath $temporaryCopilotDirectory) {
            if ($SingleFile) {
                Assert-OwnedDirectoryPath -Root $repositoryRoot -Path $temporaryCopilotDirectory -IncludeChildren
            }

            Remove-Item -LiteralPath $temporaryCopilotDirectory -Recurse -Force
        }
    }
    finally {
        if ($SingleFile) {
            Assert-SingleFileLockHashes -Hashes $singleFileLockHashes
        }

        foreach ($lockPath in $canonicalLockPaths) {
            if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf) -or
                (Get-Sha256Hex -Path $lockPath) -cne $canonicalLockHashes[$lockPath]) {
                throw "Canonical package lock changed during publish: $lockPath"
            }
        }
    }
}
