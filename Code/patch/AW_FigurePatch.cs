using AncientWarfare3.content.figures;
using AncientWarfare3.core.db;
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
            if (pFromLoad) return;
            if (__instance == null || pActor == null) return;
            HistoricalFigureService.OnFigureKingBecame(__instance, pActor);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawFavoritesMap")]
        public static void DrawFavoritesMap_Postfix(QuantumSpriteAsset pAsset)
        {
            if (!FigureStateStore.AnyAliveFigure()) return;
            if (pAsset?.group_system == null) return;

            Sprite baseIcon = SpriteTextureLoader.getSprite("civ/icons/minimap_figure");
            if (baseIcon == null) return;

            var units = World.world?.units;
            if (units == null) return;

            Actor[] visible = units.visible_units_alive.array;
            int count = units.visible_units_alive.count;
            for (int i = 0; i < count; i++)
            {
                Actor unit = visible[i];
                if (unit == null || !unit.isAlive()) continue;
                if (unit.isInMagnet()) continue;
                if (unit.current_tile == null || unit.current_zone == null || !unit.current_zone.visible) continue;
                if (!unit.hasTrait(HistoricalFigureService.TRAIT_FIRST) &&
                    !unit.hasTrait(HistoricalFigureService.TRAIT_FIGURE)) continue;

                Vector3 pos = unit.current_position;
                pos.y -= 3f;

                QuantumSprite qs = pAsset.group_system.getNext();
                if (qs == null) continue;
                qs.set(ref pos, pAsset.base_scale);

                ColorAsset color = unit.kingdom?.getColor();
                qs.setSprite(color != null ? DynamicSprites.getIcon(baseIcon, color) : baseIcon);
            }
        }
    }
}
