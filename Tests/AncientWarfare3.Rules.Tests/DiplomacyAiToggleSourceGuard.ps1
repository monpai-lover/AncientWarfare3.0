$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$path = Join-Path $root 'Code/content/GodPowerLibrary.cs'
$source = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
$lineagePath = Join-Path $root 'Code/ui/AW_LineageTab.cs'
$lineageSource = [IO.File]::ReadAllText($lineagePath,
    [Text.Encoding]::UTF8)
$failures = [System.Collections.Generic.List[string]]::new()

$registerStart = $source.IndexOf(
    'private static void RegisterDiplomacyAiToggle()')
$registerEnd = $source.IndexOf(
    'private static void SyncDiplomacyAiSetting()', $registerStart)
if ($registerStart -lt 0 -or $registerEnd -le $registerStart) {
    $failures.Add('diplomacy AI toggle registration could not be located')
}
else {
    $registration = $source.Substring($registerStart,
        $registerEnd - $registerStart)
    if (-not $registration.Contains(
            'BuildBooleanToggleAction(SyncDiplomacyAiSetting)')) {
        $failures.Add(
            'diplomacy AI must use the normal boolean toggle action')
    }
    if ($registration.Contains('BuildMapModeToggleAction')) {
        $failures.Add(
            'diplomacy AI must not use the map-mode zone toggle action')
    }
}

foreach ($needle in @(
        'private static PowerToggleAction BuildBooleanToggleAction',
        'optionData.boolVal = !optionData.boolVal;',
        'PlayerConfig.saveData();')) {
    if (-not $source.Contains($needle)) {
        $failures.Add("normal boolean toggle is missing '$needle'")
    }
}

$buttonStart = $lineageSource.IndexOf(
    'PowerButton diplomacyAiToggle =')
$buttonEnd = $lineageSource.IndexOf(
    'Register(groups, AWLineageTabLayoutRules.Settings,', $buttonStart)
if ($buttonStart -lt 0 -or $buttonEnd -le $buttonStart) {
    $failures.Add('diplomacy AI toolbar button could not be located')
}
else {
    $buttonRegistration = $lineageSource.Substring($buttonStart,
        $buttonEnd - $buttonStart)
    if (-not $buttonRegistration.Contains(
            'pNoAutoSetToggleAction: true')) {
        $failures.Add(
            'diplomacy AI button must suppress NML auto-toggle to avoid a double inversion')
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Diplomacy AI toggle source guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Diplomacy AI toggle source guard passed.'
