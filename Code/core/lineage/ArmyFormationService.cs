using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct ArmyFormationCounters
    {
        public ArmyFormationCounters(int pLiving, int pRallied,
            int pDeployed)
        {
            Living = pLiving;
            Rallied = pRallied;
            Deployed = pDeployed;
        }

        public int Living { get; }
        public int Rallied { get; }
        public int Deployed { get; }
        public bool RallyReady =>
            ArmyFormationRules.HasQuorum(Living, Rallied);
        public bool DeploymentReady =>
            ArmyFormationRules.HasQuorum(Living, Deployed);
    }

    public sealed class ArmyFormationCounterIndex
    {
        private sealed class MemberState
        {
            internal int Slot;
            internal bool Living;
            internal bool Rallied;
            internal bool Deployed;
        }

        private sealed class ArmyState
        {
            internal readonly Dictionary<long, MemberState> Members =
                new Dictionary<long, MemberState>();
            internal readonly bool[] OccupiedSlots = new bool[
                ArmyFormationRules.MaximumTrackedMembers];
            internal readonly Queue<long> OverflowMembers =
                new Queue<long>();
            internal readonly HashSet<long> OverflowMemberIds =
                new HashSet<long>();
            internal int Living;
            internal int Rallied;
            internal int Deployed;
        }

        private readonly Dictionary<long, ArmyState> _armies =
            new Dictionary<long, ArmyState>();

        public int ObserveMember(long armyId, long actorId, bool living,
            bool rallied, bool deployed)
        {
            if (!_armies.TryGetValue(armyId, out ArmyState army))
            {
                if (!living) return -1;
                army = new ArmyState();
                _armies[armyId] = army;
            }

            if (!army.Members.TryGetValue(actorId, out MemberState member))
            {
                if (!living) return -1;
                member = new MemberState
                {
                    Slot = FindAvailableSlot(army.OccupiedSlots,
                        ArmyFormationRules.StableSlotOrder(armyId,
                            actorId))
                };
                army.Members[actorId] = member;
                if (member.Slot < 0 &&
                    army.OverflowMemberIds.Add(actorId))
                    army.OverflowMembers.Enqueue(actorId);
            }

            if (!living)
            {
                RemoveMember(armyId, actorId);
                return -1;
            }
            army.Living += Delta(member.Living, living);
            army.Rallied += Delta(member.Rallied, rallied);
            army.Deployed += Delta(member.Deployed, deployed);
            member.Living = living;
            member.Rallied = rallied;
            member.Deployed = deployed;
            return member.Slot;
        }

        public bool RemoveMember(long armyId, long actorId)
        {
            if (!_armies.TryGetValue(armyId, out ArmyState army) ||
                !army.Members.TryGetValue(actorId, out MemberState member))
                return false;
            army.Living -= member.Living ? 1 : 0;
            army.Rallied -= member.Rallied ? 1 : 0;
            army.Deployed -= member.Deployed ? 1 : 0;
            int releasedSlot = member.Slot;
            if (releasedSlot >= 0 &&
                member.Slot < army.OccupiedSlots.Length)
                army.OccupiedSlots[member.Slot] = false;
            else
                army.OverflowMemberIds.Remove(actorId);
            army.Members.Remove(actorId);
            if (army.Members.Count == 0)
                _armies.Remove(armyId);
            else if (releasedSlot >= 0)
                PromoteOverflowMember(army, releasedSlot);
            return true;
        }

        public int RetainMembers(long armyId, ISet<long> actorIds,
            List<long> pRemovalScratch)
        {
            if (!_armies.TryGetValue(armyId, out ArmyState army)) return 0;
            if (actorIds == null || pRemovalScratch == null) return 0;
            pRemovalScratch.Clear();
            foreach (long actorId in army.Members.Keys)
                if (!actorIds.Contains(actorId))
                    pRemovalScratch.Add(actorId);
            for (int i = 0; i < pRemovalScratch.Count; i++)
                RemoveMember(armyId, pRemovalScratch[i]);
            return pRemovalScratch.Count;
        }

        public bool TryGetSlot(long armyId, long actorId, out int slot)
        {
            slot = -1;
            if (!_armies.TryGetValue(armyId, out ArmyState army) ||
                !army.Members.TryGetValue(actorId, out MemberState member))
                return false;
            slot = member.Slot;
            return true;
        }

        public ArmyFormationCounters GetCounters(long armyId)
        {
            return _armies.TryGetValue(armyId, out ArmyState army)
                ? new ArmyFormationCounters(army.Living, army.Rallied,
                    army.Deployed)
                : new ArmyFormationCounters(0, 0, 0);
        }

        public ArmyFormationCounters GetCountersExcluding(long armyId,
            long excludedActorId)
        {
            if (!_armies.TryGetValue(armyId, out ArmyState army))
                return new ArmyFormationCounters(0, 0, 0);
            int living = army.Living;
            int rallied = army.Rallied;
            int deployed = army.Deployed;
            if (excludedActorId >= 0L &&
                army.Members.TryGetValue(excludedActorId,
                    out MemberState excluded))
            {
                if (excluded.Living) living--;
                if (excluded.Rallied) rallied--;
                if (excluded.Deployed) deployed--;
            }
            return new ArmyFormationCounters(Math.Max(0, living),
                Math.Max(0, rallied), Math.Max(0, deployed));
        }

        public bool TryGetStrandedMemberId(long armyId, out long actorId)
        {
            actorId = -1L;
            if (!_armies.TryGetValue(armyId, out ArmyState army) ||
                ArmyFormationRules.HasQuorum(army.Living, army.Rallied))
                return false;
            foreach (KeyValuePair<long, MemberState> pair in army.Members)
            {
                if (!pair.Value.Living || pair.Value.Rallied ||
                    pair.Key < 0L) continue;
                if (actorId < 0L || pair.Key < actorId)
                    actorId = pair.Key;
            }
            return actorId >= 0L;
        }

        public bool TryGetUndeployedMemberId(long armyId, out long actorId)
        {
            actorId = -1L;
            if (!_armies.TryGetValue(armyId, out ArmyState army) ||
                ArmyFormationRules.HasQuorum(army.Living, army.Deployed))
                return false;
            foreach (KeyValuePair<long, MemberState> pair in army.Members)
            {
                if (!pair.Value.Living || pair.Value.Deployed ||
                    pair.Key < 0L) continue;
                if (actorId < 0L || pair.Key < actorId)
                    actorId = pair.Key;
            }
            return actorId >= 0L;
        }

        public bool TryGetUnralliedMemberId(long armyId,
            long excludedActorId, out long actorId)
        {
            actorId = -1L;
            if (!_armies.TryGetValue(armyId, out ArmyState army))
                return false;
            foreach (KeyValuePair<long, MemberState> pair in army.Members)
            {
                if (!pair.Value.Living || pair.Value.Rallied ||
                    pair.Key < 0L || pair.Key == excludedActorId) continue;
                if (actorId < 0L || pair.Key < actorId)
                    actorId = pair.Key;
            }
            return actorId >= 0L;
        }

        public bool RemoveArmy(long armyId)
        {
            return _armies.Remove(armyId);
        }

        public void Clear()
        {
            _armies.Clear();
        }

        private static int FindAvailableSlot(bool[] occupied,
            int preferred)
        {
            for (int offset = 0; offset < occupied.Length; offset++)
            {
                int slot = (preferred + offset) % occupied.Length;
                if (occupied[slot]) continue;
                occupied[slot] = true;
                return slot;
            }
            return -1;
        }

        private static void PromoteOverflowMember(ArmyState pArmy,
            int pReleasedSlot)
        {
            if (pArmy == null || pReleasedSlot < 0 ||
                pReleasedSlot >= pArmy.OccupiedSlots.Length) return;
            while (pArmy.OverflowMembers.Count > 0)
            {
                long actorId = pArmy.OverflowMembers.Dequeue();
                if (!pArmy.OverflowMemberIds.Remove(actorId) ||
                    !pArmy.Members.TryGetValue(actorId,
                        out MemberState member) || member.Slot >= 0)
                    continue;
                member.Slot = pReleasedSlot;
                pArmy.OccupiedSlots[pReleasedSlot] = true;
                return;
            }
        }

        private static int Delta(bool previous, bool current)
        {
            if (previous == current) return 0;
            return current ? 1 : -1;
        }
    }

