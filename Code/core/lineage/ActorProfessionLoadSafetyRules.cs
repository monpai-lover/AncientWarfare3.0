namespace AncientWarfare3.core.lineage
{
    public static class ActorProfessionLoadSafetyRules
    {
        public static bool ShouldBypassTransitionRestrictions(
            bool pHasProfessionAsset)
        {
            return !pHasProfessionAsset;
        }
    }
}
