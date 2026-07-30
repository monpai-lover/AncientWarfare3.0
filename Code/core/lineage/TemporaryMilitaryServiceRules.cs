namespace AncientWarfare3.core.lineage
{
    public static class TemporaryMilitaryServiceRules
    {
        public static bool ShouldDemobilize(
            bool temporaryRoleActive,
            bool militaryEmergency)
        {
            return temporaryRoleActive && !militaryEmergency;
        }
    }
}
