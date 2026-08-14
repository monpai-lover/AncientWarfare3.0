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
        public const int AbsoluteWithdrawalRemnant = 6;
        public const int ResumePercent = 80;

        public static bool ShouldWithdraw(int living, int baseline)
        {
            int surviving = Math.Max(0, living);
            return baseline > 0 &&
                   ((baseline > AbsoluteWithdrawalRemnant &&
                     surviving <= AbsoluteWithdrawalRemnant) ||
                    (long)surviving * 100L <=
                    (long)baseline * WithdrawalPercent);
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

        public static bool ShouldTreatAsTargetTerritory(bool exactZone,
            bool borderZone, bool adjacentTargetZone)
        {
            return exactZone || borderZone || adjacentTargetZone;
        }

        public static bool ShouldAllowVanillaCityAttack(
            ArmyRtsWarPhase pPhase)
        {
            return pPhase == ArmyRtsWarPhase.VanillaCombat;
        }

        public static bool ShouldReleaseToVanilla(
            ArmyRtsWarResolutionMode pMode,
            bool insideTargetTerritory, bool targetIsEnemy,
            bool objectiveOpen)
        {
            return pMode != ArmyRtsWarResolutionMode.AbstractDecisive &&
                   insideTargetTerritory && targetIsEnemy && objectiveOpen;
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
                if (!objectiveOpen)
                    return ArmyRtsCombatControlDecision.
                        ReacquireStrategicControl;
                return ArmyRtsCombatControlDecision.KeepVanillaControl;
            }
            return ShouldReleaseToVanilla(insideTargetTerritory,
                hostileCombatUnitNearby) && objectiveOpen
                ? ArmyRtsCombatControlDecision.ReleaseToVanilla
                : ArmyRtsCombatControlDecision.KeepStrategicControl;
        }

        public static ArmyRtsCombatControlDecision ResolveCombatControl(
            ArmyRtsWarResolutionMode pMode, ArmyRtsWarPhase pPhase,
            bool pWithdrawalRequired, bool pInsideTargetTerritory,
            bool pTargetIsEnemy, bool pObjectiveOpen,
            bool pPlayerCommand = false)
        {
            if (pPhase == ArmyRtsWarPhase.Withdrawal ||
                pPhase == ArmyRtsWarPhase.Replenishing)
                return ArmyRtsCombatControlDecision.KeepStrategicControl;
            if (pWithdrawalRequired &&
                ArmyRtsWarDoctrineRules.AllowWithdrawal(pMode,
                    ArmyRtsWithdrawalOrigin.CasualtyThreshold,
                    pPlayerCommand))
                return ArmyRtsCombatControlDecision.ReacquireForWithdrawal;
            if (pPhase == ArmyRtsWarPhase.VanillaCombat)
                return pObjectiveOpen
                    ? ArmyRtsCombatControlDecision.KeepVanillaControl
                    : ArmyRtsCombatControlDecision.
                        ReacquireStrategicControl;
            if (pMode == ArmyRtsWarResolutionMode.AbstractDecisive)
                return ArmyRtsCombatControlDecision.KeepStrategicControl;
            return ShouldReleaseToVanilla(pMode, pInsideTargetTerritory,
                    pTargetIsEnemy, pObjectiveOpen)
                ? ArmyRtsCombatControlDecision.ReleaseToVanilla
                : ArmyRtsCombatControlDecision.KeepStrategicControl;
        }

        public static bool OwnsTacticalActors(ArmyRtsWarPhase phase)
        {
            return phase != ArmyRtsWarPhase.VanillaCombat;
        }

        public static int RecoveryTargetStrength(int baseline)
        {
            if (baseline <= 0) return 0;
            long target = ((long)baseline * ResumePercent + 99L) / 100L;
            return target >= int.MaxValue ? int.MaxValue : (int)target;
        }

        public static int RecoveryShortage(int living, int baseline)
        {
            return Math.Max(0, RecoveryTargetStrength(baseline) -
                               Math.Max(0, living));
        }

        public static int ReplacementBatchRequest(int living, int baseline,
            int maximumBatch)
        {
            return Math.Min(Math.Max(0, maximumBatch),
                RecoveryShortage(living, baseline));
        }

        public static bool CanReplenishInCurrentCity(
            bool controlledByArmySide, bool hostileCombatUnitNearby)
        {
            return controlledByArmySide && !hostileCombatUnitNearby;
        }
    }
}
