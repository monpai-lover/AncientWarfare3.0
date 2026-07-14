using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public readonly struct HistoricalSchoolIndexEntry
    {
        public HistoricalSchoolIndexEntry(
            long pActorId,
            string pSchoolId,
            long pResidenceCityId,
            HistoricalSchoolStanding pStanding,
            bool pPresent,
            bool pTravelling,
            long pServiceKingdomId,
            int pTravelBucket = 0)
        {
            ActorId = pActorId;
            SchoolId = pSchoolId ?? "";
            ResidenceCityId = pResidenceCityId;
            Standing = pStanding;
            Present = pPresent;
            Travelling = pTravelling;
            ServiceKingdomId = pServiceKingdomId;
            TravelBucket = pTravelling ? Math.Max(0, pTravelBucket) : -1;
        }

        public long ActorId { get; }
        public string SchoolId { get; }
        public long ResidenceCityId { get; }
        public HistoricalSchoolStanding Standing { get; }
        public bool Present { get; }
        public bool Travelling { get; }
        public long ServiceKingdomId { get; }
        public int TravelBucket { get; }
        public bool IsValid => ActorId >= 0 && !string.IsNullOrEmpty(SchoolId);
    }

    public sealed class HistoricalSchoolRuntimeIndex
    {
        private static readonly HistoricalSchoolRuntimeIndex Shared =
            new HistoricalSchoolRuntimeIndex();

        private readonly Dictionary<long, HistoricalSchoolIndexEntry> _byActor =
            new Dictionary<long, HistoricalSchoolIndexEntry>();
        private readonly Dictionary<string, HashSet<long>> _membersBySchool =
            new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<long>> _teachersBySchool =
            new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<long>> _leadersBySchool =
            new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        private readonly Dictionary<long, Dictionary<string, HashSet<long>>>
            _presentByCitySchool =
                new Dictionary<long, Dictionary<string, HashSet<long>>>();
        private readonly Dictionary<int, HashSet<long>> _travelByBucket =
            new Dictionary<int, HashSet<long>>();
        private readonly Dictionary<long, HashSet<long>> _servingByKingdom =
            new Dictionary<long, HashSet<long>>();
        private readonly HashSet<long> _livingXiaCities = new HashSet<long>();

        public static HistoricalSchoolRuntimeIndex Instance => Shared;
        public int Count => _byActor.Count;
        public int LivingXiaCityCount => _livingXiaCities.Count;

        public void Upsert(HistoricalSchoolIndexEntry pEntry)
        {
            Remove(pEntry.ActorId);
            if (!pEntry.IsValid) return;

            _byActor[pEntry.ActorId] = pEntry;
            Add(_membersBySchool, pEntry.SchoolId, pEntry.ActorId);
            if (IsTeacher(pEntry.Standing))
                Add(_teachersBySchool, pEntry.SchoolId, pEntry.ActorId);
            if (IsLeader(pEntry.Standing))
                Add(_leadersBySchool, pEntry.SchoolId, pEntry.ActorId);
            if (pEntry.Present && pEntry.ResidenceCityId >= 0)
                AddPresent(pEntry.ResidenceCityId, pEntry.SchoolId, pEntry.ActorId);
            if (pEntry.Travelling && pEntry.TravelBucket >= 0)
                Add(_travelByBucket, pEntry.TravelBucket, pEntry.ActorId);
            if (pEntry.ServiceKingdomId >= 0)
                Add(_servingByKingdom, pEntry.ServiceKingdomId, pEntry.ActorId);
        }

        public bool Remove(long pActorId)
        {
            if (!_byActor.TryGetValue(pActorId, out HistoricalSchoolIndexEntry old))
                return false;

            _byActor.Remove(pActorId);
            Remove(_membersBySchool, old.SchoolId, pActorId);
            if (IsTeacher(old.Standing))
                Remove(_teachersBySchool, old.SchoolId, pActorId);
            if (IsLeader(old.Standing))
                Remove(_leadersBySchool, old.SchoolId, pActorId);
            if (old.Present && old.ResidenceCityId >= 0)
                RemovePresent(old.ResidenceCityId, old.SchoolId, pActorId);
            if (old.Travelling && old.TravelBucket >= 0)
                Remove(_travelByBucket, old.TravelBucket, pActorId);
            if (old.ServiceKingdomId >= 0)
                Remove(_servingByKingdom, old.ServiceKingdomId, pActorId);
            return true;
        }

        public bool TryGet(long pActorId, out HistoricalSchoolIndexEntry pEntry)
        {
            return _byActor.TryGetValue(pActorId, out pEntry);
        }

        public int MemberCount(string pSchoolId) =>
            BucketCount(_membersBySchool, pSchoolId);
        public int TeacherCount(string pSchoolId) =>
            BucketCount(_teachersBySchool, pSchoolId);
        public int LeaderCount(string pSchoolId) =>
            BucketCount(_leadersBySchool, pSchoolId);
        public int TravellingCount(int pBucket) =>
            BucketCount(_travelByBucket, pBucket);
        public int ServingCount(long pKingdomId) =>
            BucketCount(_servingByKingdom, pKingdomId);

        public int ResidentCount(long pCityId, string pSchoolId)
        {
            if (!_presentByCitySchool.TryGetValue(pCityId, out var schools) ||
                string.IsNullOrEmpty(pSchoolId) ||
                !schools.TryGetValue(pSchoolId, out HashSet<long> actors))
                return 0;
            return actors.Count;
        }

        public long[] MemberIds(string pSchoolId) =>
            CopyStable(_membersBySchool, pSchoolId);

        public long[] TeacherIds(string pSchoolId) =>
            CopyStable(_teachersBySchool, pSchoolId);

        public long[] LeaderIds(string pSchoolId) =>
            CopyStable(_leadersBySchool, pSchoolId);

        public long[] ResidentIds(long pCityId, string pSchoolId)
        {
            if (!_presentByCitySchool.TryGetValue(pCityId, out var schools) ||
                string.IsNullOrEmpty(pSchoolId) ||
                !schools.TryGetValue(pSchoolId, out HashSet<long> actors))
                return Array.Empty<long>();
            return CopyStable(actors);
        }

        public long[] TravellingIds(int pBucket) =>
            CopyStable(_travelByBucket, pBucket);

        public long[] ServingIds(long pKingdomId) =>
            CopyStable(_servingByKingdom, pKingdomId);

        public void SetLivingXiaCity(long pCityId, bool pLivingXia)
        {
            if (pCityId < 0) return;
            if (pLivingXia) _livingXiaCities.Add(pCityId);
            else _livingXiaCities.Remove(pCityId);
        }

        public long[] LivingXiaCityIds() => CopyStable(_livingXiaCities);

        public void Clear()
        {
            ClearMembers();
            _livingXiaCities.Clear();
        }

        public void ClearMembers()
        {
            _byActor.Clear();
            _membersBySchool.Clear();
            _teachersBySchool.Clear();
            _leadersBySchool.Clear();
            _presentByCitySchool.Clear();
            _travelByBucket.Clear();
            _servingByKingdom.Clear();
        }

        public void ClearLivingXiaCities() => _livingXiaCities.Clear();

        private static bool IsTeacher(HistoricalSchoolStanding pStanding)
        {
            return pStanding == HistoricalSchoolStanding.Teacher ||
                   pStanding == HistoricalSchoolStanding.Leader ||
                   pStanding == HistoricalSchoolStanding.CanonicalMaster;
        }

        private static bool IsLeader(HistoricalSchoolStanding pStanding)
        {
            return pStanding == HistoricalSchoolStanding.Leader ||
                   pStanding == HistoricalSchoolStanding.CanonicalMaster;
        }

        private void AddPresent(long pCityId, string pSchoolId, long pActorId)
        {
            if (!_presentByCitySchool.TryGetValue(pCityId, out var schools))
            {
                schools = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
                _presentByCitySchool.Add(pCityId, schools);
            }
            Add(schools, pSchoolId, pActorId);
        }

        private void RemovePresent(long pCityId, string pSchoolId, long pActorId)
        {
            if (!_presentByCitySchool.TryGetValue(pCityId, out var schools)) return;
            Remove(schools, pSchoolId, pActorId);
            if (schools.Count == 0) _presentByCitySchool.Remove(pCityId);
        }

        private static void Add<TKey>(
            Dictionary<TKey, HashSet<long>> pBuckets,
            TKey pKey,
            long pActorId)
        {
            if (!pBuckets.TryGetValue(pKey, out HashSet<long> actors))
            {
                actors = new HashSet<long>();
                pBuckets.Add(pKey, actors);
            }
            actors.Add(pActorId);
        }

        private static void Remove<TKey>(
            Dictionary<TKey, HashSet<long>> pBuckets,
            TKey pKey,
            long pActorId)
        {
            if (!pBuckets.TryGetValue(pKey, out HashSet<long> actors)) return;
            actors.Remove(pActorId);
            if (actors.Count == 0) pBuckets.Remove(pKey);
        }

        private static int BucketCount<TKey>(
            Dictionary<TKey, HashSet<long>> pBuckets,
            TKey pKey)
        {
            return pBuckets.TryGetValue(pKey, out HashSet<long> actors)
                ? actors.Count
                : 0;
        }

        private static long[] CopyStable<TKey>(
            Dictionary<TKey, HashSet<long>> pBuckets,
            TKey pKey)
        {
            return pBuckets.TryGetValue(pKey, out HashSet<long> actors)
                ? CopyStable(actors)
                : Array.Empty<long>();
        }

        private static long[] CopyStable(HashSet<long> pActors)
        {
            if (pActors == null || pActors.Count == 0) return Array.Empty<long>();
            var result = new long[pActors.Count];
            pActors.CopyTo(result);
            Array.Sort(result);
            return result;
        }
    }
}
