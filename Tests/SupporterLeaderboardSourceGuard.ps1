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
if ($csv -notmatch '(?m)^rank,name,amount,date$') { throw 'supporters.csv header is invalid' }
if ($csv -notmatch '(?m)^1,Justin,20,') { throw 'Justin supporter entry is missing' }
if ($csv -notmatch '(?m)^9,\u672a\u660e\u5929\u900d\u9065\u884c,20\.00,2026-08-02$') {
    throw 'supporter entry 9 is missing'
}
if ($csv -notmatch '(?m)^10,\u963f\u826f,50,2026-08-04$') {
    throw 'supporter entry 10 is missing'
}
if ($csv -notmatch '(?m)^11,MO,10,2026-08-04$') {
    throw 'supporter entry 11 is missing'
}
foreach ($requiredRow in @(
        '(?m)^2,Jake,20,',
        '(?m)^3,Au,10,',
        '(?m)^4,[^,]+,5,',
        '(?m)^5,[^,]+,10,',
        '(?m)^6,Beluga,15,',
        '(?m)^7,[^,]+,25,')) {
    if ($csv -notmatch $requiredRow) {
        throw "supporter entry is missing: $requiredRow"
    }
}
$source = Get-Content -Raw -Encoding UTF8 -LiteralPath $service
foreach ($required in @(
        'supporters.csv',
        'Name = "Justin"',
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
