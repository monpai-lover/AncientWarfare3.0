$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$window = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\ui\windows\DiplomacyConversationWindow.cs')

foreach ($token in @(
    'private ScrollRect _leftScroll;',
    'private Scrollbar _leftScrollbar;',
    'out _leftViewport, out _leftContent, out _leftScroll);',
    '_leftScrollbar = CreateVerticalScrollbar(_leftViewport,',
    '_leftScroll);'
)) {
    if (-not $window.Contains($token)) {
        throw "Diplomacy kingdom list scrollbar is missing $token"
    }
}

Write-Output 'Diplomacy kingdom list scrollbar source guard passed.'
