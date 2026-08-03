$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeService.cs')
$labels = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeLabelLayer.cs')
$rules = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeRules.cs')
$minimapPatch = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\patch\AW_HierarchicalVassalMapMinimapPatch.cs')
$framePatch = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\patch\AW_HierarchicalVassalMapLabelPatch.cs')

function Require([string]$text, [string]$needle, [string]$message) {
    if (-not $text.Contains($needle)) { throw $message }
}

Require $service 'city.city_center' `
    'city labels do not use the native city anchor'
Require $service 'PublishNativeCityLabels();' `
    'city labels are not published from the native draw pass'
Require $service 'HideRuntimeLabelsExcept(null);' `
    'layer switch does not hide previous labels immediately'
Require $service 'setDrawnZonesDirty();' `
    'layer switch does not request a native redraw'
Require $labels 'PositionThreshold' `
    'label nodes do not cache position changes'
Require $labels 'SizeThreshold' `
    'label nodes do not cache size changes'
Require $labels 'AngleThreshold' `
    'label nodes do not cache angle changes'
Require $labels 'HasEquivalentLayout' `
    'equivalent label state still rebuilds TextMesh objects'
Require $labels 'RefreshStyle(pColor, pOutlineColor);' `
    'color-only label changes still rebuild glyph geometry'
Require $labels 'EvictNativeCity' `
    'destroyed cities leave native TextMesh nodes cached forever'
Require $labels 'EvictNativeKingdom' `
    'destroyed kingdoms leave native TextMesh nodes cached forever'
Require $service 'EvictNativeCity(pCity.id);' `
    'city destruction does not evict its native label nodes'
Require $service 'EvictNativeKingdom(pKingdomId);' `
    'kingdom destruction does not evict its native label nodes'
Require $service 'EvictNativeCity(pKingdom.cities[index].id);' `
    'kingdom destruction leaves its native city label nodes cached'
Require $labels 'GetNativeLabelKey' `
    'native label keys are rebuilt as strings every redraw'
Require $labels 'RefreshSortingLayer(pMinimap)' `
    'resolution refresh discards the requested minimap state'
Require $labels 'ApplySortingLayer(bool pCountry, bool pMinimap)' `
    'label sorting does not use an explicit render target'
if ($labels.Contains('MapBox.isRenderMiniMap()')) {
    throw 'label sorting races the global minimap render flag'
}
Require $rules 'CountryLabelVisualScale = 2.0f' `
    'country map labels no longer preserve their requested visual scale'
Require $rules 'CityLabelVisualScale = 1.65f' `
    'city map labels no longer use the tuned reduced visual scale'
Require $labels `
    'ResolveRenderedLabelSize(pPlacement.Size, pCountry);' `
    'runtime labels bypass the shared map-label visual-size rule'
Require $rules 'ResolveRenderedLabelSize(' `
    'map label visual scaling is not centralized'
Require $framePatch `
    'ObserveResolutionMode(MapBox.isRenderMiniMap());' `
    'map label sorting does not follow the authoritative resolution state'
if ($minimapPatch.Contains('PrepareHierarchicalLabelsForMinimap') -or
    $minimapPatch.Contains('RestoreHierarchicalLabelsAfterMinimap')) {
    throw 'map label sorting still treats pixel redraw as the render lifecycle'
}

$sizeRuleStart = $rules.IndexOf('ResolveRenderedLabelSize(')
$sizeRuleEnd = $rules.IndexOf(
    'internal static int GetLabelOutlinePassCount(', $sizeRuleStart)
if ($sizeRuleStart -lt 0 -or $sizeRuleEnd -le $sizeRuleStart) {
    throw 'map label visual-size rule could not be inspected'
}
$sizeRule = $rules.Substring($sizeRuleStart,
    $sizeRuleEnd - $sizeRuleStart)
if ($sizeRule -notmatch `
    'Math\.Min\(maximum,\s*Math\.Max\(minimum,\s*pPlacementSize\)\)') {
    throw 'map label placement is not clamped to its logical size range first'
}
if ($sizeRule -notmatch `
    'pCountry\s*\?\s*CountryLabelVisualScale\s*:\s*CityLabelVisualScale') {
    throw 'map label visual scale does not distinguish country and city text'
}
if ($sizeRule -notmatch 'logicalSize\s*\*\s*visualScale') {
    throw 'map label visual scale is still swallowed by the logical size cap'
}
if ($service.Contains('"native:city:" +') -or
    $service.Contains('"native:country:" +')) {
    throw 'native label keys still allocate once per label per redraw'
}

$layoutStart = $labels.IndexOf('private bool HasEquivalentLayout(')
$layoutEnd = $labels.IndexOf('private static float AngleDistance(',
    $layoutStart)
if ($layoutStart -lt 0 -or $layoutEnd -le $layoutStart -or
    $labels.Substring($layoutStart, $layoutEnd - $layoutStart).
        Contains('ColorsEqual(')) {
    throw 'style changes are still part of TextMesh geometry equivalence'
}

Write-Output 'HierarchicalVassalMapLabelLifecycleSourceGuard: PASS'
