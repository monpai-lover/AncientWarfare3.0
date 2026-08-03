using AncientWarfare3.core.policy;
using HarmonyLib;
using System.Collections.Generic;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_HierarchicalVassalMapMinimapPatch
    {
        private static bool _nonEssentialAssetsCleared;
        private static bool _unknownIconAssetCleared;
        private static bool _iconFilteringActive;
        private static readonly HashSet<string> ClearedIconAssetIds =
            new HashSet<string>();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.redrawMiniMap))]
        private static void PrepareHierarchicalLabelsForMinimap()
        {
            if (HierarchicalVassalMapModeService.IsActive())
                HierarchicalVassalMapModeLabelLayer.
                    ObserveResolutionMode(true);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.redrawMiniMap))]
        private static void RestoreHierarchicalLabelsAfterMinimap()
        {
            if (HierarchicalVassalMapModeService.IsActive())
                HierarchicalVassalMapModeLabelLayer.
                    ObserveResolutionMode(false);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuantumSpriteManager),
            nameof(QuantumSpriteManager.update))]
        private static void HideNonEssentialMinimapAssets()
        {
            if (!HierarchicalVassalMapModeService.IsActive() ||
                AssetManager.quantum_sprites?.list == null)
            {
                ResetIconFilteringState();
                return;
            }
            _iconFilteringActive = true;
            if (AssetManager.quantum_sprites.list.Count == 0) return;
            // QuantumSpriteManager.update runs every render frame. The
            // minimap whitelist is static while this mode is active, so a
            // full asset-list sweep is only needed on mode entry.
            if (_nonEssentialAssetsCleared) return;
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
            _nonEssentialAssetsCleared = true;
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

        private static bool KeepOrClearMapIcons(QuantumSpriteAsset pAsset)
        {
            if (!HierarchicalVassalMapModeService.IsActive())
            {
                ResetIconFilteringState();
                return true;
            }
            _iconFilteringActive = true;

            // Returning false stops new markers. Clear the retained native
            // group only once per asset after map-mode entry; calling
            // clearFull from every draw pass needlessly walks the full sprite
            // pool while time is flowing.
            string assetId = pAsset?.id;
            bool firstAssetPass = string.IsNullOrEmpty(assetId)
                ? !_unknownIconAssetCleared
                : ClearedIconAssetIds.Add(assetId);
            if (pAsset?.group_system != null && firstAssetPass)
            {
                if (string.IsNullOrEmpty(assetId))
                    _unknownIconAssetCleared = true;
                pAsset?.group_system?.clearFull();
            }
            return false;
        }

        private static void ResetIconFilteringState()
        {
            if (!_iconFilteringActive) return;
            _iconFilteringActive = false;
            _nonEssentialAssetsCleared = false;
            _unknownIconAssetCleared = false;
            ClearedIconAssetIds.Clear();
        }
    }
}
