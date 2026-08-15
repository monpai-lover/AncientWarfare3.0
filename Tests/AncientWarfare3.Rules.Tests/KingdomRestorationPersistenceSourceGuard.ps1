$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$path = Join-Path $repo `
    'Code/core/lineage/KingdomIdentityContinuityService.cs'
$source = [IO.File]::ReadAllText($path)
$start = $source.IndexOf(
    'private static void RestoreDetachedStatsRow(',
    [StringComparison]::Ordinal)
$end = $source.IndexOf(
    'private static void ClearDeadKingdomCache(', $start,
    [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) {
    throw 'Could not locate detached KingdomData rollback implementation.'
}
$rollback = $source.Substring($start, $end - $start)
if ($rollback.IndexOf('DBInserter.insertData(',
        [StringComparison]::Ordinal) -ge 0) {
    throw 'Restoration rollback must not append duplicate KingdomData rows to DBInserter.'
}
if ($rollback.IndexOf('InsertOrReplace(pSnapshot)',
        [StringComparison]::Ordinal) -lt 0) {
    throw 'Restoration rollback must atomically replace the detached KingdomData row.'
}

Write-Host 'Kingdom restoration persistence guard passed.'
