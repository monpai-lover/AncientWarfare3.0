$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

function Read-Source([string] $relativePath) {
    return [IO.File]::ReadAllText((Join-Path $root $relativePath))
}

function Require([string] $source, [string] $pattern, [string] $message) {
    if ($source -notmatch $pattern) { throw $message }
}

function Reject([string] $source, [string] $pattern, [string] $message) {
    if ($source -match $pattern) { throw $message }
}

function Source-Segment([string] $source, [string] $startMarker,
        [string] $endMarker) {
    $start = $source.IndexOf($startMarker, [StringComparison]::Ordinal)
    $end = $source.IndexOf($endMarker, $start + $startMarker.Length,
        [StringComparison]::Ordinal)
    if ($start -lt 0 -or $end -lt 0) {
        throw "Cannot locate source segment $startMarker"
    }
    return $source.Substring($start, $end - $start)
}

$vassal = Read-Source 'Code\core\lineage\VassalService.cs'
$ruler = Read-Source 'Code\core\lineage\RulerAppellationService.cs'
$heir = Read-Source 'Code\core\lineage\HeirTitleRules.cs'
$chronicle = Read-Source 'Code\core\lineage\ChronicleEvents.cs'
$map = Read-Source 'Code\core\policy\VassalMapModeService.cs'
$hierarchical = Read-Source 'Code\core\policy\HierarchicalVassalMapModeService.cs'
$nameplate = Read-Source 'Code\ui\components\VassalNameplateSuzerainFlag.cs'
$succession = Read-Source 'Code\core\lineage\MilitaryGovernorateSuccessionService.cs'
$store = Read-Source 'Code\core\lineage\MilitaryGovernorateStore.cs'
$kingdomWindow = Read-Source 'Code\ui\windows\KingdomWindowAddition.cs'
$kingdomPatch = Read-Source 'Code\patch\AW_KingdomWindowPatch.cs'
$locales = Read-Source 'Locales\others.csv'

Require $vassal 'data\.get\(LineageKeys\.MILITARY_GOVERNORATE_SUBJECT_KIND' `
    'subject kind must come from the cached kingdom-data projection'
Require $ruler 'VassalService\.GetSubjectKind\(pKingdom\)' `
    'ruler presentation must consume cached subject kind'
Require $heir 'VassalService\.GetSubjectKind\(pKingdom\)' `
    'heir presentation must consume cached subject kind'
Require $map 'VassalService\.GetSubjectKind\(pKingdom\)' `
    'vassal map tooltip must consume cached subject kind'
Require $map 'aw_military_governorate_marker' `
    'vassal map tooltip must include the governorate marker legend label'
Require $hierarchical 'GetProjectedStateName\(pKingdom\)' `
    'hierarchical map labels must reuse projected state names'
Require $nameplate 'VassalService\.GetSubjectKind\(pKingdom\)' `
    'nameplate marker must consume cached subject kind'
Require $nameplate 'aw_military_governorate_marker_short' `
    'nameplate marker must use its dedicated localized short label'
Require $nameplate 'markerRect\.anchoredPosition = new Vector2\(flagSize \+ 2f, 0f\)' `
    'nameplate marker must be positioned beside the suzerain flag'
Reject $nameplate 'markerRect\.anchorMin = Vector2\.zero' `
    'nameplate marker must not stretch over the suzerain flag'
Require $chronicle 'HeirTitleRules\.TitleKey\(pKingdom, pMode\)' `
    'heir chronicle must use relationship-aware title selection'
Require $succession 'MILITARY_GOVERNORATE_SUCCESSOR_ACTOR_ID' `
    'designation must project its successor actor id'
Require $succession 'ChronicleEvents\.OnHeirDesignated\(' `
    'governorate designation must record the heir chronicle'
Require $store 'MILITARY_GOVERNORATE_SUCCESSOR_ACTOR_ID' `
    'restoration must project the persisted successor actor id'
Require $kingdomWindow 'GetDesignatedSuccessorForReadModel\(' `
    'kingdom window must display the governorate designated successor'
Require $vassal 'nameplate_manager\?\.clearCaches\(\)' `
    'relation projection changes must invalidate native nameplates'
Require $vassal 'RulerAppellationService\.RemoveKingdom\(' `
    'relation projection changes must invalidate projected state names'
Require $vassal 'MILITARY_GOVERNORATE_SUCCESSOR_ACTOR_ID, -1L' `
    'relation projection clearing must remove the cached successor'
Require $kingdomPatch 'GetDesignatedSuccessorForReadModel\(' `
    'original kingdom stats must display the governorate successor'
Require $nameplate 'pShow \? flagSize \+ MarkerWidth : flagSize' `
    'nameplate layout must reserve marker width only when visible'
Require $nameplate 'RefreshMilitaryMarkerVisual\(\)' `
    'pooled nameplates must refresh marker font and localization'
Require $locales 'aw_military_governorate_ruler,\u5c06\u519b,General,\u5c07\u8ecd' `
    'ruler label localization is required'
Require $locales 'aw_military_governorate_marker,\u519b\u9547,Military governorate,\u8ecd\u93ae' `
    'marker legend localization is required'
Require $locales 'aw_military_governorate_marker_short,\u519b,M,\u8ecd' `
    'marker short-label localization is required'

$rulerPresentation = (Source-Segment $ruler `
    'public static string GetFullLivingAppellation' `
    'public static string GetCompactLivingAppellation') + "`n" + `
    (Source-Segment $ruler 'private static string ResolveProjectedStateName' `
        'private static RulerRank MapRank')
$mapPresentation = Source-Segment $map 'public static string BuildTooltip' `
    'public static void DirtyMap'
$hierarchicalPresentation = Source-Segment $hierarchical `
    'private static string SafeDisplayName' `
    'private static int CompareKingdoms'
$presentation = $rulerPresentation + "`n" + $heir + "`n" + `
    $mapPresentation + "`n" + $hierarchicalPresentation + "`n" + $nameplate
Reject $presentation 'SQLiteCommand|OperatingDB|ExecuteReader' `
    'rendering and update paths must not query SQLite'
Reject $presentation 'foreach\s*\([^\)]*World\.world\??\.kingdoms' `
    'rendering and update paths must not globally scan kingdoms'

Write-Host 'Military governorate presentation source guard passed.'
