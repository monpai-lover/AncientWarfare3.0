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
            int pTravelBucket = 0,
            int pPromotionDueYear = -1,
            bool pTravelEligible = false)
        {
            ActorId = pActorId;
            SchoolId = pSchoolId ?? "";
            ResidenceCityId = pResidenceCityId;
            Standing = pStanding;
            Present = pPresent;
            Travelling = pTravelling;
            TravelEligible = pTravelEligible || pTravelling;
            ServiceKingdomId = pServiceKingdomId;
            TravelBucket = TravelEligible ? Math.Max(0, pTravelBucket) : -1;
            PromotionDueYear = pPromotionDueYear;
        }

        public long ActorId { get; }
        public string SchoolId { get; }
        public long ResidenceCityId { get; }
        public HistoricalSchoolStanding Standing { get; }
        public bool Present { get; }
        public bool Travelling { get; }
        public bool TravelEligible { get; }
        public long ServiceKingdomId { get; }
        public int TravelBucket { get; }
        public int PromotionDueYear { get; }
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
        private readonly Dictionary<int, HashSet<long>> _travelEligibleByBucket =
            new Dictionary<int, HashSet<long>>();
        private readonly Dictionary<long, HashSet<long>> _servingByKingdom =
            new Dictionary<long, HashSet<long>>();
        private readonly SortedDictionary<int, HashSet<long>> _promotionByYear =
            new SortedDictionary<int, HashSet<long>>();
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
            if (pEntry.TravelEligible && pEntry.TravelBucket >= 0)
                Add(_travelEligibleByBucket, pEntry.TravelBucket, pEntry.ActorId);
            if (pEntry.ServiceKingdomId >= 0)
                Add(_servingByKingdom, pEntry.ServiceKingdomId, pEntry.ActorId);
            if (pEntry.PromotionDueYear >= 0)
                Add(_promotionByYear, pEntry.PromotionDueYear, pEntry.ActorId);
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
            if (old.TravelEligible && old.TravelBucket >= 0)
                Remove(_travelEligibleByBucket, old.TravelBucket, pActorId);
            if (old.ServiceKingdomId >= 0)
                Remove(_servingByKingdom, old.ServiceKingdomId, pActorId);
            if (old.PromotionDueYear >= 0)
                Remove(_promotionByYear, old.PromotionDueYear, pActorId);
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
        public int TravelEligibleCount(int pBucket) =>
            BucketCount(_travelEligibleByBucket, pBucket);
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

        public long[] TravelEligibleIds(int pBucket) =>
            CopyStable(_travelEligibleByBucket, pBucket);

        public long[] ServingIds(long pKingdomId) =>
            CopyStable(_servingByKingdom, pKingdomId);

        public long[] PromotionDueIds(int pYear)
        {
            if (pYear < 0 || _promotionByYear.Count == 0) return Array.Empty<long>();
            var result = new List<long>();
            foreach (KeyValuePair<int, HashSet<long>> bucket in _promotionByYear)
            {
                if (bucket.Key > pYear) break;
                result.AddRange(bucket.Value);
            }
            if (result.Count == 0) return Array.Empty<long>();
            long[] ids = result.ToArray();
            Array.Sort(ids);
            return ids;
        }

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
            _travelEligibleByBucket.Clear();
            _servingByKingdom.Clear();
            _promotionByYear.Clear();
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
            IDictionary<TKey, HashSet<long>> pBuckets,
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
            IDictionary<TKey, HashSet<long>> pBuckets,
            TKey pKey,
            long pActorId)
        {
            if (!pBuckets.TryGetValue(pKey, out HashSet<long> actors)) return;
            actors.Remove(pActorId);
            if (actors.Count == 0) pBuckets.Remove(pKey);
        }

        private static int BucketCount<TKey>(
            IDictionary<TKey, HashSet<long>> pBuckets,
            TKey pKey)
        {
            return pBuckets.TryGetValue(pKey, out HashSet<long> actors)
                ? actors.Count
                : 0;
        }

        private static long[] CopyStable<TKey>(
            IDictionary<TKey, HashSet<long>> pBuckets,
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
