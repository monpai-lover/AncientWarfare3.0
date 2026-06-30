using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     修复夏人「地图头 ≠ inspect 头像头」(2026-06-30,两边都是普通头却不一样)。
    ///
    ///     根因(亲验 AssetRipper):heads_male/female 文件是 head_0..head_10(11 张)。
    ///     - 地图:Actor.checkSpriteHead → ActorAnimationLoader.getHead(path, data.head)
    ///       (ActorAnimationLoader.cs:34)按**文件名** head_{idx} 取头(key = path_head_idx)。
    ///     - inspect:DynamicActorSpriteCreatorUI.getSpriteHeadForUI(:142)对普通头用
    ///       getSprite(idx, container.heads_male) 按**数组下标**取头。
    ///     - getSpriteList 字符串排序 → 数组序为 head_0, head_1, head_10, head_2, head_3...
    ///       即下标 2 = head_10.png ≠ 文件名 head_2.png。索引≥2 时两边错位 → 同 data.head
    ///       地图显 head_2、inspect 显 head_10。
    ///
    ///     修复:Postfix getSpriteHeadForUI,仅对 Xia 的**普通头**分支(非 king/warrior/wise/egg/
    ///     boat、成年),改用与地图同款的 ActorAnimationLoader.getHead(按文件名)覆盖 __result,
    ///     使 inspect 与地图一致。其它种族/特殊头不动。
    /// </summary>
    [HarmonyPatch]
    public static class AW_AvatarHeadPatch
    {
        private const string XIA = "Xia";

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DynamicActorSpriteCreatorUI), nameof(DynamicActorSpriteCreatorUI.getSpriteHeadForUI))]
        public static void GetSpriteHeadForUI_Postfix(ref Sprite __result, ActorAsset pAsset, ActorSex pSex,
            AnimationContainerUnit pContainer, long pActorId, int pHeadId, bool pAdult, bool pEgg,
            bool pKing, bool pWarrior, bool pWise, bool pRandom)
        {
            if (pAsset == null || pAsset.id != XIA) return;
            if (pEgg || pAsset.is_boat) return;
            if (pKing || pWarrior || pWise) return;           // 特殊头(走 head_king/warrior/old),不动
            if (pRandom) return;                              // 随机头不参与按文件名对齐
            if (!pAdult && (pContainer == null || !pContainer.render_heads_for_children)) return;

            ActorTextureSubAsset tex = pAsset.texture_asset;
            if (tex == null || !tex.has_advanced_textures) return; // 简单头(用 container.heads)地图也按下标,无错位

            string path = (pSex == ActorSex.Male) ? tex.texture_heads_male : tex.texture_heads_female;
            if (string.IsNullOrEmpty(path)) return;

            // 与地图同款:按文件名 head_{id} 取头。pHeadId<0(未定)时回退原结果。
            if (pHeadId < 0) return;
            Sprite byName = ActorAnimationLoader.getHead(path, pHeadId);
            if (byName != null) __result = byName;
        }
    }
}
