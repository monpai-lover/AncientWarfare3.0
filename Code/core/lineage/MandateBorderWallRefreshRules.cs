using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class MandateBorderWallRefreshRules
    {
        public static bool ShouldRefresh(bool activated,
            bool cityEligible)
        {
            return activated && cityEligible;
        }

        public static IReadOnlyCollection<long> AffectedCityIds(
            long changedCityId, long previousCityId,
            IEnumerable<long> neighbourCityIds)
        {
            var result = new HashSet<long>();
            AddPositive(result, changedCityId);
            AddPositive(result, previousCityId);
            if (neighbourCityIds != null)
                foreach (long id in neighbourCityIds)
                    AddPositive(result, id);
            return result;
        }

        public static bool ShouldRestore(string currentTopTypeId,
            string placedWallTypeId)
        {
            return !string.IsNullOrWhiteSpace(placedWallTypeId) &&
                   string.Equals(currentTopTypeId, placedWallTypeId,
                       StringComparison.Ordinal);
        }

        private static void AddPositive(HashSet<long> values, long value)
        {
            if (value > 0) values.Add(value);
        }
    }
}
