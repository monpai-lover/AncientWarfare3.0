namespace AncientWarfare3.core.lineage
{
    public static class ActiveMilitaryLifecycleRules
    {
        public static bool HasWartimeMilitaryLock(bool actorAlive,
            bool kingdomAtWar, bool isWarrior, bool hasArmy,
            bool isCurrentCaptain, bool isGeneral, bool isRoyalGuard,
            bool hasValidRtsMission)
        {
            if (!actorAlive) return false;
            if (hasValidRtsMission || isRoyalGuard) return true;
            return kingdomAtWar && (isWarrior || hasArmy ||
                   isCurrentCaptain || isGeneral);
        }

        public static bool CanBecomeCivilGovernor(bool actorAlive,
            bool hasWartimeMilitaryLock, bool isKing, bool isCityLeader)
        {
            return actorAlive && !hasWartimeMilitaryLock && !isKing &&
                   !isCityLeader;
        }

        public static bool CanCommitRetirement(bool retirementRequested,
            bool hasWartimeMilitaryLock, bool remainsWarrior,
            bool remainsInArmy)
        {
            return retirementRequested && !hasWartimeMilitaryLock &&
                   !remainsWarrior && !remainsInArmy;
        }

        public static bool ShouldInvalidateMissionForEndedWar(
            long missionWarId, long endedWarId)
        {
            return missionWarId >= 0L && missionWarId == endedWarId;
        }

        public static bool CanDemobilizeAtLocation(bool actorAlive,
            bool hasActiveWar, bool insideHomeKingdom,
            bool inFriendlySafeCity)
        {
            return actorAlive && !hasActiveWar && insideHomeKingdom &&
                   inFriendlySafeCity;
        }

        public static bool ShouldRepeatRetreatToAnchor(int missionGeneration,
            int completedGeneration, long candidateAnchorCityId,
            long completedAnchorCityId, bool hasNewThreat)
        {
            if (hasNewThreat) return true;
            return missionGeneration != completedGeneration ||
                   candidateAnchorCityId != completedAnchorCityId;
        }
    }
}
