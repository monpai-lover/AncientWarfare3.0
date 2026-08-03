$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$chronicle = Get-Content -Raw (Join-Path $root 'Code/core/lineage/ChronicleEvents.cs')
$mandate = Get-Content -Raw (Join-Path $root 'Code/core/lineage/MandateService.cs')
$reigns = Get-Content -Raw (Join-Path $root 'Code/core/lineage/ReignRecordWriter.cs')
$facts = Get-Content -Raw (Join-Path $root 'Code/core/lineage/RulerTitleFactService.cs')
$posthumous = Get-Content -Raw (Join-Path $root 'Code/core/lineage/PosthumousTitleService.cs')

function Require([string]$content, [string]$needle, [string]$message) {
    if (-not $content.Contains($needle)) { throw $message }
}

Require $chronicle 'MandateService.OnRulerSucceeded(pKingdom, pNewKing)' `
    'normal accession does not settle the active Mandate ruler projection'
Require $chronicle 'isActiveMandate: MandateService.IsMandateKingdom(pKingdom)' `
    'active Mandate accession can still project a branch state name'
Require $mandate 'public static void OnRulerSucceeded' `
    'MandateService exposes no synchronous succession settlement boundary'
Require $mandate 'MandateSuccessionRules.ShouldRefreshRulerProjection' `
    'Mandate succession does not validate the installed live ruler'
Require $mandate 'UpsertState(pKingdom, report.period_id' `
    'Mandate succession does not refresh MandateState immediately'
Require $mandate 'MandateSuccessionRules.ShouldTransferRulerTrait' `
    'Mandate succession does not distinguish a real ruler transfer'
Require $mandate 'previousRuler.removeTrait(TRAIT_TIANMING)' `
    'the former emperor retains the Mandate trait after succession'
Require $mandate 'pNewKing.addTrait(TRAIT_TIANMING)' `
    'the installed emperor does not receive the Mandate trait'
Require $mandate 'KingdomTitleService.SetTitle(pKingdom, KingdomTitle.Emperor)' `
    'an active Mandate realm can remain downgraded after succession'
Require $mandate 'FamilyTreeProjectionChange.RankOrMandate' `
    'Mandate succession does not invalidate its display projection'
Require $mandate 'ReignRecordWriter.ProjectMandateContext' `
    'Mandate establishment does not persist imperial reign context'
Require $reigns 'public static void ProjectMandateContext' `
    'open reigns cannot persist a mid-reign Mandate promotion'
Require $facts 'RulerTitleFactRules.ResolveSavedHighestTitle' `
    'posthumous facts still derive rank from downgraded live state'
Require $posthumous 'RulerTitleFactRules.ResolveSavedHighestTitle' `
    'posthumous context can overwrite the saved imperial rank'

Write-Output 'Mandate succession runtime source guard passed.'
