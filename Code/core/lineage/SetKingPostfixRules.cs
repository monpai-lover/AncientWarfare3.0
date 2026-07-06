namespace AncientWarfare3.core.lineage
{
    public static class SetKingPostfixRules
    {
        public static bool ShouldRun(bool pFromLoad, bool pActorIsActualKing)
        {
            return !pFromLoad && pActorIsActualKing;
        }
    }
}
