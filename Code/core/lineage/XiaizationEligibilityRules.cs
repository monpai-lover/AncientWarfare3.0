namespace AncientWarfare3.core.lineage
{
    public static class XiaizationEligibilityRules
    {
        public const int PseudoDynastyLevel = 2;
        public const int XiaInstitutionsLevel = 4;

        public static bool CanUseMandateSystem(bool pIsXiaKingdom, int pXiaizationLevel)
        {
            return CanUseMandateSystem(pIsXiaKingdom, pXiaizationLevel, pIsForeignPseudoDynasty: false);
        }

        public static bool CanUseMandateSystem(bool pIsXiaKingdom, int pXiaizationLevel,
            bool pIsForeignPseudoDynasty)
        {
            return pIsXiaKingdom || pIsForeignPseudoDynasty || pXiaizationLevel >= XiaInstitutionsLevel;
        }

        public static bool CanUsePolicySystem(bool pIsXiaKingdom, int pXiaizationLevel)
        {
            return pIsXiaKingdom || pXiaizationLevel >= PseudoDynastyLevel;
        }

        public static bool CanUsePolicyNode(bool pIsXiaKingdom, int pXiaizationLevel, string pNodeId,
            bool pIsXiaizationPolicy)
        {
            if (pIsXiaKingdom) return true;
            if (pXiaizationLevel < PseudoDynastyLevel) return false;
            if (pXiaizationLevel >= XiaInstitutionsLevel) return true;
            return pIsXiaizationPolicy;
        }

        public static bool CanUseInstitutionSystem(bool pIsXiaKingdom, int pXiaizationLevel,
            bool pIsForeignPseudoDynasty)
        {
            return pIsXiaKingdom || pIsForeignPseudoDynasty || pXiaizationLevel >= XiaInstitutionsLevel;
        }
    }
}
