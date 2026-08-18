$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$patchPath = Join-Path $root `
    'Code\patch\naming\AW_ActorManualNamePatch.cs'
$synchronizerPath = Join-Path $root `
    'Code\patch\naming\ActorManualNameInputSynchronizer.cs'

if (-not (Test-Path -LiteralPath $synchronizerPath)) {
    throw 'Missing actor manual-name InputField synchronizer.'
}

$patch = Get-Content -Raw -LiteralPath $patchPath
$synchronizer = Get-Content -Raw -LiteralPath $synchronizerPath

$rewriteCalls = [regex]::Matches($patch,
    'ActorManualNameInputSynchronizer\.TryRewrite\s*\(').Count
if ($rewriteCalls -ne 3) {
    throw "Actor manual-name patch must route all three text rewrites through the synchronizer (actual $rewriteCalls)."
}

if ($patch -match '\bp(?:First|State\.Second)\.setText\s*\(') {
    throw 'Actor manual-name patch still rewrites editor text outside the synchronizer.'
}

$textWrite = $synchronizer.IndexOf('pField.text = pText ?? string.Empty;')
$caretClamp = $synchronizer.IndexOf('pField.caretPosition = Mathf.Clamp(')
$anchorClamp = $synchronizer.IndexOf(
    'pField.selectionAnchorPosition = Mathf.Clamp(')
$focusClamp = $synchronizer.IndexOf(
    'pField.selectionFocusPosition = Mathf.Clamp(')
if ($textWrite -lt 0 -or $caretClamp -le $textWrite -or
    $anchorClamp -le $caretClamp -or $focusClamp -le $anchorClamp) {
    throw 'InputField rewrite must clamp caret, anchor, and focus after assigning text.'
}

if ($synchronizer -notmatch
    'if\s*\(pField\.isFocused\)[\s\S]*Input\.compositionString' -or
    $synchronizer -notmatch
    'catch\s*\{\s*return false;\s*\}') {
    throw 'Focused InputField rewrite must fail closed when composition state is active or unreadable.'
}

foreach ($savedPosition in @('int caret =', 'int anchor =', 'int focus =')) {
    $position = $synchronizer.IndexOf($savedPosition)
    if ($position -lt 0 -or $position -gt $textWrite) {
        throw "InputField rewrite must capture $savedPosition before assigning text."
    }
}

if ($patch -notmatch 'CanRewrite\(\s*pFirst\.inputField\s*\)' -or
    $patch -notmatch 'ScheduleRetry\(') {
    throw 'IME deferral must preserve editor state and schedule a later rewrite.'
}

Write-Output 'Actor manual-name InputField source guard passed.'
