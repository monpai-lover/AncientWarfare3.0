$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$requiredFiles = @(
    'Code/core/naming/AWNameGeneratorAsset.cs'
    'Code/core/naming/AWNameGeneratorLibrary.cs'
    'Code/core/naming/AWNameTemplate.cs'
    'Code/core/naming/AWWordLibraryAsset.cs'
    'Code/core/naming/AWWordLibraryManager.cs'
    'Code/core/naming/AWNameParameterGetters.cs'
    'Code/core/naming/AWNamingResourceLoader.cs'
    'Code/core/naming/AWNameGenerationContext.cs'
    'Code/core/naming/AWNameDataKeys.cs'
    'Code/core/naming/AWInvalidNameTemplateException.cs'
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing integrated naming engine source: $relativePath"
    }

    $source = Get-Content -LiteralPath $path -Raw
    if ($source -notmatch 'namespace\s+AncientWarfare3\.core\.naming') {
        throw "Integrated naming source has the wrong namespace: $relativePath"
    }
    if ($source -cmatch '\bChinese_Name\b') {
        throw "Integrated naming source still depends on Chinese_Name: $relativePath"
    }
}

$allSource = ($requiredFiles | ForEach-Object {
    Get-Content -LiteralPath (Join-Path $repoRoot $_) -Raw
}) -join "`n"

$requiredTypes = @(
    'AWNameGeneratorAsset'
    'AWNameGeneratorLibrary'
    'AWNameTemplate'
    'AWWordLibraryAsset'
    'AWWordLibraryManager'
    'AWNameParameterGetters'
    'AWNamingResourceLoader'
    'AWNameGenerationContext'
    'AWNameDataKeys'
    'AWInvalidNameTemplateException'
)

foreach ($typeName in $requiredTypes) {
    if ($allSource -notmatch "\b(class|struct)\s+$typeName\b") {
        throw "Integrated naming engine type is missing: $typeName"
    }
}

$parameterGetterPath = Join-Path $repoRoot 'Code/core/naming/AWNameParameterGetters.cs'
$parameterGetterSource = Get-Content -LiteralPath $parameterGetterPath -Raw -Encoding UTF8
if ($parameterGetterSource -cmatch '\bLM\.' -and
    $parameterGetterSource -notmatch 'using\s+NeoModLoader\.General\s*;') {
    throw 'AWNameParameterGetters uses LM without importing NeoModLoader.General.'
}
if ($parameterGetterSource -match 'LM\.Get\("天干地支-' -or
    $parameterGetterSource -notmatch
        'GanzhiChronologyRules\.GetYearName\(year\)') {
    throw 'AWNameParameterGetters must reuse GanzhiChronologyRules instead of missing locale keys.'
}

$xiaNamingPath = Join-Path $repoRoot 'Code/content/XiaNaming.cs'
$xiaNamingSource = Get-Content -LiteralPath $xiaNamingPath -Raw -Encoding UTF8
if ($xiaNamingSource -match '\?\.get\([^;]*\bout\s+string\s+(clan|family)\b') {
    throw 'XiaNaming assigns an out local through a null-conditional call.'
}

$generatorRoot = Join-Path $repoRoot 'name_generators/default'
$generatorOwners = foreach ($file in Get-ChildItem -LiteralPath $generatorRoot `
    -Recurse -File -Filter '*.json') {
    $generators = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 |
        ConvertFrom-Json
    foreach ($generator in $generators) {
        [pscustomobject]@{
            Id = [string]$generator.id
            File = $file.Name
        }
    }
}
$duplicateGenerators = @($generatorOwners | Group-Object Id |
    Where-Object Count -gt 1)
if ($duplicateGenerators.Count -gt 0) {
    $details = $duplicateGenerators | ForEach-Object {
        $_.Name + ': ' + (($_.Group.File | Sort-Object) -join ', ')
    }
    throw "Integrated naming generator IDs must be globally unique:`n" +
        ($details -join [Environment]::NewLine)
}

Write-Output 'Integrated naming engine source guard passed.'
