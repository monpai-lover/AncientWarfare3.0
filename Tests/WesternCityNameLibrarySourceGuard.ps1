$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
function U([int[]] $codePoints) {
    return -join @($codePoints | ForEach-Object { [char]$_ })
}

$westernCityStem = 'CK3' + (U @(0x897F, 0x65B9, 0x57CE, 0x540D))
$libraryRelative = 'word_libraries/default/' + $westernCityStem + '.txt'
$libraryPath = Join-Path $repoRoot $libraryRelative
if (-not (Test-Path -LiteralPath $libraryPath)) {
    throw "Missing CK3 western city-name library: $libraryRelative"
}

$words = @(Get-Content -LiteralPath $libraryPath -Encoding UTF8 |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($words.Count -lt 15000) {
    throw "CK3 western city-name library is unexpectedly small: $($words.Count)"
}
if (@($words | Sort-Object -Unique).Count -ne $words.Count) {
    throw 'CK3 western city-name library must be duplicate-free.'
}
if (@($words | Where-Object { $_ -match '[\$\[\]#{}]' }).Count -ne 0) {
    throw 'CK3 western city-name library contains localization markup.'
}
foreach ($expected in @(
    (U @(0x4E9A, 0x741B)),
    (U @(0x5DF4, 0x9ECE)),
    (U @(0x4F26, 0x6566)),
    (U @(0x7F57, 0x9A6C))
)) {
    if ($words -notcontains $expected) {
        throw "CK3 western city-name library is missing expected place: $expected"
    }
}

$citiesPath = Join-Path $repoRoot 'name_generators/default/cities.json'
$parsedGenerators = Get-Content -LiteralPath $citiesPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
$generators = @($parsedGenerators)
$western = @($generators | Where-Object { $_.id -eq 'western_city' })
if ($western.Count -ne 1) {
    throw 'western_city must be defined exactly once.'
}
$westernText = ($western[0]) | ConvertTo-Json -Depth 10
if ($westernText -notmatch ([regex]::Escape('{' + $westernCityStem + '}'))) {
    throw 'western_city does not use the CK3 western city-name library.'
}
foreach ($legacyPart in @(
    (U @(0x897F, 0x65B9, 0x57CE, 0x540D, 0x4E0A)),
    (U @(0x897F, 0x65B9, 0x57CE, 0x540D, 0x4E0B)),
    (U @(0x897F, 0x65B9, 0x57CE, 0x540D, 0x540E, 0x7F00))
)) {
    if ($westernText -match ([regex]::Escape($legacyPart))) {
        throw "western_city still composes the legacy library: $legacyPart"
    }
}

$otherText = @($generators | Where-Object { $_.id -ne 'western_city' } |
    ForEach-Object { $_ | ConvertTo-Json -Depth 10 }) -join "`n"
if ($otherText -match ([regex]::Escape($westernCityStem))) {
    throw 'The CK3 western city-name library leaked into another city profile.'
}
foreach ($expectation in @(
    @{ Id = 'Xia_city'; Marker = (U @(0x771F, 0x5B9E, 0x57CE, 0x540D)) },
    @{ Id = 'orc_nomadic_city'; Marker =
        (U @(0x6E38, 0x7267, 0x57CE, 0x540D)) }
)) {
    $generator = @($generators | Where-Object {
        $_.id -eq $expectation.Id })
    if ($generator.Count -ne 1 -or
        ((($generator[0]) | ConvertTo-Json -Depth 10) -notmatch
            ([regex]::Escape($expectation.Marker)))) {
        throw "$($expectation.Id) no longer uses $($expectation.Marker)."
    }
}

$monkeyGeneratorPath = Join-Path $repoRoot 'Code/content/CivMonkeyNamingContent.cs'
$monkeyGeneratorText = Get-Content -LiteralPath $monkeyGeneratorPath -Raw `
    -Encoding UTF8
$monkeyMarker = U @(0x7334, 0x65CF, 0x57CE, 0x5E02)
if ($monkeyGeneratorText -notmatch 'CityLibraryId\s*=\s*"' -or
    $monkeyGeneratorText -notmatch ([regex]::Escape($monkeyMarker))) {
    throw 'The dedicated monkey city generator no longer uses its own library.'
}
if ($monkeyGeneratorText -match ([regex]::Escape($westernCityStem))) {
    throw 'The CK3 western city-name library leaked into monkey naming.'
}

Write-Output 'Western CK3 city-name library source guard passed.'
