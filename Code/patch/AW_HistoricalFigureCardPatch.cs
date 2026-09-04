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
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(PlayerControl), "clickedFinal",
            new[] { typeof(Vector2Int), typeof(GodPower), typeof(bool) })]
        private static bool ClickedFinal_Prefix(Vector2Int pPos, GodPower pPower,
            bool pTrack)
        {
            if (!HistoricalFigureDrawWindow.IsPlacementActive) return true;
            if (MapBox.isRenderMiniMap()) return true;
            WorldTile tile = World.world?.GetTile(pPos.x, pPos.y);
            ModClass.LogInfo("[AW3 cards deploy] clickedFinal intercepted pos=" +
                pPos.x + "," + pPos.y + " power=" + (pPower?.id ?? "null") +
                " tile=" + (tile == null ? "null" : "ok"));
            HistoricalFigureDrawWindow.SelectMapTile(tile);
            return false;
        }

        /// <summary>
        ///     选点期间兜住原版的落地动作。
        ///
        ///     <para>
        ///     <c>clickedFinal</c> 是 <c>internal</c> 且带默认参数,单靠它一处
        ///     拦截不保险。这里再补一道:神力真正生效的入口是
        ///     <c>GodPower.click_power_action</c> / <c>click_action</c>,
        ///     它们由 <c>clickedFinal</c> 调起。万一上面的前缀没能挂上,
        ///     这条 <c>clickedStart</c> 前缀会在更外层把整次点击接管掉,
        ///     避免「直接放出一个普通 actor」。
        ///     </para>
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(PlayerControl), "clickedStart")]
        private static bool ClickedStart_Prefix()
        {
            if (!HistoricalFigureDrawWindow.IsPlacementActive) return true;
            if (MapBox.isRenderMiniMap()) return true;
            if (!PixelDetector.GetSpritePixelColorUnderMousePointer(
                    World.world, out Vector2Int pos) || pos.x == -1)
                return false;
            WorldTile tile = World.world?.GetTile(pos.x, pos.y);
            ModClass.LogInfo("[AW3 cards deploy] clickedStart intercepted pos=" +
                pos.x + "," + pos.y +
                " tile=" + (tile == null ? "null" : "ok"));
            HistoricalFigureDrawWindow.SelectMapTile(tile);
            return false;
        }

        /// <summary>
        ///     每帧驱动「延后一帧开确认窗」。窗口在选点期间是隐藏的,
        ///     它自己的 Update 不跑,所以挂在原版 PlayerControl 上。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControl), "updateControls")]
        private static void UpdateControls_Postfix()
        {
            HistoricalFigureDrawWindow.TickPendingConfirmWindow();
        }

        /// <summary>
        ///     选点期间强制放行原版的点击闸门。
        ///
        ///     <para>
        ///     <c>PlayerControl.Update</c> 在 <c>isOverUI()</c> 为真时直接
        ///     <c>return</c>,连 <c>clickedStart</c> 都不会调。而
        ///     <c>ScrollWindow.clickHide</c> 关窗后
        ///     <c>_over_ui_timeout</c> 仍在计时、指针也常常还压在刚关掉的
        ///     窗口区域上 —— 于是选点状态下点地图整个链路都被这道闸门吃掉,
        ///     表现就是「有提示但点了没反应」。
        ///     </para>
        ///
        ///     <para>
        ///     只在选点状态下放行,且只影响这一个判断:别的 UI 交互不受影响。
        ///     </para>
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.isOverUI))]
        private static void IsOverUI_Postfix(ref bool __result)
        {
            if (!HistoricalFigureDrawWindow.IsPlacementActive) return;
            if (ScrollWindow.isWindowActive()) return;
            __result = false;
        }
    }
}
