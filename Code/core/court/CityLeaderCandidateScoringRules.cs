namespace AncientWarfare3.core.court
{
    public static class CityLeaderCandidateScoringRules
    {
        public static int Score(int baseScore, int developmentScore, int existingClanLeaderCount,
            bool sameNativeCity, bool hasClan, bool isRoyalClan)
        {
            int score = baseScore + developmentScore;
            if (sameNativeCity) score += 10;
            if (hasClan) score -= existingClanLeaderCount * 15;
            return score;
        }

        public static bool CanEnterUnifiedPool(bool eligible, bool hasClan)
        {
            return eligible;
        }
    }
}
