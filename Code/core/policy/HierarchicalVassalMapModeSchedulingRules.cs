namespace AncientWarfare3.core.policy
{
    /// <summary>
    /// Small main-thread budgets for label discovery and tile-coordinate
    /// copying. Native kingdom map rendering remains owned by WorldBox.
    /// </summary>
    internal static class HierarchicalVassalMapModeSchedulingRules
    {
        public const int MaximumLabelBudget = 2;
        public const int MaximumLabelTileCopyBudget = 512;
        public const int MaximumInactiveLabelBudget = 1;
        public const int MaximumInactiveLabelTileCopyBudget = 128;
        public const int MaximumLabelIndexBudget = 512;
        public const int MaximumInactiveLabelIndexBudget = 128;
        public const int MaximumLabelDiscoveryKingdomBudget = 2;
        public const int MaximumLabelDiscoveryCityBudget = 4;
        public const int MaximumLabelDiscoveryZoneBudget = 32;
        public const int MaximumInactiveLabelDiscoveryKingdomBudget = 1;
        public const int MaximumInactiveLabelDiscoveryCityBudget = 2;
        public const int MaximumInactiveLabelDiscoveryZoneBudget = 16;
        public const int MaximumInFlightLabelWorkers =
            HierarchicalVassalLabelPipelineRules.MaximumInFlightWorkers;

        internal static int ClampLabelBudget(int pRequested)
        {
            return Clamp(pRequested, MaximumLabelBudget);
        }

        private static int Clamp(int pRequested, int pMaximum)
        {
            if (pRequested <= 0) return 1;
            return pRequested > pMaximum ? pMaximum : pRequested;
        }
    }
}
