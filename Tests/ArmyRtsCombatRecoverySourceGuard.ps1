$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$controllerPath = Join-Path $repo 'Code\core\lineage\ArmyRtsControllerService.cs'
$rulesPath = Join-Path $repo 'Code\core\lineage\ArmyRtsWarLifecycleRules.cs'
$runnerPath = Join-Path $repo 'Code\core\performance\AWCooperativeActorPostRunner.cs'
$cadencePath = Join-Path $repo 'Code\core\performance\ArmyRtsMovementCadenceRules.cs'

foreach ($path in @($controllerPath, $rulesPath, $runnerPath, $cadencePath)) {
    if (-not (Test-Path $path)) { throw "Missing source: $path" }
}

$controller = Get-Content -Raw $controllerPath
$rules = Get-Content -Raw $rulesPath
$runner = Get-Content -Raw $runnerPath
$cadence = Get-Content -Raw $cadencePath

function Require-Contains([string] $text, [string] $needle,
    [string] $message) {
    if (-not $text.Contains($needle)) { throw $message }
}

function Require-Match([string] $text, [string] $pattern,
    [string] $message) {
    if ($text -notmatch $pattern) { throw $message }
}

Require-Contains $controller 'pCity.target_attack_city' `
    'The vanilla city attack check receives the source city and must inspect its attack target.'
Require-Match $controller 'MissionIndex\.SnapshotTarget\(\s*attackTarget\.id\s*\)' `
    'Target-city vanilla combat handoff must find the detached RTS mission by the source city attack target.'
Require-Contains $controller 'TryIssueVanillaCityAttackOrder(pArmy,' `
    'Target-territory handoff must publish a vanilla city attack order before releasing RTS actors.'
Require-Contains $controller 'source.target_attack_city = pTarget;' `
    'The released RTS army anchor city must receive the exact target city.'
Require-Contains $controller 'source.target_attack_zone = pTarget.hasZones()' `
    'The released RTS army anchor city must receive a target-city attack zone.'
Require-Contains $controller 'RefreshReleasedArmyPeacetimeJobs(army);' `
    'Invalidating an RTS mission must immediately restore released standing-army jobs.'
Require-Contains $controller 'StandingArmyPeacetimeService.RefreshJob(actor);' `
    'Released standing soldiers must be evaluated for the peacetime patrol job.'
Require-Match $controller 'ShouldHandoffObjective\([\s\S]{0,420}Invalidate\(pArmyId\)' `
    'A completed objective must end its RTS mission before patrol can own the actor.'
Require-Match $controller 'target_control_event[\s\S]{0,480}Invalidate\(armyId\)' `
    'City-control completion must end every matching RTS mission before patrol can own the actor.'
Require-Contains $controller 'RoyalGuardService.EnsureProtectKingTask(pActor);' `
    'RTS release must restore royal guards to protect-the-king behavior.'
Require-Match $controller 'ShouldOwnMilitaryActor\(Actor pActor,[\s\S]{0,160}RoyalGuardService\.IsRoyalGuard\(pActor\)' `
    'The universal RTS ownership gate must exclude royal guards before task or decision interception.'
Require-Contains (Get-Content -Raw (Join-Path $repo 'Code\core\lineage\AWArmyRoleRules.cs')) `
    'return pArmyRole != AWArmyRole.RoyalGuard &&' `
    'Royal Guard captains must never become RTS-owned, including kings and leaders.'
Require-Contains $rules 'ShouldAllowVanillaCityAttack(' `
    'Only a persisted VanillaCombat lifecycle phase may release a target city.'
Require-Contains $runner 'ConfigurePriorityOnly(batch, job,' `
    'Skipped large-scheduler movement batches must retain a restricted RTS path pulse.'
Require-Contains $cadence 'actorInArmy && actorIsMoving' `
    'Skipped smooth-movement priority must remain limited to moving army actors.'

Write-Output 'ArmyRtsCombatRecoverySourceGuard: PASS'
