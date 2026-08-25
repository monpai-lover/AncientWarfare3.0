$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$court = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\court\CourtService.cs')
$rules = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\court\WesternCourtElectionRules.cs')
$lineage = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\LineageService.cs')

if (-not $rules.Contains('public static bool CanUseLocalCandidate(')) {
    throw 'Western court local-candidate eligibility rule is missing.'
}
if (-not $court.Contains(
        'WesternCourtElectionRules.CanUseLocalCandidate(')) {
    throw 'Western court candidate boundary does not use the profile rule.'
}
if (-not $court.Contains(
        'LineageService.EnsureOfficialShiAndClan(pActor, pOfficeId);')) {
    throw 'Committed court appointments no longer enter lineage promotion.'
}

$westernPromotionStart = $lineage.IndexOf(
    'public static void EnsureOfficialShiAndClan(')
$westernPromotionEnd = $lineage.IndexOf(
    'public static void EnsureForeignPseudoDynastyLineage(',
    $westernPromotionStart)
if ($westernPromotionStart -lt 0 -or $westernPromotionEnd -le
    $westernPromotionStart) {
    throw 'Western official lineage promotion boundary cannot be located.'
}
$westernPromotion = $lineage.Substring($westernPromotionStart,
    $westernPromotionEnd - $westernPromotionStart)
if (-not $westernPromotion.Contains(
        'pNoble: SocialIdentityService.IsFormalNoble(pActor)') -or
    -not $westernPromotion.Contains('pOfficial: true')) {
    throw 'Western appointments must preserve only existing formal nobility.'
}

Write-Output 'Western court candidate eligibility source guard passed.'
