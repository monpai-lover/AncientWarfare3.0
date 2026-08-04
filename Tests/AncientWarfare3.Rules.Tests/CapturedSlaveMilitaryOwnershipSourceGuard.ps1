param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$slavePath = Join-Path $root 'Code/core/lineage/SlaveService.cs'
$source = [IO.File]::ReadAllText($slavePath)
$reconciliationPath = Join-Path $root `
    'Code/core/lineage/ArmyMembershipReconciliationService.cs'
$cleanupSource = [IO.File]::ReadAllText($reconciliationPath)
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

function Require-CleanupText([string]$name, [string]$text) {
    if (-not $cleanupSource.Contains($text)) {
        $failures.Add("${name}: missing '$text'")
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
Require-Text 'captured ownership delegates to the shared reconciler' `
    'ArmyMembershipReconciliationService.ReleaseForeignMember('
Require-CleanupText 'captain continuity protection is opened for detachment' `
    'using (ArmyCaptainDisposalScope.Open(pArmy))'
Require-CleanupText 'former army membership is removed' 'pActor.removeFromArmy();'
Require-CleanupText 'RTS ownership is released' `
    'ArmyRtsControllerService.ReleaseActor(pActor);'
Require-CleanupText 'deployment ownership is released' `
    'ArmyDeploymentService.ReleaseActor(pActor, restoreJob: true);'
Require-CleanupText 'temporary levy ownership is released' `
    'TemporaryLevyService.OnActorInvalidated(pActor);'
Require-CleanupText 'wartime garrison ownership is released' `
    'WartimeGarrisonService.OnActorInvalidated(pActor);'
Require-Text 'strategic army index is refreshed' `
    'ArmyStrategicIndexService.OnArmyRosterChanged(pFormerArmy);'

if ($failures.Count -gt 0) {
    Write-Host "Captured slave military ownership failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Captured slave military ownership guard passed.'
