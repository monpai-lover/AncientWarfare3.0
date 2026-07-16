using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_StandingArmyPatch
    {
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
