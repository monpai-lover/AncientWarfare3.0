$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$planPath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdPlan.cs'
$servicePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'
$statePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditStrongholdState.cs'

if (-not (Test-Path -LiteralPath $planPath)) {
    throw 'Missing PeasantRebelBanditStrongholdPlan.cs'
}
if (-not (Test-Path -LiteralPath $servicePath)) {
    throw 'Missing PeasantRebelBanditStrongholdService.cs'
}
if (-not (Test-Path -LiteralPath $statePath)) {
    throw 'Missing PeasantRebelBanditStrongholdState.cs'
}

$plan = Get-Content -Raw -Encoding UTF8 $planPath
$service = Get-Content -Raw -Encoding UTF8 $servicePath
$state = Get-Content -Raw -Encoding UTF8 $statePath
foreach ($token in @('TryPlan(', 'PeasantRebelBanditZoneWallService.TryPlan',
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

foreach ($token in @('CurrentSchemaVersion = 3',
        'OriginalTopTypeId')) {
    if (-not $state.Contains($token)) {
        throw "Stronghold wall state is missing $token"
    }
}
foreach ($token in @('snapshot.TopType?.id ?? ""', 'RestoreWalls(',
        'PeasantRebelBanditStrongholdRules.ShouldRestoreWall(',
        'AssetManager.top_tiles.get(',
        'setTopTileType(originalTopType)')) {
    if (-not $service.Contains($token)) {
        throw "Stronghold wall lifecycle is missing $token"
    }
}

$fallStart = $service.IndexOf('private static bool CompleteFall(')
$fallEnd = $service.IndexOf('private static City ResolveCity(', $fallStart)
if ($fallStart -lt 0 -or $fallEnd -le $fallStart) {
    throw 'CompleteFall lifecycle boundary is unavailable'
}
$fall = $service.Substring($fallStart, $fallEnd - $fallStart)
$restoreIndex = $fall.IndexOf('RestoreWalls(')
$completeIndex = $fall.IndexOf(
    'pState.Phase = BanditStrongholdPhase.Completed')
$removeIndex = $fall.IndexOf('World.world.cities.removeObject')
if ($restoreIndex -lt 0 -or $completeIndex -le $restoreIndex -or
    $removeIndex -le $restoreIndex) {
    throw 'Stronghold walls must restore before completion and city removal'
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
