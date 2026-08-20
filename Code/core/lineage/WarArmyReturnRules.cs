namespace AncientWarfare3.core.lineage
{
    public enum WarArmyReturnOrderDecision
    {
        Continue = 0,
        Complete = 1,
        CancelForMission = 2
    }

    public static class WarArmyReturnRules
    {
        public const int ArrivalCenterRadius = 16;

        public static bool ShouldBeginReturn(bool armyAlive,
            long currentMissionWarId, long endedWarId)
        {
            return armyAlive && endedWarId >= 0L &&
                   currentMissionWarId == endedWarId;
        }

        public static bool MatchesDepartedParticipant(long missionWarId,
            long missionKingdomId, long departedWarId,
            long departedKingdomId)
        {
            return departedWarId >= 0L && departedKingdomId >= 0L &&
                   missionWarId == departedWarId &&
                   missionKingdomId == departedKingdomId;
        }

        public static bool ShouldReturnInvalidMission(bool armyAlive,
            bool missionExists, bool missionWarActive,
            bool missionKingdomParticipating)
        {
            return armyAlive && missionExists &&
                   (!missionWarActive || !missionKingdomParticipating);
        }

        public static bool HasArrived(bool armyAlive,
            bool insideFriendlySafeCity)
        {
            return !armyAlive || insideFriendlySafeCity;
        }

        public static bool ShouldReleaseActiveVoyageOnReturn(
            bool returnEligible, bool activeVoyage)
        {
            return returnEligible && activeVoyage;
        }

        public static bool IsInsideReturnArrivalRadius(
            bool insideFriendlySafeCity, long deltaX, long deltaY)
        {
            if (!insideFriendlySafeCity) return false;
            long radius = ArrivalCenterRadius;
            if (deltaX < -radius || deltaX > radius ||
                deltaY < -radius || deltaY > radius) return false;
            return deltaX * deltaX + deltaY * deltaY <= radius * radius;
        }

        public static bool ShouldAllowPeacetimeJob(bool returnActive)
        {
            return !returnActive;
        }

        public static bool ShouldWaitAtReturnArrival(bool arrivalVerified,
            bool actorIsCaptain, bool captainAtTargetCenter)
        {
            return arrivalVerified &&
                   (!actorIsCaptain || captainAtTargetCenter);
        }

        public static bool ShouldRepairReturnTask(bool returnActive,
            bool actorAlive, bool expectedJobActive,
            bool expectedTaskActive, bool actorMoving,
            bool transportOwned)
        {
            _ = actorMoving;
            return returnActive && actorAlive && !transportOwned &&
                   (!expectedJobActive || !expectedTaskActive);
        }

        public static bool ShouldCancelForMission(bool hasValidMission)
        {
            return hasValidMission;
        }

        public static bool IsMissionPublishable(bool armyAlive, long armyId,
            long missionArmyId, bool kingdomMatches, long warId,
            long targetCityId, bool runtimeMissionValid)
        {
            return armyAlive && armyId >= 0L && missionArmyId == armyId &&
                   kingdomMatches && warId >= 0L && targetCityId >= 0L &&
                   runtimeMissionValid;
        }

        public static bool ShouldCancelForPublishedMission(
            bool missionPublishable, bool controllerPublished,
            bool indexPublished)
        {
            return missionPublishable && controllerPublished &&
                   indexPublished;
        }

        public static WarArmyReturnOrderDecision ResolveOrder(
            bool armyAlive, bool insideFriendlySafeCity,
            bool hasValidMission)
        {
            if (ShouldCancelForMission(hasValidMission))
                return WarArmyReturnOrderDecision.CancelForMission;
            return HasArrived(armyAlive, insideFriendlySafeCity)
                ? WarArmyReturnOrderDecision.Complete
                : WarArmyReturnOrderDecision.Continue;
        }
    }
}
