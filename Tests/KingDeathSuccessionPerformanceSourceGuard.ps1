$ErrorActionPreference = 'Stop'

$death = Get-Content -Raw 'Code/patch/AW_ActorDeathPatch.cs'
$mandate = Get-Content -Raw 'Code/patch/AW_MandateSuccessionPatch.cs'
$heir = Get-Content -Raw 'Code/patch/AW_HeirPatch.cs'
$heirService = Get-Content -Raw 'Code/core/lineage/HeirService.cs'
$dispute = Get-Content -Raw 'Code/core/lineage/SuccessionDisputeService.cs'
$preparation = Get-Content -Raw `
    'Code/core/lineage/SuccessionPreparationService.cs'
$civil = Get-Content -Raw 'Code/core/court/CivilServiceExamService.cs'
$save = Get-Content -Raw 'Code/patch/AW_SavePatch.cs'
$authority = Get-Content -Raw `
    'Code/core/performance/AWAuthorityCycleService.cs'
$accession = Get-Content -Raw `
    'Code/core/lineage/AccessionIdentityService.cs'
$guestOffice = Get-Content -Raw `
    'Code/core/schools/SchoolGuestOfficeService.cs'
$court = Get-Content -Raw 'Code/core/court/CourtService.cs'
$careerState = Get-Content -Raw `
    'Code/core/court/OfficialCareerStateService.cs'
$schoolWriteBuffer = Get-Content -Raw `
    'Code/core/schools/HistoricalSchoolWriteBufferService.cs'
$schoolAsync = Get-Content -Raw `
    'Code/core/schools/HistoricalSchoolAsyncWrite.cs'
$historicalWrite = Get-Content -Raw `
    'Code/core/db/HistoricalWriteService.cs'

if ($death.Contains('PrepareSuccessionBeforeKingDeath')) {
    throw 'Actor.die must not synchronously prepare succession'
}
if (-not $death.Contains('SuccessionPreparationService.CaptureDeath')) {
    throw 'Actor.die must capture the scalar succession context'
}
foreach ($forbidden in @('SuccessionDisputeService.Prepare',
        'SQLiteCommand', 'BeginTransaction', 'LineageQuery',
        'World.world.units')) {
    if ($death.Contains($forbidden)) {
        throw "Actor.die contains forbidden king-death work: $forbidden"
    }
}
if ($mandate.Contains('PrepareSuccessionBeforeKingDeath')) {
    throw 'KingdomBehCheckKing must not repeat succession preparation'
}
if (-not $mandate.Contains(
        'SuccessionPreparationService.TryPublishForNativeSuccession')) {
    throw 'KingdomBehCheckKing must consume a revision-valid snapshot'
}
if ($heir.Contains('? HeirService.GetHeir(pKingdom)')) {
    throw 'SuccessionTool must not recompute an heir during installation'
}
if (-not $heir.Contains(
        'SuccessionPreparationService.TryGetPublishedCandidate')) {
    throw 'SuccessionTool must read only the published candidate'
}
if (-not $heir.Contains(
        'SuccessionPreparationService.OnSuccessorInstalled')) {
    throw 'successful native installation must consume the death context'
}
if ($dispute.Contains('public static void Prepare(')) {
    throw 'legacy synchronous succession dispute preparation remains callable'
}
foreach ($required in @('SuccessionRelationshipIndex.Reset();',
        'SuccessionPreparationService.Reset();',
        'CivilServiceExamService.ClearRuntime();')) {
    if (-not $authority.Contains($required)) {
        throw "world reset does not clear succession runtime: $required"
    }
}
foreach ($required in @('PreparePendingPersistenceForSave()',
        'HasPendingPersistence')) {
    if (-not $preparation.Contains($required)) {
        throw "succession persistence lacks save handling: $required"
    }
}
$commitStart = $preparation.IndexOf('private static void AcceptDisputeCommit(')
$commitEnd = $preparation.IndexOf(
    'private static void MarkDisputePersistencePending(', $commitStart)
$commit = $preparation.Substring($commitStart, $commitEnd - $commitStart)
$queuePublication = $commit.IndexOf(
    'CommittedDisputePublications[pFacts.KingdomId]')
$removePending = $commit.IndexOf('PendingDisputes.Remove(pFacts.KingdomId);')
if ($queuePublication -lt 0 -or $removePending -le $queuePublication -or
    -not $preparation.Contains('RetryCommittedDisputePublication()') -or
    -not $preparation.Contains('CommittedDisputePublicationQueue')) {
    throw 'a committed dispute is neither transferred to non-blocking publication nor removed from the save barrier'
}
$designationStart = $heirService.IndexOf('private static bool PrepareForDesignation(')
$designationEnd = $heirService.IndexOf('private static City ResolveDesignationCity(',
    $designationStart)
$designation = $heirService.Substring($designationStart,
    $designationEnd - $designationStart)
