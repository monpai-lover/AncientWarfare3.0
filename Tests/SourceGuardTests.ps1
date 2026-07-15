param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    return [System.IO.File]::ReadAllText((Join-Path $root $relativePath))
}

function Require-Absent([string]$name, [string]$relativePath, [string]$needle) {
    $fullPath = Join-Path $root $relativePath
    if (-not [System.IO.File]::Exists($fullPath)) {
        return
    }
    $text = [System.IO.File]::ReadAllText($fullPath)
    if ($text.Contains($needle)) {
        $failures.Add("${name}: found forbidden text '$needle' in $relativePath")
    }
}

function Require-Present([string]$name, [string]$relativePath, [string]$needle) {
    $fullPath = Join-Path $root $relativePath
    if (-not [System.IO.File]::Exists($fullPath)) {
        $failures.Add("${name}: missing source file $relativePath")
        return
    }
    $text = [System.IO.File]::ReadAllText($fullPath)
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
Require-Present 'deferred school maintenance schedule' 'Code/core/schools/HistoricalSchoolActionService.cs' 'ScheduleDeferredActions(pYear)'
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
Require-Present 'pending master requires slot attachment' 'Code/core/schools/HistoricalSchoolDescentService.cs' 'if (!ActiveMasterSlots.TryAttachActor(pMaster.SchoolId, pMaster.Id, actorId))'
Require-Present 'nearby lecture completion effect' 'Code/core/schools/HistoricalSchoolActionService.cs' 'EffectsLibrary.spawnAtTileRandomScale("fx_experience_gain"'

Require-Absent 'school updateAge synchronous runner' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'HistoricalSchoolRuntime.OnWorldYear()'
Require-Absent 'school frame stopwatch allocation' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'Stopwatch.StartNew()'
Require-Absent 'per-frame activity LINQ ordering' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' '.OrderBy('
Require-Absent 'per-frame debate distinct scan' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' '.Distinct()'
Require-Absent 'permanent scholar job restoration' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'RestoreScholarJob('
Require-Absent 'direct scholar travel task replacement' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'pActor.setTask('
Require-Absent 'lecture requires vanilla city equality' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'pActor.city?.data?.id == residence.data.id'
Require-Absent 'inactive school map rebuild' 'Code/core/policy/SchoolMapModeService.cs' 'IsActive() ? 4 : 1'
Require-Absent 'dirty city batch array' 'Code/core/court/CitySchoolSnapshotService.cs' 'Dirty.TakeBatch('
Require-Present 'dirty city dequeue' 'Code/core/court/CitySchoolSnapshotService.cs' 'source.TryDequeue('
Require-Absent 'global school resident rebuild' 'Code/core/court/CitySchoolSnapshotService.cs' 'SchoolMembershipService.ActiveMemberships()'
Require-Absent 'obsolete global resident index cache' 'Code/core/court/CitySchoolResidentIndexRules.cs' 'class CitySchoolResidentIndexCache'
Require-Present 'indexed city residents' 'Code/core/court/CitySchoolSnapshotService.cs' 'HistoricalSchoolRuntimeIndex.Instance.ResidentIds('
Require-Present 'bottom bar visibility gate' 'Code/core/policy/SchoolMapBottomBarController.cs' '_visibleOrPending'
Require-Absent 'global roster membership revision' 'Code/core/schools/SchoolRosterReadModelService.cs' 'SchoolMembershipService.Version'
Require-Absent 'global roster residence revision' 'Code/core/schools/SchoolRosterReadModelService.cs' 'HistoricalAffiliationService.ResidenceRevision'
Require-Absent 'global roster lecture revision' 'Code/core/schools/SchoolRosterReadModelService.cs' 'HistoricalSchoolStore.LectureRevision'
Require-Absent 'obsolete global lecture revision counter' 'Code/core/schools/HistoricalSchoolStore.cs' '_lectureRevision'
Require-Absent 'obsolete global residence revision facade' 'Code/core/schools/HistoricalAffiliationService.cs' 'public static long ResidenceRevision'
Require-Absent 'obsolete global residence revision counter' 'Code/core/schools/HistoricalSchoolRevisionService.cs' '_residenceRevision'
Require-Absent 'global roster disciple count scan' 'Code/core/schools/SchoolRosterReadModelService.cs' 'SchoolLineageService.BuildDirectDiscipleCounts()'
Require-Present 'indexed roster disciple count' 'Code/core/schools/SchoolRosterReadModelService.cs' 'HistoricalSchoolRuntimeIndex.Instance.DirectDiscipleCount('
Require-Present 'narrow roster revision stamp' 'Code/core/schools/SchoolRosterReadModelService.cs' 'HistoricalSchoolRosterRevisionStamp.Capture('
Require-Absent 'school overview global membership revision' 'Code/ui/windows/SchoolWindow.cs' 'SchoolMembershipService.Version'
Require-Absent 'school overview global lecture revision' 'Code/ui/windows/SchoolWindow.cs' 'HistoricalSchoolStore.LectureRevision'
Require-Present 'school overview selected-school revision' 'Code/ui/windows/SchoolWindow.cs' '_displayedSchoolRevisionStamp'
Require-Present 'lecture activity revision projection' 'Code/core/schools/HistoricalSchoolActionService.cs' 'HistoricalSchoolRevisionService.MarkActivity('
Require-Present 'debate activity revision projection' 'Code/core/schools/HistoricalSchoolDebateService.cs' 'HistoricalSchoolRevisionService.MarkActivity('
Require-Present 'bounded school write buffer' 'Code/core/schools/HistoricalSchoolWriteBuffer.cs' 'public const int MaxCapacity = 512'
Require-Present 'single school SQL batch transaction' 'Code/core/schools/HistoricalSchoolWriteBuffer.cs' '_db.BeginTransaction()'
Require-Present 'school SQL batch diagnostics' 'Code/core/schools/HistoricalSchoolWriteBuffer.cs' 'HistoricalSchoolDiagnostics.RecordSqlBatch('
Require-Present 'teaching transaction overload' 'Code/core/schools/HistoricalSchoolTeachingPersistenceDb.cs' 'RecordInTransaction('
Require-Present 'school write frame drain' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolWriteBufferService.ProcessFrame()'
Require-Present 'school write save flush' 'Code/patch/AW_SavePatch.cs' 'HistoricalSchoolWriteBufferService.FlushForSave()'
Require-Absent 'annual full ledger decay stage' 'Code/core/schools/HistoricalSchoolScheduler.cs' 'ApplyLedgerDecay('
Require-Absent 'annual full ledger decay update' 'Code/core/schools/HistoricalSchoolStore.cs' 'public static int ApplyLedgerDecay('
Require-Present 'lazy ledger read decay' 'Code/core/schools/HistoricalSchoolStore.cs' 'HistoricalSchoolLedgerDecayRules.Effective('
Require-Present 'lazy teaching ledger decay' 'Code/core/schools/HistoricalSchoolTeachingPersistenceDb.cs' 'HistoricalSchoolLedgerDecayRules.Effective('
Require-Absent 'direct ready lecture transaction' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'CommitQueuedLecture(pActivity)'
Require-Present 'buffered ready lecture write' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'TryQueueLectureCommit(pActivity)'
Require-Absent 'direct ready debate transaction' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'CommitQueuedDebate(pActivity)'
Require-Present 'buffered ready debate write' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'TryQueueDebateCommit(pActivity)'
Require-Present 'debate transaction overload' 'Code/core/schools/HistoricalSchoolStore.cs' 'RecordDebateAndLedgerInTransaction('
Require-Present 'guest start transaction overload' 'Code/core/schools/GuestOfficePersistenceDb.cs' 'StartInTransaction('
Require-Present 'guest end transaction overload' 'Code/core/schools/GuestOfficeEndPersistence.cs' 'EndInTransaction('
Require-Present 'buffered guest office writes' 'Code/core/schools/SchoolGuestOfficeService.cs' 'HistoricalSchoolWriteBufferService.TryEnqueue('
Require-Present 'membership join transaction overload' 'Code/core/schools/HistoricalSchoolStore.cs' 'InsertMembershipInTransaction('
Require-Present 'membership conversion transaction overload' 'Code/core/schools/HistoricalSchoolStore.cs' 'ConvertMembershipInTransaction('
Require-Present 'membership close transaction overload' 'Code/core/schools/HistoricalSchoolStore.cs' 'CloseMembershipInTransaction('
Require-Present 'buffered membership join' 'Code/core/schools/SchoolMembershipService.cs' 'TryQueueJoin('
Require-Present 'buffered membership conversion' 'Code/core/schools/SchoolMembershipService.cs' 'TryQueueConversion('
Require-Present 'membership event city identity' 'Code/core/schools/SchoolMembershipService.cs' 'public long CityId;'
Require-Present 'membership commit ledger invalidation' 'Code/core/schools/SchoolMembershipService.cs' 'HistoricalSchoolStore.InvalidateTeachingCommit(Event.CityId)'
Require-Present 'committed membership projection retry' 'Code/core/schools/SchoolMembershipService.cs' 'committed school membership adoption failed'
Require-Absent 'synchronous school action join' 'Code/core/schools/HistoricalSchoolActionService.cs' 'SchoolMembershipService.TryJoin('
Require-Absent 'synchronous school action conversion' 'Code/core/schools/HistoricalSchoolActionService.cs' 'SchoolMembershipService.TryConvert('
Require-Present 'year token enqueue' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'HistoricalSchoolRuntime.EnqueueWorldYear()'
Require-Present 'temporary school task scheduling' 'Code/core/schools/HistoricalSchoolTaskLeaseService.cs' 'scheduleTask('
Require-Present 'travel task lease scheduling' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'HistoricalSchoolTaskLeaseService.TrySchedule('
Require-Present 'indexed quarterly travel bucket' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'HistoricalSchoolRuntimeIndex.Instance.TravelEligibleIds(bucket)'
Require-Present 'bounded venue city cache' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'HistoricalSchoolFixedLru<long, CityVenueCacheEntry>'
Require-Present 'bounded recruit city cache' 'Code/core/schools/HistoricalSchoolRecruitCandidateCache.cs' 'HistoricalSchoolFixedLru<long, Entry>'
Require-Present 'canonical master idle roam behaviour' 'Code/ai/behaviours/actor/BehHistoricalSchoolIdleRoam.cs' 'class BehHistoricalSchoolIdleRoam'
Require-Present 'canonical master idle roam task' $contentPath 'IdleRoamTaskId = "aw_historical_school_idle_roam"'
Require-Present 'scoped formal affiliation transfer' 'Code/core/schools/FormalAffiliationTransferScope.cs' 'FormalAffiliationTransferRules.Allows'
Require-Present 'archive WAL pragma' 'Code/core/db/LineageArchivePragmaService.cs' 'PRAGMA journal_mode=WAL'
Require-Present 'archive NORMAL sync pragma' 'Code/core/db/LineageArchivePragmaService.cs' 'PRAGMA synchronous=NORMAL'
Require-Present 'archive save checkpoint' 'Code/patch/AW_SavePatch.cs' 'LineageArchivePragmaService.CheckpointForSave'
Require-Present 'school performance counters' 'Code/core/schools/HistoricalSchoolDiagnostics.cs' 'IdleAllocatedBytes'
Require-Absent 'unconditional school residence revision' 'Code/core/schools/HistoricalAffiliationService.cs' 'AdvanceResidenceRevision()'
Require-Present 'membership runtime index projection' 'Code/core/schools/SchoolMembershipService.cs' 'HistoricalSchoolRuntimeIndex.Instance.Upsert'
Require-Present 'narrow affiliation revisions' 'Code/core/schools/HistoricalAffiliationService.cs' 'HistoricalSchoolRevisionService.ApplyAffiliationChange'
Require-Absent 'reputation twenty-five lecture gate' 'Code/core/schools/HistoricalSchoolLectureRules.cs' 'LaterTeacherMinimumReputation'
Require-Absent 'annual member snapshot runtime' 'Code/core/schools/HistoricalSchoolScheduler.cs' 'HistoricalSchoolAnnualMemberSnapshotBuilder.Build'
Require-Absent 'annual member snapshot action planner' 'Code/core/schools/HistoricalSchoolActionService.cs' 'HistoricalSchoolAnnualMemberSnapshot<Actor>'
Require-Absent 'annual member snapshot debate planner' 'Code/core/schools/HistoricalSchoolDebateService.cs' 'HistoricalSchoolAnnualMemberSnapshot<Actor>'
Require-Absent 'annual active affiliation array' 'Code/core/schools/SchoolGuestOfficeService.cs' 'ActiveSnapshots()'
Require-Absent 'quarter active affiliation array' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'ActiveSnapshots('
Require-Absent 'lecture formal-city equality' 'Code/core/schools/HistoricalSchoolActionService.cs' 'pTeacher.city?.data?.id != residence.data.id'

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
