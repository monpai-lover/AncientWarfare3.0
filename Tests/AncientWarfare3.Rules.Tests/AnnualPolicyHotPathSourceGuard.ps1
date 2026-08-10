param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$court = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/court/CourtService.cs'))
$annual = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/court/CityBureauAnnualWorkService.cs'))
$runtime = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/policy/KingdomAnnualWorkService.cs'))
$failures = [System.Collections.Generic.List[string]]::new()

if (-not $court.Contains('CityBureauAnnualWorkService.Schedule(')) {
    $failures.Add('annual court work must schedule deferred city bureau slices')
}
if ($court.Contains('foreach (City city in pKingdom.getCities())') -or
    $annual.Contains('foreach (City city in pKingdom.getCities())')) {
    $failures.Add('annual policy hot path must not synchronously scan every city')
}
foreach ($required in @(
        'private const int CitiesPerSlice = 2;',
        'private const int MaximumWriteAttempts = 3;',
        'IEnumerator<City>',
        'RetryCityId',
        'DeferredWorkClass.Persistent',
        'HistoricalWriteService.TryUpsertState(')) {
    if (-not $annual.Contains($required)) {
        $failures.Add("city bureau slices are missing required boundary: $required")
    }
}
if (-not $annual.Contains('private static bool ProcessCity(') -or
    -not $annual.Contains('if (!ProcessCity(')) {
    $failures.Add('failed city bureau writes must remain in a bounded retry path')
}
foreach ($forbidden in @('DB.Insert(', 'DB.UpdateValue(',
        'DB.CheckKeyExist(', 'SQLiteCommand')) {
    if ($annual.Contains($forbidden)) {
        $failures.Add("city bureau slices must not synchronously use $forbidden")
    }
}
if (-not $runtime.Contains('CityBureauAnnualWorkService.ClearRuntime();')) {
    $failures.Add('world reset must clear pending annual city bureau slices')
}

if ($failures.Count -gt 0) {
    Write-Host "Annual policy hot-path failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Annual policy hot-path guard passed.'
