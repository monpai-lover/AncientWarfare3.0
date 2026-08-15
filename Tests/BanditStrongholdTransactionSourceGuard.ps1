$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$planPath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdPlan.cs'
$servicePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'

if (-not (Test-Path -LiteralPath $planPath)) {
    throw 'Missing PeasantRebelBanditStrongholdPlan.cs'
}
if (-not (Test-Path -LiteralPath $servicePath)) {
    throw 'Missing PeasantRebelBanditStrongholdService.cs'
}

$plan = Get-Content -Raw -Encoding UTF8 $planPath
$service = Get-Content -Raw -Encoding UTF8 $servicePath
foreach ($token in @('TryPlan(', 'CultiwayStyleCityWallService.TryPlan',
        'TopTileLibrary.wall_wild', 'IsViableSplit', 'FixedZoneKeys',
        'ReserveMotherActor', 'MotherCoreTile')) {
    if (($plan + $service) -notmatch [regex]::Escape($token)) {
        throw "Stronghold preflight is missing $token"
    }
}
foreach ($token in @('TryCreate(', 'World.world.cities.newCity',
        'setUnitMetas', 'newCityEvent', 'addZone(', 'joinCity(',
        'spawnOn(', 'joinAnotherKingdom(', 'addBuilding("bonfire"',
        'setTopTileType', 'Rollback', 'PeasantRebelBanditStateStore.Write')) {
    if ($service -notmatch [regex]::Escape($token)) {
        throw "Stronghold transaction is missing $token"
    }
}

foreach ($token in @('PrepareBanditKingdomRemoval(',
        'pBandit.units.ToList()', 'actor.kingdom = null;')) {
    if ($service -notmatch [regex]::Escape($token)) {
        throw "Bandit rollback cleanup is missing $token"
    }
}

$directBanditCleanup =
    'PrepareBanditKingdomRemoval\s*\(\s*bandit,\s*origin,\s*' +
    'pMother,\s*null\s*\);\s*' +
    'World\.world\.kingdoms\.removeObject\(bandit\);'
if ($service -notmatch $directBanditCleanup) {
    throw 'Direct failed bandit removal is not preceded by actor cleanup'
}
$directCatchCleanup =
    'PrepareBanditKingdomRemoval\s*\(\s*pBandit,\s*origin,\s*' +
    'pMother,\s*null\s*\);\s*' +
    'World\.world\.kingdoms\.removeObject\(pBandit\);'
if ($service -notmatch $directCatchCleanup) {
    throw 'Direct bandit exception removal is not preceded by actor cleanup'
}
$rollbackCleanup =
    'PrepareBanditKingdomRemoval\s*\(\s*plan\.Context\.Bandit,\s*' +
    'plan\.Context\.Origin,\s*plan\.Context\.Mother,\s*' +
    'pTransaction\.Actors\s*\);\s*' +
    'World\.world\.kingdoms\.removeObject\(plan\.Context\.Bandit\);'
if ($service -notmatch $rollbackCleanup) {
    throw 'Transactional bandit removal is not preceded by actor cleanup'
}

$tryPlanStart = $service.IndexOf('internal static bool TryPlan(')
$tryCreateStart = $service.IndexOf('internal static bool TryCreate(')
if ($tryPlanStart -lt 0 -or $tryCreateStart -le $tryPlanStart) {
    throw 'TryPlan must precede TryCreate'
}
$preflight = $service.Substring($tryPlanStart,
    $tryCreateStart - $tryPlanStart)
foreach ($mutation in @('.newCity(', '.addZone(', '.joinCity(',
        '.setTopTileType(', '.addBuilding(')) {
    if ($preflight -match [regex]::Escape($mutation)) {
        throw "Preflight mutates world state through $mutation"
    }
}

Write-Output 'Bandit stronghold transaction source guard passed.'
