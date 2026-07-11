namespace AncientWarfare3.core.lineage
{
    public static class SlaveArmyFillSideEffectRules
    {
        public static bool ShouldDeferPerActorSideEffects(bool pInsideFill, bool pIsSlave)
        {
            return pInsideFill && pIsSlave;
        }

        public static bool ShouldRefreshArmyOnce(int pChangedActors)
        {
            return pChangedActors > 0;
        }
    }
}
