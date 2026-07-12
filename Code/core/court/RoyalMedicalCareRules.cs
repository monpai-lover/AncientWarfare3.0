namespace AncientWarfare3.core.court
{
    public static class RoyalMedicalCareRules
    {
        public static long[] BuildTargetIds(long kingId, long heirId, bool physicianValid)
        {
            if (!physicianValid) return System.Array.Empty<long>();
            if (kingId < 0 && heirId < 0) return System.Array.Empty<long>();
            if (kingId >= 0 && heirId >= 0 && kingId != heirId) return new[] { kingId, heirId };
            return new[] { kingId >= 0 ? kingId : heirId };
        }

        public static long[] RemovedTargetIds(long oldKingId, long oldHeirId, long[] currentIds)
        {
            var current = new System.Collections.Generic.HashSet<long>(currentIds ??
                System.Array.Empty<long>());
            var removed = new System.Collections.Generic.List<long>(2);
            if (oldKingId >= 0 && !current.Contains(oldKingId)) removed.Add(oldKingId);
            if (oldHeirId >= 0 && oldHeirId != oldKingId && !current.Contains(oldHeirId))
                removed.Add(oldHeirId);
            return removed.ToArray();
        }

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
