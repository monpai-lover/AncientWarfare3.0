namespace AncientWarfare3.core.lineage
{
    public enum DynastyTransitionStatus
    {
        NoChange,
        Created,
        Failure
    }

    public static class DynastyTransitionRules
    {
        public static bool TryResolve(DynastyTransitionStatus pStatus,
            out bool pCreated)
        {
            pCreated = pStatus == DynastyTransitionStatus.Created;
            return pStatus != DynastyTransitionStatus.Failure;
        }

        public static bool ShouldProjectCreatedDynasty(
            DynastyTransitionStatus pStatus)
        {
            return pStatus == DynastyTransitionStatus.Created;
        }
    }
}
