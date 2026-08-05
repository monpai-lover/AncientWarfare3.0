$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-Source([string] $relative) {
    $path = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing western lineage naming source: $relative"
    }
    return Get-Content -LiteralPath $path -Raw -Encoding UTF8
}

function Require-Match([string] $text, [string] $pattern,
    [string] $message) {
    if ($text -notmatch $pattern) { throw $message }
}

$keys = Read-Source 'Code/core/lineage/LineageKeys.cs'
$rules = Read-Source 'Code/core/naming/AWCultureNamingTraditionRules.cs'
$service = Read-Source 'Code/core/naming/AWCultureNamingTraditionService.cs'
$localized = Read-Source 'Code/core/naming/AWLocalizedNameService.cs'
$orcRules = Read-Source 'Code/core/naming/AWOrcNomadicNamingRules.cs'
$patch = Read-Source 'Code/patch/naming/AW_CultureNamingTraditionPatch.cs'
$xiaRepair = Read-Source 'Code/content/XiaNamingRepair.cs'
$xiaTraits = Read-Source 'Code/content/XiaCultureTraits.cs'
$xiaIntegration = Read-Source `
    'Code/core/lineage/XiaCultureIntegrationService.cs'
$xiaization = Read-Source 'Code/core/lineage/XiaizationService.cs'
$xiaPatch = Read-Source 'Code/patch/AW_XiaNamingPatch.cs'

foreach ($token in @('NAMING_PROFILE', 'WESTERN_NAMING_TRADITION',
    'CULTURE_PARENT_ID')) {
    Require-Match $keys ("\b" + $token + "\b") `
        "LineageKeys is missing persisted naming key: $token"
}

Require-Match $service 'AWNamingProfileRules\.Resolve\s*\(' `
    'The culture tradition service does not call AWNamingProfileRules in production.'
Require-Match $service 'AWCultureNamingTraditionRules\.' `
    'The culture tradition service does not use the pure persistence rules.'
Require-Match $service `
    'XiaCultureIntegrationService\.IsFullyIntegrated\s*\(' `
    'Naming must retain the persisted full-entry marker as a personal-name input.'
Require-Match $service `
    'XiaCultureIntegrationService\.IsIntegrated\s*\(' `
    'Integrated cultures must switch personal naming before full institutional entry.'
$migrationRules = Read-Source `
    'Code/core/lineage/IntegratedCultureNamingMigrationRules.cs'
Require-Match $migrationRules `
    'ShouldUseXiaPersonalNaming\s*\(' `
    'Personal naming authority must be centralized in the culture migration rules.'
Require-Match $service 'ResolveForActor\s*\(\s*Actor' `
    'The culture tradition service is missing ResolveForActor(Actor).'
Require-Match $localized 'AWCultureNamingTraditionService\.ResolveForActor\s*\(' `
    'The localized-name authority path does not resolve the persisted actor profile.'
Require-Match $localized 'AWCultureNamingTraditionRules\.ResolveGeneratorId\s*\(' `
    'The localized-name authority path does not route through profile rules.'
Require-Match $rules 'AWOrcNomadicNamingRules\.ResolveGeneratorId\s*\(' `
    'The production routing rules do not call the orc nomadic profile.'
foreach ($kind in @('Actor', 'Alliance', 'Book', 'City', 'Clan', 'Culture',
    'Item', 'Kingdom', 'Language', 'Religion', 'Subspecies', 'War')) {
    Require-Match $localized ("AWNamingObjectKind\." + $kind + "\b") `
        "The production naming matrix is missing category: $kind"
}

Require-Match $patch 'HarmonyPrefix' `
    'Culture creation does not capture the parent culture before mutation.'
Require-Match $patch 'pActor\?\.culture' `
    'Culture creation does not capture the founder culture in O(1).'
Require-Match $patch 'AWCultureNamingTraditionService\.Inherit\s*\(' `
    'Culture creation does not inherit the persisted naming tradition.'
Require-Match $patch 'AWCultureNamingTraditionService\.Ensure\s*\(' `
    'Culture creation does not ensure a naming tradition without a parent.'
if ($patch -match 'World\.world\.cultures|foreach\s*\(\s*Culture') {
    throw 'Culture creation naming persistence must not scan all cultures.'
}

