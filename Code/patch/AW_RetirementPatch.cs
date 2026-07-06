using AncientWarfare3.core.lineage;
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
            SlaveService.RetireIfNeeded(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CityBehCheckArmy), nameof(CityBehCheckArmy.execute))]
        public static void CityBehCheckArmy_Postfix(City pCity)
        {
            if (!ShouldRunAwCityArmyMaintenance(pCity)) return;

            SlaveService.CheckCityRetirements(pCity);
            SlaveService.CheckCitySlaveLabor(pCity);
            SlaveService.AssignSlaveCatchers(pCity);
            RoyalGuardService.EnsureKingdomGuard(pCity?.kingdom);
            FiefMilitaryService.EnsureFiefCommand(pCity);
            if (pCity != null && pCity.hasArmy())
            {
                RoyalGuardService.StripGuardsFromNormalArmy(pCity.getArmy());
                SlaveService.EnsureNonSlaveCaptain(pCity.getArmy());
                SlaveService.RenameArmyIfSlaveArmy(pCity.getArmy());
                FiefMilitaryService.RefreshArmyName(pCity.getArmy());
            }
        }

        private static bool ShouldRunAwCityArmyMaintenance(City pCity)
        {
            if (pCity?.data == null) return false;
            Kingdom kingdom = pCity.kingdom;
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) return false;

            int now = (int)LineageService.CurTime();
            pCity.data.get(CITY_ARMY_MAINTENANCE_LAST_CHECK, out int lastRun, -1);
            if (!CityMaintenanceThrottleRules.ShouldRun(now, lastRun, CITY_ARMY_MAINTENANCE_INTERVAL)) return false;
            pCity.data.set(CITY_ARMY_MAINTENANCE_LAST_CHECK, now);
            return true;
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
