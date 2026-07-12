using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.schools
{
    public sealed class HistoricalSchoolAnnualMemberSnapshot<TActor> where TActor : class
    {
        private readonly SortedDictionary<long, TActor> _livingByActor =
            new SortedDictionary<long, TActor>();
        private readonly Dictionary<string, SortedDictionary<long, TActor>> _livingBySchool =
            new Dictionary<string, SortedDictionary<long, TActor>>(StringComparer.Ordinal);
        private readonly Dictionary<long, SchoolMembershipRecord> _activeByActor =
            new Dictionary<long, SchoolMembershipRecord>();
        private readonly Dictionary<long, int> _directDiscipleCounts =
            new Dictionary<long, int>();
        private readonly HashSet<string> _unreliableLivingCountSchools =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Func<TActor, long> _actorId;
        private readonly Func<TActor, bool> _alive;
        private readonly Func<TActor, bool> _canonical;
        private readonly Func<TActor, long> _residenceCityId;
        private readonly Func<TActor, bool> _present;

        public HistoricalSchoolAnnualMemberSnapshot(
            IEnumerable<SchoolMembershipRecord> pActiveMemberships,
            Func<long, TActor> pResolveActor, Func<TActor, long> pActorId,
            Func<TActor, bool> pAlive, Func<TActor, bool> pCanonical,
            Func<TActor, long> pResidenceCityId, Func<TActor, bool> pPresent)
        {
            if (pResolveActor == null) throw new ArgumentNullException(nameof(pResolveActor));
            _actorId = pActorId ?? throw new ArgumentNullException(nameof(pActorId));
            _alive = pAlive ?? throw new ArgumentNullException(nameof(pAlive));
            _canonical = pCanonical ?? throw new ArgumentNullException(nameof(pCanonical));
            _residenceCityId = pResidenceCityId ??
                               throw new ArgumentNullException(nameof(pResidenceCityId));
            _present = pPresent ?? throw new ArgumentNullException(nameof(pPresent));

            foreach (SchoolMembershipRecord record in
                     pActiveMemberships ?? Array.Empty<SchoolMembershipRecord>())
            {
                if (!ValidActive(record) || _activeByActor.ContainsKey(record.ActorId)) continue;
                _activeByActor[record.ActorId] = record;
                AdjustDirectDiscipleCount(record, 1);
                TActor actor;
                try { actor = pResolveActor(record.ActorId); }
                catch
                {
                    MarkLivingCountUnreliable(record.SchoolId);
                    continue;
                }
                if (!TryIsLivingActor(actor, record.ActorId, out bool living))
                {
                    MarkLivingCountUnreliable(record.SchoolId);
                    continue;
                }
                if (living) AddLiving(record, actor);
            }
        }

        public IReadOnlyDictionary<long, int> DirectDiscipleCounts =>
            _directDiscipleCounts;

        public IReadOnlyList<TActor> LivingMembers()
        {
            return _livingByActor.Values.ToArray();
        }

        public IReadOnlyList<TActor> LivingMembers(string pSchoolId)
        {
            return pSchoolId != null && _livingBySchool.TryGetValue(pSchoolId,
                out SortedDictionary<long, TActor> members)
                ? members.Values.ToArray()
                : Array.Empty<TActor>();
        }

        public IEnumerable<TActor> EnumerateLivingMembers(string pSchoolId, int pLimit,
            Func<TActor, bool> pEligible)
        {
            if (pLimit <= 0 || pSchoolId == null ||
                !_livingBySchool.TryGetValue(pSchoolId,
                    out SortedDictionary<long, TActor> members)) yield break;
            int selected = 0;
            foreach (KeyValuePair<long, TActor> item in members)
            {
                if (pEligible != null && !pEligible(item.Value)) continue;
                yield return item.Value;
                if (++selected >= pLimit) yield break;
            }
        }

        public int LivingCount(string pSchoolId)
        {
            return pSchoolId != null && _livingBySchool.TryGetValue(pSchoolId,
                out SortedDictionary<long, TActor> members) ? members.Count : 0;
        }

        public bool HasReliableLivingCount(string pSchoolId)
        {
            return CourtSchoolRegistry.Find(pSchoolId) != null &&
                   !_unreliableLivingCountSchools.Contains(pSchoolId);
        }

        public bool TryGetLivingCount(string pSchoolId, out int pCount)
        {
            pCount = 0;
            if (!HasReliableLivingCount(pSchoolId)) return false;
            pCount = LivingCount(pSchoolId);
            return true;
        }

        public IReadOnlyList<TActor> QualifiedTeachers(int pYear, int pCapacity)
        {
            if (pCapacity <= 0 || _livingByActor.Count == 0) return Array.Empty<TActor>();
            var selected = new List<TActor>(Math.Min(pCapacity, _livingByActor.Count));
            foreach (KeyValuePair<long, TActor> item in _livingByActor)
            {
                if (selected.Count >= pCapacity) break;
                SchoolMembershipRecord record = _activeByActor[item.Key];
                if (SafeCanonical(item.Value) && IsQualified(record, pCanonical: true))
                    selected.Add(item.Value);
            }

            int remaining = pCapacity - selected.Count;
            if (remaining <= 0) return selected;
            var later = new List<TeacherCandidate>(remaining);
            foreach (KeyValuePair<long, TActor> item in _livingByActor)
            {
                SchoolMembershipRecord record = _activeByActor[item.Key];
                if (SafeCanonical(item.Value) || !IsQualified(record, pCanonical: false))
                    continue;
                var candidate = new TeacherCandidate(item.Key,
                    HistoricalSchoolRules.TeacherOrder(item.Key, pYear),
                    SchoolOrdinal(record.SchoolId), item.Value);
                InsertBounded(later, candidate, remaining);
            }
            foreach (TeacherCandidate candidate in later) selected.Add(candidate.Actor);
            return selected;
        }

        public Dictionary<string, HashSet<long>> BuildAvailableTeacherIndex()
        {
            var result = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
            foreach (KeyValuePair<long, TActor> item in _livingByActor)
            {
                SchoolMembershipRecord record = _activeByActor[item.Key];
                bool canonical = SafeCanonical(item.Value);
                if (!IsQualified(record, canonical) || !SafePresent(item.Value)) continue;
                long cityId = SafeResidenceCityId(item.Value);
                if (cityId < 0) continue;
                string key = record.SchoolId + ":" + cityId;
                if (!result.TryGetValue(key, out HashSet<long> actors))
                {
                    actors = new HashSet<long>();
                    result[key] = actors;
                }
                actors.Add(item.Key);
            }
            return result;
        }

        public bool ApplyMembershipChange(SchoolMembershipRecord pPrevious,
            SchoolMembershipRecord pCurrent, TActor pActor)
        {
            long actorId = pCurrent?.ActorId ?? pPrevious?.ActorId ?? -1L;
            if (actorId < 0 || (pCurrent != null && !ValidActive(pCurrent)) ||
                (pPrevious != null && pPrevious.ActorId != actorId) ||
                (pCurrent != null && pCurrent.ActorId != actorId)) return false;
            _activeByActor.TryGetValue(actorId, out SchoolMembershipRecord existing);
            if (SameRecord(existing, pCurrent)) return false;
            if (pPrevious == null)
            {
                if (existing != null) return false;
            }
            else if (!SameRecord(existing, pPrevious))
                return false;

            if (existing != null)
            {
                RemoveLiving(existing, actorId);
                AdjustDirectDiscipleCount(existing, -1);
                _activeByActor.Remove(actorId);
            }
            if (pCurrent == null) return true;

            _activeByActor[actorId] = pCurrent;
            AdjustDirectDiscipleCount(pCurrent, 1);
            if (!TryIsLivingActor(pActor, actorId, out bool living))
                MarkLivingCountUnreliable(pCurrent.SchoolId);
            else if (living)
                AddLiving(pCurrent, pActor);
            return true;
        }

        private void AddLiving(SchoolMembershipRecord pRecord, TActor pActor)
        {
            _livingByActor[pRecord.ActorId] = pActor;
            if (!_livingBySchool.TryGetValue(pRecord.SchoolId,
                    out SortedDictionary<long, TActor> members))
            {
                members = new SortedDictionary<long, TActor>();
                _livingBySchool[pRecord.SchoolId] = members;
            }
            members[pRecord.ActorId] = pActor;
        }

        private void RemoveLiving(SchoolMembershipRecord pRecord, long pActorId)
        {
            _livingByActor.Remove(pActorId);
            if (!_livingBySchool.TryGetValue(pRecord.SchoolId,
                    out SortedDictionary<long, TActor> members)) return;
            members.Remove(pActorId);
            if (members.Count == 0) _livingBySchool.Remove(pRecord.SchoolId);
        }

        private void AdjustDirectDiscipleCount(SchoolMembershipRecord pRecord, int pDelta)
        {
            if (pRecord == null || pRecord.TeacherActorId < 0 ||
                (pRecord.Source != SchoolMembershipSource.DirectDiscipleship &&
                 pRecord.Source != SchoolMembershipSource.LaterDiscipleship)) return;
            _directDiscipleCounts.TryGetValue(pRecord.TeacherActorId, out int count);
            count += pDelta;
            if (count > 0) _directDiscipleCounts[pRecord.TeacherActorId] = count;
            else _directDiscipleCounts.Remove(pRecord.TeacherActorId);
        }

        private bool TryIsLivingActor(TActor pActor, long pExpectedActorId,
            out bool pLiving)
        {
            pLiving = false;
            if (pActor == null) return true;
            try
            {
                if (_actorId(pActor) != pExpectedActorId) return false;
                pLiving = _alive(pActor);
                return true;
            }
            catch { return false; }
        }

        private void MarkLivingCountUnreliable(string pSchoolId)
        {
            if (CourtSchoolRegistry.Find(pSchoolId) != null)
                _unreliableLivingCountSchools.Add(pSchoolId);
        }

        private bool SafeCanonical(TActor pActor)
        {
            try { return pActor != null && _canonical(pActor); }
            catch { return false; }
        }

        private bool SafePresent(TActor pActor)
        {
            try { return pActor != null && _present(pActor); }
            catch { return false; }
        }

        private long SafeResidenceCityId(TActor pActor)
        {
            try { return pActor == null ? -1L : _residenceCityId(pActor); }
            catch { return -1L; }
        }

        private static bool IsQualified(SchoolMembershipRecord pRecord, bool pCanonical)
        {
            if (pCanonical) return true;
            if (pRecord == null || pRecord.Reputation < 10f) return false;
            return pRecord.Source == SchoolMembershipSource.DirectDiscipleship ||
                   pRecord.Source == SchoolMembershipSource.LaterDiscipleship ||
                   pRecord.Source == SchoolMembershipSource.ExplicitConversion ||
                   pRecord.Source == SchoolMembershipSource.PreservedWork;
        }

        private static bool ValidActive(SchoolMembershipRecord pRecord)
        {
            return pRecord != null && pRecord.Active && pRecord.IsValid;
        }

        private static bool SameRecord(SchoolMembershipRecord pFirst,
            SchoolMembershipRecord pSecond)
        {
            if (ReferenceEquals(pFirst, pSecond)) return true;
            return pFirst != null && pSecond != null &&
                   pFirst.MembershipId == pSecond.MembershipId &&
                   pFirst.ActorId == pSecond.ActorId && pFirst.SchoolId == pSecond.SchoolId &&
                   pFirst.Source == pSecond.Source && pFirst.SourceId == pSecond.SourceId &&
                   pFirst.TeacherActorId == pSecond.TeacherActorId &&
                   pFirst.CityId == pSecond.CityId && pFirst.Generation == pSecond.Generation &&
                   pFirst.Reputation.Equals(pSecond.Reputation) &&
                   pFirst.StartYear == pSecond.StartYear && pFirst.EndYear == pSecond.EndYear &&
                   pFirst.Active == pSecond.Active && pFirst.EndReason == pSecond.EndReason;
        }

        private static void InsertBounded(List<TeacherCandidate> pCandidates,
            TeacherCandidate pCandidate, int pCapacity)
        {
            int low = 0;
            int high = pCandidates.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (Compare(pCandidates[middle], pCandidate) <= 0) low = middle + 1;
                else high = middle;
            }
            if (low >= pCapacity) return;
            pCandidates.Insert(low, pCandidate);
            if (pCandidates.Count > pCapacity) pCandidates.RemoveAt(pCapacity);
        }

        private static int Compare(TeacherCandidate pFirst, TeacherCandidate pSecond)
        {
            int order = pFirst.Order.CompareTo(pSecond.Order);
            if (order != 0) return order;
            int schoolOrder = pFirst.SchoolOrdinal.CompareTo(pSecond.SchoolOrdinal);
            return schoolOrder != 0
                ? schoolOrder
                : pFirst.ActorId.CompareTo(pSecond.ActorId);
        }

        private static int SchoolOrdinal(string pSchoolId)
        {
            for (int index = 0; index < CourtSchoolRegistry.All.Count; index++)
                if (string.Equals(CourtSchoolRegistry.All[index].Id, pSchoolId,
                        StringComparison.Ordinal)) return index;
            return int.MaxValue;
        }

        private sealed class TeacherCandidate
        {
            public TeacherCandidate(long pActorId, long pOrder, int pSchoolOrdinal,
                TActor pActor)
            {
                ActorId = pActorId;
                Order = pOrder;
                SchoolOrdinal = pSchoolOrdinal;
                Actor = pActor;
            }

            public long ActorId { get; }
            public long Order { get; }
            public int SchoolOrdinal { get; }
            public TActor Actor { get; }
        }
    }
}
