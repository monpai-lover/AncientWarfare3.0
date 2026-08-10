$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Read-Source([string] $relativePath) {
    return Get-Content -Raw (Join-Path $projectRoot $relativePath)
}

function Require-Contains([string] $text, [string] $needle,
    [string] $message) {
    if (-not $text.Contains($needle)) { throw $message }
}

function Require-Match([string] $text, [string] $pattern,
    [string] $message) {
    if ($text -notmatch $pattern) { throw $message }
}

$content = Read-Source 'Code\content\ArmyRtsContent.cs'
$safetyPatch = Read-Source 'Code\patch\AW_ArmySafetyPatch.cs'
Require-Contains $content 'ArmyRtsControllerService.OwnsLiveActor(pActor)' 'Strategic decision assets must use actor-level live RTS ownership.'
Require-Contains $safetyPatch 'ArmyRtsControllerService.OwnsLiveActor(pActor)' 'DecisionHelper interception must use actor-level live RTS ownership.'
Require-Contains $safetyPatch 'ShouldAllowVanillaDecisionEvaluation(' 'Vanilla tactical decisions must resume outside RTS actor ownership.'

$heirPatch = Read-Source 'Code\patch\AW_HeirPatch.cs'
Require-Match $heirPatch 'if \(setKingSucceeded\)\s+ArmyRtsSuccessionRecoveryService\.OnKingInstalled\(' 'Every successful Kingdom.setKing path must enqueue RTS recovery.'
Require-Contains $heirPatch '__instance, king, pFromLoad' 'King recovery must preserve the load-boundary fact.'

$deathPatch = Read-Source 'Code\patch\AW_ActorDeathPatch.cs'
Require-Contains $deathPatch 'ShouldEnqueueCaptainRecovery(' 'Actor death must identify an active RTS captain before vanilla teardown.'
Require-Contains $deathPatch 'ArmyRtsSuccessionRecoveryService.OnCaptainDied(' 'Confirmed captain death must enqueue bounded recovery.'

$succession = Read-Source 'Code\core\lineage\ArmyRtsSuccessionRecoveryService.cs'
Require-Contains $succession 'army.checkCaptainExistence();' 'Captain recovery must reuse the original army captain repair method.'
Require-Contains $succession 'RehydrateAfterAuthorityChange(army);' 'Authority recovery must rehydrate RTS mission ownership.'

$guardService = Read-Source 'Code\core\lineage\RoyalGuardService.cs'
Require-Contains $guardService 'RepairProtectKingTaskIfNeeded(captain);' 'Royal guard maintenance must repair the captain protection task.'
Require-Contains $guardService 'RepairProtectKingTaskIfNeeded(guard);' 'Royal guard maintenance must repair bounded member protection tasks.'

$watchdogRules = Read-Source 'Code\core\lineage\ArmyStallWatchdogRules.cs'
Require-Match $watchdogRules 'pState == ArmyRtsState\.Rally\s+\|\|' 'Rally must be eligible for formation-progress watchdog recovery.'

$controller = Read-Source 'Code\core\lineage\ArmyRtsControllerService.cs'
Require-Contains $controller 'ShouldRecoverStaleInstalledRoute(' 'Controller must detect stale installed routes.'
Require-Contains $controller 'RequestRouteReplan(pArmyId,' 'Stale routes must request bounded replanning.'
Require-Contains $controller 'HasReachedStrategicDestination(' 'Controller must accept validated strategic endpoints.'

$scheduler = Read-Source 'Code\core\performance\ArmyRtsSchedulingService.cs'
Require-Contains $scheduler 'ArmyRtsExecutionBudgetRules.Capture(simulationMode,' 'RTS logical passes must use one stable pending snapshot.'
Require-Contains $scheduler 'ArmyRouteProviderService.ProcessFrame' 'Route planning must retain its independent bounded pulse.'
if ($scheduler -match 'Process(?:Frame|AuthorityCycle|PendingRecoveries)\s*\(\s*int\.MaxValue') {
    throw 'RTS drains must never use int.MaxValue as a live queue budget.'
}
if ($scheduler.Contains('World.world.armies') -or
    $scheduler.Contains('World.world?.armies')) {
    throw 'Large scheduling must not enumerate every world army for route planning.'
}
