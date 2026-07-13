namespace AncientWarfare3.core.court
{
    public enum OfficialCareerPersistenceOutcome
    {
        Committed,
        CleanFailure,
        Unknown
    }

    public enum OfficialCareerMutation
    {
        Started,
        Reassigned,
        Refreshed
    }

    public readonly struct OfficialCareerAppointmentResult
    {
        public OfficialCareerAppointmentResult(OfficialCareerPersistenceOutcome pPersistence,
            OfficialCareerMutation pMutation)
        {
            Persistence = pPersistence;
            Mutation = pMutation;
        }

        public OfficialCareerPersistenceOutcome Persistence { get; }
        public OfficialCareerMutation Mutation { get; }
        public bool IsCommitted => Persistence == OfficialCareerPersistenceOutcome.Committed;
        public bool CreatedAppointmentEvent => IsCommitted &&
            (Mutation == OfficialCareerMutation.Started ||
             Mutation == OfficialCareerMutation.Reassigned);
    }

    public static class OfficialCareerReadbackRules
    {
        public static OfficialCareerPersistenceOutcome Resolve(bool pQuerySucceeded,
            int pActiveCount, bool pDesiredExact, bool pOriginalExisted,
            bool pOriginalExact)
        {
            if (!pQuerySucceeded || pActiveCount < 0)
                return OfficialCareerPersistenceOutcome.Unknown;
            if (pActiveCount == 1 && pDesiredExact)
                return OfficialCareerPersistenceOutcome.Committed;
            if (pOriginalExisted)
                return pActiveCount == 1 && pOriginalExact
                    ? OfficialCareerPersistenceOutcome.CleanFailure
                    : OfficialCareerPersistenceOutcome.Unknown;
            return pActiveCount == 0
                ? OfficialCareerPersistenceOutcome.CleanFailure
                : OfficialCareerPersistenceOutcome.Unknown;
        }
    }
}
