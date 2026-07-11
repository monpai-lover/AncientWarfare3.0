namespace AncientWarfare3.core.court
{
    public static class RoyalMedicalCareRules
    {
        public static bool ShouldTreat(bool physicianAlive, bool physicianActive,
            bool sameKingdom, bool patientAlive)
        {
            return physicianAlive && physicianActive && sameKingdom && patientAlive;
        }

        public static bool ShouldRecordCure(int removedCurableTraits)
        {
            return removedCurableTraits > 0;
        }

        public static bool IsCachedPhysicianValid(long cachedActorId, long actorId,
            bool physicianAlive, bool sameKingdom, long courtKingdomId,
            long kingdomId, string officeId)
        {
            return cachedActorId >= 0 && cachedActorId == actorId && physicianAlive &&
                   sameKingdom && courtKingdomId == kingdomId &&
                   officeId == CourtOfficeId.ImperialPhysician;
        }

        public static bool ShouldClearCachedPhysician(long cachedActorId,
            long officerActorId, string officeId)
        {
            return cachedActorId >= 0 && cachedActorId == officerActorId &&
                   officeId == CourtOfficeId.ImperialPhysician;
        }
    }
}
