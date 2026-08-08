$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$kingdom = Get-Content -Raw (Join-Path $root 'Code/ui/windows/KingdomWindowAddition.cs')
$kingdomTab = Get-Content -Raw (Join-Path $root 'Code/patch/AW_KingdomTabPatch.cs')
$unitTab = Get-Content -Raw (Join-Path $root 'Code/patch/AW_UnitTabPatch.cs')
$unitSource = Get-Content -Raw (Join-Path $root 'Code/patch/AW_UnitWindowPatch.cs')
$grant = Get-Content -Raw (Join-Path $root 'Code/ui/windows/VirtualNobleTitleGrantWindow.cs')
$roster = Get-Content -Raw (Join-Path $root 'Code/ui/windows/VirtualNobleTitleRosterWindow.cs')
$atlas = Get-Content -Raw (Join-Path $root 'Code/ui/windows/KingdomAtlasWindow.cs')
$locale = Get-Content -Raw (Join-Path $root 'Locales/aw3_virtual_titles.csv')
$windowLocale = Get-Content -Raw (Join-Path $root 'Locales/aw3_window_titles.csv')
$mapModeLocale = Get-Content -Raw (Join-Path $root 'Locales/aw3_ancestry_mapmode.csv')
$configLocale = Get-Content -Raw (Join-Path $root 'Locales/aw3_config.csv')
$titleTable = Get-Content -Raw (Join-Path $root 'Code/core/db/VirtualNobleTitleTableItem.cs')
$titleService = Get-Content -Raw (Join-Path $root 'Code/core/lineage/VirtualNobleTitleService.cs')
$commandModels = Get-Content -Raw (Join-Path $root 'Code/api/multiplayer/AW3MultiplayerCatalogModels.cs')
function Test-CsvKey([string] $text, [string] $key) {
    return $text.Contains($key + ',') -or
           $text.Contains('"' + $key + '",')
}
foreach ($needle in @('VirtualNobleTitleRosterWindow.Open', 'GetActiveForKingdom', 'VirtualNobleTitleGrantWindow.Open')) {
    if (-not ($kingdom.Contains($needle) -or $kingdomTab.Contains($needle) -or $unitTab.Contains($needle) -or $roster.Contains($needle))) { throw "missing UI entry: $needle" }
}
foreach ($needle in @('WideWindowChrome.Attach', 'DisableNativeScroll', 'ActivateInputField',
                      'EventSystem.current', 'ContentSizeFitter', 'LayoutGroup',
                      'value.rectTransform.anchorMin = Vector2.zero',
                      'value.rectTransform.anchorMax = Vector2.one',
                      'placeholder.rectTransform.anchorMin = Vector2.zero',
                      'placeholder.rectTransform.anchorMax = Vector2.one')) {
    if (-not $grant.Contains($needle)) { throw "missing wide grant window input contract: $needle" }
}
foreach ($needle in @('WideWindowChrome.Attach', 'DisableNativeScroll', 'BuildScroller',
                      'RectMask2D', 'ScrollRect', 'CreateScrollbar', '_viewport',
                      '_scrollbar', 'text.rectTransform.anchorMin = Vector2.zero',
                      'text.rectTransform.anchorMax = Vector2.one')) {
    if (-not $roster.Contains($needle)) { throw "missing wide roster window contract: $needle" }
}
foreach ($needle in @('new Vector2(580f, 420f)',
                      'new Vector2(560f, 300f)',
                      'new Vector2(880f, 680f)',
                      'FamilyTreeNodeView.GetAvatarPrefab',
                      'UiUnitAvatarElement',
                      'avatar.show(pEntry.Actor)',
                      'SetRect(titleText, 68f, 2f',
                      'SetRect(identity, 68f, 25f',
                      'SetRect(inputLabel, 188f, 2f',
                      'SetRect(input, 188f, 24f',
                      'SetRect(edit, 310f, 24f',
                      'SetRect(delete, 364f, 24f')) {
    if (-not $roster.Contains($needle)) {
        throw "missing narrowed roster portrait layout contract: $needle"
    }
}
if (-not $roster.Contains('native.vertical = false')) {
    throw 'native roster scroll must be disabled when the custom viewport is active'
}
foreach ($needle in @('WideWindowChrome.Attach', 'DisableNativeScroll',
                      'ContentSizeFitter', 'RectMask2D', '_chronicleViewport',
                       'RenderChronicleColumns', 'AppendVerticalColumns',
                       'ClearPreview', '_previewRequested',
                       '_mapContent.sizeDelta',
                       'AWFontDropdown.Create', 'OnMapFontSelected')) {
    if (-not $atlas.Contains($needle)) {
        throw "missing kingdom atlas wide-window contract: $needle"
    }
}
foreach ($needle in @('DefaultWidth = 480f', 'DefaultHeight = 310f',
                      'MinWidth = 480f', 'MinHeight = 310f',
                      'MaxWidth = 480f', 'MaxHeight = 310f',
                      'const float controlWidth = 280f',
                      'const float footerButtonWidth = 40f',
                      'const float fontButtonWidth = 100f')) {
    if (-not $atlas.Contains($needle)) {
        throw "kingdom atlas must match family tree dimensions: $needle"
    }
}
foreach ($needle in @('AW_KingdomTitleHoldersButton', 'AW_KingdomAtlasButton',
                      'historyBtn.transform.GetSiblingIndex() + 1',
                      'aw_kingdom_atlas', 'aw_virtual_titles')) {
    if (-not $kingdomTab.Contains($needle)) { throw "missing kingdom sidebar contract: $needle" }
}
foreach ($key in @(
    'aw_kingdom_atlas',
    'aw_kingdom_atlas_desc',
    'aw_kingdom_atlas_empty',
    'aw_kingdom_atlas_ready',
    'aw_kingdom_atlas_generating_progress',
    'aw_kingdom_atlas_generated',
    'aw_kingdom_atlas_failed',
    'aw_kingdom_atlas_node'
)) {
    if (-not (Test-CsvKey $windowLocale $key)) {
        throw "missing kingdom atlas locale key: $key"
    }
}
foreach ($needle in @('GrantVirtualNobleTitle', 'DispatchFromUi', 'characterLimit',
                      'hereditary', 'CycleHereditary')) {
    if (-not $grant.Contains($needle)) { throw "missing grant UI contract: $needle" }
}
if (-not $titleTable.Contains('hereditary')) { throw 'virtual title table must persist hereditary state' }
foreach ($needle in @('HEREDITARY', 'pHereditary', 'ShouldCreateSuccessor')) {
    if (-not $titleService.Contains($needle)) { throw "missing hereditary service contract: $needle" }
}
if (-not $commandModels.Contains('hereditary') -or
    -not $commandModels.Contains('BoolValue')) {
    throw 'grant command must carry hereditary state'
}
if (-not $roster.Contains('SetRect(titleText, 68f')) {
    throw 'virtual title roster text must move 40 pixels right'
}
if (-not $roster.Contains('ActionLibrary.openUnitWindow')) { throw 'missing roster actor navigation' }
foreach ($needle in @('NobleRankService.GetDisplayTitle',
                      'GetActiveForKingdom',
                      'SetAsFirstSibling',
                      'SetRect(titleText, 68f, 2f',
                      'SetRect(input, 188f, 24f',
                      'getColor()?.color_text')) {
    if (-not $roster.Contains($needle)) {
        throw "missing roster ceremonial title contract: $needle"
    }
}
foreach ($needle in @('CeremonialTitleResolver.Resolve',
                      'SetAsFirstSibling',
                      'getColor()?.color_text',
                      'aw_ruler_appellation", appellation, kingdomColor')) {
    if (-not $unitSource.Contains($needle)) {
        throw "missing unit ceremonial title contract: $needle"
    }
}
foreach ($key in @(
    'aw_virtual_titles',
    'aw_virtual_titles_short',
    'aw_virtual_titles_none',
    'aw_virtual_title_grant',
    'aw_virtual_title_grant_desc',
    'aw_virtual_title_grant_action',
    'aw_virtual_title_prompt',
    'aw_virtual_title_placeholder',
    'aw_virtual_noble_title',
    'aw_unknown_actor',
    'aw_virtual_title_error_generic',
    'aw_virtual_title_error_not_ready',
    'aw_virtual_title_error_invalid_target',
    'aw_virtual_title_error_invalid_text',
    'aw_virtual_title_error_duplicate',
    'aw_virtual_title_error_persistence',
    'aw_virtual_title_hereditary_on',
    'aw_virtual_title_hereditary_off',
    'aw_virtual_title_hereditary',
    'aw_virtual_title_edit',
    'aw_virtual_title_delete'
)) {
    if (-not (Test-CsvKey $locale $key)) { throw "missing locale key: $key" }
}
foreach ($key in @('AWMapModeSettings',
                   'AW3_USE_BUNDLED_HIERARCHICAL_VASSAL_MAP_FONT',
                   'AW3_USE_BUNDLED_HIERARCHICAL_VASSAL_MAP_FONT Description')) {
    if (-not (Test-CsvKey $configLocale $key)) {
        throw "missing config CSV locale key: $key"
    }
}
foreach ($key in @('aw3_map_font_prefix', 'aw3_map_font_bundled')) {
    if (-not (Test-CsvKey $mapModeLocale $key)) {
        throw "missing map-mode CSV locale key: $key"
    }
}
foreach ($key in @('aw_virtual_titles Title', 'aw_virtual_title_grant Title')) {
    if (-not (Test-CsvKey $locale $key)) {
        throw "missing virtual-title CSV locale key: $key"
    }
}
foreach ($key in @('aw_kingdom_atlas Title', 'aw_kingdom_atlas_png',
                   'aw_kingdom_atlas_gif')) {
    if (-not (Test-CsvKey $windowLocale $key)) {
        throw "missing atlas CSV locale key: $key"
    }
}
if ($grant.Contains('result.Error')) { throw 'raw command error leaked into UI' }
Write-Output 'virtual noble title UI source guard passed'
