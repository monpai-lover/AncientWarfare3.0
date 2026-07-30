using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class CoalitionWarTaskService
    {
        private const int MaximumTasksPerSide =
            CoalitionWarTaskRules.MaximumTasksPerWar / 2;

        private sealed class SideRefreshWork : IDisposable
        {
            private readonly War _war;
            private readonly CoalitionWarSide _side;
            private readonly Kingdom _leader;
            private readonly long _expiryWorldDay;
            private readonly long _formalGoalCityId;
            private readonly HashSet<long> _seenCityIds =
                new HashSet<long>();
            private readonly List<CoalitionWarTaskSpec> _candidates =
                new List<CoalitionWarTaskSpec>(
                    CoalitionWarTaskRules.MaximumTargetsInspectedPerSide);
            private IEnumerator<Kingdom> _opponentEnumerator;
            private Kingdom _currentOpponent;
            private int _currentCityIndex;
            private int _inspectedCities;

            internal SideRefreshWork(War pWar, CoalitionWarSide pSide,
                Kingdom pLeader, long pExpiryWorldDay)
            {
                _war = pWar;
                _side = pSide;
                _leader = pLeader;
                _expiryWorldDay = pExpiryWorldDay;
                WarTerritoryService.TryGetPrimaryOpenGoalCityId(
                    pWar.data.id, out _formalGoalCityId);
                _opponentEnumerator = CreateParticipantEnumerator(pWar,
                    pSide);
                if (pLeader?.data == null || _opponentEnumerator == null)
                    CompleteScan();
            }

            internal bool Complete { get; private set; }
            internal Kingdom Leader => _leader;
            internal IReadOnlyList<CoalitionWarTaskSpec> Tasks { get;
                private set; } = Array.Empty<CoalitionWarTaskSpec>();

            internal void Advance()
            {
                if (Complete) return;
                int remainingParticipants = CoalitionWarTaskRefreshRules.
                    MaximumParticipantsPerWorkItem;
                int remainingCities = CoalitionWarTaskRefreshRules.
                    MaximumCitiesPerWorkItem;
                while (remainingCities > 0 &&
                       _inspectedCities < CoalitionWarTaskRules.
                           MaximumTargetsInspectedPerSide)
                {
                    if (_currentOpponent?.cities != null &&
                        _currentCityIndex < _currentOpponent.cities.Count)
                    {
                        City city = _currentOpponent.cities[
                            _currentCityIndex++];
                        remainingCities--;
                        _inspectedCities++;
                        CaptureCandidate(city);
                        continue;
                    }

                    _currentOpponent = null;
                    _currentCityIndex = 0;
                    if (remainingParticipants <= 0) return;
                    if (!MoveNextParticipant())
                    {
                        CompleteScan();
                        return;
                    }
                    remainingParticipants--;
                }
                if (_inspectedCities >= CoalitionWarTaskRules.
                        MaximumTargetsInspectedPerSide)
                    CompleteScan();
            }

            public void Dispose()
            {
                _opponentEnumerator?.Dispose();
                _opponentEnumerator = null;
            }

            private bool MoveNextParticipant()
            {
                if (_opponentEnumerator == null) return false;
                try
                {
                    if (!_opponentEnumerator.MoveNext()) return false;
                    Kingdom participant = _opponentEnumerator.Current;
                    _currentOpponent = participant?.data != null
                        ? participant
                        : null;
                    return true;
                }
                catch { return false; }
            }

            private void CaptureCandidate(City pCity)
            {
                if (!IsValidTarget(_war, _leader, pCity) ||
                    !_seenCityIds.Add(pCity.id)) return;
                bool formalGoal = pCity.id == _formalGoalCityId;
                bool capital = _currentOpponent?.capital == pCity;
                int priority = formalGoal ? 400 : capital ? 300 :
                    SafeWarriorCount(pCity) <= 4 ? 200 : 100;
                int reservationLimit = ArmyRtsRules.
                    AssaultReservationCap(capital, formalGoal);
                _candidates.Add(new CoalitionWarTaskSpec(
                    StableTaskId(pCity.id, _side), pCity.id, priority,
                    reservationLimit, _expiryWorldDay));
            }

            private void CompleteScan()
            {
                if (Complete) return;
                Complete = true;
                Dispose();
                Tasks = CoalitionWarTaskRules.SelectPublishedTasks(
                    _candidates, MaximumTasksPerSide);
            }
        }

        private sealed class RefreshWork : IDisposable
        {
            internal RefreshWork(War pWar, int pGeneration,
                long pExpiryWorldDay)
            {
                WarId = pWar.data.id;
                Generation = pGeneration;
                Attackers = new SideRefreshWork(pWar,
                    CoalitionWarSide.Attackers, SafeMainAttacker(pWar),
                    pExpiryWorldDay);
                Defenders = new SideRefreshWork(pWar,
                    CoalitionWarSide.Defenders, SafeMainDefender(pWar),
                    pExpiryWorldDay);
            }

            internal long WarId { get; }
            internal int Generation { get; }
            internal SideRefreshWork Attackers { get; }
            internal SideRefreshWork Defenders { get; }

            internal void Advance()
            {
                if (!Attackers.Complete) Attackers.Advance();
                else if (!Defenders.Complete) Defenders.Advance();
            }

            public void Dispose()
            {
                Attackers.Dispose();
                Defenders.Dispose();
            }
        }

        private sealed class TargetInvalidationWork : IDisposable
        {
            private IEnumerator<long> _warEnumerator;

            internal TargetInvalidationWork(long pTargetCityId,
                HashSet<long> pWarIds)
            {
                TargetCityId = pTargetCityId;
                _warEnumerator = pWarIds?.GetEnumerator();
            }

            internal long TargetCityId { get; }

            internal bool TryTake(out long pWarId)
            {
                pWarId = -1L;
                if (_warEnumerator == null) return false;
                try
                {
                    if (!_warEnumerator.MoveNext()) return false;
                    pWarId = _warEnumerator.Current;
                    return true;
                }
                catch { return false; }
            }

            public void Dispose()
            {
                _warEnumerator?.Dispose();
                _warEnumerator = null;
            }
        }

        private static readonly CoalitionWarTaskLedger Ledger =
            new CoalitionWarTaskLedger();
        private static readonly SortedSet<long> DirtyWarIds =
            new SortedSet<long>();
        private static readonly Dictionary<long, HashSet<long>>
            TargetIdsByWar = new Dictionary<long, HashSet<long>>();
        private static readonly Dictionary<long, HashSet<long>>
            WarIdsByTarget = new Dictionary<long, HashSet<long>>();
        private static readonly SortedDictionary<long, SortedSet<long>>
            RefreshWarIdsByDay =
                new SortedDictionary<long, SortedSet<long>>();
        private static readonly Dictionary<long, long> RefreshDayByWarId =
            new Dictionary<long, long>();
        private static readonly Dictionary<long, RefreshWork>
            RefreshWorkByWar = new Dictionary<long, RefreshWork>();
        private static readonly Dictionary<long, int> RefreshGenerationByWar =
            new Dictionary<long, int>();
        private static readonly Queue<TargetInvalidationWork>
            TargetInvalidationQueue =
                new Queue<TargetInvalidationWork>();

        public static void OnWarStarted(War pWar)
        {
            if (pWar?.data != null) RequestRefresh(pWar.data.id);
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            ReleaseWar(pWar.data.id);
        }

        public static void OnWarParticipantChanged(War pWar,
            Kingdom pKingdom)
        {
            if (pWar?.data == null) return;
            if (!IsActiveWar(pWar))
            {
                ReleaseWar(pWar.data.id);
                return;
            }
            RequestRefresh(pWar.data.id);
        }

        public static void OnArmyInvalidated(long pArmyId)
        {
            if (pArmyId >= 0L) Ledger.ReleaseArmy(pArmyId);
        }

        public static void OnTargetInvalidated(City pCity)
        {
            if (pCity?.data == null ||
                !WarIdsByTarget.TryGetValue(pCity.id,
                    out HashSet<long> indexedWars)) return;
            WarIdsByTarget.Remove(pCity.id);
            TargetInvalidationQueue.Enqueue(new TargetInvalidationWork(
                pCity.id, indexedWars));
        }

        public static bool TryResolveTarget(War pWar,
            Kingdom pParticipant, Army pArmy, bool pCommit,
            out City pTarget, out long pTaskId)
        {
            pTarget = null;
            pTaskId = -1L;
            if (!IsActiveWar(pWar) || pParticipant?.data == null ||
                pArmy?.data == null) return false;
            CoalitionWarSide side;
            bool participantOnSide;
            try
            {
                if (pWar.isAttacker(pParticipant))
                {
                    side = CoalitionWarSide.Attackers;
                    participantOnSide = true;
                }
                else if (pWar.isDefender(pParticipant))
                {
                    side = CoalitionWarSide.Defenders;
                    participantOnSide = true;
                }
                else return false;
            }
            catch { return false; }

            Army indexed = ArmyFieldIndexService.ResolveIndexedArmy(
                pArmy.id, pParticipant.id);
            Kingdom armyKingdom = SafeKingdom(pArmy);
            if (indexed != pArmy || armyKingdom?.data == null ||
                armyKingdom.id != pParticipant.id) return false;

            long worldDay = CurrentWorldDay();
            CoalitionWarTaskSpec selected;
            if (pCommit)
            {
                if (!Ledger.TryClaim(pWar.data.id, side,
                        pParticipant.id, pArmy.id, armyKingdom.id,
                        participantOnSide, worldDay,
                        out CoalitionWarTaskClaim claim)) return false;
                pTaskId = claim.TaskId;
                pTarget = FindCity(claim.TargetCityId);
            }
            else
            {
                if (!Ledger.TrySelect(pWar.data.id, side,
                        pParticipant.id, worldDay, out selected))
                    return false;
                pTaskId = selected.TaskId;
                pTarget = FindCity(selected.TargetCityId);
            }
            if (IsValidTarget(pWar, pParticipant, pTarget)) return true;
            if (pCommit) Ledger.ReleaseArmy(pArmy.id);
            pTarget = null;
            pTaskId = -1L;
            return false;
        }

        public static void ReleaseArmyClaim(long pArmyId)
        {
            Ledger.ReleaseParticipantClaim(pArmyId);
        }

        public static bool ReleaseObjectiveClaim(long pWarId,
            long pArmyId, long pTargetCityId)
        {
            if (pWarId < 0L || pArmyId < 0L || pTargetCityId < 0L ||
                !Ledger.TryGetClaim(pArmyId,
                    out CoalitionWarTaskClaim claim) ||
                claim.WarId != pWarId ||
                claim.TargetCityId != pTargetCityId)
                return false;
            return Ledger.ReleaseArmy(pArmyId);
        }

        public static void ClearLeaderReservations(Kingdom pLeader)
        {
            if (pLeader?.data == null) return;
            Ledger.ClearLeaderReservations(pLeader.id);
        }

        public static bool IsWarLeader(War pWar, Kingdom pKingdom)
        {
            return TryResolveLeaderSide(pWar, pKingdom, out _);
        }

        public static IReadOnlyDictionary<long, int>
            ExternalReservationCounts(War pWar, Kingdom pLeader)
        {
            if (!TryResolveLeaderSide(pWar, pLeader,
                    out CoalitionWarSide side))
                return new Dictionary<long, int>();
            return Ledger.ClaimCountsByTarget(pWar.data.id, side,
                pLeader.id, CurrentWorldDay());
        }

        public static int ReplaceLeaderReservations(War pWar,
            Kingdom pLeader,
            IReadOnlyList<CoalitionLeaderReservationSpec> pReservations)
        {
            if (pReservations == null ||
                !TryResolveLeaderSide(pWar, pLeader,
                    out CoalitionWarSide side)) return 0;
            return Ledger.ReplaceLeaderReservations(pWar.data.id, side,
                pLeader.id, CurrentWorldDay(), pReservations);
        }

        public static void ProcessFrame()
        {
            ProcessTargetInvalidations();
            long worldDay = CurrentWorldDay();
            long warId;
            if (!TryTakeDirtyWar(out warId) &&
                !TryTakeDueWar(worldDay, out warId)) return;
            War war = FindWar(warId);
            if (IsActiveWar(war)) RefreshWar(war, worldDay);
            else ReleaseWar(warId);
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.wars == null) return;
            foreach (War war in World.world.wars)
                if (IsActiveWar(war)) RequestRefresh(war.data.id);
        }

        public static void ClearRuntime()
        {
            Ledger.Clear();
            DirtyWarIds.Clear();
            TargetIdsByWar.Clear();
            WarIdsByTarget.Clear();
            RefreshWarIdsByDay.Clear();
            RefreshDayByWarId.Clear();
            foreach (RefreshWork work in RefreshWorkByWar.Values)
                work.Dispose();
            RefreshWorkByWar.Clear();
            RefreshGenerationByWar.Clear();
            while (TargetInvalidationQueue.Count > 0)
                TargetInvalidationQueue.Dequeue().Dispose();
        }

        private static void RefreshWar(War pWar, long pWorldDay)
        {
            if (!IsActiveWar(pWar)) return;
            long warId = pWar.data.id;
            DirtyWarIds.Remove(warId);
            if (!RefreshGenerationByWar.TryGetValue(warId,
                    out int generation))
            {
                generation = 1;
                RefreshGenerationByWar[warId] = generation;
            }
            if (RefreshWorkByWar.TryGetValue(warId,
                    out RefreshWork stale) &&
                stale.Generation != generation)
            {
                RemoveRefreshWork(warId);
                stale = null;
            }
            if (stale == null)
            {
                long expiry = SaturatingAdd(pWorldDay,
                    CoalitionWarTaskRules.TaskLifetimeWorldDays);
                stale = new RefreshWork(pWar, generation, expiry);
                RefreshWorkByWar[warId] = stale;
            }

            stale.Advance();
            if (!CoalitionWarTaskRefreshRules.ShouldPublish(
                    stale.Attackers.Complete,
                    stale.Defenders.Complete))
            {
                MarkDirty(warId);
                return;
            }

            RemoveTargetMappings(warId);
            Kingdom attacker = stale.Attackers.Leader;
            Kingdom defender = stale.Defenders.Leader;
            if (attacker?.data != null)
                PublishSide(pWar, CoalitionWarSide.Attackers, attacker,
                    stale.Attackers.Tasks);
            else Ledger.ReleaseSide(warId, CoalitionWarSide.Attackers);
            if (defender?.data != null)
                PublishSide(pWar, CoalitionWarSide.Defenders, defender,
                    stale.Defenders.Tasks);
            else Ledger.ReleaseSide(warId, CoalitionWarSide.Defenders);
            long dueDay = SaturatingAdd(pWorldDay,
                CoalitionWarTaskRules.TaskLifetimeWorldDays);
            RemoveRefreshWork(warId);
            ScheduleRefresh(warId, dueDay);
        }

        private static void PublishSide(War pWar, CoalitionWarSide pSide,
            Kingdom pLeader,
            IReadOnlyList<CoalitionWarTaskSpec> pTasks)
        {
            Ledger.Publish(pWar.data.id, pSide, pLeader.id, pLeader.id,
                pTasks);
            for (int i = 0; i < pTasks.Count; i++)
                RegisterTargetMapping(pWar.data.id,
                    pTasks[i].TargetCityId);
        }

        private static bool IsValidTarget(War pWar, Kingdom pParticipant,
            City pCity)
        {
            ArmyRtsObjectiveState state = ArmyRtsObjectiveService.Classify(
                pWar, pParticipant, pCity);
            return state == ArmyRtsObjectiveState.OpenAttack ||
                   state == ArmyRtsObjectiveState.OpenDefense;
        }

        private static void RegisterTargetMapping(long pWarId,
            long pTargetCityId)
        {
            if (!TargetIdsByWar.TryGetValue(pWarId,
                    out HashSet<long> targets))
            {
                targets = new HashSet<long>();
                TargetIdsByWar[pWarId] = targets;
            }
            targets.Add(pTargetCityId);
            if (!WarIdsByTarget.TryGetValue(pTargetCityId,
                    out HashSet<long> wars))
            {
                wars = new HashSet<long>();
                WarIdsByTarget[pTargetCityId] = wars;
            }
            wars.Add(pWarId);
        }

        private static void RemoveTargetMappings(long pWarId)
        {
            if (!TargetIdsByWar.TryGetValue(pWarId,
                    out HashSet<long> targets)) return;
            foreach (long targetId in targets)
            {
                if (!WarIdsByTarget.TryGetValue(targetId,
                        out HashSet<long> wars)) continue;
                wars.Remove(pWarId);
                if (wars.Count == 0) WarIdsByTarget.Remove(targetId);
            }
            TargetIdsByWar.Remove(pWarId);
        }

        private static void ReleaseWar(long pWarId)
        {
            RemoveRefreshWork(pWarId);
            Ledger.ReleaseWar(pWarId);
            DirtyWarIds.Remove(pWarId);
            RemoveTargetMappings(pWarId);
            UnscheduleRefresh(pWarId);
            RefreshGenerationByWar.Remove(pWarId);
        }

        private static void ScheduleRefresh(long pWarId, long pDueDay)
        {
            UnscheduleRefresh(pWarId);
            if (!RefreshWarIdsByDay.TryGetValue(pDueDay,
                    out SortedSet<long> wars))
            {
                wars = new SortedSet<long>();
                RefreshWarIdsByDay[pDueDay] = wars;
            }
            wars.Add(pWarId);
            RefreshDayByWarId[pWarId] = pDueDay;
        }

        private static void UnscheduleRefresh(long pWarId)
        {
            if (!RefreshDayByWarId.TryGetValue(pWarId,
                    out long dueDay)) return;
            RefreshDayByWarId.Remove(pWarId);
            if (!RefreshWarIdsByDay.TryGetValue(dueDay,
                    out SortedSet<long> wars)) return;
            wars.Remove(pWarId);
            if (wars.Count == 0) RefreshWarIdsByDay.Remove(dueDay);
        }

        private static bool TryTakeDueWar(long pWorldDay, out long pWarId)
        {
            pWarId = -1L;
            long dueDay = -1L;
            SortedSet<long> dueWars = null;
            foreach (KeyValuePair<long, SortedSet<long>> pair in
                     RefreshWarIdsByDay)
            {
                if (pair.Key > pWorldDay) break;
                dueDay = pair.Key;
                dueWars = pair.Value;
                break;
            }
            if (dueWars == null || dueWars.Count == 0) return false;
            using (SortedSet<long>.Enumerator enumerator =
                   dueWars.GetEnumerator())
                if (enumerator.MoveNext()) pWarId = enumerator.Current;
            if (pWarId < 0L) return false;
            dueWars.Remove(pWarId);
            if (dueWars.Count == 0) RefreshWarIdsByDay.Remove(dueDay);
            RefreshDayByWarId.Remove(pWarId);
            return true;
        }

        private static void MarkDirty(long pWarId)
        {
            if (pWarId >= 0L) DirtyWarIds.Add(pWarId);
        }

        private static void RequestRefresh(long pWarId)
        {
            if (pWarId < 0L) return;
            int generation = RefreshGenerationByWar.TryGetValue(pWarId,
                out int previous) && previous < int.MaxValue
                ? previous + 1
                : 1;
            RefreshGenerationByWar[pWarId] = generation;
            RemoveRefreshWork(pWarId);
            UnscheduleRefresh(pWarId);
            MarkDirty(pWarId);
        }

        private static void ProcessTargetInvalidations()
        {
            int remaining = CoalitionWarTaskRefreshRules.
                MaximumTargetInvalidationsPerWorkItem;
            while (remaining > 0 && TargetInvalidationQueue.Count > 0)
            {
                TargetInvalidationWork work =
                    TargetInvalidationQueue.Peek();
                if (!work.TryTake(out long warId))
                {
                    TargetInvalidationQueue.Dequeue();
                    work.Dispose();
                    continue;
                }

                remaining--;
                Ledger.ReleaseTarget(warId, work.TargetCityId);
                if (TargetIdsByWar.TryGetValue(warId,
                        out HashSet<long> targets))
                    targets.Remove(work.TargetCityId);
                War war = FindWar(warId);
                if (IsActiveWar(war)) RequestRefresh(warId);
                else ReleaseWar(warId);
            }
        }

        private static void RemoveRefreshWork(long pWarId)
        {
            if (!RefreshWorkByWar.TryGetValue(pWarId,
                    out RefreshWork work)) return;
            RefreshWorkByWar.Remove(pWarId);
            work.Dispose();
        }

        private static bool TryTakeDirtyWar(out long pWarId)
        {
            pWarId = -1L;
            if (DirtyWarIds.Count == 0) return false;
            using (SortedSet<long>.Enumerator enumerator =
                   DirtyWarIds.GetEnumerator())
                if (enumerator.MoveNext()) pWarId = enumerator.Current;
            if (pWarId < 0L) return false;
            DirtyWarIds.Remove(pWarId);
            return true;
        }

        private static IEnumerator<Kingdom> CreateParticipantEnumerator(
            War pWar, CoalitionWarSide pSide)
        {
            if (pWar == null) return null;
            try
            {
                IEnumerable<Kingdom> participants = pSide ==
                    CoalitionWarSide.Attackers
                    ? pWar.getDefenders()
                    : pWar.getAttackers();
                return participants?.GetEnumerator();
            }
            catch { }
            return null;
        }

        private static long StableTaskId(long pCityId,
            CoalitionWarSide pSide)
        {
            unchecked
            {
                return ((pCityId << 1) ^ (long)pSide) & long.MaxValue;
            }
        }

        private static int SafeWarriorCount(City pCity)
        {
            try { return Math.Max(0, pCity?.countWarriors() ?? 0); }
            catch { return 0; }
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

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return pArmy?.getKingdom(); }
            catch { return null; }
        }

        private static bool TryResolveLeaderSide(War pWar,
            Kingdom pLeader, out CoalitionWarSide pSide)
        {
            pSide = CoalitionWarSide.Attackers;
            if (!IsActiveWar(pWar) || pLeader?.data == null) return false;
            Kingdom attacker = SafeMainAttacker(pWar);
            if (attacker?.id == pLeader.id) return true;
            Kingdom defender = SafeMainDefender(pWar);
            if (defender?.id != pLeader.id) return false;
            pSide = CoalitionWarSide.Defenders;
            return true;
        }

        private static War FindWar(long pWarId)
        {
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static bool IsActiveWar(War pWar)
        {
            try { return pWar?.data != null && !pWar.hasEnded(); }
            catch { return false; }
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

        private static long SaturatingAdd(long pValue, int pDelta)
        {
            long delta = Math.Max(0, pDelta);
            return pValue > long.MaxValue - delta
                ? long.MaxValue
                : pValue + delta;
        }
    }
}
