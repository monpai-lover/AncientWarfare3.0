$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $repoRoot `
    'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'
$runnerPath = Join-Path $repoRoot `
    'Code/core/performance/AWCooperativeActorPostRunner.cs'
$service = Get-Content -Raw -Encoding UTF8 $servicePath
$runner = Get-Content -Raw -Encoding UTF8 $runnerPath

function Get-MethodBody([string] $source, [string] $startToken,
    [string] $nextToken) {
    $start = $source.IndexOf($startToken)
    if ($start -lt 0) { throw "Missing method token: $startToken" }
    $end = $source.IndexOf($nextToken, $start + $startToken.Length)
    if ($end -lt 0) { throw "Missing method boundary: $nextToken" }
    return $source.Substring($start, $end - $start)
}

$capture = Get-MethodBody $service 'internal static bool TryHandleCapture(' `
    'internal static bool IsHostileKingdom('
$death = Get-MethodBody $service 'internal static void OnBanditResidentDied(' `
    'internal static void RestoreRuntime('
$restore = Get-MethodBody $service 'internal static void RestoreRuntime(' `
    'private static void QueueFall('

if ($capture -notmatch 'QueueFall\(' -or $capture -match 'CompleteFall\(') {
    throw 'Completed bandit capture must queue fall instead of mutating inline'
}
if ($death -match 'CompleteFall\(') {
    throw 'Actor-death callback still completes bandit fall inline'
}
foreach ($token in @('ResolveFallAction(', 'QueueFall(')) {
    if ($death -notmatch [regex]::Escape($token)) {
        throw "Actor-death callback is missing $token"
    }
}
if ($restore -match 'CompleteFall\(' -or $restore -notmatch 'QueueFall\(') {
    throw 'Runtime restore must defer stronghold fall'
}

$queueStart = $service.IndexOf('private static void QueueFall(')
if ($queueStart -lt 0) { throw 'Stronghold service has no QueueFall method' }
$queueBody = $service.Substring($queueStart,
    [Math]::Min(2600, $service.Length - $queueStart))
foreach ($token in @('DeferredRuntimeWorkService.EnqueueCoalesced(',
        'DeferredWorkClass.CriticalRuntime', 'bandit_stronghold_fall:')) {
    if ($queueBody -notmatch [regex]::Escape($token)) {
        throw "Deferred stronghold fall is missing $token"
    }
}

$enemyStart = $runner.IndexOf('private sealed class EnemyPrepareBatchWork')
$enemyEnd = $runner.IndexOf('private sealed class ActorGateBatchWork',
    $enemyStart)
if ($enemyStart -lt 0 -or $enemyEnd -lt 0) {
    throw 'Cannot isolate EnemyPrepareBatchWork'
}
$enemy = $runner.Substring($enemyStart, $enemyEnd - $enemyStart)
foreach ($token in @('actor?.data != null', '!actor.isRekt()',
        'actor.isAlive()', 'actor.asset != null',
        'actor.kingdom?.data != null')) {
    if ($enemy -notmatch [regex]::Escape($token)) {
        throw "Enemy preparation snapshot filter is missing $token"
    }
}
$validIndex = $enemy.IndexOf('actor?.data != null')
$searchIndex = $enemy.IndexOf('actor.isAllowedToLookForEnemies()')
if ($validIndex -lt 0 -or $searchIndex -lt 0 -or
    $validIndex -gt $searchIndex) {
    throw 'Enemy snapshot validation must run before native enemy lookup'
}

Write-Output 'Bandit stronghold deferred fall source guard passed.'
