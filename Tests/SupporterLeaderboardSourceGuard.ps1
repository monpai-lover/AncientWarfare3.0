$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$data = Join-Path $repo 'supporters.csv'
$qr = Join-Path $repo 'sponsor_qr.jpg'
$window = Join-Path $repo 'Code/ui/windows/SupporterLeaderboardWindow.cs'
$item = Join-Path $repo 'Code/ui/items/SupporterLeaderboardListItem.cs'
$service = Join-Path $repo 'Code/ui/SupporterLeaderboardData.cs'
$tab = Join-Path $repo 'Code/ui/AW_LineageTab.cs'
$ids = Join-Path $repo 'Code/ui/AW_LineageWindowIds.cs'
$locale = Join-Path $repo 'Locales/aw3_supporters.csv'
foreach ($path in @($data, $qr, $window, $item, $service, $tab, $ids, $locale)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "missing supporter leaderboard source: $path" }
}
$csv = Get-Content -Raw -Encoding UTF8 -LiteralPath $data
if ($csv -notmatch '(?m)^rank,name,amount,date\r?$') { throw 'supporters.csv header is invalid' }
$technicalName = -join ([char]0x4e00, [char]0x7c73)
$technicalDescription = -join (
    [char]0x63d0, [char]0x4f9b, [char]0x4e86, [char]0x6280,
    [char]0x672f, [char]0x652f, [char]0x6301, [char]0x548c,
    [char]0x5e2e, [char]0x52a9, ':', [char]0x5bfb, [char]0x8def,
    '/', [char]0x5927, [char]0x6b65, [char]0x957f, [char]0x8c03,
    [char]0x5ea6, [char]0x5668)
if ($csv -notmatch ('(?m)^1,' + [regex]::Escape($technicalName) + ',,,' +
        [regex]::Escape($technicalDescription) + '\r?$')) {
    throw 'technical support entry is missing or outdated'
}
if ($csv -notmatch '(?m)^2,Justin,40,2026-08-05\r?$') {
    throw 'Justin supporter entry is missing or outdated'
}
if ([regex]::Matches($csv, '(?m)^\d+,Justin,[^,\r\n]+,[^\r\n]+\r?$').Count -ne 1) {
    throw 'supporters.csv must contain exactly one Justin entry'
}
if ($csv -notmatch '(?m)^10,\u672a\u660e\u5929\u900d\u9065\u884c,20\.00,2026-08-02\r?$') {
    throw 'supporter entry 9 is missing'
}
if ($csv -notmatch '(?m)^11,\u963f\u826f,50,2026-08-04\r?$') {
    throw 'supporter entry 10 is missing'
}
if ($csv -notmatch '(?m)^12,MO,10,2026-08-04\r?$') {
    throw 'supporter entry 11 is missing'
}
foreach ($requiredRow in @(
        '(?m)^3,Jake,20,',
        '(?m)^4,Au,10,',
        '(?m)^5,[^,]+,5,',
        '(?m)^6,[^,]+,10,',
        '(?m)^7,Beluga,15,',
        '(?m)^8,Coherence,22\.90,',
        '(?m)^9,[^,]+,25,')) {
    if ($csv -notmatch $requiredRow) {
        throw "supporter entry is missing: $requiredRow"
    }
}
$source = Get-Content -Raw -Encoding UTF8 -LiteralPath $service
if ($source -notmatch ('(?s)Rank = 1,\s*Name = "' +
        [regex]::Escape($technicalName) + '",\s*Amount = "",\s*Date = "",\s*Description = "' +
        [regex]::Escape($technicalDescription) + '"')) {
    throw 'technical support fallback is missing or outdated'
}
if ($source -notmatch '(?s)Rank = 2,\s*Name = "Justin",\s*Amount = "40",\s*Date = "2026-08-05"') {
    throw 'Justin supporter fallback is missing or outdated'
}
$builtIn = [regex]::Match(
    $source,
    '(?s)private static readonly IReadOnlyList<SupporterLeaderboardEntry> BuiltIn\s*=\s*new List<SupporterLeaderboardEntry>\s*\{(?<entries>.*?)\n\s*\};')
if (-not $builtIn.Success -or
    [regex]::Matches($builtIn.Groups['entries'].Value, 'Name = "Justin"').Count -ne 1) {
    throw 'built-in supporter data must contain exactly one Justin entry'
}
foreach ($required in @(
        'supporters.csv',
        'Name = "Jake"',
        'Name = "Beluga"',
        'Amount = "15"',
        'Amount = "20.00"',
        'Parse(')) {
    if ($source -notmatch [regex]::Escape($required)) { throw "supporter data fallback missing: $required" }
}
if ($source -notmatch 'Name = "\u963f\u826f"') {
    throw 'supporter data fallback entry 10 is missing'
}
if ($source -notmatch 'Name = "MO"') {
    throw 'supporter data fallback entry 11 is missing'
}
if ($source -notmatch [regex]::Escape('Name = "' + $technicalName + '"') -or
    $source -notmatch [regex]::Escape('Description = "' + $technicalDescription + '"')) {
    throw 'supporter data technical support fallback is missing'
}
$itemSource = Get-Content -Raw -Encoding UTF8 -LiteralPath $item
foreach ($required in @('decimal.TryParse', 'amountPrefix + amount', 'Description', 'description')) {
    if ($itemSource -notmatch [regex]::Escape($required)) {
        throw "supporter non-monetary display handling missing: $required"
    }
}
$windowSource = Get-Content -Raw -Encoding UTF8 -LiteralPath $window
foreach ($required in @('sponsor_qr.jpg', 'ImageConversion.LoadImage', 'SponsorQr')) {
    if ($windowSource -notmatch [regex]::Escape($required)) {
        throw "supporter QR integration missing: $required"
    }
}
$tabSource = Get-Content -Raw -Encoding UTF8 -LiteralPath $tab
if ($tabSource -notmatch 'aw_supporter_leaderboard_btn' -or
    $tabSource -notmatch 'SupporterLeaderboardWindow\.Open') {
    throw 'supporter leaderboard tab entry is missing'
}
$idSource = Get-Content -Raw -Encoding UTF8 -LiteralPath $ids
if ($idSource -notmatch 'SUPPORTERS\s*=\s*"aw_supporters"') {
    throw 'supporter window id is missing'
}
Write-Output 'Supporter leaderboard source guard passed.'
