namespace AncientWarfare3.core.lineage
{
    public static class MandateSuccessionRules
    {
        public static bool ShouldRefreshRulerProjection(bool active,
            long trackedKingdomId, long installedKingdomId,
            long installedActorId, long liveKingActorId)
        {
            return active && trackedKingdomId >= 0L &&
                   trackedKingdomId == installedKingdomId &&
                   installedActorId >= 0L &&
                   installedActorId == liveKingActorId;
        }

        public static bool ShouldTransferRulerTrait(
            bool projectionRefreshAccepted, long previousActorId,
            long installedActorId)
        {
            return projectionRefreshAccepted && previousActorId >= 0L &&
                   installedActorId >= 0L &&
                   previousActorId != installedActorId;
        }

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
            bool isAdult, bool isKing, bool hasMadness, bool sameLineage, bool belongsToLegitimateShi,
            bool canTraceToLegitimateBranch = false, bool requireAgnatic = false,
            bool isAgnaticLineDescendant = false)
        {
            // 男系(同姓父系)优先:要求 agnatic 时,只有真正的男系后裔才合格,
            // 避免选到父亲是异姓的人来"延续"本姓王统。氏(分支)可不同,不受此限。
            if (requireAgnatic && !isAgnaticLineDescendant) return false;
            return isXia && isMale && isAlive && isAdult && !isKing && !hasMadness &&
                   sameLineage && (belongsToLegitimateShi || canTraceToLegitimateBranch);
        }

        public static bool ShouldUseOrdinaryClanFallbackAfterCollateralSearch(bool hasDirectSon,
            bool hasRegisteredHeir, bool hasCollateralRestorationCandidate, bool isMandateOrLegitimateDynasty)
        {
            if (hasDirectSon || hasRegisteredHeir || hasCollateralRestorationCandidate) return false;
            return !isMandateOrLegitimateDynasty;
        }

        public static bool ShouldRecordSuccessionCrisis(int pLastRecordedYear, int pCurrentYear)
        {
            return pLastRecordedYear != pCurrentYear;
        }
    }
}
