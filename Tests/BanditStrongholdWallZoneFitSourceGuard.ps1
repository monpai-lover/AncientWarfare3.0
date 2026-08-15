$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$wallServicePath = Join-Path $root `
    'Code/core/lineage/CultiwayStyleCityWallService.cs'
$strongholdServicePath = Join-Path $root `
    'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'
$zoneWallServicePath = Join-Path $root `
    'Code/core/lineage/PeasantRebelBanditZoneWallService.cs'
$strongholdRulesPath = Join-Path $root `
    'Code/core/lineage/PeasantRebelBanditStrongholdRules.cs'

if (-not (Test-Path -LiteralPath $wallServicePath)) {
    throw 'Missing CultiwayStyleCityWallService.cs'
}
if (-not (Test-Path -LiteralPath $strongholdServicePath)) {
    throw 'Missing PeasantRebelBanditStrongholdService.cs'
}
if (-not (Test-Path -LiteralPath $zoneWallServicePath)) {
    throw 'Missing PeasantRebelBanditZoneWallService.cs'
}
if (-not (Test-Path -LiteralPath $strongholdRulesPath)) {
    throw 'Missing PeasantRebelBanditStrongholdRules.cs'
}

$wallService = Get-Content -Raw -Encoding UTF8 $wallServicePath
$strongholdService = Get-Content -Raw -Encoding UTF8 `
    $strongholdServicePath
$zoneWallService = Get-Content -Raw -Encoding UTF8 $zoneWallServicePath
$strongholdRules = Get-Content -Raw -Encoding UTF8 $strongholdRulesPath

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
        'new BanditZoneFact(ZoneKey(zone), enclosedTiles,',
        'SelectZoneAlignedKeys(',
        'PeasantRebelBanditZoneWallService.TryPlan(',
        'InteriorZones = interior',
        'WallPoints = zoneWallPlan.WallPoints.ToList()',
        'FixedZoneKeys = interior.Select(ZoneKey)')) {
    if (-not $strongholdService.Contains($token)) {
        throw "Stronghold wall-zone fit is missing $token"
    }
}

$selectionIndex = $strongholdService.IndexOf('SelectZoneAlignedKeys(')
$wallIndex = $strongholdService.IndexOf(
    'PeasantRebelBanditZoneWallService.TryPlan(')
if ($selectionIndex -lt 0 -or $wallIndex -le $selectionIndex) {
    throw 'Stronghold must select native zones before planning its wall'
}

foreach ($forbidden in @('wallPoints.Min(', 'wallPoints.Max(',
        'center.x > minX', 'center.y > minY',
        'IsMajorityEnclosed', 'SelectInteriorZoneKeys(')) {
    if ($strongholdService.Contains($forbidden) -or
        $strongholdRules.Contains($forbidden)) {
        throw "Rejected stronghold zone rule remains: $forbidden"
    }
}

Write-Output 'Bandit stronghold wall-zone fit source guard passed.'
