using System;

namespace AncientWarfare3.core.lineage
{
    public static class CityOccupationAccelerationRules
    {
        public static bool HasActiveDefenderSignal(bool pZonePresence,
            bool pIndexedGarrisonPresence)
        {
            return pZonePresence || pIndexedGarrisonPresence;
        }

        public static bool HasReachedNaturalCaptureLimit(float pCaptureProgress)
        {
            return pCaptureProgress >= 100f;
        }

        public static bool ShouldBlockPermanentTransfer(
            bool pActiveHostileWar, bool pFreezeRecorded,
            bool pPeaceExecution)
        {
            return pActiveHostileWar && !pPeaceExecution;
        }

        public static bool ShouldCommitBanditSuppressionCapture(
            bool pActiveSuppressionWar, bool pRecipientIsBandit,
            bool pRecipientAlreadyOwnsCity)
        {
            return pActiveSuppressionWar && pRecipientIsBandit &&
                   !pRecipientAlreadyOwnsCity;
        }

        public static bool ShouldAttemptControlledSettlementImmediately(
            bool hasOpenNonTerritorialGoal, bool cityManagerLocked)
        {
            return hasOpenNonTerritorialGoal && !cityManagerLocked;
        }

        public static bool ShouldTreatSettlementAttemptAsComplete(
            bool settlementReportedSuccess, bool warEndedAfterAttempt)
        {
            return settlementReportedSuccess || warEndedAfterAttempt;
        }

        public static bool ShouldHonorQueuedCompletion(
            bool completionAuthorized, bool cityOwnerUnchanged,
            bool capturerStillEnemy)
        {
            return completionAuthorized && cityOwnerUnchanged &&
                   capturerStillEnemy;
        }

        public static bool ShouldRetryQueuedSettlement(
            bool settlementSucceeded, bool goalStillOpen)
        {
            return !settlementSucceeded && goalStillOpen;
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

        public static float ApplyResistance(float pCaptureDelta,
            float pOccupationResistance)
        {
            if (pCaptureDelta <= 0f) return pCaptureDelta;
            float resistance = Math.Max(0f,
                Math.Min(0.95f, pOccupationResistance));
            return pCaptureDelta * (1f - resistance);
        }

    }
}
