$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$failures = [Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    return [IO.File]::ReadAllText(
        (Join-Path $root $relativePath), [Text.Encoding]::UTF8)
}

function Require-Contains(
    [string]$text,
    [string]$needle,
    [string]$message) {
    if (-not $text.Contains($needle)) {
        $failures.Add($message)
    }
}

function Forbid-Contains(
    [string]$text,
    [string]$needle,
    [string]$message) {
    if ($text.Contains($needle)) {
        $failures.Add($message)
    }
}

function Get-MethodBlock([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) {
        throw "Could not locate method '$signature'."
    }

    $open = $source.IndexOf('{', $start)
    if ($open -lt 0) {
        throw "Could not locate opening brace for '$signature'."
    }

    $depth = 0
    for ($index = $open; $index -lt $source.Length; $index++) {
        if ($source[$index] -eq '{') {
            $depth++
        }
        elseif ($source[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) {
                return $source.Substring($start, $index - $start + 1)
            }
        }
    }

    throw "Could not locate closing brace for '$signature'."
}

$runner = Read-Source `
    'Code\core\performance\AWCooperativeSimulationRunner.cs'
$runFrame = Get-MethodBlock $runner `
    'public void RunFrame(MapBox pMap, bool pAllowNewCycles = true)'
Require-Contains $runFrame @'
bool actorBackgroundPending =
                        _actorRunner.WaitingForBackgroundWork &&
                        !_actorRunner.IsBackgroundWorkCompleted;
'@ 'Completed Actor post work must return to Step() for consumption.'
Require-Contains $runFrame @'
bool buildingBackgroundPending =
                        _buildingRunner.WaitingForBackgroundWork &&
                        !_buildingRunner.IsBackgroundWorkCompleted;
'@ 'Completed building background work must return to Step() for consumption.'

$schedulerPatch = Read-Source `
    'Code\patch\AW_FramePrioritySchedulerPatch.cs'
$takeOver = Get-MethodBlock $schedulerPatch `
    'private static bool TakeOverMainSimulation(MapBox __instance,'
Require-Contains $takeOver @'
if (!AWPerformanceSettings.EnableFramePriorityScheduler &&
                !runner.Active)
'@ 'Disabling the scheduler may release control only at a cycle boundary.'
Require-Contains $takeOver @'
runner.RunFrame(__instance,
                    AWPerformanceSettings.EnableFramePriorityScheduler);
'@ 'The scheduler setting must control new-cycle admission, not an active cycle.'
Forbid-Contains $takeOver 'runner.RunFrame(__instance);' `
    'TakeOverMainSimulation must pass the new-cycle admission decision explicitly.'

$disabledGate = $takeOver.IndexOf(
    'if (!AWPerformanceSettings.EnableFramePriorityScheduler',
    [StringComparison]::Ordinal)
foreach ($boundary in @(
        'if (SmoothLoader.isLoading())',
        'if (AW3MultiplayerReplicaScope.IsReplicaSession)',
        'if (runner.RequiresControl &&')) {
    $boundaryIndex = $takeOver.IndexOf(
        $boundary, [StringComparison]::Ordinal)
    if ($boundaryIndex -lt 0 -or $boundaryIndex -gt $disabledGate) {
        $failures.Add(
            "Scheduler abort boundary '$boundary' must remain explicit before disabled admission handling.")
    }
    else {
        $boundaryBody = Get-MethodBlock $takeOver $boundary
        Require-Contains $boundaryBody 'ResetSchedulerState(' `
            "Scheduler abort boundary '$boundary' must explicitly reset an active cycle."
    }
}

foreach ($method in @(
        'private static bool PrepareBuildingPresentationFrame(',
        'private static bool PreparePresentationFrame(')) {
    $body = Get-MethodBlock $schedulerPatch $method
    Require-Contains $body @'
if (!AWPerformanceSettings.EnableFramePriorityScheduler &&
                !runner.Active &&
                !runner.HasMutatingPresentationWorkInFlight)
'@ "$method must keep read-boundary protection until active and mutating work finishes."
    Forbid-Contains $body @'
if (!AWPerformanceSettings.EnableFramePriorityScheduler)
'@ "$method must not return to native presentation while scheduler work is active."
}

