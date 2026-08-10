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
