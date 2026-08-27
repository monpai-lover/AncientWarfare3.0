namespace AncientWarfare3.core.lineage
{
    public readonly struct ActorAgeWorkState
    {
        public ActorAgeWorkState(bool isAdult, int profession,
            bool inPermanentArmy, bool atWar, bool dynasticEligible,
            bool shouldUsePeacetimeJob, bool shouldReleaseMilitaryRole,
            int yearBucket)
        {
            IsAdult = isAdult;
            Profession = profession;
            InPermanentArmy = inPermanentArmy;
            AtWar = atWar;
            DynasticEligible = dynasticEligible;
            ShouldUsePeacetimeJob = shouldUsePeacetimeJob;
            ShouldReleaseMilitaryRole = shouldReleaseMilitaryRole;
            YearBucket = yearBucket;
        }

        public bool IsAdult { get; }
        public int Profession { get; }
        public bool InPermanentArmy { get; }
        public bool AtWar { get; }
        public bool DynasticEligible { get; }
        public bool ShouldUsePeacetimeJob { get; }
        public bool ShouldReleaseMilitaryRole { get; }
        public int YearBucket { get; }
    }
}
