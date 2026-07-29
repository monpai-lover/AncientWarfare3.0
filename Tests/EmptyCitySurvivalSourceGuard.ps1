$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$patchPath = Join-Path $repo `
    'Code\patch\AW_EmptyCitySurvivalPatch.cs'
$servicePath = Join-Path $repo `
    'Code\core\lineage\EmptyCitySurvivalService.cs'
$zoneServicePath = Join-Path $repo `
    'Code\core\lineage\EnclosedUnownedZoneRepairService.cs'

function Read-RequiredFile([string]$path, [string]$name) {
    if (-not [IO.File]::Exists($path)) {
        throw "$name is missing: $path"
    }
    return [IO.File]::ReadAllText($path)
}

function Require-Present([string]$source, [string]$needle,
    [string]$message) {
    if (-not $source.Contains($needle)) { throw $message }
}

function Require-Absent([string]$source, [string]$needle,
    [string]$message) {
    if ($source.Contains($needle)) { throw $message }
}

$patch = Read-RequiredFile $patchPath 'Empty city survival patch'
$service = Read-RequiredFile $servicePath 'Empty city survival service'
$zoneService = Read-RequiredFile $zoneServicePath `
    'Enclosed Zone repair service'

Require-Present $patch `
    '[HarmonyPatch(typeof(CityBehBorderShrink), nameof(CityBehBorderShrink.execute))]' `
    'Natural empty-city preservation must intercept only the shrink behavior.'
Require-Present $patch 'ref BehResult __result' `
    'The shrink Prefix must return an explicit behavior result.'
Require-Present $patch '__result = BehResult.Stop;' `
    'Suppressed natural shrink must stop the current city task.'
Require-Present $patch 'ShouldSuppressNaturalBorderShrink(pCity)' `
    'The Harmony Prefix must delegate its condition to the survival service.'
Require-Present $patch `
    '[HarmonyPatch(typeof(CityBehCheckDestruction), nameof(CityBehCheckDestruction.execute))]' `
    'The original xenophobic raze branch must be observed explicitly.'
Require-Present $patch 'ShouldRecordXenophobicRazeIntent(pCity)' `
    'Raze intent must use the original destruction branch conditions.'
Require-Present $patch 'RecordXenophobicRazeIntent(pCity)' `
    'Completed xenophobic razing must persist its removal intent.'
Require-Present $patch `
    '[HarmonyPatch(typeof(City), nameof(City.eventUnitAdded))]' `
    'A newly assigned resident must clear stale raze intent.'
Require-Present $patch 'ClearRazeIntentForResident(__instance, pActor)' `
    'Resident arrival must use the guarded intent clear path.'
Require-Present $patch '[HarmonyPatch(typeof(City), "setKingdom")]' `
    'A non-neutral takeover must clear stale raze intent.'
Require-Present $patch 'pFromLoad' `
    'Owner changes restored from a save must preserve persisted intent.'
Require-Present $patch '[HarmonyPatch(typeof(City), "turnCityToNeutral")]' `
    'Frozen occupation must preserve the formal owner.'
Require-Present $patch 'ShouldKeepFormalOwner(__instance)' `
    'Neutralization must delegate to the frozen-occupation guard.'
Require-Present $patch `
    '[HarmonyPatch(typeof(CityZoneAbandon), nameof(CityZoneAbandon.check))]' `
    'Live-city retention must intercept automatic abandoned-Zone cleanup.'
Require-Present $patch `
    'ShouldSuppressAutomaticAbandonedZoneCleanup(pCity)' `
    'The cleanup Prefix must delegate to the survival service.'

Require-Present $service 'aw_xenophobic_raze_pending' `
    'Raze intent must use a stable persisted CityData key.'
Require-Present $service '.data.set(RazeIntentKey, true)' `
    'Raze intent must be stored in CityData for save/load continuity.'
Require-Present $service '.data.removeBool(RazeIntentKey)' `
    'Clearing raze intent must remove the persisted boolean.'

Require-Absent $patch 'nameof(City.removeZone)' `
    'The feature must not intercept City.removeZone.'
Require-Absent $patch 'nameof(City.isReadyForRemoval)' `
    'The feature must not create Zone-less zombie cities.'
Require-Absent $patch 'typeof(CityManager)' `
    'The feature must not intercept CityManager removal.'
Require-Absent $patch 'nameof(City.destroyCity)' `
    'The feature must not intercept the shared city destruction path.'

Require-Present $zoneService '!kingdom.isNeutral()' `
    'Neutral cities cannot contribute an enclosing sovereign boundary.'
Require-Present $zoneService '!pCity.kingdom.isNeutral()' `
    'Neutral cities cannot receive enclosed unowned Zones.'

Write-Output 'Empty city survival source guard passed.'
