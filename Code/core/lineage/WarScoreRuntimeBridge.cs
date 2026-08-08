using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Reflection;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.core.lineage
{
    public sealed partial class WarScoreService
    {
        private static readonly object RuntimeGate = new object();
        private static readonly FieldInfo CaptureTicksField =
            AccessTools.Field(typeof(City), "_capture_ticks");
        private static readonly FieldInfo CapturingUnitsField =
            AccessTools.Field(typeof(City), "_capturing_units");
        private const int CaptureCleanupBatchSize = 32;
        private const int MaximumPendingCityOccupations = 1024;
        private const double PendingOccupationRetrySeconds = 1d;
        private static WarScoreService _runtime;
        private static SQLiteConnection _runtimeDatabase;
        private static readonly Dictionary<long, PendingCityOccupation>
            PendingCityOccupations =
                new Dictionary<long, PendingCityOccupation>();

        private sealed class PendingCityOccupation
        {
            public long OccupierKingdomId;
            public long HostileParticipantKingdomId;
            public double NextRetryTime;
        }

        private sealed class WarCaptureCleanupBatch
        {
            public long WarId;
            public IReadOnlyList<WarScoreOccupiedCitySnapshot> Cities;
            public int NextIndex;
        }

        private sealed class ParticipantCityRevaluationBatch
        {
            public long WarId;
            public long HomeKingdomId;
            public string AfterControlKey = "";
        }

        public static bool StartWar(War war)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || war?.data == null || war.hasEnded())
                    return false;
                WarParticipantCityBaselineService.RegisterExistingParticipants(war);
                WarParticipantMobilizationBaselines baselines =
                    WarParticipantMobilizationBaselineService.
                        RegisterExistingParticipants(war);
                Kingdom attacker = SafeMainAttacker(war);
                Kingdom defender = SafeMainDefender(war);
                if (attacker?.data == null) return false;
                string warType;
                try
                {
                    warType = war.getAsset()?.id ??
                              war.data.war_type ?? "";
                }
                catch
                {
                    warType = war.data.war_type ?? "";
                }
                int cityScoreBudget =
                    WarScoreRules.CityScoreBudgetForWarType(warType);
                bool nonNegotiableWar =
                    ZhuluPeaceGuard.BlocksOrdinarySettlement(war) ||
                    RebellionDirectTerritoryTransferService.
                        BlocksOrdinarySettlement(war);
                return runtime.StartWar(war.data.id, attacker.id,
                    defender?.id ?? -1L, CurrentWorldTime(),
                    cityScoreBudget, baselines.Attackers,
                    baselines.Defenders, nonNegotiableWar);
            }
            catch { return false; }
        }

        public static void RegisterParticipantMobilization(War pWar,
            Kingdom pKingdom)
        {
            try
            {
                if (pWar?.data == null || pKingdom?.data == null ||
                    pWar.hasEnded()) return;
                if (!pWar.isAttacker(pKingdom) &&
                    !pWar.isDefender(pKingdom)) return;
                StartWar(pWar);
            }
            catch { }
        }

        public static bool EndWar(War war, WarWinner winner)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || war?.data == null) return false;
                IReadOnlyList<WarScoreOccupiedCitySnapshot> frozen =
                    runtime.ReadAllOccupiedCitiesForWarCleanup(war.data.id);
                bool ended = runtime.EndWar(war.data.id,
                    winner.ToString().ToLowerInvariant(), CurrentWorldTime());
                if (ended) ClearCaptureStateAfterWar(frozen);
                return ended;
            }
            catch { return false; }
        }

        public static bool RecordDeath(War war,
            bool casualtyWasAttacker)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || war?.data == null || war.hasEnded())
                    return false;
                RepairMobilizationBaselinesIfMissing(war, runtime);
                int attackerLosses = war.getDeadAttackers();
                int defenderLosses = war.getDeadDefenders();
                bool changed = runtime.SynchronizeDeaths(war.data.id,
                    attackerLosses, defenderLosses, CurrentWorldTime());
                if (changed)
                    QueueSettlementChecks(war);
                return changed;
            }
            catch { return false; }
        }

        public static bool RecordBattleResult(War pWar,
            WarScoreSide pWinnerSide, int pIntensity)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || pWar?.data == null || pWar.hasEnded())
                    return false;
                bool changed = runtime.RecordBattleResult(pWar.data.id,
                    pWinnerSide,
                    pIntensity, CurrentWorldTime());
                if (changed)
                    QueueSettlementChecks(pWar);
                return changed;
            }
            catch { return false; }
        }

        public static bool RecordBattleVictoryRelief(War pWar,
            string pEpisodeId, WarScoreSide pWinnerSide, int pIntensity)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || pWar?.data == null || pWar.hasEnded())
                    return false;
                bool changed = runtime.RecordBattleVictoryRelief(
                    pWar.data.id, pEpisodeId, pWinnerSide, pIntensity,
                    CurrentWorldTime());
                if (changed) QueueSettlementChecks(pWar);
                return changed;
            }
            catch { return false; }
        }

        public static bool RecordGoalControlChanged(War war, string goalId,
            Kingdom beneficiary, bool controlled, int value,
            bool matchesActiveWarGoal)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || war?.data == null ||
                    beneficiary?.data == null || war.hasEnded()) return false;
                WarScoreSide side = SideOf(war, beneficiary);
                bool changed = WarScoreRules.IsParticipantSide(side) &&
                               runtime.RecordGoalControlChanged(war.data.id,
                                   goalId, side, controlled, value,
                                   matchesActiveWarGoal,
                                   CurrentWorldTime());
                if (changed)
                    QueueSettlementChecks(war);
                return changed;
            }
            catch { return false; }
        }

        public static bool CalibrateYear(War war, int year)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || war?.data == null || war.hasEnded())
                    return false;
                int duration;
                try { duration = war.getDuration(); }
                catch { duration = 0; }
                WarParticipantMobilizationBaselines baselines =
                    WarParticipantMobilizationBaselineService.
                        RegisterExistingParticipants(war);
                bool nonNegotiableWar =
                    ZhuluPeaceGuard.BlocksOrdinarySettlement(war) ||
                    RebellionDirectTerritoryTransferService.
                        BlocksOrdinarySettlement(war);
                bool changed = runtime.CalibrateYear(war.data.id, duration,
                    year, baselines.Attackers, baselines.Defenders,
                    CurrentWorldTime(), nonNegotiableWar);
                if (changed)
                {
                    ScheduleActiveParticipantControlRevaluation(war);
                    QueueSettlementChecks(war);
                }
                return changed;
            }
            catch { return false; }
        }

        public static bool TryFreezeCityOccupation(City city,
            Kingdom occupier)
        {
            try { return TryFreezeCityOccupationCore(city, occupier); }
            catch { return false; }
        }

        public static bool HasActiveHostileWar(City pCity,
            Kingdom pOccupier)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null ||
                pOccupier?.data == null ||
                pCity.kingdom.id == pOccupier.id) return false;
            IReadOnlyList<War> wars = SnapshotWars(pOccupier);
            for (int i = 0; i < wars.Count; i++)
                if (IsSharedActiveWar(wars[i], pCity.kingdom, pOccupier))
                    return true;
            return false;
        }

        public static bool HoldPendingCityOccupation(City pCity,
            Kingdom pOccupier, Kingdom pHostileParticipant)
        {
            if (pCity?.data == null || pOccupier?.data == null) return false;
            if (!PendingCityOccupations.TryGetValue(pCity.id,
                    out PendingCityOccupation pending))
            {
                if (PendingCityOccupations.Count >=
                    MaximumPendingCityOccupations)
                    RemoveOldestPendingCityOccupation();
                pending = new PendingCityOccupation();
                PendingCityOccupations[pCity.id] = pending;
            }
            pending.OccupierKingdomId = pOccupier.id;
            pending.HostileParticipantKingdomId =
                pHostileParticipant?.id ?? pOccupier.id;
            pending.NextRetryTime = CurrentWorldTime() +
                                    PendingOccupationRetrySeconds;
            HoldCityAtCaptureLimit(pCity, pOccupier);
            return true;
        }

        public static bool RetryPendingCityOccupation(City pCity)
        {
            if (pCity?.data == null ||
                !PendingCityOccupations.TryGetValue(pCity.id,
                    out PendingCityOccupation pending)) return false;
            Kingdom occupier = FindKingdom(pending.OccupierKingdomId);
            Kingdom hostileParticipant = FindKingdom(
                pending.HostileParticipantKingdomId);
            if (occupier?.data == null ||
                !HasActiveHostileWar(pCity, occupier) &&
                !HasActiveHostileWar(pCity, hostileParticipant))
            {
                PendingCityOccupations.Remove(pCity.id);
                ResetCaptureState(pCity);
                return false;
            }
            HoldCityAtCaptureLimit(pCity, occupier);
            double now = CurrentWorldTime();
            if (now < pending.NextRetryTime) return true;
            pending.NextRetryTime = now + PendingOccupationRetrySeconds;
            if (!TryFreezeCityOccupation(pCity, occupier)) return true;
            PendingCityOccupations.Remove(pCity.id);
            return true;
        }

        public static void ClearPendingCityOccupations()
        {
            PendingCityOccupations.Clear();
        }

        private static bool TryFreezeCityOccupationCore(City pCity,
            Kingdom pOccupier)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null ||
                pOccupier?.data == null) return false;
            pOccupier = VassalCaptureService.ResolveCaptureRecipient(
                pCity, pOccupier);
            if (pOccupier?.data == null) return false;
            WarScoreService runtime = GetRuntime();
            WarScoreControlState existingState = null;
            bool existingFrozenControl = runtime != null &&
                TryResolveFrozenCityControl(runtime, pCity,
                    out existingState);
            long existingControllerId = existingFrozenControl
                ? existingState.ControllerKingdomId
                : -1L;
            long legalOwnerId = pCity.kingdom.id;
            if (existingFrozenControl && pOccupier.id == legalOwnerId)
                return false;
            if (!ArmyRtsObjectiveRules.CanReplaceFrozenOccupation(
                    existingFrozenControl, existingControllerId,
                    pOccupier.id, legalOwnerId))
            {
                Kingdom lockedController = FindKingdom(existingControllerId);
                if (lockedController?.data == null) return false;
                SetCaptureProgress(pCity, 100f);
                pCity.last_visual_capture_ticks = 100;
                pCity.being_captured_by = lockedController;
                return true;
            }
            bool frozen = WarRemainingTerritoryOrchestration.
                ApplyToEverySharedActiveWar(
                    SnapshotWars(pOccupier),
                    war => war?.data?.id ?? -1L,
                    war => IsSharedActiveWar(war, pCity.kingdom, pOccupier),
                    war => TryFreezeCityOccupationForWar(pCity, pOccupier,
                        war));
            if (!frozen) return false;
            SetCaptureProgress(pCity, 100f);
            pCity.last_visual_capture_ticks = 100;
            pCity.being_captured_by = pOccupier;
            CityAttackZoneService.OnCityFrozenControlled(pCity, pOccupier);
            OccupiedCitySupplyService.OnFrozenControlChanged(pCity);
            return true;
        }

        private static void HoldCityAtCaptureLimit(City pCity,
            Kingdom pOccupier)
        {
            SetCaptureProgress(pCity, 100f);
            pCity.last_visual_capture_ticks = 100;
            pCity.being_captured_by = pOccupier;
        }

        private static void RemoveOldestPendingCityOccupation()
        {
            long oldestCityId = -1L;
            double oldestRetry = double.MaxValue;
            foreach (KeyValuePair<long, PendingCityOccupation> pair in
                     PendingCityOccupations)
            {
                if (pair.Value.NextRetryTime >= oldestRetry) continue;
                oldestRetry = pair.Value.NextRetryTime;
                oldestCityId = pair.Key;
            }
            if (oldestCityId >= 0L)
                PendingCityOccupations.Remove(oldestCityId);
        }

        private static bool TryFreezeCityOccupationForWar(City pCity,
            Kingdom pOccupier, War pWar)
        {
            if (pWar?.data == null || pWar.hasEnded()) return false;
            WarScoreSide homeSide = SideOf(pWar, pCity.kingdom);
            WarScoreSide controllerSide = SideOf(pWar, pOccupier);
            if (!WarScoreRules.IsParticipantSide(homeSide) ||
                !WarScoreRules.IsParticipantSide(controllerSide) ||
                homeSide == controllerSide) return false;

            WarScoreService runtime = GetRuntime();
            if (runtime == null) return false;
            StartWar(pWar);
            if (!runtime.TryGetSnapshot(pWar.data.id, WarScoreSide.Attackers,
                    out WarScoreSnapshot active) || !active.Active)
                return false;
            WarScoreCityFacts facts = BuildCityFacts(pCity, pOccupier, pWar);
            bool changed = runtime.RecordCityControlChanged(pWar.data.id,
                facts, homeSide,
                controllerSide, pCity.kingdom.id, pOccupier.id,
                CurrentWorldTime());
            if (!changed && !runtime.TryReadFrozenOccupation(pWar.data.id,
                    pCity.id, out _)) return false;
            if (facts.MatchesActiveWarGoal)
                runtime.RecordGoalControlChanged(pWar.data.id,
                    "city:" + pCity.id, controllerSide, pControlled: true,
                    pValue: 15, pMatchesActiveWarGoal: true,
                    CurrentWorldTime());
            WarGoalSettlementRuntimeService.OnCityControlChanged(pWar,
                pCity, pOccupier);
            WarScoreDecisiveSettlementService.QueueIfDecisive(pWar);
            return true;
        }

        public static bool ShouldHoldFrozenOccupation(City city)
        {
            try { return ShouldHoldFrozenOccupationCore(city); }
            catch { return false; }
        }

        private static bool ShouldHoldFrozenOccupationCore(City pCity)
        {
            WarScoreService runtime = GetRuntime();
            if (runtime == null || pCity?.data == null ||
                !TryResolveFrozenCityControl(runtime, pCity,
                    out WarScoreControlState state)) return false;
            if (pCity.kingdom?.data == null ||
                (state.HomeKingdomId >= 0 &&
                 pCity.kingdom.id != state.HomeKingdomId))
            {
                ClearGoalControlForCity(runtime, state, pCity.id,
                    CurrentWorldTime());
                return false;
            }

            War war = FindWar(state.WarId);
            if (war?.data == null || war.hasEnded())
            {
                ClearGoalControlForCity(runtime, state, pCity.id,
                    CurrentWorldTime());
                ResetCaptureState(pCity);
                return false;
            }

            Kingdom controller = FindKingdom(state.ControllerKingdomId);
            bool controllerAlive = false;
            try
            {
                controllerAlive = controller?.data != null &&
                                  controller.isAlive() &&
                                  !controller.isRekt();
            }
            catch { }
            WarScoreSide controllerSide = SideOf(war, controller);
            bool controllerStillParticipant =
                WarScoreRules.IsParticipantSide(controllerSide) &&
                controllerSide == state.ControllerSide;
            if (!controllerAlive || !controllerStillParticipant)
            {
                ClearGoalControlForCity(runtime, state, pCity.id,
                    CurrentWorldTime());
                ResetCaptureState(pCity);
                return false;
            }

            ClearConflictingFrozenCityControls(runtime, pCity, state);
            Kingdom dominant = ResolveDominantPresence(pCity);
            if (dominant?.data != null && dominant == pCity.kingdom)
                return false;
            SetCaptureProgress(pCity, 100f);
            pCity.last_visual_capture_ticks = 100;
            pCity.being_captured_by = controller;
            return true;
        }

        public static void OnCaptureProgressCleared(City city)
        {
            try
            {
                if (city?.data == null) return;
                WarScoreService runtime = GetRuntime();
                if (runtime == null) return;
                IReadOnlyList<WarScoreControlState> states =
                    ReadFrozenCityControls(runtime, city);
                double time = CurrentWorldTime();
                for (int i = 0; i < states.Count; i++)
                    ClearGoalControlForCity(runtime, states[i], city.id,
                        time);
            }
            catch { }
        }

        public static void ClearDepartedParticipantControls(War pWar,
            Kingdom pKingdom)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || pWar?.data == null ||
                    pKingdom?.data == null) return;
                ScheduleDepartedParticipantCleanup(pWar.data.id,
                    pKingdom.id);
            }
            catch { }
        }

        internal static bool ClearSeparatePeaceParticipantControls(
            long pWarId, HashSet<long> pExitKingdomIds,
            out string pReason)
        {
            pReason = "";
            if (pWarId < 0 || pExitKingdomIds == null ||
                pExitKingdomIds.Count == 0)
            {
                pReason = "separate_peace_exit_group_invalid";
                return false;
            }
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null)
                {
                    pReason = "war_score_unavailable";
                    return false;
                }
                IReadOnlyList<WarScoreOccupiedCitySnapshot> frozen =
                    runtime.ReadAllOccupiedCitiesForWarCleanup(pWarId);
                for (int i = 0; i < frozen.Count; i++)
                {
                    WarScoreOccupiedCitySnapshot snapshot = frozen[i];
                    if (snapshot == null ||
                        !pExitKingdomIds.Contains(
                            snapshot.ControllerKingdomId) &&
                        !pExitKingdomIds.Contains(snapshot.HomeKingdomId))
                        continue;
                    ClearFrozenSnapshot(runtime, snapshot);
                }

                IReadOnlyList<WarScoreOccupiedCitySnapshot> remaining =
                    runtime.ReadAllOccupiedCitiesForWarCleanup(pWarId);
                for (int i = 0; i < remaining.Count; i++)
                {
                    WarScoreOccupiedCitySnapshot snapshot = remaining[i];
                    if (snapshot != null &&
                        (pExitKingdomIds.Contains(
                             snapshot.ControllerKingdomId) ||
                         pExitKingdomIds.Contains(snapshot.HomeKingdomId)))
                    {
                        pReason = "separate_peace_occupation_cleanup_pending";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception error)
            {
                pReason = "separate_peace_occupation_cleanup_exception:" +
                          error.GetType().Name;
                return false;
            }
        }

        private static void ScheduleDepartedParticipantCleanup(long pWarId,
            long pKingdomId)
        {
            string key = "war_score_departed_cleanup:" + pWarId + ":" +
                         pKingdomId;
            DeferredRuntimeWorkService.EnqueueCoalesced(key,
                DeferredWorkClass.Persistent,
                () => ProcessDepartedParticipantCleanup(pWarId,
                    pKingdomId));
        }

        private static void ProcessDepartedParticipantCleanup(long pWarId,
            long pKingdomId)
        {
            WarScoreService runtime = GetRuntime();
            if (runtime == null) return;
            IReadOnlyList<WarScoreOccupiedCitySnapshot> frozen =
                runtime.ReadOccupiedCities(pWarId, pKingdomId,
                    CaptureCleanupBatchSize);
            for (int i = 0; i < frozen.Count; i++)
                ClearFrozenSnapshot(runtime, frozen[i]);
            if (frozen.Count == CaptureCleanupBatchSize)
                ScheduleDepartedParticipantCleanup(pWarId, pKingdomId);
        }

        internal static void ScheduleParticipantCityControlRevaluation(
            War pWar, Kingdom pHomeKingdom)
        {
            if (pWar?.data == null || pHomeKingdom?.data == null) return;
            ScheduleParticipantCityControlRevaluation(
                new ParticipantCityRevaluationBatch
                {
                    WarId = pWar.data.id,
                    HomeKingdomId = pHomeKingdom.id
                });
        }

        private static void ScheduleActiveParticipantControlRevaluation(
            War pWar)
        {
            if (pWar?.data == null) return;
            var seen = new HashSet<long>();
            try
            {
                foreach (Kingdom kingdom in pWar.getAttackers())
                    ScheduleActiveParticipantControlRevaluation(pWar,
                        kingdom, seen);
                foreach (Kingdom kingdom in pWar.getDefenders())
                    ScheduleActiveParticipantControlRevaluation(pWar,
                        kingdom, seen);
            }
            catch { }
        }

        private static void ScheduleActiveParticipantControlRevaluation(
            War pWar, Kingdom pKingdom, HashSet<long> pSeen)
        {
            if (pKingdom?.data == null || pSeen == null ||
                !pSeen.Add(pKingdom.id)) return;
            WarParticipantCityBaselineService.RegisterParticipant(pWar,
                pKingdom);
            ScheduleParticipantCityControlRevaluation(pWar, pKingdom);
        }

        private static void ScheduleParticipantCityControlRevaluation(
            ParticipantCityRevaluationBatch pBatch)
        {
            string key = "war_score_city_revaluation:" + pBatch.WarId +
                         ":" + pBatch.HomeKingdomId;
            DeferredRuntimeWorkService.EnqueueCoalesced(key,
                DeferredWorkClass.Persistent,
                () => ProcessParticipantCityControlRevaluation(pBatch));
        }

        private static void ProcessParticipantCityControlRevaluation(
            ParticipantCityRevaluationBatch pBatch)
        {
            WarScoreService runtime = GetRuntime();
            War war = FindWar(pBatch.WarId);
            if (runtime == null || war?.data == null || war.hasEnded()) return;
            StartWar(war);
            IReadOnlyList<WarScoreOccupiedCitySnapshot> page =
                runtime.ReadOccupiedCitiesByHomeKingdom(pBatch.WarId,
                    pBatch.HomeKingdomId, pBatch.AfterControlKey,
                    WarRemainingTerritoryOrchestration.RevaluationPageSize);
            RemainingTerritoryRevaluationPage result =
                WarRemainingTerritoryOrchestration.ProcessRevaluationPage(
                    page,
                    snapshot => snapshot.ControlKey,
                    ControlHomeStillOwnsCity,
                    snapshot => ClearRevaluedCityControl(runtime, snapshot),
                    snapshot => RevalueCityControl(runtime, war, snapshot));
            bool changed = result.RemovedCount > 0 ||
                           result.RevaluedCount > 0;
            if (changed)
                QueueSettlementChecks(war);
            if (result.HasMore)
            {
                pBatch.AfterControlKey = result.NextControlKey;
                ScheduleParticipantCityControlRevaluation(pBatch);
            }
        }

        private static bool ControlHomeStillOwnsCity(
            WarScoreOccupiedCitySnapshot pSnapshot)
        {
            City city = FindCity(pSnapshot.CityId);
            return city?.data != null && city.kingdom?.data != null &&
                   city.kingdom.id == pSnapshot.HomeKingdomId;
        }

        private static bool RevalueCityControl(WarScoreService pRuntime,
            War pWar, WarScoreOccupiedCitySnapshot pSnapshot)
        {
            City city = FindCity(pSnapshot.CityId);
            Kingdom controller = FindKingdom(pSnapshot.ControllerKingdomId);
            WarScoreSide homeSide = SideOf(pWar, city?.kingdom);
            WarScoreSide controllerSide = SideOf(pWar, controller);
            if (city?.data == null ||
                !WarScoreRules.IsParticipantSide(homeSide) ||
                !WarScoreRules.IsParticipantSide(controllerSide) ||
                homeSide == controllerSide)
                return ClearRevaluedCityControl(pRuntime, pSnapshot);
            WarScoreCityFacts facts = BuildCityFacts(city, controller, pWar);
            return pRuntime.RecordCityControlChanged(pSnapshot.WarId,
                facts, homeSide, controllerSide, city.kingdom.id,
                controller.id, CurrentWorldTime());
        }

        private static bool ClearRevaluedCityControl(
            WarScoreService pRuntime,
            WarScoreOccupiedCitySnapshot pSnapshot)
        {
            double time = CurrentWorldTime();
            if (pSnapshot.MatchesActiveWarGoal)
                pRuntime.RecordGoalControlChanged(pSnapshot.WarId,
                    "city:" + pSnapshot.CityId,
                    pSnapshot.ControllerSide, pControlled: false,
                    pValue: 15, pMatchesActiveWarGoal: true, time);
            bool changed = pRuntime.ClearCityControl(pSnapshot.WarId,
                pSnapshot.CityId, time);
            if (changed) NotifyFrozenControlChanged(pSnapshot.CityId);
            return changed;
        }

        private static void ClearGoalControlForCity(
            WarScoreService pRuntime, WarScoreControlState pState,
            long pCityId, double pWorldTime)
        {
            if (pRuntime == null || pState == null) return;
            if (pState.VerifiedGoal)
                pRuntime.RecordGoalControlChanged(pState.WarId,
                    "city:" + pCityId, pState.ControllerSide,
                    pControlled: false, pValue: 15,
                    pMatchesActiveWarGoal: true, pWorldTime);
            bool changed = pRuntime.ClearCityControl(pState.WarId, pCityId,
                pWorldTime);
            if (changed) NotifyFrozenControlChanged(pCityId);
            QueueDecisiveAfterControlClear(pState.WarId);
        }

        private static void ClearFrozenSnapshot(WarScoreService pRuntime,
            WarScoreOccupiedCitySnapshot pSnapshot)
        {
            if (pRuntime == null || pSnapshot == null) return;
            double time = CurrentWorldTime();
            if (pSnapshot.MatchesActiveWarGoal)
                pRuntime.RecordGoalControlChanged(pSnapshot.WarId,
                    "city:" + pSnapshot.CityId,
                    pSnapshot.ControllerSide, pControlled: false,
                    pValue: 15, pMatchesActiveWarGoal: true, time);
            bool changed = pRuntime.ClearCityControl(pSnapshot.WarId,
                pSnapshot.CityId, time);
            QueueDecisiveAfterControlClear(pSnapshot.WarId);
            City city = null;
            try { city = World.world?.cities?.get(pSnapshot.CityId); }
            catch { }
            ResetCaptureState(city);
            if (changed)
                OccupiedCitySupplyService.OnFrozenControlChanged(city);
        }

        private static void NotifyFrozenControlChanged(long pCityId)
        {
            City city = null;
            try { city = World.world?.cities?.get(pCityId); }
            catch { }
            OccupiedCitySupplyService.OnFrozenControlChanged(city);
        }

        private static void QueueDecisiveAfterControlClear(long pWarId)
        {
            War war = FindWar(pWarId);
            if (war?.data != null && !war.hasEnded())
                QueueSettlementChecks(war);
        }

        private static void ClearCaptureStateAfterWar(
            IReadOnlyList<WarScoreOccupiedCitySnapshot> pFrozen)
        {
            if (pFrozen == null || pFrozen.Count == 0) return;
            var batch = new WarCaptureCleanupBatch
            {
                WarId = pFrozen[0].WarId,
                Cities = pFrozen,
                NextIndex = 0
            };
            ScheduleWarCaptureCleanup(batch);
        }

        private static void ScheduleWarCaptureCleanup(
            WarCaptureCleanupBatch pBatch)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "war_score_capture_cleanup:" + pBatch.WarId,
                DeferredWorkClass.Persistent,
                () => ProcessWarCaptureCleanup(pBatch));
        }

        private static void ProcessWarCaptureCleanup(
            WarCaptureCleanupBatch pBatch)
        {
            int end = Math.Min(pBatch.Cities.Count,
                pBatch.NextIndex + CaptureCleanupBatchSize);
            while (pBatch.NextIndex < end)
            {
                WarScoreOccupiedCitySnapshot snapshot =
                    pBatch.Cities[pBatch.NextIndex++];
                City city = null;
                try { city = World.world?.cities?.get(snapshot.CityId); }
                catch { }
                ResetCaptureState(city);
                OccupiedCitySupplyService.OnFrozenControlChanged(city);
            }
            if (pBatch.NextIndex < pBatch.Cities.Count)
                ScheduleWarCaptureCleanup(pBatch);
        }

        private static void ResetCaptureState(City pCity)
        {
            if (pCity?.data == null) return;
            SetCaptureProgress(pCity, 0f);
            pCity.last_visual_capture_ticks = 0;
            pCity.being_captured_by = null;
        }

        public static bool TryGetSnapshot(War war, Kingdom viewer,
            out WarScoreSnapshot snapshot)
        {
            snapshot = null;
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || war?.data == null || viewer?.data == null)
                    return false;
                RepairMobilizationBaselinesIfMissing(war, runtime);
                WarScoreSide side = SideOf(war, viewer);
                return WarScoreRules.IsParticipantSide(side) &&
                       runtime.TryGetSnapshot(war.data.id, side, out snapshot);
            }
            catch { return false; }
        }

        private static void RepairMobilizationBaselinesIfMissing(War war,
            WarScoreService runtime)
        {
            if (war?.data == null || runtime == null || war.hasEnded()) return;
            if (!runtime.TryGetSnapshot(war.data.id, WarScoreSide.Attackers,
                    out WarScoreSnapshot snapshot) ||
                snapshot.AttackerMobilizationBaseline <= 0 ||
                snapshot.DefenderMobilizationBaseline <= 0)
                StartWar(war);
        }

        public static IReadOnlyList<WarScoreOccupiedCitySnapshot>
            ReadOccupiedCities(War pWar, Kingdom pController,
                int pLimit = 64)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || pWar?.data == null ||
                    pController?.data == null)
                    return Array.Empty<WarScoreOccupiedCitySnapshot>();
                return runtime.ReadOccupiedCities(pWar.data.id, pController.id,
                    Math.Max(1, Math.Min(64, pLimit)));
            }
            catch
            {
                return Array.Empty<WarScoreOccupiedCitySnapshot>();
            }
        }

        public static IReadOnlyList<City> ReadOccupiedCityObjects(War pWar,
            Kingdom pController, int pLimit = 64)
        {
            IReadOnlyList<WarScoreOccupiedCitySnapshot> snapshots =
                ReadOccupiedCities(pWar, pController, pLimit);
            var result = new List<City>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                City city = null;
                try { city = World.world?.cities?.get(snapshots[i].CityId); }
                catch { }
                if (city?.data != null) result.Add(city);
            }
            return result;
        }

        public static IReadOnlyList<WarScoreOccupiedCitySnapshot>
            ReadOccupiedCitySnapshots(War pWar, Kingdom pController,
                int pLimit = 64)
        {
            WarScoreService runtime = GetRuntime();
            if (runtime == null || pWar?.data == null ||
                pController?.data == null)
                return Array.Empty<WarScoreOccupiedCitySnapshot>();
            return ReadOccupiedCities(pWar, pController, pLimit);
        }

        public static bool TryGetFrozenOccupation(long warId, long cityId,
            out long occupierKingdomId)
        {
            occupierKingdomId = -1;
            try
            {
                WarScoreService runtime = GetRuntime();
                return runtime != null && runtime.TryReadFrozenOccupation(
                    warId, cityId, out occupierKingdomId);
            }
            catch { return false; }
        }

        public static IReadOnlyList<WarScoreOccupiedCitySnapshot>
            ReadFrozenOccupationsForHomeKingdom(long pWarId,
                long pHomeKingdomId, int pLimit = 64)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || pWarId < 0 ||
                    pHomeKingdomId < 0)
                    return Array.Empty<WarScoreOccupiedCitySnapshot>();
                return runtime.ReadOccupiedCitiesByHomeKingdom(pWarId,
                    pHomeKingdomId, "", Math.Max(1,
                        Math.Min(64, pLimit)));
            }
            catch
            {
                return Array.Empty<WarScoreOccupiedCitySnapshot>();
            }
        }

        public static bool IsCityFrozenControlledBySide(City pCity,
            Kingdom pController)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || pCity?.data == null ||
                    pController?.data == null) return false;
                IReadOnlyList<WarScoreControlState> states =
                    ReadFrozenCityControls(runtime, pCity);
                for (int i = 0; i < states.Count; i++)
                {
                    WarScoreControlState state = states[i];
                    War war = FindWar(state.WarId);
                    if (war?.data != null && !war.hasEnded() &&
                        state.ControllerKingdomId == pController.id &&
                        SideOf(war, pController) == state.ControllerSide)
                        return true;
                }
                return false;
            }
            catch { return false; }
        }

        public static bool IsCityFrozenOccupationLockedAgainst(City pCity,
            Kingdom pKingdom)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || pCity?.data == null ||
                    pCity.kingdom?.data == null ||
                    pKingdom?.data == null) return false;
                if (!TryResolveFrozenCityControl(runtime, pCity,
                        out WarScoreControlState state)) return false;
                return !ArmyRtsObjectiveRules.CanReplaceFrozenOccupation(
                    existingFrozenControl: true,
                    existingControllerKingdomId:
                        state.ControllerKingdomId,
                    incomingControllerKingdomId: pKingdom.id,
                    legalOwnerKingdomId: pCity.kingdom.id);
            }
            catch { return false; }
        }

        public static bool IsFriendlySideRecaptureNeeded(long pWarId,
            City pCity, Kingdom pKingdom)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || pCity?.data == null ||
                    pKingdom?.data == null ||
                    !runtime.TryGetFrozenCityControl(pWarId, pCity.id,
                        out WarScoreControlState state)) return false;
                War war = FindWar(state.WarId);
                if (war?.data == null || war.hasEnded()) return false;
                WarScoreSide side = SideOf(war, pKingdom);
                bool participant = WarScoreRules.IsParticipantSide(side);
                return GarrisonSortieRules.IsFriendlyRecaptureNeeded(
                    hasFrozenControl: participant,
                    homeOnKingdomSide: state.HomeSide == side,
                    controllerOnKingdomSide: state.ControllerSide == side);
            }
            catch { return false; }
        }

        public static bool IsCityFrozenControlledByEnemySide(City pCity,
            Kingdom pKingdom)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || pCity?.data == null ||
                    pKingdom?.data == null) return false;
                IReadOnlyList<WarScoreControlState> states =
                    ReadFrozenCityControls(runtime, pCity);
                for (int i = 0; i < states.Count; i++)
                {
                    WarScoreControlState state = states[i];
                    War war = FindWar(state.WarId);
                    if (war?.data == null || war.hasEnded()) continue;
                    WarScoreSide side = SideOf(war, pKingdom);
                    if (side != WarScoreSide.None &&
                        side != state.ControllerSide) return true;
                }
                return false;
            }
            catch { return true; }
        }

        public static IReadOnlyList<WarScoreSnapshot> ReadHistory(
            Kingdom pViewer, int pLimit = 64)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                if (runtime == null || pViewer?.data == null)
                    return Array.Empty<WarScoreSnapshot>();
                return runtime.ReadHistory(pViewer.id,
                    Math.Max(1, Math.Min(64, pLimit)));
            }
            catch
            {
                return Array.Empty<WarScoreSnapshot>();
            }
        }

        public static bool ApplyReserveExhaustion(long pWarId,
            WarScoreSide pSide, double pWorldTime)
        {
            try
            {
                WarScoreService runtime = GetRuntime();
                return runtime != null && runtime.TryApplyReserveExhaustion(
                    pWarId, pSide, pWorldTime);
            }
            catch { return false; }
        }

        private static WarScoreService GetRuntime()
        {
            try
            {
                LineageArchiveManager manager = LineageArchiveManager.Instance;
                SQLiteConnection database = manager?.OperatingDB;
                if (manager == null || !manager.InitializeSuccessful ||
                    database == null) return null;
                if (_runtime != null &&
                    ReferenceEquals(_runtimeDatabase, database))
                    return _runtime;
                lock (RuntimeGate)
                {
                    if (_runtime != null &&
                        ReferenceEquals(_runtimeDatabase, database))
                        return _runtime;
                    _runtime = new WarScoreService(database);
                    _runtimeDatabase = database;
                    return _runtime;
                }
            }
            catch
            {
                return null;
            }
        }

        private static WarScoreCityFacts BuildCityFacts(City pCity,
            Kingdom pController, War pWar)
        {
            float development = 0f;
            int population = 0;
            int zones = 0;
            int buildings = 0;
            bool capital = false;
            bool activeGoal = false;
            int initialOwnerCityCount =
                WarParticipantCityBaselineService.GetOrRegister(pWar, pCity.kingdom);
            try { development = DevelopmentMapModeService.GetCityScore(pCity); }
            catch { }
            try { population = pCity.getPopulationPeople(); }
            catch { }
            try { zones = pCity.countZones(); }
            catch { }
            try { buildings = pCity.countBuildings(); }
            catch { }
            try { capital = pCity.isCapitalCity(); }
            catch { }
            try { activeGoal = HasMatchingActiveCityGoal(pWar, pCity,
                pController); }
            catch { }
            bool onlyLiveCity = initialOwnerCityCount == 1;
            return new WarScoreCityFacts(pCity.id, development, population,
                zones, buildings, capital, activeGoal, onlyLiveCity,
                initialOwnerCityCount);
        }

        private static bool HasMatchingActiveCityGoal(War pWar, City pCity,
            Kingdom pController)
        {
            if (pWar?.data == null || pCity?.data == null ||
                SideOf(pWar, pController) != WarScoreSide.Attackers)
                return false;
            SQLiteConnection database =
                LineageArchiveManager.Instance?.OperatingDB;
            if (database == null) return false;
            using var command = new SQLiteCommand(database);
            command.CommandText = "SELECT GOAL_TYPE FROM " +
                WarGoalTableItem.GetTableName() +
                " WHERE WAR_ID=@war AND RESOLVED=0 AND " +
                "(TARGET_CITY_ID=@dataCity OR TARGET_CITY_ID=@city) " +
                "LIMIT 8";
            command.Parameters.AddWithValue("@war", pWar.data.id);
            command.Parameters.AddWithValue("@dataCity", pCity.data.id);
            command.Parameters.AddWithValue("@city", pCity.id);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string goalType = reader.IsDBNull(0) ? "" :
                    reader.GetString(0);
                if (WarGoalControlRules.ShouldResolveControlledCityGoal(
                        goalType, true)) return true;
            }
            return false;
        }

        private static bool TryResolveFrozenCityControl(
            WarScoreService pRuntime, City pCity,
            out WarScoreControlState pState)
        {
            pState = null;
            IReadOnlyList<WarScoreControlState> states =
                ReadFrozenCityControls(pRuntime, pCity);
            for (int i = 0; i < states.Count; i++)
            {
                WarScoreControlState state = states[i];
                if (pCity?.kingdom?.data == null ||
                    state.HomeKingdomId != pCity.kingdom.id)
                    continue;
                War war = FindWar(state.WarId);
                if (war?.data == null || war.hasEnded()) continue;
                Kingdom controller = FindKingdom(state.ControllerKingdomId);
                bool controllerAlive;
                try
                {
                    controllerAlive = controller?.data != null &&
                                      controller.isAlive() &&
                                      !controller.isRekt();
                }
                catch { controllerAlive = false; }
                if (!controllerAlive ||
                    SideOf(war, controller) != state.ControllerSide)
                    continue;
                if (pState == null || state.StartedTime < pState.StartedTime ||
                    (state.StartedTime == pState.StartedTime &&
                     state.WarId < pState.WarId))
                    pState = state;
            }
            return pState != null;
        }

        private static void ClearConflictingFrozenCityControls(
            WarScoreService pRuntime, City pCity,
            WarScoreControlState pCanonical)
        {
            if (pRuntime == null || pCity?.data == null ||
                pCanonical == null) return;
            IReadOnlyList<WarScoreControlState> states =
                ReadFrozenCityControls(pRuntime, pCity);
            double time = CurrentWorldTime();
            for (int i = 0; i < states.Count; i++)
            {
                WarScoreControlState state = states[i];
                if (state == null || state.WarId == pCanonical.WarId ||
                    state.ControllerKingdomId ==
                    pCanonical.ControllerKingdomId) continue;
                ClearGoalControlForCity(pRuntime, state, pCity.id, time);
            }
        }

        private static IReadOnlyList<WarScoreControlState>
            ReadFrozenCityControls(WarScoreService pRuntime, City pCity)
        {
            if (pRuntime == null || pCity?.data == null)
                return Array.Empty<WarScoreControlState>();
            IReadOnlyList<long> warIds =
                pRuntime.ReadFrozenCityControlWarIds(pCity.id);
            var result = new List<WarScoreControlState>(warIds.Count);
            for (int i = 0; i < warIds.Count; i++)
                if (pRuntime.TryGetFrozenCityControl(warIds[i], pCity.id,
                        out WarScoreControlState state))
                    result.Add(state);
            return result;
        }

        private static IReadOnlyList<War> SnapshotWars(Kingdom pKingdom)
        {
            var result = new List<War>();
            if (pKingdom?.data == null) return result;
            try
            {
                foreach (War war in pKingdom.getWars()) result.Add(war);
            }
            catch { }
            return result;
        }

        private static bool IsSharedActiveWar(War pWar, Kingdom pHome,
            Kingdom pController)
        {
            try
            {
                return pWar?.data != null && !pWar.hasEnded() &&
                       pWar.isInWarWith(pHome, pController);
            }
            catch { return false; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static War FindWar(long pWarId)
        {
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static WarScoreSide SideOf(War pWar, Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom?.data == null)
                return WarScoreSide.None;
            try
            {
                if (pWar.isAttacker(pKingdom)) return WarScoreSide.Attackers;
                if (pWar.isDefender(pKingdom)) return WarScoreSide.Defenders;
            }
            catch { }
            return WarScoreSide.None;
        }

        private static void QueueSettlementChecks(War pWar)
        {
            if (WarForceEliminationSettlementService.QueueIfReady(pWar)) return;
            WarScoreDecisiveSettlementService.QueueIfDecisive(pWar);
            WarGoalSettlementRuntimeService.QueueIfReady(pWar);
            WarExhaustionSettlementRuntimeService.QueueIfReady(pWar);
            RebellionCollapseSettlementService.QueueIfCollapsed(pWar);
        }

        private static Kingdom ResolveDominantPresence(City pCity)
        {
            try
            {
                var capturing = CapturingUnitsField?.GetValue(pCity) as
                    IDictionary<Kingdom, int>;
                Kingdom best = null;
                int bestCount = 0;
                if (capturing != null)
                    foreach (KeyValuePair<Kingdom, int> pair in capturing)
                    {
                        if (pair.Key?.data == null || pair.Value <= bestCount)
                            continue;
                        best = pair.Key;
                        bestCount = pair.Value;
                    }
                return best;
            }
            catch
            {
                return null;
            }
        }

        private static float ReadCaptureProgress(City pCity)
        {
            try
            {
                return CaptureTicksField == null
                    ? 0f
                    : Convert.ToSingle(CaptureTicksField.GetValue(pCity));
            }
            catch { return 0f; }
        }

        private static void SetCaptureProgress(City pCity, float pProgress)
        {
            try
            {
                CaptureTicksField?.SetValue(pCity,
                    Math.Max(0f, Math.Min(100f, pProgress)));
            }
            catch { }
        }

        private static Kingdom SafeMainAttacker(War pWar)
        {
            try { return pWar.getMainAttacker(); }
            catch { return null; }
        }

        private static Kingdom SafeMainDefender(War pWar)
        {
            try { return pWar.getMainDefender(); }
            catch { return null; }
        }

        private static double CurrentWorldTime()
        {
            try { return World.world?.getCurWorldTime() ?? 0d; }
            catch { return 0d; }
        }
    }
}
