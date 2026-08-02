param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$lineageService = Join-Path $root 'Code/core/lineage/LineageService.cs'
$source = [IO.File]::ReadAllText($lineageService)
$required = @(
    'string inheritedOrExistingShi = CivMonkeyNamingRules.ResolveLineageSurname(',
    'existingShiId >= 0, existingClan, chineseFamily, existingFamily);')

if ($required.Where({ -not $source.Contains($_) }).Count -gt 0) {
    Write-Host 'Civ monkey clan surname failure: lineage initialization can reroll the ChineseName surname.'
    exit 1
}

$namingContent = [IO.File]::ReadAllText((Join-Path $root 'Code/content/CivMonkeyNamingContent.cs'))
$namingPatch = [IO.File]::ReadAllText((Join-Path $root 'Code/patch/AW_CivMonkeyNamingPatch.cs'))
$creationContract = @(
    'set.clan = CivMonkeyNamingRules.ClanGeneratorId;',
    'ParameterGetters.PutClanParameterGetter(',
    'MonkeyNameKind.Clan',
    'pType == MetaType.Clan')
if (-not $namingContent.Contains($creationContract[0]) -or
    -not $namingContent.Contains($creationContract[1]) -or
    -not $namingContent.Contains($creationContract[2]) -or
    -not $namingPatch.Contains($creationContract[3])) {
    Write-Host 'Civ monkey clan surname failure: clan creation still uses an independent monkey name.'
    exit 1
}

Write-Host 'Civ monkey clan surname guard passed.'
