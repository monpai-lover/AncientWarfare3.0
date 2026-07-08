namespace AncientWarfare3.core.lineage
{
    public static class XiaizationEligibilityRules
    {
        public const int PseudoDynastyLevel = 2;

        public static bool CanUseMandateSystem(bool pIsXiaKingdom, int pXiaizationLevel)
        {
            return pIsXiaKingdom || pXiaizationLevel >= PseudoDynastyLevel;
        }

        public static bool CanUsePolicySystem(bool pIsXiaKingdom, int pXiaizationLevel)
        {
            return pIsXiaKingdom || pXiaizationLevel >= PseudoDynastyLevel;
        }
    }
}
