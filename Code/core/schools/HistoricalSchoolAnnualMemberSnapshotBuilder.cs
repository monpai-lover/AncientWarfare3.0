namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolAnnualMemberSnapshotBuilder
    {
        public static HistoricalSchoolAnnualMemberSnapshot<Actor> Build()
        {
            return new HistoricalSchoolAnnualMemberSnapshot<Actor>(
                SchoolMembershipService.ActiveMemberships(), FindActor,
                p => p?.data?.id ?? -1L,
                p => p?.data != null && p.isAlive() && !p.isRekt(),
                HistoricalSchoolDescentService.IsCanonicalMaster,
                ResidenceCityId,
                HistoricalAffiliationService.IsPresentForInfluence);
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static long ResidenceCityId(Actor pActor)
        {
            City city = HistoricalAffiliationService.ResidenceCity(pActor) ?? pActor?.city;
            return city?.data != null && !city.isRekt() ? city.data.id : -1L;
        }
    }
}
