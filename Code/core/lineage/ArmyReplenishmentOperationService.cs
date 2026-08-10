using System;
using System.Collections.Generic;
using System.Globalization;

namespace AncientWarfare3.core.lineage
{
    internal sealed class ArmyReplenishmentOperationState
    {
        internal long ArmyId { get; set; } = -1L;
        internal long KingdomId { get; set; } = -1L;
        internal long SourceCityId { get; set; } = -1L;
        internal int ApprovedShortage { get; set; }
        internal int EnlistedCount { get; set; }
        internal double StartTime { get; set; }
        internal double DeadlineTime { get; set; }
    }

    internal static class ArmyReplenishmentOperationService
    {
        private static readonly SortedSet<long> ActiveArmyIds =
            new SortedSet<long>();
        private static long _cursorAfterArmyId = -1L;

        internal static bool TryRead(Army pArmy,
            out ArmyReplenishmentOperationState pState)
        {
            pState = null;
            if (pArmy?.data == null) return false;

            pArmy.data.get(LineageKeys.ARMY_REPLENISHMENT_OPERATION_VERSION,
                out int version, 0);
            if (version == 0) return false;

            pArmy.data.get(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_KINGDOM_ID,
                out long kingdomId, -1L);
            pArmy.data.get(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_SOURCE_CITY_ID,
                out long sourceCityId, -1L);
            pArmy.data.get(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_APPROVED_SHORTAGE,
                out int approvedShortage, 0);
            pArmy.data.get(LineageKeys.ARMY_REPLENISHMENT_OPERATION_ENLISTED,
                out int persistedEnlisted, 0);
            pArmy.data.get(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_START_TIME,
                out string startText, string.Empty);
            pArmy.data.get(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_DEADLINE_TIME,
                out string deadlineText, string.Empty);

            if (version != ArmyReplenishmentOperationRules.SchemaVersion ||
                kingdomId < 0L || sourceCityId < 0L ||
                approvedShortage <= 0 ||
                !TryParseFinite(startText, out double startTime) ||
                !TryParseFinite(deadlineText, out double persistedDeadline))
            {
                Clear(pArmy);
                return false;
            }

            Kingdom kingdom = SafeKingdom(pArmy);
            if (!IsLiveOrdinaryArmy(pArmy) ||
                !IsLiveKingdom(kingdom) || kingdom.id != kingdomId ||
                !TryGetActiveMissionWar(pArmy, kingdom, out _))
            {
                Clear(pArmy);
                return false;
            }

            double deadline = ArmyReplenishmentOperationRules.ResolveDeadline(
                startTime, persistedDeadline);
            int enlisted = ArmyReplenishmentOperationRules.ClampEnlisted(
                approvedShortage, persistedEnlisted);
            pState = new ArmyReplenishmentOperationState
            {
                ArmyId = pArmy.id,
                KingdomId = kingdomId,
                SourceCityId = sourceCityId,
                ApprovedShortage = approvedShortage,
                EnlistedCount = enlisted,
                StartTime = startTime,
                DeadlineTime = deadline
            };

            if (enlisted != persistedEnlisted || deadline != persistedDeadline)
                Persist(pArmy, pState);
            ActiveArmyIds.Add(pArmy.id);
            return true;
        }

