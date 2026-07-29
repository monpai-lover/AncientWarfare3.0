$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot

function Read-Source([string]$path) {
    $fullPath = Join-Path $repo $path
    if (-not [IO.File]::Exists($fullPath)) {
        throw "Missing required source file: $path"
    }
    return [IO.File]::ReadAllText($fullPath)
}

function Require-Text([string]$path, [string]$needle,
    [string]$message) {
    if (-not (Read-Source $path).Contains($needle)) {
        throw $message
    }
}

function Require-Absent([string]$path, [string]$needle,
    [string]$message) {
    if ((Read-Source $path).Contains($needle)) {
        throw $message
    }
}

$service = 'Code/core/lineage/DynasticMaleLineContinuityService.cs'
Require-Text 'Code/core/lineage/NobleRankService.cs' `
    'DynasticMaleLineContinuityService.OnTitleProjectionChanged' `
    'Noble-title projection changes must refresh male-line eligibility.'
Require-Text 'Code/core/lineage/DynasticTitleService.cs' `
    'DynasticMaleLineContinuityService.OnChildBorn' `
    'A birth must refresh the title-holder successor index.'
Require-Text 'Code/core/lineage/DynasticLivingSonIndexService.cs' `
    'DynasticMaleLineContinuityService.OnActorDying' `
    'A male death must invalidate parent and holder successor entries.'
Require-Text 'Code/patch/AW_NobleHeirPregnancyPatch.cs' `
    'DynasticMaleLineContinuityService.OnActorLoaded' `
    'Actor loading must rebuild title-holder eligibility lazily.'
Require-Text 'Code/core/performance/AWAuthorityCycleService.cs' `
    'DynasticMaleLineContinuityService.ProcessAuthorityCycle' `
    'The bounded successor refresh queue must run on authority cycles.'
Require-Text 'Code/core/performance/AWAuthorityCycleService.cs' `
    'DynasticMaleLineContinuityService.Reset' `
    'World cleanup must clear the rebuildable successor index.'
Require-Text $service 'MaxHolderRefreshesPerCycle = 8' `
    'Successor refreshes must have a fixed per-cycle budget.'
Require-Text $service 'NobleRankRules.SelectEldestEligibleId' `
    'The fertility index must reuse the actual noble inheritance order.'
Require-Absent $service 'OperatingDB' `
    'The reproduction hot index must not query SQLite.'
Require-Absent $service 'World.world.units.ToList' `
    'The reproduction hot index must not copy every actor.'

Write-Output 'Dynastic male-line continuity source guard passed.'
