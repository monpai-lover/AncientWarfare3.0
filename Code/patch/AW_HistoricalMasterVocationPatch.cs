using AncientWarfare3.content.schools;
using AncientWarfare3.core.schools;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_HistoricalMasterVocationPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(City), nameof(City.checkCanMakeWarrior))]
        private static void CheckCanMakeWarrior_Postfix(Actor pActor, ref bool __result)
        {
            if (__result && !HistoricalMasterVocationService.CanEnter(pActor,
                    HistoricalMasterMilitaryContext.OrdinaryWarrior))
                __result = false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(City), nameof(City.makeWarrior))]
        private static bool MakeWarrior_Prefix(Actor pActor)
        {
            return HistoricalMasterVocationService.CanEnter(pActor,
                HistoricalMasterMilitaryContext.OrdinaryWarrior);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Actor), "setProfession")]
        private static bool SetProfession_Prefix(Actor __instance, UnitProfession pType)
        {
            return pType != UnitProfession.Warrior ||
                   HistoricalMasterVocationService.CanEnter(__instance,
                       HistoricalMasterMilitaryContext.OrdinaryWarrior);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setArmy))]
        private static bool SetArmy_Prefix(Actor __instance, Army pObject)
        {
            return pObject == null || HistoricalMasterVocationService.CanJoinArmy(
                __instance, pObject);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(ArmyManager), nameof(ArmyManager.newArmy))]
        private static bool NewArmy_Prefix(Actor pActor, ref Army __result)
        {
            if (HistoricalMasterVocationService.CanEnter(pActor,
                    HistoricalMasterMilitaryContext.NormalArmy) &&
                HistoricalMasterVocationService.CanEnter(pActor,
                    HistoricalMasterMilitaryContext.ArmyCaptain))
                return true;
            __result = null;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Army), nameof(Army.setCaptain))]
        private static bool SetCaptain_Prefix(Army __instance, Actor pActor)
        {
            return pActor == null ||
                   HistoricalMasterVocationService.CanJoinArmy(pActor, __instance) &&
                   HistoricalMasterVocationService.CanEnter(pActor,
                       HistoricalMasterMilitaryContext.ArmyCaptain);
        }
    }
}
