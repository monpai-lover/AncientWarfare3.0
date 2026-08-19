$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$model = [IO.File]::ReadAllText(
    (Join-Path $root 'Code\core\court\CourtPyramidRules.cs'),
    [Text.Encoding]::UTF8)
$node = [IO.File]::ReadAllText(
    (Join-Path $root 'Code\ui\items\CourtActorNodeView.cs'),
    [Text.Encoding]::UTF8)
$windowPath = Join-Path $root 'Code\ui\windows\CourtOfficeHistoryWindow.cs'
$window = if ([IO.File]::Exists($windowPath)) {
    [IO.File]::ReadAllText($windowPath, [Text.Encoding]::UTF8)
} else { '' }
$readServicePath = Join-Path $root `
    'Code\core\court\OfficialCareerHistoryReadService.cs'
$readService = if ([IO.File]::Exists($readServicePath)) {
    [IO.File]::ReadAllText($readServicePath, [Text.Encoding]::UTF8)
} else { '' }
$failures = [Collections.Generic.List[string]]::new()

if (-not $model.Contains('public string OfficeLayer')) {
    $failures.Add('Court nodes must carry their persisted office layer.')
}
if (-not $node.Contains('CourtOfficeHistoryWindow.Open(') -or
    -not $node.Contains('pNode.OfficeLayer')) {
    $failures.Add('Every shared office card must open scoped history.')
}
if (-not $window.Contains('OfficialCareerHistoryReadService.Read(')) {
    $failures.Add('History window must use the core read gateway.')
}
if (-not $readService.Contains('OfficialCareerHistoryQuery.Read(')) {
    $failures.Add('History read gateway must use the bounded SQLite query.')
}
if (-not $window.Contains('private const float ContentInsetX = 30f;')) {
    $failures.Add('History content must retain the requested 30-pixel inset.')
}
if (-not $window.Contains(
        '_root.anchoredPosition = new Vector2(ContentInsetX, 0f);')) {
    $failures.Add('History content root must move right by its inset.')
}
if (-not $window.Contains(
        'float usableWidth = Mathf.Max(1f, contentWidth - ContentInsetX);')) {
    $failures.Add('History content width must shrink by the rightward inset.')
}
if (-not $window.Contains('OfficialCareerHistoryRules.YearRange(')) {
    $failures.Add('History rows must use the shared year-range rule.')
}
foreach ($forbidden in @('World.world.units_only_alive', 'getSimpleList()',
        'foreach (Actor')) {
    if ($window.Contains($forbidden)) {
        $failures.Add("History UI must not scan live actors via $forbidden.")
    }
}

if ($failures.Count -gt 0) {
    Write-Output "Office history source guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Output " - $failure" }
    exit 1
}

Write-Output 'Office history source guard passed.'
