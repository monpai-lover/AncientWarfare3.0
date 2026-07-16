namespace AncientWarfare3.core.lineage
{
    public static class TemporaryLevyRules
    {
        public const int MaxWorkItemsPerKingdomYear = 4;
        public const int MaxCandidatesPerWorkItem = 16;
        public const int MaxRecruitsPerWorkItem = 8;
        public const int MaxCandidatesPerKingdomYear = 64;
        public const int MaxRecruitsPerKingdomYear = 32;
        public const int DemobilizationBatchSize = 8;
        public const float MaximumEnlistmentAge = 65f;

        public static bool ShouldRunRecruitmentWorkItem(bool emergencyActive,
            int completedWorkItems, int scannedCandidates, int recruitedActors)
        {
            return emergencyActive &&
                   completedWorkItems < MaxWorkItemsPerKingdomYear &&
                   scannedCandidates < MaxCandidatesPerKingdomYear &&
                   recruitedActors < MaxRecruitsPerKingdomYear;
        }

        public static int ClampRestoredCounter(int pValue, int pMaximum)
        {
            return System.Math.Max(0, System.Math.Min(System.Math.Max(0, pMaximum), pValue));
        }

        public static bool CanEnlist(bool originalEligible, bool protectedIdentity, float age,
            int currentWarriors, int warriorSlots)
        {
            return originalEligible && !protectedIdentity && age < MaximumEnlistmentAge &&
                   warriorSlots > 0 && currentWarriors < warriorSlots;
        }

        public static bool ShouldRemainMobilized(int pActiveNotices, int pActiveWars)
        {
            return pActiveNotices > 0 || pActiveWars > 0;
        }
    }
}
