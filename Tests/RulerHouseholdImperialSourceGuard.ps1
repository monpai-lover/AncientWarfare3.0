$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$query = [IO.File]::ReadAllText(
    (Join-Path $root 'Code\core\lineage\RulerHouseholdQuery.cs'),
    [Text.Encoding]::UTF8)
$service = [IO.File]::ReadAllText(
    (Join-Path $root 'Code\core\lineage\RulerHouseholdService.cs'),
    [Text.Encoding]::UTF8)
$failures = [Collections.Generic.List[string]]::new()

if (-not $query.Contains("IFNULL(STATUS,'') NOT IN ('slave','slave_lineage')")) {
    $failures.Add('Consort archive candidates must admit bounded non-slave commoners.')
}
if (-not $query.Contains('LIMIT @limit')) {
    $failures.Add('Household candidate queries must retain a hard SQL limit.')
}
if (-not $service.Contains('RulerHouseholdRankRules.ConsortScore(')) {
    $failures.Add('Consort ordering must use attribute-first scoring.')
}
foreach ($forbidden in @('World.world.units_only_alive', 'getSimpleList()')) {
    if ($query.Contains($forbidden) -or $service.Contains($forbidden)) {
        $failures.Add("Household candidates must not enumerate $forbidden.")
    }
}

if ($failures.Count -gt 0) {
    Write-Output "Imperial household source guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Output " - $failure" }
    exit 1
}

Write-Output 'Imperial household source guard passed.'
