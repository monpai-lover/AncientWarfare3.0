namespace AncientWarfare3.core.lineage
{
    public static class ArmyRtsSuccessionRecoveryRules
    {
        public const int MaximumArmiesPerCycle = 8;
        public const int CaptainRecoveryRetryCooldownCycles = 60;

        public static bool ShouldAttemptCaptainRecovery(long currentCycle,
            long retryAfterCycle)
        {
            return currentCycle >= retryAfterCycle;
        }

        public static bool ShouldEnqueue(bool kingdomValid, bool kingValid,
            bool fromLoad, long currentKingId, long requestedKingId,
            long completedKingId)
        {
            return kingdomValid && kingValid &&
                   requestedKingId >= 0L &&
                   currentKingId == requestedKingId &&
                   completedKingId != requestedKingId;
        }

        public static bool ShouldEnqueueCaptainRecovery(bool armyValid,
            bool actorWasCaptain, bool missionActive,
            bool wartimeEmergency, bool royalGuard)
        {
            return armyValid && actorWasCaptain && !royalGuard &&
                   (missionActive || wartimeEmergency);
        }

        public static bool ShouldEnqueueCaptainVacancy(bool armyValid,
            long previousCaptainId, long currentCaptainId,
            bool missionActive, bool wartimeEmergency, bool royalGuard,
            bool disposalScope)
        {
            return armyValid && previousCaptainId >= 0L &&
                   currentCaptainId < 0L && !royalGuard && !disposalScope &&
                   (missionActive || wartimeEmergency);
        }

        public static bool ShouldRetryCaptainRecovery(bool armyValid,
            bool captainOperational, int liveWarriorCount,
            bool missionActive, bool wartimeEmergency)
        {
            return armyValid && !captainOperational &&
                   liveWarriorCount > 0 &&
                   (missionActive || wartimeEmergency);
        }

        public static bool ShouldInstallWarriorCaptain(bool candidateEligible,
            bool captainAlreadyOperational)
        {
            return candidateEligible && !captainAlreadyOperational;
        }
    }
}
