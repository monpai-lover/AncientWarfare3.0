using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     同姓不婚:Postfix Actor.canFallInLoveWith(Actor.cs:3596,public bool)。
    ///     原版判定通过(__result=true)后,若双方都是 Xia 且合流前同姓,则改判 false。
    ///
    ///     用 Postfix 而非 Prefix:原版自己的物种/年龄/亲缘隔离照常先生效,
    ///     我们只在它放行后追加"同姓不婚"这一层,不接管原逻辑(Cultiway 哲学)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_LovePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.canFallInLoveWith))]
        public static void CanFallInLoveWith_Postfix(Actor __instance, Actor pTarget, ref bool __result)
        {
            if (!__result) return; // 原版已禁止,无需再判
            if (__instance == null || pTarget == null) return;

            if (!LineageService.CanFallInLoveByLineage(__instance, pTarget))
                __result = false;
        }
    }
}
