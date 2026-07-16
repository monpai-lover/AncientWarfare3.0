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
Require-Present 'pathfinder exposes one-lookup ready cursor' `
    'Code/core/pathfinding/AWPathFinder.cs' 'public readonly struct ReadyPathCursor'
Require-Present 'path lifecycle rules centralize request and retry decisions' `
    'Code/core/pathfinding/AWPathLifecycleRules.cs' 'public readonly struct AWPathRequestKey'
Require-Absent 'path stream no longer locks every state read and step write' `
    'Code/core/pathfinding/AWPathStream.cs' '_stateGate'
Require-Present 'pathfinder opens ready cursor once' `
    'Code/core/pathfinding/AWPathFinder.cs' 'public AWPathPollResult OpenReadyCursor('
Require-Present 'smooth movement retains one ready cursor' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'AWPathFinder.ReadyPathCursor customPathCursor = default;'
Require-Present 'smooth movement continues through retained cursor' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'ContinuePathMovementFromSmooth(pActor, ref customPathCursor);'
Require-Present 'path movement classifies fast-step blockers' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'GetFastMoveBlockReason('
Require-Present 'ordinary ground movement uses Cultiway fast step' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'FastMoveTo(pActor, tile, adjacentStep);'
Require-Present 'special ground movement replays vanilla side effects' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'FastMoveToWithMoveToSideEffects(pActor, tile, adjacentStep);'
Require-Present 'fast movement maintains actor movement batch' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'SetMoveStepTile('
Require-Present 'fast movement preserves tile step actions' `
    'Code/core/pathfinding/AWPathMovementBridge.cs' 'ApplyStepActionForCurrentTile('
Require-Present 'smooth movement transpiler resolves updateMovement' `
    'Code/patch/AW_GlobalPathfindingPatch.cs' 'var updateMovement = AccessTools.Method(typeof(Actor), "updateMovement"'
Require-Present 'smooth movement transpiler uses direct optimized call' `
    'Code/patch/AW_GlobalPathfindingPatch.cs' 'nameof(UpdateMovementDirect)'
Require-Present 'AW3 smooth transpiler runs before real Cultiway' `
    'Code/patch/AW_GlobalPathfindingPatch.cs' '[HarmonyBefore("inmny.cultiway")]'
$pathMovementBridge = Read-Source 'Code/core/pathfinding/AWPathMovementBridge.cs'
$pathFailureStart = $pathMovementBridge.IndexOf('private static void HandleFailure(')
$pathFailureEnd = $pathMovementBridge.IndexOf('private static bool TryStartDueRetry(',
    $pathFailureStart)
if ($pathFailureStart -lt 0 -or $pathFailureEnd -lt 0 -or
    -not $pathMovementBridge.Substring($pathFailureStart,
        $pathFailureEnd - $pathFailureStart).Contains('pActor.setNotMoving();')) {
    $failures.Add('path failure must leave the smooth movement batch before scheduling recovery')
}
$vassalService = Read-Source 'Code/core/lineage/VassalService.cs'
$getSuzerainStart = $vassalService.IndexOf('public static long GetSuzerainId(')
$getSuzerainEnd = $vassalService.IndexOf('public static Kingdom GetSuzerain(', $getSuzerainStart)
if ($getSuzerainStart -lt 0 -or $getSuzerainEnd -lt 0 -or
    $vassalService.Substring($getSuzerainStart,
        $getSuzerainEnd - $getSuzerainStart).Contains('ReadActiveSuzerainId(')) {
    $failures.Add('runtime suzerain lookup must not query archived vassal relations')
}
Require-Absent 'stable vassal plate component lookup' `
    'Code/ui/components/VassalNameplateSuzerainFlag.cs' '.GetComponent<'
Require-Present 'missing name binding suppresses optional vassal flag' `
    'Code/ui/components/VassalNameplateSuzerainFlag.cs' `
    'if (_root != null || _nameplate == null || _nameText == null) return;'
Require-Present 'nameplates receive one cached vassal flag component' `
    'Code/patch/AW_VassalNameplatePatch.cs' 'VassalNameplateSuzerainFlag.Attach(__instance);'
Require-Present 'kingdom suffix uses native nameplate string generation' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'getStringForNameplate'
Require-Absent 'kingdom suffix does not recalculate population' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'getPopulationPeople'
Require-Absent 'kingdom suffix does not assign nameplate text twice' `
    'Code/patch/AW_NameplateTitlePatch.cs' '.setText('
Require-Absent 'mandate marker service has no field reflection' `
    'Code/core/policy/MandateMapMarkerService.cs' 'FieldInfo'
Require-Absent 'mandate marker service does not mutate nameplate fields' `
    'Code/core/policy/MandateMapMarkerService.cs' '.SetValue('
Require-Absent 'mandate marker render does not rebuild database report' `
    'Code/core/policy/MandateMapMarkerService.cs' 'ReadReport('
Require-Present 'kingdom suffix reads runtime mandate projection' `
    'Code/patch/AW_NameplateTitlePatch.cs' 'IsRuntimeMandateKingdom('
Require-Present 'mandate marker uses native species loader' `
    'Code/patch/AW_MandateMapModePatch.cs' '"showSpecies"'
Require-Absent 'mandate marker no longer post-processes kingdom plates' `
    'Code/patch/AW_MandateMapModePatch.cs' 'ApplyNameplate('
Require-Present 'archive switch rebuilds mandate marker projection' `
    'Code/patch/AW_SavePatch.cs' 'MandateService.RebuildRuntimeMarkerProjection();'
$godPowerLibrary = Read-Source 'Code/content/GodPowerLibrary.cs'
$schoolDrawStart = $godPowerLibrary.IndexOf('private static void DrawSchoolNameplates(')
$schoolDrawEnd = $godPowerLibrary.IndexOf('private static void ConfigureMapModePower(', $schoolDrawStart)
if ($schoolDrawStart -lt 0 -or $schoolDrawEnd -lt 0) {
    $failures.Add('school nameplate draw method could not be inspected')
} else {
    $schoolDrawRegion = $godPowerLibrary.Substring($schoolDrawStart,
        $schoolDrawEnd - $schoolDrawStart)
    if (-not $schoolDrawRegion.Contains('zone_camera.getVisibleZones()')) {
        $failures.Add('school nameplates must enumerate visible zones')
    }
    if ($schoolDrawRegion.Contains('foreach (City city in World.world.cities)')) {
        $failures.Add('school nameplates must not scan every world city')
    }
    if ([regex]::Matches($schoolDrawRegion,
            'CitySchoolSnapshotService\.GetSnapshot\(').Count -ne 1) {
        $failures.Add('school nameplate candidate must read exactly one snapshot')
    }
    if (-not $schoolDrawRegion.Contains(
            'GetSchoolIdentityMetaForCity(city, snapshot)')) {
        $failures.Add('school nameplate identity and icon must share one snapshot')
    }
}
Require-Present 'school identity accepts cached snapshot' `
    'Code/core/policy/AWMapModeMetaLibrary.cs' `
    'GetSchoolIdentityMetaForCity(City pCity, CitySchoolSnapshot pSnapshot)'
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
Require-Absent 'army retreat cannot enumerate the whole roster synchronously' 'Code/core/lineage/ArmyRetreatService.cs' 'SafeUnits('
Require-Absent 'army retreat cannot scan every kingdom city' 'Code/core/lineage/ArmyRetreatService.cs' 'kingdom.getCities()'
Require-Present 'army retreat uses direct roster count' 'Code/core/lineage/ArmyRetreatService.cs' 'pArmy.countUnits()'
Require-Present 'army retreat mutates only one captain per item' 'Code/core/lineage/ArmyRetreatService.cs' 'CaptainMutationBudget = 1'
Require-Present 'army retreat batches use the deferred queue' 'Code/core/lineage/ArmyRetreatService.cs' 'CoalescingKey("army_retreat", pArmyId)'
Require-Present 'army retreat uses the existing captain path request' 'Code/core/lineage/ArmyRetreatService.cs' 'captain.goTo('
Require-Absent 'army retreat cannot iterate direct army members' 'Code/core/lineage/ArmyRetreatService.cs' 'army.units'
Require-Absent 'one retreating army cannot clear its city shared attack target' 'Code/core/lineage/ArmyRetreatService.cs' 'pSourceCity.target_attack_city = null'
Require-Present 'army retreat runtime state has world reset' 'Code/core/lineage/ArmyRetreatService.cs' 'public static void ClearRuntime()'
Require-Present 'archive switch clears army retreat runtime state' 'Code/patch/AW_SavePatch.cs' 'ArmyRetreatService.ClearRuntime();'
$armySafetyPatch = Read-Source 'Code/patch/AW_ArmySafetyPatch.cs'
$retreatGate = $armySafetyPatch.IndexOf('ArmyRetreatService.ShouldStopAttack(pActor)',
    [System.StringComparison]::Ordinal)
$vanguardGate = $armySafetyPatch.IndexOf(
    'TemporarySlaveVanguardService.ShouldDelayBehindVanguard(pActor)',
    [System.StringComparison]::Ordinal)
