using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public static class MandateCoreTransferRules
    {
        public static bool ShouldInvalidate(bool pHasCurrentPeriod, bool pIsLegalCore)
        {
            return pHasCurrentPeriod && pIsLegalCore;
        }

        public static bool ShouldApplyMandateLoss(bool pHasCurrentPeriod,
            bool pIsLegalCore, bool pOldOwnerIsMandate, bool pOwnerChanged)
        {
            return pHasCurrentPeriod && pIsLegalCore &&
                   pOldOwnerIsMandate && pOwnerChanged;
        }

        public static int AllowedAnnualLossDelta(int pCurrentAnnualLoss,
            int pRequestedDelta)
        {
            int current = Math.Max(0, Math.Min(12, -pCurrentAnnualLoss));
            int requested = Math.Max(0, -pRequestedDelta);
            return -Math.Min(requested, 12 - current);
        }

        public static bool ShouldTransferCapitalRing(bool pMandateWar,
            bool pAttackersWon, bool pCapitalCaptured,
            bool pCityOwnedByFormerMandate, bool pAlreadyTransferred)
        {
            return pMandateWar && pAttackersWon && pCapitalCaptured &&
                   !pAlreadyTransferred;
        }

        public static IReadOnlyList<long> MergeCapitalTerritoryIds(
            IEnumerable<long> pCoreCityIds,
            IEnumerable<long> pDeJureCityIds,
            IEnumerable<long> pNeighborCityIds,
            long pCapitalCityId)
        {
            var result = new List<long>();
            var seen = new HashSet<long>();
            if (pCapitalCityId >= 0L)
            {
                seen.Add(pCapitalCityId);
                result.Add(pCapitalCityId);
            }
            AddIds(pCoreCityIds, result, seen);
            AddIds(pDeJureCityIds, result, seen);
            AddIds(pNeighborCityIds, result, seen);
            return result;
        }

        private static void AddIds(IEnumerable<long> pIds,
            List<long> pResult, HashSet<long> pSeen)
        {
            if (pIds == null) return;
            foreach (long id in pIds)
                if (id >= 0L && pSeen.Add(id)) pResult.Add(id);
        }
    }
}
