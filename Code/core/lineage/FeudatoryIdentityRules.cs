namespace AncientWarfare3.core.lineage
{
    public static class FeudatoryIdentityRules
    {
        public static bool CanEnslave(bool ordinaryEligible,
            bool activePrince)
        {
            return ordinaryEligible && !activePrince;
        }
    }
}
