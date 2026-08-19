$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$runtime = Get-Content -Raw (Join-Path $root 'Code\core\court\CustomCourtRuntime.cs')
$window = Get-Content -Raw (Join-Path $root 'Code\ui\windows\CourtWindow.cs')
$locale = Get-Content -Raw (Join-Path $root 'Locales\aw3_court.csv')
foreach ($token in @('ResolvedLocalTemplates(', 'TrySetLocalTemplate(', 'BuiltInLocalTemplates')) {
    if (-not $runtime.Contains($token)) { throw "Missing runtime token: $token" }
}
if (-not $window.Contains('UpdateLocalSummary')) { throw 'Missing local summary' }
$localStart = $window.IndexOf('private void UpdateLocalSummary')
$localEnd = $window.IndexOf('private void UpdateLocalTemplateOptions', $localStart)
if ($localStart -lt 0 -or $localEnd -le $localStart -or
    $window.Substring($localStart, $localEnd - $localStart) -notmatch 'KingdomFlagBuilder\.Build') {
    throw 'Local summary does not build the kingdom flag'
}
if ($window -match 'bool available = CustomCourtRuntime\.TryGetSnapshot') {
    throw 'Local selector still requires a custom snapshot'
}
foreach ($key in @('aw_court_office_minzhou_governor','aw_court_office_minzhou_changshi','aw_court_office_minzhou_sicang','aw_court_office_minzhou_sihu')) {
    if (-not $locale.Contains($key + ',')) { throw "Missing locale key: $key" }
}
Write-Output 'PASS: court window regional regressions'
