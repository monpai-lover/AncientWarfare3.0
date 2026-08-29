param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$court = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/court/CourtService.cs'))
$annual = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/court/CityBureauAnnualWorkService.cs'))
$runtime = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/policy/KingdomAnnualWorkService.cs'))
$failures = [System.Collections.Generic.List[string]]::new()

if (-not $court.Contains('CityBureauAnnualWorkService.Schedule(')) {
    $failures.Add('annual court work must schedule deferred city bureau slices')
}
if ($court.Contains('foreach (City city in pKingdom.getCities())') -or
    $annual.Contains('foreach (City city in pKingdom.getCities())')) {
    $failures.Add('annual policy hot path must not synchronously scan every city')
}
foreach ($required in @(
        'private const int CitiesPerSlice = 2;',
        'private const int MaximumWriteAttempts = 3;',
        'IEnumerator<City>',
        'RetryCityId',
        'DeferredWorkClass.Persistent',
        'HistoricalWriteService.TryUpsertState(')) {
    if (-not $annual.Contains($required)) {
        $failures.Add("city bureau slices are missing required boundary: $required")
    }
}
if (-not $annual.Contains('private static bool ProcessCity(') -or
    -not $annual.Contains('if (!ProcessCity(')) {
    $failures.Add('failed city bureau writes must remain in a bounded retry path')
}
foreach ($forbidden in @('DB.Insert(', 'DB.UpdateValue(',
        'DB.CheckKeyExist(', 'SQLiteCommand')) {
    if ($annual.Contains($forbidden)) {
        $failures.Add("city bureau slices must not synchronously use $forbidden")
    }
}
if (-not $runtime.Contains('CityBureauAnnualWorkService.ClearRuntime();')) {
    $failures.Add('world reset must clear pending annual city bureau slices')
}

# Core fabrication must not rescan the realm every month. The scan is only
# allowed when the slot is free AND the queue is empty; one scan then fixes
# the order for the following fabrications.
$policy = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/policy/KingdomPolicyService.cs'))
$slot = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/policy/CoreFabricationSlotRules.cs'))
$territory = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/WarTerritoryService.cs'))

if (-not $slot.Contains('ShouldScanForNextTarget(')) {
    $failures.Add('the core fabrication scan gate must be a pure rule')
}
if (-not $policy.Contains('CoreFabricationSlotRules.ShouldScanForNextTarget(')) {
    $failures.Add('monthly core fabrication must gate the scan on a free slot')
}
if (-not $policy.Contains('FillCoreFabricationQueue(')) {
    $failures.Add('one scan must fix the fabrication order into the queue')
}
# The scan must sit behind the queue pop, never before it.
$startIndex = $policy.IndexOf('private static void TryStartCoreFabrication(')
if ($startIndex -lt 0) {
    $failures.Add('TryStartCoreFabrication must remain the monthly entry point')
} else {
    $body = $policy.Substring($startIndex, 1200)
    $popIndex = $body.IndexOf('StartNextQueuedCoreFabrication(')
    $fillIndex = $body.IndexOf('FillCoreFabricationQueue(')
    if ($popIndex -lt 0 -or $fillIndex -lt 0 -or $popIndex -gt $fillIndex) {
        $failures.Add('the queue pop must precede any realm scan')
    }
}
# The per-city SQL fan-out is what made this the most expensive monthly step.
$collectIndex = $territory.IndexOf('CollectCoreProjectTargetCities(Kingdom pSource,')
if ($collectIndex -lt 0) {
    $failures.Add('core target collection must batch its lookups')
} else {
    $collectBody = $territory.Substring($collectIndex, 1800)
    if ($collectBody.Contains('CanFabricateCoreProject(')) {
        $failures.Add('core target collection must not query per city')
    }
    foreach ($required in @('LoadCoreCityIds(', 'LoadPendingProjectCityIds(',
            'CollectCoreFabricationCityIds(')) {
        if (-not $collectBody.Contains($required)) {
            $failures.Add("core target collection is missing batch lookup: $required")
        }
    }
}

# The royal succession pool is event-maintained: the kinship walk runs once per
# (kingdom, reference ruler) and is repaired only when no heir can be chosen.
$succession = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/InheritanceCandidateService.cs'))
$heir = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/HeirService.cs'))
$pool = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/SuccessionPoolService.cs'))

if (-not $succession.Contains('SuccessionPoolService.Get(')) {
    $failures.Add('royal candidate collection must go through the pool')
}
if (-not $heir.Contains('SuccessionPoolService.Insert(')) {
    $failures.Add('a new royal child must be inserted, not force a rebuild')
}
if (-not $heir.Contains('TryRepairSuccessionPool(')) {
    $failures.Add('an unfillable throne must trigger one pool repair')
}
if (-not $pool.Contains('IsPermanentlyOut(')) {
    $failures.Add('only irreversible conditions may evict from the pool')
}
# Publish a replacement list instead of mutating one a caller may be walking.
foreach ($forbidden in @('pEntry.Candidates.RemoveAt',
        'entry.Candidates.Add(', 'pEntry.Candidates.Remove(')) {
    if ($pool.Contains($forbidden)) {
        $failures.Add("the succession pool must not mutate a published list: $forbidden")
    }
}

if (-not $slot.Contains('ShouldScanForTargets(')) {
    $failures.Add('the empty-result latch must be a pure rule')
}
if (-not $policy.Contains('CoreFabricationSlotRules.ShouldScanForTargets(')) {
    $failures.Add('a fully cored realm must latch the scan off')
}
if (-not $policy.Contains('CoreTargetEmptyLatch[pKingdom.id]')) {
    $failures.Add('an empty scan result must be recorded so it stops repeating')
}
if (-not $territory.Contains('MarkCoreTargetsChanged(')) {
    $failures.Add('territory changes must re-arm the core scan')
}
# Losing or gaining a city has to re-arm, or a conquered realm never cores up.
if (-not $territory.Contains('MarkCoreTargetsChanged(pNewKingdom.id)')) {
    $failures.Add('a transferred city must re-arm its new owner')
}

if ($failures.Count -gt 0) {
    Write-Host "Annual policy hot-path failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Annual policy hot-path guard passed.'
