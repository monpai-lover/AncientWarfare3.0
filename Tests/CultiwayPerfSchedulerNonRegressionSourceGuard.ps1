$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot

function Get-NormalizedTextSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    $text = [IO.File]::ReadAllText($Path).
        Replace("`r`n", "`n").Replace("`r", "`n")
    $utf8 = New-Object Text.UTF8Encoding($false)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString(
            $sha256.ComputeHash($utf8.GetBytes($text))).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

$authorityPath = Join-Path $projectRoot `
    'Code/core/performance/AWAuthorityCycleService.cs'
$authority = Get-Content -Raw -Encoding UTF8 $authorityPath
if ($authority -notmatch
    'if \(!CooperativeGate\.TryEnter\(pCycleToken, allowed\)\)' -and
    $authority -notmatch
    'if \(!pGate\.TryEnter\(pCycleToken, CanRunAuthorityCycle\(pPaused\)\)\)') {
    throw 'AW authority work must remain behind the cycle-token gate.'
}

$framePatch = Get-Content -Raw -Encoding UTF8 (Join-Path $projectRoot `
    'Code/patch/AW_FramePrioritySchedulerPatch.cs')
if ($framePatch -notmatch 'AWAuthorityCycleService\.ProcessNativeCycle\(\)') {
    throw 'Native mode lost the canonical AW authority-cycle entry.'
}
if ($framePatch -notmatch
    'AWThirdPartySchedulerFaultRules\.ShouldQuarantine\(pError\)') {
    throw 'Known third-party scheduler faults lost their narrow classifier.'
}
if ($framePatch -notmatch
    'if \(quarantineThirdPartyFault && cleanupSucceeded\)') {
    throw 'Third-party faults must not resume after failed scheduler cleanup.'
}
if ($framePatch -notmatch
    'AWFramePriorityGovernor\.MarkFault\(pError\);\s*Config\.paused = true;') {
    throw 'Unclassified scheduler faults must still pause the simulation.'
}

$runner = Get-Content -Raw -Encoding UTF8 (Join-Path $projectRoot `
    'Code/core/performance/AWCooperativeSimulationRunner.cs')
if ($runner -notmatch
    'AWAuthorityCycleService\.ProcessCooperativeStep\(') {
    throw 'Large mode lost the canonical AW authority-cycle entry.'
}
if ($runner -notmatch 'Aw3RtsLogicalPulse') {
    throw 'Large mode lost the AW3 RTS logical pulse.'
}

Write-Host 'Cultiway perf scheduler non-regression guard passed.'
