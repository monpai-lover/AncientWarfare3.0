namespace AncientWarfare3.core.court
{
    public enum OfficialCareerPersistenceOutcome
    {
        Unknown,
        Committed,
        CleanFailure
    }

    public enum OfficialCareerMutation
    {
        Noop,
        Started,
        Reassigned,
        Refreshed
    }

    public sealed class OfficialCareerPrior
    {
        public OfficialCareerPrior(long pKingdomId, long pCityId, string pLayer,
            string pOfficeId)
        {
            KingdomId = pKingdomId;
            CityId = pCityId;
            Layer = pLayer ?? "";
            OfficeId = pOfficeId ?? "";
        }

        public long KingdomId { get; }
        public long CityId { get; }
        public string Layer { get; }
        public string OfficeId { get; }
    }

    public readonly struct OfficialCareerAppointmentResult
    {
        public OfficialCareerAppointmentResult(OfficialCareerPersistenceOutcome pPersistence,
            OfficialCareerMutation pMutation)
            : this(pPersistence, pMutation, null)
        {
        }

        public OfficialCareerAppointmentResult(OfficialCareerPersistenceOutcome pPersistence,
            OfficialCareerMutation pMutation, OfficialCareerPrior pPrior)
        {
            if (pPersistence == OfficialCareerPersistenceOutcome.Committed &&
                pMutation == OfficialCareerMutation.Reassigned && pPrior == null)
            {
                Persistence = OfficialCareerPersistenceOutcome.Unknown;
                Mutation = OfficialCareerMutation.Noop;
                Prior = null;
                return;
            }
            Persistence = pPersistence;
            Mutation = pMutation;
            Prior = pPersistence == OfficialCareerPersistenceOutcome.Committed &&
                    pMutation == OfficialCareerMutation.Reassigned
                ? pPrior
                : null;
        }

        public OfficialCareerPersistenceOutcome Persistence { get; }
        public OfficialCareerMutation Mutation { get; }
        public OfficialCareerPrior Prior { get; }
        public bool IsCommitted => Persistence == OfficialCareerPersistenceOutcome.Committed;
        public bool CreatedAppointmentEvent => IsCommitted &&
            (Mutation == OfficialCareerMutation.Started ||
             Mutation == OfficialCareerMutation.Reassigned);
    }

    public static class OfficialCareerProjectionRecoveryRules
    {
        public static OfficialCareerPrior SelectCleanupPrior(
            OfficialCareerAppointmentResult pResult, OfficialCareerPrior pRuntimePrior,
            long pTargetKingdomId, string pTargetOfficeId)
        {
            if (!pResult.IsCommitted) return null;
            if (pResult.Mutation == OfficialCareerMutation.Reassigned)
                return pResult.Prior;
            if (pResult.Mutation != OfficialCareerMutation.Refreshed ||
                pRuntimePrior == null) return null;

            return pRuntimePrior.KingdomId != pTargetKingdomId ||
                   pRuntimePrior.OfficeId != (pTargetOfficeId ?? "")
                ? pRuntimePrior
                : null;
        }
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
