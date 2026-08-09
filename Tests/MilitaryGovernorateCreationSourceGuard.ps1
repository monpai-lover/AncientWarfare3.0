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

$aiPath = Join-Path $root `
    'Code\core\lineage\MilitaryGovernorateAiService.cs'
if (-not (Test-Path -LiteralPath $aiPath)) {
    throw 'Missing MilitaryGovernorateAiService.'
}
$ai = Get-Content -Raw -LiteralPath $aiPath
$keys = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\lineage\LineageKeys.cs')
$annual = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\policy\KingdomAnnualWorkService.cs')
$powers = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\content\GodPowerLibrary.cs')
$tab = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\ui\AW_LineageTab.cs')
$windowPath = Join-Path $root `
    'Code\ui\windows\MilitaryGovernorateWindow.cs'

foreach ($token in @(
    'MILITARY_GOVERNORATE_AI_LAST_EVALUATION_YEAR',
    'MILITARY_GOVERNORATE_OVER_LIMIT_SINCE_YEAR',
    'MILITARY_GOVERNORATE_CITY_CURSOR'
)) {
    if (-not $keys.Contains($token)) {
        throw "Missing military governorate AI persistence key: $token"
    }
}
foreach ($token in @(
    'MilitaryGovernorateRules.AnnualCreationLimit',
    'MilitaryGovernorateRules.CityScanBudget',
    'MilitaryGovernorateRules.GeneralScanBudget',
    'GetEligibleSeats(',
    'GetGeneralCandidates(',
    'MilitaryGovernorateCreationService.TryCreateFromCandidateBatch('
)) {
    if (-not $ai.Contains($token)) {
        throw "Missing bounded military governorate AI token: $token"
    }
}
if ($ai.Contains('MilitaryGovernorateCreationService.TryCreate(')) {
    throw 'Military governorate AI re-queries its bounded general batch.'
}
if ($ai.Contains('World.world.units')) {
    throw 'Military governorate AI contains a global actor scan.'
}
if (-not $annual.Contains(
        'MilitaryGovernorateAiService.OnKingdomYear(pKingdom)')) {
    throw 'Military governorate AI is not on the annual scheduler.'
}
if (-not $powers.Contains('MILITARY_GOVERNORATE') -or
    -not $powers.Contains('MilitaryGovernorateClick') -or
    -not $powers.Contains('MilitaryGovernorateWindow.OpenCreation(city)')) {
    throw 'Player military governorate city power is incomplete.'
}
if (-not $tab.Contains('GodPowerLibrary.MILITARY_GOVERNORATE')) {
    throw 'Military governorate power button is missing from the lineage tab.'
}
if (-not (Test-Path -LiteralPath $windowPath)) {
    throw 'Military governorate general selection window is missing.'
}

Write-Output 'Military governorate creation source guard passed.'
