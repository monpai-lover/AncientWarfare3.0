$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $repoRoot `
    'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'
$runnerPath = Join-Path $repoRoot `
    'Code/core/performance/AWCooperativeActorPostRunner.cs'
$service = Get-Content -Raw -Encoding UTF8 $servicePath
$runner = Get-Content -Raw -Encoding UTF8 $runnerPath
$population = Get-Content -Raw -Encoding UTF8 (Join-Path $repoRoot `
    'Code/core/lineage/PeasantRebelBanditStrongholdPopulationService.cs')

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
    'internal static void QueuePopulationFall('
$restore = Get-MethodBody $service 'internal static void RestoreRuntime(' `
    'private static void QueueFall('

if ($capture -notmatch 'QueueFall\(' -or $capture -match 'CompleteFall\(') {
    throw 'Completed bandit capture must queue fall instead of mutating inline'
}
if ($death -match 'CompleteFall\(') {
    throw 'Actor-death callback still completes bandit fall inline'
}
foreach ($token in @('LastHostileKillerKingdomId',
        'PeasantRebelBanditStateStore.Write(', 'EnqueueStronghold(')) {
    if ($death -notmatch [regex]::Escape($token)) {
        throw "Actor-death callback is missing $token"
    }
}
if ($death -match 'getPopulationPeople\(|CountLivingResidents\(|QueueFall\(') {
    throw 'Actor death must only enqueue population work until resident mutation finishes'
}
if ($restore -match 'CompleteFall\(' -or $restore -notmatch 'QueueFall\(') {
    throw 'Runtime restore must defer stronghold fall'
}
if ($restore -notmatch 'EnqueueStronghold\(' -or
    $restore -match 'getPopulationPeople\(|ResolveFallAction\(') {
    throw 'Runtime restore must queue living-resident validation, including combat-empty strongholds'
}
foreach ($token in @('CountLivingResidents(city)', 'ShouldQueueFall(true, living)',
        'QueuePopulationFall(cityId)', 'IsLivingResident(',
        'actor.isAlive()', 'actor.isRekt()', 'actor.city == pCity')) {
    if (-not $population.Contains($token)) {
        throw "Deferred stronghold population validation is missing $token"
    }
}
$populationFall = Get-MethodBody $service 'internal static void QueuePopulationFall(' `
    'internal static void RestoreRuntime('
if ($populationFall -notmatch 'QueueFall\(' -or
    $populationFall -match 'CompleteFall\(') {
    throw 'Population settlement must pass through the deferred fall boundary'
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
$liveActor = Get-MethodBody $runner 'private static bool IsLiveEnemySearchActor(' `
    'private void PrepareEnemySearch('
foreach ($token in @('actor?.data != null', '!actor.isRekt()',
        'actor.isAlive()', 'actor.asset != null',
        'actor.kingdom?.data != null', 'actor.kingdom.asset != null',
        'actor.current_tile != null')) {
    if ($liveActor -notmatch [regex]::Escape($token)) {
        throw "Enemy preparation snapshot filter is missing $token"
    }
}
$validIndex = $enemy.IndexOf('if (!IsLiveEnemySearchActor(actor))')
$searchIndex = $enemy.IndexOf('actor.isAllowedToLookForEnemies()')
if ($validIndex -lt 0 -or $searchIndex -lt 0 -or
    $validIndex -gt $searchIndex) {
    throw 'Enemy snapshot validation must run before native enemy lookup'
}

Write-Output 'Bandit stronghold deferred fall source guard passed.'