if ($retreatGate -lt 0 -or $vanguardGate -lt 0 -or $retreatGate -gt $vanguardGate) {
    $failures.Add('an active or newly triggered retreat must precede vanguard assault holding')
}
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
Require-Present 'controlled recruitment uses thread-static scope' 'Code/core/lineage/MilitaryRecruitmentScope.cs' '[ThreadStatic]'
Require-Present 'vanilla random enlistment is intercepted' 'Code/patch/AW_StandingArmyPatch.cs' '[HarmonyPatch(typeof(City), "tryToMakeWarrior")]'
Require-Present 'standing army candidate scan is bounded' 'Code/core/lineage/StandingArmyRules.cs' 'MaxCandidateScan = 64'
Require-Present 'standing army roster scan is bounded' 'Code/core/lineage/StandingArmyRules.cs' 'MaxStandingScanPerPass = 64'
Require-Present 'standing army appointments are bounded' 'Code/core/lineage/StandingArmyRules.cs' 'MaxAppointmentsPerPass = 2'
Require-Present 'standing army reductions are bounded' 'Code/core/lineage/StandingArmyRules.cs' 'MaxReductionsPerPass = 2'
Require-Present 'standing army replacements are bounded' 'Code/core/lineage/StandingArmyRules.cs' 'MaxReplacementsPerPass = 1'
Require-Present 'standing army candidate cursor indexes residents directly' 'Code/core/lineage/StandingArmyService.cs' 'pCity.units'
Require-Present 'standing army roster cursor indexes members directly' 'Code/core/lineage/StandingArmyService.cs' 'army.units'
Require-Present 'standing army maintenance pauses during mobilization' 'Code/core/lineage/StandingArmyService.cs' 'StandingArmyRules.ShouldMaintainPeacetime('
Require-Absent 'standing army candidate cursor cannot reskip prior residents' 'Code/core/lineage/StandingArmyService.cs' 'skipped++ < cursor'
Require-Absent 'standing army maintenance cannot enumerate the full roster' 'Code/core/lineage/StandingArmyService.cs' 'foreach (Actor actor in army.getUnits())'
Require-Absent 'per-warrior retention cannot recount the whole army' 'Code/core/lineage/StandingArmyService.cs' 'return CountOrdinaryMilitary(pCity)'
Require-Present 'Xia genome uses tested offspring delta' 'Code/content/XiaRace.cs' '("offspring", XiaFertilityRules.XiaOffspringDelta)'
Require-Present 'war notice signature key' 'Code/core/lineage/LineageKeys.cs' 'DECISION_NOTICE_SIGNATURE = "aw_decision_notice_signature"'
Require-Present 'war notice earliest-year key' 'Code/core/lineage/LineageKeys.cs' 'DECISION_NOTICE_EARLIEST_YEAR = "aw_decision_notice_earliest_year"'
Require-Present 'war notice forced-year key' 'Code/core/lineage/LineageKeys.cs' 'DECISION_NOTICE_FORCED_YEAR = "aw_decision_notice_forced_year"'
Require-Present 'current decision captures notice state' 'Code/core/policy/KingdomPolicyService.cs' 'DECISION_NOTICE_SIGNATURE, out item.notice_signature'
Require-Present 'queued decision restores notice state' 'Code/core/policy/KingdomPolicyService.cs' 'DECISION_NOTICE_SIGNATURE, pItem.notice_signature'
Require-Present 'current war decision issues notice' 'Code/core/policy/KingdomPolicyService.cs' 'WarNoticeService.EnsureCurrentNotice(pKingdom);'
Require-Present 'full-progress declaration has completion gate' 'Code/core/policy/KingdomPolicyService.cs' 'WarNoticeService.CanCompleteCurrentDeclaration(pKingdom, progress, def.Cost)'
Require-Present 'war notice runtime rebuild exists' 'Code/core/lineage/WarNoticeService.cs' 'public static void RebuildRuntime()'
Require-Present 'war notice runtime clear exists' 'Code/core/lineage/WarNoticeService.cs' 'public static void ClearRuntime()'
Require-Present 'kingdom year invokes temporary levies' 'Code/patch/AW_KingdomPolicyPatch.cs' 'TemporaryLevyService.OnKingdomYear(__instance);'
Require-Present 'war start activates temporary levies' 'Code/patch/AW_WarPatch.cs' 'TemporaryLevyService.OnWarStarted(__result'
Require-Present 'war end reevaluates temporary levies' 'Code/patch/AW_WarPatch.cs' 'TemporaryLevyService.OnWarEnded(pWar);'
Require-Present 'war notice issued history locale' 'Locales/aw3_war_decisions.csv' 'aw_hist_war_notice_issued_mid,'
Require-Present 'war notice received history locale' 'Locales/aw3_war_decisions.csv' 'aw_hist_war_notice_received_mid,'
Require-Present 'temporary levy enlistment history locale' 'Locales/aw3_war_decisions.csv' 'aw_hist_temporary_levy_enlisted,'
Require-Present 'temporary levy demobilization history locale' 'Locales/aw3_war_decisions.csv' 'aw_hist_temporary_levy_demobilized,'
Require-Present 'temporary slave vanguard demobilization locale' 'Locales/aw3_war_decisions.csv' 'aw_hist_temporary_slave_vanguard_demobilized,'
Require-Present 'temporary levy work-item bound' 'Code/core/lineage/TemporaryLevyRules.cs' 'MaxWorkItemsPerKingdomYear = 4'
Require-Present 'temporary levy per-item scan bound' 'Code/core/lineage/TemporaryLevyRules.cs' 'MaxCandidatesPerWorkItem = 16'
Require-Present 'temporary levy per-item recruitment bound' 'Code/core/lineage/TemporaryLevyRules.cs' 'MaxRecruitsPerWorkItem = 8'
Require-Present 'temporary levy scan bound' 'Code/core/lineage/TemporaryLevyRules.cs' 'MaxCandidatesPerKingdomYear = 64'
Require-Present 'temporary levy recruitment bound' 'Code/core/lineage/TemporaryLevyRules.cs' 'MaxRecruitsPerKingdomYear = 32'
Require-Present 'temporary levy demobilization bound' 'Code/core/lineage/TemporaryLevyRules.cs' 'DemobilizationBatchSize = 8'
Require-Present 'temporary levy persisted work-item key' 'Code/core/lineage/LineageKeys.cs' 'TEMPORARY_LEVY_WORK_ITEMS = "aw_temporary_levy_work_items"'
Require-Present 'temporary levy persisted scan key' 'Code/core/lineage/LineageKeys.cs' 'TEMPORARY_LEVY_SCANNED = "aw_temporary_levy_scanned"'
Require-Present 'temporary levy persisted recruit key' 'Code/core/lineage/LineageKeys.cs' 'TEMPORARY_LEVY_RECRUITED = "aw_temporary_levy_recruited"'
Require-Present 'temporary levy persisted frontier cursor key' 'Code/core/lineage/LineageKeys.cs' 'TEMPORARY_LEVY_FRONTIER_CURSOR = "aw_temporary_levy_frontier_cursor"'
Require-Present 'actor retirement rejects temporary service' 'Code/patch/AW_RetirementPatch.cs' 'TemporaryLevyService.IsTemporaryLevy(__instance) ||'
Require-Present 'fallback retirement rejects temporary service' 'Code/core/lineage/SlaveService.cs' 'TemporaryLevyService.IsTemporaryLevy(pActor) ||'
Require-Present 'temporary enlistment suppresses permanent chronicle' 'Code/patch/AW_EnlistPatch.cs' 'MilitaryRecruitmentScope.SuppressesPermanentEnlistmentHistory'
Require-Present 'temporary enlistment suppresses permanent service clock' 'Code/patch/AW_SlaveryPatch.cs' 'MilitaryRecruitmentScope.SuppressesPermanentEnlistmentHistory'
Require-Present 'temporary levy deferred cleanup' 'Code/core/lineage/TemporaryLevyService.cs' 'DeferredRuntimeWorkService.EnqueueCoalesced('
Require-Present 'temporary levy hot lookup uses actor id index' 'Code/core/lineage/TemporaryLevyService.cs' 'ActiveActorIds'
Require-Present 'temporary levy pool activity is kingdom indexed' 'Code/core/lineage/TemporaryLevyService.cs' 'public static bool HasActivePool(Kingdom pKingdom)'
Require-Present 'temporary levy joins a city army immediately' 'Code/core/lineage/TemporaryLevyService.cs' 'EnsureArmyMembership('
Require-Absent 'temporary levy cannot use combat score' 'Code/core/lineage/TemporaryLevyService.cs' 'MilitaryScore('
Require-Absent 'city maintenance cannot form slave armies' 'Code/patch/AW_RetirementPatch.cs' 'SlaveService.EnsureSlaveArmy(pCity);'
Require-Absent 'forced slave control cannot form peacetime armies' 'Code/core/lineage/SlaveService.cs' 'EnsureSlaveArmy(city);'
Require-Absent 'slave enlistment cannot trigger legacy army formation' 'Code/core/lineage/SlaveService.cs' 'EnsureSlaveArmy(pCity ?? pActor.city);'
Require-Absent 'legacy slave frontline actor scan is removed' 'Code/core/lineage/SlaveService.cs' 'DriveSlaveArmyFrontline('
Require-Present 'slave population count is event indexed' 'Code/core/lineage/SlavePopulationIndexService.cs' 'internal static class SlavePopulationIndexService'
Require-Present 'slave population uses persisted city count' 'Code/core/lineage/SlavePopulationIndexService.cs' 'LineageKeys.SLAVE_POPULATION_COUNT'
Require-Present 'slave population tracks each actor city once' 'Code/core/lineage/SlavePopulationIndexService.cs' 'LineageKeys.SLAVE_COUNTED_CITY_ID'
Require-Absent 'slave population index cannot scan world units' 'Code/core/lineage/SlavePopulationIndexService.cs' 'World.world.units'
Require-Present 'enslavement increments slave population index' 'Code/core/lineage/SlaveService.cs' 'SlavePopulationIndexService.Activate('
Require-Present 'manumission decrements slave population index' 'Code/core/lineage/SlaveService.cs' 'SlavePopulationIndexService.Deactivate(pActor);'
Require-Present 'slave migration updates population index' 'Code/patch/AW_SlaveryPatch.cs' 'SlavePopulationIndexService.OnActorCityChanged(__instance);'
Require-Present 'slave death decrements population index' 'Code/patch/AW_ActorDeathPatch.cs' 'SlavePopulationIndexService.Deactivate(__instance);'
Require-Absent 'slave food quota cannot scan city residents' 'Code/core/lineage/SlaveService.cs' 'private static bool HasAnySlave('
Require-Absent 'slave labor count cannot scan city residents' 'Code/core/lineage/SlaveService.cs' 'private static int CountSlaves('
Require-Present 'city-fall enslavement has a resident scan cap' 'Code/core/lineage/SlaveService.cs' 'MAX_CITY_FALL_SCAN = 80'
Require-Present 'city-fall enslavement indexes bounded residents directly' 'Code/core/lineage/SlaveService.cs' 'Actor unit = pCity.units[i];'
Require-Absent 'city maintenance cannot invoke disabled retirement scans' 'Code/patch/AW_RetirementPatch.cs' 'SlaveService.CheckCityRetirements(pCity);'
Require-Present 'slave catcher cooldown is actor-local' 'Code/core/lineage/LineageKeys.cs' 'SLAVE_CAPTURE_NEXT_SEARCH_TIME'
Require-Present 'slave catcher reads its own cooldown' 'Code/core/lineage/SlaveService.cs' 'LineageKeys.SLAVE_CAPTURE_NEXT_SEARCH_TIME, out float nextAllowed'
Require-Absent 'slave catcher cooldown cannot use a global actor dictionary' 'Code/core/lineage/SlaveService.cs' 'CaptureSearchNextAllowed'
Require-Absent 'slave catcher cooldown cannot run global pruning' 'Code/core/lineage/SlaveService.cs' 'PruneExpiredCaptureSearchCooldowns('
Require-Present 'capture scan cache has a hard state cap' 'Code/core/lineage/SlaveCaptureScanService.cs' 'MaxStates = 256'
Require-Present 'capture scan eviction is bounded per request' 'Code/core/lineage/SlaveCaptureScanService.cs' 'MaxEvictionsPerRequest = 8'
Require-Present 'capture scan completion uses an eviction queue' 'Code/core/lineage/SlaveCaptureScanService.cs' 'CompletedKeys.Enqueue(pState.key);'
Require-Absent 'capture scan cache cannot run full dictionary pruning' 'Code/core/lineage/SlaveCaptureScanService.cs' 'foreach (KeyValuePair<string, ScanState> entry in States)'
Require-Present 'capture scan waiter publication is bounded' 'Code/core/lineage/SlaveCaptureScanRules.cs' 'MaxWaiterNotificationsPerWorkItem = 4'
Require-Present 'capture scan waiter publication is deferred' 'Code/core/lineage/SlaveCaptureScanService.cs' '"slave_capture_waiters:" + pState.key'
Require-Absent 'capture scan completion cannot synchronously notify every waiting city' 'Code/core/lineage/SlaveCaptureScanService.cs' 'foreach (long cityId in pState.waitingCityIds)'
Require-Present 'temporary vanguard service exists' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'internal static class TemporarySlaveVanguardService'
Require-Present 'temporary vanguard uses kingdom coalescing key' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'CoalescingKey("slave_vanguard", pKingdomId)'
Require-Present 'temporary vanguard scans one city per work item' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'MaxCitiesPerWorkItem = 1'
Require-Present 'temporary vanguard uses 32-resident scan bound' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'MaxResidentsScannedPerWorkItem'
Require-Present 'temporary vanguard initial roster is atomic' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'FormInitialRosterAtomically('
Require-Present 'temporary vanguard uses special recruitment scope' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'MilitaryRecruitmentKind.SlaveVanguard'
Require-Present 'temporary vanguard cleanup is four actors' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'MaxActorsChangedPerWorkItem'
Require-Present 'ordinary armies wait for the indexed vanguard on the same front' `
    'Code/patch/AW_ArmySafetyPatch.cs' `
    'TemporarySlaveVanguardService.ShouldDelayBehindVanguard(pActor)'
