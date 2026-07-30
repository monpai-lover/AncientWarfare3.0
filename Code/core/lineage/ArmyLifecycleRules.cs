namespace AncientWarfare3.core.lineage
{
    public static class ArmyLifecycleRules
    {
        public static bool ShouldRemoveEmptyArmy(bool pHasData,
            bool pIsAlive, int pListedUnitCount, bool pHasLinkedLiveUnit,
            bool pCreationInProgress)
        {
            if (!pHasData || !pIsAlive || pCreationInProgress) return false;
            if (pListedUnitCount > 1) return false;
            return !pHasLinkedLiveUnit;
        }

        public static bool ShouldDeferEmptyCheck(bool pMembershipDirty,
            int pCompletedDeferrals, int pMaximumDeferrals)
        {
            return pMembershipDirty && pCompletedDeferrals <
                   System.Math.Max(0, pMaximumDeferrals);
        }

        public static bool ShouldRequestOffensiveReinforcement(
            bool pRequestReplacement, bool pWasSpecialArmy,
            bool pEmergencyActive,
            bool pHasReplacementArmy)
        {
            return pRequestReplacement && !pWasSpecialArmy &&
                   pEmergencyActive &&
                   !pHasReplacementArmy;
        }

        public static bool ShouldBlockOrdinaryArmyCreation(
            bool pCandidateIsWartimeGarrison)
        {
            return pCandidateIsWartimeGarrison;
        }

        public static bool ShouldDetachInvalidCityArmy(
            bool pCityHasArmyReference, bool pArmyHasData)
        {
            return pCityHasArmyReference && !pArmyHasData;
        }

        public static bool ShouldQueueArmyShellForCleanup(
            int pListedUnitCount)
        {
            return pListedUnitCount <= 1;
        }

        public static bool CanAssignArmyToAuthorityRole(
            bool pAssigningArmy, bool pIsKing, bool pIsLeader)
        {
            return !pAssigningArmy || !pIsKing && !pIsLeader;
        }
    }
}
