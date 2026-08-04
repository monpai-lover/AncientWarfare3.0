$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$sourcePath = Join-Path $root 'Code\content\ClassicCivLifespanContent.cs'
$startupPath = Join-Path $root 'Code\content\XiaContent.cs'

if (-not (Test-Path $sourcePath)) {
    throw 'Missing classic civilization lifespan initializer.'
}

$source = Get-Content -Raw $sourcePath
$startup = Get-Content -Raw $startupPath

foreach ($raceId in @('human', 'elf', 'dwarf')) {
    if ($source -notmatch ('"' + $raceId + '"')) {
        throw "Missing lifespan target: $raceId"
    }
}

if ($source -notmatch 'TargetLifespan\s*=\s*70f') {
    throw 'Classic civilization lifespan must be normalized to 70f.'
}
if ($source -notmatch 'genome_parts' -or
    $source -notmatch 'new\s+GenomePart\("lifespan",\s*TargetLifespan\)') {
    throw 'Lifespan normalization must replace the genome lifespan part.'
}
if ($startup -notmatch 'XiaRace\.Init\(\);[\s\S]*ClassicCivLifespanContent\.Init\(\);') {
    throw 'Classic lifespan normalization must run after Xia race cloning.'
}

foreach ($forbidden in @('"orc"', '"Xia"', 'monkey', 'base_stats["lifespan"]')) {
    if ($source.Contains($forbidden)) {
        throw "Classic lifespan initializer must not modify: $forbidden"
    }
}

Write-Host 'Classic civilization lifespan source guard passed.'
