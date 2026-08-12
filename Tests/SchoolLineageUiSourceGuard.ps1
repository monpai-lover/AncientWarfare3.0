$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$schoolWindow = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/windows/SchoolWindow.cs')
$navigation = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/SchoolActorNavigation.cs')
$rosterWindow = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/windows/SchoolRosterWindow.cs')

if ($schoolWindow -notmatch '\.Take\(_lineageRows\.Count\)') {
    throw 'ShowLineage must keep the v1.1.2 fixed-row rendering contract.'
}
if ($schoolWindow -match 'EnsureLineageRows|LogLineageDiagnostic|_lastLineageDiagnosticSignature') {
    throw 'ShowLineage must not dynamically mutate its row pool or diagnostic state.'
}
if ($schoolWindow -match 'SchoolMembershipService\.AllActive\(\)') {
    throw 'ShowLineage must use the per-school membership index, not enumerate all active records.'
}
if ($schoolWindow -match '\[SchoolWindow\.ShowLineage\]') {
    throw 'ShowLineage must not emit per-refresh diagnostic logs.'
}
if ($navigation -notmatch 'ActionLibrary\.openUnitWindow\(pActor\)') {
    throw 'School actor navigation must match FamilyTreeWindow and use ActionLibrary.openUnitWindow.'
}
if ($navigation -notmatch 'if \(pActor == null \|\| pActor\.isRekt\(\)\) return;') {
    throw 'School actor navigation must use the same null/rekts guard as FamilyTreeWindow.'
}
if ($navigation -match 'pActor\?\.data|pActor\.isAlive\(\)') {
    throw 'School actor navigation must not impose liveness or data guards that FamilyTreeWindow does not use.'
}
if ($navigation -match 'selectAndInspect|SelectedUnit\.(clear|select)|ScrollWindow\.finishAnimations\(\)|ScrollWindow\.showWindow\("unit"\)|SchoolMapModeService\.EndWindowMode') {
    throw 'School actor navigation must not add a second window-selection protocol beside FamilyTreeWindow.'
}
if ($rosterWindow -notmatch '(?s)private void Refresh\(\).*CancelPendingRender\(\);\s*HideNodesAndLinks\(\);\s*UpdateSchoolSelector\(\);') {
    throw 'The school roster must clear pending rendering at refresh start like v1.1.2.'
}
if ($rosterWindow -match 'needsInitialModel') {
    throw 'The school roster must not add a second initial synchronous apply path.'
}
if ($rosterWindow -notmatch '(?s)SchoolRosterLayout synchronousLayout = shadow\s*\?.*if \(shadow\)\s*ApplyRosterModel') {
    throw 'The school roster must keep synchronous materialization limited to shadow mode.'
}
if ($rosterWindow -match '(?s)private void ApplyRosterModel\(SchoolRosterReadModel model\)\s*\{\s*if \(model == null\) return;\s*CancelPendingRender\(\);\s*HideNodesAndLinks\(\);') {
    throw 'The school roster must not clear an accepted model again while applying it.'
}

Write-Output 'School lineage UI source guard passed.'
