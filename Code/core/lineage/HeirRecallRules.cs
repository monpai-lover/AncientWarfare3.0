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

        public static bool ShouldUseLeaderFallbackForXiaizedSuccession(bool pHasRegisteredHeir,
            bool pHasLeaderCandidate)
        {
            return !pHasRegisteredHeir && pHasLeaderCandidate;
        }

        public static bool ShouldRecallForeignSelectedHeir(bool pHasHeir, bool pSameKingdom, bool pHasCapital)
        {
            return pHasHeir && !pSameKingdom && pHasCapital;
        }
    }
}
