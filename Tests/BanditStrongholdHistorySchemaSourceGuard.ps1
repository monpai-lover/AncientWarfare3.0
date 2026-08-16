$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$cityHistory = Get-Content -Raw (Join-Path $root 'Code\core\db\CityHistoryTableItem.cs')
$indexes = Get-Content -Raw (Join-Path $root 'Code\core\db\LineageArchiveIndexRules.cs')
$manager = Get-Content -Raw (Join-Path $root 'Code\core\db\LineageArchiveManager.cs')
$service = Get-Content -Raw (Join-Path $root 'Code\core\lineage\PeasantRebelBanditStrongholdService.cs')

if ($cityHistory -notmatch 'public\s+string\s+projection_key\s*=\s*""') {
    throw 'CityHistory schema must declare PROJECTION_KEY for idempotent chronicles'
}
if ($indexes -notmatch 'uq_CityHistory_projection') {
    throw 'CityHistory must have a unique non-empty projection-key index'
}
if ($manager -notmatch 'AddMissingColumns\(tableName, upgradeColumns\)') {
    throw 'Loaded archives must add newly declared CityHistory columns'
}
if ($service.Contains('cannot record stronghold establishment')) {
    throw 'Chronicle failure must not roll back an otherwise valid stronghold'
}
if ($service -notmatch 'Stronghold establishment chronicle failed') {
    throw 'Non-fatal stronghold chronicle failure must be logged for repair'
}

Write-Output 'Bandit stronghold history schema source guard passed.'
