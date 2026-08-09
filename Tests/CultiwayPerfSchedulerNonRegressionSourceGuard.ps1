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
        'FFEB5975322A2FC1DB52958284DC4554C15B9DC67844715D815CB430AC74127D'
    'Code/core/performance/AWCooperativeSimulationRunner.cs' =
        '12DC261FF37E1BB5CDD34D4AD1466725CA88248332D9C21E01C121DF68925EB1'
    'Code/core/performance/AWCooperativeBatchRunner.cs' =
        'D86AE5C98B043F3B95DEA3EE62E36787D94DA4AD9898A28D62ADC5F81A9A767C'
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

$runner = Get-Content -Raw -Encoding UTF8 (Join-Path $projectRoot `
    'Code/core/performance/AWCooperativeSimulationRunner.cs')
if ($runner -notmatch
    'AWAuthorityCycleService\.ProcessCooperativeStep\(') {
    throw 'Large mode lost the canonical AW authority-cycle entry.'
}

Write-Host 'Cultiway perf scheduler non-regression guard passed.'
