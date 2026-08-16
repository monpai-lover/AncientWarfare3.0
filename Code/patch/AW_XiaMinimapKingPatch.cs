using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_XiaMinimapKingPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawKings")]
        private static bool DrawKingsPrefix(QuantumSpriteAsset pAsset)
        {
            if (HierarchicalVassalMapModeService.IsActive()) return true;
            if (!PlayerConfig.optionBoolEnabled("map_kings_leaders") ||
                pAsset?.group_system == null) return false;

            int createdThisFrame = 0;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (createdThisFrame > 2) break;
                Actor king = kingdom?.king;
                if (king?.data == null || king.isRekt() ||
                    king.isInMagnet() || king.current_zone?.visible != true)
                    continue;

                bool integrated = XiaCultureIntegrationService.IsIntegrated(
                    kingdom.culture);
                string iconPath = XiaMinimapVisualRules.ResolveKingIconPath(
                    integrated, king.has_attack_target, king.hasPlot(),
                    kingdom.hasEnemies());
                Sprite baseIcon = SpriteTextureLoader.getSprite(iconPath);
                if (baseIcon == null && integrated)
                    baseIcon = SpriteTextureLoader.getSprite(
                        XiaMinimapVisualRules.ResolveKingIconPath(
                            cultureIntegrated: false,
                            king.has_attack_target, king.hasPlot(),
                            kingdom.hasEnemies()));
                if (baseIcon == null) continue;

                Vector3 position = king.current_position;
                position.y -= 3f;
                if (!pAsset.group_system.is_within_active_index)
                    createdThisFrame++;
                QuantumSprite marker = pAsset.group_system.getNext();
                if (marker == null) continue;
                float scale = ResolveScale(pAsset, king.city);
                marker.set(ref position, scale);
                marker.setSprite(DynamicSprites.getIcon(baseIcon,
                    kingdom.getColor()));
            }
            return false;
        }

        private static float ResolveScale(QuantumSpriteAsset pAsset,
            City pCity)
        {
            float scale = pAsset.base_scale;
            if (pAsset.add_camera_zoom_multiplier &&
                MoveCamera.instance?.main_camera != null)
            {
                scale *= Mathf.Clamp(
                    MoveCamera.instance.main_camera.orthographicSize / 30f,
                    pAsset.add_camera_zoom_multiplier_min,
                    pAsset.add_camera_zoom_multiplier_max);
            }
            if (pAsset.selected_city_scale)
                scale = pCity == null
                    ? scale * 0.5f
                    : scale * pCity.mark_scale_effect;
            return scale;
        }
    }
}
