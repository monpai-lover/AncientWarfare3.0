$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$wallServicePath = Join-Path $root `
    'Code/core/lineage/CultiwayStyleCityWallService.cs'
$strongholdServicePath = Join-Path $root `
    'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'
$zoneWallServicePath = Join-Path $root `
    'Code/core/lineage/PeasantRebelBanditZoneWallService.cs'

if (-not (Test-Path -LiteralPath $wallServicePath)) {
    throw 'Missing CultiwayStyleCityWallService.cs'
}
if (-not (Test-Path -LiteralPath $strongholdServicePath)) {
    throw 'Missing PeasantRebelBanditStrongholdService.cs'
}
if (-not (Test-Path -LiteralPath $zoneWallServicePath)) {
    throw 'Missing PeasantRebelBanditZoneWallService.cs'
}

$wallService = Get-Content -Raw -Encoding UTF8 $wallServicePath
$strongholdService = Get-Content -Raw -Encoding UTF8 `
    $strongholdServicePath
$zoneWallService = Get-Content -Raw -Encoding UTF8 $zoneWallServicePath

foreach ($token in @('PeasantRebelBanditZoneWallRules.Build(',
        'ClosedWallPoints', 'WallPoints', 'zone.tiles',
        'TerrainCollectionPadding')) {
    if (-not $zoneWallService.Contains($token)) {
        throw "Zone-aligned wall runtime is missing $token"
    }
}

foreach ($token in @('CultiwayStyleCityWallPlan', 'TryPlanDetailed(',
        'ComputeEnclosedLand(input)', 'WallPoints', 'EnclosedLand')) {
    if (-not $wallService.Contains($token)) {
        throw "Detailed wall plan is missing $token"
    }
}

foreach ($token in @('TryPlanDetailed(', '.EnclosedLand',
        'zone.tiles', 'enclosedTiles', 'totalTiles',
        'new BanditZoneFact(ZoneKey(zone), enclosedTiles,')) {
    if (-not $strongholdService.Contains($token)) {
        throw "Stronghold wall-zone fit is missing $token"
    }
}

foreach ($forbidden in @('wallPoints.Min(', 'wallPoints.Max(',
        'center.x > minX', 'center.y > minY')) {
    if ($strongholdService.Contains($forbidden)) {
        throw "Stronghold still uses wall bounding rectangle: $forbidden"
    }
}

Write-Output 'Bandit stronghold wall-zone fit source guard passed.'
