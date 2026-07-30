namespace AncientWarfare3.core.lineage
{
    public enum LoverHeirConceptionDisposition
    {
        Cancel,
        Wait,
        Start
    }

    public static class DynasticLoverConceptionRules
    {
        public const int MalePercent = 70;

        public static bool IsInScope(bool holdsTitle,
            int paternalDistance, bool actorIsMale)
        {
            if (holdsTitle && paternalDistance == 0) return true;
            return actorIsMale && paternalDistance >= 1 &&
                   paternalDistance <= 3;
        }

        public static bool RollMakesMale(int pRoll)
        {
            return pRoll >= 0 && pRoll < MalePercent;
        }

        public static bool ShouldContinueAfterBirth(bool managedRequest,
            bool sonBorn)
        {
            return managedRequest && !sonBorn;
        }

        public static LoverHeirConceptionDisposition Evaluate(
            bool authority, bool mutual, bool motherAlive,
            bool fatherAlive, bool motherAdult, bool fatherAdult,
            bool motherBreedingAge, bool fatherBreedingAge,
            bool motherPregnant, bool motherFertile,
            bool fatherFertile, bool nutrition, bool citySafe,
            bool metaRoom, bool worldLaw)
        {
            if (!mutual || !motherAlive || !fatherAlive ||
                !motherAdult || !fatherAdult || !motherBreedingAge ||
                !fatherBreedingAge)
                return LoverHeirConceptionDisposition.Cancel;
            return authority && !motherPregnant && motherFertile &&
                   fatherFertile && nutrition && citySafe && metaRoom &&
                   worldLaw
                ? LoverHeirConceptionDisposition.Start
                : LoverHeirConceptionDisposition.Wait;
        }
    }
}
