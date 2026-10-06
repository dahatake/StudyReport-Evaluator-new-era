#Requires -Version 7.0
#Requires -PSEdition Core

<#
.SYNOPSIS
Copies the already-published Windows single file into the release EXE/checksum pair.
.DESCRIPTION
P05 only: no publish, execution, extraction, signing, or bundle-content verification.
P02 and P06 own standard-host extraction and internal dependency/content validation.
PE/version checks here are not proof that an arbitrary EXE is a self-contained bundle.
Run packaging serially for a destination. Rollback covers handled failures, not a
power loss/process termination between the two file renames.
.PARAMETER PublishedDirectory
Read-only, caller-owned input containing only StudyReportEvaluator.App.exe from P02.
Defaults to artifacts/package/publish/win-x64-singlefile under this repository.
It is never a cleanup root; an explicitly supplied directory may be outside the repo
but must use the same logical drive as the repository/output (case-insensitive).
This developer packaging parameter restriction does not affect end-user app paths.
.PARAMETER OutputDirectory
Defaults to artifacts/package. Overrides must remain inside that repository-owned
artifact root, outside the input directory. Only the fixed EXE/checksum names and
this invocation's temporary directory are owned; unrelated files are left alone.
Relative arguments are resolved against the repository, not the caller's cwd.
#>
[CmdletBinding()]
param(
    [string] $PublishedDirectory,
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Assert-SupportedHost {
    if ($PSVersionTable.PSEdition -cne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
        throw 'PowerShell Core 7 or later is required. Windows PowerShell is not supported.'
    }

    if (-not $IsWindows -or [System.Environment]::OSVersion.Version.Build -lt 22000 -or
        [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64 -or
        [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
        throw 'Single-file packaging requires Windows 11 x64 and an x64 PowerShell process.'
    }
}

function Resolve-PackageDirectory {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    if ([System.IO.Path]::IsPathRooted($Path) -and -not [System.IO.Path]::IsPathFullyQualified($Path)) {
        throw 'Drive-relative package paths are not allowed.'
    }

    $inputRoot = [System.IO.Path]::GetPathRoot($Path)
    foreach ($segment in $Path.Substring($inputRoot.Length).Replace('/', '\').Split('\', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        if ($segment -eq '.' -or $segment -eq '..') { continue }
        # Check BEFORE normalization can obscure Win32 trailing-dot/space or DOS aliases.
        if ($segment.EndsWith('.') -or $segment.EndsWith(' ') -or
            $segment -match '~[0-9]+(?:\.|$)' -or
            $segment.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
            throw 'Package paths must use canonical directory names, without aliases or streams.'
        }
    }

    $fullPath = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($Path, $RepositoryRoot))
    if ([System.IO.Path]::GetPathRoot($fullPath) -notmatch '^[A-Za-z]:\\$') {
        throw 'Package paths must use a local drive, not a device or UNC path.'
    }

    return $fullPath
}

function Test-PackagePathWithinRoot {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $Path
    )

    $relative = [System.IO.Path]::GetRelativePath($Root, $Path)
    return -not ([System.IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or
        $relative.StartsWith('..\', [System.StringComparison]::Ordinal) -or
        $relative.StartsWith('../', [System.StringComparison]::Ordinal))
}

function Assert-PlainDirectoryAncestors {
    param([Parameter(Mandatory)] [string] $Directory)

    # Check all ancestors, including those above the repository and missing output leaves.
    $current = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($Directory))
    while (-not [string]::IsNullOrEmpty($current)) {
        $item = $null
        try { $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop }
        catch [System.Management.Automation.ItemNotFoundException] { }
        if ($null -ne $item) {
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points are not allowed in package paths or their parents: $current"
            }

            if (-not $item.PSIsContainer) {
                throw "Expected a package directory, not a file: $current"
            }
        }

        $current = [System.IO.Path]::GetDirectoryName($current)
    }
}

function Get-PlainPackageFile {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [switch] $AllowMissing
    )

    try { $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop }
    catch [System.Management.Automation.ItemNotFoundException] {
        if ($AllowMissing) { return $null }
        throw
    }

    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Reparse points are not allowed in package files: $Path"
    }

    if ($item.PSIsContainer) {
        throw "Refusing to use or replace a directory at a package file path: $Path"
    }

    # Hard links are not reparse points. Reject them rather than aliasing input/user data.
    if ([string]$item.LinkType -ceq 'HardLink') {
        throw "Hard links are not allowed in package files: $Path"
    }

    return $item
}

