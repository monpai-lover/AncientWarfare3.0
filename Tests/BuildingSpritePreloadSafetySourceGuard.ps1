$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -Raw (Join-Path $root 'Code/patch/AW_BuildingSpritePatch.cs')

if (-not $source.Contains('loadBuildingSpriteList')) {
    throw 'building sprite list must be guarded at its load boundary'
}
if (-not $source.Contains('Array.Empty<Sprite>()')) {
    throw 'null building sprite lists must become empty arrays'
}
if (-not $source.Contains('HasIndexedBuildingSpriteName')) {
    throw 'malformed building sprite names must be filtered before vanilla iteration'
}
if (-not $source.Contains('ModClass.LogError')) {
    throw 'repaired building sprite lists must be logged as errors'
}
Write-Output 'PASS: building sprite preload safety source guard'
