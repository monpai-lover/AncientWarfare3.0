using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Keeps Xia inspect/family-tree heads aligned with map heads.
    /// World map loads head_N by file name, while the UI helper uses array index order.
    /// This patch also makes Xia old heads age-based instead of wise-trait-based.
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
            if (pKing || pWarrior) return;
            if (pRandom) return;
            if (!pAdult && (pContainer == null || !pContainer.render_heads_for_children)) return;

            ActorTextureSubAsset tex = pAsset.texture_asset;
            if (tex == null || !tex.has_advanced_textures) return;

            if (AW_AgePatch.ShouldUseXiaOldHead(pActorId))
            {
                string oldPath = (pSex == ActorSex.Male) ? tex.texture_heads_old_male : tex.texture_heads_old_female;
                if (!string.IsNullOrEmpty(oldPath))
                {
                    Sprite oldHead = ActorAnimationLoader.getHeadSpecial(oldPath);
                    if (oldHead != null) __result = oldHead;
                    return;
                }
            }

            string path = (pSex == ActorSex.Male) ? tex.texture_heads_male : tex.texture_heads_female;
            if (string.IsNullOrEmpty(path)) return;
            if (pHeadId < 0) return;

            Sprite byName = ActorAnimationLoader.getHead(path, pHeadId);
            if (byName != null) __result = byName;
        }
    }
}
