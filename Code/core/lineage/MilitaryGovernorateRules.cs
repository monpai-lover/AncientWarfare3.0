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
        public const int CentralPowerReleaseThreshold = 60;
        public const int CentralPowerReclaimThreshold = 80;
        public const string GovernmentState = "military_governorate";

        public static bool CanCreate(bool pIsXiaSystem, int pCityCount,
            int pMaxCities, int pCentralPower,
            bool pSuzerainIsVassal = false,
            bool pSuzerainIsMilitaryGovernorate = false)
        {
            return pIsXiaSystem && pCityCount > pMaxCities &&
                   pCentralPower < CentralPowerReleaseThreshold &&
                   !pSuzerainIsVassal && !pSuzerainIsMilitaryGovernorate;
        }

        public static bool CanSplitCity(bool pDirectSuzerain,
            bool pCityBelongsToGovernorate, bool pCityIsSeat,
            int pGovernorateCityCount)
        {
            return pDirectSuzerain && pCityBelongsToGovernorate &&
                   !pCityIsSeat && pGovernorateCityCount >= 2;
        }

        public static bool CanReclaimCity(bool pDirectSuzerain,
            bool pCityBelongsToGovernorate, bool pCityIsSeat,
            int pGovernorateCityCount)
        {
            return pDirectSuzerain && pCityBelongsToGovernorate &&
                   (!pCityIsSeat || pGovernorateCityCount == 1);
        }

        public static bool ShouldAiReclaim(int pCentralPower)
        {
            return pCentralPower >= CentralPowerReclaimThreshold;
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

        public static string CanonicalCommandName(string pRegion,
            string pSuffix)
        {
            string name = (pRegion ?? string.Empty).Trim();
            string suffix = (pSuffix ?? string.Empty).Trim();
            if (suffix.Length > 0 && name.EndsWith(suffix,
                    System.StringComparison.Ordinal))
                return name.Substring(0, name.Length - suffix.Length).Trim();
            return name;
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
