using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.performance;
using ai;
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
        private const float ProductionQueueCost = 48f;
        private const float BoatTravelCostPerTile = 0.6f;
        private const double BoatPathDiagnosticIntervalSeconds = 1d;

        private sealed class TransportState
        {
            internal Army Army;
            internal int TargetTileId;
            internal AWDockRouteCandidate Route;
            internal int RouteTopologyRevision;
            internal ArmyRtsTransportP0Stage Stage;
            internal Actor Boat;
            internal bool LoggedDirectorOmissionPreserved;
            internal string LastPendingDiagnostic = string.Empty;
            internal double NextBoatPathDiagnosticAt;
            internal readonly HashSet<long> TemporaryBoatIds =
                new HashSet<long>();
            internal readonly Dictionary<long, Actor> Members =
                new Dictionary<long, Actor>();
            internal readonly List<long> InvalidMemberIds =
                new List<long>();
        }

        private readonly struct RosterCensus
        {
            internal RosterCensus(int pValidCount, int pEmbarkedCount,
                int pAboardCurrentBoatCount, int pLandedCount)
            {
                ValidCount = pValidCount;
                EmbarkedCount = pEmbarkedCount;
                AboardCurrentBoatCount = pAboardCurrentBoatCount;
                LandedCount = pLandedCount;
            }

            internal int ValidCount { get; }
            internal int EmbarkedCount { get; }
            internal int AboardCurrentBoatCount { get; }
            internal int LandedCount { get; }
            internal bool HasAnyEmbarked => EmbarkedCount > 0;
            internal bool AllEmbarked => ValidCount > 0 &&
                                         AboardCurrentBoatCount == ValidCount;
            internal bool AllLanded => ValidCount > 0 &&
                                       LandedCount == ValidCount;
        }

        private static readonly Dictionary<long, TransportState> States =
            new Dictionary<long, TransportState>();
        private static readonly Dictionary<long, Actor> OwnedTransportBoats =
            new Dictionary<long, Actor>();
        private static readonly List<long> TransportStateIds =
            new List<long>();
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
            return pArmy?.data != null &&
                   States.TryGetValue(pArmy.id, out TransportState state) &&
                   state.Members.Count > 0 &&
                   FindTile(state.TargetTileId)?.data != null;
        }

        internal static bool HasAnyActiveVoyage => States.Count > 0;

        internal static ArmyRtsTransportPhase GetPhase(Army pArmy)
        {
            if (pArmy?.data == null ||
                !States.TryGetValue(pArmy.id, out TransportState state))
                return ArmyRtsTransportPhase.None;
            switch (state.Stage)
            {
                case ArmyRtsTransportP0Stage.Boarding:
                    return ArmyRtsTransportPhase.Embarking;
                case ArmyRtsTransportP0Stage.Sailing:
                    return ArmyRtsTransportPhase.Sailing;
                case ArmyRtsTransportP0Stage.Landing:
                case ArmyRtsTransportP0Stage.Complete:
                    return ArmyRtsTransportPhase.Landing;
                default:
                    return ArmyRtsTransportPhase.AwaitingPickup;
            }
        }

        internal static bool TryGetRouteEstimate(Army pArmy,
            WorldTile pTarget, out ArmyRtsTransportEstimate pEstimate)
        {
            pEstimate = default;
            Actor captain = SafeCaptain(pArmy);
            WorldTile start = captain?.current_tile;
            if (pArmy?.data == null || start?.data == null ||
                pTarget?.data == null) return false;
            AWDockRouteCandidate route;
            if (!AWDockTransportService.TryResolveRoute(start, pTarget,
                    out route) &&
                !AWDockTransportService.TryResolveEmergencyShoreRoute(
                    start, pTarget, out route, out _)) return false;
            pEstimate = new ArmyRtsTransportEstimate(
                pPickupCost: 0f, pQueueCost: ProductionQueueCost,
                pSeaCost: Math.Max(1f, route.EstimatedRouteTiles) *
                          BoatTravelCostPerTile,
                pLandingCost: 0f);
            return true;
        }

        public static bool OwnsActorTask(Actor pActor)
        {
            Army army = pActor?.army;
            return pActor?.data != null && army?.data != null &&
                   States.TryGetValue(army.id, out TransportState state) &&
                   state.Members.TryGetValue(pActor.data.id,
                       out Actor member) &&
                   ReferenceEquals(member, pActor);
        }

        /// <summary>
        /// P0 transport boats may be spawned after the current BatchActors
        /// pass. They remain owned by the voyage and must advance before the
        /// next native batch rebuild admits them.
        /// </summary>
        public static bool OwnsTransportBoat(Actor pActor)
        {
            if (pActor?.data == null || pActor.asset?.is_boat != true)
                return false;
            long boatId = pActor.data.id;
            if (!OwnedTransportBoats.TryGetValue(boatId,
                    out Actor owned) || !ReferenceEquals(owned, pActor))
                return false;
            if (IsLiveBoat(pActor)) return true;
            OwnedTransportBoats.Remove(boatId);
            return false;
        }

        internal static bool IsExecutingVanillaPassengerTask(Actor pActor)
        {
            return false;
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
                AWDockRouteCandidate route;
                if (!AWDockTransportService.TryResolveRoute(
                        pActor.current_tile, pTarget, out route))
                {
                    if (!AWDockTransportService.TryResolveEmergencyShoreRoute(
                            pActor.current_tile, pTarget, out route, out _))
                        return false;
                    ArmyRtsTransportDiagnostics.RecordEmergencyShoreRoute();
                }
                state = Begin(army, pTarget, route);
                if (state == null) return false;
            }
            else
            {
                WorldTile activeTarget = FindTile(state.TargetTileId);
                bool activeTargetMatches = SameTile(activeTarget, pTarget);
                if (ArmyRtsTransportRules.ShouldReplaceActiveVoyageTarget(
                        activeTargetMatches, pMayBegin,
                        IsCaptain(pActor, army), pForceTransport,
                        HasEmbarkedMembers(army)))
                {
                    AWDockRouteCandidate route;
                    if (!AWDockTransportService.TryResolveRoute(
                            pActor.current_tile, pTarget, out route))
                    {
                        if (!AWDockTransportService.
                                TryResolveEmergencyShoreRoute(
                                    pActor.current_tile, pTarget,
                                    out route, out _)) return false;
                        ArmyRtsTransportDiagnostics.
                            RecordEmergencyShoreRoute();
                    }
                    ReleaseArmy(army);
                    state = Begin(army, pTarget, route);
                    if (state == null) return false;
                }
                else if (activeTarget?.data != null)
                {
                    if (!activeTargetMatches && pMayBegin) return false;
                    pTarget = activeTarget;
                }
            }

            state.Members[pActor.data.id] = pActor;
            ArmyMilitaryMovementPriorityIndex.Register(pActor.data.id,
                ArmyMilitaryMovementPriorityKind.RtsMember);
            return true;
        }

        public static void ProcessOrdinaryFrame()
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                AW3MultiplayerReplicaScope.IsReplicaSession ||
                World.world == null || World.world.isPaused() ||
                AWPerformanceSettings.Mode == AWSimulationMode.Large)
                return;
            ArmyRtsTransportProductionService.
                ProcessTemporaryBoatDisposals();
            ProcessVoyageFrame(Time.deltaTime, pDriveMembers: true);
        }

        internal static void RefreshMilitaryP0Priority()
        {
            if (AWPerformanceSettings.Mode != AWSimulationMode.Large) return;
            foreach (TransportState state in States.Values)
            {
                if (state?.Army?.data == null) continue;
                foreach (Actor member in state.Members.Values)
                {
                    bool insideBoat;
                    try { insideBoat = member?.is_inside_boat == true; }
                    catch { insideBoat = false; }
                    if (!ArmyRtsTransportRules.ShouldAdmitMemberP0(
                            IsValidMember(member, state.Army), insideBoat))
                        continue;
                    ArmyMilitaryMovementPriorityIndex.Register(member.data.id,
                        ArmyMilitaryMovementPriorityKind.RtsMember);
                }
            }
        }

        internal static void ProcessMilitaryP0(float pCycleElapsed)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                AW3MultiplayerReplicaScope.IsReplicaSession ||
                World.world == null || World.world.isPaused() ||
                AWPerformanceSettings.Mode != AWSimulationMode.Large)
                return;
            ArmyRtsTransportProductionService.
                ProcessTemporaryBoatDisposals();
            if (!ArmyRtsTransportRules.ShouldProcessInMilitaryP0(
                    largeStepMode: true,
                    activeVoyageCount: States.Count)) return;
            ProcessVoyageFrame(pCycleElapsed, pDriveMembers: false);
        }

        internal static bool TryDriveMemberP0(Actor pActor,
            float pCycleElapsed)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                !States.TryGetValue(army.id, out TransportState state) ||
                !state.Members.ContainsKey(pActor.data.id)) return false;

            if (state.Stage == ArmyRtsTransportP0Stage.AssembleAtEntry &&
                IsCaptain(pActor, army))
            {
                WorldTile entryLand = FindTile(
                    state.Route.Entry.LandTileId);
                DriveActorToTileP0(pActor, entryLand, pCycleElapsed);
            }
            else
            {
                HoldTransportMember(pActor);
            }
            pActor.skipBehaviour();
            ArmyMilitaryMovementPriorityIndex.MarkProcessed(pActor.data.id);
            return true;
        }

        internal static bool SuppressCombatForVoyage(Actor pActor)
        {
            if (!OwnsActorTask(pActor)) return false;
            try { pActor.clearAttackTarget(); }
            catch { }
            try { pActor.beh_actor_target = null; }
            catch { }
            return true;
        }

        private static void ProcessVoyageFrame(float pCycleElapsed,
            bool pDriveMembers)
        {
            if (!ArmyRtsTransportRules.ShouldProcessFrame(States.Count))
                return;
            TransportStateIds.Clear();
            foreach (long armyId in States.Keys)
                TransportStateIds.Add(armyId);

            for (int i = 0; i < TransportStateIds.Count; i++)
            {
                long armyId = TransportStateIds[i];
                if (!States.TryGetValue(armyId,
                        out TransportState state)) continue;
                Army army = state.Army;
                WorldTile target = FindTile(state.TargetTileId);
                if (army?.data == null)
                {
                    States.Remove(armyId);
                    DestroyTemporaryTransportBoats(state);
                    continue;
                }
                if (target?.data == null)
                {
                    ReleaseArmy(army);
                    continue;
                }

                Actor captain = SafeCaptain(army);
                if (!IsValidMember(captain, army))
                {
                    ReleaseArmy(army);
                    continue;
                }
                state.Members[captain.data.id] = captain;
                RosterCensus census =
                    RefreshRosterAndBuildCensus(state, target);

                if (!TryResolveLiveRoute(state, captain, target,
                        census.HasAnyEmbarked,
                        out WorldTile entryLand, out WorldTile pickupSea,
                        out WorldTile destinationSea,
                        out WorldTile landingTile))
                {
                    state.Stage = ArmyRtsTransportP0Stage.RoutePending;
                    LogPending(state, "reason=route_unavailable");
                    continue;
                }

                if (!IsLiveBoat(state.Boat))
                {
                    UnregisterOwnedTransportBoat(state.Boat);
                    state.Boat = null;
                    if (state.Stage >=
                        ArmyRtsTransportP0Stage.Boarding)
                        state.Stage =
                            ArmyRtsTransportP0Stage.BoatToPickup;
                }

                bool captainAtEntry =
                    state.Stage >= ArmyRtsTransportP0Stage.BoatToPickup ||
                    IsAtTile(captain, entryLand);
                bool boatAtPickup =
                    state.Stage >= ArmyRtsTransportP0Stage.Boarding ||
                    IsAtTile(state.Boat, pickupSea);
                bool boatAtDestination =
                    state.Stage >= ArmyRtsTransportP0Stage.Landing ||
                    IsAtTile(state.Boat, destinationSea);
                ArmyRtsTransportP0Stage nextStage =
                    ArmyRtsTransportRules.ResolveP0Stage(
                        routeValid: true,
                        captainAtEntry: captainAtEntry,
                        boatAtPickup: boatAtPickup,
                        allEmbarked: census.AllEmbarked,
                        boatAtDestination: boatAtDestination,
                        allLanded: census.AllLanded);
                SetStage(state, nextStage);

                switch (state.Stage)
                {
                    case ArmyRtsTransportP0Stage.AssembleAtEntry:
                        if (pDriveMembers && !DriveActorToTileP0(captain,
                                entryLand, pCycleElapsed))
                            LogPending(state,
                                "reason=assembly_path_pending");
                        break;
                    case ArmyRtsTransportP0Stage.BoatToPickup:
                        if (!EnsureTemporaryBoat(state))
                            LogPending(state,
                                "reason=boat_provision_failed");
                        else if (!DriveBoatToTileP0(state, pickupSea,
                                     pCycleElapsed))
                            LogPending(state,
                                "reason=boat_pickup_path_pending");
                        break;
                    case ArmyRtsTransportP0Stage.Boarding:
                        if (!BoardRoster(state))
                            LogPending(state,
                                "reason=boarding_incomplete");
                        break;
                    case ArmyRtsTransportP0Stage.Sailing:
                        if (!DriveBoatToTileP0(state,
                                destinationSea, pCycleElapsed))
                            LogPending(state,
                                "reason=sailing_path_pending");
                        break;
                    case ArmyRtsTransportP0Stage.Landing:
                        if (LandRoster(state, landingTile, target))
                            CompleteVoyage(armyId, state);
                        else
                            LogPending(state,
                                "reason=landing_incomplete");
                        break;
                    case ArmyRtsTransportP0Stage.Complete:
                        CompleteVoyage(armyId, state);
                        break;
                }
            }
        }

        private static bool TryResolveLiveRoute(TransportState pState,
            Actor pCaptain, WorldTile pTarget, bool pHasAnyEmbarked,
            out WorldTile pEntryLand,
            out WorldTile pPickupSea, out WorldTile pDestinationSea,
            out WorldTile pLandingLand)
        {
            bool embarked = pHasAnyEmbarked;
            if (!embarked &&
                AWDockTransportService.IsRouteLive(pState.Route) &&
                AWDockTransportService.TryResolveRouteTiles(pState.Route,
                    out pEntryLand, out pPickupSea,
                    out pDestinationSea, out pLandingLand)) return true;
            if (embarked &&
                AWDockTransportService.TryResolveDestinationTiles(
                    pState.Route, out pDestinationSea,
                    out pLandingLand))
            {
                pEntryLand = FindTile(pState.Route.Entry.LandTileId);
                pPickupSea = FindTile(pState.Route.Entry.OceanTileId);
                return true;
            }

            pEntryLand = null;
            pPickupSea = null;
            pDestinationSea = null;
            pLandingLand = null;
            if (embarked)
            {
                if (!AWDockTransportService.TryResolveDestination(
                        pState.Route, pTarget,
                        out AWDockRouteCandidate destination))
                    return false;
                pState.Route = destination;
                pState.RouteTopologyRevision =
                    AWDockTransportService.TopologyRevision;
                if (pState.Stage < ArmyRtsTransportP0Stage.Sailing)
                    SetStage(pState, ArmyRtsTransportP0Stage.Sailing);
                pEntryLand = FindTile(pState.Route.Entry.LandTileId);
                pPickupSea = FindTile(pState.Route.Entry.OceanTileId);
                return AWDockTransportService.TryResolveDestinationTiles(
                    pState.Route, out pDestinationSea,
                    out pLandingLand);
            }
            if (pCaptain?.current_tile?.data == null ||
                !AWDockTransportService.TryResolveRoute(
                    pCaptain.current_tile, pTarget,
                    out AWDockRouteCandidate replacement)) return false;
            pState.Route = replacement;
            pState.RouteTopologyRevision =
                AWDockTransportService.TopologyRevision;
            SetStage(pState, ArmyRtsTransportP0Stage.AssembleAtEntry);
            return AWDockTransportService.TryResolveRouteTiles(
                pState.Route, out pEntryLand, out pPickupSea,
                out pDestinationSea, out pLandingLand);
        }

        private static bool EnsureTemporaryBoat(TransportState pState)
        {
            if (IsLiveBoat(pState?.Boat)) return true;
            Kingdom kingdom;
            try { kingdom = pState?.Army?.getKingdom(); }
            catch { kingdom = null; }
            if (!ArmyRtsTransportProductionService.TryProvisionAtRoute(
                    kingdom, pState.Route, out Actor boat)) return false;
            pState.Boat = boat;
            pState.TemporaryBoatIds.Add(boat.data.id);
            OwnedTransportBoats[boat.data.id] = boat;
            LogPhase(pState, "boat_provisioned",
                " boat=" + boat.data.id);
            return true;
        }

        private static bool BoardRoster(TransportState pState)
        {
            Actor boatActor = pState?.Boat;
            if (!IsLiveBoat(boatActor)) return false;
            Boat boat;
            try { boat = boatActor.getSimpleComponent<Boat>(); }
            catch { boat = null; }
            if (boat == null) return false;
            int valid = 0;
            int aboard = 0;
            foreach (Actor member in pState.Members.Values)
            {
                if (!IsValidMember(member, pState.Army)) continue;
                valid++;
                bool alreadyAboard = false;
                try
                {
                    alreadyAboard = member.is_inside_boat &&
                                     ReferenceEquals(
                                         member.inside_boat, boat);
                }
                catch { }
                if (!alreadyAboard)
                {
                    try
                    {
                        AWPathMovementBridge.Cancel(member,
                            AWPathFailureReason.CancelledByNewRequest);
                        Boat previousBoat = member.inside_boat;
                        if (previousBoat != null &&
                            !ReferenceEquals(previousBoat, boat))
                            previousBoat.removePassenger(member);

                        // Match Cultiway's batch-independent loading handoff.
                        // Actor.embarkInto calls native stopMovement first,
                        // which can throw for a newly admitted P0 actor with no
                        // movement batch yet.
                        member.data.transportID = boat.actor.data.id;
                        member.is_inside_boat = true;
                        member.inside_boat = boat;
                        boat.addPassenger(member);
                        AWInsideBoatActorIndex.Notify(member, true);
                    }
                    catch { }
                }
                try
                {
                    if (member.is_inside_boat &&
                        ReferenceEquals(member.inside_boat, boat)) aboard++;
                }
                catch { }
            }
            return valid > 0 && aboard == valid;
        }

        private static bool LandRoster(TransportState pState,
            WorldTile landingTile, WorldTile pTarget)
        {
            Actor boatActor = pState?.Boat;
            if (!IsLiveBoat(boatActor) || landingTile?.data == null)
                return false;
            Boat boat;
            try { boat = boatActor.getSimpleComponent<Boat>(); }
            catch { boat = null; }
            if (boat == null) return false;
            try { boat.unloadPassengers(landingTile, false); }
            catch { }
            int valid = 0;
            int landed = 0;
            foreach (Actor member in pState.Members.Values)
            {
                if (!IsValidMember(member, pState.Army)) continue;
                valid++;
                if (member.is_inside_boat)
                {
                    try
                    {
                        member.disembarkTo(boat, landingTile);
                        AWInsideBoatActorIndex.Notify(member, false);
                        ArmyMilitaryMovementPriorityIndex.Register(
                            member.data.id,
                            ArmyMilitaryMovementPriorityKind.RtsMember);
                    }
                    catch { }
                }
                if (!member.is_inside_boat &&
                    SameIsland(member.current_tile, pTarget) &&
                    IsStableLandingTile(member.current_tile)) landed++;
            }
            return valid > 0 && landed == valid;
        }

        private static bool DriveActorToTileP0(Actor pActor,
            WorldTile pTarget, float pCycleElapsed)
        {
            if (pActor?.data == null || pTarget?.data == null) return false;
            if (IsAtTile(pActor, pTarget))
            {
                AWPathMovementBridge.Cancel(pActor,
                    AWPathFailureReason.CancelledByNewRequest);
                HoldTransportMember(pActor);
                return true;
            }
            SubmitLockedPathIfNeeded(pActor, pTarget,
                pPathOnWater: false, out _, out _);
            DriveOwnedPathP0(pActor, pCycleElapsed);
            return IsAtTile(pActor, pTarget);
        }

        private static bool DriveBoatToTileP0(TransportState pState,
            WorldTile pTarget, float pCycleElapsed)
        {
            Actor pBoat = pState?.Boat;
            if (!IsLiveBoat(pBoat) || pTarget?.data == null) return false;
            if (IsAtTile(pBoat, pTarget))
            {
                AWPathMovementBridge.Cancel(pBoat,
                    AWPathFailureReason.CancelledByNewRequest);
                HoldTransportMember(pBoat);
                return true;
            }
            SubmitLockedPathIfNeeded(pBoat, pTarget,
                pPathOnWater: true, out bool submitted,
                out ExecuteEvent submitResult);
            DriveOwnedPathP0(pBoat, pCycleElapsed, pState,
                submitted ? submitResult.ToString() : "unchanged");
            return IsAtTile(pBoat, pTarget);
        }

        private static void SubmitLockedPathIfNeeded(Actor pActor,
            WorldTile pTarget, bool pPathOnWater,
            out bool pSubmitted, out ExecuteEvent pSubmitResult)
        {
            pSubmitted = false;
            pSubmitResult = ExecuteEvent.False;
            if (pActor?.data == null || pTarget?.data == null) return;
            bool exactTarget = SameTile(pActor.tile_target, pTarget);
            // Vanilla transport boats own their accepted native water path.
            // The RTS front lane may revisit them every frame, but must not
            // cancel and resubmit that path before the native boat lifecycle
            // consumes it.
            if (exactTarget && pActor.asset?.is_boat == true)
            {
                pSubmitResult = ExecuteEvent.True;
                return;
            }
            bool customPathOwned = AWPathMovementBridge.HasOwnership(pActor);
            // The front lane revisits transport members every render frame.
            // Keep accepted work alive until its owner consumes the result.
            if (ArmyRtsTransportRules.ShouldKeepExistingP0Path(
                    exactTarget, customPathOwned,
                    nativeTransportBoat: pActor.asset?.is_boat == true))
            {
                pSubmitResult = ExecuteEvent.True;
                return;
            }
            if (customPathOwned)
                AWPathMovementBridge.Cancel(pActor,
                    AWPathFailureReason.CancelledByNewRequest);
            pSubmitted = true;
            pSubmitResult = pActor.goTo(pTarget, pPathOnWater,
                pWalkOnBlocks: false, pWalkOnLava: false,
                pLimitPathfindingRegions: 0);
        }

        private static void DriveOwnedPathP0(Actor pActor,
            float pCycleElapsed, TransportState pState = null,
            string pSubmitResult = "not_applicable")
        {
            if (pActor?.data == null) return;
            try
            {
                pActor.updateParallelChecks(pCycleElapsed);
                if (pActor.asset?.is_boat == true &&
                    OwnsTransportBoat(pActor))
                {
                    // Keep the original boat task/path lifecycle alive. The
                    // AW bridge is intentionally bypassed for these boats;
                    // b5 advances the native water path and u10 performs the
                    // native smooth movement for the current step.
                    pActor.b4_checkTaskVerifier(pCycleElapsed);
                    pActor.b5_checkPathMovement(pCycleElapsed);
                    pActor.b6_updateAI(pCycleElapsed);
                    pActor.b5_checkPathMovement(pCycleElapsed);
                    pActor.u10_checkSmoothMovement(pCycleElapsed);
                    pActor.skipBehaviour();
                    ArmyMilitaryMovementPriorityIndex.MarkProcessed(
                        pActor.data.id);
                    return;
                }
                bool captureDiagnostic =
                    CaptureTransportPathDiagnostic(pState, pActor);
                string pre = captureDiagnostic
                    ? AWPathMovementBridge.DescribeRuntimeState(pActor)
                    : string.Empty;
                if (AWPathMovementBridge.HasOwnership(pActor) &&
                    !pActor.is_moving)
                    AWPathMovementBridge.Update(pActor);
                string postUpdate = captureDiagnostic
                    ? AWPathMovementBridge.DescribeRuntimeState(pActor)
                    : string.Empty;
                pActor.u10_checkSmoothMovement(pCycleElapsed);
                string postSmooth = captureDiagnostic
                    ? AWPathMovementBridge.DescribeRuntimeState(pActor)
                    : string.Empty;
                if (captureDiagnostic)
                    ModClass.LogInfo(
                        "[AW3 RTS transport] phase=boat_path_state" +
                        " army=" + (pState?.Army?.id ?? -1L) +
                        " boat=" + pActor.data.id +
                        " stage=" + StageName(pState.Stage) +
                        " submit=" + pSubmitResult +
                        " pre=" + pre +
                        " post_update=" + postUpdate +
                        " post_smooth=" + postSmooth);
                pActor.skipBehaviour();
                ArmyMilitaryMovementPriorityIndex.MarkProcessed(
                    pActor.data.id);
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "[AW3 RTS transport] phase=p0_move_error actor=" +
                    pActor.data.id + " error=" +
                    error.GetType().Name);
            }
        }

        private static bool CaptureTransportPathDiagnostic(
            TransportState pState, Actor pActor)
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled ||
                pState == null || pActor?.data == null ||
                pActor.asset?.is_boat != true) return false;
            double now;
            try { now = Time.realtimeSinceStartupAsDouble; }
            catch { return false; }
            if (now < pState.NextBoatPathDiagnosticAt) return false;
            pState.NextBoatPathDiagnosticAt = now +
                BoatPathDiagnosticIntervalSeconds;
            return true;
        }

        private static TransportState Begin(Army pArmy,
            WorldTile pTarget, AWDockRouteCandidate pRoute)
        {
            if (pArmy?.data == null || pTarget?.data == null ||
                !pRoute.IsValid) return null;
            ClearMovementForTransport(pArmy);
            var state = new TransportState
            {
                Army = pArmy,
                TargetTileId = pTarget.data.tile_id,
                Route = pRoute,
                RouteTopologyRevision =
                    AWDockTransportService.TopologyRevision,
                Stage = ArmyRtsTransportP0Stage.RoutePending
            };
            int count;
            try { count = pArmy.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor member;
                try { member = pArmy.units[i]; }
                catch { continue; }
                if (IsValidMember(member, pArmy))
                    state.Members[member.data.id] = member;
            }
            Actor captain = SafeCaptain(pArmy);
            if (IsValidMember(captain, pArmy))
                state.Members[captain.data.id] = captain;
            if (state.Members.Count == 0) return null;
            States[pArmy.id] = state;
            AWArmyMarchService.OnTransportStarted(pArmy);
            LogPhase(state, "route_selected",
                " source=" + pRoute.Source +
                " entry=" + pRoute.Entry.LandTileId +
                " pickup=" + pRoute.Entry.OceanTileId +
                " destination=" + pRoute.Exit.OceanTileId +
                " landing=" + pRoute.Exit.LandTileId);
            return state;
        }

        private static RosterCensus RefreshRosterAndBuildCensus(
            TransportState pState, WorldTile pTarget)
        {
            pState.InvalidMemberIds.Clear();
            int armyCount;
            try { armyCount = pState.Army.units?.Count ?? 0; }
            catch { armyCount = 0; }
            for (int i = 0; i < armyCount; i++)
            {
                Actor member;
                try { member = pState.Army.units[i]; }
                catch { continue; }
                if (IsValidMember(member, pState.Army))
                    pState.Members[member.data.id] = member;
            }
            Boat activeBoat = null;
            if (IsLiveBoat(pState.Boat))
                try { activeBoat = pState.Boat.getSimpleComponent<Boat>(); }
                catch { activeBoat = null; }
            int valid = 0;
            int embarked = 0;
            int aboardCurrentBoat = 0;
            int landed = 0;
            foreach (KeyValuePair<long, Actor> pair in pState.Members)
            {
                if (!IsValidMember(pair.Value, pState.Army))
                {
                    pState.InvalidMemberIds.Add(pair.Key);
                    continue;
                }
                Actor member = pair.Value;
                valid++;
                bool insideBoat;
                try { insideBoat = member.is_inside_boat; }
                catch { insideBoat = false; }
                if (insideBoat)
                {
                    embarked++;
                    try
                    {
                        if (activeBoat != null && ReferenceEquals(
                                member.inside_boat, activeBoat))
                            aboardCurrentBoat++;
                    }
                    catch { }
                }
                else if (SameIsland(member.current_tile, pTarget) &&
                         IsStableLandingTile(member.current_tile))
                    landed++;
            }
            for (int i = 0; i < pState.InvalidMemberIds.Count; i++)
                pState.Members.Remove(pState.InvalidMemberIds[i]);
            return new RosterCensus(valid, embarked,
                aboardCurrentBoat, landed);
        }

        private static void CompleteVoyage(long pArmyId,
            TransportState pState)
        {
            if (pState == null ||
                !States.TryGetValue(pArmyId, out TransportState current) ||
                !ReferenceEquals(current, pState)) return;
            SetStage(pState, ArmyRtsTransportP0Stage.Complete);
            if (!States.Remove(pArmyId)) return;
            DestroyTemporaryTransportBoats(pState);
            AWArmyMarchService.OnTransportCompleted(pState.Army);
            ArmyRtsControllerService.OnTransportCompleted(pState.Army);
        }

        public static void ReleaseArmy(Army pArmy)
        {
            if (pArmy?.data == null) return;
            if (!States.TryGetValue(pArmy.id,
                    out TransportState state)) return;
            DisembarkRosterForRelease(state);
            States.Remove(pArmy.id);
            foreach (Actor member in state.Members.Values)
            {
                if (member?.data == null) continue;
                try
                {
                    AWPathMovementBridge.Cancel(member,
                        AWPathFailureReason.CancelledByNewRequest);
                }
                catch { }
            }
            DestroyTemporaryTransportBoats(state);
            AWArmyMarchService.OnTransportCancelled(pArmy);
            ArmyRtsControllerService.OnTransportCancelled(pArmy);
        }

        private static void DisembarkRosterForRelease(
            TransportState pState)
        {
            Actor boatActor = pState?.Boat;
            if (boatActor?.data == null) return;
            Boat boat;
            try { boat = boatActor.getSimpleComponent<Boat>(); }
            catch { boat = null; }
            if (boat == null) return;

            WorldTile releaseTile = ResolveEmergencyLandingTile(pState,
                boatActor.current_tile);
            if (releaseTile?.data == null) return;
            try { boat.unloadPassengers(releaseTile, false); }
            catch { }
            foreach (Actor member in pState.Members.Values)
            {
                if (member?.data == null) continue;
                bool stillAboard;
                try
                {
                    stillAboard = member.is_inside_boat &&
                                   ReferenceEquals(member.inside_boat,
                                       boat);
                }
                catch { stillAboard = false; }
                if (!stillAboard) continue;
                try
                {
                    member.disembarkTo(boat, releaseTile);
                    AWInsideBoatActorIndex.Notify(member, false);
                }
                catch { }
            }
            LogPhase(pState, "release_disembarked",
                " boat=" + boatActor.data.id +
                " landing=" + releaseTile.data.tile_id);
        }

        private static WorldTile ResolveEmergencyLandingTile(
            TransportState pState, WorldTile pBoatTile)
        {
            WorldTile entry = FindTile(pState.Route.Entry.LandTileId);
            WorldTile exit = FindTile(pState.Route.Exit.LandTileId);
            bool entryValid = IsStableLandingTile(entry);
            bool exitValid = IsStableLandingTile(exit);
            float entryDistance = entryValid
                ? SafeSquaredDistance(pBoatTile, entry)
                : float.PositiveInfinity;
            float exitDistance = exitValid
                ? SafeSquaredDistance(pBoatTile, exit)
                : float.PositiveInfinity;
            switch (ArmyRtsTransportRules.SelectEmergencyLanding(
                        entryValid, entryDistance, exitValid, exitDistance))
            {
                case ArmyRtsEmergencyLandingChoice.Entry:
                    return entry;
                case ArmyRtsEmergencyLandingChoice.Exit:
                    return exit;
            }

            WorldTile capitalTile = null;
            try
            {
                capitalTile = pState.Army.getKingdom()?.capital?.getTile();
            }
            catch { }
            return IsStableLandingTile(capitalTile) ? capitalTile : null;
        }

        private static float SafeSquaredDistance(WorldTile pFirst,
            WorldTile pSecond)
        {
            if (pFirst?.data == null || pSecond?.data == null)
                return float.PositiveInfinity;
            try { return Toolbox.SquaredDistTile(pFirst, pSecond); }
            catch { return float.PositiveInfinity; }
        }

        public static void Clear()
        {
            var armies = new List<Army>();
            foreach (TransportState state in States.Values)
                if (state?.Army != null) armies.Add(state.Army);
            for (int i = 0; i < armies.Count; i++)
                ReleaseArmy(armies[i]);
            States.Clear();
            OwnedTransportBoats.Clear();
            TransportStateIds.Clear();
            ActiveClock.Reset();
            ArmyRtsTransportProductionService.Clear();
        }

        internal static void EnsureNativeTaxiRequest(Actor pActor,
            WorldTile pTarget)
        {
            if (OwnsActorTask(pActor) && pActor?.data != null)
                ArmyMilitaryMovementPriorityIndex.Register(pActor.data.id,
                    ArmyMilitaryMovementPriorityKind.RtsMember);
        }

        private static void ClearMovementForTransport(Army pArmy)
        {
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor member;
                try { member = pArmy.units[i]; }
                catch { continue; }
                if (!IsValidMember(member, pArmy) ||
                    member.is_inside_boat) continue;
                try
                {
                    AWPathMovementBridge.Cancel(member,
                        AWPathFailureReason.CancelledByNewRequest);
                    member.stopMovement();
                    member.clearOldPath();
                    member.clearTileTarget();
                    member.beh_tile_target = null;
                    member.beh_actor_target = null;
                }
                catch { }
            }
        }

        private static void HoldTransportMember(Actor pActor)
        {
            if (pActor?.data == null || pActor.is_inside_boat) return;
            try
            {
                pActor.stopMovement();
                pActor.next_step_position = pActor.current_tile.posV3;
            }
            catch { }
        }

        private static void DestroyTemporaryTransportBoats(
            TransportState pState)
        {
            if (pState == null) return;
            foreach (long boatId in pState.TemporaryBoatIds)
            {
                OwnedTransportBoats.Remove(boatId);
                ArmyRtsTransportProductionService.
                    DestroyTemporaryTransportBoat(boatId);
            }
            pState.TemporaryBoatIds.Clear();
            UnregisterOwnedTransportBoat(pState.Boat);
            pState.Boat = null;
        }

        private static void UnregisterOwnedTransportBoat(Actor pBoat)
        {
            if (pBoat?.data == null) return;
            long boatId = pBoat.data.id;
            if (OwnedTransportBoats.TryGetValue(boatId,
                    out Actor owned) && ReferenceEquals(owned, pBoat))
                OwnedTransportBoats.Remove(boatId);
        }

        private static void SetStage(TransportState pState,
            ArmyRtsTransportP0Stage pStage)
        {
            if (pState == null || pState.Stage == pStage) return;
            pState.Stage = pStage;
            pState.LastPendingDiagnostic = string.Empty;
            LogPhase(pState, StageName(pStage));
        }

        private static void LogPending(TransportState pState,
            string pReason)
        {
            if (pState == null || string.IsNullOrEmpty(pReason) ||
                pState.LastPendingDiagnostic == pReason) return;
            pState.LastPendingDiagnostic = pReason;
            LogPhase(pState, "pending", " stage=" +
                StageName(pState.Stage) + " " + pReason);
        }

        private static string StageName(ArmyRtsTransportP0Stage pStage)
        {
            switch (pStage)
            {
                case ArmyRtsTransportP0Stage.AssembleAtEntry:
                    return "assembling";
                case ArmyRtsTransportP0Stage.BoatToPickup:
                    return "boat_to_pickup";
                case ArmyRtsTransportP0Stage.Boarding:
                    return "boarding";
                case ArmyRtsTransportP0Stage.Sailing:
                    return "sailing";
                case ArmyRtsTransportP0Stage.Landing:
                    return "landing";
                case ArmyRtsTransportP0Stage.Complete:
                    return "complete";
                case ArmyRtsTransportP0Stage.Failed:
                    return "failed";
                default:
                    return "route_pending";
            }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try { return pArmy?.getCaptain(); }
            catch { return null; }
        }

        private static bool IsValidMember(Actor pActor, Army pArmy)
        {
            try
            {
                return pActor?.data != null && pArmy?.data != null &&
                       pActor.army == pArmy &&
                       pActor.kingdom?.data != null &&
                       pActor.current_tile?.data != null && pActor.isAlive() &&
                       !pActor.isRekt();
            }
            catch { return false; }
        }

        private static bool IsCaptain(Actor pActor, Army pArmy)
        {
            return ReferenceEquals(SafeCaptain(pArmy), pActor);
        }

        private static bool IsLiveBoat(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.asset?.is_boat == true &&
                       pActor.isAlive() && !pActor.isRekt() &&
                       pActor.current_tile?.data != null;
            }
            catch { return false; }
        }

        private static bool IsAtTile(Actor pActor, WorldTile pTile)
        {
            if (pActor?.current_tile?.data == null || pTile?.data == null)
                return false;
            if (SameTile(pActor.current_tile, pTile)) return true;
            try
            {
                return Toolbox.SquaredDistTile(pActor.current_tile,
                    pTile) <= 2;
            }
            catch { return false; }
        }

        private static bool SameIsland(WorldTile pFirst,
            WorldTile pSecond)
        {
            if (pFirst == null || pSecond == null) return false;
            try { return pFirst.isSameIsland(pSecond); }
            catch { return false; }
        }

        private static bool IsStableLandingTile(WorldTile pTile)
        {
            try
            {
                TileTypeBase type = pTile?.Type;
                return type != null && type.ground && !type.liquid &&
                       !type.ocean && !type.lava && !type.block;
            }
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
