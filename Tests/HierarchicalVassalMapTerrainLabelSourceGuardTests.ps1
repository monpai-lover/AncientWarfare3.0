$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeService.cs')
$labels = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeLabelLayer.cs')

function Require([string]$text, [string]$needle, [string]$message) {
    if (-not $text.Contains($needle)) { throw $message }
}

function Forbid([string]$text, [string]$needle, [string]$message) {
    if ($text.Contains($needle)) { throw $message }
}

Require $service 'HandleZoneClick(WorldTile pTile, string pPowerId)' `
    'terrain drill-down is missing'
Require $service 'State.TryPushFocus' `
    'direct-vassal terrain cannot open the next layer'
Require $service 'State.PopFocus()' `
    'focused terrain cannot return to its parent'
Require $service 'GetProjectedStateName' `
    'country labels do not use projected state names'
Require $labels 'TextMesh' 'labels are not world-space TextMesh objects'
Require $labels 'LocalizedTextManager.current_font' `
    'labels do not use the localized game font'
Require $labels 'ApplyRuntimeLabel' `
    'native results have no TextMesh publication entry point'
Forbid $labels 'HierarchicalVassalMapLabelRuntime.ProcessFrame();' `
    'visible labels still depend on the multi-frame worker runtime'

Write-Output 'HierarchicalVassalMapTerrainLabelSourceGuard: PASS'
