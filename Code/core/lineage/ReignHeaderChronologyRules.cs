using System;

namespace AncientWarfare3.core.lineage
{
    public static class ReignHeaderChronologyRules
    {
        public static int CalculateReignYear(int pStartYear,
            int pStartMonth, int pStartDay, int pEndYear,
            int pEndMonth, int pEndDay)
        {
            int elapsed = pEndYear - pStartYear;
            if (pEndMonth < pStartMonth ||
                pEndMonth == pStartMonth && pEndDay < pStartDay)
                elapsed--;
            return Math.Max(1, elapsed + 1);
        }

        public static string FormatSpan(string pStartGanzhi,
            string pStartChronology, string pEndGanzhi,
            string pEndChronology)
        {
            string start = FormatEndpoint(pStartGanzhi,
                pStartChronology);
            string end = FormatEndpoint(pEndGanzhi, pEndChronology);
            if (start.Length == 0) return end;
            if (end.Length == 0 ||
                string.Equals(start, end, StringComparison.Ordinal))
                return start;
            return start + "-" + end;
        }

        public static bool ShouldRecoverProjection(bool pHasRuler,
            bool pIsRepublic, long pCurrentRulerId,
            long pRecordedRulerId, double pReignStart)
        {
            return pHasRuler && !pIsRepublic && pCurrentRulerId >= 0 &&
                   (pReignStart < 0d || pRecordedRulerId != pCurrentRulerId);
        }

        private static string FormatEndpoint(string pGanzhi,
            string pChronology)
        {
            string ganzhi = (pGanzhi ?? "").Trim();
            string chronology = (pChronology ?? "").Trim();
            if (ganzhi.Length == 0) return chronology;
            return chronology.Length == 0
                ? ganzhi
                : ganzhi + " " + chronology;
        }
    }
}
