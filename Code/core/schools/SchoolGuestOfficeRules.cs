using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.schools
{
    public enum GuestOfficeSubmissionOutcome
    {
        Rejected = 0,
        Queued = 1,
        Completed = 2
    }

    public readonly struct SchoolGuestOfficeRankCandidate
    {
        public SchoolGuestOfficeRankCandidate(long pActorId, float pScore)
            : this(pActorId, pScore, pActing: false)
        {
        }

        public SchoolGuestOfficeRankCandidate(long pActorId, float pScore,
            bool pActing)
        {
            ActorId = pActorId;
            Score = pScore;
            IsActing = pActing;
        }

        public long ActorId { get; }
        public float Score { get; }
        public bool IsActing { get; }
    }

    public static class SchoolGuestOfficeRules
    {
        public const int MinimumTermYears = 5;
        public const int MinTermYears = MinimumTermYears;
        public const int MaxTermYears = 20;

        public static int NormalizeTermYears(int pRequestedYears)
        {
            return Math.Max(MinimumTermYears,
                Math.Min(MaxTermYears, pRequestedYears));
        }

        public static int RenewedEndYear(int pCurrentYear, int pRequestedYears)
        {
            long end = (long)pCurrentYear + NormalizeTermYears(pRequestedYears);
            return end >= int.MaxValue ? int.MaxValue - 1 : (int)end;
        }

        public static bool ShouldCloseServiceOnRenewal(bool pRenewalSucceeded)
        {
            return !pRenewalSucceeded;
        }

        public static bool CanInvite(bool realScholar, bool alive, bool adult,
            bool residenceInHost, bool available, bool serviceFree, bool forbidden,
            bool centralOfficeMale, bool reputationFit, bool officeFit)
        {
            return realScholar && alive && adult && residenceInHost && available &&
                   serviceFree && !forbidden && centralOfficeMale && reputationFit && officeFit;
        }

        public static int AppointmentBudgetForHost(int pVacancyCount,
            int pMaxPerHost)
        {
            return Math.Min(Math.Max(0, pVacancyCount), Math.Max(0, pMaxPerHost));
        }

        public static bool IsQualifiedTeacher(
            bool pCanonicalMaster,
            HistoricalSchoolStanding pStanding)
        {
            return pCanonicalMaster ||
                   pStanding == HistoricalSchoolStanding.Teacher ||
                   pStanding == HistoricalSchoolStanding.Leader ||
                   pStanding == HistoricalSchoolStanding.CanonicalMaster;
        }

        public static int TermYears(long pActorId, long pHostKingdomId, int pYear)
        {
            unchecked
            {
                long value = pActorId * 6364136223846793005L +
                             pHostKingdomId * 1442695040888963407L + pYear * 31L;
                value ^= value >> 33;
                int span = MaxTermYears - MinimumTermYears + 1;
                int offset = (int)(Math.Abs(value == long.MinValue ? long.MaxValue : value) %
                                   span);
                return NormalizeTermYears(MinimumTermYears + offset);
            }
        }

        public static bool ShouldRenew(float pReputation, float pHostReceptiveness,
            int pRemainingYears, bool pHostAlive, bool pActorAlive)
        {
            if (!pHostAlive || !pActorAlive || pRemainingYears > 0) return false;
            float reputation = Bound01(pReputation / 100f);
            float receptiveness = Bound01(pHostReceptiveness);
            return reputation * 0.65f + receptiveness * 0.35f >= 0.6f;
        }

        public static SchoolGuestOfficeRankCandidate? SelectBestCandidate(
            IEnumerable<SchoolGuestOfficeRankCandidate> pCandidates)
        {
            if (pCandidates == null) return null;
            SchoolGuestOfficeRankCandidate best = default;
            bool found = false;
            foreach (SchoolGuestOfficeRankCandidate candidate in pCandidates)
            {
                if (found && !IsPreferred(candidate, best)) continue;
                best = candidate;
                found = true;
            }
            return found ? best : (SchoolGuestOfficeRankCandidate?)null;
        }

        public static bool IsPreferred(SchoolGuestOfficeRankCandidate pCandidate,
            SchoolGuestOfficeRankCandidate pCurrent)
        {
            if (pCandidate.IsActing != pCurrent.IsActing)
                return !pCandidate.IsActing;
            int scoreOrder = Comparer<float>.Default.Compare(pCandidate.Score,
                pCurrent.Score);
            return scoreOrder > 0 || scoreOrder == 0 &&
                   pCandidate.ActorId < pCurrent.ActorId;
        }

        public static bool ReservesOffice(GuestOfficeSubmissionOutcome pOutcome)
        {
            return pOutcome == GuestOfficeSubmissionOutcome.Queued ||
                   pOutcome == GuestOfficeSubmissionOutcome.Completed;
        }

        private static float Bound01(float pValue)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue)) return 0f;
            return Math.Max(0f, Math.Min(1f, pValue));
        }
    }
}
