using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     夏朝野生王国 nomads_Xia 时序修复。
    ///
    ///     根本原因:WildKingdomsManager 在 MapBox 构造时一次性遍历当时已注册的 KingdomAsset
    ///     建实例。mod 在 OnModLoad 注册 nomads_Xia,可能晚于 MapBox 构造 → 该实例永久缺失
    ///     → 任何调 kingdoms_wild.get("nomads_Xia") 的路径都返回 null。
    ///
    ///     修复策略:
    ///     1. SaveManager.loadWorld Prefix:在建筑/单位载入之前,确保 nomads_Xia 实例存在。
    ///        这是存档加载路径的最早可用钩子,World.world 此时已就绪。
    ///     2. Actor.setDefaultKingdom Prefix:spawn 单位时的后备保证,覆盖地图生成路径。
    /// </summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.setDefaultKingdom))]
    public static class AW_WildKingdomPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Actor __instance)
        {
            EnsureWildKingdom(__instance?.asset?.kingdom_id_wild);
        }

        internal static Kingdom EnsureWildKingdom(ActorAsset pActorAsset)
        {
            return EnsureWildKingdom(pActorAsset?.kingdom_id_wild);
        }

        internal static Kingdom EnsureWildKingdom(string pWildId)
        {
            if (string.IsNullOrEmpty(pWildId)) return null;
            WildKingdomsManager mgr = World.world?.kingdoms_wild;
            if (mgr == null) return null;
            Kingdom existing = mgr.get(pWildId);
            if (existing != null) return existing;
            KingdomAsset asset = AssetManager.kingdoms.get(pWildId);
            if (asset == null)
            {
                ModClass.LogWarning("EnsureWildKingdom: KingdomAsset '" +
                    pWildId + "' 缺失,无法补建野生王国实例。");
                return null;
            }
            Kingdom created = mgr.newWildKingdom(asset);
            ModClass.LogInfo("[AW3] 补建野生王国: " + pWildId);
            return created ?? mgr.get(pWildId);
        }
    }

    /// <summary>
    ///     在存档载入前确保所有夏朝野生王国实例已存在,
    ///     防止 Building.setBuilding 内部 kingdoms_wild.get("nomads_Xia") 返回 null
    ///     导致 setKingdom(null) 崩溃(Building.cs:584)。
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadWorld),
        new[] { typeof(string), typeof(bool) })]
    internal static class AW_LoadWorldWildKingdomPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            AW_WildKingdomPatch.EnsureWildKingdom("nomads_Xia");
        }
    }

    /// <summary>
    ///     时序修复:SaveManager.loadWorld 只把 cleanUpWorld 注册成 SmoothLoader lambda 然后立即返回。
    ///     LoadWorld_Postfix 在 loadWorld 返回时触发,此时建筑尚未写入 world,清理是空操作。
    ///     真正的清理窗口是 SmoothLoader 驱动 cleanUpWorld → beginChecksBuildings →
    ///     updateDirtyBuildings 之前。
    ///
    ///     策略:LoadWorld_Prefix 设 _pendingPurge 标志; beginChecksBuildings Prefix 检测到标志后
    ///     执行一次性清理并清标志,确保 updateDirtyBuildings 遍历时不会遇到 kingdom==null 建筑崩溃。
    ///     标志只在读档流程中置位,不影响正常游戏帧。
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.loadWorld),
        new[] { typeof(string), typeof(bool) })]
    internal static class AW_LoadWorldPurgeFlagPatch
    {
        internal static bool PendingPurge;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix()
        {
            PendingPurge = true;
        }
    }

    /// <summary>
    ///     夏朝箭塔关闭 city_building 后，原版不会再把它绑定到所在城市。
    ///     创建和读档都在 BuildingManager 完成后补一次城市王国归属；没有城市的
    ///     寨子/边境箭塔不在这里处理，由对应业务路径显式绑定。
    /// </summary>
    [HarmonyPatch]
    internal static class AW_XiaWatchTowerKingdomPatch
    {
        private const string WatchTowerAssetId = "watch_tower_Xia";

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BuildingManager), "addBuilding",
            new[] { typeof(BuildingAsset), typeof(WorldTile), typeof(bool),
                typeof(bool), typeof(BuildPlacingType) })]
        private static void AddBuildingPrefix(BuildingAsset pAsset)
        {
            AW_WildKingdomPatch.EnsureWildKingdom(pAsset?.kingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BuildingManager), "addBuilding",
            new[] { typeof(BuildingAsset), typeof(WorldTile), typeof(bool),
                typeof(bool), typeof(BuildPlacingType) })]
        private static void AddBuildingPostfix(Building __result,
            BuildingAsset pAsset, WorldTile pTile)
        {
            RestoreCityWatchTowerKingdom(__result, pAsset, pTile);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.loadObject),
            new[] { typeof(BuildingData) })]
        private static void LoadObjectPrefix(BuildingData pData)
        {
            AW_WildKingdomPatch.EnsureWildKingdom(
                AssetManager.buildings.get(pData?.asset_id)?.kingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.loadObject),
            new[] { typeof(BuildingData) })]
        private static void LoadObjectPostfix(Building __result)
        {
            RestoreCityWatchTowerKingdom(__result, __result?.asset,
                __result?.current_tile);
        }

        internal static void RestoreCityWatchTowerKingdoms()
        {
            if (World.world?.buildings == null) return;
            foreach (Building building in World.world.buildings.getSimpleList())
            {
                RestoreCityWatchTowerKingdom(building, building?.asset,
                    building?.current_tile);
            }
        }

        private static void RestoreCityWatchTowerKingdom(Building pBuilding,
            BuildingAsset pAsset, WorldTile pTile)
        {
            if (pBuilding?.data == null || pBuilding.isRemoved() ||
                pBuilding.isOnRemove() || pAsset?.id != WatchTowerAssetId ||
                pTile == null) return;

            City city = null;
            try
            {
                city = pTile.zone_city ?? pTile.zone?.city;
            }
            catch { }

            if (city?.kingdom != null)
                pBuilding.setKingdom(city.kingdom);
        }
    }

    /// <summary>
    ///     换国/并国时把「漏网的建筑」一并归到新王国。
    ///
    ///     原版 <c>City.switchedKingdom()</c> 遍历的是 <c>city.buildings</c>,
    ///     而建筑要先经 <c>City.listBuilding</c> 才会进这个列表。于是存在一个
    ///     窗口:合并国家的那一刻,某座正在建造、尚未挂进城市列表的建筑就**不会**
    ///     被切到新王国 —— 它保留着旧王国的引用。旧王国随后被销毁,引用变成
    ///     悬空/null。
    ///
    ///     后果不是显示问题:原版
    ///     <c>ChunkObjectContainer.addBuilding</c> 第一行就是裸的
    ///     <c>pBuilding.kingdom.id</c>,而 <c>SimObjectsZones.checkBuildings</c>
    ///     的守卫 <c>isUsable()</c> 只查存活/废墟/移除,**不查 kingdom**。
    ///     于是 sim_object_zones 每 0.1 秒重算一次就抛一次 NRE,游戏被暂停
    ///     (玩家反馈的那次栈:checkBuildings → addBuilding → NanoObject.get_id)。
    ///
    ///     这里按城市所在地块补扫一遍,把 kingdom 为空或指向已销毁王国的建筑
    ///     重新绑到城市的新王国上。只在换国这一刻跑,不进每帧路径。
    /// </summary>
    [HarmonyPatch(typeof(City), "switchedKingdom")]
    internal static class AW_CitySwitchedKingdomBuildingPatch
    {
        [HarmonyPostfix]
        private static void Postfix(City __instance)
        {
            if (__instance?.data == null) return;
            Kingdom kingdom = __instance.kingdom;
            if (kingdom?.data == null) return;

            int repaired = 0;
            try
            {
                System.Collections.Generic.List<Building> buildings =
                    __instance.buildings;
                for (int i = 0; i < buildings.Count; i++)
                {
                    Building building = buildings[i];
                    if (building?.data == null || building.isRemoved() ||
                        building.isOnRemove()) continue;
                    // asset == null 的王国同样会让 isCiv() 这类裸解引用炸,
                    // 一并当作"坏引用"重绑。
                    if (building.kingdom != null &&
                        building.kingdom.asset != null) continue;
                    building.setKingdom(kingdom);
                    repaired++;
                }
            }
            catch (System.Exception error)
            {
                ModClass.LogWarning(
                    "[AW3] switchedKingdom 建筑归属补扫失败: " + error.Message);
                return;
            }

            if (repaired > 0)
                ModClass.LogInfo("[AW3] 换国补扫: 重绑 " + repaired +
                    " 座王国引用失效的建筑 -> " + kingdom.data.id);
        }
    }

    /// <summary>
    ///     最后一道闸:建筑在进 chunk 索引前必须有可用的 kingdom。
    ///
    ///     上面的换国补扫覆盖的是**已经挂进城市列表**的建筑;而真正触发玩家那次
    ///     崩溃的,恰恰是合并国家时**正在建造、还没进列表**的那一座 —— 它谁也
    ///     扫不到。这类建筑随后由 <c>City.listBuilding</c> 补进列表,但在那之前
    ///     sim_object_zones 已经先一步把它塞进 chunk 索引了。
    ///
    ///     原版 <c>ChunkObjectContainer.addBuilding</c> 第一行
    ///     <c>pBuilding.kingdom.id</c> 是裸解引用,而上游守卫
    ///     <c>Building.isUsable()</c> 只查存活/废墟/移除,不查 kingdom。
    ///     这里在入口把住:能就地修好(按所在城市/地块重绑)就修,修不好就跳过
    ///     这一次索引 —— 建筑本身不受影响,下一次 recalc(0.1 秒后)还会再来,
    ///     那时它通常已经有归属了。跳过一次索引远好过让整局游戏暂停。
    /// </summary>
    [HarmonyPatch(typeof(ChunkObjectContainer), "addBuilding")]
    internal static class AW_ChunkAddBuildingKingdomGuardPatch
    {
        private static int _loggedSkips;

        [HarmonyPrefix]
        private static bool Prefix(Building pBuilding)
        {
            if (pBuilding?.data == null) return false;
            if (pBuilding.kingdom?.data != null) return true;

            Kingdom resolved = null;
            try
            {
                resolved = pBuilding.city?.kingdom;
                if (resolved?.data == null)
                {
                    WorldTile tile = pBuilding.current_tile;
                    resolved = tile?.zone_city?.kingdom ??
                               tile?.zone?.city?.kingdom;
                }
            }
            catch { resolved = null; }

            if (resolved?.data != null)
            {
                try
                {
                    pBuilding.setKingdom(resolved);
                    return true;
                }
                catch { }
            }

            // 修不好:跳过这次索引,不抛异常。只报前几次,避免每 0.1 秒刷屏。
            if (_loggedSkips < 3)
            {
                _loggedSkips++;
                ModClass.LogWarning(
                    "[AW3] 跳过 kingdom 为空的建筑索引 id=" +
                    pBuilding.data.id + " asset=" +
                    (pBuilding.asset?.id ?? "?"));
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(WildKingdomsManager), "beginChecksBuildings")]
    internal static class AW_WildKingdomsBeginChecksPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            if (!AW_LoadWorldPurgeFlagPatch.PendingPurge) return;
            AW_LoadWorldPurgeFlagPatch.PendingPurge = false;
            AW_XiaWatchTowerKingdomPatch.RestoreCityWatchTowerKingdoms();
            AW_SavePatch.PurgeNullKingdomBuildings();
            AW_SavePatch.RepairNullKingdomAssets();
        }
    }
}
