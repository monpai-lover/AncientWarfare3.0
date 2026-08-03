$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$meta = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\AWMapModeMetaLibrary.cs')
$service = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\HierarchicalVassalMapModeService.cs')

function Require([string]$text, [string]$needle, [string]$message) {
    if (-not $text.Contains($needle)) { throw $message }
}

function Forbid([string]$text, [string]$needle, [string]$message) {
    if ($text.Contains($needle)) { throw $message }
}

Require $meta 'pAsset.draw_zones = DrawZones;' `
    'kingdom-style draw delegate is missing'
Require $meta 'HierarchicalVassalMapModeService.BeginNativeDrawPass();' `
    'native label pass does not begin with zone rendering'
Require $meta 'RecordNativeDrawZone(zone);' `
    'native zones do not contribute label statistics'
Require $meta 'HierarchicalVassalMapModeService.EndNativeDrawPass();' `
    'native labels are not finalized with zone rendering'
Require $meta 'finally' 'native pass is not finalized on failure'
Require $service 'NativeDrawMetaCache.TryGetValue' `
    'native neighbour lookups do not use the transient cache'
Forbid $service 'GetLiveZonesForRepresentative' `
    'country labels still rescan live zones per representative'

Write-Output 'HierarchicalVassalMapNativeRenderSourceGuard: PASS'
