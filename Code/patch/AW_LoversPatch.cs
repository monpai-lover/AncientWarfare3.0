using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     恋爱编年史:Postfix Actor.becomeLoversWith(Actor pTarget)(在 Actor 自身声明,typeof 正确)。
    ///     __instance 与 pTarget 结为伴侣 → 双方各记一条(贵族门槛),同一对去重(ChronicleEvents 内部)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_LoversPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.setLover))]
        public static bool SetLover_Prefix(Actor __instance, Actor pActor)
        {
            if (SyntheticLevyService.IsSynthetic(__instance) ||
                SyntheticLevyService.IsSynthetic(pActor))
                return pActor == null;
            NobleHeirPregnancyService.OnLoverChanging(__instance, pActor);
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.becomeLoversWith))]
        public static bool BecomeLoversWith_Prefix(Actor __instance,
            Actor pTarget)
        {
            return !SyntheticLevyService.IsSynthetic(__instance) &&
                   !SyntheticLevyService.IsSynthetic(pTarget);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.becomeLoversWith))]
        public static void BecomeLoversWith_Postfix(Actor __instance, Actor pTarget)
        {
            if (SyntheticLevyService.IsSynthetic(__instance) ||
                SyntheticLevyService.IsSynthetic(pTarget)) return;
            ChronicleEvents.OnBecameLovers(__instance, pTarget);
            NobleHeirPregnancyService.OnBecameLovers(__instance, pTarget);
        }
    }
}
