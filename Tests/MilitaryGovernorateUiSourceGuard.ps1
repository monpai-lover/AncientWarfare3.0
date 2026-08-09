$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string] $relativePath) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing source file: $relativePath"
    }
    return [IO.File]::ReadAllText($path)
}

function Require([string] $source, [string] $token, [string] $message) {
    if (-not $source.Contains($token)) { throw $message }
}

$window = Read-Source 'Code\ui\windows\MilitaryGovernorateWindow.cs'
$candidateItem = Read-Source 'Code\ui\items\WarDecisionTargetListItem.cs'
$vassalWindow = Read-Source 'Code\ui\windows\VassalRelationWindow.cs'
$item = Read-Source 'Code\ui\items\VassalRelationListItem.cs'
$dto = Read-Source 'Code\core\lineage\LineageDTO.cs'
$vassal = Read-Source 'Code\core\lineage\VassalService.cs'
$succession = Read-Source `
    'Code\core\lineage\MilitaryGovernorateSuccessionService.cs'
$models = Read-Source 'Code\api\multiplayer\AW3MultiplayerCatalogModels.cs'
$router = Read-Source `
    'Code\core\multiplayer\commands\AW3AuthoritativeCommandRouter.cs'
$handler = Read-Source `
    'Code\core\multiplayer\commands\AW3RealmCommandHandler.cs'
$store = Read-Source 'Code\core\lineage\MilitaryGovernorateStore.cs'
$stateTable = Read-Source `
    'Code\core\db\MilitaryGovernorateStateTableItem.cs'
$renamePatch = Read-Source 'Code\patch\AW_KingdomRenamePatch.cs'
$replication = Read-Source `
    'Code\api\multiplayer\AW3MultiplayerReplicationModels.cs'
$locales = (Read-Source 'Locales\others.csv') + "`n" +
    (Read-Source 'Locales\aw3_military_governorate.csv') + "`n" +
    (Read-Source 'Locales\aw3_diplomacy.csv') + "`n" +
    (Read-Source 'Locales\aw3_ancestry_mapmode.csv')

foreach ($token in @(
    'subject_kind',
    'governorate_seat_name',
    'governorate_governor_name',
    'governorate_successor_name',
    'can_designate_governorate_successor',
    'can_replace_governorate_governor',
    'can_rename_governorate'
)) {
    Require $dto $token "VassalRelationInfo is missing governorate field: $token"
}
Require $vassal 'AttachContextActions' `
    'Governorate controls must reuse VassalService.AttachContextActions.'
Require $vassal 'MilitaryGovernorateStore.TryGetActive(' `
    'Vassal rows must read active governorate state.'
Require $vassal 'VassalSubjectKind.MilitaryGovernorate' `
    'Vassal rows must identify active military governorates.'

foreach ($token in @(
    'aw_military_governorate_marker',
    'aw_military_governorate_label_suzerain',
    'aw_military_governorate_label_seat',
    'aw_military_governorate_label_governor',
    'aw_military_governorate_label_successor',
    'aw_vassal_military'
)) {
    Require $item $token "Governorate vassal row is missing label: $token"
}
foreach ($token in @(
    'OpenSuccessorSelection(',
    'OpenGovernorReplacement(',
    'OpenKingdomRenameFlow('
)) {
    Require $item $token "Governorate context action is missing: $token"
}
Require $vassalWindow 'OpenKingdomRenameFlow(' `
    'Rename must route through the existing kingdom rename flow.'

foreach ($token in @(
    'OpenCreation(',
    'OpenSuccessorSelection(',
    'OpenGovernorReplacement(',
    'MilitaryGovernorateRules.GeneralScanBudget',
    'GeneralService.GetActiveGeneralsForReadModel(',
    'entry.Merit',
    'entry.Loyalty',
    'entry.Ambition',
    'SafeCommand('
)) {
    Require $window $token "Temporary governorate candidate window is missing: $token"
}
foreach ($token in @(
    'actor_id',
    'UiUnitAvatarElement',
    'FamilyTreeNodeView.GetAvatarPrefab()',
    '_livePortrait.show(actor)'
)) {
    Require $candidateItem $token "Candidate row is missing live portrait support: $token"
}
if ($window.Contains('MilitaryGovernorateCreationService.TryCreate(')) {
    throw 'Creation UI must mutate only through the authoritative command.'
}

foreach ($token in @(
    'DesignateMilitaryGovernorateSuccessor',
    'ReplaceMilitaryGovernorateGovernor'
)) {
    Require $models $token "Missing authoritative command model: $token"
    Require $router $token "Missing authoritative command route: $token"
    Require $handler $token "Missing authoritative command handler: $token"
}
Require $handler 'MilitaryGovernorateSuccessionService.TryDesignate(' `
    'Designation command must call the domain API.'
Require $handler 'MilitaryGovernorateSuccessionService.TryReplaceGovernor(' `
    'Replacement command must call the domain API.'
Require $handler 'AW3CommandResult.Pending(' `
    'Queued governor replacement must return Pending.'
Require $handler 'reason == "replacement_pending"' `
    'Only queued governor replacement may return Pending.'
Require $succession 'pSubject.setKing(pSuccessor)' `
    'Governor replacement must use native setKing.'
Require $succession 'MILITARY_GOVERNORATE_REPLACEMENT_ALLOWED' `
    'Governor replacement must require a failed rebellion outcome.'
Require $succession 'MilitaryGovernorateStore.SetSuccessor(' `
    'Governor replacement must persist a recovery anchor before mutation.'
Require $succession 'Commit(pSubject, pSuzerain, state, pGovernor)' `
    'Governor replacement must reuse recoverable succession commit.'
