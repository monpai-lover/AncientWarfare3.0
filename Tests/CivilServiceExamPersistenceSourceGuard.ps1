$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        $failures.Add("missing source file: $relativePath")
        return ''
    }
    return [IO.File]::ReadAllText($path)
}

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

function Method-Source([string]$source, [string]$startNeedle,
        [string]$endNeedle, [string]$label) {
    $start = $source.IndexOf($startNeedle,
        [System.StringComparison]::Ordinal)
    $end = if ($start -ge 0) {
        $source.IndexOf($endNeedle, $start + $startNeedle.Length,
            [System.StringComparison]::Ordinal)
    } else { -1 }
    if ($start -lt 0 -or $end -le $start) {
        $failures.Add("${label}: method boundary not found")
        return ''
    }
    return $source.Substring($start, $end - $start)
}

$session = Read-Source 'Code/core/db/CivilServiceExamSessionTableItem.cs'
$candidate = Read-Source 'Code/core/db/CivilServiceExamCandidateTableItem.cs'
$indexes = Read-Source 'Code/core/db/LineageArchiveIndexRules.cs'
$persistence = Read-Source 'Code/core/court/CivilServiceExamPersistence.cs'
$service = Read-Source 'Code/core/court/CivilServiceExamService.cs'
$deathPatch = Read-Source 'Code/patch/AW_ActorDeathPatch.cs'

Require-Text $session '[TableDef("CivilServiceExamSession")]' 'session table'
foreach ($field in @('kingdom_id', 'kingdom_name', 'mode', 'cycle_year',
        'stage', 'status', 'open_world_day', 'next_due_world_day',
        'host_ruler_id', 'final_ruler_id', 'player_ranking_pending',
        'candidate_cursor', 'central_vacancies', 'city_vacancies',
        'waiting_candidate_count', 'reserve_target', 'admission_quota',
        'updated_time')) {
    Require-Text $session $field "session field $field"
}
foreach ($required in @(
        'CENTRAL_VACANCIES', 'CITY_VACANCIES', 'WAITING_CANDIDATE_COUNT',
        'RESERVE_TARGET', 'ADMISSION_QUOTA', '@central_vacancies',
        '@city_vacancies', '@waiting_candidate_count', '@reserve_target',
        '@admission_quota')) {
    Require-Text $persistence $required "frozen examination demand $required"
}

Require-Text $candidate '[TableDef("CivilServiceExamCandidate")]' 'candidate table'
foreach ($field in @('session_id', 'kingdom_id', 'actor_id', 'actor_name',
        'home_city_id', 'home_city_name', 'social_origin', 'school_id',
        'local_grade', 'local_score', 'metropolitan_score', 'palace_score',
        'national_score', 'local_result', 'metropolitan_result',
        'palace_result', 'national_result', 'current_stage_result', 'qualification',
        'final_rank', 'final_title', 'entry_bonus', 'updated_time')) {
    Require-Text $candidate $field "candidate field $field"
}

Require-Text $indexes 'uq_CivilServiceExamSession_kingdom_cycle' `
    'unique kingdom cycle index'
Require-Text $indexes 'idx_CivilServiceExamSession_status_due' `
    'due session index'
Require-Text $indexes 'uq_CivilServiceExamCandidate_session_actor' `
    'unique session actor index'
Require-Text $indexes 'idx_CivilServiceExamCandidate_actor_kingdom' `
    'qualification lookup index'

foreach ($method in @('TryCreateSession(', 'InsertCandidates(',
        'LoadDueSession(', 'LoadCandidatesPage(', 'CommitCandidateBatch(',
        'CompleteStage(', 'FinalizeRanking(', 'CommitFinalRankingBatch(',
        'CompleteRanking(', 'CancelActiveSession(',
        'CancelActiveSessionForKingdom(',
        'LoadLatestQualification(', 'LoadActiveSessions(')) {
    Require-Text $persistence $method "persistence method $method"
}
Require-Text $persistence 'transaction = pDb.BeginTransaction();' `
    'atomic persistence transaction'
Require-Text $persistence 'WHERE ID=@id AND CANDIDATE_CURSOR=@expected_cursor' `
    'session cursor compare-and-set'
Require-Text $persistence `
    "WHERE ID=@session AND STAGE='ranking' AND STATUS='ranking_pending' AND " `
    'player ranking compares the persisted stage and status'
Require-Text $persistence 'PLAYER_RANKING_PENDING=1' `
    'player ranking compares the pending ownership flag'
Require-Text $persistence 'LIMIT @limit OFFSET @offset' `
    'bounded candidate page'
Require-Text $persistence 'INSERT OR IGNORE INTO' `
    'idempotent inserts'
foreach ($column in @('LOCAL_RESULT', 'METROPOLITAN_RESULT',
        'PALACE_RESULT', 'NATIONAL_RESULT')) {
    Require-Text $persistence $column "stage-specific result $column"
}
Reject-Text $persistence 'SELECT *' 'explicit database projection'

$deathPrefix = Method-Source $deathPatch `
    'public static void Die_Prefix(' `
    'public static void Die_Postfix(' `
    'actor death prefix'
