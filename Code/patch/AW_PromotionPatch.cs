using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     贵族晋升:Postfix City.setLeader / Kingdom.setKing。
    ///     - 城主(setLeader)走分流 OnCityLeaderAppointed:无谱系→初次贵族建姓族+氏支;
    ///       已有谱系(父系继承)→多余 male 子嗣分封新氏支(长子/继承人留原氏)。
    ///     - 国王(setKing)是大宗,不分封,直接 OnActorPromoted 赋/刷新贵族身份。
    ///
    ///     读档/重复设置安全:已有谱系幂等(EnsureLineageForNoble 直接 return,只刷新身份)。
    ///     Postfix 注入,不接管原方法。
    /// </summary>
    [HarmonyPatch]
    public static class AW_PromotionPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.setLeader))]
        public static bool SetLeader_HeirGuard_Prefix(City __instance, Actor pActor, bool pNew, out bool __state)
        {
            __state = false;
            if (!pNew) return true;
            if (pActor == null || !LineageService.IsXia(pActor)) return true;

            Kingdom kingdom = __instance?.kingdom ?? pActor.kingdom;
            if (!HeirService.IsCurrentHeir(kingdom, pActor)) return true;

            __state = true;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.setLeader))]
        public static void SetLeader_Postfix(Actor pActor, bool pNew, bool __state)
        {
            if (__state) return;
            if (pActor == null || !LineageService.IsXia(pActor)) return;
            LineageService.OnCityLeaderAppointed(pActor);
            if (pNew) ChronicleEvents.OnBecomeLeader(pActor); // 编年史:仅新任命记(pNew=false 是读档/复位,不重复记)
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.High)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad)
        {
            if (pFromLoad) return;
            if (pActor == null || !LineageService.IsXia(pActor)) return;
            LineageService.OnActorPromoted(pActor, NobleTrigger.King);
        }
    }
}
