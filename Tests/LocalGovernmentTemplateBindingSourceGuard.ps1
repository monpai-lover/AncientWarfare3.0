$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Require-Text([string]$path, [string]$pattern, [string]$message) {
    $content = Get-Content -Raw (Join-Path $root $path)
    if ($content -notmatch $pattern) { throw $message }
}

Require-Text 'Code/core/lineage/LineageKeys.cs' `
    'CITY_LOCAL_COURT_TEMPLATE_ID' `
    'missing stable city local-court template key'
Require-Text 'Code/core/lineage/LineageKeys.cs' `
    'CITY_LOCAL_COURT_TEMPLATE_MANUAL' `
    'missing manual city local-court override key'
Require-Text 'Code/core/db/CityBureauStateTableItem.cs' `
    'local_template_id' `
    'missing persisted local template id'
Require-Text 'Code/core/db/CityBureauStateTableItem.cs' `
    'local_template_manual' `
    'missing persisted manual override flag'
Require-Text 'Code/core/court/CustomCourtRuntime.cs' `
    'TryGetLocalTemplate\(' `
    'missing shared city local-template resolver'
Require-Text 'Code/core/court/CustomCourtRuntime.cs' `
    'CustomLocalCourtTemplateRules\.ResolveTemplateId\(' `
    'runtime does not use the tested assignment rules'
Require-Text 'Code/core/court/CourtDefinitionResolver.cs' `
    'ResolveLocalGraph\(' `
    'court definition resolver cannot resolve a city template graph'

Write-Host 'Local government template binding source guard passed.'
