$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$controller = Get-Content -Raw -LiteralPath (Join-Path $root 'Code\core\lineage\ArmyRtsControllerService.cs')
$behaviour = Get-Content -Raw -LiteralPath (Join-Path $root 'Code\ai\behaviours\actor\BehArmyRtsMission.cs')
$postRunner = Get-Content -Raw -LiteralPath (Join-Path $root 'Code\core\performance\AWCooperativeActorPostRunner.cs')

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

Require -Condition $controller.Contains('TrySubmitMemberObjectiveRoute(') -Message 'RTS controller must submit independent member path sessions.'
Require -Condition $controller.Contains('ResolveStableStrategicEndpoint(army, targetCity, runtime)') -Message 'RTS followers must resolve the locked strategic endpoint directly.'
Require -Condition $controller.Contains('ClearIndependentMemberPaths(') -Message 'RTS mission changes must cancel recorded member paths.'
Require -Condition $controller.Contains('ShouldReplaceMemberPath(') -Message 'RTS members must replace paths when their objective changes.'
Require -Condition (-not $controller.Contains('InstallFollowerSharedRoutes(')) -Message 'RTS controller must not install shared follower routes.'
Require -Condition (-not $behaviour.Contains('HasActiveCompleteSharedRoute(pActor)')) -Message 'RTS follower behaviour must not wait for a shared captain route.'
Require -Condition (-not $behaviour.Contains('TryStepFollowerDirect(pActor, target)')) -Message 'RTS follower behaviour must use normal individual path submission.'
Require -Condition $postRunner.Contains('RunMilitaryP0Slice(cycleElapsed);') -Message 'large-step military P0 must run before ordinary actor post work.'
Require -Condition $postRunner.Contains('ArmyMilitaryMovementPriorityIndex.TakeNextSlice(') -Message 'military P0 must use a bounded actor-ID slice.'
Require -Condition $postRunner.Contains('ArmyMilitaryMovementPriorityIndex.WasProcessed(') -Message 'P0 members must not receive a second path or smooth movement pass.'

Write-Output 'Army RTS independent path source guard passed.'