#if !AW3_RULES_TESTS
    internal static class ArmyFormationService
    {
        private const int MembersObservedPerWorkItem = 16;
        private const int DeployedToleranceSquared = 9;

        private sealed class AnchorState
        {
            internal WorldTile Tile;
            internal int DirectionX;
            internal int DirectionY = 1;
            internal int DesiredWidth = 1;
            internal int TerrainWidth = 1;
            internal bool DeploymentEligible;
        }

        private sealed class RefreshState
        {
            internal int Cursor;
            internal int MemberCount;
            internal int RestartCount;
            internal bool Initialized;
            internal readonly HashSet<long> ObservedMemberIds =
                new HashSet<long>();
            internal readonly List<long> RemovedMemberIds =
                new List<long>();
        }

        private static readonly ArmyFormationCounterIndex CounterIndex =
            new ArmyFormationCounterIndex();
        private static readonly Dictionary<long, AnchorState> Anchors =
            new Dictionary<long, AnchorState>();
        private static readonly Dictionary<long, RefreshState> RefreshByArmy =
            new Dictionary<long, RefreshState>();
        private static readonly Dictionary<long, double>
            NextCorrectionByActor = new Dictionary<long, double>();

        public static void SetAnchor(Army pArmy, WorldTile pAnchor,
            int pDirectionX = 0, int pDirectionY = 1,
            bool pDeploymentEligible = false,
            bool pAllowObservationRestart = true)
        {
            if (pArmy?.data == null || pAnchor?.data == null) return;
            int directionX = Math.Sign(pDirectionX);
            int directionY = Math.Sign(pDirectionY);
            if (directionX == 0 && directionY == 0) directionY = 1;
            int desiredWidth = 1;
            int terrainWidth = 1;
            bool hadPrevious = Anchors.TryGetValue(pArmy.id,
                out AnchorState previous);
            bool changed = !hadPrevious ||
                           previous.Tile != pAnchor ||
                           previous.DirectionX != directionX ||
                           previous.DirectionY != directionY ||
                           previous.DesiredWidth != desiredWidth ||
                           previous.TerrainWidth != terrainWidth ||
                           previous.DeploymentEligible !=
                           pDeploymentEligible;
            bool deploymentEligibilityChanged = !hadPrevious ||
                previous.DeploymentEligible != pDeploymentEligible;
            bool geometryChanged = !hadPrevious ||
                previous.Tile != pAnchor ||
                previous.DirectionX != directionX ||
                previous.DirectionY != directionY ||
                previous.DesiredWidth != desiredWidth ||
                previous.TerrainWidth != terrainWidth;
            if (!changed) return;
            if (!hadPrevious)
            {
                previous = new AnchorState();
                Anchors[pArmy.id] = previous;
            }
            previous.Tile = pAnchor;
            previous.DirectionX = directionX;
            previous.DirectionY = directionY;
            previous.DesiredWidth = desiredWidth;
            previous.TerrainWidth = terrainWidth;
            previous.DeploymentEligible = pDeploymentEligible;
            if (changed && ArmyFormationRules.
                    ShouldRestartObservationForAnchorUpdate(
                        pAllowObservationRestart,
                        deploymentEligibilityChanged, pDeploymentEligible,
                        geometryChanged) && RefreshByArmy.TryGetValue(pArmy.id,
                    out RefreshState refresh))
            {
                refresh.Cursor = 0;
                refresh.RestartCount++;
                refresh.Initialized = false;
                refresh.ObservedMemberIds.Clear();
            }
        }

        // Follower movement may update the captain tile many times between
        // controller passes. Keep the controller-owned deployment mode intact.
        public static void RefreshAnchorFromCaptain(Army pArmy,
            WorldTile pCaptainTile)
        {
            if (pArmy?.data == null || pCaptainTile?.data == null) return;
            if (Anchors.TryGetValue(pArmy.id, out AnchorState current))
            {
                SetAnchor(pArmy, pCaptainTile, current.DirectionX,
                    current.DirectionY, current.DeploymentEligible,
                    pAllowObservationRestart: false);
                return;
            }
            SetAnchor(pArmy, pCaptainTile);
        }

        public static void ObserveArmy(Army pArmy, WorldTile pAnchor,
            bool pDeploymentEligible, int pDirectionX = 0,
            int pDirectionY = 1)
        {
            if (pArmy?.data == null) return;
            SetAnchor(pArmy, pAnchor, pDirectionX, pDirectionY,
                pDeploymentEligible);
            if (!Anchors.TryGetValue(pArmy.id, out AnchorState anchor))
                return;
            if (!RefreshByArmy.TryGetValue(pArmy.id,
                    out RefreshState refresh))
            {
                refresh = new RefreshState();
                RefreshByArmy[pArmy.id] = refresh;
            }

            int count;
            try { count = pArmy.units.Count; }
            catch { count = 0; }
            bool rosterChanged = refresh.Initialized &&
                refresh.MemberCount != count;
            if (!ArmyFormationRules.ShouldObserveRoster(
                    refresh.Initialized, refresh.MemberCount, count))
                return;
            refresh.MemberCount = count;
            if (rosterChanged) refresh.Initialized = false;
            if (refresh.Cursor < 0 || refresh.Cursor > count)
                refresh.Cursor = 0;
            if (refresh.Cursor == 0)
                refresh.ObservedMemberIds.Clear();
            int end = Math.Min(count, refresh.Cursor +
                                      MembersObservedPerWorkItem);
            for (int i = refresh.Cursor; i < end; i++)
            {
                Actor actor = null;
                try { actor = pArmy.units[i]; }
                catch { }
                if (IsEligibleFormationMember(actor, pArmy))
                    refresh.ObservedMemberIds.Add(actor.data.id);
                ObserveMemberAtAnchor(pArmy, actor, anchor);
            }
            if (end >= count)
            {
                CounterIndex.RetainMembers(pArmy.id,
                    refresh.ObservedMemberIds,
                    refresh.RemovedMemberIds);
                refresh.Cursor = 0;
                refresh.Initialized = true;
            }
            else
            {
                refresh.Cursor = end;
            }
        }

        public static ArmyFormationCounters GetCounters(Army pArmy)
        {
            if (pArmy?.data == null ||
                !RefreshByArmy.TryGetValue(pArmy.id,
                    out RefreshState refresh) || !refresh.Initialized)
                return new ArmyFormationCounters(0, 0, 0);
            return CounterIndex.GetCounters(pArmy.id);
        }

        public static ArmyFormationCounters GetIncrementalFollowerCounters(
            Army pArmy)
        {
            if (pArmy?.data == null)
                return new ArmyFormationCounters(0, 0, 0);
            Actor captain = SafeCaptain(pArmy);
            long captainId = captain?.data?.id ?? -1L;
            return CounterIndex.GetCountersExcluding(pArmy.id, captainId);
        }

        public static bool TryGetAnchor(Army pArmy, out WorldTile pAnchor)
        {
            pAnchor = null;
            if (pArmy?.data == null ||
                !Anchors.TryGetValue(pArmy.id, out AnchorState anchor) ||
                anchor.Tile?.data == null) return false;
            pAnchor = anchor.Tile;
            return true;
        }

        public static bool TryGetStrandedMember(Army pArmy,
            out Actor pActor)
        {
            pActor = null;
            if (pArmy?.data == null || !HasCompleteObservation(pArmy) ||
                !CounterIndex.TryGetStrandedMemberId(pArmy.id,
                    out long actorId)) return false;
            try { pActor = World.world?.units?.get(actorId); }
            catch { pActor = null; }
            return pActor?.data != null && pActor.army == pArmy &&
                   !pActor.isRekt() && pActor.isAlive() &&
                   pActor.current_tile?.data != null;
        }

        public static bool TryGetUndeployedMember(Army pArmy,
            out Actor pActor)
        {
            pActor = null;
            if (pArmy?.data == null || !HasCompleteObservation(pArmy) ||
                !CounterIndex.TryGetUndeployedMemberId(pArmy.id,
                    out long actorId)) return false;
            try { pActor = World.world?.units?.get(actorId); }
            catch { pActor = null; }
            return pActor?.data != null && pActor.army == pArmy &&
                   !pActor.isRekt() && pActor.isAlive() &&
                   pActor.current_tile?.data != null;
        }

        public static bool TryGetUnralliedMember(Army pArmy,
            out Actor pActor)
        {
            pActor = null;
            if (pArmy?.data == null || !HasCompleteObservation(pArmy))
                return false;
            Actor captain = SafeCaptain(pArmy);
            long captainId = captain?.data?.id ?? -1L;
            if (!CounterIndex.TryGetUnralliedMemberId(pArmy.id, captainId,
                    out long actorId)) return false;
            try { pActor = World.world?.units?.get(actorId); }
            catch { pActor = null; }
            return pActor?.data != null && pActor.army == pArmy &&
                   !pActor.isRekt() && pActor.isAlive() &&
                   pActor.current_tile?.data != null;
        }

        public static bool HasCompleteObservation(Army pArmy)
        {
            return pArmy?.data != null &&
                   RefreshByArmy.TryGetValue(pArmy.id,
                       out RefreshState refresh) && refresh.Initialized;
        }

        public static ArmyFormationObservationProgress
            GetObservationProgress(Army pArmy)
        {
            if (pArmy?.data == null || !RefreshByArmy.TryGetValue(pArmy.id,
                    out RefreshState refresh))
                return ArmyFormationRules.DescribeObservationProgress(0, 0,
                    observationComplete: false, restartCount: 0);
            return ArmyFormationRules.DescribeObservationProgress(
                refresh.MemberCount, refresh.Cursor, refresh.Initialized,
                refresh.RestartCount);
        }

        public static bool TryGetFollowerTarget(Actor pActor,
            out WorldTile pTarget)
        {
            pTarget = null;
            Army army = pActor?.army;
            if (!IsEligibleFormationMember(pActor, army) ||
                IsCaptain(pActor, army))
                return false;
            Actor captain = SafeCaptain(army);
            WorldTile captainTile = captain?.current_tile;
            if (captainTile?.data == null) return false;
            RefreshAnchorFromCaptain(army, captainTile);
            float captainDistance = DistanceSquared(pActor.current_tile,
                captainTile);
            int slot = GetOrAssignEscortSlot(army, pActor);
            bool rallied = captainDistance <=
                           ArmyFormationRules.LooseEscortOuterRadius *
                           ArmyFormationRules.LooseEscortOuterRadius;
            CounterIndex.ObserveMember(army.id, pActor.data.id,
                living: true, rallied: rallied, deployed: rallied);
            if (ArmyFormationRules.IsInsideLooseEscort(captainDistance))
            {
                pTarget = pActor.current_tile;
                return true;
            }
            WorldTile desired = ResolveLooseEscortTile(captainTile, slot);
            if (desired?.data == null) desired = captainTile;
            pTarget = desired;
            return pTarget?.data != null;
        }

        public static bool TryGetFollowerRecoveryTarget(Actor pActor,
            out WorldTile pTarget, bool pPreferAlternateSlot = false)
        {
            pTarget = null;
            Army army = pActor?.army;
            if (!IsEligibleFormationMember(pActor, army) ||
                IsCaptain(pActor, army)) return false;
            Actor captain = SafeCaptain(army);
            WorldTile captainTile = captain?.current_tile;
            if (captainTile?.data == null) return false;
            int slot = GetOrAssignEscortSlot(army, pActor);
            if (slot < 0) return false;
            WorldTile candidate = ResolveStrictLooseEscortTile(captainTile,
                slot);
            if (!pPreferAlternateSlot && candidate?.data != null &&
                IsRecoveryTileFree(candidate, pActor))
            {
                pTarget = candidate;
                return true;
            }

            // A stale formation slot can be blocked by a wall, terrain, or
            // another actor. Long-stall recovery still needs a safe landing.
            for (int attempt = 0;
                 ArmyFormationRules.TryGetFallbackRecoveryOffset(attempt,
                     out ArmyFormationOffset offset);
                 attempt++)
            {
                candidate = SafeTile(captainTile.x + offset.X,
                    captainTile.y + offset.Y);
                if (candidate == captainTile ||
                    !IsSafeFormationCandidate(captainTile, candidate) ||
                    !IsRecoveryTileFree(candidate, pActor)) continue;
                pTarget = candidate;
                return true;
            }
            return false;
        }

        public static bool TryResolveSharedPathTarget(Actor pActor,
            WorldTile pPathCenter, int pDirectionX, int pDirectionY,
            int pSlot, out WorldTile pTarget, out int pRowBehind)
        {
            pTarget = null;
            pRowBehind = 0;
            Army army = pActor?.army;
            if (!IsEligibleFormationMember(pActor, army) ||
                IsCaptain(pActor, army) ||
                pPathCenter?.data == null || pSlot < 0) return false;
            pRowBehind = Math.Abs(pSlot) % 3;
            pTarget = ResolveLooseEscortTile(pPathCenter, pSlot);
            return pTarget?.data != null;
        }

        public static bool TryResolveProviderRouteTarget(Actor pActor,
            WorldTile pPathCenter, int pDirectionX, int pDirectionY,
            int pSlot, out WorldTile pTarget)
        {
            pTarget = null;
            Army army = pActor?.army;
            if (!IsEligibleFormationMember(pActor, army) ||
                pPathCenter?.data == null || pSlot < 0) return false;
            int preferredLane = ArmySharedPathRules.StableRouteLane(pSlot);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int lane = attempt == 0
                    ? preferredLane
                    : attempt == 1
                        ? preferredLane == 0 ? 1 : 0
                        : preferredLane == -1 ? 1 : -1;
                ArmyMarchRules.RotateSlot(lane, 0, pDirectionX,
                    pDirectionY, out int x, out int y);
                WorldTile candidate = SafeTile(pPathCenter.x + x,
                    pPathCenter.y + y);
                if (!IsSafeFormationCandidate(pPathCenter, candidate))
                    continue;
                pTarget = candidate;
                return true;
            }
            ArmyProviderRouteTargetSource fallback = ArmySharedPathRules.
                ResolveProviderRouteTargetSource(
                    formationLaneAvailable: false,
                    providerCenterValidated: pPathCenter?.data != null);
            if (fallback !=
                ArmyProviderRouteTargetSource.ValidatedCenterline)
                return false;
            pTarget = pPathCenter;
            return true;
        }

        public static WorldTile ClampFollowerCorrectionTarget(
            WorldTile pCurrent, WorldTile pDesired)
        {
            return ClampToLocalCorrection(pCurrent, pDesired);
        }

        public static bool HasFollower(Actor pActor)
        {
            Army army = pActor?.army;
            return IsEligibleFormationMember(pActor, army) &&
                   !IsCaptain(pActor, army) &&
                   SafeCaptain(army)?.current_tile?.data != null;
        }

        public static bool IsInsideLooseEscort(Actor pActor)
        {
            Army army = pActor?.army;
            Actor captain = SafeCaptain(army);
            return IsEligibleFormationMember(pActor, army) &&
                   !IsCaptain(pActor, army) &&
                   captain?.current_tile?.data != null &&
                   ArmyFormationRules.IsInsideLooseEscort(
                       DistanceSquared(pActor.current_tile,
                           captain.current_tile));
        }

        public static bool CanIssueCorrection(Actor pActor, double pNow)
        {
            if (pActor?.data == null) return false;
            return !NextCorrectionByActor.TryGetValue(pActor.data.id,
                       out double next) || pNow >= next;
        }

        public static void RecordCorrection(Actor pActor, double pNow,
            double pCooldown)
        {
            if (pActor?.data == null) return;
            NextCorrectionByActor[pActor.data.id] = pNow +
                                                    Math.Max(0d, pCooldown);
        }

        public static void OnActorArmyChanged(Actor pActor, Army pPrevious,
            Army pCurrent)
        {
            if (pActor?.data == null) return;
            NextCorrectionByActor.Remove(pActor.data.id);
            if (pPrevious?.data != null)
                CounterIndex.RemoveMember(pPrevious.id, pActor.data.id);
            if (pCurrent?.data == null) return;
            bool living = IsEligibleFormationMember(pActor, pCurrent);
            bool rallied = false;
            bool deployed = false;
            if (living && Anchors.TryGetValue(pCurrent.id,
                    out AnchorState anchor))
            {
                bool captain = IsCaptain(pActor, pCurrent);
                WorldTile desired = captain
                    ? anchor.Tile
                    : ResolveSlotTile(pCurrent, pActor, anchor, out _);
                float anchorDistance = DistanceSquared(pActor.current_tile,
                    anchor.Tile);
                rallied = anchorDistance <=
                           ArmyFormationRules.LocalRadius *
                           ArmyFormationRules.LocalRadius;
                deployed = desired != null &&
                           ArmyFormationRules.IsMemberDeployed(
                               anchor.DeploymentEligible, captain,
                               anchorDistance,
                               DistanceSquared(pActor.current_tile, desired),
                               DeployedToleranceSquared);
            }
            CounterIndex.ObserveMember(pCurrent.id, pActor.data.id,
                living, rallied, deployed);
        }

        public static void OnActorDying(Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null) return;
            NextCorrectionByActor.Remove(pActor.data.id);
            CounterIndex.ObserveMember(army.id, pActor.data.id,
                living: false, rallied: false, deployed: false);
        }

        public static void RemoveArmy(long pArmyId)
        {
            CounterIndex.RemoveArmy(pArmyId);
            Anchors.Remove(pArmyId);
            RefreshByArmy.Remove(pArmyId);
        }

        public static void ClearRuntime()
        {
            CounterIndex.Clear();
            Anchors.Clear();
            RefreshByArmy.Clear();
            NextCorrectionByActor.Clear();
        }

        private static void ObserveMemberAtAnchor(Army pArmy, Actor pActor,
            AnchorState pAnchor)
        {
            if (pActor?.data == null) return;
            bool living = IsEligibleFormationMember(pActor, pArmy);
            if (!living)
            {
                CounterIndex.ObserveMember(pArmy.id, pActor.data.id,
                    living: false, rallied: false, deployed: false);
                return;
            }
            bool captain = IsCaptain(pActor, pArmy);
            WorldTile desired = captain
                ? pAnchor.Tile
                : ResolveSlotTile(pArmy, pActor, pAnchor, out _);
            float anchorDistance = DistanceSquared(pActor.current_tile,
                pAnchor.Tile);
            bool rallied = anchorDistance <=
                           ArmyFormationRules.LocalRadius *
                           ArmyFormationRules.LocalRadius;
            bool deployed = desired != null &&
                            ArmyFormationRules.IsMemberDeployed(
                                pAnchor.DeploymentEligible, captain,
                                anchorDistance,
                                DistanceSquared(pActor.current_tile, desired),
                                DeployedToleranceSquared);
            CounterIndex.ObserveMember(pArmy.id, pActor.data.id,
                living: true, rallied: rallied, deployed: deployed);
        }

        private static WorldTile ResolveSlotTile(Army pArmy, Actor pActor,
            AnchorState pAnchor, out int pSlot)
        {
            pSlot = -1;
            if (pArmy?.data == null || pActor?.data == null ||
                pAnchor?.Tile?.data == null) return null;
            if (!CounterIndex.TryGetSlot(pArmy.id, pActor.data.id,
                    out pSlot))
                pSlot = CounterIndex.ObserveMember(pArmy.id,
                    pActor.data.id, living: true, rallied: false,
                    deployed: false);
            if (pSlot < 0) return null;
            for (int attempt = 0;
                 attempt < ArmyFormationRules.PlacementAttempts; attempt++)
            {
                ArmyFormationOffset offset =
                    ArmyFormationRules.PlacementOffset(pSlot,
                        pAnchor.DesiredWidth, pAnchor.TerrainWidth, attempt);
                ArmyMarchRules.RotateSlot(offset.X, offset.Y,
                    pAnchor.DirectionX, pAnchor.DirectionY,
                    out int x, out int y);
                WorldTile candidate = SafeTile(pAnchor.Tile.x + x,
                    pAnchor.Tile.y + y);
                if (IsSafeFormationCandidate(pAnchor.Tile, candidate))
                    return candidate;
            }
            return null;
        }

        private static int GetOrAssignEscortSlot(Army pArmy,
            Actor pActor)
        {
            if (pArmy?.data == null || pActor?.data == null) return -1;
            if (CounterIndex.TryGetSlot(pArmy.id, pActor.data.id,
                    out int slot)) return slot;
            return CounterIndex.ObserveMember(pArmy.id, pActor.data.id,
                living: true, rallied: false, deployed: false);
        }

        private static WorldTile ResolveLooseEscortTile(WorldTile pAnchor,
            int pSlot)
        {
            if (pAnchor?.data == null || pSlot < 0) return null;
            ArmyFormationOffset offset =
                ArmyFormationRules.LooseEscortOffset(pSlot);
            WorldTile preferred = SafeTile(pAnchor.x + offset.X,
                pAnchor.y + offset.Y);
            if (IsSafeFormationCandidate(pAnchor, preferred))
                return preferred;
            WorldTile[] neighbours = preferred?.neighboursAll;
            int count = Math.Min(8, neighbours?.Length ?? 0);
            for (int i = 0; i < count; i++)
                if (IsSafeFormationCandidate(pAnchor, neighbours[i]))
                    return neighbours[i];
            return IsSafeFormationCandidate(pAnchor, pAnchor)
                ? pAnchor
                : null;
        }

        private static WorldTile ResolveStrictLooseEscortTile(
            WorldTile pAnchor, int pSlot)
        {
            if (pAnchor?.data == null || pSlot < 0) return null;
            ArmyFormationOffset offset =
                ArmyFormationRules.LooseEscortOffset(pSlot);
            WorldTile candidate = SafeTile(pAnchor.x + offset.X,
                pAnchor.y + offset.Y);
            return candidate != pAnchor &&
                   IsSafeFormationCandidate(pAnchor, candidate)
                ? candidate
                : null;
        }

        private static bool IsRecoveryTileFree(WorldTile pTile,
            Actor pActor)
        {
            if (pTile?.data == null) return false;
            bool occupied = false;
            try
            {
                pTile.doUnits(actor =>
                {
                    if (actor == pActor || actor?.data == null ||
                        actor.isRekt() || !actor.isAlive() ||
                        actor.current_tile != pTile) return;
                    occupied = true;
                });
            }
            catch { return false; }
            return !occupied;
        }

        private static WorldTile ClampToLocalCorrection(WorldTile pCurrent,
            WorldTile pDesired)
        {
            if (pCurrent?.data == null || pDesired?.data == null) return null;
            float distance = DistanceSquared(pCurrent, pDesired);
            if (ArmyFormationRules.CanDirectCorrect(distance))
                return pDesired;
            ArmyFormationRules.ClampVectorToRadius(
                pDesired.x - pCurrent.x, pDesired.y - pCurrent.y,
                ArmyFormationRules.LocalRadius, out int offsetX,
                out int offsetY);
            WorldTile local = SafeTile(pCurrent.x + offsetX,
                pCurrent.y + offsetY);
            if (IsSafeFormationCandidate(pCurrent, local)) return local;
            return FindSafeReconnectTarget(pCurrent, pDesired);
        }

        private static WorldTile FindSafeReconnectTarget(WorldTile pCurrent,
            WorldTile pDesired)
        {
            WorldTile best = null;
            float bestDistance = DistanceSquared(pCurrent, pDesired);
            int maximum = ArmySharedPathRules.LocalReconnectRadius;
            for (int radius = 1; radius <= maximum; radius++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    ConsiderReconnectCandidate(pCurrent, pDesired,
                        offsetX, -radius, ref best, ref bestDistance);
                    ConsiderReconnectCandidate(pCurrent, pDesired,
                        offsetX, radius, ref best, ref bestDistance);
                }
                for (int offsetY = -radius + 1;
                     offsetY < radius; offsetY++)
                {
                    ConsiderReconnectCandidate(pCurrent, pDesired,
                        -radius, offsetY, ref best, ref bestDistance);
                    ConsiderReconnectCandidate(pCurrent, pDesired,
                        radius, offsetY, ref best, ref bestDistance);
                }
            }
            if (best?.data != null) return best;
            return IsSafeFormationCandidate(pCurrent, pCurrent)
                ? pCurrent
                : null;
        }

        private static void ConsiderReconnectCandidate(WorldTile pCurrent,
            WorldTile pDesired, int pOffsetX, int pOffsetY,
            ref WorldTile pBest, ref float pBestDistance)
        {
            if (pOffsetX * pOffsetX + pOffsetY * pOffsetY >
                ArmySharedPathRules.LocalReconnectRadius *
                ArmySharedPathRules.LocalReconnectRadius) return;
            WorldTile candidate = SafeTile(pCurrent.x + pOffsetX,
                pCurrent.y + pOffsetY);
            if (!IsSafeFormationCandidate(pCurrent, candidate)) return;
            float distance = DistanceSquared(candidate, pDesired);
            if (distance >= pBestDistance) return;
            pBest = candidate;
            pBestDistance = distance;
        }

        private static int AvailableTerrainWidth(WorldTile pAnchor,
            int pDirectionX, int pDirectionY)
        {
            int perpendicularX = pDirectionY;
            int perpendicularY = -pDirectionX;
            int width = 1;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int distance = 1;
                     distance <= ArmyFormationRules.LocalRadius;
                     distance++)
                {
                    WorldTile candidate = SafeTile(
                        pAnchor.x + perpendicularX * distance * side,
                        pAnchor.y + perpendicularY * distance * side);
                    if (!IsSafeFormationCandidate(pAnchor, candidate)) break;
                    width++;
                }
            }
            return ArmyFormationRules.ClampTerrainWidth(
                ArmyFormationRules.MaximumFormationWidth, width);
        }

        private static bool IsSafeFormationCandidate(WorldTile pBase,
            WorldTile pCandidate)
        {
            if (pBase?.Type == null || pCandidate?.Type == null ||
                pCandidate.Type.block || pCandidate.Type.lava) return false;
            try
            {
                if (pCandidate.hasWallsAround()) return false;
            }
            catch { return false; }
            bool baseLiquid = pBase.Type.liquid || pBase.Type.ocean;
            bool candidateLiquid = pCandidate.Type.liquid ||
                                   pCandidate.Type.ocean;
            if (baseLiquid != candidateLiquid) return false;
            if (baseLiquid) return true;
            try { return pCandidate.isSameIsland(pBase); }
            catch { return false; }
        }

        private static bool IsEligibleFormationMember(Actor pActor,
            Army pArmy)
        {
            try
            {
                bool actorValid = pActor?.data != null &&
                                  pArmy?.data != null &&
                                  !pActor.isRekt() && pActor.isAlive() &&
                                  pActor.current_tile != null;
                bool belongsToArmy = actorValid && pActor.army == pArmy;
                return ArmyFormationRules.IsEligibleFormationMember(
                    actorValid, belongsToArmy,
                    pActor?.is_profession_warrior == true,
                    IsCivilAuthority(pActor), IsCaptain(pActor, pArmy));
            }
            catch { return false; }
        }

        private static bool IsCivilAuthority(Actor pActor)
        {
            try
            {
                return pActor?.data != null &&
                       (pActor.isKing() || pActor.isCityLeader());
            }
            catch { return false; }
        }

        private static bool IsCaptain(Actor pActor, Army pArmy)
        {
            try { return pArmy?.getCaptain() == pActor; }
            catch { return false; }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try { return pArmy?.getCaptain(); }
            catch { return null; }
        }

        private static WorldTile SafeTile(int pX, int pY)
        {
            try { return World.world?.GetTile(pX, pY); }
            catch { return null; }
        }

        private static float DistanceSquared(WorldTile pFirst,
            WorldTile pSecond)
        {
            if (pFirst == null || pSecond == null) return float.MaxValue;
            long x = pFirst.x - pSecond.x;
            long y = pFirst.y - pSecond.y;
            long distance = x * x + y * y;
            return distance >= int.MaxValue ? int.MaxValue : distance;
        }
    }
#endif
}
