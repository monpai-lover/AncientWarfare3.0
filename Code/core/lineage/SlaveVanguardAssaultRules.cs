namespace AncientWarfare3.core.lineage
{
    public static class SlaveVanguardAssaultRules
    {
        public static bool ShouldHoldOrdinaryArmy(
            bool pActorIsCaptain,
            bool pActorArmyIsVanguard,
            bool pVanguardReady,
            bool pSameAttackTarget,
            bool pVanguardReachedTarget,
            bool pVanguardRetreating)
        {
            if (!pActorIsCaptain || pActorArmyIsVanguard) return false;
            if (!pVanguardReady || !pSameAttackTarget) return false;
            return !pVanguardReachedTarget && !pVanguardRetreating;
        }
    }
}
