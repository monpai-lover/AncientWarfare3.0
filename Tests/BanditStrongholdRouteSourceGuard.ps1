$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$routePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditRoute.cs'
$transitionPath = Join-Path $root 'Code/core/lineage/PeasantRebelGovernmentTransitionService.cs'
$servicePath = Join-Path $root 'Code/core/lineage/PeasantRebelRouteService.cs'
$wallServicePath = Join-Path $root 'Code/core/lineage/PeasantRebelBanditWallService.cs'
$zonePatchPath = Join-Path $root 'Code/patch/AW_BanditStrongholdZonePatch.cs'

$route = Get-Content -Raw -Encoding UTF8 $routePath
$transition = Get-Content -Raw -Encoding UTF8 $transitionPath
$service = Get-Content -Raw -Encoding UTF8 $servicePath
$wallService = Get-Content -Raw -Encoding UTF8 $wallServicePath

if ($route -notmatch 'PeasantRebelBanditStrongholdService\.TryCreate') {
    throw 'Bandit route does not use the stronghold creation transaction'
}
foreach ($legacy in @('PeasantRebelBanditTerritoryService.CaptureCurrentCities',
        'PeasantRebelBanditWallService.CaptureAndBuild')) {
    if ($route -match [regex]::Escape($legacy)) {
        throw "Bandit route still uses legacy entry mutation: $legacy"
    }
}
if ($transition -notmatch 'PeasantRebelBanditStrongholdService\.TryPlan') {
    throw 'Manual government transition does not use stronghold preflight'
}
foreach ($token in @('ReleaseToFounding', 'ResolveStronghold')) {
    if ($service -notmatch $token) {
        throw "Founding conversion is missing $token"
    }
}
if (-not (Test-Path -LiteralPath $zonePatchPath)) {
    throw 'Missing AW_BanditStrongholdZonePatch.cs'
}
if ($wallService -notmatch 'PeasantRebelBanditStateStore\.TryResolveActive') {
    throw 'Wall repair does not read active stronghold wall points'
}
$zonePatch = Get-Content -Raw -Encoding UTF8 $zonePatchPath
foreach ($token in @('HarmonyPatch(typeof(City), nameof(City.addZone))',
        'HarmonyPrefix', 'CanAcquireZone', 'IsReplicaSession',
        'IsApplying')) {
    if ($zonePatch -notmatch [regex]::Escape($token)) {
        throw "Fixed-zone patch is missing $token"
    }
}

Write-Output 'Bandit stronghold route source guard passed.'
