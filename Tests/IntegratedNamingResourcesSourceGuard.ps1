$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path (Split-Path -Parent $repoRoot) 'ChineseName_1.5.0new'
$sourceGenerators = Join-Path $sourceRoot 'name_generators\default'
$sourceWords = Join-Path $sourceRoot 'word_libraries\default'
$targetGenerators = Join-Path $repoRoot 'name_generators\default'
$targetWords = Join-Path $repoRoot 'word_libraries\default'

foreach ($directory in @($sourceGenerators, $sourceWords)) {
    if (-not (Test-Path -LiteralPath $directory)) {
        throw "Missing upstream Chinese Name resource directory: $directory"
    }
}

function Get-RelativeFileSet([string] $root, [string] $filter) {
    if (-not (Test-Path -LiteralPath $root)) { return @() }
    return @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter $filter |
        ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') } |
        Sort-Object -Unique)
}

$sourceGeneratorFiles = Get-RelativeFileSet $sourceGenerators '*.json'
$targetGeneratorFiles = Get-RelativeFileSet $targetGenerators '*.json'
$sourceWordFiles = Get-RelativeFileSet $sourceWords '*.txt'
$targetWordFiles = Get-RelativeFileSet $targetWords '*.txt'

foreach ($relative in $sourceGeneratorFiles) {
    if ($targetGeneratorFiles -notcontains $relative) {
        throw "Missing integrated generator resource: $relative"
    }
}
foreach ($relative in $sourceWordFiles) {
    if ($targetWordFiles -notcontains $relative) {
        throw "Missing integrated word-library resource: $relative"
    }
}

$requiredCategories = @(
    'alliances.json', 'books.json', 'cities.json', 'clans.json',
    'creatures.json', 'cultures.json', 'kingdoms.json', 'languages.json',
    'religions.json', 'subspecies.json', 'wars.json',
    'armor_and_accessory.json', 'weapons.json'
)
foreach ($category in $requiredCategories) {
    if ($targetGeneratorFiles -notcontains $category) {
        throw "Missing required integrated naming category: $category"
    }
}

$generatorCount = 0
$targetAssetsById = @{}
$referencedLibraries = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($relative in $targetGeneratorFiles) {
    $path = Join-Path $targetGenerators $relative
    try {
        $assets = Get-Content -LiteralPath $path -Raw -Encoding UTF8 |
            ConvertFrom-Json
    }
    catch {
        throw "Invalid integrated generator JSON '$relative': $($_.Exception.Message)"
    }
    foreach ($asset in $assets) {
        if ([string]::IsNullOrWhiteSpace([string]$asset.id)) {
            throw "Generator without id in $relative"
        }
        $targetAssetsById[[string]$asset.id] = $asset
        $generatorCount++
        $templates = @($asset.templates)
        if ($null -ne $asset.default_template) { $templates += $asset.default_template }
        foreach ($template in $templates) {
            $format = [string]$template.format
            foreach ($match in [regex]::Matches($format, '[\{<]([^\}:>]+)(?::[^\}>]+)?[\}>]')) {
                $library = $match.Groups[1].Value
                if ($library -notmatch '\$' -and -not [string]::IsNullOrWhiteSpace($library)) {
                    [void]$referencedLibraries.Add($library)
                }
            }
        }
    }
}
if ($generatorCount -lt 100) {
    throw "Integrated generator catalog is incomplete: only $generatorCount assets"
}

# AW3 may add generators, but the ids imported from ChineseName_1.5.0new are
# compatibility contracts. Existing saves and AW3 features already refer to
# their old behavior, so extensions must never rewrite those assets in place.
foreach ($relative in $sourceGeneratorFiles) {
    $sourcePath = Join-Path $sourceGenerators $relative
    $sourceAssets = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    foreach ($sourceAsset in $sourceAssets) {
        $id = [string]$sourceAsset.id
        if (-not $targetAssetsById.ContainsKey($id)) {
            throw "Missing upstream generator id: $id"
        }
        $sourceJson = $sourceAsset | ConvertTo-Json -Depth 32 -Compress
        $targetJson = $targetAssetsById[$id] |
            ConvertTo-Json -Depth 32 -Compress
        if ($sourceJson -cne $targetJson) {
            throw "Integrated generator '$id' rewrites upstream Chinese Name behavior; add a new id instead."
        }
    }
}

$wordIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$nonEmptyWordIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($relative in $targetWordFiles) {
    $path = Join-Path $targetWords $relative
    $lines = @(Get-Content -LiteralPath $path -Encoding UTF8 |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $wordId = [IO.Path]::GetFileNameWithoutExtension($relative)
    [void]$wordIds.Add($wordId)
    if ($lines.Count -gt 0) { [void]$nonEmptyWordIds.Add($wordId) }
}
function ConvertFrom-Utf8Base64([string] $value) {
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($value))
}
$legacyAliases = @{}
$legacyAliases[(ConvertFrom-Utf8Base64 '6Zi/5ouJ5Lyv5ZCN5a2X')] = @(
    (ConvertFrom-Utf8Base64 '6Zi/5ouJ5Lyv55S35ZCN'),
    (ConvertFrom-Utf8Base64 '6Zi/5ouJ5Lyv5aWz5ZCN'))
$legacyAliases[(ConvertFrom-Utf8Base64 '572X5pav5ZCN5a2X')] = @(
    (ConvertFrom-Utf8Base64 '572X5pav55S35ZCN'),
    (ConvertFrom-Utf8Base64 '572X5pav5aWz5ZCN'))
$legacyAliases[(ConvertFrom-Utf8Base64 '54q55aSq5Lq65ZCN')] = @(
    (ConvertFrom-Utf8Base64 '54q55aSq55S35ZCN'),
    (ConvertFrom-Utf8Base64 '54q55aSq5aWz5ZCN'))
foreach ($library in $referencedLibraries) {
    if ($legacyAliases.ContainsKey($library)) {
        foreach ($sourceId in $legacyAliases[$library]) {
            if (-not $nonEmptyWordIds.Contains($sourceId)) {
                throw "Legacy word-library alias '$library' has missing source: $sourceId"
            }
        }
        continue
    }
    if (-not $wordIds.Contains($library)) {
        throw "Generator template references missing word library: $library"
    }
    if (-not $nonEmptyWordIds.Contains($library)) {
        throw "Generator template references empty word library: $library"
    }
}

$licensePath = Join-Path $repoRoot 'THIRD_PARTY_NOTICES\ChineseName-MIT.txt'
if (-not (Test-Path -LiteralPath $licensePath)) {
    throw 'Missing Chinese Name MIT attribution file.'
}
$licenseText = Get-Content -LiteralPath $licensePath -Raw -Encoding UTF8
if ($licenseText -notmatch 'MIT License' -or
    $licenseText -notmatch 'Chinese') {
    throw 'Chinese Name attribution must identify the project and include the MIT license.'
}

$projectText = Get-Content -LiteralPath `
    (Join-Path $repoRoot 'AncientWarfare3.csproj') -Raw -Encoding UTF8
foreach ($resourceGlob in @(
    'name_generators\**\*',
    'word_libraries\**\*')) {
    if (-not $projectText.Contains("Content Include=`"$resourceGlob`"")) {
        throw "Project packaging does not include integrated naming resources: $resourceGlob"
    }
}

Write-Output ("Integrated naming resources source guard passed: " +
    "$generatorCount generators, $($targetWordFiles.Count) word libraries.")
