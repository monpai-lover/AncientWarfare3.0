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
    'bool circulatingOfficials = CourtService.HasNineRankSystem(pKingdom);' `
    'rotation unlock follows Nine-Rank'
Require-Text $cityPatch `
    'bool hasNineRankSystem = CourtService.HasNineRankSystem(kingdom);' `
    'vacancy search reads the Nine-Rank unlock'
Require-Text $cityPatch `
    'ShouldUseCivilServiceGovernorPipeline(hasNineRankSystem);' `
    'single-city vacancies keep the civil-service appointment gate'
Require-Text $cityPatch `
    'ShouldUseIntercityGovernorCirculation(hasNineRankSystem,' `
    'intercity vacancy search follows the Nine-Rank circulation rule'
Reject-Text $service `
    'bool circulatingOfficials = CourtService.HasOfficialCourt(pKingdom);' `
    'official-court early rotation gate'
Reject-Text $cityPatch `
    'bool circulating = CourtService.HasOfficialCourt(kingdom) &&' `
    'official-court early vacancy gate'

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
    $catch = $rotation.IndexOf('catch (Exception',
        [System.StringComparison]::Ordinal)
    $restore = $rotation.IndexOf('RestoreGovernorRotation(',
        [System.StringComparison]::Ordinal)
    $retry = $rotation.IndexOf('ScheduleGovernorRotationRetry(',
        [System.StringComparison]::Ordinal)
    $failureReturn = if ($retry -ge 0) {
        $rotation.IndexOf('return;', $retry,
            [System.StringComparison]::Ordinal)
    }
    else { -1 }
    $publish = $rotation.IndexOf('PublishCommittedGovernorRotation(',
        [System.StringComparison]::Ordinal)
    if ($commit -lt 0 -or $catch -le $commit -or
        $restore -le $catch -or $retry -le $restore -or
        $failureReturn -le $retry -or $publish -le $failureReturn) {
        $failures.Add(
            'rotation must return after pre-commit rollback and publish only beyond the commit catch boundary')
    }
    if ($rotation.Contains('item.Actor.data.set(LineageKeys.COURT_CITY_ID')) {
        $failures.Add(
            'committed governor projections must be isolated from the pre-commit transaction block')
    }
    if (-not $rotation.Contains(
            'bool restored = RestoreGovernorRotation(pPlan);')) {
        $failures.Add(
            'pre-commit rollback must verify that every former governor projection was restored')
    }
    Require-Text $rotation `
        'if (!restored) ScheduleGovernorRotationRuntimeRepair(' `
        'incomplete runtime rollback enters bounded deferred repair'
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

Require-Text $cityPatch 'TryGetActingLocalLeader(' `
    'educated local acting fallback'
Require-Text $cityPatch 'HistoricalSchoolEducationService.CanAppoint(' `
    'acting fallback education gate'
Require-Text $cityPatch 'City previousCity = actor.city;' `
    'vacancy appointment remembers the original city'
Require-Text $cityPatch 'bool appointed = acting' `
    'vacancy appointment observes the persistence result'
Require-Text $cityPatch 'if (!appointed)' `
    'failed vacancy appointment enters rollback'
Require-Text $cityPatch 'if (pCity.leader == actor)' `
    'failed vacancy appointment only removes its tentative leader'
Require-Text $cityPatch 'pCity.removeLeader();' `
    'failed vacancy appointment clears the tentative leader'
Require-Text $cityPatch 'actor.joinCity(previousCity);' `
    'failed vacancy appointment restores the original city'

$selectionStart = $cityPatch.IndexOf(
    'if (actor != null)', [System.StringComparison]::Ordinal)
$selectionEnd = $cityPatch.IndexOf(
    'if (civilServiceCareer) return false;', $selectionStart,
    [System.StringComparison]::Ordinal)
if ($selectionStart -lt 0 -or $selectionEnd -le $selectionStart) {
    $failures.Add('vacancy appointment segment could not be located')
}
else {
    $selection = $cityPatch.Substring($selectionStart,
        $selectionEnd - $selectionStart)
    $setLeader = $selection.IndexOf('pCity.setLeader(actor, pNew: true);',
        [System.StringComparison]::Ordinal)
    $appointment = $selection.IndexOf('bool appointed = acting',
        [System.StringComparison]::Ordinal)
    $rollback = $selection.IndexOf('if (!appointed)',
        [System.StringComparison]::Ordinal)
    if ($setLeader -lt 0 -or $appointment -le $setLeader -or
        $rollback -le $appointment) {
        $failures.Add(
            'vacancy leader projection must be followed by checked appointment and rollback')
    }
}

if ($failures.Count -gt 0) {
    throw "Official circulation atomic guard failures:`n - " +
        ($failures -join "`n - ")
}

Write-Output 'Official circulation atomic source guards passed.'
