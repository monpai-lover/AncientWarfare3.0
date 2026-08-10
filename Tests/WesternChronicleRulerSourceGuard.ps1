$root = Split-Path -Parent $PSScriptRoot
$chronicle = Get-Content -Raw (Join-Path $root 'Code\core\lineage\ChronicleEvents.cs')
$court = Get-Content -Raw (Join-Path $root 'Code\core\court\CourtService.cs')
$historyUi = Get-Content -Raw (Join-Path $root 'Code\ui\windows\HistoryListWindow.cs')

if ($chronicle -notmatch 'RecordWesternRulerChanged') {
    throw 'missing western ruler chronicle writer'
}
if ($chronicle -notmatch 'KingdomEvent\.RULER_CHANGE') {
    throw 'missing western ruler transition event'
}
if ($court -notmatch 'ChronicleEvents\.EnsureCurrentRulerRecorded\(pKingdom\)') {
    throw 'missing current western ruler recovery'
}
if ($historyUi -notmatch 'pReign\.is_ruler_period') {
    throw 'missing western ruler-period rendering'
}

Write-Host 'Western chronicle ruler source guard passed.'
