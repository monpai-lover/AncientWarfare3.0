namespace AncientWarfare3.core.court
{
    public static class CourtTraitRules
    {
        public static bool ShouldHoldSchoolTrait(bool isOfficer, bool alive, bool defected)
        {
            return isOfficer && alive && !defected;
        }

        public static string TraitForSchool(string schoolId)
        {
            return CourtSchoolRegistry.Find(schoolId)?.TraitId ?? "";
        }
    }
}
