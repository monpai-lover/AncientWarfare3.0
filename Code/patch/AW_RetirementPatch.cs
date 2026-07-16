using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using HarmonyLib;
using ai.behaviours;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_RetirementPatch
    {
        private const string CITY_ARMY_MAINTENANCE_LAST_CHECK = "aw_city_army_maintenance_last_check";
        private const int CITY_ARMY_MAINTENANCE_INTERVAL = 5;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "updateAge")]
        public static void UpdateAge_Postfix(Actor __instance)
        {
            if (__instance?.data == null) return;
            bool supportedActor = SlaveService.IsSupportedSlaveryActor(__instance);
            bool rekt = __instance.isRekt();
            bool warrior = __instance.isWarrior();
            if (!SoldierRetirementRules.ShouldEnterActorUpdateAgeRetirement(supportedActor, rekt, warrior)) return;

            long benchmark = UpdateAgeBenchmark.Begin();
            try
            {
                SlaveService.RetireIfNeeded(__instance);
            }
            finally
            {
                UpdateAgeBenchmark.End(UpdateAgeBenchmarkRules.ActorRetirementIndex, benchmark);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CityBehCheckArmy), nameof(CityBehCheckArmy.execute))]
        public static void CityBehCheckArmy_Postfix(City pCity)
        {
            if (!ShouldRunAwCityArmyMaintenance(pCity)) return;

            Bench.bench(CityMaintenanceBenchmarkRules.Total, CityMaintenanceBenchmarkRules.Group);
            Bench.bench(CityMaintenanceBenchmarkRules.Retirements, CityMaintenanceBenchmarkRules.Group);
            SlaveService.CheckCityRetirements(pCity);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.Retirements, CityMaintenanceBenchmarkRules.Group);

            Bench.bench(CityMaintenanceBenchmarkRules.StandingArmy, CityMaintenanceBenchmarkRules.Group);
            StandingArmyService.MaintainCity(pCity);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.StandingArmy, CityMaintenanceBenchmarkRules.Group);

            Bench.bench(CityMaintenanceBenchmarkRules.SlaveLabor, CityMaintenanceBenchmarkRules.Group);
            SlaveService.CheckCitySlaveLabor(pCity);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveLabor, CityMaintenanceBenchmarkRules.Group);

            Bench.bench(CityMaintenanceBenchmarkRules.SlaveArmy, CityMaintenanceBenchmarkRules.Group);
            SlaveService.EnsureSlaveArmy(pCity);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveArmy, CityMaintenanceBenchmarkRules.Group);

            if (ShouldRunRoyalGuardMaintenance(pCity))
            {
                Bench.bench(CityMaintenanceBenchmarkRules.RoyalGuard, CityMaintenanceBenchmarkRules.Group);
                RoyalGuardService.EnsureKingdomGuard(pCity?.kingdom);
                Bench.benchEnd(CityMaintenanceBenchmarkRules.RoyalGuard, CityMaintenanceBenchmarkRules.Group);

                // 求嗣:无在世男嗣的国王疯狂生育直到有儿子(与禁卫军同在都城错帧触发)。
                RoyalFertilityService.RefreshHeirUrge(pCity?.kingdom);
            }

            Bench.bench(CityMaintenanceBenchmarkRules.FiefCommand, CityMaintenanceBenchmarkRules.Group);
            FiefMilitaryService.EnsureFiefCommand(pCity);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.FiefCommand, CityMaintenanceBenchmarkRules.Group);

            Bench.bench(CityMaintenanceBenchmarkRules.ArmyCleanup, CityMaintenanceBenchmarkRules.Group);
            if (pCity != null && pCity.hasArmy())
            {
                Bench.bench(CityMaintenanceBenchmarkRules.ArmyCleanupGuardStrip, CityMaintenanceBenchmarkRules.Group);
                RoyalGuardService.StripGuardsFromNormalArmy(pCity.getArmy());
                Bench.benchEnd(CityMaintenanceBenchmarkRules.ArmyCleanupGuardStrip, CityMaintenanceBenchmarkRules.Group);

                Bench.bench(CityMaintenanceBenchmarkRules.ArmyCleanupSlaveCaptain, CityMaintenanceBenchmarkRules.Group);
                SlaveService.EnsureNonSlaveCaptain(pCity.getArmy());
                Bench.benchEnd(CityMaintenanceBenchmarkRules.ArmyCleanupSlaveCaptain, CityMaintenanceBenchmarkRules.Group);

                Bench.bench(CityMaintenanceBenchmarkRules.ArmyCleanupSlaveName, CityMaintenanceBenchmarkRules.Group);
                SlaveService.RenameArmyIfSlaveArmy(pCity.getArmy());
                Bench.benchEnd(CityMaintenanceBenchmarkRules.ArmyCleanupSlaveName, CityMaintenanceBenchmarkRules.Group);

                Bench.bench(CityMaintenanceBenchmarkRules.ArmyCleanupFiefName, CityMaintenanceBenchmarkRules.Group);
                FiefMilitaryService.RefreshArmyName(pCity.getArmy());
                Bench.benchEnd(CityMaintenanceBenchmarkRules.ArmyCleanupFiefName, CityMaintenanceBenchmarkRules.Group);
            }
            Bench.benchEnd(CityMaintenanceBenchmarkRules.ArmyCleanup, CityMaintenanceBenchmarkRules.Group);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.Total, CityMaintenanceBenchmarkRules.Group);
        }

        private static bool ShouldRunAwCityArmyMaintenance(City pCity)
        {
            if (pCity?.data == null) return false;
            Kingdom kingdom = pCity.kingdom;
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) return false;

            int now = (int)LineageService.CurTime();
            pCity.data.get(CITY_ARMY_MAINTENANCE_LAST_CHECK, out int lastRun, -1);
            if (!CityMaintenanceThrottleRules.ShouldRunStaggered(
                    now, lastRun, CITY_ARMY_MAINTENANCE_INTERVAL, pCity.id)) return false;
            pCity.data.set(CITY_ARMY_MAINTENANCE_LAST_CHECK, now);
            return true;
        }

        private static bool ShouldRunRoyalGuardMaintenance(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            return RoyalGuardMaintenanceRules.ShouldCheckFromCity(
                pHasCity: pCity?.data != null,
                pHasKingdom: kingdom?.data != null,
                pHasCapital: kingdom?.capital?.data != null,
                pIsCapital: kingdom?.capital == pCity);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.checkCanMakeWarrior))]
        public static void CheckCanMakeWarrior_Postfix(City __instance, Actor pActor, ref bool __result)
        {
            if (!__result) return;
            if (RoyalGuardService.ShouldBlockNormalArmy(pActor))
                __result = false;
            if (SlaveService.ShouldBlockConscription(__instance, pActor))
                __result = false;
        }
    }
}
