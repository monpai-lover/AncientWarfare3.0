$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$rulesPath = Join-Path $repo 'Code\core\schools\HistoricalSchoolEliteEnrollmentRules.cs'
$servicePath = Join-Path $repo 'Code\core\schools\HistoricalSchoolEliteEnrollmentService.cs'
$schedulerPath = Join-Path $repo 'Code\core\schools\HistoricalSchoolScheduler.cs'
$runtimePath = Join-Path $repo 'Code\core\schools\HistoricalSchoolRuntime.cs'
$courtPath = Join-Path $repo 'Code\core\court\CourtService.cs'
$noblePath = Join-Path $repo 'Code\core\lineage\NobleRankService.cs'
$membershipPath = Join-Path $repo 'Code\core\schools\SchoolMembershipService.cs'

if (-not (Test-Path $servicePath)) {
    throw 'Historical school elite enrollment service is missing.'
}

$rules = Get-Content -Raw $rulesPath
$service = Get-Content -Raw $servicePath
$scheduler = Get-Content -Raw $schedulerPath
$runtime = Get-Content -Raw $runtimePath
$court = Get-Content -Raw $courtPath
$noble = Get-Content -Raw $noblePath
$membership = Get-Content -Raw $membershipPath

foreach ($required in @(
    'MaxSuccessfulJoinsPerRealmPerYear = 6;',
    'MaxSuccessfulJoinsPerRealmHardCap = 16;',
    'MaxCandidateAttemptsPerRealmPerYear = 24;',
    'MaxNobleArchiveRowsPerRealmYear = 24;',
    'MaxAcademyResidentsPerYear = 24;',
    'MaxCommonerAdmissionsPerAcademyYear = 2;',
    'return pRemainingCandidates > 0 ? 1 : 0;',
    'return pRemainingRealms > 0 ? 1 : 0;')) {
    if ($rules -notmatch [regex]::Escape($required)) {
        throw "Relaxed school admission rule is missing: $required"
    }
}

foreach ($required in @(
    'HistoricalSchoolEliteEnrollmentRules.FrameAttemptBudget(',
    'RealmPreparationBudget(',
    'MaxSuccessfulJoinsPerRealmPerYear',
    'CourtService.GetActiveOfficers(',
    'FeudatoryService.GetByKingdom(',
    'GetActiveTitleHolderIds(',
    'HeirService.PeekRegisteredHeir(',
    'ResidentTeacherIds(',
    'TeacherIds(',
    'HistoricalSchoolRuntimeIndex.Instance.MemberCount(',
    'HistoricalSchoolLectureRules.BuildPopulationPriorityOrder(',
    'SchoolMembershipService.TryQueueJoin(',
    'SchoolMembershipSource.LaterDiscipleship')) {
    if ($service -notmatch [regex]::Escape($required)) {
        throw "Elite school enrollment is missing bounded behavior: $required"
    }
}

if ($service -match 'foreach\s*\([^\)]*World\.world\.units' -or
    $service -match 'World\.world\.units\s*\)') {
    throw 'Elite school enrollment must not enumerate all world actors.'
}

foreach ($check in @(
    @($scheduler, 'HistoricalSchoolEliteEnrollmentService.ProcessYearFrame('),
    @($runtime, 'HistoricalSchoolEliteEnrollmentService.ClearRuntime();'),
    @($court, 'HistoricalSchoolEliteEnrollmentService.MarkPriority('),
    @($noble, 'HistoricalSchoolEliteEnrollmentService.MarkPriority('),
    @($noble, 'GetActiveTitleHolderIds('),
    @($membership, 'IsJoinPending('))) {
    if ($check[0] -notmatch [regex]::Escape($check[1])) {
        throw "Elite school enrollment integration is missing: $($check[1])"
    }
}

if ($membership -notmatch [regex]::Escape(
        'HistoricalSchoolRuntimeMembershipRules.ShouldIndex(')) {
    throw 'School runtime indexing does not reject missing or dead actors.'
}
if ($membership -notmatch
    'public static int Count\(string pSchoolId\)[\s\S]{0,180}' +
    'HistoricalSchoolRuntimeIndex\.Instance\.MemberCount\(\s*pSchoolId\s*\)') {
    throw 'School overview count does not use the living runtime index.'
}

Write-Host 'Historical school elite enrollment source guard passed.'
