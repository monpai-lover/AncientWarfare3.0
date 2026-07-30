$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$servicePath = Join-Path $repo `
    'Code\core\lineage\EnclosedUnownedZoneRepairService.cs'
$rulesPath = Join-Path $repo `
    'Code\core\lineage\EnclosedUnownedZoneRules.cs'
$patchPath = Join-Path $repo `
    'Code\patch\AW_EnclosedUnownedZonePatch.cs'
$authorityPath = Join-Path $repo `
    'Code\core\performance\AWAuthorityCycleService.cs'

function Read-RequiredFile([string]$path, [string]$name) {
    if (-not (Test-Path $path)) {
        throw "$name is missing: $path"
    }
    return [IO.File]::ReadAllText($path)
}

function Require-Present([string]$source, [string]$needle,
    [string]$message) {
    if (-not $source.Contains($needle)) { throw $message }
}

function Require-Absent([string]$source, [string]$needle,
    [string]$message) {
    if ($source.Contains($needle)) { throw $message }
}

$service = Read-RequiredFile $servicePath 'Enclosed Zone repair service'
$rules = Read-RequiredFile $rulesPath 'Enclosed Zone repair rules'
$patch = Read-RequiredFile $patchPath 'Enclosed Zone ownership patch'
$authority = Read-RequiredFile $authorityPath 'Authority cycle service'

Require-Present $patch `
    '[HarmonyPatch(typeof(TileZone), "setCity")]' `
    'Zone ownership changes must be observed at TileZone.setCity.'
Require-Present $patch 'ObserveOwnershipChange(__instance)' `
    'The ownership patch must enqueue the changed Zone.'
Require-Present $patch '[HarmonyPatch(typeof(City), "setKingdom")]' `
    'Whole-city kingdom transfers must be observed.'
Require-Present $patch 'ObserveCityKingdomChange(__instance)' `
    'A transferred city must queue its local boundary for repair.'
Require-Present $patch 'MapBox.on_world_loaded += OnWorldLoaded' `
    'Each loaded world must start one bounded repair sweep.'
Require-Absent $patch 'nameof(MapBox.Update)' `
    'Zone repair must not run from the render-frame Update patch.'
Require-Absent $patch '"Update"' `
    'Zone repair must not run from the render-frame Update patch.'

Require-Present $service 'Queue<long>' `
    'Changed Zone coordinates must use a bounded FIFO queue.'
Require-Present $service 'HashSet<long>' `
    'Changed Zone coordinates must be coalesced.'
Require-Present $service 'MaxCandidatesPerCycle = 8' `
    'Candidate repair must have an eight-Zone cycle budget.'
Require-Present $service 'MaxSweepZonesPerCycle = 64' `
    'The initial sweep must advance at most 64 Zones per cycle.'
Require-Present $service 'MaxCityBoundaryZonesPerCycle = 16' `
    'City transfer boundary inspection must have a fixed cycle budget.'
Require-Present $service 'MaxCityBoundaryRecordsPerCycle = 4' `
    'Invalid city transfer records must also have a fixed cycle budget.'
Require-Present $service 'MaxEnclosedComponentZones = 64' `
    'Connected unowned components must have a fixed traversal budget.'
Require-Present $service 'Queue<CityBoundaryScan>' `
    'Transferred cities must use a resumable boundary queue.'
Require-Present $service 'Dictionary<long, CityBoundaryScan>' `
    'Transferred city boundary work must be coalesced by city id.'
Require-Present $service 'rescanRequested = true' `
    'A repeated city transfer must request a full boundary rescan.'
Require-Present $service 'scan.zoneIndex = 0' `
    'Repeated city transfers must restart from the first city Zone.'
Require-Present $service 'recordsRemaining--' `
    'Every dequeued city scan record must consume the record budget.'
Require-Present $service 'if (neighbour?.city == null)' `
    'City transfer repair must enqueue only unowned boundary neighbours.'
Require-Present $service 'pTargetCity.addZone(pZone);' `
    'Repair must use the original City.addZone ownership API.'
Require-Present $service `
    'EnclosedUnownedZoneRules.SelectComponentTargetCity(' `
    'Runtime repair must evaluate the complete connected unowned component.'
Require-Present $service 'CanStartComponentScan(' `
    'Open wilderness must be rejected before allocating traversal state.'
Require-Present $service 'Queue<TileZone>' `
    'Connected unowned components must be traversed without recursion.'
Require-Absent $rules 'containsGroundlessZone' `
    'Rule selection must not reject enclosed non-land components.'
Require-Absent $service 'containsGroundlessZone' `
    'Runtime repair must not reject or track groundless components.'
Require-Absent $service 'OnWorldYear' `
    'Zone repair must not add an annual global scan.'
Require-Absent $service 'World.world.cities' `
    'Zone repair must not scan the global city collection.'
Require-Absent $service 'World.world.kingdoms' `
    'Zone repair must not scan the global kingdom collection.'

$repairStart = $service.IndexOf('private static void TryRepair(',
    [StringComparison]::Ordinal)
$repairEnd = $service.IndexOf(
    'private static EnclosedZoneNeighbourFacts BuildFacts(',
    $repairStart, [StringComparison]::Ordinal)
if ($repairStart -lt 0 -or $repairEnd -le $repairStart) {
    throw 'The bounded TryRepair method region could not be located.'
}
$repairRegion = $service.Substring($repairStart,
    $repairEnd - $repairStart)
Require-Absent $repairRegion 'zone_calculator.zones' `
    'Candidate repair cannot scan the global Zone list.'
Require-Absent $repairRegion 'foreach' `
    'Candidate repair must inspect exactly four indexed neighbours.'

Require-Present $authority `
    'EnclosedUnownedZoneRepairService.ProcessAuthorityCycle();' `
    'Zone repair must drain inside the existing authority gate.'
Require-Present $authority 'EnclosedUnownedZoneRepairService.Reset();' `
    'World lifecycle reset must clear Zone repair runtime state.'

Write-Output 'Enclosed unowned Zone source guard passed.'
