namespace AncientWarfare3.core.lineage
{
    public static class MandateSuccessionRules
    {
        public static bool ShouldBlockPeacefulFellApart(bool pIsActiveMandate, int pMandateValue,
            string pCrisisLevel, bool pHasSuccessionCandidate)
        {
            if (!pIsActiveMandate) return false;
            if (pHasSuccessionCandidate) return true;
            if (pCrisisLevel == "golden" || pCrisisLevel == "stable" || pCrisisLevel == "shaken")
                return true;
            return pMandateValue >= 20;
        }

        public static int ChildScarcityPenalty(int pAdultSons, int pUnderageSons, int pTotalChildren, bool pHasKing,
            int pYearsSinceAccession)
        {
            if (!pHasKing) return -4;
            if (pTotalChildren > 0) return 0;
            if (pYearsSinceAccession < 10) return 0;
            return -4;
        }

        public static bool CanUseUnderageDirectSonFallback(bool pIsDirectSon, bool pIsMale, bool pIsAlive,
            bool pIsKing, bool pHasAdultDirectSon)
        {
            return pIsDirectSon && pIsMale && pIsAlive && !pIsKing && !pHasAdultDirectSon;
        }

        public static string ResolveSuccessionMode(bool hasAdultDirectSon, bool hasUnderageDirectSon,
            bool hasRegisteredHeir, bool hasCollateralRestoration, bool hasClanFallback, bool hasLeaderFallback)
        {
            if (hasAdultDirectSon) return "direct";
            if (hasUnderageDirectSon) return "underage_direct";
            if (hasRegisteredHeir) return "registered";
            if (hasCollateralRestoration) return "collateral_restore";
            if (hasClanFallback) return "clan_fallback";
            if (hasLeaderFallback) return "leader_fallback";
            return "none";
        }

        public static bool IsValidCollateralRestorationCandidate(bool isXia, bool isMale, bool isAlive,
            bool isAdult, bool isKing, bool hasMadness, bool sameLineage, bool belongsToLegitimateShi)
        {
            return isXia && isMale && isAlive && isAdult && !isKing && !hasMadness &&
                   sameLineage && belongsToLegitimateShi;
        }

        public static bool ShouldRecordSuccessionCrisis(int pLastRecordedYear, int pCurrentYear)
        {
            return pLastRecordedYear != pCurrentYear;
        }
    }
}
