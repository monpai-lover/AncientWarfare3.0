$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$failures = [Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    return [IO.File]::ReadAllText(
        (Join-Path $root $relativePath), [Text.Encoding]::UTF8)
}

function Require-Contains(
    [string]$text,
    [string]$needle,
    [string]$message) {
    if (-not $text.Contains($needle)) {
        $failures.Add($message)
    }
}

function Forbid-Contains(
    [string]$text,
    [string]$needle,
    [string]$message) {
    if ($text.Contains($needle)) {
        $failures.Add($message)
    }
}

$index = Read-Source 'Code\core\performance\AWNearbyStatusTargetIndex.cs'
foreach ($contract in @(
        'internal static bool TryFindClosest(',
        'RegisterTrackedStatusIds(statusIds);',
        'AWChunkWindowIndex.Get(self.current_tile.chunk, 1)',
        'indexedUnitMembershipVersion ==',
        'AWParallelSimObjectZoneUnits',
        'TryApplyChunkMembershipChanges(',
        'List<IndexedActor>',
        'LowerBound(candidates, unitOffset)')) {
    Require-Contains $index $contract `
        "Nearby status index is missing contract: $contract"
}
Forbid-Contains $index 'Cultiway.' `
    'Nearby status index must not depend on Cultiway ECS/content.'
Forbid-Contains $index 'return false;`r`n        }`r`n`r`n        internal static void AddUnitMembership' `
    'Nearby status membership hooks must not remain no-op stubs.'

$parallel = Read-Source `
    'Code\core\performance\AWParallelSimObjectZoneUnits.cs'
$parallel = [Regex]::Replace($parallel, '\s+', '')
foreach ($contract in @(
        'AWNearbyStatusTargetIndex.BeginUnitMembershipRebuild()',
        'AWNearbyStatusTargetIndex.AddUnitMembership(',
        'AWNearbyStatusTargetIndex.TryApplyChunkMembershipChanges(',
        'AWNearbyStatusTargetIndex.NotifyUnitMembershipRebuilt(')) {
    Require-Contains $parallel $contract `
        "Parallel membership rebuild is missing status-index contract: $contract"
}

$patch = Read-Source 'Code\patch\AW_NearbyStatusTargetIndexPatch.cs'
foreach ($contract in @(
        '"getClosestActorWithStatus"',
        'AWNearbyStatusTargetIndex.TryFindClosest(',
        'NotifyStatusAdded(',
        'NotifyStatusRemoved(',
        'NotifyAllStatusesRemoved(',
        'AWNearbyStatusTargetIndex.Reset();')) {
    Require-Contains $patch $contract `
        "Nearby status lifecycle patch is missing contract: $contract"
}
Forbid-Contains $patch 'nameof(BehTryFindTargetWithStatusNearby.execute)' `
    'Nearby status index patch must not collide with execute throttling patches.'

if ($failures.Count -gt 0) {
    Write-Output "Nearby status target index source guard failures: $($failures.Count)"
    foreach ($failure in $failures) {
        Write-Output " - $failure"
    }
    exit 1
}

Write-Output 'Nearby status target index source guard passed.'
