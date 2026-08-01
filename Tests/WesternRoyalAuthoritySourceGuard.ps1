$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$keys = Get-Content -Raw (Join-Path $root 'Code/core/lineage/LineageKeys.cs')
$table = Get-Content -Raw (Join-Path $root 'Code/core/db/KingdomPolicyStateTableItem.cs')
$policy = Get-Content -Raw (Join-Path $root 'Code/core/policy/KingdomPolicyService.cs')
$effects = Get-Content -Raw (Join-Path $root 'Code/core/policy/KingdomPolicyEffectService.cs')
$inheritance = Get-Content -Raw (Join-Path $root 'Code/core/lineage/InheritanceLawService.cs')
$restoration = Get-Content -Raw (Join-Path $root 'Code/core/policy/RestorationInstitutionRules.cs')

function Require([string]$content, [string]$needle, [string]$message) {
    if (-not $content.Contains($needle)) { throw $message }
}

Require $keys 'WESTERN_ROYAL_AUTHORITY' `
    'missing persisted western royal-authority key'
Require $keys 'INHERITANCE_INSTITUTIONAL_AUTHORITY_BONUS' `
    'missing inheritance authority diagnostic key'
Require $table 'royal_authority' `
    'KingdomPolicyState does not persist royal authority'
Require $policy 'case "aw_west_decision_consolidate_royal_authority":' `
    'consolidate-royal-authority still falls through the default effect branch'
Require $policy 'KingdomPolicyEffectService.ApplyRoyalAuthorityDecision' `
    'decision completion does not call the authority effect service'
Require $policy 'snapshot.royal_authority' `
    'policy snapshots do not carry royal authority'
Require $policy 'ColumnVal.Create("ROYAL_AUTHORITY"' `
    'policy persistence does not write royal authority'
Require $effects 'WesternRoyalAuthorityRules.ApplyConsolidation' `
    'runtime effect does not use the tested consolidation rule'
Require $effects 'WesternRoyalAuthorityRules.ResolveSuccessionBonus' `
    'runtime effect does not resolve active-profile succession authority'
Require $inheritance 'KingdomPolicyEffectService.ReadSuccessionAuthorityBonus' `
    'inheritance evaluation does not consume western authority'
Require $inheritance 'WesternRoyalAuthorityRules.ApplyToCourtInfluence' `
    'inheritance does not combine raw and institutional influence through the tested rule'
Require $inheritance 'INHERITANCE_INSTITUTIONAL_AUTHORITY_BONUS' `
    'inheritance UI state cannot expose the institutional authority contribution'
Require $restoration 'royalAuthority' `
    'restoration continuity drops royal authority'

Write-Output 'Western royal authority source guard passed.'
