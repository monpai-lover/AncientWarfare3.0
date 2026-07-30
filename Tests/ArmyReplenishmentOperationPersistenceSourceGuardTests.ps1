param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $root $relativePath
    if (-not [System.IO.File]::Exists($path)) {
        $failures.Add("missing source file $relativePath")
        return ''
    }
    return [System.IO.File]::ReadAllText($path)
}

function Require([string]$source, [string]$needle, [string]$message) {
    if (-not $source.Contains($needle)) {
        $failures.Add("${message}: missing '$needle'")
    }
}

function Reject([string]$source, [string]$needle, [string]$message) {
    if ($source.Contains($needle)) {
        $failures.Add("${message}: found forbidden '$needle'")
    }
}

$keys = Read-Source 'Code/core/lineage/LineageKeys.cs'
$service = Read-Source `
    'Code/core/lineage/ArmyReplenishmentOperationService.cs'

Require $keys 'ARMY_REPLENISHMENT_OPERATION_VERSION' `
    'operation schema must persist on the army'
Require $keys 'ARMY_REPLENISHMENT_OPERATION_KINGDOM_ID' `
    'operation ownership must persist on the army'
Require $keys 'ARMY_REPLENISHMENT_OPERATION_SOURCE_CITY_ID' `
    'the selected reserve source must persist'
Require $keys 'ARMY_REPLENISHMENT_OPERATION_APPROVED_SHORTAGE' `
    'the immutable approval must persist'
Require $keys 'ARMY_REPLENISHMENT_OPERATION_ENLISTED' `
    'converted progress must persist'
Require $keys 'ARMY_REPLENISHMENT_OPERATION_START_TIME' `
    'the original start time must persist'
Require $keys 'ARMY_REPLENISHMENT_OPERATION_DEADLINE_TIME' `
    'the immutable deadline must persist'
Require $service 'internal static bool TryRead(' `
    'restore must validate persisted operation state'
Require $service 'internal static ArmyReplenishmentOperationState Ensure(' `
    'RTS callers need one idempotent operation entry point'
Require $service `
    'if (TryRead(pArmy, out ArmyReplenishmentOperationState existing))' `
    'repeated ensure calls must retain the first approval and times'
Require $service 'CultureInfo.InvariantCulture' `
    'double times must use invariant round-trip text'
Require $service 'ArmyReplenishmentOperationRules.ResolveDeadline(' `
    'restore cannot move a persisted deadline later'
Require $service 'internal static void Clear(' `
    'invalid or completed operations must clear every army key'
if ($failures.Count -gt 0) {
    Write-Host "Army replenishment persistence guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Army replenishment persistence source guard passed.'
