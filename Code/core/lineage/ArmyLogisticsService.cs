using System;
using System.Collections.Generic;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.lineage
{
    public readonly struct ArmyLogisticsEventSnapshot
    {
        public ArmyLogisticsEventSnapshot(int casualties,
            bool captainLost)
        {
            Casualties = Math.Max(0, casualties);
            CaptainLost = captainLost;
        }

        public int Casualties { get; }
        public bool CaptainLost { get; }
    }

    public sealed class ArmyLogisticsRuntimeIndex
    {
        private sealed class EventState
        {
            internal int Casualties;
            internal bool CaptainLost;
        }

        private readonly Dictionary<long, EventState> _eventsByArmy =
            new Dictionary<long, EventState>();

        public void RecordCasualty(long armyId, bool captainLost)
        {
            if (armyId < 0L) return;
            if (!_eventsByArmy.TryGetValue(armyId, out EventState state))
            {
                state = new EventState();
                _eventsByArmy[armyId] = state;
            }
            if (state.Casualties < int.MaxValue) state.Casualties++;
            state.CaptainLost |= captainLost;
        }

        public ArmyLogisticsEventSnapshot ConsumeCasualties(long armyId)
        {
            if (!_eventsByArmy.TryGetValue(armyId, out EventState state))
                return new ArmyLogisticsEventSnapshot(0, false);
            _eventsByArmy.Remove(armyId);
            return new ArmyLogisticsEventSnapshot(state.Casualties,
                state.CaptainLost);
        }

        public void Remove(long armyId)
        {
            _eventsByArmy.Remove(armyId);
        }

        public void Clear()
        {
            _eventsByArmy.Clear();
        }
    }

    public sealed class ArmyLogisticsActivityIndex
    {
        private readonly struct MissionRef
        {
            internal MissionRef(long pKingdomId, long pWarId)
            {
                KingdomId = pKingdomId;
                WarId = pWarId;
            }

            internal long KingdomId { get; }
            internal long WarId { get; }
        }

        private sealed class WarSides
        {
            internal readonly HashSet<long> Attackers = new HashSet<long>();
            internal readonly HashSet<long> Defenders = new HashSet<long>();
        }

        private readonly Dictionary<long, WarSides> _wars =
            new Dictionary<long, WarSides>();
        private readonly Dictionary<long, int> _warRefsByKingdom =
            new Dictionary<long, int>();
        private readonly Dictionary<long, MissionRef> _missionByArmy =
            new Dictionary<long, MissionRef>();
        private readonly Dictionary<long, int> _missionRefsByKingdom =
            new Dictionary<long, int>();

        public void RegisterWar(long warId,
            IReadOnlyList<long> attackerKingdomIds,
            IReadOnlyList<long> defenderKingdomIds)
        {
            if (warId < 0L) return;
            EndWar(warId);
            var sides = new WarSides();
            AddSide(sides.Attackers, attackerKingdomIds);
            AddSide(sides.Defenders, defenderKingdomIds);
            _wars[warId] = sides;
            AddWarRefs(sides.Attackers, 1);
            AddWarRefs(sides.Defenders, 1);
        }

        public void JoinWar(long warId, long kingdomId, bool attacker)
        {
            if (warId < 0L || kingdomId < 0L) return;
            if (!_wars.TryGetValue(warId, out WarSides sides))
            {
                sides = new WarSides();
                _wars[warId] = sides;
            }
            HashSet<long> side = attacker ? sides.Attackers : sides.Defenders;
            HashSet<long> other = attacker ? sides.Defenders : sides.Attackers;
            if (other.Remove(kingdomId)) AddRef(_warRefsByKingdom,
                kingdomId, -1);
            if (side.Add(kingdomId)) AddRef(_warRefsByKingdom,
                kingdomId, 1);
        }

        public void LeaveWar(long warId, long kingdomId)
        {
            if (!_wars.TryGetValue(warId, out WarSides sides)) return;
            if (sides.Attackers.Remove(kingdomId) ||
                sides.Defenders.Remove(kingdomId))
                AddRef(_warRefsByKingdom, kingdomId, -1);
        }

        public IReadOnlyList<long> EndWar(long warId)
        {
            if (_wars.TryGetValue(warId, out WarSides sides))
            {
                AddWarRefs(sides.Attackers, -1);
                AddWarRefs(sides.Defenders, -1);
                _wars.Remove(warId);
            }
            var expired = new List<long>();
            foreach (KeyValuePair<long, MissionRef> pair in _missionByArmy)
                if (pair.Value.WarId == warId) expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++)
                InvalidateMission(expired[i]);
            return expired;
        }

        public void AssignMission(long armyId, long kingdomId,
            long warId = -1L)
        {
            if (armyId < 0L || kingdomId < 0L) return;
            if (_missionByArmy.TryGetValue(armyId,
                    out MissionRef previous))
            {
                if (previous.KingdomId == kingdomId &&
                    previous.WarId == warId) return;
                AddRef(_missionRefsByKingdom, previous.KingdomId, -1);
            }
            _missionByArmy[armyId] = new MissionRef(kingdomId, warId);
            AddRef(_missionRefsByKingdom, kingdomId, 1);
        }

        public void InvalidateMission(long armyId)
        {
            if (!_missionByArmy.TryGetValue(armyId,
                    out MissionRef mission)) return;
            _missionByArmy.Remove(armyId);
            AddRef(_missionRefsByKingdom, mission.KingdomId, -1);
        }

        public void RemoveArmy(long armyId)
        {
            InvalidateMission(armyId);
        }

        public bool IsKingdomActive(long kingdomId)
        {
            return Count(_warRefsByKingdom, kingdomId) > 0 ||
                   Count(_missionRefsByKingdom, kingdomId) > 0;
        }

        public IReadOnlyList<long> GetActiveKingdomIds()
        {
            var result = new SortedSet<long>();
            foreach (KeyValuePair<long, int> pair in _warRefsByKingdom)
                if (pair.Value > 0) result.Add(pair.Key);
            foreach (KeyValuePair<long, int> pair in _missionRefsByKingdom)
                if (pair.Value > 0) result.Add(pair.Key);
            var ids = new long[result.Count];
            result.CopyTo(ids);
            return ids;
        }

        public bool AreOnSameWarSide(long warId, long firstKingdomId,
            long secondKingdomId)
        {
            if (!_wars.TryGetValue(warId, out WarSides sides)) return false;
            return sides.Attackers.Contains(firstKingdomId) &&
                   sides.Attackers.Contains(secondKingdomId) ||
                   sides.Defenders.Contains(firstKingdomId) &&
                   sides.Defenders.Contains(secondKingdomId);
        }

        public void Clear()
        {
            _wars.Clear();
            _warRefsByKingdom.Clear();
            _missionByArmy.Clear();
            _missionRefsByKingdom.Clear();
        }

        private void AddWarRefs(IEnumerable<long> pIds, int pDelta)
        {
            foreach (long id in pIds)
                AddRef(_warRefsByKingdom, id, pDelta);
        }

        private static void AddSide(HashSet<long> pSide,
            IReadOnlyList<long> pIds)
        {
            if (pIds == null) return;
            for (int i = 0; i < pIds.Count; i++)
                if (pIds[i] >= 0L) pSide.Add(pIds[i]);
        }

        private static void AddRef(Dictionary<long, int> pRefs,
            long pId, int pDelta)
        {
            if (pId < 0L || pDelta == 0) return;
            int next = Count(pRefs, pId) + pDelta;
            if (next <= 0) pRefs.Remove(pId);
            else pRefs[pId] = next;
        }

        private static int Count(Dictionary<long, int> pRefs, long pId)
        {
            return pRefs.TryGetValue(pId, out int count) ? count : 0;
        }
    }

    public readonly struct ArmyOperationalStateSnapshot
    {
        public ArmyOperationalStateSnapshot(int pSupply, int pOrganization,
            ArmyRtsState pState, bool pConnectedSupply, bool pInCorridor,
            bool pMissionActive, long pKingdomId, long pWarId,
            bool pMissionConnected)
        {
            Supply = pSupply;
            Organization = pOrganization;
            State = pState;
            ConnectedSupply = pConnectedSupply;
            InCorridor = pInCorridor;
            MissionActive = pMissionActive;
            KingdomId = pKingdomId;
            WarId = pWarId;
            MissionConnected = pMissionConnected;
        }

        public int Supply { get; }
        public int Organization { get; }
        public ArmyRtsState State { get; }
        public bool ConnectedSupply { get; }
        public bool InCorridor { get; }
        public bool MissionActive { get; }
        public long KingdomId { get; }
        public long WarId { get; }
        public bool MissionConnected { get; }
    }

    public sealed class ArmyOperationalStateIndex
    {
        private sealed class State
        {
            internal int Supply = 100;
            internal int Organization = 100;
            internal ArmyRtsState RtsState = ArmyRtsState.Idle;
            internal bool ConnectedSupply;
            internal bool InCorridor;
            internal bool MissionActive;
            internal long KingdomId = -1L;
            internal long WarId = -1L;
            internal bool MissionConnected;
            internal int LastObservedCaptainTileId = -1;
        }

        private readonly Dictionary<long, State> _states =
            new Dictionary<long, State>();

        public void AssignMission(long armyId, long kingdomId,
            bool connectedSupply, bool inCorridor, long warId = -1L)
        {
            if (armyId < 0L) return;
            State state = GetOrCreate(armyId);
            state.KingdomId = kingdomId;
            state.MissionActive = true;
            state.ConnectedSupply = connectedSupply;
            state.InCorridor = inCorridor;
            state.WarId = warId;
            state.MissionConnected = inCorridor;
            state.LastObservedCaptainTileId = -1;
        }

        public void SetValues(long armyId, int supply, int organization,
            ArmyRtsState state)
        {
            State current = GetOrCreate(armyId);
            current.Supply = Math.Max(0, Math.Min(100, supply));
            current.Organization = Math.Max(0, Math.Min(100, organization));
            current.RtsState = state;
        }

        public void SetConnectivity(long armyId, bool connectedSupply,
            bool inCorridor)
        {
            State state = GetOrCreate(armyId);
            state.ConnectedSupply = connectedSupply;
            state.InCorridor = inCorridor;
        }

        public void SetState(long armyId, ArmyRtsState state)
        {
            GetOrCreate(armyId).RtsState = state;
        }

        public bool ObserveCaptainPosition(long armyId, int currentTileId)
        {
            State state = GetOrCreate(armyId);
            int previousTileId = state.LastObservedCaptainTileId;
            state.LastObservedCaptainTileId = currentTileId;
            return previousTileId >= 0 && currentTileId >= 0 &&
                   previousTileId != currentTileId;
        }

        public void InvalidateMission(long armyId)
        {
            if (!_states.TryGetValue(armyId, out State state)) return;
            state.MissionActive = false;
            state.RtsState = ArmyRtsState.Idle;
            state.ConnectedSupply = false;
            state.InCorridor = false;
            state.WarId = -1L;
            state.MissionConnected = false;
            state.LastObservedCaptainTileId = -1;
        }

        public bool TryGet(long armyId,
            out ArmyOperationalStateSnapshot pSnapshot)
        {
            if (!_states.TryGetValue(armyId, out State state))
            {
                pSnapshot = default;
                return false;
            }
            pSnapshot = Snapshot(state);
            return true;
        }

        public ArmyOperationalStateSnapshot GetOrCreateSnapshot(long armyId)
        {
            return Snapshot(GetOrCreate(armyId));
        }

        public void RemoveArmy(long armyId)
        {
            _states.Remove(armyId);
        }

        public void Clear()
        {
            _states.Clear();
        }

        private State GetOrCreate(long pArmyId)
        {
            if (!_states.TryGetValue(pArmyId, out State state))
            {
                state = new State();
                _states[pArmyId] = state;
            }
            return state;
        }

        private static ArmyOperationalStateSnapshot Snapshot(State pState)
        {
            return new ArmyOperationalStateSnapshot(pState.Supply,
                pState.Organization, pState.RtsState,
                pState.ConnectedSupply, pState.InCorridor,
                pState.MissionActive, pState.KingdomId, pState.WarId,
                pState.MissionConnected);
        }
    }

    public sealed class ArmyStrategicDayScheduler
    {
        private readonly Queue<long> _kingdomIds = new Queue<long>();
        private long _worldDay = -1L;

        public bool BeginDay(long pWorldDay,
            IReadOnlyList<long> pActiveKingdomIds)
        {
            long day = Math.Max(0L, pWorldDay);
            if (day == _worldDay || _kingdomIds.Count > 0) return false;
            _worldDay = day;
            var sorted = new SortedSet<long>();
            if (pActiveKingdomIds != null)
                for (int i = 0; i < pActiveKingdomIds.Count; i++)
                    if (pActiveKingdomIds[i] >= 0L)
                        sorted.Add(pActiveKingdomIds[i]);
            foreach (long id in sorted) _kingdomIds.Enqueue(id);
            return true;
        }

        public bool ShouldBeginDay(long pWorldDay)
        {
            return _kingdomIds.Count == 0 &&
                   Math.Max(0L, pWorldDay) != _worldDay;
        }

        public long TakeKingdom()
        {
            return _kingdomIds.Count > 0 ? _kingdomIds.Dequeue() : -1L;
        }

        public bool HasPending => _kingdomIds.Count > 0;
        public int PendingCount => _kingdomIds.Count;

        public void Clear()
        {
            _kingdomIds.Clear();
            _worldDay = -1L;
        }
    }

    public sealed class ArmySupplyNetworkIndex
    {
        private readonly HashSet<(long KingdomId, long WarId, long CityId)>
            _corridorCities =
                new HashSet<(long KingdomId, long WarId, long CityId)>();

        public void SetCorridorCity(long kingdomId, long warId,
            long cityId, bool connected)
        {
            if (kingdomId < 0L || warId < 0L || cityId < 0L) return;
            var key = (kingdomId, warId, cityId);
            if (connected)
                _corridorCities.Add(key);
            else
                _corridorCities.Remove(key);
        }

        public void RegisterCorridorCity(long kingdomId, long warId,
            long cityId)
        {
            SetCorridorCity(kingdomId, warId, cityId, connected: true);
        }

        public bool IsCorridorCity(long kingdomId, long warId, long cityId)
        {
            return _corridorCities.Contains((kingdomId, warId, cityId));
        }

        public void RemoveWar(long warId)
        {
            var removed = new List<(long KingdomId, long WarId, long CityId)>();
            foreach (var key in _corridorCities)
                if (key.WarId == warId) removed.Add(key);
            for (int i = 0; i < removed.Count; i++)
                _corridorCities.Remove(removed[i]);
        }

        public void RemoveCity(long cityId)
        {
            var removed = new List<(long KingdomId, long WarId, long CityId)>();
            foreach (var key in _corridorCities)
                if (key.CityId == cityId) removed.Add(key);
            for (int i = 0; i < removed.Count; i++)
                _corridorCities.Remove(removed[i]);
        }

        public void Clear()
        {
            _corridorCities.Clear();
        }
    }

