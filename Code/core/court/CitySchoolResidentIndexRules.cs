using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public sealed class CitySchoolResidentCandidate
    {
        public CitySchoolResidentCandidate(long pActorId, string pSchoolId, long pCityId,
            bool pPresent, bool pQualifiedScholar, int pSchoolOrder)
        {
            ActorId = pActorId;
            SchoolId = pSchoolId ?? "";
            CityId = pCityId;
            Present = pPresent;
            QualifiedScholar = pQualifiedScholar;
            SchoolOrder = pSchoolOrder;
        }

        public long ActorId { get; }
        public string SchoolId { get; }
        public long CityId { get; }
        public bool Present { get; }
        public bool QualifiedScholar { get; }
        public int SchoolOrder { get; }
    }

    public sealed class CitySchoolResidentIndex
    {
        private static readonly IReadOnlyList<long> EmptyActors =
            Array.AsReadOnly(Array.Empty<long>());
        private static readonly IReadOnlyList<string> EmptySchools =
            Array.AsReadOnly(Array.Empty<string>());
        private readonly Dictionary<long, Dictionary<string, int>> _counts;
        private readonly Dictionary<long, IReadOnlyList<long>> _scholars;
        private readonly Dictionary<long, IReadOnlyList<string>> _schools;

        internal CitySchoolResidentIndex(
            Dictionary<long, Dictionary<string, int>> pCounts,
            Dictionary<long, IReadOnlyList<long>> pScholars,
            Dictionary<long, IReadOnlyList<string>> pSchools)
        {
            _counts = pCounts;
            _scholars = pScholars;
            _schools = pSchools;
        }

        public int Count(long pCityId, string pSchoolId)
        {
            if (pCityId < 0 || string.IsNullOrWhiteSpace(pSchoolId) ||
                !_counts.TryGetValue(pCityId, out Dictionary<string, int> cityCounts) ||
                !cityCounts.TryGetValue(pSchoolId, out int count)) return 0;
            return count;
        }

        public IReadOnlyList<long> ScholarActorIds(long pCityId)
        {
            return pCityId >= 0 && _scholars.TryGetValue(pCityId,
                out IReadOnlyList<long> actors)
                ? actors
                : EmptyActors;
        }

        public IReadOnlyList<string> SchoolIds(long pCityId)
        {
            return pCityId >= 0 && _schools.TryGetValue(pCityId,
                out IReadOnlyList<string> schools)
                ? schools
                : EmptySchools;
        }
    }

    public static class SchoolResidenceInvalidationRules
    {
        public static bool ShouldInvalidateActiveMemberMove(bool pOriginalAllowed,
            bool pHasActiveMembership, long pOldCityId, long pNewCityId)
        {
            return pOriginalAllowed && pHasActiveMembership && pOldCityId != pNewCityId;
        }
    }

    public sealed class CitySchoolResidentIndexCache
    {
        private CitySchoolResidentIndex _index;
        private long _membershipVersion;
        private long _residenceRevision;

        public CitySchoolResidentIndex GetOrBuild(long pMembershipVersion,
            long pResidenceRevision, Func<CitySchoolResidentIndex> pBuild)
        {
            if (_index != null && _membershipVersion == pMembershipVersion &&
                _residenceRevision == pResidenceRevision) return _index;
            if (pBuild == null) throw new ArgumentNullException(nameof(pBuild));
            CitySchoolResidentIndex next = pBuild();
            if (next == null) throw new InvalidOperationException(
                "Resident index builder returned null");
            _index = next;
            _membershipVersion = pMembershipVersion;
            _residenceRevision = pResidenceRevision;
            return _index;
        }

        public void Clear()
        {
            _index = null;
            _membershipVersion = 0L;
            _residenceRevision = 0L;
        }
    }

    public static class CitySchoolResidentIndexRules
    {
        public const int MaxScholarActorsPerCity = 24;

        public static CitySchoolResidentIndex Build(
            IEnumerable<CitySchoolResidentCandidate> pCandidates)
        {
            CitySchoolResidentCandidate[] residents = (pCandidates ??
                    Array.Empty<CitySchoolResidentCandidate>())
                .Where(IsValid)
                .GroupBy(p => p.ActorId)
                .Select(p => p.OrderBy(v => v.SchoolOrder)
                    .ThenBy(v => v.SchoolId, StringComparer.Ordinal)
                    .ThenBy(v => v.CityId)
                    .First())
                .OrderBy(p => p.CityId)
                .ThenBy(p => p.SchoolOrder)
                .ThenBy(p => p.ActorId)
                .ToArray();

            var counts = new Dictionary<long, Dictionary<string, int>>();
            foreach (CitySchoolResidentCandidate resident in residents)
            {
                if (!counts.TryGetValue(resident.CityId,
                        out Dictionary<string, int> cityCounts))
                {
                    cityCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                    counts[resident.CityId] = cityCounts;
                }
                cityCounts.TryGetValue(resident.SchoolId, out int previous);
                cityCounts[resident.SchoolId] = previous + 1;
            }

            Dictionary<long, IReadOnlyList<long>> scholars = residents
                .Where(p => p.QualifiedScholar)
                .GroupBy(p => p.CityId)
                .ToDictionary(p => p.Key,
                    p => (IReadOnlyList<long>)Array.AsReadOnly(p
                        .OrderBy(v => v.SchoolOrder)
                        .ThenBy(v => v.ActorId)
                        .Take(MaxScholarActorsPerCity)
                        .Select(v => v.ActorId)
                        .ToArray()));
            Dictionary<long, IReadOnlyList<string>> schools = residents
                .GroupBy(p => p.CityId)
                .ToDictionary(p => p.Key,
                    p => (IReadOnlyList<string>)Array.AsReadOnly(p
                        .OrderBy(v => v.SchoolOrder)
                        .ThenBy(v => v.SchoolId, StringComparer.Ordinal)
                        .Select(v => v.SchoolId)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()));
            return new CitySchoolResidentIndex(counts, scholars, schools);
        }

        private static bool IsValid(CitySchoolResidentCandidate pCandidate)
        {
            return pCandidate != null && pCandidate.ActorId >= 0 && pCandidate.CityId >= 0 &&
                   pCandidate.Present && pCandidate.SchoolOrder >= 0 &&
                   !string.IsNullOrWhiteSpace(pCandidate.SchoolId) &&
                   CourtSchoolRegistry.Find(pCandidate.SchoolId) != null;
        }
    }
}
