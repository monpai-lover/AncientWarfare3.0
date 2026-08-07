$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$logisticsPath = Join-Path $projectRoot 'Code\core\lineage\ArmyLogisticsService.cs'
$retreatPath = Join-Path $projectRoot 'Code\core\lineage\ArmyRetreatService.cs'
$authorityPath = Join-Path $projectRoot 'Code\core\performance\AWAuthorityCycleService.cs'
$deferredRulesPath = Join-Path $projectRoot 'Code\core\lineage\DeferredRuntimeWorkRules.cs'
$deferredServicePath = Join-Path $projectRoot 'Code\core\lineage\DeferredRuntimeWorkService.cs'

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
