$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sourcePath = Join-Path $repoRoot "Code/content/XiaItems.cs"
$source = Get-Content -Raw $sourcePath

function Require-SourceText {
    param(
        [string]$Needle,
        [string]$Message
    )

    if (-not $source.Contains($Needle)) {
        throw $Message
    }
}

Require-SourceText `
    "PreloadGameplaySprites(ji);" `
    "ji must be loaded into the dynamic item atlas after late registration."
Require-SourceText `
    "PreloadGameplaySprites(ge);" `
    "ge must be loaded into the dynamic item atlas after late registration."
Require-SourceText `
    "DynamicSprites.preloadItemSprite(sprite, color);" `
    "colored late-registered weapons must preload every kingdom-color atlas variant."
Require-SourceText `
    "DynamicSprites.preloadItemSprite(sprite);" `
    "uncolored late-registered weapons must preload their base atlas sprite."

Write-Host "Xia item dynamic atlas source guard passed."
