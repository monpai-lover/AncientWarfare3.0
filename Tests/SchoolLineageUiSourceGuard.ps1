$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$schoolWindow = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/windows/SchoolWindow.cs')
$navigation = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/SchoolActorNavigation.cs')
$rosterWindow = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/windows/SchoolRosterWindow.cs')
$traitPatch = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/patch/AW_TraitWindowSafetyPatch.cs')
$rosterReadModel = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/core/schools/SchoolRosterReadModelService.cs')
$rosterNode = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/items/SchoolRosterNodeView.cs')
$actorCard = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/items/SchoolActorCardView.cs')
$masterCard = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/items/SchoolMasterCardView.cs')
$composition = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/ui/items/SchoolCompositionElement.cs')
$mapBottomBar = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/core/policy/SchoolMapBottomBarController.cs')

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
if ($navigation -notmatch 'ActionLibrary\.openUnitWindow\(pActor\)') {
    throw 'School actor navigation must use the null-safe native unit-window entry point.'
}
if ($navigation -match 'selectAndInspect\(') {
    throw 'School actor navigation must not dereference a missing previous meta selection.'
}
if ($composition -notmatch '(?s)private static void OpenSchoolWindow\(string pSchoolId\).*SchoolMapBottomBarController\.Hide\(\);\s*SchoolWindow\.OpenSchool\(pSchoolId\)') {
    throw 'School map-mode percentage buttons must close the selected-city tab before opening a school.'
}
if ($composition -notmatch '(?s)private static void OpenSchoolWindow\(long pCityId\).*SchoolMapBottomBarController\.Hide\(\);\s*SchoolWindow\.OpenCity\(pCityId\)') {
    throw 'School map-mode details must close the selected-city tab before opening city school details.'
}
if ($mapBottomBar -notmatch '(?s)public static void ProcessFrame\(\).*ScrollWindow\.getCurrentWindow\(\) != null.*Hide\(\);\s*return;') {
    throw 'The school map selected-city tab must stay hidden while a regular window is open.'
}
if ($actorCard -notmatch 'DisablePortraitInteraction\(_avatar\);') {
    throw 'School detail member portraits must be display-only so they cannot use the prefab unit-window click handler.'
}
if ($actorCard -notmatch 'blocksRaycasts\s*=\s*false') {
    throw 'School detail member portraits must block prefab pointer events at the avatar root.'
}
if ($masterCard -notmatch 'DisablePortraitInteraction\(_avatar\);') {
    throw 'School detail master portraits must be display-only so they cannot use the prefab unit-window click handler.'
}
if ($masterCard -notmatch 'blocksRaycasts\s*=\s*false') {
    throw 'School detail master portraits must block prefab pointer events at the avatar root.'
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
