$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cityBuild = Get-Content -Raw (Join-Path $root `
    'Code\patch\AW_CityBuildNullSafetyPatch.cs')
$banditStronghold = Get-Content -Raw (Join-Path $root `
    'Code\core\lineage\PeasantRebelBanditStrongholdService.cs')

if (-not $cityBuild.Contains('under_construction_building = null')) {
    throw 'City build guard must clear stale construction references.'
}
if ($cityBuild.Contains('updateDirtyBuildings') -or
    $cityBuild.Contains('foreach (TileZone zone') -or
    $cityBuild.Contains('RemoveInvalid(')) {
    throw 'City build guard must not scan city or zone building indexes.'
}
if ($banditStronghold.Contains('World.world.buildings.removeObject(')) {
    throw 'Bandit stronghold buildings must use the native final-removal lifecycle.'
}

Write-Output 'City building removal source guard passed.'
