using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     贵族晋升:Postfix City.setLeader / Kingdom.setKing。
    ///     成为城主/国王时,若是 Xia 则赋予或刷新贵族身份(建姓族/氏支、加 guizu、距离归零)。
    ///
    ///     pNew/pFromLoad 为 true(读档恢复)时不应重复晋升,但 LineageService.EnsureLineageForNoble
    ///     已对"已有谱系"幂等(直接沿用),故读档安全。
    ///
    ///     Postfix 注入,不接管原方法。
    /// </summary>
    [HarmonyPatch]
    public static class AW_PromotionPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.setLeader))]
        public static void SetLeader_Postfix(Actor pActor)
        {
            if (pActor == null || !LineageService.IsXia(pActor)) return;
            LineageService.OnActorPromoted(pActor, NobleTrigger.CityLeader);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Actor pActor)
        {
            if (pActor == null || !LineageService.IsXia(pActor)) return;
            LineageService.OnActorPromoted(pActor, NobleTrigger.King);
        }
    }
}
