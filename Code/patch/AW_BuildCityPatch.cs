using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     积极建城标记:Postfix Actor.canBuildNewCity(Actor.cs:2609)。
    ///
    ///     **不改返回值**(不阻原版任何人建城)。仅当 actor 是 king 家族里有资格分封的
    ///     成年 male 多余子嗣时,给它打 aw_eager_builder flag,标记"应更积极去建新 city 当 leader"。
    ///
    ///     当前为最小标记版(原版建城 AI 无显式概率 gate 可调)。flag 的实际倾向强度
    ///     待 UI 能观察后再接入(转成 AI job 加权 / 主动派发建城)。
    ///
    ///     收敛范围(用户定):只 king 家族(royal_clan 成员,或父亲是当前 king)的成年 male 子嗣。
    /// </summary>
    [HarmonyPatch]
    public static class AW_BuildCityPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.canBuildNewCity))]
        public static void CanBuildNewCity_Postfix(Actor __instance)
        {
            if (__instance?.data == null) return;
            if (!LineageService.IsXia(__instance)) return;

            bool eager = LineageService.IsEnfeoffmentCandidate(__instance) && IsInKingFamily(__instance);
            __instance.data.set(LineageKeys.EAGER_BUILDER, eager);
        }

        /// <summary>是否属于 king 家族:在国家 royal_clan 内,或父亲是当前国王。</summary>
        private static bool IsInKingFamily(Actor pActor)
        {
            var kingdom = pActor.kingdom;
            if (kingdom?.data == null) return false;

            // ① 在 royal_clan 内
            long royalClanId = kingdom.data.royal_clan_id;
            if (royalClanId >= 0 && pActor.clan != null && pActor.clan.data != null &&
                pActor.clan.data.id == royalClanId)
                return true;

            // ② 父亲是当前国王
            var king = kingdom.king;
            if (king != null && king.data != null)
            {
                if (pActor.data.parent_id_1 == king.data.id || pActor.data.parent_id_2 == king.data.id)
                    return true;
            }

            return false;
        }
    }
}
