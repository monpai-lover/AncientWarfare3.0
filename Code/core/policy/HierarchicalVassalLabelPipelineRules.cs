namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalLabelPipelineRules
    {
        internal const int MaximumInFlightWorkers = 2;

        internal static bool CanSubmit(int pInFlightCount)
        {
            return pInFlightCount >= 0 &&
                   pInFlightCount < MaximumInFlightWorkers;
        }

        internal static bool CanFinish(bool pAllSourcesSubmitted,
            bool pCollecting, int pInFlightCount)
        {
            return pAllSourcesSubmitted && !pCollecting &&
                   pInFlightCount == 0;
        }
    }
}
