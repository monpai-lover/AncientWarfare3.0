using System;

namespace AncientWarfare3.core.lineage
{
    public static class GarrisonSortieRules
    {
        public const int ExtractionPercent = 40;
        public const int MinimumSortieForce = 4;
        public const int MemberMutationBatchSize = 8;

        public static bool CanLaunchForWar(long activeWarId,
            long lastLaunchedWarId)
        {
            return activeWarId >= 0L && activeWarId != lastLaunchedWarId;
        }

        public static bool ShouldLaunch(bool capitalThreatened,
            bool hasUsableFieldArmy, bool adjacentRecaptureNeeded,
            bool cityAlreadyHasSortie)
        {
            return !cityAlreadyHasSortie &&
                   (capitalThreatened || !hasUsableFieldArmy ||
                    adjacentRecaptureNeeded);
        }

        public static bool ShouldWaitForFieldArmyScan(bool scanComplete)
        {
            return !scanComplete;
        }

        public static int ExtractionSize(int garrison,
            int minimumDefense)
        {
            int available = Math.Max(0, garrison -
                                        Math.Max(0, minimumDefense));
            int fraction = Math.Max(0, garrison) * ExtractionPercent / 100;
            return Math.Min(fraction, available);
        }

        public static bool ShouldAttemptLaunch(int garrison,
            int minimumDefense)
        {
            return CanFormSortie(ExtractionSize(garrison,
                minimumDefense));
        }

        public static int RequiredGarrisonForSortie(int minimumDefense)
        {
            int minimum = Math.Max(0, minimumDefense);
            long extractionFloor = ((long)MinimumSortieForce * 100L +
                ExtractionPercent - 1L) / ExtractionPercent;
            long protectedFloor = (long)minimum + MinimumSortieForce;
            long required = Math.Max(extractionFloor, protectedFloor);
            return required >= int.MaxValue ? int.MaxValue :
                (int)required;
        }

        public static bool CanFormSortie(int memberCount)
        {
            return Math.Max(0, memberCount) >= MinimumSortieForce;
        }

        public static bool IsFriendlyRecaptureNeeded(
            bool hasFrozenControl, bool homeOnKingdomSide,
            bool controllerOnKingdomSide)
        {
            return hasFrozenControl && homeOnKingdomSide &&
                   !controllerOnKingdomSide;
        }

        public static bool ShouldCompleteMission(bool targetIsOrigin,
            bool originThreatened, bool adjacentRecaptureNeeded,
            bool targetControlledByKingdom)
        {
            if (targetIsOrigin)
                return !originThreatened && !adjacentRecaptureNeeded;
            return targetControlledByKingdom &&
                   !adjacentRecaptureNeeded;
        }

        public static bool IsTargetSecuredForMissionCompletion(
            ArmyRtsObjectiveState pState)
        {
            return pState == ArmyRtsObjectiveState.ClosedOccupied;
        }
    }
}
