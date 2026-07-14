using System;

namespace AncientWarfare3.core.schools
{
    public enum HistoricalSchoolStanding
    {
        Member,
        Disciple,
        Teacher,
        Leader,
        CanonicalMaster
    }

    public static class HistoricalSchoolStandingRules
    {
        public const int TeacherMembershipYears = 3;
        public const float TeacherReputation = 10f;
        public const int ConversionLoyaltyYears = 12;
        public const int TeacherAbsenceYears = 5;
        public const float RivalShareMinimum = 0.25f;

        public static HistoricalSchoolStanding ResolvePromotion(
            HistoricalSchoolStanding pCurrent,
            int pMembershipYears,
            float pReputation)
        {
            if (pCurrent != HistoricalSchoolStanding.Disciple) return pCurrent;
            return pMembershipYears >= TeacherMembershipYears &&
                   pReputation >= TeacherReputation
                ? HistoricalSchoolStanding.Teacher
                : pCurrent;
        }

        public static bool CanConvert(
            int pCurrentYear,
            int pMembershipStartYear,
            int pYearsWithoutTeacher,
            float pRivalShare,
            bool pBusy)
        {
            return !pBusy &&
                   pCurrentYear - pMembershipStartYear >= ConversionLoyaltyYears &&
                   pYearsWithoutTeacher >= TeacherAbsenceYears &&
                   !float.IsNaN(pRivalShare) &&
                   pRivalShare >= RivalShareMinimum;
        }

        public static int NextFairIndex(int pCurrentIndex, int pCount)
        {
            return pCount <= 0 ? -1 : (Math.Max(-1, pCurrentIndex) + 1) % pCount;
        }
    }
}
