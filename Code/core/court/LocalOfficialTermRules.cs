using System;

namespace AncientWarfare3.core.court
{
    public static class LocalOfficialTermRules
    {
        public const int MinimumYears = 10;
        public const int MaximumYears = 15;

        public static int TermLength(int ability, int merit, int age,
            long actorId, int appointmentYear)
        {
            int fitness = Math.Max(0, ability) / 35 +
                          Math.Max(0, merit) / 50;
            if (age >= 25 && age <= 50) fitness++;
            long stable = unchecked(actorId * 397L +
                                    appointmentYear * 17L);
            int jitter = (int)(Math.Abs(stable % 3L));
            return Math.Max(MinimumYears, Math.Min(MaximumYears,
                MinimumYears + fitness + jitter));
        }

        public static bool IsValidTermLength(int pYears)
        {
            return pYears >= MinimumYears && pYears <= MaximumYears;
        }
    }
}
