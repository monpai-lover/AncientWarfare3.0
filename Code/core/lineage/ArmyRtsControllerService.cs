using System;
using System.Collections.Generic;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.pathfinding;
#if !AW3_RULES_TESTS
using AncientWarfare3.api.commands;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.content;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.presentation;
#endif

namespace AncientWarfare3.core.lineage
{
    public sealed class ArmyRtsControllerRecord
    {
        public ArmyRtsMission Mission { get; internal set; }
        public ArmyRtsState State { get; internal set; }
    }

    public static class ArmyRtsControllerRules
    {
        public const int MaximumControllersPerFrame = 32;

        public static ArmyRtsMission CopyMission(ArmyRtsMission pMission)
        {
            if (pMission == null) return null;
            return new ArmyRtsMission
            {
                ArmyId = pMission.ArmyId,
                KingdomId = pMission.KingdomId,
                WarId = pMission.WarId,
                FrontId = pMission.FrontId,
                TargetCityId = pMission.TargetCityId,
                TargetStrength = pMission.TargetStrength,
                ProposalKind = pMission.ProposalKind,
                Role = pMission.Role,
                Posture = pMission.Posture,
                PlayerOrder = pMission.PlayerOrder,
                IssuedTime = pMission.IssuedTime
            };
        }

        public static bool SameStrategicIntent(ArmyRtsMission pFirst,
            ArmyRtsMission pSecond)
        {
            return pFirst != null && pSecond != null &&
                   pFirst.ArmyId == pSecond.ArmyId &&
                   pFirst.KingdomId == pSecond.KingdomId &&
                   pFirst.WarId == pSecond.WarId &&
                   pFirst.FrontId == pSecond.FrontId &&
                   pFirst.TargetCityId == pSecond.TargetCityId &&
                   pFirst.ProposalKind == pSecond.ProposalKind &&
                   pFirst.Role == pSecond.Role &&
                   pFirst.Posture == pSecond.Posture &&
                   pFirst.PlayerOrder == pSecond.PlayerOrder;
        }

        public static bool SameTransportObjective(ArmyRtsMission pFirst,
            ArmyRtsMission pSecond)
        {
            return pFirst != null && pSecond != null &&
                   ArmyRtsTransportRules.SamePhysicalDestination(
                       pFirst.ArmyId, pFirst.KingdomId, pFirst.WarId,
                       pFirst.TargetCityId, pSecond.ArmyId,
                       pSecond.KingdomId, pSecond.WarId,
                       pSecond.TargetCityId);
        }

        public static bool ShouldResetTransport(ArmyRtsMission pPrevious,
            ArmyRtsMission pNext, bool pHasEmbarkedMembers)
        {
            return !pHasEmbarkedMembers &&
                   !SameTransportObjective(pPrevious, pNext);
        }

        public static bool ShouldResetOperationalProgress(
            ArmyRtsMission pPrevious, ArmyRtsMission pNext)
        {
            return !SameTransportObjective(pPrevious, pNext) ||
                   pPrevious?.ProposalKind != pNext?.ProposalKind;
        }

        public static bool ShouldClearSharedRoute(
            bool operationalProgressChanged,
            bool runtimeExists, int previousDirectorGeneration,
            int nextDirectorGeneration)
        {
            return operationalProgressChanged;
        }

        public static bool ShouldExpectStrategicRoutePlanning(
            ArmyRtsState pState, bool targetComplete, bool routeArrived,
            bool destinationPublished, bool transportOwned)
        {
            if (targetComplete || routeArrived || destinationPublished ||
                transportOwned) return false;
            return pState == ArmyRtsState.March ||
                   pState == ArmyRtsState.Pursue ||
                   pState == ArmyRtsState.Retreat;
        }

        public static bool ShouldExpectMovementCommand(
            ArmyRtsState pState, bool destinationPublished,
            bool routeArrived, bool atStrategicEndpoint,
            bool deploymentProgressExpected)
        {
            if (deploymentProgressExpected) return true;
            if (pState == ArmyRtsState.Assault)
                return !atStrategicEndpoint;
            return (pState == ArmyRtsState.March ||
                    pState == ArmyRtsState.Pursue ||
                    pState == ArmyRtsState.Retreat) &&
                   destinationPublished && !routeArrived;
        }

        public static bool ShouldExpectTransportCommand(
            bool transportRouteConfirmed, bool activeVoyage)
        {
            return transportRouteConfirmed && !activeVoyage;
        }

        public static int ResolveRallyAnchorTileId(int existingAnchorTileId,
            int captainTileId)
        {
            return existingAnchorTileId >= 0
                ? existingAnchorTileId
                : captainTileId;
        }

        public static bool ShouldPublishStrategicDestination(
            ArmyRoutePollKind pPollKind)
        {
            return pPollKind == ArmyRoutePollKind.Completed;
        }

        public static bool HasReachedStrategicDestination(int captainTileId,
            int destinationTileId)
        {
            return captainTileId >= 0 &&
                   captainTileId == destinationTileId;
        }

        public static bool ShouldUseCaptainAsMarchFormationAnchor(
            ArmyRtsState pState, bool destinationPublished,
            bool routeArrived)
        {
            return pState == ArmyRtsState.March && destinationPublished &&
                   !routeArrived;
        }

        public static bool ShouldUseMissionCityAsMovementTarget(
            ArmyRtsState pState, bool hasRouteAnchor)
        {
            if (hasRouteAnchor) return false;
            return pState == ArmyRtsState.Deploy ||
                   pState == ArmyRtsState.Assault ||
                   pState == ArmyRtsState.Hold;
        }

        public static bool ShouldAdvanceRoute(ArmyRtsState pCurrent,
            ArmyRtsState pNext, bool rallyReady)
        {
            return ArmyRtsRules.ShouldAdvanceStrategicRoute(
                pCurrent, pNext, rallyReady);
        }

        public static bool IsPublishedRouteReady(bool routeSubmitted,
            bool destinationPublished, bool routeArrived)
        {
            return routeSubmitted && destinationPublished && !routeArrived;
        }

        public static bool ShouldCompleteFriendlyTarget(ArmyRtsRole pRole)
        {
            return true;
        }

        public static bool ShouldUseFrontHoldJob(
            ArmyRtsProposalKind pProposalKind, ArmyRtsState pState)
        {
            return pProposalKind == ArmyRtsProposalKind.FrontHold &&
                   pState == ArmyRtsState.Hold;
        }
    }

    public sealed class ArmyRtsControllerWorkIndex
    {
        private readonly Dictionary<long, ArmyRtsControllerRecord> _records =
            new Dictionary<long, ArmyRtsControllerRecord>();
        private readonly Queue<long> _queued = new Queue<long>();
        private readonly HashSet<long> _queuedIds = new HashSet<long>();
        private readonly List<long> _frameBatch = new List<long>(
            ArmyRtsControllerRules.MaximumControllersPerFrame);

        public int Count => _records.Count;
        public int QueuedCount => _queuedIds.Count;

        public bool AssignMission(ArmyRtsMission pMission)
        {
            if (pMission == null || pMission.ArmyId < 0L ||
                pMission.KingdomId < 0L || pMission.WarId < 0L ||
                pMission.TargetCityId < 0L) return false;
            ArmyRtsMission copy = ArmyRtsControllerRules.CopyMission(pMission);
            bool changed = true;
            if (_records.TryGetValue(pMission.ArmyId,
                    out ArmyRtsControllerRecord current))
            {
                bool resetOperationalProgress = ArmyRtsControllerRules.
                    ShouldResetOperationalProgress(current.Mission, copy);
                changed = !ArmyRtsControllerRules.SameStrategicIntent(
                    current.Mission, copy);
                current.Mission = copy;
                if (changed && resetOperationalProgress)
                    current.State = ArmyRtsState.Rally;
            }
            else
            {
                _records[pMission.ArmyId] = new ArmyRtsControllerRecord
                {
                    Mission = copy,
                    State = ArmyRtsState.Rally
                };
            }
            Enqueue(pMission.ArmyId);
            return changed;
        }

        public bool TryGet(long pArmyId,
            out ArmyRtsControllerRecord pRecord)
        {
            return _records.TryGetValue(pArmyId, out pRecord);
        }

        public ArmyRtsState ResolveAndSet(long pArmyId,
            ArmyRtsTransitionFacts pFacts)
        {
            if (!_records.TryGetValue(pArmyId,
                    out ArmyRtsControllerRecord record) || pFacts == null)
                return ArmyRtsState.Idle;
            pFacts.CurrentState = record.State;
            pFacts.Role = record.Mission.Role;
            pFacts.Posture = record.Mission.Posture;
            ArmyRtsState next = ArmyRtsRules.ResolveState(pFacts);
            record.State = next;
            return next;
        }

        public IReadOnlyList<long> Take(int pMaximum)
        {
            int limit = Math.Max(0, pMaximum);
            _frameBatch.Clear();
            while (_frameBatch.Count < limit && _queued.Count > 0)
            {
                long armyId = _queued.Dequeue();
                if (!_queuedIds.Remove(armyId) ||
                    !_records.ContainsKey(armyId)) continue;
                _frameBatch.Add(armyId);
            }
            return _frameBatch;
        }

        public bool Requeue(long pArmyId)
        {
            return _records.ContainsKey(pArmyId) && Enqueue(pArmyId);
        }

        public bool SetState(long pArmyId, ArmyRtsState pState)
        {
            if (!_records.TryGetValue(pArmyId,
                    out ArmyRtsControllerRecord record)) return false;
            record.State = pState;
            return true;
        }

        public bool Invalidate(long pArmyId)
        {
            _queuedIds.Remove(pArmyId);
            return _records.Remove(pArmyId);
        }

        public void Clear()
        {
            _records.Clear();
            _queued.Clear();
            _queuedIds.Clear();
        }

        private bool Enqueue(long pArmyId)
        {
            if (!_queuedIds.Add(pArmyId)) return false;
            _queued.Enqueue(pArmyId);
            return true;
        }
    }

    public sealed class ArmyRtsJobAssignmentCursor
    {
        public int MemberCursor { get; private set; }
        public bool JobsInitialized { get; private set; }

        public void Advance(int processedEnd, int rosterCount)
        {
            int count = Math.Max(0, rosterCount);
            MemberCursor = Math.Min(count, Math.Max(0, processedEnd));
            JobsInitialized = MemberCursor >= count;
        }

        public bool Reopen()
        {
            bool changed = MemberCursor != 0 || JobsInitialized;
            MemberCursor = 0;
            JobsInitialized = false;
            return changed;
        }
    }

    public sealed class ArmyRtsMissionIndex
    {
        private readonly Dictionary<long, (long KingdomId, long WarId,
            long TargetCityId)> _missionByArmy =
                new Dictionary<long, (long KingdomId, long WarId,
                    long TargetCityId)>();
        private readonly Dictionary<long, HashSet<long>> _armiesByKingdom =
            new Dictionary<long, HashSet<long>>();
        private readonly Dictionary<long, HashSet<long>> _armiesByWar =
            new Dictionary<long, HashSet<long>>();
        private readonly Dictionary<long, HashSet<long>> _armiesByTarget =
            new Dictionary<long, HashSet<long>>();

        public bool Upsert(ArmyRtsMission pMission)
        {
            if (pMission == null || pMission.ArmyId < 0L ||
                pMission.KingdomId < 0L || pMission.WarId < 0L ||
                pMission.TargetCityId < 0L)
                return false;
            var next = (pMission.KingdomId, pMission.WarId,
                pMission.TargetCityId);
            if (_missionByArmy.TryGetValue(pMission.ArmyId,
                    out (long KingdomId, long WarId,
                        long TargetCityId) previous))
            {
                if (previous == next) return false;
                RemoveFrom(_armiesByKingdom, previous.KingdomId,
                    pMission.ArmyId);
                RemoveFrom(_armiesByWar, previous.WarId,
                    pMission.ArmyId);
                RemoveFrom(_armiesByTarget, previous.TargetCityId,
                    pMission.ArmyId);
            }
            _missionByArmy[pMission.ArmyId] = next;
            AddTo(_armiesByKingdom, next.KingdomId, pMission.ArmyId);
            AddTo(_armiesByWar, next.WarId, pMission.ArmyId);
            AddTo(_armiesByTarget, next.TargetCityId, pMission.ArmyId);
            return true;
        }

        public bool Matches(ArmyRtsMission pMission)
        {
            if (pMission == null || pMission.ArmyId < 0L ||
                pMission.KingdomId < 0L || pMission.WarId < 0L ||
                pMission.TargetCityId < 0L ||
                !_missionByArmy.TryGetValue(pMission.ArmyId,
                    out (long KingdomId, long WarId,
                        long TargetCityId) current)) return false;
            return current.KingdomId == pMission.KingdomId &&
                   current.WarId == pMission.WarId &&
                   current.TargetCityId == pMission.TargetCityId;
        }

        public bool Remove(long pArmyId)
        {
            if (!_missionByArmy.TryGetValue(pArmyId,
                    out (long KingdomId, long WarId,
                        long TargetCityId) previous))
                return false;
            _missionByArmy.Remove(pArmyId);
            RemoveFrom(_armiesByKingdom, previous.KingdomId, pArmyId);
            RemoveFrom(_armiesByWar, previous.WarId, pArmyId);
            RemoveFrom(_armiesByTarget, previous.TargetCityId, pArmyId);
            return true;
        }

        public IReadOnlyList<long> SnapshotKingdom(long pKingdomId)
        {
            return Snapshot(_armiesByKingdom, pKingdomId);
        }

        public IReadOnlyList<long> SnapshotTarget(long pTargetCityId)
        {
            return Snapshot(_armiesByTarget, pTargetCityId);
        }

        public IReadOnlyList<long> SnapshotWar(long pWarId)
        {
            return Snapshot(_armiesByWar, pWarId);
        }

        public void Clear()
        {
            _missionByArmy.Clear();
            _armiesByKingdom.Clear();
            _armiesByWar.Clear();
            _armiesByTarget.Clear();
        }

        private static void AddTo(Dictionary<long, HashSet<long>> pIndex,
            long pKey, long pArmyId)
        {
            if (!pIndex.TryGetValue(pKey, out HashSet<long> ids))
            {
                ids = new HashSet<long>();
                pIndex[pKey] = ids;
            }
            ids.Add(pArmyId);
        }

        private static void RemoveFrom(
            Dictionary<long, HashSet<long>> pIndex, long pKey,
            long pArmyId)
        {
            if (!pIndex.TryGetValue(pKey, out HashSet<long> ids)) return;
            ids.Remove(pArmyId);
            if (ids.Count == 0) pIndex.Remove(pKey);
        }

        private static IReadOnlyList<long> Snapshot(
            Dictionary<long, HashSet<long>> pIndex, long pKey)
        {
            if (!pIndex.TryGetValue(pKey, out HashSet<long> ids) ||
                ids.Count == 0) return Array.Empty<long>();
            var result = new List<long>(ids);
            result.Sort();
            return result;
        }
    }

#if !AW3_RULES_TESTS
    internal sealed class ArmyRtsStrategicProjection
    {
        internal ArmyRtsState State;
        internal ArmyRtsRole Role;
        internal ArmyRtsPosture Posture;
        internal ArmyRtsProposalKind ProposalKind;
        internal long WarId = -1L;
        internal long FrontId = -1L;
        internal long TargetCityId = -1L;
        internal int Supply = 100;
        internal int Organization = 100;
        internal bool PlayerOrder;
    }

    internal static class ArmyRtsControllerService
    {
        private const int MaximumJobMutationsPerController = 8;
        private const int MaximumFollowerRouteChecksPerController = 4;
        private const int MaximumRoutePollsPerController = 64;

        private sealed class RuntimeState
        {
            internal bool RouteSubmitted;
            internal bool RouteArrived;
            internal bool TransportRouteConfirmed;
            internal bool ForceTransportRoute;
            internal int AnchorTileId = -1;
            internal int AlternateTargetTileId = -1;
            internal int LastStrategicEndpointTileId = -1;
            internal int RouteProgress;
            internal int InitialRosterCount;
            internal bool RouteImpossible;
            internal bool TargetCompletionLatched;
            internal bool DirectorForceReady;
            internal bool ReplenishmentRequested;
            internal bool ReplenishmentRetryDue;
            internal int ObservedLiving;
            internal int TargetStrength;
            internal int DirectorGeneration = -1;
            internal int DirectorFriendlyForce;
            internal int DirectorEnemyForce;
            internal bool DirectorConnectivityInitialized;
            internal bool DirectorConnectedSupply;
            internal bool DirectorConnectedCorridor;
            internal int MobilizationStatusCursor;
            internal bool MobilizationStatusCleanupPending;
            internal bool MobilizationStatusCatchupPending;
            internal bool MobilizationStatusSweepHasPendingAssembly;
            internal int FollowerRouteInstallCursor;
            internal double NextJobOwnershipRepairWorldTime =
                double.PositiveInfinity;
            internal ArmyRtsState MobilizationStatusState =
                ArmyRtsState.Idle;
            internal readonly ArmyRtsProgressDeadline ReplenishmentProgress =
                new ArmyRtsProgressDeadline();
            internal readonly ArmyRtsReplenishmentBypassLatch
                ReplenishmentBypass =
                    new ArmyRtsReplenishmentBypassLatch();
            internal readonly ArmyRtsRegroupRecoveryDeadline RegroupRecoveryProgress =
                new ArmyRtsRegroupRecoveryDeadline();
            internal double DeploymentWaitStartedWorldTime =
                double.NaN;
            internal readonly ArmyPursuitRouteState PursuitRoute =
                new ArmyPursuitRouteState();
            internal bool PursuitCompleted => PursuitRoute.Completed;
            internal readonly ArmyRtsJobAssignmentCursor JobCursor =
                new ArmyRtsJobAssignmentCursor();
            internal readonly ArmyRtsTransitionFacts TransitionFacts =
                new ArmyRtsTransitionFacts();
        }

        private sealed class PendingReplenishmentArrival
        {
            internal long ArmyId;
            internal double EnlistedRealtime;
        }

        private static readonly ArmyRtsControllerWorkIndex Controllers =
            new ArmyRtsControllerWorkIndex();
        private static readonly Dictionary<long, RuntimeState> RuntimeByArmy =
            new Dictionary<long, RuntimeState>();
        private static readonly ArmyRtsMissionIndex MissionIndex =
            new ArmyRtsMissionIndex();
        private static readonly Dictionary<long, ArmyRtsStrategicProjection>
            ReplicaProjectionByArmy =
                new Dictionary<long, ArmyRtsStrategicProjection>();
        private static readonly Dictionary<long, PendingReplenishmentArrival>
            PendingReplenishmentArrivals =
                new Dictionary<long, PendingReplenishmentArrival>();
        private static readonly Queue<long> PendingReplenishmentArrivalQueue =
            new Queue<long>();

