$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$rules = Get-Content -Raw (Join-Path $root 'Code/core/lineage/FamilyTreeRelationRules.cs')
$bulk = Get-Content -Raw (Join-Path $root 'Code/core/lineage/LineageBulkQuery.cs')
$query = Get-Content -Raw (Join-Path $root 'Code/core/lineage/LineageQuery.cs')
$window = Get-Content -Raw (Join-Path $root 'Code/ui/windows/FamilyTreeWindow.cs')
$dto = Get-Content -Raw (Join-Path $root 'Code/core/lineage/LineageDTO.cs')

if ($rules -notmatch 'pHasHeldTitle' -or
    $rules -notmatch 'UsesBilateralBigTree') {
    throw 'family-tree relation rules must use profile-aware title-holder facts'
}
if ($bulk -notmatch 'KingdomReign' -or $bulk -notmatch 'Enfeoffment' -or
    $bulk -notmatch 'HasHeldTitle') {
    throw 'bulk family-tree snapshots must read and carry title-holder evidence'
}
if ($query -notmatch 'HasHeldTitle' -or
    $query -notmatch 'ShouldIncludeBigTreeEdge') {
    throw 'synchronous family-tree fallback must apply title-aware edge rules'
}
if ($query -notmatch 'ReadHot\(live\)\.Rank\s*>\s*NobleRankRules\.RankNone' -or
    $query -match 'ReadHot\(live\)\.IsActive') {
    throw 'live western women must qualify by an actual noble rank, not a princess or ceremonial title'
}
if ($window -notmatch 'ShouldIncludeBigTreeEdge' -or
    $window -notmatch 'HasHeldTitle') {
    throw 'incremental family-tree materialization must apply title-aware edges'
}
if ($dto -notmatch 'has_held_title') {
    throw 'family-tree nodes must carry persisted title-holder evidence'
}

Write-Output 'Western bilateral family-tree source guard passed.'
