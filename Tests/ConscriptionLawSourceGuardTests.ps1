param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $root $relativePath
    if (-not [System.IO.File]::Exists($path)) {
        $failures.Add("missing source file $relativePath")
        return ''
    }
    return [System.IO.File]::ReadAllText($path)
}

function Require([string]$source, [string]$needle, [string]$message) {
    if (-not $source.Contains($needle)) {
        $failures.Add("${message}: missing '$needle'")
    }
}

$keys = Read-Source 'Code/core/lineage/LineageKeys.cs'
$service = Read-Source 'Code/core/court/CourtAuxiliaryLawService.cs'
$window = Read-Source 'Code/ui/windows/CourtAuxiliaryLawWindow.cs'
$chronicle = Read-Source 'Code/core/lineage/ChronicleEvents.cs'
$history = Read-Source 'Code/core/lineage/HistoryLocalizationRules.cs'
$locales = Read-Source 'locales/aw3_court.csv'

Require $keys `
    'COURT_CONSCRIPTION_LAW = "aw_court_conscription_law"' `
    'the selected tier must survive save and load'
Require $keys 'COURT_CONSCRIPTION_LAW_LAST_CHANGE_YEAR' `
    'conscription must have an independent cooldown'
Require $service 'public static CourtConscriptionLaw GetConscriptionLaw' `
    'the service must expose the missing-key default'
Require $service 'CourtConscriptionLawRules.Score(' `
    'AI evaluation must use the shared conscription score'
Require $service 'CityReservePoolService.OnConscriptionLawChanged(' `
    'a successful law change must reconcile uncommitted reserves'
Require $window `
    'CreateLawSection(CourtAuxiliaryLawKind.Conscription, 4)' `
    'the auxiliary window must contain the fourth section'
Require $chronicle 'CourtAuxiliaryLawKind.Conscription =>' `
    'chronicle history must name the conscription section'
Require $history 'new Entry("aw_court_aux_law_conscription"' `
    'history localization must include the conscription section'
Require $locales 'aw_court_aux_law_conscription,' `
    'the section title must be localized'
Require $locales 'aw_court_conscription_full_desc,' `
    'full mobilization semantics must be localized'

if ($failures.Count -gt 0) {
    Write-Host "Conscription law source guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Conscription law source guard passed.'
