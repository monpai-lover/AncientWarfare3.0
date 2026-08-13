$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$controller = Get-Content -Raw (Join-Path $root 'Code\core\lineage\ArmyRtsControllerService.cs')
$watchdog = Get-Content -Raw (Join-Path $root 'Code\core\lineage\ArmyStallWatchdogService.cs')
$scheduler = Get-Content -Raw (Join-Path $root 'Code\core\performance\ArmyRtsSchedulingService.cs')
$rules = Get-Content -Raw (Join-Path $root 'Code\core\lineage\ArmyAbstractBattleRules.cs')
$service = Get-Content -Raw (Join-Path $root 'Code\core\lineage\ArmyAbstractBattleService.cs')
$config = Get-Content -Raw (Join-Path $root 'default_config.json')
$patch = Get-Content -Raw (Join-Path $root 'Code\patch\AW_ModConfigSelectPatch.cs')

if ($controller.Contains('ArmyRtsPosture.Assault')) {
    throw 'Controller references a non-existent ArmyRtsPosture.Assault value.'
}
if (-not $watchdog.Contains('ArmyRtsWarDoctrine.IsAbstractDecisive')) {
    throw 'AbstractDecisive must disable watchdog movement and withdrawal recovery.'
}
if (-not ($scheduler.IndexOf('ArmyAbstractBattleService.ProcessFrame()') -lt
          $scheduler.IndexOf('ArmyRouteProviderService.ProcessFrame'))) {
    throw 'Abstract battle resolution must run before route generation.'
}
if ($rules -match 'UnityEngine\.Random|System\.Random|new Random\s*\(' -or
    $service -match 'UnityEngine\.Random|System\.Random|new Random\s*\(') {
    throw 'Abstract battle resolution must use deterministic hashing only.'
}
if (-not $config.Contains('"AW3_ARMY_RTS_WAR_RESOLUTION_MODE"') -or
    -not $config.Contains('"Type": "SELECT"') -or
    -not $config.Contains('AWPerformanceSettings:SetArmyRtsWarResolutionMode')) {
    throw 'RTS war resolution selector is missing from default_config.json.'
}
if (-not $patch.Contains('pItem.Id + " Option " +') -or
    -not $patch.Contains('ModeCount = 3') -or
    -not $patch.Contains('ArmyRtsWarDoctrineRules.') -or
    -not $patch.Contains('Normalize(pIndex)')) {
    throw 'RTS war resolution selector does not dynamically render normalized options.'
}
foreach ($locale in @('en.json', 'ch.json', 'cz.json')) {
    $localeText = Get-Content -Raw (Join-Path $root ('Locales\' + $locale))
    foreach ($option in 0..2) {
        if (-not $localeText.Contains('AW3_ARMY_RTS_WAR_RESOLUTION_MODE Option ' + $option)) {
            throw "RTS war resolution localization is missing option $option in $locale."
        }
    }
}
$forbidden = @(
    'Code/core/lineage/ZhuluWarService.cs',
    'Code/core/lineage/ZhuluWarRules.cs',
    'Code/core/lineage/ZhuluWarMigrationService.cs',
    'Code/core/lineage/ZhuluAgeDirectorService.cs',
    'Code/core/lineage/ZhuluAgeRules.cs',
    'Code/core/lineage/ZhuluAgeStatePersistence.cs',
    'Code/core/lineage/ZhuluAgeStateTableItem.cs',
    'Code/core/lineage/ZhuluWorldAgeContent.cs',
    'Code/core/lineage/MandateService.cs'
)
$changed = git -C $root diff --name-only
foreach ($path in $forbidden) {
    if ($changed -contains $path) {
        throw "Doctrine change touched forbidden file: $path"
    }
}
Write-Output 'RTS doctrine boundary source guard passed.'
