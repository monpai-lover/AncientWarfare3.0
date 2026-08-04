$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$sourcePath = Join-Path $root 'Code\content\ClassicCivLifespanContent.cs'
$startupPath = Join-Path $root 'Code\content\XiaContent.cs'

if (-not (Test-Path $sourcePath)) {
    throw 'Missing classic civilization lifespan initializer.'
}

$source = Get-Content -Raw $sourcePath
$startup = Get-Content -Raw $startupPath

$targetBlock = [regex]::Match($source,
    'TargetRaceIds\s*=\s*\{(?<body>[\s\S]*?)\};')
if (-not $targetBlock.Success) {
    throw 'Cannot read the classic lifespan target race list.'
}
$targetIds = [regex]::Matches($targetBlock.Groups['body'].Value,
    '"(?<id>[^"]+)"') | ForEach-Object { $_.Groups['id'].Value }
$expectedIds = @('human', 'elf', 'dwarf')
if ($targetIds.Count -ne $expectedIds.Count) {
    throw 'Classic lifespan target list must contain exactly three races.'
}
foreach ($raceId in $expectedIds) {
    if ($targetIds -notcontains $raceId) {
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
if ($source -notmatch 'if\s*\(found\)\s*pActor\.genome_parts\.Remove\(existing\)' -or
    ([regex]::Matches($source,
        'new\s+GenomePart\("lifespan",\s*TargetLifespan\)').Count -ne 1)) {
    throw 'Repeated initialization must replace one existing lifespan part before adding one normalized value.'
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