        public static bool TryGetProjection(Army pArmy,
            out ArmyRtsStrategicProjection pProjection)
        {
            pProjection = null;
            if (pArmy?.data == null) return false;
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
            {
                return ReplicaProjectionByArmy.TryGetValue(pArmy.id,
                    out pProjection);
            }
            if (!Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return false;
            ArmyOperationalStateView operational =
                ArmyLogisticsService.GetOperationalState(pArmy);
            pProjection = new ArmyRtsStrategicProjection
            {
                State = record.State,
                Role = record.Mission.Role,
                Posture = record.Mission.Posture,
                ProposalKind = record.Mission.ProposalKind,
                WarId = record.Mission.WarId,
                FrontId = record.Mission.FrontId,
                TargetCityId = record.Mission.TargetCityId,
                Supply = operational.Supply,
                Organization = operational.Organization,
                PlayerOrder = record.Mission.PlayerOrder
            };
            return true;
        }

        public static void InstallReplicaProjection(Army pArmy,
            string pStateId, string pRtsRoleId, string pPostureId, long pWarId,
            long pFrontId, long pTargetCityId, int pSupply,
            int pOrganization, bool pPlayerOrder)
        {
            if (!AW3MultiplayerReplicaScope.IsApplying)
                throw new InvalidOperationException(
                    "Strategic replica projection requires apply scope.");
            if (pArmy?.data == null ||
                !TryParseDefined(pStateId, out ArmyRtsState state) ||
                !TryParseDefined(pRtsRoleId, out ArmyRtsRole role) ||
                !TryParseDefined(pPostureId,
                    out ArmyRtsPosture posture) ||
                pWarId < -1L || pFrontId < -1L ||
                pTargetCityId < -1L || pSupply < 0 || pSupply > 100 ||
                pOrganization < 0 || pOrganization > 100)
                throw new ArgumentException(
                    "Strategic replica projection is invalid.");
            Kingdom kingdom = SafeKingdom(pArmy);
            if (kingdom?.data == null)
                throw new ArgumentException(
                    "Strategic replica Army kingdom is invalid.");
            ReplicaProjectionByArmy[pArmy.id] =
                new ArmyRtsStrategicProjection
                {
                    State = state,
                    Role = role,
                    Posture = posture,
                    ProposalKind = state == ArmyRtsState.Hold &&
                                   role == ArmyRtsRole.Reserve &&
                                   posture == ArmyRtsPosture.Defend
                        ? ArmyRtsProposalKind.FrontHold
                        : ArmyRtsProposalKind.Attack,
                    WarId = pWarId,
                    FrontId = pFrontId,
                    TargetCityId = pTargetCityId,
                    Supply = pSupply,
                    Organization = pOrganization,
                    PlayerOrder = pPlayerOrder
                };
        }

        public static void RetainReplicaProjections(
            IReadOnlyList<long> pArmyIds)
        {
            if (!AW3MultiplayerReplicaScope.IsApplying)
                throw new InvalidOperationException(
                    "Strategic replica projection cleanup requires apply scope.");
            if (pArmyIds == null)
                throw new ArgumentNullException(nameof(pArmyIds));
            var retained = new HashSet<long>(pArmyIds);
            var stale = new List<long>();
            foreach (long armyId in ReplicaProjectionByArmy.Keys)
                if (!retained.Contains(armyId)) stale.Add(armyId);
            for (var index = 0; index < stale.Count; index++)
                ReplicaProjectionByArmy.Remove(stale[index]);
        }

        public static void ApplyDirectorSnapshot(Kingdom pKingdom,
            KingdomWarDirectorShadowSnapshot pSnapshot)
        {
            if (pKingdom?.data == null || pSnapshot == null ||
                !ArmyRtsRuntimeModeRules.ShouldPlan(
                    ArmyRtsRuntimeMode.Current)) return;
            if (pSnapshot.KingdomId != pKingdom.id ||
                !KingdomWarDirectorService.IsCurrentGeneration(pKingdom,
                    pSnapshot.Generation))
            {
                ReleaseSnapshotClaims(pSnapshot);
                if (pSnapshot.KingdomId != pKingdom.id)
                    KingdomWarDirectorService.OnArmyChanged(pKingdom);
                return;
            }
            IReadOnlyList<long> previous =
                MissionIndex.SnapshotKingdom(pKingdom.id);
            var proposed = new HashSet<long>();
            var replanKingdomIds = new HashSet<long>();
            bool replanRequired = false;
            double issuedTime = CurrentWorldTime();
            for (int i = 0; i < pSnapshot.Missions.Count; i++)
            {
                KingdomWarDirectorShadowMission proposal =
                    pSnapshot.Missions[i];
                if (proposal == null || proposal.ArmyId < 0L ||
                    proposal.TargetCityId < 0L) continue;
                Army army = FindArmy(proposal.ArmyId);
                proposed.Add(proposal.ArmyId);
                if (ShouldPreservePlayerOrder(army)) continue;
                Kingdom armyKingdom = SafeKingdom(army);
                War war = FindWar(proposal.WarId);
                City target = FindCity(proposal.TargetCityId);
                ArmyRtsObjectiveState objectiveState =
                    ArmyRtsObjectiveService.Classify(war, armyKingdom,
                        target);
                bool armyKingdomMatches = armyKingdom?.data != null && armyKingdom.id == pSnapshot.KingdomId;
                bool warMembershipValid = armyKingdomMatches &&
                    IsKingdomInWar(war, armyKingdom);
                bool proposalValid = warMembershipValid &&
                    ArmyRtsObjectiveRules.CanCommit(
                        proposal.ProposalKind, objectiveState,
                        armyKingdom.id, pSnapshot.KingdomId,
                        proposal.OpenObjectiveCount);
                if (!proposalValid)
                {
                    CoalitionWarTaskService.ReleaseObjectiveClaim(
                        proposal.WarId, proposal.ArmyId,
                        proposal.TargetCityId);
                    replanRequired = true;
                    if (armyKingdom?.data != null)
                        replanKingdomIds.Add(armyKingdom.id);
                    continue;
                }
                var mission = new ArmyRtsMission
                {
                    ArmyId = proposal.ArmyId,
                    KingdomId = pKingdom.id,
                    WarId = proposal.WarId,
                    FrontId = proposal.FrontId,
                    TargetCityId = proposal.TargetCityId,
                    ProposalKind = proposal.ProposalKind,
                    Role = proposal.Role,
                    Posture = proposal.Posture,
                    IssuedTime = issuedTime
                };
                if (ShouldRetainDirectorMission(army, mission))
                {
                    RefreshRetainedDirectorProjection(army, proposal,
                        pSnapshot.Generation);
                    continue;
                }
                AssignMission(army, mission, proposal.ConnectedSupply,
                    proposal.ConnectedCorridor, pSnapshot.Generation);
                ApplyDirectorTacticalProjection(proposal);
            }

            var stale = new List<long>();
            for (int i = 0; i < previous.Count; i++)
            {
                long armyId = previous[i];
                if (proposed.Contains(armyId)) continue;
                Army army = FindArmy(armyId);
                if (ShouldPreservePlayerOrder(army)) continue;
                bool liveArmy = IsLiveArmy(army);
                ArmyRtsMission mission = null;
                if (Controllers.TryGet(armyId,
                        out ArmyRtsControllerRecord record))
                    mission = record?.Mission;
                bool missionValid = liveArmy &&
                                    IsMissionValid(army, mission);
                City target = missionValid
                    ? FindCity(mission.TargetCityId)
                    : null;
                Kingdom kingdom = liveArmy ? SafeKingdom(army) : null;
                bool targetComplete = missionValid &&
                                      TargetComplete(army, mission, target,
                                          kingdom);
                if (ArmyRtsTransportRules.ShouldPreserveDirectorOmission(
                        ArmyRtsTransportService.HasActiveVoyage(army),
                        liveArmy, missionValid, targetComplete))
                {
                    ArmyRtsTransportService.LogDirectorOmissionPreserved(army);
                    continue;
                }
                stale.Add(armyId);
            }
            for (int i = 0; i < stale.Count; i++) Invalidate(stale[i]);
            if (!replanRequired) return;
            replanKingdomIds.Add(pKingdom.id);
            foreach (long kingdomId in replanKingdomIds)
                KingdomWarDirectorService.OnArmyChanged(
                    FindKingdom(kingdomId));
        }

        private static void ReleaseSnapshotClaims(
            KingdomWarDirectorShadowSnapshot pSnapshot)
        {
            if (pSnapshot?.Missions == null) return;
            for (int i = 0; i < pSnapshot.Missions.Count; i++)
            {
                KingdomWarDirectorShadowMission proposal =
                    pSnapshot.Missions[i];
                if (proposal == null) continue;
                CoalitionWarTaskService.ReleaseObjectiveClaim(
                    proposal.WarId, proposal.ArmyId,
                    proposal.TargetCityId);
            }
        }

        private static void RefreshRetainedDirectorProjection(Army pArmy,
            KingdomWarDirectorShadowMission pProposal,
            int pDirectorGeneration)
        {
            if (pArmy?.data == null || pProposal == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return;
            bool runtimeExists = RuntimeByArmy.TryGetValue(pArmy.id,
                out RuntimeState runtime);
            if (!runtimeExists)
            {
                runtime = new RuntimeState
                {
                    InitialRosterCount = SafeUnitCount(pArmy)
                };
                RuntimeByArmy[pArmy.id] = runtime;
            }
            runtime.DirectorForceReady = pProposal.ForceReady;
            runtime.DirectorFriendlyForce = Math.Max(0,
                pProposal.FriendlyForce);
            runtime.DirectorEnemyForce = Math.Max(0, pProposal.EnemyForce);
            runtime.DirectorGeneration = pDirectorGeneration;
            bool connectivityChanged =
                !runtime.DirectorConnectivityInitialized ||
                runtime.DirectorConnectedSupply !=
                pProposal.ConnectedSupply ||
                runtime.DirectorConnectedCorridor !=
                pProposal.ConnectedCorridor;
            runtime.DirectorConnectivityInitialized = true;
            runtime.DirectorConnectedSupply = pProposal.ConnectedSupply;
            runtime.DirectorConnectedCorridor =
                pProposal.ConnectedCorridor;
            if (connectivityChanged)
                ArmyLogisticsService.OnMissionAssigned(pArmy,
                    record.Mission, pProposal.ConnectedSupply,
                    pProposal.ConnectedCorridor);
        }

        private static void ApplyDirectorTacticalProjection(
            KingdomWarDirectorShadowMission pProposal)
        {
            if (pProposal == null || !RuntimeByArmy.TryGetValue(
                    pProposal.ArmyId, out RuntimeState runtime)) return;
            runtime.DirectorForceReady = pProposal.ForceReady;
            runtime.DirectorFriendlyForce = Math.Max(0,
                pProposal.FriendlyForce);
            runtime.DirectorEnemyForce = Math.Max(0, pProposal.EnemyForce);
        }

        private static bool IsKingdomInWar(War pWar,
            Kingdom pKingdom)
        {
            if (pWar?.data == null || pKingdom?.data == null) return false;
            try
            {
                return !pWar.hasEnded() &&
                       (pWar.isAttacker(pKingdom) ||
                        pWar.isDefender(pKingdom));
            }
            catch { return false; }
        }

        private static bool ShouldRetainDirectorMission(Army pArmy,
            ArmyRtsMission pProposed)
        {
            if (pArmy?.data == null || pProposed == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord current) ||
                current?.Mission == null) return false;
            ArmyRtsMission existing = current.Mission;
            City currentTarget = FindCity(existing.TargetCityId);
            Kingdom kingdom = SafeKingdom(pArmy);
            bool missionValid = IsMissionValid(pArmy, existing);
            bool targetComplete = missionValid && TargetComplete(pArmy,
                existing, currentTarget, kingdom);
            bool targetCoolingDown = missionValid &&
                ArmyStallWatchdogService.IsTargetCoolingDown(
                    existing.KingdomId, existing.TargetCityId);
            bool currentHomelandEmergency = IsHomelandEmergencyMission(
                existing, kingdom);
            bool proposedHomelandEmergency = IsHomelandEmergencyMission(
                pProposed, kingdom);
            if (ArmyRtsTransportRules.ShouldRetainActiveVoyageMission(
                    ArmyRtsTransportService.HasActiveVoyage(pArmy),
                    missionValid, targetComplete, targetCoolingDown,
                    currentHomelandEmergency,
                    proposedHomelandEmergency))
                return true;
            return KingdomWarDirectorRules.ShouldRetainMissionLease(
                ArmyRtsControllerRules.SameTransportObjective(existing,
                    pProposed) &&
                existing.ProposalKind == pProposed.ProposalKind,
                missionValid, targetComplete,
                targetCoolingDown,
                existing.Posture == ArmyRtsPosture.Retreat,
                currentHomelandEmergency,
                proposedHomelandEmergency,
                existing.ProposalKind == ArmyRtsProposalKind.FrontHold);
        }

        private static bool IsHomelandEmergencyMission(
            ArmyRtsMission pMission, Kingdom pKingdom)
        {
            if (pMission == null || pMission.Role != ArmyRtsRole.Defense ||
                pKingdom?.data == null) return false;
            City target = FindCity(pMission.TargetCityId);
            return target?.data != null && target.kingdom == pKingdom;
        }

        public static void AssignMission(Army pArmy,
            ArmyRtsMission pMission)
        {
            bool corridor = ResolveInitialMissionCorridor(pArmy, pMission);
            AssignMission(pArmy, pMission, pConnectedSupply: corridor,
                pInCorridor: corridor);
        }

        private static bool ResolveInitialMissionCorridor(Army pArmy,
            ArmyRtsMission pMission)
        {
            if (pArmy?.data == null || pMission == null) return false;
            Kingdom kingdom = SafeKingdom(pArmy);
            City target = FindCity(pMission.TargetCityId);
            return KingdomWarDirectorService.IsConnectedCorridor(
                FindWar(pMission.WarId), target, kingdom);
        }

        private static void AssignMission(Army pArmy,
            ArmyRtsMission pMission, bool pConnectedSupply,
            bool pInCorridor, int pDirectorGeneration = -1)
        {
            if (pArmy?.data == null || pMission == null) return;
            Kingdom missionKingdom = SafeKingdom(pArmy);
            bool missionPublishable = WarArmyReturnRules.
                IsMissionPublishable(IsLiveArmy(pArmy), pArmy.id,
                    pMission.ArmyId,
                    missionKingdom?.data != null &&
                    missionKingdom.id == pMission.KingdomId,
                    pMission.WarId, pMission.TargetCityId,
                    IsMissionValid(pArmy, pMission));
            if (!missionPublishable) return;
            AWArmyService.EnsureOrdinaryNativeName(pArmy);
            if (pMission.IssuedTime < 0d ||
                double.IsNaN(pMission.IssuedTime) ||
                double.IsInfinity(pMission.IssuedTime))
                pMission.IssuedTime = CurrentWorldTime();
            ArmyRtsMission previousMission = null;
            if (Controllers.TryGet(pMission.ArmyId,
                    out ArmyRtsControllerRecord previousRecord))
                previousMission = ArmyRtsControllerRules.CopyMission(
                    previousRecord.Mission);
            pMission.TargetStrength = ArmyRtsRules.
                ResolveMissionTargetStrength(pMission.TargetStrength,
                    Math.Max(previousMission?.TargetStrength ?? 0,
                        StandingArmyService.TargetStrength(pArmy,
                            SafeKingdom(pArmy))),
                    SafeUnitCount(pArmy));
            bool resetTransport = ArmyRtsControllerRules.
                ShouldResetTransport(previousMission, pMission,
                    ArmyRtsTransportService.HasEmbarkedMembers(pArmy));
            bool resetOperationalProgress = ArmyRtsControllerRules.
                ShouldResetOperationalProgress(previousMission, pMission);
            bool runtimeExists = RuntimeByArmy.TryGetValue(pArmy.id,
                out RuntimeState previousRuntime);
            bool changed = Controllers.AssignMission(pMission);
            MissionIndex.Upsert(pMission);
            ArmyRtsWarLifecycleService.OnMissionAssigned(pArmy, pMission);
            bool controllerPublished = Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord assignedRecord) &&
                assignedRecord?.Mission != null &&
                ArmyRtsControllerRules.SameStrategicIntent(
                    assignedRecord.Mission, pMission);
            bool indexPublished = MissionIndex.Matches(pMission);
            if (WarArmyReturnRules.ShouldCancelForPublishedMission(
                    missionPublishable, controllerPublished, indexPublished))
                WarArmyReturnService.Cancel(pArmy.id);
            bool clearSharedRoute = ArmyRtsControllerRules.
                ShouldClearSharedRoute(resetOperationalProgress,
                    runtimeExists,
                    previousRuntime?.DirectorGeneration ?? -1,
                    pDirectorGeneration);
            if (clearSharedRoute)
            {
                AWArmyMarchService.ClearArmy(pArmy);
                if (previousRuntime != null)
                {
                    previousRuntime.RouteSubmitted = false;
                    previousRuntime.RouteArrived = false;
                    previousRuntime.RouteProgress = 0;
                    previousRuntime.AnchorTileId = -1;
                }
            }
            if (changed && resetOperationalProgress)
            {
                if (resetTransport)
                    ArmyRtsTransportService.ReleaseArmy(pArmy);
                RuntimeByArmy[pArmy.id] = new RuntimeState
                {
                    InitialRosterCount = SafeUnitCount(pArmy)
                };
            }
            else if (!RuntimeByArmy.ContainsKey(pArmy.id))
                RuntimeByArmy[pArmy.id] = new RuntimeState
                {
                    InitialRosterCount = SafeUnitCount(pArmy)
                };
            RuntimeByArmy[pArmy.id].DirectorGeneration =
                pDirectorGeneration;
            if (changed)
            {
                LogMissionChanged(pArmy, previousMission, pMission,
                    "director_assigned");
                ArmyRtsPlanSnapshotService.OnMissionChanged(pArmy,
                    pMission, "mission_changed");
            }
            ArmyLogisticsService.OnMissionAssigned(pArmy, pMission,
                pConnectedSupply, pInCorridor);
            RuntimeState assignedRuntime = RuntimeByArmy[pArmy.id];
            assignedRuntime.DirectorConnectivityInitialized = true;
            assignedRuntime.DirectorConnectedSupply = pConnectedSupply;
            assignedRuntime.DirectorConnectedCorridor = pInCorridor;
            ArmyStallWatchdogService.OnMissionAssigned(pArmy,
                resetOperationalProgress);
            if (ArmyRtsRuntimeModeRules.ShouldCommit(
                    ArmyRtsRuntimeMode.Current))
                ArmyMissionPersistence.Persist(pArmy, pMission);
        }

