$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$requirements = [ordered]@{
    'Locales/trait.csv' = @(
        'culture_trait_aw_xia_fully_integrated'
        'culture_trait_aw_xia_fully_integrated_info'
    )
    'Locales/aw3_mandate.csv' = @(
        'aw_grant_mandate_error_zhulu_unresolved'
    )
    'Locales/aw3_army_rts.csv' = @(
        'aw_army_rts_transport_awaiting_pickup'
        'aw_army_rts_transport_embarking'
        'aw_army_rts_transport_sailing'
        'aw_army_rts_transport_landing'
        'task_unit_aw_army_rts_transport_awaiting_pickup'
        'task_unit_aw_army_rts_transport_embarking'
        'task_unit_aw_army_rts_transport_sailing'
        'task_unit_aw_army_rts_transport_landing'
    )
    'Locales/others.csv' = @(
        ([string][char]0x4E00) + ([string][char]0x7C73) + '_' +
        ([string][char]0x4E2D) + ([string][char]0x6587) +
        ([string][char]0x540D)
    )
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($entry in $requirements.GetEnumerator()) {
    $path = Join-Path $repoRoot $entry.Key
    $rows = Import-Csv -LiteralPath $path -Encoding UTF8
    $keys = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($row in $rows) { [void]$keys.Add([string]$row.key) }
    foreach ($key in $entry.Value) {
        if (-not $keys.Contains($key)) {
            $failures.Add("$($entry.Key): missing localization key $key")
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Reported localization failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Reported localization coverage guard passed.'
