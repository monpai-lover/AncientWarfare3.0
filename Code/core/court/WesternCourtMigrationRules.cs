namespace AncientWarfare3.core.court
{
    public static class WesternCourtMigrationRules
    {
        public static string NormalizeOfficeId(string pOfficeId)
        {
            return pOfficeId == CourtOfficeId.WestRoyalChamberlain
                ? CourtOfficeId.WestRoyalConstable
                : pOfficeId ?? "";
        }
    }
}
