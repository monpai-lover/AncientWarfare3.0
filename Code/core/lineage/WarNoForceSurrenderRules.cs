using System;

namespace AncientWarfare3.core.lineage
{
    internal static class WarNoForceSurrenderRules
    {
        public const int MinimumLateWarYears = 3;

        public static bool IsNoForce(int activeFieldSoldiers,
            int reserveSoldiers, int recruitableSoldiers,
            int minimumOperationalArmyCount)
        {
            return Math.Max(0, activeFieldSoldiers) == 0 &&
                   Math.Max(0, reserveSoldiers) == 0 &&
                   Math.Max(0, recruitableSoldiers) == 0 &&
                   Math.Max(0, minimumOperationalArmyCount) == 0;
        }

        public static bool ShouldSurrender(int warYears, bool sideNoForce,
            bool enemyHasForce, bool ordinaryNegotiationBlocked,
            bool bothSidesNoForce)
        {
            _ = ordinaryNegotiationBlocked;
            return warYears >= MinimumLateWarYears && sideNoForce &&
                   enemyHasForce && !bothSidesNoForce;
        }

        public static bool ShouldTransferRegion(
            bool isLargestConnectedRegion, bool isDisconnectedEnclave)
        {
            return isLargestConnectedRegion && !isDisconnectedEnclave;
        }
    }
}
