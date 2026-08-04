$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$service = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code/core/lineage/LineageService.cs')
$archive = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code/core/lineage/LineageArchiveWriter.cs')

if ($service -notmatch 'pActor\.data\.name\s*\?\?\s*pActor\.getName\(\)') {
    throw 'Foreign lineage capture must prefer actor.data.name over patched getName().'
}
if ($service -notmatch 'LineageKeys\.GIVEN_NAME,\s*normalizedGiven') {
    throw 'Live lineage repair must write the normalized structured given name.'
}
if ($service -notmatch 'AWNameDataKeys\.GivenName[\s\S]*dirtyGiven') {
    throw 'Live lineage repair must conditionally repair the localized given field.'
}
if ($archive -notmatch 'given\s*=\s*LineageGivenNameNormalizationRules\.Normalize') {
    throw 'Actor archive capture must normalize given_name before snapshot assignment.'
}

Write-Output 'Lineage given-name normalization source guard passed.'
