using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     Building.setKingdom 王国有效性守卫。
    ///
    ///     <para>
    ///     成因:读档时 <c>SaveManager.loadBuildings</c> → <c>BuildingManager.loadObject</c>
    ///     → <c>setBuilding</c> 会给城市建筑调
    ///     <c>setKingdom(current_tile.zone_city.kingdom)</c>(Building.cs:369)。
    ///     而 <c>Kingdom.loadData</c>(Kingdom.cs:784) 用
    ///     <c>AssetManager.kingdoms.get(actorAsset.kingdom_id_civilization)</c> 解析
    ///     <c>kingdom.asset</c>,取不到时**静默**赋 null —— 存档里的王国 id 指向一个
    ///     当时没注册上的王国资产(改过 id / 读档时资产未就绪)就会这样。
    ///     </para>
    ///
    ///     <para>
    ///     后果:<c>Building.setKingdom</c> 把 <c>kingdom = pKingdom</c> 赋上后调用
    ///     <c>isKingdomCiv()</c>(内联<see cref="Kingdom.isCiv"/>,裸的
    ///     <c>return asset.civ;</c>),<c>kingdom.asset == null</c> → 每帧读档时抛 NRE
    ///     (原版栈 Building.cs:584 / Kingdom.isCiv)。
    ///     </para>
    ///
    ///     <para>
    ///     这里在入口拦截:pKingdom 无效(为 null 或其 asset 为 null)时,尝试就地解析一个
    ///     有效王国;解析不出就**跳过本次绑定**,不让坏王国进 <c>isKingdomCiv</c>。
    ///     跳过不产生破坏 —— 建筑保持当前(可能是空)归属,后续
    ///     <c>AW_ChunkAddBuildingKingdomGuardPatch</c> /
    ///     <c>AW_CitySwitchedKingdomBuildingPatch</c> 会按城市/地块补绑。
    ///     根因(王国 asset 丢失)由 <c>AW_SavePatch.RepairNullKingdomAssets</c> 在读档早期修复。
    ///     </para>
    /// </summary>
    [HarmonyPatch]
    internal static class AW_BuildingKingdomSafetyPatch
    {
        private static int _loggedSkips;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Building), "setKingdom")]
        private static bool SetKingdomPrefix(Building __instance,
            Kingdom pKingdom, out Kingdom __state)
        {
            __state = __instance?.kingdom;

            // pKingdom 有效(非 null 且 asset 已解析)→ 放行原逻辑。
            if (pKingdom != null && pKingdom.asset != null)
                return true;

            // pKingdom 无效:先尝试就地解析一个有效王国,以免建筑被置成坏归属。
            Kingdom resolved = TryResolveValidKingdom(__instance);
            if (resolved != null)
            {
                // resolved 有效 → 递归一次,前缀看到 asset 非 null 会放行,不重复处理。
                __instance.setKingdom(resolved);
                return false;
            }

            // 解析不出:跳过本次绑定,建筑维持当前归属(可能是 null)。
            // 报前几次即可,避免读档/换国时刷屏。
            if (_loggedSkips < 3)
            {
                _loggedSkips++;
                ModClass.LogWarning(
                    "[AW3] 跳过 setKingdom 绑定无效王国 asset=" +
                    (pKingdom?.asset?.id ?? "(pKingdom null)") +
                    " 建筑 id=" + (__instance?.data?.id ?? -1L));
            }
            return false;
        }

        private static Kingdom TryResolveValidKingdom(Building pBuilding)
        {
            if (pBuilding == null) return null;
            try
            {
                // 所在城市的当前王国,通常是正确归属。
                Kingdom fromCity = pBuilding.city?.kingdom;
                if (fromCity?.asset != null) return fromCity;

                WorldTile tile = pBuilding.current_tile;
                Kingdom fromTile = tile?.zone_city?.kingdom ??
                                   tile?.zone?.city?.kingdom;
                if (fromTile?.asset != null) return fromTile;
            }
            catch
            {
                // current_tile.zone 未就绪等情形,返回 null 交给跳过逻辑。
            }
            return null;
        }
    }
}
