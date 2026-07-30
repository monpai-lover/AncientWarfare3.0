namespace AncientWarfare3.core.policy
{
    public static class KingdomPolicySplitInheritanceRules
    {
        public const int XiaInstitutionsLevel = 4;
        public const int MaximumXiaizationLevel = 5;

        public static bool ShouldCaptureSplitSource(bool pRebellion,
            bool pFellApart, bool pIsIdentityRestoration,
            bool pFounderValid, bool pSourceValid, bool pSourceAlive)
        {
            return (pRebellion || pFellApart) &&
                   !pIsIdentityRestoration && pFounderValid &&
                   pSourceValid && pSourceAlive;
        }

        public static bool ShouldInheritFromSplit(bool pHasCapturedSource,
            bool pNewKingdomValid, bool pSourceValid, bool pSourceAlive,
            bool pCultureIntegrated)
        {
            return pHasCapturedSource && pNewKingdomValid &&
                   pSourceValid && pSourceAlive && pCultureIntegrated;
        }

        public static bool ShouldMarkCultureIntegrated(bool pNativeXiaCulture,
            int pPersistedXiaizationLevel)
        {
            return pNativeXiaCulture ||
                   pPersistedXiaizationLevel >= XiaInstitutionsLevel;
        }

        public static int NormalizeInheritedXiaizationLevel(int pSourceLevel)
        {
            if (pSourceLevel <= 0) return 0;
            return pSourceLevel >= MaximumXiaizationLevel
                ? MaximumXiaizationLevel
                : pSourceLevel;
        }
    }
}
