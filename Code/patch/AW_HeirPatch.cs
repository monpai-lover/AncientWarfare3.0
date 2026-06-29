using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     继承人接管 + 新王换年号。
    ///
    ///     1. Prefix SuccessionTool.getKingFromRoyalClan(Kingdom, Actor) —— 原版从王族选王前,
    ///        若我们已指定继承人(aw_heir_id 有效),直接返回继承人接管继位(return false 完全接管);
    ///        否则放行原版(return true),由原版/getKingFromLeaders 兜底。
    ///        这是少数"Prefix return false 完全接管"场景(Cultiway 哲学允许)。
    ///
    ///     2. Postfix Kingdom.setKing —— 新王上任后:清旧继承人、重选继承人、换年号。
    ///
    ///     不依赖 AW_Kingdom 子类(新版不可行),全用 kingdom.data + HeirService/YearNameService。
    /// </summary>
    [HarmonyPatch]
    public static class AW_HeirPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuccessionTool), nameof(SuccessionTool.getKingFromRoyalClan))]
        public static bool GetKingFromRoyalClan_Prefix(Kingdom pKingdom, ref Actor __result)
        {
            var heir = HeirService.GetHeir(pKingdom);
            if (heir == null) return true; // 无继承人,放行原版选王

            __result = heir;
            return false; // 有继承人,接管:直接让继承人继位
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance)
        {
            if (__instance?.data == null) return;

            // 新王即位:清旧继承人,重选,换年号。
            HeirService.ClearHeir(__instance);
            HeirService.RefreshHeir(__instance);
            YearNameService.OnNewKing(__instance);
        }
    }
}
