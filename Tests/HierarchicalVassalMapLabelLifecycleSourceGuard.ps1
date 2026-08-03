$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeService.cs')
$labels = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeLabelLayer.cs')

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

Write-Output 'HierarchicalVassalMapLabelLifecycleSourceGuard: PASS'
