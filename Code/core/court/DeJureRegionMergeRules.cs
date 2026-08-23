namespace AncientWarfare3.core.court
{
    internal static class DeJureRegionMergeRules
    {
        internal const int CooldownYears = 3;

        internal static bool CanMerge(bool primaryActive, int primaryMemberCount,
            bool secondaryActive, int secondaryMemberCount, bool sameKingdom,
            bool adjacent, bool primaryEligible, bool secondaryEligible)
        {
            return primaryActive && secondaryActive &&
                   primaryMemberCount == 1 && secondaryMemberCount == 1 &&
                   sameKingdom && adjacent && primaryEligible &&
                   secondaryEligible;
        }

        internal static int ComparePrimary(int leftPopulation,
            int rightPopulation, int leftEconomy, int rightEconomy,
            long leftRegionId, long rightRegionId)
        {
            int population = rightPopulation.CompareTo(leftPopulation);
            if (population != 0) return population;
            int economy = rightEconomy.CompareTo(leftEconomy);
            if (economy != 0) return economy;
            return leftRegionId.CompareTo(rightRegionId);
        }

        internal static bool CooldownAllows(int lastYear, int currentYear)
        {
            return lastYear < 0 || currentYear - lastYear >= CooldownYears;
        }
    }
}
