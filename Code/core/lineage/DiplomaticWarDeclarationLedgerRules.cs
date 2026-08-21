using System;

namespace AncientWarfare3.core.lineage
{
    public static class DiplomaticWarDeclarationLedgerRules
    {
        public static bool IsPending(string pLifecycle)
        {
            return string.Equals(pLifecycle, "pending",
                StringComparison.Ordinal);
        }

        public static bool CanAppendForPair(bool pExistingPendingPair)
        {
            return !pExistingPendingPair;
        }

        public static bool CanMutateStoredPayload(bool pPayloadPresent,
            bool pPayloadValid)
        {
            return !pPayloadPresent || pPayloadValid;
        }

        public static bool ShouldExecute(int currentYear,
            int earliestWarYear, int forcedWarYear, bool noticeReady)
        {
            if (earliestWarYear >= 0 && currentYear < earliestWarYear)
                return false;
            return noticeReady || forcedWarYear >= 0 &&
                   currentYear >= forcedWarYear;
        }

        public static bool ShouldRetryExecutionFailure(string pReason)
        {
            return !string.Equals(pReason, "invalid_participants",
                StringComparison.Ordinal);
        }

        public static bool ShouldRevalidateMutableEligibility(
            bool declarationLocked)
        {
            return !declarationLocked;
        }

        public static bool ShouldBlockWarWithActiveTreaty(
            bool activeTreaty, bool independenceWar,
            bool treatyExemptInternalWar)
        {
            return activeTreaty && !independenceWar &&
                   !treatyExemptInternalWar;
        }

        public static long ResolveTargetCityId(bool storedCityValid,
            long storedCityId, long capitalCityId, long firstCityId)
        {
            if (storedCityValid && storedCityId >= 0L)
                return storedCityId;
            return capitalCityId >= 0L ? capitalCityId : firstCityId;
        }

        public static bool MatchesDirectedWarPair(long recordAttackerId,
            long recordDefenderId, long warAttackerId,
            long warDefenderId)
        {
            return recordAttackerId == warAttackerId &&
                   recordDefenderId == warDefenderId;
        }

        public static int ComparePriority(int pLeftEarliestYear,
            int pLeftNoticeYear, string pLeftSignature,
            int pRightEarliestYear, int pRightNoticeYear,
            string pRightSignature)
        {
            int earliest = pLeftEarliestYear.CompareTo(
                pRightEarliestYear);
            if (earliest != 0) return earliest;
            int notice = pLeftNoticeYear.CompareTo(pRightNoticeYear);
            if (notice != 0) return notice;
            return string.CompareOrdinal(pLeftSignature ?? "",
                pRightSignature ?? "");
        }
    }
}
