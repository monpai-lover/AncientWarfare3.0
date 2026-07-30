$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$readModel = Join-Path $repo 'Code/core/court/CivilServiceExamReadModel.cs'
$row = Join-Path $repo 'Code/ui/items/CivilServiceExamCandidateRow.cs'
$window = Join-Path $repo 'Code/ui/windows/CivilServiceExamWindow.cs'
$court = Join-Path $repo 'Code/ui/windows/CourtWindow.cs'
$ids = Join-Path $repo 'Code/ui/AW_LineageWindowIds.cs'
$locale = Join-Path $repo 'Locales/aw3_court.csv'
$gitIgnore = Join-Path $repo '.gitignore'

function Read-Required([string]$label, [string]$path) {
    if (-not [IO.File]::Exists($path)) { throw "$label file missing: $path" }
    return [IO.File]::ReadAllText($path)
}

function Require-Text([string]$label, [string]$source, [string]$needle) {
    if (-not $source.Contains($needle)) { throw "$label missing: $needle" }
}

function Reject-Text([string]$label, [string]$source, [string]$needle) {
    if ($source.Contains($needle)) { throw "$label contains forbidden text: $needle" }
}

$readModelSource = Read-Required 'exam read model' $readModel
$rowSource = Read-Required 'exam candidate row' $row
$windowSource = Read-Required 'exam window' $window
$courtSource = Read-Required 'court window' $court
$idSource = Read-Required 'window ids' $ids
$localeSource = Read-Required 'court locale' $locale
$gitIgnoreLines = [IO.File]::ReadAllLines($gitIgnore)
$uiSource = $rowSource + "`n" + $windowSource

foreach ($entry in @(
    '!/Tests/AncientWarfare3.Rules.Tests/',
    '!/Tests/CivilServiceExamPersistenceSourceGuard.ps1',
    '!/Tests/OfficialCirculationAtomicSourceGuard.ps1',
    '!/Tests/CivilServiceCareerGateSourceGuard.ps1',
    '!/Tests/CivilServiceForeignTalentSourceGuard.ps1',
    '!/Tests/CivilServiceExamRuntimeSourceGuard.ps1',
    '!/Tests/CivilServiceExamPolicySourceGuard.ps1',
    '!/Tests/CivilServiceExamHistorySourceGuard.ps1',
    '!/Tests/CivilServiceExamUiSourceGuard.ps1',
    '!/Tests/CivilServiceExamRulerDeathSourceGuard.ps1',
    '!/Tests/CivilServiceCentralAppointmentSourceGuard.ps1'
)) {
    if ($gitIgnoreLines -notcontains $entry) {
        throw "civil-service regression source is not trackable: $entry"
    }
}

Require-Text 'detached session view' $readModelSource 'CivilServiceExamSessionView'
Require-Text 'detached waiting snapshot' $readModelSource `
    'public int WaitingCandidateCount = -1;'
Require-Text 'detached reserve snapshot' $readModelSource `
    'public int ReserveTarget = -1;'
Require-Text 'explicit waiting projection' $readModelSource `
    'WAITING_CANDIDATE_COUNT,RESERVE_TARGET,'
Require-Text 'detached candidate view' $readModelSource 'CivilServiceExamCandidateView'
Require-Text 'bounded session history' $readModelSource 'SessionHistoryLimit = 24'
Require-Text 'bounded candidate reads' $readModelSource 'CandidateLimit = 96'
Require-Text 'tribute sessions use the persisted mode value' $readModelSource `
    'pSession?.Mode == "tributary_exam"'
Require-Text 'explicit candidate projection' $readModelSource 'ACTOR_NAME,HOME_CITY_ID,HOME_CITY_NAME'
foreach ($result in @('LocalResult', 'MetropolitanResult',
        'PalaceResult', 'NationalResult')) {
    Require-Text 'stage-specific candidate result' $readModelSource $result
}
Require-Text 'legacy stage results are repaired in the detached UI read model' `
    $readModelSource 'RepairLegacyStageResult('
Reject-Text 'read model avoids unbounded projections' $readModelSource 'SELECT *'

Require-Text 'candidate row has stable height' $rowSource 'public const float Height = 58f;'
Require-Text 'candidate row supports pooled rebinding' $rowSource 'public void Bind('
Require-Text 'candidate row supports deferred portraits' $rowSource 'public bool TryEnsurePortrait()'
Require-Text 'dead portraits use archive reconstruction' $rowSource 'FamilyTreeNodeView.BuildArchivedPortrait'
Require-Text 'candidate rows use deterministic window geometry' $windowSource `
    'CivilServiceExamRules.CandidateRowWidth('
