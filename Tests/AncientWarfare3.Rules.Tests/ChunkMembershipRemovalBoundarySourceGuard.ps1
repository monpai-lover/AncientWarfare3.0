$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$source = Get-Content -Raw (Join-Path $root 'Code\core\performance\AWIncrementalChunkActorMembership.cs')

if (-not $source.Contains('bool removedFromAll =')) {
    throw 'chunk removal must record whether units_all actually contained the actor'
}
if (-not $source.Contains('RemoveActorReferences(container.units_all, actor)')) {
    throw 'chunk removal must repair all-units references idempotently'
}
if (-not $source.Contains('RemoveActorReferences(kingdomUnits, actor)')) {
    throw 'chunk removal must repair the requested kingdom projection idempotently'
}
if (-not $source.Contains('foreach (List<Actor> units in unitsByKingdom.Values)')) {
    throw 'chunk removal must scan remaining kingdom projections when the requested one is stale'
}

$removeStart = $source.IndexOf('internal static void Remove(')
$addStart = $source.IndexOf('internal static void Add(', $removeStart)
if ($removeStart -lt 0 -or $addStart -lt 0) {
    throw 'could not locate chunk removal boundary'
}
$remove = $source.Substring($removeStart, $addStart - $removeStart)
if ($remove.Contains('throw new InvalidOperationException')) {
    throw 'chunk removal must not throw when one projection is already absent'
}
if (-not $remove.Contains('if (removedFromAll)')) {
    throw 'chunk total count must only change after an actual units_all removal'
}

Write-Output 'ChunkMembershipRemovalBoundarySourceGuard passed.'
