$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string] $relativePath) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path $path)) { throw "Missing source: $relativePath" }
    return Get-Content -Raw $path
}

function Require([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
}

function Require-Contains([string] $source, [string] $token,
    [string] $message) {
    Require $source.Contains($token) $message
}

function Require-Before([string] $source, [string] $first,
    [string] $second, [string] $message) {
    $left = $source.IndexOf($first, [StringComparison]::Ordinal)
    $right = $source.IndexOf($second, [StringComparison]::Ordinal)
    Require ($left -ge 0 -and $right -ge 0 -and $left -lt $right) $message
}

$stronghold = Read-Source `
    'Code/core/lineage/PeasantRebelBanditStrongholdService.cs'
$guiyi = Read-Source 'Code/core/lineage/PeasantRebelGuiyiService.cs'
$route = Read-Source 'Code/core/lineage/PeasantRebelRouteService.cs'
$mandate = Read-Source 'Code/core/lineage/MandateRebelService.cs'
$power = Read-Source 'Code/content/GodPowerLibrary.cs'

Require-Contains $stronghold 'TryRedirectBanditFounder' `
    'Direct bandit creation has no claimant redirect.'
Require-Before $stronghold 'TryRedirectBanditFounder' 'makeNewCivKingdom' `
    'Claimant redirect must run before bandit kingdom creation.'
Require-Contains $stronghold 'restorationRedirected' `
    'Direct bandit creation does not expose the redirect outcome.'
Require-Contains $guiyi 'pAllowClaimRedirect: false' `
    'Guiyi creation must bypass the ordinary claimant redirect.'
Require-Contains $route 'TryRedirectBanditFounder' `
    'Bandit route selection has no claimant redirect.'
Require-Before $route 'TryRedirectBanditFounder' 'TryEnterBandit' `
    'Route redirect must precede bandit government entry.'
Require-Contains $mandate 'effectiveRebel' `
    'Mandate rebellion does not carry the effective restored kingdom.'
Require-Contains $power 'restorationRedirected' `
    'Bandit divine power does not report restoration redirects.'

Write-Output 'Restoration bandit redirect source guard passed.'
