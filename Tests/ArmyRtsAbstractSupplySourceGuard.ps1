$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$patchPath = Join-Path $root 'Code\patch\AW_RtsAbstractSupplyPatch.cs'

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

Require (Test-Path -LiteralPath $patchPath) `
    'RTS abstract supply must hook the vanilla hunger task assignment.'
$patch = Get-Content -Raw -LiteralPath $patchPath

Require $patch.Contains('AiSystemActor') `
    'Supply hook must intercept actor task assignment.'
Require $patch.Contains('try_to_eat_city_food') `
    'Supply hook must target only the vanilla city-food task.'
Require $patch.Contains('TryConsumeHomeRation') `
    'Supply hook must request real anchor-city supply.'
Require $patch.Contains('ShouldSuppressVanillaFoodTask') `
    'Supply hook must suppress only after a successful ration.'
Require $patch.Contains('return true') `
    'Supply failure must preserve vanilla hunger behavior.'
Require $patch.Contains('return false') `
    'Successful supply must suppress the vanilla eating task.'

Write-Output 'Army RTS abstract supply source guard passed.'