Reject-Text 'candidate rows cannot depend on stale first-frame rect width' `
    $windowSource '_listViewport.rect.width - 14f'
Require-Text 'candidate content uses fixed top-left anchors' $windowSource `
    '_listContent.anchorMin = _listContent.anchorMax = new Vector2(0f, 1f);'
Require-Text 'candidate content uses top-left pivot' $windowSource `
    '_listContent.pivot = new Vector2(0f, 1f);'
Reject-Text 'candidate content does not double its width through stretch anchors' `
    $windowSource '_listContent.anchorMax = new Vector2(1f, 1f);'

Require-Text 'exam window uses court default width' $windowSource 'DefaultWidth = 560f'
Require-Text 'exam window uses court default height' $windowSource 'DefaultHeight = 360f'
Require-Text 'exam window uses court minimum width' $windowSource 'MinWidth = 420f'
Require-Text 'exam window uses court minimum height' $windowSource 'MinHeight = 280f'
Require-Text 'exam window uses shared chrome' $windowSource 'WideWindowChrome.Attach'
Require-Text 'exam window keeps scrollbar visible' $windowSource 'ScrollRect.ScrollbarVisibility.Permanent'
Require-Text 'exam window batches portraits' $windowSource 'PortraitsPerFrame = 8'
Require-Text 'exam window has stage tabs' $windowSource 'BuildStageTabs'
Require-Text 'exam stage tabs show actual participants' $windowSource `
    'CivilServiceExamRules.IsStageParticipant('
Require-Text 'exam stage tabs show their own result' $windowSource `
    'CivilServiceExamRules.ResolveStageResult('
Require-Text 'exam window has history tab' $windowSource 'HistoryTabId'
Require-Text 'exam window has palace ranking controls' $windowSource 'SubmitPalaceRanking'
Require-Text 'ranking goes through multiplayer command facade' $windowSource `
    'AW3MultiplayerCommandFacade.DispatchFromUi('
Require-Text 'ranking uses the typed civil-service command' $windowSource `
    'AW3CommandRequest.SubmitCivilServiceRanking('
Reject-Text 'exam UI cannot mutate ranking state directly' $windowSource `
    'CivilServiceExamService.TrySubmitPlayerRanking('
Require-Text 'selected session freezes the rendered exam mode' $windowSource `
    'CivilServiceExamReadModel.ResolveMode(_snapshot?.SelectedSession, pKingdom)'
Reject-Text 'exam window does not reclassify a persisted sitting from current title' `
    $windowSource 'CivilServiceExamReadModel.ResolveMode(pKingdom)'
Require-Text 'exam window returns to court' $windowSource 'CourtWindow.Open(_kingdomId);'
Require-Text 'exam window uses frozen vacancy snapshot' $windowSource `
    'session.CentralVacancies + session.CityVacancies'
Require-Text 'exam window uses frozen waiting snapshot' $windowSource `
    'session.WaitingCandidateCount'
Require-Text 'exam window uses frozen reserve target' $windowSource `
    'session.ReserveTarget'
Require-Text 'legacy reserve summary is omitted' $windowSource `
    'CivilServiceExamRules.ShouldShowReserveSummary('

Require-Text 'court owns exam entry' $courtSource 'private Button _civilServiceExamButton;'
Require-Text 'court keeps exam entry visible' $courtSource '_civilServiceExamButton.gameObject.SetActive(true);'
Require-Text 'court checks nine rank prerequisite' $courtSource 'CourtService.HasNineRankSystem(pKingdom)'
Require-Text 'court checks exam prerequisite' $courtSource 'CivilServiceQualificationService.TechnologyId'
Require-Text 'court opens exam window' $courtSource 'CivilServiceExamWindow.Open(_kingdomId);'
Require-Text 'exam window id exists' $idSource 'CIVIL_SERVICE_EXAM = "aw_civil_service_exam"'

foreach ($key in @(
    'aw_civil_service_exam_title,',
    'aw_civil_service_exam_entry,',
    'aw_civil_service_exam_locked_nine_rank,',
    'aw_civil_service_exam_locked_policy,',
    'aw_civil_service_exam_back_to_court,',
    'aw_civil_service_exam_history,'
    'aw_civil_service_vacancies,'
    'aw_civil_service_reserve,'
    'aw_civil_service_admission,'
)) {
    Require-Text 'exam localization' $localeSource $key
}

Reject-Text 'exam UI does not scan all actors' $uiSource 'World.world.units'
Reject-Text 'exam UI does not discover candidates' $uiSource 'CivilServiceExamCandidateQuery'
Reject-Text 'exam UI does not execute SQL writes' $uiSource 'ExecuteNonQuery'
Reject-Text 'exam UI does not write through DB facade' $uiSource 'DB.Insert'
Reject-Text 'exam UI does not advance simulation' $uiSource 'ProcessAuthorityCycle'

Write-Output 'Civil service examination UI source guard passed.'
