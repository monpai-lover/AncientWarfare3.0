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
        }
    }
}
