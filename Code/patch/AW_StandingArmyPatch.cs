using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_StandingArmyPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setArmy))]
        private static void SetArmy_Prefix(Actor __instance, out Army __state)
        {
            __state = __instance?.army;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setArmy))]
        private static void SetArmy_Postfix(Actor __instance, Army pObject, Army __state)
        {
            if (__state == pObject) return;
            KingdomMilitaryReadinessService.MarkArmyCitiesDirty(
                __instance, __state, pObject);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.destroyCity))]
        private static void DestroyCity_Prefix(City __instance)
        {
            KingdomMilitaryReadinessService.OnCityDestroyed(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "tryToMakeWarrior")]
        private static bool TryToMakeWarrior_Prefix()
        {
            return MilitaryRecruitmentScope.AllowsVanillaTryToMakeWarrior;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.checkCanMakeWarrior))]
        private static bool CheckCanMakeWarrior_Prefix(City __instance, Actor pActor, ref bool __result)
        {
            if (!MilitaryRecruitmentScope.BypassesWarriorCapacity) return true;
            __result = PassesOriginalEligibilityWithoutCapacity(__instance, pActor);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.checkIfWarriorStillOk))]
        private static bool CheckIfWarriorStillOk_Prefix(City __instance, Actor pActor, ref bool __result)
        {
            if (TemporaryLevyService.IsTemporaryLevy(pActor) ||
                TemporarySlaveVanguardService.IsMember(pActor) ||
                StandingArmyService.ShouldKeepWithinOriginalArmyLimit(__instance, pActor))
            {
                __result = true;
                return false;
            }
            return true;
        }

        private static bool PassesOriginalEligibilityWithoutCapacity(City pCity, Actor pActor)
        {
            if (pCity?.data == null || pActor?.data == null || pActor.isBaby()) return false;
            if (!pCity.hasCulture()) return true;
            if (pActor.isSexFemale() && pCity.culture.hasTrait("conscription_male_only")) return false;
            if (pActor.isSexMale() && pCity.culture.hasTrait("conscription_female_only")) return false;
            return true;
        }
    }
}
