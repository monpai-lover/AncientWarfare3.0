using System;
using System.Collections.Generic;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyRetreatService
    {
        private const int CaptainMutationBudget = 1;
        private const int MaximumCitiesPerIndexBatch = 8;
        private const int MaximumCandidatesPerSelectionBatch = 8;

        private sealed class RetreatState
        {
            public long TargetCityId = -1L;
            public long KingdomId = -1L;
            public long SourceCityId = -1L;
            public long ExcludedCityId = -1L;
            public int OriginTileId = -1;
            public long ReachabilityCityId = -1L;
            public long BestCityId = -1L;
            public long BestDistance = long.MaxValue;
            public int ObservedIndexGeneration = -1;
            public bool SelectionPending;
            public ArmySafeCityIdCursor CandidateCursor;
            public ArmyRetreatSelectionState Selection =
                new ArmyRetreatSelectionState();
            public readonly ArmyRetreatCandidateFlow CandidateFlow =
                new ArmyRetreatCandidateFlow();
            public readonly ArmyLegacyRetreatMovementFlow LegacyMovement =
                new ArmyLegacyRetreatMovementFlow();
        }

        private sealed class SafeCityIndexState
        {
            public long KingdomId = -1L;
            public int ObservedCityCount = -1;
            public int Cursor;
            public int Generation;
            public bool Complete;
            public bool Queued;
        }

        private static readonly Dictionary<long, RetreatState> RetreatStates =
            new Dictionary<long, RetreatState>();
        private static readonly Dictionary<long, SafeCityIndexState>
            SafeCityStates = new Dictionary<long, SafeCityIndexState>();
        private static readonly ArmySafeCityIndex SafeCityIndex =
            new ArmySafeCityIndex();
        private static readonly ArmyLegacyRetreatIndex LegacyRetreatIndex =
            new ArmyLegacyRetreatIndex();

        public static bool ShouldStopAttack(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt()) return false;
            if (ArmyRtsWarDoctrine.IsLastStand &&
                ArmyRtsControllerService.OwnsLiveActor(pActor))
                return false;
            Army army = pActor.army;
            City sourceCity = pActor.city;
            City targetCity = sourceCity?.target_attack_city ?? sourceCity?.target_attack_zone?.city;
            if (army?.data == null || sourceCity?.data == null || targetCity?.data == null) return false;

            int year = Date.getCurrentYear();
            army.data.get(LineageKeys.AW_ARMY_RETREAT_UNTIL_YEAR, out int retreatUntil, -1);
            if (ArmyRetreatRules.ShouldSkipAttackWhileRetreating(retreatUntil, year))
            {
                City cachedRetreat = ResolveCachedRetreatCity(army);
                if (cachedRetreat?.data != null)
                    ScheduleArmyRetreat(army, cachedRetreat);
                return true;
            }

            string role = AWArmyService.GetRole(army);
            long targetId = targetCity.id;
            ArmyLegacyRetreatPersistenceFlow persistence =
                ReadLegacyRetreatPersistence(army);
            if (persistence.ShouldSuppressTarget(targetId)) return false;
            if (!LegacyRetreatIndex.TryGet(army.id, targetId,
                    out int baselineUnits, out int currentUnits))
            {
                army.data.get(LineageKeys.AW_ARMY_RETREAT_TARGET_CITY_ID,
                    out long storedTargetId, -1L);
                army.data.get(LineageKeys.AW_ARMY_RETREAT_BASELINE,
                    out int storedBaseline, 0);
                if (storedTargetId == targetId && storedBaseline > 0)
                    LegacyRetreatIndex.BeginTarget(army.id, targetId,
                        storedBaseline);
                if (!LegacyRetreatIndex.TryGet(army.id, targetId,
                        out baselineUnits, out currentUnits)) return false;
            }

            Actor captain = SafeCaptain(army);
            bool captainAlive = captain?.data != null && !captain.isRekt();
            if (ShouldProtectOccupation(targetCity, pActor.kingdom)) return false;
            bool shouldRetreat = ArmyRetreatRules.ShouldRetreat(
                role,
                baselineUnits,
                currentUnits,
                captainAlive,
                pIsAttacking: true,
                pCooldownActive: false);
            if (!shouldRetreat) return false;

            BeginRetreat(army, pActor.kingdom, sourceCity, targetCity, year);
            return true;
        }

        private static bool ShouldProtectOccupation(City pTargetCity, Kingdom pAttacker)
        {
            if (pTargetCity?.data == null || pAttacker?.data == null) return false;
            try
            {
                bool activeUnits = pTargetCity.isGettingCapturedBy(pAttacker);
                bool noDefenders = !CityOccupationAccelerationService.HasActiveDefenders(pTargetCity);
                bool ownershipChanged = pTargetCity.kingdom == pAttacker ||
                                        !pAttacker.isEnemy(pTargetCity.kingdom);
                CityOccupationAccelerationService.DescribeCaptureFor(
                    pTargetCity, pAttacker, out bool attackerIsDominant, out bool hostileRivalActive);
                return ArmyRetreatRules.ProtectUncontestedOccupation(
                    attackerIsDominant, activeUnits, noDefenders, hostileRivalActive, ownershipChanged);
            }
            catch { return false; }
        }

        private static void BeginRetreat(Army pArmy, Kingdom pKingdom, City pSourceCity, City pTargetCity, int pYear)
        {
            if (pArmy?.data == null) return;
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_UNTIL_YEAR, pYear + ArmyRetreatRules.RetreatCooldownYears);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_CITY_ID, -1L);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_BASELINE, 0);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_TARGET_CITY_ID, -1L);
            RequestRetreatSelection(pArmy, pKingdom, pSourceCity,
                pTargetCity?.id ?? -1L);
        }

        public static void ClearRuntime()
        {
            RetreatStates.Clear();
            SafeCityStates.Clear();
            SafeCityIndex.Clear();
            LegacyRetreatIndex.Clear();
        }

        public static void OnSafeCityObserved(City pCity,
            Kingdom pKingdom)
        {
            bool safe = pCity?.data != null && pKingdom?.data != null &&
                        !pCity.isRekt() && pCity.kingdom == pKingdom;
            SafeCityIndex.SetCity(pCity?.id ?? -1L,
                pKingdom?.id ?? -1L, safe);
        }

        public static void OnCityControlChanged(City pCity,
            Kingdom pPreviousOwner)
        {
            if (pCity?.data == null) return;
            Kingdom current = pCity.kingdom;
            bool safe = current?.data != null && !pCity.isRekt();
            SafeCityIndex.SetCity(pCity.id, current?.id ?? -1L, safe);
            RefreshObservedCount(pPreviousOwner);
            if (current != pPreviousOwner) RefreshObservedCount(current);
        }

        public static void OnCityDestroyed(City pCity)
        {
            if (pCity?.data == null) return;
            Kingdom previous = pCity.kingdom;
            SafeCityIndex.SetCity(pCity.id, previous?.id ?? -1L,
                safe: false);
            RefreshObservedCount(previous);
        }

        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            SafeCityIndex.RemoveKingdom(pKingdom.id);
            SafeCityStates.Remove(pKingdom.id);
        }

        public static void OnAttackTargetAssigned(Army pArmy,
            City pTargetCity)
        {
            if (pArmy?.data == null || pTargetCity?.data == null) return;
            ArmyLegacyRetreatPersistenceFlow persistence =
                ReadLegacyRetreatPersistence(pArmy);
            long suppressedTarget = persistence.SuppressedTargetCityId;
            if (!persistence.TryBeginTarget(pTargetCity.id)) return;
            if (persistence.SuppressedTargetCityId != suppressedTarget)
                WriteLegacyRetreatPersistence(pArmy, persistence);
            if (LegacyRetreatIndex.TryGet(pArmy.id, pTargetCity.id,
                    out _, out _)) return;
            pArmy.data.get(LineageKeys.AW_ARMY_RETREAT_BASELINE,
                out int storedBaseline, 0);
            if (storedBaseline > 0)
            {
                LegacyRetreatIndex.BeginTarget(pArmy.id, pTargetCity.id,
                    storedBaseline);
                pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_TARGET_CITY_ID,
                    pTargetCity.id);
                return;
            }

            int baseline;
            try { baseline = Math.Max(0, pArmy.countUnits()); }
            catch { return; }
            pArmy.data.get(LineageKeys.AW_RTS_LIFECYCLE_BASELINE,
                out int formalWarBaseline, 0);
            if (formalWarBaseline > 0)
                baseline = formalWarBaseline;
            LegacyRetreatIndex.BeginTarget(pArmy.id, pTargetCity.id,
                baseline);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_BASELINE, baseline);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_TARGET_CITY_ID,
                pTargetCity.id);
        }

        public static void OnActorDying(Actor pActor)
        {
            Army army = pActor?.army;
            if (army?.data == null) return;
            ArmyLegacyRetreatPersistenceFlow persistence =
                ReadLegacyRetreatPersistence(army);
            long suppressedTarget = persistence.SuppressedTargetCityId;
            persistence.RecordCasualty();
            if (persistence.SuppressedTargetCityId != suppressedTarget)
                WriteLegacyRetreatPersistence(army, persistence);
            LegacyRetreatIndex.RecordCasualty(army.id);
        }

        public static void OnArmyDisposed(Army pArmy)
        {
            if (pArmy == null) return;
            LegacyRetreatIndex.Remove(pArmy.id);
            RetreatStates.Remove(pArmy.id);
        }

        public static bool AssignArmyRetreat(Army pArmy)
        {
            return AssignArmyRetreat(pArmy, failedTargetCityId: -1L,
                ArmyRtsWithdrawalOrigin.Watchdog);
        }

        public static bool AssignArmyRetreat(Army pArmy,
            long failedTargetCityId,
            ArmyRtsWithdrawalOrigin pOrigin =
                ArmyRtsWithdrawalOrigin.Watchdog)
        {
            if (pArmy?.data == null) return false;
            bool playerCommand = ArmyRtsControllerService.TryGetMission(
                pArmy, out ArmyRtsMission mission) &&
                ArmyRtsWarDoctrineRules.IsExplicitPlayerRetreat(mission);
            if (!ArmyRtsWarDoctrineRules.AllowWithdrawal(
                    ArmyRtsWarDoctrine.Current, pOrigin, playerCommand))
                return false;
            Kingdom kingdom = SafeKingdom(pArmy, null);
            return RequestRetreatSelection(pArmy, kingdom,
                pSourceCity: null,
                failedTargetCityId: failedTargetCityId);
        }

        private static void ScheduleArmyRetreat(Army pArmy, City pRetreatCity)
        {
            if (pArmy?.data == null || pRetreatCity?.data == null) return;
            if (ArmyRtsRuntimeMode.ShouldCommit)
            {
                if (ArmyRtsControllerService.AssignRetreatMission(
                        pArmy, pRetreatCity))
                    ArmyRtsBenchmark.RecordRetreat();
                else
                {
                    ArmyRtsControllerService.RecoverUnavailableRetreat(
                        pArmy);
                    KingdomWarDirectorService.OnArmyChanged(
                        SafeKingdom(pArmy, null));
                }
                return;
            }
            if (!RetreatStates.TryGetValue(pArmy.id, out RetreatState state))
            {
                state = new RetreatState();
                RetreatStates[pArmy.id] = state;
            }
            if (state.TargetCityId != pRetreatCity.id)
            {
                state.TargetCityId = pRetreatCity.id;
                ArmyRtsBenchmark.RecordRetreat();
            }
            EnqueueRetreatBatch(pArmy.id);
        }

        private static void EnqueueRetreatBatch(long pArmyId)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("army_retreat", pArmyId),
                DeferredWorkClass.Runtime,
                () => ProcessRetreatBatch(pArmyId));
        }

        private static void ProcessRetreatBatch(long pArmyId)
        {
            if (!RetreatStates.TryGetValue(pArmyId, out RetreatState state)) return;
            Army army = ResolveArmy(pArmyId);
            City retreatCity = ResolveCity(state.TargetCityId);
            WorldTile tile = SafeCityTile(retreatCity);
            if (army?.data == null)
            {
                RetreatStates.Remove(pArmyId);
                return;
            }
            if (tile == null)
            {
                RecoverLegacyRetreat(army);
                return;
            }
            Actor captain = SafeCaptain(army);
            if (captain?.current_tile == null)
            {
                HandleLegacyMovementOutcome(army, state,
                    succeeded: false);
                return;
            }
            bool movementSucceeded = false;
            try
            {
                if (CaptainMutationBudget > 0 && tile.isSameIsland(captain.current_tile))
                {
                    captain.goTo(tile, pLimitPathfindingRegions: 6);
                    movementSucceeded = true;
                }
            }
            catch { movementSucceeded = false; }
            HandleLegacyMovementOutcome(army, state, movementSucceeded);
        }

        private static void HandleLegacyMovementOutcome(Army pArmy,
            RetreatState pState, bool succeeded)
        {
            ArmyLegacyRetreatMovementOutcome outcome =
                pState.LegacyMovement.RecordAttempt(succeeded);
            if (outcome == ArmyLegacyRetreatMovementOutcome.Pending)
            {
                EnqueueRetreatBatch(pArmy.id);
                return;
            }
            if (outcome == ArmyLegacyRetreatMovementOutcome.Recover)
            {
                RecoverLegacyRetreat(pArmy);
                return;
            }
            RetreatStates.Remove(pArmy.id);
        }

        private static void RecoverLegacyRetreat(Army pArmy)
        {
            if (pArmy?.data == null) return;
            RetreatStates.Remove(pArmy.id);
            long suppressedTarget =
                LegacyRetreatIndex.RecordRecovery(pArmy.id);
            ArmyLegacyRetreatPersistenceFlow persistence =
                ReadLegacyRetreatPersistence(pArmy);
            persistence.RecordRecovery(suppressedTarget);
            WriteLegacyRetreatPersistence(pArmy, persistence);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_CITY_ID, -1L);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_UNTIL_YEAR, -1);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_BASELINE, 0);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_TARGET_CITY_ID, -1L);
        }

        private static bool RequestRetreatSelection(Army pArmy,
            Kingdom pKingdom, City pSourceCity, long failedTargetCityId)
        {
            Kingdom kingdom = pKingdom?.data != null
                ? pKingdom
                : SafeKingdom(pArmy, pSourceCity);
            if (pArmy?.data == null || kingdom?.data == null) return false;
            if (RetreatStates.TryGetValue(pArmy.id,
                    out RetreatState current) && current.SelectionPending &&
                current.KingdomId == kingdom.id &&
                current.ExcludedCityId == failedTargetCityId) return true;
            var state = new RetreatState
            {
                KingdomId = kingdom.id,
                SourceCityId = pSourceCity?.id ?? -1L,
                ExcludedCityId = failedTargetCityId,
                SelectionPending = true
            };
            RetreatStates[pArmy.id] = state;
            if (ArmyRtsRuntimeMode.ShouldCommit)
                ArmyRtsControllerService.PrepareForRetreatSelection(pArmy);
            EnsureSafeCityIndex(kingdom, out int generation);
            state.ObservedIndexGeneration = generation;
            EnqueueRetreatSelection(pArmy.id);
            return true;
        }

        private static void EnqueueRetreatSelection(long pArmyId)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "army_retreat_select", pArmyId),
                DeferredWorkClass.Runtime,
                () => ProcessRetreatSelectionBatch(pArmyId));
        }

        private static void ProcessRetreatSelectionBatch(long pArmyId)
        {
            if (!RetreatStates.TryGetValue(pArmyId,
                    out RetreatState state) || !state.SelectionPending) return;
            Army army = ResolveArmy(pArmyId);
            Kingdom kingdom = FindKingdom(state.KingdomId);
            if (army?.data == null || kingdom?.data == null)
            {
                RetreatStates.Remove(pArmyId);
                return;
            }
            if (!EnsureSafeCityIndex(kingdom, out int generation))
            {
                if (state.ObservedIndexGeneration != generation)
                {
                    state.ObservedIndexGeneration = generation;
                    if (state.Selection.RecordPending() ==
                        ArmyRetreatSelectionOutcome.Recover)
                    {
                        RecoverUnavailableRetreat(army);
                        return;
                    }
                }
                EnqueueRetreatSelection(pArmyId);
                return;
            }

            if (state.OriginTileId < 0 &&
                !TryResolveSelectionOrigin(army, kingdom, state))
            {
                if (state.Selection.RecordPending() ==
                    ArmyRetreatSelectionOutcome.Pending)
                    EnqueueRetreatSelection(pArmyId);
                else
                    RecoverUnavailableRetreat(army);
                return;
            }
            if (state.CandidateCursor == null ||
                state.CandidateCursor.IsStale)
            {
                if (state.CandidateCursor != null &&
                    state.Selection.RecordPending() ==
                    ArmyRetreatSelectionOutcome.Recover)
                {
                    RecoverUnavailableRetreat(army);
                    return;
                }
                state.CandidateCursor = SafeCityIndex.CreateCursor(
                    state.KingdomId);
                state.BestCityId = -1L;
                state.BestDistance = long.MaxValue;
            }

            IReadOnlyList<long> cityIds = state.CandidateCursor.Take(
                MaximumCandidatesPerSelectionBatch);
            var candidates = new List<ArmySafeCityCandidate>(
                cityIds.Count + (state.BestCityId >= 0L ? 1 : 0));
            City excludedCity = ResolveCity(state.ExcludedCityId);
            WorldTile origin = FindTile(state.OriginTileId);
            City reachabilityCity = ResolveCity(
                state.ReachabilityCityId);
            if (state.BestCityId >= 0L)
                candidates.Add(DescribeCandidate(ResolveCity(state.BestCityId),
                    kingdom, excludedCity, origin, reachabilityCity));
            for (int i = 0; i < cityIds.Count; i++)
            {
                City candidate = ResolveCity(cityIds[i]);
                ArmySafeCityCandidate facts = DescribeCandidate(candidate,
                    kingdom, excludedCity, origin, reachabilityCity);
                candidates.Add(facts);
            }
            ArmyRetreatCandidateFlowResult flowResult =
                state.CandidateFlow.ObserveBatch(candidates,
                    state.CandidateCursor.IsComplete);
            long selected = flowResult.CityId;
            if (selected >= 0L)
            {
                state.BestCityId = selected;
                for (int i = 0; i < candidates.Count; i++)
                    if (candidates[i].CityId == selected)
                    {
                        state.BestDistance = candidates[i].DistanceSquared;
                        break;
                    }
            }
            else
            {
                state.BestCityId = -1L;
                state.BestDistance = long.MaxValue;
            }
            if (flowResult.Outcome == ArmyRetreatSelectionOutcome.Pending)
            {
                EnqueueRetreatSelection(pArmyId);
                return;
            }
            if (flowResult.Outcome == ArmyRetreatSelectionOutcome.Recover)
            {
                RecoverUnavailableRetreat(army);
                return;
            }

            City retreatCity = ResolveCity(state.BestCityId);
            if (retreatCity?.data == null)
            {
                RecoverUnavailableRetreat(army);
                return;
            }
            state.Selection.RecordAssigned();
            state.SelectionPending = false;
            state.TargetCityId = retreatCity.id;
            army.data.set(LineageKeys.AW_ARMY_RETREAT_CITY_ID,
                retreatCity.id);
            ScheduleArmyRetreat(army, retreatCity);
        }

        private static ArmySafeCityCandidate DescribeCandidate(City pCity,
            Kingdom pKingdom, City pExcludedCity, WorldTile pOrigin,
            City pReachabilityOrigin)
        {
            if (!IsValidRetreatCity(pCity, pKingdom))
                return new ArmySafeCityCandidate(-1L, long.MaxValue,
                    friendly: false, underAttack: true,
                    enemyFrozenControlled: true, reachable: false,
                    sameIsland: false);
            WorldTile tile = SafeCityTile(pCity);
            bool underAttack;
            try { underAttack = pCity.isGettingCaptured(); }
            catch { underAttack = true; }
            bool reachable;
            try
            {
                reachable = pReachabilityOrigin?.data != null &&
                    (pCity == pReachabilityOrigin ||
                     pCity.reachableFrom(pReachabilityOrigin));
            }
            catch { reachable = false; }
            bool sameIsland;
            try
            {
                sameIsland = tile != null && pOrigin != null &&
                             tile.isSameIsland(pOrigin);
            }
            catch { sameIsland = false; }
            return new ArmySafeCityCandidate(pCity.id,
                TileDistanceSquared(pOrigin, tile),
                friendly: pCity.kingdom == pKingdom,
                underAttack: underAttack,
                enemyFrozenControlled:
                    WarScoreService.IsCityFrozenControlledByEnemySide(
                        pCity, pKingdom),
                reachable: reachable, sameIsland: sameIsland,
                coolingDown: ArmyStallWatchdogService.IsTargetCoolingDown(
                    pKingdom.id, pCity.id),
                excluded: pCity == pExcludedCity);
        }

        private static bool TryResolveSelectionOrigin(Army pArmy,
            Kingdom pKingdom, RetreatState pState)
        {
            WorldTile captain = SafeCaptain(pArmy)?.current_tile;
            ArmyFormationService.TryGetAnchor(pArmy,
                out WorldTile formationAnchor);
            City sourceCity = ResolveCity(pState.SourceCityId);
            City currentCity = sourceCity?.kingdom == pKingdom
                ? sourceCity
                : SafeAnchorCity(pArmy);
            WorldTile currentCityTile = SafeCityTile(currentCity);
            ArmyRtsControllerService.TryGetRetreatAnchor(pArmy,
                out WorldTile missionAnchor);
            int tileId = ArmyRetreatRules.SelectRetreatOriginTileId(
                TileId(captain), TileId(formationAnchor),
                TileId(currentCityTile), TileId(missionAnchor));
            WorldTile origin = FindTile(tileId);
            if (origin == null) return false;
            City originCity = origin.zone?.city;
            City reachability = currentCity?.kingdom == pKingdom
                ? currentCity
                : originCity?.kingdom == pKingdom
                    ? originCity
                    : pKingdom.capital;
            if (reachability?.data == null) return false;
            pState.OriginTileId = tileId;
            pState.ReachabilityCityId = reachability.id;
            return true;
        }

        private static void RecoverUnavailableRetreat(Army pArmy)
        {
            if (pArmy?.data == null) return;
            RetreatStates.Remove(pArmy.id);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_CITY_ID, -1L);
            if (ArmyRtsRuntimeMode.ShouldCommit)
                ArmyRtsControllerService.RecoverUnavailableRetreat(pArmy);
            KingdomWarDirectorService.OnArmyChanged(
                SafeKingdom(pArmy, null));
        }

        private static bool IsValidRetreatCity(City pCity, Kingdom pKingdom)
        {
            return pCity?.data != null && !pCity.isRekt() &&
                   pCity.kingdom == pKingdom;
        }

        private static bool EnsureSafeCityIndex(Kingdom pKingdom,
            out int pGeneration)
        {
            pGeneration = -1;
            if (pKingdom?.data == null) return false;
            if (!SafeCityStates.TryGetValue(pKingdom.id,
                    out SafeCityIndexState state))
            {
                state = new SafeCityIndexState { KingdomId = pKingdom.id };
                SafeCityStates[pKingdom.id] = state;
                BeginSafeCityIndexScan(pKingdom, state);
            }
            int cityCount;
            try { cityCount = pKingdom.cities.Count; }
            catch { cityCount = 0; }
            if (state.ObservedCityCount != cityCount)
                BeginSafeCityIndexScan(pKingdom, state);
            pGeneration = state.Generation;
            if (!state.Complete) ScheduleSafeCityIndex(state);
            return state.Complete;
        }

        private static void BeginSafeCityIndexScan(Kingdom pKingdom,
            SafeCityIndexState pState)
        {
            if (pKingdom?.data == null || pState == null) return;
            SafeCityIndex.RemoveKingdom(pKingdom.id);
            try { pState.ObservedCityCount = pKingdom.cities.Count; }
            catch { pState.ObservedCityCount = 0; }
            pState.Cursor = 0;
            pState.Complete = false;
            pState.Generation = pState.Generation == int.MaxValue
                ? 1
                : pState.Generation + 1;
            ScheduleSafeCityIndex(pState);
        }

        private static void ScheduleSafeCityIndex(SafeCityIndexState pState)
        {
            if (pState == null || pState.Queued) return;
            pState.Queued = true;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "army_retreat_city_index", pState.KingdomId),
                DeferredWorkClass.Runtime,
                () => ProcessSafeCityIndexBatch(pState.KingdomId));
        }

        private static void ProcessSafeCityIndexBatch(long pKingdomId)
        {
            if (!SafeCityStates.TryGetValue(pKingdomId,
                    out SafeCityIndexState state)) return;
            state.Queued = false;
            Kingdom kingdom = FindKingdom(pKingdomId);
            if (kingdom?.data == null)
            {
                SafeCityIndex.RemoveKingdom(pKingdomId);
                SafeCityStates.Remove(pKingdomId);
                return;
            }
            int cityCount;
            try { cityCount = kingdom.cities.Count; }
            catch { cityCount = 0; }
            if (cityCount != state.ObservedCityCount)
            {
                BeginSafeCityIndexScan(kingdom, state);
                return;
            }
            int start = Math.Max(0, Math.Min(state.Cursor, cityCount));
            int end = Math.Min(cityCount,
                start + MaximumCitiesPerIndexBatch);
            for (int i = start; i < end; i++)
            {
                City city = null;
                try { city = kingdom.cities[i]; }
                catch { }
                OnSafeCityObserved(city, kingdom);
            }
            state.Cursor = end;
            if (end < cityCount)
            {
                ScheduleSafeCityIndex(state);
                return;
            }
            state.Complete = true;
        }

        private static void RefreshObservedCount(Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                !SafeCityStates.TryGetValue(pKingdom.id,
                    out SafeCityIndexState state)) return;
            if (!state.Complete)
            {
                BeginSafeCityIndexScan(pKingdom, state);
                return;
            }
            try { state.ObservedCityCount = pKingdom.cities.Count; }
            catch { state.ObservedCityCount = 0; }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try
            {
                Actor captain = pArmy.getCaptain();
                return captain?.data != null && !captain.isRekt() ? captain : null;
            }
            catch { return null; }
        }

        private static Kingdom SafeKingdom(Army pArmy, City pSourceCity)
        {
            try
            {
                Kingdom kingdom = pArmy?.getKingdom();
                if (kingdom?.data != null) return kingdom;
            }
            catch { }
            return pSourceCity?.kingdom;
        }

        private static WorldTile SafeCityTile(City pCity)
        {
            try { return pCity.getTile(); }
            catch { return null; }
        }

        private static City SafeAnchorCity(Army pArmy)
        {
            try { return AWArmyService.FindAnchorCity(pArmy); }
            catch { return null; }
        }

        private static ArmyLegacyRetreatPersistenceFlow
            ReadLegacyRetreatPersistence(Army pArmy)
        {
            long suppressedTarget = -1L;
            pArmy?.data?.get(
                LineageKeys.AW_ARMY_RETREAT_SUPPRESSED_TARGET_CITY_ID,
                out suppressedTarget, -1L);
            return new ArmyLegacyRetreatPersistenceFlow(suppressedTarget);
        }

        private static void WriteLegacyRetreatPersistence(Army pArmy,
            ArmyLegacyRetreatPersistenceFlow persistence)
        {
            pArmy?.data?.set(
                LineageKeys.AW_ARMY_RETREAT_SUPPRESSED_TARGET_CITY_ID,
                persistence.SuppressedTargetCityId);
        }

        private static City ResolveCachedRetreatCity(Army pArmy)
        {
            if (pArmy?.data == null) return null;
            if (RetreatStates.TryGetValue(pArmy.id,
                    out RetreatState state))
            {
                City cached = ResolveCity(state.TargetCityId);
                if (cached?.data != null) return cached;
            }
            pArmy.data.get(LineageKeys.AW_ARMY_RETREAT_CITY_ID,
                out long cityId, -1L);
            return ResolveCity(cityId);
        }

        private static long TileDistanceSquared(WorldTile pFirst,
            WorldTile pSecond)
        {
            if (pFirst == null || pSecond == null) return long.MaxValue;
            long x = (long)pFirst.x - pSecond.x;
            long y = (long)pFirst.y - pSecond.y;
            return x * x + y * y;
        }

        private static Army ResolveArmy(long pId)
        {
            try { return pId >= 0 ? World.world?.armies?.get(pId) : null; }
            catch { return null; }
        }

        private static City ResolveCity(long pId)
        {
            try { return pId >= 0 ? World.world?.cities?.get(pId) : null; }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pId)
        {
            try { return pId >= 0 ? World.world?.kingdoms?.get(pId) : null; }
            catch { return null; }
        }

        private static WorldTile FindTile(int pId)
        {
            try
            {
                WorldTile[] tiles = World.world?.tiles_list;
                return tiles != null && pId >= 0 && pId < tiles.Length
                    ? tiles[pId]
                    : null;
            }
            catch { return null; }
        }

        private static int TileId(WorldTile pTile)
        {
            return pTile?.data?.tile_id ?? -1;
        }
    }
}
