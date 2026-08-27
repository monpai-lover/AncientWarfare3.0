using System;

namespace AncientWarfare3.core.lineage
{
    public static class SyntheticMobilizationRules
    {
        public const int SpawnBatchLimit = 8;
        public const int ReplacementBatchLimit = 8;
        public const int DemobilizationBatchLimit = 16;

        public static bool ShouldUseSyntheticMobilization(bool enabled)
        {
            return enabled;
        }

        public static bool ShouldBypassAw3RecruitmentRestrictions(
            bool syntheticEnabled)
        {
            return !syntheticEnabled;
        }

        public static int Quota(int cityPopulation, int knownSynthetic,
            int lawPercent)
        {
            long realPopulation = Math.Max(0L,
                (long)Math.Max(0, cityPopulation) -
                Math.Max(0, knownSynthetic));
            long percent = Math.Max(0, Math.Min(100, lawPercent));
            return (int)Math.Min(int.MaxValue,
                realPopulation * percent / 100L);
        }

        public static int Batch(int pending, int limit)
        {
            return Math.Min(Math.Max(0, pending), Math.Max(0, limit));
        }

        public static int ReplacementDemand(int target, int living,
            int remainingReserve)
        {
            return Math.Min(Math.Max(0, remainingReserve),
                Math.Max(0, target - Math.Max(0, living)));
        }

        public static int DemobilizationBatch(int pending)
        {
            return Batch(pending, DemobilizationBatchLimit);
        }

        public static bool ShouldRestartCityCursor(int expectedCityCount,
            int currentCityCount)
        {
            return Math.Max(0, expectedCityCount) !=
                   Math.Max(0, currentCityCount);
        }

        public static int ExpandLifecycleEnd(int currentEndExclusive,
            int eventRecordCount)
        {
            return Math.Max(Math.Max(0, currentEndExclusive),
                Math.Max(0, eventRecordCount));
        }

        public static bool ShouldDeferOrphanScan(
            bool loadReconciliationPending)
        {
            return loadReconciliationPending;
        }

        public static bool ShouldDeferDemobilization(bool syntheticActor,
            bool returnArrivalConfirmed, bool armyReturnActive,
            bool wartimeOwned, bool insideFriendlySafeCity)
        {
            return syntheticActor && (!returnArrivalConfirmed ||
                   armyReturnActive || wartimeOwned ||
                   !insideFriendlySafeCity);
        }
    }
}
