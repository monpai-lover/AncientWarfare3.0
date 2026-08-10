$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$taskPatch = [IO.File]::ReadAllText(
    (Join-Path $repoRoot 'Code/patch/AW_WartimeMilitaryTaskPatch.cs'),
    [Text.Encoding]::UTF8)
$ledger = [IO.File]::ReadAllText(
    (Join-Path $repoRoot 'Code/core/lineage/SyntheticMobilizationLedgerService.cs'),
    [Text.Encoding]::UTF8)

function Get-MethodBlock([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Could not locate '$signature'." }
    $open = $source.IndexOf('{', $start)
    $depth = 0
    for ($i = $open; $i -lt $source.Length; $i++) {
        if ($source[$i] -eq '{') { $depth++ }
        elseif ($source[$i] -eq '}') {
            $depth--
            if ($depth -eq 0) {
                return $source.Substring($start, $i - $start + 1)
            }
        }
    }
    throw "Could not locate closing brace for '$signature'."
}

$update = Get-MethodBlock $taskPatch 'private static bool Update_Prefix('
$failures = [Collections.Generic.List[string]]::new()
if ($taskPatch.Contains('SyntheticLevyService.IsSynthetic(')) {
    $failures.Add('AI job/task/update patches must not poll synthetic levy data')
}
if ($taskPatch.Contains('AW_WartimeMilitaryJobPatch')) {
    $failures.Add('synthetic levies must not install a global setJob prefix')
}
foreach ($required in @(
    'private static void ProcessDemobilization(',
    'pRecord.ActorIds',
    'SyntheticLevyService.RemoveWithoutPersonalHistory(actor)')) {
    if (-not $ledger.Contains($required)) {
        $failures.Add("war-ledger demobilization is missing '$required'")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Synthetic levy update-path guard failures: $($failures.Count)"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host 'Synthetic levy update-path guard passed.'
