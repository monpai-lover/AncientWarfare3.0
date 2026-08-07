$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string] $relativePath) {
    $path = Join-Path $root $relativePath
    if (-not [IO.File]::Exists($path)) {
        $failures.Add("missing source file $relativePath")
        return ''
    }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

function Require-Text([string] $source, [string] $needle,
    [string] $message) {
    if (-not $source.Contains($needle)) { $failures.Add($message) }
}

$configText = Read-Source 'default_config.json'
$fontSettings = Read-Source `
    'Code/core/policy/HierarchicalVassalMapFontSettings.cs'
$fontPatch = Read-Source 'Code/patch/AW_ModConfigSelectPatch.cs'
$dropdown = Read-Source 'Code/ui/components/AWFontDropdown.cs'
$atlasWindow = Read-Source 'Code/ui/windows/KingdomAtlasWindow.cs'
$configLocale = Read-Source 'Locales/aw3_config.csv'

try {
    $config = $configText | ConvertFrom-Json
    $font = @($config.AWMapModeSettings) |
        Where-Object { $_.Id -eq 'AW3_USE_BUNDLED_HIERARCHICAL_VASSAL_MAP_FONT' }
    if ($null -eq $font) {
        $failures.Add('the hierarchical map font setting is missing')
    }
    else {
        if ($font.Type -ne 'SELECT') {
            $failures.Add('the hierarchical map font setting must use SELECT')
        }
        if ($font.Callback -ne
            'HierarchicalVassalMapFontSettings:SelectFont') {
            $failures.Add('the hierarchical map font callback must accept an index')
        }
        if ($null -ne $font.BoolVal) {
            $failures.Add('the hierarchical map font setting must not use BoolVal')
        }
    }
}
catch {
    $failures.Add('default_config.json is not valid JSON: ' + $_.Exception.Message)
}

Require-Text $fontSettings 'public static void SelectFont(int pIndex)' `
    'the indexed font callback is missing'
Require-Text $fontSettings 'GetFontFamilyName' `
    'font loading must retain the raw Unity font family name'
Require-Text $fontSettings 'aw3_map_font_bundled_name' `
    'the bundled font display name is not localized'
Require-Text $fontSettings 'aw3_map_font_system_name' `
    'system font display names do not use a localized prefix'
if ($fontSettings.Contains('SwitchBundledFont(bool')) {
    $failures.Add('the old boolean font callback is still present')
}
Require-Text $fontSettings 'InitializeConfig' `
    'the font catalog is not connected to the live mod config'
Require-Text $fontPatch 'HierarchicalVassalMapFontSettings.OptionId' `
    'the settings UI patch does not target the font option'
Require-Text $fontPatch 'AWFontDropdown.Create' `
    'the font settings UI does not create the shared dropdown'
Require-Text $fontPatch 'SetupFontSelect' `
    'the font settings UI does not have a font-specific dropdown path'
Require-Text $fontPatch 'SelectFont' `
    'the font settings UI does not apply the selected font index'
Require-Text $dropdown 'internal static AWFontDropdown Create' `
    'the shared font dropdown factory is missing'
Require-Text $dropdown 'OpenDropdown' `
    'the shared font dropdown does not open a selectable list'
Require-Text $dropdown 'HierarchicalVassalMapFontSettings.GetFontName' `
    'the shared dropdown does not use the single font catalog'
Require-Text $atlasWindow 'AWFontDropdown.Create' `
    'the atlas font control does not use the shared dropdown'
if ($atlasWindow.Contains('CycleMapFont')) {
    $failures.Add('the atlas font control still uses a cycle callback')
}
$fontSetupStart = $fontPatch.IndexOf('private static void SetupFontSelect')
$fontSetupEnd = $fontPatch.IndexOf('private static void SetupIndexedSelect')
if ($fontSetupStart -lt 0 -or $fontSetupEnd -le $fontSetupStart) {
    $failures.Add('the font-specific settings setup boundary is missing')
}
else {
    $fontSetup = $fontPatch.Substring($fontSetupStart,
        $fontSetupEnd - $fontSetupStart)
    foreach ($needle in @('SetupIndexedSelect', 'Previous', 'Next')) {
        if ($fontSetup.Contains($needle)) {
            $failures.Add("the font settings path still contains '$needle'")
        }
    }
}
Require-Text $fontPatch 'Type tooltipType = AccessTools.TypeByName' `
    'the settings UI must resolve optional tooltip types before use'
Require-Text $fontPatch 'if (tooltipType == null) return' `
    'the settings UI must tolerate NML builds without TooltipButton'
Require-Text $fontPatch 'Find("Info/Text")' `
    'the settings UI must reuse the native select label'
Require-Text $fontPatch 'Find("Options")' `
    'the settings UI must place custom controls in the native options area'
if ($fontPatch.Contains('selectArea.AddComponent<HorizontalLayoutGroup>')) {
    $failures.Add('the settings UI must not stack a horizontal layout on NML select_area')
}
if ($fontPatch.Contains('FindOrCreateText(selectArea.transform, "Title"')) {
    $failures.Add('the settings UI must not create a competing title row')
}
Require-Text $dropdown 'typeof(LayoutElement)' `
    'the shared dropdown must expose a stable layout element'
Require-Text $dropdown 'Resources.GetBuiltinResource<Font>("Arial.ttf")' `
    'the shared dropdown must have a guaranteed font fallback'

$firstLine = ($configLocale -split "`r?`n")[0]
if ($firstLine -ne 'key,cz,en,ch') {
    $failures.Add('aw3_config.csv must use the unquoted NML CSV header')
}
if ($configLocale.Contains('"AW3_ENABLE_PERFORMANCE_DIAGNOSTICS"')) {
    $failures.Add('aw3_config.csv still uses quoted keys unsupported by NML CSV parsing')
}
if ($configLocale -match '循环切换|Cycles through|循環切換') {
    $failures.Add('the font setting description still describes cycle buttons')
}
if ($configLocale -notmatch '下拉|dropdown|下拉選單') {
    $failures.Add('the font setting description does not describe dropdown selection')
}
foreach ($optionIndex in 0..2) {
    $optionKey = 'AW3_ARMY_RTS_WAR_RESOLUTION_MODE Option ' + $optionIndex
    if ($configLocale -notmatch [regex]::Escape($optionKey) + ',') {
        $failures.Add("missing localized war resolution option: $optionIndex")
    }
}
Require-Text $fontPatch 'ApplyLayoutSize' `
    'the settings select controls must use stable layout dimensions'
Require-Text $fontPatch 'pPopupOffsetX: 80f' `
    'the settings font dropdown popup must shift right by 80 pixels'
if ($fontPatch.Contains('SetItemHorizontalOffset')) {
    $failures.Add('the settings font button itself must not be offset')
}
Require-Text $dropdown '_popupOffsetX' `
    'the shared dropdown must support popup-only horizontal offset'

if ($failures.Count -gt 0) {
    Write-Host "Font settings source guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Font settings source guards passed.'
