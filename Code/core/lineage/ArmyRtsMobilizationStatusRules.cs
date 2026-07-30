namespace AncientWarfare3.core.lineage
{
    public static class ArmyRtsMobilizationStatusRules
    {
        public static bool RequiresSpeedStatus(ArmyRtsState pState)
        {
            return pState == ArmyRtsState.Rally ||
                   pState == ArmyRtsState.Replenish ||
                   pState == ArmyRtsState.March ||
                   pState == ArmyRtsState.Deploy;
        }

        public static bool ShouldApplyToMember(ArmyRtsState pState,
            bool pEligible, bool pAssemblyComplete)
        {
            return RequiresSpeedStatus(pState) && pEligible &&
                   !pAssemblyComplete;
        }

        public static bool ShouldStartCleanup(ArmyRtsState pPreviousState,
            ArmyRtsState pNextState)
        {
            return RequiresSpeedStatus(pPreviousState) &&
                   !RequiresSpeedStatus(pNextState);
        }

        public static bool ShouldStartCatchup(ArmyRtsState pPreviousState,
            ArmyRtsState pNextState)
        {
            return RequiresSpeedStatus(pNextState) &&
                   pPreviousState != pNextState;
        }

        public static bool RequiresReconciliation(ArmyRtsState pState,
            bool pPendingPass)
        {
            return pPendingPass;
        }
    }
}
