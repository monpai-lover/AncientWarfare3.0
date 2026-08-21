using AncientWarfare3.core.policy;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_HierarchicalVassalMapClickPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.clickedFinal))]
        private static bool ClickedFinal_Prefix(Vector2Int pPos,
            GodPower pPower)
        {
            GodPower selected = pPower ?? World.world?.selected_buttons?.
                selectedButton?.godPower;
            bool hierarchyActive = HierarchicalVassalMapModeService.IsActive();
            if (MapBox.isRenderMiniMap() ||
                !HierarchicalVassalMapClickRules.ShouldIntercept(
                    hierarchyActive, selected?.id,
                    HierarchicalVassalMapModeService.POWER_ID)) return true;
            WorldTile tile = World.world?.GetTile(pPos.x, pPos.y);
            HierarchicalVassalMapModeService.HandleZoneClick(tile,
                HierarchicalVassalMapModeService.POWER_ID);
            return false;
        }
    }
}
