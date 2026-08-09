namespace AncientWarfare3.core.lineage
{
    public enum VassalSubjectKind
    {
        Ordinary = 0,
        MilitaryGovernorate = 1
    }

    public static class MilitaryGovernorateRules
    {
        public const int AnnualCreationLimit = 1;
        public const int CityScanBudget = 16;
        public const int GeneralScanBudget = 32;

        public static bool CanCreate(bool pIsXiaSystem, int pCityCount,
            int pMaxCities)
        {
            return pIsXiaSystem && pCityCount > pMaxCities;
        }

        public static bool IsEligibleSeat(bool pOwned, bool pCapital,
            bool pSpecialAdministration,
            bool pBordersOutsideRootNetwork)
        {
            return pOwned && !pCapital && !pSpecialAdministration &&
                   pBordersOutsideRootNetwork;
        }

        public static string CommandName(string pRegion, string pSuffix)
        {
            return KingdomNameplateSuffixRules.ProjectName(
                pRegion, pSuffix, true);
        }

        public static bool MustJoinSuzerainWar(VassalSubjectKind pKind)
        {
            return pKind == VassalSubjectKind.MilitaryGovernorate;
        }

        public static bool CanConductStateDiplomacy(
            VassalSubjectKind pKind)
        {
            return pKind != VassalSubjectKind.MilitaryGovernorate;
        }

        public static bool HasPersistedOverLimit(int pCurrentYear,
            int pOverLimitSinceYear)
        {
            return pOverLimitSinceYear >= 0 &&
                   pCurrentYear > pOverLimitSinceYear;
        }

        public static bool CanRunAnnualAi(int pCurrentYear,
            int pLastEvaluationYear)
        {
            return pCurrentYear >= 0 && pLastEvaluationYear < pCurrentYear;
        }

        public static bool ShouldSynchronizeColor(bool pDirect,
            bool pActive, VassalSubjectKind pKind)
        {
            return pDirect && pActive &&
                   pKind == VassalSubjectKind.MilitaryGovernorate;
        }

        public static bool ShouldRandomizeIndependentColor(
            string pEndReason)
        {
            return pEndReason == "independence_war";
        }

        public static bool ShouldTransferIndependenceToUpper(
            VassalSubjectKind pKind)
        {
            return pKind != VassalSubjectKind.MilitaryGovernorate;
        }
    }
}
