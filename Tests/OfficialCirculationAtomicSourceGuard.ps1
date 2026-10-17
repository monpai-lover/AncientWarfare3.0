$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $root 'Code/core/court/OfficialCareerStateService.cs'
$patchPath = Join-Path $root 'Code/patch/AW_CityLeaderPatch.cs'
$promotionPath = Join-Path $root 'Code/patch/AW_PromotionPatch.cs'
$runtimeScopePath = Join-Path $root `
    'Code/core/court/GovernorRotationRuntimeScope.cs'
$service = [IO.File]::ReadAllText($servicePath)
$cityPatch = [IO.File]::ReadAllText($patchPath)
$promotionPatch = [IO.File]::ReadAllText($promotionPath)
$runtimeScope = [IO.File]::ReadAllText($runtimeScopePath)
$failures = [System.Collections.Generic.List[string]]::new()

function Require-Text([string]$source, [string]$needle, [string]$label) {
    if (-not $source.Contains($needle)) {
        $failures.Add("${label}: missing '$needle'")
    }
}

function Reject-Text([string]$source, [string]$needle, [string]$label) {
    if ($source.Contains($needle)) {
        $failures.Add("${label}: forbidden '$needle'")
    }
}

Require-Text $service `
    'bool nineRankSystem = CourtService.HasNineRankSystem(pKingdom);' `
    'career evaluation reads the Nine-Rank unlock'
Reject-Text $service `
    'bool circulatingOfficials = CourtService.HasOfficialCourt(pKingdom);' `
    'official-court early rotation gate'
Require-Text $promotionPatch 'CourtVacancyReconciliationService.RegisterCityVacancies(' `
    'city leader vacancy discovery uses the shared coordinator'

Require-Text $service 'OfficialCirculationRules.TryBuildRotationPlan(' `
    'complete pure rotation plan'
Require-Text $runtimeScope 'public static bool IsActive => _depth > 0;' `
    'nested runtime mutation suppression state'
Require-Text $runtimeScope 'public static IDisposable Enter()' `
    'scoped runtime mutation suppression entry'
Require-Text $service 'using (GovernorRotationRuntimeScope.Enter())' `
    'rotation runtime writes execute under projection suppression'
$projectionGateCount = [regex]::Matches($promotionPatch,
    'GovernorRotationRuntimeScope\.IsActive').Count
if ($projectionGateCount -lt 4) {
    $failures.Add(
        'city-leader promotion and career patches must all honor rotation suppression')
}
Require-Text $service 'ValidateGovernorRotationPlan(' `
    'live preflight before mutation'
Require-Text $service 'CommitGovernorRotationPersistence(' `
    'atomic career and court persistence'
Require-Text $service 'RestoreGovernorRotation(' `
    'runtime rollback path'
Require-Text $service 'transaction = DB.BeginTransaction();' `
    'rotation database transaction'
