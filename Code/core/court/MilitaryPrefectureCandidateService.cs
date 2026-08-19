using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    internal static class MilitaryPrefectureCandidateService
    {
        public static bool IsCandidate(Kingdom pKingdom, City pCity)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.kingdom != pKingdom) return false;
            if (pCity.isRekt() || !pCity.isAlive()) return false;
            if (!CustomCourtRuntime.TryGetLocalTemplate(pKingdom, pCity,
                    out CustomLocalCourtTemplate template) || template == null)
                return false;
            return MilitaryPrefectureCandidateRules.IsCandidate(true, true,
                pCity.kingdom == pKingdom, template.Id);
        }

        public static List<City> GetCandidates(Kingdom pKingdom)
        {
            var result = new List<City>();
            if (pKingdom?.data == null || pKingdom.cities == null) return result;
            foreach (City city in pKingdom.cities)
                if (IsCandidate(pKingdom, city)) result.Add(city);
            result.Sort((a, b) => (a?.id ?? long.MaxValue).CompareTo(
                b?.id ?? long.MaxValue));
            return result;
        }
    }
}
