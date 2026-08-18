using System;

namespace AncientWarfare3.core.court
{
    public static class CourtCityOfficeRules
    {
        public static string Resolve(CourtProfileId pProfile,
            string pInstitution, bool pFeudatorySeat)
        {
            if (pProfile != CourtProfileId.Western)
                return CourtOfficeId.Governor;
            if (pInstitution == CourtInstitutionId.WesternPrimitive)
                return "";
            if (pFeudatorySeat &&
                pInstitution == CourtInstitutionId.WesternFeudalBureaucratic)
                return CourtOfficeId.WestCount;
            return CourtOfficeId.WestMayor;
        }

        public static bool IsCityLeaderOffice(string pOfficeId)
        {
            return LocalCourtOfficeRules.IsLocalLeaderOffice(pOfficeId);
        }
    }
}
