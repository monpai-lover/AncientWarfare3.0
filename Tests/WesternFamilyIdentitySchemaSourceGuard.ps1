$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-Source([string] $RelativePath) {
    $path = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing required source: $RelativePath"
    }
    return Get-Content -LiteralPath $path -Raw -Encoding UTF8
}

function Require-Match([string] $Text, [string] $Pattern,
    [string] $Message) {
    if ($Text -notmatch $Pattern) { throw $Message }
}

function Assert-ShiSnapshotMaterializer([string] $BulkSource) {
    $marker =
        'result[reader.GetInt64(0)] = new LineageTreeShiSnapshot'
    $start = $BulkSource.IndexOf($marker,
        [System.StringComparison]::Ordinal)
    if ($start -lt 0) {
        throw 'The bulk Shi snapshot initializer could not be located.'
    }
    $end = $BulkSource.IndexOf('};', $start,
        [System.StringComparison]::Ordinal)
    if ($end -lt $start) {
        throw 'The bulk Shi snapshot initializer is incomplete.'
    }

    $initializer = $BulkSource.Substring($start, $end - $start)
    foreach ($mapping in @(
        @{ Field = 'NamingProfile'; Variable = 'namingProfile' },
        @{ Field = 'WesternNamingTradition';
            Variable = 'westernNamingTradition' },
        @{ Field = 'OriginCityChineseName';
            Variable = 'originCityChineseName' },
        @{ Field = 'DisplayStem'; Variable = 'displayStem' })) {
        $pattern = '\b' + $mapping.Field + '\s*=\s*' +
            $mapping.Variable + '\s*(,|$)'
        $count = [regex]::Matches($initializer, $pattern,
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
        ).Count
        if ($count -ne 1) {
            throw "Bulk Shi snapshot must connect $($mapping.Field) to $($mapping.Variable) exactly once."
        }
    }
}

$schema = Read-Source 'Code/core/db/ShiBranchTableItem.cs'
$dto = Read-Source 'Code/core/lineage/LineageDTO.cs'
$query = Read-Source 'Code/core/lineage/LineageQuery.cs'
$bulk = Read-Source 'Code/core/lineage/LineageBulkQuery.cs'
$rules = Read-Source 'Code/core/lineage/WesternFamilyIdentityRules.cs'
$archiveManager = Read-Source 'Code/core/db/LineageArchiveManager.cs'
$sqliteHelper = Read-Source 'Code/utils/SQLiteHelper.cs'

Require-Match $schema `
    '\[TableItemDef\(pDefaultValue:\s*"xia"\)\]\s*public string naming_profile\s*=\s*"xia"' `
    'ShiBranch.naming_profile must have the physical TEXT default xia.'
foreach ($field in @(
    'western_naming_tradition',
    'origin_city_chinese_name',
    'display_stem',
    'parent_shi_id')) {
    Require-Match $schema ("public\s+\w+\s+" + $field + "\b") `
        "ShiBranch schema is missing $field."
}

Require-Match $archiveManager `
    '(?s)EnsureLoadedSchema\s*\(\).*?AddMissingColumns\s*\(' `
    'Loaded saves must use the additive missing-column path.'
Require-Match $sqliteHelper `
    '(?s)AddMissingColumns\s*\(.*?ALTER TABLE.*?ADD COLUMN.*?AppendDefault' `
    'SQLite missing-column upgrades must remain additive and append defaults.'
Require-Match $sqliteHelper `
    '(?s)AppendDefault\s*\(.*?ColumnType\.TEXT.*?pBuilder\.Append\(\(pCol\.Default' `
    'TEXT defaults must be emitted as quoted SQLite literals.'
if (-not $sqliteHelper.Contains("pBuilder.Append('\'');")) {
    throw 'TEXT defaults must be wrapped in SQLite single quotes.'
}
if (-not $sqliteHelper.Contains("Replace(`"'`", `"''`")")) {
    throw 'Embedded quotes in TEXT defaults must be escaped.'
}