Require $succession 'EnqueueRecovery(pSubject);' `
    'A partially committed governor replacement must enqueue recovery.'
Require $handler 'request.TargetActorId >= 0' `
    'Combined governor-and-successor replacement requests must be rejected.'
$replaceRequestStart = $models.IndexOf(
    'public static AW3CommandRequest ReplaceMilitaryGovernorateGovernor(')
$replaceRequestEnd = $models.IndexOf(
    'public static AW3CommandRequest ChangeEra(', $replaceRequestStart)
if ($replaceRequestStart -lt 0 -or $replaceRequestEnd -le $replaceRequestStart) {
    throw 'Cannot isolate governor replacement request factory.'
}
if ($models.Substring($replaceRequestStart,
        $replaceRequestEnd - $replaceRequestStart).Contains('targetActorId')) {
    throw 'Governor replacement must not combine successor designation.'
}
Require $item 'string governorateDetails' `
    'Governorate fields must be visible in the relation row.'
Require $window '_mode = WindowMode.Successor' `
    'Successful governor replacement must offer successor selection.'
Require $window 'AW3MultiplayerCommandFacade.Changed +=' `
    'Pending multiplayer replacement must observe authoritative completion.'
Require $window '_pendingReplacement' `
    'Pending replacement state must be tracked.'
Require $window 'ResetPendingContext();' `
    'Opening a governorate selector must clear stale pending context.'
Require $item '_labelRect.offsetMax' `
    'Relation details must reserve space for context buttons.'
Require $item 'VerticalWrapMode.Truncate' `
    'Long localized relation details must not overflow the row.'
foreach ($token in @(
    'aw_military_governorate_designate_successor_short',
    'aw_military_governorate_replace_governor_short',
    'aw_military_governorate_rename_short',
    'new TooltipData'
)) {
    Require $item $token "Governorate row action lacks compact tooltip UI: $token"
}

Require $stateTable '[TableItemDef(pDefaultValue: "0")]' `
    'Governor replacement permission needs a migration-safe default.'
Require $stateTable 'public int replacement_allowed = 0;' `
    'Governor replacement permission must have an authoritative DB field.'
foreach ($token in @(
    'REPLACEMENT_ALLOWED',
    'SetReplacementAllowed(',
    'ReplacementAllowed = !pReader.IsDBNull(11) &&',
    'MILITARY_GOVERNORATE_REPLACEMENT_ALLOWED,'
    'pReplacementAllowed'
)) {
    Require $store $token "Governor replacement permission is not persisted: $token"
}
$clearStart = $store.IndexOf('private static void ClearProjection(')
$clearEnd = $store.IndexOf('private static Kingdom FindKingdom(', $clearStart)
if ($clearStart -lt 0 -or $clearEnd -le $clearStart -or
    -not $store.Substring($clearStart, $clearEnd - $clearStart).Contains(
        'MILITARY_GOVERNORATE_REPLACEMENT_ALLOWED')) {
    throw 'Clearing a governorate projection leaves replacement permission cached.'
}
Require $vassal 'MilitaryGovernorateStore.SetReplacementAllowed(' `
    'Failed-rebellion replacement permission must be written to the DB.'
Require $renamePatch 'MilitaryGovernorateStore.SetCommandName(' `
    'Native kingdom rename must update the active governorate command name.'
Require $replication 'public const int CatalogVersion = 2;' `
    'Adding authoritative commands must bump the multiplayer catalog version.'

$headerStart = $candidateItem.IndexOf('if (pObject != null && pObject.is_header)')
$headerReturn = $candidateItem.IndexOf('return;', $headerStart)
$portraitHide = $candidateItem.IndexOf(
    '_livePortrait.gameObject.SetActive(false)', $headerStart)
if ($headerStart -lt 0 -or $headerReturn -lt 0 -or
    $portraitHide -lt $headerStart -or $portraitHide -gt $headerReturn) {
    throw 'A recycled candidate header can retain the previous live portrait.'
}

$guarded = $window + "`n" + $item + "`n" + $vassal + "`n" +
    $succession + "`n" + $handler
if ($guarded -match 'foreach\s*\([^)]*\bin\s+World\.world\??\.units' -or
    $guarded -match 'World\.world\??\.units\s*\.\s*(ToList|ToArray|GetEnumerator)') {
    throw 'Governorate UI/management contains a global actor scan.'
}

$managementWindows = Get-ChildItem -LiteralPath (Join-Path $root `
    'Code\ui\windows') -Filter '*MilitaryGovernorate*Window.cs'
if ($managementWindows.Count -ne 1 -or
    $managementWindows[0].Name -ne 'MilitaryGovernorateWindow.cs') {
    throw 'A separate military governorate management window was added.'
}

foreach ($key in @(
    'aw_military_governorate_label_suzerain',
    'aw_military_governorate_label_seat',
    'aw_military_governorate_label_governor',
    'aw_military_governorate_label_successor',
    'aw_military_governorate_designate_successor',
    'aw_military_governorate_replace_governor',
    'aw_military_governorate_clear_successor',
    'aw_military_governorate_rename',
    'aw_military_governorate_designate_successor_short',
    'aw_military_governorate_replace_governor_short',
    'aw_military_governorate_rename_short',
    'aw_military_governorate_failure_replacement_not_allowed'
)) {
    if ($locales -notmatch ('(?m)^' + [regex]::Escape($key) +
            ',[^,]+,[^,]+,[^,]+\s*$')) {
        throw "Missing zh/en/traditional localization: $key"
    }
}

$invalidGeneralCount = [regex]::Matches($locales,
    '(?m)^aw_military_governorate_failure_invalid_general,').Count
if ($invalidGeneralCount -ne 1) {
    throw 'Governorate invalid-general localization must have one CSV owner.'
}

Write-Output 'Military governorate UI source guard passed.'
