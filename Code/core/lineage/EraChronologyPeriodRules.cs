using System;

namespace AncientWarfare3.core.lineage
{
    public static class EraChronologyPeriodRules
    {
        public static double ResolveEnd(double pEraStart,
            double pRecordedEraEnd, double pNextEraStart,
            double pReignEnd, double pCurrentTime)
        {
            double end = pRecordedEraEnd >= pEraStart
                ? pRecordedEraEnd
                : pNextEraStart >= pEraStart
                    ? pNextEraStart
                    : pReignEnd >= pEraStart
                        ? pReignEnd
                        : Math.Max(pEraStart, pCurrentTime);
            if (pNextEraStart >= pEraStart)
                end = Math.Min(end, pNextEraStart);
            if (pReignEnd >= pEraStart)
                end = Math.Min(end, pReignEnd);
            return Math.Max(pEraStart, end);
        }

        public static bool OverlapsReign(double pEraStart,
            double pEraEnd, double pReignStart, double pReignEnd)
        {
            if (pEraStart < 0d || pReignStart < 0d) return false;
            double eraEnd = pEraEnd < 0d ? double.MaxValue : pEraEnd;
            double reignEnd = pReignEnd < 0d
                ? double.MaxValue
                : pReignEnd;
            return pEraStart < reignEnd && eraEnd > pReignStart;
        }

        public static bool ShouldAddPreEraSpan(double pReignStart,
            double pFirstEraStart)
        {
            return pReignStart >= 0d &&
                   pFirstEraStart > pReignStart + 0.000001d;
        }
    }
}
