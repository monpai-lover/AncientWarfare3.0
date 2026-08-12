$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$schoolWindow = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/windows/SchoolWindow.cs')
$navigation = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/SchoolActorNavigation.cs')
$rosterWindow = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/windows/SchoolRosterWindow.cs')
$traitPatch = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/patch/AW_TraitWindowSafetyPatch.cs')
$rosterReadModel = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/core/schools/SchoolRosterReadModelService.cs')
$rosterNode = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/items/SchoolRosterNodeView.cs')

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
if ($navigation -notmatch 'SelectedUnit\.clear\(\);\s*SelectedUnit\.select\(pActor\);') {
    throw 'School actor navigation must bind SelectedUnit before UnitWindow enables.'
}
if ($navigation -notmatch 'unitMeta\.selectAndInspect\(pActor') {
    throw 'School actor navigation must use the native unit MetaType inspection path after binding.'
}
if ($navigation -notmatch 'pClearAction: false') {
    throw 'School actor navigation must retain the native inspection window transition.'
}
if ($rosterWindow -notmatch '(?s)private void Refresh\(\).*CancelPendingRender\(\);\s*HideNodesAndLinks\(\);\s*UpdateSchoolSelector\(\);') {
    throw 'The school roster must clear pending rendering at refresh start like v1.1.2.'
}
if ($rosterWindow -notmatch '(?s)private void Refresh\(\).*SchoolRosterReadModelService\.Build\(\s*_selectedSchool, HorizontalSpacing, VerticalSpacing,\s*ColumnsPerRow\)') {
    throw 'The school roster must synchronously materialize every refresh.'
}
if ($rosterWindow -match 'AWAsyncRuntime\.TrySchedule\(request\)') {
    throw 'The school roster must not leave visible content dependent on the async UI queue.'
}
if ($rosterReadModel -notmatch '(?s)private static Actor FindActor\(long pActorId\).*foreach \(Actor actor in World\.world\.units') {
    throw 'Roster capture must fall back to enumerating actors when the ID dictionary misses.'
}
if ($rosterNode -notmatch '(?s)private static Actor FindActor\(long pActorId\).*foreach \(Actor actor in World\.world\.units') {
    throw 'Roster node actions must use the same actor lookup fallback as capture.'
}
if ($rosterWindow -match '(?s)private void ApplyRosterModel\(SchoolRosterReadModel model\)\s*\{\s*if \(model == null\) return;\s*CancelPendingRender\(\);\s*HideNodesAndLinks\(\);') {
    throw 'The school roster must not clear an accepted model again while applying it.'
}

Write-Output 'School lineage UI source guard passed.'
