param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    return [System.IO.File]::ReadAllText((Join-Path $root $relativePath))
}

function Require-Absent([string]$name, [string]$relativePath, [string]$needle) {
    $text = Read-Source $relativePath
    if ($text.Contains($needle)) {
        $failures.Add("${name}: found forbidden text '$needle' in $relativePath")
    }
}

function Require-Present([string]$name, [string]$relativePath, [string]$needle) {
    $text = Read-Source $relativePath
    if (-not $text.Contains($needle)) {
        $failures.Add("${name}: missing required text '$needle' in $relativePath")
    }
}

Require-Absent 'generic meta-window name patch' 'Code/patch/AW_WorldLogGuardPatch.cs' 'WindowMetaGeneric<War'
Require-Absent 'generic meta-window helper' 'Code/patch/AW_WorldLogGuardPatch.cs' 'MetaWindowSafetyRules'
Require-Absent 'kingdom display-time name repair' 'Code/patch/AW_KingdomWindowPatch.cs' 'nameInput.setText(dataName)'
Require-Absent 'load-time world name scan' 'Code/patch/AW_SavePatch.cs' 'XiaNamingRepair.EnsureWorldNames()'
Require-Absent 'custom tab native sprite overwrite' 'Code/ui/AW_LineageTab.cs' 'ApplyNativeTabSprites'
Require-Absent 'custom tab selected sprite overwrite' 'Code/ui/AW_LineageTab.cs' 'tab_main.image_selected'
Require-Absent 'anonymous Xia clan placeholder' 'Code/content/XiaNaming.cs' '"无名"'

$lineage = Read-Source 'Code/core/lineage/LineageService.cs'
$branchStart = $lineage.IndexOf('public static void OnKingFoundBranch(', [System.StringComparison]::Ordinal)
$newClan = $lineage.IndexOf('newClan(pKing', $branchStart, [System.StringComparison]::Ordinal)
$freezeShi = $lineage.IndexOf('GenerateShiName(pKing)', $branchStart, [System.StringComparison]::Ordinal)
if ($branchStart -lt 0 -or $newClan -lt 0 -or $freezeShi -lt 0 -or $freezeShi -gt $newClan) {
    $failures.Add('king-founded branch must resolve its shi before newClan(pKing)')
}

$contentPath = 'Code/content/schools/HistoricalSchoolContent.cs'
Require-Present 'lecture task id' $contentPath 'LectureTaskId = "aw_historical_school_lecture"'
Require-Present 'debate travel task id' $contentPath 'DebateTravelTaskId = "aw_historical_school_debate_travel"'
Require-Present 'debate task id' $contentPath 'DebateTaskId = "aw_historical_school_debate"'
Require-Present 'debate receiver task id' $contentPath 'DebateReceivingTaskId ='
Require-Present 'debate receiver task value' $contentPath '"aw_historical_school_debate_receiving"'

$localePath = 'Locales/others.csv'
Require-Present 'lecture task locale' $localePath 'task_unit_aw_historical_school_lecture,'
Require-Present 'debate travel task locale' $localePath 'task_unit_aw_historical_school_debate_travel,'
Require-Present 'debate task locale' $localePath 'task_unit_aw_historical_school_debate,'
Require-Present 'debate receiver task locale' $localePath 'task_unit_aw_historical_school_debate_receiving,'

Require-Present 'frame activity queue' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolActivityQueue.ProcessFrame()'
Require-Present 'school-level canonical master slots' 'Code/core/schools/HistoricalSchoolDescentService.cs' 'HistoricalSchoolActiveMasterSlots'
Require-Present 'deferred school maintenance schedule' 'Code/core/schools/HistoricalSchoolActionService.cs' 'ScheduleDeferredActions(pYear, pMembers)'
Require-Present 'deferred school maintenance frame step' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolActionService.ProcessDeferredFrame()'
Require-Absent 'per-teacher city resident scan' 'Code/core/schools/HistoricalSchoolActionService.cs' 'foreach (Actor actor in pCity.units)'
Require-Present 'school activity save flush' 'Code/patch/AW_SavePatch.cs' 'HistoricalSchoolActivityQueue.FlushPendingPersistenceForSave()'
Require-Present 'lecture ready save flush' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolDebateActivityService.FlushPendingPersistenceForSave()'
Require-Present 'debate ready save flush' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'FlushPendingPersistenceForSave()'
Require-Absent 'debate unknown persistence discard' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' '++ready.Attempts >= 3'
Require-Absent 'school activity city-center fallback' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'result.Add(center)'
Require-Present 'city-change activity task restoration' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'CancelActor(__instance, pRestoreActor: true)'
Require-Present 'death activity cleanup without restoration' 'Code/core/schools/SchoolMembershipService.cs' 'CancelActor(pActor, pRestoreActor: false)'
Require-Present 'lecture excludes active debate actors' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolDebateActivityService.IsActorBusy(actor.data.id)'
Require-Present 'debate excludes active lecture actors' 'Code/core/schools/HistoricalSchoolDebateService.cs' 'HistoricalSchoolActivityQueue.IsLectureActorBusy(pActor.data.id)'
Require-Present 'lecture planning excludes serving guests' 'Code/core/schools/HistoricalSchoolActionService.cs' 'HistoricalAffiliationService.IsAvailableForOffice(pTeacher)'
Require-Present 'lecture runtime excludes serving guests' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalAffiliationService.IsAvailableForOffice(pActor)'
Require-Present 'pending master requires slot attachment' 'Code/core/schools/HistoricalSchoolDescentService.cs' 'if (!ActiveMasterSlots.TryAttachActor(pMaster.SchoolId, pMaster.Id, actorId))'
Require-Present 'nearby lecture completion effect' 'Code/core/schools/HistoricalSchoolActionService.cs' 'EffectsLibrary.spawnAtTileRandomScale("fx_experience_gain"'

$activityQueue = Read-Source 'Code/core/schools/HistoricalSchoolActivityQueue.cs'
$debateFrame = $activityQueue.IndexOf('if (HistoricalSchoolDebateActivityService.ProcessFrame()) return;', [System.StringComparison]::Ordinal)
$deferredFrame = $activityQueue.IndexOf('HistoricalSchoolActionService.ProcessDeferredFrame()', [System.StringComparison]::Ordinal)
if ($debateFrame -lt 0 -or $deferredFrame -lt 0 -or $debateFrame -gt $deferredFrame) {
    $failures.Add('visible debate transitions must be scheduled before deferred school maintenance')
}

if ($failures.Count -gt 0) {
    Write-Host "Source guard failures: $($failures.Count)"
    foreach ($failure in $failures) {
        Write-Host " - $failure"
    }
    exit 1
}

Write-Host 'Source guards passed.'
