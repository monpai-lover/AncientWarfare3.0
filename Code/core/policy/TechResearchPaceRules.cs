namespace AncientWarfare3.core.policy
{
    public static class TechResearchPaceRules
    {
        public const int FrontierSlowdownStartsAfterLevel = 3;
        public const float Level4FrontierMultiplier = 0.65f;
        public const float Level5FrontierMultiplier = 0.5f;

        public static float FrontierMultiplier(bool pIsTech, int pOwnTechLevel, int pWorldMaxTechLevel)
        {
            if (!pIsTech) return 1f;
            if (pOwnTechLevel <= FrontierSlowdownStartsAfterLevel) return 1f;
            if (pOwnTechLevel < pWorldMaxTechLevel) return 1f;
            return pOwnTechLevel >= 5 ? Level5FrontierMultiplier : Level4FrontierMultiplier;
        }
    }
}
