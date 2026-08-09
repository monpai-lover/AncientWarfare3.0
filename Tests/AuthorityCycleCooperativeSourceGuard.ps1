$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $root 'Code\core\performance\AWAuthorityCycleService.cs'
$runnerPath = Join-Path $root 'Code\core\performance\AWCooperativeSimulationRunner.cs'
$service = Get-Content -Raw $servicePath
$runner = Get-Content -Raw $runnerPath

function Require-Text([string]$text, [string]$needle, [string]$message) {
    if (-not $text.Contains($needle)) {
        throw "Missing ${message}: ${needle}"
    }
}

function Require-Regex([string]$text, [string]$pattern, [string]$message) {
    if ($text -notmatch $pattern) {
        throw "Missing ${message}: ${pattern}"
    }
}

Require-Text $service 'private enum CooperativeAuthorityStage' `
    'persistent cooperative authority stage model'
Require-Text $service 'public static string GetCooperativePhaseName()' `
    'service-specific phase accessor'
Require-Text $service 'public static bool ProcessCooperativeStep(' `
    'single-step cooperative authority API'
Require-Text $service 'public static void AbortCooperativeCycle()' `
    'scheduler abort clearing cooperative authority state'
Require-Text $service 'ResetCooperativeState();' `
    'world reset clearing cooperative cursor state'
Require-Text $service 'aw3.authority.succession_relationships' `
    'succession relationship phase name'
Require-Text $service 'aw3.authority.army_rts' `
    'RTS phase name'
Require-Text $service 'aw3.authority.actor_death_archive' `
    'actor death archive phase name'
Require-Text $service 'public static void ProcessNativeCycle()' `
    'unchanged native scheduler entry point'

Require-Regex $runner `
    'case SimulationStage\.Aw3Authority:\s*return AWAuthorityCycleService\s*\.\s*GetCooperativePhaseName\(\);' `
    'authority service phase routing through the frame governor'
Require-Regex $runner `
    'case SimulationStage\.Aw3Authority:\s*if \(AWAuthorityCycleService\.ProcessCooperativeStep\(\s*_logicalTicksAdmitted, _cyclePaused\)\)\s*Advance\(SimulationStage\.Complete\);' `
    'completion-gated authority simulation advancement'
Require-Text $runner 'AWAuthorityCycleService.AbortCooperativeCycle();' `
    'cooperative authority cleanup from scheduler abort'

if ($runner.Contains('AWAuthorityCycleService.ProcessCooperativeCycle(')) {
    throw 'Large scheduler still invokes the monolithic authority cycle.'
}

Write-Host 'Authority cycle cooperative source guard passed.'
