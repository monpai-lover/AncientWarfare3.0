namespace AncientWarfare3.core.lineage
{
    public static class MandateLegalCoreInheritanceRules
    {
        public static bool ShouldInheritPreviousCore(long pPreviousPeriodId, long pCityId,
            bool pAlreadyInsertedInNewPeriod)
        {
            return pPreviousPeriodId >= 0 && pCityId >= 0 && !pAlreadyInsertedInNewPeriod;
        }

        public static bool ShouldAddFoundingCore(long pCityId, bool pAlreadyInsertedInNewPeriod)
        {
            return pCityId >= 0 && !pAlreadyInsertedInNewPeriod;
        }
    }
}
