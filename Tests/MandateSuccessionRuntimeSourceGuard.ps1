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

function RequireOrder([string]$content, [string]$first, [string]$second,
    [string]$message) {
    $firstIndex = $content.IndexOf($first)
    $secondIndex = $content.IndexOf($second)
    if ($firstIndex -lt 0 -or $secondIndex -le $firstIndex) {
        throw $message
    }
}

Require $chronicle 'MandateService.OnRulerSucceeded(pKingdom, pNewKing)' `
    'normal accession does not settle the active Mandate ruler projection'
Require $chronicle 'isActiveMandate: MandateService.IsMandateKingdom(pKingdom)' `
    'active Mandate accession can still project a branch state name'
Require $mandate 'public static bool OnRulerSucceeded' `
    'MandateService exposes no synchronous succession settlement boundary'
Require $mandate 'MandateSuccessionPersistence.TryRefreshRuler' `
    'Mandate succession has no checked persistent ruler refresh'
RequireOrder $mandate 'MandateSuccessionPersistence.TryRefreshRuler' `
    'KingdomTitleService.SetTitle(pKingdom, KingdomTitle.Emperor)' `
    'Mandate runtime projection occurs before persistence succeeds'
Require $mandate 'MandateSuccessionRules.ShouldRefreshRulerProjection' `
    'Mandate succession does not validate the installed live ruler'
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
Require $reigns 'public static bool ProjectMandateContext' `
    'open reigns cannot persist a mid-reign Mandate promotion'
Require $reigns 'ReignMandateProjectionPersistence.TryProject' `
    'Mandate reign projection does not use the checked identity update'
Require $reigns 'pKing.data.id' `
    'Mandate reign projection is not bound to the installed ruler'
Require $mandate 'ProjectMandateContext(pKingdom, king, periodId)' `
    'Mandate establishment does not pass its confirmed ruler identity'
Require $facts 'RulerTitleFactRules.ResolveSavedHighestTitle' `
    'posthumous facts still derive rank from downgraded live state'
Require $posthumous 'RulerTitleFactRules.ResolveSavedHighestTitle' `
    'posthumous context can overwrite the saved imperial rank'

$sameKingStart = $chronicle.IndexOf('if (lastKingId == pNewKing.data.id)')
$sameKingEnd = $chronicle.IndexOf('RecordPreviousKingLostThrone',
    $sameKingStart)
if ($sameKingStart -lt 0 -or $sameKingEnd -le $sameKingStart -or
    -not $chronicle.Substring($sameKingStart,
        $sameKingEnd - $sameKingStart).Contains(
            'MandateService.OnRulerSucceeded(pKingdom, pNewKing)')) {
    throw 'same-ruler retry is blocked by the chronicle deduplication return'
}

$openReign = $chronicle.IndexOf(
    'ReignRecordWriter.OpenReign(pKingdom, pNewKing)')
$normalMandate = $chronicle.LastIndexOf(
    'MandateService.OnRulerSucceeded(pKingdom, pNewKing)')
if ($normalMandate -lt 0 -or $openReign -le $normalMandate) {
    throw 'normal Mandate settlement does not precede new-reign creation'
}

Write-Output 'Mandate succession runtime source guard passed.'
