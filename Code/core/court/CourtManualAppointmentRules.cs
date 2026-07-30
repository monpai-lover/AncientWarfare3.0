using System;

namespace AncientWarfare3.core.court
{
    public enum CourtManualAppointmentResult
    {
        Success,
        InvalidKingdom,
        InvalidOffice,
        OfficeOccupied,
        OfficeChanged,
        InvalidActor,
        CandidateIneligible,
        PersistenceFailed
    }

    public enum CourtManualOfficeAction
    {
        None,
        Select,
        Replace
    }

    public enum CourtReplacementPersistenceOutcome
    {
        Unknown,
        Committed,
        CleanFailure
    }

    public readonly struct CourtManualCandidateFacts
    {
        public readonly bool Alive;
        public readonly bool Adult;
        public readonly bool Domestic;
        public readonly bool Slave;
        public readonly bool Madness;
        public readonly bool Male;
        public readonly bool RoyalAsylum;
        public readonly bool King;
        public readonly bool HasCentralOffice;
        public readonly bool AffiliationAvailable;

        public CourtManualCandidateFacts(bool alive, bool adult, bool domestic, bool slave,
            bool madness, bool male, bool royalAsylum, bool king,
            bool hasCentralOffice, bool affiliationAvailable)
        {
            Alive = alive;
            Adult = adult;
            Domestic = domestic;
            Slave = slave;
            Madness = madness;
            Male = male;
            RoyalAsylum = royalAsylum;
            King = king;
            HasCentralOffice = hasCentralOffice;
            AffiliationAvailable = affiliationAvailable;
        }

    }

    public static class CourtManualAppointmentRules
    {
        public const int CandidateScanPerFrame = 32;
        public const int CandidateRowsPerFrame = 4;
        public const int CandidatePageSize = 48;
        public const double CandidateFrameBudgetMilliseconds = 1d;

        public static bool CanListCandidate(CourtManualCandidateFacts pFacts)
        {
            return pFacts.Alive && pFacts.Adult && pFacts.Domestic && !pFacts.Slave &&
                   !pFacts.Madness && pFacts.Male && !pFacts.RoyalAsylum &&
                   !pFacts.King && !pFacts.HasCentralOffice &&
                   pFacts.AffiliationAvailable;
        }

        public static bool IsSchoolEligible(string pCandidateSchoolId,
            string pPreferredSchoolId)
        {
            return true;
        }

        public static float CandidateScore(float pAbilityScore,
            float pSchoolCompatibilityBonus)
        {
            return pAbilityScore + Math.Max(0f, pSchoolCompatibilityBonus);
        }

        public static bool CanCommit(bool officeInCurrentTier,
            bool officeVacant, bool candidateEligible)
        {
            return officeInCurrentTier && officeVacant && candidateEligible;
        }

        public static CourtManualOfficeAction ResolveOfficeAction(
            bool officeInCurrentTier, long incumbentActorId)
        {
            if (!officeInCurrentTier) return CourtManualOfficeAction.None;
            return incumbentActorId >= 0
                ? CourtManualOfficeAction.Replace
                : CourtManualOfficeAction.Select;
        }

        public static CourtManualAppointmentResult ValidateTarget(
            bool officeInCurrentTier, long expectedIncumbentActorId,
            long actualIncumbentActorId)
        {
            if (!officeInCurrentTier)
                return CourtManualAppointmentResult.InvalidOffice;
            if (expectedIncumbentActorId < 0)
                return actualIncumbentActorId < 0
                    ? CourtManualAppointmentResult.Success
                    : CourtManualAppointmentResult.OfficeOccupied;
            if (actualIncumbentActorId < 0 ||
                actualIncumbentActorId == expectedIncumbentActorId)
                return CourtManualAppointmentResult.Success;
            return CourtManualAppointmentResult.OfficeChanged;
        }

        public static bool CanChooseCandidate(long candidateActorId,
            long incumbentActorId)
        {
            return candidateActorId >= 0 && candidateActorId != incumbentActorId;
        }

        public static bool IsMilitaryCentralOffice(string pOfficeId)
        {
            return string.Equals(pOfficeId, CourtOfficeId.SiMa,
                       StringComparison.Ordinal) ||
                   string.Equals(pOfficeId, "marshal", StringComparison.Ordinal) ||
                   string.Equals(pOfficeId, "bingbu", StringComparison.Ordinal);
        }

        public static bool ShouldReleaseMilitaryIdentity(string pLayer,
            string pOfficeId)
        {
            return string.Equals(pLayer, "central", StringComparison.Ordinal) &&
                   !IsMilitaryCentralOffice(pOfficeId);
        }

        public static int PageCount(int pCandidateCount)
        {
            int count = Math.Max(0, pCandidateCount);
            return Math.Max(1, (count + CandidatePageSize - 1) / CandidatePageSize);
        }

        public static CourtReplacementPersistenceOutcome ResolveReplacementOutcome(
            bool closeCommitted, bool closeCleanFailure,
            bool appointmentCommitted, bool appointmentCleanFailure)
        {
            if (closeCommitted && appointmentCommitted)
                return CourtReplacementPersistenceOutcome.Committed;
            if (closeCleanFailure && appointmentCleanFailure)
                return CourtReplacementPersistenceOutcome.CleanFailure;
            return CourtReplacementPersistenceOutcome.Unknown;
        }

        public static int CompareCandidates(float leftScore, long leftActorId,
            float rightScore, long rightActorId)
        {
            int scoreOrder = rightScore.CompareTo(leftScore);
            return scoreOrder != 0 ? scoreOrder : leftActorId.CompareTo(rightActorId);
        }
    }
}
