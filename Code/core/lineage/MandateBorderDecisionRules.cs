using System;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateBorderDecisionRules
    {
        internal const int MaximumUsesPerDynasty = 2;
        internal const int MandateCost = 20;

        internal static bool CanExecute(int pUses, int pMandate)
        {
            return pUses >= 0 && pUses < MaximumUsesPerDynasty &&
                   pMandate >= MandateCost;
        }

        internal static int NextUseCount(int pUses)
        {
            return Math.Max(0, pUses) + 1;
        }

        internal static int RemainingMandate(int pMandate)
        {
            return pMandate - MandateCost;
        }
    }
}
