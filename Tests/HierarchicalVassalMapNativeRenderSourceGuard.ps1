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

Require $meta '? DrawHierarchicalZones' `
    'hierarchical asset does not select the native draw wrapper'
Require $meta ': DrawZones;' `
    'other kingdom-style assets lost their generic draw delegate'
Require $meta 'MetaTypeLibrary.kingdom?.draw_zones' `
    'hierarchical country rendering does not invoke the vanilla delegate'
Require $meta 'HierarchicalVassalMapModeService.BeginNativeDrawPass();' `
    'native label pass does not begin with zone rendering'
Require $service 'RecordNativeDrawZone(pZone);' `
    'resolved native zones do not contribute label statistics'
Require $meta 'HierarchicalVassalMapModeService.EndNativeDrawPass();' `
    'native labels are not finalized with zone rendering'
Require $meta 'finally' 'native pass is not finalized on failure'
Require $service 'NativeDrawMetaCache.TryGetValue' `
    'native neighbour lookups do not use the transient cache'
Require $service 'NativeCountryLabelPool' `
    'country label statistics allocate new entries every native draw'
Require $service 'NativeCityLabelPool' `
    'city label statistics allocate new entries every native draw'
Require $service 'NativeActiveLabelKeys.Clear();' `
    'active label publication allocates a new key set every native draw'
$endPassStart = $service.IndexOf('internal static void EndNativeDrawPass()')
$endPassFinally = $service.IndexOf('finally', $endPassStart)
$endPassCatch = $service.IndexOf('catch (Exception error)', $endPassStart)
if ($endPassStart -lt 0 -or $endPassCatch -lt $endPassStart -or
    $endPassFinally -le $endPassCatch -or
    -not $service.Substring($endPassCatch,
        $endPassFinally - $endPassCatch).
        Contains('HideRuntimeLabelsExcept(NativeActiveLabelKeys);')) {
    throw 'failed native publication leaves stale labels visible'
}

$resolveStart = $service.IndexOf('private static IMetaObject ResolveMetaForZone(')
$resolveEnd = $service.IndexOf('internal static void BeginNativeDrawPass()',
    $resolveStart)
$resolveRegion = if ($resolveStart -ge 0 -and $resolveEnd -gt $resolveStart) {
    $service.Substring($resolveStart, $resolveEnd - $resolveStart)
} else { '' }
$validKingdom = $resolveRegion.IndexOf(
    'if (!IsValidKingdom(pPhysicalKingdom)) return null;')
$cityLayer = $resolveRegion.IndexOf('if (IsCityLayer)')
if ($validKingdom -lt 0 -or $cityLayer -lt 0 -or
    $validKingdom -gt $cityLayer) {
    throw 'city layer publishes neutral or invalid kingdom cities'
}

foreach ($methodName in @('PublishNativeCityLabels()',
    'PublishNativeCountryLabels()')) {
    $publishStart = $service.IndexOf(
        "private static void $methodName")
    $nextMethod = $service.IndexOf('private static ', $publishStart + 1)
    $publishRegion = if ($publishStart -ge 0 -and
        $nextMethod -gt $publishStart) {
        $service.Substring($publishStart, $nextMethod - $publishStart)
    } else { '' }
    $apply = $publishRegion.IndexOf('ApplyRuntimeLabel(')
    $activate = $publishRegion.IndexOf('NativeActiveLabelKeys.Add(key);')
    if ($apply -lt 0 -or $activate -lt 0 -or $activate -lt $apply) {
        throw "$methodName marks a label active before TextMesh apply succeeds"
    }
}
if ($service.Contains(
    'new List<NativeCountryLabelEntry>(NativeCountryLabels.Values)')) {
    throw 'country publication allocates a new list every native draw'
}
if ($service.Contains(
    'new List<NativeCityLabelEntry>(NativeCityLabels.Values)')) {
    throw 'city publication allocates a new list every native draw'
}
Forbid $service 'GetLiveZonesForRepresentative' `
    'country labels still rescan live zones per representative'

Write-Output 'HierarchicalVassalMapNativeRenderSourceGuard: PASS'
