using System;

namespace AncientWarfare3.core.court
{
    public static class CourtBureauRules
    {
        public const int RefreshIntervalYears = 5;

        public static bool ShouldRefreshCityBureau(int currentYear, int lastRefreshYear, bool hasOfficialCourt)
        {
            return hasOfficialCourt && CourtRules.ShouldRefreshCourt(currentYear, lastRefreshYear, RefreshIntervalYears);
        }

        public static float BureauEfficiency(int officeSlots, int filledSlots)
        {
            if (officeSlots <= 0) return 0f;
            float ratio = Math.Max(0f, filledSlots) / (float)officeSlots;
            return Math.Min(100f, 30f + ratio * 60f);
        }

        public static string CityOfficeForSlot(int pSlot, bool pIsCapital)
        {
            if (pSlot <= 0) return CourtOfficeId.Governor;
            if (pSlot == 1) return CourtOfficeId.GranaryOfficer;
            return CourtOfficeId.Constable;
        }

        public static int FilledSlots(int officeSlots, float courtEfficiency)
        {
            if (officeSlots <= 0) return 0;
            float ratio = Math.Max(0f, Math.Min(100f, courtEfficiency)) / 100f;
            int filled = (int)Math.Round(officeSlots * ratio, MidpointRounding.AwayFromZero);
            return Math.Max(0, Math.Min(officeSlots, filled));
        }

        public static string PreferredSchoolForCityOffice(string pOfficeId)
        {
            switch (pOfficeId ?? "")
            {
                case CourtOfficeId.Governor:
                    return CourtSchoolId.Ru;
                case CourtOfficeId.GranaryOfficer:
                    return CourtSchoolId.Agrarian;
                case CourtOfficeId.Constable:
                    return CourtSchoolId.Legalist;
                default:
                    return CourtSchoolId.None;
            }
        }

        public static bool ShouldRecordCityBureauChange(int previousSlots, int nextSlots,
            string previousSchool, string nextSchool)
        {
            if (previousSlots <= 0 && nextSlots > 0) return true;
            if (previousSlots != nextSlots) return true;
            return !string.Equals(previousSchool ?? "", nextSchool ?? "", StringComparison.Ordinal);
        }
    }
}