function Resolve-SingleFilePackagePaths {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [string] $PublishedDirectory,
        [string] $OutputDirectory
    )

    $RepositoryRoot = Resolve-PackageDirectory -Path $RepositoryRoot -RepositoryRoot $RepositoryRoot
    $packageRoot = Join-Path $RepositoryRoot 'artifacts\package'
    if ([string]::IsNullOrWhiteSpace($PublishedDirectory)) {
        $PublishedDirectory = Join-Path $packageRoot 'publish\win-x64-singlefile'
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = $packageRoot
    }

    $PublishedDirectory = Resolve-PackageDirectory -Path $PublishedDirectory -RepositoryRoot $RepositoryRoot
    # Different logical drives can alias the input via SUBST; reject before filesystem probes.
    if (-not [string]::Equals([System.IO.Path]::GetPathRoot($PublishedDirectory),
            [System.IO.Path]::GetPathRoot($RepositoryRoot), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'PublishedDirectory must use the same logical drive as the repository and output.'
    }

    $OutputDirectory = Resolve-PackageDirectory -Path $OutputDirectory -RepositoryRoot $RepositoryRoot
    if (-not (Test-PackagePathWithinRoot -Root $packageRoot -Path $OutputDirectory)) {
        throw 'OutputDirectory must remain inside the repository-owned artifacts/package root.'
    }

    if (Test-PackagePathWithinRoot -Root $PublishedDirectory -Path $OutputDirectory) {
        throw 'Package outputs must not alias or be inside the published input directory.'
    }

    Assert-PlainDirectoryAncestors -Directory $PublishedDirectory
    Assert-PlainDirectoryAncestors -Directory $OutputDirectory
    return [pscustomobject]@{
        RepositoryRoot = $RepositoryRoot
        PublishedDirectory = $PublishedDirectory
        OutputDirectory = $OutputDirectory
        ExecutablePath = Join-Path $OutputDirectory 'StudyReportEvaluator-win-x64.exe'
        HashPath = Join-Path $OutputDirectory 'StudyReportEvaluator-win-x64.exe.sha256'
    }
}

function Get-SingleFilePackageInput {
    param([Parameter(Mandatory)] [string] $Directory)

    Assert-PlainDirectoryAncestors -Directory $Directory
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw "Published directory does not exist; run P02 separately: $Directory"
    }

    $entries = @(Get-ChildItem -LiteralPath $Directory -Force)
    if ($entries.Count -ne 1 -or $entries[0].PSIsContainer -or
        $entries[0].Name -cne 'StudyReportEvaluator.App.exe') {
        throw 'Published input must contain exactly one StudyReportEvaluator.App.exe and no sidecars or subdirectories.'
    }

    $file = Get-PlainPackageFile -Path $entries[0].FullName
    if ($file.Length -le 0) {
        throw 'The published single-file EXE must be nonzero.'
    }

    return $file.FullName
}

function Assert-Amd64PortableExecutable {
    param([Parameter(Mandatory)] [string] $Path)

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    $reader = [System.IO.BinaryReader]::new($stream)
    try {
        # Bounded PE recognition only: never read the whole bundle or execute/extract it.
        $dos = $reader.ReadBytes(64)
        if ($dos.Length -ne 64 -or $dos[0] -ne 0x4D -or $dos[1] -ne 0x5A) {
            throw 'Application EXE has an invalid PE DOS header.'
        }

        $offset = [System.BitConverter]::ToInt32($dos, 0x3C)
        if ($offset -lt 64 -or [long]$offset -gt $stream.Length - 26) {
            throw 'Application EXE has an invalid PE header offset.'
        }

        $stream.Position = $offset
        $header = $reader.ReadBytes(26)
        if ($header.Length -ne 26 -or $header[0] -ne 0x50 -or $header[1] -ne 0x45 -or
            $header[2] -ne 0 -or $header[3] -ne 0) {
            throw 'Application EXE has an invalid PE signature.'
        }

        $optionalSize = [System.BitConverter]::ToUInt16($header, 20)
        $characteristics = [System.BitConverter]::ToUInt16($header, 22)
        if ([System.BitConverter]::ToUInt16($header, 4) -ne 0x8664 -or
            [System.BitConverter]::ToUInt16($header, 24) -ne 0x020B -or
            ($characteristics -band 0x0002) -eq 0 -or ($characteristics -band 0x2000) -ne 0 -or
            $optionalSize -lt 2 -or [long]$offset + 24 + $optionalSize -gt $stream.Length) {
            throw 'Application EXE must be an AMD64 PE32+ executable image, not a DLL.'
        }
    }
    finally { $reader.Dispose() }
}

