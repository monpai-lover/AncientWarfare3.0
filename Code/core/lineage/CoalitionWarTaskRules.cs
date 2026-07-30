using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum CoalitionWarSide
    {
        Attackers = 0,
        Defenders = 1
    }

    public sealed class CoalitionWarTaskSpec
    {
        public CoalitionWarTaskSpec(long taskId, long targetCityId,
            int priority, int reservationLimit, long expiryWorldDay)
        {
            TaskId = taskId;
            TargetCityId = targetCityId;
            Priority = priority;
            ReservationLimit = Math.Max(1,
                Math.Min(CoalitionWarTaskRules.MaximumClaimsPerTask,
                    reservationLimit));
            ExpiryWorldDay = Math.Max(0L, expiryWorldDay);
        }

        public long TaskId { get; }
        public long TargetCityId { get; }
        public int Priority { get; }
        public int ReservationLimit { get; }
        public long ExpiryWorldDay { get; }
    }

    public sealed class CoalitionWarTaskClaim
    {
        internal CoalitionWarTaskClaim(long warId, long taskId,
            long targetCityId, CoalitionWarSide side,
            long participantKingdomId, long armyId)
        {
            WarId = warId;
            TaskId = taskId;
            TargetCityId = targetCityId;
            Side = side;
            ParticipantKingdomId = participantKingdomId;
            ArmyId = armyId;
        }

        public long WarId { get; }
        public long TaskId { get; }
        public long TargetCityId { get; }
        public CoalitionWarSide Side { get; }
        public long ParticipantKingdomId { get; }
        public long ArmyId { get; }
    }

    public readonly struct CoalitionLeaderReservationSpec
    {
        public CoalitionLeaderReservationSpec(long armyId,
            long targetCityId)
        {
            ArmyId = armyId;
            TargetCityId = targetCityId;
        }

        public long ArmyId { get; }
        public long TargetCityId { get; }
    }

    public static class CoalitionWarTaskRules
    {
        public const int MaximumTasksPerWar = 8;
        public const int MaximumClaimsPerParticipant = 2;
        public const int MaximumClaimsPerTask = 4;
        public const int TaskLifetimeWorldDays = 30;
        public const int MaximumTargetsInspectedPerSide = 16;

        public static bool CanPublish(long leaderKingdomId,
            long publisherKingdomId)
        {
            return leaderKingdomId >= 0L &&
                   leaderKingdomId == publisherKingdomId;
        }

        public static bool CanClaim(long participantKingdomId,
            long armyKingdomId, bool participantOnSide,
            int participantClaimCount, int taskClaimCount,
            int taskReservationLimit, long currentWorldDay,
            long expiryWorldDay)
        {
            return participantKingdomId >= 0L &&
                   participantKingdomId == armyKingdomId &&
                   participantOnSide &&
                   participantClaimCount < MaximumClaimsPerParticipant &&
                   taskClaimCount < Math.Max(1, taskReservationLimit) &&
                   currentWorldDay < expiryWorldDay;
        }

        public static bool IsTargetAvailableForSide(
            bool ownerAtWarWithParticipant,
            bool controlledByParticipantSide)
        {
            return ownerAtWarWithParticipant &&
                   !controlledByParticipantSide;
        }

        public static IReadOnlyList<CoalitionWarTaskSpec>
            SelectPublishedTasks(
                IReadOnlyList<CoalitionWarTaskSpec> pTasks,
                int maximumTasks)
        {
            int limit = Math.Max(0, maximumTasks);
            var candidates = new List<CoalitionWarTaskSpec>();
            var seenTaskIds = new HashSet<long>();
            var seenTargetIds = new HashSet<long>();
            if (pTasks != null)
                for (int i = 0; i < pTasks.Count; i++)
                {
                    CoalitionWarTaskSpec task = pTasks[i];
                    if (task == null || task.TaskId < 0L ||
                        task.TargetCityId < 0L ||
                        !seenTaskIds.Add(task.TaskId) ||
                        !seenTargetIds.Add(task.TargetCityId)) continue;
                    candidates.Add(task);
                }
            candidates.Sort((left, right) =>
            {
                int priority = right.Priority.CompareTo(left.Priority);
                return priority != 0
                    ? priority
                    : left.TaskId.CompareTo(right.TaskId);
            });
            if (candidates.Count > limit)
                candidates.RemoveRange(limit, candidates.Count - limit);
            return candidates;
        }
    }

    public sealed class CoalitionWarTaskLedger
    {
        private sealed class TaskState
        {
            internal CoalitionWarSide Side;
            internal long LeaderKingdomId;
            internal CoalitionWarTaskSpec Spec;
            internal readonly Dictionary<long, CoalitionWarTaskClaim>
                ClaimsByArmy =
                    new Dictionary<long, CoalitionWarTaskClaim>();
        }

        private sealed class WarState
        {
            internal readonly Dictionary<long, TaskState> TasksById =
                new Dictionary<long, TaskState>();
        }

        private readonly Dictionary<long, WarState> _wars =
            new Dictionary<long, WarState>();
        private readonly Dictionary<long, CoalitionWarTaskClaim>
            _claimsByArmy =
                new Dictionary<long, CoalitionWarTaskClaim>();
        private readonly Dictionary<long, HashSet<long>>
            _leaderArmyIdsByKingdom =
                new Dictionary<long, HashSet<long>>();

        public int Publish(long warId, CoalitionWarSide side,
            long leaderKingdomId, long publisherKingdomId,
            IReadOnlyList<CoalitionWarTaskSpec> pTasks)
        {
            if (warId < 0L || !CoalitionWarTaskRules.CanPublish(
                    leaderKingdomId, publisherKingdomId) || pTasks == null)
                return 0;
            if (!_wars.TryGetValue(warId, out WarState war))
            {
                war = new WarState();
                _wars[warId] = war;
            }

            var candidates = new List<CoalitionWarTaskSpec>();
            var seenTaskIds = new HashSet<long>();
            var seenTargetIds = new HashSet<long>();
            for (int i = 0; i < pTasks.Count; i++)
            {
                CoalitionWarTaskSpec task = pTasks[i];
                if (task == null || task.TaskId < 0L ||
                    task.TargetCityId < 0L ||
                    !seenTaskIds.Add(task.TaskId) ||
                    !seenTargetIds.Add(task.TargetCityId)) continue;
                candidates.Add(task);
            }
            candidates.Sort(CompareSpecs);

            int otherSideCount = 0;
            foreach (TaskState task in war.TasksById.Values)
                if (task.Side != side) otherSideCount++;
            int accepted = Math.Min(candidates.Count, Math.Max(0,
                CoalitionWarTaskRules.MaximumTasksPerWar -
                otherSideCount));
            var retainedTaskIds = new HashSet<long>();
            for (int i = 0; i < accepted; i++)
                retainedTaskIds.Add(candidates[i].TaskId);

            var removedTaskIds = new List<long>();
            foreach (KeyValuePair<long, TaskState> pair in war.TasksById)
                if (pair.Value.Side == side &&
                    !retainedTaskIds.Contains(pair.Key))
                    removedTaskIds.Add(pair.Key);
            for (int i = 0; i < removedTaskIds.Count; i++)
                RemoveTask(war, removedTaskIds[i]);

            for (int i = 0; i < accepted; i++)
            {
                CoalitionWarTaskSpec spec = candidates[i];
                if (war.TasksById.TryGetValue(spec.TaskId,
                        out TaskState existing) &&
                    existing.Side == side &&
                    existing.LeaderKingdomId == leaderKingdomId &&
                    existing.Spec.TargetCityId == spec.TargetCityId)
                {
                    existing.Spec = spec;
                    TrimClaimsToReservationLimit(existing);
                    continue;
                }
                if (existing != null) RemoveTask(war, spec.TaskId);
                war.TasksById[spec.TaskId] = new TaskState
                    {
                        Side = side,
                        LeaderKingdomId = leaderKingdomId,
                        Spec = spec
                    };
            }
            if (war.TasksById.Count == 0) _wars.Remove(warId);
            return accepted;
        }

        public bool TryClaim(long warId, CoalitionWarSide side,
            long participantKingdomId, long armyId, long armyKingdomId,
            bool participantOnSide, long currentWorldDay,
            out CoalitionWarTaskClaim pClaim)
        {
            pClaim = null;
            if (armyId < 0L || IsSideLeader(warId, side,
                    participantKingdomId)) return false;
            if (_claimsByArmy.TryGetValue(armyId,
                    out CoalitionWarTaskClaim existing))
            {
                if (existing.WarId == warId && existing.Side == side &&
                    existing.ParticipantKingdomId == participantKingdomId &&
                    armyKingdomId == participantKingdomId &&
                    TryGetLiveTask(existing, currentWorldDay,
                        out TaskState existingTask) &&
                    existingTask.LeaderKingdomId != participantKingdomId)
                {
                    pClaim = existing;
                    return true;
                }
                ReleaseArmy(armyId);
            }
            if (!_wars.TryGetValue(warId, out WarState war)) return false;
            int participantClaims = ClaimCount(warId,
                participantKingdomId);
            TaskState selected = null;
            foreach (TaskState task in OrderedTasks(war, side))
            {
                if (task.LeaderKingdomId == participantKingdomId ||
                    !CoalitionWarTaskRules.CanClaim(participantKingdomId,
                        armyKingdomId, participantOnSide,
                        participantClaims, task.ClaimsByArmy.Count,
                        task.Spec.ReservationLimit, currentWorldDay,
                        task.Spec.ExpiryWorldDay)) continue;
                selected = task;
                break;
            }
            if (selected == null) return false;
            pClaim = new CoalitionWarTaskClaim(warId,
                selected.Spec.TaskId, selected.Spec.TargetCityId, side,
                participantKingdomId, armyId);
            selected.ClaimsByArmy[armyId] = pClaim;
            _claimsByArmy[armyId] = pClaim;
            return true;
        }

        public int ReplaceLeaderReservations(long warId,
            CoalitionWarSide side, long leaderKingdomId,
            long currentWorldDay,
            IReadOnlyList<CoalitionLeaderReservationSpec> pReservations)
        {
            if (leaderKingdomId < 0L || pReservations == null ||
                !_wars.TryGetValue(warId, out WarState war)) return 0;

            var tasksByTarget = new Dictionary<long, TaskState>();
            var staleArmyIds = new List<long>();
            foreach (TaskState task in war.TasksById.Values)
            {
                if (task.Side != side ||
                    task.LeaderKingdomId != leaderKingdomId) continue;
                if (currentWorldDay < task.Spec.ExpiryWorldDay)
                    tasksByTarget[task.Spec.TargetCityId] = task;
                foreach (CoalitionWarTaskClaim claim in
                         task.ClaimsByArmy.Values)
                    if (claim.ParticipantKingdomId == leaderKingdomId)
                        staleArmyIds.Add(claim.ArmyId);
            }
            for (int i = 0; i < staleArmyIds.Count; i++)
                ReleaseArmy(staleArmyIds[i]);

            int added = 0;
            var seenArmyIds = new HashSet<long>();
            for (int i = 0; i < pReservations.Count; i++)
            {
                CoalitionLeaderReservationSpec reservation =
                    pReservations[i];
                if (reservation.ArmyId < 0L ||
                    reservation.TargetCityId < 0L ||
                    !seenArmyIds.Add(reservation.ArmyId) ||
                    !tasksByTarget.TryGetValue(reservation.TargetCityId,
                        out TaskState task) ||
                    task.ClaimsByArmy.Count >=
                    task.Spec.ReservationLimit) continue;
                ReleaseArmy(reservation.ArmyId);
                var claim = new CoalitionWarTaskClaim(warId,
                    task.Spec.TaskId, task.Spec.TargetCityId, side,
                    leaderKingdomId, reservation.ArmyId);
                task.ClaimsByArmy[reservation.ArmyId] = claim;
                _claimsByArmy[reservation.ArmyId] = claim;
                IndexLeaderReservation(leaderKingdomId,
                    reservation.ArmyId);
                added++;
            }
            return added;
        }

        public bool TrySelect(long warId, CoalitionWarSide side,
            long participantKingdomId, long currentWorldDay,
            out CoalitionWarTaskSpec pTask)
        {
            pTask = null;
            if (!_wars.TryGetValue(warId, out WarState war) ||
                ClaimCount(warId, participantKingdomId) >=
                CoalitionWarTaskRules.MaximumClaimsPerParticipant)
                return false;
            foreach (TaskState task in OrderedTasks(war, side))
            {
                if (task.LeaderKingdomId == participantKingdomId ||
                    currentWorldDay >= task.Spec.ExpiryWorldDay ||
                    task.ClaimsByArmy.Count >= task.Spec.ReservationLimit)
                    continue;
                pTask = task.Spec;
                return true;
            }
            return false;
        }

        public bool TryGetClaim(long armyId,
            out CoalitionWarTaskClaim pClaim)
        {
            return _claimsByArmy.TryGetValue(armyId, out pClaim);
        }

        public int TaskCount(long warId)
        {
            return _wars.TryGetValue(warId, out WarState war)
                ? war.TasksById.Count
                : 0;
        }

        public int ClaimCount(long warId, long participantKingdomId)
        {
            if (!_wars.TryGetValue(warId, out WarState war)) return 0;
            int count = 0;
            foreach (TaskState task in war.TasksById.Values)
                foreach (CoalitionWarTaskClaim claim in
                         task.ClaimsByArmy.Values)
                    if (claim.ParticipantKingdomId == participantKingdomId)
                        count++;
            return count;
        }

        public int ClaimCountForTask(long warId, long taskId)
        {
            return _wars.TryGetValue(warId, out WarState war) &&
                   war.TasksById.TryGetValue(taskId, out TaskState task)
                ? task.ClaimsByArmy.Count
                : 0;
        }

        public IReadOnlyDictionary<long, int> ClaimCountsByTarget(
            long warId, CoalitionWarSide side,
            long excludedParticipantKingdomId, long currentWorldDay)
        {
            var result = new Dictionary<long, int>();
            if (!_wars.TryGetValue(warId, out WarState war))
                return result;
            foreach (TaskState task in war.TasksById.Values)
            {
                if (task.Side != side ||
                    currentWorldDay >= task.Spec.ExpiryWorldDay) continue;
                int count = 0;
                foreach (CoalitionWarTaskClaim claim in
                         task.ClaimsByArmy.Values)
                    if (claim.ParticipantKingdomId !=
                        excludedParticipantKingdomId) count++;
                if (count > 0) result[task.Spec.TargetCityId] = count;
            }
            return result;
        }

        public bool ReleaseArmy(long armyId)
        {
            if (!_claimsByArmy.TryGetValue(armyId,
                    out CoalitionWarTaskClaim claim)) return false;
            _claimsByArmy.Remove(armyId);
            RemoveLeaderReservationIndex(claim.ParticipantKingdomId,
                armyId);
            if (_wars.TryGetValue(claim.WarId, out WarState war) &&
                war.TasksById.TryGetValue(claim.TaskId,
                    out TaskState task))
                task.ClaimsByArmy.Remove(armyId);
            return true;
        }

        public bool ReleaseParticipantClaim(long armyId)
        {
            if (!_claimsByArmy.TryGetValue(armyId,
                    out CoalitionWarTaskClaim claim)) return false;
            if (_wars.TryGetValue(claim.WarId, out WarState war) &&
                war.TasksById.TryGetValue(claim.TaskId,
                    out TaskState task) &&
                task.LeaderKingdomId == claim.ParticipantKingdomId)
                return false;
            return ReleaseArmy(armyId);
        }

        public int ClearLeaderReservations(long leaderKingdomId)
        {
            if (!_leaderArmyIdsByKingdom.TryGetValue(leaderKingdomId,
                    out HashSet<long> indexed)) return 0;
            var armyIds = new List<long>(indexed);
            int released = 0;
            for (int i = 0; i < armyIds.Count; i++)
                if (ReleaseArmy(armyIds[i])) released++;
            _leaderArmyIdsByKingdom.Remove(leaderKingdomId);
            return released;
        }

        public int ReleaseTarget(long warId, long targetCityId)
        {
            if (!_wars.TryGetValue(warId, out WarState war)) return 0;
            var taskIds = new List<long>();
            foreach (KeyValuePair<long, TaskState> pair in war.TasksById)
                if (pair.Value.Spec.TargetCityId == targetCityId)
                    taskIds.Add(pair.Key);
            int releasedClaims = 0;
            for (int i = 0; i < taskIds.Count; i++)
                releasedClaims += RemoveTask(war, taskIds[i]);
            if (war.TasksById.Count == 0) _wars.Remove(warId);
            return releasedClaims;
        }

        public bool ReleaseWar(long warId)
        {
            if (!_wars.TryGetValue(warId, out WarState war)) return false;
            var armyIds = new List<long>();
            foreach (TaskState task in war.TasksById.Values)
                armyIds.AddRange(task.ClaimsByArmy.Keys);
            for (int i = 0; i < armyIds.Count; i++)
                ReleaseArmy(armyIds[i]);
            _wars.Remove(warId);
            return true;
        }

        public void Clear()
        {
            _wars.Clear();
            _claimsByArmy.Clear();
            _leaderArmyIdsByKingdom.Clear();
        }

        public int ReleaseSide(long warId, CoalitionWarSide side)
        {
            if (!_wars.TryGetValue(warId, out WarState war)) return 0;
            var taskIds = new List<long>();
            foreach (KeyValuePair<long, TaskState> pair in war.TasksById)
                if (pair.Value.Side == side) taskIds.Add(pair.Key);
            for (int i = 0; i < taskIds.Count; i++)
                RemoveTask(war, taskIds[i]);
            if (war.TasksById.Count == 0) _wars.Remove(warId);
            return taskIds.Count;
        }

        private int RemoveTask(WarState pWar, long taskId)
        {
            if (pWar == null || !pWar.TasksById.TryGetValue(taskId,
                    out TaskState task)) return 0;
            int count = task.ClaimsByArmy.Count;
            var armyIds = new List<long>(task.ClaimsByArmy.Keys);
            for (int i = 0; i < armyIds.Count; i++)
                ReleaseArmy(armyIds[i]);
            pWar.TasksById.Remove(taskId);
            return count;
        }

        private void IndexLeaderReservation(long leaderKingdomId,
            long armyId)
        {
            if (!_leaderArmyIdsByKingdom.TryGetValue(leaderKingdomId,
                    out HashSet<long> armyIds))
            {
                armyIds = new HashSet<long>();
                _leaderArmyIdsByKingdom[leaderKingdomId] = armyIds;
            }
            armyIds.Add(armyId);
        }

        private void RemoveLeaderReservationIndex(long leaderKingdomId,
            long armyId)
        {
            if (!_leaderArmyIdsByKingdom.TryGetValue(leaderKingdomId,
                    out HashSet<long> armyIds)) return;
            armyIds.Remove(armyId);
            if (armyIds.Count == 0)
                _leaderArmyIdsByKingdom.Remove(leaderKingdomId);
        }

        private void TrimClaimsToReservationLimit(TaskState pTask)
        {
            if (pTask == null || pTask.ClaimsByArmy.Count <=
                pTask.Spec.ReservationLimit) return;
            var claims = new List<CoalitionWarTaskClaim>(
                pTask.ClaimsByArmy.Values);
            claims.Sort((left, right) =>
            {
                bool leftLeader = left.ParticipantKingdomId ==
                                  pTask.LeaderKingdomId;
                bool rightLeader = right.ParticipantKingdomId ==
                                   pTask.LeaderKingdomId;
                if (leftLeader != rightLeader)
                    return leftLeader ? -1 : 1;
                return left.ArmyId.CompareTo(right.ArmyId);
            });
            for (int i = pTask.Spec.ReservationLimit;
                 i < claims.Count; i++)
                ReleaseArmy(claims[i].ArmyId);
        }

        private bool TryGetLiveTask(CoalitionWarTaskClaim pClaim,
            long currentWorldDay, out TaskState pTask)
        {
            pTask = null;
            return pClaim != null &&
                   _wars.TryGetValue(pClaim.WarId, out WarState war) &&
                   war.TasksById.TryGetValue(pClaim.TaskId, out pTask) &&
                   currentWorldDay < pTask.Spec.ExpiryWorldDay;
        }

        private bool IsSideLeader(long warId, CoalitionWarSide side,
            long participantKingdomId)
        {
            if (!_wars.TryGetValue(warId, out WarState war)) return false;
            foreach (TaskState task in war.TasksById.Values)
                if (task.Side == side &&
                    task.LeaderKingdomId == participantKingdomId)
                    return true;
            return false;
        }

        private static List<TaskState> OrderedTasks(WarState pWar,
            CoalitionWarSide side)
        {
            var tasks = new List<TaskState>();
            foreach (TaskState task in pWar.TasksById.Values)
                if (task.Side == side) tasks.Add(task);
            tasks.Sort((left, right) =>
            {
                int priority = right.Spec.Priority.CompareTo(
                    left.Spec.Priority);
                return priority != 0
                    ? priority
                    : left.Spec.TaskId.CompareTo(right.Spec.TaskId);
            });
            return tasks;
        }

        private static int CompareSpecs(CoalitionWarTaskSpec pLeft,
            CoalitionWarTaskSpec pRight)
        {
            int priority = pRight.Priority.CompareTo(pLeft.Priority);
            return priority != 0
                ? priority
                : pLeft.TaskId.CompareTo(pRight.TaskId);
        }
    }
}
