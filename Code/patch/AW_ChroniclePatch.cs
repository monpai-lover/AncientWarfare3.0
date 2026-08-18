using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     编年史新增钩点:
    ///     - Postfix Kingdom.newCivKingdom —— 建国(internal,用字符串方法名)。
    ///     - Postfix KingdomManager.removeObject —— 亡国(public override,KingdomManager 自身声明,typeof 正确)。
    ///     - Prefix  City.setKingdom —— 城市易主(internal;Prefix 时 city.kingdom 仍是旧国,参数为新国;
    ///       pFromLoad 读档回填跳过)。
    ///     成王/换君事件在本 patch 的 Kingdom.setKing Postfix 里最后统一写入。
    /// </summary>
    [HarmonyPatch]
    public static class AW_ChroniclePatch
    {
        public readonly struct RebellionPatchState
        {
            public RebellionPatchState(Kingdom pOriginalKingdom,
                bool pRestorationRedirected)
            {
                OriginalKingdom = pOriginalKingdom;
                RestorationRedirected = pRestorationRedirected;
            }

            public Kingdom OriginalKingdom { get; }
            public bool RestorationRedirected { get; }
        }

        private static readonly System.Reflection.MethodInfo RemoveCitySoldiers =
            AccessTools.Method(typeof(City), "removeSoldiers");

        // 建国(newCivKingdom 是 internal,用字符串名避免可见性问题)
        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.makeNewCivKingdom))]
        public static void MakeNewCivKingdom_Postfix(Kingdom __result)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (KingdomIdentityContinuityService.ShouldSuppressNewKingdomEffects(__result)) return;
            ChronicleEvents.OnKingdomFounded(__result);
            HierarchicalVassalMapModeService.MarkHierarchyDirty(__result);
            WesternLineageMigrationService.Request(__result);
        }

        // 亡国(removeObject 是 KingdomManager 自身的 public override,typeof(KingdomManager) 正确)
        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.removeObject))]
        internal static void RemoveKingdom_Prefix(Kingdom pKingdom,
            out VassalService.KingdomDestroyWarCleanupState __state)
        {
            // UI and map-mode references are local state and must be cleared
            // even when the destruction is being applied on a replica.
            KingdomSelectionLifecycleService.OnKingdomDestroying(pKingdom);
            bool routeAuthority =
                PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) &&
                !AW3MultiplayerReplicaScope.IsApplying;
            PeasantRebelRouteService.OnKingdomDestroying(pKingdom,
                pAuthoritative: routeAuthority);
            PeasantRebelGuiyiService.OnKingdomDestroying(pKingdom);
            if (AW3MultiplayerReplicaScope.IsApplying)
            {
                __state = default;
                return;
            }
            __state = VassalService.CaptureKingdomDestroyWarCleanup(pKingdom);
            KingdomArchiveWriter.EnsureRow(pKingdom);
            RoyalAsylumService.NaturalizeBeforeExtinction(pKingdom);
            try { KingdomIdentityContinuityService.CaptureBeforeDestruction(pKingdom); }
            catch (System.Exception e) { ModClass.LogWarning("Kingdom continuity capture failed: " + e.Message); }
            bool restorationCampaign = false;
            try { restorationCampaign = AutonomousRestorationService.OnKingdomDestroying(pKingdom); }
            catch (System.Exception e)
            {
                ModClass.LogWarning("Restoration campaign fall failed: " + e.Message);
                restorationCampaign = AutonomousRestorationService.IsActiveCampaignKingdom(pKingdom);
            }
            if (!restorationCampaign)
            {
                try { RoyalClaimService.CreateClaimsFromFallenKingdom(pKingdom); }
                catch (System.Exception e) { ModClass.LogWarning("Fallen kingdom claim capture failed: " + e.Message); }
            }
            VassalService.OnKingdomDestroyed(pKingdom);
            DiplomacyExtinctionService.OnKingdomDestroying(pKingdom);
            WarNoticeService.OnKingdomDestroying(pKingdom);
            VirtualNobleTitleService.OnKingdomDestroying(pKingdom);
            MilitaryEmergencyService.OnKingdomDestroying(pKingdom);
            TemporaryLevyService.OnKingdomDestroying(pKingdom);
            WartimeGarrisonService.OnKingdomDestroying(pKingdom);
            TemporarySlaveVanguardService.OnKingdomDestroying(pKingdom);
            RoyalGuardService.OnKingdomDestroying(pKingdom);
            GeneralService.OnKingdomDestroying(pKingdom);
            FeudatoryService.OnKingdomDestroying(pKingdom);
            KingdomMilitaryReadinessService.OnKingdomDestroying(pKingdom);
            ArmyRetreatService.OnKingdomDestroying(pKingdom);
            CivilServiceExamService.OnKingdomDestroying(pKingdom);
            CourtService.OnKingdomDestroying(pKingdom);
            FormerHeirService.ArchiveAndClear(pKingdom);
            ChronicleEvents.OnKingdomDestroyed(pKingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.removeObject))]
        internal static void RemoveKingdom_Postfix(Kingdom pKingdom,
            VassalService.KingdomDestroyWarCleanupState __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            VassalService.CleanupWarsAfterKingdomDestroyed(__state);
        }

        // 城市易主(setKingdom 是 internal,用字符串名;Prefix 取旧国——原方法未执行,__instance.kingdom 仍是旧国)
        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        public static void CitySetKingdom_Prefix(City __instance, Kingdom pKingdom, bool pFromLoad, out Kingdom __state)
        {
            Kingdom oldKingdom = __instance != null ? __instance.kingdom : null;
            __state = oldKingdom;
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (!pFromLoad)
                MandateService.OnCityTransferStarting(__instance,
                    oldKingdom, pKingdom);
            ChronicleEvents.OnCityTransferred(__instance, oldKingdom, pKingdom, pFromLoad);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        public static void CitySetKingdom_Postfix(City __instance, Kingdom pKingdom, bool pFromLoad, Kingdom __state)
        {
            if (!WarRemainingTerritoryOrchestration.ShouldRebaseOwnerChange(
                    pFromLoad, AW3MultiplayerReplicaScope.IsApplying)) return;
            WarParticipantCityBaselineService.OnCityOwnerChanged(
                __instance, __state, __instance?.kingdom ?? pKingdom);
            KingdomMilitaryReadinessService.OnCityKingdomChanged(
                __instance, __state, __instance?.kingdom ?? pKingdom);
            ArmyRetreatService.OnCityControlChanged(__instance, __state);
            WartimeGarrisonService.OnCityOwnerChanged(__instance, __state);
            KingdomArchiveWriter.Upsert(__state);
            KingdomArchiveWriter.Upsert(__instance?.kingdom ?? pKingdom);
            CityTechService.OnCityChangedKingdom(__instance, __instance?.kingdom ?? pKingdom);
            ForeignOccupationService.OnCityTransferred(__instance, __state, __instance?.kingdom ?? pKingdom);
            GeneralService.OnCityTransferred(__instance, __state, __instance?.kingdom ?? pKingdom);
            WarTerritoryService.OnCityTransferred(__instance, __state, __instance?.kingdom ?? pKingdom);
            AutonomousRestorationService.OnCityTransferred(
                __instance, __state, __instance?.kingdom ?? pKingdom);
            MandateService.OnCityTransferred(
                __instance, __state, __instance?.kingdom ?? pKingdom);
            FeudatoryService.OnCityTransferred(
                __instance, __state, __instance?.kingdom ?? pKingdom);
            FeudatoryJingnanService.OnCityTransferred(
                __instance, __state, __instance?.kingdom ?? pKingdom);
            CoupRestorationService.OnCityTransferred(
                __instance, __state, __instance?.kingdom ?? pKingdom);
            KingdomStrategyRevisionService.MarkChanged(
                __state?.id ?? -1L,
                (__instance?.kingdom ?? pKingdom)?.id ?? -1L);
            CitySchoolSnapshotService.MarkDirty(__instance);
            HierarchicalVassalMapModeService.MarkCityOwnershipChanged(
                __instance, __state, __instance?.kingdom ?? pKingdom);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.joinAnotherKingdom))]
        public static void CityJoinAnotherKingdom_Prefix(City __instance,
            Kingdom pNewSetKingdom, bool pCaptured)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (!CityOwnershipTransferRules.ShouldDisbandLocalArmy(
                    __instance?.kingdom != null,
                    __instance?.kingdom != pNewSetKingdom,
                    pCaptured)) return;
            try { RemoveCitySoldiers.Invoke(__instance, null); }
            catch (System.Exception e)
            {
                ModClass.LogWarning("City transfer army cleanup failed: " +
                                    e.Message);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.joinAnotherKingdom))]
        public static void CityJoinAnotherKingdom_Postfix(City __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            FeudatoryJingnanService.OnCityTransferCompleted(__instance);
            CoupRestorationService.OnCityTransferCompleted(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.destroyCity))]
        public static void DestroyCity_Postfix(City __instance)
        {
            HierarchicalVassalMapModeService.RemoveCity(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.addZone))]
        public static void CityAddZone_Postfix(City __instance, TileZone pZone)
        {
            long diagnostic = RuntimePerformanceDiagnostic.
                BeginContinuousScope();
            try
            {
                HierarchicalVassalMapModeService.MarkCityZoneGeometryDirty(__instance, pZone);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndContinuousStage(
                    "city_add_zone", diagnostic);
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (!SetKingPostfixRules.ShouldRun(pFromLoad, pActor != null && __instance?.king == pActor)) return;
            if (KingdomIdentityContinuityService.ShouldSuppressNewKingdomEffects(__instance)) return;
            ChronicleEvents.OnKingChanged(__instance, pActor);
            CitySchoolSnapshotService.MarkKingdomDirty(__instance);
        }

        // 建城(newCityEvent 在 City 自身声明,typeof 正确;纯新建城,读档走 loadCity 不经此)。
        // Postfix:此时 generateName 已跑,city.data.name / city.kingdom 就绪,记城市史起点 found 事件。
        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.newCityEvent))]
        public static void NewCityEvent_Postfix(City __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            KingdomMilitaryReadinessService.OnCityKingdomChanged(
                __instance, null, __instance?.kingdom);
            ArmyRetreatService.OnCityControlChanged(__instance, null);
            ChronicleEvents.OnCityFounded(__instance);
            CityTechService.OnCityFounded(__instance);
            HierarchicalVassalMapModeService.MarkCityOwnershipChanged(
                __instance, null, __instance?.kingdom);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(DiplomacyHelpersRebellion),
            nameof(DiplomacyHelpersRebellion.startRebellion))]
        public static bool VanillaRebellion_Prefix(Actor pActor,
            out RebellionPatchState __state)
        {
            Kingdom original = pActor?.kingdom;
            if (AW3MultiplayerReplicaScope.IsApplying)
            {
                __state = new RebellionPatchState(original, false);
                return true;
            }
            RestorationRebellionStartOutcome outcome =
                RestorationRebellionRedirectService.TryRedirect(
                    pActor, pActor?.city, out _);
            bool redirected = RestorationRebellionRedirectRules
                .ShouldSuppressVanilla(outcome);
            __state = new RebellionPatchState(original, redirected);
            return !redirected;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DiplomacyHelpersRebellion),
            nameof(DiplomacyHelpersRebellion.startRebellion))]
        public static void VanillaRebellion_Postfix(Actor pActor,
            RebellionPatchState __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__state.RestorationRedirected ||
                __state.OriginalKingdom?.data == null ||
                pActor?.data == null ||
                pActor.kingdom == __state.OriginalKingdom) return;
            ChronicleEvents.OnRebellion(pActor,
                __state.OriginalKingdom);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(City), "useInspire")]
        public static bool InspiredRebellion_Prefix(City __instance,
            Actor pActor, out RebellionPatchState __state)
        {
            Kingdom original = __instance?.kingdom ?? pActor?.kingdom;
            if (AW3MultiplayerReplicaScope.IsApplying)
            {
                __state = new RebellionPatchState(original, false);
                return true;
            }
            RestorationRebellionStartOutcome outcome =
                RestorationRebellionRedirectService.TryRedirect(
                    pActor, __instance, out _);
            bool redirected = RestorationRebellionRedirectRules
                .ShouldSuppressVanilla(outcome);
            __state = new RebellionPatchState(original, redirected);
            return !redirected;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "useInspire")]
        public static void InspiredRebellion_Postfix(Actor pActor,
            RebellionPatchState __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__state.RestorationRedirected ||
                __state.OriginalKingdom?.data == null ||
                pActor?.data == null ||
                pActor.kingdom == __state.OriginalKingdom) return;
            ChronicleEvents.OnRebellion(pActor,
                __state.OriginalKingdom);
        }
    }
}