foreach ($forbidden in @('GuestOfficeEndPersistence.End(',
        'HistoricalAffiliationService.EndService(')) {
    if ($accession.Contains($forbidden)) {
        throw "succession identity preparation still performs synchronous guest-office SQL: $forbidden"
    }
}
if (-not $accession.Contains(
        'SchoolGuestOfficeService.QueueGuestOfficerEnd(')) {
    throw 'succession guest-office persistence is not queued asynchronously'
}
if ($accession -notmatch
    'CourtService\.ClearOfficeForReignTransition\(pActor,\s*"became_king",\s*pPersistCareer:\s*false\)') {
    throw 'accession still synchronously persists official-career termination'
}
$clearOfficerStart = $court.IndexOf('private static void ClearOfficer(')
$clearOfficerEnd = $court.IndexOf('private static void CloseStaleOfficerRows(',
    $clearOfficerStart)
$clearOfficer = $court.Substring($clearOfficerStart,
    $clearOfficerEnd - $clearOfficerStart)
if ($clearOfficer -notmatch
    'if\s*\(pPersistCareer\)[\s\S]*OfficialCareerService\.End[\s\S]*OfficialCareerStateService\.ClearCurrentOffice') {
    throw 'non-persistent succession role cleanup still reaches official-career SQL'
}
if ($guestOffice -notmatch
    'OfficialCareerStateService\.\s*StageClearCurrentOffice\(') {
    throw 'queued guest-office ending does not close career state in its worker transaction'
}
if ($guestOffice -notmatch
    'GuestEndWriteOperation\s*:\s*IHistoricalSchoolWriteOperation,\s*IHistoricalSchoolAsyncWriteOperation') {
    throw 'succession guest-office SQL still falls back to the main-thread school buffer'
}
if ($guestOffice -notmatch 'GuestEndWriteOperation[\s\S]*IHistoricalSchoolBackgroundOnlyWriteOperation' -or
    -not $schoolAsync.Contains('IHistoricalSchoolBackgroundOnlyWriteOperation') -or
    -not $schoolWriteBuffer.Contains('HistoricalWriteService.EnsureRequiredWorker')) {
    throw 'succession guest-office end is not guaranteed to stay off the main-thread SQL buffer'
}
if (-not $guestOffice.Contains(
        'private sealed class GuestEndBackgroundWrite')) {
    throw 'guest-office end has no detached immutable background operation'
}
if (-not $careerState.Contains(
        'internal static OfficialCareerStateView StageClearCurrentOffice(')) {
    throw 'official career state has no transaction-bound worker clear operation'
}
if (-not $guestOffice.Contains('internal static bool QueueGuestOfficerEnd(')) {
    throw 'guest-office service has no non-blocking succession end boundary'
}
if (-not $court.Contains('runtimeAlreadyCleared')) {
    throw 'async guest-office completion cannot accept an already-cleared succession projection'
}
if (-not $guestOffice.Contains('internal static bool FlushPendingForSave()')) {
    throw 'queued succession guest-office endings have no save barrier'
}
if (-not $save.Contains('SchoolGuestOfficeService.FlushPendingForSave')) {
    throw 'save preparation does not drain queued succession guest-office endings'
}
if (-not $guestOffice.Contains('SuccessionIdentityEnd')) {
    throw 'succession guest-office endings cannot distinguish retryable identity writes'
}
if ($guestOffice -notmatch
    'ShouldRetrySuccessionEndCleanFailure[\s\S]*ScheduleSuccessionEndRetry' -or
    -not $guestOffice.Contains('_flushingForSave')) {
    throw 'succession guest-office clean failures are not bounded outside save or terminal during save'
}
if (-not $historicalWrite.Contains('public static bool EnsureRequiredWorker(')) {
    throw 'required background persistence cannot start the historical worker on demand'
}
if ($guestOffice -notmatch
    'IsSuccessionProtectedActor\(actor\)[\s\S]*PendingGuestOffice\.ForEnd') {
    throw 'load recovery can still pull an installed king or heir back into an old host realm'
}
foreach ($required in @('PreparePendingRulerDeathPersistenceForSave()',
        'HasPendingRulerDeathPersistence')) {
    if (-not $civil.Contains($required)) {
        throw "civil-service ruler-death persistence lacks save handling: $required"
    }
}
foreach ($required in @(
        'SuccessionPreparationService.PreparePendingPersistenceForSave()',
        'CivilServiceExamService.PreparePendingRulerDeathPersistenceForSave()',
        '!SuccessionPreparationService.HasPendingPersistence',
        '!CivilServiceExamService.HasPendingRulerDeathPersistence')) {
    if (-not $save.Contains($required)) {
        throw "save barrier does not resolve succession writes: $required"
    }
}

Write-Host 'King death succession performance source guard passed.'
