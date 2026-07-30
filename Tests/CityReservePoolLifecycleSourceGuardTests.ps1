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

$reservePatch = Read-Source 'Code/patch/AW_CityReservePoolPatch.cs'
$reserveService = Read-Source 'Code/core/lineage/CityReservePoolService.cs'
$death = Read-Source 'Code/patch/AW_ActorDeathPatch.cs'
$enlist = Read-Source 'Code/patch/AW_EnlistPatch.cs'
$slavery = Read-Source 'Code/patch/AW_SlaveryPatch.cs'
$authority = Read-Source 'Code/core/performance/AWAuthorityCycleService.cs'
$deferred = Read-Source 'Code/patch/AW_DeferredRuntimeWorkPatch.cs'
$warPatch = Read-Source 'Code/patch/AW_WarPatch.cs'
$restore = Read-Source 'Code/core/multiplayer/AW3RuntimeRestorePipeline.cs'
$reset = $authority

Require $reservePatch '[HarmonyPatch(typeof(Actor), "eventBecomeAdult")]' `
    'reserve enrollment must use the original adulthood event'
Require $reservePatch 'CityReservePoolService.OnActorBecameAdult(__instance)' `
    'the adulthood event must attempt reserve enrollment'
Require $death 'CityReservePoolService.OnActorInvalidated(__instance)' `
    'death must remove reserve membership immediately'
Require $slavery 'CityReservePoolService.OnActorKingdomChanged(' `
    'kingdom changes must invalidate old reserve ownership'
Require $reservePatch 'CityReservePoolService.OnActorCityChanged(' `
    'city migration must invalidate old city membership'
Require $enlist 'CityReservePoolService.OnActorEnlisted(' `
    'non-reserve enlistment must consume reserve membership'
Require $reserveService 'LineageKeys.CITY_RESERVE_MEMBER' `
    'actor membership must be persisted'
Require $reserveService 'SortedSet<long>' `
    'runtime city membership must be deterministic'
Require $authority 'CityReservePoolService.ProcessAuthorityCycle' `
    'reserve repair must run from authority cycles'
if ($deferred.Contains('CityReservePoolService.ProcessAuthorityCycle')) {
    $failures.Add(
        'reserve repair must not run from MapBox.Update presentation work')
}
Require $warPatch 'CityReservePoolService.OnWarStarted(__result)' `
    'formal war start must freeze both sides before levy conversion'
Require $warPatch 'CityReservePoolService.OnWarEnded(' `
    'war end must reevaluate the final-war freeze'
Require $restore 'new AW3RestoreStage("city_reserve_pools",' `
    'restore must rebuild only persisted reserve membership'
Require $reset 'CityReservePoolService.ClearRuntime' `
    'world reset must clear runtime reserve indexes'

$reserveStart = $warPatch.IndexOf(
    'CityReservePoolService.OnWarStarted(__result)',
    [System.StringComparison]::Ordinal)
$levyStart = $warPatch.IndexOf(
    'TemporaryLevyService.OnWarStarted(__result',
    [System.StringComparison]::Ordinal)
if ($reserveStart -lt 0 -or $levyStart -lt 0 -or
    $reserveStart -ge $levyStart) {
    $failures.Add(
        'city reserve freeze must run before temporary levy conversion')
}

if ($failures.Count -gt 0) {
    Write-Host "City reserve lifecycle source guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'City reserve lifecycle source guard passed.'
