using System;

namespace AncientWarfare3.core.lineage
{
    internal static class BanditGreatUprisingRules
    {
        public const double BanditPopulationRatioThreshold = 0.05d;
        public const int CorruptionStreakYears = 5;
        public const int FamineStreakYears = 2;
        public const int ConversionBudgetPerYear = 4;

        public static bool MeetsBanditRatio(int banditPopulation,
            int originPopulation)
        {
            double bandits = Math.Max(0, banditPopulation);
            double population = Math.Max(1, originPopulation);
            return bandits / population >=
                   BanditPopulationRatioThreshold;
        }

        public static bool ShouldActivate(int banditPopulation,
            int originPopulation, int corruptionStreak, int famineStreak)
        {
            return MeetsBanditRatio(banditPopulation, originPopulation) &&
                   (Math.Max(0, corruptionStreak) >= CorruptionStreakYears ||
                    Math.Max(0, famineStreak) >= FamineStreakYears);
        }

        public static int AdvanceStreak(int current, bool condition,
            int cap)
        {
            if (!condition) return 0;
            int maximum = Math.Max(0, cap);
            return Math.Min(maximum, Math.Max(0, current) + 1);
        }

        public static int AdvanceCursor(int current, int processed,
            int count)
        {
            if (count <= 0) return 0;
            int normalized = current % count;
            if (normalized < 0) normalized += count;
            int delta = Math.Max(0, processed) % count;
            return (normalized + delta) % count;
        }

        public static bool CanConvert(bool uprisingActive, bool banditRoute,
            bool originValid)
        {
            return uprisingActive && banditRoute && originValid;
        }
    }
}
