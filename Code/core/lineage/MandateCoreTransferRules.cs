namespace AncientWarfare3.core.lineage
{
    public static class MandateCoreTransferRules
    {
        public static bool ShouldInvalidate(bool pHasCurrentPeriod, bool pIsLegalCore)
        {
            return pHasCurrentPeriod && pIsLegalCore;
        }
    }
}
