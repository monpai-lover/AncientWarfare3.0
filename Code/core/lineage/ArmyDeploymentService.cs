using System;
using System.Collections.Generic;
using AncientWarfare3.content;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyDeploymentService
    {
        private const int ArrivalRadius = 3;
        private const int ActorMutationBatchSize = 16;
        private static readonly int[] TargetOffsetX = { 2, -2, 0, 0, 2, -2, 2, -2 };
        private static readonly int[] TargetOffsetY = { 0, 0, 2, -2, 2, 2, -2, -2 };

        private sealed class NoticeAssignments
        {
            public WarNoticeState State;
            public readonly Dictionary<long, long> TargetCityByArmy = new Dictionary<long, long>();
            public readonly HashSet<long> ActorIds = new HashSet<long>();
            public readonly List<long> TargetCityIds = new List<long>();
            public readonly HashSet<long> TargetCitySet = new HashSet<long>();
            public readonly List<long> RequiredArmyIds = new List<long>();
            public readonly HashSet<long> RequiredArmySet = new HashSet<long>();
            public readonly HashSet<long> BlockingArmyIds = new HashSet<long>();
            public readonly Dictionary<long, int> ArmyOrderById = new Dictionary<long, int>();
            public readonly Dictionary<long, int> NextActorIndexByArmy = new Dictionary<long, int>();
            public readonly Dictionary<long, long> AssignedTargetCityByArmy = new Dictionary<long, long>();
            public readonly HashSet<long> ArrivedArmyIds = new HashSet<long>();
            public readonly long[] CleanupBuffer = new long[ActorMutationBatchSize];
            public int CityDiscoveryCursor;
            public int ArmyReviewCursor;
            public long FallbackCityId = -1L;
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

        private sealed class DefenderNoticeGroup
        {
            public readonly long DefenderId;
            public readonly Dictionary<string, WarNoticeState> Notices =
                new Dictionary<string, WarNoticeState>(StringComparer.Ordinal);
            public readonly SortedSet<NoticePriority> Priorities =
                new SortedSet<NoticePriority>();
            public string PrimarySignature = "";

            public DefenderNoticeGroup(long pDefenderId)
            {
                DefenderId = pDefenderId;
            }
        }

        private static readonly Dictionary<string, NoticeAssignments> Assignments =
            new Dictionary<string, NoticeAssignments>(StringComparer.Ordinal);
        private static readonly Dictionary<long, DefenderNoticeGroup> DefenderNoticeGroups =
            new Dictionary<long, DefenderNoticeGroup>();
        private static readonly Dictionary<string, long> DefenderIdByNoticeSignature =
            new Dictionary<string, long>(StringComparer.Ordinal);

        public static void ActivateNotice(WarNoticeState pState)
        {
            if (pState == null || string.IsNullOrEmpty(pState.Signature)) return;
            RegisterNotice(pState);
        }

        public static void RefreshNotice(WarNoticeState pState)
        {
            if (pState == null || string.IsNullOrEmpty(pState.Signature)) return;
            RegisterNotice(pState);
            NoticeAssignments assignments = ResolvePrimaryAssignments(pState);
            if (assignments == null) return;
            if (assignments.Closing) return;
            if (!assignments.DiscoveryComplete)
                ScheduleDiscovery(assignments.State.Signature);
            else
                ScheduleArmyReview(assignments.State.Signature, pRestart: true);
        }

        public static bool AreAllRequiredArmiesReady(WarNoticeState pState)
        {
            if (pState == null) return true;
            ActivateNotice(pState);
            Kingdom defender = ResolveKingdom(pState.DefenderId);
            if (defender?.data == null || defender.isRekt()) return true;
            bool defenderAlreadyAtWar = MilitaryEmergencyService.TryGetActiveWarId(defender, out _);
            if (ArmyDeploymentRules.ShouldBypassPrewarDeployment(defenderAlreadyAtWar)) return true;
            NoticeAssignments assignments = ResolvePrimaryAssignments(pState);
            if (assignments == null) return false;
            return assignments.DiscoveryComplete && assignments.BlockingArmyIds.Count == 0;
        }

        public static bool TryGetCachedReadiness(WarNoticeState pState, out bool pReady)
        {
            pReady = false;
            if (pState == null ||
                !DefenderNoticeGroups.TryGetValue(pState.DefenderId, out DefenderNoticeGroup group) ||
                string.IsNullOrEmpty(group.PrimarySignature) ||
                !Assignments.TryGetValue(group.PrimarySignature, out NoticeAssignments assignments) ||
                assignments.Closing) return false;
            pReady = assignments.DiscoveryComplete && assignments.BlockingArmyIds.Count == 0;
            return true;
        }

        public static bool TryGetPreferredLevyCity(Kingdom pDefender, int pOrdinal,
            out City pCity)
        {
            pCity = null;
            if (pDefender?.data == null || pOrdinal < 0 ||
                !DefenderNoticeGroups.TryGetValue(pDefender.id, out DefenderNoticeGroup group) ||
                string.IsNullOrEmpty(group.PrimarySignature) ||
                !Assignments.TryGetValue(group.PrimarySignature, out NoticeAssignments assignments) ||
                assignments.Closing || pOrdinal >= assignments.TargetCityIds.Count) return false;
            City city = ResolveCity(assignments.TargetCityIds[pOrdinal]);
            if (city?.data == null || city.isRekt() || city.kingdom != pDefender) return false;
            pCity = city;
            return true;
        }

        public static void OnArmyChanged(Kingdom pDefender, Army pArmy, bool pRosterExpanded)
        {
            if (pDefender?.data == null || pArmy?.data == null ||
                !DefenderNoticeGroups.TryGetValue(pDefender.id, out DefenderNoticeGroup group)) return;
            NoticeAssignments assignments = ResolvePrimaryAssignments(group);
            if (assignments == null || assignments.Closing) return;
            if (pRosterExpanded) assignments.ArrivedArmyIds.Remove(pArmy.id);
            RegisterOrRefreshArmy(assignments, pArmy);
        }

        public static void OnArmyInvalidated(Kingdom pDefender, long pArmyId)
        {
            if (pDefender?.data == null || pArmyId < 0 ||
                !DefenderNoticeGroups.TryGetValue(pDefender.id, out DefenderNoticeGroup group)) return;
            NoticeAssignments assignments = ResolvePrimaryAssignments(group);
            if (assignments == null) return;
            assignments.BlockingArmyIds.Remove(pArmyId);
            assignments.ArrivedArmyIds.Remove(pArmyId);
            assignments.TargetCityByArmy.Remove(pArmyId);
            assignments.AssignedTargetCityByArmy.Remove(pArmyId);
            assignments.NextActorIndexByArmy.Remove(pArmyId);
        }

        public static void OnKingdomEnteredWar(Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                !DefenderNoticeGroups.TryGetValue(pKingdom.id, out DefenderNoticeGroup group) ||
                string.IsNullOrEmpty(group.PrimarySignature)) return;
            BeginAssignmentCleanup(group.PrimarySignature, restoreJobs: true);
        }

        public static bool TryPrepareMove(Actor pActor, out WorldTile pTarget)
        {
            pTarget = null;
            if (pActor?.data == null || pActor.isRekt()) return false;
            pActor.data.get(LineageKeys.DEPLOYMENT_NOTICE_SIGNATURE, out string signature, "");
            pActor.data.get(LineageKeys.DEPLOYMENT_TARGET_CITY_ID, out long cityId, -1L);
            if (string.IsNullOrEmpty(signature) ||
                !Assignments.TryGetValue(signature, out NoticeAssignments assignments) ||
                assignments.Closing) return false;
            City city = ResolveCity(cityId);
            if (city?.data == null || city.isRekt()) return false;
            pTarget = StableTargetTile(city, pActor.data.id);
            if (pTarget == null) return false;
            pActor.data.set(LineageKeys.DEPLOYMENT_TARGET_X, pTarget.x);
            pActor.data.set(LineageKeys.DEPLOYMENT_TARGET_Y, pTarget.y);
            return true;
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
                if (!assignments.ArrivedArmyIds.Add(pActor.army.id)) return;
                RegisterOrRefreshArmy(assignments, pActor.army);
            }
        }

        public static bool HasActiveAssignment(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.DEPLOYMENT_NOTICE_SIGNATURE, out string signature, "");
            return !string.IsNullOrEmpty(signature) &&
                   Assignments.TryGetValue(signature, out NoticeAssignments assignments) &&
                   !assignments.Closing;
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
            DefenderNoticeGroups.Clear();
            DefenderIdByNoticeSignature.Clear();
        }

        private static void RegisterNotice(WarNoticeState pState)
        {
            if (pState == null || string.IsNullOrEmpty(pState.Signature) || pState.DefenderId < 0) return;
            if (DefenderIdByNoticeSignature.TryGetValue(pState.Signature, out long previousDefenderId) &&
                previousDefenderId != pState.DefenderId)
                RemoveNoticeFromGroup(pState.Signature, restoreJobs: true);

            if (!DefenderNoticeGroups.TryGetValue(pState.DefenderId, out DefenderNoticeGroup group))
            {
                group = new DefenderNoticeGroup(pState.DefenderId);
                DefenderNoticeGroups[pState.DefenderId] = group;
            }

            if (group.Notices.TryGetValue(pState.Signature, out WarNoticeState previousState))
                group.Priorities.Remove(new NoticePriority(previousState));
            group.Notices[pState.Signature] = pState;
            group.Priorities.Add(new NoticePriority(pState));
            DefenderIdByNoticeSignature[pState.Signature] = pState.DefenderId;

            string nextPrimary = group.Priorities.Count > 0
                ? group.Priorities.Min.Signature
                : "";
            if (!string.Equals(group.PrimarySignature, nextPrimary, StringComparison.Ordinal))
            {
                string previousPrimary = group.PrimarySignature;
                group.PrimarySignature = nextPrimary;
                if (!string.IsNullOrEmpty(previousPrimary))
                    BeginAssignmentCleanup(previousPrimary, restoreJobs: true);
            }

            NoticeAssignments assignments = ResolvePrimaryAssignments(group);
            if (assignments != null && group.Notices.TryGetValue(
                    group.PrimarySignature, out WarNoticeState primaryState))
                assignments.State = primaryState;
        }

        private static NoticeAssignments ResolvePrimaryAssignments(WarNoticeState pState)
        {
            if (pState == null ||
                !DefenderNoticeGroups.TryGetValue(pState.DefenderId, out DefenderNoticeGroup group))
                return null;
            return ResolvePrimaryAssignments(group);
        }

        private static NoticeAssignments ResolvePrimaryAssignments(DefenderNoticeGroup pGroup)
        {
            if (pGroup == null || string.IsNullOrEmpty(pGroup.PrimarySignature) ||
                !pGroup.Notices.TryGetValue(pGroup.PrimarySignature, out WarNoticeState primaryState))
                return null;

            Kingdom defender = ResolveKingdom(pGroup.DefenderId);
            bool defenderAlreadyAtWar = defender?.data != null &&
                                        MilitaryEmergencyService.TryGetActiveWarId(defender, out _);
            if (ArmyDeploymentRules.ShouldBypassPrewarDeployment(defenderAlreadyAtWar))
            {
                BeginAssignmentCleanup(pGroup.PrimarySignature, restoreJobs: true);
                return null;
            }

            bool created = !Assignments.TryGetValue(
                pGroup.PrimarySignature, out NoticeAssignments assignments);
            if (created)
            {
                assignments = new NoticeAssignments { State = primaryState };
                Assignments[pGroup.PrimarySignature] = assignments;
            }
            else
            {
                if (assignments.Closing) return null;
                assignments.State = primaryState;
            }
            if (created) ScheduleDiscovery(pGroup.PrimarySignature);
            return assignments;
        }

        private static void RemoveNoticeFromGroup(string pSignature, bool restoreJobs)
        {
            if (!DefenderIdByNoticeSignature.TryGetValue(pSignature, out long defenderId) ||
                !DefenderNoticeGroups.TryGetValue(defenderId, out DefenderNoticeGroup group))
            {
                BeginAssignmentCleanup(pSignature, restoreJobs);
                return;
            }

            if (group.Notices.TryGetValue(pSignature, out WarNoticeState state))
            {
                group.Priorities.Remove(new NoticePriority(state));
                group.Notices.Remove(pSignature);
            }
            DefenderIdByNoticeSignature.Remove(pSignature);

            bool wasPrimary = string.Equals(
                group.PrimarySignature, pSignature, StringComparison.Ordinal);
            if (wasPrimary) BeginAssignmentCleanup(pSignature, restoreJobs);

            if (group.Notices.Count == 0 || group.Priorities.Count == 0)
            {
                DefenderNoticeGroups.Remove(defenderId);
                return;
            }

            if (!wasPrimary) return;
            group.PrimarySignature = group.Priorities.Min.Signature;
            ResolvePrimaryAssignments(group);
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
                    if (city?.data != null && city.getArmy() == pArmy) return city.isOkToSendArmy();
                }
                catch { }
            }
            Actor captain = null;
            try { captain = pArmy.getCaptain(); } catch { }
            return captain?.data != null && !captain.isRekt() && captain.isAlive();
        }

        private static bool IsArmyArrived(NoticeAssignments pAssignments, Army pArmy)
        {
            return pArmy?.data != null && pAssignments.TargetCityByArmy.ContainsKey(pArmy.id) &&
                   pAssignments.ArrivedArmyIds.Contains(pArmy.id);
        }

        private static City ResolveAssignedCity(NoticeAssignments pAssignments, Army pArmy)
        {
            if (pAssignments.TargetCityByArmy.TryGetValue(pArmy.id, out long cityId))
            {
                City existing = ResolveCity(cityId);
                if (existing?.data != null && !existing.isRekt() && existing.kingdom?.id == pStateDefender(pAssignments))
                    return existing;
            }
            if (pAssignments.TargetCityIds.Count == 0 ||
                !pAssignments.ArmyOrderById.TryGetValue(pArmy.id, out int order)) return null;
            long targetId = pAssignments.TargetCityIds[order % pAssignments.TargetCityIds.Count];
            City target = ResolveCity(targetId);
            if (target?.data == null || target.isRekt() || target.kingdom?.id != pStateDefender(pAssignments))
                return null;
            pAssignments.TargetCityByArmy[pArmy.id] = target.id;
            return target;
        }

        private static long pStateDefender(NoticeAssignments pAssignments)
        {
            return pAssignments?.State?.DefenderId ?? -1L;
        }

        private static void AssignArmy(NoticeAssignments pAssignments, Army pArmy, City pTarget)
        {
            if (pAssignments?.State == null || pArmy?.data == null || pTarget?.data == null) return;
            bool targetChanged = !pAssignments.AssignedTargetCityByArmy.TryGetValue(
                                     pArmy.id, out long assignedTargetId) ||
                                 assignedTargetId != pTarget.id;
            pAssignments.AssignedTargetCityByArmy[pArmy.id] = pTarget.id;
            if (targetChanged)
            {
                pAssignments.NextActorIndexByArmy[pArmy.id] = 0;
                pAssignments.ArrivedArmyIds.Remove(pArmy.id);
            }
            else if (!pAssignments.NextActorIndexByArmy.ContainsKey(pArmy.id))
            {
                pAssignments.NextActorIndexByArmy[pArmy.id] = 0;
            }
            ScheduleArmyAssignment(pAssignments.State.Signature, pArmy.id, pTarget.id);
        }

        private static void ScheduleArmyAssignment(string pSignature, long pArmyId, long pTargetCityId)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("deployment_assign:" + (pSignature ?? ""), pArmyId),
                DeferredWorkClass.Runtime,
                () => AssignArmyBatch(pSignature, pArmyId, pTargetCityId));
        }

        private static void AssignArmyBatch(string pSignature, long pArmyId, long pTargetCityId)
        {
            if (string.IsNullOrEmpty(pSignature) ||
                !Assignments.TryGetValue(pSignature, out NoticeAssignments assignments) ||
                assignments.Closing) return;
            Army army = ResolveArmy(pArmyId);
            City target = ResolveCity(pTargetCityId);
            if (army?.data == null || target?.data == null || target.isRekt()) return;
            assignments.NextActorIndexByArmy.TryGetValue(pArmyId, out int cursor);
            if (cursor < 0 || cursor > army.units.Count) cursor = 0;
            int end = Math.Min(army.units.Count, cursor + ActorMutationBatchSize);
            for (int i = cursor; i < end; i++)
            {
                Actor actor = army.units[i];
                if (actor?.data == null || actor.isRekt() || !actor.isAlive() || !actor.isWarrior()) continue;
                if (RoyalGuardService.IsRoyalGuard(actor)) continue;
                actor.data.get(LineageKeys.DEPLOYMENT_NOTICE_SIGNATURE, out string currentSignature, "");
                actor.data.get(LineageKeys.DEPLOYMENT_TARGET_CITY_ID, out long currentCityId, -1L);
                bool reset = ArmyDeploymentRules.ShouldResetAssignment(currentSignature,
                    pSignature, currentCityId, pTargetCityId);
                actor.data.set(LineageKeys.DEPLOYMENT_NOTICE_SIGNATURE, pSignature);
                actor.data.set(LineageKeys.DEPLOYMENT_TARGET_CITY_ID, pTargetCityId);
                if (reset) actor.data.set(LineageKeys.DEPLOYMENT_ARRIVED, false);
                assignments.ActorIds.Add(actor.data.id);
                try
                {
                    if (actor.ai?.job?.id != WarMobilizationContent.DeploymentJobId)
                        actor.ai?.setJob(WarMobilizationContent.DeploymentJobId);
                }
                catch { }
            }
            assignments.NextActorIndexByArmy[pArmyId] = end;
            if (end < army.units.Count)
                ScheduleArmyAssignment(pSignature, pArmyId, pTargetCityId);
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
                        ClearActorAssignment(actor, assignments.RestoreJobs);
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
            Kingdom defender = ResolveKingdom(assignments.State?.DefenderId ?? -1L);
            Kingdom attacker = ResolveKingdom(assignments.State?.AttackerId ?? -1L);
            if (defender?.data == null || defender.isRekt()) return;

            int cityCount = defender.cities.Count;
            if (assignments.CityDiscoveryCursor < 0 || assignments.CityDiscoveryCursor > cityCount)
                assignments.CityDiscoveryCursor = 0;
            int end = Math.Min(cityCount, assignments.CityDiscoveryCursor +
                                          ArmyDeploymentRules.MaxCitiesDiscoveredPerWorkItem);
            for (int i = assignments.CityDiscoveryCursor; i < end; i++)
            {
                City city = defender.cities[i];
                if (city?.data == null || city.isRekt() || city.kingdom != defender) continue;
                if (assignments.FallbackCityId < 0 || city == defender.capital)
                    assignments.FallbackCityId = city.id;
                if (BordersKingdom(city, attacker)) AddTargetCity(assignments, city);
                if (city.hasArmy()) RegisterOrRefreshArmy(assignments, city.getArmy());
            }
            assignments.CityDiscoveryCursor = end;
            if (end < cityCount)
            {
                ScheduleDiscovery(pSignature);
                return;
            }

            AddIndexedRoleArmies(assignments, defender, AWArmyRole.BorderArmy);
            AddIndexedRoleArmies(assignments, defender, AWArmyRole.SlaveArmy);
            if (assignments.TargetCityIds.Count == 0)
            {
                City fallback = defender.capital?.data != null && !defender.capital.isRekt()
                    ? defender.capital
                    : ResolveCity(assignments.FallbackCityId);
                AddTargetCity(assignments, fallback);
            }
            assignments.DiscoveryComplete = true;
            ScheduleArmyReview(pSignature, pRestart: true);
        }

        private static void AddTargetCity(NoticeAssignments pAssignments, City pCity)
        {
            if (pAssignments == null || pCity?.data == null || pCity.isRekt() ||
                !pAssignments.TargetCitySet.Add(pCity.id)) return;
            pAssignments.TargetCityIds.Add(pCity.id);
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
            if (assignments.ArmyReviewCursor < 0 || assignments.ArmyReviewCursor > count)
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
            Kingdom defender = ResolveKingdom(pAssignments.State.DefenderId);
            if (!IsRequiredArmy(pArmy, defender, out int living))
            {
                if (pArmy?.data != null) pAssignments.BlockingArmyIds.Remove(pArmy.id);
                return;
            }

            if (pAssignments.RequiredArmySet.Add(pArmy.id))
            {
                pAssignments.ArmyOrderById[pArmy.id] = pAssignments.RequiredArmyIds.Count;
                pAssignments.RequiredArmyIds.Add(pArmy.id);
            }

            bool ready = IsReady(pArmy, living);
            if (ready && pAssignments.DiscoveryComplete)
            {
                City target = ResolveAssignedCity(pAssignments, pArmy);
                if (target?.data != null) AssignArmy(pAssignments, pArmy, target);
            }
            bool arrived = ready && IsArmyArrived(pAssignments, pArmy);
            if (ArmyDeploymentRules.BlocksDeclarationGate(
                    living > 0, IsRoyalGuardArmy(pArmy), ready, arrived))
                pAssignments.BlockingArmyIds.Add(pArmy.id);
            else
                pAssignments.BlockingArmyIds.Remove(pArmy.id);
        }

        private static void AddIndexedRoleArmies(NoticeAssignments pAssignments,
            Kingdom pKingdom, string pRole)
        {
            foreach (Army army in AWArmyService.GetRoleArmies(pKingdom, pRole))
                RegisterOrRefreshArmy(pAssignments, army);
        }

        private static bool BordersKingdom(City pCity, Kingdom pAttacker)
        {
            if (pCity?.data == null || pAttacker?.data == null) return false;
            try { return pCity.neighbours_kingdoms.Contains(pAttacker); }
            catch { return false; }
        }

        private static WorldTile StableTargetTile(City pCity, long pActorId)
        {
            WorldTile center = pCity?.getTile();
            if (center == null) return null;
            int phase = (int)(Math.Abs(pActorId) % 8L);
            WorldTile candidate = World.world?.GetTile(
                center.x + TargetOffsetX[phase], center.y + TargetOffsetY[phase]);
            if (candidate != null && candidate.Type != null && candidate.Type.ground &&
                !candidate.Type.liquid && !candidate.Type.lava && !candidate.Type.block &&
                candidate.isSameIsland(center)) return candidate;
            return center.getNeighbourTileSameIsland() ?? center;
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
            if (!pRestoreJob || pActor.isRekt() || pActor.ai == null) return;
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
