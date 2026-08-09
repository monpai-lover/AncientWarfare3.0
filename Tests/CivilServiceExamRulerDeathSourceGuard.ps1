$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $repo $relativePath
    if (-not [IO.File]::Exists($path)) {
        $failures.Add("missing source file $relativePath")
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

$deathPatch = Read-Source 'Code/patch/AW_ActorDeathPatch.cs'
$service = Read-Source 'Code/core/court/CivilServiceExamService.cs'
$persistence = Read-Source 'Code/core/court/CivilServiceExamPersistence.cs'
$rulerDeathPersistence = Read-Source `
    'Code/core/court/CivilServiceRulerDeathPersistence.cs'

$prefix = Method-Source $deathPatch 'public static void Die_Prefix(' `
    'public static void Die_Postfix(' 'actor death prefix'
foreach ($required in @(
        'if (__instance.isKing() && __instance.kingdom != null)',
        '__state.DyingKingdom = __instance.kingdom;',
        '__state.DyingKingActorId = __instance.data.id;')) {
    Require-Text $prefix $required "captured current king $required"
}
Reject-Text $prefix 'CivilServiceExamService.' `
    'prefix cannot revoke ranking rights before death succeeds'

$postfix = Method-Source $deathPatch '[HarmonyPostfix]' `
    'private static void TryRunDeathStage(' 'actor death postfix'
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
    Require-Text $postfix $required "confirmed successful death $required"
}
foreach ($forbidden in @('CompleteRanking(', 'LoadFinalRankingPage(',
        'LoadCandidatesPage(')) {
    Reject-Text $postfix $forbidden `
        'postfix queues without finalizing or scanning candidates'
}

$finalizer = Method-Source $deathPatch `
    'public static Exception Die_Finalizer(' `
    'private static void EnsureDeathCause(' 'actor death finalizer'
Require-Text $finalizer '__state?.Diagnostic ?? 0L' `
    'finalizer closes the captured diagnostic'
Reject-Text $finalizer 'CivilServiceExamService.' `
    'exception path never revokes ranking rights'

$handler = Method-Source $service `
    'public static void OnCurrentRulerDied(Kingdom pKingdom)' `
    'public static void OnKingdomDestroying(Kingdom pKingdom)' `
    'confirmed ruler-death service handler'
foreach ($required in @(
        'AW3MultiplayerReplicaScope.IsApplying ||',
        'AW3MultiplayerReplicaScope.IsReplicaSession',
        'PlayerRankingByKingdom.TryGetValue',
        'long dueDay = CurrentWorldDay();',
        'DueSessions.Remove(new DueSession(session.NextDueWorldDay,',
        'PendingRulerDeathWrites[session.Id] = pending;',
        'TryEnqueueRulerDeathWrite(pending)')) {
    Require-Text $handler $required "authority-only due-now scheduling $required"
}
foreach ($required in @(
        'WHERE ID=@id AND KINGDOM_ID=@kingdom',
        'MODE=''imperial_exam'' AND STAGE=''ranking''',
        'STATUS=''ranking_pending''',
        'PLAYER_RANKING_PENDING=1',
        'SET PLAYER_RANKING_PENDING=0,',
        'NEXT_DUE_WORLD_DAY=@due,UPDATED_TIME=@time',
        'pCommand.Parameters.AddWithValue("@kingdom", pFacts.KingdomId);',
        'pCommand.Parameters.AddWithValue("@due", pFacts.DueWorldDay);')) {
    Require-Text $rulerDeathPersistence $required `
        "bounded worker CAS $required"
}
Reject-Text $handler 'CivilServiceExamPersistence.' `
    'ruler-death handler never queries SQLite'
Reject-Text $persistence 'TryRevokePlayerRankingForRulerDeath' `
    'legacy synchronous ruler-death transaction is removed'
Reject-Text $rulerDeathPersistence 'CandidateTable' `
    'ruler-death CAS never scans candidates'

$ranking = Method-Source $service `
    'private static void ProcessFinalRanking(' `
    'private static void RecordCommittedQualificationHistory(' `
    'authority ranking completion'
Require-Text $ranking 'CivilServiceExamPersistence.CompleteRanking(DB,' `
    'authority cycle owns completion'
Require-Text $ranking 'pKingdom.king?.data?.id ?? -1L' `
    'completion records the successor ruler'

if ($failures.Count -gt 0) {
    throw "Civil-service ruler-death guard failures:`n - " +
        ($failures -join "`n - ")
}

Write-Output 'Civil-service ruler-death source guard passed.'
