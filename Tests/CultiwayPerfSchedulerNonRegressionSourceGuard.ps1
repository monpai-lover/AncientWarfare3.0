$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$expected = @{
    'Code/patch/AW_FramePrioritySchedulerPatch.cs' =
        '57D6FF4A9E4B7AE65C01DD3E9CAB847944DB6181272737FDA6A9C94513B1F2DF'
    'Code/core/performance/AWCooperativeSimulationRunner.cs' =
        '816BDD74B747636C997BE053FE8610763BFD7D5F6BD6C88DA6822A55233CACF4'
    'Code/core/performance/AWCooperativeBatchRunner.cs' =
        '3DA5988A81FD07F0364C5C831B9CE027B06923925DF6E98192C6784F3FC5E667'
    'Code/core/performance/AWCooperativeActorParallelJobRunner.cs' =
        '26D75F158D6289387180DD8802E812E462D754A07D1A412795AD8B157E5A79F2'
    'Code/core/performance/AWFrameSchedulerRules.cs' =
        '07BE0EA942A0AA1A3782C4DD62947517F66364DECFACA557B05EA202CABB041E'
    'Code/core/performance/AWSimulationStepContext.cs' =
        '2809229EC394DD8C5CE9319A4D31AEC63198E7C5265C9246EF0C13F8F17A4E97'
}

foreach ($entry in $expected.GetEnumerator()) {
    $path = Join-Path $projectRoot $entry.Key
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
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
    'AWAuthorityCycleService\.ProcessCooperativeCycle\(') {
    throw 'Large mode lost the canonical AW authority-cycle entry.'
}

Write-Host 'Cultiway perf scheduler non-regression guard passed.'
