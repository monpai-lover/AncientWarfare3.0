$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$enemy = Get-Content -Raw (Join-Path $root 'Code\patch\AW_EnemyFinderCachePatch.cs')
$building = Get-Content -Raw (Join-Path $root 'Code\patch\AW_BuildingNullSafetyPatch.cs')
$cityBuild = Get-Content -Raw (Join-Path $root 'Code\patch\AW_CityBuildNullSafetyPatch.cs')
$visual = Get-Content -Raw (Join-Path $root 'Code\patch\AW_ActorVisualRolePatch.cs')

if (-not $enemy.Contains('kingdom.asset == null')) {
    throw 'EnemyFinder guard must reject kingdoms with missing assets.'
}
if (-not $enemy.Contains('ClearNegativeKeys')) {
    throw 'EnemyFinder cleanup must remain wired.'
}
if (-not $building.Contains('__instance.asset != null') -or
    -not $building.Contains('__instance.data != null')) {
    throw 'Building construction checks must reject stale asset/data references.'
}
if (-not $cityBuild.Contains('under_construction_building = null')) {
    throw 'City build guard must clear stale construction references.'
}
if (-not $visual.Contains('"bandit_male"') -or
    -not $visual.Contains('"bandit_female"') -or
    -not $visual.Contains('PeasantRebelRouteService.IsBandit') -or
    -not $visual.Contains('TryGetBanditCivilianTexturePath') -or
    -not $visual.Contains('TryGetBanditKingTexturePath') -or
    -not $visual.Contains('heads_special/head_bandit_general') -or
    -not $visual.Contains('ActorAnimationLoader.getHeadSpecial')) {
    throw 'Bandit civilian texture routing is missing.'
}
Write-Output 'Null-reference boundary source guard passed.'
