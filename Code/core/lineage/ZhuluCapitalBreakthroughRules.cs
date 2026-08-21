using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    internal static class ZhuluCapitalBreakthroughRules
    {
        public static bool ShouldTrigger(bool pZhuluActive, bool pIsCapital,
            bool pIsDeJureSeat, bool pAlreadyProcessed)
        {
            return pZhuluActive && (pIsCapital || pIsDeJureSeat) &&
                   !pAlreadyProcessed;
        }

        public static bool ShouldTransferCity(bool pOwnerIsEnemyParticipant,
            bool pOwnerIsAttacker, bool pOwnerIsFriendlyParticipant,
            bool pOwnerIsNeutral, bool pCityEligible)
        {
            return pCityEligible && pOwnerIsEnemyParticipant &&
                   !pOwnerIsAttacker && !pOwnerIsFriendlyParticipant &&
                   !pOwnerIsNeutral;
        }

        public static IReadOnlyList<long> MergeCityIds(
            IEnumerable<long> pRegionCityIds,
            IEnumerable<long> pNeighborCityIds, long pBreakthroughCityId)
        {
            var result = new HashSet<long>();
            Add(result, pRegionCityIds);
            Add(result, pNeighborCityIds);
            if (pBreakthroughCityId >= 0L) result.Remove(pBreakthroughCityId);
            return result.OrderBy(p => p).ToArray();
        }

        private static void Add(HashSet<long> pResult,
            IEnumerable<long> pValues)
        {
            if (pResult == null || pValues == null) return;
            foreach (long value in pValues)
                if (value >= 0L) pResult.Add(value);
        }
    }
}
