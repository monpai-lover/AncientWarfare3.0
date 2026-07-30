using System;
using System.Collections.Generic;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.lineage
{
    internal sealed class KingdomWarDirectorShadowMission
    {
        internal long ArmyId = -1L;
        internal long WarId = -1L;
        internal long FrontId = -1L;
        internal long TargetCityId = -1L;
        internal long LegacyTargetCityId = -1L;
        internal ArmyRtsRole Role;
        internal ArmyRtsPosture Posture;
        internal ArmyRtsProposalKind ProposalKind;
        internal int OpenObjectiveCount;
        internal int FriendlyForce;
        internal int EnemyForce;
        internal int RequiredRatioBasisPoints;
        internal bool ForceReady;
        internal bool SurvivalException;
        internal bool ConnectedSupply;
        internal bool ConnectedCorridor;
    }

    internal sealed class KingdomWarDirectorShadowSnapshot
    {
        internal long KingdomId = -1L;
        internal long WorldDay;
        internal int Generation;
        internal bool WouldCommit;
        internal IReadOnlyList<KingdomWarDirectorShadowMission> Missions =
            Array.Empty<KingdomWarDirectorShadowMission>();
    }

    internal static class KingdomWarDirectorService
    {
        private enum PlanningStage
        {
            Wars,
            Armies,
            Fronts,
            Publish
        }

        private sealed class WarPlanWork
        {
            internal War War;
            internal WarAllocationFacts Allocation;
            internal long FormalGoalCityId = -1L;
            internal FrontScanWork Front;
            internal int OpenObjectiveCount;
            internal bool AggressiveRuler;
            internal bool CautiousRuler;
            internal bool ExpansionPhase;
            internal bool CorruptPhase;
            internal bool Fatigued;
        }

        private sealed class PlanningWork : IDisposable
        {
            internal long KingdomId;
            internal int Generation;
            internal PlanningStage Stage;
            internal long LastWarId = -1L;
            internal bool WarScanComplete;
            internal readonly List<WarPlanWork> SelectedWars =
                new List<WarPlanWork>(KingdomWarDirectorWorkRules.
                    MaximumSelectedWarPlans);
            internal ArmyStrategicIdCursor ArmyCursor;
            internal readonly List<ArmyStrategicFacts> Armies =
                new List<ArmyStrategicFacts>();
            internal IReadOnlyList<WarArmyAssignment> Assignments =
                Array.Empty<WarArmyAssignment>();
            internal int FrontIndex;
            internal readonly Dictionary<long, WarPlanWork> PlanByWarId =
                new Dictionary<long, WarPlanWork>();
            internal readonly Dictionary<long, int> WarriorCountByCity =
                new Dictionary<long, int>();
            internal readonly Dictionary<FrontTargetReservationKey, bool>
                CorridorByTarget =
                    new Dictionary<FrontTargetReservationKey, bool>();
            internal readonly Dictionary<FrontTargetReservationKey,
                ArmyRtsObjectiveState> ObjectiveStateByTarget =
                    new Dictionary<FrontTargetReservationKey,
                        ArmyRtsObjectiveState>();

            public void Dispose()
            {
                for (int i = 0; i < SelectedWars.Count; i++)
                    SelectedWars[i].Front?.Dispose();
            }
        }

        private sealed class FrontScanWork : IDisposable
        {
            private readonly Kingdom _kingdom;
            private readonly War _war;
            private readonly long _formalGoalCityId;
            private IEnumerator<Kingdom> _opponentEnumerator;
            private readonly HashSet<long> _seenCityIds = new HashSet<long>();
            private readonly List<FrontTargetFacts> _targets =
                new List<FrontTargetFacts>();
            private int _friendlyCityIndex;
            private Kingdom _currentOpponent;
            private int _opponentCityIndex;
            private bool _opponentsComplete;

            internal FrontScanWork(Kingdom pKingdom, War pWar,
                long pFormalGoalCityId)
            {
                _kingdom = pKingdom;
                _war = pWar;
                _formalGoalCityId = pFormalGoalCityId;
                _opponentEnumerator = CreateOpponentEnumerator(pWar,
                    pKingdom);
                _opponentsComplete = _opponentEnumerator == null;
            }

            internal FrontTargetFacts BestTarget { get; private set; }
            internal IReadOnlyList<FrontTargetFacts> Targets => _targets;

            internal bool CaptureNext()
            {
                int remainingCities = KingdomWarDirectorWorkRules.
                    MaximumFrontCitiesPerWorkItem;
                int remainingParticipants = KingdomWarDirectorWorkRules.
                    MaximumFrontParticipantsPerWorkItem;
                while (remainingCities > 0)
                {
                    City city;
                    bool friendly;
                    if (_kingdom?.cities != null &&
                        _friendlyCityIndex < _kingdom.cities.Count)
                    {
                        city = _kingdom.cities[_friendlyCityIndex++];
                        friendly = true;
                    }
                    else if (_currentOpponent?.cities != null &&
                             _opponentCityIndex <
                             _currentOpponent.cities.Count)
                    {
                        city = _currentOpponent.cities[
                            _opponentCityIndex++];
                        friendly = false;
                    }
                    else
                    {
                        _currentOpponent = null;
                        _opponentCityIndex = 0;
                        if (_opponentsComplete) break;
                        if (remainingParticipants <= 0) return false;
                        remainingParticipants--;
                        if (!MoveNextOpponent()) break;
                        continue;
                    }

                    remainingCities--;
                    FrontTargetFacts candidate = BuildTargetFacts(city,
                        friendly);
                    if (candidate == null) continue;
                    _targets.Add(candidate);
                    IReadOnlyList<FrontTargetFacts> retained =
                        KingdomWarDirectorRules.RetainFrontTargets(_targets,
                            KingdomWarDirectorWorkRules.
                                MaximumFrontCitiesPerWorkItem);
                    _targets.Clear();
                    for (int i = 0; i < retained.Count; i++)
                        _targets.Add(retained[i]);
                    int selected = KingdomWarDirectorRules.
                        SelectBestTargetIndex(_targets);
                    BestTarget = selected >= 0 ? _targets[selected] : null;
                }
                return IsComplete;
            }

            private bool IsComplete
            {
                get
                {
                    bool friendlyComplete = _kingdom?.cities == null ||
                        _friendlyCityIndex >= _kingdom.cities.Count;
                    bool currentComplete = _currentOpponent?.cities == null ||
                        _opponentCityIndex >= _currentOpponent.cities.Count;
                    return friendlyComplete && currentComplete &&
                           _opponentsComplete;
                }
            }

            public void Dispose()
            {
                _opponentEnumerator?.Dispose();
                _opponentEnumerator = null;
            }

            private bool MoveNextOpponent()
            {
                if (_opponentEnumerator == null)
                {
                    _opponentsComplete = true;
                    return false;
                }
                bool hasNext;
                try { hasNext = _opponentEnumerator.MoveNext(); }
                catch { hasNext = false; }
                if (hasNext)
                {
                    Kingdom opponent = _opponentEnumerator.Current;
                    _currentOpponent = IsLiveKingdom(opponent)
                        ? opponent
                        : null;
                    return true;
                }
                _opponentsComplete = true;
                Dispose();
                return false;
            }

            private FrontTargetFacts BuildTargetFacts(City pCity,
                bool pFriendly)
            {
                if (!IsLiveCity(pCity) || !_seenCityIds.Add(pCity.id))
                    return null;
                if (pFriendly)
                    ArmyRetreatService.OnSafeCityObserved(pCity, _kingdom);
                if (ArmyStallWatchdogService.IsTargetCoolingDown(
                        _kingdom.id, pCity.id)) return null;
                bool frozenFriendly = pFriendly &&
                    IsFrozenControlledByEnemy(_war, pCity, _kingdom);
                bool activelyCapturedFriendly = pFriendly &&
                    IsActivelyCapturedByEnemy(_war, pCity, _kingdom);
                bool friendlyDefenseTarget = pFriendly &&
                    KingdomWarDirectorRules.ShouldAdmitFriendlyDefenseTarget(
                        frozenFriendly, activelyCapturedFriendly);
                ArmyRtsObjectiveState objectiveState =
                    ArmyRtsObjectiveService.Classify(_war, _kingdom,
                        pCity);
                if (!KingdomWarDirectorRules.ShouldAdmitFrontTarget(
                        objectiveState)) return null;
                bool defensiveObjective = objectiveState ==
                                          ArmyRtsObjectiveState.OpenDefense;

                bool formalGoal = MatchesCityId(pCity,
                    _formalGoalCityId);
                bool enemyCapital = !pFriendly &&
                                    pCity.kingdom?.capital == pCity;
                bool connected = friendlyDefenseTarget ||
                                 defensiveObjective ||
                                 IsConnectedCorridor(_war, pCity,
                                     _kingdom);
                ResolveStrategicReachability(_kingdom, pCity,
                    out bool sameIslandLand,
                    out bool transportReachable);
                bool landReachable = connected || sameIslandLand;
                if (landReachable) transportReachable = false;
                ArmyLogisticsService.OnFrontCityObserved(_kingdom.id,
                    _war.data.id, pCity.id, connected);
                int warriorCount = SafeWarriorCount(pCity);
                bool exposed = !formalGoal && !enemyCapital &&
                               warriorCount <= 4;
                int distance = DistanceSquared(_kingdom?.capital, pCity);
                int targetX = int.MinValue;
                int targetY = int.MinValue;
                try
                {
                    WorldTile tile = pCity.getTile();
                    if (tile?.data != null)
                    {
                        targetX = tile.x;
                        targetY = tile.y;
                    }
                }
                catch { }
                return new FrontTargetFacts(pCity.id, friendlyDefenseTarget,
                    formalGoal, enemyCapital, connected, landReachable,
                    transportReachable, exposed, distance,
                    warriorCount, targetX, targetY,
                    defensiveObjective);
            }
        }

        private readonly struct FrontTargetReservationKey :
            IEquatable<FrontTargetReservationKey>
        {
            internal FrontTargetReservationKey(long pWarId,
                long pTargetCityId)
            {
                WarId = pWarId;
                TargetCityId = pTargetCityId;
            }

            private long WarId { get; }
            private long TargetCityId { get; }

            public bool Equals(FrontTargetReservationKey pOther)
            {
                return WarId == pOther.WarId &&
                       TargetCityId == pOther.TargetCityId;
            }

            public override bool Equals(object pObject)
            {
                return pObject is FrontTargetReservationKey other &&
                       Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)WarId * 397) ^ (int)TargetCityId;
                }
            }
        }

        private static readonly KingdomWarDirectorWorkQueue WorkQueue =
            new KingdomWarDirectorWorkQueue();
        private static readonly Dictionary<long, PlanningWork> WorkByKingdom =
            new Dictionary<long, PlanningWork>();
        private static readonly Dictionary<long, int> GenerationByKingdom =
            new Dictionary<long, int>();
        private static readonly Dictionary<long,
            KingdomWarDirectorShadowSnapshot> ShadowByKingdom =
                new Dictionary<long, KingdomWarDirectorShadowSnapshot>();
        private static readonly Dictionary<long, SortedSet<long>>
            WarIdsByKingdom = new Dictionary<long, SortedSet<long>>();
        private static readonly Dictionary<long, HashSet<long>>
            ParticipantIdsByWar = new Dictionary<long, HashSet<long>>();
        private static readonly Dictionary<long, long>
            ActiveThreatCapturerByCity = new Dictionary<long, long>();

        public static void Schedule(Kingdom pKingdom)
        {
            if (!IsLiveKingdom(pKingdom)) return;
            int generation = GenerationByKingdom.TryGetValue(pKingdom.id,
                out int previous) && previous < int.MaxValue
                ? previous + 1
                : 1;
            GenerationByKingdom[pKingdom.id] = generation;
            ArmyRtsAsyncPlanningService.InvalidateKingdom(pKingdom.id);
            RemoveWork(pKingdom.id);
            WorkQueue.MarkDirty(pKingdom.id);
        }

        public static void OnWarStarted(War pWar)
        {
            if (!IsActiveWar(pWar)) return;
            CityMilitaryThreatFacts.InvalidateWar(pWar);
            ArmyRtsAsyncPlanningService.InvalidateWar(pWar.data.id);
            IReadOnlyList<long> participants = RegisterWar(pWar);
            ScheduleParticipants(participants);
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            CityMilitaryThreatFacts.InvalidateWar(pWar);
            ArmyRtsAsyncPlanningService.InvalidateWar(pWar.data.id);
            long warId = pWar.data.id;
            var participants = new HashSet<long>();
            if (ParticipantIdsByWar.TryGetValue(warId,
                    out HashSet<long> indexed))
                participants.UnionWith(indexed);
            AddParticipantIds(pWar, participants);
            ArmyRtsControllerService.InvalidateWar(warId);
            RemoveWar(warId);
            ScheduleParticipants(new List<long>(participants));
        }

        public static void OnWarParticipantChanged(War pWar,
            Kingdom pKingdom)
        {
            if (pWar?.data == null) return;
            CityMilitaryThreatFacts.InvalidateWar(pWar);
            ArmyRtsAsyncPlanningService.InvalidateWar(pWar.data.id);
            var affected = new HashSet<long>();
            if (ParticipantIdsByWar.TryGetValue(pWar.data.id,
                    out HashSet<long> previous))
                affected.UnionWith(previous);
            if (IsActiveWar(pWar))
                affected.UnionWith(RegisterWar(pWar));
            else
                RemoveWar(pWar.data.id);
            if (pKingdom?.data != null) affected.Add(pKingdom.id);
            ScheduleParticipants(new List<long>(affected));
        }

        public static void OnCityControlChanged(City pCity,
            Kingdom pController, Kingdom pPreviousOwner = null)
        {
            if (pCity?.data == null) return;
            CityMilitaryThreatFacts.InvalidateCity(pCity);
            ArmyRtsAsyncPlanningService.InvalidateCity(pCity.id);
            ActiveThreatCapturerByCity.Remove(pCity.id);
            CoalitionWarTaskService.OnTargetInvalidated(pCity);
            ArmyLogisticsService.OnCityControlChanged(pCity);
            ArmyRetreatService.OnCityControlChanged(pCity,
                pPreviousOwner);
            Schedule(pCity.kingdom);
            Schedule(pController);
            Schedule(pPreviousOwner);
        }

        public static void OnCityThreatStateObserved(City pCity)
        {
            if (pCity?.data == null) return;
            long currentCapturerId = ActiveEnemyCapturerId(pCity);
            long previousCapturerId = ActiveThreatCapturerByCity.
                TryGetValue(pCity.id, out long previous)
                ? previous
                : -1L;
            if (!KingdomWarDirectorRules.
                    ShouldPublishFriendlyThreatTransition(
                        previousCapturerId, currentCapturerId)) return;
            if (currentCapturerId >= 0L)
                ActiveThreatCapturerByCity[pCity.id] = currentCapturerId;
            else
                ActiveThreatCapturerByCity.Remove(pCity.id);
            Schedule(pCity.kingdom);
            ScheduleFrozenOccupationController(pCity);
        }

        private static void ScheduleFrozenOccupationController(City pCity)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null ||
                !WarIdsByKingdom.TryGetValue(pCity.kingdom.id,
                    out SortedSet<long> warIds)) return;
            foreach (long warId in warIds)
            {
                if (!WarScoreService.TryGetFrozenOccupation(warId,
                        pCity.id, out long controllerId)) continue;
                Schedule(FindKingdom(controllerId));
            }
        }

        public static void OnArmyChanged(Kingdom pKingdom)
        {
            Schedule(pKingdom);
        }

        public static void QueueArmyChanged(Kingdom pKingdom)
        {
            if (!IsLiveKingdom(pKingdom)) return;
            long kingdomId = pKingdom.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "rts_army_roster_changed", kingdomId),
                DeferredWorkClass.CriticalRuntime,
                () =>
                {
                    Kingdom kingdom = FindKingdom(kingdomId);
                    if (IsLiveKingdom(kingdom)) OnArmyChanged(kingdom);
                });
        }

        public static void ProcessFrame()
        {
            ArmyRtsMode mode = ArmyRtsRuntimeMode.Current;
            if (!ArmyRtsRuntimeModeRules.ShouldPlan(mode)) return;
            long worldDay = CurrentWorldDay();
            if (!WorkQueue.TryTake(worldDay, out long kingdomId)) return;
            Kingdom kingdom = FindKingdom(kingdomId);
            if (!IsLiveKingdom(kingdom))
            {
                RemoveWork(kingdomId);
                ShadowByKingdom.Remove(kingdomId);
                return;
            }

            if (!WorkByKingdom.TryGetValue(kingdomId,
                    out PlanningWork work))
            {
                GenerationByKingdom.TryGetValue(kingdomId,
                    out int generation);
                work = new PlanningWork
                {
                    KingdomId = kingdomId,
                    Generation = generation,
                    Stage = PlanningStage.Wars
                };
                WorkByKingdom[kingdomId] = work;
            }
            else if (GenerationByKingdom.TryGetValue(kingdomId,
                         out int generation) && generation != work.Generation)
            {
                RemoveWork(kingdomId);
                WorkQueue.MarkDirty(kingdomId);
                return;
            }

            bool complete = AdvanceOneWorkItem(kingdom, work, mode, worldDay);
            if (!complete)
            {
                WorkQueue.MarkDirty(kingdomId);
                return;
            }
            RemoveWork(kingdomId);
            WorkQueue.SchedulePeriodic(kingdomId, worldDay);
        }

        public static bool TryGetShadowSnapshot(Kingdom pKingdom,
            out KingdomWarDirectorShadowSnapshot pSnapshot)
        {
            pSnapshot = null;
            return pKingdom?.data != null &&
                   ShadowByKingdom.TryGetValue(pKingdom.id, out pSnapshot);
        }

        internal static bool IsCurrentGeneration(Kingdom pKingdom,
            int pGeneration)
        {
            return pKingdom?.data != null &&
                   GenerationByKingdom.TryGetValue(pKingdom.id,
                       out int current) && current == pGeneration;
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.wars == null) return;
            foreach (War war in World.world.wars)
            {
                if (!IsActiveWar(war)) continue;
                IReadOnlyList<long> participants = RegisterWar(war);
                ScheduleParticipants(participants);
            }
        }

        public static void ClearRuntime()
        {
            WorkQueue.Clear();
            foreach (PlanningWork work in WorkByKingdom.Values)
                work.Dispose();
            WorkByKingdom.Clear();
            GenerationByKingdom.Clear();
            ShadowByKingdom.Clear();
            WarIdsByKingdom.Clear();
            ParticipantIdsByWar.Clear();
            ActiveThreatCapturerByCity.Clear();
            ArmyRtsAsyncPlanningService.ClearRuntime();
        }

        private static bool AdvanceOneWorkItem(Kingdom pKingdom,
            PlanningWork pWork, ArmyRtsMode pMode, long pWorldDay)
        {
            switch (pWork.Stage)
            {
                case PlanningStage.Wars:
                    CaptureWarBatch(pKingdom, pWork);
                    return false;
                case PlanningStage.Armies:
                    CaptureArmyBatch(pKingdom, pWork);
                    return false;
                case PlanningStage.Fronts:
                    CaptureFrontBatch(pKingdom, pWork);
                    return false;
                case PlanningStage.Publish:
                    PublishShadow(pKingdom, pWork, pMode, pWorldDay);
                    return true;
                default:
                    return true;
            }
        }

        private static void CaptureWarBatch(Kingdom pKingdom,
            PlanningWork pWork)
        {
            var warIds = new List<long>(KingdomWarDirectorWorkRules.
                MaximumWarPlansPerWorkItem);
            TryTakeWarIdBatch(pWork.KingdomId, pWork.LastWarId, warIds,
                out long lastWarId, out bool complete);
            pWork.LastWarId = lastWarId;
            pWork.WarScanComplete = complete;

            var batchPlans = new List<WarPlanWork>(warIds.Count);
            for (int i = 0; i < warIds.Count; i++)
            {
                War war = FindWar(warIds[i]);
                WarPlanWork plan = BuildWarPlan(pKingdom, war);
                if (plan != null) batchPlans.Add(plan);
            }

            var retainedFacts = new List<WarAllocationFacts>(
                pWork.SelectedWars.Count);
            var batchFacts = new List<WarAllocationFacts>(batchPlans.Count);
            var candidates = new List<WarPlanWork>(
                pWork.SelectedWars.Count + batchPlans.Count);
            for (int i = 0; i < pWork.SelectedWars.Count; i++)
            {
                retainedFacts.Add(pWork.SelectedWars[i].Allocation);
                candidates.Add(pWork.SelectedWars[i]);
            }
            for (int i = 0; i < batchPlans.Count; i++)
            {
                batchFacts.Add(batchPlans[i].Allocation);
                candidates.Add(batchPlans[i]);
            }
            IReadOnlyList<WarAllocationFacts> selected =
                KingdomWarDirectorRules.MergeTopWarPlans(retainedFacts,
                    batchFacts,
                    KingdomWarDirectorWorkRules.MaximumSelectedWarPlans);
            pWork.SelectedWars.Clear();
            pWork.PlanByWarId.Clear();
            for (int i = 0; i < selected.Count; i++)
            {
                WarPlanWork plan = FindSelectedPlan(candidates,
                    selected[i].WarId);
                if (plan == null) continue;
                pWork.SelectedWars.Add(plan);
                pWork.PlanByWarId[plan.Allocation.WarId] = plan;
            }
            if (!pWork.WarScanComplete) return;
            pWork.ArmyCursor = ArmyStrategicIndexService.
                CreateSnapshotCursor(pKingdom);
            pWork.Stage = PlanningStage.Armies;
        }

        private static void CaptureArmyBatch(Kingdom pKingdom,
            PlanningWork pWork)
        {
            ArmyStrategicSnapshotBatch batch =
                ArmyStrategicSnapshotService.CaptureNext(pKingdom,
                    pWork.ArmyCursor);
            for (int i = 0; i < batch.Armies.Count; i++)
            {
                ArmyStrategicFacts army = batch.Armies[i];
                if (KingdomWarDirectorRules.
                        ShouldRequestMissingCaptainRecovery(
                            army?.UnitCount ?? 0,
                            army?.CaptainAlive ?? false,
                            army?.RoyalGuard ?? false,
                            army?.DedicatedGarrison ?? false))
                {
                    if (TryRecoverMissingCaptain(pKingdom, army))
                    {
                        OnArmyChanged(pKingdom);
                        return;
                    }
                    continue;
                }
                if (!(army?.SpecialArmy ?? false) &&
                    KingdomWarDirectorRules.
                        ShouldRequestDepletedArmyRecovery(
                            army?.UnitCount ?? 0,
                            army?.CaptainAlive ?? false,
                            army?.RoyalGuard ?? false,
                            army?.DedicatedGarrison ?? false,
                            canCommit: ArmyRtsRuntimeModeRules.ShouldCommit(
                                ArmyRtsRuntimeMode.Current)))
                    RequestDepletedArmyRecovery(pKingdom, army);
                if (IsEligibleFieldArmy(army))
                    pWork.Armies.Add(army);
            }
            if (!batch.Complete) return;

            if (TemporaryLevyRules.ShouldRequestZeroArmyRecovery(
                    MilitaryEmergencyService.HasAny(pKingdom),
                    pWork.Armies.Count,
                    TemporaryLevyService.HasPendingOffensiveRecovery(
                        pKingdom)))
                TemporaryLevyService.RequestOffensiveRecovery(pKingdom,
                    pKingdom.capital,
                    TemporaryLevyRules.MaxRecruitsPerWorkItem,
                    pForceEstablishment: true);

            var warFacts = new List<WarAllocationFacts>(
                pWork.SelectedWars.Count);
            for (int i = 0; i < pWork.SelectedWars.Count; i++)
                warFacts.Add(pWork.SelectedWars[i].Allocation);
            var armyFacts = new List<ArmyAllocationFacts>(
                pWork.Armies.Count);
            for (int i = 0; i < pWork.Armies.Count; i++)
            {
                ArmyStrategicFacts army = pWork.Armies[i];
                ArmyOperationalDirectorProjection operational =
                    ArmyOperationalDirectorRules.Project(
                        new ArmyOperationalDirectorFacts(army.UnitCount,
                            army.Supply, army.Organization));
                armyFacts.Add(new ArmyAllocationFacts(army.ArmyId,
                    operational.EffectiveForce));
            }
            pWork.Assignments = KingdomWarDirectorRules.AllocateWars(
                warFacts, armyFacts);
            pWork.Stage = pWork.SelectedWars.Count == 0
                ? PlanningStage.Publish
                : PlanningStage.Fronts;
        }

        private static void CaptureFrontBatch(Kingdom pKingdom,
            PlanningWork pWork)
        {
            if (pWork.FrontIndex >= pWork.SelectedWars.Count)
            {
                pWork.Stage = PlanningStage.Publish;
                return;
            }
            WarPlanWork plan = pWork.SelectedWars[pWork.FrontIndex];
            if (plan.Front == null)
                plan.Front = new FrontScanWork(pKingdom, plan.War,
                    plan.FormalGoalCityId);
            if (!plan.Front.CaptureNext()) return;
            pWork.FrontIndex++;
            if (pWork.FrontIndex >= pWork.SelectedWars.Count)
                pWork.Stage = PlanningStage.Publish;
        }

        private static void PublishShadow(Kingdom pKingdom,
            PlanningWork pWork, ArmyRtsMode pMode, long pWorldDay)
        {
            IReadOnlyList<KingdomWarDirectorShadowMission> missions =
                BuildShadowMissions(pKingdom, pWork,
                    ArmyRtsRuntimeModeRules.ShouldCommit(pMode));
            ScheduleFrontPrefetches(pKingdom, pWork);
            RecordPlannerBenchmark(missions);
            var snapshot = new KingdomWarDirectorShadowSnapshot
            {
                KingdomId = pKingdom.id,
                WorldDay = pWorldDay,
                Generation = pWork.Generation,
                WouldCommit = ArmyRtsRuntimeModeRules.ShouldCommit(pMode),
                Missions = missions
            };
            ShadowByKingdom[pKingdom.id] = snapshot;
            ArmyRtsControllerService.ApplyDirectorSnapshot(pKingdom,
                snapshot);
        }

        private static void ScheduleFrontPrefetches(Kingdom pKingdom,
            PlanningWork pWork)
        {
            if (!IsLiveKingdom(pKingdom) || pWork == null) return;
            for (int index = 0; index < pWork.SelectedWars.Count; index++)
            {
                WarPlanWork plan = pWork.SelectedWars[index];
                if (plan?.War?.data == null) continue;
                ArmyRtsAsyncPlanningService.Schedule(
                    CreateAsyncPlanStamp(pKingdom, pWork, plan.War),
                    plan.Front?.Targets);
            }
        }

        private static ArmyRtsAsyncPlanStamp CreateAsyncPlanStamp(
            Kingdom pKingdom, PlanningWork pWork, War pWar)
        {
            return new ArmyRtsAsyncPlanStamp(AWAsyncRuntime.WorldGeneration,
                pKingdom?.data == null ? -1L : pKingdom.id,
                pWork?.Generation ?? -1,
                pWar?.data == null ? -1L : pWar.data.id,
                CityMilitaryThreatFacts.Revision);
        }

        private static void RecordPlannerBenchmark(
            IReadOnlyList<KingdomWarDirectorShadowMission> pMissions)
        {
            int comparisons = 0;
            int agreements = 0;
            int duplicates = 0;
            var reservedArmyIds = new HashSet<long>();
            for (var index = 0; index < pMissions.Count; index++)
            {
                KingdomWarDirectorShadowMission mission = pMissions[index];
                if (mission == null) continue;
                if (!reservedArmyIds.Add(mission.ArmyId)) duplicates++;
                if (mission.LegacyTargetCityId < 0L ||
                    mission.TargetCityId < 0L) continue;
                comparisons++;
                if (mission.LegacyTargetCityId == mission.TargetCityId)
                    agreements++;
            }
            ArmyRtsBenchmark.RecordPlannerPass(pMissions.Count,
                comparisons, agreements, duplicates);
        }

        private static IReadOnlyList<KingdomWarDirectorShadowMission>
            BuildShadowMissions(Kingdom pKingdom, PlanningWork pWork,
                bool pCommit)
        {
            RefreshPublishCaches(pKingdom, pWork);
            var result = new List<KingdomWarDirectorShadowMission>(
                pWork.Assignments.Count);
            var armies = new Dictionary<long, ArmyStrategicFacts>();
            for (int i = 0; i < pWork.Armies.Count; i++)
                armies[pWork.Armies[i].ArmyId] = pWork.Armies[i];
            IReadOnlyDictionary<long, FrontTargetAssignment>
                frontAssignments = BuildFrontTargetAssignments(pKingdom,
                    pWork, armies, pCommit);
            var cumulativeForce =
                new Dictionary<FrontTargetReservationKey, int>();

            for (int i = 0; i < pWork.Assignments.Count; i++)
            {
                WarArmyAssignment assignment = pWork.Assignments[i];
                if (!armies.TryGetValue(assignment.ArmyId,
                        out ArmyStrategicFacts army)) continue;
                if (!pWork.PlanByWarId.TryGetValue(assignment.WarId,
                        out WarPlanWork plan)) continue;
                frontAssignments.TryGetValue(assignment.ArmyId,
                    out FrontTargetAssignment frontAssignment);

                City target = ResolvePlanTarget(pKingdom, pWork, plan,
                    assignment, frontAssignment,
                    FindCity(army.AnchorCityId), army.CurrentTargetCityId,
                    pCommit,
                    out long coalitionTaskId,
                    out ArmyRtsProposalKind proposalKind);
                bool hasStrategicTarget =
                    proposalKind == ArmyRtsProposalKind.Attack ||
                    proposalKind == ArmyRtsProposalKind.Defend;
                int enemyForce = target?.kingdom == pKingdom
                    ? 0
                    : GetWarriorCount(pWork, target);
                var reservationKey = new FrontTargetReservationKey(
                    assignment.WarId, target?.id ?? -1L);
                cumulativeForce.TryGetValue(reservationKey,
                    out int previousForce);
                ArmyOperationalDirectorProjection operational =
                    ArmyOperationalDirectorRules.Project(
                        new ArmyOperationalDirectorFacts(army.UnitCount,
                            army.Supply, army.Organization));
                int friendlyForce = SaturatingAdd(previousForce,
                    operational.EffectiveForce);
                cumulativeForce[reservationKey] = friendlyForce;
                bool survival = plan.Allocation.CapitalThreat &&
                                assignment.Role == ArmyRtsRole.Defense;
                ArmyAttackThresholdFacts thresholdFacts =
                    BuildThresholdFacts(pKingdom, plan, target,
                        operational);
                int ratio = KingdomWarDirectorRules.
                    RequiredAttackRatioBasisPoints(thresholdFacts);
                bool forceReady = target?.data != null &&
                    KingdomWarDirectorRules.CanLaunchAttack(friendlyForce,
                        enemyForce, ratio, survival);
                bool homelandRecapture = frontAssignment?.FriendlyDefense ==
                                         true;
                ArmyRtsRole role = proposalKind ==
                                   ArmyRtsProposalKind.Defend ||
                                   homelandRecapture
                    ? ArmyRtsRole.Defense
                    : KingdomWarDirectorRules.ResolveMissionRole(
                        assignment.Role, hasStrategicTarget, forceReady,
                        friendlyDefenseTarget: homelandRecapture);
                bool connectedCorridor = target?.data != null &&
                    GetConnectedCorridor(pWork, plan.War, target,
                        pKingdom);

                result.Add(new KingdomWarDirectorShadowMission
                {
                    ArmyId = assignment.ArmyId,
                    WarId = assignment.WarId,
                    FrontId = coalitionTaskId >= 0L
                        ? coalitionTaskId
                        : target?.id ?? assignment.WarId,
                    TargetCityId = target?.id ?? -1L,
                    LegacyTargetCityId = army.CurrentTargetCityId,
                    ProposalKind = proposalKind,
                    OpenObjectiveCount = plan.OpenObjectiveCount,
                    Role = role,
                    Posture = role == ArmyRtsRole.Defense ||
                              role == ArmyRtsRole.Reserve
                        ? ArmyRtsPosture.Defend
                        : ArmyRtsPosture.Automatic,
                    FriendlyForce = friendlyForce,
                    EnemyForce = enemyForce,
                    RequiredRatioBasisPoints = ratio,
                    ForceReady = forceReady,
                    SurvivalException = survival,
                    ConnectedSupply = connectedCorridor,
                    ConnectedCorridor = connectedCorridor
                });
            }
            FinalizeTargetReservations(result);
            return result;
        }

        private static void FinalizeTargetReservations(
            IReadOnlyList<KingdomWarDirectorShadowMission> pMissions)
        {
            var finalForceByTarget =
                new Dictionary<FrontTargetReservationKey, int>();
            for (int i = 0; i < pMissions.Count; i++)
            {
                KingdomWarDirectorShadowMission mission = pMissions[i];
                if (mission == null || mission.TargetCityId < 0L) continue;
                var key = new FrontTargetReservationKey(mission.WarId,
                    mission.TargetCityId);
                finalForceByTarget.TryGetValue(key, out int previous);
                if (mission.FriendlyForce > previous)
                    finalForceByTarget[key] = mission.FriendlyForce;
            }

            for (int i = 0; i < pMissions.Count; i++)
            {
                KingdomWarDirectorShadowMission mission = pMissions[i];
                if (mission == null || mission.TargetCityId < 0L) continue;
                var key = new FrontTargetReservationKey(mission.WarId,
                    mission.TargetCityId);
                if (!finalForceByTarget.TryGetValue(key,
                        out int finalForce)) continue;
                mission.FriendlyForce = finalForce;
                mission.ForceReady = KingdomWarDirectorRules.
                    CanLaunchAttack(finalForce, mission.EnemyForce,
                        mission.RequiredRatioBasisPoints,
                        mission.SurvivalException);
                if (mission.Role == ArmyRtsRole.Defense ||
                    mission.Role == ArmyRtsRole.Reserve) continue;
                mission.Role = KingdomWarDirectorRules.ResolveMissionRole(
                    mission.Role, hasStrategicTarget: true,
                    forceReady: mission.ForceReady);
            }
        }

        private static IReadOnlyDictionary<long, FrontTargetAssignment>
            BuildFrontTargetAssignments(Kingdom pKingdom,
                PlanningWork pWork,
                IReadOnlyDictionary<long, ArmyStrategicFacts> pArmies,
                bool pCommit)
        {
            var result = new Dictionary<long, FrontTargetAssignment>();
            if (pCommit)
                CoalitionWarTaskService.ClearLeaderReservations(pKingdom);
            for (int planIndex = 0;
                 planIndex < pWork.SelectedWars.Count; planIndex++)
            {
                WarPlanWork plan = pWork.SelectedWars[planIndex];
                IReadOnlyList<FrontTargetFacts> targets =
                    plan?.Front?.Targets ??
                     (IReadOnlyList<FrontTargetFacts>)
                     Array.Empty<FrontTargetFacts>();
                if (plan == null) continue;
                targets = RevalidateFrontTargets(pKingdom, pWork,
                    plan.War, targets);
                targets = ArmyRtsAsyncPlanningService.OrderTargets(
                    CreateAsyncPlanStamp(pKingdom, pWork, plan.War),
                    targets);
                plan.OpenObjectiveCount = targets.Count;
                bool warLeader = CoalitionWarTaskService.IsWarLeader(
                    plan.War, pKingdom);
                IReadOnlyList<FrontTargetFacts> assignmentTargets =
                    targets;
                IReadOnlyDictionary<long, int> externalReservations =
                    warLeader
                        ? CoalitionWarTaskService.ExternalReservationCounts(
                            plan.War, pKingdom)
                        : null;
                if (assignmentTargets.Count == 0)
                {
                    if (pCommit && warLeader)
                        CoalitionWarTaskService.ReplaceLeaderReservations(
                            plan.War, pKingdom,
                            Array.Empty<CoalitionLeaderReservationSpec>());
                    continue;
                }
                var frontArmies = new List<FrontArmyFacts>();
                for (int assignmentIndex = 0;
                     assignmentIndex < pWork.Assignments.Count;
                     assignmentIndex++)
                {
                    WarArmyAssignment assignment =
                        pWork.Assignments[assignmentIndex];
                    if (assignment.WarId != plan.Allocation.WarId ||
                        assignment.Role == ArmyRtsRole.Defense &&
                        plan.Allocation.CapitalThreat ||
                        !pArmies.TryGetValue(assignment.ArmyId,
                            out ArmyStrategicFacts army)) continue;
                    ArmyOperationalDirectorProjection operational =
                        ArmyOperationalDirectorRules.Project(
                            new ArmyOperationalDirectorFacts(army.UnitCount,
                                army.Supply, army.Organization));
                    int ratio = KingdomWarDirectorRules.
                        RequiredAttackRatioBasisPoints(BuildThresholdFacts(
                            pKingdom, plan, null, operational));
                    frontArmies.Add(new FrontArmyFacts(assignment.ArmyId,
                        operational.EffectiveForce,
                        army.CurrentTargetCityId, ratio,
                        army.CaptainX, army.CaptainY));
                }

                IReadOnlyList<FrontTargetAssignment> assignments =
                    KingdomWarDirectorRules.AssignFrontTargets(frontArmies,
                        assignmentTargets, externalReservations);
                if (pCommit && warLeader)
                {
                    var leaderReservations =
                        new List<CoalitionLeaderReservationSpec>(
                            assignments.Count);
                    for (int i = 0; i < assignments.Count; i++)
                        leaderReservations.Add(
                            new CoalitionLeaderReservationSpec(
                                assignments[i].ArmyId,
                                assignments[i].TargetCityId));
                    CoalitionWarTaskService.ReplaceLeaderReservations(
                        plan.War, pKingdom, leaderReservations);
                }
                for (int i = 0; i < assignments.Count; i++)
                    result[assignments[i].ArmyId] = assignments[i];
            }
            return result;
        }

        private static IReadOnlyList<FrontTargetFacts>
            RevalidateFrontTargets(Kingdom pKingdom, PlanningWork pWork,
                War pWar,
                IReadOnlyList<FrontTargetFacts> pTargets)
        {
            if (pTargets == null || pTargets.Count == 0)
                return Array.Empty<FrontTargetFacts>();
            var valid = new List<FrontTargetFacts>(pTargets.Count);
            for (int i = 0; i < pTargets.Count; i++)
            {
                FrontTargetFacts facts = pTargets[i];
                City city = FindCity(facts?.CityId ?? -1L);
                ArmyRtsObjectiveState state = GetObjectiveState(pWork,
                    pWar, pKingdom, city);
                if (ArmyRtsObjectiveService.IsOpen(state))
                    valid.Add(facts);
            }
            return valid;
        }

        private static WarPlanWork BuildWarPlan(Kingdom pKingdom, War pWar)
        {
            if (!IsActiveWar(pWar) || !SafeHasKingdom(pWar, pKingdom))
                return null;
            WarTerritoryService.TryGetPrimaryOpenGoalCityId(
                pWar.data.id, out long formalGoalCityId);
            bool capitalThreat = IsCapitalThreatened(pWar, pKingdom);
            bool localTerritoryThreat = IsTerritoryThreatened(pWar,
                pKingdom);
            bool warGoal = formalGoalCityId >= 0L;
            int score = 0;
            if (WarScoreService.TryGetSnapshot(pWar, pKingdom,
                    out WarScoreSnapshot snapshot))
                score = snapshot.Score;
            int required = ArmyRtsRules.AssaultReservationCap(
                capitalThreat, warGoal);
            return new WarPlanWork
            {
                War = pWar,
                FormalGoalCityId = formalGoalCityId,
                Allocation = new WarAllocationFacts(pWar.data.id,
                    capitalThreat, warGoal, score, required,
                    localTerritoryThreat)
            };
        }

        private static void RefreshPublishCaches(Kingdom pKingdom,
            PlanningWork pWork)
        {
            pWork.WarriorCountByCity.Clear();
            pWork.CorridorByTarget.Clear();
            pWork.ObjectiveStateByTarget.Clear();
            Actor ruler = pKingdom?.king;
            bool aggressive = SafeHasTrait(ruler, "ambitious");
            bool cautious = SafeHasTrait(ruler, "content");
            bool mandate = MandateService.IsMandateKingdom(pKingdom);
            MandatePhase phase = MandatePhaseService.CurrentPhase;
            for (int i = 0; i < pWork.SelectedWars.Count; i++)
            {
                WarPlanWork plan = pWork.SelectedWars[i];
                if (plan?.War?.data == null) continue;
                int exhaustion = 0;
                if (WarScoreService.TryGetSnapshot(plan.War, pKingdom,
                        out WarScoreSnapshot snapshot))
                    exhaustion = plan.War.isAttacker(pKingdom)
                        ? snapshot.AttackerExhaustion
                        : snapshot.DefenderExhaustion;
                plan.AggressiveRuler = aggressive;
                plan.CautiousRuler = cautious;
                plan.ExpansionPhase = mandate &&
                                      phase == MandatePhase.Renewal;
                plan.CorruptPhase = mandate &&
                    (phase == MandatePhase.Decline ||
                     phase == MandatePhase.Chaos);
                plan.Fatigued = exhaustion >= 60;
            }
        }

        private static ArmyAttackThresholdFacts BuildThresholdFacts(
            Kingdom pKingdom, WarPlanWork pPlan, City pTarget,
            ArmyOperationalDirectorProjection pOperational)
        {
            int distance = DistanceSquared(pKingdom?.capital, pTarget);
            return new ArmyAttackThresholdFacts
            {
                AggressiveRuler = pPlan.AggressiveRuler,
                CautiousRuler = pPlan.CautiousRuler,
                ExpansionPhase = pPlan.ExpansionPhase,
                CorruptPhase = pPlan.CorruptPhase,
                GoodSupply = pOperational.GoodSupply,
                LowSupply = pOperational.LowSupply,
                Fatigued = pPlan.Fatigued,
                LongDistance = IsLiveCity(pTarget) && distance >= 14_400,
                PoorOrganization = pOperational.PoorOrganization
            };
        }

        private static City ResolvePlanTarget(Kingdom pKingdom,
            PlanningWork pWork, WarPlanWork pPlan,
            WarArmyAssignment pAssignment,
            FrontTargetAssignment pFrontAssignment, City pReserveTarget,
            long pCurrentTargetCityId,
            bool pCommit, out long pCoalitionTaskId,
            out ArmyRtsProposalKind pProposalKind)
        {
            pCoalitionTaskId = -1L;
            pProposalKind = ArmyRtsProposalKind.None;
            if (pAssignment.Role == ArmyRtsRole.Defense &&
                pPlan.Allocation.CapitalThreat &&
                IsLiveCity(pKingdom?.capital))
                return ResolveOpenTarget(pWork, pPlan.War, pKingdom,
                    pKingdom.capital, out pProposalKind);
            Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                pAssignment.ArmyId, pKingdom?.id ?? -1L);
            if (pFrontAssignment != null &&
                pFrontAssignment.FriendlyDefense)
            {
                City localDefense = FindCity(
                    pFrontAssignment.TargetCityId);
                ArmyRtsObjectiveState localState = GetObjectiveState(pWork,
                    pPlan.War, pKingdom, localDefense);
                ArmyRtsProposalKind localKind = ArmyRtsObjectiveRules.
                    ResolveHomelandRecaptureProposal(localState);
                if (localKind != ArmyRtsProposalKind.None)
                {
                    if (pCommit)
                        CoalitionWarTaskService.ReleaseArmyClaim(
                            pAssignment.ArmyId);
                    pProposalKind = localKind;
                    return localDefense;
                }
            }
            if (CoalitionWarTaskService.TryResolveTarget(pPlan.War,
                    pKingdom, army, pCommit, out City coalitionTarget,
                    out pCoalitionTaskId))
                return ResolveOpenTarget(pWork, pPlan.War, pKingdom,
                    coalitionTarget, out pProposalKind);
            if (pCommit)
                CoalitionWarTaskService.ReleaseArmyClaim(
                    pAssignment.ArmyId);
            if (pFrontAssignment != null)
            {
                City target = FindCity(pFrontAssignment.TargetCityId);
                City open = ResolveOpenTarget(pWork, pPlan.War, pKingdom,
                    target, out pProposalKind);
                if (open != null) return open;
            }
            if (pPlan.OpenObjectiveCount > 0) return null;
            City currentTarget = FindCity(pCurrentTargetCityId);
            ArmyRtsObjectiveState currentTargetState =
                GetObjectiveState(pWork, pPlan.War, pKingdom,
                    currentTarget);
            if (ArmyRtsObjectiveRules.ShouldRetainForwardFrontHoldTarget(
                    pNoOpenAttackObjectives:
                        pPlan.OpenObjectiveCount <= 0,
                    pTargetLive: IsLiveCity(currentTarget),
                    pTargetSecuredByThisWar: currentTargetState ==
                        ArmyRtsObjectiveState.ClosedOccupied))
            {
                pProposalKind = ArmyRtsProposalKind.FrontHold;
                return currentTarget;
            }
            City hold = IsLiveCity(pReserveTarget)
                ? pReserveTarget
                : IsLiveCity(pKingdom?.capital) ? pKingdom.capital : null;
            if (hold != null) pProposalKind = ArmyRtsProposalKind.FrontHold;
            return hold;
        }

        private static City ResolveOpenTarget(PlanningWork pWork, War pWar,
            Kingdom pKingdom, City pTarget,
            out ArmyRtsProposalKind pProposalKind)
        {
            pProposalKind = ArmyRtsProposalKind.None;
            ArmyRtsObjectiveState state = GetObjectiveState(pWork, pWar,
                pKingdom, pTarget);
            if (state == ArmyRtsObjectiveState.OpenAttack)
                pProposalKind = ArmyRtsProposalKind.Attack;
            else if (state == ArmyRtsObjectiveState.OpenDefense)
                pProposalKind = ArmyRtsProposalKind.Defend;
            return pProposalKind == ArmyRtsProposalKind.None
                ? null
                : pTarget;
        }

        private static int GetWarriorCount(PlanningWork pWork, City pCity)
        {
            if (pCity?.data == null) return 0;
            if (pWork != null && pWork.WarriorCountByCity.TryGetValue(
                    pCity.id, out int cached)) return cached;
            int count = SafeWarriorCount(pCity);
            if (pWork != null) pWork.WarriorCountByCity[pCity.id] = count;
            return count;
        }

        private static bool GetConnectedCorridor(PlanningWork pWork,
            War pWar, City pCity, Kingdom pKingdom)
        {
            if (pWar?.data == null || pCity?.data == null) return false;
            var key = new FrontTargetReservationKey(pWar.data.id,
                pCity.id);
            if (pWork != null && pWork.CorridorByTarget.TryGetValue(key,
                    out bool cached)) return cached;
            bool connected = IsConnectedCorridor(pWar, pCity, pKingdom);
            if (pWork != null) pWork.CorridorByTarget[key] = connected;
            return connected;
        }

        private static ArmyRtsObjectiveState GetObjectiveState(
            PlanningWork pWork, War pWar, Kingdom pKingdom, City pCity)
        {
            if (pWar?.data == null || pCity?.data == null)
                return ArmyRtsObjectiveState.Unavailable;
            var key = new FrontTargetReservationKey(pWar.data.id,
                pCity.id);
            if (pWork != null && pWork.ObjectiveStateByTarget.TryGetValue(
                    key, out ArmyRtsObjectiveState cached)) return cached;
            ArmyRtsObjectiveState state = ArmyRtsObjectiveService.Classify(
                pWar, pKingdom, pCity);
            if (pWork != null) pWork.ObjectiveStateByTarget[key] = state;
            return state;
        }

        private static IReadOnlyList<long> RegisterWar(War pWar)
        {
            if (pWar?.data == null) return Array.Empty<long>();
            long warId = pWar.data.id;
            RemoveWar(warId);
            var participants = new HashSet<long>();
            AddParticipantIds(pWar, participants);
            ParticipantIdsByWar[warId] = participants;
            foreach (long kingdomId in participants)
            {
                if (!WarIdsByKingdom.TryGetValue(kingdomId,
                        out SortedSet<long> wars))
                {
                    wars = new SortedSet<long>();
                    WarIdsByKingdom[kingdomId] = wars;
                }
                wars.Add(warId);
            }
            return new List<long>(participants);
        }

        private static void RemoveWar(long pWarId)
        {
            if (!ParticipantIdsByWar.TryGetValue(pWarId,
                    out HashSet<long> participants)) return;
            ParticipantIdsByWar.Remove(pWarId);
            foreach (long kingdomId in participants)
            {
                if (!WarIdsByKingdom.TryGetValue(kingdomId,
                        out SortedSet<long> wars)) continue;
                wars.Remove(pWarId);
                if (wars.Count == 0) WarIdsByKingdom.Remove(kingdomId);
            }
        }

        private static void AddParticipantIds(War pWar,
            HashSet<long> pResult)
        {
            if (pWar?.data == null || pResult == null) return;
            try
            {
                foreach (Kingdom kingdom in pWar.getAttackers())
                    if (kingdom?.data != null) pResult.Add(kingdom.id);
                foreach (Kingdom kingdom in pWar.getDefenders())
                    if (kingdom?.data != null) pResult.Add(kingdom.id);
            }
            catch { }
            Kingdom attacker = SafeMainAttacker(pWar);
            Kingdom defender = SafeMainDefender(pWar);
            if (attacker?.data != null) pResult.Add(attacker.id);
            if (defender?.data != null) pResult.Add(defender.id);
        }

        private static bool TryTakeWarIdBatch(long pKingdomId,
            long pAfterWarId, List<long> pResult, out long pLastWarId,
            out bool pComplete)
        {
            pResult?.Clear();
            pLastWarId = pAfterWarId;
            pComplete = true;
            if (pResult == null || pAfterWarId == long.MaxValue)
                return false;
            if (!WarIdsByKingdom.TryGetValue(pKingdomId,
                    out SortedSet<long> wars) || wars.Count == 0)
                return false;
            long lower = pAfterWarId < 0L ? 0L : pAfterWarId + 1L;
            SortedSet<long> remaining;
            try { remaining = wars.GetViewBetween(lower, long.MaxValue); }
            catch { return false; }
            using (SortedSet<long>.Enumerator enumerator =
                   remaining.GetEnumerator())
            {
                while (pResult.Count < KingdomWarDirectorWorkRules.
                           MaximumWarPlansPerWorkItem &&
                       enumerator.MoveNext())
                {
                    pLastWarId = enumerator.Current;
                    pResult.Add(enumerator.Current);
                }
                if (pResult.Count >= KingdomWarDirectorWorkRules.
                        MaximumWarPlansPerWorkItem)
                    pComplete = false;
            }
            return pResult.Count > 0;
        }

        private static void ScheduleParticipants(
            IReadOnlyList<long> pKingdomIds)
        {
            if (pKingdomIds == null) return;
            for (int i = 0; i < pKingdomIds.Count; i++)
                Schedule(FindKingdom(pKingdomIds[i]));
        }

        private static IEnumerator<Kingdom> CreateOpponentEnumerator(
            War pWar, Kingdom pKingdom)
        {
            if (pWar == null || pKingdom == null) return null;
            try
            {
                IEnumerable<Kingdom> opponents = pWar.isAttacker(pKingdom)
                    ? pWar.getDefenders()
                    : pWar.getAttackers();
                return opponents?.GetEnumerator();
            }
            catch { }
            return null;
        }

        private static void RemoveWork(long pKingdomId)
        {
            if (!WorkByKingdom.TryGetValue(pKingdomId,
                    out PlanningWork work)) return;
            WorkByKingdom.Remove(pKingdomId);
            work.Dispose();
        }

        internal static bool IsConnectedCorridor(War pWar, City pCity,
            Kingdom pKingdom)
        {
            if (!IsLiveCity(pCity) || !IsLiveKingdom(pKingdom)) return false;
            if (pCity.kingdom == pKingdom) return true;
            try
            {
                if (pCity.neighbours_cities == null) return false;
                foreach (City neighbour in pCity.neighbours_cities)
                {
                    if (!IsLiveCity(neighbour)) continue;
                    if (neighbour.kingdom == pKingdom) return true;
                    if (IsFrozenControlledByOurSide(pWar, neighbour,
                            pKingdom)) return true;
                }
            }
            catch { }
            return false;
        }

        private static void ResolveStrategicReachability(
            Kingdom pKingdom, City pTarget, out bool pSameIslandLand,
            out bool pTransportReachable)
        {
            pSameIslandLand = false;
            pTransportReachable = false;
            City origin = pKingdom?.capital;
            if (!IsLiveCity(origin) || !IsLiveCity(pTarget)) return;
            try
            {
                WorldTile originTile = origin.getTile();
                WorldTile targetTile = pTarget.getTile();
                if (originTile?.data == null || targetTile?.data == null)
                    return;
                pSameIslandLand = originTile.isSameIsland(targetTile);
                pTransportReachable =
                    ArmyRtsTransportRules.ShouldAdmitTransportTarget(
                        pSameIslandLand,
                        targetTile.reachableFrom(originTile));
            }
            catch
            {
                pSameIslandLand = false;
                pTransportReachable = false;
            }
        }

        private static bool IsCapitalThreatened(War pWar,
            Kingdom pKingdom)
        {
            City capital = pKingdom?.capital;
            if (!IsLiveCity(capital)) return false;
            if (IsFrozenControlledByEnemy(pWar, capital, pKingdom))
                return true;
            return IsActivelyCapturedByEnemy(pWar, capital, pKingdom);
        }

        private static bool IsTerritoryThreatened(War pWar,
            Kingdom pKingdom)
        {
            if (pWar?.data == null || !IsLiveKingdom(pKingdom))
                return false;
            try
            {
                for (int i = 0; i < pKingdom.cities.Count; i++)
                {
                    City city = pKingdom.cities[i];
                    if (!IsLiveCity(city)) continue;
                    if (IsFrozenControlledByEnemy(pWar, city, pKingdom) ||
                        IsActivelyCapturedByEnemy(pWar, city, pKingdom))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsActivelyCapturedByEnemy(War pWar,
            City pCity, Kingdom pKingdom)
        {
            if (!IsLiveCity(pCity) || !IsLiveKingdom(pKingdom) ||
                pWar?.data == null) return false;
            try
            {
                Kingdom capturer = pCity.getCapturingKingdom();
                return capturer?.data != null &&
                       pWar.isInWarWith(pKingdom, capturer) &&
                       pCity.getCaptureTicks() > 0f;
            }
            catch { return false; }
        }

        private static long ActiveEnemyCapturerId(City pCity)
        {
            Kingdom owner = pCity?.kingdom;
            if (!IsLiveCity(pCity) || !IsLiveKingdom(owner)) return -1L;
            try
            {
                Kingdom capturer = pCity.getCapturingKingdom();
                return capturer?.data != null &&
                       pCity.getCaptureTicks() > 0f &&
                       owner.isInWarWith(capturer)
                    ? capturer.id
                    : -1L;
            }
            catch { return -1L; }
        }

        private static bool IsFrozenControlledByEnemy(War pWar, City pCity,
            Kingdom pKingdom)
        {
            return CityAttackZoneService.IsControlledByEnemySide(pWar,
                pCity, pKingdom);
        }

        private static bool IsFrozenControlledByOurSide(War pWar, City pCity,
            Kingdom pKingdom)
        {
            return CityAttackZoneService.IsControlledBySide(pWar, pCity,
                pKingdom);
        }

        private static bool IsEnemyCity(War pWar, City pCity,
            Kingdom pKingdom)
        {
            if (!IsLiveCity(pCity) || !IsLiveKingdom(pCity.kingdom))
                return false;
            try { return pWar.isInWarWith(pKingdom, pCity.kingdom); }
            catch { return false; }
        }

        private static bool IsEligibleFieldArmy(ArmyStrategicFacts pArmy)
        {
            return pArmy != null && pArmy.ArmyId >= 0L &&
                   KingdomWarDirectorRules.ShouldAllocateFieldArmy(
                       pArmy.UnitCount, pArmy.CaptainAlive,
                       pArmy.RoyalGuard, pArmy.DedicatedGarrison,
                       pArmy.SpecialArmy);
        }

        private static void RequestDepletedArmyRecovery(Kingdom pKingdom,
            ArmyStrategicFacts pArmy)
        {
            if (pKingdom?.data == null || pArmy == null) return;
            Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                pArmy.ArmyId, pKingdom.id);
            City anchor = FindCity(pArmy.AnchorCityId);
            int targetStrength = StandingArmyService.TargetStrength(army,
                pKingdom);
            int demand = Math.Max(
                ArmyLogisticsRules.MinimumOperationalForce -
                Math.Max(0, pArmy.UnitCount),
                targetStrength - Math.Max(0, pArmy.UnitCount));
            TemporaryLevyService.RequestOffensiveRecovery(pKingdom,
                anchor, Math.Max(1, demand), pTargetArmy: army);
        }

        private static bool TryRecoverMissingCaptain(Kingdom pKingdom,
            ArmyStrategicFacts pArmy)
        {
            if (pKingdom?.data == null || pArmy == null ||
                !ArmyRtsRuntimeModeRules.ShouldCommit(
                    ArmyRtsRuntimeMode.Current)) return false;
            Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                pArmy.ArmyId, pKingdom.id);
            if (army?.data == null) return false;
            if (AWArmyService.IsRoleArmy(army, AWArmyRole.SlaveArmy))
            {
                TemporarySlaveVanguardService.RequestCaptainRecovery(
                    pKingdom, army);
                return false;
            }

            try { army.checkCaptainExistence(); }
            catch { }
            try
            {
                Actor captain = army.getCaptain();
                if (captain?.data != null && captain.isAlive() &&
                    !captain.isRekt()) return true;
            }
            catch { }
            TemporaryLevyService.RequestCaptainRecovery(pKingdom, army);
            return false;
        }

        private static WarPlanWork FindSelectedPlan(
            IReadOnlyList<WarPlanWork> pPlans, long pWarId)
        {
            if (pPlans == null) return null;
            for (int i = 0; i < pPlans.Count; i++)
                if (pPlans[i]?.Allocation?.WarId == pWarId)
                    return pPlans[i];
            return null;
        }

        private static bool MatchesCityId(City pCity, long pCityId)
        {
            return pCity?.data != null && pCityId >= 0L &&
                   (pCity.id == pCityId || pCity.data.id == pCityId);
        }

        private static int DistanceSquared(City pFirst, City pSecond)
        {
            if (!IsLiveCity(pFirst) || !IsLiveCity(pSecond))
                return int.MaxValue;
            try
            {
                float value = Toolbox.SquaredDistVec2Float(
                    pFirst.city_center, pSecond.city_center);
                if (value >= int.MaxValue) return int.MaxValue;
                return Math.Max(0, (int)value);
            }
            catch { return int.MaxValue; }
        }

        private static int SafeWarriorCount(City pCity)
        {
            try { return Math.Max(0, pCity?.countWarriors() ?? 0); }
            catch { return 0; }
        }

        private static bool SafeHasTrait(Actor pActor, string pTraitId)
        {
            try
            {
                return pActor?.data != null && pActor.hasTrait(pTraitId);
            }
            catch { return false; }
        }

        private static bool SafeHasKingdom(War pWar, Kingdom pKingdom)
        {
            try { return pWar?.data != null && pWar.hasKingdom(pKingdom); }
            catch { return false; }
        }

        private static bool IsActiveWar(War pWar)
        {
            try { return pWar?.data != null && !pWar.hasEnded(); }
            catch { return false; }
        }

        private static bool IsLiveKingdom(Kingdom pKingdom)
        {
            try
            {
                return pKingdom?.data != null && !pKingdom.isRekt() &&
                       pKingdom.isAlive();
            }
            catch { return false; }
        }

        private static bool IsLiveCity(City pCity)
        {
            try
            {
                return pCity?.data != null && !pCity.isRekt() &&
                       pCity.isAlive();
            }
            catch { return false; }
        }

        private static Kingdom SafeMainAttacker(War pWar)
        {
            try { return pWar?.getMainAttacker(); }
            catch { return null; }
        }

        private static Kingdom SafeMainDefender(War pWar)
        {
            try { return pWar?.getMainDefender(); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static War FindWar(long pWarId)
        {
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return pCityId < 0L ? null : World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static long CurrentWorldDay()
        {
            try
            {
                double time = Math.Max(0d,
                    World.world?.getCurWorldTime() ?? 0d);
                double days = Math.Floor(time * 6d);
                return days >= long.MaxValue ? long.MaxValue : (long)days;
            }
            catch { return 0L; }
        }

        private static int SaturatingAdd(int pLeft, int pRight)
        {
            long value = (long)Math.Max(0, pLeft) + Math.Max(0, pRight);
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
