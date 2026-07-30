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

$controller = Read-Source `
    'Code/core/lineage/ArmyRtsControllerService.cs'
$operation = Read-Source `
    'Code/core/lineage/ArmyReplenishmentOperationService.cs'
$authority = Read-Source `
    'Code/core/performance/AWAuthorityCycleService.cs'
$restore = Read-Source `
    'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs'
$strategicIndex = Read-Source `
    'Code/core/lineage/ArmyStrategicIndexService.cs'
$warPatch = Read-Source 'Code/patch/AW_WarPatch.cs'
$updateRegion = Method-Region $controller `
    'private static void UpdateReplenishmentRequest' `
    'private static void TryApplyReserveExhaustion'

Require $updateRegion 'ArmyReplenishmentOperationService.Ensure(' `
    'RTS replenishment opens one durable operation'
Reject $updateRegion 'TemporaryLevyService.RequestOffensiveRecovery(' `
    'RTS cannot bypass the three-month operation'
Require $authority `
    'ArmyReplenishmentOperationService.ProcessAuthorityCycle' `
    'conversion must run on authoritative simulation cycles'
Require $restore `
    'new AW3RestoreStage("army_replenishment_operations"' `
    'save/load must restore immutable operations'
Require $operation 'CityReservePoolService.TryConsumeBatch(' `
    'operations consume indexed real actors'
Require $operation 'TemporaryLevyService.EnlistReserveActors(' `
    'operations reuse one actor conversion path'
Require $operation 'ArmyReplenishmentOperationRules.BatchRequest(' `
    'conversion is proportional and bounded'
Require $operation `
    'ArmyRtsControllerService.TryTeleportReinforcementMember' `
    'successful recruits join the formation immediately'
Require $operation 'ActiveArmyIds.Remove(pArmyId);' `
    'unresolvable army IDs must leave the runtime operation index'
Require $operation 'if (!deadlineReached)' `
    'ordinary months must consume only one bounded indexed batch'
Require $strategicIndex `
    'ArmyReplenishmentOperationService.OnArmyDisposed(' `
    'army disposal must close its operation'
Require $strategicIndex `
    'ArmyReplenishmentOperationService.OnArmyKingdomChanged(' `
    'ownership changes must invalidate foreign approval'
Require $warPatch 'ArmyReplenishmentOperationService.OnWarEnded(' `
    'war end must close participant operations'
Reject $operation 'foreach (Actor actor in city.units)' `
    'wartime replenishment cannot scan live residents'

if ($failures.Count -gt 0) {
    Write-Host "Army replenishment operation source guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Army replenishment operation source guard passed.'
