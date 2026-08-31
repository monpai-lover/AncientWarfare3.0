using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     确保 spawn 出来的单位拿得到野生王国,否则 kingdom==null 崩。
    ///
    ///     背景:<c>Actor.setDefaultKingdom()</c> = `setKingdom(World.world.kingdoms_wild.get(asset.kingdom_id_wild))`
    ///     (Actor.cs:7899)。god power 生成夏人 → 找 `kingdoms_wild.get("nomads_Xia")`,若该野生王国实例不在
    ///     运行时 → 返 null → 单位 kingdom==null → ActorManager.prepareForMetaChecks / ChunkObjectContainer.addActor
    ///     (取 `pActor.kingdom.id`)崩 NullReferenceException。
    ///
    ///     <c>WildKingdomsManager</c> 构造(MapBox 初始化,MapBox.cs:326)遍历**当时**的
    ///     AssetManager.kingdoms.list 建实例。但模组在 OnModLoad 注册 nomads_Xia 的时机与主菜单 MapBox 初始化
    ///     存在竞态:若 MapBox 先建好,nomads_Xia 那一刻还没注册 → 构造遍历漏掉它 → 之后永远缺。
    ///
    ///     **可靠修复 = Prefix setDefaultKingdom**:这是 spawn 必经点、World.world 必然已就绪。开局检查
    ///     `kingdoms_wild.get(kingdom_id_wild)` 是否为 null,缺则当场 `newWildKingdom(asset)` 补建。
    ///     通用(不写死 nomads_Xia),对齐 Cultiway「不在就 newWildKingdom」的思路,但挂在保证生效的钩子上。
    /// </summary>
    [HarmonyPatch(typeof(Actor), nameof(Actor.setDefaultKingdom))]
    public static class AW_WildKingdomPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Actor __instance)
        {
            EnsureWildKingdom(__instance?.asset);
        }

        internal static Kingdom EnsureWildKingdom(ActorAsset pActorAsset)
        {
            return EnsureWildKingdom(pActorAsset?.kingdom_id_wild);
        }

        internal static Kingdom EnsureWildKingdom(string pWildId)
        {
            string wildId = pWildId;
            if (string.IsNullOrEmpty(wildId)) return null;

            WildKingdomsManager mgr = World.world?.kingdoms_wild;
            if (mgr == null) return null;

            Kingdom existing = mgr.get(wildId);
            if (existing != null) return existing;

            KingdomAsset asset = AssetManager.kingdoms.get(wildId);
            if (asset == null)
            {
                ModClass.LogWarning("setDefaultKingdom: 野生王国 " + wildId + " 的 KingdomAsset 缺失,无法补建。");
                return null;
            }

            Kingdom created = mgr.newWildKingdom(asset); // private,publicized dll 可访问
            ModClass.LogInfo("[补建] 野生王国 " + wildId + " 不在 kingdoms_wild,已补建(spawn 单位 kingdom 不再为 null)。");
            return created ?? mgr.get(wildId);
        }

    }

    [HarmonyPatch]
    internal static class AW_BuildingWildKingdomPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Building), nameof(Building.setBuilding))]
        private static void BuildingSetBuilding_Prefix(BuildingAsset pAsset)
        {
            AW_WildKingdomPatch.EnsureWildKingdom(pAsset?.kingdom);
        }
    }
}
