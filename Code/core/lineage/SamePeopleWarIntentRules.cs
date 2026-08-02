namespace AncientWarfare3.core.lineage
{
    public enum SamePeopleWarRoute
    {
        NotApplicable,
        Territorial,
        SubjugationCompetition
    }

    public enum SamePeopleWarDirective
    {
        KeepSelection,
        SuppressSelection,
        PrepareClaim
    }

    public static class SamePeopleWarIntentRules
    {
        public const int TerritorialPercent = 80;

        public static int StableBucket(long attackerId, long targetId,
            int decisionPeriod)
        {
            unchecked
            {
                ulong value = (ulong)attackerId;
                value ^= (ulong)targetId + 0x9E3779B97F4A7C15UL +
                         (value << 6) + (value >> 2);
                value ^= (uint)decisionPeriod + 0xBF58476D1CE4E5B9UL;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return (int)(value % 100UL);
            }
        }

        public static SamePeopleWarRoute Resolve(
            WarAiPeopleRelation relation, long attackerId, long targetId,
            int decisionPeriod, bool territorialIntentLocked)
        {
            return RouteFromBucket(relation,
                StableBucket(attackerId, targetId, decisionPeriod),
                territorialIntentLocked);
        }

        public static SamePeopleWarRoute RouteFromBucket(
            WarAiPeopleRelation relation, int bucket,
            bool territorialIntentLocked)
        {
            if (relation != WarAiPeopleRelation.SameCulture &&
                relation != WarAiPeopleRelation.SameSpecies)
                return SamePeopleWarRoute.NotApplicable;
            if (territorialIntentLocked)
                return SamePeopleWarRoute.Territorial;
            int normalized = ((bucket % 100) + 100) % 100;
            return normalized < TerritorialPercent
                ? SamePeopleWarRoute.Territorial
                : SamePeopleWarRoute.SubjugationCompetition;
        }

        public static bool ShouldSuppressSubjugation(
            SamePeopleWarRoute route, string goalType)
        {
            if (route != SamePeopleWarRoute.Territorial) return false;
            return goalType == "force_vassal" ||
                   goalType == "force_tributary";
        }

        public static SamePeopleWarDirective ResolveDirective(
            SamePeopleWarRoute route, string selectedGoal,
            bool hasTerritorialOption, bool canFabricate)
        {
            if (route != SamePeopleWarRoute.Territorial ||
                !ShouldSuppressSubjugation(route, selectedGoal))
                return SamePeopleWarDirective.KeepSelection;
            if (!hasTerritorialOption && canFabricate)
                return SamePeopleWarDirective.PrepareClaim;
            return SamePeopleWarDirective.SuppressSelection;
        }
    }
}
