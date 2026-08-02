using AncientWarfare3.content.figures;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_FigurePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "newCreature")]
        public static void NewCreature_Postfix(Actor __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (HistoricalSchoolActorSpawnCapture.IsTargetActor(__instance)) return;
            HistoricalFigureService.TrySpawnOn(__instance, "newCreature");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BabyMaker), nameof(BabyMaker.makeBaby))]
        public static void MakeBaby_Figure_Postfix(Actor __result)
        {
            if (__result == null || __result.isBaby() || __result.isEgg()) return;
            HistoricalFigureService.TrySpawnOn(__result, "baby_final");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawFavoritesMap")]
        public static bool DrawFavoritesMap_Figure_Prefix(QuantumSpriteAsset pAsset)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                if (HierarchicalVassalMapModeService.IsActive()) return false;
                if (pAsset?.group_system == null) return false;
                bool markersEnabled =
                    PlayerConfig.optionBoolEnabled("marks_favorites");
                if (!markersEnabled) return false;

                Sprite favoriteIcon = SpriteTextureLoader.getSprite("ui/Icons/iconFavoriteStar_Map");
                Sprite baseIcon = SpriteTextureLoader.getSprite(
                    "civ/icons/minimap_figure");
                if (favoriteIcon == null) return false;

                if (World.world?.units?.visible_units_with_favorite == null)
                    return false;
                Actor[] visibleFavorites =
                    World.world.units.visible_units_with_favorite.array;
                int count =
                    World.world.units.visible_units_with_favorite.count;
                for (int i = 0; i < count; i++)
                {
                    Actor unit = visibleFavorites[i];
                    if (unit == null) continue;

                    bool visibleZone = unit.current_zone != null &&
                                       unit.current_zone.visible;
                    bool drawFigure = baseIcon != null && HistoricalFigureMinimapRules.ShouldDrawIcon(
                        markersEnabled,
                        unit.isAlive(),
                        unit.isInMagnet(),
                        unit.current_tile != null,
                        visibleZone,
                        unit.isKing(),
                        unit.isCityLeader(),
                        unit.data != null &&
                        FigureStateStore.IndexOfActor(unit.data.id) >= 0);

                    Sprite icon = favoriteIcon;
                    City scaleCity = unit.city;
                    if (drawFigure)
                    {
                        Kingdom currentKingdom = unit.kingdom;
                        Kingdom cityKingdom = unit.city?.kingdom;
                        long visualKingdomId = HeirMinimapVisualRules.ResolveVisualKingdomId(
                        cityKingdom?.id ?? -1L,
                        currentKingdom?.id ?? -1L);
                        Kingdom visualKingdom = currentKingdom?.id == visualKingdomId
                            ? currentKingdom
                            : cityKingdom?.id == visualKingdomId
                                ? cityKingdom
                                : null;
                        scaleCity = unit.city != null && unit.city.kingdom == visualKingdom
                            ? unit.city
                            : visualKingdom?.capital;
                        icon = visualKingdom == null
                            ? baseIcon
                            : DynamicSprites.getIcon(baseIcon, visualKingdom.getColor());
                    }

                    Vector3 pos = unit.current_position;
                    pos.y -= 3f;

                    QuantumSprite qs = pAsset.group_system.getNext();
                    if (qs == null) continue;
                    qs.set(ref pos, GetMapIconScale(pAsset, scaleCity));
                    qs.setSprite(icon);
                }
                return false;
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.MinimapMarkersIndex,
                    benchmark);
            }
        }

        private static float GetMapIconScale(QuantumSpriteAsset pAsset, City city)
        {
            float scale = pAsset.base_scale;
            if (pAsset.add_camera_zoom_multiplier && MoveCamera.instance?.main_camera != null)
            {
                scale *= Mathf.Clamp(MoveCamera.instance.main_camera.orthographicSize / 30f,
                    pAsset.add_camera_zoom_multiplier_min, pAsset.add_camera_zoom_multiplier_max);
            }
            if (pAsset.selected_city_scale)
            {
                scale *= city == null ? 0.5f : city.mark_scale_effect;
            }
            return scale;
        }
    }
}
