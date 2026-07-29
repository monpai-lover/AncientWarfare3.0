$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$rulePath = Join-Path $repo `
    'Code\core\lineage\EmptyCitySurvivalRules.cs'
if (-not [IO.File]::Exists($rulePath)) {
    throw 'EmptyCitySurvivalRules.cs is missing.'
}

$source = [IO.File]::ReadAllText($rulePath)
Add-Type -TypeDefinition $source -Language CSharp
$rules = [AncientWarfare3.core.lineage.EmptyCitySurvivalRules]

function Assert-Equal([string]$name, [bool]$expected, [bool]$actual) {
    if ($expected -ne $actual) {
        throw "$name expected $expected but got $actual"
    }
}

Assert-Equal 'a natural empty city keeps its Zones' $true `
    ($rules::ShouldSuppressNaturalBorderShrink(
        $true, $false, $true, 4, $false, $false))
Assert-Equal 'a populated city uses vanilla border behavior' $false `
    ($rules::ShouldSuppressNaturalBorderShrink(
        $true, $false, $true, 4, $true, $false))
Assert-Equal 'a Zone-less city remains removable' $false `
    ($rules::ShouldSuppressNaturalBorderShrink(
        $true, $false, $true, 0, $false, $false))
Assert-Equal 'a rekt city remains removable' $false `
    ($rules::ShouldSuppressNaturalBorderShrink(
        $true, $true, $true, 4, $false, $false))
Assert-Equal 'a city with an invalid owner remains repairable' $false `
    ($rules::ShouldSuppressNaturalBorderShrink(
        $true, $false, $false, 4, $false, $false))
Assert-Equal 'xenophobic razing keeps vanilla shrink enabled' $false `
    ($rules::ShouldSuppressNaturalBorderShrink(
        $true, $false, $true, 4, $false, $true))

Assert-Equal 'the exact vanilla raze branch records intent' $true `
    ($rules::ShouldRecordXenophobicRazeIntent(
        $true, $true, $true, $true, $false))
Assert-Equal 'a defender still inside prevents raze intent' $false `
    ($rules::ShouldRecordXenophobicRazeIntent(
        $true, $true, $true, $true, $true))
Assert-Equal 'same-species occupation is not razing' $false `
    ($rules::ShouldRecordXenophobicRazeIntent(
        $true, $true, $true, $false, $false))

Assert-Equal 'a new resident clears raze intent' $true `
    ($rules::ShouldClearRazeIntent($true, $false, $false, $false))
Assert-Equal 'a non-neutral takeover clears raze intent' $true `
    ($rules::ShouldClearRazeIntent($false, $true, $false, $false))
Assert-Equal 'natural neutralization preserves raze intent' $false `
    ($rules::ShouldClearRazeIntent($false, $true, $true, $false))
Assert-Equal 'load restoration preserves persisted intent' $false `
    ($rules::ShouldClearRazeIntent($false, $true, $false, $true))

Assert-Equal 'a live city suppresses automatic abandoned-zone cleanup' $true `
    ($rules::ShouldSuppressAutomaticAbandonedZoneCleanup(
        $true, $false, 4))
Assert-Equal 'an invalid city permits automatic abandoned-zone cleanup' $false `
    ($rules::ShouldSuppressAutomaticAbandonedZoneCleanup(
        $false, $false, 4))
Assert-Equal 'a destroyed city permits automatic abandoned-zone cleanup' $false `
    ($rules::ShouldSuppressAutomaticAbandonedZoneCleanup(
        $true, $true, 4))
Assert-Equal 'a zoneless city permits automatic abandoned-zone cleanup' $false `
    ($rules::ShouldSuppressAutomaticAbandonedZoneCleanup(
        $true, $false, 0))

Write-Output 'Empty city survival rule tests passed.'
