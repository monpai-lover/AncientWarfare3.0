$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$mandatePath = Join-Path $root 'Code/core/lineage/MandateService.cs'
$phasePath = Join-Path $root 'Code/core/lineage/MandatePhaseService.cs'
$mandate = Get-Content -Raw -LiteralPath $mandatePath
$phase = Get-Content -Raw -LiteralPath $phasePath

function Require-Text([string]$source, [string]$needle, [string]$message) {
    if (-not $source.Contains($needle)) { throw $message }
}

function Method-Slice([string]$source, [string]$start, [string]$next) {
    $begin = $source.IndexOf($start, [System.StringComparison]::Ordinal)
    if ($begin -lt 0) { throw "Missing method start: $start" }
    $end = $source.IndexOf($next, $begin + $start.Length,
        [System.StringComparison]::Ordinal)
    if ($end -lt 0) { throw "Missing method boundary: $next" }
    return $source.Substring($begin, $end - $begin)
}

$city = Method-Slice $mandate 'public static void OnCityTransferStarting(' `
    'private static void TrackHostileMandateFinalCityConqueror('
$war = Method-Slice $mandate 'public static void OnWarEnded(' `
    'private static int ReadOrdinaryWarDefeatDelta('
$change = Method-Slice $mandate 'private static void ChangeMandate(' `
    'private static MandateReport ReadReportFromDb('
$chaos = Method-Slice $phase 'private static bool EvaluateChaosLifecycle(' `
    'private static void SetPhase('

Require-Text $city 'ApplyImmediateCoreCityLoss(' `
    'Mandate city transfer does not apply immediate core loss.'
Require-Text $war 'ReadOrdinaryWarDefeatDelta(' `
    'Ordinary war defeat is not connected to Mandate loss.'
Require-Text $war 'Kingdom loser = pWinner == WarWinner.Attackers' `
    'Ordinary war defeat does not resolve the losing side.'
Require-Text $mandate 'snapshot.AttackerLosses' `
    'Attacking mandate losses are not read from the war snapshot.'
Require-Text $mandate 'snapshot.DefenderLosses' `
    'Defending mandate losses are not read from the war snapshot.'
Require-Text $change 'SyncMandateRuntimeMirrors(' `
    'ChangeMandate does not synchronize kingdom.data mirrors.'
Require-Text $mandate 'LineageKeys.MANDATE_VALUE' `
    'Mandate value mirror write is missing.'
Require-Text $mandate 'LineageKeys.MANDATE_AUTHORITY' `
    'Mandate authority mirror write is missing.'
Require-Text $mandate 'LineageKeys.MANDATE_PRESTIGE' `
    'Mandate prestige mirror write is missing.'
Require-Text $chaos 'MandateDeclineRules.IsChaosUnresolved(' `
    'Chaos unresolved-year rule is not wired.'
Require-Text $chaos 'MandateDeclineRules.ShouldRecoverChaos(' `
    'Chaos recovery rule is not wired.'

foreach ($slice in @($city, $war, $chaos)) {
    foreach ($forbidden in @('World.world.actors', 'World.world.cities',
            '.getUnits()')) {
        if ($slice.Contains($forbidden)) {
            throw "Mandate decline hot path contains forbidden scan: $forbidden"
        }
    }
}

Write-Output 'Mandate decline/collapse source guard passed.'
