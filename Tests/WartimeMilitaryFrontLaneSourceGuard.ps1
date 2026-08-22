$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string]$Path) {
    Get-Content -Raw (Join-Path $root $Path)
}

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$post = Read-Source 'Code/core/performance/AWCooperativeActorPostRunner.cs'
$index = Read-Source 'Code/core/performance/ArmyMilitaryMovementPriorityIndex.cs'
$scheduler = Read-Source 'Code/core/performance/AWMilitaryFrontLaneScheduler.cs'
$patch = Read-Source 'Code/patch/AW_FramePrioritySchedulerPatch.cs'
$governor = Read-Source 'Code/core/performance/AWFramePriorityGovernor.cs'

Require ($post.Contains(
    'internal static void ProcessMilitaryP0Actor(long actorId,')) `
    'military P0 must expose one shared single-actor execution entry'
Require ($index.Contains('ProcessedFrameByActor')) `
    'military duplicate suppression must be keyed by render frame'
Require ($index.Contains('internal static void BeginFrame(int frameId)')) `
    'the military priority index must expose an explicit render-frame boundary'
Require ($index.Contains('internal static int Count => Entries.Count;')) `
    'the front lane must inspect pending military work without copying actors'
Require (-not $index.Contains('ProcessedThisCycle.Clear();')) `
    'actor-cycle startup must not erase front-lane duplicate tokens'

$nativePathCount = ([regex]::Matches($post,
    'actor\.b5_checkPathMovement\(cycleElapsed\);')).Count
Require ($nativePathCount -eq 8) `
    'the front lane must reuse the existing P0 chain instead of duplicating it'
Require ($scheduler.Contains('AWCooperativeActorPostRunner.ProcessMilitaryP0Actor(')) `
    'front lane must call the shared P0 execution entry'
Require ($scheduler.Contains('allowAdditionalFrameStep: true')) `
    'front lane must allow multiple fixed military steps in one render frame'
Require ($index.Contains('ProcessedThisMilitaryStep')) `
    'fixed-step duplicate suppression must be distinct from render-frame suppression'
Require ($scheduler.Contains('ArmyMilitaryMovementPriorityIndex.RtsMemberCount')) `
    'peaceful royal guards must not activate wartime dynamic FPS'
Require ($scheduler.Contains('FrameBudgetMilliseconds = 2.5d')) `
    'front lane must keep a bounded per-frame budget'
Require ($scheduler.Contains('AWMilitaryFrontLaneRules.FixedStepSeconds')) `
    'front lane must use the fixed military step'
Require ($patch.Contains('AWMilitaryFrontLaneScheduler.ProcessFrame();')) `
    'MapBox frame boundary must run the front lane'
Require ($governor.Contains('EffectiveTargetRenderFps')) `
    'governor must expose a dynamic target without mutating config'
Require ($governor.Contains('AWWartimeFrameBudgetRules.Advance(')) `
    'governor must use the wartime hysteresis rules'
Require ($governor.Contains('AWMilitaryFrontLaneScheduler.GetDiagnostics()')) `
    'diagnostics must expose front-lane counters'
foreach ($forbidden in @(
    'CityMilitaryThreatFacts',
    'KingdomWarDirectorService',
    'ArmyStallWatchdogService',
    'Finder.Poll')) {
    Require (-not $scheduler.Contains($forbidden)) `
        "front lane must not run strategic or path-result consumer: $forbidden"
}

Write-Output 'Wartime military front lane source guard passed.'
