using System;

namespace AncientWarfare3.core.policy
{
    public static class KingdomDecisionMonthlyRules
    {
        public const int MonthsPerYear = 4;
        public const float MonthlyYearFraction = 1f / MonthsPerYear;

        public static int ToMonthKey(int pYear, int pMonth)
        {
            int normalizedMonth = Math.Max(1, Math.Min(MonthsPerYear,
                pMonth));
            return pYear * MonthsPerYear + normalizedMonth - 1;
        }

        public static bool ShouldProcessMonth(int pCurrentMonthKey,
            int pLastProcessedMonthKey)
        {
            return pCurrentMonthKey != pLastProcessedMonthKey;
        }

        public static float MonthlyShare(float pAnnualValue)
        {
            return Math.Max(0f, pAnnualValue) * MonthlyYearFraction;
        }
    }
}
