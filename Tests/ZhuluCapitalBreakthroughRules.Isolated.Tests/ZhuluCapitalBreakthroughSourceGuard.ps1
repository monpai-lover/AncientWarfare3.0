$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$patch = Get-Content -Raw (Join-Path $root 'Code\patch\AW_CityOccupationAccelerationPatch.cs')
$service = Get-Content -Raw (Join-Path $root 'Code\core\lineage\ZhuluCapitalBreakthroughService.cs')
if (-not $patch.Contains('ZhuluCapitalBreakthroughService.TryApplyAfterCapture')) {
    throw 'capture completion must invoke Zhulu breakthrough service'
}
if (-not $service.Contains('ZhuluWarService.IsZhuluWar(war)')) {
    throw 'breakthrough service must reject ordinary wars'
}
if (-not $service.Contains('DeJureRegionStore.TryGetBySeat')) {
    throw 'breakthrough service must recognize de jure seats'
}
if (-not $service.Contains('pCapturedCity.neighbours_cities')) {
    throw 'capital breakthrough must inspect direct city neighbors'
}
if (-not $service.Contains('city.joinAnotherKingdom(pNewKingdom)')) {
    throw 'city transfer must use the existing city transfer chain'
}
Write-Output 'Zhulu capital breakthrough source guard passed.'