$boatPatch = Read-Source 'Code\patch\AW_ActorBoatLifecyclePatch.cs'
foreach ($method in @(
        'private static void EmbarkIntoPostfix(Actor __instance)',
        'private static void ExitBoatPostfix(Actor __instance)',
        'private static void ClearManagersPostfix(Actor __instance)',
        'private static void DisposePrefix(Actor __instance)')) {
    $body = Get-MethodBlock $boatPatch $method
    Require-Contains $body 'AWInsideBoatActorIndex.Notify(' `
        "$method must keep the boat index current in native mode."
    Forbid-Contains $body 'EnableFramePriorityScheduler' `
        "$method must not gate boat-index lifecycle notifications."
}

foreach ($method in @(
        'private static bool CheckInsideBatchPrefix(',
        'private static bool CheckInsidePrefix(Actor __instance)')) {
    $body = Get-MethodBlock $boatPatch $method
    Require-Contains $body 'EnableFramePriorityScheduler' `
        "$method must leave the native checkInside path enabled when the scheduler is off."
}

$clearWorld = Get-MethodBlock $boatPatch `
    'private static void ClearWorldPrefix()'
Require-Contains $clearWorld 'AWInsideBoatActorIndex.Reset();' `
    'MapBox.clearWorld must reset the boat index.'

$actorParallel = Read-Source `
    'Code\core\performance\AWCooperativeActorParallelJobRunner.cs'
$timerActor = Get-MethodBlock $actorParallel `
    'private void RunTimerActor(Actor pActor)'
Require-Contains $timerActor @'
if (pActor._precalc_movement_speed_skips > 0)
'@ 'Moving Actor speed calculation must preserve the native skip counter.'
Require-Contains $timerActor @'
pActor._precalc_movement_speed_skips--;
'@ 'Moving Actor speed skips must count down instead of recalculating every tick.'
Require-Contains $actorParallel `
    'internal static void RefreshFrameVisibility()' `
    'Skipped visibility jobs require one explicit visibility refresh per render frame.'
Require-Contains $actorParallel `
    'lastVisibilityFrame == UnityEngine.Time.frameCount' `
    'Actor visibility refresh must be idempotent within one render frame.'

$beforeMapUpdate = Get-MethodBlock $schedulerPatch `
    'private static void BeforeMapBoxUpdate(MapBox __instance,'
Require-Contains $beforeMapUpdate @'
EnsureActorReadBoundary("mapbox.frame_begin");
                    EnsureBuildingReadBoundary("mapbox.frame_begin");
'@ 'Frame visibility refresh must follow Actor and Building read barriers.'
Require-Contains $beforeMapUpdate `
    'AWCooperativeActorParallelJobRunner.RefreshFrameVisibility();' `
    'MapBox frame start must refresh Actor visibility before timer workers consume it.'
$actorBoundaryIndex = $beforeMapUpdate.IndexOf(
    'EnsureActorReadBoundary("mapbox.frame_begin");',
    [StringComparison]::Ordinal)
$visibilityRefreshIndex = $beforeMapUpdate.IndexOf(
    'AWCooperativeActorParallelJobRunner.RefreshFrameVisibility();',
    [StringComparison]::Ordinal)
if ($visibilityRefreshIndex -lt 0 -or
    $actorBoundaryIndex -lt 0 -or
    $visibilityRefreshIndex -lt $actorBoundaryIndex) {
    $failures.Add(
        'Actor visibility refresh must run after the frame-start Actor read barrier.')
}

if ($failures.Count -gt 0) {
    Write-Output "Cultiway scheduler completion source guard failures: $($failures.Count)"
    foreach ($failure in $failures) {
        Write-Output " - $failure"
    }
    exit 1
}

Write-Output 'Cultiway scheduler completion source guard passed.'
