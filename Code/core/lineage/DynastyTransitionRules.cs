namespace AncientWarfare3.core.lineage
{
    public enum DynastyTransitionStatus
    {
        NoChange,
        Created,
        Failure
    }

    public enum DynastyReignProjectionDisposition
    {
        Failure,
        SkipNoDynasty,
        Converged,
        Reconcile
    }

    public static class DynastyTransitionRules
    {
        public static bool TryResolve(DynastyTransitionStatus pStatus,
            out bool pCreated)
        {
            pCreated = pStatus == DynastyTransitionStatus.Created;
            return pStatus != DynastyTransitionStatus.Failure;
        }

        public static DynastyReignProjectionDisposition
            ResolveReignProjection(DynastyTransitionStatus pStatus,
                long currentDynastyId, long openReignDynastyId)
        {
            if (pStatus == DynastyTransitionStatus.Failure)
                return DynastyReignProjectionDisposition.Failure;
            if (currentDynastyId < 0L)
                return pStatus == DynastyTransitionStatus.Created
                    ? DynastyReignProjectionDisposition.Failure
                    : DynastyReignProjectionDisposition.SkipNoDynasty;
            if (pStatus == DynastyTransitionStatus.Created)
                return DynastyReignProjectionDisposition.Reconcile;
            return currentDynastyId == openReignDynastyId
                ? DynastyReignProjectionDisposition.Converged
                : DynastyReignProjectionDisposition.Reconcile;
        }

        public static bool ShouldProjectStateNameAsCreatedDynasty(
            DynastyTransitionStatus pStatus,
            DynastyReignProjectionDisposition pDisposition)
        {
            return pStatus != DynastyTransitionStatus.Failure &&
                   pDisposition ==
                   DynastyReignProjectionDisposition.Reconcile;
        }
    }
}
