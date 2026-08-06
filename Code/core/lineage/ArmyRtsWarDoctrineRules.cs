namespace AncientWarfare3.core.lineage
{
    public enum ArmyRtsWarResolutionMode
    {
        Standard = 0,
        LastStand = 1,
        AbstractDecisive = 2
    }

    public enum ArmyRtsWithdrawalOrigin
    {
        CasualtyThreshold = 0,
        Logistics = 1,
        MinimumForce = 2,
        RegroupStall = 3,
        Watchdog = 4,
        PlayerCommand = 5
    }

    public static class ArmyRtsWarDoctrineRules
    {
        public static ArmyRtsWarResolutionMode Normalize(int pValue)
        {
            return pValue >= (int)ArmyRtsWarResolutionMode.Standard &&
                   pValue <= (int)ArmyRtsWarResolutionMode.AbstractDecisive
                ? (ArmyRtsWarResolutionMode)pValue
                : ArmyRtsWarResolutionMode.Standard;
        }

        public static bool AllowAutomaticWithdrawal(
            ArmyRtsWarResolutionMode pMode,
            ArmyRtsWithdrawalOrigin pOrigin)
        {
            return pOrigin == ArmyRtsWithdrawalOrigin.PlayerCommand ||
                   Normalize((int)pMode) !=
                   ArmyRtsWarResolutionMode.LastStand;
        }

        public static bool AllowWithdrawal(
            ArmyRtsWarResolutionMode pMode,
            ArmyRtsWithdrawalOrigin pOrigin)
        {
            return pOrigin == ArmyRtsWithdrawalOrigin.PlayerCommand ||
                   AllowAutomaticWithdrawal(pMode, pOrigin);
        }

        public static bool AllowWithdrawal(
            ArmyRtsWarResolutionMode pMode,
            ArmyRtsWithdrawalOrigin pOrigin,
            bool playerCommand)
        {
            return playerCommand || AllowWithdrawal(pMode, pOrigin);
        }

        public static bool IsExplicitPlayerRetreat(
            ArmyRtsMission pMission)
        {
            return pMission != null && pMission.PlayerOrder &&
                   pMission.Posture == ArmyRtsPosture.Retreat;
        }

        public static bool ShouldPersistPreviousOffensiveMission(
            ArmyRtsProposalKind pKind)
        {
            return pKind != ArmyRtsProposalKind.None &&
                   pKind != ArmyRtsProposalKind.Retreat &&
                   pKind != ArmyRtsProposalKind.FrontHold;
        }

        public static bool ShouldCreateStrategicRoute(
            ArmyRtsWarResolutionMode pMode)
        {
            return Normalize((int)pMode) !=
                   ArmyRtsWarResolutionMode.AbstractDecisive;
        }

        public static bool ShouldResolveRemoteDuel(
            ArmyRtsWarResolutionMode pMode)
        {
            return Normalize((int)pMode) ==
                   ArmyRtsWarResolutionMode.AbstractDecisive;
        }

        public static ArmyRtsWarResolutionMode Next(
            ArmyRtsWarResolutionMode pMode)
        {
            int normalized = (int)Normalize((int)pMode);
            return (ArmyRtsWarResolutionMode)((normalized + 1) % 3);
        }
    }
}
