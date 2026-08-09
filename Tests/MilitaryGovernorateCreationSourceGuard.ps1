$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$servicePath = Join-Path $root `
    'Code\core\lineage\MilitaryGovernorateCreationService.cs'
if (-not (Test-Path -LiteralPath $servicePath)) {
    throw 'Missing MilitaryGovernorateCreationService.'
}
$service = Get-Content -Raw -LiteralPath $servicePath
$general = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\GeneralService.cs')
$border = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\CentralizationBorderDeploymentService.cs')
$vassal = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\VassalService.cs')
$chronicle = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\ChronicleEvents.cs')
$historyLocalization = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\HistoryLocalizationRules.cs')

foreach ($token in @(
    'makeNewCivKingdom(',
    'pSeat.setKingdom(subject)',
    'pGeneral.joinCity(pSeat)',
    'subject.setCapital(pSeat)',
    'VassalService.SetMilitaryGovernorate(',
    'MilitaryGovernorateStore.TryCreate(',
    'MilitaryGovernorateRules.CityScanBudget',
    'MilitaryGovernorateRules.GeneralScanBudget',
    'MilitaryGovernorateCreationRules.RollbackFor(',
    'MilitaryGovernorateStore.End(',
    'VassalService.RollbackCreatedRelation(',
    'World.world.kingdoms.removeObject('
)) {
    if (-not $service.Contains($token)) {
        throw "Missing transactional governorate creation token: $token"
    }
}

if ($service.Contains('GeneralService.GetActiveGenerals(') -or
    $service.Contains('World.world.units')) {
    throw 'Governorate creation contains an unbounded actor scan.'
}
if (-not $general.Contains('RetireForMilitaryGovernorate')) {
    throw 'General retirement integration is missing.'
}
if (-not $border.Contains('HasExternalLandBorderForRoot')) {
    throw 'Governorate seats do not reuse root-network frontier semantics.'
}
if (-not $vassal.Contains('RollbackCreatedRelation')) {
    throw 'Vassal rollback integration is missing.'
}
if (-not $chronicle.Contains('OnMilitaryGovernorateCreated')) {
    throw 'Military governorate chronicles are missing.'
}
foreach ($key in @(
    'aw_hist_military_governorate_created_at',
    'aw_hist_military_governorate_created_as',
    'aw_hist_military_governorate_general'
)) {
    if (-not $historyLocalization.Contains(('new Entry("' + $key + '"'))) {
        throw "Missing military governorate history localization: $key"
    }
}

Write-Output 'Military governorate creation source guard passed.'
