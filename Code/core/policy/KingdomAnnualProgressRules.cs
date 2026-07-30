using System;

namespace AncientWarfare3.core.policy
{
    public static class KingdomAnnualProgressRules
    {
        private const int MaximumCatchUpYears = 256;

        public static int ResolveElapsedYears(int pLastYear, int pCurrentYear)
        {
            if (pCurrentYear < 0) return 0;
            if (pLastYear == int.MinValue) return 1;
            if (pCurrentYear <= pLastYear) return 0;
            long elapsed = (long)pCurrentYear - pLastYear;
            return (int)Math.Min(MaximumCatchUpYears, elapsed);
        }

        public static float ScaleAnnualValue(float pAnnualValue,
            int pElapsedYears)
        {
            if (pAnnualValue <= 0f || pElapsedYears <= 0) return 0f;
            double scaled = (double)pAnnualValue * pElapsedYears;
            return scaled >= float.MaxValue ? float.MaxValue : (float)scaled;
        }

        public static float ResolveSpendLimit(float pAnnualLimit,
            int pElapsedYears)
        {
            return ScaleAnnualValue(pAnnualLimit, pElapsedYears);
        }
    }
}
