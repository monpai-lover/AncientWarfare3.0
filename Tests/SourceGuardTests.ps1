param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    return [System.IO.File]::ReadAllText((Join-Path $root $relativePath))
}

function Require-Absent([string]$name, [string]$relativePath, [string]$needle) {
    $fullPath = Join-Path $root $relativePath
    if (-not [System.IO.File]::Exists($fullPath)) {
        return
    }
    $text = [System.IO.File]::ReadAllText($fullPath)
    if ($text.Contains($needle)) {
        $failures.Add("${name}: found forbidden text '$needle' in $relativePath")
    }
}

function Require-Present([string]$name, [string]$relativePath, [string]$needle) {
    $fullPath = Join-Path $root $relativePath
    if (-not [System.IO.File]::Exists($fullPath)) {
        $failures.Add("${name}: missing source file $relativePath")
        return
    }
    $text = [System.IO.File]::ReadAllText($fullPath)
    if (-not $text.Contains($needle)) {
        $failures.Add("${name}: missing required text '$needle' in $relativePath")
    }
}

$nmlVisibleTestSources = @(
    git -C $root -c core.quotepath=false ls-files -- Tests |
        Where-Object {
            $_.EndsWith('.cs', [System.StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf)
        }
)
if ($nmlVisibleTestSources.Count -gt 0) {
    $failures.Add('NML-visible test sources must use .cs.txt: ' + ($nmlVisibleTestSources -join ', '))
}

Require-Absent 'generic meta-window name patch' 'Code/patch/AW_WorldLogGuardPatch.cs' 'WindowMetaGeneric<War'
Require-Absent 'generic meta-window helper' 'Code/patch/AW_WorldLogGuardPatch.cs' 'MetaWindowSafetyRules'
Require-Absent 'kingdom display-time name repair' 'Code/patch/AW_KingdomWindowPatch.cs' 'nameInput.setText(dataName)'
Require-Absent 'load-time world name scan' 'Code/patch/AW_SavePatch.cs' 'XiaNamingRepair.EnsureWorldNames()'
Require-Absent 'custom tab native sprite overwrite' 'Code/ui/AW_LineageTab.cs' 'ApplyNativeTabSprites'
Require-Absent 'custom tab selected sprite overwrite' 'Code/ui/AW_LineageTab.cs' 'tab_main.image_selected'
Require-Present 'school actor navigation finishes window animation' 'Code/ui/SchoolActorNavigation.cs' 'ScrollWindow.finishAnimations();'
Require-Present 'school actor navigation resolves unit meta selection' 'Code/ui/SchoolActorNavigation.cs' 'MetaType.Unit.getAsset()'
Require-Present 'school actor navigation uses unit meta selection' 'Code/ui/SchoolActorNavigation.cs' 'unitMeta.selectAndInspect('
Require-Absent 'school card avoids legacy unit navigation' 'Code/ui/items/SchoolActorCardView.cs' 'ActionLibrary.openUnitWindow'
Require-Absent 'school master card avoids legacy unit navigation' 'Code/ui/items/SchoolMasterCardView.cs' 'ActionLibrary.openUnitWindow'
Require-Absent 'school roster avoids legacy unit navigation' 'Code/ui/items/SchoolRosterNodeView.cs' 'ActionLibrary.openUnitWindow'
Require-Absent 'anonymous Xia clan placeholder' 'Code/content/XiaNaming.cs' '"无名"'
Require-Absent 'historical master must not force player favorite marker' 'Code/core/schools/HistoricalMasterIdentityProjection.cs' 'pActor.data.favorite = true;'
Require-Absent 'marriage must not copy spouse blood lineage' 'Code/core/lineage/LineageService.cs' 'TryInheritLineageFromSource(pHusband, pWife'
Require-Absent 'lover patch must not mutate blood lineage' 'Code/patch/AW_LoversPatch.cs' 'LineageService.OnBecameLovers'
Require-Absent 'occupation protection must not wait for capture-owner registration' 'Code/core/lineage/ArmyRetreatService.cs' 'pTargetCity.being_captured_by == pAttacker'
Require-Present 'occupation protection reads current capture forces' 'Code/core/lineage/ArmyRetreatService.cs' 'CityOccupationAccelerationService.DescribeCaptureFor('
Require-Present 'stale capture owner adopts active dominant enemy' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'ShouldAdoptDominantCapturer('
Require-Present 'defender defeat attempts immediate city transfer' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'public static bool TryCompleteAfterDefenderDefeat(City pCity)'
Require-Present 'capture prefix can stop stale post-transfer update' 'Code/patch/AW_CityOccupationAccelerationPatch.cs' 'public static bool UpdateCapture_Prefix(City __instance, float pElapsed)'
Require-Present 'capture prefix skips vanilla only after transfer' 'Code/patch/AW_CityOccupationAccelerationPatch.cs' 'if (CityOccupationAccelerationService.TryCompleteAfterDefenderDefeat(__instance)) return false;'
Require-Present 'zone capture scan records actual warriors' 'Code/patch/AW_CityOccupationAccelerationPatch.cs' 'RecordActiveMilitaryPresence(__instance, pObject);'
Require-Present 'zone capture reset clears military presence' 'Code/patch/AW_CityOccupationAccelerationPatch.cs' 'ClearActiveMilitaryPresence(__instance);'
Require-Present 'active defender check uses military presence index' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'HasActiveMilitaryPresence(pCity, pCity.kingdom)'
Require-Present 'instant capture requires exact defender engagement' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'HasDefenderEngagement(pCity, oldOwner, capturer)'
Require-Present 'instant capture forwards defender engagement evidence' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'defenderEngagementObserved))'
Require-Present 'city engagement cache has war-end invalidation' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'public static void OnWarEnded(War pWar)'
Require-Present 'war end clears related city engagement evidence' 'Code/patch/AW_WarPatch.cs' 'CityOccupationAccelerationService.OnWarEnded(pWar);'
Require-Present 'city occupation runtime cache has world reset' 'Code/core/lineage/CityOccupationAccelerationService.cs' 'public static void ClearRuntime()'
Require-Present 'archive switch clears city occupation runtime cache' 'Code/patch/AW_SavePatch.cs' 'CityOccupationAccelerationService.ClearRuntime();'
Require-Present 'royal asylum active key' 'Code/core/lineage/LineageKeys.cs' 'ROYAL_ASYLUM_ACTIVE = "aw_royal_asylum_active"'
Require-Present 'royal asylum home kingdom key' 'Code/core/lineage/LineageKeys.cs' 'ROYAL_ASYLUM_HOME_KINGDOM_ID = "aw_royal_asylum_home_kingdom_id"'
Require-Present 'royal asylum former city key' 'Code/core/lineage/LineageKeys.cs' 'ROYAL_ASYLUM_FORMER_CITY_ID = "aw_royal_asylum_former_city_id"'
Require-Present 'royal asylum host city key' 'Code/core/lineage/LineageKeys.cs' 'ROYAL_ASYLUM_HOST_CITY_ID = "aw_royal_asylum_host_city_id"'
Require-Present 'royal asylum roster key' 'Code/core/lineage/LineageKeys.cs' 'ROYAL_ASYLUM_ROSTER_IDS = "aw_royal_asylum_roster_ids"'
Require-Present 'royal asylum content registration' 'Code/content/XiaContent.cs' 'RoyalAsylumContent.Init();'
Require-Present 'royal asylum dedicated actor job' 'Code/content/RoyalAsylumContent.cs' 'ActorJobId = "aw_royal_asylum_job"'
Require-Present 'royal asylum dedicated roam task' 'Code/content/RoyalAsylumContent.cs' 'RoamTaskId = "aw_royal_asylum_roam"'
Require-Present 'royal asylum status asset' 'Code/content/RoyalAsylumContent.cs' 'StatusId = "aw_royal_asylum"'
Require-Present 'royal asylum roam behavior' 'Code/ai/behaviours/actor/BehRoyalAsylumRoam.cs' 'class BehRoyalAsylumRoam'
Require-Present 'royal asylum roam uses logical host' 'Code/ai/behaviours/actor/BehRoyalAsylumRoam.cs' 'RoyalAsylumService.TryGetRoamTile'
Require-Present 'royal asylum status title locale' 'Locales/others.csv' 'status_title_aw_royal_asylum,'
Require-Present 'royal asylum status description locale' 'Locales/others.csv' 'status_description_aw_royal_asylum,'
Require-Present 'royal asylum roam task locale' 'Locales/others.csv' 'task_unit_aw_royal_asylum_roam,'
Require-Present 'royal asylum host row locale' 'Locales/others.csv' 'aw_royal_asylum_host,'
Require-Present 'royal asylum roster is bounded' 'Code/core/lineage/RoyalAsylumService.cs' 'MaxRosterSize = 64'
Require-Present 'royal asylum reacts to war start' 'Code/core/lineage/RoyalAsylumService.cs' 'public static void OnWarStarted(War pWar)'
Require-Present 'royal asylum reconciles by home kingdom year' 'Code/core/lineage/RoyalAsylumService.cs' 'public static void OnKingdomYear(Kingdom pHome)'
Require-Present 'royal asylum runtime reload exists' 'Code/core/lineage/RoyalAsylumService.cs' 'public static void LoadRuntimeState()'
Require-Present 'royal asylum runtime reset exists' 'Code/core/lineage/RoyalAsylumService.cs' 'public static void ClearRuntime()'
Require-Present 'royal asylum evacuation removes formal city only' 'Code/core/lineage/RoyalAsylumService.cs' 'pActor.setCity(null);'
Require-Present 'royal asylum evacuation verifies retained nationality' 'Code/core/lineage/RoyalAsylumService.cs' 'pActor.kingdom != pHome'
Require-Absent 'royal asylum cannot set a foreign formal city' 'Code/core/lineage/RoyalAsylumService.cs' 'setCity(pHost'
Require-Absent 'royal asylum cannot scan every actor' 'Code/core/lineage/RoyalAsylumService.cs' 'foreach (Actor actor in World.world.units)'
Require-Present 'war start invokes royal asylum' 'Code/patch/AW_WarPatch.cs' 'RoyalAsylumService.OnWarStarted(__result);'
Require-Present 'kingdom year invokes royal asylum' 'Code/patch/AW_KingdomPolicyPatch.cs' 'RoyalAsylumService.OnKingdomYear(__instance);'
Require-Present 'archive load rebuilds royal asylum runtime' 'Code/patch/AW_SavePatch.cs' 'RoyalAsylumService.LoadRuntimeState();'
Require-Present 'archive switch clears royal asylum runtime' 'Code/patch/AW_SavePatch.cs' 'RoyalAsylumService.ClearRuntime();'
Require-Present 'royal asylum extinction naturalization exists' 'Code/core/lineage/RoyalAsylumService.cs' 'public static void NaturalizeBeforeExtinction(Kingdom pHome)'
Require-Present 'royal asylum extinction uses formal host join' 'Code/core/lineage/RoyalAsylumService.cs' 'actor.joinCity(hostCity);'
Require-Present 'naturalized refugee leaves extinct kingdom unit cache immediately' 'Code/core/lineage/RoyalAsylumService.cs' 'pHome.units.Remove(actor);'
Require-Present 'failed extinction asylum cannot survive nomad conversion' 'Code/core/lineage/RoyalAsylumService.cs' 'CloseBeforeNomadFallback(actor, pHome);'
Require-Present 'kingdom extinction invokes asylum naturalization' 'Code/patch/AW_KingdomExtinctionPatch.cs' 'RoyalAsylumService.NaturalizeBeforeExtinction(__instance);'
Require-Present 'ordinary enlistment rejects royal refugees' 'Code/patch/AW_EnlistPatch.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'direct warrior promotion rejects royal refugees' 'Code/patch/AW_EnlistPatch.cs' 'SetProfession_Asylum_Prefix'
Require-Present 'special army attachment rejects royal refugees' 'Code/core/lineage/AWArmyService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'royal refugee next job stays asylum job' 'Code/patch/AW_EnlistPatch.cs' '__result = RoyalAsylumContent.ActorJobId;'
Require-Present 'city leader selection rejects royal refugees' 'Code/patch/AW_CityLeaderPatch.cs' 'RoyalAsylumService.IsActive(pUnit)'
Require-Present 'court appointment rejects royal refugees' 'Code/core/court/CourtService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'general selection rejects royal refugees' 'Code/core/lineage/GeneralService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'royal guard selection rejects royal refugees' 'Code/core/lineage/RoyalGuardService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'slave army selection rejects royal refugees' 'Code/core/lineage/SlaveService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'new heir is recalled from royal asylum' 'Code/core/lineage/HeirService.cs' 'RoyalAsylumService.RecallForSuccession(heir, pKingdom);'
Require-Present 'new king is recalled from royal asylum' 'Code/patch/AW_PromotionPatch.cs' 'RoyalAsylumService.RecallForSuccession(pActor, __instance)'
Require-Present 'royal asylum started person event key' 'Code/core/lineage/ChronicleKeys.cs' 'ROYAL_ASYLUM_STARTED = "royal_asylum_started"'
Require-Present 'royal asylum relocated person event key' 'Code/core/lineage/ChronicleKeys.cs' 'ROYAL_ASYLUM_RELOCATED = "royal_asylum_relocated"'
Require-Present 'royal asylum returned person event key' 'Code/core/lineage/ChronicleKeys.cs' 'ROYAL_ASYLUM_RETURNED = "royal_asylum_returned"'
Require-Present 'royal asylum naturalized person event key' 'Code/core/lineage/ChronicleKeys.cs' 'ROYAL_ASYLUM_NATURALIZED = "royal_asylum_naturalized"'
Require-Present 'royal asylum history service exists' 'Code/core/lineage/RoyalAsylumHistoryService.cs' 'internal static class RoyalAsylumHistoryService'
Require-Present 'evacuation records asylum start once' 'Code/core/lineage/RoyalAsylumService.cs' 'RoyalAsylumHistoryService.RecordStarted(pActor, pHome, hostCity);'
Require-Present 'host change records asylum relocation once' 'Code/core/lineage/RoyalAsylumService.cs' 'RoyalAsylumHistoryService.RecordRelocated(pActor, pHome, hostCity);'
Require-Present 'return records asylum completion once' 'Code/core/lineage/RoyalAsylumService.cs' 'RoyalAsylumHistoryService.RecordReturned(pActor, pHome, destination);'
Require-Present 'extinction records asylum naturalization once' 'Code/core/lineage/RoyalAsylumService.cs' 'RoyalAsylumHistoryService.RecordNaturalized(actor, homeName, host, hostCity);'
Require-Present 'history window localizes asylum event labels' 'Code/core/lineage/WarDisplayLabelRules.cs' 'case "royal_asylum_started"'
Require-Present 'royal asylum started history localization' 'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_event_royal_asylum_started'
Require-Present 'royal asylum relocated history localization' 'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_event_royal_asylum_relocated'
Require-Present 'royal asylum returned history localization' 'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_event_royal_asylum_returned'
Require-Present 'royal asylum naturalized history localization' 'Code/core/lineage/HistoryLocalizationRules.cs' 'aw_hist_event_royal_asylum_naturalized'
Require-Present 'actor window shows logical asylum host' 'Code/patch/AW_UnitWindowPatch.cs' 'RoyalAsylumService.ResolveHostCity(actor)'
Require-Present 'actor window labels asylum host row' 'Code/patch/AW_UnitWindowPatch.cs' 'ShowRawRow(__instance, "aw_royal_asylum_host"'
Require-Present 'empty target city attack uses target zones' 'Code/core/lineage/CityAttackZoneService.cs' 'targetCity.zones.GetRandom()'
Require-Absent 'empty target city attack cannot use source zones' 'Code/core/lineage/CityAttackZoneService.cs' 'pSourceCity.zones.GetRandom()'
Require-Absent 'heir minimap cannot scan every kingdom unit' 'Code/patch/AW_HeirMinimapPatch.cs' 'foreach (Actor unit in kingdom.getUnits())'
Require-Present 'heir minimap uses stored heir index without succession mutation' 'Code/patch/AW_HeirMinimapPatch.cs' 'Actor unit = HeirService.PeekStoredHeirForMinimap(kingdom);'
Require-Present 'heir minimap resolves current visual affiliation' 'Code/patch/AW_HeirMinimapPatch.cs' 'HeirMinimapVisualRules.ResolveVisualKingdomId('
Require-Present 'heir minimap colors from resolved affiliation' 'Code/patch/AW_HeirMinimapPatch.cs' 'DynamicSprites.getIcon(baseIcon, visualKingdom.getColor())'
Require-Present 'heir minimap display lookup exists' 'Code/core/lineage/HeirService.cs' 'public static Actor PeekStoredHeirForMinimap(Kingdom pKingdom)'
Require-Present 'heir flag clear counts other live registrations' 'Code/core/lineage/HeirService.cs' 'CountOtherLiveHeirRegistrations(oldId, pKingdom)'
Require-Present 'heir flag clear applies shared registration rule' 'Code/core/lineage/HeirService.cs' 'HeirRegistrationRules.ShouldClearGlobalFlag(otherRegistrations)'
Require-Present 'heir registration scan applies tested eligibility rule' 'Code/core/lineage/HeirService.cs' 'HeirRegistrationRules.CountsAsOtherLiveRegistration('
Require-Absent 'heir minimap trusts kingdom registration instead of global actor flag' 'Code/core/lineage/HeirService.cs' 'heir.data.get(LineageKeys.IS_HEIR'
Require-Present 'heir minimap follows king marker visibility option' 'Code/patch/AW_HeirMinimapPatch.cs' 'PlayerConfig.optionBoolEnabled("map_kings_leaders")'
Require-Present 'heir minimap uses complete visibility rule' 'Code/patch/AW_HeirMinimapPatch.cs' 'HeirMinimapVisualRules.ShouldDrawIcon('
Require-Present 'heir minimap bounds quantum sprite growth' 'Code/patch/AW_HeirMinimapPatch.cs' 'if (createdThisFrame > 2) break;'
Require-Present 'heir minimap reuses one actor marker index' 'Code/patch/AW_HeirMinimapPatch.cs' 'private static readonly HashSet<long> DrawnHeirActorIds'
Require-Present 'heir minimap resets marker index once per draw' 'Code/patch/AW_HeirMinimapPatch.cs' 'DrawnHeirActorIds.Clear();'
Require-Present 'heir minimap deduplicates cross-realm registrations' 'Code/patch/AW_HeirMinimapPatch.cs' 'MinimapActorMarkerRules.TryReserve(DrawnHeirActorIds, unit.data.id)'
Require-Present 'historical figures preserve intended favorite protection' 'Code/content/figures/HistoricalFigureService.cs' 'pActor.data.favorite = true;'
Require-Present 'historical figure replaces favorite draw in one pass' 'Code/patch/AW_FigurePatch.cs' 'public static bool DrawFavoritesMap_Figure_Prefix(QuantumSpriteAsset pAsset)'
Require-Absent 'historical figure cannot append a second favorite marker pass' 'Code/patch/AW_FigurePatch.cs' 'DrawFavoritesMap_Figure_Postfix'
Require-Present 'historical figure replacement preserves ordinary favorite star' 'Code/patch/AW_FigurePatch.cs' 'SpriteTextureLoader.getSprite("ui/Icons/iconFavoriteStar_Map")'
Require-Present 'historical figure replacement reuses visible favorite index' 'Code/patch/AW_FigurePatch.cs' 'World.world.units.visible_units_with_favorite.array'
Require-Present 'historical figure minimap follows favorite marker option' 'Code/patch/AW_FigurePatch.cs' 'PlayerConfig.optionBoolEnabled("marks_favorites")'
Require-Present 'historical figure minimap resolves current visual affiliation' 'Code/patch/AW_FigurePatch.cs' 'HeirMinimapVisualRules.ResolveVisualKingdomId('
Require-Present 'historical figure minimap colors from resolved affiliation' 'Code/patch/AW_FigurePatch.cs' 'DynamicSprites.getIcon(baseIcon, visualKingdom.getColor())'
Require-Present 'historical figure minimap uses authoritative FigureState identity' 'Code/patch/AW_FigurePatch.cs' 'FigureStateStore.IndexOfActor(unit.data.id) >= 0'
Require-Absent 'historical figure minimap cannot trust figure trait alone' 'Code/patch/AW_FigurePatch.cs' 'unit.hasTrait(HistoricalFigureService.TRAIT_FIGURE)'
Require-Absent 'historical figure minimap cannot trust first trait alone' 'Code/patch/AW_FigurePatch.cs' 'unit.hasTrait(HistoricalFigureService.TRAIT_FIRST)'
Require-Absent 'king is never persisted as a court officer' 'Code/core/court/CourtService.cs' '"king_council"'
Require-Present 'new king clears prior court office' 'Code/patch/AW_HeirPatch.cs' 'CourtService.ClearOfficeForReignTransition(king, "became_king")'
Require-Present 'abdication closes prior court office' 'Code/patch/AW_AbdicatePatch.cs' 'CourtService.ClearOfficeForReignTransition(__state, "abdicated")'
Require-Present 'aristocratic succession service exists' 'Code/core/lineage/AristocraticSuccessionService.cs' 'internal static class AristocraticSuccessionService'
Require-Present 'vacancy resolver selects a noble house' 'Code/core/lineage/RepublicGovernmentService.cs' 'AristocraticSuccessionService.SelectRuler(pKingdom)'
Require-Present 'house accession records clan fallback mode' 'Code/core/lineage/RepublicGovernmentService.cs' 'HeirService.MarkClanFallbackSuccession(pKingdom, houseRuler)'
Require-Present 'royal-clan path uses unified vacancy resolver' 'Code/patch/AW_HeirPatch.cs' 'RepublicGovernmentService.ResolveRulerForVacancy(pKingdom)'
Require-Absent 'house selection cannot mutate class policy' 'Code/core/lineage/AristocraticSuccessionService.cs' 'POLICY_CLASS_STATE'
Require-Present 'vassal settlement requires surviving defender cities' 'Code/core/lineage/VassalService.cs' 'pVassal.hasCities()'
Require-Present 'independence war records service suspension' 'Code/core/lineage/VassalService.cs' 'BeginIndependenceSuspension(pWar, attacker, defender);'
Require-Present 'independence war leaves old suzerain wars' 'Code/core/lineage/VassalService.cs' 'LeaveSuzerainWarsForIndependence(pWar, attacker, defender);'
Require-Present 'independence settlement clears service suspension' 'Code/core/lineage/VassalService.cs' 'EndIndependenceSuspension(pWar, attacker);'
Require-Present 'yearly vassal pull checks independence suspension' 'Code/core/lineage/VassalService.cs' 'HasActiveIndependenceSuspension(vassal, pSuzerain)'
Require-Absent 'alliance constructor cannot return null from AW prefix' 'Code/patch/AW_VassalDiplomacyPatch.cs' 'NewAlliance_Prefix'
Require-Present 'alliance plot filters vassal target before construction' 'Code/patch/AW_VassalDiplomacyPatch.cs' 'GetAllianceTarget_Postfix'
Require-Present 'alliance plot uses tested vassal permission rule' 'Code/patch/AW_VassalDiplomacyPatch.cs' 'VassalWarPermissionRules.CanUseAlliancePlot('
Require-Present 'school extinction transfer rule exists' 'Code/core/schools/SchoolAffiliationTransferRules.cs' 'AllowsExtinctionRelease('
Require-Present 'school affiliation guard applies extinction release' 'Code/core/schools/HistoricalAffiliationService.cs' 'SchoolAffiliationTransferRules.AllowsExtinctionRelease('
Require-Present 'school extinction release waits for stable city index' 'Code/core/schools/HistoricalAffiliationService.cs' '!manager.hasDirtyCities()'
Require-Present 'school extinction release targets actor wild kingdom' 'Code/core/schools/HistoricalAffiliationService.cs' 'pTarget.asset.id == pActor.asset.kingdom_id_wild'

$lineage = Read-Source 'Code/core/lineage/LineageService.cs'
$branchStart = $lineage.IndexOf('public static void OnKingFoundBranch(', [System.StringComparison]::Ordinal)
$newClan = $lineage.IndexOf('newClan(pKing', $branchStart, [System.StringComparison]::Ordinal)
$freezeShi = $lineage.IndexOf('GenerateShiName(pKing)', $branchStart, [System.StringComparison]::Ordinal)
if ($branchStart -lt 0 -or $newClan -lt 0 -or $freezeShi -lt 0 -or $freezeShi -gt $newClan) {
    $failures.Add('king-founded branch must resolve its shi before newClan(pKing)')
}

$vacancy = Read-Source 'Code/core/lineage/RepublicGovernmentService.cs'
$houseSelection = $vacancy.IndexOf('AristocraticSuccessionService.SelectRuler(pKingdom)', [System.StringComparison]::Ordinal)
$setRepublic = $vacancy.IndexOf('SetRepublic(pKingdom)', [System.StringComparison]::Ordinal)
if ($houseSelection -lt 0 -or $setRepublic -lt 0 -or $houseSelection -gt $setRepublic) {
    $failures.Add('aristocratic house selection must run before SetRepublic(pKingdom)')
}

$contentPath = 'Code/content/schools/HistoricalSchoolContent.cs'
$academyContentPath = 'Code/content/schools/SchoolAcademyBuildingContent.cs'
$academyConstructionPath = 'Code/core/schools/HistoricalSchoolAcademyConstructionService.cs'
Require-Present 'academy building id' $academyContentPath 'BuildingId = "academy_Xia"'
Require-Present 'academy unique building type' $academyContentPath 'BuildingTypeId = "type_aw_school_academy"'
Require-Present 'academy clones Xia library' $academyContentPath 'AssetManager.buildings.clone(BuildingId, "library_Xia")'
Require-Present 'academy keeps book storage' $academyContentPath 'academy.book_slots = source.book_slots;'
Require-Present 'academy uses requested 1.45x scale' $academyContentPath 'new Vector3(0.07975f, 0.07975f, 0.25f)'
Require-Present 'academy uses stable footprint' $academyContentPath 'new BuildingFundament(3, 3, 2, 0)'
Require-Present 'academy replaces Xia library order' $academyContentPath 'pArchitecture.addBuildingOrderKey("order_library", BuildingId);'
Require-Present 'academy registration follows Xia building generation' 'Code/content/XiaArchitecture.cs' 'SchoolAcademyBuildingContent.Init(Xia);'
foreach ($academySprite in @('construction_0.png', 'main_0.png', 'mini_0.png', 'ruin_0.png')) {
    $academySpritePath = Join-Path $root "GameResources/buildings/civ_main/Xia/academy_Xia/$academySprite"
    if (-not [System.IO.File]::Exists($academySpritePath)) {
        $failures.Add("academy sprite missing: $academySprite")
    }
}
Require-Present 'academy minimap crop metadata' 'GameResources/buildings/civ_main/Xia/academy_Xia/sprites.json' '"Path": "mini_0.png"'
Require-Present 'academy minimap crop width' 'GameResources/buildings/civ_main/Xia/academy_Xia/sprites.json' '"RectW": 7'
Require-Present 'academy minimap crop height' 'GameResources/buildings/civ_main/Xia/academy_Xia/sprites.json' '"RectH": 6'
Require-Present 'academy construction uses committed-descent event' 'Code/core/schools/HistoricalSchoolDescentService.cs' 'HistoricalSchoolAcademyConstructionService.TryStart(pHome);'
Require-Present 'academy construction service exists' $academyConstructionPath 'internal static class HistoricalSchoolAcademyConstructionService'
Require-Present 'academy construction rejects duplicate asset id' $academyConstructionPath 'pCity.countBuildingsOfID(SchoolAcademyBuildingContent.BuildingId)'
Require-Present 'academy construction rejects duplicate type' $academyConstructionPath 'pCity.countBuildingsType(SchoolAcademyBuildingContent.BuildingTypeId,'
Require-Present 'academy construction applies pure eligibility rule' $academyConstructionPath 'SchoolAcademyConstructionRules.ShouldStart('
Require-Present 'academy construction bounds zone checks' $academyConstructionPath 'MaxZonesToInspect = 24'
Require-Present 'academy construction bounds tile checks' $academyConstructionPath 'MaxTilesPerZone = 8'
Require-Present 'academy construction rotates bounded retry windows' $academyConstructionPath 'SchoolAcademyConstructionRules.ZoneStartIndex('
Require-Present 'academy construction validates original footprint' $academyConstructionPath 'World.world.buildings.canBuildFrom('
Require-Present 'academy construction creates original building entity' $academyConstructionPath 'World.world.buildings.addBuilding('
Require-Present 'academy construction assigns current city kingdom' $academyConstructionPath 'building.setKingdom(pCity.kingdom);'
Require-Present 'academy construction starts unfinished site' $academyConstructionPath 'building.setUnderConstruction();'
Require-Present 'academy construction runtime claim is cleared' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolAcademyConstructionService.ClearRuntime();'
Require-Absent 'academy construction cannot scan all world cities' $academyConstructionPath 'World.world.cities'
Require-Absent 'academy registration diagnostics removed' $academyContentPath '[academy diagnostic]'
Require-Absent 'academy city diagnostics removed' 'Code/core/schools/HistoricalSchoolAcademyService.cs' '[academy diagnostic]'
Require-Present 'academy venue source installation' $contentPath 'HistoricalSchoolAcademyService.Init();'
Require-Present 'academy indexed city lookup' 'Code/core/schools/HistoricalSchoolAcademyService.cs' 'pCity.getBuildingOfType('
Require-Absent 'academy lookup cannot scan city buildings' 'Code/core/schools/HistoricalSchoolAcademyService.cs' 'foreach (Building'
Require-Absent 'academy cleanup cannot require living actor data' 'Code/core/schools/HistoricalSchoolAcademyService.cs' 'if (pActor?.data == null || pAcademy == null) return;'
Require-Present 'academic work has no outdoor fallback' 'Code/core/schools/HistoricalSchoolVenueProvider.cs' 'HistoricalSchoolVenueRules.RequiresAcademy(pKind)'
Require-Present 'venue selection carries academy building' 'Code/core/schools/HistoricalSchoolVenueProvider.cs' 'out Building pAcademy'
Require-Present 'venue claim carries academy building' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'public Building Academy { get; }'
Require-Present 'same academy debate layout rule' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'HistoricalSchoolVenueRules.IsDebateLayoutValid('
Require-Present 'same academy tile reserved only once' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'secondary != null && secondary != primary'
Require-Present 'lecture prepares academy building target' 'Code/ai/behaviours/actor/BehHistoricalSchoolLecture.cs' 'out Building academy'
Require-Present 'lecture assigns academy building target' 'Code/ai/behaviours/actor/BehHistoricalSchoolLecture.cs' 'pActor.beh_building_target = academy;'
Require-Absent 'lecture cannot move to a bare venue tile' 'Code/ai/behaviours/actor/BehHistoricalSchoolLecture.cs' 'pActor.beh_tile_target = target;'
Require-Present 'lecture walks to academy building' $contentPath 'lecture.addBeh(new BehGoToBuildingTarget());'
Require-Present 'lecture stays inside academy' $contentPath 'lecture.addBeh(new BehStayInBuildingTarget(4f, 7f));'
Require-Present 'lecture completion requires exact academy interior' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolAcademyService.IsInside(pActor, activity.Venue?.Academy)'
Require-Present 'lecture terminal path exits academy' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolAcademyService.Exit(actor, pActivity.Venue?.Academy);'
Require-Present 'debate prepares academy building target' 'Code/ai/behaviours/actor/BehHistoricalSchoolDebate.cs' 'out Building academy'
Require-Present 'debate assigns academy building target' 'Code/ai/behaviours/actor/BehHistoricalSchoolDebate.cs' 'pActor.beh_building_target = academy;'
Require-Absent 'debate cannot move to a bare venue tile' 'Code/ai/behaviours/actor/BehHistoricalSchoolDebate.cs' 'pActor.beh_tile_target = target;'
Require-Present 'first debater walks to academy building' $contentPath 'travel.addBeh(new BehGoToBuildingTarget());'
Require-Present 'first debater enters academy before debate task' $contentPath 'travel.addBeh(new BehStayInBuildingTarget(0f, 0f));'
Require-Present 'second debater stays inside academy' $contentPath 'receiving.addBeh(new BehStayInBuildingTarget(4f, 7f));'
Require-Present 'debate completion requires exact academy interior' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'HistoricalSchoolAcademyService.IsInside(pActor, activity.Venue?.Academy)'
Require-Present 'debate first terminal path exits academy' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'HistoricalSchoolAcademyService.Exit(first, pActivity.Venue?.Academy);'
Require-Present 'debate second terminal path exits academy' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'HistoricalSchoolAcademyService.Exit(second, pActivity.Venue?.Academy);'
Require-Present 'lecture task id' $contentPath 'LectureTaskId = "aw_historical_school_lecture"'
Require-Present 'debate travel task id' $contentPath 'DebateTravelTaskId = "aw_historical_school_debate_travel"'
Require-Present 'debate task id' $contentPath 'DebateTaskId = "aw_historical_school_debate"'
Require-Present 'debate receiver task id' $contentPath 'DebateReceivingTaskId ='
Require-Present 'debate receiver task value' $contentPath '"aw_historical_school_debate_receiving"'

$localePath = 'Locales/others.csv'
Require-Present 'academy asset locale' $localePath 'academy_Xia,'
Require-Present 'academy building locale' $localePath 'building_academy_Xia,'
Require-Present 'academy type locale' $localePath 'type_aw_school_academy,'
Require-Present 'academy generic locale' $localePath 'aw_school_academy,'
Require-Present 'lecture task locale' $localePath 'task_unit_aw_historical_school_lecture,'
Require-Present 'debate travel task locale' $localePath 'task_unit_aw_historical_school_debate_travel,'
Require-Present 'debate task locale' $localePath 'task_unit_aw_historical_school_debate,'
Require-Present 'debate receiver task locale' $localePath 'task_unit_aw_historical_school_debate_receiving,'

Require-Present 'frame activity queue' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolActivityQueue.ProcessFrame()'
Require-Present 'school-level canonical master slots' 'Code/core/schools/HistoricalSchoolDescentService.cs' 'HistoricalSchoolActiveMasterSlots'
Require-Present 'deferred school maintenance schedule' 'Code/core/schools/HistoricalSchoolActionService.cs' 'ScheduleDeferredActions(pYear)'
Require-Present 'deferred school maintenance frame step' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolActionService.ProcessDeferredFrame()'
Require-Absent 'per-teacher city resident scan' 'Code/core/schools/HistoricalSchoolActionService.cs' 'foreach (Actor actor in pCity.units)'
Require-Present 'school activity save flush' 'Code/patch/AW_SavePatch.cs' 'HistoricalSchoolActivityQueue.FlushPendingPersistenceForSave()'
Require-Present 'lecture ready save flush' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolDebateActivityService.FlushPendingPersistenceForSave()'
Require-Present 'debate ready save flush' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'FlushPendingPersistenceForSave()'
Require-Absent 'debate unknown persistence discard' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' '++ready.Attempts >= 3'
Require-Absent 'school activity city-center fallback' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'result.Add(center)'
Require-Present 'city-change activity task restoration' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'CancelActor(__instance, pRestoreActor: true)'
Require-Present 'death activity cleanup without restoration' 'Code/core/schools/SchoolMembershipService.cs' 'CancelActor(pActor, pRestoreActor: false)'
Require-Present 'lecture excludes active debate actors' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'HistoricalSchoolDebateActivityService.IsActorBusy(actor.data.id)'
Require-Present 'debate excludes active lecture actors' 'Code/core/schools/HistoricalSchoolDebateService.cs' 'HistoricalSchoolActivityQueue.IsLectureActorBusy(pActor.data.id)'
Require-Present 'pending master requires slot attachment' 'Code/core/schools/HistoricalSchoolDescentService.cs' 'if (!ActiveMasterSlots.TryAttachActor(pMaster.SchoolId, pMaster.Id, actorId))'
Require-Present 'nearby lecture completion effect' 'Code/core/schools/HistoricalSchoolActionService.cs' 'EffectsLibrary.spawnAtTileRandomScale("fx_experience_gain"'

Require-Absent 'school updateAge synchronous runner' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'HistoricalSchoolRuntime.OnWorldYear()'
Require-Absent 'school yearly enqueue cannot run once per kingdom' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'typeof(Kingdom), nameof(Kingdom.updateAge)'
Require-Present 'school yearly enqueue runs once per world year' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'HarmonyPatch(typeof(MapBox), "updateObjectAge")'
Require-Absent 'school frame stopwatch allocation' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'Stopwatch.StartNew()'
Require-Absent 'per-frame activity LINQ ordering' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' '.OrderBy('
Require-Absent 'per-frame debate distinct scan' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' '.Distinct()'
Require-Absent 'permanent scholar job restoration' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'RestoreScholarJob('
Require-Absent 'direct scholar travel task replacement' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'pActor.setTask('
Require-Absent 'lecture requires vanilla city equality' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'pActor.city?.data?.id == residence.data.id'
Require-Absent 'inactive school map rebuild' 'Code/core/policy/SchoolMapModeService.cs' 'IsActive() ? 4 : 1'
Require-Absent 'dirty city batch array' 'Code/core/court/CitySchoolSnapshotService.cs' 'Dirty.TakeBatch('
Require-Present 'dirty city dequeue' 'Code/core/court/CitySchoolSnapshotService.cs' 'source.TryDequeue('
Require-Absent 'global school resident rebuild' 'Code/core/court/CitySchoolSnapshotService.cs' 'SchoolMembershipService.ActiveMemberships()'
Require-Absent 'obsolete global resident index cache' 'Code/core/court/CitySchoolResidentIndexRules.cs' 'class CitySchoolResidentIndexCache'
Require-Present 'indexed city residents' 'Code/core/court/CitySchoolSnapshotService.cs' 'HistoricalSchoolRuntimeIndex.Instance.ResidentIds('
Require-Present 'bottom bar visibility gate' 'Code/core/policy/SchoolMapBottomBarController.cs' '_visibleOrPending'
Require-Absent 'global roster membership revision' 'Code/core/schools/SchoolRosterReadModelService.cs' 'SchoolMembershipService.Version'
Require-Absent 'global roster residence revision' 'Code/core/schools/SchoolRosterReadModelService.cs' 'HistoricalAffiliationService.ResidenceRevision'
Require-Absent 'global roster lecture revision' 'Code/core/schools/SchoolRosterReadModelService.cs' 'HistoricalSchoolStore.LectureRevision'
Require-Absent 'obsolete global lecture revision counter' 'Code/core/schools/HistoricalSchoolStore.cs' '_lectureRevision'
Require-Absent 'obsolete global residence revision facade' 'Code/core/schools/HistoricalAffiliationService.cs' 'public static long ResidenceRevision'
Require-Absent 'obsolete global residence revision counter' 'Code/core/schools/HistoricalSchoolRevisionService.cs' '_residenceRevision'
Require-Absent 'global roster disciple count scan' 'Code/core/schools/SchoolRosterReadModelService.cs' 'SchoolLineageService.BuildDirectDiscipleCounts()'
Require-Present 'indexed roster disciple count' 'Code/core/schools/SchoolRosterReadModelService.cs' 'HistoricalSchoolRuntimeIndex.Instance.DirectDiscipleCount('
Require-Present 'narrow roster revision stamp' 'Code/core/schools/SchoolRosterReadModelService.cs' 'HistoricalSchoolRosterRevisionStamp.Capture('
Require-Absent 'school overview global membership revision' 'Code/ui/windows/SchoolWindow.cs' 'SchoolMembershipService.Version'
Require-Absent 'school overview global lecture revision' 'Code/ui/windows/SchoolWindow.cs' 'HistoricalSchoolStore.LectureRevision'
Require-Present 'school overview selected-school revision' 'Code/ui/windows/SchoolWindow.cs' '_displayedSchoolRevisionStamp'
Require-Present 'lecture activity revision projection' 'Code/core/schools/HistoricalSchoolActionService.cs' 'HistoricalSchoolRevisionService.MarkActivity('
Require-Present 'debate activity revision projection' 'Code/core/schools/HistoricalSchoolDebateService.cs' 'HistoricalSchoolRevisionService.MarkActivity('
Require-Present 'bounded school write buffer' 'Code/core/schools/HistoricalSchoolWriteBuffer.cs' 'public const int MaxCapacity = 512'
Require-Present 'single school SQL batch transaction' 'Code/core/schools/HistoricalSchoolWriteBuffer.cs' '_db.BeginTransaction()'
Require-Present 'school SQL batch diagnostics' 'Code/core/schools/HistoricalSchoolWriteBuffer.cs' 'HistoricalSchoolDiagnostics.RecordSqlBatch('
Require-Present 'teaching transaction overload' 'Code/core/schools/HistoricalSchoolTeachingPersistenceDb.cs' 'RecordInTransaction('
Require-Present 'school write frame drain' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolWriteBufferService.ProcessFrame()'
Require-Present 'school write save flush' 'Code/patch/AW_SavePatch.cs' 'HistoricalSchoolWriteBufferService.FlushForSave()'
Require-Absent 'annual full ledger decay stage' 'Code/core/schools/HistoricalSchoolScheduler.cs' 'ApplyLedgerDecay('
Require-Absent 'annual full ledger decay update' 'Code/core/schools/HistoricalSchoolStore.cs' 'public static int ApplyLedgerDecay('
Require-Present 'lazy ledger read decay' 'Code/core/schools/HistoricalSchoolStore.cs' 'HistoricalSchoolLedgerDecayRules.Effective('
Require-Present 'lazy teaching ledger decay' 'Code/core/schools/HistoricalSchoolTeachingPersistenceDb.cs' 'HistoricalSchoolLedgerDecayRules.Effective('
Require-Absent 'direct ready lecture transaction' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'CommitQueuedLecture(pActivity)'
Require-Present 'buffered ready lecture write' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'TryQueueLectureCommit(pActivity)'
Require-Absent 'direct ready debate transaction' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'CommitQueuedDebate(pActivity)'
Require-Present 'buffered ready debate write' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'TryQueueDebateCommit(pActivity)'
Require-Present 'debate transaction overload' 'Code/core/schools/HistoricalSchoolStore.cs' 'RecordDebateAndLedgerInTransaction('
Require-Present 'guest start transaction overload' 'Code/core/schools/GuestOfficePersistenceDb.cs' 'StartInTransaction('
Require-Present 'guest end transaction overload' 'Code/core/schools/GuestOfficeEndPersistence.cs' 'EndInTransaction('
Require-Present 'buffered guest office writes' 'Code/core/schools/SchoolGuestOfficeService.cs' 'HistoricalSchoolWriteBufferService.TryEnqueue('
Require-Present 'membership join transaction overload' 'Code/core/schools/HistoricalSchoolStore.cs' 'InsertMembershipInTransaction('
Require-Present 'membership conversion transaction overload' 'Code/core/schools/HistoricalSchoolStore.cs' 'ConvertMembershipInTransaction('
Require-Present 'membership close transaction overload' 'Code/core/schools/HistoricalSchoolStore.cs' 'CloseMembershipInTransaction('
Require-Present 'buffered membership join' 'Code/core/schools/SchoolMembershipService.cs' 'TryQueueJoin('
Require-Present 'buffered membership conversion' 'Code/core/schools/SchoolMembershipService.cs' 'TryQueueConversion('
Require-Present 'membership event city identity' 'Code/core/schools/SchoolMembershipService.cs' 'public long CityId;'
Require-Present 'membership commit ledger invalidation' 'Code/core/schools/SchoolMembershipService.cs' 'HistoricalSchoolStore.InvalidateTeachingCommit(Event.CityId)'
Require-Present 'committed membership projection retry' 'Code/core/schools/SchoolMembershipService.cs' 'committed school membership adoption failed'
Require-Absent 'synchronous school action join' 'Code/core/schools/HistoricalSchoolActionService.cs' 'SchoolMembershipService.TryJoin('
Require-Absent 'synchronous school action conversion' 'Code/core/schools/HistoricalSchoolActionService.cs' 'SchoolMembershipService.TryConvert('
Require-Present 'year token enqueue' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'HistoricalSchoolRuntime.EnqueueWorldYear()'
Require-Present 'temporary school task scheduling' 'Code/core/schools/HistoricalSchoolTaskLeaseService.cs' 'scheduleTask('
Require-Present 'travel task lease scheduling' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'HistoricalSchoolTaskLeaseService.TrySchedule('
Require-Present 'indexed quarterly travel bucket' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'HistoricalSchoolRuntimeIndex.Instance.TravelEligibleIds(bucket)'
Require-Present 'bounded venue city cache' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'HistoricalSchoolFixedLru<long, CityVenueCacheEntry>'
Require-Present 'bounded recruit city cache' 'Code/core/schools/HistoricalSchoolRecruitCandidateCache.cs' 'HistoricalSchoolFixedLru<long, Entry>'
Require-Present 'bounded active venue claims' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'HistoricalSchoolActiveReservationBook<string, HistoricalSchoolVenueClaim>'
Require-Present 'twelve active venue maximum' 'Code/core/schools/HistoricalSchoolVenueService.cs' 'MaxActiveClaims = 12'
Require-Present 'eight concurrent lectures' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'MaxConcurrentLectures = 8'
Require-Present 'two-year lecture backlog bound' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'MaxRetainedLectures = MaxQueuedLectures * 2'
Require-Present 'lecture global backlog gate' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'PendingLectures.Count + ActiveLectures.Count'
Require-Present 'lecture concurrent activation gate' 'Code/core/schools/HistoricalSchoolActivityQueue.cs' 'ActiveLectures.Count, MaxConcurrentLectures'
Require-Present 'two-year debate backlog bound' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'MaxRetainedDebates = MaxQueuedPerYear * 2'
Require-Present 'debate global backlog gate' 'Code/core/schools/HistoricalSchoolDebateActivityService.cs' 'PendingCities.Count + ActivitiesById.Count'
Require-Present 'bounded active travel reservations' 'Code/core/schools/SchoolLineageService.cs' 'HistoricalSchoolTravelReservationBook'
Require-Present 'transient teacher death gate' 'Code/core/schools/SchoolLineageService.cs' 'HistoricalSchoolTransientIdGate'
Require-Absent 'permanent handled teacher deaths' 'Code/core/schools/SchoolLineageService.cs' 'HandledTeacherDeaths'
Require-Present 'teacher death gate terminal release' 'Code/core/schools/SchoolLineageService.cs' 'ProcessingTeacherDeaths.Complete(teacherId)'
Require-Present 'buffered lineage successor write' 'Code/core/schools/SchoolLineageService.cs' 'HistoricalSchoolWriteBufferService.TryEnqueue('
Require-Present 'transactional lineage successor event' 'Code/core/schools/SchoolLineageService.cs' 'HistoricalSchoolStore.RecordSchoolEventInTransaction('
Require-Absent 'synchronous lineage successor event' 'Code/core/schools/SchoolLineageService.cs' 'HistoricalSchoolStore.RecordSchoolEvent('
Require-Present 'fresh world clears school actions' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolActionService.ClearRuntime()'
Require-Present 'fresh world clears school activities' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolActivityQueue.ClearRuntime()'
Require-Present 'fresh world clears school writes' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolWriteBufferService.Clear()'
Require-Present 'fresh world clears guest offices' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'SchoolGuestOfficeService.ClearRuntime()'
Require-Present 'fresh world clears lineage reservations' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'SchoolLineageService.ClearRuntime()'
Require-Present 'fresh world clears travel targets' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'HistoricalSchoolTravelService.ClearRuntime()'
Require-Present 'fresh world clears school memberships' 'Code/core/schools/HistoricalSchoolRuntime.cs' 'SchoolMembershipService.ClearRuntime()'
Require-Absent 'map clear does not duplicate membership clear' 'Code/patch/AW_HistoricalSchoolPatch.cs' 'SchoolMembershipService.ClearRuntime();'
Require-Absent 'archive switch does not duplicate membership clear' 'Code/patch/AW_SavePatch.cs' 'try { SchoolMembershipService.ClearRuntime(); } catch { }'
Require-Present 'stale guest pending nodes are skipped' 'Code/core/schools/SchoolGuestOfficeService.cs' '!Pending.TryGetValue(actorId'
Require-Present 'stale death retry nodes are skipped' 'Code/core/schools/SchoolMembershipService.cs' '!QueuedDeathRetries.Contains(candidate)'
Require-Present 'canonical master idle roam behaviour' 'Code/ai/behaviours/actor/BehHistoricalSchoolIdleRoam.cs' 'class BehHistoricalSchoolIdleRoam'
Require-Present 'canonical master idle roam task' $contentPath 'IdleRoamTaskId = "aw_historical_school_idle_roam"'
Require-Present 'scoped formal affiliation transfer' 'Code/core/schools/FormalAffiliationTransferScope.cs' 'FormalAffiliationTransferRules.Allows'
Require-Present 'exact guest city permit' 'Code/core/schools/HistoricalAffiliationService.cs' 'FormalAffiliationTransferScope.Allows('
Require-Present 'exact guest kingdom permit' 'Code/core/schools/HistoricalAffiliationService.cs' 'FormalAffiliationTransferScope.AllowsKingdom('
Require-Present 'committed guest formal transfer scope' 'Code/core/schools/SchoolGuestOfficeService.cs' 'using (FormalAffiliationTransferScope.Open('
Require-Present 'committed guest vanilla city transfer' 'Code/core/schools/SchoolGuestOfficeService.cs' 'actor.joinCity(residence);'
Require-Present 'committed guest transfer verification' 'Code/core/schools/SchoolGuestOfficeService.cs' 'actor.city == residence && actor.kingdom == host'
Require-Absent 'travel cannot formally join a city' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'joinCity('
Require-Absent 'guest dismissal cannot move formal city' 'Code/core/court/CourtService.cs' 'joinCity('
Require-Absent 'guest dismissal cannot move formal kingdom' 'Code/core/court/CourtService.cs' 'joinKingdom('
Require-Absent 'guest end retains immutable home kingdom' 'Code/core/schools/GuestOfficeEndPersistence.cs' 'desired.HomeKingdomId ='
Require-Absent 'guest end retains immutable hometown city' 'Code/core/schools/GuestOfficeEndPersistence.cs' 'desired.HometownCityId ='
Require-Present 'archive WAL pragma' 'Code/core/db/LineageArchivePragmaService.cs' 'PRAGMA journal_mode=WAL'
Require-Present 'archive NORMAL sync pragma' 'Code/core/db/LineageArchivePragmaService.cs' 'PRAGMA synchronous=NORMAL'
Require-Present 'archive save checkpoint' 'Code/patch/AW_SavePatch.cs' 'LineageArchivePragmaService.CheckpointForSave'
Require-Present 'school performance counters' 'Code/core/schools/HistoricalSchoolDiagnostics.cs' 'IdleAllocatedBytes'
Require-Absent 'unconditional school residence revision' 'Code/core/schools/HistoricalAffiliationService.cs' 'AdvanceResidenceRevision()'
Require-Present 'membership runtime index projection' 'Code/core/schools/SchoolMembershipService.cs' 'HistoricalSchoolRuntimeIndex.Instance.Upsert'
Require-Present 'narrow affiliation revisions' 'Code/core/schools/HistoricalAffiliationService.cs' 'HistoricalSchoolRevisionService.ApplyAffiliationChange'
Require-Absent 'reputation twenty-five lecture gate' 'Code/core/schools/HistoricalSchoolLectureRules.cs' 'LaterTeacherMinimumReputation'
Require-Absent 'annual member snapshot runtime' 'Code/core/schools/HistoricalSchoolScheduler.cs' 'HistoricalSchoolAnnualMemberSnapshotBuilder.Build'
Require-Absent 'annual member snapshot action planner' 'Code/core/schools/HistoricalSchoolActionService.cs' 'HistoricalSchoolAnnualMemberSnapshot<Actor>'
Require-Absent 'annual member snapshot debate planner' 'Code/core/schools/HistoricalSchoolDebateService.cs' 'HistoricalSchoolAnnualMemberSnapshot<Actor>'
Require-Absent 'annual active affiliation array' 'Code/core/schools/SchoolGuestOfficeService.cs' 'ActiveSnapshots()'
Require-Absent 'quarter active affiliation array' 'Code/core/schools/HistoricalSchoolTravelService.cs' 'ActiveSnapshots('
Require-Absent 'lecture formal-city equality' 'Code/core/schools/HistoricalSchoolActionService.cs' 'pTeacher.city?.data?.id != residence.data.id'

$extinctionPatch = Read-Source 'Code/patch/AW_KingdomExtinctionPatch.cs'
$asylumNaturalization = $extinctionPatch.IndexOf('RoyalAsylumService.NaturalizeBeforeExtinction(__instance);', [System.StringComparison]::Ordinal)
$survivorNomadConversion = $extinctionPatch.IndexOf('__instance.makeSurvivorsToNomads();', [System.StringComparison]::Ordinal)
if ($asylumNaturalization -lt 0 -or $survivorNomadConversion -lt 0 -or
    $asylumNaturalization -gt $survivorNomadConversion) {
    $failures.Add('royal asylum naturalization must precede survivor nomad conversion')
}

$activityQueue = Read-Source 'Code/core/schools/HistoricalSchoolActivityQueue.cs'
$debateFrame = $activityQueue.IndexOf('if (HistoricalSchoolDebateActivityService.ProcessFrame()) return;', [System.StringComparison]::Ordinal)
$deferredFrame = $activityQueue.IndexOf('HistoricalSchoolActionService.ProcessDeferredFrame()', [System.StringComparison]::Ordinal)
if ($debateFrame -lt 0 -or $deferredFrame -lt 0 -or $debateFrame -gt $deferredFrame) {
    $failures.Add('visible debate transitions must be scheduled before deferred school maintenance')
}

if ($failures.Count -gt 0) {
    Write-Host "Source guard failures: $($failures.Count)"
    foreach ($failure in $failures) {
        Write-Host " - $failure"
    }
    exit 1
}

Write-Host 'Source guards passed.'
