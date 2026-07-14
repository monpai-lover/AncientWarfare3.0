param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    return [System.IO.File]::ReadAllText((Join-Path $root $relativePath))
}

function Require-Absent([string]$name, [string]$relativePath, [string]$needle) {
    $text = Read-Source $relativePath
    if ($text.Contains($needle)) {
        $failures.Add("${name}: found forbidden text '$needle' in $relativePath")
    }
}

function Require-Present([string]$name, [string]$relativePath, [string]$needle) {
    $text = Read-Source $relativePath
    if (-not $text.Contains($needle)) {
        $failures.Add("${name}: missing required text '$needle' in $relativePath")
    }
}

Require-Absent 'generic meta-window name patch' 'Code/patch/AW_WorldLogGuardPatch.cs' 'WindowMetaGeneric<War'
Require-Absent 'generic meta-window helper' 'Code/patch/AW_WorldLogGuardPatch.cs' 'MetaWindowSafetyRules'
Require-Absent 'kingdom display-time name repair' 'Code/patch/AW_KingdomWindowPatch.cs' 'nameInput.setText(dataName)'
Require-Absent 'load-time world name scan' 'Code/patch/AW_SavePatch.cs' 'XiaNamingRepair.EnsureWorldNames()'
Require-Absent 'custom tab native sprite overwrite' 'Code/ui/AW_LineageTab.cs' 'ApplyNativeTabSprites'
Require-Absent 'custom tab selected sprite overwrite' 'Code/ui/AW_LineageTab.cs' 'tab_main.image_selected'
Require-Absent 'anonymous Xia clan placeholder' 'Code/content/XiaNaming.cs' '"无名"'

$lineage = Read-Source 'Code/core/lineage/LineageService.cs'
$branchStart = $lineage.IndexOf('public static void OnKingFoundBranch(', [System.StringComparison]::Ordinal)
$newClan = $lineage.IndexOf('newClan(pKing', $branchStart, [System.StringComparison]::Ordinal)
$freezeShi = $lineage.IndexOf('GenerateShiName(pKing)', $branchStart, [System.StringComparison]::Ordinal)
if ($branchStart -lt 0 -or $newClan -lt 0 -or $freezeShi -lt 0 -or $freezeShi -gt $newClan) {
    $failures.Add('king-founded branch must resolve its shi before newClan(pKing)')
}

$contentPath = 'Code/content/schools/HistoricalSchoolContent.cs'
Require-Present 'lecture task id' $contentPath 'LectureTaskId = "aw_historical_school_lecture"'
Require-Present 'debate travel task id' $contentPath 'DebateTravelTaskId = "aw_historical_school_debate_travel"'
Require-Present 'debate task id' $contentPath 'DebateTaskId = "aw_historical_school_debate"'
Require-Present 'debate receiver task id' $contentPath 'DebateReceivingTaskId = "aw_historical_school_debate_receiving"'

$localePath = 'Locales/others.csv'
Require-Present 'lecture task locale' $localePath 'task_unit_aw_historical_school_lecture,'
Require-Present 'debate travel task locale' $localePath 'task_unit_aw_historical_school_debate_travel,'
Require-Present 'debate task locale' $localePath 'task_unit_aw_historical_school_debate,'
Require-Present 'debate receiver task locale' $localePath 'task_unit_aw_historical_school_debate_receiving,'

Require-Present 'frame activity queue' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolActivityQueue.ProcessFrame()'
Require-Present 'school-level canonical master slots' 'Code/core/schools/HistoricalSchoolDescentService.cs' 'HistoricalSchoolActiveMasterSlots'

if ($failures.Count -gt 0) {
    Write-Host "Source guard failures: $($failures.Count)"
    foreach ($failure in $failures) {
        Write-Host " - $failure"
    }
    exit 1
}

Write-Host 'Source guards passed.'
