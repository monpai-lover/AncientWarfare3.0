using AncientWarfare3.content.figures;
using AncientWarfare3.core.lineage;
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

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad)
        {
            if (!SetKingPostfixRules.ShouldRun(pFromLoad, pActor != null && __instance?.king == pActor)) return;
            if (__instance == null || pActor == null) return;
            HistoricalFigureService.OnFigureKingBecame(__instance, pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawFavoritesMap")]
        public static void DrawFavoritesMap_Figure_Postfix(QuantumSpriteAsset pAsset)
        {
            if (pAsset?.group_system == null) return;

            Sprite baseIcon = SpriteTextureLoader.getSprite("civ/icons/minimap_figure");
            if (baseIcon == null) return;

            if (World.world?.units?.visible_units_with_favorite == null) return;
            Actor[] visibleFavorites = World.world.units.visible_units_with_favorite.array;
            int count = World.world.units.visible_units_with_favorite.count;
            for (int i = 0; i < count; i++)
            {
                Actor unit = visibleFavorites[i];
                if (unit == null || unit.kingdom == null) continue;

                bool visibleZone = unit.current_zone != null && unit.current_zone.visible;
                if (!HistoricalFigureMinimapRules.ShouldDrawIcon(
                        unit.isAlive(),
                        unit.isInMagnet(),
                        unit.current_tile != null,
                        visibleZone,
                        unit.isKing(),
                        unit.isCityLeader(),
                        unit.hasTrait(HistoricalFigureService.TRAIT_FIGURE),
                        unit.hasTrait(HistoricalFigureService.TRAIT_FIRST)))
                    continue;

                Vector3 pos = unit.current_position;
                pos.y -= 3f;

                QuantumSprite qs = pAsset.group_system.getNext();
                if (qs == null) continue;
                qs.set(ref pos, GetMapIconScale(pAsset, unit.city));
                qs.setSprite(DynamicSprites.getIcon(baseIcon, unit.kingdom.getColor()));
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
