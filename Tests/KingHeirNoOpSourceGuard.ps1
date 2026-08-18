$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$path = Join-Path $projectRoot 'Code/core/lineage/HeirService.cs'
$source = Get-Content -Raw -Encoding UTF8 $path

function Get-Slice([string] $start, [string] $end) {
    $startIndex = $source.IndexOf($start, [StringComparison]::Ordinal)
    $endIndex = $source.IndexOf($end, $startIndex + $start.Length,
        [StringComparison]::Ordinal)
    if ($startIndex -lt 0 -or $endIndex -le $startIndex) {
        throw "Cannot locate HeirService slice: $start"
    }
    return $source.Substring($startIndex, $endIndex - $startIndex)
}

$refresh = Get-Slice 'private static Actor RefreshHeirAndReturn' `
    'private static long ResolveReferenceKingId'
$manual = Get-Slice 'public static bool StoreSelectedHeir' `
    'public static string ResolveSuccessionModeForCandidate'
$store = Get-Slice 'private static Actor StoreHeirSelection' `
    'private static HeirSelection SelectByEffectiveLaw'

foreach ($caller in @($refresh, $manual)) {
    if ($caller.Contains('ClearOldHeirFlag')) {
        throw 'Heir callers must not scan registrations before the no-op gate.'
    }
}

$gate = $store.IndexOf('HeirSelectionSignatureRules.IsUnchanged',
    [StringComparison]::Ordinal)
$clear = $store.IndexOf('ClearOldHeirFlag(pKingdom)',
    [StringComparison]::Ordinal)
$firstSideEffect = $store.IndexOf('FormerHeirService.ClearSnapshot',
    [StringComparison]::Ordinal)
if ($gate -lt 0 -or $clear -le $gate -or $firstSideEffect -le $clear) {
    throw 'The unchanged-heir gate must precede registration and heir maintenance.'
}

Write-Host 'King heir no-op source guard passed.'
