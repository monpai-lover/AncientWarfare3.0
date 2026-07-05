using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public static class CityTechSyncRules
    {
        public static string[] SelectMissingCompletedTechIds(IEnumerable<string> pCompletedTechIds,
            Func<string, bool> pCityAlreadyAdopted)
        {
            var result = new List<string>();
            if (pCompletedTechIds == null) return result.ToArray();
            foreach (string id in pCompletedTechIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (pCityAlreadyAdopted != null && pCityAlreadyAdopted(id)) continue;
                result.Add(id);
            }
            return result.ToArray();
        }
    }
}