        public static bool TryGetMission(Army pArmy,
            out ArmyRtsMission pMission)
        {
            pMission = null;
            if (pArmy?.data != null &&
                AW3MultiplayerReplicaScope.IsReplicaSession &&
                ReplicaProjectionByArmy.TryGetValue(pArmy.id,
                    out ArmyRtsStrategicProjection projection))
            {
                Kingdom kingdom = SafeKingdom(pArmy);
                if (kingdom?.data == null) return false;
                pMission = new ArmyRtsMission
                {
                    ArmyId = pArmy.id,
                    KingdomId = kingdom.id,
                    WarId = projection.WarId,
                    FrontId = projection.FrontId,
                    TargetCityId = projection.TargetCityId,
                    ProposalKind = projection.ProposalKind,
                    Role = projection.Role,
                    Posture = projection.Posture,
                    PlayerOrder = projection.PlayerOrder
                };
                return pMission.TargetCityId >= 0L;
            }
            if (pArmy?.data == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return false;
            pMission = ArmyRtsControllerRules.CopyMission(record.Mission);
            return pMission != null;
        }

        public static bool HasValidMission(Army pArmy)
        {
            if (pArmy?.data == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return false;
            return IsMissionValid(pArmy, record.Mission);
        }

        public static bool TryGetMissionTarget(Army pArmy,
            out WorldTile pTarget)
        {
            pTarget = null;
            if (!TryGetMission(pArmy, out ArmyRtsMission mission))
                return false;
            City targetCity = FindCity(mission.TargetCityId);
            try { pTarget = targetCity?.getTile(); }
            catch { pTarget = null; }
            return pTarget != null;
        }

        public static bool ShouldPreservePlayerOrder(Army pArmy)
        {
            if (pArmy?.data == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null ||
                !record.Mission.PlayerOrder) return false;
            RuntimeByArmy.TryGetValue(pArmy.id, out RuntimeState runtime);
            City target = FindCity(record.Mission.TargetCityId);
            ArmyOperationalStateView operational =
                ArmyLogisticsService.GetOperationalState(pArmy);
            int initial = Math.Max(1,
                runtime?.InitialRosterCount ?? SafeUnitCount(pArmy));
            int current = Math.Max(0, SafeUnitCount(pArmy));
            int lossPercent = current >= initial
                ? 0
                : (int)Math.Min(100L,
                    (initial - current) * 100L / initial);
            var facts = new ArmyPlayerOrderInterruptionFacts(
                targetExists: IsLiveCity(target),
                routeImpossible: runtime?.RouteImpossible ?? false,
                supply: operational.Supply,
                rosterLossPercent: lossPercent);
            return !ArmyRtsCommandRules.ShouldInterruptPlayerOrder(
                playerOrder: true, facts);
        }

        public static void MarkRouteImpossible(long pArmyId)
        {
            if (RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState runtime))
            {
                runtime.RouteImpossible = true;
                runtime.RouteSubmitted = false;
                runtime.RouteArrived = false;
                runtime.TransportRouteConfirmed = false;
                runtime.ForceTransportRoute = false;
                runtime.AnchorTileId = -1;
                runtime.AlternateTargetTileId = -1;
                runtime.PursuitRoute.Reset();
                Controllers.SetState(pArmyId, ArmyRtsState.Retreat);
                ArmyRouteProviderService.Cancel(pArmyId,
                    ArmyRouteCancelReason.TargetReplaced);
                AWArmyMarchService.ClearArmy(pArmyId);
                Controllers.Requeue(pArmyId);
            }
        }

        public static void ProcessFrame()
        {
            ArmyRtsMode mode = ArmyRtsRuntimeMode.Current;
            if (!ArmyRtsRuntimeModeRules.ShouldPlan(mode)) return;
            ProcessPendingReplenishmentArrivals();
            ArmyRtsTransportService.ProcessFrame();
            IReadOnlyList<long> batch = Controllers.Take(
                ArmyRtsControllerRules.MaximumControllersPerFrame);
            for (int i = 0; i < batch.Count; i++)
                ProcessOne(batch[i], mode);
        }

        internal static void TrackReplenishmentArrival(Actor pActor,
            Army pArmy)
        {
            bool missionActive = pArmy?.data != null &&
                                 HasActiveMission(pArmy.id);
            bool wartimeEmergency = pArmy?.data != null &&
                MilitaryEmergencyService.HasAny(SafeKingdom(pArmy));
            if (!ArmyRtsReplenishmentArrivalRules.ShouldTrackArrival(
                    ArmyRtsRuntimeMode.ShouldCommit,
                    IsLiveWarriorActor(pActor),
                    pActor?.army == pArmy, IsCaptain(pActor, pArmy),
                    missionActive, wartimeEmergency)) return;
            long actorId = pActor.data.id;
            if (actorId < 0L) return;
            if (missionActive) RecoverFormationMember(pArmy.id, actorId);
            if (TryTeleportReinforcementMember(pArmy.id, actorId,
                    pAllowCaptainCombat: true))
            {
                ReleaseReplenishmentForDeparture(pArmy.id);
                return;
            }
            PendingReplenishmentArrivals[actorId] =
                new PendingReplenishmentArrival
                {
                    ArmyId = pArmy.id,
                    EnlistedRealtime = CurrentRealtime()
                };
            PendingReplenishmentArrivalQueue.Enqueue(actorId);
        }

        private static void ProcessPendingReplenishmentArrivals()
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                PendingReplenishmentArrivals.Count == 0 ||
                PendingReplenishmentArrivalQueue.Count == 0 ||
                World.world == null || World.world.isPaused()) return;
            double now = CurrentRealtime();
            int limit = Math.Min(
                ArmyRtsReplenishmentArrivalRules.MaximumArrivalChecksPerFrame,
                PendingReplenishmentArrivalQueue.Count);
            for (int i = 0; i < limit; i++)
            {
                long actorId = PendingReplenishmentArrivalQueue.Dequeue();
                if (!PendingReplenishmentArrivals.TryGetValue(actorId,
                        out PendingReplenishmentArrival pending)) continue;
                ArmyRtsReplenishmentArrivalAction action =
                    ResolveReplenishmentArrivalAction(actorId, pending, now);
                if (action == ArmyRtsReplenishmentArrivalAction.Teleport &&
                    TryTeleportReinforcementMember(pending.ArmyId, actorId,
                        pAllowCaptainCombat: false))
                {
                    ReleaseReplenishmentForDeparture(pending.ArmyId);
                    action = ArmyRtsReplenishmentArrivalAction.Complete;
                }
                if (action == ArmyRtsReplenishmentArrivalAction.Wait ||
                    action == ArmyRtsReplenishmentArrivalAction.Teleport)
                {
                    PendingReplenishmentArrivalQueue.Enqueue(actorId);
                    continue;
                }
                PendingReplenishmentArrivals.Remove(actorId);
            }
        }

        private static ArmyRtsReplenishmentArrivalAction
            ResolveReplenishmentArrivalAction(long pActorId,
                PendingReplenishmentArrival pPending, double pNow)
        {
            Army army = FindArmy(pPending?.ArmyId ?? -1L);
            Actor actor = FindActor(pActorId);
            Actor captain = SafeCaptain(army);
            bool missionActive = army?.data != null &&
                                 HasActiveMission(army.id);
            bool wartimeEmergency = army?.data != null &&
                MilitaryEmergencyService.HasAny(SafeKingdom(army));
            bool targetArmyActive = army?.data != null &&
                (missionActive || wartimeEmergency);
            bool memberStillEligible =
                ArmyRtsReplenishmentArrivalRules.ShouldTrackArrival(
                    ArmyRtsRuntimeMode.ShouldCommit,
                    IsLiveWarriorActor(actor), actor?.army == army,
                    IsCaptain(actor, army), missionActive,
                    wartimeEmergency) &&
                (!missionActive || ArmyFormationService.HasFollower(actor));
            bool atFormation = memberStillEligible &&
                (missionActive
                    ? ArmyFormationService.IsInsideLooseEscort(actor)
                    : actor?.current_tile == captain?.current_tile);
            bool combatActive = memberStillEligible &&
                (HasImmediateCombatPriority(actor) ||
                 HasImmediateCombatPriority(captain));
            bool transportActive = memberStillEligible &&
                (ArmyRtsTransportService.HasActiveVoyage(army) ||
                 actor.is_inside_boat ||
                 ArmyRtsTransportService.OwnsActorTask(actor));
            double elapsed = Math.Max(0d, pNow -
                Math.Max(0d, pPending?.EnlistedRealtime ?? pNow));
            return ArmyRtsReplenishmentArrivalRules.ResolveAction(
                tracked: pPending != null,
                targetArmyActive: targetArmyActive,
                memberStillEligible: memberStillEligible,
                atFormation: atFormation,
                combatActive: combatActive,
                transportActive: transportActive,
                elapsedRealtime: elapsed);
        }

