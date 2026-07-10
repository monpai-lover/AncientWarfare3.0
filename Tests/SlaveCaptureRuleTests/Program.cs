using System;
using AncientWarfare3.core.lineage;

namespace SlaveCaptureRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectScanBlocked("no_war", pHasEnemyWar: false, pInEnemyTerritory: true);
            ExpectScanBlocked("not_enemy_territory", pHasEnemyWar: true, pInEnemyTerritory: false);
            ExpectScanAllowed("wartime_enemy_territory", pHasEnemyWar: true, pInEnemyTerritory: true);
            ExpectCaptureSpatialSearchRadius();
            ExpectCityFallCaptureLimit();

            Console.WriteLine("Slave capture rule tests passed.");
            return 0;
        }

        private static void ExpectScanBlocked(string pLabel, bool pHasEnemyWar, bool pInEnemyTerritory)
        {
            if (SlaveCaptureCommandRules.ShouldScanForCaptureTargets(pHasEnemyWar, pInEnemyTerritory))
                throw new Exception($"Expected scan '{pLabel}' to be blocked.");
        }

        private static void ExpectScanAllowed(string pLabel, bool pHasEnemyWar, bool pInEnemyTerritory)
        {
            if (!SlaveCaptureCommandRules.ShouldScanForCaptureTargets(pHasEnemyWar, pInEnemyTerritory))
                throw new Exception($"Expected scan '{pLabel}' to be allowed.");
        }

        private static void ExpectCaptureSpatialSearchRadius()
        {
            if (ActorAiSearchThrottleRules.ChunkRadiusForTileRadius(80, 16) != 5)
                throw new Exception("An 80-tile catcher radius must cover five 16-tile chunks.");
        }

        private static void ExpectCityFallCaptureLimit()
        {
            if (SlaveCaptureCommandRules.CityFallSlaveTargetCount(
                    pEligibleCount: 1000, pRatio: 0.1f, pMaximum: 8) != 8)
                throw new Exception("Large captured cities must retain the eight-slave cap.");
            if (SlaveCaptureCommandRules.CityFallSlaveTargetCount(
                    pEligibleCount: 3, pRatio: 0.1f, pMaximum: 8) != 1)
                throw new Exception("A non-empty captured city must still produce one target.");
            if (SlaveCaptureCommandRules.CityFallSlaveTargetCount(
                    pEligibleCount: 0, pRatio: 0.1f, pMaximum: 8) != 0)
                throw new Exception("An empty candidate stream must not produce a target.");
        }
    }
}
