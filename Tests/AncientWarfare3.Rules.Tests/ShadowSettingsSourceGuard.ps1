$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$localePath = Join-Path $repo 'Locales/aw3_config.csv'
if (-not [IO.File]::Exists($localePath)) {
    throw 'Locale file is missing: Locales/aw3_config.csv'
}
$rows = @(Import-Csv -Encoding UTF8 $localePath)
$shadow = $rows | Where-Object {
    $_.key -eq 'AW3_ENABLE_ASYNC_SHADOW_CHECKS Description'
}
if ($null -eq $shadow) {
    throw 'Shadow description is missing from aw3_config.csv'
}
foreach ($language in @('cz', 'en', 'ch')) {
    if ([string]::IsNullOrWhiteSpace([string]$shadow.$language)) {
        throw "Shadow description is missing for language: $language"
    }
}
$requiredWords = @{
    cz = @(
        [string]([char]0x8BCA + [char]0x65AD),
        [string]([char]0x5F00 + [char]0x542F),
        [string]([char]0x5173 + [char]0x95ED))
    en = @('diagnostics', 'overhead', 'Keep it off')
    ch = @(
        [string]([char]0x8A3A + [char]0x65B7),
        [string]([char]0x958B + [char]0x555F),
        [string]([char]0x95DC + [char]0x9589))
}
foreach ($language in $requiredWords.Keys) {
    foreach ($word in $requiredWords[$language]) {
        if (-not ([string]$shadow.$language).Contains($word)) {
            throw "Shadow description lacks '$word': $language"
        }
    }
}

$configPath = Join-Path $repo 'default_config.json'
$config = Get-Content -Raw -Encoding UTF8 $configPath | ConvertFrom-Json
$setting = $config.AWPerformanceSettings |
    Where-Object { $_.Id -eq 'AW3_ENABLE_ASYNC_SHADOW_CHECKS' }
if ($null -eq $setting -or $setting.BoolVal -ne $false) {
    throw 'Shadow checks must remain disabled by default.'
}

Write-Output 'Shadow settings source guard passed.'