Require-Present 'vanguard assault ordering has an O(1) service gate' `
    'Code/core/lineage/TemporarySlaveVanguardService.cs' `
    'public static bool ShouldDelayBehindVanguard(Actor pActor)'
Require-Present 'invalid vanguard composition forces terminal cleanup' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'ForceCleanup'
Require-Absent 'forced vanguard cleanup cannot depend on a mutable cleaning flag' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'if (state.Cleaning && state.ForceCleanup)'
Require-Present 'vanguard casualties refresh cached deployment readiness' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'WarNoticeService.OnArmyChanged(kingdom, army, pRosterExpanded: false);'
Require-Present 'removed vanguards leave cached deployment blockers' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'WarNoticeService.OnArmyInvalidated(pKingdom, removedArmyId);'
Require-Present 'war notice activates slave vanguard' 'Code/core/lineage/WarNoticeService.cs' 'TemporarySlaveVanguardService.OnEmergencyChanged('
Require-Present 'war start activates slave vanguard' 'Code/patch/AW_WarPatch.cs' 'TemporarySlaveVanguardService.OnWarStarted(__result);'
Require-Present 'war end reevaluates slave vanguard' 'Code/patch/AW_WarPatch.cs' 'TemporarySlaveVanguardService.OnWarEnded(pWar);'
Require-Present 'temporary vanguard runtime rebuild' 'Code/patch/AW_SavePatch.cs' 'TemporarySlaveVanguardService.RebuildRuntime();'
Require-Present 'temporary vanguard runtime clear' 'Code/patch/AW_SavePatch.cs' 'TemporarySlaveVanguardService.ClearRuntime();'
Require-Present 'temporary vanguard persists bounded roster ids' 'Code/core/lineage/LineageKeys.cs' 'TEMPORARY_SLAVE_VANGUARD_ROSTER_IDS'
Require-Present 'temporary vanguard rebuild reads roster ids' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'ReadRosterIds('
Require-Absent 'temporary vanguard cannot scan world units' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'World.world.units'
Require-Absent 'temporary vanguard cannot scan world armies' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'World.world.armies'
Require-Absent 'temporary vanguard cannot enumerate kingdom cities' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'getCities()'
Require-Present 'temporary vanguard indexes kingdom city list directly' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'pKingdom.cities'
Require-Present 'temporary vanguard indexes city residents directly' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'pCity.units'
Require-Present 'new slaves wake a dormant vanguard scan' 'Code/core/lineage/SlaveService.cs' 'TemporarySlaveVanguardService.OnCandidateAvailable('
Require-Present 'dead vanguard members invalidate by actor id' 'Code/patch/AW_ActorDeathPatch.cs' 'TemporarySlaveVanguardService.OnMemberInvalidated(__instance);'
Require-Present 'vanguard casualty scan restart is conditional' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'EnsureScanPassAvailable(state, kingdom);'
Require-Present 'dead levies invalidate by actor id' 'Code/patch/AW_ActorDeathPatch.cs' 'TemporaryLevyService.OnActorInvalidated(__instance);'
Require-Present 'freed vanguard members invalidate by actor id' 'Code/core/lineage/SlaveService.cs' 'TemporarySlaveVanguardService.OnMemberInvalidated(pActor);'
Require-Present 'nationality changes invalidate vanguard membership' 'Code/patch/AW_SlaveryPatch.cs' 'TemporarySlaveVanguardService.OnActorKingdomChanged(__instance, __state);'
Require-Present 'nationality changes invalidate levy membership' 'Code/patch/AW_SlaveryPatch.cs' 'TemporaryLevyService.OnActorInvalidated(__instance);'
Require-Present 'nationality changes release stale deployment' 'Code/patch/AW_SlaveryPatch.cs' 'ArmyDeploymentService.ReleaseActor(__instance, restoreJob: true);'
Require-Present 'nationality changes refresh the former kingdom army' 'Code/patch/AW_SlaveryPatch.cs' 'WarNoticeService.QueueArmyChanged(__state, __instance.army);'
Require-Present 'levy invalidation removes the hot actor index first' 'Code/core/lineage/TemporaryLevyService.cs' 'if (!ActiveActorIds.Remove(pActor.data.id)) return;'
Require-Absent 'retirement maintenance cannot drive vanguard work' 'Code/patch/AW_RetirementPatch.cs' 'TemporarySlaveVanguardService.OnEmergencyChanged'
Require-Absent 'legacy slave frontline cache is removed' 'Code/core/lineage/SlaveService.cs' 'FrontlineTargetCache'
Require-Absent 'legacy slave warrior-count cache is removed' 'Code/core/lineage/SlaveService.cs' 'CityWarriorCountCache'
Require-Absent 'slave identity checks cannot infer armies by roster scan' 'Code/core/lineage/SlaveService.cs' 'CountArmyComposition('
Require-Absent 'slave army naming cannot scan all armies' 'Code/core/lineage/SlaveService.cs' 'foreach (Army army in World.world.armies)'
Require-Absent 'obsolete city slave-army schedule key is removed' 'Code/core/lineage/LineageKeys.cs' 'SLAVE_ARMY_LAST_CHECK'
Require-Absent 'obsolete city slave-army failure key is removed' 'Code/core/lineage/LineageKeys.cs' 'SLAVE_ARMY_FAILURE_YEAR'
Require-Absent 'obsolete city slave-army cursor key is removed' 'Code/core/lineage/LineageKeys.cs' 'SLAVE_ARMY_FILL_SCAN_CURSOR'
Require-Absent 'obsolete city slave-army continuation key is removed' 'Code/core/lineage/LineageKeys.cs' 'SLAVE_ARMY_FILL_CONTINUE_TIME'
Require-Absent 'obsolete slave-army benchmark group is removed' 'Code/core/policy/CityMaintenanceBenchmarkRules.cs' 'public const string SlaveArmy ='
Require-Absent 'obsolete slave-army frontline benchmark is removed' 'Code/core/policy/CityMaintenanceBenchmarkRules.cs' 'SlaveArmyFrontlineScan'
Require-Absent 'obsolete slave-army maintenance rules are removed' 'Code/core/lineage/SlaveArmyMaintenanceRules.cs' 'ShouldRunMaintenance('
Require-Absent 'obsolete slave-army fill side effects are removed' 'Code/core/lineage/SlaveArmyFillSideEffectRules.cs' 'SlaveArmyFillSideEffectRules'
Require-Absent 'obsolete global special-army scan benchmark is removed' 'Code/core/policy/CityMaintenanceBenchmarkRules.cs' 'SpecialArmyGlobalScan'
Require-Present 'military emergencies use a kingdom war index' 'Code/core/lineage/MilitaryEmergencyService.cs' 'WarIdsByKingdom'
Require-Absent 'military emergency lookup cannot scan war manager' 'Code/core/lineage/MilitaryEmergencyService.cs' 'World.world?.wars?.hasWars('
Require-Present 'war start updates military emergency index' 'Code/patch/AW_WarPatch.cs' 'MilitaryEmergencyService.OnWarStarted(__result);'
Require-Present 'war end updates military emergency index' 'Code/patch/AW_WarPatch.cs' 'MilitaryEmergencyService.OnWarEnded(pWar);'
Require-Present 'late war participants enter emergency index' 'Code/patch/AW_WarPatch.cs' 'MilitaryEmergencyService.OnKingdomJoinedWar('
Require-Present 'departing war participants leave emergency index' 'Code/patch/AW_WarPatch.cs' 'MilitaryEmergencyService.OnKingdomLeftWar('
Require-Present 'levy emergency wakeup is deferred' 'Code/core/lineage/TemporaryLevyService.cs' 'CoalescingKey("levy_emergency"'
Require-Present 'levy annual recruitment is deferred' 'Code/core/lineage/TemporaryLevyService.cs' 'CoalescingKey("levy_recruit"'
Require-Present 'levy annual hook only schedules recruitment' 'Code/core/lineage/TemporaryLevyService.cs' 'ScheduleRecruitmentYear(pKingdom, year);'
Require-Absent 'levy annual hook cannot scan synchronously' 'Code/core/lineage/TemporaryLevyService.cs' 'private static void RecruitYearBatch('
Require-Present 'levy recruitment consumes cached frontier cities' 'Code/core/lineage/TemporaryLevyService.cs' 'ArmyDeploymentService.TryGetPreferredLevyCity('
Require-Present 'levy frontier cache has an independent cursor' 'Code/core/lineage/TemporaryLevyService.cs' 'public int PreferredCityCursor;'
Require-Present 'levy frontier cursor advances only after a cached hit' 'Code/core/lineage/TemporaryLevyService.cs' 'plan.PreferredCityCursor++;'
Require-Present 'same-year levy work resumes its remaining budget' 'Code/core/lineage/TemporaryLevyService.cs' 'ResumeRecruitmentYear(pKingdom, year);'
Require-Present 'same-year levy work restores a missing runtime plan' 'Code/core/lineage/TemporaryLevyService.cs' 'RestoreRecruitmentPlan(pKingdom, pYear)'
Require-Present 'levy batches persist their consumed budget' 'Code/core/lineage/TemporaryLevyService.cs' 'PersistRecruitmentPlan(kingdom, plan);'
Require-Present 'load rebuild resumes active levy plans' 'Code/core/lineage/TemporaryLevyService.cs' 'ResumeActiveRecruitmentPlans();'
Require-Present 'new notice immediately wakes attacker levies' 'Code/core/lineage/WarNoticeService.cs' 'TemporaryLevyService.OnEmergencyChanged(pAttacker);'
Require-Present 'new notice immediately wakes defender levies' 'Code/core/lineage/WarNoticeService.cs' 'TemporaryLevyService.OnEmergencyChanged(defender);'
Require-Present 'war preparation summary has a cached notice index' 'Code/core/lineage/WarNoticeService.cs' 'SummaryNoticeByKingdom'
Require-Present 'repeat notice indexing preserves cached summary' 'Code/core/lineage/WarNoticeService.cs' 'if (added) RefreshPreparationSummaries(pState);'
Require-Present 'war preparation summary reads cached levy count' 'Code/core/lineage/WarNoticeService.cs' 'TemporaryLevyService.ActiveLevyCount(pKingdom)'
Require-Present 'war preparation summary reads cached deployment state' 'Code/core/lineage/WarNoticeService.cs' 'ArmyDeploymentService.TryGetCachedReadiness('
Require-Present 'policy window reads cached war preparation summary' 'Code/ui/windows/KingdomPolicyWindow.cs' 'WarNoticeService.TryGetPreparationSummary('
Require-Present 'policy window shows war preparation status' 'Code/ui/windows/KingdomPolicyWindow.cs' 'BuildWarPreparationStatus('
Require-Present 'war preparation status supports vanilla tooltip hover' 'Code/ui/windows/KingdomPolicyWindow.cs' 'typeof(Image), typeof(Button), typeof(TipButton));'
Require-Present 'war preparation status is not a click command' 'Code/ui/windows/KingdomPolicyWindow.cs' 'tip.showOnClick = false;'
Require-Present 'war preparation locale' 'Locales/aw3_war_decisions.csv' 'aw_war_preparation,'
Require-Present 'war notice target locale' 'Locales/aw3_war_decisions.csv' 'aw_war_notice_target,'
Require-Present 'war notice years locale' 'Locales/aw3_war_decisions.csv' 'aw_war_notice_window,'
Require-Present 'war levy count locale' 'Locales/aw3_war_decisions.csv' 'aw_war_levy_count,'
Require-Present 'war deployment ready locale' 'Locales/aw3_war_decisions.csv' 'aw_war_deployment_ready,'
Require-Present 'war deployment preparing locale' 'Locales/aw3_war_decisions.csv' 'aw_war_deployment_preparing,'
Require-Present 'military emergency runtime rebuild' 'Code/patch/AW_SavePatch.cs' 'MilitaryEmergencyService.RebuildRuntime();'
Require-Present 'military emergency runtime clear' 'Code/patch/AW_SavePatch.cs' 'MilitaryEmergencyService.ClearRuntime();'
Require-Present 'temporary levies use indexed emergency lookup' 'Code/core/lineage/TemporaryLevyService.cs' 'MilitaryEmergencyService.HasAny(pKingdom)'
Require-Absent 'temporary levies cannot enumerate all active wars' 'Code/core/lineage/TemporaryLevyService.cs' 'foreach (War war in pKingdom.getWars())'
Require-Absent 'temporary levy recruitment cannot sort all cities' 'Code/core/lineage/TemporaryLevyService.cs' '.Sort('
Require-Absent 'temporary levy recruitment cannot enumerate kingdom cities' 'Code/core/lineage/TemporaryLevyService.cs' 'pKingdom.getCities()'
Require-Present 'temporary levy recruitment indexes kingdom cities directly' 'Code/core/lineage/TemporaryLevyService.cs' 'pKingdom.cities'
Require-Absent 'deployment cannot enumerate dirty kingdom city iterator' 'Code/core/lineage/ArmyDeploymentService.cs' 'pKingdom.getCities()'
Require-Absent 'deployment target discovery cannot enumerate dirty kingdom city iterator' 'Code/core/lineage/ArmyDeploymentService.cs' 'pDefender.getCities()'
Require-Absent 'deployment cannot scan all world armies' 'Code/core/lineage/ArmyDeploymentService.cs' 'foreach (Army army in World.world.armies)'
Require-Present 'special armies have kingdom role index' 'Code/core/lineage/AWArmyService.cs' 'RoleArmyIdsByKingdomRole'
Require-Present 'kingdom-unique special army lookup is indexed' 'Code/core/lineage/AWArmyService.cs' 'TryGetRoleArmy('
Require-Present 'special army lookup cache has reverse index' 'Code/core/lineage/AWArmyService.cs' 'LookupCacheKeysByArmy'
Require-Present 'deployment uses indexed special armies' 'Code/core/lineage/ArmyDeploymentService.cs' 'AWArmyService.GetRoleArmies('
Require-Present 'new border armies update deployment incrementally' 'Code/core/lineage/MandateBorderDefenseService.cs' 'WarNoticeService.OnArmyChanged(owner, borderArmy);'
Require-Present 'deployment caches facing cities per notice' 'Code/core/lineage/ArmyDeploymentService.cs' 'TargetCityIds'
Require-Present 'deployment caches required army ids per notice' 'Code/core/lineage/ArmyDeploymentService.cs' 'RequiredArmyIds'
Require-Present 'deployment arrival is indexed by army' 'Code/core/lineage/ArmyDeploymentService.cs' 'ArrivedArmyIds'
Require-Present 'deployment arrival requires the army captain' 'Code/core/lineage/ArmyDeploymentService.cs' 'ArmyDeploymentRules.ShouldMarkArmyArrived('
Require-Present 'repeat deployment arrivals do not enqueue refresh work' 'Code/core/lineage/ArmyDeploymentService.cs' 'if (!assignments.ArrivedArmyIds.Add(pActor.army.id)) return;'
Require-Present 'deployment target offsets are static' 'Code/core/lineage/ArmyDeploymentService.cs' 'private static readonly int[] TargetOffsetX'
Require-Absent 'deployment movement cannot allocate offset matrices' 'Code/core/lineage/ArmyDeploymentService.cs' 'int[,] offsets ='
Require-Present 'deployment actor mutation batch is bounded' 'Code/core/lineage/ArmyDeploymentService.cs' 'ActorMutationBatchSize = 16'
Require-Present 'deployment city discovery batch is bounded' 'Code/core/lineage/ArmyDeploymentRules.cs' 'MaxCitiesDiscoveredPerWorkItem = 8'
Require-Present 'deployment army review batch is bounded' 'Code/core/lineage/ArmyDeploymentRules.cs' 'MaxArmiesReviewedPerWorkItem = 8'
Require-Present 'deployment discovery is deferred and coalesced' 'Code/core/lineage/ArmyDeploymentService.cs' '"deployment_discovery:" + (pSignature ?? "")'
Require-Present 'deployment readiness gate uses cached blockers' 'Code/core/lineage/ArmyDeploymentService.cs' 'BlockingArmyIds.Count == 0'
Require-Present 'new warriors coalesce deployment refresh by army id' 'Code/patch/AW_EnlistPatch.cs' 'WarNoticeService.QueueArmyChanged('
Require-Present 'new warriors preserve roster expansion semantics' 'Code/patch/AW_EnlistPatch.cs' 'pRosterExpanded: true'
Require-Absent 'warrior enlistment cannot synchronously fan out notices' 'Code/patch/AW_EnlistPatch.cs' 'WarNoticeService.OnArmyChanged('
Require-Present 'levy enlistment coalesces deployment refresh' 'Code/core/lineage/TemporaryLevyService.cs' 'WarNoticeService.QueueArmyChanged(pKingdom, pActor.army, pRosterExpanded: true);'
Require-Absent 'levy enlistment cannot synchronously fan out notices' 'Code/core/lineage/TemporaryLevyService.cs' 'WarNoticeService.OnArmyChanged(pKingdom, pActor.army);'
Require-Present 'army losses coalesce deployment refresh by army id' 'Code/core/lineage/WarNoticeService.cs' 'CoalescingKey("deployment_army_changed", armyId)'
Require-Present 'coalesced army changes retain expansion state' 'Code/core/lineage/WarNoticeService.cs' 'PendingExpandedArmyIds'
Require-Present 'actor death refreshes only its former army' 'Code/patch/AW_ActorDeathPatch.cs' 'WarNoticeService.QueueArmyChanged(__instance.kingdom, __instance.army);'
Require-Present 'warrior demotion refreshes only its former army' 'Code/patch/AW_EnlistPatch.cs' 'WarNoticeService.QueueArmyChanged(__instance.kingdom, __instance.army);'
Require-Present 'warrior demotion releases actor deployment immediately' 'Code/patch/AW_EnlistPatch.cs' 'ArmyDeploymentService.ReleaseActor(__instance, restoreJob: true);'
Require-Present 'deployment border lookup uses kingdom hash index' 'Code/core/lineage/ArmyDeploymentService.cs' 'pCity.neighbours_kingdoms.Contains(pAttacker)'
Require-Absent 'deployment border lookup cannot enumerate neighbour cities' 'Code/core/lineage/ArmyDeploymentService.cs' 'foreach (City other in pCity.neighbours_cities)'
Require-Present 'deployment actor changes use deferred queue' 'Code/core/lineage/ArmyDeploymentService.cs' 'DeferredRuntimeWorkService.EnqueueCoalesced('
Require-Present 'deployment cancellation is phased' 'Code/core/lineage/ArmyDeploymentService.cs' 'assignments.Closing = true;'
Require-Present 'closing deployment removes its own member ids directly' 'Code/core/lineage/ArmyDeploymentService.cs' 'assignments.ActorIds.Remove(batch[i]);'
Require-Present 'closing deployment preserves actors reassigned to another notice' 'Code/core/lineage/ArmyDeploymentService.cs' 'ArmyDeploymentRules.ShouldClearForClosingNotice('
Require-Absent 'closing deployment cannot release whichever notice happens to be current' 'Code/core/lineage/ArmyDeploymentService.cs' 'ReleaseActor(actor, assignments.RestoreJobs);'
Require-Present 'stale deployment task restores normal job' 'Code/ai/behaviours/actor/BehWarDeploymentMove.cs' 'pActor.ai?.setJob(pActor.getNextJob())'
Require-Absent 'deployment cannot sort all facing cities' 'Code/core/lineage/ArmyDeploymentService.cs' 'selected.Sort('
Require-Absent 'deployment cannot enumerate army members through iterator' 'Code/core/lineage/ArmyDeploymentService.cs' 'foreach (Actor actor in pArmy.getUnits())'
Require-Absent 'temporary levy no longer allocates city priority snapshots' 'Code/core/lineage/TemporaryLevyService.cs' 'CityPrioritySnapshot'
Require-Present 'city maintenance invokes standing army' 'Code/patch/AW_RetirementPatch.cs' 'StandingArmyService.MaintainCity(pCity);'
Require-Present 'guard readiness reads direct ordinary army counts' 'Code/core/lineage/KingdomMilitaryReadinessService.cs' 'StandingArmyService.CountOrdinaryStandingFast('
Require-Present 'normal-army guard cleanup uses the bounded roster index' 'Code/core/lineage/RoyalGuardService.cs' 'List<long> rosterIds = ReadGuardRosterIds(kingdom);'
Require-Absent 'normal-army guard cleanup cannot copy and scan the full army' 'Code/core/lineage/RoyalGuardService.cs' 'new List<Actor>(pArmy.getUnits())'
Require-Present 'guard cursor scans index kingdom units directly' 'Code/core/lineage/RoyalGuardService.cs' 'List<Actor> units = pKingdom.units;'
Require-Absent 'guard cursor scans cannot re-enumerate skipped prefixes' 'Code/core/lineage/RoyalGuardService.cs' 'scanned++ < cursor'
Require-Absent 'guard candidate cursor cannot re-enumerate skipped prefixes' 'Code/core/lineage/RoyalGuardService.cs' 'skipped++ < cursor'
Require-Present 'guard readiness indexes kingdom cities directly' 'Code/core/lineage/KingdomMilitaryReadinessService.cs' 'pKingdom.cities'
Require-Absent 'guard readiness cannot allocate dirty city iterators' 'Code/core/lineage/KingdomMilitaryReadinessService.cs' 'pKingdom.getCities()'
Require-Absent 'guard readiness cannot scan standing army actors' 'Code/core/lineage/KingdomMilitaryReadinessService.cs' 'StandingArmyService.CountOrdinaryStanding(city)'
Require-Present 'standing maintenance retains only bounded weakest actors' 'Code/core/lineage/StandingArmyService.cs' 'AddBoundedWeakest('
Require-Absent 'standing maintenance cannot sort the full standing army' 'Code/core/lineage/StandingArmyService.cs' 'pStanding.Sort(CompareWeakestFirst)'
Require-Absent 'standing army cannot run from actor updateAge' 'Code/patch/AW_RetirementPatch.cs' 'UpdateAge_Postfix(Actor __instance)\n        {\n            StandingArmyService'
Require-Present 'special army attachment rejects royal refugees' 'Code/core/lineage/AWArmyService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'royal refugee next job stays asylum job' 'Code/patch/AW_EnlistPatch.cs' '__result = RoyalAsylumContent.ActorJobId;'
Require-Present 'city leader selection rejects royal refugees' 'Code/patch/AW_CityLeaderPatch.cs' 'RoyalAsylumService.IsActive(pUnit)'
Require-Present 'court appointment rejects royal refugees' 'Code/core/court/CourtService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'general selection rejects royal refugees' 'Code/core/lineage/GeneralService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'royal guard selection rejects royal refugees' 'Code/core/lineage/RoyalGuardService.cs' 'RoyalAsylumService.IsActive(pActor)'
Require-Present 'slave army selection rejects royal refugees' 'Code/core/lineage/TemporarySlaveVanguardService.cs' 'RoyalAsylumService.IsActive(pActor)'
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
Require-Present 'manual appointment window id' 'Code/ui/AW_LineageWindowIds.cs' 'COURT_APPOINTMENT = "aw_court_appointment"'
Require-Present 'vacant court card opens appointment window' 'Code/ui/items/CourtActorNodeView.cs' 'CourtAppointmentWindow.Open(pKingdom.id, pNode.OfficeId)'
Require-Present 'court cards create a visible management button' 'Code/ui/items/CourtActorNodeView.cs' 'new GameObject("ManageOffice"'
Require-Present 'filled court card uses replace action' 'Code/ui/items/CourtActorNodeView.cs' 'CourtManualOfficeAction.Replace'
Require-Present 'court management passes frozen incumbent id' 'Code/ui/items/CourtActorNodeView.cs' 'CourtAppointmentWindow.Open(pKingdom.id, pNode.OfficeId, incumbentActorId)'
Require-Present 'manual appointment window uses revalidating service' 'Code/ui/windows/CourtAppointmentWindow.cs' 'CourtService.TryManualAppointment('
Require-Present 'manual appointment success refreshes court' 'Code/ui/windows/CourtAppointmentWindow.cs' 'CourtWindow.Open(_kingdomId);'
Require-Present 'manual appointment candidate uses a live avatar' 'Code/ui/items/CourtAppointmentCandidateListItem.cs' '_avatar.show(actor);'
Require-Present 'manual appointment excludes minors with original adulthood state' 'Code/core/court/CourtService.cs' 'adult: pActor.isAdult()'
Require-Present 'manual appointment snapshots actor ids before incremental projection' 'Code/core/court/CourtService.cs' 'BeginManualAppointmentScan('
Require-Present 'manual appointment scan is frame bounded' 'Code/ui/windows/CourtAppointmentWindow.cs' 'CourtManualAppointmentRules.CandidateScanPerFrame'
Require-Present 'manual appointment scan has a time budget' 'Code/ui/windows/CourtAppointmentWindow.cs' 'CandidateFrameBudgetMilliseconds'
Require-Present 'manual appointment portrait rows are frame bounded' 'Code/ui/windows/CourtAppointmentWindow.cs' 'CourtManualAppointmentRules.CandidateRowsPerFrame'
Require-Present 'manual appointment candidates are paged' 'Code/ui/windows/CourtAppointmentWindow.cs' 'CourtManualAppointmentRules.CandidatePageSize'
Require-Absent 'manual appointment window cannot build every candidate synchronously' 'Code/ui/windows/CourtAppointmentWindow.cs' 'GetManualAppointmentCandidates('
Require-Present 'manual appointment revalidates current tier' 'Code/core/court/CourtService.cs' 'IsManualOfficeInCurrentTier(pKingdom, pOfficeId)'
Require-Present 'manual appointment revalidates vacancy' 'Code/core/court/CourtService.cs' 'HasActiveOffice(pKingdom, pOfficeId)'
Require-Present 'manual appointment uses persisted nationality authority' 'Code/core/court/CourtService.cs' 'CourtAffiliationResolver.IsDomestic(pActor, pKingdom)'
Require-Present 'manual appointment commits through official career path' 'Code/core/court/CourtService.cs' 'return SetOfficer(actor, kingdom, CourtOfficeLayer.Central,'
Require-Present 'school never gates manual appointment eligibility' 'Code/core/court/CourtManualAppointmentRules.cs' 'return true;'
Require-Present 'civil appointment has a focused military transition service' 'Code/core/court/CourtOfficerMilitaryTransitionService.cs' 'ReleaseAfterCommittedAppointment('
$courtService = Read-Source 'Code/core/court/CourtService.cs'
$committedProjection = $courtService.IndexOf('internal static bool ApplyCommittedOfficerProjection(',
    [System.StringComparison]::Ordinal)
