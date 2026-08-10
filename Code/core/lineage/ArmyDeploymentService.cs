using System;
using System.Collections.Generic;
using AncientWarfare3.content;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyDeploymentService
    {
        private const int ArrivalRadius = 3;
        private const int ActorMutationBatchSize = 16;

        private sealed class FrontierTarget
        {
            public long CityId;
            public int TileId;
        }

        private sealed class NoticeAssignments
        {
            public WarNoticeState State;
            public string AssignmentKey = "";
            public long OwnerKingdomId = -1L;
            public long OpponentKingdomId = -1L;
            public bool IsDefenderSide;
            public readonly Dictionary<long, long> TargetCityByArmy = new Dictionary<long, long>();
            public readonly Dictionary<long, int> TargetTileByArmy = new Dictionary<long, int>();
            public readonly HashSet<long> ActorIds = new HashSet<long>();
            public readonly List<FrontierTarget> FrontierTargets = new List<FrontierTarget>();
            public readonly HashSet<int> FrontierTileSet = new HashSet<int>();
            public readonly List<long> TargetCityIds = new List<long>();
            public readonly HashSet<long> TargetCitySet = new HashSet<long>();
            public readonly List<long> RequiredArmyIds = new List<long>();
            public readonly HashSet<long> RequiredArmySet = new HashSet<long>();
            public readonly HashSet<long> BlockingArmyIds = new HashSet<long>();
            public readonly Dictionary<long, int> ArmyOrderById = new Dictionary<long, int>();
            public readonly Dictionary<long, int> NextActorIndexByArmy = new Dictionary<long, int>();
            public readonly Dictionary<long, int> AssignedTargetTileByArmy = new Dictionary<long, int>();
            public readonly HashSet<long> ArrivedArmyIds = new HashSet<long>();
            public readonly long[] CleanupBuffer = new long[ActorMutationBatchSize];
            public int CityDiscoveryCursor;
            public int ArmyReviewCursor;
            public long FallbackCityId = -1L;
            public int FallbackCoastTileId = -1;
            public long FallbackCoastCityId = -1L;
            public long FallbackCoastDistance = long.MaxValue;
            public int FallbackApproachTileId = -1;
            public long FallbackApproachCityId = -1L;
            public long FallbackApproachDistance = long.MaxValue;
            public bool DiscoveryComplete;
            public bool Closing;
            public bool RestoreJobs;
        }

        private sealed class NoticePriority : IComparable<NoticePriority>
        {
            public readonly int EarliestWarYear;
            public readonly int NoticeYear;
            public readonly string Signature;

            public NoticePriority(WarNoticeState pState)
            {
                EarliestWarYear = pState?.EarliestWarYear ?? int.MaxValue;
                NoticeYear = pState?.NoticeYear ?? int.MaxValue;
                Signature = pState?.Signature ?? "";
            }

            public int CompareTo(NoticePriority pOther)
            {
                if (pOther == null) return -1;
                return ArmyDeploymentRules.CompareNoticePriority(
                    EarliestWarYear, NoticeYear, Signature,
                    pOther.EarliestWarYear, pOther.NoticeYear, pOther.Signature);
            }
        }

        private sealed class SideNotice
        {
            public WarNoticeState State;
            public string AssignmentKey = "";
            public long OwnerKingdomId = -1L;
            public long OpponentKingdomId = -1L;
            public bool IsDefenderSide;
        }

        private sealed class KingdomNoticeGroup
        {
            public readonly long KingdomId;
            public readonly Dictionary<string, SideNotice> Notices =
                new Dictionary<string, SideNotice>(StringComparer.Ordinal);
            public readonly SortedSet<NoticePriority> Priorities =
                new SortedSet<NoticePriority>();
            public string PrimarySignature = "";

            public KingdomNoticeGroup(long pKingdomId)
            {
                KingdomId = pKingdomId;
            }
        }

        private static readonly Dictionary<string, NoticeAssignments> Assignments =
            new Dictionary<string, NoticeAssignments>(StringComparer.Ordinal);
        private static readonly Dictionary<long, KingdomNoticeGroup> KingdomNoticeGroups =
            new Dictionary<long, KingdomNoticeGroup>();
        private static readonly Dictionary<string, long> KingdomIdByAssignmentKey =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<string>> AssignmentKeysByNotice =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public static void ActivateNotice(WarNoticeState pState)
        {
            if (pState == null || string.IsNullOrEmpty(pState.Signature)) return;
            RegisterNotice(pState);
        }

        public static void RefreshNotice(WarNoticeState pState)
        {
            if (pState == null || string.IsNullOrEmpty(pState.Signature)) return;
            RegisterNotice(pState);
            RefreshSide(pState, pState.AttackerId);
            RefreshSide(pState, pState.DefenderId);
        }

        private static void RefreshSide(WarNoticeState pState,
            long pOwnerKingdomId)
        {
            NoticeAssignments assignments = ResolveDeclarationAssignments(
                pState, pOwnerKingdomId);
            if (assignments == null || assignments.Closing) return;
            if (!assignments.DiscoveryComplete)
                ScheduleDiscovery(assignments.AssignmentKey);
            else
                ScheduleArmyReview(assignments.AssignmentKey,
                    pRestart: true);
        }

        public static bool AreAllRequiredArmiesReady(WarNoticeState pState)
        {
            if (pState == null) return true;
            ActivateNotice(pState);
            bool attackerReady = EvaluateSideReadiness(pState,
                pState.AttackerId, out bool attackerBypassed);
            bool defenderReady = EvaluateSideReadiness(pState,
                pState.DefenderId, out bool defenderBypassed);
            return ArmyDeploymentRules.AreBothSidesReady(
                attackerBypassed, attackerReady,
                defenderBypassed, defenderReady);
        }

        public static bool TryGetCachedReadiness(WarNoticeState pState, out bool pReady)
        {
            pReady = false;
            if (pState == null ||
                !TryGetCachedSideReadiness(pState, pState.AttackerId,
                    out bool attackerBypassed,
                    out bool attackerReady) ||
                !TryGetCachedSideReadiness(pState, pState.DefenderId,
                    out bool defenderBypassed,
                    out bool defenderReady)) return false;
            pReady = ArmyDeploymentRules.AreBothSidesReady(
                attackerBypassed, attackerReady,
                defenderBypassed, defenderReady);
            return true;
        }

        private static bool EvaluateSideReadiness(WarNoticeState pState,
            long pOwnerKingdomId, out bool pBypassed)
        {
            pBypassed = false;
            Kingdom owner = ResolveKingdom(pOwnerKingdomId);
            if (owner?.data == null || owner.isRekt())
            {
                pBypassed = true;
                return false;
            }
            bool alreadyAtWar = MilitaryEmergencyService.
                TryGetActiveWarId(owner, out _);
            if (ArmyDeploymentRules.ShouldBypassPrewarDeployment(
                    alreadyAtWar))
            {
                pBypassed = true;
                return false;
            }
            NoticeAssignments assignments = ResolveDeclarationAssignments(
                pState, pOwnerKingdomId);
            return assignments != null &&
                   assignments.DiscoveryComplete &&
                   assignments.BlockingArmyIds.Count == 0;
        }

        private static bool TryGetCachedSideReadiness(
            WarNoticeState pState, long pOwnerKingdomId,
            out bool pBypassed, out bool pReady)
        {
            pBypassed = false;
            pReady = false;
            Kingdom owner = ResolveKingdom(pOwnerKingdomId);
            if (owner?.data == null || owner.isRekt())
            {
                pBypassed = true;
                return true;
            }
            bool alreadyAtWar = MilitaryEmergencyService.
                TryGetActiveWarId(owner, out _);
            if (ArmyDeploymentRules.ShouldBypassPrewarDeployment(
                    alreadyAtWar))
            {
                pBypassed = true;
                return true;
            }
            NoticeAssignments assignments = ResolveDeclarationAssignments(
                pState, pOwnerKingdomId);
            if (assignments == null) return false;
            pReady = assignments.DiscoveryComplete &&
                     assignments.BlockingArmyIds.Count == 0;
            return true;
        }

        public static bool TryGetPreferredLevyCity(Kingdom pDefender, int pOrdinal,
            out City pCity)
        {
            pCity = null;
            if (pDefender?.data == null || pOrdinal < 0 ||
                !KingdomNoticeGroups.TryGetValue(pDefender.id, out KingdomNoticeGroup group) ||
                string.IsNullOrEmpty(group.PrimarySignature) ||
                !Assignments.TryGetValue(BuildAssignmentKey(
                    group.PrimarySignature, group.KingdomId), out NoticeAssignments assignments) ||
                assignments.Closing || pOrdinal >= assignments.TargetCityIds.Count) return false;
            City city = ResolveCity(assignments.TargetCityIds[pOrdinal]);
            if (city?.data == null || city.isRekt() || city.kingdom != pDefender) return false;
            pCity = city;
            return true;
        }

        public static bool TryGetPreferredLevyCityCount(Kingdom pKingdom,
            out int pCount)
        {
            pCount = 0;
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                !KingdomNoticeGroups.TryGetValue(pKingdom.id,
                    out KingdomNoticeGroup group) ||
                string.IsNullOrEmpty(group.PrimarySignature) ||
                !Assignments.TryGetValue(BuildAssignmentKey(
                    group.PrimarySignature, group.KingdomId),
                    out NoticeAssignments assignments) ||
                assignments.Closing || !assignments.DiscoveryComplete)
                return false;
            pCount = assignments.TargetCityIds.Count;
            return true;
        }

        public static void OnArmyChanged(Kingdom pKingdom, Army pArmy, bool pRosterExpanded)
        {
            if (pArmy?.data == null) return;
            AWArmyMarchService.ReleaseCompletedDeploymentTrailIfUnused(pArmy);
            if (pKingdom?.data == null ||
                !KingdomNoticeGroups.TryGetValue(pKingdom.id, out KingdomNoticeGroup group)) return;
            foreach (SideNotice side in group.Notices.Values)
            {
                NoticeAssignments assignments = ResolveNoticeAssignments(side);
                if (assignments == null || assignments.Closing) continue;
                if (pRosterExpanded)
                    assignments.ArrivedArmyIds.Remove(pArmy.id);
                RegisterOrRefreshArmy(assignments, pArmy);
            }
        }

        public static void OnArmyInvalidated(Kingdom pKingdom, long pArmyId)
        {
            if (pArmyId < 0) return;
            AWArmyMarchService.ClearArmy(pArmyId);
            if (pKingdom?.data == null ||
                !KingdomNoticeGroups.TryGetValue(pKingdom.id, out KingdomNoticeGroup group)) return;
            foreach (SideNotice side in group.Notices.Values)
            {
                if (!Assignments.TryGetValue(side.AssignmentKey,
                        out NoticeAssignments assignments)) continue;
                RemoveArmyFromProjection(assignments, pArmyId);
            }
        }

        public static void OnKingdomEnteredWar(Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                !KingdomNoticeGroups.TryGetValue(pKingdom.id,
                    out KingdomNoticeGroup group)) return;
            foreach (SideNotice side in group.Notices.Values)
                BeginAssignmentCleanup(side.AssignmentKey,
                    restoreJobs: true);
        }

        public static bool TryPrepareMove(Actor pActor, out WorldTile pTarget)
        {
            pTarget = null;
            if (pActor?.data == null || pActor.isRekt()) return false;
            pActor.data.get(LineageKeys.DEPLOYMENT_NOTICE_SIGNATURE, out string signature, "");
            pActor.data.get(LineageKeys.DEPLOYMENT_TARGET_X, out int x, -1);
            pActor.data.get(LineageKeys.DEPLOYMENT_TARGET_Y, out int y, -1);
            if (string.IsNullOrEmpty(signature) ||
                !Assignments.TryGetValue(signature, out NoticeAssignments assignments) ||
                assignments.Closing) return false;
            pTarget = ResolveTargetTile(assignments, x, y);
            return pTarget?.data != null;
        }

        public static void MarkArrival(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.DEPLOYMENT_TARGET_X, out int x, -1);
            pActor.data.get(LineageKeys.DEPLOYMENT_TARGET_Y, out int y, -1);
            WorldTile tile = pActor.current_tile;
            bool arrived = tile != null && x >= 0 && y >= 0 &&
                           Math.Abs(tile.x - x) <= ArrivalRadius && Math.Abs(tile.y - y) <= ArrivalRadius;
            pActor.data.get(LineageKeys.DEPLOYMENT_ARRIVED, out bool previous, false);
            if (previous != arrived) pActor.data.set(LineageKeys.DEPLOYMENT_ARRIVED, arrived);
            if (!arrived || pActor.army?.data == null) return;
            bool actorIsCaptain;
            try { actorIsCaptain = pActor.army.getCaptain() == pActor; }
            catch { actorIsCaptain = false; }
            if (!ArmyDeploymentRules.ShouldMarkArmyArrived(arrived, actorIsCaptain)) return;
            pActor.data.get(LineageKeys.DEPLOYMENT_NOTICE_SIGNATURE, out string signature, "");
            if (!string.IsNullOrEmpty(signature) &&
                Assignments.TryGetValue(signature, out NoticeAssignments assignments) &&
                !assignments.Closing)
            {
                WorldTile target = ResolveTargetTile(assignments, x, y);
                if (target?.data == null) return;
                if (!assignments.ArrivedArmyIds.Add(pActor.army.id)) return;
                RegisterOrRefreshArmy(assignments, pActor.army);
            }
        }

        public static bool HasActiveAssignment(Actor pActor)
        {
            return TryGetActiveAssignmentKey(pActor, out _);
        }

        public static bool TryGetActiveAssignmentKey(Actor pActor,
            out string pAssignmentKey)
        {
            pAssignmentKey = "";
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.DEPLOYMENT_NOTICE_SIGNATURE,
                out string signature, "");
            if (string.IsNullOrEmpty(signature) ||
                !Assignments.TryGetValue(signature,
                    out NoticeAssignments assignments) ||
                assignments.Closing) return false;
            pAssignmentKey = signature;
            return true;
        }

        public static void QueueFormationReview(Actor pActor)
        {
            return;
        }

        public static void CancelNotice(string pSignature, bool restoreJobs)
        {
            if (string.IsNullOrEmpty(pSignature)) return;
            RemoveNoticeFromGroup(pSignature, restoreJobs);
        }

        public static void ReleaseActor(Actor pActor, bool restoreJob)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.DEPLOYMENT_NOTICE_SIGNATURE, out string signature, "");
            if (!string.IsNullOrEmpty(signature) &&
                Assignments.TryGetValue(signature, out NoticeAssignments assignments))
            {
                assignments.ActorIds.Remove(pActor.data.id);
                if (pActor.army?.data != null) assignments.ArrivedArmyIds.Remove(pActor.army.id);
            }
            ClearActorAssignment(pActor, restoreJob);
        }

        public static void ClearRuntime()
        {
            Assignments.Clear();
            KingdomNoticeGroups.Clear();
            KingdomIdByAssignmentKey.Clear();
            AssignmentKeysByNotice.Clear();
        }

        private static void RegisterNotice(WarNoticeState pState)
        {
            if (pState == null || string.IsNullOrEmpty(pState.Signature) ||
                pState.AttackerId < 0 || pState.DefenderId < 0 ||
                pState.AttackerId == pState.DefenderId) return;
            RegisterNoticeSide(pState, pState.AttackerId,
                pState.DefenderId, pIsDefenderSide: false);
            RegisterNoticeSide(pState, pState.DefenderId,
                pState.AttackerId, pIsDefenderSide: true);
        }

        private static void RegisterNoticeSide(WarNoticeState pState,
            long pOwnerKingdomId, long pOpponentKingdomId,
            bool pIsDefenderSide)
        {
            if (pOwnerKingdomId < 0 || pOpponentKingdomId < 0 ||
                !ArmyDeploymentRules.ShouldCreateSideProjection(
                    pOwnerKingdomId == pState.AttackerId,
                    pOwnerKingdomId == pState.DefenderId)) return;
            string assignmentKey = BuildAssignmentKey(pState.Signature, pOwnerKingdomId);
            if (KingdomIdByAssignmentKey.TryGetValue(assignmentKey,
                    out long previousKingdomId) &&
                previousKingdomId != pOwnerKingdomId)
                RemoveProjection(pState.Signature, assignmentKey,
                    restoreJobs: true);

            if (!KingdomNoticeGroups.TryGetValue(pOwnerKingdomId,
                    out KingdomNoticeGroup group))
            {
                group = new KingdomNoticeGroup(pOwnerKingdomId);
                KingdomNoticeGroups[pOwnerKingdomId] = group;
            }

            if (group.Notices.TryGetValue(pState.Signature,
                    out SideNotice previous))
                group.Priorities.Remove(new NoticePriority(previous.State));
            group.Notices[pState.Signature] = new SideNotice
            {
                State = pState,
                AssignmentKey = assignmentKey,
                OwnerKingdomId = pOwnerKingdomId,
                OpponentKingdomId = pOpponentKingdomId,
                IsDefenderSide = pIsDefenderSide
            };
            group.Priorities.Add(new NoticePriority(pState));
            KingdomIdByAssignmentKey[assignmentKey] = pOwnerKingdomId;
            if (!AssignmentKeysByNotice.TryGetValue(pState.Signature,
                    out HashSet<string> assignmentKeys))
            {
                assignmentKeys = new HashSet<string>(StringComparer.Ordinal);
                AssignmentKeysByNotice[pState.Signature] = assignmentKeys;
            }
            assignmentKeys.Add(assignmentKey);

            string nextPrimary = group.Priorities.Count > 0
                ? group.Priorities.Min.Signature
                : "";
            group.PrimarySignature = nextPrimary;
            ResolveNoticeAssignments(group.Notices[pState.Signature]);
            RebalanceGroupAssignments(group);
        }

        private static NoticeAssignments ResolveDeclarationAssignments(
            WarNoticeState pState, long pOwnerKingdomId)
        {
            if (pState == null ||
                !KingdomNoticeGroups.TryGetValue(pOwnerKingdomId,
                    out KingdomNoticeGroup group)) return null;
            if (!group.Notices.TryGetValue(pState.Signature,
                    out SideNotice side)) return null;
            ResolveNoticeAssignments(side);
            string assignmentKey = BuildAssignmentKey(pState.Signature, pOwnerKingdomId);
            bool exists = Assignments.TryGetValue(assignmentKey,
                out NoticeAssignments assignments);
            return ArmyDeploymentRules.CanUseDeclarationProjection(
                    pState.Signature, group.PrimarySignature, exists,
                    assignments?.Closing == true)
                ? assignments
                : null;
        }

        private static NoticeAssignments ResolvePrimaryAssignments(
            KingdomNoticeGroup pGroup)
        {
            if (pGroup == null || string.IsNullOrEmpty(pGroup.PrimarySignature) ||
                !pGroup.Notices.TryGetValue(pGroup.PrimarySignature,
                    out SideNotice primary))
                return null;

            return ResolveNoticeAssignments(primary);
        }

        private static NoticeAssignments ResolveNoticeAssignments(
            SideNotice pSide)
        {
            if (pSide?.State == null ||
                string.IsNullOrEmpty(pSide.AssignmentKey)) return null;

            Kingdom owner = ResolveKingdom(pSide.OwnerKingdomId);
            bool ownerAlreadyAtWar = owner?.data != null &&
                                     MilitaryEmergencyService.
                                         TryGetActiveWarId(owner, out _);
            if (ArmyDeploymentRules.ShouldBypassPrewarDeployment(
                    ownerAlreadyAtWar))
            {
                BeginAssignmentCleanup(pSide.AssignmentKey,
                    restoreJobs: true);
                return null;
            }

            bool created = !Assignments.TryGetValue(
                pSide.AssignmentKey, out NoticeAssignments assignments);
            if (created)
            {
                assignments = new NoticeAssignments();
                ApplySide(assignments, pSide);
                Assignments[pSide.AssignmentKey] = assignments;
            }
            else
            {
                if (assignments.Closing) return null;
                ApplySide(assignments, pSide);
            }
            if (created) ScheduleDiscovery(pSide.AssignmentKey);
            return assignments;
        }

        private static void RebalanceGroupAssignments(
            KingdomNoticeGroup pGroup)
        {
            if (pGroup == null) return;
            foreach (SideNotice side in pGroup.Notices.Values)
            {
                NoticeAssignments assignments =
                    ResolveNoticeAssignments(side);
                if (assignments == null || assignments.Closing) continue;
                if (!assignments.DiscoveryComplete)
                    ScheduleDiscovery(assignments.AssignmentKey);
                else
                    ScheduleArmyReview(assignments.AssignmentKey,
                        pRestart: true);
            }
        }

        private static void RemoveNoticeFromGroup(string pSignature, bool restoreJobs)
        {
            if (!AssignmentKeysByNotice.TryGetValue(pSignature,
                    out HashSet<string> assignmentKeys) ||
                assignmentKeys.Count == 0)
            {
                BeginAssignmentCleanup(pSignature, restoreJobs);
                return;
            }
            string[] keys = new string[assignmentKeys.Count];
            assignmentKeys.CopyTo(keys);
            AssignmentKeysByNotice.Remove(pSignature);
            for (int i = 0; i < keys.Length; i++)
                RemoveProjection(pSignature, keys[i], restoreJobs);
        }

        private static void RemoveProjection(string pSignature,
            string pAssignmentKey, bool restoreJobs)
        {
            if (!KingdomIdByAssignmentKey.TryGetValue(pAssignmentKey,
                    out long kingdomId) ||
                !KingdomNoticeGroups.TryGetValue(kingdomId,
                    out KingdomNoticeGroup group))
            {
                BeginAssignmentCleanup(pAssignmentKey, restoreJobs);
                return;
            }

            if (group.Notices.TryGetValue(pSignature,
                    out SideNotice side))
            {
                group.Priorities.Remove(new NoticePriority(side.State));
                group.Notices.Remove(pSignature);
            }
            KingdomIdByAssignmentKey.Remove(pAssignmentKey);

            bool wasPrimary = string.Equals(group.PrimarySignature,
                pSignature, StringComparison.Ordinal);
            BeginAssignmentCleanup(pAssignmentKey, restoreJobs);

            if (group.Notices.Count == 0 || group.Priorities.Count == 0)
            {
                KingdomNoticeGroups.Remove(kingdomId);
                return;
            }

            if (wasPrimary)
                group.PrimarySignature = group.Priorities.Min.Signature;
            RebalanceGroupAssignments(group);
        }

        private static void ApplySide(NoticeAssignments pAssignments,
            SideNotice pSide)
        {
            if (pAssignments == null || pSide == null) return;
            pAssignments.State = pSide.State;
            pAssignments.AssignmentKey = pSide.AssignmentKey;
            pAssignments.OwnerKingdomId = pSide.OwnerKingdomId;
            pAssignments.OpponentKingdomId = pSide.OpponentKingdomId;
            pAssignments.IsDefenderSide = pSide.IsDefenderSide;
        }

        private static string BuildAssignmentKey(string pNoticeSignature,
            long pOwnerKingdomId)
        {
            return (pNoticeSignature ?? "") + "|side:" +
                   pOwnerKingdomId;
        }

        private static void BeginAssignmentCleanup(string pSignature, bool restoreJobs)
        {
            if (string.IsNullOrEmpty(pSignature) ||
                !Assignments.TryGetValue(pSignature, out NoticeAssignments assignments) ||
                assignments.Closing) return;
            assignments.Closing = true;
            assignments.RestoreJobs = restoreJobs;
            ScheduleCleanup(pSignature);
        }

        private static bool IsRequiredArmy(Army pArmy, Kingdom pDefender, out int pLiving)
        {
            pLiving = 0;
            if (pArmy?.data == null || !pArmy.isAlive() || IsRoyalGuardArmy(pArmy)) return false;
            try { if (pArmy.getKingdom() != pDefender) return false; }
            catch { return false; }
            pLiving = pArmy.countUnits();
            return pLiving > 0;
        }

        private static bool IsReady(Army pArmy, int pLiving)
        {
            if (pArmy?.data == null || pLiving <= 0) return false;
            if (AWArmyService.IsRoleArmy(pArmy, AWArmyRole.SlaveArmy))
                return TemporarySlaveVanguardService.IsDeploymentReady(pArmy);
            if (!AWArmyService.IsSpecialArmy(pArmy))
            {
                try
                {
                    City city = pArmy.hasCity() ? pArmy.getCity() : null;
                    if (city?.data != null && city.getArmy() == pArmy)
                        return city.isOkToSendArmy();
                }
                catch { }
            }
            Actor captain = null;
            try { captain = pArmy.getCaptain(); } catch { }
            return captain?.data != null &&
                   !captain.isRekt() && captain.isAlive();
        }

        private static bool IsArmyArrived(NoticeAssignments pAssignments, Army pArmy)
        {
            if (pArmy?.data == null ||
                !pAssignments.TargetCityByArmy.ContainsKey(pArmy.id) ||
                !pAssignments.TargetTileByArmy.ContainsKey(pArmy.id))
                return false;
            return pAssignments.ArrivedArmyIds.Contains(pArmy.id);
        }

        private static FrontierTarget ResolveAssignedTarget(
            NoticeAssignments pAssignments, Army pArmy)
        {
            if (pAssignments.TargetCityByArmy.TryGetValue(pArmy.id,
                    out long cityId) &&
                pAssignments.TargetTileByArmy.TryGetValue(pArmy.id,
                    out int tileId))
            {
                var existing = new FrontierTarget
                {
                    CityId = cityId,
                    TileId = tileId
                };
                if (ResolveFrontierTarget(pAssignments, existing) != null)
                    return existing;
            }
            int index = ArmyDeploymentRules.StableFrontierIndex(
                pArmy.id, pAssignments.FrontierTargets.Count);
            if (index < 0) return null;
            FrontierTarget target = pAssignments.FrontierTargets[index];
            if (ResolveFrontierTarget(pAssignments, target) == null)
                return null;
            pAssignments.TargetCityByArmy[pArmy.id] = target.CityId;
            pAssignments.TargetTileByArmy[pArmy.id] = target.TileId;
            return target;
        }

        private static void AssignArmy(NoticeAssignments pAssignments,
            Army pArmy, FrontierTarget pTarget)
        {
            if (pAssignments?.State == null || pArmy?.data == null ||
                pTarget == null || ResolveFrontierTarget(pAssignments,
                    pTarget) == null) return;
            bool targetChanged = !pAssignments.AssignedTargetTileByArmy.
                                     TryGetValue(pArmy.id,
                                         out int assignedTargetId) ||
                                 assignedTargetId != pTarget.TileId;
            pAssignments.AssignedTargetTileByArmy[pArmy.id] =
                pTarget.TileId;
            if (targetChanged)
            {
                pAssignments.NextActorIndexByArmy[pArmy.id] = 0;
                pAssignments.ArrivedArmyIds.Remove(pArmy.id);
            }
            else if (!pAssignments.NextActorIndexByArmy.ContainsKey(pArmy.id))
            {
                pAssignments.NextActorIndexByArmy[pArmy.id] = 0;
            }
            ScheduleArmyAssignment(pAssignments.AssignmentKey, pArmy.id,
                pTarget.CityId, pTarget.TileId);
        }

        private static void ScheduleArmyAssignment(string pSignature,
            long pArmyId, long pTargetCityId, int pTargetTileId)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("deployment_assign:" + (pSignature ?? ""), pArmyId),
                DeferredWorkClass.Runtime,
                () => AssignArmyBatch(pSignature, pArmyId, pTargetCityId,
                    pTargetTileId));
        }

        private static void AssignArmyBatch(string pSignature, long pArmyId,
            long pTargetCityId, int pTargetTileId)
        {
            if (string.IsNullOrEmpty(pSignature) ||
                !Assignments.TryGetValue(pSignature, out NoticeAssignments assignments) ||
                assignments.Closing) return;
            Army army = ResolveArmy(pArmyId);
            City target = ResolveCity(pTargetCityId);
            WorldTile targetTile = FindTile(pTargetTileId);
            if (army?.data == null || target?.data == null ||
                target.isRekt() || targetTile?.data == null ||
                target.kingdom?.id != assignments.OwnerKingdomId ||
                targetTile.zone?.city != target) return;
            Actor captain = null;
            try { captain = army.getCaptain(); } catch { }
            ArmyRtsMode mode = ArmyRtsRuntimeMode.Current;
            bool useFormationMovement =
                ArmyDeploymentRules.ShouldUseFormationQuorum(
                    mode);
            int count;
            try { count = army.units.Count; }
            catch { count = 0; }
            if (!useFormationMovement)
            {
                if (ArmyDeploymentRules.ShouldAssignDeploymentActor(
                        mode, actorIsCaptain: true))
                    AssignDeploymentActor(assignments, captain, pSignature,
                        pTargetCityId, targetTile,
                        WarMobilizationContent.DeploymentJobId);
                assignments.NextActorIndexByArmy[pArmyId] = count;
                return;
            }

            WorldTile deploymentAnchor = ResolveDeploymentAnchor(army,
                targetTile, out bool deploymentEligible);
            ArmyFormationService.SetAnchor(army, deploymentAnchor,
                pDeploymentEligible: deploymentEligible);
            assignments.NextActorIndexByArmy.TryGetValue(pArmyId,
                out int cursor);
            if (cursor < 0 || cursor > count) cursor = 0;
            int end = Math.Min(count, cursor + ActorMutationBatchSize);
            for (int i = cursor; i < end; i++)
            {
                Actor actor = null;
                try { actor = army.units[i]; }
                catch { }
                if (actor?.data == null || actor.isRekt() ||
                    !actor.isAlive() || !actor.isWarrior() ||
                    RoyalGuardService.IsRoyalGuard(actor)) continue;
                bool actorIsCaptain = actor == captain;
                if (!ArmyDeploymentRules.ShouldAssignDeploymentActor(
                        mode, actorIsCaptain)) continue;
                string jobId = ArmyDeploymentRules.
                    ShouldUseFormationFollowerJob(
                        mode, actorIsCaptain)
                    ? ArmyRtsContent.FollowerJobId
                    : WarMobilizationContent.DeploymentJobId;
                AssignDeploymentActor(assignments, actor, pSignature,
                    pTargetCityId, targetTile, jobId);
            }
            assignments.NextActorIndexByArmy[pArmyId] = end;
            if (end < count)
                ScheduleArmyAssignment(pSignature, pArmyId,
                    pTargetCityId, pTargetTileId);
        }

        private static void AssignDeploymentActor(
            NoticeAssignments pAssignments, Actor pActor,
            string pSignature, long pTargetCityId, WorldTile pTargetTile,
            string pJobId)
        {
            if (pAssignments == null || pActor?.data == null ||
                pTargetTile?.data == null ||
                pActor.isRekt() || !pActor.isAlive() ||
                !pActor.isWarrior() ||
                RoyalGuardService.IsRoyalGuard(pActor)) return;
            pActor.data.get(LineageKeys.DEPLOYMENT_NOTICE_SIGNATURE,
                out string currentSignature, "");
            pActor.data.get(LineageKeys.DEPLOYMENT_TARGET_CITY_ID,
                out long currentCityId, -1L);
            pActor.data.get(LineageKeys.DEPLOYMENT_TARGET_X,
                out int currentX, -1);
            pActor.data.get(LineageKeys.DEPLOYMENT_TARGET_Y,
                out int currentY, -1);
            bool reset = ArmyDeploymentRules.ShouldResetAssignment(
                currentSignature, pSignature, currentCityId,
                pTargetCityId) || currentX != pTargetTile.x ||
                                 currentY != pTargetTile.y;
            pActor.data.set(LineageKeys.DEPLOYMENT_NOTICE_SIGNATURE,
                pSignature);
            pActor.data.set(LineageKeys.DEPLOYMENT_TARGET_CITY_ID,
                pTargetCityId);
            pActor.data.set(LineageKeys.DEPLOYMENT_TARGET_X,
                pTargetTile.x);
            pActor.data.set(LineageKeys.DEPLOYMENT_TARGET_Y,
                pTargetTile.y);
            if (reset)
                pActor.data.set(LineageKeys.DEPLOYMENT_ARRIVED, false);
            pAssignments.ActorIds.Add(pActor.data.id);
            try
            {
                if (pActor.ai?.job?.id != pJobId)
                    pActor.ai?.setJob(pJobId);
            }
            catch { }
        }

        private static void ScheduleCleanup(string pSignature)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "deployment_cleanup:" + (pSignature ?? ""), DeferredWorkClass.Runtime,
                () => CleanupBatch(pSignature));
        }

        private static void CleanupBatch(string pSignature)
        {
            if (string.IsNullOrEmpty(pSignature) ||
                !Assignments.TryGetValue(pSignature, out NoticeAssignments assignments) ||
                !assignments.Closing) return;
            long[] batch = assignments.CleanupBuffer;
            int count = 0;
            foreach (long actorId in assignments.ActorIds)
            {
                batch[count++] = actorId;
                if (count >= batch.Length) break;
            }
            for (int i = 0; i < count; i++)
            {
                assignments.ActorIds.Remove(batch[i]);
                Actor actor = ResolveActor(batch[i]);
                if (actor?.data != null)
                {
                    actor.data.get(LineageKeys.DEPLOYMENT_NOTICE_SIGNATURE,
                        out string currentSignature, "");
                    if (ArmyDeploymentRules.ShouldClearForClosingNotice(
                            currentSignature, pSignature))
                    {
                        AWArmyMarchService.ClearRetainedDeploymentTrail(
                            actor.army, pSignature);
                        ClearActorAssignment(actor, assignments.RestoreJobs);
                    }
                }
            }
            if (assignments.ActorIds.Count > 0)
            {
                ScheduleCleanup(pSignature);
                return;
            }
            Assignments.Remove(pSignature);
        }

        private static void ScheduleDiscovery(string pSignature)
        {
            if (string.IsNullOrEmpty(pSignature)) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "deployment_discovery:" + (pSignature ?? ""), DeferredWorkClass.Runtime,
                () => DiscoverCityBatch(pSignature));
        }

        private static void DiscoverCityBatch(string pSignature)
        {
            if (string.IsNullOrEmpty(pSignature) ||
                !Assignments.TryGetValue(pSignature, out NoticeAssignments assignments) ||
                assignments.Closing || assignments.DiscoveryComplete) return;
            Kingdom owner = ResolveKingdom(assignments.OwnerKingdomId);
            Kingdom opponent = ResolveKingdom(assignments.OpponentKingdomId);
            if (owner?.data == null || owner.isRekt() ||
                opponent?.data == null || opponent.isRekt()) return;
            City focus = ResolveOpponentFocus(assignments, opponent);

            int cityCount = owner.cities.Count;
            if (assignments.CityDiscoveryCursor < 0 || assignments.CityDiscoveryCursor > cityCount)
                assignments.CityDiscoveryCursor = 0;
            int end = Math.Min(cityCount, assignments.CityDiscoveryCursor +
                                          ArmyDeploymentRules.MaxCitiesDiscoveredPerWorkItem);
            for (int i = assignments.CityDiscoveryCursor; i < end; i++)
            {
                City city = owner.cities[i];
                if (city?.data == null || city.isRekt() ||
                    city.kingdom != owner) continue;
                if (assignments.FallbackCityId < 0 || city == owner.capital)
                    assignments.FallbackCityId = city.id;
                DiscoverFrontierTargets(assignments, city, opponent,
                    focus);
                if (city.hasArmy()) RegisterOrRefreshArmy(assignments, city.getArmy());
            }
            assignments.CityDiscoveryCursor = end;
            if (end < cityCount)
            {
                ScheduleDiscovery(pSignature);
                return;
            }

            AddIndexedRoleArmies(assignments, owner, AWArmyRole.BorderArmy);
            AddIndexedRoleArmies(assignments, owner, AWArmyRole.SlaveArmy);
            if (assignments.FrontierTargets.Count == 0)
            {
                int fallbackTileId = assignments.FallbackCoastTileId >= 0
                    ? assignments.FallbackCoastTileId
                    : assignments.FallbackApproachTileId;
                long fallbackCityId = assignments.FallbackCoastTileId >= 0
                    ? assignments.FallbackCoastCityId
                    : assignments.FallbackApproachCityId;
                AddFrontierTarget(assignments, fallbackCityId,
                    fallbackTileId);
            }
            assignments.FrontierTargets.Sort((left, right) =>
                left.TileId.CompareTo(right.TileId));
            assignments.DiscoveryComplete = true;
            ScheduleArmyReview(pSignature, pRestart: true);
        }

        private static void AddTargetCity(NoticeAssignments pAssignments, City pCity)
        {
            if (pAssignments == null || pCity?.data == null || pCity.isRekt() ||
                !pAssignments.TargetCitySet.Add(pCity.id)) return;
            pAssignments.TargetCityIds.Add(pCity.id);
        }

        private static void DiscoverFrontierTargets(
            NoticeAssignments pAssignments, City pCity,
            Kingdom pOpponent, City pFocus)
        {
            if (pAssignments == null || pCity?.data == null ||
                pOpponent?.data == null) return;
            try { pCity.recalculateNeighbourZones(); }
            catch { }
            try
            {
                foreach (TileZone zone in pCity.border_zones)
                {
                    if (zone?.tiles == null || zone.city != pCity) continue;
                    foreach (WorldTile tile in zone.tiles)
                    {
                        if (tile?.data == null || tile.Type == null) continue;
                        bool ownedBySide = tile.zone?.city == pCity &&
                                           pCity.kingdom?.id ==
                                           pAssignments.OwnerKingdomId;
                        bool touchesOpponent = TouchesOpponentLand(tile,
                            pOpponent);
                        if (ArmyDeploymentRules.IsFacingFrontierTile(
                                ownedBySide, tile.Type.ground,
                                tile.Type.liquid || tile.Type.ocean,
                                tile.Type.lava, tile.Type.block,
                                touchesOpponent))
                            TryAddFacingBorderTarget(pAssignments, pCity,
                                tile);
                        ObserveFallbackTarget(pAssignments, pCity, tile,
                            pFocus);
                    }
                }
            }
            catch { }
        }

        private static void TryAddFacingBorderTarget(
            NoticeAssignments pAssignments, City pCity,
            WorldTile pTile)
        {
            AddFrontierTarget(pAssignments, pCity?.id ?? -1L,
                pTile?.data?.tile_id ?? -1);
        }

        private static void AddFrontierTarget(
            NoticeAssignments pAssignments, long pCityId, int pTileId)
        {
            if (pAssignments == null || pCityId < 0 || pTileId < 0 ||
                !pAssignments.FrontierTileSet.Add(pTileId)) return;
            City city = ResolveCity(pCityId);
            WorldTile tile = FindTile(pTileId);
            if (city?.data == null || city.isRekt() ||
                city.kingdom?.id != pAssignments.OwnerKingdomId ||
                tile?.data == null || tile.zone?.city != city)
            {
                pAssignments.FrontierTileSet.Remove(pTileId);
                return;
            }
            pAssignments.FrontierTargets.Add(new FrontierTarget
            {
                CityId = pCityId,
                TileId = pTileId
            });
            AddTargetCity(pAssignments, city);
        }

        private static void ObserveFallbackTarget(
            NoticeAssignments pAssignments, City pCity,
            WorldTile pTile, City pFocus)
        {
            if (pAssignments == null || pCity?.data == null ||
                pTile?.data == null || pTile.Type == null ||
                pTile.zone?.city != pCity || !pTile.Type.ground ||
                pTile.Type.liquid || pTile.Type.ocean || pTile.Type.lava ||
                pTile.Type.block) return;
            long distance = TileDistanceSquared(pTile, pFocus?.getTile());
            int tileId = pTile.data.tile_id;
            if (distance < pAssignments.FallbackApproachDistance ||
                distance == pAssignments.FallbackApproachDistance &&
                tileId < pAssignments.FallbackApproachTileId)
            {
                pAssignments.FallbackApproachDistance = distance;
                pAssignments.FallbackApproachTileId = tileId;
                pAssignments.FallbackApproachCityId = pCity.id;
            }
            if (!TouchesLiquid(pTile)) return;
            if (distance < pAssignments.FallbackCoastDistance ||
                distance == pAssignments.FallbackCoastDistance &&
                tileId < pAssignments.FallbackCoastTileId)
            {
                pAssignments.FallbackCoastDistance = distance;
                pAssignments.FallbackCoastTileId = tileId;
                pAssignments.FallbackCoastCityId = pCity.id;
            }
        }

        private static bool TouchesOpponentLand(WorldTile pTile,
            Kingdom pOpponent)
        {
            if (pTile?.neighboursAll == null || pOpponent?.data == null)
                return false;
            foreach (WorldTile neighbour in pTile.neighboursAll)
            {
                if (neighbour?.Type == null || !neighbour.Type.ground ||
                    neighbour.Type.liquid || neighbour.Type.ocean ||
                    neighbour.Type.lava || neighbour.Type.block) continue;
                try
                {
                    if (neighbour.zone?.city?.kingdom == pOpponent)
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static bool TouchesLiquid(WorldTile pTile)
        {
            if (pTile?.neighboursAll == null) return false;
            foreach (WorldTile neighbour in pTile.neighboursAll)
                if (neighbour?.Type != null &&
                    (neighbour.Type.liquid || neighbour.Type.ocean))
                    return true;
            return false;
        }

        private static void ScheduleArmyReview(string pSignature, bool pRestart)
        {
            if (string.IsNullOrEmpty(pSignature) ||
                !Assignments.TryGetValue(pSignature, out NoticeAssignments assignments) ||
                assignments.Closing) return;
            if (pRestart) assignments.ArmyReviewCursor = 0;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "deployment_review:" + (pSignature ?? ""), DeferredWorkClass.Runtime,
                () => ReviewArmyBatch(pSignature));
        }

        private static void ScheduleFormationReview(string pSignature,
            long pArmyId)
        {
            if (string.IsNullOrEmpty(pSignature) || pArmyId < 0L)
                return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "deployment_formation:" + (pSignature ?? ""),
                    pArmyId), DeferredWorkClass.Runtime,
                () => ReviewFormationArmy(pSignature, pArmyId));
        }

        private static void ReviewFormationArmy(string pSignature,
            long pArmyId)
        {
            if (string.IsNullOrEmpty(pSignature) ||
                !Assignments.TryGetValue(pSignature,
                    out NoticeAssignments assignments) ||
                assignments.Closing) return;
            RegisterOrRefreshArmy(assignments, ResolveArmy(pArmyId));
        }

        private static void ReviewArmyBatch(string pSignature)
        {
            if (string.IsNullOrEmpty(pSignature) ||
                !Assignments.TryGetValue(pSignature, out NoticeAssignments assignments) ||
                assignments.Closing) return;
            if (!assignments.DiscoveryComplete)
            {
                ScheduleDiscovery(pSignature);
                return;
            }

            int count = assignments.RequiredArmyIds.Count;
            if (assignments.ArmyReviewCursor < 0 ||
                assignments.ArmyReviewCursor >= count)
                assignments.ArmyReviewCursor = 0;
            int end = Math.Min(count, assignments.ArmyReviewCursor +
                                      ArmyDeploymentRules.MaxArmiesReviewedPerWorkItem);
            for (int i = assignments.ArmyReviewCursor; i < end; i++)
                RegisterOrRefreshArmy(assignments, ResolveArmy(assignments.RequiredArmyIds[i]));
            assignments.ArmyReviewCursor = end;
            if (end < assignments.RequiredArmyIds.Count)
                ScheduleArmyReview(pSignature, pRestart: false);
        }

        private static void RegisterOrRefreshArmy(NoticeAssignments pAssignments, Army pArmy)
        {
            if (pAssignments?.State == null) return;
            Kingdom owner = ResolveKingdom(pAssignments.OwnerKingdomId);
            ObserveFormation(pAssignments, pArmy);
            if (!IsRequiredArmy(pArmy, owner, out int living))
            {
                if (pArmy?.data != null)
                    RemoveArmyFromProjection(pAssignments, pArmy.id);
                return;
            }
            if (!OwnsArmyForNotice(pAssignments, pArmy.id))
            {
                RemoveArmyFromProjection(pAssignments, pArmy.id);
                return;
            }

            if (pAssignments.RequiredArmySet.Add(pArmy.id))
            {
                pAssignments.ArmyOrderById[pArmy.id] = pAssignments.RequiredArmyIds.Count;
                pAssignments.RequiredArmyIds.Add(pArmy.id);
            }

            bool ready = IsReady(pArmy, living);
            if (ArmyDeploymentRules.CanAssignPrewarDeployment(living > 0,
                    pAssignments.DiscoveryComplete))
            {
                FrontierTarget target = ResolveAssignedTarget(pAssignments,
                    pArmy);
                if (target != null) AssignArmy(pAssignments, pArmy, target);
            }
            bool arrived = ready && IsArmyArrived(pAssignments, pArmy);
            if (ArmyDeploymentRules.BlocksDeclarationGateForSide(
                    pAssignments.IsDefenderSide, living > 0,
                    IsRoyalGuardArmy(pArmy), ready, arrived))
                pAssignments.BlockingArmyIds.Add(pArmy.id);
            else
                pAssignments.BlockingArmyIds.Remove(pArmy.id);
        }

        private static bool OwnsArmyForNotice(
            NoticeAssignments pAssignments, long pArmyId)
        {
            if (pAssignments?.State == null ||
                !KingdomNoticeGroups.TryGetValue(
                    pAssignments.OwnerKingdomId,
                    out KingdomNoticeGroup group) ||
                group.Priorities.Count == 0) return false;
            int selected = ArmyDeploymentRules.StableNoticeIndex(
                pArmyId, group.Priorities.Count);
            int index = 0;
            foreach (NoticePriority priority in group.Priorities)
            {
                if (index++ != selected) continue;
                return string.Equals(priority.Signature,
                    pAssignments.State.Signature,
                    StringComparison.Ordinal);
            }
            return false;
        }

        private static void RemoveArmyFromProjection(
            NoticeAssignments pAssignments, long pArmyId)
        {
            if (pAssignments == null || pArmyId < 0L) return;
            pAssignments.BlockingArmyIds.Remove(pArmyId);
            pAssignments.ArrivedArmyIds.Remove(pArmyId);
            pAssignments.TargetCityByArmy.Remove(pArmyId);
            pAssignments.TargetTileByArmy.Remove(pArmyId);
            pAssignments.AssignedTargetTileByArmy.Remove(pArmyId);
            pAssignments.NextActorIndexByArmy.Remove(pArmyId);
        }

        private static void ObserveFormation(NoticeAssignments pAssignments,
            Army pArmy)
        {
            if (pAssignments == null || pArmy?.data == null) return;
            WorldTile target = null;
            if (pAssignments.AssignedTargetTileByArmy.TryGetValue(pArmy.id,
                    out int targetTileId))
                target = FindTile(targetTileId);
            WorldTile anchor = ResolveDeploymentAnchor(pArmy, target,
                out bool deploymentEligible);
            ArmyFormationService.SetAnchor(pArmy, anchor,
                pDeploymentEligible: deploymentEligible);
        }

        private static WorldTile ResolveDeploymentAnchor(Army pArmy,
            WorldTile pFrontierTarget, out bool pDeploymentEligible)
        {
            pDeploymentEligible = false;
            Actor captain = null;
            try { captain = pArmy?.getCaptain(); }
            catch { }
            WorldTile captainTile = captain?.current_tile;
            bool captainAtTarget = captainTile?.data != null &&
                                   pFrontierTarget?.data != null &&
                                   Math.Abs(captainTile.x -
                                            pFrontierTarget.x) <=
                                   ArrivalRadius &&
                                   Math.Abs(captainTile.y -
                                            pFrontierTarget.y) <=
                                   ArrivalRadius;
            if (ArmyDeploymentRules.ShouldUseFrontierAnchor(
                    captainAtTarget,
                    pFrontierTarget?.data != null))
            {
                pDeploymentEligible = true;
                return pFrontierTarget;
            }
            return captainTile ?? pFrontierTarget;
        }

        private static void AddIndexedRoleArmies(NoticeAssignments pAssignments,
            Kingdom pKingdom, string pRole)
        {
            foreach (Army army in AWArmyService.GetRoleArmies(pKingdom, pRole))
                RegisterOrRefreshArmy(pAssignments, army);
        }

        private static City ResolveOpponentFocus(
            NoticeAssignments pAssignments, Kingdom pOpponent)
        {
            City target = ResolveCity(pAssignments?.State?.TargetCityId ??
                                      -1L);
            if (target?.data != null && !target.isRekt() &&
                target.kingdom == pOpponent) return target;
            if (pOpponent?.capital?.data != null &&
                !pOpponent.capital.isRekt()) return pOpponent.capital;
            try
            {
                for (int i = 0; i < pOpponent.cities.Count; i++)
                {
                    City city = pOpponent.cities[i];
                    if (city?.data != null && !city.isRekt() &&
                        city.kingdom == pOpponent) return city;
                }
            }
            catch { }
            return null;
        }

        private static WorldTile ResolveFrontierTarget(
            NoticeAssignments pAssignments, FrontierTarget pTarget)
        {
            if (pAssignments == null || pTarget == null) return null;
            City city = ResolveCity(pTarget.CityId);
            WorldTile tile = FindTile(pTarget.TileId);
            return IsFriendlyWalkableTarget(pAssignments, city, tile)
                ? tile
                : null;
        }

        private static WorldTile ResolveTargetTile(
            NoticeAssignments pAssignments, int pX, int pY)
        {
            if (pAssignments == null || pX < 0 || pY < 0) return null;
            WorldTile tile;
            try { tile = World.world?.GetTile(pX, pY); }
            catch { return null; }
            City city = tile?.zone?.city;
            return IsFriendlyWalkableTarget(pAssignments, city, tile)
                ? tile
                : null;
        }

        private static bool IsFriendlyWalkableTarget(
            NoticeAssignments pAssignments, City pCity, WorldTile pTile)
        {
            return pAssignments != null && pCity?.data != null &&
                   !pCity.isRekt() &&
                   pCity.kingdom?.id == pAssignments.OwnerKingdomId &&
                   pTile?.data != null && pTile.zone?.city == pCity &&
                   pTile.Type != null && pTile.Type.ground &&
                   !pTile.Type.liquid && !pTile.Type.ocean &&
                   !pTile.Type.lava && !pTile.Type.block;
        }

        private static WorldTile FindTile(int pTileId)
        {
            try
            {
                WorldTile[] tiles = World.world?.tiles_list;
                return tiles != null && pTileId >= 0 &&
                       pTileId < tiles.Length
                    ? tiles[pTileId]
                    : null;
            }
            catch { return null; }
        }

        private static long TileDistanceSquared(WorldTile pFirst,
            WorldTile pSecond)
        {
            if (pFirst == null || pSecond == null) return long.MaxValue;
            long x = pFirst.x - pSecond.x;
            long y = pFirst.y - pSecond.y;
            if (Math.Abs(x) > 3_000_000_000L ||
                Math.Abs(y) > 3_000_000_000L) return long.MaxValue;
            long distance = x * x + y * y;
            return distance < 0 ? long.MaxValue : distance;
        }

        private static bool IsRoyalGuardArmy(Army pArmy)
        {
            return AWArmyService.IsRoleArmy(pArmy, AWArmyRole.RoyalGuard) ||
                   RoyalGuardService.IsRoyalGuard(pArmy?.getCaptain());
        }

        private static void ClearActorAssignment(Actor pActor, bool pRestoreJob)
        {
            pActor.data.set(LineageKeys.DEPLOYMENT_NOTICE_SIGNATURE, "");
            pActor.data.set(LineageKeys.DEPLOYMENT_TARGET_CITY_ID, -1L);
            pActor.data.set(LineageKeys.DEPLOYMENT_TARGET_X, -1);
            pActor.data.set(LineageKeys.DEPLOYMENT_TARGET_Y, -1);
            pActor.data.set(LineageKeys.DEPLOYMENT_ARRIVED, false);
            bool restoreJob = ArmyDeploymentRules.ShouldRestoreLegacyJob(
                pRestoreJob,
                ArmyRtsControllerService.OwnsLiveActor(pActor));
            if (!restoreJob || pActor.isRekt() || pActor.ai == null) return;
            try { pActor.ai.setJob(pActor.getNextJob()); } catch { }
        }

        private static Kingdom ResolveKingdom(long pId)
        {
            try { return pId >= 0 ? World.world?.kingdoms?.get(pId) : null; }
            catch { return null; }
        }

        private static City ResolveCity(long pId)
        {
            try { return pId >= 0 ? World.world?.cities?.get(pId) : null; }
            catch { return null; }
        }

        private static Actor ResolveActor(long pId)
        {
            try { return pId >= 0 ? World.world?.units?.get(pId) : null; }
            catch { return null; }
        }

        private static Army ResolveArmy(long pId)
        {
            try { return pId >= 0 ? World.world?.armies?.get(pId) : null; }
            catch { return null; }
        }
    }
}
