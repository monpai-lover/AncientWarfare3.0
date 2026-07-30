namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolMaritimeTravelRules
    {
        public static bool ShouldRequestTaxi(bool travelValid,
            bool actorInsideBoat, bool sameIsland,
            bool hasOwnedRequest)
        {
            return travelValid && !actorInsideBoat && !sameIsland &&
                   !hasOwnedRequest;
        }

        public static bool ShouldResumeAfterDisembark(bool ownedTravel,
            bool actorUsable, bool lifecycleTravelling,
            bool destinationValid, bool reachedDestinationIsland)
        {
            return ownedTravel && actorUsable && lifecycleTravelling &&
                   destinationValid && reachedDestinationIsland;
        }

        public static bool ShouldCancelOwnedTravel(bool ownedTravel,
            bool actorUsable, bool lifecycleTravelling,
            bool destinationValid)
        {
            return ownedTravel && (!actorUsable || !lifecycleTravelling ||
                                   !destinationValid);
        }
    }
}
