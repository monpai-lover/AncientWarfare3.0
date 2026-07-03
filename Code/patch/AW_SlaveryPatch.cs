using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_SlaveryPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.makeWarrior))]
        public static void MakeWarrior_Postfix(City __instance, Actor pActor)
        {
            SlaveService.OnMadeWarrior(__instance, pActor);
            RoyalGuardService.StripActorFromNormalArmy(pActor);
            if (__instance != null && __instance.hasArmy())
                SlaveService.RenameArmyIfSlaveArmy(__instance.getArmy());
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.newKillAction))]
        public static void NewKillAction_Postfix(Actor __instance, Actor pDeadUnit, Kingdom pPrevKingdom, AttackType pAttackType)
        {
            SlaveService.TryPromoteSlaveByMerit(__instance, pDeadUnit);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "getHit")]
        public static bool GetHit_Prefix(Actor __instance, AttackType pAttackType, BaseSimObject pAttacker)
        {
            return !SlaveService.TryCaptureCombatTarget(__instance, pAttacker, pAttackType);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.updateAge))]
        public static void CityUpdateAge_Postfix(City __instance)
        {
            SlaveService.ResetSlaveFoodQuota(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehTryToEatCityFood), nameof(BehTryToEatCityFood.eatFood))]
        public static bool EatCityFood_Prefix(Actor pActor, City pCity)
        {
            return SlaveService.CanConsumeCityFood(pActor, pCity);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.joinAnotherKingdom))]
        public static void JoinAnotherKingdom_Prefix(City __instance, out Kingdom __state)
        {
            __state = __instance?.kingdom;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.joinAnotherKingdom))]
        public static void JoinAnotherKingdom_Postfix(City __instance, Kingdom pNewSetKingdom, bool pCaptured, bool pRebellion, Kingdom __state)
        {
            if (!pCaptured) return;
            SlaveService.HandleCityCaptured(__instance, __state, pNewSetKingdom);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ArmyManager), nameof(ArmyManager.newArmy))]
        public static bool NewArmy_Prefix(ref Actor pActor, City pCity, ref Army __result)
        {
            RoyalGuardService.PrepareArmyCaptain(ref pActor, pCity);
            SlaveService.PrepareArmyCaptain(ref pActor, pCity);
            if (!RoyalGuardService.ShouldBlockNormalArmy(pActor) && !SlaveService.IsSlave(pActor)) return true;

            __result = null;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Army), nameof(Army.setCaptain))]
        public static bool SetCaptain_Prefix(Army __instance, ref Actor pActor)
        {
            if (!RoyalGuardService.TryReplaceGuardCaptain(__instance, ref pActor))
                return false;
            return SlaveService.TryReplaceSlaveCaptain(__instance, ref pActor);
        }
    }
}
