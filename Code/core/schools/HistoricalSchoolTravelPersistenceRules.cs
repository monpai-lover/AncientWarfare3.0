namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolTravelPersistenceRules
    {
        public static string OperationKey(long pActorId,
            long pDestinationCityId, int pYear)
        {
            return "journey_arrival:" + pActorId + ":" +
                   pDestinationCityId + ":" + pYear;
        }

        public static HistoricalSchoolTeachingPersistenceOutcome Combine(
            HistoricalSchoolTeachingPersistenceOutcome pAffiliation,
            HistoricalSchoolTeachingPersistenceOutcome pJourneyEvent)
        {
            if (pAffiliation ==
                    HistoricalSchoolTeachingPersistenceOutcome.Unknown ||
                pJourneyEvent ==
                    HistoricalSchoolTeachingPersistenceOutcome.Unknown)
                return HistoricalSchoolTeachingPersistenceOutcome.Unknown;
            if (pAffiliation ==
                    HistoricalSchoolTeachingPersistenceOutcome.CleanFailure ||
                pJourneyEvent ==
                    HistoricalSchoolTeachingPersistenceOutcome.CleanFailure)
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            if (pAffiliation ==
                    HistoricalSchoolTeachingPersistenceOutcome.Replayed ||
                pJourneyEvent ==
                    HistoricalSchoolTeachingPersistenceOutcome.Replayed)
                return HistoricalSchoolTeachingPersistenceOutcome.Replayed;
            return HistoricalSchoolTeachingPersistenceOutcome.Committed;
        }
    }
}
