using System;

namespace AncientWarfare3.core.performance
{
    internal static class RuntimePerformanceBudgetRules
    {
        // A watchdog pass is observational. Spread it over nearby frames so
        // recovery remains prompt without competing with actor movement.
        public const int MaximumWatchdogArmiesPerFrame = 2;
        public const int MaximumFollowerChecksPerArmy = 3;
        public const int FollowerScanMultiplier = 8;

        public static int ResolveWatchdogArmiesPerFrame(int pendingArmies)
        {
            if (pendingArmies <= 0) return 0;
            return Math.Min(MaximumWatchdogArmiesPerFrame, pendingArmies);
        }

        public static int ResolveFollowerChecksPerArmy(int requestedChecks)
        {
            if (requestedChecks <= 0) return 0;
            return Math.Min(MaximumFollowerChecksPerArmy, requestedChecks);
        }

        public static int ResolveFollowerScanWindow(int requestedChecks)
        {
            int checks = ResolveFollowerChecksPerArmy(requestedChecks);
            return checks <= 0 ? 0 : checks * FollowerScanMultiplier;
        }
    }
}
