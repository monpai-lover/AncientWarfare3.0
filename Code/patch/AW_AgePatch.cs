using AncientWarfare3.core.lineage;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_AgePatch
    {
        private const float XIA_OLD_HEAD_AGE = 60f;
        private const float XIA_AGE_PRESSURE_START_RATIO = 1.25f;
        private const float XIA_AGE_HIGH_PRESSURE_RATIO = 1.65f;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "updateAge")]
        public static void UpdateAge_Postfix(Actor __instance)
        {
            if (__instance?.data == null) return;
            if (!LineageService.IsXia(__instance)) return;
            if (__instance.isRekt() || !__instance.isAlive()) return;
            if (__instance.getAge() < XIA_OLD_HEAD_AGE) return;
            if (__instance.hasTrait("wise")) return;

            if (__instance.addTrait("wise"))
                __instance.clearGraphicsFully();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.checkNaturalDeath))]
        public static void CheckNaturalDeath_Postfix(Actor __instance, ref bool __result)
        {
            if (__result) return;
            if (__instance?.data == null) return;
            if (!LineageService.IsXia(__instance)) return;
            if (!WorldLawLibrary.world_law_old_age.isEnabled()) return;
            if (__instance.hasTrait("immortal")) return;

            float lifespan = __instance.stats["lifespan"];
            if (lifespan <= 0f) return;

            float ratio = __instance.getAge() / lifespan;
            if (ratio <= XIA_AGE_PRESSURE_START_RATIO) return;

            float chance = ratio >= XIA_AGE_HIGH_PRESSURE_RATIO
                ? 0.85f
                : Mathf.Clamp((ratio - XIA_AGE_PRESSURE_START_RATIO) * 0.5f, 0.02f, 0.35f);
            if (!Randy.randomChance(chance)) return;

            __instance.data.set(LineageKeys.DEATH_CAUSE, "\u81EA\u7136\u8001\u6B7B");
            __instance.getHitFullHealth(AttackType.Age);
            __result = true;
        }
    }
}
