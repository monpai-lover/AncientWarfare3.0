param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string]$relativePath) {
    return Get-Content -Raw (Join-Path $root $relativePath)
}

function Require-Contains([string]$name, [string]$source,
    [string]$value) {
    if (-not $source.Contains($value)) {
        throw "$name is missing required source: $value"
    }
}

$runtime = Read-Source 'Code/core/policy/RuntimePerformanceDiagnostic.cs'
$ai = Read-Source 'Code/patch/AW_ActorAiBenchmarkPatch.cs'
$batch = Read-Source 'Code/patch/AW_ActorBatchBenchmarkPatch.cs'
$age = Read-Source 'Code/patch/AW_AgePatch.cs'

$racePatchPath = Join-Path $root 'Code/patch/AW_ActorRacePerformancePatch.cs'
if (Test-Path -LiteralPath $racePatchPath) {
    throw 'Actor race sampling must not patch updateAge or calculateMainSprite.'
}

Require-Contains 'runtime diagnostics' $runtime `
    'public static bool ShouldCollectActorDetail()'
Require-Contains 'runtime diagnostics' $runtime `
    'public static bool TryConsumeActorDetailSample()'
Require-Contains 'runtime diagnostics' $runtime '_actorDetailSamples'

$gate = $ai.IndexOf('ShouldCollectActorDetail()')
$task = $ai.IndexOf('__instance.ai.task.id')
if ($gate -lt 0 -or $task -lt 0 -or $task -lt $gate) {
    throw 'Actor task lookup must occur after the disabled diagnostic gate.'
}

Require-Contains 'Actor batch diagnostics' $batch `
    'ShouldCollectActorBatch()'

Require-Contains 'Actor age patch' $age `
    'ActorAgeWorkService.Process(__instance);'
if ($age.Contains('DynasticTitleService.OnAgeUpdated(__instance);') -or
    $age.Contains('StandingArmyPeacetimeService.RefreshJob(__instance);') -or
    $age.Contains('DynasticReproductionService.ReleaseExistingMilitaryRole(')) {
    throw 'Actor age patch must delegate AW3 state work to ActorAgeWorkService.'
}

$pathRequest = Read-Source 'Code/core/pathfinding/AWPathRequest.cs'
$pathFinder = Read-Source 'Code/core/pathfinding/AWPathFinder.cs'
Require-Contains 'Actor path request' $pathRequest `
    'public AWPathReuseKey ReuseKey { get; private set; }'
Require-Contains 'Actor path finder' $pathFinder `
    'AWPathRequestReuseRules.CanReuse('
Require-Contains 'Actor path finder' $pathFinder 'pRequest.ReuseKey'
Require-Contains 'Actor path diagnostics log' $runtime `
    'path_reused_running='

Write-Output 'ActorRuntimePerformanceSourceGuardTests: PASS'
