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

$expected = @{
    'Code/patch/AW_FramePrioritySchedulerPatch.cs' =
        'B3B66EE7CC4780721AD2BC2C95E2DCDD9F18C107863541DFBB6D1E97E43D6B7E'
    'Code/core/performance/AWCooperativeSimulationRunner.cs' =
        '12DC261FF37E1BB5CDD34D4AD1466725CA88248332D9C21E01C121DF68925EB1'
    'Code/core/performance/AWCooperativeBatchRunner.cs' =
        '4FB1808412CB30043976F73895F3D839834FEA314FEA526AC3BD81006C2894C8'
    'Code/core/performance/AWCooperativeActorParallelJobRunner.cs' =
        '71C6AF13988F69825D18E111440A4EE6CEB9AA540AA89C62031328767B7075F4'
    'Code/core/performance/AWFrameSchedulerRules.cs' =
        '72A165218B11E070E4CD42FDA58B943491294D494BE1011EB489B1AF84189B7D'
    'Code/core/performance/AWSimulationStepContext.cs' =
        'EA57EEEF2DD9BF71362A119F2EFF94FCA19C8B7A672E8FF297392A7A63E47AAA'
}

foreach ($entry in $expected.GetEnumerator()) {
    $path = Join-Path $projectRoot $entry.Key
    $actual = Get-NormalizedTextSha256 -Path $path
    if ($actual -ne $entry.Value) {
        throw "Protected Cultiway scheduler file changed: $($entry.Key)"
    }
}

$authorityPath = Join-Path $projectRoot `
    'Code/core/performance/AWAuthorityCycleService.cs'
$authority = Get-Content -Raw -Encoding UTF8 $authorityPath
if ($authority -notmatch
    'if \(!pGate\.TryEnter\(pCycleToken, allowed\)\) return;') {
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

Write-Host 'Cultiway perf scheduler non-regression guard passed.'
