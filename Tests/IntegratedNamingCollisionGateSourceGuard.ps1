$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$modClassPath = Join-Path $repoRoot 'Code\ModClass.cs'
$rulesPath = Join-Path $repoRoot 'Code\core\naming\AWNamingCollisionRules.cs'
$xiaPatchPath = Join-Path $repoRoot 'Code\patch\AW_XiaNamingPatch.cs'

$modText = Get-Content -LiteralPath $modClassPath -Raw -Encoding UTF8
$rulesText = Get-Content -LiteralPath $rulesPath -Raw -Encoding UTF8
$xiaPatchText = Get-Content -LiteralPath $xiaPatchPath -Raw -Encoding UTF8

function Require-Match {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

Require-Match $rulesText '\\u4e00\\u7c73_\\u4e2d\\u6587\\u540d' `
    'Collision rules must retain the exact external Chinese Name UID.'
Require-Match $modText 'using System\.Collections;' `
    'ModClass must use non-generic IDictionary for the internal NML registry.'
Require-Match $modText 'WorldBoxMod\.LoadedMods' `
    'Collision detection must inspect already loaded mods.'
Require-Match $modText 'GetField\(\s*"AllRecognizedMods"' `
    'Collision detection must reflect WorldBoxMod.AllRecognizedMods.'
Require-Match $modText 'BindingFlags\.Static\s*\|\s*BindingFlags\.NonPublic' `
    'AllRecognizedMods reflection must request the internal static field.'
Require-Match $modText '\bas IDictionary\b' `
    'AllRecognizedMods must be consumed through System.Collections.IDictionary.'
Require-Match $modText 'AWNamingCollisionRules\.IsRecognizedModConflict' `
    'Runtime collision detection must delegate state semantics to pure rules.'
Require-Match $modText 'AWNamingCollisionRules\.ShouldDisableIntegratedNamingPatches' `
    'Independent collision evidence must pass through the pure collision rule.'
Require-Match $modText `
    'bool loadedModsConflict\s*=\s*DetectLoadedExternalChineseNameConflict\(\)' `
    'LoadedMods collision evidence must be captured before registry reflection.'
Require-Match $modText `
    'TryDetectRecognizedExternalChineseNameConflict\(\s*out bool registryConflictDetected\)' `
    'Recognized-registry collision evidence must be scanned independently.'
Require-Match $modText `
    'ShouldDisableIntegratedNamingPatches\(\s*loadedModsConflict,\s*registryScanSucceeded,\s*registryConflictDetected\)' `
    'A known LoadedMods conflict must not be overwritten by registry scan failure.'
Require-Match $modText `
    'AWNamingCollisionRules\.ShouldSkipHarmonyPatch\(\s*type\.Namespace' `
    'Harmony registration must filter each patch type by its exact namespace.'
Require-Match $modText 'private const string NamingCollisionLogMessage\s*=' `
    'The conflict warning must be a fixed message.'
Require-Match $modText 'private static bool _namingCollisionLogWritten;' `
    'The conflict warning must have a process-wide once guard.'
Require-Match $xiaPatchText `
    'namespace\s+AncientWarfare3\.patch(?:\s|\{|;)' `
    'AW_XiaNamingPatch must remain outside the integrated naming namespace.'

$registryDetectorMatch = [regex]::Match(
    $modText,
    '(?s)private bool TryDetectRecognizedExternalChineseNameConflict\(.*?\r?\n\s*}\r?\n\s*\r?\n\s*private ')
if (-not $registryDetectorMatch.Success) {
    throw 'ModClass must isolate recognized-registry reflection in a non-throwing detector.'
}

$registryDetectorText = $registryDetectorMatch.Value
if ($registryDetectorText -match '(?:\.ToString\(\)|\.Message)') {
    throw 'Collision detection must not log exception details or declaration dumps.'
}
if ($registryDetectorText -notmatch `
    '(?s)catch\s*\([^)]*\)\s*\{.*?return false;') {
    throw 'Registry reflection failure must report scan failure without erasing LoadedMods evidence.'
}
if ($modText -match 'TryDetectExternalChineseNameConflict') {
    throw 'LoadedMods and recognized-registry evidence must not share the old combined detector.'
}

Write-Output 'Integrated naming collision gate source guard passed.'