        internal static ArmyReplenishmentOperationState Ensure(Army pArmy,
            Kingdom pKingdom, City pSourceCity, int pRequestedShortage,
            double pStartTime)
        {
            if (TryRead(pArmy, out ArmyReplenishmentOperationState existing))
                return existing;
            if (!IsLiveOrdinaryArmy(pArmy) ||
                !IsLiveKingdom(pKingdom) || SafeKingdom(pArmy) != pKingdom ||
                !IsControlledCity(pSourceCity, pKingdom) ||
                !TryGetActiveMissionWar(pArmy, pKingdom, out _) ||
                pRequestedShortage <= 0 ||
                !ArmyMobilizationRules.CanConsume(
                    CityReservePoolService.ResolveMobilizationPhase(
                        pKingdom)) ||
                !IsFinite(pStartTime)) return null;

            int approved =
                ArmyReplenishmentOperationRules.ResolveApprovedShortage(
                    existingApproved: 0,
                    requestedShortage: pRequestedShortage);
            var state = new ArmyReplenishmentOperationState
            {
                ArmyId = pArmy.id,
                KingdomId = pKingdom.id,
                SourceCityId = pSourceCity.id,
                ApprovedShortage = approved,
                EnlistedCount = 0,
                StartTime = pStartTime,
                DeadlineTime = pStartTime +
                    ArmyReplenishmentOperationRules.DurationWorldSeconds
            };
            Persist(pArmy, state);
            ActiveArmyIds.Add(pArmy.id);
            return state;
        }

        internal static bool IsDepartureReleased(Army pArmy)
        {
            if (!TryRead(pArmy, out ArmyReplenishmentOperationState state))
                return true;
            if (!TryGetLiveShortage(pArmy, out int liveShortage))
                return false;
            int living;
            try { living = Math.Max(0, pArmy.countUnits()); }
            catch { living = 0; }
            int target = living + Math.Max(0, liveShortage);
            return ArmyReplenishmentOperationRules.ShouldReleaseDeparture(
                       living, target) ||
                   CurrentWorldTime() >= state.DeadlineTime;
        }

        internal static bool TryGetSourceReserveAvailability(Army pArmy,
            out bool pAvailable)
        {
            pAvailable = false;
            if (pArmy?.data == null) return false;
            Kingdom kingdom = SafeKingdom(pArmy);
            if (!IsLiveKingdom(kingdom)) return false;

            City sourceCity = null;
            if (TryRead(pArmy,
                    out ArmyReplenishmentOperationState operation))
                sourceCity = FindCity(operation.SourceCityId);
            if (sourceCity == null)
            {
                try { sourceCity = AWArmyService.FindAnchorCity(pArmy); }
                catch { sourceCity = null; }
            }
            if (sourceCity?.data == null) return false;
            if (!IsControlledCity(sourceCity, kingdom)) return true;
            ArmyMobilizationPhase phase = CityReservePoolService.
                ResolveMobilizationPhase(kingdom);
            if (!ArmyMobilizationRules.CanConsume(phase)) return true;
            int available;
            if (phase == ArmyMobilizationPhase.War &&
                TryGetActiveMissionWar(pArmy, kingdom, out War war))
                available = CityReservePoolService.OpenOrReadWarReserve(
                    sourceCity, war.data.id);
            else
                available = CityReservePoolService.CountAvailable(sourceCity);
            if (available > 0)
            {
                pAvailable = true;
                return true;
            }
            if (!TemporaryLevyService.HasConfirmedReserveExhaustion(
                    kingdom, pArmy)) return false;
            return true;
        }

        internal static void ProcessAuthorityCycle()
        {
            IReadOnlyList<long> batch = TakeActiveBatch(
                ArmyReplenishmentOperationRules.MaximumOperationsPerCycle);
            double now = CurrentWorldTime();
            for (int i = 0; i < batch.Count; i++)
                ProcessOne(batch[i], now);
        }

