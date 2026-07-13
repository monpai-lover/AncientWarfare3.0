using System.Globalization;

namespace AncientWarfare3.core.schools
{
    public enum GuestOfficePersistenceOutcome
    {
        Unknown = 0,
        Committed = 1,
        CleanFailure = 2
    }

    public enum GuestOfficeProjectionState
    {
        Conflict = 0,
        Original = 1,
        Desired = 2,
        Both = 3
    }

    public readonly struct GuestOfficePersistenceResult
    {
        public GuestOfficePersistenceResult(GuestOfficePersistenceOutcome pOutcome)
        {
            Outcome = pOutcome;
        }

        public GuestOfficePersistenceOutcome Outcome { get; }
        public bool IsCommitted => Outcome == GuestOfficePersistenceOutcome.Committed;
    }

    public static class GuestOfficeReadbackRules
    {
        public static GuestOfficePersistenceOutcome Resolve(bool pQuerySucceeded,
            GuestOfficeProjectionState pAffiliation, GuestOfficeProjectionState pCareer,
            GuestOfficeProjectionState pSchoolEvent)
        {
            if (!pQuerySucceeded) return GuestOfficePersistenceOutcome.Unknown;
            if (MatchesDesired(pAffiliation) && MatchesDesired(pCareer) &&
                MatchesDesired(pSchoolEvent))
                return GuestOfficePersistenceOutcome.Committed;
            if (MatchesOriginal(pAffiliation) && MatchesOriginal(pCareer) &&
                MatchesOriginal(pSchoolEvent))
                return GuestOfficePersistenceOutcome.CleanFailure;
            return GuestOfficePersistenceOutcome.Unknown;
        }

        private static bool MatchesOriginal(GuestOfficeProjectionState pState)
        {
            return pState == GuestOfficeProjectionState.Original ||
                   pState == GuestOfficeProjectionState.Both;
        }

        private static bool MatchesDesired(GuestOfficeProjectionState pState)
        {
            return pState == GuestOfficeProjectionState.Desired ||
                   pState == GuestOfficeProjectionState.Both;
        }
    }

    public static class GuestOfficeAdoptionRules
    {
        public static bool ShouldAdopt(GuestOfficePersistenceOutcome pOutcome)
        {
            return pOutcome == GuestOfficePersistenceOutcome.Committed;
        }

        public static bool ShouldCompensate(GuestOfficePersistenceOutcome pOutcome)
        {
            return false;
        }
    }

    public static class GuestOfficePendingRules
    {
        public static bool ShouldRetain(GuestOfficePersistenceOutcome pOutcome,
            bool pAffiliationAdopted, bool pCourtApplied, bool pStatusApplied)
        {
            return pOutcome != GuestOfficePersistenceOutcome.Committed ||
                   !pAffiliationAdopted || !pCourtApplied || !pStatusApplied;
        }

        public static int DrainCount(int pPendingCount, int pBudget)
        {
            if (pPendingCount <= 0 || pBudget <= 0) return 0;
            return pPendingCount < pBudget ? pPendingCount : pBudget;
        }

        public static int RetryDelayFrames(int pAttempt, int pMaximumFrames)
        {
            if (pMaximumFrames <= 0) return 0;
            int exponent = pAttempt <= 1 ? 0 : pAttempt - 1;
            exponent = exponent > 30 ? 30 : exponent;
            long delay = 1L << exponent;
            return delay < pMaximumFrames ? (int)delay : pMaximumFrames;
        }

        public static bool ShouldRecordHistory(bool pRecoveredExisting)
        {
            return !pRecoveredExisting;
        }
    }

    public enum GuestOfficeRecoveryDecision
    {
        Retry = 0,
        Adopt = 1,
        End = 2
    }

    public static class GuestOfficeRecoveryRules
    {
        public static GuestOfficeRecoveryDecision Resolve(bool pQuerySucceeded,
            bool pCompleteTuple, bool pMixedTuple, bool pDefinitelyAbsent)
        {
            if (!pQuerySucceeded || pMixedTuple) return GuestOfficeRecoveryDecision.Retry;
            if (pCompleteTuple) return GuestOfficeRecoveryDecision.Adopt;
            return GuestOfficeRecoveryDecision.Retry;
        }
    }

    public static class GuestOfficeOperationKeyRules
    {
        public static string Build(string pEventType, long pActorId, long pHostKingdomId,
            long pCityId, string pSchoolId, string pOfficeId, int pStartYear, int pEndYear)
        {
            return Build(pEventType, pActorId, pHostKingdomId, pCityId, pSchoolId,
                pOfficeId, pStartYear, pEndYear, "");
        }

        public static string Build(string pEventType, long pActorId, long pHostKingdomId,
            long pCityId, string pSchoolId, string pOfficeId, int pStartYear, int pEndYear,
            string pTupleFingerprint)
        {
            string key = "guest-start:v1|event=" + (pEventType ?? "") +
                   "|actor=" + Invariant(pActorId) +
                   "|host=" + Invariant(pHostKingdomId) +
                   "|city=" + Invariant(pCityId) +
                   "|school=" + (pSchoolId ?? "") +
                   "|office=" + (pOfficeId ?? "") +
                   "|start=" + Invariant(pStartYear) +
                   "|end=" + Invariant(pEndYear);
            return string.IsNullOrEmpty(pTupleFingerprint)
                ? key
                : key + "|tuple=" + pTupleFingerprint;
        }

        private static string Invariant(long pValue)
        {
            return pValue.ToString(CultureInfo.InvariantCulture);
        }
    }
}
