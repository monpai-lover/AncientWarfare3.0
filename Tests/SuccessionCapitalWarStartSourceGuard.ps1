$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$service = Get-Content (Join-Path $root 'Code/core/lineage/SuccessionDisputeService.cs') -Raw
$table = Get-Content (Join-Path $root 'Code/core/db/SuccessionDisputeTableItem.cs') -Raw
foreach ($field in @('OriginalCapitalCityIdAtWarStart','RivalCapitalCityIdAtWarStart')) {
    if ($service -notmatch [regex]::Escape($field)) { throw "missing snapshot field $field" }
}
if ($table -notmatch 'original_capital_city_id_at_war_start' -or
    $table -notmatch 'rival_capital_city_id_at_war_start') { throw 'missing persistence columns' }
if ($service -notmatch 'ReadValidCapitalId\(original\)' -or
    $service -notmatch 'ReadValidCapitalId\(rival\)') { throw 'war-start capture missing' }
Write-Output 'Succession war-start capital source guard passed.'