        internal static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.armies == null) return;
            foreach (Army army in World.world.armies)
                TryRead(army, out _);
        }

        internal static void ClearRuntime()
        {
            ActiveArmyIds.Clear();
            _cursorAfterArmyId = -1L;
        }

        internal static void OnArmyDisposed(Army pArmy)
        {
            Clear(pArmy);
        }

        internal static void OnArmyKingdomChanged(Army pArmy)
        {
            if (pArmy?.data == null) return;
            if (TryRead(pArmy, out ArmyReplenishmentOperationState state) &&
                SafeKingdom(pArmy)?.id == state.KingdomId) return;
            Clear(pArmy);
        }

        internal static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null || ActiveArmyIds.Count == 0) return;
            var snapshot = new List<long>(ActiveArmyIds);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Army army = FindArmy(snapshot[i]);
                Kingdom kingdom = SafeKingdom(army);
                if (!TryGetMissionWar(army, kingdom,
                        out War missionWar))
                {
                    Clear(army);
                    continue;
                }
                if (missionWar.data.id != pWar.data.id) continue;
                Clear(army);
            }
        }

        internal static void Clear(Army pArmy)
        {
            if (pArmy == null) return;
            ActiveArmyIds.Remove(pArmy.id);
            if (pArmy.data == null) return;
            pArmy.data.removeInt(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_VERSION);
            pArmy.data.removeLong(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_KINGDOM_ID);
            pArmy.data.removeLong(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_SOURCE_CITY_ID);
            pArmy.data.removeInt(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_APPROVED_SHORTAGE);
            pArmy.data.removeInt(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_ENLISTED);
            pArmy.data.removeString(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_START_TIME);
            pArmy.data.removeString(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_DEADLINE_TIME);
        }

        internal static bool CanUseReservePool(Army pArmy)
        {
            try
            {
                if (pArmy?.data == null) return false;
                bool ordinary =
                    ArmyNativeNameService.IsOrdinaryArmy(pArmy);
                bool royalGuard =
                    RoyalGuardService.IsRoyalGuardArmy(pArmy);
                return ArmyReplenishmentOperationRules.CanUseReservePool(
                    ordinary, royalGuard);
            }
            catch { return false; }
        }

        private static void ProcessOne(long pArmyId, double pNow)
        {
            Army army = FindArmy(pArmyId);
            if (army?.data == null)
            {
                ActiveArmyIds.Remove(pArmyId);
                return;
            }
            if (!TryRead(army, out ArmyReplenishmentOperationState state) ||
                !TryGetLiveShortage(army, out int liveShortage))
            {
                Clear(army);
                return;
            }
            if (ArmyReplenishmentOperationRules.ShouldFinishEarly(
                    liveShortage))
            {
                Clear(army);
                ArmyReplenishmentCompletionService.Complete(army);
                return;
            }

            int requested = ArmyReplenishmentOperationRules.BatchRequest(
                state.ApprovedShortage, state.EnlistedCount, liveShortage,
                state.StartTime, pNow);
            bool deadlineReached = pNow >= state.DeadlineTime;
            bool confirmedExhausted = false;
            Kingdom kingdom = SafeKingdom(army);
            City preferredCity = FindCity(state.SourceCityId);
            long missionWarId = ResolveMissionWarId(army, kingdom);
            if (!ArmyRtsControllerService.CanGenerateWartimeReplacements(
                    army, preferredCity, missionWarId)) return;
            if (requested > 0)
            {
                int enlisted = RecruitFormalWarShortage(army, kingdom,
                    preferredCity, requested, out confirmedExhausted,
                    out List<Actor> recruits);
                if (enlisted > 0)
                {
                    state.EnlistedCount =
                        ArmyReplenishmentOperationRules.ClampEnlisted(
                            state.ApprovedShortage,
                            state.EnlistedCount + enlisted);
                    Persist(army, state);
                    TeleportSuccessfulRecruits(army, recruits);
                    KingdomWarDirectorService.QueueArmyChanged(kingdom);
                }
            }

            bool shortageReadable = TryGetLiveShortage(army,
                out liveShortage);
            if (!shortageReadable)
            {
                Clear(army);
                return;
            }
            bool reservesConfirmedExhausted = confirmedExhausted ||
                deadlineReached && CityReservePoolService.
                    OpenOrReadWarReserve(preferredCity,
                        ResolveMissionWarId(army, kingdom)) <= 0;
            if (ArmyReplenishmentOperationRules.ShouldFinish(
                    liveShortage <= 0, reservesConfirmedExhausted,
                    deadlineReached))
            {
                if (liveShortage > 0 && reservesConfirmedExhausted)
                    TemporaryLevyService.RecordConfirmedReserveExhaustion(
                        kingdom, army, liveShortage);
                Clear(army);
                ArmyReplenishmentCompletionService.Complete(army);
            }
        }

        private static int RecruitFormalWarShortage(Army army,
            Kingdom kingdom, City sourceCity, int requested,
            out bool confirmedExhausted, out List<Actor> recruits)
        {
            recruits = new List<Actor>(Math.Min(Math.Max(0, requested),
                TemporaryLevyRules.MaxRecruitsPerWorkItem));
            confirmedExhausted = false;
            if (sourceCity?.data == null || kingdom?.data == null ||
                CityReservePoolService.ResolveMobilizationPhase(kingdom) !=
                ArmyMobilizationPhase.War ||
                !TryGetActiveMissionWar(army, kingdom, out War war))
                return 0;

            long emergencyId = war.data.id;
            int available = CityReservePoolService.OpenOrReadWarReserve(
                sourceCity, emergencyId);
            int syntheticRequest = TemporaryLevyRules.SyntheticFallbackRequest(
                ArmyMobilizationPhase.War, requested, available);
            int reserved = CityReservePoolService.TryReserveWarManpower(
                sourceCity, emergencyId, syntheticRequest);
            int synthetic = SyntheticLevyService.CreateBatch(
                sourceCity, kingdom, army, reserved, emergencyId, recruits);
            CityReservePoolService.ReleaseUnmaterializedWarReservation(
                sourceCity, emergencyId, reserved - synthetic);
            confirmedExhausted = synthetic <= 0 &&
                                 CityReservePoolService.OpenOrReadWarReserve(
                                     sourceCity, emergencyId) <= 0;
            return synthetic;
        }

        private static long ResolveMissionWarId(Army army,
            Kingdom kingdom)
        {
            return TryGetActiveMissionWar(army, kingdom, out War war)
                ? war.data.id
                : -1L;
        }

        private static void TeleportSuccessfulRecruits(Army pArmy,
            IReadOnlyList<Actor> pCandidates)
        {
            if (pArmy?.data == null || pCandidates == null) return;
            for (int i = 0; i < pCandidates.Count; i++)
            {
                Actor actor = pCandidates[i];
                bool enlisted;
                try
                {
                    enlisted = actor?.data != null && actor.isWarrior() &&
                               actor.army == pArmy;
                }
                catch { enlisted = false; }
                if (!enlisted) continue;
                if (!ArmyRtsControllerService.TryTeleportReinforcementMember(
                        pArmy.id, actor.data.id,
                        pAllowCaptainCombat: true))
                    ArmyRtsControllerService.TrackReplenishmentArrival(
                        actor, pArmy);
            }
        }

        private static bool TryGetLiveShortage(Army pArmy,
            out int pShortage)
        {
            pShortage = 0;
            if (pArmy?.data == null ||
                !ArmyRtsControllerService.TryGetMission(pArmy,
                    out ArmyRtsMission mission) || mission == null ||
                mission.TargetStrength <= 0) return false;
            int living;
            try { living = Math.Max(0, pArmy.countUnits()); }
            catch { return false; }
            if (ArmyRtsControllerService.TryGetWartimeRecovery(pArmy,
                    out int baseline, out _))
            {
                pShortage = ArmyRtsWarLifecycleRules.RecoveryShortage(
                    living, baseline);
                return true;
            }
            pShortage = Math.Max(0, mission.TargetStrength - living);
            return true;
        }

        private static IReadOnlyList<long> TakeActiveBatch(int pLimit)
        {
            var result = new List<long>(Math.Max(0, pLimit));
            if (pLimit <= 0 || ActiveArmyIds.Count == 0) return result;
            foreach (long armyId in ActiveArmyIds)
            {
                if (armyId <= _cursorAfterArmyId) continue;
                result.Add(armyId);
                if (result.Count >= pLimit) break;
            }
            if (result.Count < pLimit)
            {
                foreach (long armyId in ActiveArmyIds)
                {
                    if (armyId > _cursorAfterArmyId) break;
                    result.Add(armyId);
                    if (result.Count >= pLimit) break;
                }
            }
            if (result.Count > 0)
                _cursorAfterArmyId = result[result.Count - 1];
            return result;
        }

        private static void Persist(Army pArmy,
            ArmyReplenishmentOperationState pState)
        {
            if (pArmy?.data == null || pState == null) return;
            pArmy.data.set(LineageKeys.ARMY_REPLENISHMENT_OPERATION_VERSION,
                ArmyReplenishmentOperationRules.SchemaVersion);
            pArmy.data.set(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_KINGDOM_ID,
                pState.KingdomId);
            pArmy.data.set(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_SOURCE_CITY_ID,
                pState.SourceCityId);
            pArmy.data.set(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_APPROVED_SHORTAGE,
                pState.ApprovedShortage);
            pArmy.data.set(LineageKeys.ARMY_REPLENISHMENT_OPERATION_ENLISTED,
                pState.EnlistedCount);
            pArmy.data.set(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_START_TIME,
                pState.StartTime.ToString("R", CultureInfo.InvariantCulture));
            pArmy.data.set(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_DEADLINE_TIME,
                pState.DeadlineTime.ToString("R",
                    CultureInfo.InvariantCulture));
        }

        private static bool TryParseFinite(string pText, out double pValue)
        {
            return double.TryParse(pText, NumberStyles.Float,
                       CultureInfo.InvariantCulture, out pValue) &&
                   IsFinite(pValue);
        }

        private static bool IsFinite(double pValue)
        {
            return !double.IsNaN(pValue) && !double.IsInfinity(pValue) &&
                   pValue >= 0d;
        }

        private static bool IsLiveOrdinaryArmy(Army pArmy)
        {
            try
            {
                return pArmy?.data != null && pArmy.isAlive() &&
                       CanUseReservePool(pArmy);
            }
            catch { return false; }
        }

        private static bool IsLiveKingdom(Kingdom pKingdom)
        {
            try
            {
                return pKingdom?.data != null && pKingdom.isAlive() &&
                       !pKingdom.isRekt();
            }
            catch { return false; }
        }

        private static bool IsControlledCity(City pCity, Kingdom pKingdom)
        {
            try
            {
                return pCity?.data != null && pCity.isAlive() &&
                       !pCity.isRekt() && pCity.kingdom == pKingdom;
            }
            catch { return false; }
        }

        private static bool TryGetActiveMissionWar(Army pArmy,
            Kingdom pKingdom, out War pWar)
        {
            if (!TryGetMissionWar(pArmy, pKingdom, out pWar)) return false;
            try
            {
                return !pWar.hasEnded() && pWar.hasKingdom(pKingdom);
            }
            catch { return false; }
        }

        private static bool TryGetMissionWar(Army pArmy,
            Kingdom pKingdom, out War pWar)
        {
            pWar = null;
            if (!IsLiveKingdom(pKingdom) || pArmy?.data == null ||
                !ArmyRtsControllerService.TryGetMission(pArmy,
                    out ArmyRtsMission mission) || mission == null ||
                mission.KingdomId != pKingdom.id || mission.WarId < 0L)
                return false;
            try { pWar = World.world?.wars?.get(mission.WarId); }
            catch { pWar = null; }
            return pWar?.data != null;
        }

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return pArmy?.getKingdom(); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static Army FindArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }

        private static double CurrentWorldTime()
        {
            try { return World.world?.getCurWorldTime() ?? 0d; }
            catch { return 0d; }
        }
    }
}
