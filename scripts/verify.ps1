#Requires -Version 7.0
<#
.SYNOPSIS
  Runs every repository gate with one command: locked restore, build (analyzers, warnings as errors),
  all automated tests, and the management-data consistency checks (FR-064).
.DESCRIPTION
  Exits 0 only when every step succeeds. Any failure exits with a non-zero code.
  -SkipTests is intended for quick local iteration only; CI and acceptance use the default.
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [string]$Configuration = 'Release',
    # Optional dotnet test filter (CI excludes packaging tests that its own package steps run).
    [string]$TestFilter = '',
    # Optional directory for TRX results.
    [string]$ResultsDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$failures = [System.Collections.Generic.List[string]]::new()

function Invoke-Step([string]$Name, [scriptblock]$Body) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    try {
        & $Body
        if ($LASTEXITCODE -ne 0) { throw "exit code $LASTEXITCODE" }
        Write-Host "    OK: $Name" -ForegroundColor Green
    }
    catch {
        Write-Host "    FAILED: $Name ($_)" -ForegroundColor Red
        $failures.Add($Name)
    }
}

$solution = Join-Path $root 'StudyReportEvaluator.slnx'

Invoke-Step 'restore (locked mode)' { dotnet restore $solution --locked-mode }
Invoke-Step 'build (analyzers, warnings as errors)' { dotnet build $solution -c $Configuration --no-restore -warnaserror }
if (-not $SkipTests) {
    Invoke-Step 'tests' {
        $testArgs = @('test', $solution, '-c', $Configuration, '--no-build')
        if ($TestFilter) { $testArgs += @('--filter', $TestFilter) }
        if ($ResultsDirectory) { $testArgs += @('--results-directory', $ResultsDirectory, '--logger', 'trx', '--logger', 'console;verbosity=minimal') }
        dotnet @testArgs
    }
}

Invoke-Step 'management data consistency' {
    $global:LASTEXITCODE = 0
    $problems = & (Join-Path $PSScriptRoot 'verify-management-data.ps1') -Root $root
    if ($problems) {
        $problems | ForEach-Object { Write-Host "    - $_" -ForegroundColor Yellow }
        throw "$(@($problems).Count) management data problem(s)"
    }
}

if ($failures.Count -gt 0) {
    Write-Host "verify: FAILED ($($failures -join ', '))" -ForegroundColor Red
    exit 1
}
Write-Host 'verify: all gates passed' -ForegroundColor Green
exit 0
