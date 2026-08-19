$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw (Join-Path $root 'Code\core\policy\HierarchicalVassalMapModeService.cs')
$job = Get-Content -Raw (Join-Path $root 'Code\core\policy\HierarchicalVassalLabelDiscoveryJob.cs')
$runtime = Get-Content -Raw (Join-Path $root 'Code\core\policy\HierarchicalVassalMapLabelRuntime.cs')
$key = Get-Content -Raw (Join-Path $root 'Code\core\policy\HierarchicalVassalLabelCacheKey.cs')
foreach ($pair in @(
    @($service, 'CityAdministrationState'),
    @($service, 'BuildCityAdministrationRegionSources'),
    @($service, 'IsCityRegionLayer'),
    @($service, 'HandleCityRegionClick'),
    @($service, 'CityAdministrationState.FocusSeatCityId'),
    @($job, 'RegionSources'),
    @($runtime, 'AppendRegionSource'),
    @($runtime, '"region"'),
    @($key, 'parts[2] != "region"')
)) {
    if (-not $pair[0].Contains($pair[1])) { throw "Missing city administration token: $($pair[1])" }
}
Write-Output 'City administration map mode source guard PASS'
