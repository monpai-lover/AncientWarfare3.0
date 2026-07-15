namespace AncientWarfare3.core.lineage
{
    public static class HeirRegistrationRules
    {
        public static bool ShouldClearGlobalFlag(int pOtherLiveRegistrations)
        {
            return pOtherLiveRegistrations <= 0;
        }

        public static bool CountsAsOtherLiveRegistration(
            bool pIsExcludedKingdom,
            bool pIsCivilization,
            bool pIsRekt,
            bool pHasCities,
            long pRegisteredActorId,
            long pTargetActorId)
        {
            return pTargetActorId >= 0 &&
                   !pIsExcludedKingdom &&
                   pIsCivilization &&
                   !pIsRekt &&
                   pHasCities &&
                   pRegisteredActorId == pTargetActorId;
        }
    }
}
