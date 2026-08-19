$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$aggregation = Get-Content -Raw (Join-Path $root 'Code\core\court\RegionalGovernmentAggregationService.cs')
$rules = Get-Content -Raw (Join-Path $root 'Code\core\court\RegionalGovernmentRules.cs')
foreach ($pair in @(
    @($aggregation, 'city.neighbours_cities_kingdom'),
    @($aggregation, 'city.kingdom != pKingdom'),
    @($aggregation, 'Cache[pKingdom.id]'),
    @($aggregation, 'Invalidate(Kingdom pKingdom)'),
    @($rules, 'MaximumNeighborMembers'),
    @($rules, 'OrderByDescending(Development)'),
    @($rules, 'ThenBy(city => city.CityId)')
)) {
    if (-not $pair[0].Contains($pair[1])) {
        throw "Regional aggregation guard missing: $($pair[1])"
    }
}
if ($aggregation -match 'SQLite|Persist|Save|Serialize') {
    throw 'Regional projections must remain runtime-derived and non-persistent'
}
Write-Output 'Regional government aggregation source guard PASS'
