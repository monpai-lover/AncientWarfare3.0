$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$schoolWindow = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/windows/SchoolWindow.cs')
$navigation = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/SchoolActorNavigation.cs')
$rosterWindow = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/windows/SchoolRosterWindow.cs')
$traitPatch = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/patch/AW_TraitWindowSafetyPatch.cs')

if ($schoolWindow -notmatch '\.Take\(_lineageRows\.Count\)') {
    throw 'ShowLineage must keep the v1.1.2 fixed-row rendering contract.'
}
if ($schoolWindow -match 'EnsureLineageRows|LogLineageDiagnostic|_lastLineageDiagnosticSignature') {
    throw 'ShowLineage must not dynamically mutate its row pool or diagnostic state.'
}
if ($schoolWindow -match 'SchoolMembershipService\.AllActive\(\)') {
    throw 'ShowLineage must use the per-school membership index, not enumerate all active records.'
}
if ($schoolWindow -notmatch '(?s)private float ShowLineage\(string pSchoolId, float pTop\).*HistoricalSchoolMasterRegistry\.All') {
    throw 'ShowLineage must include historical school masters in the lineage display.'
}
if ($schoolWindow -notmatch '(?s)private float ShowLineage\(string pSchoolId, float pTop\).*HistoricalSchoolStore\.LoadMasterStates\(\)') {
    throw 'ShowLineage must use persisted master state to render deceased founders.'
}
if ($schoolWindow -notmatch 'LineageRowPoolSize\s*=\s*32') {
    throw 'The school detail lineage row pool must be large enough for founders and members.'
}
if ($traitPatch -notmatch 'TraitsContainer<ActorTrait, ActorTraitButton>') {
    throw 'Trait window safety must target the concrete unit trait container.'
}
if ($traitPatch -notmatch 'HarmonyPrefix|_trait_window') {
    throw 'Trait window safety must guard sortTraits before UnitWindow owner binding.'
}
if ($traitPatch -notmatch 'Finalizer|Exception') {
    throw 'Trait window safety must suppress the initialization-only null trait exception.'
}
if ($schoolWindow -match '\[SchoolWindow\.ShowLineage\]') {
    throw 'ShowLineage must not emit per-refresh diagnostic logs.'
}
if ($navigation -notmatch 'MetaType\.Unit\.getAsset\(\)') {
    throw 'School actor navigation must use the native unit MetaType path.'
}
if ($navigation -notmatch 'pClearAction: false') {
    throw 'School actor navigation must preserve the school window action context.'
}
if ($navigation -notmatch 'ScrollWindow\.finishAnimations\(\)') {
    throw 'School actor navigation must finish the current window transition before inspection.'
}
if ($navigation -match 'ActionLibrary\.openUnitWindow|SelectedUnit\.(clear|select)|ScrollWindow\.showWindow\("unit"\)|SchoolMapModeService\.EndWindowMode') {
    throw 'School actor navigation must not clear school state or use the stateless unit-window protocol.'
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
