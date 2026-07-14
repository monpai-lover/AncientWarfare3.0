namespace AncientWarfare3.content.schools
{
    public enum HistoricalMasterMilitaryContext
    {
        OrdinaryWarrior,
        NormalArmy,
        ArmyCaptain,
        BorderArmy,
        General,
        RoyalGuard,
        SlaveArmyCadre,
        RebelLevy
    }

    public static class HistoricalMasterVocationRules
    {
        public static bool CanEnter(bool pCanonicalMaster, bool pDefinitionResolved,
            bool pMilitaryEligible, HistoricalMasterMilitaryContext pContext)
        {
            if (!pCanonicalMaster) return true;
            if (!pDefinitionResolved || !pMilitaryEligible) return false;
            return pContext != HistoricalMasterMilitaryContext.RoyalGuard &&
                   pContext != HistoricalMasterMilitaryContext.SlaveArmyCadre &&
                   pContext != HistoricalMasterMilitaryContext.RebelLevy;
        }
    }
}