function Get-SingleFilePackageVersion {
    param([Parameter(Mandatory)] [string] $RepositoryRoot)

    Assert-PlainDirectoryAncestors -Directory $RepositoryRoot
    $propsPath = Join-Path $RepositoryRoot 'Directory.Build.props'
    [void](Get-PlainPackageFile -Path $propsPath)
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($propsPath, $settings)
    try {
        $props = [System.Xml.XmlDocument]::new()
        $props.XmlResolver = $null
        $props.Load($reader)
    }
    finally { $reader.Dispose() }

    $prefixes = @($props.SelectNodes('/Project/PropertyGroup/VersionPrefix'))
    $suffixes = @($props.SelectNodes('/Project/PropertyGroup/VersionSuffix'))
    if ($prefixes.Count -ne 1 -or $suffixes.Count -ne 1 -or
        $prefixes[0].InnerText -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or
        $suffixes[0].InnerText -notmatch '^[0-9A-Za-z.-]*$') {
        throw 'Cannot resolve the canonical product version from Directory.Build.props.'
    }

    # Match P02 and the SDK defaults; do not hard-code the current or future patch.
    $productVersion = $prefixes[0].InnerText
    if (-not [string]::IsNullOrEmpty($suffixes[0].InnerText)) {
        $productVersion += '-' + $suffixes[0].InnerText
    }

    return [pscustomobject]@{
        ProductVersion = $productVersion
        FileVersion = $prefixes[0].InnerText + '.0'
    }
}

function Assert-SingleFileBinaryVersion {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $ProductVersion,
        [Parameter(Mandatory)] [string] $FileVersion
    )

    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if (([string]$info.ProductVersion).Split('+', 2)[0] -cne $ProductVersion -or
        [string]$info.FileVersion -cne $FileVersion) {
        throw 'Application EXE ProductVersion/FileVersion must match Directory.Build.props.'
    }
}

function Get-Sha256Hex {
    param([Parameter(Mandatory)] [string] $Path)

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try { return [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream)) }
    finally { $stream.Dispose() }
}

function Assert-ExactSingleFileSidecar {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Hash
    )

    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes("$Hash  StudyReportEvaluator-win-x64.exe`n")
    $file = Get-PlainPackageFile -Path $Path
    # Length first bounds the read and byte comparison rejects BOM, CRLF and extra lines.
    if ($Hash -cnotmatch '^[0-9A-F]{64}$' -or $file.Length -ne $bytes.Length -or
        [System.Convert]::ToHexString([System.IO.File]::ReadAllBytes($Path)) -cne [System.Convert]::ToHexString($bytes)) {
        throw 'SHA-256 sidecar must be exact uppercase HASH, two spaces, final EXE basename, and LF in UTF-8 without BOM.'
    }
}

function Get-ExistingSingleFilePackageHash {
    param([Parameter(Mandatory)] [string] $OutputDirectory)

    Assert-PlainDirectoryAncestors -Directory $OutputDirectory
    $exe = Get-PlainPackageFile -Path (Join-Path $OutputDirectory 'StudyReportEvaluator-win-x64.exe') -AllowMissing
    $sidecar = Get-PlainPackageFile -Path (Join-Path $OutputDirectory 'StudyReportEvaluator-win-x64.exe.sha256') -AllowMissing
    if ($null -eq $exe -and $null -eq $sidecar) { return $null }
    if ($null -eq $exe -or $null -eq $sidecar) {
        throw 'Refusing to replace an incomplete existing EXE/checksum pair.'
    }

    # Reserved names alone do not authorize overwriting arbitrary files/workbooks.
    # An older product version may be replaced; the incoming version is checked separately.
    Assert-Amd64PortableExecutable -Path $exe.FullName
    $hash = Get-Sha256Hex -Path $exe.FullName
    Assert-ExactSingleFileSidecar -Path $sidecar.FullName -Hash $hash
    return $hash
}

