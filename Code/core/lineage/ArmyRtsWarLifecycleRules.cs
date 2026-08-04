using System;

namespace AncientWarfare3.core.lineage
{
    public enum ArmyRtsCombatControlDecision
    {
        KeepStrategicControl = 0,
        ReleaseToVanilla = 1,
        KeepVanillaControl = 2,
        ReacquireStrategicControl = 3,
        ReacquireForWithdrawal = 4
    }

    public enum ArmyRtsWarPhase
    {
        PreparationRecruitment = 0,
        StrategicMovement = 1,
        VanillaCombat = 2,
        Withdrawal = 3,
        Replenishing = 4,
        AwaitingObjective = 5
    }

    public static class ArmyRtsWarLifecycleRules
    {
        public const int WithdrawalPercent = 20;
        public const int ResumePercent = 80;

        public static bool ShouldWithdraw(int living, int baseline)
        {
            return baseline > 0 &&
                   (long)Math.Max(0, living) * 100L <=
                   (long)baseline * WithdrawalPercent;
        }

        public static bool ShouldResume(int living, int baseline)
        {
            return baseline > 0 &&
                   (long)Math.Max(0, living) * 100L >=
                   (long)baseline * ResumePercent;
        }

        public static int CaptureBaseline(int existingBaseline, int living)
        {
            return existingBaseline > 0
                ? existingBaseline
                : Math.Max(0, living);
        }

        public static bool ShouldReleaseToVanilla(
            bool insideTargetTerritory, bool hostileCombatUnitNearby)
        {
            return insideTargetTerritory && hostileCombatUnitNearby;
        }

        public static bool CanGenerateReplacements(bool combatActive,
            bool transportActive, bool movementActive)
        {
            return !combatActive && !transportActive && !movementActive;
        }

        public static ArmyRtsCombatControlDecision ResolveCombatControl(
            ArmyRtsWarPhase phase, bool withdrawalRequired,
            bool insideTargetTerritory, bool hostileCombatUnitNearby,
            bool objectiveOpen)
        {
            if (phase == ArmyRtsWarPhase.Withdrawal ||
                phase == ArmyRtsWarPhase.Replenishing)
                return ArmyRtsCombatControlDecision.KeepStrategicControl;
            if (withdrawalRequired)
                return ArmyRtsCombatControlDecision.
                    ReacquireForWithdrawal;
            if (phase == ArmyRtsWarPhase.VanillaCombat)
            {
                if (!insideTargetTerritory || !hostileCombatUnitNearby ||
                    !objectiveOpen)
                    return ArmyRtsCombatControlDecision.
                        ReacquireStrategicControl;
                return ArmyRtsCombatControlDecision.KeepVanillaControl;
            }
            return ShouldReleaseToVanilla(insideTargetTerritory,
                hostileCombatUnitNearby) && objectiveOpen
                ? ArmyRtsCombatControlDecision.ReleaseToVanilla
                : ArmyRtsCombatControlDecision.KeepStrategicControl;
        }

        public static bool OwnsTacticalActors(ArmyRtsWarPhase phase)
        {
            return phase != ArmyRtsWarPhase.VanillaCombat;
        }
    }
}
