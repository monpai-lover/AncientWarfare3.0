using System;

namespace AncientWarfare3.core.lineage
{
    public static class WarParticipantCityBaselineRules
    {
        private const string KeyPrefix = "aw_war_remaining_city_count_";

        public static int NormalizeInitialCityCount(int pCityCount)
        {
            return Math.Max(1, pCityCount);
        }

        public static int ResolveRemainingCityCount(int pRecordedCount,
            int pLiveCount, bool pPermanentOwnershipChanged)
        {
            if (!pPermanentOwnershipChanged && pRecordedCount > 0)
                return NormalizeInitialCityCount(pRecordedCount);
            return pPermanentOwnershipChanged
                ? Math.Max(0, pLiveCount)
                : NormalizeInitialCityCount(pLiveCount);
        }

        public static string Key(long pKingdomId)
        {
            return KeyPrefix + pKingdomId;
        }
    }
}
