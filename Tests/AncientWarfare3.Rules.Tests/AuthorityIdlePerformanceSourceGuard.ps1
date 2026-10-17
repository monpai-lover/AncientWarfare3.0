$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string] $relativePath) {
    $path = Join-Path $root $relativePath
    if (-not [IO.File]::Exists($path)) {
        $failures.Add("missing source file $relativePath")
        return ''
    }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

$warService = Read-Source `
    'Code/core/lineage/WarForceEliminationSettlementService.cs'
$electionService = Read-Source `
    'Code/core/court/WesternCourtElectionService.cs'
$replenishmentService = Read-Source `
    'Code/core/lineage/ArmyReplenishmentOperationService.cs'

$terminal = Read-Source 'Code/core/lineage/WarTerminalSettlementCoordinator.cs'
$warGate = $terminal.IndexOf('if (_recoveryCyclesUntilScan > 0)')
$warList = $terminal.IndexOf('wars.checkLists();')
if (-not $warService.Contains('WarTerminalSettlementCoordinator.ProcessAuthorityCycle();') -or
    $warGate -lt 0 -or $warList -lt 0 -or $warGate -gt $warList -or
    -not $terminal.Contains('WarsPerAuthorityCycle = 8') -or
    -not $terminal.Contains('inspected < WarsPerAuthorityCycle') -or
    -not $terminal.Contains('RecoveryScanIntervalCycles = 4') -or
    $terminal.Contains('new List<War>')) {
    $failures.Add('terminal settlement recovery must gate and bound live-war scans without allocating snapshots')
}

$electionMethod = $electionService.IndexOf(
    'public static void ProcessAuthorityCycle()')
$electionReturn = $electionService.IndexOf(
    'if (VacancyQueue.Count == 0) return;', $electionMethod)
$electionList = $electionService.IndexOf(
    'var retry = new List<WesternCourtVacancy>', $electionMethod)
if ($electionMethod -lt 0 -or $electionReturn -lt 0 -or
    $electionList -lt 0 -or $electionReturn -gt $electionList) {
    $failures.Add('western elections must return before allocating when the vacancy queue is empty')
}

$replenishmentMethod = $replenishmentService.IndexOf(
    'internal static void ProcessAuthorityCycle()')
$replenishmentReturn = $replenishmentService.IndexOf(
    'if (ActiveArmyIds.Count == 0) return;', $replenishmentMethod)
$replenishmentBatch = $replenishmentService.IndexOf(
    'TakeActiveBatch(', $replenishmentMethod)
if ($replenishmentMethod -lt 0 -or $replenishmentReturn -lt 0 -or
    $replenishmentBatch -lt 0 -or
    $replenishmentReturn -gt $replenishmentBatch) {
    $failures.Add('army replenishment must return before allocating an empty active batch')
}

if ($failures.Count -gt 0) {
    Write-Host "Authority idle performance guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Authority idle performance source guards passed.'
