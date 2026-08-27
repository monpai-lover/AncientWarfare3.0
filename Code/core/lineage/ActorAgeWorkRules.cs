using System;

namespace AncientWarfare3.core.lineage
{
    [Flags]
    public enum ActorAgeWorkStage
    {
        None = 0,
        DynasticTitle = 1,
        StandingArmyJob = 2,
        MilitaryRoleRelease = 4,
        All = DynasticTitle | StandingArmyJob | MilitaryRoleRelease
    }

    public static class ActorAgeWorkRules
    {
        public static bool ShouldTrack(bool wasTracked, bool dirty,
            bool dynasticEligible, bool warrior)
        {
            return wasTracked || dirty || dynasticEligible || warrior;
        }

        public static ActorAgeWorkStage Resolve(ActorAgeWorkState pPrevious,
            ActorAgeWorkState pCurrent, bool force)
        {
            bool annualFallback = pPrevious.YearBucket !=
                                  pCurrent.YearBucket;
            ActorAgeWorkStage result = ActorAgeWorkStage.None;

            if ((annualFallback && pCurrent.DynasticEligible) ||
                pPrevious.IsAdult != pCurrent.IsAdult ||
                pPrevious.DynasticEligible != pCurrent.DynasticEligible)
                result |= ActorAgeWorkStage.DynasticTitle;

            if (pPrevious.Profession != pCurrent.Profession ||
                pPrevious.InPermanentArmy != pCurrent.InPermanentArmy ||
                pPrevious.AtWar != pCurrent.AtWar ||
                pPrevious.ShouldUsePeacetimeJob !=
                pCurrent.ShouldUsePeacetimeJob)
                result |= ActorAgeWorkStage.StandingArmyJob;

            if ((annualFallback && pCurrent.ShouldReleaseMilitaryRole) ||
                pPrevious.Profession != pCurrent.Profession ||
                pPrevious.InPermanentArmy != pCurrent.InPermanentArmy ||
                pPrevious.AtWar != pCurrent.AtWar ||
                pPrevious.DynasticEligible != pCurrent.DynasticEligible ||
                pPrevious.ShouldReleaseMilitaryRole !=
                pCurrent.ShouldReleaseMilitaryRole)
                result |= ActorAgeWorkStage.MilitaryRoleRelease;

            if (!force) return result;

            result = ActorAgeWorkStage.None;
            if (pCurrent.DynasticEligible)
                result |= ActorAgeWorkStage.DynasticTitle;
            if (pCurrent.InPermanentArmy ||
                pCurrent.ShouldUsePeacetimeJob)
                result |= ActorAgeWorkStage.StandingArmyJob;
            if (pCurrent.ShouldReleaseMilitaryRole)
                result |= ActorAgeWorkStage.MilitaryRoleRelease;
            return result;
        }
    }
}
