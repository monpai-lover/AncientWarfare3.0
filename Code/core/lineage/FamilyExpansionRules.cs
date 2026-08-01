namespace AncientWarfare3.core.lineage
{
    public enum FamilyExpansionTier
    {
        Civilian = 3,
        Noble = 4,
        Royal = 6
    }

    public static class FamilyExpansionRules
    {
        public const float PrioritizedReproductionWeight = 4f;

        public static int Target(FamilyExpansionTier pTier)
        {
            return (int)pTier;
        }

        public static bool NeedsExpansion(int pLivingChildren,
            FamilyExpansionTier pTier)
        {
            return pLivingChildren < Target(pTier);
        }
    }
}
