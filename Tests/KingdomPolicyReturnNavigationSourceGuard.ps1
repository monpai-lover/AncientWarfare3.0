$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$windowPath = Join-Path $root 'Code\ui\windows\KingdomPolicyWindow.cs'
$localePath = Join-Path $root 'Locales\aw3_policy_ui.csv'
$window = Get-Content -LiteralPath $windowPath -Raw
$locales = Get-Content -LiteralPath $localePath -Raw

function Require-Text([string]$label, [string]$source, [string]$needle) {
    if ($source.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) {
        throw "$label missing: $needle"
    }
}

Require-Text 'policy window owns a persistent kingdom return button' $window `
    'private Button _kingdomBack;'
Require-Text 'policy initialization creates the kingdom return button' $window `
    'EnsureKingdomBackButton();'
Require-Text 'return button lives in window chrome' $window `
    'buttonObject.transform.SetParent(BackgroundTransform.parent, false);'
Require-Text 'return button uses the stock arrow' $window `
    '"ui/icons/iconArrowMetaRight"'
Require-Text 'return arrow is mirrored horizontally' $window `
    'iconRect.localScale = new Vector3(-1f, 1f, 1f);'
Require-Text 'return button tooltip uses its locale key' $window `
    '"aw_policy_back_to_kingdom", "Return to Kingdom"'
Require-Text 'layout repositions the return button' $window `
    'LayoutKingdomBackButton(close);'
Require-Text 'navigation validates the owning kingdom' $window `
    'Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);'
Require-Text 'navigation returns to the owning kingdom' $window `
    'AW_LineageWindowIds.ShowKingdom(_kingdomId);'
Require-Text 'shared kingdom navigation bypasses map nameplate hit testing' `
    (Get-Content -LiteralPath (Join-Path $root `
        'Code\ui\AW_LineageWindowIds.cs') -Raw) `
    'selectAndInspect(kingdom, pFromNameplate: false, pCheckNameplate: false);'
Require-Text 'return tooltip has a locale entry' $locales `
    'aw_policy_back_to_kingdom,'

if ($window.Contains('_created.Add(_kingdomBack.gameObject)') -or
    $window.Contains('_created.Add(buttonObject)')) {
    throw 'The persistent title-bar return button must not be owned by ClearCreated().'
}

$backStart = $window.IndexOf('private void BackToKingdom()',
    [StringComparison]::Ordinal)
$backEnd = if ($backStart -ge 0) {
    $window.IndexOf('private void HideNativePolicyContent()', $backStart,
        [StringComparison]::Ordinal)
} else { -1 }
if ($backStart -lt 0 -or $backEnd -le $backStart) {
    throw 'BackToKingdom method boundaries are unavailable.'
}
$backMethod = $window.Substring($backStart, $backEnd - $backStart)
$showIndex = $backMethod.IndexOf(
    'if (canOpen)', [StringComparison]::Ordinal)
$hideIndex = $backMethod.IndexOf(
    'ScrollWindow.hideAllEvent();', [StringComparison]::Ordinal)
if ($showIndex -lt 0 -or $hideIndex -lt 0 -or $hideIndex -lt $showIndex -or
    $backMethod.IndexOf('return;', $showIndex,
        [StringComparison]::Ordinal) -lt 0) {
    throw 'A valid kingdom must switch directly before the invalid-target close path.'
}

Write-Output 'Kingdom policy return navigation source guard passed.'
