using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolRecruitCandidateCache
    {
        private sealed class Entry
        {
            public City City;
            public HistoricalSchoolCityCacheStamp Stamp;
            public int Year;
            public long[] ActorIds;
        }

        private static readonly HistoricalSchoolFixedLru<long, Entry> ByCity =
            new HistoricalSchoolFixedLru<long, Entry>(128);

        public static long[] Get(City pCity, Actor pTeacher, int pYear)
        {
            if (pCity?.data == null || pCity.isRekt()) return Array.Empty<long>();
            if (!ByCity.TryGet(pCity.data.id, out Entry entry) ||
                !ReferenceEquals(entry.City, pCity) || entry.Year != pYear ||
                !StampMatches(entry.Stamp, pCity))
            {
                entry = BuildEntry(pCity, pYear);
                ByCity.Set(pCity.data.id, entry);
            }
            return entry.ActorIds;
        }

        public static void InvalidateCity(long pCityId)
        {
            if (pCityId >= 0) ByCity.Remove(pCityId);
        }

        public static void Clear()
        {
            ByCity.Clear();
        }

        private static Entry BuildEntry(City pCity, int pYear)
        {
            var actorIds = new List<long>(
                HistoricalSchoolRecruitCandidateRules.MaxCachedPerCityYear);
            try
            {
                int residentCount = pCity.units?.Count ?? 0;
                int start = HistoricalSchoolRecruitCandidateRules.ScanStart(
                    pCity.data.id, pYear, residentCount);
                int scanCount = HistoricalSchoolRecruitCandidateRules.ScanCount(
                    residentCount);
                for (int offset = 0; offset < scanCount; offset++)
                {
                    int index = HistoricalSchoolRecruitCandidateRules.ResidentIndex(
                        start, offset, residentCount);
                    Actor actor = pCity.units[index];
                    if (actor?.data == null || !actor.isAlive() || actor.isRekt() ||
                        actor.isBaby() ||
                        SchoolMembershipService.GetActive(actor.data.id) != null)
                        continue;
                    actorIds.Add(actor.data.id);
                    if (actorIds.Count >=
                        HistoricalSchoolRecruitCandidateRules.MaxCachedPerCityYear)
                        break;
                }
            }
            catch { }
            actorIds.Sort();
            return new Entry
            {
                City = pCity,
                Stamp = StampFor(pCity),
                Year = pYear,
                ActorIds = actorIds.ToArray()
            };
        }

        private static HistoricalSchoolCityCacheStamp StampFor(City pCity)
        {
            WorldTile center = pCity?.getTile();
            return new HistoricalSchoolCityCacheStamp(
                RuntimeHelpers.GetHashCode(pCity),
                pCity?.kingdom?.data?.id ?? -1L,
                pCity?.zones?.Count ?? 0,
                center?.x ?? int.MinValue,
                center?.y ?? int.MinValue);
        }

        private static bool StampMatches(HistoricalSchoolCityCacheStamp pStamp, City pCity)
        {
            WorldTile center = pCity?.getTile();
            return pStamp.Matches(
                RuntimeHelpers.GetHashCode(pCity),
                pCity?.kingdom?.data?.id ?? -1L,
                pCity?.zones?.Count ?? 0,
                center?.x ?? int.MinValue,
                center?.y ?? int.MinValue);
        }
    }
}
