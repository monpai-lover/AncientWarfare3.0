$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw (Join-Path $root 'Code\core\policy\HierarchicalVassalMapModeService.cs')
$job = Get-Content -Raw (Join-Path $root 'Code\core\policy\HierarchicalVassalLabelDiscoveryJob.cs')
$runtime = Get-Content -Raw (Join-Path $root 'Code\core\policy\HierarchicalVassalMapLabelRuntime.cs')
$key = Get-Content -Raw (Join-Path $root 'Code\core\policy\HierarchicalVassalLabelCacheKey.cs')
$promotion = Get-Content -Raw (Join-Path $root 'Code\patch\AW_PromotionPatch.cs')
$chronicle = Get-Content -Raw (Join-Path $root 'Code\patch\AW_ChroniclePatch.cs')
foreach ($pair in @(
    @($service, 'CityAdministrationState'),
    @($service, 'BuildCityAdministrationRegionSources'),
    @($service, 'IsCityRegionLayer'),
    @($service, 'HandleCityRegionClick'),
    @($service, 'CityAdministrationState.FocusSeatCityId'),
    @($job, 'RegionSources'),
    @($runtime, 'AppendRegionSource'),
    @($runtime, '"region"'),
    @($key, 'parts[2] != "region"'),
    @($service, 'CityAdministrationMapModeRules.ResolveClick'),
    @($service, 'CityAdministrationMapClickAction.PopToRegions'),
    @($service, 'if (IsCityLayer) return false;'),
    @($service, 'RegionalGovernmentAggregationService.Invalidate(pCity.kingdom)'),
    @($service, 'RegionalGovernmentAggregationService.Invalidate(pFormerKingdom)'),
    @($service, 'AdministrativeLabel('),
    @($runtime, 'AdministrativeLabel(')
)) {
    if (-not $pair[0].Contains($pair[1])) { throw "Missing city administration token: $($pair[1])" }
}
if ($service -notmatch 'RemoveCity\(City pCity,\s*Kingdom pFormerKingdom') {
    throw 'City removal must accept the former kingdom captured before destruction'
}
if ([regex]::Matches($promotion,
        'InvalidateRegionalGovernmentCache\(__instance, __state\);').Count -lt 2) {
    throw 'setLeader/removeLeader must invalidate regional cache before guarded career effects'
}
if ($promotion -notmatch 'SetLeader_Postfix[\s\S]*?\{\s*InvalidateRegionalGovernmentCache\(__instance, __state\);\s*if \(AW3MultiplayerReplicaScope') {
    throw 'setLeader cache invalidation must precede multiplayer early return'
}
if ($promotion -notmatch 'RemoveLeader_Postfix[\s\S]*?\{\s*InvalidateRegionalGovernmentCache\(__instance, __state\);\s*if \(AW3MultiplayerReplicaScope') {
    throw 'removeLeader cache invalidation must precede multiplayer early return'
}
foreach ($token in @('DestroyCity_Prefix', 'out Kingdom __state',
        'RemoveCity(__instance, __state)')) {
    if (-not $chronicle.Contains($token)) {
        throw "City destruction must preserve its former kingdom: $token"
    }
}
Write-Output 'City administration map mode source guard PASS'
