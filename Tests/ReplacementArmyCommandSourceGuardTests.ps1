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

function Reject([string]$source, [string]$needle, [string]$message) {
    if ($source.Contains($needle)) {
        $failures.Add("${message}: found '$needle'")
    }
}

function Method-Region([string]$source, [string]$start,
    [string]$next) {
    $begin = $source.IndexOf($start, [System.StringComparison]::Ordinal)
    if ($begin -lt 0) { return '' }
    $end = $source.IndexOf($next, $begin + $start.Length,
        [System.StringComparison]::Ordinal)
    if ($end -lt 0) { $end = $source.Length }
    return $source.Substring($begin, $end - $begin)
}

$index = Read-Source 'Code/core/lineage/ArmyStrategicIndexService.cs'
$models = Read-Source 'Code/core/lineage/ArmyRtsModels.cs'
$snapshot = Read-Source `
    'Code/core/lineage/ArmyStrategicSnapshotService.cs'
$director = Read-Source 'Code/core/lineage/KingdomWarDirectorService.cs'
$registeredRegion = Method-Region $index `
    'public static void OnArmyRegistered' `
    'public static void OnArmyKingdomChanged'
$kingdomChangedRegion = Method-Region $index `
    'public static void OnArmyKingdomChanged' `
    'public static void OnArmyRosterChanged'
$rosterRegion = Method-Region $index `
    'public static void OnArmyRosterChanged' `
    'public static void OnArmyDisposed'
$disposedRegion = Method-Region $index `
    'public static void OnArmyDisposed' `
    'public static ArmyStrategicIdCursor CreateSnapshotCursor'

Require $index 'OnArmyRegistered' `
    'new armies must enter the strategic index'
Require $registeredRegion 'KingdomWarDirectorService.QueueArmyChanged' `
    'new army registration must use the coalesced director queue'
Reject $registeredRegion 'KingdomWarDirectorService.OnArmyChanged' `
    'captain-only creation must not run an immediate stale plan'
Require $kingdomChangedRegion 'KingdomWarDirectorService.QueueArmyChanged' `
    'army ownership changes must queue a fresh plan'
Require $rosterRegion 'KingdomWarDirectorService.QueueArmyChanged' `
    'roster growth must queue a fresh plan'
Require $disposedRegion 'CoalitionWarTaskService.OnArmyInvalidated(pArmy.id)' `
    'destroyed assault reservations must be released'
Require $disposedRegion 'KingdomWarDirectorService.QueueArmyChanged' `
    'army disposal must queue the former kingdom'
Require $models 'public bool SpecialArmy { get; }' `
    'strategic snapshots must carry special-army identity'
Require $snapshot 'AWArmyService.IsSpecialArmy(pArmy)' `
    'special armies must be identified before field allocation'
Require $director 'pArmy.SpecialArmy' `
    'special armies must stay outside ordinary role conversion'

if ($failures.Count -gt 0) {
    Write-Host "Replacement army command source guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Replacement army command source guard passed.'
