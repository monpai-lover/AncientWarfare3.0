$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$definitions = Get-Content -Raw -Encoding UTF8 `
    (Join-Path $root 'Code/content/policies/KingdomPolicyDefs.cs')
$migration = Get-Content -Raw -Encoding UTF8 `
    (Join-Path $root 'Code/core/policy/KingdomPolicyProfileMigrationRules.cs')
$policyService = Get-Content -Raw -Encoding UTF8 `
    (Join-Path $root 'Code/core/policy/KingdomPolicyService.cs')
$xiaization = Get-Content -Raw -Encoding UTF8 `
    (Join-Path $root 'Code/core/lineage/XiaizationService.cs')
$policyAi = Get-Content -Raw -Encoding UTF8 `
    (Join-Path $root 'Code/core/policy/KingdomPolicyAI.cs')
$priority = Get-Content -Raw -Encoding UTF8 `
    (Join-Path $root 'Code/core/policy/KingdomDecisionPriorityRules.cs')
$locales = @(
    Get-Content -Raw -Encoding UTF8 `
        (Join-Path $root 'Locales/aw3_policy_ui.csv')
    Get-Content -Raw -Encoding UTF8 `
        (Join-Path $root 'Locales/aw3_policy_decisions.csv')
) -join "`n"

function Require-Text([string]$name, [string]$source, [string]$needle) {
    if (-not $source.Contains($needle)) {
        throw "$name missing: $needle"
    }
}

function Reject-Text([string]$name, [string]$source, [string]$needle) {
    if ($source.Contains($needle)) {
        throw "$name retains obsolete active reference: $needle"
    }
}

Require-Text 'definitions' $definitions 'Id = "aw_decision_appease_foreign_cities"'
Require-Text 'definitions' $definitions 'Id = "aw_west_decision_consolidate_royal_authority"'
Reject-Text 'active definitions' $definitions 'Id = "aw_decision_appease_xia_cities"'
Require-Text 'migration' $migration 'MapLegacyDecisionId'
Require-Text 'migration' $migration 'aw_decision_appease_xia_cities'
Require-Text 'migration' $migration 'aw_decision_appease_foreign_cities'
Require-Text 'queue migration' $policyService 'MigrateLegacyDecisionQueue'
Require-Text 'government state application' $policyService `
    'LineageKeys.POLICY_GOVERNMENT_STATE, pDef.GovernmentStateAfter'

foreach ($source in @($xiaization, $policyAi, $priority)) {
    Require-Text 'runtime decision consumer' $source `
        'aw_decision_appease_foreign_cities'
    Reject-Text 'runtime decision consumer' $source `
        'aw_decision_appease_xia_cities'
}

$localizedIds = @(
    'aw_west_tech_iron_casting',
    'aw_west_tech_coin_minting',
    'aw_west_tech_irrigation',
    'aw_west_tech_enfeoffment_study',
    'aw_west_tech_tax_office',
    'aw_west_tech_landlord_tax',
    'aw_west_tech_office_system',
    'aw_west_tech_elective_offices',
    'aw_west_tech_ritual_order',
    'aw_west_tech_feudal_retainers',
    'aw_west_tech_royal_domain',
    'aw_west_policy_landlord_taxation',
    'aw_west_policy_noble_council',
    'aw_west_policy_elective_offices',
    'aw_west_policy_feudal_retainers',
    'aw_west_policy_royal_direct_rule',
    'aw_decision_appease_foreign_cities',
    'aw_west_decision_consolidate_royal_authority'
)
foreach ($id in $localizedIds) {
    if ($locales -notmatch "(?m)^$([regex]::Escape($id)),[^,]+,[^,]+,[^,]+$") {
        throw "missing three-language localization row: $id"
    }
    if ($locales -notmatch "(?m)^$([regex]::Escape($id + '_desc')),[^,]+,[^,]+,[^,]+$") {
        throw "missing three-language description row: $id"
    }
}

Write-Output 'Western policy definition source guard passed.'
