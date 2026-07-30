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
        $failures.Add("${message}: found forbidden '$needle'")
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

$completion = Read-Source `
    'Code/core/lineage/ArmyReplenishmentCompletionService.cs'
$armyService = Read-Source 'Code/core/lineage/AWArmyService.cs'
$director = Read-Source `
    'Code/core/lineage/KingdomWarDirectorService.cs'
$operation = Read-Source `
    'Code/core/lineage/ArmyReplenishmentOperationService.cs'
$completeRegion = Method-Region $completion `
    'internal static void Complete' `
    'internal static bool HasViableAttack'
$continuityRegion = Method-Region $director `
    'internal static void EnsureOffensiveContinuity' `
    'public static void ProcessFrame'

Require $completion 'AWArmyService.IsSpecialArmy(' `
    'special role armies must be excluded from ordinary consolidation'
Require $completion 'RoyalGuardService.IsRoyalGuard(' `
    'royal guards must not enter ordinary consolidation'
Require $completion 'TemporarySlaveVanguardService.IsMember(' `
    'slave vanguards must not enter ordinary consolidation'
Require $completion 'GarrisonSortieService.IsSortieArmy(' `
    'dedicated garrison sorties must not enter ordinary consolidation'
Require $completion 'LineageKeys.RESTORATION_UPRISING_ARMY' `
    'restoration armies must not enter ordinary consolidation'
Require $completion 'living > bestLiving ||' `
    'the primary assault army must be the largest viable candidate'
Require $completion 'living == bestLiving && army.id < bestId' `
    'equal-sized primary candidates must use stable army ID order'
Require $completion 'if (bestAny != pPrimary &&' `
    'an insufficient unreserved force must fall back to a viable protected army'
Require $completion 'KingdomWarDirectorService.QueueArmyChanged(' `
    'completion and merge must queue a fresh strategic plan'
Reject $completeRegion 'TryPrepareOffensivePrimary(' `
    'operation completion cannot merge armies before validating a target'
Require $armyService `
    'internal static bool TryMergeOrdinaryArmyInto(' `
    'ordinary consolidation must use one actor-safe merge entry point'
Require $armyService 'ArmyCaptainDisposalScope.Open(' `
    'merge must preserve captain disposal safety'
Require $armyService 'ArmyReplenishmentOperationService.Clear(' `
    'the merged source operation must be invalidated'
Require $director 'internal static void EnsureOffensiveContinuity(' `
    'the director must expose the national attack guarantee'
Require $director 'private static double CurrentWorldTime()' `
    'fallback attack missions need a valid world-time stamp'
$selectIndex = $continuityRegion.IndexOf('TrySelectOffensivePrimary(')
$targetIndex = $continuityRegion.IndexOf(
    'TryFindOffensiveContinuityTarget(')
$prepareIndex = $continuityRegion.IndexOf('TryPrepareOffensivePrimary(')
if ($selectIndex -lt 0 -or $targetIndex -lt 0 -or $prepareIndex -lt 0 -or
    $selectIndex -gt $targetIndex -or $targetIndex -gt $prepareIndex) {
    $failures.Add(
        'the director must select a candidate, validate an open target, then permit consolidation')
}
Require $operation 'ArmyReplenishmentCompletionService.Complete(' `
    'early and deadline completion must run consolidation logic'

if ($failures.Count -gt 0) {
    Write-Host "Army replenishment completion source guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Army replenishment completion source guard passed.'
