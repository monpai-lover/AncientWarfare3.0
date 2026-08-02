using AncientWarfare3.core.policy;
using HarmonyLib;
using System.Collections.Generic;
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
        private static readonly Dictionary<QuantumSpriteAsset, bool>
            SavedMapFlags = new Dictionary<QuantumSpriteAsset, bool>();
        private static bool _mapAssetsSuppressed;

        internal static void ResetSuppression()
        {
            if (_mapAssetsSuppressed) RestoreMapAssets();
            _mapAssetsSuppressed = false;
        }

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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteManager),
            nameof(QuantumSpriteManager.update))]
        private static void SyncMapAssets()
        {
            bool active = HierarchicalVassalMapModeService.IsActive();
            if (_mapAssetsSuppressed == active) return;
            _mapAssetsSuppressed = active;
            if (active)
            {
                SuppressNonEssentialAssets();
                return;
            }
            RestoreMapAssets();
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
            // The transition prefix clears retained groups once.  Returning
            // false here prevents the native draw call from repopulating them
            // on every frame while the mode remains active.
            return false;
        }

        private static void SuppressNonEssentialAssets()
        {
            SavedMapFlags.Clear();
            if (AssetManager.quantum_sprites?.list == null) return;
            foreach (QuantumSpriteAsset asset in
                     AssetManager.quantum_sprites.list)
            {
                if (asset == null || !asset.render_map ||
                    HierarchicalVassalMapModeRules.
                        ShouldKeepMinimapQuantumAsset(asset.id)) continue;
                SavedMapFlags[asset] = asset.render_map;
                asset.render_map = false;
                asset.group_system?.clearFull();
            }
        }

        private static void RestoreMapAssets()
        {
            foreach (KeyValuePair<QuantumSpriteAsset, bool> pair in
                     SavedMapFlags)
            {
                if (pair.Key == null) continue;
                pair.Key.render_map = pair.Value;
            }
            SavedMapFlags.Clear();
        }
    }
}
