using System;

namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolRecoveryRules
    {
        public const int MinimumLivingMembers = 4;
        public const int MaxSchoolsPerYear = 4;
        public const int MaxTeachersPerSchoolAttempt = 8;
        public const int MaxCandidatesPerSchoolAttempt = 8;

        public static bool NeedsRecruitment(int pLivingMembers,
            int pQualifiedTeachers, bool pPendingRecruitment)
        {
            return pLivingMembers > 0 &&
                   pLivingMembers < MinimumLivingMembers &&
                   pQualifiedTeachers > 0 &&
                   !pPendingRecruitment;
        }

        public static int SchoolWorkBudget(int pDeficientSchoolCount)
        {
            return Math.Min(MaxSchoolsPerYear,
                Math.Max(0, pDeficientSchoolCount));
        }

        public static int CandidateWorkBudget(int pCandidateCount)
        {
            return Math.Min(MaxCandidatesPerSchoolAttempt,
                Math.Max(0, pCandidateCount));
        }

        public static int CandidateStart(int pYear, int pSchoolIndex,
            int pCandidateCount)
        {
            if (pCandidateCount <= 0) return 0;
            long cursor = (long)Math.Max(0, pYear) *
                          MaxCandidatesPerSchoolAttempt +
                          (long)Math.Max(0, pSchoolIndex) * 17L;
            return (int)(cursor % pCandidateCount);
        }

        public static int CandidateIndex(int pStart, int pOffset,
            int pCandidateCount)
        {
            if (pCandidateCount <= 0) return 0;
            int start = pStart % pCandidateCount;
            if (start < 0) start += pCandidateCount;
            int offset = Math.Max(0, pOffset) % pCandidateCount;
            return (start + offset) % pCandidateCount;
        }

        public static bool ShouldPromoteContinuityTeacher(
            int pLivingMembers, int pQualifiedTeachers,
            HistoricalSchoolStanding pStanding, bool pPresent,
            int pMembershipYears, float pReputation)
        {
            bool eligibleStanding =
                pStanding == HistoricalSchoolStanding.Member ||
                pStanding == HistoricalSchoolStanding.Disciple;
            return pLivingMembers > 0 &&
                   pLivingMembers < MinimumLivingMembers &&
                   pQualifiedTeachers == 0 &&
                   pPresent && eligibleStanding &&
                   pMembershipYears >=
                   HistoricalSchoolStandingRules.TeacherMembershipYears &&
                   !float.IsNaN(pReputation) &&
                   !float.IsInfinity(pReputation) &&
                   pReputation >= HistoricalSchoolStandingRules.TeacherReputation;
        }
    }
}
