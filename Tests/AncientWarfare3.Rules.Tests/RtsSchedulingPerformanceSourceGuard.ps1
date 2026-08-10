$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$logisticsPath = Join-Path $projectRoot 'Code\core\lineage\ArmyLogisticsService.cs'
$retreatPath = Join-Path $projectRoot 'Code\core\lineage\ArmyRetreatService.cs'
$authorityPath = Join-Path $projectRoot 'Code\core\performance\AWAuthorityCycleService.cs'
$deferredRulesPath = Join-Path $projectRoot 'Code\core\lineage\DeferredRuntimeWorkRules.cs'
$deferredServicePath = Join-Path $projectRoot 'Code\core\lineage\DeferredRuntimeWorkService.cs'
$schedulerPath = Join-Path $projectRoot 'Code\core\performance\ArmyRtsSchedulingService.cs'
$budgetRulesPath = Join-Path $projectRoot 'Code\core\performance\ArmyRtsExecutionBudgetRules.cs'
$controllerPath = Join-Path $projectRoot 'Code\core\lineage\ArmyRtsControllerService.cs'
$watchdogPath = Join-Path $projectRoot 'Code\core\lineage\ArmyStallWatchdogService.cs'
$lifecyclePath = Join-Path $projectRoot 'Code\core\lineage\ArmyRtsWarLifecycleService.cs'
$reconciliationPath = Join-Path $projectRoot 'Code\core\lineage\ArmyRtsAssignmentReconciliationService.cs'
$successionPath = Join-Path $projectRoot 'Code\core\lineage\ArmyRtsSuccessionRecoveryService.cs'

function Require-Contains([string] $text, [string] $needle, [string] $message) {
    if (-not $text.Contains($needle)) {
        throw $message
    }
}

function Require-Match([string] $text, [string] $pattern, [string] $message) {
    if ($text -notmatch $pattern) {
        throw $message
    }
}

$logistics = Get-Content -Raw $logisticsPath
$processStart = $logistics.IndexOf('public static void ProcessFrame()',
    [System.StringComparison]::Ordinal)
$processEnd = $logistics.IndexOf('public static void RebuildRuntime()',
    $processStart, [System.StringComparison]::Ordinal)
if ($processStart -lt 0 -or $processEnd -lt 0) {
    throw 'Army logistics must expose an explicit ProcessFrame compatibility boundary.'
}
$processBody = $logistics.Substring($processStart, $processEnd - $processStart)
foreach ($forbidden in @('UpdateArmy(', 'TryGetLogisticsSample(',
        'UpdateSupply(', 'UpdateOrganization(', 'SetConnectivity(')) {
    if ($processBody.Contains($forbidden)) {
        throw "Army logistics ProcessFrame must not simulate $forbidden."
    }
}
$retreat = Get-Content -Raw $retreatPath
Require-Contains $retreat 'LegacyRetreatIndex' 'Army retreat must retain vanilla Army-loss baseline tracking.'

$authority = Get-Content -Raw $authorityPath
Require-Match $authority 'DeferredRuntimeWorkRules\s*\.\s*ResolveItemsPerAuthorityFrame' 'Authority deferred work must use the one-item frame budget.'
Require-Contains $authority 'pMaxItems: itemLimit' 'Authority deferred work must pass its explicit item budget to the drain.'

$deferredRules = Get-Content -Raw $deferredRulesPath
Require-Contains $deferredRules 'MaximumItemsPerAuthorityFrame = 1' 'Deferred work must allow at most one Action per authority frame.'
Require-Contains $deferredRules 'ResolveItemsPerAuthorityFrame' 'Deferred work must expose the authority-frame budget rule.'

$deferredService = Get-Content -Raw $deferredServicePath
Require-Contains $deferredService 'ShouldStartFrameDrain' 'Deferred work must gate repeated authority drains within one render frame.'
Require-Contains $deferredService '_lastDrainFrame' 'Deferred work must retain its last render-frame drain token.'

$scheduler = Get-Content -Raw $schedulerPath
Require-Contains $scheduler 'ArmyRtsExecutionBudgetRules.Capture(simulationMode,' 'RTS scheduling must capture one mode-aware budget at logical-pass entry.'
foreach ($budget in @('budget.FirstOrders', 'budget.AbstractBattles',
        'budget.ControllerArmies', 'budget.ReplenishmentArrivals',
        'budget.WatchdogArmies', 'budget.LifecycleDiscoveries',
        'budget.AssignmentReconciliations', 'budget.SuccessionRecoveries')) {
    Require-Contains $scheduler $budget "RTS scheduling must wire $budget into its drain."
}
Require-Contains $scheduler 'ArmyRouteProviderService.ProcessFrame' 'RTS scheduling must retain the bounded route-provider pulse.'
if ($scheduler.Contains('ArmyRouteProviderService.ProcessFrame(') -and
    -not $scheduler.Contains('ArmyRouteProviderService.ProcessFrame()')) {
    throw 'Large mode must not pass an expanded army budget into route planning.'
}
if ($scheduler.Contains('int.MaxValue')) {
    throw 'RTS scheduling must use stable pending snapshots instead of int.MaxValue drains.'
}

$budgetRules = Get-Content -Raw $budgetRulesPath
Require-Contains $budgetRules 'if (pMode == AWSimulationMode.Large) return pending;' 'Large mode must drain the stable pending snapshot.'
Require-Contains $budgetRules 'return Math.Min(pending, Math.Max(0, pNativeCap));' 'Native mode must preserve bounded subsystem caps.'

$controller = Get-Content -Raw $controllerPath
Require-Contains $controller 'public int PendingCount => _queuedIds.Count +' 'Controller snapshots must include normal and priority queues.'
Require-Contains $controller 'ProcessFrame(int pControllerBudget,' 'Controller processing must accept an explicit stable budget.'

$watchdog = Get-Content -Raw $watchdogPath
Require-Contains $watchdog 'public static int PendingArmyCount => ActiveArmyIds.Count;' 'Watchdog must expose its pending army snapshot.'
Require-Contains $watchdog 'bool pForceSample)' 'Large mode must be able to force one complete active-army sample.'

$lifecycle = Get-Content -Raw $lifecyclePath
Require-Contains $lifecycle 'public static int PendingDiscoveryArmyCount' 'Lifecycle discovery must expose its pending snapshot.'
Require-Contains $lifecycle 'ProcessAuthorityCycle(int pMaximumArmies)' 'Lifecycle discovery must accept an explicit budget.'

$reconciliation = Get-Content -Raw $reconciliationPath
Require-Contains $reconciliation 'public static int PendingRecordCount =>' 'Assignment reconciliation must expose its pending snapshot.'
Require-Contains $reconciliation 'ProcessAuthorityCycle(int pMaximumRecords)' 'Assignment reconciliation must accept an explicit budget.'

$succession = Get-Content -Raw $successionPath
Require-Contains $succession 'internal static int PendingRecoveryUpperBound' 'Authority recovery must expose its pending snapshot.'
Require-Contains $succession 'ProcessPendingRecoveries(int pMaximum,' 'Authority recovery must accept an explicit combined budget.'