foreach ($required in @(
        'if (__instance.isKing() && __instance.kingdom != null)',
        '__state.DyingKingdom = __instance.kingdom;',
        '__state.DyingKingActorId = __instance.data.id;')) {
    Require-Text $deathPrefix $required "current-ruler capture $required"
}
Reject-Text $deathPrefix 'CivilServiceExamService.' `
    'actor death prefix captures identity without revoking ranking rights'

$deathPostfix = Method-Source $deathPatch `
    '[HarmonyPostfix]' `
    'private static void TryRunDeathStage(' `
    'successful actor death postfix'
foreach ($required in @(
        '[HarmonyPostfix]',
        'bool __runOriginal',
        'if (!__runOriginal) return;',
        'AW3MultiplayerReplicaScope.IsApplying ||',
        'AW3MultiplayerReplicaScope.IsReplicaSession',
        '__state?.DyingKingdom == null',
        '__state.DyingKingActorId < 0L',
        '__instance.isAlive()',
        'CivilServiceExamService.OnCurrentRulerDied(',
        '__state.DyingKingdom')) {
    Require-Text $deathPostfix $required "confirmed-death callback $required"
}
Reject-Text $deathPostfix 'CompleteRanking(' `
    'successful death postfix only queues ranking work'
Reject-Text $deathPatch 'CivilServiceExamPersistence.' `
    'actor death hook does not write examination persistence directly'
Reject-Text $deathPatch 'CompleteRanking(' `
    'actor death hook does not complete an examination'

$handler = Method-Source $service `
    'public static void OnCurrentRulerDied(Kingdom pKingdom)' `
    'public static bool TrySubmitPlayerRanking(' `
    'current-ruler death handler'
foreach ($required in @(
        'AW3MultiplayerReplicaScope.IsApplying ||',
        'AW3MultiplayerReplicaScope.IsReplicaSession',
        'long dueDay = CurrentWorldDay();',
        'TryRevokePlayerRankingForRulerDeath(DB, pKingdom.id,',
        'out long sessionId, out long previousDueDay',
        'DueSessions.Remove(new DueSession(previousDueDay, sessionId));',
        'DueSessions.Add(new DueSession(dueDay, sessionId));')) {
    Require-Text $handler $required "due-now ranking queue $required"
}
foreach ($forbidden in @('CompleteRanking(', 'ProcessFinalRanking(',
        'LoadFinalRankingPage(', 'LoadCandidatesPage(')) {
    Reject-Text $handler $forbidden `
        'ruler-death handler does not finalize or scan candidates'
}

$finalizer = Method-Source $deathPatch `
    'public static Exception Die_Finalizer(' `
    'private static void EnsureDeathCause(' `
    'actor death finalizer'
Require-Text $finalizer '__state?.Diagnostic ?? 0L' `
    'finalizer closes the captured death diagnostic'
Reject-Text $finalizer 'CivilServiceExamService.' `
    'exception finalizer never revokes ranking rights'

$deathCas = Method-Source $persistence `
    'public static bool TryRevokePlayerRankingForRulerDeath(' `
    'public static bool FinalizeRanking(' `
    'ruler-death ranking compare-and-set'
foreach ($required in @(
        'transaction = pDb.BeginTransaction();',
        'SELECT ID,NEXT_DUE_WORLD_DAY FROM ',
        'WHERE KINGDOM_ID=@kingdom AND MODE=''imperial_exam'' AND ',
        'STAGE=''ranking'' AND STATUS=''ranking_pending'' AND ',
        'PLAYER_RANKING_PENDING=1 ORDER BY ID LIMIT 1',
        'SET PLAYER_RANKING_PENDING=0,NEXT_DUE_WORLD_DAY=@due,',
        'WHERE ID=@id AND KINGDOM_ID=@kingdom AND ',
        'command.Parameters.AddWithValue("@kingdom", pKingdomId);',
        'command.Parameters.AddWithValue("@due", pDueWorldDay);',
        'command.ExecuteNonQuery() != 1')) {
    Require-Text $deathCas $required "bounded ranking CAS $required"
}
Reject-Text $deathCas 'CandidateTable' `
    'ruler-death ranking CAS never scans candidates'

$finalRanking = Method-Source $service `
    'private static void ProcessFinalRanking(' `
    'private static void RecordCommittedQualificationHistory(' `
    'authority final-ranking handler'
Require-Text $finalRanking 'CivilServiceExamPersistence.CompleteRanking(DB,' `
    'authority cycle owns examination completion'
Require-Text $finalRanking 'pKingdom.king?.data?.id ?? -1L' `
    'completion records the successor ruler'

if ($failures.Count -gt 0) {
    throw "Civil-service exam persistence guard failures:`n - " +
        ($failures -join "`n - ")
}

Write-Output 'Civil-service exam persistence source guards passed.'