foreach ($field in @(
    'naming_profile',
    'western_naming_tradition',
    'origin_city_chinese_name',
    'display_stem')) {
    Require-Match $dto ("public\s+string\s+" + $field + "\b") `
        "ShiBranchInfo is missing $field."
}

Require-Match $query `
    '(?s)private const string ShiBranchIdentitySelectColumns\s*=.*?NAMING_PROFILE.*?WESTERN_NAMING_TRADITION.*?ORIGIN_CITY_CHINESE_NAME.*?DISPLAY_STEM' `
    'LineageQuery must define one ordered identity-column projection.'
$identityReferences = [regex]::Matches($query,
    '\{ShiBranchIdentitySelectColumns\}').Count
if ($identityReferences -ne 4) {
    throw "Expected four ShiBranch identity SELECT references, got $identityReferences."
}
foreach ($method in @(
    'GetShiBranches',
    'GetShiBranchInfo',
    'GetRootShiBranchInfo',
    'ReadShiBranches')) {
    Require-Match $query ("(?s)" + $method +
        ".*?\{ShiBranchIdentitySelectColumns\}") `
        "$method does not select the shared identity-column projection."
}
foreach ($mapping in @(
    'naming_profile\s*=\s*SafeStr\(pReader,\s*12\)',
    'western_naming_tradition\s*=\s*SafeStr\(pReader,\s*13\)',
    'origin_city_chinese_name\s*=\s*SafeStr\(pReader,\s*14\)',
    'display_stem\s*=\s*SafeStr\(pReader,\s*15\)')) {
    Require-Match $query $mapping `
        "ReadShiBranchInfo has an out-of-sync identity mapping: $mapping"
}

foreach ($field in @(
    'NamingProfile',
    'WesternNamingTradition',
    'OriginCityChineseName',
    'DisplayStem')) {
    Require-Match $bulk ("public\s+string\s+" + $field + "\b") `
        "Detached Shi snapshot is missing $field."
}
foreach ($field in @(
    'ShiNamingProfile',
    'ShiWesternNamingTradition',
    'ShiOriginCityChineseName',
    'ShiDisplayStem',
    'BranchNamingProfile',
    'BranchWesternNamingTradition',
    'BranchOriginCityChineseName',
    'BranchDisplayStem')) {
    Require-Match $bulk ("public\s+string\s+" + $field + "\b") `
        "Detached tree node snapshot is missing $field."
}
Require-Match $bulk `
    '(?s)WITH RECURSIVE chain\(SHI_ID,PARENT_SHI_ID,.*?NAMING_PROFILE,.*?WESTERN_NAMING_TRADITION,.*?ORIGIN_CITY_CHINESE_NAME,.*?DISPLAY_STEM\)' `
    'The bulk recursive branch CTE does not carry all identity fields.'
foreach ($column in @(
    @{ Seed = "IFNULL(NAMING_PROFILE,'xia')";
        Parent = "IFNULL(parent.NAMING_PROFILE,'xia')" },
    @{ Seed = "IFNULL(WESTERN_NAMING_TRADITION,'')";
        Parent = "IFNULL(parent.WESTERN_NAMING_TRADITION,'')" },
    @{ Seed = "IFNULL(ORIGIN_CITY_CHINESE_NAME,'')";
        Parent = "IFNULL(parent.ORIGIN_CITY_CHINESE_NAME,'')" },
    @{ Seed = "IFNULL(DISPLAY_STEM,'')";
        Parent = "IFNULL(parent.DISPLAY_STEM,'')" })) {
    if ($bulk -notmatch [regex]::Escape($column.Seed) -or
        $bulk -notmatch [regex]::Escape($column.Parent)) {
        throw "The bulk CTE seed and parent arms do not both select $($column.Seed)."
    }
}
Assert-ShiSnapshotMaterializer $bulk

foreach ($forbidden in @(
    'Replace\s*\(\s*"家族"',
    'Replace\s*\(\s*"部落"',
    'TrimEnd\s*\(',
    'Substring\s*\(.*家族',
    'Substring\s*\(.*部落')) {
    if ($rules -match $forbidden) {
        throw "Family identity rules must not recover raw stems by stripping titles: $forbidden"
    }
}
Require-Match $rules 'rawDisplayStem' `
    'The pure branch projector must accept a structured raw display stem.'

foreach ($source in @($schema, $query, $bulk)) {
    if ($source -match '(?i)DROP\s+TABLE|ALTER\s+TABLE.*RENAME|DELETE\s+FROM\s+ShiBranch') {
        throw 'Western family identity must remain an additive schema change.'
    }
}

Write-Output 'Western family identity schema source guard passed.'
