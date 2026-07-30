param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $root $relativePath
    if (-not [System.IO.File]::Exists($path)) {
        $failures.Add("missing source file $relativePath")
        return ''
    }
    return [System.IO.File]::ReadAllText($path)
}

function Require([string]$source, [string]$needle, [string]$message) {
    if (-not $source.Contains($needle)) {
        $failures.Add("${message}: missing '$needle'")
    }
}

$table = Read-Source 'Code/core/db/WarScoreSnapshotTableItem.cs'
$persistence = Read-Source 'Code/core/lineage/WarScorePersistence.cs'
$score = Read-Source 'Code/core/lineage/WarScoreService.cs'
$controller = Read-Source `
    'Code/core/lineage/ArmyRtsControllerService.cs'

Require $table 'attacker_reserve_exhaustion' `
    'the attacker reserve contribution must be archived'
Require $table 'defender_reserve_exhaustion' `
    'the defender reserve contribution must be archived'
Require $persistence 'ATTACKER_RESERVE_EXHAUSTION' `
    'SQLite persistence must include the attacker column'
Require $persistence 'DEFENDER_RESERVE_EXHAUSTION' `
    'SQLite persistence must include the defender column'
Require $persistence 'INTEGER NOT NULL DEFAULT 0' `
    'old autosaves must migrate reserve columns with zero defaults'
Require $score 'ApplyReserveExhaustion' `
    'war score must expose an idempotent mutation'
Require $controller 'ShouldApplyReserveExhaustion' `
    'only a confirmed attacking shortage may trigger the mutation'
Require $controller 'if (!pCommit ||' `
    'non-committing RTS evaluations must not mutate war exhaustion'
if ($controller.Contains('ReserveExhaustionWarId')) {
    $failures.Add(
        'the persisted side contribution must be the only once-per-war latch')
}

if ($failures.Count -gt 0) {
    Write-Host "Reserve exhaustion persistence source guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Reserve exhaustion persistence source guard passed.'
