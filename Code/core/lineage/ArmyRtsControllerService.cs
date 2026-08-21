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
using life.taxi;
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
        public const int StrategicArrivalRadius = 2;

        public static bool ShouldPrioritizeMission(ArmyRtsMission pMission)
        {
            return pMission != null && pMission.WarId >= 0L;
        }

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

        public static bool ShouldApplyTargetChangeCooldown(
            ArmyRtsProposalKind pPreviousKind,
            ArmyRtsProposalKind pNextKind)
        {
            return pPreviousKind == ArmyRtsProposalKind.Attack &&
                   pNextKind == ArmyRtsProposalKind.Attack;
        }

        public static bool ShouldEnterTargetCityCombat(
            ArmyRtsProposalKind pProposalKind, bool objectiveOpen,
            bool captainInsideTargetCombatZone,
            bool hostileMilitaryInsideTarget)
        {
            if (!objectiveOpen || !captainInsideTargetCombatZone ||
                !hostileMilitaryInsideTarget) return false;
            return pProposalKind == ArmyRtsProposalKind.Attack ||
                   pProposalKind == ArmyRtsProposalKind.Defend;
        }

        public static bool ShouldInspectTargetCityCombat(
            bool captainInsideTargetCombatZone, bool siegeCombatActive)
        {
            return captainInsideTargetCombatZone || siegeCombatActive;
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

        public static bool ShouldResetNativeMovement(
            bool previousMissionExists, bool operationalProgressChanged)
        {
            return previousMissionExists && operationalProgressChanged;
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

        public static bool HasReachedStrategicDestination(int captainTileId,
            int destinationTileId, int distanceSquared,
            bool sameTargetZone, bool endpointValidated,
            int arrivalRadius)
        {
            if (HasReachedStrategicDestination(captainTileId,
                    destinationTileId)) return true;
            if (captainTileId < 0 || destinationTileId < 0 ||
                distanceSquared < 0 || !sameTargetZone ||
                !endpointValidated) return false;
            int radius = Math.Max(0, arrivalRadius);
            return (long)distanceSquared <= (long)radius * radius;
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
            return pState == ArmyRtsState.March ||
                   pState == ArmyRtsState.Deploy ||
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

        public static bool ShouldUseVanillaArmyMovement(
            ArmyRtsProposalKind pProposalKind, bool targetIsEnemy)
        {
            return pProposalKind == ArmyRtsProposalKind.Attack &&
                   targetIsEnemy;
        }

        public static bool ShouldUseVanillaRetreatMovement(
            ArmyRtsProposalKind pProposalKind, bool targetIsEnemy)
        {
            return pProposalKind == ArmyRtsProposalKind.Retreat &&
                   !targetIsEnemy;
        }

        public static bool ShouldUseVanillaFollowerMovement(
            ArmyRtsProposalKind pProposalKind, bool targetIsEnemy)
        {
            _ = targetIsEnemy;
            return pProposalKind == ArmyRtsProposalKind.Attack ||
                   pProposalKind == ArmyRtsProposalKind.Defend ||
                   pProposalKind == ArmyRtsProposalKind.Retreat;
        }

        public static bool ShouldUseNativeMissionExecution(
            ArmyRtsProposalKind pProposalKind, ArmyRtsState pState,
            bool targetIsEnemy)
        {
            _ = targetIsEnemy;
            if (pProposalKind == ArmyRtsProposalKind.None ||
                pProposalKind == ArmyRtsProposalKind.FrontHold)
                return false;
            switch (pState)
            {
                case ArmyRtsState.March:
                case ArmyRtsState.Deploy:
                case ArmyRtsState.Assault:
                case ArmyRtsState.Pursue:
                case ArmyRtsState.Retreat:
                case ArmyRtsState.Regroup:
                    return true;
                default:
                    return false;
            }
        }

        public static bool ShouldPrimeForcedTransportBeforeNativeAttack(
            bool isCaptain, bool usesVanillaAttack, bool sameIsland,
            bool transportOwned)
        {
            return isCaptain && usesVanillaAttack && !sameIsland &&
                   !transportOwned;
        }
    }

    internal static class ArmyRtsP0Rules
    {
        internal static bool ShouldTraceDiagnosticActor(bool isCaptain,
            bool isRoyalGuard, long actorId, bool anomaly,
            int memberSampleModulo)
        {
            if (isCaptain || isRoyalGuard || anomaly) return true;
            int modulo = Math.Max(1, memberSampleModulo);
            return actorId >= 0L && actorId % modulo == 0L;
        }

        internal static bool ShouldWriteDiagnosticStage(string stage,
            bool anomaly)
        {
            if (anomaly) return true;
            return string.Equals(stage, "p0_enter",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "combat_p0",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "combat_after_ai",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "combat_after_move_command",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "follower_after_ai",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "follower_after_move_command",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "self_landing_enter",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "self_landing_move_command",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "return_prepare",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "return_native_pipeline",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "return_target_resolved",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "return_transport_yield",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "return_after_path",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "return_after_ai",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "transport_yield",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "prepare_failed",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "transport_result",
                       StringComparison.Ordinal) ||
                   string.Equals(stage, "p0_chunk_begin",
                       StringComparison.Ordinal);
        }

        internal static bool ShouldRateLimitDiagnostic(bool anomaly,
            double elapsedSeconds, double minimumIntervalSeconds)
        {
            return !anomaly && elapsedSeconds < minimumIntervalSeconds;
        }
    }

    public sealed class ArmyRtsControllerWorkIndex
    {
        private readonly Dictionary<long, ArmyRtsControllerRecord> _records =
            new Dictionary<long, ArmyRtsControllerRecord>();
        private readonly Queue<long> _queued = new Queue<long>();
        private readonly HashSet<long> _queuedIds = new HashSet<long>();
        private readonly Queue<long> _priorityQueued = new Queue<long>();
        private readonly HashSet<long> _priorityQueuedIds =
            new HashSet<long>();
        private readonly List<long> _frameBatch = new List<long>(
            ArmyRtsControllerRules.MaximumControllersPerFrame);

        public int Count => _records.Count;
        public int QueuedCount => _queuedIds.Count;
        public int PendingCount => _queuedIds.Count +
                                   _priorityQueuedIds.Count;

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
            Enqueue(pMission.ArmyId,
                ArmyRtsControllerRules.ShouldPrioritizeMission(copy));
            return changed;
        }

        public bool TryGet(long pArmyId,
            out ArmyRtsControllerRecord pRecord)
        {
            return _records.TryGetValue(pArmyId, out pRecord);
        }

        public IReadOnlyList<long> SnapshotArmyIds()
        {
            var ids = new List<long>(_records.Keys);
            ids.Sort();
            return ids;
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
            while (_frameBatch.Count < limit &&
                   (_priorityQueued.Count > 0 || _queued.Count > 0))
            {
                bool priority = _priorityQueued.Count > 0;
                long armyId = priority
                    ? _priorityQueued.Dequeue()
                    : _queued.Dequeue();
                bool removed = priority
                    ? _priorityQueuedIds.Remove(armyId)
                    : _queuedIds.Remove(armyId);
                if (!removed ||
                    !_records.ContainsKey(armyId)) continue;
                _frameBatch.Add(armyId);
            }
            return _frameBatch;
        }

        public bool Requeue(long pArmyId)
        {
            return _records.ContainsKey(pArmyId) && Enqueue(pArmyId,
                ArmyRtsControllerRules.ShouldPrioritizeMission(
                    _records[pArmyId].Mission));
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
            _priorityQueuedIds.Remove(pArmyId);
            return _records.Remove(pArmyId);
        }

        public void Clear()
        {
            _records.Clear();
            _queued.Clear();
            _queuedIds.Clear();
            _priorityQueued.Clear();
            _priorityQueuedIds.Clear();
        }

        private bool Enqueue(long pArmyId, bool pPriority)
        {
            if (pPriority)
            {
                _queuedIds.Remove(pArmyId);
                if (!_priorityQueuedIds.Add(pArmyId)) return false;
                _priorityQueued.Enqueue(pArmyId);
                return true;
            }
            _priorityQueuedIds.Remove(pArmyId);
            if (!_queuedIds.Add(pArmyId)) return false;
            _queued.Enqueue(pArmyId);
            return true;
        }
    }

    public sealed class ArmyRtsJobAssignmentCursor
    {
        private long _observedRosterVersion = long.MinValue;

        public int MemberCursor { get; private set; }
        public bool JobsInitialized { get; private set; }

        public bool ObserveRosterVersion(long rosterVersion)
        {
            if (_observedRosterVersion == rosterVersion) return false;
            bool changed = _observedRosterVersion != long.MinValue;
            _observedRosterVersion = rosterVersion;
            Reopen();
            return changed;
        }

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
        private const int MaximumJobMutationsPerController = 128;
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
            internal bool FieldCombatReleased;
            internal bool SiegeCombatActive;
            internal long SiegeTargetActorId = -1L;
            internal bool RetreatSelectionPending;
            internal bool NoSafeRetreat;
            internal long RosterVersion;
            internal long LastTargetCityId = -1L;
            internal double LastTargetChangeTime = -1d;
            internal double EscortBelowQuorumSinceWorldTime = double.NaN;
            internal bool EscortHoldActive;
            internal double NextJobOwnershipRepairWorldTime =
                double.PositiveInfinity;
            internal double NextMissingTargetRecoveryWorldTime =
                double.NegativeInfinity;
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
            internal readonly ArmyNativeRouteLock NativeRoute =
                new ArmyNativeRouteLock();
            internal readonly Dictionary<long, int> MemberObjectiveTileByActor =
                new Dictionary<long, int>();
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

        internal static bool TryGetLockedCaptainRoute(Army pArmy,
            out IReadOnlyList<int> pTileIds, out int pCursor,
            out int pGeneration)
        {
            pTileIds = null;
            pCursor = 0;
            pGeneration = 0;
            if (pArmy?.data == null ||
                !RuntimeByArmy.TryGetValue(pArmy.id,
                    out RuntimeState runtime) ||
                !runtime.NativeRoute.IsLocked) return false;
            pTileIds = new List<int>(runtime.NativeRoute.TileIds);
            pCursor = runtime.NativeRoute.Cursor;
            pGeneration = runtime.NativeRoute.Generation;
            return pTileIds.Count > 0;
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
                bool proposalValid = warMembershipValid &&
                    ArmyRtsObjectiveRules.CanCommit(
                        proposal.ProposalKind, objectiveState,
                        armyKingdom?.id ?? -1L, pSnapshot.KingdomId,
                        proposal.OpenObjectiveCount) &&
                    ValidateMissionTarget(army, mission);
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
            bool sameStrategicIntent = record.Mission.WarId ==
                                           pProposal.WarId &&
                                       record.Mission.TargetCityId ==
                                           pProposal.TargetCityId &&
                                       record.Mission.ProposalKind ==
                                           pProposal.ProposalKind;
            if (ArmyRtsAssignmentReconciliationRules.
                    MustRehydrateRetainedMission(
                        runtime.TargetCompletionLatched,
                        ArmyStallWatchdogService.IsRegistered(pArmy.id),
                        sameStrategicIntent,
                        replacementPublished: false) &&
                RehydrateAfterAuthorityChange(pArmy))
                RuntimeByArmy.TryGetValue(pArmy.id, out runtime);
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
            if (ArmyRtsMissionLockRules.
                    ShouldRetainLockedStrategicMission(
                        missionValid, targetComplete, targetCoolingDown,
                        proposedHomelandEmergency))
                return true;
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
            if (!missionPublishable)
            {
                if (!HasActiveMission(pArmy.id))
                    ArmyRtsWarLifecycleService.MarkWaiting(pMission.WarId,
                        pArmy, "mission_rejected_invalid",
                        CurrentWorldTime() +
                        ArmyRtsAssignmentReconciliationRules.
                            AssignmentRetryWorldSeconds);
                KingdomWarDirectorService.QueueArmyChanged(missionKingdom);
                return;
            }
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

            // 目标城市变更冷却：防止频繁振荡
            bool targetCityChanged = previousMission != null &&
                previousMission.TargetCityId != pMission.TargetCityId;
            if (targetCityChanged &&
                ArmyRtsControllerRules.ShouldApplyTargetChangeCooldown(
                    previousMission.ProposalKind,
                    pMission.ProposalKind) &&
                RuntimeByArmy.TryGetValue(pArmy.id, out RuntimeState cooldownCheck))
            {
                double now = CurrentWorldTime();
                double timeSinceLastChange = now - cooldownCheck.LastTargetChangeTime;
                const double MinChangeIntervalSeconds = 30d;
                if (cooldownCheck.LastTargetCityId == pMission.TargetCityId &&
                    timeSinceLastChange < MinChangeIntervalSeconds)
                {
                    // 冷却期内切回旧目标 → 拒绝变更，保持当前目标
                    pMission.TargetCityId = previousMission.TargetCityId;
                    targetCityChanged = false;
                }
            }

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
                ClearIndependentMemberPaths(pArmy, previousRuntime);
                ClearArmyAttackTargets(pArmy);
                if (ArmyRtsControllerRules.ShouldResetNativeMovement(
                        previousMission != null,
                        resetOperationalProgress))
                    ClearNativeMovementForMissionReplacement(pArmy);
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
            RuntimeByArmy[pArmy.id].RetreatSelectionPending = false;
            RuntimeByArmy[pArmy.id].NoSafeRetreat = false;

            // 记录目标城市变更时间（用于冷却期检测）
            if (targetCityChanged)
            {
                RuntimeState runtime = RuntimeByArmy[pArmy.id];
                runtime.LastTargetCityId = previousMission.TargetCityId;
                runtime.LastTargetChangeTime = CurrentWorldTime();
            }

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

        internal static IReadOnlyList<ArmyRtsMission> SnapshotMissions()
        {
            var missions = new List<ArmyRtsMission>();
            IReadOnlyList<long> ids = Controllers.SnapshotArmyIds();
            for (int i = 0; i < ids.Count; i++)
            {
                if (!Controllers.TryGet(ids[i],
                        out ArmyRtsControllerRecord record) ||
                    record?.Mission == null) continue;
                missions.Add(ArmyRtsControllerRules.CopyMission(
                    record.Mission));
            }
            return missions;
        }

        public static bool HasValidMission(Army pArmy)
        {
            if (pArmy?.data == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return false;
            return IsMissionValid(pArmy, record.Mission);
        }

        internal static bool ValidateMissionTarget(Army pArmy,
            ArmyRtsMission pMission)
        {
            return IsMissionValid(pArmy, pMission);
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
                bool playerRetreat = Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord routeRecord) &&
                    ArmyRtsWarDoctrineRules.IsExplicitPlayerRetreat(
                        routeRecord?.Mission);
                if (!ArmyRtsWarDoctrineRules.AllowWithdrawal(
                        ArmyRtsWarDoctrine.Current,
                        ArmyRtsWithdrawalOrigin.Watchdog,
                        playerRetreat))
                {
                    Controllers.SetState(pArmyId, ArmyRtsState.Hold);
                    Controllers.Requeue(pArmyId);
                    return;
                }
                Controllers.SetState(pArmyId, ArmyRtsState.Retreat);
                ArmyRouteProviderService.Cancel(pArmyId,
                    ArmyRouteCancelReason.TargetReplaced);
                AWArmyMarchService.ClearArmy(pArmyId);
                Controllers.Requeue(pArmyId);
            }
        }

        public static void ProcessFrame()
        {
            ProcessFrame(
                ArmyRtsControllerRules.MaximumControllersPerFrame,
                ArmyRtsReplenishmentArrivalRules.
                    MaximumArrivalChecksPerFrame);
        }

        public static void ProcessFrame(int pControllerBudget,
            int pReplenishmentBudget)
        {
            ArmyRtsMode mode = ArmyRtsRuntimeMode.Current;
            // Director planning is intentionally throttled by large-step
            // scheduling. Controller work owns live tasks and bounded
            // follower recovery, so it must consume every admitted logical
            // pass while RTS remains enabled.
            if (mode == ArmyRtsMode.Off) return;
            if (ArmyRtsWarDoctrine.IsAbstractDecisive) return;
            ProcessPendingReplenishmentArrivals(pReplenishmentBudget);
            ArmyRtsTransportService.ProcessOrdinaryFrame();
            IReadOnlyList<long> batch = Controllers.Take(Math.Min(
                Math.Max(0, pControllerBudget), Controllers.PendingCount));
            for (int i = 0; i < batch.Count; i++)
            {
                try
                {
                    ProcessOne(batch[i], mode);
                }
                catch (System.Exception error)
                {
                    ModClass.LogWarning(
                        "AW army RTS controller faulted on army " +
                        batch[i] + "; requeued to avoid orphaning: " + error);
                    Controllers.Requeue(batch[i]);
                }
            }
        }

        public static int PendingControllerCount =>
            Controllers.PendingCount;

        public static int PendingReplenishmentArrivalCount =>
            PendingReplenishmentArrivalQueue.Count;

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

        private static void ProcessPendingReplenishmentArrivals(
            int pMaximum)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                PendingReplenishmentArrivals.Count == 0 ||
                PendingReplenishmentArrivalQueue.Count == 0 ||
                World.world == null || World.world.isPaused()) return;
            double now = CurrentRealtime();
            int limit = Math.Min(Math.Max(0, pMaximum),
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
                 HasMilitaryTransportOwnership(actor));
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
            Kingdom kingdom = SafeKingdom(pArmy);
            if (kingdom?.data == null || pSourceCity.kingdom != kingdom ||
                captain?.current_tile?.data == null ||
                captain.is_inside_boat) return false;
            return true;
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
            if (runtime.RetreatSelectionPending || runtime.NoSafeRetreat)
                return false;
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
            if (!sameMissionIsland) return false;
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
            if (!HasFollowerMission(pActor) || army?.data == null)
            {
                pTarget = null;
                return ArmyFollowerTargetResult.Unavailable;
            }
            if (!Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null ||
                !RuntimeByArmy.TryGetValue(army.id,
                    out RuntimeState runtime))
            {
                ArmyFollowerTargetResult sharedResult =
                    AWArmyMarchService.ResolveFollowerTarget(pActor,
                        out pTarget);
                if (sharedResult != ArmyFollowerTargetResult.Unavailable)
                    return sharedResult;
                bool formationTargetAvailable =
                    ArmyFormationService.TryGetFollowerTarget(pActor,
                        out pTarget);
                return ArmySharedPathRules.ResolveFollowerTargetSource(
                    sharedResult, formationTargetAvailable,
                    pTarget == pActor.current_tile);
            }
            City targetCity = FindCity(
                ArmyRtsMemberObjectiveRules.ResolveTargetCityId(
                    record.Mission.TargetCityId,
                    routeFailureTargetCityId: -1L));
            pTarget = runtime.PursuitRoute.Active
                ? FindTile(runtime.PursuitRoute.EndpointTileId)
                : FindTile(runtime.AlternateTargetTileId) ??
                  ResolveStableStrategicEndpoint(army, targetCity, runtime);
            if (pTarget?.data == null)
                return ArmyFollowerTargetResult.Unavailable;
            return pTarget == pActor.current_tile
                ? ArmyFollowerTargetResult.Hold
                : ArmyFollowerTargetResult.Move;
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

        internal static bool IsValidCaptainCombatTarget(Actor pCaptain,
            Actor pTarget)
        {
            if (pCaptain?.data == null || pTarget?.data == null ||
                pCaptain.isRekt() || pTarget.isRekt() ||
                !pCaptain.isAlive() || !pTarget.isAlive() ||
                pCaptain.current_tile?.data == null ||
                pTarget.current_tile?.data == null ||
                pCaptain.current_tile.isSameIsland(pTarget.current_tile) != true)
                return false;
            return ArmyRtsCaptainCombatRules.ShouldRetainTarget(
                targetAlive: true,
                targetHostile: IsHostileCaptainTarget(pCaptain, pTarget),
                withinEnvelope: IsWithinCaptainCombatEnvelope(
                    pCaptain, pTarget));
        }

        internal static bool IsValidMemberCombatTarget(Actor pActor,
            Actor pTarget)
        {
            if (!HasValidMemberCombatActorContext(pActor)) return false;
            return IsValidOwnedMemberCombatTarget(pActor, pTarget);
        }

        private static bool HasValidMemberCombatActorContext(Actor pActor)
        {
            return pActor?.data != null && !pActor.isRekt() &&
                   pActor.isAlive() && pActor.current_tile?.data != null &&
                   HasMemberCombatMission(pActor);
        }

        private static bool IsValidOwnedMemberCombatTarget(Actor pActor,
            Actor pTarget)
        {
            bool targetAlive = pTarget?.data != null &&
                               !pTarget.isRekt() && pTarget.isAlive();
            bool canAttack = false;
            try { canAttack = targetAlive && pActor.isTargetOkToAttack(pTarget); }
            catch { }
            bool sameIsland = targetAlive &&
                              pTarget.current_tile?.data != null &&
                              pActor.current_tile.isSameIsland(
                                  pTarget.current_tile) == true;
            return ArmyRtsCaptainCombatRules.ShouldRetainMemberTarget(
                targetAlive,
                targetHostile: canAttack &&
                                IsHostileCaptainTarget(pActor, pTarget),
                sameIsland,
                combatOwned: true);
        }

        private static bool IsViableSiegeCombatTarget(Actor pActor,
            Actor pTarget)
        {
            if (pActor?.data == null || pTarget?.data == null ||
                pActor.isRekt() || pTarget.isRekt() ||
                !pActor.isAlive() || !pTarget.isAlive() ||
                !pTarget.is_profession_warrior ||
                pActor.current_tile?.data == null ||
                pTarget.current_tile?.data == null ||
                pActor.current_tile.isSameIsland(pTarget.current_tile) != true)
                return false;
            return IsHostileCaptainTarget(pActor, pTarget);
        }

        internal static Actor FindCaptainCombatTarget(Actor pCaptain)
        {
            if (pCaptain?.data == null || pCaptain.current_tile?.data == null)
                return null;
            Actor best = null;
            int bestDistance = int.MaxValue;
            try
            {
                foreach (Actor candidate in Finder.getUnitsFromChunk(
                             pCaptain.current_tile, 2, 10))
                {
                    if (!IsValidCaptainCombatTarget(pCaptain, candidate))
                        continue;
                    int distance = Toolbox.SquaredDistTile(
                        pCaptain.current_tile, candidate.current_tile);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = candidate;
                }
            }
            catch { }
            return best;
        }

        internal static bool HasActiveTargetCitySiege(Actor pActor)
        {
            return TryGetActiveTargetCitySiege(pActor, out _, out _);
        }

        internal static bool IsValidAssignedCombatTarget(Actor pActor,
            Actor pTarget)
        {
            if (HasActiveTargetCitySiege(pActor))
                return IsValidSiegeCombatTarget(pActor, pTarget);
            if (pActor?.isTask(ArmyRtsContent.MemberCombatTaskId) == true)
                return IsValidMemberCombatTarget(pActor, pTarget);
            return IsValidCaptainCombatTarget(pActor, pTarget);
        }

        internal static bool IsValidSiegeCombatTarget(Actor pActor,
            Actor pTarget)
        {
            if (!TryGetActiveTargetCitySiege(pActor, out _,
                    out City targetCity) ||
                !IsViableSiegeCombatTarget(pActor, pTarget)) return false;
            return IsInsideCityCombatZone(pTarget, targetCity);
        }

        internal static Actor FindSiegeCombatTarget(Actor pActor)
        {
            if (!TryGetActiveTargetCitySiege(pActor, out RuntimeState runtime,
                    out City targetCity)) return null;
            Actor target = null;
            try { target = World.world?.units?.get(runtime.SiegeTargetActorId); }
            catch { }
            if (IsValidSiegeCombatTarget(pActor, target)) return target;
            return FindCaptainCombatTarget(pActor) is Actor localTarget &&
                   IsInsideCityCombatZone(localTarget, targetCity)
                ? localTarget
                : null;
        }

        private static bool TryGetActiveTargetCitySiege(Actor pActor,
            out RuntimeState pRuntime, out City pTargetCity)
        {
            pRuntime = null;
            pTargetCity = null;
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                !RuntimeByArmy.TryGetValue(army.id, out pRuntime) ||
                !pRuntime.SiegeCombatActive || !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record)) return false;
            pTargetCity = FindCity(record?.Mission?.TargetCityId ?? -1L);
            return pTargetCity?.data != null;
        }

        private static bool IsInsideCityCombatZone(Actor pActor,
            City pTargetCity)
        {
            if (pActor?.current_tile == null || pTargetCity?.data == null)
                return false;
            TileZone zone = null;
            try { zone = pActor.current_tile.zone; }
            catch { }
            try
            {
                return zone?.city == pTargetCity ||
                       pTargetCity.border_zones?.Contains(zone) == true;
            }
            catch { return false; }
        }

        private static bool IsWarActive(War pWar)
        {
            if (pWar?.data == null) return false;
            try { return !pWar.hasEnded(); }
            catch { return false; }
        }

        private static Actor FindTargetCitySiegeTarget(Actor pSeeker,
            City pTargetCity)
        {
            if (pSeeker?.current_tile?.data == null ||
                pTargetCity?.data == null) return null;
            var scan = new TargetCitySiegeScan(pSeeker, pTargetCity);
            try
            {
                for (int i = 0; i < pTargetCity.zones.Count; i++)
                    scan.ScanZone(pTargetCity.zones[i]);
                if (pTargetCity.border_zones != null)
                    foreach (TileZone zone in pTargetCity.border_zones)
                        scan.ScanZone(zone);
            }
            catch { }
            return scan.Best;
        }

        private sealed class TargetCitySiegeScan
        {
            private readonly Actor _seeker;
            private readonly City _targetCity;
            private int _bestDistance = int.MaxValue;
            internal Actor Best { get; private set; }

            internal TargetCitySiegeScan(Actor pSeeker, City pTargetCity)
            {
                _seeker = pSeeker;
                _targetCity = pTargetCity;
            }

            internal void ScanZone(TileZone pZone)
            {
                if (pZone?.tiles == null) return;
                for (int i = 0; i < pZone.tiles.Length; i++)
                {
                    WorldTile tile = pZone.tiles[i];
                    if (tile == null) continue;
                    tile.doUnits(candidate =>
                    {
                        if (!IsViableSiegeCombatTarget(_seeker, candidate) ||
                            !IsInsideCityCombatZone(candidate, _targetCity))
                            return true;
                        int distance = Toolbox.SquaredDistTile(
                            _seeker.current_tile, candidate.current_tile);
                        if (distance < _bestDistance)
                        {
                            _bestDistance = distance;
                            Best = candidate;
                        }
                        return true;
                    });
                }
            }
        }

        internal static void ReassertCaptainMissionTask(Actor pCaptain)
        {
            Army army = pCaptain?.army;
            if (pCaptain?.data == null || army?.data == null ||
                !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return;
            string taskId = ArmyRtsContent.ResolveCaptainTaskId(
                record.State, ArmyRtsTransportService.GetPhase(army));
            SetJob(pCaptain, ArmyRtsContent.CaptainJobId, taskId,
                pForceReassert: true);
        }

        private static bool IsHostileCaptainTarget(Actor pCaptain,
            Actor pTarget)
        {
            try
            {
                return pTarget?.kingdom != null &&
                       pCaptain?.kingdom != null &&
                       pCaptain.kingdom.isEnemy(pTarget.kingdom) &&
                       pCaptain.canAttackTarget(pTarget,
                           pCheckForFactions: true,
                           pAttackBuildings: false);
            }
            catch { return false; }
        }

        private static bool IsWithinCaptainCombatEnvelope(Actor pCaptain,
            Actor pTarget)
        {
            try
            {
                int distance = Toolbox.SquaredDistTile(
                    pCaptain.current_tile, pTarget.current_tile);
                return distance <= 10 * 10;
            }
            catch { return false; }
        }

        internal static void SetCaptainCombatTask(Actor pCaptain)
        {
            if (pCaptain?.data == null || pCaptain.army?.data == null) return;
            SetJob(pCaptain, ArmyRtsContent.CaptainJobId,
                ArmyRtsContent.CaptainCombatTaskId,
                pForceReassert: true);
            ArmyMilitaryMovementPriorityIndex.Register(
                pCaptain.data.id,
                ArmyMilitaryMovementPriorityKind.RtsMember);
        }

        internal static void TrySetCaptainTacticalTask(Actor pCaptain)
        {
            Army army = pCaptain?.army;
            if (!HasCaptainMission(pCaptain) || army?.data == null ||
                !RuntimeByArmy.TryGetValue(army.id,
                    out RuntimeState runtime))
                return;
            if (!runtime.FieldCombatReleased && !runtime.SiegeCombatActive)
            {
                RepairCaptainVanillaFight(pCaptain);
                return;
            }
            SetCaptainTacticalTask(pCaptain);
        }

        internal static bool TryRedirectVanillaFight(Actor pActor,
            string pTaskId)
        {
            if (!string.Equals(pTaskId, "fighting",
                    StringComparison.Ordinal)) return false;
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                !HasActiveMission(army.id) ||
                RoyalGuardService.IsRoyalGuard(pActor)) return false;
            // A member's fighting task is the native task that just acquired its target.
            // Replacing its job here clears that handoff before native fighting can run.
            if (!IsCaptain(pActor, army)) return false;
            Actor target = pActor.attack_target?.a;
            if (!IsValidCaptainCombatTarget(pActor, target))
                target = pActor.beh_actor_target?.a;
            if (!IsValidCaptainCombatTarget(pActor, target))
                target = FindCaptainCombatTarget(pActor);
            if (IsValidCaptainCombatTarget(pActor, target))
            {
                pActor.beh_actor_target = target;
                SetCaptainTacticalTask(pActor);
            }
            else
            {
                ClearActorAttackTarget(pActor);
                ReassertCaptainMissionTask(pActor);
            }
            return true;
        }

        private static void RepairCaptainVanillaFight(Actor pCaptain)
        {
            if (pCaptain?.data == null || !pCaptain.isTask("fighting"))
                return;
            Actor target = pCaptain.attack_target?.a;
            if (!IsValidCaptainCombatTarget(pCaptain, target))
                target = pCaptain.beh_actor_target?.a;
            if (!IsValidCaptainCombatTarget(pCaptain, target))
                target = FindCaptainCombatTarget(pCaptain);
            if (IsValidCaptainCombatTarget(pCaptain, target))
            {
                pCaptain.beh_actor_target = target;
                SetCaptainCombatTask(pCaptain);
                return;
            }
            ClearActorAttackTarget(pCaptain);
            ReassertCaptainMissionTask(pCaptain);
        }

        private static void SetCaptainTacticalTask(Actor pCaptain)
        {
            if (pCaptain?.data == null || pCaptain.army?.data == null) return;
            bool siegeCombat = HasActiveTargetCitySiege(pCaptain);
            string taskId = siegeCombat
                ? ArmyRtsContent.SiegeCombatTaskId
                : ShouldUseCaptainSiegeAdvance(pCaptain)
                ? ArmyRtsContent.CaptainSiegeAdvanceTaskId
                : ArmyRtsContent.CaptainCombatTaskId;
            if (pCaptain.isTask(taskId)) return;
            SetJob(pCaptain, ArmyRtsContent.CaptainJobId, taskId,
                pForceReassert: true);
            ArmyMilitaryMovementPriorityIndex.Register(pCaptain.data.id,
                ArmyMilitaryMovementPriorityKind.RtsMember);
        }

        public static bool TryGetSiegeAdvanceTarget(Actor pActor,
            out WorldTile pTarget)
        {
            pTarget = null;
            if (!ShouldUseCaptainSiegeAdvance(pActor)) return false;
            Army army = pActor.army;
            if (!Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record)) return false;
            if (!RuntimeByArmy.TryGetValue(army.id,
                    out RuntimeState runtime)) return false;
            City targetCity = FindCity(record?.Mission?.TargetCityId ?? -1L);
            pTarget = ResolveStableStrategicEndpoint(army, targetCity,
                runtime);
            return pTarget?.data != null;
        }

        private static bool ShouldUseCaptainSiegeAdvance(Actor pCaptain)
        {
            Army army = pCaptain?.army;
            if (!HasCaptainMission(pCaptain) || army?.data == null ||
                !RuntimeByArmy.TryGetValue(army.id,
                    out RuntimeState runtime) || !runtime.FieldCombatReleased ||
                runtime.SiegeCombatActive ||
                !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record)) return false;
            City targetCity = FindCity(record?.Mission?.TargetCityId ?? -1L);
            return targetCity?.data != null &&
                   FindCaptainCombatTarget(pCaptain) == null;
        }

        internal static bool TrySetMemberCombatTask(Actor pActor)
        {
            Army army = pActor?.army;
            if (ShouldSuppressCombatPreemption(pActor))
            {
                if (IsCaptain(pActor, army))
                    ClearActorAttackTarget(pActor);
                else
                    SetRetreatFollowerJob(pActor);
                return false;
            }
            bool missionActive = army?.data != null &&
                                 HasActiveMission(army.id);
            bool actorIsCaptain = IsCaptain(pActor, army);
            if (!missionActive || actorIsCaptain) return false;
            RuntimeState runtime = null;
            bool fieldCombatReleased = army?.data != null &&
                RuntimeByArmy.TryGetValue(army.id, out runtime) &&
                runtime.FieldCombatReleased;
            bool siegeCombatActive = runtime?.SiegeCombatActive == true;
            bool useSiegeCombatTask = siegeCombatActive &&
                TryGetActiveTargetCitySiege(pActor, out _,
                    out City activeSiegeCity) &&
                ArmyRtsCaptainCombatRules.ShouldUseSiegeCombatTask(
                    siegeCombatActive,
                    IsInsideCityCombatZone(pActor, activeSiegeCity));
            if (useSiegeCombatTask)
            {
                if (!HasMemberCombatMission(pActor)) return false;
                return SetMemberSiegeCombatTask(pActor);
            }
            if (pActor.isTask("fighting"))
                return HasValidMemberCombatTarget(pActor);
            if (pActor.isTask(ArmyRtsContent.MemberCombatTaskId))
                RestoreVanillaMemberFollow(pActor);
            return false;
        }

        private static bool SetMemberSiegeCombatTask(Actor pActor)
        {
            try
            {
                if (pActor.isTask(ArmyRtsContent.SiegeCombatTaskId))
                    return true;
                pActor.ai.setJob(ArmyRtsContent.MemberCombatJobId);
                pActor.ai.setTask(ArmyRtsContent.SiegeCombatTaskId);
                return pActor.isTask(ArmyRtsContent.SiegeCombatTaskId);
            }
            catch { return false; }
        }

        private static bool HasValidMemberCombatTarget(Actor pActor)
        {
            Actor attackTarget = pActor?.attack_target?.a;
            return IsValidMemberCombatTarget(pActor, attackTarget);
        }

        private static void RestoreVanillaMemberFollow(Actor pActor)
        {
            if (pActor?.data == null || IsCaptain(pActor, pActor.army))
                return;
            SetJob(pActor, ArmyRtsContent.VanillaFollowerJobId,
                "warrior_army_follow_leader", pForceReassert: true);
        }

        internal static bool HasMemberCombatMission(Actor pActor)
        {
            Army army = pActor?.army;
            bool missionActive = army?.data != null &&
                                 Controllers.TryGet(army.id, out _);
            return !IsCaptain(pActor, army) && IsLiveWarriorActor(pActor) &&
                   ShouldOwnMilitaryActor(pActor, missionActive);
        }

        internal static bool HasActiveCaptainObjective(Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null || record.State == ArmyRtsState.Rally)
                return false;
            bool transportOwned = HasMilitaryTransportOwnership(pActor);
            return !transportOwned && IsCaptain(pActor, army) &&
                   ShouldOwnMilitaryActor(pActor, pMissionActive: true);
        }

        // Runs at the actor-post boundary before the P0 snapshot. This only
        // refreshes execution priority; task, target and path ownership stay
        // with the controller and the native movement pipeline.
        internal static bool TryRefreshMilitaryPriority(Actor pActor)
        {
            if (pActor?.data == null || RoyalGuardService.IsRoyalGuard(pActor))
                return false;
            if (WarArmyReturnService.IsActive(pActor.army))
            {
                ArmyMilitaryMovementPriorityIndex.Register(pActor.data.id,
                    ArmyMilitaryMovementPriorityKind.RtsMember);
                return true;
            }
            bool ownsNativeCombatCycle = HasMemberCombatMission(pActor) &&
                HasImmediateCombatPriority(pActor);
            if (HasActiveCaptainObjective(pActor) ||
                HasActiveMemberObjective(pActor) || ownsNativeCombatCycle)
            {
                ArmyMilitaryMovementPriorityIndex.Register(pActor.data.id,
                    ArmyMilitaryMovementPriorityKind.RtsMember);
                return true;
            }
            if (ArmyMilitaryMovementPriorityIndex.TryGetKind(
                    pActor.data.id,
                    out ArmyMilitaryMovementPriorityKind kind) &&
                kind == ArmyMilitaryMovementPriorityKind.RtsMember)
                ArmyMilitaryMovementPriorityIndex.Unregister(pActor.data.id);
            return false;
        }

        public static bool ShouldHoldDeploymentMove(Actor pActor)
        {
            Army army = pActor?.army;
            if (!ArmyRtsRuntimeMode.ShouldCommit || pActor?.data == null ||
                army?.data == null || !IsCaptain(pActor, army) ||
                !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record)) return false;
            bool captainPresent = pActor.isAlive() && !pActor.isRekt() &&
                                  pActor.current_tile?.data != null;
            if (ArmyRtsRules.ShouldForcePreDeparture(
                    authoritative: true, state: ArmyRtsState.Rally,
                    minimumForceReady: ArmyLogisticsRules.
                        HasMinimumOperationalForce(SafeUnitCount(army)),
                    captainPresent: captainPresent,
                    issuedWorldTime: record?.Mission?.IssuedTime ?? 0d,
                    currentWorldTime: CurrentWorldTime())) return false;
            ArmyFormationObservationProgress observation =
                ArmyFormationService.GetObservationProgress(army);
            if (!observation.Complete) return true;
            ArmyFormationCounters counters =
                ArmyFormationService.GetIncrementalFollowerCounters(army);
            bool transportOwnsMovement =
                HasMilitaryTransportOwnership(pActor);
            return !ArmyRtsRules.CanCaptainAdvanceWithEscort(
                requiresEscort: true,
                rosterLiving: ArmyRtsRules.ResolveEscortPopulation(
                    SafeUnitCount(army), counters.Living,
                    observation.Complete, captainPresent),
                nearbyFollowers: counters.Rallied,
                captainPresent: captainPresent,
                immediateCombat: HasImmediateCombatPriority(pActor),
                transportOwnsMovement: transportOwnsMovement);
        }

        internal static bool HasActiveMemberObjective(Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null || record.State == ArmyRtsState.Rally)
                return false;
            bool transportOwned = HasMilitaryTransportOwnership(pActor);
            return ArmyRtsMemberObjectiveRules.ShouldOwnMemberObjective(
                true, IsCaptain(pActor, army), IsLiveWarriorActor(pActor),
                HasImmediateCombatPriority(pActor), transportOwned);
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
            if (pActor?.data == null || army?.data == null) return false;
            bool controllerActive = Controllers.TryGet(army.id,
                out ArmyRtsControllerRecord record);
            bool missionActive = controllerActive ||
                                 ArmyDeploymentService.
                                     HasActiveAssignment(pActor);
            if (!ShouldOwnMilitaryActor(pActor, missionActive)) return false;
            bool transportOwned = HasMilitaryTransportOwnership(pActor);
            if (!controllerActive)
            {
                return !IsCaptain(pActor, army) &&
                       ArmyFormationService.HasFollower(pActor) &&
                       ArmyFormationRules.ShouldOwnEscortFollow(
                           ArmyRtsState.Rally,
                           HasImmediateCombatPriority(pActor),
                           transportOwned);
            }
            return ArmyRtsMemberObjectiveRules.ShouldOwnMemberObjective(
                missionActive, IsCaptain(pActor, army), actorEligible: true,
                HasImmediateCombatPriority(pActor), transportOwned);
        }

        public static bool OwnsLiveActor(Actor pActor)
        {
            Army army = pActor?.army;
            bool missionActive = army?.data != null &&
                                 Controllers.TryGet(army.id, out _);
            return ShouldOwnMilitaryActor(pActor, missionActive);
        }

        private static bool UsesVanillaArmyMovement(Army pArmy,
            ArmyRtsMission pMission)
        {
            City target = FindCity(pMission?.TargetCityId ?? -1L);
            Kingdom kingdom = SafeKingdom(pArmy);
            bool targetIsEnemy = false;
            try
            {
                targetIsEnemy = target?.kingdom?.data != null &&
                    kingdom?.data != null && target.kingdom.isEnemy(kingdom);
            }
            catch { }
            return ArmyRtsControllerRules.ShouldUseVanillaArmyMovement(
                pMission?.ProposalKind ?? ArmyRtsProposalKind.None,
                targetIsEnemy);
        }

        private static bool UsesVanillaRetreatMovement(Army pArmy,
            ArmyRtsMission pMission)
        {
            return ArmyRtsControllerRules.ShouldUseVanillaRetreatMovement(
                pMission?.ProposalKind ?? ArmyRtsProposalKind.None,
                IsMissionTargetEnemy(pArmy, pMission));
        }

        private static bool UsesNativeMissionExecution(Army pArmy,
            ArmyRtsControllerRecord pRecord)
        {
            return pArmy?.data != null && pRecord?.Mission != null &&
                   ArmyRtsControllerRules.ShouldUseNativeMissionExecution(
                       pRecord.Mission.ProposalKind, pRecord.State,
                       IsMissionTargetEnemy(pArmy, pRecord.Mission));
        }

        internal static bool UsesVanillaFollowerMovement(Actor pActor)
        {
            Army army = pActor?.army;
            if (army?.data == null ||
                !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record)) return false;
            return ArmyRtsControllerRules.ShouldUseVanillaFollowerMovement(
                record?.Mission?.ProposalKind ?? ArmyRtsProposalKind.None,
                IsMissionTargetEnemy(army, record?.Mission));
        }

        internal static bool HasActiveMilitaryP0Owner(Actor pActor)
        {
            Army army = pActor?.army;
            return pActor?.data != null && army?.data != null &&
                   HasActiveMission(army.id);
        }

        internal static bool ShouldSuppressCombatPreemption(Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null ||
                !RuntimeByArmy.TryGetValue(army.id,
                    out RuntimeState runtime) ||
                runtime.RetreatSelectionPending || runtime.NoSafeRetreat)
                return false;
            City target = FindCity(record.Mission.TargetCityId);
            Kingdom kingdom = SafeKingdom(army);
            bool targetAvailable = target?.data != null &&
                                   kingdom?.data != null &&
                                   target.kingdom == kingdom;
            return ArmyRtsTaskOwnershipRules.ShouldSuppressCombatPreemption(
                record.State, record.Mission.ProposalKind, targetAvailable);
        }

        internal static void SuppressCombatForTransit(Actor pActor)
        {
            if (!ShouldSuppressCombatPreemption(pActor)) return;
            Army army = pActor.army;
            if (IsCaptain(pActor, army))
            {
                ClearActorAttackTarget(pActor);
                SetJob(pActor, ArmyRtsContent.RetreatCaptainJobId,
                    ArmyRtsContent.RetreatTaskId);
            }
            else
                SetRetreatFollowerJob(pActor);
        }

        internal static bool ShouldUseNativeMilitaryPath(Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (ArmyRtsTransportService.OwnsActorTask(pActor)) return false;
            if (RoyalGuardService.IsRoyalGuard(pActor)) return true;
            if (WarArmyReturnService.IsActive(pActor.army)) return true;
            Army army = pActor.army;
            if (army?.data == null ||
                !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return false;
            return UsesNativeMissionExecution(army, record) ||
                   UsesVanillaArmyMovement(army, record.Mission) ||
                   UsesVanillaRetreatMovement(army, record.Mission) ||
                   ArmyRtsControllerRules.ShouldUseVanillaFollowerMovement(
                       record.Mission.ProposalKind,
                       IsMissionTargetEnemy(army, record.Mission));
        }

        internal static bool ShouldBlockLiquidMilitaryMovement(
            Actor pActor, WorldTile pTarget)
        {
            if (pActor?.data == null || pTarget?.Type == null ||
                !pTarget.Type.liquid || pActor.is_inside_boat ||
                pActor.asset?.is_boat == true || pActor.isWaterCreature())
                return false;
            if (ShouldUseNativeMilitaryPath(pActor)) return false;
            return HasActiveMilitaryP0Owner(pActor) ||
                   RoyalGuardService.IsRoyalGuard(pActor);
        }

        internal static bool HasMilitaryTransportOwnership(Actor actor)
        {
            bool insideBoat = actor?.is_inside_boat == true;
            bool vanillaTaxi = ArmyMilitaryMovementPriorityIndex.HasVanillaTaxiOwnership(
                actor?.data?.id ?? -1L);
            // An active voyage must preserve force_into_a_boat until the
            // native taxi task actually embarks the actor. The voyage state
            // itself proves that this is not stale task ownership.
            bool customTransport =
                ArmyRtsTransportService.OwnsActorTask(actor);
            return ArmyMilitaryMovementPriorityRules.ShouldYieldToTransport(
                insideBoat, customTransport, vanillaTaxi);
        }

        internal static bool RefreshMilitaryTransportOwnership(Actor actor)
        {
            if (HasMilitaryTransportOwnership(actor)) return true;
            bool vanillaTaxiOwned = false;
            try
            {
                vanillaTaxiOwned =
                    TaxiManager.getRequestForActor(actor) != null;
            }
            catch { }
            if (vanillaTaxiOwned)
                ArmyMilitaryMovementPriorityIndex.MarkVanillaTaxiOwnership(
                    actor?.data?.id ?? -1L);
            return vanillaTaxiOwned;
        }

        internal static bool TryPrepareMilitaryP0Actor(Actor pActor)
        {
            ArmyRtsMovementDiagnostic.Log("rts", "prepare_enter", pActor);
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                pActor.current_tile?.data == null ||
                !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null)
            {
                ArmyRtsMovementDiagnostic.Log("rts", "prepare_rejected",
                    pActor, "reason=invalid_actor_or_tile");
                return false;
            }
            bool captain = IsCaptain(pActor, army);
            if (captain && RuntimeByArmy.TryGetValue(army.id,
                    out RuntimeState fieldCombatRuntime) &&
                fieldCombatRuntime.FieldCombatReleased)
            {
                Actor combatTarget = pActor.beh_actor_target?.a;
                if (!IsValidCaptainCombatTarget(pActor, combatTarget))
                    combatTarget = FindCaptainCombatTarget(pActor);
                if (combatTarget == null)
                {
                    CountFieldCombatEngagement(army, out int engaged,
                        out _, out _);
                    if (ArmyRtsFieldCombatRules.ShouldAbortFieldCombatFromP0(
                            fieldCombatRuntime.FieldCombatReleased,
                            pCaptainHasCombatTarget: false, engaged > 0))
                    {
                        ExitFieldCombat(army, fieldCombatRuntime);
                        if (Controllers.TryGet(army.id,
                                out ArmyRtsControllerRecord fieldCombatRecord) &&
                            fieldCombatRecord?.Mission != null)
                        {
                            EnsureJobs(army, fieldCombatRuntime,
                                fieldCombatRecord.Mission,
                                fieldCombatRecord.State);
                        }
                        ArmyRtsMovementDiagnostic.Log("rts",
                            "field_combat_cleared_p0", pActor,
                            "reason=no_captain_target");
                    }
                }
            }
            if (!captain || !UsesNativeMissionExecution(army, record))
                return true;

            City targetCity = FindCity(record.Mission.TargetCityId);
            if (UsesVanillaArmyMovement(army, record.Mission) &&
                RuntimeByArmy.TryGetValue(army.id,
                    out RuntimeState runtime))
            {
                WorldTile strategicTarget = ResolveStableStrategicEndpoint(
                    army, targetCity, runtime);
                bool transportOwned =
                    HasMilitaryTransportOwnership(pActor);
                bool sameIsland = SafeSameIsland(pActor.current_tile,
                    strategicTarget);
                ArmyRtsMovementDiagnostic.Log("rts", "transport_check",
                    pActor, "same_island=" + sameIsland +
                            " target_tile=" +
                            (strategicTarget?.data?.tile_id ?? -1) +
                            " transport_owned=" + transportOwned);
                if (ArmyRtsControllerRules.
                        ShouldPrimeForcedTransportBeforeNativeAttack(
                            isCaptain: true, usesVanillaAttack: true,
                            sameIsland: sameIsland,
                            transportOwned: transportOwned))
                {
                    bool started = strategicTarget?.data != null &&
                        ArmyRtsTransportService.TryHandleActor(
                            pActor, strategicTarget, pMayBegin: true,
                            pForceTransport: true);
                    ArmyRtsMovementDiagnostic.Log("rts",
                        "transport_result", pActor,
                        "started=" + started + " target_tile=" +
                        (strategicTarget?.data?.tile_id ?? -1));
                }
            }

            WorldTile currentTarget = null;
            try { currentTarget = targetCity?.getTile(); }
            catch { }
            if (!SafeSameIsland(pActor.current_tile, currentTarget))
            {
                ArmyRtsMovementDiagnostic.Log("rts",
                    "transport_wait", pActor,
                    "reason=cross_island_land_attack_blocked target_tile=" +
                    (currentTarget?.data?.tile_id ?? -1));
                return true;
            }

            if (!UsesVanillaArmyMovement(army, record.Mission)) return true;

            // The original captain verifier reads pActor.city, but an army
            // captain can temporarily lose that residence reference after
            // leaving the source city. The army anchor is the authoritative
            // command owner, so a successful source publication must not be
            // invalidated by a best-effort actor-city publication.
            bool armyOrderPublished =
                TryIssueVanillaCityAttackOrder(army, record.Mission,
                    targetCity);
            bool actorOrderPublished = false;
            try
            {
                if (pActor.city?.data != null &&
                    pActor.city != AWArmyService.FindAnchorCity(army))
                    actorOrderPublished =
                        TryIssueVanillaCityAttackOrder(pActor.city,
                            record.Mission, targetCity);
            }
            catch { }
            if (!armyOrderPublished && !actorOrderPublished)
                ArmyRtsMovementDiagnostic.Log("rts",
                    "attack_order_unavailable", pActor,
                    "actor_city=" + (pActor.city?.id ?? -1L) +
                    " anchor_city=" +
                    (AWArmyService.FindAnchorCity(army)?.id ?? -1L));
            // The captain remains RTS-owned even when the best-effort vanilla
            // attack-zone publication cannot be made during a city transition.
            return true;
        }

        private static bool IsMissionTargetEnemy(Army pArmy,
            ArmyRtsMission pMission)
        {
            City target = FindCity(pMission?.TargetCityId ?? -1L);
            Kingdom kingdom = SafeKingdom(pArmy);
            try
            {
                return target?.kingdom?.data != null &&
                       kingdom?.data != null &&
                       target.kingdom.isEnemy(kingdom);
            }
            catch { return false; }
        }

        public static bool TryGetRetreatTarget(Actor pActor,
            out WorldTile pTarget)
        {
            pTarget = null;
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                !IsCaptain(pActor, army) ||
                !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record) ||
                !RuntimeByArmy.TryGetValue(army.id,
                    out RuntimeState runtime) ||
                runtime.RetreatSelectionPending || runtime.NoSafeRetreat ||
                !UsesVanillaRetreatMovement(army, record?.Mission))
                return false;
            City targetCity = FindCity(record.Mission.TargetCityId);
            Kingdom kingdom = SafeKingdom(army);
            if (targetCity?.data == null || kingdom?.data == null ||
                targetCity.kingdom != kingdom) return false;
            try { pTarget = targetCity.getTile(); }
            catch { pTarget = null; }
            return pTarget?.data != null;
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
            if (pArmy?.data == null) return;
            WarArmyReturnService.OnArmyRosterChanged(pArmy);
            if (!Controllers.TryGet(pArmy.id, out _) ||
                !RuntimeByArmy.TryGetValue(pArmy.id,
                    out RuntimeState runtime)) return;
            runtime.RosterVersion++;
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
            runtime.NativeRoute.Invalidate();
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
            if (RuntimeByArmy.TryGetValue(pArmy.id,
                    out RuntimeState runtime))
            {
                runtime.RetreatSelectionPending = false;
                runtime.NoSafeRetreat = false;
            }
            return true;
        }

        public static void PrepareForRetreatSelection(Army pArmy)
        {
            if (pArmy?.data == null) return;
            if (!RuntimeByArmy.TryGetValue(pArmy.id,
                    out RuntimeState runtime))
            {
                runtime = new RuntimeState
                {
                    InitialRosterCount = SafeUnitCount(pArmy)
                };
                RuntimeByArmy[pArmy.id] = runtime;
            }
            runtime.RetreatSelectionPending = true;
            runtime.NoSafeRetreat = false;
            ArmyRouteProviderService.Cancel(pArmy.id,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmy.id);
            ArmyRtsTransportService.ReleaseArmy(pArmy);
            ClearIndependentMemberPaths(pArmy, runtime);
            ClearArmyAttackTargets(pArmy);
            ResetStrategicMovementRuntime(runtime);
            runtime.RouteImpossible = false;
            runtime.LastStrategicEndpointTileId = -1;
            runtime.JobCursor.Reopen();
            Controllers.Requeue(pArmy.id);
        }

        public static void RecoverUnavailableRetreat(Army pArmy)
        {
            if (pArmy?.data == null) return;
            Kingdom kingdom = SafeKingdom(pArmy);
            if (!Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null)
            {
                Invalidate(pArmy.id);
                KingdomWarDirectorService.OnArmyChanged(kingdom);
                return;
            }
            if (!RuntimeByArmy.TryGetValue(pArmy.id,
                    out RuntimeState runtime))
            {
                runtime = new RuntimeState
                {
                    InitialRosterCount = SafeUnitCount(pArmy)
                };
                RuntimeByArmy[pArmy.id] = runtime;
            }
            runtime.NoSafeRetreat = true;
            runtime.RetreatSelectionPending = false;
            ArmyRouteProviderService.Cancel(pArmy.id,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmy.id);
            ClearIndependentMemberPaths(pArmy, runtime);
            ClearArmyAttackTargets(pArmy);
            ResetStrategicMovementRuntime(runtime);
            runtime.RouteImpossible = false;
            runtime.LastStrategicEndpointTileId = -1;
            runtime.JobCursor.Reopen();
            Controllers.SetState(pArmy.id,
                record.Mission.Role == ArmyRtsRole.Assault ||
                record.Mission.ProposalKind == ArmyRtsProposalKind.Attack
                    ? ArmyRtsState.Assault
                    : ArmyRtsState.Hold);
            Controllers.Requeue(pArmy.id);
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
                LogMissionChanged(army, record.Mission, null,
                    "target_control_event");
                ArmyConquestReserveService.GrantForConqueredCity(army,
                    pTargetCity, record.Mission.WarId);
                CoalitionWarTaskService.ReleaseObjectiveClaim(
                    record.Mission.WarId, armyId,
                    record.Mission.TargetCityId);
                Invalidate(armyId);
                if (!KingdomWarDirectorService.
                        TryContinueSameArmyAfterCapture(army,
                            record.Mission, pTargetCity))
                    KingdomWarDirectorService.OnArmyChanged(kingdom);
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
            bool sampledCombatActive =
                !ShouldSuppressCombatPreemption(sampledActor) &&
                HasImmediateCombatPriority(sampledActor);
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
                !Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord record) ||
                !RuntimeByArmy.ContainsKey(pArmyId)) return 0;
            Army army = FindArmy(pArmyId);
            if (ArmyRtsControllerRules.ShouldUseVanillaFollowerMovement(
                    record?.Mission?.ProposalKind ??
                        ArmyRtsProposalKind.None,
                    IsMissionTargetEnemy(army, record?.Mission))) return 0;
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
                    TransportActive = followerTransport,
                    LocalPathFollowing =
                        follower.isFollowingLocalPath(),
                    LocalPathIndex = follower.current_path_index,
                    LocalTargetTileId =
                        follower.tile_target?.data?.tile_id ?? -1
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

        public static bool HasActiveMissionForKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            IReadOnlyList<long> armyIds = MissionIndex.SnapshotKingdom(
                pKingdom.id);
            for (int i = 0; i < armyIds.Count; i++)
                if (HasActiveMission(armyIds[i])) return true;
            return false;
        }

        internal static bool HasExpectedCaptainTask(Army pArmy)
        {
            if (pArmy?.data == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return false;
            Actor captain = SafeCaptain(pArmy);
            if (!IsLiveActor(captain)) return false;
            string expected = RuntimeByArmy.TryGetValue(pArmy.id,
                    out RuntimeState runtime) && runtime.SiegeCombatActive
                ? ArmyRtsContent.SiegeCombatTaskId
                : ArmyRtsControllerRules.ShouldUseFrontHoldJob(
                    record.Mission.ProposalKind, record.State)
                ? ArmyRtsContent.HoldTaskId
                : ArmyRtsContent.ResolveCaptainTaskId(record.State,
                    ArmyRtsTransportService.GetPhase(pArmy));
            try { return captain.isTask(expected); }
            catch { return false; }
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
            InvalidateNativeRoute(runtime,
                SafeCaptain(army), "route_replan");
            ArmyRouteProviderService.Cancel(pArmyId,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmyId);
            ClearIndependentMemberPaths(army, runtime);
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
            runtime.JobCursor.Reopen();
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
            if (RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState handoffRuntime))
                InvalidateNativeRoute(handoffRuntime,
                    SafeCaptain(army), "objective_handoff");
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
            if (!RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState runtime)) return false;
            WorldTile targetTile = ResolveStableStrategicEndpoint(army,
                target, runtime);
            if (!IsLiveArmy(army) || captain?.current_tile?.data == null ||
                targetTile?.data == null ||
                SafeSameIsland(captain.current_tile, targetTile) ||
                !ArmyRtsTransportService.TryGetRouteEstimate(army,
                    targetTile, out _))
                return false;
            InvalidateNativeRoute(runtime, captain, "transport_started");
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
            if (!RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState runtime)) return false;
            WorldTile targetTile = ResolveStableStrategicEndpoint(army,
                target, runtime);
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
            if (!AWArmyRoleRules.ShouldRtsOwnCaptain(
                    AWArmyService.GetRole(pActor.army),
                    RoyalGuardService.IsRoyalGuard(pActor),
                    IsCivilAuthorityActor(pActor)))
            {
                RoyalGuardService.EnsureProtectKingTask(pActor);
                return;
            }
            try
            {
                string jobId = pActor.ai.job?.id ?? "";
                bool ownsRtsTask = jobId == ArmyRtsContent.CaptainJobId ||
                    jobId == ArmyRtsContent.HoldJobId ||
                    jobId == ArmyRtsContent.FollowerJobId ||
                    jobId == ArmyRtsContent.VanillaFollowerJobId ||
                    jobId == ArmyRtsContent.RetreatCaptainJobId ||
                    jobId == ArmyRtsContent.RetreatFollowerJobId ||
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
                pActor.clearOldPath();
                pActor.clearTileTarget();
                pActor.beh_tile_target = null;
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
            if (Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord record) &&
                record?.Mission != null)
            {
                if (UsesNativeMissionExecution(army, record))
                {
                    if (UsesVanillaArmyMovement(army, record.Mission))
                        TryIssueVanillaCityAttackOrder(army, record.Mission,
                            FindCity(record.Mission.TargetCityId));
                    if (UsesVanillaRetreatMovement(army, record.Mission))
                        SetJob(captain, ArmyRtsContent.RetreatCaptainJobId,
                            ArmyRtsContent.RetreatTaskId,
                            pForceReassert: true);
                    else
                    {
                        bool combat = RuntimeByArmy.TryGetValue(pArmyId,
                            out RuntimeState combatRuntime) &&
                            combatRuntime.FieldCombatReleased;
                        bool siegeCombat = combatRuntime?.SiegeCombatActive ==
                                           true;
                        SetJob(captain, ArmyRtsContent.CaptainJobId,
                            siegeCombat
                                ? ArmyRtsContent.SiegeCombatTaskId
                                : combat
                                ? ArmyRtsContent.CaptainCombatTaskId
                                : ArmyRtsContent.ResolveCaptainTaskId(
                                    record.State,
                                    ArmyRtsTransportService.GetPhase(army)),
                            pForceReassert: true);
                    }
                }
                else if (UsesVanillaRetreatMovement(army,
                             record.Mission))
                {
                    SetJob(captain, ArmyRtsContent.RetreatCaptainJobId,
                        ArmyRtsContent.RetreatTaskId,
                        pForceReassert: true);
                }
                else
                {
                    bool frontHold = ArmyRtsControllerRules.
                        ShouldUseFrontHoldJob(
                            record.Mission.ProposalKind, record.State);
                    SetJob(captain, frontHold
                        ? ArmyRtsContent.HoldJobId
                        : ArmyRtsContent.CaptainJobId,
                        frontHold ? ArmyRtsContent.HoldTaskId
                            : ArmyRtsContent.ResolveCaptainTaskId(
                                record.State,
                                ArmyRtsTransportService.GetPhase(army)),
                        pForceReassert: true);
                }
                if (IsLiveCombatantActor(captain) &&
                    record.State != ArmyRtsState.Rally &&
                    ShouldOwnMilitaryActor(captain, pMissionActive: true))
                    ArmyMilitaryMovementPriorityIndex.Register(
                        captain.data.id,
                        ArmyMilitaryMovementPriorityKind.RtsMember);
            }
            if (RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState runtime))
            {
                runtime.JobCursor.Reopen();
            }
            Controllers.Requeue(pArmyId);
        }

        public static void ReassertMissionCommand(long pArmyId,
            long pSampledActorId)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                !HasActiveMission(pArmyId)) return;
            Army army = FindArmy(pArmyId);
            if (Controllers.TryGet(pArmyId,
                    out ArmyRtsControllerRecord record) &&
                UsesNativeMissionExecution(army, record))
            {
                Actor attackActor = FindActor(pSampledActorId);
                if (IsCaptain(attackActor, army))
                    ReassertCaptainCommand(pArmyId);
                else
                {
                    SetNativeMemberMissionTask(attackActor, army,
                        pForceReassert: true);
                    if (IsLiveWarriorActor(attackActor) &&
                        !HasImmediateCombatPriority(attackActor))
                        ArmyMilitaryMovementPriorityIndex.Register(
                            attackActor.data.id,
                            ArmyMilitaryMovementPriorityKind.RtsMember);
                }
                Controllers.Requeue(pArmyId);
                return;
            }
            if (record != null && UsesVanillaRetreatMovement(army,
                    record.Mission))
            {
                Actor retreatActor = FindActor(pSampledActorId);
                if (IsCaptain(retreatActor, army))
                    ReassertCaptainCommand(pArmyId);
                else
                    SetRetreatFollowerJob(retreatActor);
                if (IsLiveWarriorActor(retreatActor))
                    ArmyMilitaryMovementPriorityIndex.Register(
                        retreatActor.data.id,
                        ArmyMilitaryMovementPriorityKind.RtsMember);
                Controllers.Requeue(pArmyId);
                return;
            }
            if (record != null && UsesVanillaFollowerMovement(
                    FindActor(pSampledActorId)))
            {
                Actor vanillaFollower = FindActor(pSampledActorId);
                if (IsCaptain(vanillaFollower, army))
                    ReassertCaptainCommand(pArmyId);
                else
                    SetNativeMemberMissionTask(vanillaFollower, army,
                        pForceReassert: true);
                if (vanillaFollower?.data != null &&
                    !HasImmediateCombatPriority(vanillaFollower))
                    ArmyMilitaryMovementPriorityIndex.Register(
                        vanillaFollower.data.id,
                        ArmyMilitaryMovementPriorityKind.RtsMember);
                Controllers.Requeue(pArmyId);
                return;
            }
            Actor sampledActor = FindActor(pSampledActorId);
            if (sampledActor?.army == army &&
                !IsCaptain(sampledActor, army) &&
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
            RecoverFormationMember(pArmyId, pActorId,
                pPreferAlternateSlot: false);
        }

        public static bool TryRecoverMissingCaptainTarget(Actor pActor)
        {
            Army army = pActor?.army;
            if (army?.data == null || !IsCaptain(pActor, army) ||
                !Controllers.TryGet(army.id,
                    out ArmyRtsControllerRecord record) ||
                !RuntimeByArmy.TryGetValue(army.id,
                    out RuntimeState runtime)) return false;
            bool captainPresent = pActor.data != null && pActor.isAlive() &&
                                  !pActor.isRekt() &&
                                  pActor.current_tile?.data != null;
            if (!ArmyRtsTaskOwnershipRules.ShouldRecoverMissingCaptainTarget(
                    record.State, missionActive: true,
                    targetAvailable: false,
                    transportOwned: ArmyRtsTransportService.HasActiveVoyage(
                        army) || pActor.is_inside_boat,
                    captainPresent: captainPresent)) return false;
            double now = CurrentWorldTime();
            if (double.IsNaN(now) || double.IsInfinity(now)) now = 0d;
            if (now < runtime.NextMissingTargetRecoveryWorldTime) return false;
            runtime.NextMissingTargetRecoveryWorldTime = now + 1d;
            return RequestRouteReplan(army.id, pAlternateEndpoint: false);
        }

        public static void RecoverFormationMember(long pArmyId,
            long pActorId, bool pPreferAlternateSlot)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                !HasActiveMission(pArmyId)) return;
            Army army = FindArmy(pArmyId);
            Actor actor = FindActor(pActorId);
            if (actor?.army != army || IsCaptain(actor, army) ||
                !HasActiveMemberObjective(actor))
                return;
            if (UsesVanillaFollowerMovement(actor))
            {
                SetNativeMemberMissionTask(actor, army,
                    pForceReassert: true);
                return;
            }
            if (!RuntimeByArmy.TryGetValue(pArmyId, out RuntimeState runtime))
                return;
            bool transportActive = ArmyRtsTransportService.
                HasActiveVoyage(army) ||
                HasMilitaryTransportOwnership(actor);
            if (!ArmyRtsMemberObjectiveRules.ShouldRecoverToMissionObjective(
                    hasActiveMission: true, actorEligible: true,
                    combatActive: HasImmediateCombatPriority(actor),
                    transportActive: transportActive)) return;
            ClearIndependentMemberPath(actor, runtime);
            SetJob(actor, ArmyRtsContent.FollowerJobId,
                pForceReassert: true);
            if (pPreferAlternateSlot)
                RequestRouteReplan(pArmyId, pAlternateEndpoint: true);
            else
                TrySubmitMemberObjectiveRoute(actor, runtime);
            Controllers.Requeue(pArmyId);
        }

        internal static bool RecoverEmptySharedRoute(long pArmyId,
            long pActorId)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit ||
                !HasActiveMission(pArmyId)) return false;
            Army army = FindArmy(pArmyId);
            Actor actor = FindActor(pActorId);
            if (actor?.army != army ||
                HasImmediateCombatPriority(actor) ||
                ArmyRtsTransportService.HasActiveVoyage(army)) return false;
            try
            {
                if (actor.data.transportID >= 0L) return false;
            }
            catch { return false; }
            if (IsCaptain(actor, army))
            {
                if (ShouldUseNativeMilitaryPath(actor))
                {
                    ReassertMissionCommand(pArmyId, pActorId);
                    return true;
                }
                return RequestRouteReplan(pArmyId,
                    pAlternateEndpoint: false);
            }
            if (ShouldUseNativeMilitaryPath(actor))
            {
                ReassertMissionCommand(pArmyId, pActorId);
                return true;
            }
            if (!HasActiveMemberObjective(actor) ||
                !RuntimeByArmy.TryGetValue(pArmyId,
                    out RuntimeState runtime)) return false;
            ClearIndependentMemberPath(actor, runtime);
            ReassertMissionCommand(pArmyId, pActorId);
            TrySubmitMemberObjectiveRoute(actor, runtime);
            return true;
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
            if (missionActive && UsesVanillaFollowerMovement(actor))
                return false;
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

        public static void ReleaseAfterReturn(Army pArmy)
        {
            if (pArmy?.data == null) return;
            Invalidate(pArmy.id, pReleaseActorJobs: false);
            ReleaseAfterReturnActors(pArmy);
        }

        private static void ReleaseAfterReturnActors(Army pArmy)
        {
            HashSet<long> releasedActorIds = new HashSet<long>();
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                ReleaseAfterReturnActorOnce(actor, releasedActorIds);
            }
            Actor captain = SafeCaptain(pArmy);
            ReleaseAfterReturnActorOnce(captain, releasedActorIds);
        }

        private static void ReleaseAfterReturnActorOnce(Actor pActor,
            HashSet<long> releasedActorIds)
        {
            bool actorValid = pActor?.data != null;
            long actorId = actorValid ? pActor.data.id : -1L;
            bool alreadyReleased = actorValid &&
                                   releasedActorIds.Contains(actorId);
            if (!StandingArmyRules.ShouldReleasePostReturnActor(
                    actorValid, alreadyReleased)) return;
            releasedActorIds.Add(actorId);
            ReleaseAfterReturnActor(pActor);
        }

        private static void ReleaseAfterReturnActor(Actor pActor)
        {
            if (pActor?.data == null || pActor.ai == null) return;
            ArmyMilitaryMovementPriorityIndex.Unregister(pActor.data.id);
            try
            {
                if (AWPathMovementBridge.HasOwnership(pActor))
                    AWPathMovementBridge.Cancel(pActor,
                        AWPathFailureReason.CancelledByNewRequest);
            }
            catch { }
            try { pActor.cancelAllBeh(); }
            catch { }
            try { pActor.stopMovement(); }
            catch { }
            try { pActor.clearOldPath(); }
            catch { }
            try { pActor.clearTileTarget(); }
            catch { }
            try { pActor.clearAttackTarget(); }
            catch { }
            try { pActor.beh_tile_target = null; }
            catch { }
            try { pActor.beh_actor_target = null; }
            catch { }
            try
            {
                if (SyntheticLevyService.IsSynthetic(pActor))
                {
                    SyntheticLevyService.ConfirmReturnArrival(pActor);
                    pActor.ai.clearJob();
                }
                else
                {
                    pActor.ai.clearJob();
                    StandingArmyPeacetimeService.RefreshAfterReturn(pActor);
                }
            }
            catch
            {
                try { pActor.ai.clearJob(); }
                catch { }
            }
        }

        public static void Invalidate(long pArmyId)
        {
            Invalidate(pArmyId, pReleaseActorJobs: true);
        }

        private static void Invalidate(long pArmyId,
            bool pReleaseActorJobs)
        {
            CoalitionWarTaskService.OnArmyInvalidated(pArmyId);
            Army army = FindArmy(pArmyId);
            ArmyRtsTransportService.ReleaseArmy(army);
            ArmyRtsMobilizationStatusService.Clear(army);
            if (pReleaseActorJobs) ReleaseArmyActors(army);
            GarrisonSortieService.OnMissionCompleted(army);
            ArmyMissionPersistence.Invalidate(army);
            Controllers.Invalidate(pArmyId);
            MissionIndex.Remove(pArmyId);
            RuntimeByArmy.Remove(pArmyId);
            if (pReleaseActorJobs)
                RefreshReleasedArmyPeacetimeJobs(army);
            ArmyLogisticsService.OnMissionInvalidated(pArmyId);
            ArmyStallWatchdogService.OnArmyInvalidated(pArmyId);
            ArmyFormationService.RemoveArmy(pArmyId);
            AWArmyMarchService.ClearArmy(pArmyId);
            RemovePendingReplenishmentArrivals(pArmyId);
        }

        private static bool InvalidateAndTryBeginReturn(long pArmyId,
            Army pArmy, bool pShouldBeginReturn)
        {
            if (!pShouldBeginReturn)
            {
                Invalidate(pArmyId);
                return false;
            }
            Invalidate(pArmyId, pReleaseActorJobs: false);
            bool returnQueued = WarArmyReturnService.TryBegin(pArmy) &&
                                WarArmyReturnService.IsActive(pArmy);
            if (!returnQueued)
            {
                ReleaseArmyActors(pArmy);
                RefreshReleasedArmyPeacetimeJobs(pArmy);
            }
            return returnQueued;
        }

        public static int InvalidateWarParticipant(long pWarId,
            long pKingdomId)
        {
            if (pWarId < 0L || pKingdomId < 0L) return 0;
            IReadOnlyList<long> armyIds = MissionIndex.SnapshotWar(pWarId);
            int invalidated = 0;
            for (int i = 0; i < armyIds.Count; i++)
            {
                long armyId = armyIds[i];
                if (!Controllers.TryGet(armyId,
                        out ArmyRtsControllerRecord record) ||
                    record?.Mission == null ||
                    !WarArmyReturnRules.MatchesDepartedParticipant(
                        record.Mission.WarId, record.Mission.KingdomId,
                        pWarId, pKingdomId)) continue;
                Army army = FindArmy(armyId);
                bool shouldBeginReturn = IsLiveArmy(army);
                bool returnQueued = InvalidateAndTryBeginReturn(armyId, army,
                    shouldBeginReturn);
                ModClass.LogInfo("[AW3 RTS return] trigger=participant_left" +
                                 " war=" + pWarId +
                                 " kingdom=" + pKingdomId +
                                 " army=" + armyId +
                                 " queued=" + returnQueued);
                invalidated++;
            }
            return invalidated;
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
                    Army liveArmy = FindArmy(armyId);
                    InvalidateAndTryBeginReturn(armyId, liveArmy, IsLiveArmy(liveArmy));
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
                InvalidateAndTryBeginReturn(armyId, army,
                    shouldBeginReturn);
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

        public static bool RehydrateAfterAuthorityChange(Army pArmy)
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit || pArmy?.data == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return false;
            ArmyRtsMission mission = ArmyRtsControllerRules.CopyMission(
                record.Mission);
            Controllers.SetState(pArmy.id, ArmyRtsState.Rally);
            RuntimeByArmy[pArmy.id] = new RuntimeState
            {
                InitialRosterCount = SafeUnitCount(pArmy)
            };
            MissionIndex.Upsert(mission);
            ArmyRouteProviderService.Cancel(pArmy.id,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmy.id);
            ArmyFormationService.RemoveArmy(pArmy.id);
            ArmyRtsWarLifecycleService.OnMissionAssigned(pArmy, mission);
            bool corridor = ResolveInitialMissionCorridor(pArmy, mission);
            ArmyLogisticsService.OnMissionAssigned(pArmy, mission,
                pConnectedSupply: corridor, pInCorridor: corridor);
            ArmyStallWatchdogService.OnMissionAssigned(pArmy,
                pResetState: true);
            ReassertCaptainMissionTask(SafeCaptain(pArmy));
            Controllers.Requeue(pArmy.id);
            return true;
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
                ArmyRtsMission invalidMission = record?.Mission;
                War missionWar = FindWar(invalidMission?.WarId ?? -1L);
                Kingdom missionKingdom = FindKingdom(
                    invalidMission?.KingdomId ?? -1L);
                Kingdom armyKingdom = liveArmy ? SafeKingdom(army) : null;
                bool missionWarActive = IsWarActive(missionWar);
                bool missionKingdomParticipating = IsKingdomInWar(
                    missionWar, missionKingdom);
                bool shouldBeginReturn = WarArmyReturnRules.
                    ShouldReturnInvalidMission(
                        armyAlive: liveArmy &&
                            armyKingdom?.data != null &&
                            armyKingdom.id == invalidMission?.KingdomId,
                        missionExists: invalidMission != null,
                        missionWarActive: missionWarActive,
                        missionKingdomParticipating:
                            missionKingdomParticipating);
                Kingdom kingdom = liveArmy ? SafeKingdom(army) : null;
                if (liveArmy)
                    GarrisonSortieService.OnMissionCompleted(army);
                bool returnQueued = InvalidateAndTryBeginReturn(pArmyId, army,
                    shouldBeginReturn);
                if (shouldBeginReturn)
                {
                    ModClass.LogInfo(
                        "[AW3 RTS return] trigger=invalid_mission" +
                        " war=" + (invalidMission?.WarId ?? -1L) +
                        " kingdom=" +
                        (invalidMission?.KingdomId ?? -1L) +
                        " army=" + pArmyId +
                        " war_active=" + missionWarActive +
                        " participant=" +
                        missionKingdomParticipating +
                        " queued=" + returnQueued);
                }
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
            }
            if (commit && TryHandleWarCombatOwnership(army, record,
                    runtime))
            {
                Controllers.Requeue(pArmyId);
                return;
            }
            if (commit && TryHandleFieldCombat(army, runtime))
            {
                Controllers.Requeue(pArmyId);
                return;
            }
            if (commit)
            {
                long jobsDiagnostic = RuntimePerformanceDiagnostic.
                    BeginArmyRtsControllerStage(
                        ArmyRtsControllerPerformanceStage.JobOwnership);
                try
                {
                    TryReopenJobOwnershipRepair(runtime);
                    EnsureJobs(army, runtime, record.Mission, record.State);
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
            if (runtime.RetreatSelectionPending || runtime.NoSafeRetreat)
            {
                next = record.Mission.Role == ArmyRtsRole.Assault ||
                       record.Mission.ProposalKind == ArmyRtsProposalKind.Attack
                    ? ArmyRtsState.Assault
                    : ArmyRtsState.Hold;
                Controllers.SetState(pArmyId, next);
            }
            if (next == ArmyRtsState.Retreat &&
                !ArmyRtsWarDoctrineRules.AllowWithdrawal(
                    ArmyRtsWarDoctrine.Current,
                    ArmyRtsWithdrawalOrigin.MinimumForce,
                    ArmyRtsWarDoctrineRules.IsExplicitPlayerRetreat(
                        record.Mission)))
            {
                next = record.Mission.ProposalKind ==
                           ArmyRtsProposalKind.Attack ||
                       record.Mission.Posture == ArmyRtsPosture.Attack
                    ? ArmyRtsState.Assault
                    : ArmyRtsState.Hold;
                Controllers.SetState(pArmyId, next);
            }
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
                if (!ArmyRtsWarDoctrineRules.AllowWithdrawal(
                        ArmyRtsWarDoctrine.Current,
                        ArmyRtsWithdrawalOrigin.RegroupStall,
                        ArmyRtsWarDoctrineRules.IsExplicitPlayerRetreat(
                            record.Mission)))
                {
                    Controllers.SetState(pArmyId, ArmyRtsState.Hold);
                    Controllers.Requeue(pArmyId);
                    return;
                }
                if (!ArmyRetreatService.AssignArmyRetreat(army,
                        record.Mission.TargetCityId,
                        ArmyRtsWithdrawalOrigin.RegroupStall))
                    RecoverUnavailableRetreat(army);
                return;
            }
            if (commit && next == ArmyRtsState.Retreat &&
                record.Mission.Posture != ArmyRtsPosture.Retreat)
            {
                if (!ArmyRtsWarDoctrineRules.AllowWithdrawal(
                        ArmyRtsWarDoctrine.Current,
                        ArmyRtsWithdrawalOrigin.MinimumForce,
                        ArmyRtsWarDoctrineRules.IsExplicitPlayerRetreat(
                            record.Mission)))
                {
                    Controllers.SetState(pArmyId, ArmyRtsState.Hold);
                    Controllers.Requeue(pArmyId);
                    return;
                }
                if (!ArmyRetreatService.AssignArmyRetreat(army, -1L,
                        ArmyRtsWithdrawalOrigin.MinimumForce))
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
                !runtime.RetreatSelectionPending && !runtime.NoSafeRetreat &&
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
                CoalitionWarTaskService.ReleaseObjectiveClaim(
                    record.Mission.WarId, pArmyId,
                    record.Mission.TargetCityId);
                Invalidate(pArmyId);
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
            bool insideTarget = IsInsideTargetTerritory(captain, target);
            Kingdom kingdom = SafeKingdom(pArmy);
            War war = FindWar(pRecord.Mission.WarId);
            bool objectiveOpen = target?.data != null &&
                !TargetComplete(pArmy, pRecord.Mission, target, kingdom);
            bool inspectTargetCityCombat = ArmyRtsControllerRules.
                ShouldInspectTargetCityCombat(insideTarget,
                    pRuntime.SiegeCombatActive);
            bool hostileInTargetCity = inspectTargetCityCombat &&
                target?.data != null &&
                CityAttackZoneService.HasHostileMilitaryInside(war,
                    target, kingdom);
            bool enterMissionCityCombat = ArmyRtsControllerRules.
                ShouldEnterTargetCityCombat(
                    pRecord.Mission.ProposalKind, objectiveOpen,
                    insideTarget, hostileInTargetCity);
            if (enterMissionCityCombat || pRuntime.SiegeCombatActive)
            {
                bool cityCombatActive = EnterTargetCitySiegeCombat(
                    pArmy, pRecord, pRuntime, target);
                if (cityCombatActive) return true;
            }
            bool targetIsEnemy = IsEnemyWarTarget(war, target, kingdom);
            bool withdrawalRequired = ArmyRtsWarLifecycleRules.
                ShouldWithdraw(SafeUnitCount(pArmy),
                    lifecycle.BaselineStrength);
            ArmyRtsCombatControlDecision decision =
                ArmyRtsWarLifecycleRules.ResolveCombatControl(
                    ArmyRtsWarDoctrine.Current, lifecycle.Phase,
                    withdrawalRequired, insideTarget, targetIsEnemy,
                    objectiveOpen,
                    ArmyRtsWarDoctrineRules.IsExplicitPlayerRetreat(
                        pRecord.Mission));
            switch (decision)
            {
                case ArmyRtsCombatControlDecision.ReleaseToVanilla:
                    // Keep city capture metadata published, but retain RTS
                    // tactical ownership so vanilla fighting cannot replace the
                    // member combat task inside the target city.
                    bool attackOrderPublished =
                        TryIssueVanillaCityAttackOrder(pArmy,
                            pRecord.Mission, target);
                    if (!attackOrderPublished &&
                        AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
                        ModClass.LogWarning(
                            "[AW3 RTS siege] city attack target " +
                            "publication deferred army=" + pArmy.id +
                            " target_city=" + (target?.id ?? -1L));
                    return EnterTargetCitySiegeCombat(pArmy, pRecord,
                        pRuntime, target);
                case ArmyRtsCombatControlDecision.KeepVanillaControl:
                    // Saved games from the former handoff model must regain
                    // tactical ownership before their next city combat tick.
                    pRuntime.FieldCombatReleased = false;
                    ReacquireFromVanillaCombat(pArmy, pRecord, pRuntime,
                        ArmyRtsWarPhase.StrategicMovement);
                    EnterFieldCombat(pArmy, pRuntime);
                    return true;
                case ArmyRtsCombatControlDecision.
                    ReacquireStrategicControl:
                    ReacquireFromVanillaCombat(pArmy, pRecord, pRuntime,
                        ArmyRtsWarPhase.StrategicMovement);
                    return false;
                case ArmyRtsCombatControlDecision.ReacquireForWithdrawal:
                    if (!ArmyRtsWarDoctrineRules.AllowWithdrawal(
                            ArmyRtsWarDoctrine.Current,
                            ArmyRtsWithdrawalOrigin.CasualtyThreshold,
                            ArmyRtsWarDoctrineRules.IsExplicitPlayerRetreat(
                                pRecord.Mission)))
                    {
                        Controllers.SetState(pArmy.id, ArmyRtsState.Hold);
                        Controllers.Requeue(pArmy.id);
                        return true;
                    }
                    ReacquireFromVanillaCombat(pArmy, pRecord, pRuntime,
                        ArmyRtsWarPhase.Replenishing);
                    City replenishmentSource = AWArmyService.FindAnchorCity(
                        pArmy);
                    if (replenishmentSource?.data != null)
                        ArmyRtsWarLifecycleService.BeginReplenishing(
                            pRecord.Mission.WarId, pArmy,
                            replenishmentSource);
                    Controllers.SetState(pArmy.id, ArmyRtsState.Regroup);
                    return false;
                default:
                    return false;
            }
        }

        // 野战脱离：两军在任意位置交火时把 actor 交给原版战斗 AI，
        // 战斗打完再收回 RTS 控制。返回 true 表示本 tick 已释放、
        // 应跳过后续行军/编队命令；返回 false 表示未释放或已收回，继续正常流程。
        private static bool TryHandleFieldCombat(Army pArmy,
            RuntimeState pRuntime)
        {
            if (pArmy?.data == null || pRuntime == null) return false;
            if (pRuntime.SiegeCombatActive) return true;
            if (IsActiveMissionObjectiveComplete(pArmy))
            {
                if (pRuntime.FieldCombatReleased)
                    ExitFieldCombat(pArmy, pRuntime);
                return false;
            }
            // 运输中禁止野战脱离：运输是不可中断的高优先级移动，
            // 释放会丢失船只分配和登船进度导致永久卡住。
            if (ArmyRtsTransportService.HasActiveVoyage(pArmy))
                return false;

            // 队长正在行军时强制收回控制，防止队长走远、士兵脱节
            Actor captain = SafeCaptain(pArmy);
            if (captain != null && AWArmyMarchService.HasActiveMarch(captain))
            {
                if (pRuntime.FieldCombatReleased)
                    ExitFieldCombat(pArmy, pRuntime);
                return false;
            }

            // 抽象决战模式不接管 actor，与 ResolveCombatControl 保持一致。
            if (ArmyRtsWarDoctrine.IsAbstractDecisive)
            {
                if (pRuntime.FieldCombatReleased)
                    ExitFieldCombat(pArmy, pRuntime);
                return false;
            }

            CountFieldCombatEngagement(pArmy, out int engaged,
                out int liveCombatants, out bool captainEngaged);
            Actor combatTarget = captain?.beh_actor_target?.a;
            if (!IsValidCaptainCombatTarget(captain, combatTarget))
                combatTarget = FindCaptainCombatTarget(captain);
            bool release = ArmyRtsFieldCombatRules.ShouldKeepFieldCombat(
                pRuntime.FieldCombatReleased, combatTarget != null, engaged,
                liveCombatants, captainEngaged);

            if (release)
            {
                if (!pRuntime.FieldCombatReleased)
                    EnterFieldCombat(pArmy, pRuntime);
                else
                {
                    Actor combatCaptain = SafeCaptain(pArmy);
                    if (combatCaptain?.data != null &&
                        !combatCaptain.isTask(
                            ArmyRtsContent.CaptainCombatTaskId))
                        SetCaptainCombatTask(combatCaptain);
                }
                return true;
            }
            if (pRuntime.FieldCombatReleased)
                ExitFieldCombat(pArmy, pRuntime);
            return false;
        }

        internal static bool TryEnterFieldCombatFromP0(Actor pContactActor)
        {
            Army army = pContactActor?.army;
            bool missionActive = army?.data != null &&
                                 HasActiveMission(army.id);
            if (!missionActive ||
                !RuntimeByArmy.TryGetValue(army.id,
                    out RuntimeState runtime)) return false;

            Actor captain = SafeCaptain(army);
            bool contactIsCaptain = pContactActor == captain;
            Actor combatTarget = pContactActor?.attack_target?.a;
            bool validContactTarget = contactIsCaptain
                ? IsValidCaptainCombatTarget(pContactActor, combatTarget)
                : IsValidMemberCombatTarget(pContactActor, combatTarget);
            if (!validContactTarget)
            {
                combatTarget = pContactActor?.beh_actor_target?.a;
                validContactTarget = contactIsCaptain
                    ? IsValidCaptainCombatTarget(pContactActor, combatTarget)
                    : IsValidMemberCombatTarget(pContactActor, combatTarget);
            }
            if (!ArmyRtsFieldCombatRules.ShouldRequestFieldCombatFromP0(
                    missionActive, runtime.FieldCombatReleased,
                    contactIsCaptain, validContactTarget))
                return runtime.FieldCombatReleased;
            if (runtime.SiegeCombatActive ||
                ArmyRtsTransportService.HasActiveVoyage(army) ||
                ArmyRtsWarDoctrine.IsAbstractDecisive ||
                IsActiveMissionObjectiveComplete(army)) return false;

            CountFieldCombatEngagement(army, out int engaged,
                out int liveCombatants, out bool captainEngaged);
            if (!ArmyRtsFieldCombatRules.ShouldKeepFieldCombat(
                    pAlreadyReleased: false,
                    pCaptainHasCombatTarget: true,
                    engaged, liveCombatants, captainEngaged)) return false;
            EnterFieldCombat(army, runtime);
            return true;
        }

        private static bool IsActiveMissionObjectiveComplete(Army pArmy)
        {
            if (pArmy?.data == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return false;
            City target = FindCity(record.Mission.TargetCityId);
            return target?.data != null && TargetComplete(pArmy,
                record.Mission, target, SafeKingdom(pArmy));
        }

        private static void CountFieldCombatEngagement(Army pArmy,
            out int pEngaged, out int pLiveCombatants,
            out bool pCaptainEngaged)
        {
            pEngaged = 0;
            pLiveCombatants = 0;
            pCaptainEngaged = false;
            Actor captain = SafeCaptain(pArmy);
            if (captain?.data != null)
                pCaptainEngaged = ArmyRtsFieldCombatRules.IsMemberEngaged(
                    HasImmediateCombatPriority(captain),
                    IsValidCaptainCombatTarget(captain,
                        captain.beh_actor_target?.a));
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                if (actor == captain) continue;
                if (!IsLiveCombatantActor(actor)) continue;
                pLiveCombatants++;
                bool immediateAttack = HasImmediateCombatPriority(actor);
                bool behaviourTarget = IsValidMemberCombatTarget(actor,
                    actor.beh_actor_target?.a);
                if (ArmyRtsFieldCombatRules.IsMemberEngaged(
                        immediateAttack, behaviourTarget)) pEngaged++;
            }
        }

        // 进入野战战斗：执行释放动作，但不改持久 phase——
        // 若设成 VanillaCombat，下一 tick 因 insideTargetTerritory==false
        // 会被 ResolveCombatControl 立刻收回，退化成拉锯。
        private static void EnterFieldCombat(Army pArmy,
            RuntimeState pRuntime)
        {
            pRuntime?.NativeRoute.MarkMovementInterrupted();
            ArmyRouteProviderService.Cancel(pArmy.id,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmy.id);
            ResetStrategicMovementRuntime(pRuntime);
            SetCaptainTacticalTask(SafeCaptain(pArmy));
            pRuntime.FieldCombatReleased = true;
            RegisterFieldCombatMembers(pArmy);
        }

        private static bool EnterTargetCitySiegeCombat(Army pArmy,
            ArmyRtsControllerRecord pRecord, RuntimeState pRuntime,
            City pTarget)
        {
            if (pArmy?.data == null || pRecord?.Mission == null ||
                pRuntime == null || pTarget?.data == null) return false;
            Actor captain = SafeCaptain(pArmy);
            Actor target = null;
            try { target = World.world?.units?.get(pRuntime.SiegeTargetActorId); }
            catch { }
            if (!IsViableSiegeCombatTarget(captain, target) ||
                !IsInsideCityCombatZone(target, pTarget))
                target = FindTargetCitySiegeTarget(captain, pTarget);
            if (target?.data == null)
            {
                ExitTargetCitySiegeCombat(pArmy, pRuntime);
                if (AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
                    ModClass.LogInfo("[AW3 RTS siege] no hostile target " +
                                     "inside target city; resume strategic " +
                                     "army=" + pArmy.id + " city=" +
                                     pTarget.id);
                return false;
            }
            if (pRuntime.FieldCombatReleased)
                ExitFieldCombat(pArmy, pRuntime);
            bool enteringSiege = !pRuntime.SiegeCombatActive;
            pRuntime.SiegeTargetActorId = target.data.id;
            if (!enteringSiege) return true;
            pRuntime.NativeRoute.MarkMovementInterrupted();
            ArmyRouteProviderService.Cancel(pArmy.id,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmy.id);
            ClearArmyAttackTargets(pArmy);
            ResetStrategicMovementRuntime(pRuntime);
            pRuntime.SiegeCombatActive = true;
            SetCaptainTacticalTask(captain);
            RegisterTargetCitySiegeMembers(pArmy);
            if (AWPerformanceSettings.ArmyRtsDiagnosticsEnabled)
                ModClass.LogInfo("[AW3 RTS siege] enter army=" + pArmy.id +
                                 " city=" + pTarget.id + " target=" +
                                 target.data.id);
            return true;
        }

        private static void RegisterTargetCitySiegeMembers(Army pArmy)
        {
            Actor captain = SafeCaptain(pArmy);
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                if (actor == captain || !IsLiveWarriorActor(actor)) continue;
                SetNativeMemberMissionTask(actor, pArmy,
                    pForceReassert: true);
                ArmyMilitaryMovementPriorityIndex.Register(actor.data.id,
                    ArmyMilitaryMovementPriorityKind.RtsMember);
            }
        }

        private static void ExitTargetCitySiegeCombat(Army pArmy,
            RuntimeState pRuntime)
        {
            if (pRuntime == null || !pRuntime.SiegeCombatActive) return;
            pRuntime.SiegeCombatActive = false;
            pRuntime.SiegeTargetActorId = -1L;
            ClearArmyCombatTasks(pArmy);
            ResetStrategicMovementRuntime(pRuntime);
            pRuntime.JobCursor.Reopen();
        }

        private static void RegisterFieldCombatMembers(Army pArmy)
        {
            Actor captain = SafeCaptain(pArmy);
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                if (actor == captain || !IsLiveWarriorActor(actor))
                    continue;
                SetNativeMemberMissionTask(actor, pArmy,
                    pForceReassert: true);
                ArmyMilitaryMovementPriorityIndex.Register(actor.data.id,
                    ArmyMilitaryMovementPriorityKind.RtsMember);
            }
        }

        // 战场已清，收回 RTS 控制：重开 job 分配，让正常流程重新接管。
        private static void ExitFieldCombat(Army pArmy,
            RuntimeState pRuntime)
        {
            pRuntime.FieldCombatReleased = false;
            Actor captain = SafeCaptain(pArmy);
            if (captain?.data != null)
            {
                try { captain.beh_actor_target = null; }
                catch { }
                try { captain.cancelAllBeh(); }
                catch { }
            }
            ResetStrategicMovementRuntime(pRuntime);
            pRuntime.JobCursor.Reopen();
        }

        private static void ClearArmyCombatTasks(Army pArmy)
        {
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                if (actor?.data == null) continue;
                try { actor.cancelAllBeh(); }
                catch { }
            }
            Actor captain = SafeCaptain(pArmy);
            if (captain?.data != null)
            {
                try { captain.cancelAllBeh(); }
                catch { }
            }
        }

        private static bool TryIssueVanillaCityAttackOrder(Army pArmy,
            ArmyRtsMission pMission, City pTarget)
        {
            City source = AWArmyService.FindAnchorCity(pArmy);
            Actor captain = SafeCaptain(pArmy);
            WorldTile targetTile = null;
            try { targetTile = pTarget?.getTile(); }
            catch { }
            if (!ArmyRtsTransportRules.ShouldAllowVanillaLandAttack(
                    captain?.current_tile?.data != null,
                    targetTile?.data != null,
                    SafeSameIsland(captain?.current_tile, targetTile)))
                return false;
            return TryIssueVanillaCityAttackOrder(source, pMission, pTarget,
                pMovementOrigin: captain?.current_tile);
        }

        private static bool TryIssueVanillaCityAttackOrder(City pSource,
            ArmyRtsMission pMission, City pTarget,
            WorldTile pMovementOrigin = null)
        {
            City source = pSource;
            if (source?.data == null || pMission == null ||
                pTarget?.data == null || pMission.TargetCityId != pTarget.id)
                return false;
            WorldTile sourceTile = pMovementOrigin;
            if (sourceTile == null)
            {
                try { sourceTile = source.getTile(); }
                catch { }
            }
            WorldTile targetTile = null;
            try { targetTile = pTarget.getTile(); }
            catch { }
            if (!ArmyRtsTransportRules.ShouldAllowVanillaLandAttack(
                    sourceTile?.data != null, targetTile?.data != null,
                    SafeSameIsland(sourceTile, targetTile)))
                return false;
            try
            {
                source.target_attack_city = pTarget;
                if (source.target_attack_zone?.city != pTarget)
                    source.target_attack_zone = pTarget.hasZones()
                        ? pTarget.zones.GetRandom()
                        : null;
                return source.target_attack_zone?.city == pTarget;
            }
            catch { return false; }
        }

        private static void ReacquireFromVanillaCombat(Army pArmy,
            ArmyRtsControllerRecord pRecord, RuntimeState pRuntime,
            ArmyRtsWarPhase pPhase)
        {
            ExitTargetCitySiegeCombat(pArmy, pRuntime);
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
            pRuntime.EscortBelowQuorumSinceWorldTime = double.NaN;
            pRuntime.EscortHoldActive = false;
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

        private static void UpdateEscortHold(RuntimeState pRuntime,
            ArmyRtsState pState, bool pObservationComplete,
            int pEscortPopulation, int pRalliedFollowers, double pWorldTime)
        {
            if (pRuntime == null) return;
            bool departed = pRuntime.RouteSubmitted ||
                            pState == ArmyRtsState.March ||
                            pState == ArmyRtsState.Deploy ||
                            pState == ArmyRtsState.Assault ||
                            pState == ArmyRtsState.Pursue;
            if (!departed || !pObservationComplete ||
                ArmyRtsRules.HasLandEscortQuorum(pEscortPopulation,
                    pRalliedFollowers, captainPresent: true))
            {
                pRuntime.EscortBelowQuorumSinceWorldTime = double.NaN;
                pRuntime.EscortHoldActive = false;
                return;
            }
            double now = double.IsNaN(pWorldTime) ||
                         double.IsInfinity(pWorldTime)
                ? 0d
                : Math.Max(0d, pWorldTime);
            if (double.IsNaN(pRuntime.EscortBelowQuorumSinceWorldTime))
                pRuntime.EscortBelowQuorumSinceWorldTime = now;
            double secondsBelowQuorum = Math.Max(0d, now -
                pRuntime.EscortBelowQuorumSinceWorldTime);
            pRuntime.EscortHoldActive = ArmyRtsRules.ShouldHoldAfterEscortLoss(
                departed, pRalliedFollowers, pEscortPopulation,
                secondsBelowQuorum);
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
            ArmyFormationObservationProgress observation =
                ArmyFormationService.GetObservationProgress(pArmy);
            bool captainPresent = captain?.data != null &&
                                  captain.isAlive() && !captain.isRekt() &&
                                  captain.current_tile?.data != null;
            int escortPopulation = ArmyRtsRules.ResolveEscortPopulation(
                rosterLiving, rallyFollowers.Living, observation.Complete,
                captainPresent);
            bool escortQuorum = observation.Complete &&
                ArmyRtsRules.HasLandEscortQuorum(escortPopulation,
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
            War missionWar = FindWar(pRecord.Mission.WarId);
            bool warStarted = missionWar?.data != null;
            if (warStarted)
            {
                try { warStarted = !missionWar.hasEnded(); }
                catch { warStarted = false; }
            }
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
            if (pCommit)
                UpdateEscortHold(pRuntime, pRecord.State,
                    observation.Complete, escortPopulation,
                    rallyFollowers.Rallied, readinessTime);
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
            // The bounded staging window is the last escape hatch out of
            // Rally. It must be gated on the minimum operational force, not
            // on departure strength: departure strength stays false forever
            // once an army takes casualties, because the mission target is
            // max(living, persisted). An army that can no longer reach its
            // recruitment target -- or whose type has no reserve pool to
            // draw from -- would otherwise hold Rally permanently while RTS
            // ownership keeps suppressing the vanilla decisions.
            bool forcePreDeparture = ArmyRtsRules.
                ShouldForcePreDeparture(pCommit, pRecord.State,
                    minimumForceReady, captainPresent, escortQuorum,
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
            bool survivalException = pRecord.Mission.Role ==
                                     ArmyRtsRole.Defense &&
                                     target?.data != null &&
                                     kingdom?.capital == target;
            bool hostileWarriorInsideTargetCity = complete &&
                CityAttackZoneService.HasHostileMilitaryInside(
                    missionWar, target, kingdom);
            ArmyRtsState pursuitState = pRecord.State ==
                                        ArmyRtsState.Pursue
                ? ResolvePursuitState(captain, pRuntime, operational,
                    missionWar, target, kingdom)
                : ArmyRtsState.Pursue;
            bool pursuitAllowed = ArmyRtsRules.
                ShouldPursueCompletedTarget(
                    complete,
                    pRuntime.PursuitCompleted,
                    pRecord.Mission.Role == ArmyRtsRole.Assault,
                    operational.Supply >
                        ArmyLogisticsRules.CriticalSupply,
                    operational.InCorridor,
                    hostileWarriorInsideTargetCity) &&
                TryPreparePursuitRoute(pArmy, pRuntime, target);
            ArmyRtsTransitionFacts facts = pRuntime.TransitionFacts;
            facts.CurrentState = pRecord.State;
            facts.Role = pRecord.Mission.Role;
            facts.Posture = pRecord.Mission.Posture;
            facts.HasMission = true;
            facts.WarStarted = warStarted;
            facts.WartimeRecovery = wartimeRecovery;
            facts.FrontHold = frontHold;
            facts.TargetValid = targetValid;
            facts.FormationObservationComplete = observation.Complete;
            facts.RallyReady = observation.Complete &&
                    ArmyRtsRules.HasIncrementalRallyReadiness(
                        departureStrengthReady, escortPopulation,
                        rallyFollowers.Rallied, captainPresent) ||
                forcePreDeparture;
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
                ArmyRtsState.Regroup ||
                pRecord.State == ArmyRtsState.Regroup &&
                pRuntime.PursuitCompleted;
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
            if (captain?.current_tile == null || targetCity?.data == null)
                return;
            if (!ArmyRouteProviderService.CanSubmit) return;
            InvalidateNativeRoute(pRuntime, captain, "strategic_route_advanced");
            WorldTile strategicTarget = ResolveStableStrategicEndpoint(
                pArmy, targetCity, pRuntime);
            if (TryActivateStrategicTransport(pArmy, captain,
                    strategicTarget, pRuntime))
            {
                ArmyRouteProviderService.Cancel(pArmy.id,
                    ArmyRouteCancelReason.TargetReplaced);
                AWArmyMarchService.ClearArmy(pArmy.id);
                pRuntime.RouteSubmitted = true;
                pRuntime.RouteArrived = false;
                pRuntime.AnchorTileId = -1;
                pRuntime.AlternateTargetTileId = -1;
                return;
            }
            if (!SafeSameIsland(captain.current_tile, strategicTarget))
            {
                pRuntime.RouteSubmitted = false;
                pRuntime.RouteArrived = false;
                pRuntime.TransportRouteConfirmed = false;
                pRuntime.ForceTransportRoute = false;
                LogStrategicRouteFailure(pArmy, pMission, pRuntime,
                    captain, strategicTarget, ArmyRoutePollKind.Failed,
                    "transport_route_unavailable");
                return;
            }
            if (pMission.ProposalKind == ArmyRtsProposalKind.Attack)
                TryIssueVanillaCityAttackOrder(pArmy, pMission,
                    targetCity);
            ArmyRouteProviderService.Cancel(pArmy.id,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmy.id);
            pRuntime.RouteSubmitted = true;
            pRuntime.RouteArrived = IsInsideTargetTerritory(captain,
                targetCity);
            pRuntime.AnchorTileId = -1;
            pRuntime.AlternateTargetTileId = -1;
            pRuntime.TransportRouteConfirmed = false;
            pRuntime.ForceTransportRoute = false;
        }

        private static bool TryActivateStrategicTransport(Army pArmy,
            Actor pCaptain, WorldTile pTarget, RuntimeState pRuntime)
        {
            if (pArmy?.data == null || pCaptain?.current_tile?.data == null ||
                pTarget?.data == null || pRuntime == null) return false;
            bool activeVoyage = ArmyRtsTransportService.HasActiveVoyage(
                pArmy);
            bool routeAvailable = activeVoyage ||
                AWDockTransportService.TryResolveRoute(
                    pCaptain.current_tile, pTarget, out _);
            bool shouldActivate = ArmyRtsTransportRules.
                ShouldActivateStrategicTransport(
                    ArmyRtsRuntimeMode.ShouldCommit,
                    strategicMovementReady: true,
                    captainTileValid: true, targetTileValid: true,
                    sameIsland: SafeSameIsland(pCaptain.current_tile,
                        pTarget),
                    physicalRouteAvailable: routeAvailable,
                    voyageAlreadyActive: activeVoyage);
            if (!shouldActivate) return false;
            bool started = activeVoyage || ArmyRtsTransportService.
                TryHandleActor(pCaptain, pTarget, pMayBegin: true,
                    pForceTransport: true);
            pRuntime.TransportRouteConfirmed = started;
            pRuntime.ForceTransportRoute = started;
            return started;
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
            RuntimeState pRuntime, ArmyOperationalStateView pOperational,
            War pMissionWar, City pTargetCity, Kingdom pKingdom)
        {
            if (!pRuntime.PursuitRoute.Active ||
                pCaptain?.current_tile == null)
                return ArmyRtsState.Hold;
            // 虚空追击防护：如果目标城市已无敌军，立即停止追击
            if (pTargetCity?.data != null && pKingdom?.data != null &&
                !CityAttackZoneService.HasHostileMilitaryInside(
                    pMissionWar, pTargetCity, pKingdom))
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
            RuntimeState pRuntime, City pTargetCity)
        {
            if (pRuntime.PursuitRoute.Active) return true;
            if (pRuntime.PursuitRoute.Completed) return false;
            Actor captain = SafeCaptain(pArmy);
            WorldTile start = captain?.current_tile;
            WorldTile targetTile = pTargetCity?.getTile();
            if (start?.data == null)
            {
                pRuntime.PursuitRoute.Complete();
                return false;
            }

            // 计算目标方向向量（用于过滤候选点）
            int targetDx = 0, targetDy = 0;
            if (targetTile?.data != null)
            {
                targetDx = targetTile.x - start.x;
                targetDy = targetTile.y - start.y;
            }
            bool hasTargetDirection = targetDx != 0 || targetDy != 0;

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

                    // 方向过滤：只选择朝向目标方向的候选点
                    // 通过点积判定：候选向量与目标向量的点积应为正
                    if (hasTargetDirection)
                    {
                        int dotProduct = x * targetDx + y * targetDy;
                        if (dotProduct <= 0) continue;
                    }

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
            pRuntime.JobCursor.ObserveRosterVersion(
                pRuntime.RosterVersion);
            if (UsesVanillaArmyMovement(pArmy, pMission))
            {
                TryIssueVanillaCityAttackOrder(pArmy, pMission,
                    FindCity(pMission?.TargetCityId ?? -1L));
                EnsureVanillaMarchJobs(pArmy, pRuntime, pMission, pState);
                return;
            }
            if (Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord activeRecord) &&
                UsesNativeMissionExecution(pArmy, activeRecord))
            {
                if (UsesVanillaRetreatMovement(pArmy, pMission))
                    EnsureVanillaRetreatJobs(pArmy, pRuntime);
                else
                    EnsureVanillaMarchJobs(pArmy, pRuntime, pMission, pState);
                return;
            }
            if (UsesVanillaRetreatMovement(pArmy, pMission))
            {
                EnsureVanillaRetreatJobs(pArmy, pRuntime);
                return;
            }
            if (ArmyRtsControllerRules.ShouldUseVanillaFollowerMovement(
                    pMission?.ProposalKind ?? ArmyRtsProposalKind.None,
                    IsMissionTargetEnemy(pArmy, pMission)))
            {
                EnsureVanillaMarchJobs(pArmy, pRuntime, pMission, pState);
                return;
            }
            EnsureCustomMovementJobs(pArmy, pRuntime, pMission, pState);
        }

        private static void EnsureVanillaMarchJobs(Army pArmy,
            RuntimeState pRuntime, ArmyRtsMission pMission,
            ArmyRtsState pState)
        {
            ArmyRouteProviderService.Cancel(pArmy.id,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmy.id);
            ClearIndependentMemberPaths(pArmy, pRuntime);
            Actor captain = SafeCaptain(pArmy);
            if (IsLiveCombatantActor(captain))
            {
                SetJob(captain, ArmyRtsContent.CaptainJobId,
                    ArmyRtsContent.ResolveCaptainTaskId(pState,
                        ArmyRtsTransportService.GetPhase(pArmy)));
                if (pState != ArmyRtsState.Rally)
                    ArmyMilitaryMovementPriorityIndex.Register(
                        captain.data.id,
                        ArmyMilitaryMovementPriorityKind.RtsMember);
            }
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
                SetNativeMemberMissionTask(actor, pArmy);
                if (pState != ArmyRtsState.Rally &&
                    IsLiveWarriorActor(actor) &&
                    !HasImmediateCombatPriority(actor))
                    ArmyMilitaryMovementPriorityIndex.Register(
                        actor.data.id,
                        ArmyMilitaryMovementPriorityKind.RtsMember);
            }
            pRuntime.JobCursor.Advance(end, count);
            if (!jobsWereInitialized && pRuntime.JobCursor.JobsInitialized)
                pRuntime.NextJobOwnershipRepairWorldTime =
                    CurrentWorldTime() +
                    ArmyRtsRules.JobOwnershipRepairIntervalSeconds;
        }

        private static void EnsureVanillaRetreatJobs(Army pArmy,
            RuntimeState pRuntime)
        {
            ArmyRouteProviderService.Cancel(pArmy.id,
                ArmyRouteCancelReason.TargetReplaced);
            AWArmyMarchService.ClearArmy(pArmy.id);
            Actor captain = SafeCaptain(pArmy);
            SetJob(captain, ArmyRtsContent.RetreatCaptainJobId,
                ArmyRtsContent.RetreatTaskId);
            if (IsLiveCombatantActor(captain))
                ArmyMilitaryMovementPriorityIndex.Register(
                    captain.data.id,
                    ArmyMilitaryMovementPriorityKind.RtsMember);
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
                SetRetreatFollowerJob(actor);
                if (IsLiveWarriorActor(actor))
                    ArmyMilitaryMovementPriorityIndex.Register(
                        actor.data.id,
                        ArmyMilitaryMovementPriorityKind.RtsMember);
            }
            pRuntime.JobCursor.Advance(end, count);
            if (!jobsWereInitialized && pRuntime.JobCursor.JobsInitialized)
                pRuntime.NextJobOwnershipRepairWorldTime =
                    CurrentWorldTime() +
                    ArmyRtsRules.JobOwnershipRepairIntervalSeconds;
        }

        private static void EnsureCustomMovementJobs(Army pArmy,
            RuntimeState pRuntime, ArmyRtsMission pMission,
            ArmyRtsState pState)
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
                if (pState != ArmyRtsState.Rally)
                {
                    ArmyMilitaryMovementPriorityIndex.Register(
                        captain.data.id,
                        ArmyMilitaryMovementPriorityKind.RtsMember);
                    TrySubmitCaptainObjectiveRoute(captain, pRuntime);
                }
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
                bool transportOwned =
                    HasMilitaryTransportOwnership(actor);
                bool ownsObjective = ArmyRtsMemberObjectiveRules.
                    ShouldOwnMemberObjective(missionActive: true,
                        isCaptain: false,
                        actorEligible: IsLiveWarriorActor(actor),
                        immediateCombat: HasImmediateCombatPriority(actor),
                        transportActive: transportOwned);
                if (ownsObjective)
                {
                    SetJob(actor, ArmyRtsContent.FollowerJobId);
                    if (pState != ArmyRtsState.Rally)
                    {
                        ArmyMilitaryMovementPriorityIndex.Register(
                            actor.data.id,
                            ArmyMilitaryMovementPriorityKind.RtsMember);
                        TrySubmitMemberObjectiveRoute(actor, pRuntime);
                    }
                }
                else
                    ReleaseActor(actor);
            }
            pRuntime.JobCursor.Advance(end, count);
            if (!jobsWereInitialized && pRuntime.JobCursor.JobsInitialized)
                pRuntime.NextJobOwnershipRepairWorldTime =
                    CurrentWorldTime() +
                    ArmyRtsRules.JobOwnershipRepairIntervalSeconds;
        }

        private static void TrySubmitMemberObjectiveRoute(Actor pActor,
            RuntimeState pRuntime)
        {
            if (ArmyRtsTransportService.HasActiveVoyage(pActor?.army))
                return;
            if (ResolveFollowerTarget(pActor, out WorldTile target) !=
                    ArmyFollowerTargetResult.Move) return;
            TrySubmitActorObjectiveRoute(pActor, target, pRuntime);
        }

        private static void TrySubmitCaptainObjectiveRoute(Actor pActor,
            RuntimeState pRuntime)
        {
            Army army = pActor?.army;
            if (ArmyRtsTransportService.HasActiveVoyage(army)) return;
            if (!TryGetCaptainTarget(pActor, out WorldTile target) ||
                target == pActor?.current_tile) return;
            long armyId = army?.data?.id ?? -1L;
            long captainId = pActor?.data?.id ?? -1L;
            long targetCityId = ResolveCaptainMissionTargetCityId(army);
            int endpointTileId = target?.data?.tile_id ?? -1;
            if (pRuntime?.NativeRoute.IsLocked == true)
            {
                bool matches = pRuntime.NativeRoute.Matches(armyId,
                    targetCityId, endpointTileId, captainId);
                if (!matches || pRuntime.RouteImpossible)
                    InvalidateNativeRoute(pRuntime, pActor,
                        matches ? "explicit_failure" :
                        "target_or_captain_changed");
            }
            if (pRuntime?.NativeRoute.IsLocked == true)
            {
                pRuntime.NativeRoute.AdvanceTo(
                    pActor?.current_path_index ?? 0);
                if (!pRuntime.NativeRoute.NeedsNativeResume) return;
                if (HasActiveNativePath(pActor))
                {
                    pRuntime.NativeRoute.ObserveNativeResume();
                    return;
                }
            }
            TrySubmitActorObjectiveRoute(pActor, target, pRuntime);
            if (pRuntime != null && pActor?.data != null &&
                pRuntime.MemberObjectiveTileByActor.TryGetValue(
                    pActor.data.id, out int recordedTargetTileId) &&
                recordedTargetTileId == endpointTileId)
                TryCaptureCaptainNativeRoute(pActor, target, pRuntime);
        }

        private static long ResolveCaptainMissionTargetCityId(Army pArmy)
        {
            if (pArmy?.data == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record)) return -1L;
            return record?.Mission?.TargetCityId ?? -1L;
        }

        private static bool HasActiveNativePath(Actor pActor)
        {
            try
            {
                return pActor?.current_path != null &&
                       pActor.current_path_index >= 0 &&
                       pActor.current_path_index < pActor.current_path.Count;
            }
            catch { return false; }
        }

        private static void TryCaptureCaptainNativeRoute(Actor pActor,
            WorldTile pTarget, RuntimeState pRuntime)
        {
            if (pActor?.data == null || pTarget?.data == null ||
                pRuntime == null || pRuntime.NativeRoute.IsLocked ||
                !HasActiveNativePath(pActor)) return;
            var tileIds = new List<int>(pActor.current_path.Count);
            int cursor = 0;
            try
            {
                for (int i = 0; i < pActor.current_path.Count; i++)
                {
                    WorldTile tile = pActor.current_path[i];
                    int tileId = tile?.data?.tile_id ?? -1;
                    if (tileId >= 0)
                    {
                        tileIds.Add(tileId);
                        if (i < pActor.current_path_index) cursor++;
                    }
                }
            }
            catch { return; }
            if (tileIds.Count == 0) return;
            pRuntime.NativeRoute.Capture(pActor.army.id,
                ResolveCaptainMissionTargetCityId(pActor.army),
                pTarget.data.tile_id, pActor.data.id, tileIds,
                cursor);
        }

        private static void InvalidateNativeRoute(RuntimeState pRuntime,
            Actor pActor, string pReason)
        {
            if (pRuntime?.NativeRoute.IsLocked != true) return;
            pRuntime.NativeRoute.Invalidate();
            ArmyRtsMovementDiagnostic.Log("rts", "route_failure", pActor,
                "reason=native_route_lock_invalidated " +
                (pReason ?? "unspecified"));
        }

        private static void TrySubmitActorObjectiveRoute(Actor pActor,
            WorldTile target, RuntimeState pRuntime)
        {
            if (ShouldUseNativeMilitaryPath(pActor)) return;
            if (!SafeSameIsland(pActor?.current_tile, target)) return;
            int targetTileId = target.data?.tile_id ?? -1;
            long actorId = pActor?.data?.id ?? -1L;
            bool ownsPath = AWPathMovementBridge.HasOwnership(pActor);
            bool nativeLocalPath = pActor?.isFollowingLocalPath() == true ||
                                   pActor?.current_path_global != null;
            int recordedTargetTileId = pRuntime != null &&
                pRuntime.MemberObjectiveTileByActor.TryGetValue(actorId,
                    out int recorded) ? recorded : -1;
            if (ArmyRtsMemberObjectiveRules.ShouldReplaceMemberPath(
                    targetTileId >= 0, recordedTargetTileId, targetTileId,
                    ownsPath, nativeLocalPath))
            {
                ClearIndependentMemberPath(pActor, pRuntime);
                ownsPath = false;
                nativeLocalPath = false;
            }
            if (!ArmyRtsMemberObjectiveRules.ShouldSubmitMemberPath(
                target?.data != null, ownsPath, nativeLocalPath,
                pathPending: ownsPath)) return;
            try
            {
                pActor.goTo(target, pLimitPathfindingRegions: 0);
                if (pRuntime != null && actorId >= 0L)
                    pRuntime.MemberObjectiveTileByActor[actorId] =
                        targetTileId;
            }
            catch { }
        }

        private static void ClearIndependentMemberPaths(Army pArmy,
            RuntimeState pRuntime)
        {
            if (pArmy?.data == null || pRuntime == null) return;
            long[] actorIds = new long[
                pRuntime.MemberObjectiveTileByActor.Count];
            pRuntime.MemberObjectiveTileByActor.Keys.CopyTo(actorIds, 0);
            foreach (long actorId in actorIds)
            {
                Actor actor = FindActor(actorId);
                if (actor?.army == pArmy && !actor.is_inside_boat)
                    ClearIndependentMemberPath(actor, pRuntime);
            }
            pRuntime.MemberObjectiveTileByActor.Clear();
        }

        private static void ClearNativeMovementForMissionReplacement(
            Army pArmy)
        {
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                if (actor?.data == null) continue;
                try
                {
                    AWPathMovementBridge.Cancel(actor,
                        AWPathFailureReason.CancelledByNewRequest);
                    actor.stopMovement();
                    actor.clearOldPath();
                    actor.clearTileTarget();
                    actor.beh_tile_target = null;
                    actor.beh_actor_target = null;
                }
                catch { }
            }
            Actor captain = SafeCaptain(pArmy);
            if (captain?.data != null)
            {
                try
                {
                    AWPathMovementBridge.Cancel(captain,
                        AWPathFailureReason.CancelledByNewRequest);
                    captain.stopMovement();
                    captain.clearOldPath();
                    captain.clearTileTarget();
                    captain.beh_tile_target = null;
                    captain.beh_actor_target = null;
                }
                catch { }
            }
        }

        private static void ClearIndependentMemberPath(Actor pActor,
            RuntimeState pRuntime)
        {
            if (pActor?.data == null) return;
            if (AWPathMovementBridge.HasOwnership(pActor))
                AWPathMovementBridge.Cancel(pActor,
                    AWPathFailureReason.CancelledByNewRequest);
            pActor.stopMovement();
            pActor.clearOldPath();
            pActor.clearTileTarget();
            pRuntime?.MemberObjectiveTileByActor.Remove(pActor.data.id);
        }

        private static void SetJob(Actor pActor, string pJobId,
            string pTaskId = null, bool pForceReassert = false)
        {
            if (pActor?.data != null &&
                !AWArmyRoleRules.ShouldRtsOwnCaptain(
                    AWArmyService.GetRole(pActor.army),
                    RoyalGuardService.IsRoyalGuard(pActor),
                    IsCivilAuthorityActor(pActor)))
            {
                RoyalGuardService.EnsureProtectKingTask(pActor);
                return;
            }
            bool captainJob = (pJobId == ArmyRtsContent.CaptainJobId ||
                               pJobId == ArmyRtsContent.HoldJobId ||
                               pJobId == ArmyRtsContent.
                                   RetreatCaptainJobId) &&
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
                bool requiredBoatWork =
                    HasMilitaryTransportOwnership(pActor);
                bool inLiquid = pActor.current_tile?.Type?.liquid == true;
                bool selfLandingTask = pActor.isTask(
                    ArmyRtsTaskOwnershipRules.SelfLandingTaskId);
                if (ArmyRtsTaskOwnershipRules.
                        ShouldPreserveSelfLandingTask(inLiquid,
                            selfLandingTask, requiredBoatWork)) return;
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

        private static void SetRetreatFollowerJob(Actor pActor)
        {
            if (!IsLiveWarriorActor(pActor)) return;
            bool hadImmediateCombat = HasImmediateCombatPriority(pActor);
            if (hadImmediateCombat) ClearActorAttackTarget(pActor);
            SetJob(pActor, ArmyRtsContent.RetreatFollowerJobId,
                "warrior_army_follow_leader",
                pForceReassert: hadImmediateCombat);
        }

        private static void SetNativeMemberMissionTask(Actor pActor,
            Army pArmy, bool pForceReassert = false)
        {
            if (pActor?.data == null || pArmy?.data == null ||
                IsCaptain(pActor, pArmy)) return;
            string jobId = ArmyRtsContent.VanillaFollowerJobId;
            string taskId = "warrior_army_follow_leader";
            if (RuntimeByArmy.TryGetValue(pArmy.id,
                    out RuntimeState runtime))
            {
                bool useSiegeCombatTask = runtime.SiegeCombatActive &&
                    TryGetActiveTargetCitySiege(pActor, out _,
                        out City activeSiegeCity) &&
                    ArmyRtsCaptainCombatRules.ShouldUseSiegeCombatTask(
                        runtime.SiegeCombatActive,
                        IsInsideCityCombatZone(pActor, activeSiegeCity));
                if (useSiegeCombatTask)
                {
                    jobId = ArmyRtsContent.MemberCombatJobId;
                    taskId = ArmyRtsContent.SiegeCombatTaskId;
                }
                else if (HasValidMemberCombatTarget(pActor) &&
                    pActor.isTask("fighting")) return;
            }
            bool taskMissing = !pActor.isTask(taskId);
            SetJob(pActor, jobId, taskId,
                pForceReassert || taskMissing);
        }

        internal static bool HasImmediateCombatPriority(Actor pActor)
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

        private static bool IsEnemyWarTarget(War pWar, City pTarget,
            Kingdom pKingdom)
        {
            if (pWar?.data == null || pTarget?.data == null ||
                pKingdom?.data == null || pTarget.kingdom == null ||
                pTarget.kingdom == pKingdom) return false;
            try
            {
                return !pWar.hasEnded() && pWar.hasKingdom(pKingdom) &&
                       pWar.hasKingdom(pTarget.kingdom);
            }
            catch { return false; }
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
            Invalidate(pArmy.id);
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

        private static void RefreshReleasedArmyPeacetimeJobs(Army pArmy)
        {
            if (WarArmyReturnService.IsActive(pArmy)) return;
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                StandingArmyPeacetimeService.RefreshJob(actor);
            }
            Actor captain = SafeCaptain(pArmy);
            if (captain?.data != null)
                StandingArmyPeacetimeService.RefreshJob(captain);
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
                bool targetFriendly = target?.kingdom != null &&
                                      (target.kingdom == kingdom ||
                                       IsSameWarSide(war, kingdom,
                                           target.kingdom));
                bool targetInWar = targetFriendly ||
                                   IsKingdomInWar(war, target?.kingdom);
                bool targetSafe = targetFriendly && target.kingdom == kingdom &&
                                  !target.isGettingCaptured() &&
                                  !WarScoreService.
                                      IsCityFrozenControlledByEnemySide(
                                          target, kingdom);
                ArmyRtsObjectiveState objective =
                    ArmyRtsObjectiveService.Classify(war, kingdom, target);
                var facts = new ArmyRtsMissionTargetFacts
                {
                    Kind = pMission?.ProposalKind ?? ArmyRtsProposalKind.None,
                    Objective = objective,
                    CityLive = IsLiveCity(target),
                    ArmyKingdomLive = IsLiveKingdom(kingdom),
                    WarActive = IsKingdomInWar(war, kingdom),
                    ArmyKingdomInWar = IsKingdomInWar(war, kingdom),
                    TargetKingdomInWar = targetInWar,
                    TargetFriendly = targetFriendly,
                    TargetSafe = targetSafe,
                    ControlledFront = targetFriendly &&
                                      pMission?.FrontId >= 0L
                };
                return ArmyRtsMissionTargetRules.Validate(facts).Valid;
            }
            catch { return false; }
        }

        private static bool IsSameWarSide(War pWar, Kingdom pFirst,
            Kingdom pSecond)
        {
            if (pWar?.data == null || pFirst?.data == null ||
                pSecond?.data == null) return false;
            try { return pWar.onTheSameSide(pFirst, pSecond); }
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

        private static bool IsInsideTargetTerritory(Actor pActor,
            City pTarget)
        {
            if (pActor?.current_tile == null || pTarget?.data == null)
                return false;
            TileZone currentZone = null;
            try { currentZone = pActor.current_tile.zone; }
            catch { }
            bool exact = currentZone?.city == pTarget;
            bool border = false;
            bool adjacent = false;
            try
            {
                border = currentZone != null && pTarget.border_zones != null &&
                         pTarget.border_zones.Contains(currentZone);
                if (!exact && !border)
                {
                    WorldTile[] neighbours = pActor.current_tile.neighboursAll;
                    int count = Math.Min(8, neighbours?.Length ?? 0);
                    for (int i = 0; i < count; i++)
                    {
                        TileZone neighbourZone = null;
                        try { neighbourZone = neighbours[i]?.zone; }
                        catch { }
                        if (neighbourZone?.city == pTarget ||
                            pTarget.border_zones != null &&
                            pTarget.border_zones.Contains(neighbourZone))
                        {
                            adjacent = true;
                            break;
                        }
                    }
                }
            }
            catch { }
            return ArmyRtsWarLifecycleRules.ShouldTreatAsTargetTerritory(
                exact, border, adjacent);
        }

        internal static bool IsVanillaCombatArmy(Army pArmy)
        {
            if (pArmy?.data == null ||
                !Controllers.TryGet(pArmy.id,
                    out ArmyRtsControllerRecord record) ||
                record?.Mission == null) return false;
            return ArmyRtsWarLifecycleService.TryGet(
                       record.Mission.WarId, pArmy.id,
                       out ArmyRtsWarLifecycleRecord lifecycle) &&
                   ArmyRtsWarLifecycleRules.ShouldAllowVanillaCityAttack(
                       lifecycle.Phase);
        }

        internal static bool IsVanillaCombatCity(City pCity)
        {
            if (pCity?.data == null) return false;
            try
            {
                City attackTarget = pCity.target_attack_city ??
                    pCity.target_attack_zone?.city;
                if (attackTarget?.data == null) return false;
                IReadOnlyList<long> armyIds = MissionIndex.SnapshotTarget(
                    attackTarget.id);
                for (int i = 0; i < armyIds.Count; i++)
                {
                    long armyId = armyIds[i];
                    if (!Controllers.TryGet(armyId,
                            out ArmyRtsControllerRecord record) ||
                        record?.Mission?.TargetCityId != attackTarget.id)
                        continue;
                    Army army = FindArmy(armyId);
                    if (AWArmyService.FindAnchorCity(army) != pCity)
                        continue;
                    if (IsVanillaCombatArmy(army)) return true;
                }
                return false;
            }
            catch { return false; }
        }

        internal static bool IsVanillaMovementCity(City pCity)
        {
            if (pCity?.data == null) return false;
            try
            {
                City attackTarget = pCity.target_attack_city ??
                    pCity.target_attack_zone?.city;
                if (attackTarget?.data == null) return false;
                IReadOnlyList<long> armyIds = MissionIndex.SnapshotTarget(
                    attackTarget.id);
                for (int i = 0; i < armyIds.Count; i++)
                {
                    long armyId = armyIds[i];
                    if (!Controllers.TryGet(armyId,
                            out ArmyRtsControllerRecord record) ||
                        record?.Mission?.TargetCityId != attackTarget.id)
                        continue;
                    Army army = FindArmy(armyId);
                    if (AWArmyService.FindAnchorCity(army) == pCity)
                        return true;
                }
                return false;
            }
            catch { return false; }
        }

        private static int ResolveMissionTargetStrength(Army pArmy,
            Kingdom pKingdom, ArmyRtsMission pMission)
        {
            int living = SafeUnitCount(pArmy);
            int resolved;
            if (pArmy?.data != null && !AWArmyService.IsSpecialArmy(pArmy))
            {
                int persisted = pMission?.TargetStrength ?? 0;
                if (persisted > 0)
                {
                    resolved = Math.Max(living, persisted);
                }
                else
                {
                    int approved = CityArmyReinforcementService.ApprovedTarget(
                        pArmy, pKingdom);
                    resolved = Math.Max(living, approved);
                }
            }
            else
            {
                resolved = ArmyRtsRules.ResolveMissionTargetStrength(
                    pMission?.TargetStrength ?? 0,
                    StandingArmyService.TargetStrength(pArmy, pKingdom),
                    living);
            }
            if (pMission != null && pMission.TargetStrength <= 0)
                pMission.TargetStrength = resolved;
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
            if (RoyalGuardService.IsRoyalGuard(pActor)) return false;
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
                isCurrentCaptain: IsCaptain(pActor, pActor?.army),
                specialWarParticipant:
                    SpecialGovernmentWarParticipationService
                        .CanParticipateInRts(pActor));
        }

        private static bool IsLiveCombatantActor(Actor pActor)
        {
            return IsLiveActor(pActor) &&
                   (IsCaptain(pActor, pActor?.army) ||
                    pActor.is_profession_warrior &&
                    !IsCivilAuthorityActor(pActor) ||
                    SpecialGovernmentWarParticipationService
                        .CanParticipateInRts(pActor));
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
                   (!IsCivilAuthorityActor(pActor) ||
                    SpecialGovernmentWarParticipationService
                        .CanParticipateInRts(pActor));
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