#if !AW3_RULES_TESTS
    internal readonly struct ArmyOperationalStateView
    {
        internal ArmyOperationalStateView(int pSupply, int pOrganization,
            bool pConnectedSupply, bool pInCorridor)
        {
            Supply = pSupply;
            Organization = pOrganization;
            ConnectedSupply = pConnectedSupply;
            InCorridor = pInCorridor;
        }

        internal int Supply { get; }
        internal int Organization { get; }
        internal bool ConnectedSupply { get; }
        internal bool InCorridor { get; }
    }

    internal static class ArmyLogisticsService
    {
        private static readonly ArmyLogisticsRuntimeIndex EventIndex =
            new ArmyLogisticsRuntimeIndex();
        private static readonly ArmyLogisticsActivityIndex ActivityIndex =
            new ArmyLogisticsActivityIndex();
        private static readonly ArmyOperationalStateIndex OperationalIndex =
            new ArmyOperationalStateIndex();
        private static readonly ArmyStrategicDayScheduler DayScheduler =
            new ArmyStrategicDayScheduler();
        private static readonly ArmySupplyNetworkIndex SupplyNetwork =
            new ArmySupplyNetworkIndex();
        private static ArmyStrategicIdCursor _armyCursor;
        private static long _cursorKingdomId = -1L;

        public static void OnMissionAssigned(Army pArmy,
            ArmyRtsMission pMission,
            bool pConnectedSupply, bool pInCorridor)
        {
            if (pArmy?.data == null || pMission == null) return;
            Kingdom kingdom = SafeKingdom(pArmy);
            long kingdomId = kingdom?.id ?? pMission.KingdomId;
            ActivityIndex.AssignMission(pArmy.id, kingdomId,
                pMission.WarId);
            OperationalIndex.AssignMission(pArmy.id, kingdomId,
                pConnectedSupply, pInCorridor, pMission.WarId);
        }

        public static void OnMissionInvalidated(long pArmyId)
        {
            ActivityIndex.InvalidateMission(pArmyId);
            OperationalIndex.InvalidateMission(pArmyId);
        }

        public static void OnArmyStateChanged(Army pArmy,
            ArmyRtsState pState)
        {
            if (pArmy?.data == null) return;
            OperationalIndex.SetState(pArmy.id, pState);
        }

        public static ArmyOperationalStateView GetOperationalState(
            Army pArmy)
        {
            if (pArmy?.data == null)
                return new ArmyOperationalStateView(100, 100, false,
                    true);
            ArmyOperationalStateSnapshot state = OperationalIndex.
                GetOrCreateSnapshot(pArmy.id);
            return new ArmyOperationalStateView(
                ArmyLogisticsRules.EffectiveSupply(state.Supply),
                state.Organization,
                ArmyLogisticsRules.EffectiveSupplyConnection(
                    state.ConnectedSupply),
                ArmyLogisticsRules.EffectiveSupplyConnection(
                    state.InCorridor));
        }

        public static void OnActorDying(Actor pActor)
        {
            // ArmyRetreatService owns the vanilla roster-loss baseline and
            // casualty counter. Do not enqueue a second logistics event.
            return;
        }

        public static void OnArmyDisposed(Army pArmy)
        {
            if (pArmy == null) return;
            long pArmyId = pArmy.id;
            ActivityIndex.RemoveArmy(pArmyId);
            OperationalIndex.RemoveArmy(pArmyId);
            EventIndex.Remove(pArmyId);
        }

        public static void OnWarStarted(War pWar)
        {
            if (!ZhuluWarService.ShouldEnrollInAw3Systems(pWar)) return;
            ActivityIndex.RegisterWar(pWar.data.id,
                KingdomIds(pWar.getAttackers()),
                KingdomIds(pWar.getDefenders()));
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            IReadOnlyList<long> expired =
                ActivityIndex.EndWar(pWar.data.id);
            for (int i = 0; i < expired.Count; i++)
                OperationalIndex.InvalidateMission(expired[i]);
            SupplyNetwork.RemoveWar(pWar.data.id);
        }

        public static void OnWarParticipantJoined(War pWar,
            Kingdom pKingdom, bool pAttacker)
        {
            if (pWar?.data == null || pKingdom?.data == null) return;
            ActivityIndex.JoinWar(pWar.data.id, pKingdom.id, pAttacker);
            SupplyNetwork.RemoveWar(pWar.data.id);
        }

        public static void OnWarParticipantLeft(War pWar,
            Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom?.data == null) return;
            ActivityIndex.LeaveWar(pWar.data.id, pKingdom.id);
            SupplyNetwork.RemoveWar(pWar.data.id);
        }

        public static void OnFrontCityObserved(long pKingdomId,
            long pWarId, long pCityId, bool pConnectedCorridor)
        {
            SupplyNetwork.SetCorridorCity(pKingdomId, pWarId, pCityId,
                pConnectedCorridor);
        }

        public static void OnCityControlChanged(City pCity)
        {
            if (pCity?.data == null) return;
            SupplyNetwork.RemoveCity(pCity.id);
        }

        public static void ProcessFrame()
        {
            // Supply, organization and movement logistics are no longer
            // simulated. Retreated armies are evaluated from vanilla Army
            // membership/loss observations in ArmyRetreatService.
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.wars == null) return;
            foreach (War war in World.world.wars)
            {
                try
                {
                    OnWarStarted(war);
                }
                catch { }
            }
        }

        public static void ClearRuntime()
        {
            EventIndex.Clear();
            ActivityIndex.Clear();
            OperationalIndex.Clear();
            DayScheduler.Clear();
            SupplyNetwork.Clear();
            _armyCursor = null;
            _cursorKingdomId = -1L;
        }

        private static void BeginNextKingdom()
        {
            int scanLimit = RuntimePerformanceBudgetRules.
                ResolveLogisticsKingdomScansPerFrame(
                    DayScheduler.PendingCount);
            for (int scan = 0; scan < scanLimit &&
                 DayScheduler.HasPending; scan++)
            {
                long kingdomId = DayScheduler.TakeKingdom();
                Kingdom kingdom = FindKingdom(kingdomId);
                if (kingdom?.data == null) continue;
                _cursorKingdomId = kingdomId;
                _armyCursor = ArmyStrategicIndexService.CreateSnapshotCursor(
                    kingdom);
                if (!_armyCursor.IsComplete) return;
                _armyCursor = null;
                _cursorKingdomId = -1L;
            }
        }

        private static void UpdateArmy(long pArmyId, long pKingdomId)
        {
            Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                pArmyId, pKingdomId);
            if (army?.data == null)
            {
                OperationalIndex.RemoveArmy(pArmyId);
                EventIndex.Remove(pArmyId);
                return;
            }
            if (!OperationalIndex.TryGet(pArmyId,
                    out ArmyOperationalStateSnapshot state) ||
                !state.MissionActive) return;
            bool nearbySupport = false;
            bool strategicMovementProgressed = false;
            if (ArmyRtsControllerService.TryGetLogisticsSample(pArmyId,
                    out ArmyLogisticsControllerSample sample))
            {
                strategicMovementProgressed =
                    OperationalIndex.ObserveCaptainPosition(pArmyId,
                        sample.CurrentTileId);
                bool corridorCity = SupplyNetwork.IsCorridorCity(
                    state.KingdomId, state.WarId, sample.CurrentCityId);
                bool allied = ActivityIndex.AreOnSameWarSide(state.WarId,
                    state.KingdomId, sample.CurrentCityKingdomId);
                bool frozen = IsFrozenControlledSupplyCity(state.WarId,
                    sample.CurrentCityId, state.KingdomId);
                bool friendly = sample.CurrentCityKingdomId ==
                                state.KingdomId;
                ArmyConnectivityResult connectivity =
                    ArmyLogisticsRules.ResolveConnectivity(
                        new ArmyConnectivityFacts
                        {
                            MissionConnected = state.MissionConnected,
                            CurrentCityInCorridor = corridorCity,
                            NearRouteAnchor = sample.NearRouteAnchor,
                            FriendlySupplyCity = friendly && sample.CurrentCitySafe &&
                                !frozen,
                            AlliedSupplyCity = ArmyLogisticsRules.IsAlliedSupplyCity(allied, sample.CurrentCitySafe),
                            FrozenControlledSupplyCity = frozen
                        });
                OperationalIndex.SetConnectivity(pArmyId,
                    connectivity.ConnectedSupply,
                    connectivity.InCorridor);
                state = OperationalIndex.GetOrCreateSnapshot(pArmyId);
                nearbySupport = ArmyLogisticsRules.
                    HasMinimumOperationalForce(sample.Rallied);
            }
            ArmyLogisticsEventSnapshot events =
                EventIndex.ConsumeCasualties(pArmyId);
            int supply = ArmyLogisticsRules.EffectiveSupply(
                ArmyLogisticsRules.UpdateSupply(state.Supply,
                    state.State, state.ConnectedSupply, state.InCorridor,
                    strategicMovementProgressed));
            int organization = ArmyLogisticsRules.UpdateOrganization(
                new ArmyOrganizationFacts
                {
                    CurrentOrganization = state.Organization,
                    RecentCasualties = events.Casualties,
                    CaptainLost = events.CaptainLost,
                    Supply = supply,
                    Regrouping = state.State == ArmyRtsState.Regroup,
                    NearbySupport = nearbySupport,
                    UninterruptedMarch = state.State == ArmyRtsState.March &&
                                           events.Casualties == 0
                });
            OperationalIndex.SetValues(pArmyId, supply, organization,
                state.State);
        }

        public static bool IsTileInMissionCorridor(Army pArmy,
            WorldTile pTile)
        {
            if (pArmy?.data == null || pTile == null) return false;
            if (!ArmyLogisticsRules.SupplySimulationEnabled) return true;
            if (!OperationalIndex.TryGet(pArmy.id,
                    out ArmyOperationalStateSnapshot state) ||
                !state.MissionActive) return false;
            City city = pTile.zone?.city;
            return city?.data != null &&
                   SupplyNetwork.IsCorridorCity(state.KingdomId,
                       state.WarId, city.id);
        }

        private static bool IsFrozenControlledSupplyCity(long pWarId,
            long pCityId, long pKingdomId)
        {
            return pWarId >= 0L && pCityId >= 0L && pKingdomId >= 0L &&
                   WarScoreService.TryGetFrozenOccupation(pWarId, pCityId,
                       out long controllerId) &&
                   controllerId == pKingdomId;
        }

        private static IReadOnlyList<long> KingdomIds(
            IEnumerable<Kingdom> pKingdoms)
        {
            var result = new List<long>();
            if (pKingdoms == null) return result;
            foreach (Kingdom kingdom in pKingdoms)
                if (kingdom?.data != null) result.Add(kingdom.id);
            return result;
        }

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return pArmy?.getKingdom(); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static long CurrentLogisticsPeriod()
        {
            try
            {
                return ArmyLogisticsRules.LogisticsPeriodForWorldTime(
                    World.world?.getCurWorldTime() ?? 0d);
            }
            catch { return 0L; }
        }
    }
#endif
}