$committedGate = if ($committedProjection -ge 0) {
    $courtService.IndexOf('!careerResult.IsCommitted', $committedProjection,
        [System.StringComparison]::Ordinal)
} else { -1 }
$militaryRelease = if ($committedProjection -ge 0) {
    $courtService.IndexOf('CourtOfficerMilitaryTransitionService.ReleaseAfterCommittedAppointment(',
        $committedProjection, [System.StringComparison]::Ordinal)
} else { -1 }
if ($committedGate -lt 0 -or $militaryRelease -lt 0 -or
    $committedGate -gt $militaryRelease) {
    $failures.Add('civil officer military identity must be released only after appointment commit')
}
Require-Present 'replacement persistence owns one transaction' 'Code/core/court/CourtOfficerReplacementPersistence.cs' 'transaction = pDb.BeginTransaction();'
Require-Present 'replacement closes incumbent before successor insert' 'Code/core/court/CourtOfficerReplacementPersistence.cs' 'OfficialCareerPersistence.StageClose('
Require-Present 'replacement inserts successor in the same transaction' 'Code/core/court/CourtOfficerReplacementPersistence.cs' 'OfficialCareerPersistence.Stage(pDb, transaction, appointmentToken);'
Require-Present 'guest replacement closes affiliation and career together' 'Code/core/court/CourtOfficerReplacementPersistence.cs' 'GuestOfficeEndPersistence.EndInTransaction('
$courtReplacement = Read-Source 'Code/core/court/CourtOfficerReplacementPersistence.cs'
$replacementGuestClose = $courtReplacement.IndexOf(
    'guestResult = GuestOfficeEndPersistence.EndInTransaction(',
    [System.StringComparison]::Ordinal)
