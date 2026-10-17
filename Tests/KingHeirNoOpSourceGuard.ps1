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
$prepare = Get-Slice `
    'public static Actor ReconcileHeir' `
    'public static void MarkSuccessionDirtyForActor'
$manual = Get-Slice 'public static void StoreSelectedHeir' `
    'public static string ResolveSuccessionModeForCandidate'
$store = Get-Slice 'private static Actor StoreHeirSelection' `
    'internal static bool PrepareRegisteredHeirForAccession'

foreach ($caller in @($refresh, $prepare, $manual)) {
    if ($caller.Contains('ClearOldHeirFlag')) {
        throw 'Heir callers must not scan registrations before the no-op gate.'
    }
}

$gate = $store.IndexOf('HeirSelectionSignatureRules.IsUnchanged',
    [StringComparison]::Ordinal)
$clear = $store.IndexOf('ClearOldHeirFlag(pKingdom, heir?.data?.id ?? -1L)',
    [StringComparison]::Ordinal)
$firstSideEffect = $store.IndexOf('NormalizeHeirForRegistration(pKingdom, heir)',
    [StringComparison]::Ordinal)
if ($gate -lt 0 -or $firstSideEffect -le $gate -or $clear -le $firstSideEffect) {
    throw 'The unchanged-heir gate must precede registration and heir maintenance.'
}

Write-Host 'King heir no-op source guard passed.'
