using System;

namespace AncientWarfare3.core.lineage
{
    public static class RestorationProtectionRules
    {
        public const int ProtectionDurationYears = 10;

        public static int ProtectionUntil(int restorationYear,
            int durationYears = ProtectionDurationYears)
        {
            return Math.Max(0, restorationYear) +
                   Math.Max(0, durationYears);
        }

        public static bool IsActive(int currentYear,
            int protectionUntilYear)
        {
            return protectionUntilYear >= 0 &&
                   currentYear < protectionUntilYear;
        }

        public static bool IsInternalWarType(string warType)
        {
            return string.Equals(warType, "independence_war",
                       StringComparison.Ordinal) ||
                   string.Equals(warType, "general_rebellion_war",
                       StringComparison.Ordinal) ||
                   string.Equals(warType, "fief_independence_war",
                       StringComparison.Ordinal) ||
                   string.Equals(warType, "succession_dispute_war",
                       StringComparison.Ordinal) ||
                   string.Equals(warType, "jingnan_war",
                       StringComparison.Ordinal) ||
                   string.Equals(warType, "coup_restoration_war",
                       StringComparison.Ordinal) ||
                   string.Equals(warType, "tianmingrebel",
                       StringComparison.Ordinal);
        }

        public static bool ShouldBlockIncoming(bool protectionActive,
            bool protectedDefender, bool internalWar,
            bool protectedKingdomIsAttacker)
        {
            return protectionActive && protectedDefender &&
                   !internalWar && !protectedKingdomIsAttacker;
        }
    }
}
