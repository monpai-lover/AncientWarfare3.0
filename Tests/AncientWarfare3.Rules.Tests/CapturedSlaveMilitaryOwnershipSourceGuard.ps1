param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$slavePath = Join-Path $root 'Code/core/lineage/SlaveService.cs'
$source = [IO.File]::ReadAllText($slavePath)
$failures = [System.Collections.Generic.List[string]]::new()

function Require-Text([string]$name, [string]$text) {
    if (-not $source.Contains($text)) { $failures.Add("${name}: missing '$text'") }
}

function Require-Count([string]$name, [string]$text, [int]$expected) {
    $actual = ([regex]::Matches($source, [regex]::Escape($text))).Count
    if ($actual -ne $expected) {
        $failures.Add("${name}: expected $expected occurrences of '$text', found $actual")
    }
}

Require-Count 'both capture paths snapshot the target army before relocation' `
    'Army formerArmy = pTarget.army;' 2
Require-Count 'both capture paths snapshot the target kingdom before relocation' `
    'Kingdom formerMilitaryKingdom = pTarget.kingdom;' 2
Require-Count 'both capture paths release foreign military ownership' `
    'ReleaseCapturedForeignMilitaryOwnership(pTarget, formerArmy,' 2
Require-Text 'cleanup is gated by successful capture and kingdom change' `
    'CapturedMilitaryOwnershipRules.ShouldReleaseFormerArmy('
Require-Text 'captain continuity protection is opened for detachment' `
    'using (ArmyCaptainDisposalScope.Open(pFormerArmy))'
Require-Text 'former army membership is removed' 'pActor.removeFromArmy();'
Require-Text 'RTS ownership is released' 'ArmyRtsControllerService.ReleaseActor(pActor);'
Require-Text 'deployment ownership is released' `
    'ArmyDeploymentService.ReleaseActor(pActor, restoreJob: true);'
Require-Text 'wartime garrison ownership is released' `
    'WartimeGarrisonService.OnActorInvalidated(pActor);'
Require-Text 'strategic army index is refreshed' `
    'ArmyStrategicIndexService.OnArmyRosterChanged(pFormerArmy);'

if ($failures.Count -gt 0) {
    Write-Host "Captured slave military ownership failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Captured slave military ownership guard passed.'
