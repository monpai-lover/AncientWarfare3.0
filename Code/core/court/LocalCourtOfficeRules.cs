namespace AncientWarfare3.core.court
{
    public static class LocalCourtOfficeRules
    {
        public static string OfficeForSlot(int pSlot,
            CourtProfileId pProfile)
        {
            if (pSlot == 0)
                return pProfile == CourtProfileId.Western
                    ? CourtOfficeId.WestMayor
                    : CourtOfficeId.Governor;
            if (pSlot == 1) return CourtOfficeId.GranaryOfficer;
            if (pSlot == 2) return CourtOfficeId.Constable;
            return "";
        }

        public static string OfficeForSlot(int pSlot,
            string pCityLeaderOfficeId)
        {
            if (pSlot == 0)
                return IsLocalLeaderOffice(pCityLeaderOfficeId)
                    ? pCityLeaderOfficeId
                    : CourtOfficeId.Governor;
            if (pSlot == 1) return CourtOfficeId.GranaryOfficer;
            if (pSlot == 2) return CourtOfficeId.Constable;
            return "";
        }

        public static bool IsLocalOffice(string pOfficeId)
        {
            return IsLocalLeaderOffice(pOfficeId) ||
                   pOfficeId == CourtOfficeId.GranaryOfficer ||
                   pOfficeId == CourtOfficeId.Constable;
        }

        public static bool IsLocalLeaderOffice(string pOfficeId)
        {
            return pOfficeId == CourtOfficeId.Governor ||
                   pOfficeId == CourtOfficeId.WestMayor ||
                   pOfficeId == CourtOfficeId.WestCount;
        }
    }
}
