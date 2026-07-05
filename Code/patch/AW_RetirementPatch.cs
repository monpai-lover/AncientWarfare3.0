using AncientWarfare3.core.lineage;
using HarmonyLib;
using ai.behaviours;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_RetirementPatch
    {
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
