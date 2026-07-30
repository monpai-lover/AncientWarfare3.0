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

$levy = Read-Source 'Code/core/lineage/TemporaryLevyService.cs'
$potential = Read-Source `
    'Code/core/lineage/WartimeMilitaryPotentialService.cs'
$controller = Read-Source `
    'Code/core/lineage/ArmyRtsControllerService.cs'
$warPatch = Read-Source 'Code/patch/AW_WarPatch.cs'
$preparationRegion = Method-Region $levy `
    'private static void ProcessPreparationRecruitment' `
    'private static bool TrySelectPreparationCity'
$casualtyRegion = Method-Region $levy `
    'private static void ProcessCasualtyReinforcement' `
    'private static int ApprovedTargetShortage'
$captainRegion = Method-Region $levy `
    'private static void ScanCityForCaptainRecovery' `
    'private static void RemoveCaptainRecoveryPlansForKingdom'
$enlistReserveRegion = Method-Region $levy `
    'internal static int EnlistReserveActors' `
    'private static int DirectedDemand'

Require $levy 'CityReservePoolService.TryConsumeBatch(' `
    'wartime levy recruitment must consume pre-war actor IDs'
Require $preparationRegion `
    'CityReservePoolService.TryConsumePreparationBatch(' `
    'preparation must consume registered actor IDs'
Require $preparationRegion 'ApprovedTargetShortage(' `
    'preparation cannot exceed an approved establishment shortage'
Reject $preparationRegion 'ScanCity(' `
    'preparation cannot rescan arbitrary residents for soldiers'
Reject $casualtyRegion 'ScanCity(' `
    'wartime casualty replacement must not scan live residents'
Reject $captainRegion 'foreach (Actor actor in pCity.units)' `
    'wartime captain replacement must not scan live residents'
Require $enlistReserveRegion `
    'CityReservePoolService.OnActorReturnedToCivilian(actor)' `
    'failed reserve conversion must return a still-eligible civilian'
Require $potential 'CityReservePoolService.CountAvailable(' `
    'military potential must reflect remaining reserve membership'
Require $controller 'TryTeleportReinforcementMember' `
    'recruits must teleport before the first mission'
Require $controller 'KingdomWarDirectorService.QueueArmyChanged' `
    'successful arrival must replan the newly operational army'
Require $warPatch `
    'CityReservePoolService.CompletePreWarReconciliation(__result)' `
    'formal war creation performs the final indexed refill'

$reconcileStart = $warPatch.IndexOf(
    'CityReservePoolService.CompletePreWarReconciliation(__result)',
    [System.StringComparison]::Ordinal)
$freezeStart = $warPatch.IndexOf(
    'CityReservePoolService.OnWarStarted(__result)',
    [System.StringComparison]::Ordinal)
$levyStart = $warPatch.IndexOf(
    'TemporaryLevyService.OnWarStarted(__result',
    [System.StringComparison]::Ordinal)
if ($reconcileStart -lt 0 -or $freezeStart -lt 0 -or $levyStart -lt 0 -or
    $reconcileStart -ge $freezeStart -or $freezeStart -ge $levyStart) {
    $failures.Add(
        'final indexed refill must run before freeze and levy conversion')
}

if ($failures.Count -gt 0) {
    Write-Host "City reserve recruitment source guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'City reserve recruitment source guard passed.'
