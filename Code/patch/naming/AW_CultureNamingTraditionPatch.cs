using System;
using AncientWarfare3.core.naming;
using HarmonyLib;

namespace AncientWarfare3.patch.naming
{
    [HarmonyPatch(typeof(Culture), nameof(Culture.createCulture))]
    internal static class AW_CultureNamingTraditionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void CreateCulture_Prefix(Actor pActor,
            ref Culture __state)
        {
            __state = pActor?.culture;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void CreateCulture_Postfix(Culture __instance,
            Culture __state)
        {
            if (__state?.data != null && !ReferenceEquals(__state, __instance))
                AWCultureNamingTraditionService.Inherit(__instance, __state);
            else
                AWCultureNamingTraditionService.Ensure(__instance);
        }
    }
}
