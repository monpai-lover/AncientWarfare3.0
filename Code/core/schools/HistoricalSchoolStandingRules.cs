using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public readonly struct HistoricalSchoolLeaderCandidate
    {
        public HistoricalSchoolLeaderCandidate(
            long pActorId,
            int pStartYear,
            HistoricalSchoolStanding pStanding,
            bool pAvailable)
        {
            ActorId = pActorId;
            StartYear = pStartYear;
            Standing = pStanding;
            Available = pAvailable;
        }

        public long ActorId { get; }
        public int StartYear { get; }
        public HistoricalSchoolStanding Standing { get; }
        public bool Available { get; }
    }

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

        public static long SelectLeaderActorId(
            IReadOnlyList<HistoricalSchoolLeaderCandidate> pCandidates)
        {
            long selectedActorId = -1L;
            int selectedStartYear = int.MaxValue;
            if (pCandidates == null) return selectedActorId;

            for (int i = 0; i < pCandidates.Count; i++)
            {
                HistoricalSchoolLeaderCandidate candidate = pCandidates[i];
                bool qualified = candidate.Standing == HistoricalSchoolStanding.Teacher ||
                                 candidate.Standing == HistoricalSchoolStanding.Leader;
                if (!candidate.Available || !qualified || candidate.ActorId < 0) continue;
                if (candidate.StartYear > selectedStartYear) continue;
                if (candidate.StartYear == selectedStartYear &&
                    selectedActorId >= 0 && candidate.ActorId >= selectedActorId) continue;
                selectedStartYear = candidate.StartYear;
                selectedActorId = candidate.ActorId;
            }
            return selectedActorId;
        }
    }
}
