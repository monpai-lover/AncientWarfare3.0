using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    internal readonly struct OccupationResistancePatchState
    {
        public OccupationResistancePatchState(bool pActive,
            float pBefore, float pResistance)
        {
            Active = pActive;
            Before = pBefore;
            Resistance = pResistance;
        }

        public bool Active { get; }
        public float Before { get; }
        public float Resistance { get; }
    }

    internal readonly struct RebellionDirectCaptureState
    {
        public RebellionDirectCaptureState(Kingdom pOldOwner,
            Kingdom pCapturer, long pWarId, bool pDirect)
            : this(pOldOwner, pCapturer, pWarId, pDirect, false)
        {
        }

        public RebellionDirectCaptureState(Kingdom pOldOwner,
            Kingdom pCapturer, long pWarId, bool pDirect,
            bool pMandateConquest)
        {
            OldOwner = pOldOwner;
            Capturer = pCapturer;
            WarId = pWarId;
            Direct = pDirect;
            MandateConquest = pMandateConquest;
        }

        public Kingdom OldOwner { get; }
        public Kingdom Capturer { get; }
        public long WarId { get; }
        public bool Direct { get; }
        public bool MandateConquest { get; }
    }

    [HarmonyPatch]
    internal static class AW_CityOccupationAccelerationPatch
    {
        [System.ThreadStatic] private static City _captureResistanceCity;
        [System.ThreadStatic] private static float _captureBefore;
        [System.ThreadStatic] private static float _captureResistance;
        [System.ThreadStatic] private static bool _finishAdjusted;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "updateCapture")]
        public static bool UpdateCapture_Prefix(City __instance,
            float pElapsed, out OccupationResistancePatchState __state)
        {
            __state = default;
            ClearCaptureResistanceContext();
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                Kingdom capturer = __instance?.getCapturingKingdom();
                if (ZhuluWarService.IsOpposingZhuluCapture(__instance,
                        capturer)) return true;
                if (WarScoreService.RetryPendingCityOccupation(__instance))
                    return false;
                if (WarScoreService.ShouldHoldFrozenOccupation(__instance))
                    return false;
                Kingdom defender = __instance?.kingdom;
                if (defender?.data != null && !defender.isRekt())
                {
                    float resistance = KingdomPolicyEffectService
                        .Read(defender).OccupationResistance;
                    if (resistance > 0f)
                    {
                        float before = __instance.getCaptureTicks();
                        __state = new OccupationResistancePatchState(
                            pActive: true, before, resistance);
                        _captureResistanceCity = __instance;
                        _captureBefore = before;
                        _captureResistance = resistance;
                    }
                }
                return true;
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.OccupationIndex, benchmark);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "updateCapture")]
        public static void UpdateCapture_Postfix(City __instance,
            OccupationResistancePatchState __state)
        {
            try
            {
                if (__state.Active && !_finishAdjusted &&
                    __instance?.data != null)
                {
                    float current = __instance.getCaptureTicks();
                    float adjusted = __state.Before +
                        CityOccupationAccelerationRules.ApplyResistance(
                            current - __state.Before,
                            __state.Resistance);
                    CityOccupationAccelerationService.TrySetCaptureProgress(
                        __instance, adjusted);
                }
                bool active = false;
                Kingdom controller = null;
                try
                {
                    controller = __instance?.getCapturingKingdom();
                    active = __instance?.isGettingCaptured() == true &&
                             __instance.getCaptureTicks() > 0f;
                }
                catch { }
                if (CityMilitaryThreatFacts.ObserveCaptureState(
                        __instance, controller, active))
                {
                    WarRefugeeService.OnCityThreatStateChanged(__instance, active);
                    KingdomWarDirectorService.OnCityThreatStateObserved(
                        __instance);
                }
            }
            finally
            {
                ClearCaptureResistanceContext();
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.addCapturePoints),
            new[] { typeof(BaseSimObject), typeof(int) })]
        public static void AddCapturePointsObject_Postfix(City __instance,
            BaseSimObject pObject, bool __state)
        {
            if (!__state) return;
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                CityOccupationAccelerationService.
                    RecordActiveMilitaryPresence(__instance, pObject);
                WartimeGarrisonService.OnCityThreatChanged(__instance);
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.OccupationIndex, benchmark);
            }
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(City), nameof(City.addCapturePoints),
            new[] { typeof(BaseSimObject), typeof(int) })]
        public static bool AddCapturePointsObject_Prefix(
            BaseSimObject pObject, out bool __state)
        {
            __state = OccupiedCityCivilianProtectionService.
                CanActorContributeCapturePoints(pObject);
            return __state;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "clearCurrentCaptureAmounts")]
        public static void ClearCurrentCaptureAmounts_Postfix(City __instance)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                CityOccupationAccelerationService.
                    ClearActiveMilitaryPresence(__instance);
                WartimeGarrisonService.OnCityThreatChanged(__instance);
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.OccupationIndex, benchmark);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "clearCapture")]
        public static void ClearCapture_Postfix(City __instance)
        {
            CityMilitaryThreatFacts.ClearCaptureState(__instance);
            WarRefugeeService.OnCityThreatStateChanged(__instance, false);
            WarScoreService.OnCaptureProgressCleared(__instance);
            KingdomWarDirectorService.OnCityControlChanged(__instance, null);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "finishCapture")]
        public static bool FinishCapture_Prefix(City __instance,
            ref Kingdom pNewKingdom,
            out RebellionDirectCaptureState __state)
        {
            Kingdom oldOwner = __instance?.kingdom;
            __state = new RebellionDirectCaptureState(oldOwner,
                pNewKingdom, -1L, pDirect: false);
            PeasantRebelBanditStrongholdService.TryHandleCapture(
                __instance, pNewKingdom, out bool strongholdHandled);
            if (strongholdHandled) return false;
            bool banditSuppressionCapture =
                WarScoreService.IsActiveBanditSuppressionWar(
                    __instance, pNewKingdom);
            bool banditCaptureAllowed =
                CityOccupationAccelerationRules.
                    ShouldCommitBanditSuppressionCapture(
                        banditSuppressionCapture,
                        PeasantRebelRouteService.IsBanditOrEntering(
                            pNewKingdom),
                        pNewKingdom == __instance?.kingdom);
            __state = new RebellionDirectCaptureState(oldOwner, pNewKingdom,
                -1L, pDirect: false);
            if (!banditCaptureAllowed &&
                !PeasantRebelRouteService.CanAcquireCity(
                    pNewKingdom, __instance)) return false;
            if (ZhuluWarService.IsOpposingZhuluCapture(__instance,
                    pNewKingdom)) return true;
            if (_captureResistanceCity == __instance &&
                _captureResistance > 0f)
            {
                float current = __instance.getCaptureTicks();
                float adjusted = _captureBefore +
                    CityOccupationAccelerationRules.ApplyResistance(
                        current - _captureBefore, _captureResistance);
                _finishAdjusted = true;
                CityOccupationAccelerationService.TrySetCaptureProgress(
                    __instance, adjusted);
                if (adjusted < 100f) return false;
            }
            if (!CityOccupationAccelerationService.HasReachedNaturalCaptureLimit(__instance))
                return false;
            if (RebellionDirectTerritoryTransferService.TryResolve(
                    __instance, pNewKingdom, out War rebellionWar))
            {
                __state = new RebellionDirectCaptureState(oldOwner,
                    pNewKingdom, rebellionWar.data.id, pDirect: true);
                return true;
            }
            if (WarTerritoryService.TryResolveMandateConquestDirect(
                    __instance, pNewKingdom, out War mandateWar))
            {
                __state = new RebellionDirectCaptureState(oldOwner,
                    pNewKingdom, mandateWar.data.id, pDirect: true,
                    pMandateConquest: true);
                return true;
            }
            Kingdom hostileParticipant = pNewKingdom;
            pNewKingdom = VassalCaptureService.ResolveCaptureRecipient(
                __instance, pNewKingdom);
            banditSuppressionCapture =
                WarScoreService.IsActiveBanditSuppressionWar(
                    __instance, pNewKingdom);
            banditCaptureAllowed =
                CityOccupationAccelerationRules.
                    ShouldCommitBanditSuppressionCapture(
                        banditSuppressionCapture,
                        PeasantRebelRouteService.IsBanditOrEntering(
                            pNewKingdom),
                        pNewKingdom == __instance?.kingdom);
            if (!banditCaptureAllowed &&
                !PeasantRebelRouteService.CanAcquireCity(
                    pNewKingdom, __instance)) return false;
            if (banditCaptureAllowed) return true;
            bool activeHostileWar = WarScoreService.HasActiveHostileWar(
                __instance, pNewKingdom) ||
                WarScoreService.HasActiveHostileWar(__instance,
                    hostileParticipant);
            bool freezeRecorded = WarScoreService.TryFreezeCityOccupation(
                __instance, pNewKingdom);
            if (freezeRecorded)
            {
                KingdomWarDirectorService.OnCityControlChanged(__instance,
                    pNewKingdom, __state.OldOwner);
            }
            if (CityOccupationAccelerationRules.
                    ShouldBlockPermanentTransfer(activeHostileWar,
                        freezeRecorded, pPeaceExecution: false))
            {
                if (!freezeRecorded)
                {
                    WarScoreService.HoldPendingCityOccupation(__instance,
                        pNewKingdom, hostileParticipant);
                    KingdomWarDirectorService.OnCityControlChanged(
                        __instance, pNewKingdom, __state.OldOwner);
                }
                return false;
            }
            if (freezeRecorded) return false;
            return !CityOccupationAccelerationService.
                TryQueueNonTerritorialSettlementAtCaptureLimit(__instance,
                    pNewKingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "finishCapture")]
        public static void FinishCapture_Postfix(City __instance,
            RebellionDirectCaptureState __state, bool __runOriginal)
        {
            if (!__runOriginal) return;
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                CityMilitaryThreatFacts.ClearCaptureState(__instance);
                WarRefugeeService.OnCityThreatStateChanged(__instance, false);
                WartimeGarrisonService.OnCityOwnerChanged(__instance,
                    __state.OldOwner);
                KingdomWarDirectorService.OnCityControlChanged(__instance,
                    __instance.kingdom, __state.OldOwner);
                ZhuluCapitalBreakthroughService.TryApplyAfterCapture(
                    __instance, __instance?.kingdom);
                if (__state.Direct && __instance?.kingdom ==
                    __state.Capturer)
                {
                    if (__state.MandateConquest)
                    {
                        War mandateWar = WarPeaceSettlementWorld.FindWar(
                            __state.WarId);
                        if (mandateWar?.data != null &&
                            !mandateWar.hasEnded())
                            World.world?.wars?.endWar(mandateWar,
                                WarWinner.Attackers);
                        return;
                    }
                    WarScoreService.ClearDirectRebellionTransferState(
                        __state.WarId, __instance.id);
                    War war = WarPeaceSettlementWorld.FindWar(__state.WarId);
                    if (RebellionDirectTerritoryTransferService.
                            BlocksOrdinarySettlement(war))
                    {
                        CityReservePoolService.RefreshCapturedCity(__instance);
                        RebellionCollapseSettlementService.QueueIfCollapsed(war);
                    }
                }
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.OccupationIndex, benchmark);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.joinAnotherKingdom))]
        public static bool JoinCapturedCity_Prefix(City __instance,
            ref Kingdom pNewSetKingdom, bool pCaptured)
        {
            bool banditSuppressionCapture =
                WarScoreService.IsActiveBanditSuppressionWar(
                    __instance, pNewSetKingdom);
            bool banditCaptureAllowed =
                CityOccupationAccelerationRules.
                    ShouldCommitBanditSuppressionCapture(
                        banditSuppressionCapture,
                        PeasantRebelRouteService.IsBanditOrEntering(
                            pNewSetKingdom),
                        pNewSetKingdom == __instance?.kingdom);
            if (!banditCaptureAllowed &&
                !PeasantRebelRouteService.CanAcquireCity(
                    pNewSetKingdom, __instance)) return false;
            if (banditCaptureAllowed) return true;
            if (!pCaptured) return true;
            if (ZhuluWarService.IsOpposingZhuluCapture(__instance,
                    pNewSetKingdom)) return true;
            if (RebellionDirectTerritoryTransferService.TryResolve(
                    __instance, pNewSetKingdom, out _)) return true;
            if (WarTerritoryService.TryResolveMandateConquestDirect(
                    __instance, pNewSetKingdom, out _)) return true;
            pNewSetKingdom = VassalCaptureService.ResolveCaptureRecipient(
                __instance, pNewSetKingdom);
            return PeasantRebelRouteService.CanAcquireCity(
                pNewSetKingdom, __instance);
        }

        private static void ClearCaptureResistanceContext()
        {
            _captureResistanceCity = null;
            _captureBefore = 0f;
            _captureResistance = 0f;
            _finishAdjusted = false;
        }
    }
}
