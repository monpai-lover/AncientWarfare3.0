$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$window = Get-Content -Raw (Join-Path $root 'Code/ui/windows/CourtWindow.cs')
$kingdom = Get-Content -Raw (Join-Path $root 'Code/patch/AW_KingdomTabPatch.cs')
$locale = Get-Content -Raw (Join-Path $root 'Locales/aw3_court.csv')

foreach ($needle in @('WideWindowChrome.Attach',
                      'aw_court_title_western',
                      'aw_court_layer_central',
                      'aw_court_layer_military',
                      'aw_court_layer_city')) {
    if (-not $window.Contains($needle)) { throw "missing western court UI contract: $needle" }
}
foreach ($needle in @('AW_KingdomTitleHoldersButton', 'AW_KingdomAtlasButton')) {
    if (-not $kingdom.Contains($needle)) { throw "missing kingdom court sidebar contract: $needle" }
}
foreach ($key in @('aw_court_title_western',
                   'aw_court_tier_western_bureaucratic',
                   'aw_court_tier_western_feudal_bureaucratic',
                   'aw_court_office_west_executive',
                   'aw_court_office_west_mayor',
                   'aw_court_office_west_royal_constable',
                   'aw_court_office_west_count',
                   'aw_court_no_officer')) {
    if (-not $locale.Contains($key + ',')) { throw "missing court locale key: $key" }
}
$rows = $locale -split "`r?`n" | Where-Object { $_ -and -not $_.StartsWith('#') }
foreach ($row in $rows) {
    $columns = $row -split ',', 4
    if ($columns.Count -ge 4 -and ($columns[1] -eq '' -or $columns[2] -eq '' -or $columns[3] -eq '')) {
        throw "court locale row is missing one of the three language columns: $row"
    }
}
Write-Output 'western court UI source guard passed'
