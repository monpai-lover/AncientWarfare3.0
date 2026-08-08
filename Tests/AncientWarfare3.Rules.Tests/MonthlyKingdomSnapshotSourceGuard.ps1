$ErrorActionPreference = 'Stop'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$queue = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\performance\MonthlyAuthorityWorkQueue.cs')
$snapshot = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\performance\MonthlyKingdomSnapshotService.cs')
$pregnancy = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\lineage\RulerHouseholdPregnancyService.cs')
$policy = Get-Content -Raw -LiteralPath (Join-Path $repo `
    'Code\core\policy\KingdomDecisionMonthlyService.cs')

function Require-Present([string] $source, [string] $needle,
    [string] $message) {
    if (-not $source.Contains($needle)) { throw $message }
}

function Require-Absent([string] $source, [string] $needle,
    [string] $message) {
    if ($source.Contains($needle)) { throw $message }
}

Require-Present $queue 'Queue<MonthlyAuthorityWorkBatch<T>>' `
    'Monthly work must store month batches instead of one object per kingdom.'
Require-Present $queue 'internal int PendingBatchCount' `
    'Monthly batch count must remain observable to regression tests.'
Require-Absent $queue 'Queue<MonthlyAuthorityWorkItem<T>>' `
    'Per-item monthly queue allocation must not return.'
Require-Present $snapshot 'IReadOnlyList<Kingdom> Get(int pMonthKey)' `
    'Monthly services must share one immutable kingdom snapshot.'
Require-Present $pregnancy 'MonthlyKingdomSnapshotService.Get(monthKey)' `
    'Pregnancy scheduling must use the shared monthly kingdom snapshot.'
Require-Present $policy 'MonthlyKingdomSnapshotService.Get(monthKey)' `
    'Policy scheduling must use the shared monthly kingdom snapshot.'
Require-Absent $pregnancy `
    'ScheduleMonth(monthKey, World.world.kingdoms)' `
    'Pregnancy scheduling must not rescan all kingdoms independently.'
Require-Absent $policy `
    'ScheduleMonth(monthKey, World.world.kingdoms)' `
    'Policy scheduling must not rescan all kingdoms independently.'

Write-Output 'Monthly kingdom snapshot source guard passed.'