Require-Match $xiaTraits '\bFullyIntegratedTraitId\b' `
    'The full Xia entry marker needs its own persisted culture trait id.'
Require-Match $xiaIntegration `
    'bool\s+IsFullyIntegrated\s*\(\s*Culture' `
    'The culture integration service cannot query full Xia entry.'
Require-Match $xiaIntegration `
    'bool\s+MarkFullyIntegrated\s*\(\s*Culture' `
    'The culture integration service cannot persist full Xia entry.'
Require-Match $xiaIntegration `
    'InheritFullyIntegrated\s*\(\s*Culture\s+pChild,\s*Culture\s+pParent' `
    'A split culture cannot inherit its parent full-entry marker.'
Require-Match $xiaization `
    'ShouldMarkCultureFullyIntegrated\s*\(' `
    'Xiaization level transitions do not distinguish level five full entry.'
Require-Match $xiaization `
    ('RestorePersistedCultureMarker\s*\(\s*LevelXiaizedDynasty,\s*' +
     'XiaCultureIntegrationService\.MarkFullyIntegrated\s*\)') `
    'Old saves do not restore the full-entry marker from level five state.'
Require-Match $xiaPatch `
    'XiaCultureIntegrationService\.InheritFullyIntegrated\s*\(' `
    'Culture creation does not inherit the full-entry marker.'

Require-Match $xiaRepair `
    'TryRenameKingdom\s*\([^)]*\)\s*\{(?s:.*?)IsCivilizedMonkeyKingdom\s*\(' `
    'Xia kingdom repair can overwrite the dedicated monkey kingdom generator.'
Require-Match $xiaRepair `
    'TryApplyFullyXiaizedKingdomName\s*\([^)]*\)\s*\{(?s:.*?)IsCivilizedMonkeyKingdom\s*\(' `
    'Full-Xia naming repair can overwrite a dedicated monkey kingdom name.'
Require-Match $xiaRepair 'CivMonkeyNamingRules\.IsCivilizedMonkey\s*\(' `
    'The Xia naming repair monkey gate does not use the canonical species rule.'

$resourceExpectations = @{
    'creatures.json' = @('western_von_name', 'western_de_name',
        'western_van_name', 'western_di_name', 'orc_nomadic_name',
        'elf_given_name', 'dwarf_given_name')
    'alliances.json' = @('western_alliance', 'orc_nomadic_alliance')
    'cities.json' = @('western_city', 'orc_nomadic_city')
    'clans.json' = @('western_clan', 'orc_nomadic_clan')
    'cultures.json' = @('western_culture', 'orc_nomadic_culture')
    'kingdoms.json' = @('western_kingdom', 'orc_nomadic_kingdom')
    'languages.json' = @('western_language', 'orc_nomadic_language')
    'religions.json' = @('western_religion', 'orc_nomadic_religion')
    'subspecies.json' = @('western_subspecies', 'orc_nomadic_subspecies')
}
foreach ($entry in $resourceExpectations.GetEnumerator()) {
    $relative = Join-Path 'name_generators/default' $entry.Key
    $json = Read-Source $relative | ConvertFrom-Json
    $ids = @($json | ForEach-Object { [string]$_.id })
    foreach ($id in $entry.Value) {
        if ($ids -notcontains $id) {
            throw "$relative is missing profile generator: $id"
        }
    }
}

