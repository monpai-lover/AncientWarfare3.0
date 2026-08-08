$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$visual = Get-Content -Raw (Join-Path $root `
    'Code\core\lineage\KingdomVisualRandomizationService.cs')
$cache = Get-Content -Raw (Join-Path $root `
    'Code\core\lineage\MetaColorCacheService.cs')
$patch = Get-Content -Raw (Join-Path $root `
    'Code\patch\AW_KingdomColorPatch.cs')
$chronicle = Get-Content -Raw (Join-Path $root `
    'Code\core\lineage\ChronicleEvents.cs')

function Require([string]$text, [string]$needle, [string]$message) {
    if (-not $text.Contains($needle)) { throw $message }
}

function Forbid([string]$text, [string]$needle, [string]$message) {
    if ($text.Contains($needle)) { throw $message }
}

Require $visual 'HashSet<int> usedColorIds = CollectUsedColorIds(pKingdom);' `
    'kingdom creation does not precompute used colors once'
Require $visual 'if (usedColorIds.Contains(i)) continue;' `
    'color candidate selection does not use constant-time membership checks'
Forbid $visual 'IsColorUsedByOtherMeta' `
    'color selection still rescans every kingdom and alliance per candidate'

Forbid $cache 'pKingdom.updateColor(' `
    'generated-color cache refresh recursively updates kingdom color'
Forbid $cache 'dirtyAndClear()' `
    'generated-color cache refresh still clears the full native map'

Forbid $patch 'nameof(Kingdom.updateColor)' `
    'generic color changes still synchronously invoke the archive patch'
Forbid $patch 'KingdomArchiveWriter.Upsert' `
    'generic color changes still synchronously write the kingdom archive'

$foundedStart = $chronicle.IndexOf(
    'public static void OnKingdomFounded(Kingdom pKingdom)')
$destroyedStart = $chronicle.IndexOf(
    'public static void OnKingdomDestroyed(Kingdom pKingdom)', $foundedStart)
if ($foundedStart -lt 0 -or $destroyedStart -le $foundedStart) {
    throw 'OnKingdomFounded body could not be located'
}
$foundedBody = $chronicle.Substring($foundedStart,
    $destroyedStart - $foundedStart)
Require $foundedBody 'KingdomArchiveWriter.Upsert(pKingdom);' `
    'kingdom founding no longer persists its single archive snapshot'

Write-Output 'Kingdom creation performance source guard passed.'
