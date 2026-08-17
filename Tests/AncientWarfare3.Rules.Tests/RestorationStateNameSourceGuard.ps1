$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
function Read-Source([string]$RelativePath) {
    return [IO.File]::ReadAllText((Join-Path $repo $RelativePath))
}
function Require([string]$Source, [string]$Needle, [string]$Message) {
    if ($Source.IndexOf($Needle, [StringComparison]::Ordinal) -lt 0) {
        throw $Message
    }
}

$continuity = Read-Source 'Code/core/lineage/KingdomIdentityContinuityService.cs'
$reconcile = $continuity.IndexOf(
    'ReconcileClaimantIdentity(pClaimant, pRequest);',
    [StringComparison]::Ordinal)
$prepare = $continuity.IndexOf(
    'PrepareClaimantLineage(pClaimant, pRequest);',
    [StringComparison]::Ordinal)
if ($reconcile -lt 0 -or $prepare -lt 0 -or $reconcile -gt $prepare) {
    throw 'Restoration must reconcile live claimant identity before writing request identity to the actor.'
}
Require $continuity 'LineageKeys.LINEAGE_ID' `
    'Restoration identity reconciliation must read the live lineage id.'
Require $continuity 'LineageKeys.SHI_ID' `
    'Restoration identity reconciliation must read the live Shi id.'
Require $continuity 'LineageKeys.CLAN_NAME' `
    'Restoration identity reconciliation must read the live clan name.'
Require $continuity 'ResolveRestorationRequestStateName(' `
    'Restoration identity reconciliation must clear stale branch state names.'
Require $continuity 'StateNameService.GetBoundStateName(' `
    'Restoration identity reconciliation must resolve the selected Shi binding.'

$autonomous = Read-Source 'Code/core/lineage/AutonomousRestorationService.cs'
$hosted = Read-Source 'Code/core/lineage/RoyalClaimService.cs'
Require $autonomous 'KingdomIdentityContinuityService.RestoreFromCity(' `
    'Autonomous restoration must use the shared identity restoration entry point.'
Require $hosted 'KingdomIdentityContinuityService.RestoreFromCity(' `
    'Hosted restoration must use the shared identity restoration entry point.'
$guiyiPath = Join-Path $repo `
    'Code/core/lineage/PeasantRebelGuiyiService.cs'
if (Test-Path -LiteralPath $guiyiPath) {
    $guiyi = [IO.File]::ReadAllText($guiyiPath)
    Require $guiyi 'RestoreFromCity(pMother, leader, request' `
        'Guiyi extinct-kingdom restoration must use the shared identity restoration entry point.'
    if ($guiyi.IndexOf('.setName(', [StringComparison]::Ordinal) -ge 0) {
        throw 'Guiyi city return must not rename an original kingdom that is still alive.'
    }
}

foreach ($path in @(
    'Code/core/lineage/CoupRestorationService.cs',
    'Code/core/lineage/FeudatoryJingnanService.cs',
    'Code/core/lineage/SuccessionDisputeService.cs')) {
    $source = Read-Source $path
    if ($source.IndexOf('KingdomIdentityContinuityService.RestoreFromCity(',
            [StringComparison]::Ordinal) -ge 0) {
        throw "$path must not rebuild or rename a still-living original kingdom."
    }
}

Write-Host 'Restoration state-name source guard passed.'