$replacementLocalClose = $courtReplacement.IndexOf(
    'OfficialCareerPersistence.StageClose(',
    [System.StringComparison]::Ordinal)
$replacementAppointment = $courtReplacement.IndexOf(
    'OfficialCareerPersistence.Stage(pDb, transaction, appointmentToken);',
    [System.StringComparison]::Ordinal)
$replacementCommit = $courtReplacement.IndexOf('transaction.Commit();',
    [System.StringComparison]::Ordinal)
if ($replacementGuestClose -lt 0 -or $replacementLocalClose -lt 0 -or
    $replacementAppointment -lt 0 -or $replacementCommit -lt 0 -or
    $replacementGuestClose -gt $replacementAppointment -or
    $replacementLocalClose -gt $replacementAppointment -or
    $replacementAppointment -gt $replacementCommit) {
    $failures.Add('court replacement must close either incumbent path before inserting and committing the successor')
}
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

$policyService = Read-Source 'Code/core/policy/KingdomPolicyService.cs'
$noticeGate = $policyService.IndexOf('WarNoticeService.CanCompleteCurrentDeclaration(pKingdom, progress, def.Cost)',
    [System.StringComparison]::Ordinal)
$pointsExit = $policyService.IndexOf('if (points <= 0f) return;', [System.StringComparison]::Ordinal)
if ($noticeGate -lt 0 -or $pointsExit -lt 0 -or $noticeGate -gt $pointsExit) {
    $failures.Add('held war completion gate must run before the political-points early exit')
}

