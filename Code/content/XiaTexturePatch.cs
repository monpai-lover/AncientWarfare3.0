using System;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.content
{
    /// <summary>
    ///     单位取贴图路径的 Prefix:
    ///     1. **继承人专属皮肤**:被标记 IS_HEIR 且未成 king/leader/warrior 的夏人 → 用 unit_heir 贴图。
    ///        (原版 getUnitTexturePath 只按 King/Leader/Warrior/普通 分,无"继承人"职业 → 须在此截获。)
    ///     2. 动态贴图委托表 <see cref="XiaTextures.AnimationTextures"/>(参考 PVZ getUnitTexturePath Prefix)。
    /// </summary>
    [HarmonyPatch]
    public static class XiaTexturePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "getUnitTexturePath")]
        public static bool GetUnitTexturePath_Prefix(Actor __instance, ref string __result)
        {
            // ① 继承人皮肤:轻量判定(IS_HEIR 标记 + 非特殊职业 + 非婴儿),命中即用 unit_heir。
            if (__instance.asset != null && __instance.asset.id == LineageService.XIA_ASSET_ID && IsHeirSkinActor(__instance))
            {
                __result = XiaRace.TEXTURE_PATH + "unit_heir";
                return false;
            }

            // ② 动态贴图委托表(当前为空,保留扩展)。
            if (XiaTextures.AnimationTextures.TryGetValue(__instance.asset.id, out Func<Actor, string> action))
            {
                string texture = action(__instance);
                if (texture != null)
                {
                    __result = texture;
                    return false; // 跳过原方法,使用自定义路径
                }
            }

            return true; // 走原方法
        }

        /// <summary>是否该用 unit_heir 皮肤:被标记继承人 + 非婴/蛋 + 非 king/leader/warrior(那些有自己的职业皮肤)。</summary>
        private static bool IsHeirSkinActor(Actor pActor)
        {
            if (pActor.data == null) return false;
            pActor.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
            if (!isHeir) return false;
            if (pActor.isBaby() || pActor.isEgg()) return false;
            if (pActor.isKing() || pActor.isCityLeader() || pActor.isWarrior()) return false;
            return true;
        }
    }
}
