using System;

namespace AncientWarfare3.core.court
{
    public static class LocalOfficialCandidateRules
    {
        public const int HometownBonus = 25;

        public static bool CanEnter(bool alive, bool adult, bool slave,
            bool alreadyOfficial, bool king, bool heir,
            bool examinationEnabled, string qualification,
            bool participatedAndFailedHigherStage)
        {
            if (!alive || !adult || slave || alreadyOfficial || king || heir)
                return false;
            if (!examinationEnabled) return true;
            return IsLocalQualification(qualification) ||
                   participatedAndFailedHigherStage;
        }

        public static bool IsLocalQualification(string pQualification)
        {
            return string.Equals(pQualification, "juren",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(pQualification, "gongshi",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(pQualification, "jinshi",
                       StringComparison.OrdinalIgnoreCase);
        }

        public static int Score(int ability, int merit,
            bool sameNativeCity)
        {
            return Math.Max(0, ability) / 2 + Math.Max(0, merit) +
                   (sameNativeCity ? HometownBonus : 0);
        }
    }
}
