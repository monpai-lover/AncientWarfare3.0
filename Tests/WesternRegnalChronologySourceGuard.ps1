$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$rules = Get-Content -Raw (Join-Path $root 'Code/core/lineage/RegnalChronologyRules.cs')
$service = Get-Content -Raw (Join-Path $root 'Code/core/lineage/YearNameService.cs')

function Require-Text([string] $text, [string] $needle, [string] $message) {
    if (-not $text.Contains($needle)) { throw $message }
}

function Forbid-Text([string] $text, [string] $needle, [string] $message) {
    if ($text.Contains($needle)) { throw $message }
}

Require-Text $rules 'RegnalChronologyProfile' 'Missing chronology profile.'
Require-Text $rules 'WesternTitleSuffix' 'Missing western rank suffix mapping.'
Require-Text $service 'RegnalChronologyRules.ResolveProfile' 'YearNameService does not resolve the chronology profile.'
Require-Text $service 'AWNameDataKeys.NativeName' 'Western chronology does not read the persisted native name.'
Require-Text $service 'AWNameDataKeys.ChineseName' 'Western chronology does not read the persisted Chinese name.'
Require-Text $service 'AWLocalizedNameProjectionRules.Select' 'Western chronology does not select the localized full name.'
Require-Text $service 'RegnalChronologyRules.Format(profile' 'YearNameService does not call the profile-aware formatter.'

Forbid-Text $service 'AWLocalizedNameService.ProjectActor' 'Chronology rendering must not generate or mutate actor identity.'
Forbid-Text $service 'TryGenerateIdentityComponent' 'Chronology rendering must not generate missing identity components.'
Forbid-Text $service 'AWNameGeneratorLibrary' 'Chronology rendering must not invoke the naming generator.'

Write-Host 'Western regnal chronology source guard passed.'
