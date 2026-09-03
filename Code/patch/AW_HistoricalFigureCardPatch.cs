using HarmonyLib;
using UnityEngine;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.windows;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_HistoricalFigureCardPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.addLoadWorldCallbacks))]
        private static void RegisterWorldLoaded_Postfix()
        {
            MapBox.on_world_loaded -= OnWorldLoaded;
            MapBox.on_world_loaded += OnWorldLoaded;
        }

        private static void OnWorldLoaded()
        {
            MapBox.on_world_loaded -= OnWorldLoaded;
            HistoricalFigureDrawWindow.ResetTransientState();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.clickedFinal))]
        private static bool ClickedFinal_Prefix(Vector2Int pPos, GodPower pPower)
        {
            if (!HistoricalFigureDrawWindow.IsPlacementActive || MapBox.isRenderMiniMap())
                return true;
            WorldTile tile = World.world?.GetTile(pPos.x, pPos.y);
            HistoricalFigureDrawWindow.SelectMapCity(tile?.zone?.city);
            return false;
        }
    }
}
