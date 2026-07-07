using System;
using AncientWarfare3.core.lineage;

namespace CityMaintenanceRuleTests
{
    internal static class Program
    {
        private static int Main()
        {
            ExpectStaggeredCityMaintenance();
            ExpectRetirementCheapGate();
            ExpectOldHeadRefreshGate();

            Console.WriteLine("City maintenance rule tests passed.");
            return 0;
        }

        private static void ExpectStaggeredCityMaintenance()
        {
            if (CityMaintenanceThrottleRules.ShouldRunStaggered(
                    pNow: 100, pLastRun: -1, pInterval: 20, pObjectId: 3))
                throw new Exception("First staggered maintenance should wait for the city id slot.");

            if (!CityMaintenanceThrottleRules.ShouldRunStaggered(
                    pNow: 103, pLastRun: -1, pInterval: 20, pObjectId: 3))
                throw new Exception("First staggered maintenance should run on its city id slot.");

            if (CityMaintenanceThrottleRules.ShouldRunStaggered(
                    pNow: 119, pLastRun: 103, pInterval: 20, pObjectId: 3))
                throw new Exception("Staggered maintenance should not run before the interval.");

            if (!CityMaintenanceThrottleRules.ShouldRunStaggered(
                    pNow: 123, pLastRun: 103, pInterval: 20, pObjectId: 3))
                throw new Exception("Staggered maintenance should run on the next city id slot.");

            if (!CityMaintenanceThrottleRules.ShouldRunStaggered(
                    pNow: 90, pLastRun: 103, pInterval: 20, pObjectId: 3))
                throw new Exception("Staggered maintenance should recover if world time moves backward.");
        }

        private static void ExpectRetirementCheapGate()
        {
            if (SoldierRetirementRules.ShouldRunExpensiveRetirementChecks(
                    isSupportedActor: true,
                    isRekt: false,
                    isWarrior: false,
                    alreadyRetired: false,
                    age: 80f,
                    lifespan: 100f,
                    retirementAgeRatio: 0.7f))
                throw new Exception("Non-warriors should skip expensive retirement checks.");

            if (SoldierRetirementRules.ShouldRunExpensiveRetirementChecks(
                    isSupportedActor: true,
                    isRekt: false,
                    isWarrior: true,
                    alreadyRetired: false,
                    age: 40f,
                    lifespan: 100f,
                    retirementAgeRatio: 0.7f))
                throw new Exception("Young warriors should skip expensive retirement checks.");

            if (!SoldierRetirementRules.ShouldRunExpensiveRetirementChecks(
                    isSupportedActor: true,
                    isRekt: false,
                    isWarrior: true,
                    alreadyRetired: false,
                    age: 75f,
                    lifespan: 100f,
                    retirementAgeRatio: 0.7f))
                throw new Exception("Old active warriors should run expensive retirement checks.");

            if (SoldierRetirementRules.ShouldRunExpensiveRetirementChecks(
                    isSupportedActor: true,
                    isRekt: false,
                    isWarrior: true,
                    alreadyRetired: false,
                    age: 75f,
                    lifespan: 0f,
                    retirementAgeRatio: 0.7f))
                throw new Exception("Invalid lifespan should skip expensive retirement checks.");
        }

        private static void ExpectOldHeadRefreshGate()
        {
            if (XiaOldHeadRefreshRules.ShouldRefresh(wasOldHead: false, shouldUseOldHead: false))
                throw new Exception("Young Xia actors should not refresh head graphics just to mark seen state.");

            if (!XiaOldHeadRefreshRules.ShouldRefresh(wasOldHead: false, shouldUseOldHead: true))
                throw new Exception("Xia actors crossing into old-head state should refresh graphics.");

            if (!XiaOldHeadRefreshRules.ShouldRefresh(wasOldHead: true, shouldUseOldHead: false))
                throw new Exception("Xia actors leaving old-head state should refresh graphics.");

            if (XiaOldHeadRefreshRules.ShouldRefresh(wasOldHead: true, shouldUseOldHead: true))
                throw new Exception("Stable old-head Xia actors should skip graphics refresh.");
        }
    }
}
