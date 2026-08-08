$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$productionPaths = @(
    (Join-Path $root 'Code\core\policy\HierarchicalVassalMapModeService.cs'),
    (Join-Path $root 'Code\core\policy\HierarchicalVassalMapModeLabelLayer.cs'),
    (Join-Path $root 'Code\core\policy\HierarchicalVassalMapLabelRuntime.cs'),
    (Join-Path $root 'Code\core\policy\AWMapModeMetaLibrary.cs'),
    (Join-Path $root 'Code\patch\AW_HierarchicalVassalMapLabelPatch.cs')
)
$production = ($productionPaths | ForEach-Object {
    Get-Content -Raw -LiteralPath $_
}) -join "`n"
$service = Get-Content -Raw -LiteralPath $productionPaths[0]
$chronicle = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\patch\AW_ChroniclePatch.cs')
$rename = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\KingdomRenameProjectionService.cs')

function Require([string]$text, [string]$needle, [string]$message) {
    if (-not $text.Contains($needle)) { throw $message }
}

function Forbid([string]$text, [string]$needle, [string]$message) {
    if ($text.Contains($needle)) { throw $message }
}

$obsoleteSymbols = @(
    'ZoneToKingdomId',
    'HierarchicalVassalMapModeCityCache',
    'HierarchicalVassalMapModeChangeTracker',
    'ShouldRunZoneCalculatorDraw',
    'ShouldRunNativeZoneClear',
    'NotifyExplicitNativeZoneClearRequested',
    'ProcessZoneDrawBatch',
    'ProcessPendingLocalRedraw',
    'BuildVisibleSnapshot',
    'HierarchicalVassalMapModeSnapshot'
)

foreach ($symbol in $obsoleteSymbols) {
    Forbid $production $symbol `
        "obsolete hierarchical map runtime symbol remains: $symbol"
}

Require $service 'Kingdom physicalKingdom = city?.kingdom;' `
    'zone meta resolution does not begin from the live city kingdom'
Require $service 'ReferenceEquals(cached.PhysicalKingdom,' `
    'persistent zone metadata does not revalidate the live city owner'
Require $service 'HierarchicalVassalHierarchyIndex' `
    'hierarchical map mode does not use the lightweight hierarchy index'
Require $service `
    'HierarchicalVassalMapModeLabelLayer.MarkCityDirty(pCity);' `
    'city name or ownership changes do not notify source-scoped invalidation'
Require $service `
    'HierarchicalVassalMapModeLabelLayer.MarkCityGeometryDirty(pCity);' `
    'city zone changes do not preserve threshold-aware invalidation'
Require $service `
    'HierarchicalVassalMapModeLabelLayer.MarkKingdomDirty(pKingdom);' `
    'kingdom changes do not notify source-scoped invalidation'
Require $service `
    'HierarchicalVassalMapModeLabelLayer.MarkHierarchyDirty();' `
    'hierarchy changes do not invalidate country hierarchy sources'
Require $service `
    'HierarchicalVassalMapModeLabelLayer.EvictCity(pCity.id);' `
    'destroyed cities do not evict cached labels across visited focuses'
Require $service `
    'HierarchicalVassalMapModeLabelLayer.EvictKingdom(pKingdomId);' `
    'destroyed kingdoms do not evict entity and focus label caches'
Require $service 'internal static void MarkCityGeometryDirty(City pCity)' `
    'city zone changes have no label-only invalidation entry point'
Require $service `
    'internal static void MarkCityZoneGeometryDirty(City pCity, TileZone pZone)' `
    'City.addZone has no changed-zone invalidation entry point'
Require $chronicle `
    'CityAddZone_Postfix(City __instance, TileZone pZone)' `
    'City.addZone postfix does not receive the changed Zone'
Require $chronicle `
    'HierarchicalVassalMapModeService.MarkCityZoneGeometryDirty(__instance, pZone);' `
    'City.addZone does not use changed-zone invalidation'
$incrementalStart = $service.IndexOf(
    'internal static void MarkCityZoneGeometryDirty(City pCity, TileZone pZone)')
$removeCityStart = $service.IndexOf('internal static void RemoveCity(',
    $incrementalStart)
if ($incrementalStart -lt 0 -or $removeCityStart -le $incrementalStart) {
    throw 'changed-zone invalidation body could not be located'
}
$incrementalBody = $service.Substring($incrementalStart,
    $removeCityStart - $incrementalStart)
Forbid $incrementalBody 'InvalidateCityMeta(pCity);' `
    'City.addZone still scans all city Zones for native cache invalidation'
Require $rename `
    'HierarchicalVassalMapModeService.MarkKingdomDirty(pKingdom);' `
    'projected kingdom-name changes do not force a country-label refresh'
$kingdomDirtyStart = $service.IndexOf('public static void MarkKingdomDirty')
$hierarchyDirtyStart = $service.IndexOf('public static void MarkHierarchyDirty',
    $kingdomDirtyStart)
if ($kingdomDirtyStart -lt 0 -or $hierarchyDirtyStart -le $kingdomDirtyStart) {
    throw 'kingdom label invalidation entry point could not be located'
}
$kingdomDirtyBody = $service.Substring($kingdomDirtyStart,
    $hierarchyDirtyStart - $kingdomDirtyStart)
Forbid $kingdomDirtyBody 'DirtyNativeZoneMap();' `
    'a projected name change still redraws the native political texture'

Write-Output 'HierarchicalVassalMapModeInvalidationSourceGuard: PASS'
