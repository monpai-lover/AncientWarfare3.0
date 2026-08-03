using System;

namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalLabelInvalidationRules
    {
        private const int MinimumChangedZones = 8;
        private const double ChangedZoneRatio = 0.03d;

        internal static int RequiredChangedZones(int pCurrentZoneCount)
        {
            int proportional = (int)Math.Ceiling(
                Math.Max(0, pCurrentZoneCount) * ChangedZoneRatio);
            return Math.Max(MinimumChangedZones, proportional);
        }

        internal static bool ShouldRecalculate(int pChangedZoneCount,
            int pCurrentZoneCount, bool pForced)
        {
            return pForced || pChangedZoneCount >=
                RequiredChangedZones(pCurrentZoneCount);
        }
    }
}
