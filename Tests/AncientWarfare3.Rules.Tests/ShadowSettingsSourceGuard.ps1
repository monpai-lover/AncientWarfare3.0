$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$locales = @{
    'Locales/cz.json' = @('Shadow', '\u8bca', '\u5f00', '\u5173')
    'Locales/en.json' = @('diagnostics', 'overhead', 'Keep it off')
    'Locales/ch.json' = @('Shadow', '\u8a3a', '\u958b', '\u95dc')
}

foreach ($entry in $locales.GetEnumerator()) {
    $path = Join-Path $repo $entry.Key
    if (-not [IO.File]::Exists($path)) {
        throw "Locale file is missing: $($entry.Key)"
    }
    $json = Get-Content -Raw -Encoding UTF8 $path | ConvertFrom-Json
    $property = $json.PSObject.Properties |
        Where-Object { $_.Name -eq 'AW3_ENABLE_ASYNC_SHADOW_CHECKS Description' }
    if ($null -eq $property) {
        throw "Shadow description is missing: $($entry.Key)"
    }
    foreach ($word in $entry.Value) {
        if (-not [regex]::IsMatch([string]$property.Value, $word,
                [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            throw "Shadow description lacks '$word': $($entry.Key)"
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