Require-Text $service 'CourtOfficerTableItem.GetTableName()' `
    'court officer city projection update'
Reject-Text $service `
    'var released = new List<(AnnualMutation mutation, City former)>' `
    'release-before-destination implementation'

$rotationStart = $service.IndexOf(
    'private static void ProcessDueGovernorRotations(',
    [System.StringComparison]::Ordinal)
$rotationEnd = $service.IndexOf(
    'private static bool CommitGovernorRotationPersistence(',
    [System.StringComparison]::Ordinal)
if ($rotationStart -lt 0 -or $rotationEnd -le $rotationStart) {
    $failures.Add('governor rotation process segment could not be located')
}
else {
    $rotation = $service.Substring($rotationStart,
        $rotationEnd - $rotationStart)
    $commit = $rotation.IndexOf('CommitGovernorRotationPersistence(',
        [System.StringComparison]::Ordinal)
    $retry = $rotation.IndexOf('ScheduleGovernorRotationRetry(',
        [System.StringComparison]::Ordinal)
    $failureReturn = if ($retry -ge 0) {
        $rotation.IndexOf('return;', $retry, [System.StringComparison]::Ordinal)
    } else { -1 }
    $publish = $rotation.IndexOf('PublishCommittedGovernorRotation(',
        [System.StringComparison]::Ordinal)
    if ($commit -lt 0 -or $retry -le $commit -or
        $failureReturn -le $retry -or $publish -le $failureReturn) {
        $failures.Add('rotation must commit durable state before publishing runtime placement, and return after a rejected transaction')
    }
    $beforeCommit = $rotation.Substring(0, [Math]::Max(0, $commit))
    foreach ($mutation in @('.removeLeader(', '.setLeader(', '.joinCity(')) {
        Reject-Text $beforeCommit $mutation 'rotation cannot mutate runtime placement before persistence'
    }

}

$publishStart = $service.IndexOf(
    'private static void PublishCommittedGovernorRotation(',
    [System.StringComparison]::Ordinal)
$commitPersistenceStart = $service.IndexOf(
    'private static bool CommitGovernorRotationPersistence(',
    [System.StringComparison]::Ordinal)
if ($publishStart -lt 0 -or $commitPersistenceStart -le $publishStart) {
    $failures.Add('committed governor projection segment could not be located')
}
else {
    $publishSegment = $service.Substring($publishStart,
        $commitPersistenceStart - $publishStart)
    Reject-Text $publishSegment `
        'private static void ProjectCommittedGovernorRotation(' `
        'single fallible committed projection sequence'
    foreach ($projection in @(
        'ProjectCommittedGovernorState(item);',
        'ProjectCommittedGovernorCourtCity(item);',
        'ProjectCommittedGovernorPreviousCity(item);',
        'ReconcileCommittedGovernorRuntime(item);')) {
        Require-Text $publishSegment $projection `
            'independent committed governor projection'
    }

    $independentStart = $publishSegment.IndexOf(
        'private static void ProjectCommittedGovernorState(',
        [System.StringComparison]::Ordinal)
    if ($independentStart -lt 0) {
        $failures.Add('independent committed projection helpers are missing')
    }
    else {
        $publishBody = $publishSegment.Substring(0, $independentStart)
        Reject-Text $publishBody 'try' `
            'one projection failure cannot short-circuit later committed facets'
    }
}

foreach ($helper in @(
    'private static void ProjectCommittedGovernorState(',
    'private static void ProjectCommittedGovernorCourtCity(',
    'private static void ProjectCommittedGovernorPreviousCity(')) {
    $helperStart = $service.IndexOf($helper,
        [System.StringComparison]::Ordinal)
    if ($helperStart -lt 0) {
        $failures.Add("committed projection helper missing: $helper")
        continue
    }
    $helperEnd = $service.IndexOf('private static ', $helperStart + $helper.Length,
        [System.StringComparison]::Ordinal)
    if ($helperEnd -le $helperStart) {
        $failures.Add("committed projection helper boundary missing: $helper")
        continue
    }
    $helperBody = $service.Substring($helperStart, $helperEnd - $helperStart)
    Require-Text $helperBody 'catch (Exception e)' `
        "committed projection helper logs and contains failure: $helper"
    Require-Text $helperBody 'ModClass.LogWarning(' `
        "committed projection helper failure log: $helper"
}

Require-Text $service 'private static void ReconcileCommittedGovernorRuntime(' `
    'committed live runtime reconciliation is independent of hot fields'
Require-Text $service 'TryRemoveCommittedFormerLeader(' `
    'committed former leader repair is independent'
Require-Text $service 'TryMoveCommittedGovernor(' `
    'committed actor city repair is independent'
Require-Text $service 'TryAssignCommittedDestinationLeader(' `
    'committed destination leader repair is independent'

Require-Text $service 'private static bool RestoreGovernorRotation(' `
    'rollback returns verified restoration outcome'
Require-Text $service 'ValidateRestoredGovernorRotation(' `
    'rollback verifies every actor and former leader'
Require-Text $service 'TryRemoveTentativeDestinationLeader(' `
    'rollback destination cleanup is per-item independent'
Require-Text $service 'TryRestoreGovernorActorCity(' `
    'rollback actor city repair is per-item independent'
Require-Text $service 'TryRestoreGovernorFormerLeader(' `
    'rollback former leader repair is per-item independent'
Require-Text $service 'MaximumGovernorRollbackRepairAttempts' `
    'rollback deferred repair has a fixed bound'
Require-Text $service 'DeferredRuntimeWorkService.EnqueueCoalesced(' `
    'incomplete rollback is queued without a world scan'

$rotationPersistenceStart = $service.IndexOf(
    'private static bool CommitGovernorRotationPersistence(',
    [System.StringComparison]::Ordinal)
$rotationPersistenceEnd = $service.IndexOf(
    'private static void AddRotationParameters(',
    [System.StringComparison]::Ordinal)
if ($rotationPersistenceStart -lt 0 -or
    $rotationPersistenceEnd -le $rotationPersistenceStart) {
    $failures.Add('governor rotation persistence segment could not be located')
}
else {
    $rotationPersistence = $service.Substring($rotationPersistenceStart,
        $rotationPersistenceEnd - $rotationPersistenceStart)
    Require-Text $rotationPersistence `
        'try { transaction?.Dispose(); } catch { }' `
        'transaction disposal cannot reverse a successful rotation outcome'
}

$retryStart = $service.IndexOf(
    'private static bool ScheduleGovernorRotationRetry(',
    [System.StringComparison]::Ordinal)
$retryEnd = $service.IndexOf(
    'private static int pYearAfter(',
    [System.StringComparison]::Ordinal)
if ($retryStart -lt 0 -or $retryEnd -le $retryStart) {
    $failures.Add('transactional governor retry segment could not be located')
}
else {
    $retry = $service.Substring($retryStart, $retryEnd - $retryStart)
    Require-Text $retry 'SQLiteTransaction transaction = null;' `
        'retry transaction declaration'
    Require-Text $retry 'transaction = DB.BeginTransaction();' `
        'retry transaction begin'
    Require-Text $retry 'new SQLiteCommand(DB) { Transaction = transaction }' `
        'retry command transaction binding'
    Require-Text $retry 'if (command.ExecuteNonQuery() != 1)' `
        'retry validates exactly one career row'
    Require-Text $retry 'transaction.Commit();' `
        'retry transaction commit'
    Require-Text $retry 'transaction?.Rollback();' `
        'retry transaction rollback'

    $retryCommit = $retry.IndexOf('transaction.Commit();',
        [System.StringComparison]::Ordinal)
    $retryHot = $retry.IndexOf(
        'item.Actor.data.set(LineageKeys.OFFICER_TERM_END_YEAR',
        [System.StringComparison]::Ordinal)
    if ($retryCommit -lt 0 -or $retryHot -le $retryCommit) {
        $failures.Add(
            'retry hot term projections must follow the committed all-row transaction')
    }
}

$planIndex = $service.IndexOf('OfficialCirculationRules.TryBuildRotationPlan(')
$removeIndex = if ($planIndex -ge 0) {
    $service.IndexOf('removeLeader()', $planIndex)
} else { -1 }
if ($planIndex -lt 0 -or $removeIndex -le $planIndex) {
    $failures.Add('no leader may be removed before a complete plan exists')
}

$placement = [IO.File]::ReadAllText((Join-Path $root 'Code/core/court/ManualLocalChiefAppointmentService.cs'))
foreach ($required in @('using (GovernorRotationRuntimeScope.Enter())',
    'Actor formerLeader = pCity.leader;', 'City formerCandidateCity = pCandidate.city;',
    'pCity.setLeader(pCandidate, pNew: true);', '!pPersistAppointment()',
    'RestoreRuntimePlacement(', 'pCity.removeLeader();',
    'pCandidate.joinCity(restoreCity);', 'pCity.setLeader(pFormerLeader, pNew: false);',
    'CityGovernorPlacementService.OnCommittedAssignment(')) {
    Require-Text $placement $required 'atomic local chief placement and rollback'
}
$leader = $placement.IndexOf('pCity.setLeader(pCandidate, pNew: true);')
$persist = $placement.IndexOf('!pPersistAppointment()')
$rollback = $placement.IndexOf('RestoreRuntimePlacement(', $persist)
$published = $placement.IndexOf('CityGovernorPlacementService.OnCommittedAssignment(')
if ($leader -lt 0 -or $persist -le $leader -or $rollback -le $persist -or $published -le $rollback) {
    $failures.Add('local chief placement must validate persistence, roll back failures and publish only after success')
}

if ($failures.Count -gt 0) {
    throw "Official circulation atomic guard failures:`n - " +
        ($failures -join "`n - ")
}

Write-Output 'Official circulation atomic source guards passed.'
