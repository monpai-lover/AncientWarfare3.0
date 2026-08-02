$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string] $relative) {
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing source: $relative"
    }
    return Get-Content -Raw -LiteralPath $path
}

function Require-Tokens([string] $relative, [string[]] $tokens) {
    $source = Read-Source $relative
    foreach ($token in $tokens) {
        if (-not $source.Contains($token)) {
            throw "$relative does not carry required policy-profile token: $token"
        }
    }
}

Require-Tokens 'Code/core/db/KingdomPolicyStateTableItem.cs' @(
    'profile_id', 'government_state', 'migration_version',
    'obsolete_node_ids'
)
Require-Tokens 'Code/core/db/KingdomCourtStateTableItem.cs' @(
    'court_profile_id', 'institution_id'
)
Require-Tokens 'Code/core/lineage/LineageKeys.cs' @(
    'POLICY_PROFILE_ID', 'POLICY_GOVERNMENT_STATE',
    'POLICY_MIGRATION_VERSION', 'POLICY_OBSOLETE_NODE_IDS'
)

$service = Read-Source 'Code/core/policy/KingdomPolicyService.cs'
foreach ($token in @(
    'profile_id', 'government_state', 'migration_version',
    'obsolete_node_ids', 'KingdomPolicyProfileMigrationRules.Sanitize',
    'PROFILE_ID', 'GOVERNMENT_STATE', 'MIGRATION_VERSION',
    'OBSOLETE_NODE_IDS'
)) {
    if (-not $service.Contains($token)) {
        throw "KingdomPolicyService omits profile snapshot token: $token"
    }
}

if ($service -notmatch 'ApplySnapshot[\s\S]*KingdomPolicyProfileMigrationRules\.Sanitize') {
    throw 'ApplySnapshot does not sanitize profile-scoped active ids.'
}
if ($service -notmatch 'RestoreIdentityContinuity[\s\S]*PROFILE_ID[\s\S]*GOVERNMENT_STATE[\s\S]*MIGRATION_VERSION[\s\S]*OBSOLETE_NODE_IDS') {
    throw 'Identity continuity does not read policy-profile state.'
}
if ($service -notmatch 'UpsertSnapshot[\s\S]*PROFILE_ID[\s\S]*GOVERNMENT_STATE[\s\S]*MIGRATION_VERSION[\s\S]*OBSOLETE_NODE_IDS') {
    throw 'Policy upsert does not persist policy-profile state.'
}

Write-Output 'Western policy split migration source guard passed.'
