#Requires -Version 7.0
#Requires -PSEdition Core

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Assert-Equal {
    param(
        [AllowNull()]
        [object] $Expected,

        [AllowNull()]
        [object] $Actual,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not [object]::Equals($Expected, $Actual)) {
        throw "$Message Expected '$Expected', actual '$Actual'."
    }
}

function ConvertFrom-ToolJson {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $Output
    )

    return (([string](@($Output) -join "`n")).Trim() | ConvertFrom-Json)
}

if ($PSVersionTable.PSEdition -cne 'Core' -or $PSVersionTable.PSVersion.Major -lt 7) {
    throw 'PowerShell Core 7 or later is required.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolPath = Join-Path $PSScriptRoot 'version.ps1'
$sourcePath = Join-Path $repositoryRoot 'Directory.Build.props'
foreach ($requiredPath in @($toolPath, $sourcePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required test input is missing: $requiredPath"
    }
}

$assertionCount = 0

# Read the canonical version independently so the self-test survives every version bump.
$expectedVersion = ([string](
        ([xml](Get-Content -LiteralPath $sourcePath -Raw)).SelectSingleNode(
            '/Project/PropertyGroup/VersionPrefix').InnerText)).Trim()
if ($expectedVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "The repository must declare a stable VersionPrefix. Actual '$expectedVersion'."
}
$assertionCount++

$show = ConvertFrom-ToolJson -Output @(& $toolPath show -RepositoryRoot $repositoryRoot -Json)
Assert-Equal -Expected 'PASS' -Actual $show.Status -Message 'show status mismatch.'
$assertionCount++
Assert-Equal -Expected $expectedVersion -Actual $show.Version -Message 'repository version mismatch.'
$assertionCount++

$verify = ConvertFrom-ToolJson -Output @(& $toolPath verify -RepositoryRoot $repositoryRoot -Json)
Assert-Equal -Expected 'PASS' -Actual $verify.Status -Message 'verify status mismatch.'
$assertionCount++
Assert-Equal -Expected 2 -Actual @($verify.Projects).Count -Message 'verify project count mismatch.'
$assertionCount++

$actualGitPath = [string](
    Get-Command git -CommandType Application -ErrorAction Stop |
        Select-Object -First 1).Source
$multipleGitRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('StudyReportEvaluator-MultipleGit-' + [System.Guid]::NewGuid().ToString('N'))
$temporaryRepository = Join-Path $multipleGitRoot 'repository'
$originalPath = $env:PATH
try {
    [void][System.IO.Directory]::CreateDirectory($multipleGitRoot)
    & $actualGitPath clone --no-hardlinks --quiet $repositoryRoot $temporaryRepository
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to create the temporary Git repository for the multiple-command test.'
    }

    # Copy every input that verify evaluates, so the clone does not depend on these files being committed yet.
    $verifyInputs = @(
        'Directory.Build.props',
        'Directory.Packages.props',
        'global.json',
        'CHANGELOG.md',
        'dev\version.ps1',
        'src\StudyReportEvaluator.Core\StudyReportEvaluator.Core.csproj',
        'src\StudyReportEvaluator.App\StudyReportEvaluator.App.csproj'
    )
    foreach ($relativePath in $verifyInputs) {
        $destinationPath = Join-Path $temporaryRepository $relativePath
        [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destinationPath))
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $relativePath) -Destination $destinationPath -Force
    }

    $temporaryChangelogPath = Join-Path $temporaryRepository 'CHANGELOG.md'
    $temporaryChangelog = [System.IO.File]::ReadAllText($temporaryChangelogPath)
    $escapedVersion = [System.Text.RegularExpressions.Regex]::Escape([string]$show.Version)
    if ($temporaryChangelog -notmatch "(?m)^## \[$escapedVersion\] - [0-9]{4}-[0-9]{2}-[0-9]{2}\r?$") {
        [System.IO.File]::AppendAllText(
            $temporaryChangelogPath,
            "`n## [$($show.Version)] - 2000-01-01`n`n- Synthetic release entry for version tool self-test.`n",
            [System.Text.UTF8Encoding]::new($false))
    }

    $pendingChanges = @(& $actualGitPath -C $temporaryRepository status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the temporary Git repository.'
    }

    if ($pendingChanges.Count -ne 0) {
        & $actualGitPath -C $temporaryRepository config user.name 'Version Tool Test'
        & $actualGitPath -C $temporaryRepository config user.email 'version-tool-test@example.invalid'
        & $actualGitPath -C $temporaryRepository add -- ($verifyInputs | ForEach-Object { $_.Replace('\', '/') })
        & $actualGitPath -C $temporaryRepository commit --quiet -m 'Prepare version tool test input'
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to commit the temporary version tool test input.'
        }
    }

    $tagName = "v$($show.Version)"
    & $actualGitPath -C $temporaryRepository tag --delete $tagName 2>$null
    if ($LASTEXITCODE -notin @(0, 1)) {
        throw 'Unable to remove an inherited temporary release tag.'
    }

    & $actualGitPath -C $temporaryRepository `
        -c user.name='Version Tool Test' `
        -c user.email='version-tool-test@example.invalid' `
        tag --annotate $tagName --message "Version tool test $($show.Version)"
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to create the temporary annotated release tag.'
    }

    $shimDirectories = @(
        (Join-Path $multipleGitRoot 'git-shim-1'),
        (Join-Path $multipleGitRoot 'git-shim-2')
    )
    $shimText = "@echo off`r`n`"$actualGitPath`" %*`r`n"
    foreach ($shimDirectory in $shimDirectories) {
        [void][System.IO.Directory]::CreateDirectory($shimDirectory)
        [System.IO.File]::WriteAllText(
            (Join-Path $shimDirectory 'git.cmd'),
            $shimText,
            [System.Text.Encoding]::ASCII)
    }

    $env:PATH = ($shimDirectories + $originalPath) -join [System.IO.Path]::PathSeparator
    $matchingGitCommands = @(Get-Command git -CommandType Application -ErrorAction Stop)
    if ($matchingGitCommands.Count -lt 2) {
        throw 'The multiple-command test did not expose at least two Git applications.'
    }

    $multipleGitVerify = ConvertFrom-ToolJson -Output @(
        & (Join-Path $temporaryRepository 'dev\version.ps1') verify `
            -RepositoryRoot $temporaryRepository `
            -Tag $tagName `
            -RequireClean `
            -Json)
    Assert-Equal -Expected 'PASS' -Actual $multipleGitVerify.Status -Message 'multiple Git command verify status mismatch.'
    $assertionCount++
    Assert-Equal -Expected $tagName -Actual $multipleGitVerify.Tag.Name -Message 'multiple Git command tag mismatch.'
    $assertionCount++
    Assert-Equal -Expected $true -Actual $multipleGitVerify.CleanWorkingTreeChecked -Message 'multiple Git command clean check mismatch.'
    $assertionCount++
}
finally {
    $env:PATH = $originalPath
    if (Test-Path -LiteralPath $multipleGitRoot) {
        Remove-Item -LiteralPath $multipleGitRoot -Recurse -Force
    }
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('StudyReportEvaluator-VersionTool-' + [System.Guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($temporaryRoot)
try {
    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $temporaryRoot 'Directory.Build.props')

    $set = ConvertFrom-ToolJson -Output @(
        & $toolPath set `
            -RepositoryRoot $temporaryRoot `
            -Version '2.3.4-rc.1' `
            -Json)
    Assert-Equal -Expected '2.3.4-rc.1' -Actual $set.Version -Message 'set version mismatch.'
    $assertionCount++
    Assert-Equal -Expected $true -Actual $set.Changed -Message 'set should report a change.'
    $assertionCount++

    $afterSet = ConvertFrom-ToolJson -Output @(
        & $toolPath show -RepositoryRoot $temporaryRoot -Json)
    Assert-Equal -Expected '2.3.4-rc.1' -Actual $afterSet.Version -Message 'persisted version mismatch.'
    $assertionCount++

    $dryRun = ConvertFrom-ToolJson -Output @(
        & $toolPath bump `
            -RepositoryRoot $temporaryRoot `
            -Part patch `
            -Prerelease 'beta.2' `
            -DryRun `
            -Json)
    Assert-Equal -Expected '2.3.5-beta.2' -Actual $dryRun.Version -Message 'dry-run bump mismatch.'
    $assertionCount++
    $afterDryRun = ConvertFrom-ToolJson -Output @(
        & $toolPath show -RepositoryRoot $temporaryRoot -Json)
    Assert-Equal -Expected '2.3.4-rc.1' -Actual $afterDryRun.Version -Message 'dry-run modified the source.'
    $assertionCount++

    $bump = ConvertFrom-ToolJson -Output @(
        & $toolPath bump `
            -RepositoryRoot $temporaryRoot `
            -Part minor `
            -Json)
    Assert-Equal -Expected '2.4.0' -Actual $bump.Version -Message 'minor bump mismatch.'
    $assertionCount++

    $invalidRejected = $false
    try {
        & $toolPath set `
            -RepositoryRoot $temporaryRoot `
            -Version '02.4.0' `
            -ErrorAction Stop | Out-Null
    }
    catch {
        $invalidRejected = $true
    }

    Assert-Equal -Expected $true -Actual $invalidRejected -Message 'invalid SemVer was not rejected.'
    $assertionCount++
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Output "Version tool tests passed: $assertionCount assertions."
