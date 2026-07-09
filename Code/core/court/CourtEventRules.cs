using System;

namespace AncientWarfare3.core.court
{
    public static class CourtEventRules
    {
        public static bool ShouldFireStrongEvent(int currentYear, int lastStrongEventYear,
            int yearsDominant, float dominantShare, bool crisis, bool weakKing)
        {
            if (!CourtInfluenceRules.ShouldTriggerStrongEvent(yearsDominant, dominantShare, crisis, weakKing))
                return false;
            if (lastStrongEventYear < 0) return true;
            return currentYear - lastStrongEventYear >= CourtRules.StrongEventCooldownYears;
        }

        public static int NextDominantSinceYear(int currentYear, string previousDominant, string dominant,
            int previousSinceYear)
        {
            if (string.IsNullOrEmpty(dominant)) return -1;
            if (string.Equals(previousDominant ?? "", dominant, StringComparison.Ordinal) && previousSinceYear >= 0)
                return previousSinceYear;
            return currentYear;
        }
    }
}
