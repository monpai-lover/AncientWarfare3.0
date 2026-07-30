$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$rulePath = Join-Path $repo `
    'Code/core/lineage/LineageDisplayNameRules.cs'
if (-not [IO.File]::Exists($rulePath)) {
    throw 'LineageDisplayNameRules.cs is missing.'
}

Add-Type -Path $rulePath
$rules = [AncientWarfare3.core.lineage.LineageDisplayNameRules]

function Assert-Equal([string]$name, [string]$expected, [string]$actual) {
    if ($expected -ne $actual) {
        throw "$name expected '$expected' but got '$actual'"
    }
}

$given = [string][char]0x53D1
$family = [string][char]0x59EC
$clan = [string][char]0x5468
$specialName = [string][char]0x5B89 + [char]0x4E50 + [char]0x516C + [char]0x4E3B

Assert-Equal 'pre-integration noble woman keeps lineage surname' ($given + $family) `
    ($rules::Build($given, $family, $clan, $true, $false, $false))
Assert-Equal 'pre-integration noble woman falls back to Shi before given name' ($clan + $given) `
    ($rules::Build($given, '', $clan, $true, $false, $false))
Assert-Equal 'stored single given name is repaired through Shi fallback' ($clan + $given) `
    ($rules::ProjectStored($given, $given, '', $clan, $true, $false, $false))
Assert-Equal 'existing complete special name remains unchanged' $specialName `
    ($rules::ProjectStored($specialName, $given, '', $clan, $true, $false, $false))
Assert-Equal 'post-integration name uses Shi before given name' ($clan + $given) `
    ($rules::Build($given, $family, $clan, $true, $false, $true))

Write-Output 'Lineage display-name rule tests passed.'
