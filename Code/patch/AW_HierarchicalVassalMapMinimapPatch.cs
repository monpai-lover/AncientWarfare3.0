using AncientWarfare3.core.policy;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_HierarchicalVassalMapMinimapPatch
    {
        private const int MinimapArmyFlagSortingOrder = -2;
        private static bool _nativeArmyFlagSortingCaptured;
        private static int _nativeArmyFlagLayerId;
        private static int _nativeArmyFlagSortingOrder;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.redrawMiniMap))]
        private static void HideCityBoundariesForMinimap()
        {
            // Hide only boundary roots; pooled political fill remains in the
            // minimap capture so hierarchy regions retain their colors.
            HierarchicalVassalMapModeBoundaryLayer.SetMinimapHidden(true);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.redrawMiniMap))]
        private static void RestoreCityBoundariesAfterMinimap()
        {
            HierarchicalVassalMapModeBoundaryLayer.SetMinimapHidden(false);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuantumSpriteManager),
            nameof(QuantumSpriteManager.update))]
        private static void HideNonEssentialMinimapAssets()
        {
            if (!HierarchicalVassalMapModeService.IsActive() ||
                AssetManager.quantum_sprites?.list == null) return;
            foreach (QuantumSpriteAsset asset in
                     AssetManager.quantum_sprites.list)
            {
                if (asset == null || !asset.render_map ||
                    HierarchicalVassalMapModeRules.
                        ShouldKeepMinimapQuantumAsset(asset.id)) continue;
                if (asset.group_system == null ||
                    asset.group_system.countActive() <= 0) continue;
                asset.group_system.clearFull();
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawUnitsAvatars")]
        private static bool SkipUnitAvatars(QuantumSpriteAsset pAsset)
        {
            return KeepOrClearMapIcons(pAsset);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawKings")]
        private static bool SkipKingIcons(QuantumSpriteAsset pAsset)
        {
            return KeepOrClearMapIcons(pAsset);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawLeaders")]
        private static bool SkipLeaderIcons(QuantumSpriteAsset pAsset)
        {
            return KeepOrClearMapIcons(pAsset);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawArmies")]
        private static void KeepArmyFlagsBelowCountryLabels(
            QuantumSpriteAsset pAsset)
        {
            if (pAsset?.group_system == null) return;
            QuantumSprite[] flags = pAsset.group_system.getAll();
            int activeCount = pAsset.group_system.countActive();
            if (flags == null || activeCount <= 0) return;

            bool hierarchical = HierarchicalVassalMapModeService.IsActive();
            int count = Mathf.Min(activeCount, flags.Length);
            for (int index = 0; index < count; index++)
            {
                QuantumSprite flag = flags[index];
                SpriteRenderer spriteRenderer = flag?.sprite_renderer;
                if (spriteRenderer == null) continue;
                if (!_nativeArmyFlagSortingCaptured)
                {
                    _nativeArmyFlagSortingCaptured = true;
                    _nativeArmyFlagLayerId = spriteRenderer.sortingLayerID;
                    _nativeArmyFlagSortingOrder = spriteRenderer.sortingOrder;
                }

                int layerId = hierarchical
                    ? SortingLayer.NameToID("EffectsBack")
                    : _nativeArmyFlagLayerId;
                int sortingOrder = hierarchical
                    ? MinimapArmyFlagSortingOrder
                    : _nativeArmyFlagSortingOrder;
                spriteRenderer.sortingLayerID = layerId;
                spriteRenderer.sortingOrder = sortingOrder;

                QuantumSpriteWithText flagWithText =
                    flag as QuantumSpriteWithText;
                Renderer textRenderer =
                    flagWithText?.text?.GetComponent<Renderer>();
                if (textRenderer == null) continue;
                textRenderer.sortingLayerID = layerId;
                textRenderer.sortingOrder = sortingOrder;
            }
        }

        private static bool KeepOrClearMapIcons(QuantumSpriteAsset pAsset)
        {
            if (!HierarchicalVassalMapModeService.IsActive()) return true;
            // Returning false stops new markers, while clearFull removes the
            // king/leader sprites retained from the frame before mode entry.
            pAsset?.group_system?.clearFull();
            return false;
        }
    }
}