function Write-SingleFileArtifactPair {
    param(
        [Parameter(Mandatory)] [System.IO.FileStream] $SourceStream,
        [Parameter(Mandatory)] [string] $OutputDirectory
    )

    # Private I/O unit: the caller has validated input metadata and output ownership.
    if (-not $SourceStream.CanRead -or $SourceStream.CanWrite -or $SourceStream.Length -le 0) {
        throw 'Packaging requires an open, nonempty, read-only source stream.'
    }

    $previousHash = Get-ExistingSingleFilePackageHash -OutputDirectory $OutputDirectory
    $hadPreviousPair = $null -ne $previousHash
    $SourceStream.Position = 0
    $hash = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($SourceStream))
    $SourceStream.Position = 0
    $temporaryDirectory = Join-Path $OutputDirectory ('.singlefile-' + [System.Guid]::NewGuid().ToString('N'))
    $entries = @(foreach ($name in @('StudyReportEvaluator-win-x64.exe', 'StudyReportEvaluator-win-x64.exe.sha256')) {
        [pscustomobject]@{
            Name = $name
            Final = Join-Path $OutputDirectory $name
            Staged = Join-Path $temporaryDirectory $name
            Backup = Join-Path $temporaryDirectory ($name + '.previous')
            PreviousHash = $null
            NewHash = $null
            Committed = $false
        }
    })
    $temporaryCreated = $false
    $preserveRecoveryFiles = $false
    $operation = 'staging'

    try {
        Assert-PlainDirectoryAncestors -Directory $temporaryDirectory
        if (Test-Path -LiteralPath $temporaryDirectory) {
            throw 'Temporary package directory is not owned by this invocation.'
        }

        [void][System.IO.Directory]::CreateDirectory($temporaryDirectory)
        $temporaryCreated = $true
        $destination = [System.IO.FileStream]::new($entries[0].Staged, [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write, [System.IO.FileShare]::None, 65536, [System.IO.FileOptions]::WriteThrough)
        try {
            $SourceStream.CopyTo($destination)
            $destination.Flush($true)
        }
        finally { $destination.Dispose() }

        $entries[0].NewHash = Get-Sha256Hex -Path $entries[0].Staged
        if ($entries[0].NewHash -cne $hash -or (Get-Item -LiteralPath $entries[0].Staged).Length -ne $SourceStream.Length) {
            throw 'Staged EXE differs from the read-only publish input.'
        }

        # Hash the byte copy already bearing the final basename, never an apphost alias.
        $hashBytes = [System.Text.UTF8Encoding]::new($false).GetBytes("$($entries[0].NewHash)  $($entries[0].Name)`n")
        $destination = [System.IO.FileStream]::new($entries[1].Staged, [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write, [System.IO.FileShare]::None, 4096, [System.IO.FileOptions]::WriteThrough)
        try {
            $destination.Write($hashBytes, 0, $hashBytes.Length)
            $destination.Flush($true)
        }
        finally { $destination.Dispose() }

        Assert-ExactSingleFileSidecar -Path $entries[1].Staged -Hash $hash
        $entries[1].NewHash = Get-Sha256Hex -Path $entries[1].Staged
        if ($hadPreviousPair) {
            foreach ($entry in $entries) {
                [void](Get-PlainPackageFile -Path $entry.Final)
                [System.IO.File]::Copy($entry.Final, $entry.Backup, $false)
                $entry.PreviousHash = Get-Sha256Hex -Path $entry.Backup
            }

            if ($entries[0].PreviousHash -cne $previousHash) { throw 'Previous EXE changed during backup.' }
            Assert-ExactSingleFileSidecar -Path $entries[1].Backup -Hash $previousHash
        }

        if ($previousHash -cne (Get-ExistingSingleFilePackageHash -OutputDirectory $OutputDirectory)) {
            throw 'Existing output pair changed during staging.'
        }

        foreach ($entry in $entries) {
            $operation = 'commit ' + $entry.Name
            Assert-PlainDirectoryAncestors -Directory $OutputDirectory
            $current = Get-PlainPackageFile -Path $entry.Final -AllowMissing
            if ($hadPreviousPair) {
                if ($null -eq $current -or (Get-Sha256Hex -Path $entry.Final) -cne $entry.PreviousHash) {
                    throw 'Refusing to overwrite an output changed after backup.'
                }
            }
            elseif ($null -ne $current) {
                throw 'Refusing to overwrite an output created by another writer.'
            }

            # Staging/backup/final paths share the output directory's volume.
            [System.IO.File]::Move($entry.Staged, $entry.Final, $hadPreviousPair)
            $entry.Committed = $true
        }

        $operation = 'final pair verification'
        if ((Get-ExistingSingleFilePackageHash -OutputDirectory $OutputDirectory) -cne $hash) {
            throw 'Final EXE/checksum pair differs from the staged input.'
        }
    }
    catch {
        $failure = $_.Exception
        for ($index = $entries.Count - 1; $index -ge 0; $index--) {
            $entry = $entries[$index]
            if (-not $entry.Committed) { continue }
            try {
                Assert-PlainDirectoryAncestors -Directory $OutputDirectory
                $current = Get-PlainPackageFile -Path $entry.Final -AllowMissing
                if ($null -ne $current -and (Get-Sha256Hex -Path $entry.Final) -cne $entry.NewHash) {
                    throw 'Refusing rollback over an output changed by another writer.'
                }

                if ($hadPreviousPair) {
                    [void](Get-PlainPackageFile -Path $entry.Backup)
                    if ((Get-Sha256Hex -Path $entry.Backup) -cne $entry.PreviousHash) {
                        throw 'Refusing to restore a changed backup.'
                    }

                    [System.IO.File]::Move($entry.Backup, $entry.Final, ($null -ne $current))
                }
                elseif ($null -ne $current) {
                    [System.IO.File]::Delete($entry.Final)
                }
            }
            catch { $preserveRecoveryFiles = $true }
        }

        try {
            if ($previousHash -cne (Get-ExistingSingleFilePackageHash -OutputDirectory $OutputDirectory)) {
                $preserveRecoveryFiles = $true
            }
        }
        catch { $preserveRecoveryFiles = $true }

        if ($preserveRecoveryFiles) {
            throw [System.IO.IOException]::new("Packaging failed during $operation; rollback incomplete. Recovery files retained at: $temporaryDirectory", $failure)
        }

        throw [System.IO.IOException]::new("Packaging failed during $operation; original output state restored.", $failure)
    }
    finally {
        if ($temporaryCreated -and -not $preserveRecoveryFiles) {
            Assert-PlainDirectoryAncestors -Directory $temporaryDirectory
            $ownedNames = @($entries.Name) + @($entries | ForEach-Object { $_.Name + '.previous' })
            $temporaryItems = @(Get-ChildItem -LiteralPath $temporaryDirectory -Force)
            foreach ($item in $temporaryItems) {
                if ($item.Name -cnotin $ownedNames) { throw 'Refusing cleanup of unexpected temporary package content.' }
                [void](Get-PlainPackageFile -Path $item.FullName)
            }

            foreach ($item in $temporaryItems) {
                # A backed-up read-only output may retain its attributes; change only this owned copy.
                if ($item.IsReadOnly) { $item.IsReadOnly = $false }
                [System.IO.File]::Delete($item.FullName)
            }

            [System.IO.Directory]::Delete($temporaryDirectory, $false)
        }
    }

    return $hash
}

function Invoke-SingleFilePackaging {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [string] $PublishedDirectory,
        [string] $OutputDirectory
    )

    Assert-SupportedHost
    $paths = Resolve-SingleFilePackagePaths -RepositoryRoot $RepositoryRoot -PublishedDirectory $PublishedDirectory -OutputDirectory $OutputDirectory
    $inputPath = Get-SingleFilePackageInput -Directory $paths.PublishedDirectory
    $version = Get-SingleFilePackageVersion -RepositoryRoot $paths.RepositoryRoot
    # Keep input write/delete denied through validation, copying, commit and rollback.
    $source = [System.IO.File]::Open($inputPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        Assert-Amd64PortableExecutable -Path $inputPath
        Assert-SingleFileBinaryVersion -Path $inputPath -ProductVersion $version.ProductVersion -FileVersion $version.FileVersion
        [void](Write-SingleFileArtifactPair -SourceStream $source -OutputDirectory $paths.OutputDirectory)
    }
    finally { $source.Dispose() }

    Write-Output "Created unsigned single-file package: $($paths.ExecutablePath)"
    Write-Output "Created SHA-256 sidecar: $($paths.HashPath)"
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Invoke-SingleFilePackaging -RepositoryRoot $repositoryRoot -PublishedDirectory $PublishedDirectory -OutputDirectory $OutputDirectory