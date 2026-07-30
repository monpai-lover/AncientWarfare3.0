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
        public static void SetLover_Prefix(Actor __instance, Actor pActor)
        {
            NobleHeirPregnancyService.OnLoverChanging(__instance, pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.becomeLoversWith))]
        public static void BecomeLoversWith_Postfix(Actor __instance, Actor pTarget)
        {
            ChronicleEvents.OnBecameLovers(__instance, pTarget);
            NobleHeirPregnancyService.OnBecameLovers(__instance, pTarget);
        }
    }
}
