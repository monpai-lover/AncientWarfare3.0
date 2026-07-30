using System;
using System.Collections.Generic;
using System.Reflection;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.performance;
using life.taxi;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct ArmyRtsTransportEstimate
    {
        internal ArmyRtsTransportEstimate(float pPickupCost,
            float pQueueCost, float pSeaCost, float pLandingCost)
        {
            PickupCost = pPickupCost;
            QueueCost = pQueueCost;
            SeaCost = pSeaCost;
            LandingCost = pLandingCost;
        }

        internal float PickupCost { get; }
        internal float QueueCost { get; }
        internal float SeaCost { get; }
        internal float LandingCost { get; }
    }

    internal static class ArmyRtsTransportService
    {
        private const int MaximumBoatScansPerStateFrame = 128;
        private const int MaximumCityScansPerStateFrame = 64;
        private const double BoatWakeIntervalSeconds = 1d;
        private const float ExistingBoatQueueCost = 8f;
        private const float ProductionQueueCost = 48f;
        private const float BoatTravelCostPerTile = 0.6f;
        private const float BoatPickupCostPerTile = 0.25f;
        private static readonly FieldInfo CityBoatsField = typeof(City).
            GetField("_boats", BindingFlags.Instance |
                               BindingFlags.NonPublic);

        private sealed class TransportState
        {
            internal Army Army;
            internal int TargetTileId;
            internal double LastProgressRealtime;
            internal bool HadAssignedBoat;
            internal bool HadEmbarkedMember;
            internal bool HadLandedMember;
            internal int PreEmbarkTimeouts;
            internal bool LoggedBoatWake;
            internal bool LoggedNoBoat;
            internal bool LoggedNoRequest;
            internal bool LoggedBoatBindFailure;
            internal bool LoggedDirectorOmissionPreserved;
            internal bool HasMovementMarker;
            internal int LastMovementTileId = -1;
            internal int BoatCityScanCursor;
            internal int BoatIndexInCity;
            internal int BoatCitiesInspected;
            internal Actor CombatBoatFallback;
            internal double NextBoatWakeRealtime;
            internal readonly Dictionary<long, Actor> Members =
                new Dictionary<long, Actor>();
            internal readonly List<long> InvalidMemberIds =
                new List<long>();
        }

        private static readonly Dictionary<long, TransportState> States =
            new Dictionary<long, TransportState>();
        private static readonly List<long> TransportStateIds =
            new List<long>();
        private static readonly HashSet<long> AssignedBoatIds =
            new HashSet<long>();
        private static readonly ArmyRtsTransportActiveClock ActiveClock =
            new ArmyRtsTransportActiveClock();

        public static void ObserveFrameClock(double pRealtime, bool pPaused)
        {
            ActiveClock.Observe(pRealtime, pPaused);
        }

        public static bool TryGetTarget(Army pArmy, out WorldTile pTarget)
        {
            pTarget = null;
            if (pArmy?.data == null ||
                !States.TryGetValue(pArmy.id, out TransportState state))
                return false;
            pTarget = FindTile(state.TargetTileId);
            return pTarget?.data != null;
        }

        public static bool HasEmbarkedMembers(Army pArmy)
        {
            if (pArmy?.data == null ||
                !States.TryGetValue(pArmy.id, out TransportState state))
                return false;
            foreach (Actor member in state.Members.Values)
            {
                try
                {
                    if (IsValidMember(member, pArmy) &&
                        member.is_inside_boat) return true;
                }
                catch { }
            }
            return false;
        }

        public static bool HasActiveVoyage(Army pArmy)
        {
            if (pArmy?.data == null ||
                !States.TryGetValue(pArmy.id, out TransportState state) ||
                state.Members.Count == 0) return false;
            return FindTile(state.TargetTileId)?.data != null;
        }

        internal static ArmyRtsTransportPhase GetPhase(Army pArmy)
        {
            if (pArmy?.data == null ||
                !States.TryGetValue(pArmy.id, out TransportState state) ||
                state.Members.Count == 0)
                return ArmyRtsTransportPhase.None;
            bool anyEmbarked = false;
            foreach (Actor member in state.Members.Values)
            {
                try
                {
                    if (IsValidMember(member, pArmy) &&
                        member.is_inside_boat)
                    {
                        anyEmbarked = true;
                        break;
                    }
                }
                catch { }
            }
            return ArmyRtsTransportRules.ResolvePhase(
                voyageActive: FindTile(state.TargetTileId)?.data != null,
                hasAssignedBoat: state.HadAssignedBoat,
                anyEmbarked: anyEmbarked,
                anyLanded: state.HadLandedMember);
        }

        internal static bool TryGetRouteEstimate(Army pArmy,
            WorldTile pTarget, out ArmyRtsTransportEstimate pEstimate)
        {
            pEstimate = default;
            Actor captain = null;
            Kingdom kingdom = null;
            try
            {
                captain = pArmy?.getCaptain();
                kingdom = pArmy?.getKingdom();
            }
            catch { }
            WorldTile pickup = captain?.current_tile;
            if (pArmy?.data == null || kingdom?.data == null ||
                pickup?.data == null || pTarget?.data == null) return false;
            float seaCost = Math.Max(1f, TileDistance(pickup, pTarget)) *
                            BoatTravelCostPerTile;
            Actor boat = FindCompatibleRouteBoat(kingdom, pickup, pTarget);
            if (boat?.current_tile?.data != null)
            {
                pEstimate = new ArmyRtsTransportEstimate(
                    pPickupCost: 0f,
                    pQueueCost: ExistingBoatQueueCost +
                        TileDistance(boat.current_tile, pickup) *
                        BoatPickupCostPerTile,
                    pSeaCost: seaCost,
                    pLandingCost: 0f);
                return true;
            }
            if (!ArmyRtsTransportProductionService.CanProvisionRoute(
                    kingdom, pickup, pTarget)) return false;
            pEstimate = new ArmyRtsTransportEstimate(
                pPickupCost: 0f, pQueueCost: ProductionQueueCost,
                pSeaCost: seaCost, pLandingCost: 0f);
            return true;
        }

        public static bool OwnsActorTask(Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                !States.TryGetValue(army.id, out TransportState state))
                return false;
            return state.Members.TryGetValue(pActor.data.id,
                       out Actor member) &&
                   ReferenceEquals(member, pActor);
        }

        public static void LogDirectorOmissionPreserved(Army pArmy)
        {
            if (pArmy?.data == null ||
                !States.TryGetValue(pArmy.id, out TransportState state) ||
                state.LoggedDirectorOmissionPreserved) return;
            state.LoggedDirectorOmissionPreserved = true;
            LogPhase(state, "director_omission_preserved");
        }

        public static bool TryHandleActor(Actor pActor, WorldTile pTarget,
            bool pMayBegin, bool pForceTransport = false)
        {
            Army army = pActor?.army;
            bool actorValid = IsValidMember(pActor, army);
            bool targetValid = pTarget?.data != null;
            bool authoritative = ArmyRtsRuntimeMode.ShouldCommit &&
                                 !AW3MultiplayerReplicaScope.IsReplicaSession;
            if (!authoritative || !actorValid || !targetValid) return false;

            if (!States.TryGetValue(army.id, out TransportState state))
            {
                bool owns = ArmyRtsTransportRules.ShouldOwnActor(
                    authoritative, actorValid, targetValid,
                    pActor.is_inside_boat,
                    SameIsland(pActor.current_tile, pTarget),
                    forceTransport: pForceTransport);
                if (!pMayBegin || !owns) return false;
                state = Begin(army, pTarget, pForceTransport);
                OpenRequests(state, pTarget);
            }
            else
            {
                WorldTile activeTarget = FindTile(state.TargetTileId);
                bool activeTargetLive = activeTarget?.data != null;
                bool activeTargetMatches = SameTile(activeTarget, pTarget);
                bool callerIsCaptain = IsCaptain(pActor, army);
                bool hasEmbarkedMembers = HasEmbarkedMembers(army);
                if (ArmyRtsTransportRules.ShouldReplaceActiveVoyageTarget(
                        activeTargetMatches, pMayBegin, callerIsCaptain,
                        pForceTransport, hasEmbarkedMembers))
                {
                    ReleaseArmy(army);
                    state = Begin(army, pTarget, pForceTransport);
                    OpenRequests(state, pTarget);
                }
                else if (activeTargetLive)
                {
                    // A returning member can request a personal boat while
                    // its army has an active voyage. It must not retarget it.
                    if (!activeTargetMatches && pMayBegin)
                        return false;
                    pTarget = activeTarget;
                }
            }

            bool sameActiveIsland = SameIsland(pActor.current_tile,
                pTarget);
            bool expectedMember = state.Members.ContainsKey(
                pActor.data.id);
            if (!expectedMember && sameActiveIsland) return false;
            state.Members[pActor.data.id] = pActor;
            ArmyRtsTransportExpectedMemberAction memberAction =
                ArmyRtsTransportRules.ResolveExpectedMemberAction(
                    memberValid: true,
                    landedOnTargetIsland: sameActiveIsland &&
                        !pActor.is_inside_boat);
            if (memberAction ==
                ArmyRtsTransportExpectedMemberAction.HoldLanded)
            {
                HoldLandedMember(pActor);
                return true;
            }
            EnsureRequest(pActor, pTarget);
            return true;
        }

        public static void ProcessFrame()
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                AW3MultiplayerReplicaScope.IsReplicaSession ||
                World.world == null || World.world.isPaused()) return;
            if (!ArmyRtsTransportRules.ShouldProcessFrame(States.Count))
                return;
            double now = CurrentRealtime();
            TransportStateIds.Clear();
            foreach (long armyId in States.Keys)
                TransportStateIds.Add(armyId);
            HashSet<long> unavailableBoatIds =
                CollectAssignedBoatIds();
            for (int i = 0; i < TransportStateIds.Count; i++)
            {
                long armyId = TransportStateIds[i];
                if (!States.TryGetValue(armyId,
                        out TransportState state)) continue;
                Army army = state.Army;
                if (army?.data == null)
                {
                    States.Remove(armyId);
                    AWArmyMarchService.OnTransportCancelled(army);
                    continue;
                }
                WorldTile target = FindTile(state.TargetTileId);
                if (target?.data == null)
                {
                    ReleaseArmy(army);
                    continue;
                }
                bool hasAssignedBoat = false;
                bool hasEmbarkedMember = false;
                long assignedBoatId = -1L;
                List<long> invalidMemberIds = state.InvalidMemberIds;
                invalidMemberIds.Clear();
                int validExpectedMembers = 0;
                int landedExpectedMembers = 0;
                long markerActorId = long.MaxValue;
                int movementTileId = -1;
                foreach (KeyValuePair<long, Actor> pair in state.Members)
                {
                    Actor member = pair.Value;
                    bool validMember = IsValidMember(member, army);
                    bool landed = validMember &&
                                  member.is_inside_boat == false &&
                                  SameIsland(member.current_tile, target);
                    ArmyRtsTransportExpectedMemberAction memberAction =
                        ArmyRtsTransportRules.ResolveExpectedMemberAction(
                            validMember, landed);
                    if (memberAction ==
                        ArmyRtsTransportExpectedMemberAction.RemoveInvalid)
                    {
                        ReleaseRequest(member);
                        invalidMemberIds.Add(pair.Key);
                        continue;
                    }
                    validExpectedMembers++;
                    if (memberAction ==
                        ArmyRtsTransportExpectedMemberAction.HoldLanded)
                    {
                        landedExpectedMembers++;
                        state.HadLandedMember = true;
                        HoldLandedMember(member);
                        continue;
                    }
                    if (member?.is_inside_boat == true)
                    {
                        hasAssignedBoat = true;
                        hasEmbarkedMember = true;
                        CaptureMovementMarker(member,
                            SafeInsideBoatTileId(member),
                            ref markerActorId, ref movementTileId);
                        continue;
                    }
                    TaxiRequest request = SafeRequest(member);
                    try
                    {
                        if (request?.hasAssignedBoat() == true)
                        {
                            hasAssignedBoat = true;
                            if (assignedBoatId < 0)
                                assignedBoatId = SafeAssignedBoatId(request);
                            CaptureMovementMarker(member,
                                SafeAssignedBoatTileId(request),
                                ref markerActorId, ref movementTileId);
                        }
                    }
                    catch { }
                }
                for (int memberIndex = 0;
                     memberIndex < invalidMemberIds.Count; memberIndex++)
                    state.Members.Remove(
                        invalidMemberIds[memberIndex]);
                ArmyRtsTransportVoyageAction voyageAction =
                    ArmyRtsTransportRules.ResolveVoyageAction(
                        validExpectedMembers, landedExpectedMembers,
                        timedOut: false);
                if (voyageAction ==
                    ArmyRtsTransportVoyageAction.Complete)
                {
                    CompleteVoyage(armyId, state);
                    continue;
                }
                if (hasAssignedBoat && !state.HadAssignedBoat)
                {
                    state.HadAssignedBoat = true;
                    state.LastProgressRealtime = now;
                    LogPhase(state, "assigned",
                        " boat=" + assignedBoatId);
                }
                if (hasEmbarkedMember && !state.HadEmbarkedMember)
                {
                    state.HadEmbarkedMember = true;
                    LogPhase(state, "embarked");
                }
                if (movementTileId >= 0)
                {
                    if (!state.HasMovementMarker ||
                        state.LastMovementTileId != movementTileId)
                    {
                        state.HasMovementMarker = true;
                        state.LastMovementTileId = movementTileId;
                        state.LastProgressRealtime = now;
                    }
                }
                bool timedOut = ArmyRtsTransportRules.
                    TransportWaitTimedOut(state.LastProgressRealtime, now,
                        hasAssignedBoat);
                voyageAction = ArmyRtsTransportRules.ResolveVoyageAction(
                    validExpectedMembers, landedExpectedMembers, timedOut);
                if (voyageAction == ArmyRtsTransportVoyageAction.Retry)
                {
                    LogPhase(state, "timeout",
                        " assigned=" + hasAssignedBoat);
                    if (state.PreEmbarkTimeouts < int.MaxValue)
                        state.PreEmbarkTimeouts++;
                    bool voyageHasProgressed =
                        state.HadEmbarkedMember ||
                        state.HadLandedMember || hasEmbarkedMember;
                    if (ArmyRtsTransportRules.
                            ShouldEscalatePreEmbarkTimeout(
                                state.PreEmbarkTimeouts,
                                voyageHasProgressed))
                    {
                        LogPhase(state, "pre_embark_timeout_exhausted",
                            " attempts=" + state.PreEmbarkTimeouts);
                        ReleaseArmy(army);
                        ArmyStallWatchdogService.OnRouteFailed(army.id,
                            pAllowTransportEscalation: false);
                        continue;
                    }
                    RetryVoyage(state, target, now);
                    TryWakeTransportBoat(state, now, unavailableBoatIds);
                    continue;
                }
                TryWakeTransportBoat(state, now, unavailableBoatIds);
            }
        }

        private static void RetryVoyage(TransportState pState,
            WorldTile pTarget, double pNow)
        {
            if (pState == null || pTarget?.data == null) return;
            var waitingMembers = new List<Actor>();
            foreach (Actor member in pState.Members.Values)
            {
                bool validMember = IsValidMember(member, pState.Army);
                bool landed = validMember && !member.is_inside_boat &&
                              SameIsland(member.current_tile, pTarget);
                if (ArmyRtsTransportRules.ResolveExpectedMemberAction(
                        validMember, landed) ==
                    ArmyRtsTransportExpectedMemberAction.AwaitTransport &&
                    member?.is_inside_boat == false)
                    waitingMembers.Add(member);
            }
            for (int i = 0; i < waitingMembers.Count; i++)
            {
                Actor member = waitingMembers[i];
                ReleaseRequest(member);
            }
            for (int i = 0; i < waitingMembers.Count; i++)
            {
                Actor member = waitingMembers[i];
                EnsureRequest(member, pTarget);
            }
            pState.LastProgressRealtime = pNow;
            pState.HadAssignedBoat = false;
            pState.HasMovementMarker = false;
            pState.LastMovementTileId = -1;
            pState.NextBoatWakeRealtime = pNow;
            pState.LoggedBoatWake = false;
            pState.LoggedNoBoat = false;
            pState.LoggedNoRequest = false;
        }

        private static void CompleteVoyage(long pArmyId,
            TransportState pState)
        {
            if (pState == null ||
                !States.TryGetValue(pArmyId, out TransportState current) ||
                !ReferenceEquals(current, pState)) return;
            if (pState.HadLandedMember) LogPhase(pState, "landed");
            if (!States.Remove(pArmyId)) return;
            AWArmyMarchService.OnTransportCompleted(pState.Army);
            ArmyRtsControllerService.OnTransportCompleted(pState.Army);
        }

        private static void TryWakeTransportBoat(TransportState pState,
            double pNow, HashSet<long> pUnavailableBoatIds)
        {
            if (pState?.Army?.data == null ||
                pNow < pState.NextBoatWakeRealtime) return;
            pState.NextBoatWakeRealtime = pNow +
                                          BoatWakeIntervalSeconds;
            Kingdom kingdom;
            try { kingdom = pState.Army.getKingdom(); }
            catch { kingdom = null; }
            TaxiRequest pendingRequest = FindPendingRequest(pState);
            if (pendingRequest == null)
            {
                if (!pState.LoggedNoRequest)
                {
                    pState.LoggedNoRequest = true;
                    LogPhase(pState, "no_request",
                        " members=" + pState.Members.Count);
                }
                return;
            }
            List<City> cities = kingdom?.cities;
            int cityCount = cities?.Count ?? 0;
            if (cityCount == 0)
            {
                pState.BoatCityScanCursor = 0;
                pState.BoatIndexInCity = 0;
                LogNoBoatOnce(pState);
                return;
            }

            int cityCursor = NormalizeCursor(pState.BoatCityScanCursor,
                cityCount);
            int cityScans = 0;
            int boatScans = 0;
            while (cityScans < MaximumCityScansPerStateFrame &&
                   cityScans < cityCount &&
                   boatScans < MaximumBoatScansPerStateFrame)
            {
                City city;
                try { city = cities[cityCursor]; }
                catch { city = null; }
                List<Actor> boats = SafeCityBoats(city);
                int boatCount = boats?.Count ?? 0;
                int boatIndex = NormalizeCursor(pState.BoatIndexInCity,
                    boatCount);
                while (boatIndex < boatCount &&
                       boatScans < MaximumBoatScansPerStateFrame)
                {
                    Actor candidate;
                    try { candidate = boats[boatIndex]; }
                    catch { candidate = null; }
                    boatIndex++;
                    boatScans++;
                    int priority = BoatTransportPriority(candidate);
                    if (priority ==
                            ArmyRtsTransportRules.InvalidBoatPriority ||
                        !IsAvailableTransportBoat(candidate, kingdom,
                            pUnavailableBoatIds) ||
                        !CanServeTransportRoute(candidate,
                            pendingRequest))
                        continue;
                    if (priority == ArmyRtsTransportRules.
                            DedicatedTransportPriority)
                    {
                        if (!TryBindBoat(pState, candidate,
                                pendingRequest, pUnavailableBoatIds))
                            continue;
                        pState.CombatBoatFallback = null;
                        StoreBoatCursor(pState, cityCursor, boatIndex,
                            boatCount, cityCount);
                        pState.BoatCitiesInspected = 0;
                        return;
                    }
                    if (pState.CombatBoatFallback == null)
                        pState.CombatBoatFallback = candidate;
                }

                if (boatIndex < boatCount)
                {
                    pState.BoatCityScanCursor = cityCursor;
                    pState.BoatIndexInCity = boatIndex;
                    return;
                }

                cityCursor = (cityCursor + 1) % cityCount;
                pState.BoatCityScanCursor = cityCursor;
                pState.BoatIndexInCity = 0;
                cityScans++;
                pState.BoatCitiesInspected++;
            }
            if (pState.BoatCitiesInspected < cityCount) return;

            Actor fallback = pState.CombatBoatFallback;
            pState.CombatBoatFallback = null;
            pState.BoatCitiesInspected = 0;
            if (fallback != null &&
                IsAvailableTransportBoat(fallback, kingdom,
                    pUnavailableBoatIds) &&
                CanServeTransportRoute(fallback, pendingRequest) &&
                TryBindBoat(pState, fallback, pendingRequest,
                    pUnavailableBoatIds))
                return;
            ArmyRtsTransportProductionService.Request(kingdom,
                pendingRequest);
            LogNoBoatOnce(pState);
        }

        private static bool TryBindBoat(TransportState pState,
            Actor pCandidate, TaxiRequest pRequest,
            HashSet<long> pUnavailableBoatIds)
        {
            Boat boat = null;
            try
            {
                boat = pCandidate.getSimpleComponent<Boat>();
                boat.taxi_request = pRequest;
                pRequest.assign(boat);
                ArmyRtsTransportProductionService.OnAssigned(pRequest);
                pCandidate.setTask("boat_transport_go_load",
                    pClean: true, pCleanJob: false,
                    pForceAction: true);
                pUnavailableBoatIds?.Add(pCandidate.data.id);
                if (!pState.LoggedBoatWake)
                {
                    pState.LoggedBoatWake = true;
                    LogPhase(pState, "boat_wake",
                        " boat=" + pCandidate.data.id);
                }
                return true;
            }
            catch (Exception error)
            {
                try
                {
                    if (boat != null && ReferenceEquals(
                            boat.taxi_request, pRequest))
                        boat.taxi_request = null;
                }
                catch { }
                LogBoatBindFailureOnce(pState, pCandidate, error);
                return false;
            }
        }

        private static int BoatTransportPriority(Actor pActor)
        {
            try
            {
                return ArmyRtsTransportRules.BoatTransportPriority(
                    pActor?.asset?.is_boat == true,
                    pActor?.asset?.is_boat_transport == true,
                    pActor?.asset?.skip_fight_logic == true);
            }
            catch
            {
                return ArmyRtsTransportRules.InvalidBoatPriority;
            }
        }

        private static void StoreBoatCursor(TransportState pState,
            int pCityIndex, int pNextBoatIndex, int pBoatCount,
            int pCityCount)
        {
            if (pState == null || pCityCount <= 0) return;
            if (pNextBoatIndex < pBoatCount)
            {
                pState.BoatCityScanCursor = pCityIndex;
                pState.BoatIndexInCity = pNextBoatIndex;
                return;
            }
            pState.BoatCityScanCursor = (pCityIndex + 1) % pCityCount;
            pState.BoatIndexInCity = 0;
        }

        private static List<Actor> SafeCityBoats(City pCity)
        {
            if (pCity?.data == null || CityBoatsField == null) return null;
            try { return CityBoatsField.GetValue(pCity) as List<Actor>; }
            catch { return null; }
        }

        private static HashSet<long> CollectAssignedBoatIds()
        {
            AssignedBoatIds.Clear();
            try
            {
                for (int i = 0; i < TaxiManager.list.Count; i++)
                {
                    TaxiRequest request = TaxiManager.list[i];
                    if (request?.hasAssignedBoat() != true) continue;
                    Actor boat = request.getBoat()?.actor;
                    if (boat?.data != null) AssignedBoatIds.Add(boat.data.id);
                }
            }
            catch { }
            return AssignedBoatIds;
        }

        private static bool IsAvailableTransportBoat(Actor pActor,
            Kingdom pKingdom, HashSet<long> pUnavailableBoatIds)
        {
            try
            {
                Boat boat = pActor?.getSimpleComponent<Boat>();
                return pActor?.data != null && pActor.asset != null &&
                       pActor.kingdom == pKingdom && pActor.isAlive() &&
                       !pActor.isRekt() && pActor.current_tile?.data != null &&
                       boat != null && boat.taxi_request == null &&
                       !boat.hasPassengers() &&
                       (pUnavailableBoatIds == null ||
                        !pUnavailableBoatIds.Contains(pActor.data.id));
            }
            catch { return false; }
        }

        private static TaxiRequest FindPendingRequest(
            TransportState pState)
        {
            if (pState == null) return null;
            WorldTile target = FindTile(pState.TargetTileId);
            TaxiRequest best = null;
            int bestCount = -1;
            foreach (Actor member in pState.Members.Values)
            {
                if (!IsValidMember(member, pState.Army)) continue;
                TaxiRequest request = SafeRequest(member);
                if (request == null ||
                    !request.isState(TaxiRequestState.Pending) ||
                    request.hasAssignedBoat() ||
                    !SameTile(SafeRequestTarget(request), target))
                    continue;
                int count;
                try { count = request.countActors(); }
                catch { count = 0; }
                if (count <= bestCount) continue;
                best = request;
                bestCount = count;
            }
            return best;
        }

        private static bool CanServeTransportRoute(Actor pBoat,
            TaxiRequest pRequest)
        {
            if (pBoat?.current_tile?.data == null || pRequest == null)
                return false;
            WorldTile pickup;
            WorldTile target;
            try
            {
                pickup = pRequest.getTileStart();
                target = pRequest.getTileTarget();
            }
            catch { return false; }
            return CanServeTransportRoute(pBoat, pickup, target);
        }

        private static bool CanServeTransportRoute(Actor pBoat,
            WorldTile pPickup, WorldTile pTarget)
        {
            return pBoat?.current_tile?.data != null &&
                   pPickup?.data != null && pTarget?.data != null &&
                   IsCoastConnectedToBoatOcean(pPickup,
                       pBoat.current_tile) &&
                   IsCoastConnectedToBoatOcean(pTarget,
                       pBoat.current_tile);
        }

        private static Actor FindCompatibleRouteBoat(Kingdom pKingdom,
            WorldTile pPickup, WorldTile pTarget)
        {
            List<City> cities = pKingdom?.cities;
            int cityCount = Math.Min(cities?.Count ?? 0,
                MaximumCityScansPerStateFrame);
            int boatScans = 0;
            for (int cityIndex = 0;
                 cityIndex < cityCount &&
                 boatScans < MaximumBoatScansPerStateFrame;
                 cityIndex++)
            {
                City city;
                try { city = cities[cityIndex]; }
                catch { continue; }
                List<Actor> boats = SafeCityBoats(city);
                int boatCount = boats?.Count ?? 0;
                for (int boatIndex = 0;
                     boatIndex < boatCount &&
                     boatScans < MaximumBoatScansPerStateFrame;
                     boatIndex++)
                {
                    Actor candidate;
                    try { candidate = boats[boatIndex]; }
                    catch { continue; }
                    boatScans++;
                    if (BoatTransportPriority(candidate) ==
                            ArmyRtsTransportRules.InvalidBoatPriority ||
                        !IsAvailableTransportBoat(candidate, pKingdom,
                            pUnavailableBoatIds: null) ||
                        !CanServeTransportRoute(candidate, pPickup,
                            pTarget)) continue;
                    return candidate;
                }
            }
            return null;
        }

        private static float TileDistance(WorldTile pFirst,
            WorldTile pSecond)
        {
            if (pFirst == null || pSecond == null) return 0f;
            float deltaX = pFirst.x - pSecond.x;
            float deltaY = pFirst.y - pSecond.y;
            return (float)Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        private static bool IsCoastConnectedToBoatOcean(
            WorldTile pLandTile, WorldTile pBoatTile)
        {
            TileIsland landIsland = pLandTile?.region?.island;
            TileIsland boatOcean = pBoatTile?.region?.island;
            if (landIsland == null || boatOcean == null) return false;
            if (ReferenceEquals(landIsland, boatOcean)) return true;
            try
            {
                landIsland.calcNeighbourIslands();
                return landIsland.isConnectedWith(boatOcean);
            }
            catch { return false; }
        }

        private static int NormalizeCursor(int pCursor, int pCount)
        {
            if (pCount <= 0) return 0;
            int normalized = pCursor % pCount;
            return normalized < 0 ? normalized + pCount : normalized;
        }

        private static void LogNoBoatOnce(TransportState pState)
        {
            if (pState == null || pState.LoggedNoBoat) return;
            pState.LoggedNoBoat = true;
            LogPhase(pState, "no_boat");
        }

        private static void LogBoatBindFailureOnce(TransportState pState,
            Actor pBoat, Exception pError)
        {
            if (pState == null || pState.LoggedBoatBindFailure) return;
            pState.LoggedBoatBindFailure = true;
            string error = pError == null
                ? "unknown"
                : pError.GetType().Name + ":" + pError.Message;
            LogPhase(pState, "boat_bind_failed",
                " boat=" + (pBoat?.data?.id ?? -1L) +
                " error=" + error);
        }

        public static void ReleaseArmy(Army pArmy)
        {
            if (pArmy?.data == null) return;
            if (States.TryGetValue(pArmy.id, out TransportState state))
            {
                var members = new List<Actor>(state.Members.Values);
                for (int i = 0; i < members.Count; i++)
                    ReleaseRequest(members[i]);
                States.Remove(pArmy.id);
                AWArmyMarchService.OnTransportCancelled(pArmy);
                ArmyRtsControllerService.OnTransportCancelled(pArmy);
            }

            int count;
            try { count = pArmy.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                ReleaseRequest(actor);
            }
        }

        public static void Clear()
        {
            var states = new List<TransportState>(States.Values);
            for (int i = 0; i < states.Count; i++)
            {
                foreach (Actor actor in states[i].Members.Values)
                    ReleaseRequest(actor);
                AWArmyMarchService.OnTransportCancelled(states[i].Army);
                ArmyRtsControllerService.OnTransportCancelled(states[i].Army);
            }
            States.Clear();
            TransportStateIds.Clear();
            AssignedBoatIds.Clear();
            ActiveClock.Reset();
            ArmyRtsTransportProductionService.Clear();
        }

        private static TransportState Begin(Army pArmy, WorldTile pTarget,
            bool pForceTransport)
        {
            var state = new TransportState
            {
                Army = pArmy,
                TargetTileId = pTarget.data.tile_id,
                LastProgressRealtime = CurrentRealtime()
            };
            int count;
            try { count = pArmy.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor member;
                try { member = pArmy.units[i]; }
                catch { continue; }
                if (IsValidMember(member, pArmy) &&
                    (member.is_inside_boat ||
                     pForceTransport ||
                     !SameIsland(member.current_tile, pTarget)))
                    state.Members[member.data.id] = member;
            }
            Actor captain = null;
            try { captain = pArmy.getCaptain(); }
            catch { }
            if (IsValidMember(captain, pArmy) &&
                (captain.is_inside_boat ||
                 pForceTransport ||
                 !SameIsland(captain.current_tile, pTarget)))
                state.Members[captain.data.id] = captain;
            States[pArmy.id] = state;
            AWArmyMarchService.OnTransportStarted(pArmy);
            LogPhase(state, "mission_assigned",
                " captain_tile=" +
                (captain?.current_tile?.data?.tile_id ?? -1));
            return state;
        }

        private static void OpenRequests(TransportState pState,
            WorldTile pTarget)
        {
            if (pState == null || pTarget?.data == null) return;
            foreach (Actor member in pState.Members.Values)
                EnsureRequest(member, pTarget);
            LogPhase(pState, "requested",
                " members=" + pState.Members.Count);
        }

        private static void EnsureRequest(Actor pActor,
            WorldTile pTarget)
        {
            if (pActor?.data == null || pTarget?.data == null ||
                pActor.is_inside_boat) return;
            TaxiRequest request = SafeRequest(pActor);
            bool exactTarget = SameTile(SafeRequestTarget(request),
                pTarget);
            if (ArmyRtsTransportRules.ShouldReuseRequest(
                    request != null, exactTarget)) return;
            if (ArmyRtsTransportRules.ShouldRemoveFromRequest(
                    request != null, pActor.is_inside_boat,
                    exactTarget))
                RemoveActorFromRequest(request, pActor);

            try
            {
                TaxiManager.newRequest(pActor, pTarget);
                request = SafeRequest(pActor);
                if (SameTile(SafeRequestTarget(request), pTarget))
                    return;

                RemoveActorFromRequest(request, pActor);
                TaxiRequest exactRequest = FindExactReusableRequest(
                    pActor, pTarget);
                if (exactRequest != null)
                {
                    exactRequest.addActor(pActor);
                    return;
                }
                TaxiManager.list.Add(new TaxiRequest(pActor,
                    pActor.kingdom, pActor.current_tile, pTarget));
            }
            catch { }
        }

        private static void ReleaseRequest(Actor pActor)
        {
            if (pActor?.data == null || pActor.is_inside_boat) return;
            TaxiRequest request = SafeRequest(pActor);
            if (request == null) return;
            RemoveActorFromRequest(request, pActor);
        }

        private static void HoldLandedMember(Actor pActor)
        {
            if (pActor?.data == null) return;
            ReleaseRequest(pActor);
            try
            {
                pActor.clearTileTarget();
                pActor.beh_tile_target = null;
                pActor.beh_actor_target = null;
                pActor.setNotMoving();
                pActor.makeWait(0.2f);
            }
            catch { }
        }

        private static void RemoveActorFromRequest(TaxiRequest request,
            Actor pActor)
        {
            if (request == null || pActor?.data == null) return;
            try
            {
                request.embarkToBoat(pActor);
                if (request.countActors() == 0)
                {
                    ArmyRtsTransportProductionService.Cancel(request);
                    TaxiManager.cancelRequest(request);
                }
            }
            catch { }
        }

        private static TaxiRequest FindExactReusableRequest(Actor pActor,
            WorldTile pTarget)
        {
            if (pActor?.kingdom?.data == null ||
                pActor.current_tile?.data == null ||
                pTarget?.data == null) return null;
            try
            {
                for (int i = 0; i < TaxiManager.list.Count; i++)
                {
                    TaxiRequest request = TaxiManager.list[i];
                    if (request == null ||
                        request.isState(TaxiRequestState.Transporting) ||
                        request.isState(TaxiRequestState.Finished) ||
                        !request.isSameKingdom(pActor.kingdom) ||
                        !SameTile(request.getTileTarget(), pTarget) ||
                        !SameIsland(request.getTileStart(),
                            pActor.current_tile)) continue;
                    return request;
                }
            }
            catch { }
            return null;
        }

        private static TaxiRequest SafeRequest(Actor pActor)
        {
            if (pActor?.data == null) return null;
            try { return TaxiManager.getRequestForActor(pActor); }
            catch { return null; }
        }

        private static int SafeAssignedBoatTileId(TaxiRequest pRequest)
        {
            try
            {
                return pRequest?.getBoat()?.actor?.current_tile?.data?.tile_id
                       ?? -1;
            }
            catch { return -1; }
        }

        private static long SafeAssignedBoatId(TaxiRequest pRequest)
        {
            try { return pRequest?.getBoat()?.actor?.data?.id ?? -1L; }
            catch { return -1L; }
        }

        private static int SafeInsideBoatTileId(Actor pActor)
        {
            try
            {
                return pActor?.inside_boat?.actor?.current_tile?.data?.tile_id
                       ?? -1;
            }
            catch { return -1; }
        }

        private static void CaptureMovementMarker(Actor pActor,
            int pTileId, ref long pMarkerActorId, ref int pMovementTileId)
        {
            if (pTileId < 0 || pActor?.data == null ||
                pActor.data.id >= pMarkerActorId) return;
            pMarkerActorId = pActor.data.id;
            pMovementTileId = pTileId;
        }

        private static WorldTile SafeRequestTarget(TaxiRequest pRequest)
        {
            if (pRequest == null) return null;
            try { return pRequest.getTileTarget(); }
            catch { return null; }
        }

        private static bool IsValidMember(Actor pActor, Army pArmy)
        {
            try
            {
                bool militaryMember = pActor?.is_profession_warrior == true ||
                                      ReferenceEquals(
                                          pArmy?.getCaptain(), pActor);
                return pActor?.data != null && pArmy?.data != null &&
                       pActor.army == pArmy && pActor.kingdom?.data != null &&
                       pActor.current_tile?.data != null &&
                       militaryMember &&
                       pActor.isAlive() && !pActor.isRekt();
            }
            catch { return false; }
        }

        private static bool IsCaptain(Actor pActor, Army pArmy)
        {
            try { return ReferenceEquals(pArmy?.getCaptain(), pActor); }
            catch { return false; }
        }

        private static bool SameIsland(WorldTile pFirst,
            WorldTile pSecond)
        {
            if (pFirst == null || pSecond == null) return false;
            try { return pFirst.isSameIsland(pSecond); }
            catch { return false; }
        }

        private static bool SameTile(WorldTile pFirst,
            WorldTile pSecond)
        {
            return pFirst?.data != null && pSecond?.data != null &&
                   pFirst.data.tile_id == pSecond.data.tile_id;
        }

        private static WorldTile FindTile(int pTileId)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            return tiles != null && pTileId >= 0 && pTileId < tiles.Length
                ? tiles[pTileId]
                : null;
        }

        private static double CurrentRealtime()
        {
            try
            {
                return ActiveClock.Current(
                    Time.realtimeSinceStartupAsDouble);
            }
            catch { return 0d; }
        }

        private static void LogPhase(TransportState pState,
            string pPhase, string pDetail = "")
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled) return;
            try
            {
                ModClass.LogInfo("[AW3 RTS transport] phase=" + pPhase +
                                 " army=" + (pState?.Army?.id ?? -1L) +
                                 " target_tile=" +
                                 (pState?.TargetTileId ?? -1) + pDetail);
            }
            catch { }
        }
    }
}
