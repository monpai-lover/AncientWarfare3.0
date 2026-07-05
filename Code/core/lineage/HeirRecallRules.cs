namespace AncientWarfare3.core.lineage
{
    public static class HeirRecallRules
    {
        public static bool ShouldRecallForSuccession(bool pWasRegisteredHeir, bool pIsNewKing,
            bool pIsCityLeader, bool pIsArmyCaptain, bool pIsGeneral, bool pHasFief)
        {
            if (!pIsNewKing) return false;
            return pIsCityLeader || pIsArmyCaptain || pIsGeneral || pHasFief;
        }

        public static bool ShouldPreferRegisteredHeirBeforeLeaderFallback(bool pHasRegisteredHeir)
        {
            return pHasRegisteredHeir;
        }
    }
}
