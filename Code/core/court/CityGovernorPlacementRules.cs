namespace AncientWarfare3.core.court
{
    public static class CityGovernorPlacementRules
    {
        public static bool ShouldPlace(bool newAssignment, bool actorValid,
            bool cityValid, bool isCurrentLeader,
            bool isInDestinationZone)
        {
            return newAssignment && actorValid && cityValid &&
                   isCurrentLeader && !isInDestinationZone;
        }
    }
}
