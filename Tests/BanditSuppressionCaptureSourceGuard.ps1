$ErrorActionPreference = 'Stop'

$rules = Get-Content -Raw `
    'Code/core/lineage/CityOccupationAccelerationRules.cs'
$bridge = Get-Content -Raw `
    'Code/core/lineage/WarScoreRuntimeBridge.cs'
$patch = Get-Content -Raw `
    'Code/patch/AW_CityOccupationAccelerationPatch.cs'
$chroniclePatch = Get-Content -Raw 'Code/patch/AW_ChroniclePatch.cs'
$route = Get-Content -Raw `
    'Code/core/lineage/PeasantRebelRouteService.cs'

if (-not $rules.Contains(
        'ShouldCommitBanditSuppressionCapture(')) {
    throw 'occupation rules must define the bandit suppression capture exception'
}
if ($bridge -notmatch
        'IsActiveBanditSuppressionWar\(City\s+pCity,\s*\r?\n?\s*Kingdom\s+pOccupier\)') {
    throw 'war-score bridge must identify the active suppression counter-capture'
}
if (-not $patch.Contains(
        'WarScoreService.IsActiveBanditSuppressionWar(')) {
    throw 'city capture must consult the suppression counter-capture exception'
}
if (-not $patch.Contains(
        'ShouldCommitBanditSuppressionCapture(')) {
    throw 'city capture must apply the tested suppression exception'
}
if (-not $route.Contains(
        'OnBanditSuppressionCityAcquired(')) {
    throw 'route service must centralize the suppression conversion'
}
if ($route -notmatch
        'ConvertBanditToFounding\(\s*pNewOwner,\s*ResolveOrigin\(pNewOwner\),\s*\r?\n?\s*pPreserveActiveWar:\s*true') {
    throw 'suppression conversion must preserve the active suppression war'
}
if ($bridge -notmatch
        'IsActiveBanditSuppressionWar\(City\s+pCity,\s*\r?\n?\s*Kingdom\s+pOccupier,') {
    throw 'war-score bridge must support the former owner after City.setKingdom'
}
if (-not $chroniclePatch.Contains(
        'PeasantRebelRouteService.OnBanditSuppressionCityAcquired(')) {
    throw 'committed city ownership changes must trigger suppression conversion'
}
$exceptionIndex = $patch.IndexOf(
    'ShouldCommitBanditSuppressionCapture(')
$freezeIndex = $patch.IndexOf(
    'WarScoreService.TryFreezeCityOccupation(', $exceptionIndex)
if ($exceptionIndex -lt 0 -or $freezeIndex -lt 0 -or
    $exceptionIndex -gt $freezeIndex) {
    throw 'suppression counter-capture must be resolved before frozen occupation'
}
if (($patch -notmatch
        'PeasantRebelRouteService\.CanAcquireCity\(') -or
    ($patch -notmatch
        'IsActiveBanditSuppressionWar\(\s*__instance,\s*pNewSetKingdom\)')) {
    throw 'joinAnotherKingdom must allow only the tested suppression exception'
}

Write-Output 'Bandit suppression capture source guard passed.'
