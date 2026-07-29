using ai.behaviours;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_EmptyCitySurvivalPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityBehBorderShrink), nameof(CityBehBorderShrink.execute))]
        private static bool BorderShrink_Prefix(City pCity,
            ref BehResult __result)
        {
            if (!EmptyCitySurvivalService.
                    ShouldSuppressNaturalBorderShrink(pCity)) return true;
            __result = BehResult.Stop;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityZoneAbandon), nameof(CityZoneAbandon.check))]
        private static bool AbandonedZoneCleanup_Prefix(City pCity)
        {
            return !EmptyCitySurvivalService.
                ShouldSuppressAutomaticAbandonedZoneCleanup(pCity);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityBehCheckDestruction), nameof(CityBehCheckDestruction.execute))]
        private static void CheckDestruction_Prefix(City pCity,
            out bool __state)
        {
            __state = EmptyCitySurvivalService.
                ShouldRecordXenophobicRazeIntent(pCity);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CityBehCheckDestruction), nameof(CityBehCheckDestruction.execute))]
        private static void CheckDestruction_Postfix(City pCity,
            bool __state, bool __runOriginal)
        {
            if (!__runOriginal || !__state) return;
            EmptyCitySurvivalService.RecordXenophobicRazeIntent(pCity);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.eventUnitAdded))]
        private static void EventUnitAdded_Postfix(City __instance,
            Actor pActor, bool __runOriginal)
        {
            if (!__runOriginal) return;
            EmptyCitySurvivalService.ClearRazeIntentForResident(__instance, pActor);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        private static void SetKingdom_Prefix(City __instance,
            out Kingdom __state)
        {
            __state = __instance?.kingdom;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        private static void SetKingdom_Postfix(City __instance,
            bool pFromLoad, Kingdom __state, bool __runOriginal)
        {
            if (!__runOriginal) return;
            EmptyCitySurvivalService.ClearRazeIntentForTakeover(
                __instance, __state, pFromLoad);
        }

    }
}
