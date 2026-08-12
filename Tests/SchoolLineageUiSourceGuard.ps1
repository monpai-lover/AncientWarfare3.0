$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$schoolWindow = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/windows/SchoolWindow.cs')
$navigation = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/SchoolActorNavigation.cs')

if ($schoolWindow -match '\.Take\(_lineageRows\.Count\)') {
    throw 'ShowLineage must not truncate source members to the fixed row pool.'
}
if ($schoolWindow -notmatch 'EnsureLineageRows') {
    throw 'ShowLineage must grow its row pool from the active member count.'
}
if ($schoolWindow -match 'SchoolMembershipService\.AllActive\(\)') {
    throw 'ShowLineage must use the per-school membership index, not enumerate all active records.'
}
if ($schoolWindow -match '\[SchoolWindow\.ShowLineage\]') {
    throw 'ShowLineage must not emit per-refresh diagnostic logs.'
}
if ($navigation -notmatch 'MetaType\.Unit\.getAsset\(\)') {
    throw 'School actor navigation must resolve the unit meta inspector.'
}
if ($navigation -notmatch 'selectAndInspect\(pActor, pFromNameplate: false,\s*\r?\n\s*pCheckNameplate: false, pClearAction: false\)') {
    throw 'School actor navigation must retain the current action while inspecting a member.'
}
if ($navigation -match 'ActionLibrary\.openUnitWindow\(pActor\)') {
    throw 'School actor navigation must not clear selection through ActionLibrary.openUnitWindow.'
}

Write-Output 'School lineage UI source guard passed.'
