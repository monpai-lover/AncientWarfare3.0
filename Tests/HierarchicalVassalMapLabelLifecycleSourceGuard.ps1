$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeService.cs')
$labels = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeLabelLayer.cs')
$rules = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeRules.cs')

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
Require $rules 'MapLabelVisualScale = 2.0f' `
    'country and city map labels are not rendered at double visual size'
Require $labels `
    'pPlacement.Size * HierarchicalVassalMapModeRules.' `
    'the double label scale is declared but not applied to placements'
Require $labels 'MapLabelVisualScale' `
    'the placement scale does not use the shared map-label multiplier'
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
