$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string]$Path) {
    Get-Content -Raw (Join-Path $root $Path)
}

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

# The old standalone AWMilitaryFrontLaneScheduler was intentionally removed
# when the scheduler converged with Cultiway master. Military P0 now runs as
# one ordered actor-post stage, so this guard validates that canonical owner
# instead of allowing the deleted second scheduler to return.
$post = Read-Source 'Code/core/performance/AWCooperativeActorPostRunner.cs'
$index = Read-Source 'Code/core/performance/ArmyMilitaryMovementPriorityIndex.cs'
$runner = Read-Source 'Code/core/performance/AWCooperativeSimulationRunner.cs'
$patch = Read-Source 'Code/patch/AW_FramePrioritySchedulerPatch.cs'
$controller = Read-Source 'Code/core/lineage/ArmyRtsControllerService.cs'

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

foreach ($forbidden in @(
    'AWMilitaryFrontLaneScheduler',
    'CityMilitaryThreatFacts',
    'KingdomWarDirectorService',
    'ArmyStallWatchdogService',
    'Finder.Poll')) {
    Require (-not ($post + $runner + $patch).Contains($forbidden)) `
        "integrated P0/scheduler must not run legacy strategic or path-result consumer: $forbidden"
}

Write-Output 'Integrated military P0 source guard passed.'
