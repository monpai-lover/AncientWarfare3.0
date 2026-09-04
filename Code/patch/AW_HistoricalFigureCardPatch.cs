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
            if (!HistoricalFigureDrawWindow.IsPickingTile) return true;
            if (MapBox.isRenderMiniMap()) return true;
            WorldTile tile = World.world?.GetTile(pPos.x, pPos.y);
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
            if (!HistoricalFigureDrawWindow.IsPickingTile) return true;
            if (MapBox.isRenderMiniMap()) return true;
            if (!PixelDetector.GetSpritePixelColorUnderMousePointer(
                    World.world, out Vector2Int pos) || pos.x == -1)
                return false;
            WorldTile tile = World.world?.GetTile(pos.x, pos.y);
            HistoricalFigureDrawWindow.SelectMapTile(tile);
            return false;
        }

        /// <summary>
        ///     没选中任何神力时的兜底兜底。
        ///
        ///     <para>
        ///     原版 <c>PlayerControl.Update</c> 分支是
        ///     <c>if (!isAnyPowerSelected()) checkEmptyClick(); else … clickedStart()</c>。
        ///     之前只挂了 <c>clickedStart</c> / <c>clickedFinal</c> 前缀,覆盖「已选神力」
        ///     的那条路;没选神力时点击走 <c>checkEmptyClick</c>,既不拦也不放,
        ///     玩家点图无任何反馈 —— 这正是在部署选点状态下「点了没反应、没弹窗」的
        ///     主因。这里把 <c>checkEmptyClick</c> 也拦截:选点期间无论是否选了神力,
        ///     点地图都路由到 <c>SelectMapTile</c>。
        ///     </para>
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(PlayerControl), "checkEmptyClick")]
        private static bool CheckEmptyClick_Prefix()
        {
            if (!HistoricalFigureDrawWindow.IsPickingTile) return true;
            if (MapBox.isRenderMiniMap()) return true;
            if (!PixelDetector.GetSpritePixelColorUnderMousePointer(
                    World.world, out Vector2Int pos) || pos.x == -1)
                return false;
            WorldTile tile = World.world?.GetTile(pos.x, pos.y);
            HistoricalFigureDrawWindow.SelectMapTile(tile);
            return false;
        }

        /// <summary>
        ///     卡片降临时把历史国号直接种进建国流程,不走「先随机后改名」。
        ///
        ///     <para>
        ///     原版 <c>Kingdom.newCivKingdom</c> 里是
        ///     <c>generateName(MetaType.Kingdom, …)</c> → <c>setName(随机名)</c>,
        ///     紧接着 <c>KingdomManager.makeNewCivKingdom</c> 就
        ///     <c>WorldLog.logNewKingdom(kingdom)</c>。部署服务的
        ///     <c>setName(历史国号)</c> 排在这之后 —— 随机名此时已经落进世界日志、
        ///     编年史与归档,数据被污染。
        ///     </para>
        ///
        ///     <para>
        ///     这里在 <c>newCivKingdom</c> 的后置里(早于 logNewKingdom)就把名字改
        ///     成历史国号,让随机名从未对外可见。仅在部署作用域内生效,
        ///     其余建国路径不受影响。
        ///     </para>
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.newCivKingdom))]
        private static void NewCivKingdom_Postfix(Kingdom __instance)
        {
            string name = HistoricalFigureCardDeploymentService.PendingKingdomName;
            if (string.IsNullOrEmpty(name) || __instance?.data == null) return;
            __instance.setName(name, pTrack: false);
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
            if (!HistoricalFigureDrawWindow.IsPickingTile) return;
            if (ScrollWindow.isWindowActive()) return;
            __result = false;
        }
    }
}
