namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalMapModeSchedulingRules
    {
        // These budgets keep first-open work below a small, predictable slice
        // of the MapBox frame.  The renderer keeps old cached objects visible
        // while the next slice is prepared.
        public const int MaximumEntryBudget = 1;
        public const int MaximumLabelBudget = 2;
        public const int MaximumBoundaryBudget = 96;

        public static int ClampEntryBudget(int pRequested)
        {
            return Clamp(pRequested, MaximumEntryBudget);
        }

        public static int ClampLabelBudget(int pRequested)
        {
            return Clamp(pRequested, MaximumLabelBudget);
        }

        public static int ClampBoundaryBudget(int pRequested)
        {
            return Clamp(pRequested, MaximumBoundaryBudget);
        }

        public static bool ShouldKeepCachedZones(bool snapshotUnchanged,
            bool renderStateDirty)
        {
            return snapshotUnchanged && !renderStateDirty;
        }

        private static int Clamp(int pRequested, int pMaximum)
        {
            if (pRequested <= 0) return 1;
            return pRequested > pMaximum ? pMaximum : pRequested;
        }
    }
}