$warCompleteStart = $policyService.IndexOf('private static void CompleteWarDecision(',
    [System.StringComparison]::Ordinal)
$warEffect = if ($warCompleteStart -ge 0) {
    $policyService.IndexOf('ApplyEffect(pKingdom, pDef)', $warCompleteStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
$warClear = if ($warCompleteStart -ge 0) {
    $policyService.IndexOf('ClearDecisionTarget(pKingdom)', $warCompleteStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($warEffect -lt 0 -or $warClear -lt 0 -or $warEffect -gt $warClear) {
    $failures.Add('war decision effect must execute before its target and notice state are cleared')
}

$armyService = Read-Source 'Code/core/lineage/AWArmyService.cs'
$duplicateCleanupStart = $armyService.IndexOf('private static void CleanupDuplicateArmies(',
    [System.StringComparison]::Ordinal)
$duplicateCleanupEnd = if ($duplicateCleanupStart -ge 0) {
    $armyService.IndexOf('private static void MergeDuplicateIntoKeeper(', $duplicateCleanupStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($duplicateCleanupStart -lt 0 -or $duplicateCleanupEnd -lt 0 -or
    $armyService.Substring($duplicateCleanupStart, $duplicateCleanupEnd - $duplicateCleanupStart).Contains(
        'World.world.armies')) {
    $failures.Add('duplicate special-army cleanup must use the kingdom-role index')
}

$cacheRemovalStart = $armyService.IndexOf('private static void RemoveArmyFromCache(',
    [System.StringComparison]::Ordinal)
$cacheRemovalEnd = if ($cacheRemovalStart -ge 0) {
    $armyService.IndexOf('private static string BuildRoleIndexKey(', $cacheRemovalStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($cacheRemovalStart -lt 0 -or $cacheRemovalEnd -lt 0 -or
    $armyService.Substring($cacheRemovalStart, $cacheRemovalEnd - $cacheRemovalStart).Contains(
        'foreach (KeyValuePair<string, long> entry in RoleArmyCache)')) {
    $failures.Add('special army cache removal must use its reverse key index')
}

$findArmyStart = $armyService.IndexOf('public static Army FindArmy(',
    [System.StringComparison]::Ordinal)
$findArmyEnd = if ($findArmyStart -ge 0) {
    $armyService.IndexOf('public static void MarkArmy(', $findArmyStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($findArmyStart -lt 0 -or $findArmyEnd -lt 0 -or
    $armyService.Substring($findArmyStart, $findArmyEnd - $findArmyStart).Contains(
        'World.world.armies')) {
    $failures.Add('runtime special-army lookup must not scan the world army manager')
}

$retirementPatch = Read-Source 'Code/patch/AW_RetirementPatch.cs'
$retirementWarriorGate = $retirementPatch.IndexOf('if (rekt || !warrior) return;',
    [System.StringComparison]::Ordinal)
$retirementSupportedLookup = $retirementPatch.IndexOf(
    'SlaveService.IsSupportedSlaveryActor(__instance)', [System.StringComparison]::Ordinal)
if ($retirementWarriorGate -lt 0 -or $retirementSupportedLookup -lt 0 -or
    $retirementWarriorGate -gt $retirementSupportedLookup) {
    $failures.Add('actor updateAge must reject dead and non-warrior actors before race checks')
}
$retirementCheapGate = $retirementPatch.IndexOf(
    'SoldierRetirementRules.ShouldEnterActorUpdateAgeRetirement(',
    [System.StringComparison]::Ordinal)
$retirementTemporaryLookup = $retirementPatch.IndexOf(
    'TemporaryLevyService.IsTemporaryLevy(__instance)',
    [System.StringComparison]::Ordinal)
if ($retirementCheapGate -lt 0 -or $retirementTemporaryLookup -lt 0 -or
    $retirementCheapGate -gt $retirementTemporaryLookup) {
    $failures.Add('actor updateAge must reject non-warriors before temporary-service lookups')
}

$standingArmyService = Read-Source 'Code/core/lineage/StandingArmyService.cs'
$ordinaryMilitaryStart = $standingArmyService.IndexOf('public static int CountOrdinaryMilitary(',
    [System.StringComparison]::Ordinal)
$ordinaryMilitaryEnd = if ($ordinaryMilitaryStart -ge 0) {
    $standingArmyService.IndexOf('public static bool ShouldKeepWithinOriginalArmyLimit(',
        $ordinaryMilitaryStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($ordinaryMilitaryStart -lt 0 -or $ordinaryMilitaryEnd -lt 0 -or
    $standingArmyService.Substring($ordinaryMilitaryStart,
        $ordinaryMilitaryEnd - $ordinaryMilitaryStart).Contains('foreach (')) {
    $failures.Add('temporary levy city checks must use direct normal-army counts')
}
Require-Present 'standing maintenance publishes one city readiness observation' `
    'Code/core/lineage/StandingArmyService.cs' `
    'KingdomMilitaryReadinessService.ObserveCity(pCity);'

$readinessService = Read-Source 'Code/core/lineage/KingdomMilitaryReadinessService.cs'
$readinessQueryStart = $readinessService.IndexOf('public static bool HasReadyStandingCore(',
    [System.StringComparison]::Ordinal)
$readinessQueryEnd = if ($readinessQueryStart -ge 0) {
    $readinessService.IndexOf('public static void ObserveCity(', $readinessQueryStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($readinessQueryStart -lt 0 -or $readinessQueryEnd -lt 0) {
    $failures.Add('standing-core readiness must expose an indexed query and city observation API')
} else {
    $readinessQueryRegion = $readinessService.Substring($readinessQueryStart,
        $readinessQueryEnd - $readinessQueryStart)
    if ($readinessQueryRegion.Contains('for (') -or
        $readinessQueryRegion.Contains('foreach (')) {
        $failures.Add('standing-core readiness query must never enumerate kingdom cities')
    }
}
if (-not $readinessService.Contains('KingdomMilitaryReadinessRules.MaxCitiesPerWorkItem') -or
    -not $readinessService.Contains('DeferredRuntimeWorkService.EnqueueCoalesced(') -or
    -not $readinessService.Contains('kingdom.cities[i]')) {
    $failures.Add('standing-core readiness backfill must use direct cursors and fixed deferred batches')
}
if (-not $readinessService.Contains('KingdomMilitaryReadinessIndex')) {
    $failures.Add('standing-core counters must use the tested generation index')
}
Require-Present 'warrior death invalidates only its ordinary army city readiness' `
    'Code/patch/AW_ActorDeathPatch.cs' `
    'KingdomMilitaryReadinessService.MarkOrdinaryArmyActorDirty(__instance);'
Require-Present 'city transfer updates standing readiness membership' `
    'Code/patch/AW_ChroniclePatch.cs' `
    'KingdomMilitaryReadinessService.OnCityKingdomChanged('
Require-Present 'city destruction removes standing readiness membership' `
    'Code/patch/AW_StandingArmyPatch.cs' `
    'KingdomMilitaryReadinessService.OnCityDestroyed(__instance);'
Require-Present 'army reassignment dirties only affected city readiness' `
    'Code/patch/AW_StandingArmyPatch.cs' `
    'HarmonyPatch(typeof(Actor), nameof(Actor.setArmy))'
Require-Present 'army reassignment uses coalesced city readiness refresh' `
    'Code/patch/AW_StandingArmyPatch.cs' `
    'KingdomMilitaryReadinessService.MarkArmyCitiesDirty('
if (-not $readinessService.Contains('ResolveOrdinaryArmyCity(')) {
    $failures.Add('army readiness dirties only original ordinary city armies')
}
$markCityDirtyStart = $readinessService.IndexOf('public static void MarkCityDirty(',
    [System.StringComparison]::Ordinal)
$markArmyDirtyStart = $readinessService.IndexOf('public static void MarkArmyCitiesDirty(',
    [System.StringComparison]::Ordinal)
if ($markCityDirtyStart -lt 0 -or $markArmyDirtyStart -lt 0 -or
    -not $readinessService.Substring($markCityDirtyStart,
        $markArmyDirtyStart - $markCityDirtyStart).Contains(
            'if (States.Count == 0) return;')) {
    $failures.Add('load-time army repair cannot enqueue readiness work before the index exists')
}
Require-Present 'archive load rebuilds standing readiness index' `
    'Code/patch/AW_SavePatch.cs' `
    'KingdomMilitaryReadinessService.RebuildRuntime();'
Require-Present 'archive switch clears standing readiness index' `
    'Code/patch/AW_SavePatch.cs' `
    'KingdomMilitaryReadinessService.ClearRuntime();'

$royalGuardService = Read-Source 'Code/core/lineage/RoyalGuardService.cs'
if (-not $royalGuardService.Contains(
        'bool standingCoreReady = !militaryEmergency &&') -or
    $royalGuardService.Contains(
        'bool standingCoreReady = active.Count > 0 ||')) {
    $failures.Add('existing guards may be preserved but must never satisfy the standing-core recruitment gate')
}
if ($royalGuardService.Contains('CollectActiveGuardsFallbackBounded(') -or
    $royalGuardService.Contains('ROYAL_GUARD_ACTIVE_SCAN_CURSOR')) {
    $failures.Add('active guard recovery must use the army or persisted roster, never a partial kingdom population scan')
}
if (-not $royalGuardService.Contains(
        'RoyalGuardMaintenanceRules.ShouldClearStaleGuardStateWithoutRoster(')) {
    $failures.Add('missing army and roster must clear stale kingdom guard hints')
}

$warNoticeService = Read-Source 'Code/core/lineage/WarNoticeService.cs'
$preparationSummaryStart = $warNoticeService.IndexOf(
    'public static bool TryGetPreparationSummary(', [System.StringComparison]::Ordinal)
$preparationSummaryEnd = if ($preparationSummaryStart -ge 0) {
    $warNoticeService.IndexOf('public static ', $preparationSummaryStart + 1,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($preparationSummaryStart -lt 0 -or $preparationSummaryEnd -lt 0) {
    $failures.Add('missing cached war preparation summary API')
} elseif ($warNoticeService.Substring($preparationSummaryStart,
        $preparationSummaryEnd - $preparationSummaryStart).Contains('foreach (')) {
    $failures.Add('policy redraw summary must not enumerate notices, armies, cities, or actors')
}
$noticeArmyChangedStart = $warNoticeService.IndexOf('public static void OnArmyChanged(',
    [System.StringComparison]::Ordinal)
$noticeArmyChangedEnd = if ($noticeArmyChangedStart -ge 0) {
    $warNoticeService.IndexOf('public static void QueueArmyChanged(', $noticeArmyChangedStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($noticeArmyChangedStart -lt 0 -or $noticeArmyChangedEnd -lt 0 -or
    -not $warNoticeService.Substring($noticeArmyChangedStart,
        $noticeArmyChangedEnd - $noticeArmyChangedStart).Contains(
            'ArmyDeploymentService.OnArmyChanged(pKingdom, pArmy, pRosterExpanded);')) {
    $failures.Add('army changes must route once through the defender deployment group')
}
if ($noticeArmyChangedStart -ge 0 -and $noticeArmyChangedEnd -gt $noticeArmyChangedStart -and
    $warNoticeService.Substring($noticeArmyChangedStart,
        $noticeArmyChangedEnd - $noticeArmyChangedStart).Contains(
            'foreach (string signature in signatures)')) {
    $failures.Add('army changes must not fan out synchronously to every incoming notice')
}
$noticeYearStart = $warNoticeService.IndexOf('public static void OnKingdomYear(',
    [System.StringComparison]::Ordinal)
$noticeYearEnd = if ($noticeYearStart -ge 0) {
    $warNoticeService.IndexOf('public static void OnDecisionClearing(', $noticeYearStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($noticeYearStart -lt 0 -or $noticeYearEnd -lt 0 -or
    $warNoticeService.Substring($noticeYearStart, $noticeYearEnd - $noticeYearStart).Contains(
        'ArmyDeploymentService.RefreshNotice(')) {
    $failures.Add('kingdom-year notice maintenance must not rescan deployment cities or armies')
}

$deploymentService = Read-Source 'Code/core/lineage/ArmyDeploymentService.cs'
if (-not $deploymentService.Contains('DefenderNoticeGroups') -or
    -not $deploymentService.Contains('SortedSet<NoticePriority>') -or
    -not $deploymentService.Contains('ResolvePrimaryAssignments(')) {
    $failures.Add('one defender must own one deterministic primary deployment across concurrent notices')
}
$militaryEmergencyService = Read-Source 'Code/core/lineage/MilitaryEmergencyService.cs'
if (-not $militaryEmergencyService.Contains(
        'ArmyDeploymentService.OnKingdomEnteredWar(kingdom);')) {
    $failures.Add('entering any real war must release stale prewar deployment jobs')
}
$deploymentCleanupStart = $deploymentService.IndexOf('private static void CleanupBatch(',
    [System.StringComparison]::Ordinal)
$deploymentCleanupEnd = if ($deploymentCleanupStart -ge 0) {
    $deploymentService.IndexOf('private static void ScheduleDiscovery(', $deploymentCleanupStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
$deploymentCleanupRegion = if ($deploymentCleanupStart -ge 0 -and
    $deploymentCleanupEnd -gt $deploymentCleanupStart) {
    $deploymentService.Substring($deploymentCleanupStart,
        $deploymentCleanupEnd - $deploymentCleanupStart)
} else { '' }
$deploymentOwnRemoval = $deploymentCleanupRegion.IndexOf(
    'assignments.ActorIds.Remove(batch[i]);', [System.StringComparison]::Ordinal)
$deploymentActorResolve = $deploymentCleanupRegion.IndexOf(
    'Actor actor = ResolveActor(batch[i]);', [System.StringComparison]::Ordinal)
if ($deploymentOwnRemoval -lt 0 -or $deploymentActorResolve -lt 0 -or
    $deploymentOwnRemoval -gt $deploymentActorResolve) {
    $failures.Add('deployment cleanup must remove the closing notice member before inspecting actor state')
}

$vanguardService = Read-Source 'Code/core/lineage/TemporarySlaveVanguardService.cs'
$vanguardAssaultStart = $vanguardService.IndexOf(
    'public static bool ShouldDelayBehindVanguard(', [System.StringComparison]::Ordinal)
$vanguardAssaultEnd = if ($vanguardAssaultStart -ge 0) {
    $vanguardService.IndexOf('public static void OnEmergencyChanged(',
        $vanguardAssaultStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($vanguardAssaultStart -lt 0 -or $vanguardAssaultEnd -lt 0) {
    $failures.Add('missing bounded vanguard assault gate')
} else {
    $vanguardAssaultRegion = $vanguardService.Substring($vanguardAssaultStart,
        $vanguardAssaultEnd - $vanguardAssaultStart)
    if ($vanguardAssaultRegion.Contains('foreach (') -or
        $vanguardAssaultRegion.Contains('for (') -or
        $vanguardAssaultRegion.Contains('Finder.')) {
        $failures.Add('vanguard assault ordering must use only indexed army, captain, and city lookups')
    }
}
$candidateStart = $vanguardService.IndexOf('public static void OnCandidateAvailable(',
    [System.StringComparison]::Ordinal)
$candidateEnd = if ($candidateStart -ge 0) {
    $vanguardService.IndexOf('public static void OnMemberInvalidated(', $candidateStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($candidateStart -lt 0 -or $candidateEnd -lt 0 -or
    $vanguardService.Substring($candidateStart, $candidateEnd - $candidateStart).Contains(
        'ResetScanPass(')) {
    $failures.Add('a new slave event must evaluate that actor without restarting a kingdom scan')
}
$candidateRegion = if ($candidateStart -ge 0 -and $candidateEnd -gt $candidateStart) {
    $vanguardService.Substring($candidateStart, $candidateEnd - $candidateStart)
} else { '' }
$candidateForceCleanup = $candidateRegion.IndexOf('if (state.ForceCleanup)',
    [System.StringComparison]::Ordinal)
$candidateClearsCleaning = $candidateRegion.IndexOf('state.Cleaning = false;',
    [System.StringComparison]::Ordinal)
if ($candidateForceCleanup -lt 0 -or $candidateClearsCleaning -lt 0 -or
    $candidateForceCleanup -gt $candidateClearsCleaning) {
    $failures.Add('forced vanguard cleanup must take precedence over new candidate events')
}

$memberInvalidationStart = $vanguardService.IndexOf('public static void OnMemberInvalidated(',
    [System.StringComparison]::Ordinal)
$memberInvalidationEnd = if ($memberInvalidationStart -ge 0) {
    $vanguardService.IndexOf('public static void OnActorKingdomChanged(',
        $memberInvalidationStart, [System.StringComparison]::Ordinal)
} else { -1 }
if ($memberInvalidationStart -lt 0 -or $memberInvalidationEnd -lt 0 -or
    $vanguardService.Substring($memberInvalidationStart,
        $memberInvalidationEnd - $memberInvalidationStart).Contains('ResetScanPass(')) {
    $failures.Add('vanguard casualties must not restart an in-progress kingdom scan')
}

$slaveService = Read-Source 'Code/core/lineage/SlaveService.cs'
$combatCaptureStart = $slaveService.IndexOf('public static bool TryCaptureCombatTarget(',
    [System.StringComparison]::Ordinal)
$combatCaptureEnd = if ($combatCaptureStart -ge 0) {
    $slaveService.IndexOf('public static bool CaptureTargetAsSlave(', $combatCaptureStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
$captureSlaveryGate = if ($combatCaptureStart -ge 0) {
    $slaveService.IndexOf('IsSlaveryEnabled(captorKingdom)', $combatCaptureStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
$captureTargetEligibility = if ($combatCaptureStart -ge 0) {
    $slaveService.IndexOf('CanBeCapturedAsTarget(pTarget', $combatCaptureStart,
        [System.StringComparison]::Ordinal)
} else { -1 }
if ($combatCaptureEnd -lt 0 -or $captureSlaveryGate -lt 0 -or
    $captureTargetEligibility -lt 0 -or $captureSlaveryGate -gt $captureTargetEligibility) {
    $failures.Add('weapon-hit capture must reject non-slavery attackers before target identity checks')
}

if ($failures.Count -gt 0) {
    Write-Host "Source guard failures: $($failures.Count)"
    foreach ($failure in $failures) {
        Write-Host " - $failure"
    }
    exit 1
}

Write-Host 'Source guards passed.'
