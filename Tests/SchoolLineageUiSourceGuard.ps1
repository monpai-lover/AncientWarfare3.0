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
if ($schoolWindow -notmatch '_lastLineageDiagnosticSignature') {
    throw 'Lineage diagnostics must be deduplicated by rendered membership state.'
}
if ($schoolWindow -notmatch '\[SchoolWindow\.Lineage\]') {
    throw 'Lineage diagnostics must identify the rendered school and members.'
}
if ($navigation -notmatch 'SelectedUnit\.select\(pActor\)') {
    throw 'School actor navigation must bind the actor used by UnitWindow.'
}
if ($navigation -notmatch 'ScrollWindow\.showWindow\("unit"\)') {
    throw 'School actor navigation must open the native UnitWindow after binding its actor.'
}
if ($navigation -match 'selectAndInspect\(pActor') {
    throw 'School actor navigation must not open UnitWindow through a meta selection that leaves SelectedUnit stale.'
}

Write-Output 'School lineage UI source guard passed.'