$creatureGenerators = Read-Source `
    'name_generators/default/creatures.json' | ConvertFrom-Json
foreach ($expectation in @(
    @{ Id = 'elf_given_name'; Library = ([string][char]0x7CBE) +
        ([string][char]0x7075) + ([string][char]0x540D) +
        ([string][char]0x5B57) },
    @{ Id = 'dwarf_given_name'; Library = ([string][char]0x77EE) +
        ([string][char]0x4EBA) + ([string][char]0x540D) +
        ([string][char]0x5B57) }
)) {
    $generators = @($creatureGenerators |
        Where-Object { $_.id -eq $expectation.Id })
    if ($generators.Count -ne 1) {
        throw "$($expectation.Id) must be defined exactly once."
    }
    $templateText = $generators[0] | ConvertTo-Json -Depth 10
    if ($templateText -notmatch [regex]::Escape($expectation.Library) -or
        $templateText -notmatch ':given_name') {
        throw "$($expectation.Id) must use its species given-name library."
    }
    if ($templateText -match 'family_name|middle_name') {
        throw "$($expectation.Id) must not generate a family or middle name."
    }
}

foreach ($id in @(
    'western_von_name',
    'western_de_name',
    'western_van_name',
    'western_di_name',
    'elf_given_name',
    'dwarf_given_name',
    'orc_nomadic_name'
)) {
    $generator = @($creatureGenerators |
        Where-Object { $_.id -eq $id })
    if ($generator.Count -ne 1) {
        throw "$id must be defined exactly once."
    }
    $templates = @($generator[0].templates)
    if ($null -ne $generator[0].default_template) {
        $templates += $generator[0].default_template
    }
    foreach ($template in $templates) {
        $format = [string]$template.format
        $givenTokenCount = [regex]::Matches($format,
            ':given_name[}>]').Count
        if ($givenTokenCount -ne 1 -or
            $format -match ':(?:family|middle)_name[}>]') {
            throw "$id must generate one given name without family or middle-name components."
        }
    }
}

$nomadicMarker = ([string][char]0x6E38) + ([string][char]0x7267)
$orcMarker = ([string][char]0x517D) + ([string][char]0x4EBA)
foreach ($entry in $resourceExpectations.GetEnumerator()) {
    $relative = Join-Path 'name_generators/default' $entry.Key
    $json = Read-Source $relative | ConvertFrom-Json
    foreach ($id in @($entry.Value | Where-Object {
        $_ -like 'orc_nomadic_*' })) {
        $generator = @($json | Where-Object { $_.id -eq $id })[0]
        $primaryText = $generator.templates | ConvertTo-Json -Depth 10
        $fallbackText = $generator.default_template |
            ConvertTo-Json -Depth 10
        if ($primaryText -notmatch [regex]::Escape($nomadicMarker)) {
            throw "$id does not route its primary templates to nomadic resources."
        }
        if ($fallbackText -notmatch [regex]::Escape($orcMarker)) {
            throw "$id does not keep a same-profile fantasy orc fallback."
        }
    }
}

$vanGenerator = @(Read-Source 'name_generators/default/creatures.json' |
    ConvertFrom-Json | Where-Object { $_.id -eq 'western_van_name' })
if ($vanGenerator.Count -ne 1) {
    throw 'western_van_name must be defined exactly once.'
}
$vanText = $vanGenerator | ConvertTo-Json -Depth 10
$lowPrefix = ([string][char]0x4F4E) + ([string][char]0x5730)
$nameSuffix = [string][char]0x540D
$lowMale = $lowPrefix + ([string][char]0x7537) + $nameSuffix
$lowFemale = $lowPrefix + ([string][char]0x5973) + $nameSuffix
foreach ($library in @($lowMale, $lowFemale)) {
    if ($vanText -notmatch [regex]::Escape($library)) {
        throw "western_van_name does not reference $library."
    }
    $wordPath = Join-Path $repoRoot "word_libraries/default/$library.txt"
    if (-not (Test-Path -LiteralPath $wordPath)) {
        throw "Missing Low Countries word library: $library.txt"
    }
    $words = @(Get-Content -LiteralPath $wordPath -Encoding UTF8 |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($words.Count -eq 0 -or @($words | Sort-Object -Unique).Count -ne
        $words.Count) {
        throw "$library.txt must be non-empty and duplicate-free."
    }
}

$generatorRoot = Join-Path $repoRoot 'name_generators/default'
$otherGeneratorText = foreach ($file in Get-ChildItem -LiteralPath `
    $generatorRoot -File -Filter '*.json') {
    $parsedGenerators = Get-Content -LiteralPath $file.FullName -Raw `
        -Encoding UTF8 | ConvertFrom-Json
    foreach ($generator in $parsedGenerators) {
        if ($generator.id -ne 'western_van_name') {
            $generator | ConvertTo-Json -Depth 10
        }
    }
}
foreach ($library in @($lowMale, $lowFemale)) {
    if (($otherGeneratorText -join "`n") -match [regex]::Escape($library)) {
        throw 'Low Countries given-name libraries may only be used by western_van_name.'
    }
}

Write-Output 'Western lineage naming source guard passed.'
