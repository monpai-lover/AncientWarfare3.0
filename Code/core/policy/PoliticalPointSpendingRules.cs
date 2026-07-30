using System;

namespace AncientWarfare3.core.policy
{
    public static class PoliticalPointSpendingRules
    {
        public const float CourtReserve = 20f;

        public static float AutomaticSpend(float points, float requested)
        {
            float available = Math.Max(0f, points - CourtReserve);
            return Math.Min(available, Math.Max(0f, requested));
        }
    }
}
