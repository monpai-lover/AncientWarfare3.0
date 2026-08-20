using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    internal static class RegionalGovernmentRules
    {
        public const int MaximumNeighborMembers = 4;
        public const string DefaultRegionTitle = "州";

        public static IReadOnlyList<RegionalGovernmentFact> Build(
            IEnumerable<RegionalGovernmentCityFact> pCities,
            string pRegionTitle)
        {
            var candidates = (pCities ?? Array.Empty<RegionalGovernmentCityFact>())
                .Where(IsValid).ToDictionary(city => city.CityId);
            var remaining = new HashSet<long>(candidates.Keys);
            var result = new List<RegionalGovernmentFact>();
            while (remaining.Count > 0)
            {
                RegionalGovernmentCityFact seat = remaining.Select(id =>
                        candidates[id]).OrderByDescending(Development)
                    .ThenByDescending(Population).ThenBy(city => city.CityId)
                    .First();
                var region = new RegionalGovernmentFact
                {
                    KingdomId = seat.KingdomId,
                    SeatCityId = seat.CityId,
                    SeatCityName = seat.CityName ?? string.Empty
                };
                region.MemberCityIds.Add(seat.CityId);
                remaining.Remove(seat.CityId);

                var neighbors = (seat.NeighborCityIds ?? Array.Empty<long>())
                    .Where(remaining.Contains)
                    .Select(id => candidates[id])
                    .Where(city => city.KingdomId == seat.KingdomId)
                    .OrderByDescending(Development).ThenByDescending(Population)
                    .ThenBy(city => city.CityId)
                    .Take(MaximumNeighborMembers);
                foreach (RegionalGovernmentCityFact member in neighbors)
                {
                    region.MemberCityIds.Add(member.CityId);
                    remaining.Remove(member.CityId);
                }
                result.Add(region);
            }
            return result.OrderBy(region => region.SeatCityId).ToArray();
        }

        public static string RegionName(string pSeatName, string pRegionTitle)
        {
            string name = (pSeatName ?? string.Empty).Trim();
            foreach (string suffix in new[] { "州", "府", "城" })
                if (name.EndsWith(suffix, StringComparison.Ordinal))
                    name = name.Substring(0, name.Length - suffix.Length);
            return name;
        }

        public static string CityName(string pCityName)
        {
            return pCityName ?? string.Empty;
        }

        public static string AdministrativeLabel(string pPlaceName,
            string pLevelTitle)
        {
            string place = pPlaceName ?? string.Empty;
            string level = (pLevelTitle ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(level)) return place;
            if (string.IsNullOrEmpty(place)) return level;
            return place + level;
        }

        private static bool IsValid(RegionalGovernmentCityFact pCity)
        {
            return pCity != null && pCity.CityId >= 0L &&
                   pCity.KingdomId >= 0L;
        }

        private static float Development(RegionalGovernmentCityFact pCity)
        {
            return pCity?.Development ?? 0f;
        }

        private static int Population(RegionalGovernmentCityFact pCity)
        {
            return pCity?.Population ?? 0;
        }
    }
}
