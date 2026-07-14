using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolRecruitCandidateCache
    {
        private const int MaxScanPerCityYear = 96;
        private const int MaxCachedPerCityYear = 48;

        private sealed class Entry
        {
            public int Year;
            public long[] ActorIds;
        }

        private static readonly Dictionary<long, Entry> ByCity =
            new Dictionary<long, Entry>();

        public static long[] Get(City pCity, Actor pTeacher, int pYear)
        {
            if (pCity?.data == null || pCity.isRekt()) return System.Array.Empty<long>();
            if (!ByCity.TryGetValue(pCity.data.id, out Entry entry) || entry.Year != pYear)
            {
                var actorIds = new List<long>(MaxCachedPerCityYear);
                int scanned = 0;
                try
                {
                    foreach (Actor actor in pCity.units)
                    {
                        if (++scanned > MaxScanPerCityYear) break;
                        if (actor?.data == null || !actor.isAlive() || actor.isRekt() ||
                            actor.isBaby()) continue;
                        actorIds.Add(actor.data.id);
                        if (actorIds.Count >= MaxCachedPerCityYear) break;
                    }
                }
                catch { }
                actorIds.Sort();
                entry = new Entry { Year = pYear, ActorIds = actorIds.ToArray() };
                ByCity[pCity.data.id] = entry;
            }
            if (pTeacher?.data == null) return entry.ActorIds;
            var filtered = new List<long>(entry.ActorIds.Length);
            foreach (long actorId in entry.ActorIds)
                if (actorId != pTeacher.data.id) filtered.Add(actorId);
            return filtered.ToArray();
        }

        public static void Clear()
        {
            ByCity.Clear();
        }
    }
}
