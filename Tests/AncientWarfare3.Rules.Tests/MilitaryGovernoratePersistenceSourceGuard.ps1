$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$relation = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\db\VassalRelationTableItem.cs')
$indexes = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\db\LineageArchiveIndexRules.cs')
$keys = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\LineageKeys.cs')

if (-not $relation.Contains('public int subject_kind = 0;')) {
    throw 'Missing migration-safe VassalRelation subject_kind.'
}

$statePath = Join-Path $root `
    'Code\core\db\MilitaryGovernorateStateTableItem.cs'
if (-not (Test-Path -LiteralPath $statePath)) {
    throw 'Missing MilitaryGovernorateState table item.'
}
$state = Get-Content -Raw -LiteralPath $statePath
if (-not $state.Contains('[TableDef("MilitaryGovernorateState")]')) {
    throw 'Missing MilitaryGovernorateState table definition.'
}

foreach ($token in @(
    'idx_MilitaryGovernorateState_subject_active',
    'idx_MilitaryGovernorateState_suzerain_active',
    'idx_MilitaryGovernorateState_relation_active'
)) {
    if (-not $indexes.Contains($token)) {
        throw "Missing persistence index $token."
    }
}

foreach ($token in @(
    'MILITARY_GOVERNORATE_SUBJECT_KIND',
    'MILITARY_GOVERNORATE_STATE_ID'
)) {
    if (-not $keys.Contains($token)) {
        throw "Missing runtime projection key $token."
    }
}

$storePath = Join-Path $root `
    'Code\core\lineage\MilitaryGovernorateStore.cs'
if (-not (Test-Path -LiteralPath $storePath)) {
    throw 'Missing MilitaryGovernorateStore.'
}
$store = Get-Content -Raw -LiteralPath $storePath
foreach ($method in @(
    'TryCreate',
    'TryGetActive',
    'GetDirectActive',
    'SetSuccessor',
    'SetExpeditionaryArmy',
    'End',
    'RestoreProjection'
)) {
    if ($store -notmatch "\b$method\s*\(") {
        throw "Missing MilitaryGovernorateStore.$method."
    }
}
if (-not $store.Contains('Parameters.AddWithValue')) {
    throw 'MilitaryGovernorateStore must use parameterized SQLite commands.'
}

Write-Output 'Military governorate persistence source guard passed.'
