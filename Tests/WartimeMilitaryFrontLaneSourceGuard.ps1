$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string]$Path) {
    Get-Content -Raw (Join-Path $root $Path)
}

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

# The b scheduler uses a bounded front lane during Large mode and shares its
# single-actor executor with actor post. Frame and military-step tokens prevent
# ordinary movement from repeating a front-lane pass.
$post = Read-Source 'Code/core/performance/AWCooperativeActorPostRunner.cs'
$index = Read-Source 'Code/core/performance/ArmyMilitaryMovementPriorityIndex.cs'
$runner = Read-Source 'Code/core/performance/AWCooperativeSimulationRunner.cs'
$patch = Read-Source 'Code/patch/AW_FramePrioritySchedulerPatch.cs'
$controller = Read-Source 'Code/core/lineage/ArmyRtsControllerService.cs'
$front = Read-Source 'Code/core/performance/AWMilitaryFrontLaneScheduler.cs'

Require ($post.Contains('internal static void ProcessMilitaryP0Actor(long actorId,')) `
    'military P0 must expose one shared single-actor execution entry'
Require ($post.Contains('case PostStage.MilitaryP0:')) `
    'military P0 must remain an ordered actor-post stage'
Require ($post.Contains('ArmyRtsTransportService.ProcessMilitaryP0')) `
    'transport passengers must share the P0 lifecycle'
Require ($index.Contains('ProcessedFrameByActor')) `
    'military duplicate suppression must be keyed by render frame'
Require ($index.Contains('internal static void BeginFrame(int frameId)')) `
    'the military priority index must expose an explicit render-frame boundary'
Require (-not $index.Contains('ProcessedThisCycle.Clear();')) `
    'actor-cycle startup must not erase front-lane duplicate tokens'
Require ($runner.Contains('case SimulationStage.Actors:')) `
    'cooperative scheduler must retain the vanilla actor lifecycle'
Require ($patch.Contains('AWCooperativeSimulationRunner.Instance')) `
    'MapBox must retain the canonical cooperative scheduler boundary'
Require ($controller.Contains('ActiveWartimeArmyIds')) `
    'controller must retire wartime pressure when a war mission is invalidated'

Require ($patch.Contains('AWMilitaryFrontLaneScheduler.ProcessFrame();')) `
    'the frame boundary must advance the military front lane'
Require ($front.Contains('AWCooperativeActorPostRunner.ProcessMilitaryP0Actor(') -and
    $front.Contains('allowAdditionalFrameStep: true')) `
    'the front lane must share the P0 actor executor with explicit military-step ownership'
Require ($front.Contains('ElapsedMilliseconds(started) < FrameBudgetMilliseconds') -and
    $front.Contains('AWMilitaryFrontLaneRules.ResolveMaximumActors(')) `
    'front-lane work must respect elapsed-time and actor budgets'
Require ($front.Contains('ArmyMilitaryMovementPriorityIndex.CopySnapshot(Snapshot);') -and
    $front.Contains('ArmyMilitaryMovementPriorityIndex.BeginMilitaryStep();')) `
    'front-lane sweeps must use a stable actor snapshot and distinct step token'
Require ($post.Contains('WasProcessedInMilitaryStep(actorId)') -and
    $post.Contains('ArmyMilitaryMovementPriorityIndex.WasProcessed(actorId)')) `
    'military and ordinary passes must check the appropriate duplicate token'
Require ($front.Contains('!AW3MultiplayerReplicaScope.IsReplicaSession') -and
    $front.Contains('!World.world.isPaused()') -and
    $front.Contains('AWPerformanceSettings.Mode == AWSimulationMode.Large')) `
    'the front lane must respect replica, pause, and simulation-mode gates'

foreach ($forbidden in @(
    'CityMilitaryThreatFacts',
    'KingdomWarDirectorService',
    'ArmyStallWatchdogService',
    'Finder.Poll')) {
    Require (-not ($post + $runner + $patch + $front).Contains($forbidden)) `
        "integrated P0/scheduler must not run legacy strategic or path-result consumer: $forbidden"
}

Write-Output 'Integrated military P0 source guard passed.'
