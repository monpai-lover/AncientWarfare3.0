using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     继承人小地图图标:给被标记 IS_HEIR 的存活夏人(未成 king/城主)头顶画 minimap_heir,国家色着色。
    ///     照 AW_FigurePatch.DrawKings_Postfix(QuantumSpriteLibrary.drawKings Postfix)同款写法
    ///     —— 用 group_system.getNext() + qs.set(pos, base_scale) 防图标过大(见 figure 图标修复教训)。
    ///     继承人成为 king/城主后改由原版皇冠/城主图标表示 → 不再画 heir 图标(避免叠图)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_HeirMinimapPatch
    {
        private static readonly HashSet<long> DrawnHeirActorIds = new HashSet<long>();

        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawKings")]
        public static void DrawKings_Heir_Postfix(QuantumSpriteAsset pAsset)
        {
            if (HierarchicalVassalMapModeService.IsActive()) return;
            if (pAsset?.group_system == null) return;
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
            bool markersEnabled = PlayerConfig.optionBoolEnabled("map_kings_leaders");
            if (!markersEnabled) return;

            Sprite defaultIcon = SpriteTextureLoader.getSprite(
                XiaMinimapVisualRules.ResolveHeirIconPath(
                    cultureIntegrated: false));
            Sprite xiaIcon = SpriteTextureLoader.getSprite(
                XiaMinimapVisualRules.ResolveHeirIconPath(
                    cultureIntegrated: true));
            if (defaultIcon == null && xiaIcon == null) return;

            DrawnHeirActorIds.Clear();
            int createdThisFrame = 0;
            IReadOnlyList<long> candidateKingdomIds =
                HeirMinimapMarkerIndex.GetCandidateKingdomIds();
            for (int index = candidateKingdomIds.Count - 1; index >= 0;
                 index--)
            {
                if (createdThisFrame > 2) break;
                Kingdom kingdom = World.world?.kingdoms?.get(
                    candidateKingdomIds[index]);
                if (kingdom == null || !kingdom.isCiv() || kingdom.isRekt() ||
                    !kingdom.hasCities())
                {
                    HeirMinimapMarkerIndex.Remove(candidateKingdomIds[index]);
                    continue;
                }
                Actor unit = HeirService.PeekStoredHeirForMinimap(kingdom);
                if (unit == null)
                {
                    HeirMinimapMarkerIndex.Remove(kingdom.id);
                    continue;
                }
                bool visibleZone = unit.current_zone != null && unit.current_zone.visible;
                if (!HeirMinimapVisualRules.ShouldDrawIcon(
                        markersEnabled,
                        unit.isAlive(),
                        unit.isInMagnet(),
                        unit.current_tile != null,
                        visibleZone,
                        unit.isKing(),
                        unit.isCityLeader()))
                    continue;
                if (!MinimapActorMarkerRules.TryReserve(DrawnHeirActorIds, unit.data.id))
                    continue;

                Kingdom currentKingdom = unit.kingdom;
                long visualKingdomId = HeirMinimapVisualRules.ResolveVisualKingdomId(
                    kingdom.id,
                    currentKingdom?.id ?? -1L);
                Kingdom visualKingdom = currentKingdom?.id == visualKingdomId
                    ? currentKingdom
                    : kingdom.id == visualKingdomId
                        ? kingdom
                        : null;
                if (visualKingdom == null) continue;
                bool integrated = XiaCultureIntegrationService.IsIntegrated(
                    visualKingdom.culture);
                Sprite baseIcon = integrated
                    ? xiaIcon ?? defaultIcon
                    : defaultIcon;
                if (baseIcon == null) continue;

                Vector3 pos = unit.current_position;
                pos.y -= 3f;

                if (!pAsset.group_system.is_within_active_index) createdThisFrame++;
                QuantumSprite qs = pAsset.group_system.getNext();
                if (qs == null) continue;
                City scaleCity = unit.city != null && unit.city.kingdom == visualKingdom
                    ? unit.city
                    : visualKingdom.capital;
                float cameraScale = 1f;
                if (pAsset.add_camera_zoom_multiplier && MoveCamera.instance?.main_camera != null)
                {
                    cameraScale = Mathf.Clamp(
                        MoveCamera.instance.main_camera.orthographicSize / 30f,
                        pAsset.add_camera_zoom_multiplier_min,
                        pAsset.add_camera_zoom_multiplier_max);
                }
                float scale = HeirMinimapScaleRules.Calculate(
                    pAsset.base_scale,
                    cameraScale,
                    pAsset.selected_city_scale,
                    scaleCity != null,
                    scaleCity?.mark_scale_effect ?? 1f);
                qs.set(ref pos, scale);
                Sprite colored = DynamicSprites.getIcon(baseIcon, visualKingdom.getColor());
                qs.setSprite(colored);
            }
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.MinimapMarkersIndex,
                    benchmark);
            }
        }
    }
}
