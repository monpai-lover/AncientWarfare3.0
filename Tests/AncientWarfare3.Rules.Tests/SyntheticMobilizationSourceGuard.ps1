$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Read-Source([string] $relativePath) {
    return Get-Content -Raw -Encoding UTF8 (Join-Path $root $relativePath)
}

function Require-Text([string] $text, [string] $needle,
    [string] $message) {
    if (-not $text.Contains($needle)) { throw $message }
}

function Reject-Text([string] $text, [string] $needle,
    [string] $message) {
    if ($text.Contains($needle)) { throw $message }
}

$ledger = Read-Source 'Code/core/lineage/SyntheticMobilizationLedgerService.cs'
$reserve = Read-Source 'Code/core/lineage/CityReservePoolService.cs'
$temporary = Read-Source 'Code/core/lineage/TemporaryLevyService.cs'
$standing = Read-Source 'Code/patch/AW_StandingArmyPatch.cs'
$scheduler = Read-Source 'Code/core/performance/ArmyRtsSchedulingService.cs'
$runner = Read-Source 'Code/core/performance/AWCooperativeSimulationRunner.cs'
$director = Read-Source 'Code/core/lineage/KingdomWarDirectorService.cs'

Require-Text $ledger 'city.getPopulationPeople()' `
    'Synthetic quota must use the city population snapshot.'
Require-Text $ledger 'TryReserveReplacement' `
    'Initial mobilization and replacements must share the city-war ledger.'
Require-Text $ledger 'DemobilizationBatchLimit' `
    'Post-war generated-actor removal must remain bounded.'
Reject-Text $reserve 'TryTakeNextActorId(pool.ActorIds' `
    'Real resident IDs cannot feed AW3 temporary mobilization.'
Reject-Text $temporary 'SyntheticLevyService.Promote(' `
    'Generated wartime actors cannot become permanent residents.'
Reject-Text $standing 'TryToMakeWarrior_Prefix' `
    'AW3 cannot globally suppress vanilla recruitment.'
Require-Text $scheduler 'SharedGate' `
    'Native and AW3 RTS entries must share one exact-once gate.'
Require-Text $runner 'Aw3RtsLogicalPulse' `
    'Every large-step internal pass must expose an RTS pulse.'
Require-Text $director 'TryAssignFirstOrderMission(' `
    'War participants must receive bounded first-order missions.'

Write-Host 'Synthetic mobilization source guard passed.'
