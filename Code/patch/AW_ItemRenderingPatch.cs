using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Guard hand-item rendering against modded equipment registered after vanilla item sprite loading.
    /// </summary>
    [HarmonyPatch(typeof(ItemRendering), nameof(ItemRendering.getItemMainSpriteFrame))]
    public static class AW_ItemRenderingPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(IHandRenderer pHandRendererAsset, ref Sprite __result)
        {
            if (pHandRendererAsset == null)
            {
                __result = null;
                return false;
            }

            Sprite[] sprites = pHandRendererAsset.getSprites();
            if (sprites == null || sprites.Length == 0)
            {
                sprites = GetFallbackSprites();
            }

            if (sprites == null || sprites.Length == 0)
            {
                __result = null;
                return false;
            }

            __result = sprites.Length > 1
                ? AnimationHelper.getSpriteFromList(0, sprites, 5f)
                : sprites[0];

            if (__result == null)
            {
                Sprite[] fallback = GetFallbackSprites();
                __result = fallback != null && fallback.Length > 0 ? fallback[0] : null;
            }

            return false;
        }

        private static Sprite[] GetFallbackSprites()
        {
            EquipmentAsset fallback = AssetManager.items.get("sword_bronze");
            if (fallback?.gameplay_sprites != null && fallback.gameplay_sprites.Length > 0)
            {
                return fallback.gameplay_sprites;
            }

            return null;
        }
    }
}
