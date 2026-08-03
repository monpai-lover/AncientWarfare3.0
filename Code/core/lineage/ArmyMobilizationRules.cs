using System;

namespace AncientWarfare3.core.lineage
{
    public enum ArmyMobilizationPhase
    {
        Inactive = 0,
        Peace = 1,
        Notice = 2,
        War = 3
    }

    public static class ArmyMobilizationRules
    {
        public static ArmyMobilizationPhase Resolve(bool liveKingdom,
            bool activeNotice, int activeWarCount)
        {
            if (!liveKingdom) return ArmyMobilizationPhase.Inactive;
            if (activeWarCount > 0) return ArmyMobilizationPhase.War;
            return activeNotice
                ? ArmyMobilizationPhase.Notice
                : ArmyMobilizationPhase.Peace;
        }

        public static bool CanConsume(ArmyMobilizationPhase phase)
        {
            return phase == ArmyMobilizationPhase.Notice ||
                   phase == ArmyMobilizationPhase.War;
        }

        public static bool CanCreateOrdinaryArmy(
            ArmyMobilizationPhase phase)
        {
            return CanConsume(phase);
        }

        public static bool IsDeploymentReady(int living, int target)
        {
            if (target <= 0) return false;
            return (long)Math.Max(0, living) * 100L >=
                   (long)target * ArmyRtsRules.DeploymentQuorumPercent;
        }

        public static bool ShouldConfirmExhausted(
            bool reconciliationComplete, int available)
        {
            return reconciliationComplete && available <= 0;
        }
    }
}