        private static void ReleaseReplenishmentForDeparture(long pArmyId)
        {
            if (!RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState runtime) || !Controllers.TryGet(
                    pArmyId, out ArmyRtsControllerRecord record)) return;
            Army army = FindArmy(pArmyId);
            int living = SafeUnitCount(army);
            int target = ResolveMissionTargetStrength(army,
                SafeKingdom(army), record.Mission);
            bool departureStrengthReady = ArmyRtsRules.HasDepartureStrength(
                living, target, ArmyLogisticsRules.HasMinimumOperationalForce(
                    living), replenishmentBypassActive: false);
            if (!ArmyRtsReplenishmentArrivalRules.
                    ShouldReleaseReplenishmentAfterArrival(
                        arrivalTeleported: true,
                        departureStrengthReady: departureStrengthReady))
                return;
            runtime.ReplenishmentRequested = false;
            runtime.ReplenishmentRetryDue = false;
            runtime.ReplenishmentProgress.Reset();
            Controllers.Requeue(pArmyId);
        }

        internal static void OnReplenishmentOperationCompleted(Army pArmy)
        {
            if (pArmy?.data == null || !RuntimeByArmy.TryGetValue(
                    pArmy.id, out RuntimeState runtime)) return;
            runtime.ReplenishmentRequested = false;
            runtime.ReplenishmentRetryDue = false;
            runtime.ReplenishmentProgress.Reset();
            if (Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) &&
                ArmyRtsWarLifecycleService.TryGet(
                    record?.Mission?.WarId ?? -1L, pArmy.id,
                    out ArmyRtsWarLifecycleRecord lifecycle) &&
                lifecycle.Phase == ArmyRtsWarPhase.Replenishing &&
                ArmyRtsWarLifecycleRules.ShouldResume(SafeUnitCount(pArmy),
                    lifecycle.BaselineStrength))
            {
                ArmyRtsMission previous = lifecycle.PreviousOffensiveMission;
                lifecycle.ReplenishmentCityId = -1L;
                ArmyRtsWarLifecycleService.TrySetPhase(
                    lifecycle.WarId, pArmy.id,
                    ArmyRtsWarPhase.StrategicMovement);
                if (previous != null && IsMissionValid(pArmy, previous))
                {
                    previous.IssuedTime = CurrentWorldTime();
                    AssignMission(pArmy, previous);
                    return;
                }
                Kingdom kingdom = SafeKingdom(pArmy);
                Invalidate(pArmy.id);
                KingdomWarDirectorService.QueueArmyChanged(kingdom);
                return;
            }
            Controllers.Requeue(pArmy.id);
        }

        internal static bool TryGetWartimeRecovery(Army pArmy,
            out int pBaseline, out long pWarId)
        {
            pBaseline = 0;
            pWarId = -1L;
            if (pArmy?.data == null || !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                !ArmyRtsWarLifecycleService.TryGet(
                    record?.Mission?.WarId ?? -1L, pArmy.id,
                    out ArmyRtsWarLifecycleRecord lifecycle) ||
                lifecycle.Phase != ArmyRtsWarPhase.Replenishing)
                return false;
            pBaseline = lifecycle.BaselineStrength;
            pWarId = lifecycle.WarId;
            return pBaseline > 0 && pWarId >= 0L;
        }

        internal static bool CanGenerateWartimeReplacements(Army pArmy,
            City pSourceCity, long pWarId)
        {
            if (pArmy?.data == null || pSourceCity?.data == null) return false;
            Actor captain = SafeCaptain(pArmy);
            City currentCity = null;
            try { currentCity = captain?.current_tile?.zone?.city; }
            catch { }
            Kingdom kingdom = SafeKingdom(pArmy);
            War war = FindWar(pWarId);
            bool controlled = currentCity == pSourceCity &&
                (pSourceCity.kingdom == kingdom ||
                 CityAttackZoneService.IsControlledBySide(war,
                     pSourceCity, kingdom));
            bool hostile = controlled && CityAttackZoneService.
                HasHostileMilitaryInside(war, pSourceCity, kingdom);
            bool combat = hostile || HasImmediateCombatPriority(captain);
            bool transport = ArmyRtsTransportService.HasActiveVoyage(pArmy) ||
                             captain?.is_inside_boat == true ||
                             ArmyRtsTransportService.OwnsActorTask(captain);
            bool movement = captain?.is_moving == true ||
                            AWPathMovementBridge.HasOwnership(captain) ||
                            AWArmyMarchService.HasActiveMarch(captain);
            return ArmyRtsWarLifecycleRules.CanReplenishInCurrentCity(
                       controlled, hostile) &&
                   ArmyRtsWarLifecycleRules.CanGenerateReplacements(
                       combat, transport, movement);
        }

        private static void ClearIneligibleReplenishmentState(Army pArmy,
            RuntimeState pRuntime)
        {
            if (pArmy?.data == null ||
                ArmyReplenishmentOperationService.CanUseReservePool(pArmy))
                return;
            pArmy.data.get(
                LineageKeys.ARMY_REPLENISHMENT_OPERATION_VERSION,
                out int operationVersion, 0);
            if (ArmyReplenishmentOperationRules.
                    ShouldClearIneligibleOperation(
                        operationVersion != 0,
                        canUseReservePool: false))
                ArmyReplenishmentOperationService.Clear(pArmy);
            RemovePendingReplenishmentArrivals(pArmy.id);
            if (pRuntime == null) return;
            pRuntime.ReplenishmentRequested = false;
            pRuntime.ReplenishmentRetryDue = false;
            pRuntime.ReplenishmentProgress.Reset();
            pRuntime.ReplenishmentBypass.Update(
                replenishmentWindow: false,
                needsReplenishment: false,
                minimumForceReady: false,
                bypassTriggered: false);
        }

        public static bool TryGetCaptainTarget(Actor pActor,
            out WorldTile pTarget)
        {
            pTarget = null;
            Army army = pActor?.army;
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
                return TryGetMissionTarget(army, out pTarget);
            if (!HasCaptainMission(pActor) || army?.data == null ||
                !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record) ||
                !RuntimeByArmy.TryGetValue(army.id,
                    out RuntimeState runtime)) return false;
            bool requiresEscort =
                (record.State == ArmyRtsState.March ||
                 record.State == ArmyRtsState.Deploy ||
                 record.State == ArmyRtsState.Assault ||
                 record.State == ArmyRtsState.Pursue) &&
                record.Mission?.ProposalKind !=
                    ArmyRtsProposalKind.Retreat;
            bool transportOwnsMovement =
                ArmyRtsTransportService.HasActiveVoyage(army) ||
                pActor.is_inside_boat ||
                ArmyRtsTransportService.OwnsActorTask(pActor);
            ArmyFormationCounters escort = ArmyFormationService.
                GetIncrementalFollowerCounters(army);
            bool captainPresent = pActor?.data != null &&
                                  pActor.isAlive() && !pActor.isRekt() &&
                                  pActor.current_tile?.data != null;
            if (!ArmyRtsRules.CanCaptainAdvanceWithEscort(
                    requiresEscort, SafeUnitCount(army), escort.Rallied,
                    captainPresent, HasImmediateCombatPriority(pActor),
                    transportOwnsMovement)) return false;
            if (ArmyRtsTransportService.TryGetTarget(army,
                    out WorldTile activeTransportTarget) &&
                (ArmyRtsTransportRules.ShouldUseTransportBeforeLandRoute(
                    ArmyRtsRuntimeMode.ShouldCommit,
                    strategicMovementReady: true,
                    actorTileValid: pActor.current_tile?.data != null,
                    targetTileValid:
                        activeTransportTarget?.data != null,
                    sameIsland: SafeSameIsland(pActor.current_tile,
                        activeTransportTarget),
                    transportRouteConfirmed: true) ||
                 ArmyRtsTransportRules.ShouldUseSelectedTransport(
                    ArmyRtsRuntimeMode.ShouldCommit,
                    strategicMovementReady: true,
                    actorTileValid: pActor.current_tile?.data != null,
                    targetTileValid:
                        activeTransportTarget?.data != null,
                    transportSelected: runtime.ForceTransportRoute)))
            {
                pTarget = activeTransportTarget;
                return true;
            }
            pTarget = FindTile(runtime.AnchorTileId);
            if (pTarget != null) return true;
            if (record.State == ArmyRtsState.Pursue)
            {
                pTarget = FindTile(runtime.PursuitRoute.EndpointTileId);
                return pTarget != null;
            }
            City missionTargetCity = FindCity(record.Mission.TargetCityId);
            WorldTile missionTarget = ResolveStableStrategicEndpoint(army,
                missionTargetCity, runtime);
            bool strategicMovementReady =
                record.State == ArmyRtsState.March ||
                record.State == ArmyRtsState.Retreat;
            bool sameMissionIsland = SafeSameIsland(pActor.current_tile,
                missionTarget);
            if (ArmyRtsTransportRules.ShouldUseTransportBeforeLandRoute(
                    ArmyRtsRuntimeMode.ShouldCommit,
                    strategicMovementReady,
                    actorTileValid: pActor.current_tile?.data != null,
                    targetTileValid: missionTarget?.data != null,
                    sameIsland: sameMissionIsland,
                    transportRouteConfirmed:
                        runtime.TransportRouteConfirmed) ||
                ArmyRtsTransportRules.ShouldUseSelectedTransport(
                    ArmyRtsRuntimeMode.ShouldCommit,
                    strategicMovementReady,
                    actorTileValid: pActor.current_tile?.data != null,
                    targetTileValid: missionTarget?.data != null,
                    transportSelected: runtime.ForceTransportRoute))
            {
                pTarget = missionTarget;
                return true;
            }
            if (!ArmyRtsControllerRules.
                    ShouldUseMissionCityAsMovementTarget(record.State,
                        hasRouteAnchor: false)) return false;
            pTarget = missionTarget;
            return pTarget != null;
        }

        public static bool ShouldHandleCaptainTransport(Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null) return false;
            bool activeVoyage = ArmyRtsTransportService.HasActiveVoyage(
                army);
            bool routeConfirmed = RuntimeByArmy.TryGetValue(army.id,
                                      out RuntimeState runtime) &&
                                  runtime.TransportRouteConfirmed;
            return ArmyRtsTransportRules.ShouldRunActorTransportHandler(
                activeVoyage, routeConfirmed);
        }

        public static ArmyFollowerTargetResult ResolveFollowerTarget(
            Actor pActor,
            out WorldTile pTarget)
        {
            Army army = pActor?.army;
            if (!HasFollowerMission(pActor))
            {
                pTarget = null;
                return ArmyFollowerTargetResult.Unavailable;
            }
            ArmyFollowerTargetResult sharedResult =
                AWArmyMarchService.ResolveFollowerTarget(pActor,
                    out pTarget);
            if (sharedResult != ArmyFollowerTargetResult.Unavailable)
                return sharedResult;
            bool formationTargetAvailable =
                ArmyFormationService.TryGetFollowerTarget(pActor,
                    out pTarget);
            bool formationTargetReached = formationTargetAvailable &&
                pTarget == pActor?.current_tile;
            return ArmySharedPathRules.ResolveFollowerTargetSource(
                sharedResult, formationTargetAvailable,
                formationTargetReached);
        }

        public static bool TryGetFollowerTarget(Actor pActor,
            out WorldTile pTarget)
        {
            return ResolveFollowerTarget(pActor, out pTarget) ==
                   ArmyFollowerTargetResult.Move;
        }

        public static bool HasCaptainMission(Actor pActor)
        {
            Army army = pActor?.army;
            bool missionActive = army?.data != null &&
                                 Controllers.TryGet(army.id, out _);
            return IsCaptain(pActor, army) &&
                   ShouldOwnMilitaryActor(pActor, missionActive);
        }

        public static bool HasFrontHoldMission(Actor pActor)
        {
            Army army = pActor?.army;
            return HasCaptainMission(pActor) && army?.data != null &&
                   Controllers.TryGet(army.id,
                       out ArmyRtsControllerRecord record) &&
                   record?.Mission?.ProposalKind ==
                   ArmyRtsProposalKind.FrontHold;
        }

        public static bool HasFollowerMission(Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                IsCaptain(pActor, army) ||
                !ArmyFormationService.HasFollower(pActor)) return false;
            bool controllerActive = Controllers.TryGet(army.id,
                out ArmyRtsControllerRecord record);
            bool missionActive = controllerActive ||
                                 ArmyDeploymentService.
                                     HasActiveAssignment(pActor);
            if (!ShouldOwnMilitaryActor(pActor, missionActive)) return false;
            bool transportOwned = pActor.is_inside_boat ||
                ArmyRtsTransportService.OwnsActorTask(pActor);
            return ArmyFormationRules.ShouldOwnEscortFollow(
                controllerActive ? record.State : ArmyRtsState.Rally,
                HasImmediateCombatPriority(pActor), transportOwned);
        }

        public static bool OwnsLiveActor(Actor pActor)
        {
            Army army = pActor?.army;
            bool missionActive = army?.data != null &&
                                 Controllers.TryGet(army.id, out _);
            return ShouldOwnMilitaryActor(pActor, missionActive);
        }

        public static bool TrySetFormationAnchor(Army pArmy,
            WorldTile pAnchor)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit || pArmy?.data == null ||
                pAnchor?.data == null) return false;
            ArmyFormationService.SetAnchor(pArmy, pAnchor);
            return true;
        }

        public static void OnArmyRosterChanged(Army pArmy)
        {
            if (pArmy?.data == null ||
                !Controllers.TryGet(pArmy.id, out _) ||
                !RuntimeByArmy.TryGetValue(pArmy.id,
                    out RuntimeState runtime)) return;
            runtime.JobCursor.Reopen();
            runtime.FollowerRouteInstallCursor = 0;
            if (ArmyRtsMobilizationStatusRules.RequiresSpeedStatus(
                    runtime.MobilizationStatusState))
            {
                runtime.MobilizationStatusCursor = 0;
                runtime.MobilizationStatusCatchupPending = true;
                runtime.MobilizationStatusSweepHasPendingAssembly = false;
            }
            Controllers.Requeue(pArmy.id);
        }

        public static void OnTransportCompleted(Army pArmy)
        {
            ResetTransportRouteRuntime(pArmy);
        }

        public static void OnTransportCancelled(Army pArmy)
        {
            ResetTransportRouteRuntime(pArmy);
        }

        private static void ResetTransportRouteRuntime(Army pArmy)
        {
            if (pArmy?.data == null ||
                !RuntimeByArmy.TryGetValue(pArmy.id,
                    out RuntimeState runtime)) return;
            runtime.RouteSubmitted = false;
            runtime.RouteArrived = false;
            runtime.TransportRouteConfirmed = false;
            runtime.ForceTransportRoute = false;
            runtime.AnchorTileId = -1;
            Controllers.Requeue(pArmy.id);
        }

        public static void OnCaptainChanged(Army pArmy)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit || pArmy?.data == null ||
                !Controllers.TryGet(pArmy.id, out _) ||
                !RuntimeByArmy.TryGetValue(pArmy.id,
                    out RuntimeState runtime)) return;
            runtime.RouteImpossible = false;
            runtime.JobCursor.Reopen();
            runtime.FollowerRouteInstallCursor = 0;
            ArmyFormationService.RemoveArmy(pArmy.id);
            RequestRouteReplan(pArmy.id, pAlternateEndpoint: false);
        }

        public static bool AssignRetreatMission(Army pArmy,
            City pRetreatCity)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit || pArmy?.data == null ||
                pRetreatCity?.data == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record)) return false;
            ArmyRtsMission mission = ArmyRtsControllerRules.CopyMission(
                record.Mission);
            mission.TargetCityId = pRetreatCity.id;
            mission.ProposalKind = ArmyRtsProposalKind.Retreat;
            mission.Posture = ArmyRtsPosture.Retreat;
            mission.PlayerOrder = false;
            mission.IssuedTime = CurrentWorldTime();
            AssignMission(pArmy, mission, pConnectedSupply: true,
                pInCorridor: true);
            return true;
        }

        public static void RecoverUnavailableRetreat(Army pArmy)
        {
            if (pArmy?.data == null) return;
            Kingdom kingdom = SafeKingdom(pArmy);
            Invalidate(pArmy.id);
            KingdomWarDirectorService.OnArmyChanged(kingdom);
        }

        public static bool TryGetRetreatAnchor(Army pArmy,
            out WorldTile pAnchor)
        {
            pAnchor = null;
            if (pArmy?.data == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record)) return false;
            if (RuntimeByArmy.TryGetValue(pArmy.id,
                    out RuntimeState runtime))
                pAnchor = FindTile(runtime.AnchorTileId);
            if (pAnchor != null) return true;
            City target = FindCity(record.Mission.TargetCityId);
            try { pAnchor = target?.getTile(); }
            catch { pAnchor = null; }
            return pAnchor != null;
        }

        public static void OnTargetCompleted(City pTargetCity)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                pTargetCity?.data == null) return;
            IReadOnlyList<long> armyIds = MissionIndex.SnapshotTarget(pTargetCity.id);
            for (int i = 0; i < armyIds.Count; i++)
            {
                long armyId = armyIds[i];
                if (!Controllers.TryGet(armyId,
                        out ArmyRtsControllerRecord record) ||
                    record?.Mission == null ||
                    !ArmyRtsObjectiveRules.ShouldUseObjectiveCompletion(
                        record.Mission.ProposalKind,
                        record.Mission.Role) ||
                    !RuntimeByArmy.TryGetValue(armyId,
                        out RuntimeState runtime)) continue;
                Army army = FindArmy(armyId);
                Kingdom kingdom = SafeKingdom(army);
                if (!IsCompletionEventForMission(army, record.Mission,
                        pTargetCity, kingdom)) continue;
                runtime.TargetCompletionLatched = true;
                ClearCompletedObjectiveRuntime(army, runtime);
                LogMissionChanged(army, record.Mission, null,
                    "target_control_event");
                CoalitionWarTaskService.ReleaseObjectiveClaim(
                    record.Mission.WarId, armyId,
                    record.Mission.TargetCityId);
                KingdomWarDirectorService.OnArmyChanged(kingdom);
                Controllers.Requeue(armyId);
            }
        }

        public static bool TryGetWatchdogSample(long pArmyId,
            out ArmyWatchdogControllerSample pSample)
        {
            pSample = null;
            if (!HasActiveMission(pArmyId) ||
                !Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord record) ||
                !RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState runtime)) return false;
            Army army = FindArmy(pArmyId);
            Actor captain = SafeCaptain(army);
            bool captainAvailable = captain?.current_tile != null;
            ArmyFormationService.TryGetAnchor(army, out WorldTile formationAnchor);
            bool formationAvailable = formationAnchor?.data != null;
            bool deploymentProgressExpected = ArmyStallWatchdogRules.
                ShouldExpectFormationProgress(record.State,
                    runtime.TargetCompletionLatched);
            Actor strandedMember = null;
            bool strandedFormationMemberAvailable =
                deploymentProgressExpected &&
                ArmyFormationService.TryGetUndeployedMember(army,
                    out strandedMember);
            ArmyWatchdogPositionSource positionSource =
                ArmyStallWatchdogRules.SelectMissionPositionSource(
                    captainAvailable, formationAvailable,
                    formationMemberAvailable:
                        strandedFormationMemberAvailable,
                    formationProgressExpected:
                        deploymentProgressExpected);
            if (positionSource == ArmyWatchdogPositionSource.None)
                return false;
            Actor sampledActor = captain;
            double positionX;
            double positionY;
            if (positionSource ==
                ArmyWatchdogPositionSource.FormationMember)
            {
                sampledActor = strandedMember;
                positionX = sampledActor.current_position.x;
                positionY = sampledActor.current_position.y;
            }
            else if (positionSource == ArmyWatchdogPositionSource.Captain)
            {
                positionX = captain.current_position.x;
                positionY = captain.current_position.y;
            }
            else
            {
                positionX = formationAnchor.x;
                positionY = formationAnchor.y;
            }
            bool destinationPublished =
                FindTile(runtime.AnchorTileId)?.data != null;
            bool transportOwned =
                ArmyRtsTransportService.HasActiveVoyage(army);
            int sampledTileId = positionSource ==
                                ArmyWatchdogPositionSource.FormationAnchor
                ? formationAnchor?.data?.tile_id ?? -1
                : sampledActor?.current_tile?.data?.tile_id ?? -1;
            bool atStrategicEndpoint =
                runtime.LastStrategicEndpointTileId >= 0 &&
                sampledTileId == runtime.LastStrategicEndpointTileId;
            bool sampledCombatActive = HasImmediateCombatPriority(
                sampledActor);
            bool transportCommandExpected = ArmyRtsControllerRules.
                ShouldExpectTransportCommand(
                    runtime.TransportRouteConfirmed, transportOwned);
            bool commandExpected = !sampledCombatActive &&
                !runtime.TargetCompletionLatched &&
                (ArmyRtsControllerRules.ShouldExpectMovementCommand(
                     record.State, destinationPublished,
                     runtime.RouteArrived, atStrategicEndpoint,
                     deploymentProgressExpected) ||
                 transportCommandExpected);
            bool routePlanningExpected = ArmyRtsControllerRules.
                ShouldExpectStrategicRoutePlanning(record.State,
                    runtime.TargetCompletionLatched,
                    runtime.RouteArrived, destinationPublished,
                    transportOwned: transportOwned);
            bool commandOwned = false;
            try
            {
                string expectedTask = ArmyRtsTaskOwnershipRules.
                    ResolveWatchdogTaskId(positionSource, record.State,
                        record.Mission.ProposalKind,
                        ArmyRtsTransportService.GetPhase(army));
                commandOwned = sampledActor?.isTask(expectedTask) == true;
            }
            catch { }
            War missionWar = FindWar(record.Mission.WarId);
            Kingdom missionKingdom = SafeKingdom(army);
            City missionTarget = FindCity(record.Mission.TargetCityId);
            ArmyRtsObjectiveState objectiveState =
                ArmyRtsObjectiveService.Classify(missionWar,
                    missionKingdom, missionTarget);
            bool objectiveOpen = IsObjectiveOpenForMission(
                record.Mission, objectiveState);
            double captureTicks = 0d;
            try { captureTicks = missionTarget?.getCaptureTicks() ?? 0f; }
            catch { }
            bool objectiveProgressExpected =
                record.State == ArmyRtsState.Assault &&
                atStrategicEndpoint && objectiveOpen;
            double objectiveProgress = ArmyStallWatchdogRules.
                ResolveObjectiveProgress(objectiveState, captureTicks);
            WorldTile missionTargetTile = null;
            try { missionTargetTile = missionTarget?.getTile(); }
            catch { }
            bool requiresTransport = commandExpected &&
                captain?.current_tile?.data != null &&
                missionTargetTile?.data != null &&
                (runtime.TransportRouteConfirmed ||
                 ArmyRtsTransportService.HasActiveVoyage(army));
            ArmyFormationCounters formation =
                ArmyFormationService.GetCounters(army);
            int rosterLiving = SafeUnitCount(army);
            int targetStrength = ResolveMissionTargetStrength(army,
                missionKingdom, record.Mission);
            bool minimumForceReady = ArmyLogisticsRules.
                HasMinimumOperationalForce(rosterLiving);
            bool departureReady = ArmyRtsRules.HasDepartureStrength(
                rosterLiving, targetStrength, minimumForceReady,
                runtime.ReplenishmentBypass.Active);
            ArmyOperationalStateView operational =
                ArmyLogisticsService.GetOperationalState(army);
            pSample = new ArmyWatchdogControllerSample
            {
                ArmyId = pArmyId,
                KingdomId = record.Mission.KingdomId,
                WarId = record.Mission.WarId,
                TargetCityId = record.Mission.TargetCityId,
                PositionX = positionX,
                PositionY = positionY,
                RouteCursor = runtime.RouteProgress,
                RouteReady = ArmyRtsControllerRules.IsPublishedRouteReady(
                    runtime.RouteSubmitted, destinationPublished,
                    runtime.RouteArrived),
                RoutePending = routePlanningExpected,
                CommandExpected = commandExpected,
                CommandOwned = commandOwned,
                CombatActive = sampledCombatActive,
                ObjectiveOpen = objectiveOpen,
                ObjectiveProgressExpected = objectiveProgressExpected,
                ObjectiveProgress = objectiveProgress,
                RequiresTransport = requiresTransport,
                TransportOwned = transportOwned,
                PositionActorId = positionSource ==
                                  ArmyWatchdogPositionSource.FormationAnchor
                    ? -1L
                    : sampledActor?.data?.id ?? -1L,
                FormationLiving = formation.Living,
                FormationRallied = formation.Rallied,
                PositionSource = positionSource,
                State = record.State,
                Role = record.Mission.Role,
                DirectorForceReady = runtime.DirectorForceReady,
                MinimumForceReady = minimumForceReady,
                DepartureReady = departureReady,
                TargetStrength = targetStrength,
                RosterLiving = rosterLiving,
                Supply = operational.Supply,
                Organization = operational.Organization,
                RouteSubmitted = runtime.RouteSubmitted,
                RouteArrived = runtime.RouteArrived,
                FormationObserved = ArmyFormationService.
                    HasCompleteObservation(army),
                ReplenishmentBypass = runtime.ReplenishmentBypass.Active,
                LocalPathStatus = AWArmyMarchService.
                    GetSharedRouteInstallStatus(sampledActor).ToString(),
                LocalPathCount = sampledActor?.current_path?.Count ?? 0,
                LocalPathIndex = sampledActor?.current_path_index ?? 0,
                LocalPathFollowing =
                    sampledActor?.isFollowingLocalPath() == true,
                LocalPathMoving = sampledActor?.is_moving == true,
                LocalTargetTileId =
                    sampledActor?.tile_target?.data?.tile_id ?? -1
            };
            return true;
        }

        public static bool TryGetFollowerStallSample(long pArmyId,
            out ArmyFollowerStallSample pSample)
        {
            pSample = null;
            var samples = new List<ArmyFollowerStallSample>(1);
            if (CollectFollowerStallSamples(pArmyId, 0, 1, samples,
                    out _) <= 0) return false;
            pSample = samples[0];
            return true;
        }

        public static int CollectFollowerStallSamples(long pArmyId,
            int pStartCursor, int pMaximum,
            List<ArmyFollowerStallSample> pSamples,
            out int pNextCursor)
        {
            pNextCursor = 0;
            if (pSamples == null || pMaximum <= 0) return 0;
            pSamples.Clear();
            if (!HasActiveMission(pArmyId) ||
                !Controllers.TryGet(pArmyId, out _) ||
                !RuntimeByArmy.ContainsKey(pArmyId)) return 0;
            Army army = FindArmy(pArmyId);
            Actor captain = SafeCaptain(army);
            if (captain?.data == null || captain.isRekt() ||
                !captain.isAlive() || captain.current_tile?.data == null)
                return 0;
            int count;
            try { count = army.units.Count; }
            catch { count = 0; }
            if (count <= 1) return 0;
            int start = pStartCursor % count;
            if (start < 0) start += count;
            int maximumChecks = Math.Min(count,
                RuntimePerformanceBudgetRules.ResolveFollowerScanWindow(
                    pMaximum));
            bool transportActive = ArmyRtsTransportService.
                HasActiveVoyage(army);
            for (int scanned = 0; scanned < maximumChecks &&
                 pSamples.Count < pMaximum; scanned++)
            {
                int index = (start + scanned) % count;
                Actor follower;
                try { follower = army.units[index]; }
                catch { continue; }
                if (follower == captain || follower?.data == null ||
                    follower.isRekt() || !follower.isAlive() ||
                    follower.current_tile?.data == null) continue;
                bool followerTransport = transportActive;
                try { followerTransport |= follower.data.transportID >= 0L; }
                catch { }
                bool combatActive = HasImmediateCombatPriority(follower) ||
                                    HasImmediateCombatPriority(captain);
                bool beyondEscortRange = TileDistanceSquared(
                    follower.current_tile, captain.current_tile) >
                    ArmyFormationRules.LooseEscortOuterRadius *
                    ArmyFormationRules.LooseEscortOuterRadius;
                if (!HasFollowerMission(follower) || !beyondEscortRange)
                    continue;
                pSamples.Add(new ArmyFollowerStallSample
                {
                    ActorId = follower.data.id,
                    PositionX = follower.current_position.x,
                    PositionY = follower.current_position.y,
                    RecoveryEligible = true,
                    CombatActive = combatActive,
                    TransportActive = followerTransport
                });
            }
            pNextCursor = (start + maximumChecks) % count;
            return pSamples.Count;
        }

        private static bool IsObjectiveOpenForMission(
            ArmyRtsMission pMission, ArmyRtsObjectiveState pState)
        {
            if (pMission == null) return false;
            switch (pMission.ProposalKind)
            {
                case ArmyRtsProposalKind.Attack:
                    return pState == ArmyRtsObjectiveState.OpenAttack;
                case ArmyRtsProposalKind.Defend:
                    return pState == ArmyRtsObjectiveState.OpenDefense;
                case ArmyRtsProposalKind.FrontHold:
                    return true;
                case ArmyRtsProposalKind.Retreat:
                    return true;
                default:
                    return ArmyRtsObjectiveService.IsOpen(pState);
            }
        }

        public static bool HasActiveMission(long pArmyId)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                !Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord record) ||
                !RuntimeByArmy.ContainsKey(pArmyId)) return false;
            Army army = FindArmy(pArmyId);
            return IsLiveArmy(army) && IsMissionValid(army, record.Mission);
        }

        public static bool TryGetLogisticsSample(long pArmyId,
            out ArmyLogisticsControllerSample pSample)
        {
            pSample = null;
            if (!Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord record) ||
                !RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState runtime)) return false;
            Army army = FindArmy(pArmyId);
            Actor captain = SafeCaptain(army);
            if (captain?.current_tile == null) return false;
            City currentCity = captain.current_tile.zone?.city;
            WorldTile anchor = FindTile(runtime.AnchorTileId);
            bool currentCitySafe = false;
            try
            {
                currentCitySafe = currentCity?.data != null &&
                                  !currentCity.isRekt() &&
                                  !currentCity.isGettingCaptured();
            }
            catch { }
            ArmyFormationCounters formation =
                ArmyFormationService.GetCounters(army);
            pSample = new ArmyLogisticsControllerSample
            {
                ArmyId = pArmyId,
                KingdomId = record.Mission.KingdomId,
                WarId = record.Mission.WarId,
                CurrentCityId = currentCity?.id ?? -1L,
                CurrentCityKingdomId = currentCity?.kingdom?.id ?? -1L,
                CurrentTileId = captain.current_tile.data?.tile_id ?? -1,
                CurrentCitySafe = currentCitySafe,
                NearRouteAnchor = anchor != null &&
                    TileDistanceSquared(captain.current_tile, anchor) <=
                    ArmyFormationRules.LocalRadius *
                    ArmyFormationRules.LocalRadius,
                Living = formation.Living,
                Rallied = formation.Rallied
            };
            return true;
        }

        public static bool RequestRouteReplan(long pArmyId,
            bool pAlternateEndpoint)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                !Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord record) ||
                !RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState runtime)) return false;
            Army army = FindArmy(pArmyId);
            if (!IsLiveArmy(army)) return false;
            ArmyRouteProviderService.Cancel(pArmyId,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmyId);
            runtime.RouteSubmitted = false;
            runtime.RouteArrived = false;
            runtime.TransportRouteConfirmed = false;
            runtime.ForceTransportRoute = false;
            runtime.AnchorTileId = -1;
            int alternateTargetTileId = pAlternateEndpoint
                ? FindAlternateEndpoint(army,
                    FindCity(record.Mission.TargetCityId),
                    runtime.LastStrategicEndpointTileId)
                : -1;
            runtime.AlternateTargetTileId = alternateTargetTileId;
            if (pAlternateEndpoint && alternateTargetTileId >= 0 &&
                runtime.PursuitRoute.ReplaceEndpoint(alternateTargetTileId))
                runtime.AlternateTargetTileId = -1;
            Controllers.Requeue(pArmyId);
            return !pAlternateEndpoint ||
                   alternateTargetTileId >= 0;
        }

        public static bool RequestObjectiveHandoff(long pArmyId)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                !Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return false;
            Army army = FindArmy(pArmyId);
            Kingdom kingdom = SafeKingdom(army);
            if (!IsLiveArmy(army) || kingdom?.data == null) return false;
            CoalitionWarTaskService.ReleaseObjectiveClaim(
                record.Mission.WarId, pArmyId,
                record.Mission.TargetCityId);
            ArmyRouteProviderService.Cancel(pArmyId,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmyId);
            if (RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState runtime))
            {
                runtime.TargetCompletionLatched = true;
                runtime.RouteSubmitted = false;
                runtime.RouteArrived = false;
                runtime.TransportRouteConfirmed = false;
                runtime.ForceTransportRoute = false;
                runtime.AnchorTileId = -1;
                runtime.AlternateTargetTileId = -1;
            }
            ArmyStallWatchdogService.OnArmyInvalidated(pArmyId);
            KingdomWarDirectorService.OnArmyChanged(kingdom);
            return true;
        }

        public static bool RequestTransportRecovery(long pArmyId)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                !Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return false;
            Army army = FindArmy(pArmyId);
            Actor captain = SafeCaptain(army);
            City target = FindCity(record.Mission.TargetCityId);
            WorldTile targetTile = null;
            try { targetTile = target?.getTile(); }
            catch { }
            if (!IsLiveArmy(army) || captain?.current_tile?.data == null ||
                targetTile?.data == null ||
                SafeSameIsland(captain.current_tile, targetTile))
                return false;
            ArmyRouteProviderService.Cancel(pArmyId,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmyId);
            bool started = ArmyRtsTransportService.TryHandleActor(
                captain, targetTile, pMayBegin: true);
            if (started) Controllers.Requeue(pArmyId);
            return started;
        }

        public static bool TryBeginCrossIslandTransportAfterRouteFailure(
            long pArmyId)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                !Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return false;
            Army army = FindArmy(pArmyId);
            Actor captain = SafeCaptain(army);
            City target = FindCity(record.Mission.TargetCityId);
            WorldTile targetTile = null;
            try { targetTile = target?.getTile(); }
            catch { }
            bool hasActiveVoyage = ArmyRtsTransportService.
                HasActiveVoyage(army);
            bool sameIsland = SafeSameIsland(captain?.current_tile,
                targetTile);
            if (!ArmyRtsTransportRules.
                    ShouldEscalateCrossIslandRouteFailure(
                        routeFailed: true,
                        sameIsland: sameIsland,
                        transportAlreadyActive: hasActiveVoyage))
                return false;
            return RequestTransportRecovery(pArmyId);
        }

        public static void ReleaseActor(Actor pActor)
        {
            if (pActor?.data == null || pActor.ai == null) return;
            try
            {
                string jobId = pActor.ai.job?.id ?? "";
                bool ownsRtsTask = jobId == ArmyRtsContent.CaptainJobId ||
                    jobId == ArmyRtsContent.HoldJobId ||
                    jobId == ArmyRtsContent.FollowerJobId ||
                    pActor.isTask(ArmyRtsContent.MissionTaskId) ||
                    pActor.isTask(ArmyRtsContent.RallyTaskId) ||
                    pActor.isTask(ArmyRtsContent.ReplenishTaskId) ||
                    pActor.isTask(ArmyRtsContent.MarchTaskId) ||
                    pActor.isTask(ArmyRtsContent.DeployTaskId) ||
                    pActor.isTask(ArmyRtsContent.AssaultTaskId) ||
                    pActor.isTask(ArmyRtsContent.PursueTaskId) ||
                    pActor.isTask(ArmyRtsContent.RetreatTaskId) ||
                    pActor.isTask(ArmyRtsContent.RegroupTaskId) ||
                    pActor.isTask(ArmyRtsContent.HoldTaskId) ||
                    pActor.isTask(ArmyRtsContent.FormationTaskId);
                if (!ownsRtsTask) return;
                pActor.stopMovement();
                pActor.beh_actor_target = null;
                pActor.ai.setJob(pActor.getNextJob());
            }
            catch { }
        }

        public static void ReassertCaptainCommand(long pArmyId)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                !HasActiveMission(pArmyId)) return;
            Army army = FindArmy(pArmyId);
            Actor captain = SafeCaptain(army);
            string captainJobId = ArmyRtsContent.CaptainJobId;
            ArmyRtsState captainState = ArmyRtsState.Idle;
            if (Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord record) &&
                record != null)
            {
                captainState = record.State;
                if (ArmyRtsControllerRules.ShouldUseFrontHoldJob(
                        record.Mission?.ProposalKind ??
                        ArmyRtsProposalKind.None, record.State))
                    captainJobId = ArmyRtsContent.HoldJobId;
            }
            SetJob(captain, captainJobId,
                pTaskId: captainJobId == ArmyRtsContent.HoldJobId
                    ? ArmyRtsContent.HoldTaskId
                    : ArmyRtsContent.ResolveCaptainTaskId(captainState,
                        ArmyRtsTransportService.GetPhase(army)),
                pForceReassert: true);
            if (RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState runtime))
                runtime.JobCursor.Reopen();
            Controllers.Requeue(pArmyId);
        }

        public static void ReassertMissionCommand(long pArmyId,
            long pSampledActorId)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                !HasActiveMission(pArmyId)) return;
            Army army = FindArmy(pArmyId);
            Actor sampledActor = FindActor(pSampledActorId);
            if (sampledActor?.army == army &&
                !IsCaptain(sampledActor, army) &&
                ArmyFormationService.HasFollower(sampledActor) &&
                ShouldOwnMilitaryActor(sampledActor,
                    pMissionActive: true))
            {
                SetJob(sampledActor, ArmyRtsContent.FollowerJobId,
                    pForceReassert: true);
                Controllers.Requeue(pArmyId);
                return;
            }
            ReassertCaptainCommand(pArmyId);
        }

        public static void RecoverFormationMember(long pArmyId,
            long pActorId)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                !HasActiveMission(pArmyId)) return;
            Army army = FindArmy(pArmyId);
            Actor actor = FindActor(pActorId);
            if (actor?.army != army || IsCaptain(actor, army) ||
                !ArmyFormationService.HasFollower(actor) ||
                !ShouldOwnMilitaryActor(actor, pMissionActive: true))
                return;
            AWArmyMarchService.ResetActorSharedRoute(actor);
            SetJob(actor, ArmyRtsContent.FollowerJobId,
                pForceReassert: true);
            TrySubmitIndependentFollowerRecoveryRoute(actor, army);
            Controllers.Requeue(pArmyId);
        }

        private static void TrySubmitIndependentFollowerRecoveryRoute(
            Actor pActor, Army pArmy)
        {
            Actor captain = SafeCaptain(pArmy);
            bool sharedRouteAvailable = AWArmyMarchService.
                GetSharedRouteInstallStatus(pActor) !=
                ArmySharedRouteInstallStatus.Unavailable;
            bool combatActive = HasImmediateCombatPriority(pActor) ||
                                HasImmediateCombatPriority(captain);
            bool transportActive = ArmyRtsTransportService.
                HasActiveVoyage(pArmy);
            try { transportActive |= pActor?.data?.transportID >= 0L; }
            catch { }
            if (!ArmyFormationService.TryGetFollowerRecoveryTarget(pActor,
                    out WorldTile target) ||
                !SafeSameIsland(pActor?.current_tile, target) ||
                !ArmySharedPathRules.
                    ShouldSubmitIndependentFollowerRecoveryRoute(
                        sharedRouteAvailable, target?.data != null,
                        combatActive, transportActive)) return;
            try
            {
                pActor.goTo(target, pLimitPathfindingRegions: 0);
            }
            catch { }
        }

        public static bool TryTeleportFormationMember(long pArmyId,
            long pActorId)
        {
            return HasActiveMission(pArmyId) &&
                   TryTeleportReinforcementMember(pArmyId, pActorId,
                pAllowCaptainCombat: false);
        }

        private static bool TryTeleportFormationMember(long pArmyId,
            long pActorId, bool pAllowCaptainCombat)
        {
            return HasActiveMission(pArmyId) &&
                   TryTeleportReinforcementMember(pArmyId, pActorId,
                       pAllowCaptainCombat);
        }

        internal static bool TryTeleportReinforcementMember(long pArmyId,
            long pActorId, bool pAllowCaptainCombat)
        {
            Army army = FindArmy(pArmyId);
            Actor actor = FindActor(pActorId);
            Actor captain = SafeCaptain(army);
            bool missionActive = army?.data != null &&
                                 HasActiveMission(pArmyId);
            bool wartimeEmergency = army?.data != null &&
                MilitaryEmergencyService.HasAny(SafeKingdom(army));
            if (!ArmyRtsReplenishmentArrivalRules.ShouldTrackArrival(
                    ArmyRtsRuntimeMode.ShouldCommit,
                    IsLiveWarriorActor(actor), actor?.army == army,
                    IsCaptain(actor, army), missionActive,
                    wartimeEmergency) ||
                 missionActive &&
                 (!ArmyFormationService.HasFollower(actor) ||
                  !ShouldOwnMilitaryActor(actor, pMissionActive: true)) ||
                 captain?.current_tile?.data == null ||
                 HasImmediateCombatPriority(actor) ||
                 !pAllowCaptainCombat && HasImmediateCombatPriority(captain) ||
                 ArmyRtsTransportService.HasActiveVoyage(army)) return false;
            try
            {
                if (actor.data.transportID >= 0L) return false;
                WorldTile target = captain.current_tile;
                if (missionActive &&
                    !ArmyFormationService.TryGetFollowerRecoveryTarget(
                        actor, out target)) return false;
                AWArmyMarchService.ResetActorSharedRoute(actor);
                actor.cancelAllBeh();
                actor.stopMovement();
                actor.spawnOn(target);
                if (missionActive)
                {
                    ArmyFormationService.TryGetFollowerTarget(actor, out _);
                    SetJob(actor, ArmyRtsContent.FollowerJobId,
                        pForceReassert: true);
                    Controllers.Requeue(pArmyId);
                }
                KingdomWarDirectorService.QueueArmyChanged(
                    SafeKingdom(army));
                return true;
            }
            catch { return false; }
        }

        public static void Invalidate(long pArmyId)
        {
            CoalitionWarTaskService.OnArmyInvalidated(pArmyId);
            Army army = FindArmy(pArmyId);
            ArmyRtsTransportService.ReleaseArmy(army);
            ArmyRtsMobilizationStatusService.Clear(army);
            ReleaseArmyActors(army);
            GarrisonSortieService.OnMissionCompleted(army);
            ArmyMissionPersistence.Invalidate(army);
            Controllers.Invalidate(pArmyId);
            MissionIndex.Remove(pArmyId);
            RuntimeByArmy.Remove(pArmyId);
            ArmyLogisticsService.OnMissionInvalidated(pArmyId);
            ArmyStallWatchdogService.OnArmyInvalidated(pArmyId);
            ArmyFormationService.RemoveArmy(pArmyId);
            AWArmyMarchService.ClearArmy(pArmyId);
            RemovePendingReplenishmentArrivals(pArmyId);
        }

        public static int InvalidateWar(long pWarId)
        {
            if (pWarId < 0L) return 0;
            IReadOnlyList<long> armyIds = MissionIndex.SnapshotWar(pWarId);
            int invalidated = 0;
            for (int i = 0; i < armyIds.Count; i++)
            {
                long armyId = armyIds[i];
                if (!Controllers.TryGet(armyId,
                        out ArmyRtsControllerRecord record))
                {
                    Invalidate(armyId);
                    invalidated++;
                    continue;
                }
                long missionWarId = record?.Mission?.WarId ?? -1L;
                if (!ActiveMilitaryLifecycleRules.
                        ShouldInvalidateMissionForEndedWar(
                            missionWarId, pWarId)) continue;
                Army army = FindArmy(armyId);
                bool shouldBeginReturn = WarArmyReturnRules.
                    ShouldBeginReturn(IsLiveArmy(army), missionWarId,
                        pWarId);
                Invalidate(armyId);
                if (shouldBeginReturn)
                    WarArmyReturnService.TryBegin(army);
                invalidated++;
            }
            return invalidated;
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
            if (!ArmyRtsRuntimeModeRules.ShouldPlan(
                    ArmyRtsRuntimeMode.Current) || World.world?.armies == null)
                return;
            foreach (Army army in World.world.armies)
            {
                if (!ArmyMissionPersistence.TryGetRestored(army,
                        out ArmyRtsMission mission)) continue;
                Controllers.AssignMission(mission);
                MissionIndex.Upsert(mission);
                ArmyRtsWarLifecycleService.OnMissionAssigned(army, mission);
                RuntimeByArmy[army.id] = new RuntimeState
                {
                    InitialRosterCount = SafeUnitCount(army)
                };
                bool corridor = ResolveInitialMissionCorridor(army, mission);
                ArmyLogisticsService.OnMissionAssigned(army, mission,
                    pConnectedSupply: corridor, pInCorridor: corridor);
                ArmyStallWatchdogService.OnMissionAssigned(army,
                    pResetState: true);
            }
        }

        public static void ClearRuntime()
        {
            Controllers.Clear();
            RuntimeByArmy.Clear();
            MissionIndex.Clear();
            ReplicaProjectionByArmy.Clear();
            ArmyFormationService.ClearRuntime();
            ArmyRtsTransportService.Clear();
            PendingReplenishmentArrivals.Clear();
            PendingReplenishmentArrivalQueue.Clear();
            ArmyRtsWarLifecycleService.ClearRuntime();
        }

        private static void RemovePendingReplenishmentArrivals(long pArmyId)
        {
            if (pArmyId < 0L || PendingReplenishmentArrivals.Count == 0)
                return;
            var removeIds = new List<long>();
            foreach (KeyValuePair<long, PendingReplenishmentArrival> pair in
                     PendingReplenishmentArrivals)
                if (pair.Value?.ArmyId == pArmyId)
                    removeIds.Add(pair.Key);
            for (int i = 0; i < removeIds.Count; i++)
                PendingReplenishmentArrivals.Remove(removeIds[i]);
        }

        private static bool TryParseDefined<T>(string pValue,
            out T pResult) where T : struct, Enum
        {
            return Enum.TryParse(pValue, true, out pResult) &&
                   Enum.IsDefined(typeof(T), pResult);
        }

        private static void ProcessOne(long pArmyId, ArmyRtsMode pMode)
        {
            if (!Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord record)) return;
            Army army = FindArmy(pArmyId);
            bool liveArmy = IsLiveArmy(army);
            if (!liveArmy || !IsMissionValid(army, record.Mission))
            {
                Kingdom kingdom = liveArmy ? SafeKingdom(army) : null;
                if (liveArmy)
                    GarrisonSortieService.OnMissionCompleted(army);
                Invalidate(pArmyId);
                if (liveArmy)
                    KingdomWarDirectorService.OnArmyChanged(kingdom);
                return;
            }
            if (!RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState runtime))
            {
                runtime = new RuntimeState();
                RuntimeByArmy[pArmyId] = runtime;
            }

            bool commit = ArmyRtsRuntimeModeRules.ShouldCommit(pMode);
            bool reservePoolEligible = ArmyReplenishmentOperationService.
                CanUseReservePool(army);
            if (commit && !reservePoolEligible)
                ClearIneligibleReplenishmentState(army, runtime);
            if (commit && TryHandleWarCombatOwnership(army, record,
                    runtime))
            {
                Controllers.Requeue(pArmyId);
                return;
            }
            if (commit)
            {
                long formationDiagnostic = RuntimePerformanceDiagnostic.
                    BeginArmyRtsControllerStage(
                        ArmyRtsControllerPerformanceStage.Formation);
                try { ObserveFormation(army, record.State); }
                finally
                {
                    RuntimePerformanceDiagnostic.EndArmyRtsControllerStage(
                        ArmyRtsControllerPerformanceStage.Formation,
                        formationDiagnostic);
                }
                long jobsDiagnostic = RuntimePerformanceDiagnostic.
                    BeginArmyRtsControllerStage(
                        ArmyRtsControllerPerformanceStage.JobOwnership);
                try
                {
                    TryReopenJobOwnershipRepair(runtime);
                    EnsureJobs(army, runtime, record.Mission, record.State);
                    InstallFollowerSharedRoutes(army, runtime, record.State);
                }
                finally
                {
                    RuntimePerformanceDiagnostic.EndArmyRtsControllerStage(
                        ArmyRtsControllerPerformanceStage.JobOwnership,
                        jobsDiagnostic);
                }
            }

            bool preservePlayerOrder =
                record.Mission.PlayerOrder &&
                ShouldPreservePlayerOrder(army);
            if (record.Mission.PlayerOrder && !preservePlayerOrder)
            {
                ArmyRtsMission interrupted = ArmyRtsControllerRules.
                    CopyMission(record.Mission);
                interrupted.PlayerOrder = false;
                Controllers.AssignMission(interrupted);
                if (commit) ArmyMissionPersistence.Persist(army,
                    interrupted);
            }

            ArmyRtsState current = record.State;
            long targetFactsDiagnostic = RuntimePerformanceDiagnostic.
                BeginArmyRtsControllerStage(
                    ArmyRtsControllerPerformanceStage.TargetFacts);
            ArmyRtsTransitionFacts facts;
            try
            {
                facts = BuildFacts(army, record, runtime, commit,
                    preservePlayerOrder);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndArmyRtsControllerStage(
                    ArmyRtsControllerPerformanceStage.TargetFacts,
                    targetFactsDiagnostic);
            }
            ArmyRtsState next = Controllers.ResolveAndSet(pArmyId, facts);
            ArmyLogisticsService.OnArmyStateChanged(army, next);
            UpdatePursuitRuntime(army, current, next, runtime);
            if (commit && current == ArmyRtsState.Retreat &&
                next == ArmyRtsState.Regroup)
                TryBeginReplenishingAtCurrentCity(army, record);
            UpdateReplenishmentRequest(army, runtime, next, commit);
            if (commit)
            {
                long mobilizationDiagnostic = RuntimePerformanceDiagnostic.
                    BeginArmyRtsControllerStage(
                        ArmyRtsControllerPerformanceStage.Mobilization);
                try
                {
                    if (ArmyRtsMobilizationStatusRules.ShouldStartCatchup(
                        runtime.MobilizationStatusState, next))
                    {
                        runtime.MobilizationStatusCursor = 0;
                        runtime.MobilizationStatusCleanupPending = false;
                        runtime.MobilizationStatusCatchupPending = true;
                        runtime.MobilizationStatusSweepHasPendingAssembly = false;
                    }
                    else if (ArmyRtsMobilizationStatusRules.ShouldStartCleanup(
                        runtime.MobilizationStatusState, next))
                    {
                        runtime.MobilizationStatusCursor = 0;
                        runtime.MobilizationStatusCleanupPending = true;
                        runtime.MobilizationStatusCatchupPending = false;
                        runtime.MobilizationStatusSweepHasPendingAssembly = false;
                    }
                    runtime.MobilizationStatusState = next;
                    if (ArmyRtsMobilizationStatusRules.RequiresReconciliation(
                        next, runtime.MobilizationStatusCleanupPending ||
                        runtime.MobilizationStatusCatchupPending))
                    {
                        ArmyRtsMobilizationStatusReconciliation reconciliation =
                            ArmyRtsMobilizationStatusService.Reconcile(army,
                                next, runtime.MobilizationStatusCursor);
                        runtime.MobilizationStatusCursor =
                            reconciliation.NextCursor;
                        runtime.MobilizationStatusSweepHasPendingAssembly |=
                            reconciliation.HasPendingAssembly;
                        if (reconciliation.CompletedPass)
                        {
                            if (runtime.MobilizationStatusCleanupPending)
                                runtime.MobilizationStatusCleanupPending = false;
                            else
                                runtime.MobilizationStatusCatchupPending =
                                    runtime.MobilizationStatusSweepHasPendingAssembly;
                            runtime.MobilizationStatusSweepHasPendingAssembly = false;
                        }
                    }
                }
                finally
                {
                    RuntimePerformanceDiagnostic.EndArmyRtsControllerStage(
                        ArmyRtsControllerPerformanceStage.Mobilization,
                        mobilizationDiagnostic);
                }
            }
            if (commit && current == ArmyRtsState.Regroup &&
                next == ArmyRtsState.Rally &&
                record.Mission.Posture == ArmyRtsPosture.Retreat)
            {
                CompleteRetreatMission(army, record.Mission);
                return;
            }
            if (commit && current == ArmyRtsState.Regroup &&
                next == ArmyRtsState.Retreat)
            {
                if (!ArmyRetreatService.AssignArmyRetreat(army,
                        record.Mission.TargetCityId))
                    RecoverUnavailableRetreat(army);
                return;
            }
            if (commit && next == ArmyRtsState.Retreat &&
                record.Mission.Posture != ArmyRtsPosture.Retreat)
            {
                if (!ArmyRetreatService.AssignArmyRetreat(army))
                    RecoverUnavailableRetreat(army);
                return;
            }
            if (commit && ArmyRtsRules.ShouldClearRallyFormationAnchor(
                    current, next, runtime.RouteSubmitted,
                    runtime.RouteArrived))
                runtime.AnchorTileId = -1;
            bool transportOwnsRoute = runtime.TransportRouteConfirmed ||
                                      ArmyRtsTransportService.
                                          HasActiveVoyage(army);
            bool shouldAdvanceRoute = ArmyRtsControllerRules.
                ShouldAdvanceRoute(current, next, facts.RallyReady) ||
                ArmyRtsRules.ShouldRetryMissingStrategicRoute(current,
                    next, runtime.RouteSubmitted, runtime.RouteArrived,
                    transportOwnsRoute);
            if (commit && shouldAdvanceRoute &&
                (next != ArmyRtsState.Retreat ||
                 record.Mission.Posture == ArmyRtsPosture.Retreat))
            {
                long routeDiagnostic = RuntimePerformanceDiagnostic.
                    BeginArmyRtsControllerStage(
                        ArmyRtsControllerPerformanceStage.Route);
                try { AdvanceRoute(army, record.Mission, runtime); }
                finally
                {
                    RuntimePerformanceDiagnostic.EndArmyRtsControllerStage(
                        ArmyRtsControllerPerformanceStage.Route,
                        routeDiagnostic);
                }
            }
            if (ArmyRtsRules.ShouldHandoffObjective(next,
                    facts.TargetComplete, facts.TargetValid))
            {
                ClearCompletedObjectiveRuntime(army, runtime);
                CoalitionWarTaskService.ReleaseObjectiveClaim(
                    record.Mission.WarId, pArmyId,
                    record.Mission.TargetCityId);
                KingdomWarDirectorService.OnArmyChanged(SafeKingdom(army));
                return;
            }
            Controllers.Requeue(pArmyId);
        }

        private static void ObserveFormation(Army pArmy,
            ArmyRtsState pState)
        {
            Actor captain = SafeCaptain(pArmy);
            WorldTile anchor = captain?.current_tile;
            if (anchor?.data == null) return;
            ArmyFormationService.ObserveArmy(pArmy, anchor,
                pDeploymentEligible: pState == ArmyRtsState.Deploy);
        }

        private static bool TryHandleWarCombatOwnership(Army pArmy,
            ArmyRtsControllerRecord pRecord, RuntimeState pRuntime)
        {
            if (pArmy?.data == null || pRecord?.Mission == null ||
                pRuntime == null) return false;
            ArmyRtsWarLifecycleRecord lifecycle =
                ArmyRtsWarLifecycleService.OnMissionAssigned(pArmy,
                    pRecord.Mission);
            if (lifecycle == null) return false;
            City target = FindCity(pRecord.Mission.TargetCityId);
            Actor captain = SafeCaptain(pArmy);
            City currentCity = null;
            try { currentCity = captain?.current_tile?.zone?.city; }
            catch { }
            bool insideTarget = target?.data != null &&
                                currentCity == target;
            Kingdom kingdom = SafeKingdom(pArmy);
            War war = FindWar(pRecord.Mission.WarId);
            bool hostileInside = insideTarget &&
                CityAttackZoneService.HasHostileMilitaryInside(war,
                    target, kingdom);
            bool hostileInCurrentCity = currentCity?.data != null &&
                CityAttackZoneService.HasHostileMilitaryInside(war,
                    currentCity, kingdom);
            bool objectiveOpen = target?.data != null &&
                !TargetComplete(pArmy, pRecord.Mission, target, kingdom);
            bool withdrawalRequired = ArmyRtsWarLifecycleRules.
                ShouldWithdraw(SafeUnitCount(pArmy),
                    lifecycle.BaselineStrength);
            ArmyRtsCombatControlDecision decision =
                ArmyRtsWarLifecycleRules.ResolveCombatControl(
                    lifecycle.Phase, withdrawalRequired, insideTarget,
                    hostileInside, objectiveOpen);
            switch (decision)
            {
                case ArmyRtsCombatControlDecision.ReleaseToVanilla:
                    ReleaseToVanillaCombat(pArmy, pRecord, pRuntime);
                    return true;
                case ArmyRtsCombatControlDecision.KeepVanillaControl:
                    return true;
                case ArmyRtsCombatControlDecision.
                    ReacquireStrategicControl:
                    ReacquireFromVanillaCombat(pArmy, pRecord, pRuntime,
                        ArmyRtsWarPhase.StrategicMovement);
                    return false;
                case ArmyRtsCombatControlDecision.ReacquireForWithdrawal:
                    if (ArmyRtsWarLifecycleRules.
                            CanReplenishInCurrentCity(
                                currentCity?.kingdom == kingdom ||
                                CityAttackZoneService.IsControlledBySide(war,
                                    currentCity, kingdom),
                                hostileInCurrentCity))
                    {
                        ReacquireFromVanillaCombat(pArmy, pRecord, pRuntime,
                            ArmyRtsWarPhase.Replenishing);
                        ArmyRtsWarLifecycleService.BeginReplenishing(
                            pRecord.Mission.WarId, pArmy, currentCity);
                        Controllers.SetState(pArmy.id,
                            ArmyRtsState.Regroup);
                        return false;
                    }
                    ReacquireFromVanillaCombat(pArmy, pRecord, pRuntime,
                        ArmyRtsWarPhase.Withdrawal);
                    Controllers.SetState(pArmy.id, ArmyRtsState.Retreat);
                    if (!ArmyRetreatService.AssignArmyRetreat(pArmy))
                        RecoverUnavailableRetreat(pArmy);
                    return true;
                default:
                    return false;
            }
        }

        private static void ReleaseToVanillaCombat(Army pArmy,
            ArmyRtsControllerRecord pRecord, RuntimeState pRuntime)
        {
            ArmyRtsWarLifecycleService.TrySetPhase(
                pRecord.Mission.WarId, pArmy.id,
                ArmyRtsWarPhase.VanillaCombat);
            ArmyRouteProviderService.Cancel(pArmy.id,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmy.id);
            ArmyRtsTransportService.ReleaseArmy(pArmy);
            ArmyFormationService.RemoveArmy(pArmy.id);
            ResetStrategicMovementRuntime(pRuntime);
            ReleaseArmyActors(pArmy);
        }

        private static void ReacquireFromVanillaCombat(Army pArmy,
            ArmyRtsControllerRecord pRecord, RuntimeState pRuntime,
            ArmyRtsWarPhase pPhase)
        {
            ArmyRtsWarLifecycleService.TrySetPhase(
                pRecord.Mission.WarId, pArmy.id, pPhase);
            ClearArmyAttackTargets(pArmy);
            ArmyRouteProviderService.Cancel(pArmy.id,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmy.id);
            ArmyFormationService.RemoveArmy(pArmy.id);
            ResetStrategicMovementRuntime(pRuntime);
            pRuntime.JobCursor.Reopen();
        }

        private static void ResetStrategicMovementRuntime(
            RuntimeState pRuntime)
        {
            if (pRuntime == null) return;
            pRuntime.RouteSubmitted = false;
            pRuntime.RouteArrived = false;
            pRuntime.TransportRouteConfirmed = false;
            pRuntime.ForceTransportRoute = false;
            pRuntime.RouteProgress = 0;
            pRuntime.AnchorTileId = -1;
            pRuntime.AlternateTargetTileId = -1;
            pRuntime.FollowerRouteInstallCursor = 0;
            pRuntime.PursuitRoute.Reset();
        }

        private static void ClearArmyAttackTargets(Army pArmy)
        {
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                ClearActorAttackTarget(actor);
            }
            ClearActorAttackTarget(SafeCaptain(pArmy));
        }

        private static void ClearActorAttackTarget(Actor pActor)
        {
            if (pActor?.data == null) return;
            try { pActor.clearAttackTarget(); }
            catch { }
            try { pActor.beh_actor_target = null; }
            catch { }
        }

        private static void TryBeginReplenishingAtCurrentCity(Army pArmy,
            ArmyRtsControllerRecord pRecord)
        {
            Actor captain = SafeCaptain(pArmy);
            City city = null;
            try { city = captain?.current_tile?.zone?.city; }
            catch { }
            if (city?.data == null || pRecord?.Mission == null) return;
            ArmyRtsWarLifecycleService.BeginReplenishing(
                pRecord.Mission.WarId, pArmy, city);
        }

        private static ArmyRtsTransitionFacts BuildFacts(Army pArmy,
            ArmyRtsControllerRecord pRecord, RuntimeState pRuntime,
            bool pCommit, bool pPreservePlayerOrder)
        {
            City target = FindCity(pRecord.Mission.TargetCityId);
            Actor captain = SafeCaptain(pArmy);
            if ((pRecord.State == ArmyRtsState.Rally ||
                 pRecord.State == ArmyRtsState.Replenish) &&
                captain?.current_tile?.data != null)
            {
                int captainTileId = captain.current_tile.data.tile_id;
                pRuntime.AnchorTileId = ArmyRtsControllerRules.
                    ResolveRallyAnchorTileId(pRuntime.AnchorTileId,
                        captainTileId);
            }
            bool targetValid = IsLiveCity(target);
            bool frontHold = pRecord.Mission.ProposalKind ==
                             ArmyRtsProposalKind.FrontHold;
            bool complete = targetValid && !frontHold &&
                (pRuntime.TargetCompletionLatched || TargetComplete(pArmy,
                    pRecord.Mission, target, SafeKingdom(pArmy)));
            if (complete && !pRuntime.TargetCompletionLatched)
            {
                pRuntime.TargetCompletionLatched = true;
                LogMissionChanged(pArmy, pRecord.Mission, null,
                    "target_completed");
            }
            bool contact = targetValid && captain?.current_tile != null &&
                TileDistanceSquared(captain.current_tile,
                    target.getTile()) <= 64;
            Kingdom kingdom = SafeKingdom(pArmy);
            int rosterLiving = SafeUnitCount(pArmy);
            ArmyFormationCounters rallyFollowers =
                ArmyFormationService.GetIncrementalFollowerCounters(pArmy);
            bool captainPresent = captain?.data != null &&
                                  captain.isAlive() && !captain.isRekt() &&
                                  captain.current_tile?.data != null;
            bool escortQuorum = ArmyRtsRules.
                HasIncrementalEscortQuorum(rosterLiving,
                    rallyFollowers.Rallied, captainPresent);
            bool minimumForceReady =
                ArmyLogisticsRules.HasMinimumOperationalForce(
                    rosterLiving);
            bool replenishWindow = ArmyReplenishmentOperationService.
                CanUseReservePool(pArmy) &&
                ArmyRtsRules.SupportsReplenishment(pRecord.State);
            int targetStrength = ResolveMissionTargetStrength(pArmy,
                kingdom, pRecord.Mission);
            bool wartimeRecovery = ArmyRtsWarLifecycleService.TryGet(
                    pRecord.Mission.WarId, pArmy.id,
                    out ArmyRtsWarLifecycleRecord lifecycle) &&
                lifecycle.Phase == ArmyRtsWarPhase.Replenishing;
            if (wartimeRecovery)
                targetStrength = ArmyRtsWarLifecycleRules.
                    RecoveryTargetStrength(lifecycle.BaselineStrength);
            pRuntime.ObservedLiving = rosterLiving;
            pRuntime.TargetStrength = targetStrength;
            bool sourceReserveKnown = !replenishWindow;
            bool sourceReserveAvailable = false;
            if (wartimeRecovery)
            {
                sourceReserveKnown = true;
                sourceReserveAvailable = true;
            }
            else if (replenishWindow)
                sourceReserveKnown = ArmyReplenishmentOperationService.
                    TryGetSourceReserveAvailability(pArmy,
                        out sourceReserveAvailable);
            bool replenishmentReserveAvailable = replenishWindow &&
                (!sourceReserveKnown || sourceReserveAvailable);
            bool needsReplenishment = replenishWindow &&
                replenishmentReserveAvailable &&
                (ArmyRtsRules.NeedsReplenishment(rosterLiving,
                     targetStrength) ||
                 ArmyRtsRules.ShouldContinueRequestedReplenishment(
                     pRuntime.ReplenishmentRequested, rosterLiving,
                     targetStrength, replenishmentReserveAvailable));
            double readinessTime = CurrentWorldTime();
            bool replenishmentStalled = pRuntime.ReplenishmentProgress.
                Observe(replenishWindow &&
                     pRuntime.ReplenishmentRequested &&
                         needsReplenishment,
                    rosterLiving, readinessTime,
                    ArmyRtsRules.ReadinessStallTimeoutSeconds);
            bool replenishmentOperationReleased =
                ArmyReplenishmentOperationService.IsDepartureReleased(
                    pArmy);
            bool replenishmentBypass = ArmyRtsRules.
                ShouldBypassStalledReadiness(pCommit,
                    minimumForceReady,
                    readinessComplete: !needsReplenishment,
                    progressStalled: replenishmentStalled &&
                        replenishmentOperationReleased);
            bool replenishmentBypassActive = pRuntime.
                ReplenishmentBypass.Update(replenishWindow,
                    needsReplenishment, minimumForceReady,
                    replenishmentBypass);
            bool departureStrengthReady = ArmyRtsRules.
                HasDepartureStrength(rosterLiving, targetStrength,
                    minimumForceReady, replenishmentBypassActive);
            pRuntime.ReplenishmentRetryDue = false;
            bool forcePreDeparture = ArmyRtsRules.
                ShouldForcePreDeparture(pCommit, pRecord.State,
                    departureStrengthReady, captainPresent, escortQuorum,
                    pRecord.Mission.IssuedTime, readinessTime);
            ArmyOperationalStateView operational =
                ArmyLogisticsService.GetOperationalState(pArmy);
            bool regroupRecoveryPending = pCommit &&
                pRecord.State == ArmyRtsState.Regroup &&
                (operational.Organization <
                     ArmyRtsRules.RegroupOrganization ||
                 operational.Supply <= ArmyRtsRules.CriticalSupply);
            bool regroupRecoveryStalled = pRuntime.
                RegroupRecoveryProgress.Observe(regroupRecoveryPending,
                    operational.Organization, operational.Supply,
                    readinessTime,
                    ArmyRtsRules.ReadinessStallTimeoutSeconds);
            ArmyRtsState pursuitState = pRecord.State ==
                                        ArmyRtsState.Pursue
                ? ResolvePursuitState(captain, pRuntime, operational)
                : ArmyRtsState.Pursue;
            bool survivalException = pRecord.Mission.Role ==
                                     ArmyRtsRole.Defense &&
                                     target?.data != null &&
                                     kingdom?.capital == target;
            bool hostileWarriorInsideTargetCity = complete &&
                CityAttackZoneService.HasHostileMilitaryInside(
                    FindWar(pRecord.Mission.WarId), target, kingdom);
            bool pursuitAllowed = ArmyRtsRules.
                ShouldPursueCompletedTarget(
                    complete,
                    pRuntime.PursuitCompleted,
                    pRecord.Mission.Role == ArmyRtsRole.Assault,
                    operational.Supply >
                        ArmyLogisticsRules.CriticalSupply,
                    operational.InCorridor,
                    hostileWarriorInsideTargetCity) &&
                TryPreparePursuitRoute(pArmy, pRuntime);
            ArmyRtsTransitionFacts facts = pRuntime.TransitionFacts;
            facts.CurrentState = pRecord.State;
            facts.Role = pRecord.Mission.Role;
            facts.Posture = pRecord.Mission.Posture;
            facts.HasMission = true;
            facts.FrontHold = frontHold;
            facts.TargetValid = targetValid;
            facts.FormationObservationComplete = true;
            facts.RallyReady = ArmyRtsRules.HasIncrementalRallyReadiness(
                departureStrengthReady, rosterLiving, rallyFollowers.Rallied,
                captainPresent) || forcePreDeparture;
            facts.RouteArrived = pCommit && pRuntime.RouteArrived;
            bool deploymentBaselineReady = pCommit &&
                ArmyFormationRules.HasEscortDeploymentReadiness(
                    pRuntime.RouteArrived, minimumForceReady);
            if (pRecord.State == ArmyRtsState.Deploy &&
                deploymentBaselineReady && captainPresent)
            {
                if (double.IsNaN(pRuntime.DeploymentWaitStartedWorldTime))
                    pRuntime.DeploymentWaitStartedWorldTime = readinessTime;
            }
            else
                pRuntime.DeploymentWaitStartedWorldTime = double.NaN;
            bool forceDeployment = ArmyRtsRules.ShouldForceDeployment(
                pCommit, pRecord.State, minimumForceReady, captainPresent,
                escortQuorum, pRuntime.RouteArrived,
                pRuntime.DeploymentWaitStartedWorldTime, readinessTime);
            facts.DeploymentReady = pCommit &&
                (deploymentBaselineReady && escortQuorum ||
                 forceDeployment);
            facts.EnemyContact = contact ||
                                 pRecord.State == ArmyRtsState.Assault;
            facts.MinimumForceReady = minimumForceReady;
            facts.ForceReady = departureStrengthReady &&
                (pRecord.Mission.Role != ArmyRtsRole.Reinforcement ||
                 pRuntime.DirectorForceReady) || forcePreDeparture;
            facts.NeedsReplenishment = ArmyRtsRules.
                ShouldRemainInReplenishment(needsReplenishment,
                    departureStrengthReady, replenishmentReserveAvailable);
            facts.TargetComplete = complete;
            facts.HoldRequired = pRecord.Mission.Role ==
                                     ArmyRtsRole.Defense ||
                                 pRecord.Mission.Role ==
                                     ArmyRtsRole.Reserve ||
                                 frontHold;
            facts.PursuitAllowed = pursuitAllowed;
            facts.RetreatArrived = pRuntime.RouteArrived;
            facts.RegroupReady = ArmyRtsRules.HasRegroupReadiness(
                departureStrengthReady, pRecord.Mission.Role,
                pRuntime.DirectorForceReady,
                pRecord.Mission.ProposalKind == ArmyRtsProposalKind.Retreat);
            facts.RegroupRecoveryStalled = regroupRecoveryStalled;
            facts.SurvivalException = survivalException || pPreservePlayerOrder;
            facts.PursuitComplete = pRecord.State == ArmyRtsState.Pursue &&
                pursuitState != ArmyRtsState.Pursue;
            facts.PursuitRequiresRegroup = pursuitState ==
                ArmyRtsState.Regroup;
            facts.OpenObjective = targetValid && !frontHold && !complete;
            facts.LocalForceAdvantage = facts.OpenObjective &&
                ArmyRtsRules.HasLocalForceAdvantage(
                    pRuntime.DirectorFriendlyForce,
                    pRuntime.DirectorEnemyForce);
            facts.Supply = operational.Supply;
            facts.Organization = operational.Organization;
            return facts;
        }

        private static void UpdateReplenishmentRequest(Army pArmy,
            RuntimeState pRuntime, ArmyRtsState pNext, bool pCommit)
        {
            if (pRuntime == null) return;
            if (!ArmyReplenishmentOperationService.CanUseReservePool(pArmy))
                return;
            if (!ArmyRtsRules.OwnsReplenishmentRequest(pNext))
            {
                pRuntime.ReplenishmentRequested = false;
                pRuntime.ReplenishmentRetryDue = false;
                pRuntime.ReplenishmentProgress.Reset();
                return;
            }
            if (pRuntime.ReplenishmentRetryDue)
            {
                pRuntime.ReplenishmentRequested = false;
                pRuntime.ReplenishmentRetryDue = false;
                pRuntime.ReplenishmentProgress.Reset();
            }
            int missingStrength = Math.Max(0,
                pRuntime.TargetStrength - pRuntime.ObservedLiving);
            bool wartimeRecovery = TryGetWartimeRecovery(pArmy, out _,
                out _);
            if (!wartimeRecovery)
                TryApplyReserveExhaustion(pArmy, pRuntime, missingStrength,
                    pCommit);
            if (!ArmyRtsRules.ShouldRequestReplenishment(
                    pCommit, pNext, pRuntime.ReplenishmentRequested,
                    missingStrength)) return;
            Kingdom kingdom = SafeKingdom(pArmy);
            City preferredCity = AWArmyService.FindAnchorCity(pArmy);
            if (Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) &&
                ArmyRtsWarLifecycleService.TryGet(
                    record?.Mission?.WarId ?? -1L, pArmy.id,
                    out ArmyRtsWarLifecycleRecord lifecycle) &&
                lifecycle.ReplenishmentCityId >= 0L)
                preferredCity = FindCity(lifecycle.ReplenishmentCityId);
            ArmyReplenishmentOperationState operation =
                ArmyReplenishmentOperationService.Ensure(pArmy, kingdom,
                    preferredCity, missingStrength, CurrentWorldTime());
            pRuntime.ReplenishmentRequested = operation != null;
        }

        private static void TryApplyReserveExhaustion(Army pArmy,
            RuntimeState pRuntime, int pReinforcementShortage,
            bool pCommit)
        {
            if (!pCommit || pArmy?.data == null || pRuntime == null ||
                pReinforcementShortage <= 0 ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null ||
                record.Mission.ProposalKind !=
                    ArmyRtsProposalKind.Attack) return;
            Kingdom kingdom = SafeKingdom(pArmy);
            bool wartime = CityReservePoolService.ResolveMobilizationPhase(
                kingdom) == ArmyMobilizationPhase.War;
            bool exhaustionConfirmed = TemporaryLevyService.
                HasConfirmedReserveExhaustion(kingdom, pArmy);
            if (!wartime || !exhaustionConfirmed) return;
            War war = FindWar(record.Mission.WarId);
            if (war?.data == null || war.hasEnded() ||
                !WarScoreService.TryGetSnapshot(war, kingdom,
                    out WarScoreSnapshot snapshot)) return;
            WarScoreSide side = snapshot.Perspective;
            int existing = side == WarScoreSide.Attackers
                ? snapshot.AttackerReserveExhaustion
                : snapshot.DefenderReserveExhaustion;
            bool shouldApply = CityReservePoolRules.
                ShouldApplyReserveExhaustion(
                    attackAssignment: true,
                    reinforcementShortage: pReinforcementShortage,
                    kingdomFrozen: wartime,
                    exhaustionConfirmed: exhaustionConfirmed,
                    alreadyApplied: existing >=
                        CityReservePoolRules.
                            ReserveExhaustionContribution);
            if (shouldApply)
                WarScoreService.ApplyReserveExhaustion(
                    record.Mission.WarId, side, CurrentWorldTime());
        }

        private static void AdvanceRoute(Army pArmy,
            ArmyRtsMission pMission, RuntimeState pRuntime)
        {
            Actor captain = SafeCaptain(pArmy);
            City targetCity = FindCity(pMission.TargetCityId);
            WorldTile target = pRuntime.PursuitRoute.Active
                ? FindTile(pRuntime.PursuitRoute.EndpointTileId)
                : FindTile(pRuntime.AlternateTargetTileId) ??
                  ResolveStableStrategicEndpoint(pArmy, targetCity,
                      pRuntime);
            if (captain?.current_tile == null || target == null) return;
            pRuntime.LastStrategicEndpointTileId =
                target.data?.tile_id ?? -1;
            if (ArmyRtsTransportService.HasActiveVoyage(pArmy))
            {
                pRuntime.RouteSubmitted = false;
                pRuntime.RouteArrived = false;
                pRuntime.AnchorTileId = -1;
                pRuntime.AlternateTargetTileId = -1;
                return;
            }
            bool sameTargetIsland = SafeSameIsland(captain.current_tile,
                target);
            bool crossIslandTransport =
                ArmyRtsTransportRules.ShouldUseTransportBeforeLandRoute(
                    ArmyRtsRuntimeMode.ShouldCommit,
                    strategicMovementReady: true,
                    actorTileValid: captain.current_tile?.data != null,
                    targetTileValid: target.data != null,
                    sameIsland: sameTargetIsland,
                    transportRouteConfirmed:
                        pRuntime.TransportRouteConfirmed);
            bool selectedTransport = ArmyRtsTransportRules.
                ShouldUseSelectedTransport(
                    ArmyRtsRuntimeMode.ShouldCommit,
                    strategicMovementReady: true,
                    actorTileValid: captain.current_tile?.data != null,
                    targetTileValid: target.data != null,
                    transportSelected: pRuntime.ForceTransportRoute);
            if (crossIslandTransport || selectedTransport)
            {
                if (crossIslandTransport)
                {
                    pRuntime.TransportRouteConfirmed = true;
                    pRuntime.ForceTransportRoute = false;
                }
                if (pRuntime.RouteSubmitted)
                    ArmyRouteProviderService.Cancel(pArmy.id,
                        ArmyRouteCancelReason.TargetReplaced);
                pRuntime.RouteSubmitted = false;
                pRuntime.RouteArrived = false;
                pRuntime.AnchorTileId = -1;
                pRuntime.AlternateTargetTileId = -1;
                bool captainCanBeginTransport = captain.data != null &&
                    captain.isAlive() && !captain.isRekt() &&
                    captain.current_tile?.data != null;
                if (ArmyRtsTransportRules.ShouldInitiateTransportImmediately(
                        routeRequiresTransport: true,
                        voyageAlreadyActive: false,
                        captainCanBeginTransport))
                    ArmyRtsTransportService.TryHandleActor(captain, target,
                        pMayBegin: true, pForceTransport: true);
                return;
            }
            int captainTileId = captain.current_tile.data?.tile_id ?? -1;
            int targetTileId = target.data?.tile_id ?? -1;
            if (ArmyRtsControllerRules.HasReachedStrategicDestination(
                    captainTileId, targetTileId))
            {
                pRuntime.AnchorTileId = -1;
                pRuntime.RouteArrived = true;
                pRuntime.RouteSubmitted = false;
                return;
            }
            WorldTile anchor = FindTile(pRuntime.AnchorTileId);
            if (anchor != null) return;
            if (pRuntime.RouteArrived) return;
            if (!pRuntime.RouteSubmitted)
            {
                if (!ArmyRouteProviderService.CanSubmit) return;
                ArmyRouteHandle handle = AWArmyMarchService.
                    SubmitStrategicRoute(pArmy, target);
                if (!handle.Accepted)
                {
                    LogStrategicRouteFailure(pArmy, pMission, pRuntime,
                        captain, target, ArmyRoutePollKind.Failed,
                        handle.FailureReason);
                    ArmyStallWatchdogService.OnRouteFailed(
                        pArmy.id);
                    return;
                }
                pRuntime.RouteSubmitted = true;
            }
            for (int i = 0; i < MaximumRoutePollsPerController; i++)
            {
                ArmyRoutePoll poll = AWArmyMarchService.
                    PollStrategicRoute(pArmy);
                if (poll.Kind == ArmyRoutePollKind.StepReady)
                {
                    if (poll.MovementMethod == AWMovementMethod.Transport)
                    {
                        pRuntime.TransportRouteConfirmed = true;
                        pRuntime.ForceTransportRoute = false;
                        pRuntime.RouteSubmitted = false;
                        pRuntime.RouteArrived = false;
                        pRuntime.AnchorTileId = -1;
                        AWArmyMarchService.ClearArmy(pArmy);
                        Controllers.Requeue(pArmy.id);
                        return;
                    }
                    if (pRuntime.RouteProgress < int.MaxValue)
                        pRuntime.RouteProgress++;
                    continue;
                }
                if (ArmyRtsControllerRules.
                    ShouldPublishStrategicDestination(poll.Kind))
                {
                    bool useTransport = AWArmyMarchService.
                        TryGetCompletedLandRouteCost(pArmy,
                            out float landRouteCost) &&
                        ArmyRtsTransportService.TryGetRouteEstimate(
                            pArmy, target,
                            out ArmyRtsTransportEstimate estimate) &&
                        ArmyRtsRouteChoiceRules.Resolve(landRouteCost, true,
                            estimate.PickupCost, estimate.QueueCost,
                            estimate.SeaCost, estimate.LandingCost) ==
                        ArmyRtsTravelChoice.Transport;
                    if (useTransport)
                    {
                        pRuntime.TransportRouteConfirmed = true;
                        pRuntime.ForceTransportRoute = true;
                        pRuntime.RouteSubmitted = false;
                        pRuntime.RouteArrived = false;
                        pRuntime.AnchorTileId = -1;
                        pRuntime.AlternateTargetTileId = -1;
                        AWArmyMarchService.ClearArmy(pArmy);
                        Controllers.Requeue(pArmy.id);
                        return;
                    }
                    pRuntime.TransportRouteConfirmed = false;
                    pRuntime.ForceTransportRoute = false;
                    pRuntime.AnchorTileId = targetTileId;
                    AWArmyMarchService.TryStartCompleteSharedRoute(
                        captain);
                    if (pRuntime.RouteProgress < int.MaxValue)
                        pRuntime.RouteProgress++;
                    return;
                }
                if (poll.Kind == ArmyRoutePollKind.Failed ||
                    poll.Kind == ArmyRoutePollKind.Cancelled ||
                    poll.Kind == ArmyRoutePollKind.NoRequest)
                {
                    LogStrategicRouteFailure(pArmy, pMission, pRuntime,
                        captain, target, poll.Kind, poll.FailureReason);
                    pRuntime.RouteSubmitted = false;
                    ArmyStallWatchdogService.OnRouteFailed(
                        pArmy.id);
                    return;
                }
                if (poll.Kind == ArmyRoutePollKind.Waiting) return;
            }
        }

        private static void LogStrategicRouteFailure(Army pArmy,
            ArmyRtsMission pMission, RuntimeState pRuntime, Actor pCaptain,
            WorldTile pTarget, ArmyRoutePollKind pKind,
            string pFailureReason)
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled) return;
            int startTileId = pCaptain?.current_tile?.data?.tile_id ?? -1;
            int targetTileId = pTarget?.data?.tile_id ?? -1;
            ArmyRtsState state = ArmyRtsState.Idle;
            if (pArmy?.data != null && Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record))
                state = record.State;
            bool sameIsland = SafeSameIsland(pCaptain?.current_tile,
                pTarget);
            string reason = string.IsNullOrWhiteSpace(pFailureReason)
                ? "unspecified"
                : pFailureReason;
            AncientWarfare3.ModClass.LogWarning(
                "[Army RTS route failure] army=" +
                (pArmy?.id ?? -1L) +
                " state=" + state +
                " role=" + (pMission?.Role.ToString() ?? "none") +
                " start_tile=" + startTileId +
                " start_xy=" + (pCaptain?.current_tile?.x ?? -1) + "," +
                (pCaptain?.current_tile?.y ?? -1) +
                " target_city=" + (pMission?.TargetCityId ?? -1L) +
                " target_tile=" + targetTileId +
                " target_xy=" + (pTarget?.x ?? -1) + "," +
                (pTarget?.y ?? -1) +
                " same_island=" + sameIsland +
                " alternate_endpoint=" +
                (pRuntime?.AlternateTargetTileId >= 0) +
                " kind=" + pKind +
                " reason=" + reason);
        }

        private static ArmyRtsState ResolvePursuitState(Actor pCaptain,
            RuntimeState pRuntime, ArmyOperationalStateView pOperational)
        {
            if (!pRuntime.PursuitRoute.Active ||
                pCaptain?.current_tile == null)
                return ArmyRtsState.Hold;
            WorldTile start = FindTile(pRuntime.PursuitRoute.StartTileId);
            double distance = start == null
                ? 0d
                : Math.Sqrt(TileDistanceSquared(start,
                    pCaptain.current_tile));
            return ArmyLogisticsRules.ResolvePursuit(
                new ArmyPursuitFacts
                {
                    ElapsedTime = Math.Max(0d, CurrentWorldTime() -
                        pRuntime.PursuitRoute.StartTime),
                    DistanceTiles = distance,
                    InCorridor = pOperational.InCorridor,
                    Supply = pOperational.Supply,
                    RouteArrived = pRuntime.RouteArrived
                });
        }

        private static bool TryPreparePursuitRoute(Army pArmy,
            RuntimeState pRuntime)
        {
            if (pRuntime.PursuitRoute.Active) return true;
            if (pRuntime.PursuitRoute.Completed) return false;
            Actor captain = SafeCaptain(pArmy);
            WorldTile start = captain?.current_tile;
            if (start?.data == null)
            {
                pRuntime.PursuitRoute.Complete();
                return false;
            }
            var candidates = new List<ArmyPursuitEndpointCandidate>();
            int budget = (int)ArmyLogisticsRules.PursuitDistanceBudget;
            for (int y = -budget; y <= budget; y++)
            {
                for (int x = -budget; x <= budget; x++)
                {
                    if (x == 0 && y == 0) continue;
                    double distance = Math.Sqrt(x * x + y * y);
                    if (distance > ArmyLogisticsRules.
                            PursuitDistanceBudget) continue;
                    WorldTile candidate;
                    try
                    {
                        candidate = World.world?.GetTileSimple(start.x + x,
                            start.y + y);
                    }
                    catch { candidate = null; }
                    if (candidate?.data == null) continue;
                    bool sameIsland;
                    try { sameIsland = candidate.isSameIsland(start); }
                    catch { sameIsland = false; }
                    bool inCorridor = ArmyLogisticsService.
                        IsTileInMissionCorridor(pArmy, candidate);
                    TileTypeBase type = candidate.Type;
                    bool walled = true;
                    try { walled = candidate.hasWallsAround(); }
                    catch { }
                    bool cityCenter = false;
                    try
                    {
                        City candidateCity = candidate.zone?.city;
                        cityCenter = candidateCity?.data != null &&
                                     candidateCity.getTile() == candidate;
                    }
                    catch { }
                    if (!ArmyLogisticsRules.CanUsePursuitEndpoint(
                            tileValid: candidate.data != null,
                            ground: type?.ground == true,
                            liquid: type?.liquid == true,
                            ocean: type?.ocean == true,
                            lava: type?.lava == true,
                            blocked: type?.block == true,
                            walled: walled,
                            cityCenter: cityCenter,
                            sameIsland: sameIsland,
                            inCorridor: inCorridor)) continue;
                    candidates.Add(new ArmyPursuitEndpointCandidate(
                        candidate.data.tile_id, distance, sameIsland,
                        inCorridor));
                }
            }
            if (pRuntime.PursuitRoute.TryBegin(start.data.tile_id,
                    CurrentWorldTime(), candidates)) return true;
            pRuntime.PursuitRoute.Complete();
            return false;
        }

        private static void CompleteRetreatMission(Army pArmy,
            ArmyRtsMission pMission)
        {
            Kingdom kingdom = SafeKingdom(pArmy);
            Invalidate(pArmy.id);
            KingdomWarDirectorService.OnArmyChanged(kingdom);
        }

        private static void UpdatePursuitRuntime(Army pArmy,
            ArmyRtsState pCurrent, ArmyRtsState pNext,
            RuntimeState pRuntime)
        {
            if (pNext == ArmyRtsState.Pursue &&
                pCurrent != ArmyRtsState.Pursue)
            {
                pRuntime.RouteSubmitted = false;
                pRuntime.RouteArrived = false;
                pRuntime.AnchorTileId = -1;
                pRuntime.AlternateTargetTileId = -1;
                return;
            }
            if (pNext == ArmyRtsState.Pursue) return;
            if (pCurrent == ArmyRtsState.Pursue)
                pRuntime.PursuitRoute.Complete();
        }

        private static int FindAlternateEndpoint(Army pArmy,
            City pTargetCity, int pExcludedTileId)
        {
            return ArmyStrategicEndpointService.Resolve(pArmy, pTargetCity,
                pExcludedTileId)?.data?.tile_id ?? -1;
        }

        private static WorldTile ResolveStableStrategicEndpoint(Army pArmy,
            City pTargetCity, RuntimeState pRuntime)
        {
            if (pRuntime == null)
                return ArmyStrategicEndpointService.Resolve(pArmy,
                    pTargetCity, pExcludedTileId: -1);
            int lockedTileId = pRuntime.LastStrategicEndpointTileId;
            WorldTile locked = FindTile(lockedTileId);
            bool lockedLive = locked?.data != null;
            WorldTile candidate = lockedLive ? null :
                ArmyStrategicEndpointService.Resolve(pArmy, pTargetCity,
                    pExcludedTileId: -1);
            int selectedTileId = ArmyRtsRules.ResolveStableStrategicEndpoint(
                lockedTileId, lockedLive, candidate?.data?.tile_id ?? -1);
            WorldTile selected = FindTile(selectedTileId);
            pRuntime.LastStrategicEndpointTileId = selected?.data != null
                ? selectedTileId
                : -1;
            return selected;
        }

        private static void TryReopenJobOwnershipRepair(
            RuntimeState pRuntime)
        {
            if (pRuntime == null) return;
            if (pRuntime.JobCursor.JobsInitialized)
            {
                double now = CurrentWorldTime();
                if (!ArmyRtsRules.ShouldReopenJobOwnershipRepair(
                        jobsInitialized: true, currentWorldTime: now,
                        nextRepairWorldTime:
                            pRuntime.NextJobOwnershipRepairWorldTime))
                    return;
                pRuntime.JobCursor.Reopen();
            }
        }

        private static void EnsureJobs(Army pArmy, RuntimeState pRuntime,
            ArmyRtsMission pMission, ArmyRtsState pState)
        {
            Actor captain = SafeCaptain(pArmy);
            if (IsLiveCombatantActor(captain))
            {
                bool frontHold = ArmyRtsControllerRules.ShouldUseFrontHoldJob(
                    pMission?.ProposalKind ?? ArmyRtsProposalKind.None,
                    pState);
                SetJob(captain, frontHold
                    ? ArmyRtsContent.HoldJobId
                    : ArmyRtsContent.CaptainJobId,
                    frontHold ? ArmyRtsContent.HoldTaskId
                        : ArmyRtsContent.ResolveCaptainTaskId(pState,
                            ArmyRtsTransportService.GetPhase(pArmy)));
            }
            else
                ReleaseActor(captain);
            int count;
            try { count = pArmy.units.Count; }
            catch { count = 0; }
            bool jobsWereInitialized = pRuntime.JobCursor.JobsInitialized;
            int end = Math.Min(count, pRuntime.JobCursor.MemberCursor +
                                       MaximumJobMutationsPerController);
            for (int i = pRuntime.JobCursor.MemberCursor; i < end; i++)
            {
                Actor actor = pArmy.units[i];
                if (actor == captain) continue;
                bool transportOwned = actor?.is_inside_boat == true ||
                    ArmyRtsTransportService.OwnsActorTask(actor);
                bool ownsEscort = IsLiveWarriorActor(actor) &&
                    ArmyFormationRules.ShouldOwnEscortFollow(pState,
                        HasImmediateCombatPriority(actor),
                        transportOwned);
                if (ownsEscort)
                    SetJob(actor, ArmyRtsContent.FollowerJobId);
                else
                    ReleaseActor(actor);
            }
            pRuntime.JobCursor.Advance(end, count);
            if (!jobsWereInitialized && pRuntime.JobCursor.JobsInitialized)
                pRuntime.NextJobOwnershipRepairWorldTime =
                    CurrentWorldTime() +
                    ArmyRtsRules.JobOwnershipRepairIntervalSeconds;
        }

        private static void InstallFollowerSharedRoutes(Army pArmy,
            RuntimeState pRuntime, ArmyRtsState pState)
        {
            if (pArmy?.data == null || pRuntime == null) return;
            int count;
            try { count = pArmy.units.Count; }
            catch { count = 0; }
            if (count <= 0)
            {
                pRuntime.FollowerRouteInstallCursor = 0;
                return;
            }
            int start = Math.Max(0, Math.Min(
                pRuntime.FollowerRouteInstallCursor, count));
            int end = Math.Min(count, start +
                MaximumFollowerRouteChecksPerController);
            Actor captain = SafeCaptain(pArmy);
            for (int i = start; i < end; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                if (actor == captain || !IsLiveWarriorActor(actor))
                    continue;
                bool transportOwned = actor.is_inside_boat ||
                    ArmyRtsTransportService.OwnsActorTask(actor);
                if (!ArmyFormationRules.ShouldOwnEscortFollow(pState,
                        HasImmediateCombatPriority(actor), transportOwned) ||
                    !AWArmyMarchService.NeedsCompleteSharedRoute(actor))
                    continue;
                AWArmyMarchService.TryStartCompleteSharedRoute(actor);
            }
            pRuntime.FollowerRouteInstallCursor = end >= count ? 0 : end;
        }

        private static void SetJob(Actor pActor, string pJobId,
            string pTaskId = null, bool pForceReassert = false)
        {
            bool captainJob = (pJobId == ArmyRtsContent.CaptainJobId ||
                               pJobId == ArmyRtsContent.HoldJobId) &&
                              IsCaptain(pActor, pActor?.army);
            if (pActor?.data == null || pActor.ai == null ||
                pActor.isRekt() || !pActor.isAlive() ||
                !pActor.is_profession_warrior && !captainJob) return;
            try
            {
                bool expectedJob = pActor.ai.job?.id == pJobId;
                string taskId = !string.IsNullOrEmpty(pTaskId) ? pTaskId :
                    pJobId == ArmyRtsContent.CaptainJobId
                        ? ArmyRtsContent.MissionTaskId
                        : pJobId == ArmyRtsContent.HoldJobId
                            ? ArmyRtsContent.HoldTaskId
                            : ArmyRtsContent.FormationTaskId;
                bool expectedTask = pActor.isTask(taskId);
                bool ownsActor = OwnsLiveActor(pActor);
                bool immediateCombat = !expectedTask &&
                                       HasImmediateCombatPriority(pActor);
                bool requiredBoatWork = pActor.is_inside_boat ||
                    ArmyRtsTransportService.OwnsActorTask(pActor);
                if (!ArmyRtsTaskOwnershipRules.ShouldReassertMissionTask(
                            ArmyRtsRuntimeMode.Current, ownsActor,
                            pActor.isAlive(), expectedJob,
                            expectedTask && !pForceReassert,
                            immediateCombat, requiredBoatWork,
                            pForceRecovery: pForceReassert)) return;
                if (!expectedJob) pActor.ai.setJob(pJobId);
                if (pForceReassert)
                {
                    pActor.clearAttackTarget();
                    pActor.beh_actor_target = null;
                }
                pActor.ai.setTask(taskId);
            }
            catch { }
        }

        private static bool HasImmediateCombatPriority(Actor pActor)
        {
            if (pActor?.data == null || !pActor.has_attack_target)
                return false;
            BaseSimObject target = pActor.attack_target;
            bool targetAlive = false;
            bool targetHostile = false;
            bool targetCombatant = false;
            double distanceSquared = double.PositiveInfinity;
            try
            {
                targetAlive = target != null && target.isAlive() &&
                              !target.isRekt();
                targetCombatant = targetAlive && target.isActor() &&
                                  target.a?.data != null &&
                                  IsLiveCombatantActor(target.a);
                targetHostile = targetCombatant &&
                                pActor.canAttackTarget(target,
                                    pCheckForFactions: true,
                                    pAttackBuildings: false);
                if (targetAlive)
                {
                    double x = target.current_position.x -
                               pActor.current_position.x;
                    double y = target.current_position.y -
                               pActor.current_position.y;
                    distanceSquared = x * x + y * y;
                }
            }
            catch { }
            bool priority = ArmyRtsTaskOwnershipRules.
                HasImmediateCombatPriority(pActor.has_attack_target,
                    targetAlive, targetHostile, targetCombatant,
                    distanceSquared);
            if (priority) return true;
            try { pActor.clearAttackTarget(); }
            catch { }
            try
            {
                if (pActor.beh_actor_target == target)
                    pActor.beh_actor_target = null;
            }
            catch { }
            return false;
        }

        private static bool TargetComplete(Army pArmy,
            ArmyRtsMission pMission, City pTarget, Kingdom pKingdom)
        {
            if (pMission.Role == ArmyRtsRole.TemporaryGarrisonSortie)
                return GarrisonSortieService.ShouldCompleteMission(
                    pArmy, pMission, pTarget, pKingdom);
            if (!ArmyRtsObjectiveRules.ShouldUseObjectiveCompletion(
                    pMission.ProposalKind, pMission.Role) ||
                !ArmyRtsControllerRules.ShouldCompleteFriendlyTarget(
                    pMission.Role)) return false;
            War war = FindWar(pMission.WarId);
            ArmyRtsObjectiveState state = ArmyRtsObjectiveService.Classify(
                war, pKingdom, pTarget);
            if (pMission.ProposalKind == ArmyRtsProposalKind.Defend)
                return state != ArmyRtsObjectiveState.OpenDefense;
            return state != ArmyRtsObjectiveState.OpenAttack;
        }

        private static void ClearCompletedObjectiveRuntime(Army pArmy,
            RuntimeState pRuntime)
        {
            if (pArmy?.data == null || pRuntime == null) return;
            ArmyRtsMobilizationStatusService.Clear(pArmy);
            ArmyRtsTransportService.ReleaseArmy(pArmy);
            ArmyRouteProviderService.Cancel(pArmy.id,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmy.id);
            pRuntime.RouteSubmitted = false;
            pRuntime.RouteArrived = false;
            pRuntime.TransportRouteConfirmed = false;
            pRuntime.ForceTransportRoute = false;
            pRuntime.RouteImpossible = false;
            pRuntime.RouteProgress = 0;
            pRuntime.AnchorTileId = -1;
            pRuntime.AlternateTargetTileId = -1;
            pRuntime.PursuitRoute.Reset();
            Controllers.SetState(pArmy.id, ArmyRtsState.Idle);
            Actor captain = SafeCaptain(pArmy);
            if (captain?.current_tile?.data != null)
                ArmyFormationService.SetAnchor(pArmy, captain.current_tile);
        }

        private static void ReleaseArmyActors(Army pArmy)
        {
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                ReleaseActor(actor);
            }
            Actor captain = SafeCaptain(pArmy);
            if (captain?.data != null) ReleaseActor(captain);
        }

        private static bool IsCompletionEventForMission(Army pArmy,
            ArmyRtsMission pMission, City pTarget, Kingdom pKingdom)
        {
            if (pMission == null || pTarget?.data == null ||
                pKingdom?.data == null ||
                pMission.TargetCityId != pTarget.id) return false;
            if (pMission.Role == ArmyRtsRole.TemporaryGarrisonSortie)
                return GarrisonSortieService.ShouldCompleteMission(
                    pArmy, pMission, pTarget, pKingdom);
            if (!ArmyRtsObjectiveRules.ShouldUseObjectiveCompletion(
                    pMission.ProposalKind, pMission.Role)) return false;
            return CityAttackZoneService.IsControlledBySide(
                FindWar(pMission.WarId), pTarget, pKingdom);
        }

        private static void LogMissionChanged(Army pArmy,
            ArmyRtsMission pPrevious, ArmyRtsMission pNext,
            string pPhase)
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled) return;
            Actor captain = SafeCaptain(pArmy);
            ArmyRtsMission mission = pNext ?? pPrevious;
            War war = FindWar(mission?.WarId ?? -1L);
            City target = FindCity(mission?.TargetCityId ?? -1L);
            Kingdom kingdom = SafeKingdom(pArmy);
            ArmyRtsObjectiveState objectiveState =
                ArmyRtsObjectiveService.Classify(war, kingdom, target);
            bool controlledBySide = CityAttackZoneService.
                IsControlledBySide(war, target, kingdom);
            bool hostileMilitaryInside = CityAttackZoneService.
                HasHostileMilitaryInside(war, target, kingdom);
            bool hostileCaptureProgress = ArmyRtsObjectiveService.
                HasHostileCaptureProgress(war, kingdom, target);
            bool externallyControlled = ArmyRtsObjectiveService.
                IsExternallyControlled(war, target);
            Kingdom physicalController = null;
            try { physicalController = target?.being_captured_by; }
            catch { }
            long frozenControllerId = -1L;
            try
            {
                if (war?.data != null && target?.data != null)
                    WarScoreService.TryGetFrozenOccupation(war.data.id,
                        target.id, out frozenControllerId);
            }
            catch { frozenControllerId = -1L; }
            int generation = RuntimeByArmy.TryGetValue(pArmy?.id ?? -1L,
                out RuntimeState runtime)
                ? runtime.DirectorGeneration
                : -1;
            ModClass.LogInfo("[AW3 RTS health] phase=" + pPhase +
                             " army=" + (pArmy?.id ?? -1L) +
                             " captain=" +
                             (captain?.data?.id ?? -1L) +
                             " units=" + SafeUnitCount(pArmy) +
                             " old_target=" +
                             (pPrevious?.TargetCityId ?? -1L) +
                             " new_target=" +
                             (pNext?.TargetCityId ?? -1L) +
                             " war=" +
                             (pNext?.WarId ?? pPrevious?.WarId ?? -1L) +
                             " role=" +
                              (pNext?.Role.ToString() ??
                               pPrevious?.Role.ToString() ?? "none") +
                             " kind=" +
                             (mission?.ProposalKind.ToString() ?? "none") +
                             " objective=" + objectiveState +
                             " controlled_by_side=" + controlledBySide +
                             " hostile_military_inside=" +
                             hostileMilitaryInside +
                             " hostile_capture_progress=" +
                             hostileCaptureProgress +
                             " external_control=" + externallyControlled +
                             " owner=" + (target?.kingdom?.id ?? -1L) +
                             " physical_controller=" +
                             (physicalController?.id ?? -1L) +
                             " frozen_controller=" + frozenControllerId +
                             " generation=" + generation +
                             " front=" + (mission?.FrontId ?? -1L));
        }

        private static bool IsMissionValid(Army pArmy,
            ArmyRtsMission pMission)
        {
            Kingdom kingdom = SafeKingdom(pArmy);
            War war = FindWar(pMission?.WarId ?? -1L);
            City target = FindCity(pMission?.TargetCityId ?? -1L);
            try
            {
                bool baseValid = pMission != null &&
                                 IsLiveKingdom(kingdom) &&
                                 IsLiveCity(target) && war?.data != null &&
                                 !war.hasEnded() && war.hasKingdom(kingdom) &&
                                 (target.kingdom == kingdom ||
                                  war.hasKingdom(target.kingdom));
                if (!baseValid || pMission.ProposalKind !=
                    ArmyRtsProposalKind.Retreat) return baseValid;
                return ArmyRtsObjectiveRules.IsRetreatAnchorValid(
                    cityLive: true,
                    ownedByArmyKingdom: target.kingdom == kingdom,
                    hostileCaptureActive: target.isGettingCaptured(),
                    enemyFrozenControlled: WarScoreService.
                        IsCityFrozenControlledByEnemySide(target, kingdom));
            }
            catch { return false; }
        }

        private static int TileDistanceSquared(WorldTile pFirst,
            WorldTile pSecond)
        {
            if (pFirst == null || pSecond == null) return int.MaxValue;
            long x = pFirst.x - pSecond.x;
            long y = pFirst.y - pSecond.y;
            long value = x * x + y * y;
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private static bool SafeSameIsland(WorldTile pFirst,
            WorldTile pSecond)
        {
            if (pFirst?.data == null || pSecond?.data == null) return false;
            try { return pFirst.isSameIsland(pSecond); }
            catch { return false; }
        }

        private static int ResolveMissionTargetStrength(Army pArmy,
            Kingdom pKingdom, ArmyRtsMission pMission)
        {
            int living = SafeUnitCount(pArmy);
            int resolved;
            if (pArmy?.data != null && !AWArmyService.IsSpecialArmy(pArmy))
            {
                int approved = CityArmyReinforcementService.ApprovedTarget(
                    pArmy, pKingdom);
                resolved = Math.Max(living, approved);
            }
            else
            {
                resolved = ArmyRtsRules.ResolveMissionTargetStrength(
                    pMission?.TargetStrength ?? 0,
                    StandingArmyService.TargetStrength(pArmy, pKingdom),
                    living);
            }
            if (pMission != null) pMission.TargetStrength = resolved;
            return resolved;
        }

        private static int SafeUnitCount(Army pArmy)
        {
            try { return Math.Max(0, pArmy?.countUnits() ?? 0); }
            catch { return 0; }
        }

        private static bool IsCaptain(Actor pActor, Army pArmy)
        {
            try { return pActor?.data != null && pArmy?.getCaptain() == pActor; }
            catch { return false; }
        }

        private static bool ShouldOwnMilitaryActor(Actor pActor,
            bool pMissionActive)
        {
            Army actorArmy = pActor?.army;
            if (pMissionActive && actorArmy?.data != null &&
                Controllers.TryGet(actorArmy.id,
                    out ArmyRtsControllerRecord record) &&
                ArmyRtsWarLifecycleService.TryGet(
                    record?.Mission?.WarId ?? -1L, actorArmy.id,
                    out ArmyRtsWarLifecycleRecord lifecycle) &&
                !ArmyRtsWarLifecycleRules.OwnsTacticalActors(
                    lifecycle.Phase))
                return false;
            bool actorValid = IsLiveActor(pActor);
            bool hasArmyIndex;
            try { hasArmyIndex = pActor?.hasArmy() == true; }
            catch { hasArmyIndex = false; }
            return ArmyRtsRules.ShouldOwnMilitaryActor(
                ArmyRtsRuntimeMode.ShouldCommit,
                actorValid,
                pActor?.is_profession_warrior == true,
                hasArmyIndex,
                pMissionActive,
                isCivilAuthority: IsCivilAuthorityActor(pActor),
                isCurrentCaptain: IsCaptain(pActor, pActor?.army));
        }

        private static bool IsLiveCombatantActor(Actor pActor)
        {
            return IsLiveActor(pActor) &&
                   (IsCaptain(pActor, pActor?.army) ||
                    pActor.is_profession_warrior &&
                    !IsCivilAuthorityActor(pActor));
        }

        private static bool IsLiveActor(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.isAlive() &&
                       !pActor.isRekt();
            }
            catch { return false; }
        }

        private static bool IsLiveWarriorActor(Actor pActor)
        {
            return IsLiveActor(pActor) && pActor.is_profession_warrior &&
                   !IsCivilAuthorityActor(pActor);
        }

        private static bool IsCivilAuthorityActor(Actor pActor)
        {
            try
            {
                return pActor?.data != null &&
                       (pActor.isKing() || pActor.isCityLeader());
            }
            catch { return false; }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try { return pArmy?.getCaptain(); }
            catch { return null; }
        }

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return pArmy?.getKingdom(); }
            catch { return null; }
        }

        private static bool IsLiveArmy(Army pArmy)
        {
            try { return pArmy?.data != null && pArmy.isAlive(); }
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

        private static Army FindArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }

        private static Actor FindActor(long pActorId)
        {
            try { return pActorId >= 0L ? World.world?.units?.get(pActorId) : null; }
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

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static WorldTile FindTile(int pTileId)
        {
            try
            {
                WorldTile[] tiles = World.world?.tiles_list;
                return tiles != null && pTileId >= 0 && pTileId < tiles.Length
                    ? tiles[pTileId]
                    : null;
            }
            catch { return null; }
        }

        private static double CurrentWorldTime()
        {
            try { return World.world?.getCurWorldTime() ?? 0d; }
            catch { return 0d; }
        }

        private static double CurrentRealtime()
        {
            try { return UnityEngine.Time.realtimeSinceStartupAsDouble; }
            catch { return 0d; }
        }
    }
#endif
}
