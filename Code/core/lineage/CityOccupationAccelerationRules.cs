using System;

namespace AncientWarfare3.core.lineage
{
    public static class CityOccupationAccelerationRules
    {
        public static bool ShouldAdoptDominantCapturer(bool hasDominantEnemy, bool hasCaptureOwner,
            bool captureOwnerAlive, bool captureOwnerStillEnemyOfCity)
        {
            return hasDominantEnemy &&
                   (!hasCaptureOwner || !captureOwnerAlive || !captureOwnerStillEnemyOfCity);
        }

        public static bool ShouldApplyCapturePointContribution(bool contributorIsCityOwner,
            int capturePoints, bool cityOwnerHasActiveDefenders)
        {
            bool passiveWatchtowerContribution = contributorIsCityOwner && capturePoints >= 10;
            return !passiveWatchtowerContribution || cityOwnerHasActiveDefenders;
        }

        public static bool ShouldLatchDefenderEngagement(bool ownerWarriorPresent,
            bool attackerWarriorPresent, bool attackerIsEnemy)
        {
            return ownerWarriorPresent && attackerWarriorPresent && attackerIsEnemy;
        }

        public static bool ShouldCompleteAfterDefenderDefeat(bool enemyCapturer,
            bool activeCaptureUnits, bool activeDefenders, bool hostileRivalActive,
            bool ownershipChanged, bool cityManagerLocked, bool defenderEngagementObserved = false)
        {
            return enemyCapturer &&
                   activeCaptureUnits &&
                   !activeDefenders &&
                   !hostileRivalActive &&
                   !ownershipChanged &&
                   !cityManagerLocked &&
                   defenderEngagementObserved;
        }

        public static bool ShouldRetainDefenderEngagement(bool ownerMatches,
            bool attackerMatches, bool attackerStillEnemy,
            bool attackerPresentInCompletedCycle)
        {
            return ownerMatches && attackerMatches && attackerStillEnemy &&
                   attackerPresentInCompletedCycle;
        }

        public static bool ShouldCountMilitaryCapturePresence(bool participantIsCityOwner,
            bool cityOwnerHasActiveDefenders)
        {
            return !participantIsCityOwner || cityOwnerHasActiveDefenders;
        }

        public static bool ShouldRecordActiveMilitaryPresence(bool isActor,
            bool actorAlive, bool actorIsWarrior, bool actorHasKingdom)
        {
            return isActor && actorAlive && actorIsWarrior && actorHasKingdom;
        }

        public static float ExtraCapturePoints(bool pIsBeingCapturedByEnemy, bool pHasDefenders,
            bool pHasCityControlGoal, int pWatchTowerCount)
        {
            return ExtraCapturePoints(pIsBeingCapturedByEnemy, true, pHasDefenders,
                pHasCityControlGoal, pWatchTowerCount);
        }

        public static float ExtraCapturePoints(bool pIsBeingCapturedByEnemy, bool pHasActiveCaptureUnits,
            bool pHasDefenders, bool pHasCityControlGoal, int pWatchTowerCount)
        {
            return ExtraCapturePoints(pIsBeingCapturedByEnemy, pHasActiveCaptureUnits,
                pCanAdvanceCurrentCapture: true, pHasDefenders, pHasCityControlGoal, pWatchTowerCount);
        }

        public static float ExtraCapturePoints(bool pIsBeingCapturedByEnemy, bool pHasActiveCaptureUnits,
            bool pCanAdvanceCurrentCapture, bool pHasDefenders, bool pHasCityControlGoal,
            int pWatchTowerCount)
        {
            if (!pIsBeingCapturedByEnemy) return 0f;
            if (!pHasActiveCaptureUnits) return 0f;
            if (!pCanAdvanceCurrentCapture) return 0f;
            if (pHasDefenders) return 0f;

            float bonus = pHasCityControlGoal ? 1.55f : 0.45f;
            bonus -= Math.Max(0, pWatchTowerCount) * 0.35f;
            return Math.Max(0f, bonus);
        }
    }
}
