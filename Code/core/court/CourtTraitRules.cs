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
            return schoolId switch
            {
                CourtSchoolId.Ru => CourtTraitId.Ru,
                CourtSchoolId.Legalist => CourtTraitId.Legalist,
                CourtSchoolId.Dao => CourtTraitId.Dao,
                CourtSchoolId.Mohist => CourtTraitId.Mohist,
                CourtSchoolId.Military => CourtTraitId.Military,
                CourtSchoolId.Diplomat => CourtTraitId.Diplomat,
                CourtSchoolId.Agrarian => CourtTraitId.Agrarian,
                CourtSchoolId.YinYang => CourtTraitId.YinYang,
                CourtSchoolId.Logician => CourtTraitId.Logician,
                CourtSchoolId.Medical => CourtTraitId.Medical,
                CourtSchoolId.Syncretist => CourtTraitId.Syncretist,
                CourtSchoolId.Merchant => CourtTraitId.Merchant,
                CourtSchoolId.Craftsman => CourtTraitId.Craftsman,
                CourtSchoolId.Historian => CourtTraitId.Historian,
                _ => ""
            };
        }
    }
}
