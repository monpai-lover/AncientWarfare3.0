$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string] $relativePath) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path $path)) {
        throw "Missing source: $relativePath"
    }
    return Get-Content -Raw $path
}

function Require-Contains([string] $source, [string] $token,
    [string] $message) {
    if (-not $source.Contains($token)) {
        throw $message
    }
}

$autonomous = Read-Source `
    'Code/core/lineage/AutonomousRestorationService.cs'
$redirect = Read-Source `
    'Code/core/lineage/RestorationRebellionRedirectService.cs'
$keys = Read-Source 'Code/core/lineage/LineageKeys.cs'

Require-Contains $autonomous `
    'TryStartSelfRestorationFromExternalBandit' `
    'External-bandit restoration entry is missing.'
Require-Contains $autonomous `
    'RestorationRebellionSeedMode.ExternalBandit' `
    'External-bandit seed mode is not threaded into restoration.'
Require-Contains $autonomous `
    'ShouldCountSeedAsCore' `
    'External seed core accounting is missing.'
Require-Contains $autonomous `
    'SortCoreIdsByDistance' `
    'Nearest historical core ordering is missing.'
Require-Contains $autonomous `
    'RESTORATION_INITIALIZATION_PENDING' `
    'Committed initialization retry is missing.'
Require-Contains $redirect `
    'FindBestDormantClaimIdForActor' `
    'Bandit redirect does not use the bounded best-claim lookup.'
Require-Contains $redirect `
    'TryRedirectBanditFounder' `
    'Bandit-founder redirect entry is missing.'
Require-Contains $keys `
    'RESTORATION_INITIALIZATION_PENDING' `
    'Pending initialization persistence key is missing.'

Write-Output 'Restoration bandit external launch source guard passed.'
