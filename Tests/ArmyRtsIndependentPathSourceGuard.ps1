$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$controller = Get-Content -Raw -LiteralPath (Join-Path $root 'Code\core\lineage\ArmyRtsControllerService.cs')
$behaviour = Get-Content -Raw -LiteralPath (Join-Path $root 'Code\ai\behaviours\actor\BehArmyRtsMission.cs')
$postRunner = Get-Content -Raw -LiteralPath (Join-Path $root 'Code\core\performance\AWCooperativeActorPostRunner.cs')
$priorityRules = Get-Content -Raw -LiteralPath (Join-Path $root 'Code\core\performance\ArmyMilitaryMovementPriorityRules.cs')
$watchdog = Get-Content -Raw -LiteralPath (Join-Path $root 'Code\core\lineage\ArmyStallWatchdogService.cs')
$routeProvider = Get-Content -Raw -LiteralPath (Join-Path $root 'Code\core\pathfinding\ArmyRouteProvider.cs')

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

Require -Condition $controller.Contains('TrySubmitMemberObjectiveRoute(') -Message 'RTS controller must submit independent member path sessions.'
Require -Condition $controller.Contains('ResolveStableStrategicEndpoint(army, targetCity, runtime)') -Message 'RTS followers must resolve the locked strategic endpoint directly.'
Require -Condition $controller.Contains('ClearIndependentMemberPaths(') -Message 'RTS mission changes must cancel recorded member paths.'
Require -Condition $controller.Contains('ShouldReplaceMemberPath(') -Message 'RTS members must replace paths when their objective changes.'
Require -Condition (-not $controller.Contains('InstallFollowerSharedRoutes(')) -Message 'RTS controller must not install shared follower routes.'
Require -Condition (-not $behaviour.Contains('HasActiveCompleteSharedRoute(pActor)')) -Message 'RTS follower behaviour must not wait for a shared captain route.'
Require -Condition $behaviour.Contains('TryStepFollowerDirect(pActor, target)') -Message 'RTS follower behaviour must use the bounded direct correction path.'
Require -Condition $behaviour.Contains('AWArmyMarchService.TryStartCompleteSharedRoute(pActor)') -Message 'RTS captains must follow the Army shared strategic route.'
Require -Condition $behaviour.Contains('ShouldWaitForProviderRoute(pActor)') -Message 'RTS captains must wait for the provider result before local fallback.'
Require -Condition (-not $controller.Contains('ShouldRecoverStaleInstalledRoute(')) -Message 'RTS captain recovery must not depend on shared-route installation state.'
Require -Condition $postRunner.Contains('RunMilitaryP0Chunk(elapsed);') -Message 'large-step military P0 must run before ordinary actor post work.'
Require -Condition $postRunner.Contains('ArmyMilitaryMovementPriorityIndex.CopySnapshot(militaryP0ActorIds);') -Message 'military P0 must use a stable actor-ID snapshot.'
Require -Condition $priorityRules.Contains('CanAdmitOrdinaryActorWork(bool p0SlicePending)') -Message 'ordinary actor work must wait until the military P0 snapshot is drained.'
Require -Condition $priorityRules.Contains('return !p0SlicePending;') -Message 'ordinary actor work must wait until the military P0 snapshot is drained.'
Require -Condition $postRunner.Contains('ArmyMilitaryMovementPriorityIndex.WasProcessed(') -Message 'P0 members must not receive a second path or smooth movement pass.'
Require -Condition (-not $watchdog.Contains('TryTeleportFormationMember(')) -Message 'RTS member stalls must recover by same-target routing, not teleport to the captain.'
Require -Condition (-not $watchdog.Contains('ShouldRecoverStaleInstalledRoute(')) -Message 'RTS watchdog must not gate independent member recovery on shared routes.'
# The b finder advances work on worker threads and schedules continuations when
# steps are consumed; it no longer exposes the old main-thread Tick API.
$finder = Get-Content -Raw -LiteralPath (Join-Path $root 'Code/core/pathfinding/AWPathFinder.cs')
Require -Condition $routeProvider.Contains('_finder.Start(pWorkerCount);') -Message 'dedicated army routing must start its worker lifecycle.'
Require -Condition $routeProvider.Contains('_finder.Consume(finderRequestId);') -Message 'consuming army steps must advance the streamed finder session.'
Require -Condition $finder.Contains('AWPathWorkPriority.Continuation') -Message 'stream consumption must schedule bounded continuation work.'
Require -Condition ($routeProvider -match 'if \(_ownsFinder\)\s*\{\s*_finder.StopAndDrain\(\);\s*_finder.Dispose\(\);') -Message 'only the owned finder may be stopped and disposed.'
Require -Condition $routeProvider.Contains('CancelSharedRequests(') -Message 'shared finder cleanup must cancel only the provider requests.'

Write-Output 'Army RTS independent path source guard passed.'
