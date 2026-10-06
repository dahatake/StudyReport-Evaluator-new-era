#Requires -Version 7.0
<#
.SYNOPSIS
  Checks docs/requirements-definition.md and docs/catalog.md for consistency (FR-064).
  Writes one string per problem to the pipeline; writes nothing when everything is consistent.
#>
[CmdletBinding()]
param([string]$Root = (Split-Path -Parent $PSScriptRoot))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$reqPath = Join-Path $Root 'docs/requirements-definition.md'
$catPath = Join-Path $Root 'docs/catalog.md'
$manualPath = Join-Path $Root 'docs/manual-tests.md'
$req = [IO.File]::ReadAllText($reqPath)
$cat = [IO.File]::ReadAllText($catPath)

function Get-StatusCategory([string]$status) {
    foreach ($k in '承認済み', '保留', '廃止') { if ($status.StartsWith($k)) { return $k } }
    if ($status.Contains('承認待ち')) { return '承認待ち' }
    return $status.Trim()
}

# ---- requirements: headings, statuses, priorities, AC definitions ----
$headingPattern = '(?m)^#### ((?:FR|NFR-[A-Z0-9]+|Q)-\d{3}) (.+)$'
$headings = [regex]::Matches($req, $headingPattern)
$requirements = [ordered]@{}
$questionIds = [System.Collections.Generic.List[string]]::new()
$acOwners = @{}

for ($i = 0; $i -lt $headings.Count; $i++) {
    $m = $headings[$i]
    $id = $m.Groups[1].Value
    $start = $m.Index
    $end = if ($i + 1 -lt $headings.Count) { $headings[$i + 1].Index } else { $req.Length }
    $nextSection = [regex]::Match($req.Substring($start + $m.Length, $end - $start - $m.Length), '(?m)^#{2,3} ')
    if ($nextSection.Success) { $end = $start + $m.Length + $nextSection.Index }
    $block = $req.Substring($start, $end - $start)

    if ($id.StartsWith('Q-')) {
        if ($questionIds.Contains($id)) { "質問票の項番が重複: $id" }
        $questionIds.Add($id)
        continue
    }
    if ($requirements.Contains($id)) { "要求 ID が重複: $id"; continue }

    $statusLine = [regex]::Match($block, '(?m)^- 状態: ([^｜\r\n]+)')
    if (-not $statusLine.Success) { "要求に決定状態がない: $id"; continue }
    $priority = [regex]::Match($block, '(?m)^- 状態: [^\r\n]*?(?:｜|優先度: )(MUST|SHOULD|MAY)')
    $acs = [regex]::Matches($block, '受入基準 (AC-\d{3})') | ForEach-Object { $_.Groups[1].Value }
    foreach ($ac in $acs) {
        if ($acOwners.ContainsKey($ac)) { "受入基準 ID が重複: $ac（$($acOwners[$ac]) と $id）" } else { $acOwners[$ac] = $id }
    }
    $category = Get-StatusCategory $statusLine.Groups[1].Value
    if ($category -ne '廃止' -and -not $priority.Success) { "要求に優先度がない: $id" }
    if (@($acs).Count -eq 0) { "要求に受入基準がない: $id" }
    $requirements[$id] = [pscustomobject]@{
        Id       = $id
        Title    = $m.Groups[2].Value.Trim()
        Status   = $category
        Priority = if ($priority.Success) { $priority.Groups[1].Value } else { '' }
        Blocked  = $block.Contains('BLOCKED（')
    }
}

# every AC-### mentioned anywhere must be defined under a requirement
foreach ($ref in ([regex]::Matches($req, '(?<![\w-])AC-\d{3}') | ForEach-Object Value | Sort-Object -Unique)) {
    $isDraftRef = [regex]::IsMatch($req, "草案 (?:[A-Z]+-\d+、)*$([regex]::Escape($ref))")
    if (-not $acOwners.ContainsKey($ref) -and -not $isDraftRef) { "参照されている受入基準が定義されていない: $ref" }
}

# ---- catalog ----
$catalogRows = [regex]::Matches($cat, '(?m)^\| ((?:FR|NFR-[A-Z0-9]+)-\d{3}) \| ([^|]+)\| ([^|]+)\| ([^|]+)\| ([^|]+)\| ([^|]+)\|\s*$')
$catalog = @{}
foreach ($row in $catalogRows) {
    $id = $row.Groups[1].Value
    $catalog[$id] = $row
    if (-not $requirements.Contains($id)) { "カタログの要求 ID が要求定義書にない: $id"; continue }
    $catStatus = $row.Groups[3].Value.Trim()
    if ($catStatus -ne $requirements[$id].Status) { "決定状態が不一致: $id（要求定義書=$($requirements[$id].Status)、カタログ=$catStatus）" }
}

# every file referenced from the catalog (backticks or relative links) must exist
$catalogDir = Split-Path -Parent $catPath
foreach ($m in [regex]::Matches($cat, '\]\((?!https?:)([^)#\s]+)')) {
    $target = [IO.Path]::GetFullPath((Join-Path $catalogDir $m.Groups[1].Value))
    if (-not (Test-Path -LiteralPath $target)) { "カタログのリンク先がない: $($m.Groups[1].Value)" }
}
foreach ($m in [regex]::Matches($cat, '`((?:src|tests|scripts|docs|eng|\.github)/[^`*]+?)`')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root $m.Groups[1].Value))) { "カタログのファイルがない: $($m.Groups[1].Value)" }
}

# ---- implementable MUST requirements must be catalogued and traced to tests ----
$testText = (Get-ChildItem (Join-Path $Root 'tests') -Recurse -Filter *.cs -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
$manualText = if (Test-Path $manualPath) { [IO.File]::ReadAllText($manualPath) } else { '' }

foreach ($r in $requirements.Values) {
    if ($r.Status -ne '承認済み' -or $r.Priority -ne 'MUST' -or $r.Blocked) { continue }
    if (-not $catalog.ContainsKey($r.Id)) { "実装すべき MUST 要求がカタログにない: $($r.Id)"; continue }
    if ($catalog[$r.Id].Groups[4].Value.Contains('未実装')) { "実装すべき MUST 要求がカタログで未実装: $($r.Id)" }
    $pattern = "(?<![\w-])$([regex]::Escape($r.Id))(?!\d)"
    if (-not [regex]::IsMatch($testText, $pattern) -and -not [regex]::IsMatch($manualText, $pattern)) {
        "実装すべき MUST 要求がテストにも手順書にも現れない: $($r.Id)"
    }
}

# deprecated requirements must not remain only in code/tests after removal from the requirements body
foreach ($m in [regex]::Matches($testText, '(?<![\w-])((?:FR|NFR-[A-Z0-9]+)-\d{3})(?!\d)')) {
    $id = $m.Groups[1].Value
    if (-not $requirements.Contains($id)) { "テストに要求定義書にない要求 ID がある: $id" }
}
