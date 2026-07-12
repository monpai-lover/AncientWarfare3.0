namespace AncientWarfare3.core.court
{
    public static class CourtTraitRules
    {
        public static bool ShouldHoldSchoolTrait(bool pHasActiveMembership, bool pAlive,
            bool pMembershipClosed)
        {
            return pHasActiveMembership && pAlive && !pMembershipClosed;
        }

        public static string TraitForSchool(string schoolId)
        {
            return CourtSchoolRegistry.Find(schoolId)?.TraitId ?? "";
        }
    }
}
