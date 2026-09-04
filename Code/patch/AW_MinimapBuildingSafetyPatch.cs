using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     小地图重绘遇到「已被回收的建筑」时的兜底。
    ///
    ///     原版 <c>Building.Dispose()</c> 会把 <c>asset</c> / <c>kingdom</c> /
    ///     <c>data</c> 全部置空并把对象丢回对象池,但**不会**清掉
    ///     <c>WorldTile.building</c> 这条反向引用 —— 那是拆除流程的活。
    ///     如果拆除流程在半途被异常打断(例如某帧 <c>CityManager.update</c>
    ///     抛异常后我们把调度器停掉、游戏暂停),地块上就会残留一个指向
    ///     已回收建筑的指针。
    ///
    ///     之后 <c>MapBox.redrawMiniMap</c> → <c>updateDirtyTile</c> 每帧都会
    ///     调 <c>pTile.building.getColorForMinimap(pTile)</c>,而它第一行就是
    ///     <c>asset.building_sprites</c> —— 空引用。玩家日志里那条
    ///     「previous errors repeated 3563 times」就是这么刷出来的:一个残留
    ///     指针足以把整局游戏的日志和帧率都毁掉。
    ///
    ///     这里做两件事:返回透明色让原版退回地形色,并且把那条残留的
    ///     <c>WorldTile.building</c> 指针清掉 —— 后者才是关键,因为
    ///     <c>WorldTile</c> 里还有一大堆 <c>building.asset.xxx</c> 的裸解引用
    ///     (烧毁、放置、冻结判定)会踩同一颗雷。
    ///
    ///     这是兜底,不是根治:真正该修的是那个把拆除流程打断的异常。见
    ///     <see cref="AW_CityManagerMutationBoundaryPatch"/> 里关于 finalizer
    ///     擦除栈帧的说明 —— 在那个问题修好之前,玩家日志根本给不出抛出点。
    /// </summary>
    [HarmonyPatch]
    internal static class AW_MinimapBuildingSafetyPatch
    {
        private static int _reported;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Building), "getColorForMinimap")]
        private static bool GetColorForMinimap_Prefix(Building __instance,
            WorldTile pTile, ref Color32 __result)
        {
            if (__instance != null && __instance.asset != null) return true;

            __result = Toolbox.clear;
            if (pTile != null && ReferenceEquals(pTile.building, __instance))
                pTile.building = null;
            if (Interlocked.Increment(ref _reported) == 1)
                ModClass.LogError(
                    "AW cleared a stale WorldTile.building pointer to a " +
                    "recycled building during minimap redraw; the tile now " +
                    "renders terrain colour. This means a building teardown " +
                    "was interrupted earlier in the session.");
            return false;
        }

        internal static void Reset()
        {
            Interlocked.Exchange(ref _reported, 0);
        }
    }
}
