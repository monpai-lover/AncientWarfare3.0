using AncientWarfare3.content.schools;

namespace AncientWarfare3.content
{
    /// <summary>
    ///     夏朝 Xia 专属建筑风格(architecture)+ 建筑资产生成。
    ///
    ///     时序坑:游戏 `ArchitectureLibrary` 的 loadAutoBuildingsForAsset 与
    ///     `BuildingLibrary.initBuildingsFromArchitectures`(BuildingLibrary.cs:106-185)都在
    ///     AssetManager init/post_init 跑,**早于** mod 的 OnModLoad。所以本 mod 在 OnModLoad 里
    ///     clone 出的 "Xia" architecture **不会**被自动建楼 → 必须在这里手动复刻那两段逻辑:
    ///       ① RekeyStyledOrders:把 clone 自 human 的 building_ids_for_construction 由 *_human re-key 成 *_Xia
    ///          (clone 深拷贝字典但值不变,不 re-key 则 getBuilding 仍取 human 建筑);
    ///       ② GenerateBuildings:对每个 styled order 从 human 源建筑 clone 出 {id}_Xia,设 main_path/fundament 等。
    ///
    ///     建筑外观:贴图放 GameResources/buildings/civ_main/Xia/{building_id}/(继承自 AW2),
    ///     游戏在真正渲染时用 main_path+id 懒加载。**绝不在此 init 阶段预读 getSpriteList 校验贴图**——
    ///     OnModLoad 时机 mod 资源未注册进 Unity Resources,预读会把空数组永久缓存进 SpriteTextureLoader,
    ///     反使 Xia 建筑真图加载不出 → getRecoloredBuilding(null) 每帧崩(曾经的元凶,已删)。
    ///     主贴图万一缺失的兜底由 AW_BuildingSpritePatch(Prefix calculateColoredSprite 防 null)负责,不在此预查。
    ///     码头/船:clone architecture 时 actor_asset_id_trading/transport 从 human 带过来(boat_*_human 存在),
    ///     复用 human 船,不做夏朝专属船。
    /// </summary>
    public static class XiaArchitecture
    {
        public const string ID = "Xia";
        public const string FROM = "human";
        private const string MAIN_PATH = "buildings/civ_main/Xia/";

        public static ArchitectureAsset asset;

        public static void Init()
        {
            if (AssetManager.architecture_library.get(FROM) == null)
            {
                ModClass.LogWarning("XiaArchitecture: 原版 human architecture 缺失,跳过。");
                return;
            }

            ArchitectureAsset Xia = AssetManager.architecture_library.clone(ID, FROM);
            asset = Xia;
            Xia.generate_buildings = true;
            Xia.generation_target = FROM; // 源建筑取自 human

            // 夏朝专属船(XiaBoats 已在本 Init 之前 clone 出 boat_*_Xia);覆盖 clone 自 human 的 *_human。
            if (AssetManager.actor_library.get(XiaBoats.TRADING_ID) != null)
                Xia.actor_asset_id_trading = XiaBoats.TRADING_ID;
            if (AssetManager.actor_library.get(XiaBoats.TRANSPORT_ID) != null)
                Xia.actor_asset_id_transport = XiaBoats.TRANSPORT_ID;

            RekeyStyledOrders(Xia);                          // 复刻 loadAutoBuildingsForAsset 的 key 映射
            if (Xia.shared_building_orders != null)          // shared 也入字典(原版 initBuildingKeys 行为)
            {
                foreach (var pair in Xia.shared_building_orders)
                {
                    Xia.addBuildingOrderKey(pair.Item1, pair.Item2);
                }
            }
            GenerateBuildings(Xia);                          // 复刻 initBuildingsFromArchitectures 的建楼
            SchoolAcademyBuildingContent.Init(Xia);
        }

        /// <summary>把 styled order 映射成 *_Xia 建筑 id(照抄 loadAutoBuildingsForAsset 的命名规则)。</summary>
        private static void RekeyStyledOrders(ArchitectureAsset arch)
        {
            if (arch.styled_building_orders == null) return;
            string id = arch.id;
            foreach (string order in arch.styled_building_orders)
            {
                string newId = null;
                switch (order)
                {
                    case "order_tent":        newId = "tent_" + id; break;
                    case "order_house_0":
                    case "order_house_1":
                    case "order_house_2":
                    case "order_house_3":
                    case "order_house_4":
                    case "order_house_5":     newId = "house_" + id + "_" + order.Substring(order.Length - 1); break;
                    case "order_hall_0":
                    case "order_hall_1":
                    case "order_hall_2":      newId = "hall_" + id + "_" + order.Substring(order.Length - 1); break;
                    case "order_windmill_0":
                    case "order_windmill_1":  newId = "windmill_" + id + "_" + order.Substring(order.Length - 1); break;
                    case "order_docks_0":     newId = "fishing_docks_" + id; break;
                    case "order_docks_1":     newId = "docks_" + id; break;
                    case "order_watch_tower": newId = "watch_tower_" + id; break;
                    case "order_temple":      newId = "temple_" + id; break;
                    case "order_library":     newId = "library_" + id; break;
                    case "order_market":      newId = "market_" + id; break;
                    case "order_barracks":    newId = "barracks_" + id; break;
                }
                if (newId != null) arch.addBuildingOrderKey(order, newId);
            }
        }

        /// <summary>对每个 styled order,从 human 源建筑 clone 出 Xia 建筑并设字段(照抄 initBuildingsFromArchitectures)。</summary>
        private static void GenerateBuildings(ArchitectureAsset arch)
        {
            if (arch.styled_building_orders == null) return;
            string archId = arch.id;
            ArchitectureAsset source = AssetManager.architecture_library.get(arch.generation_target); // human(未改,keys 仍 *_human)
            if (source == null) return;

            foreach (string order in arch.styled_building_orders)
            {
                if (!arch.building_ids_for_construction.ContainsKey(order)) continue;
                string newId = arch.building_ids_for_construction[order]; // 已 re-key 成 *_Xia
                BuildingAsset src = source.getBuilding(order);            // human 源建筑(house_human_0 等)
                if (src == null) continue;

                BuildingAsset b = AssetManager.buildings.clone(newId, src.id);

                // —— 照抄原版固定字段 ——
                b.group = "civ_building";
                b.mini_civ_auto_load = true;
                b.civ_kingdom = archId;
                // 对齐 Cultiway(CloneHuman 设了 asset.kingdom=野生王国 id):补野生王国归属,
                // 否则建筑 kingdom 仍指向 human 的 nomads,城市/野生归属可能错乱。
                b.kingdom = "nomads_" + archId;
                b.main_path = MAIN_PATH;
                b.has_sprite_construction = true;

                // re-key 升级链:clone 自 human 的 upgrade_to/upgraded_from 仍指向 *_human,
                // 统一替成 *_Xia,避免升级到无 Xia 贴图的混合 id 建筑(Cultiway 同样 Replace)。
                if (!string.IsNullOrEmpty(b.upgrade_to))
                    b.upgrade_to = b.upgrade_to.Replace("_" + FROM, "_" + archId);
                if (!string.IsNullOrEmpty(b.upgraded_from))
                    b.upgraded_from = b.upgraded_from.Replace("_" + FROM, "_" + archId);
                if (arch.spread_biome) { b.spread_biome = true; b.spread_biome_id = arch.spread_biome_id; }
                b.material = arch.material;
                if (b.material == "jelly") b.setAtlasID("buildings_wobbly", "buildings");
                b.shadow = arch.has_shadows;
                b.burnable = arch.burnable_buildings;
                b.affected_by_acid = arch.acid_affected_buildings;

                // ⚠ 绑定 atlas_asset(每帧崩真凶):原版在 post_init 的 BuildingLibrary.checkAtlasLink
                // (BuildingLibrary.cs:97/101)给所有建筑设 atlas_asset = dynamic_sprites_library.get(atlas_id);
                // mod 晚于 post_init 手建楼 → 漏设 → b.atlas_asset 恒 null →
                // calculateColoredSprite→getRecoloredBuilding(.,.,null)→ pAtlasAsset.getSprite 每帧崩(DynamicSprites.cs:23)。
                // 这里显式复刻(Cultiway PostInit 同样 asset.atlas_asset = ...get(atlas_id))。
                b.atlas_asset = AssetManager.dynamic_sprites_library.get(b.atlas_id)
                                ?? AssetManager.dynamic_sprites_library.get("buildings");

                // —— 按 order 类型分支:fundament / 特例 ——
                switch (order)
                {
                    case "order_tent":        b.fundament = new BuildingFundament(2, 2, 2, 0); break;
                    case "order_hall_0":      b.fundament = new BuildingFundament(3, 3, 4, 0); break;
                    case "order_temple":      b.fundament = new BuildingFundament(2, 2, 3, 0); break;
                    case "order_watch_tower":
                        b.fundament = new BuildingFundament(1, 1, 1, 0);
                        // 箭塔在寨子生成/读档两条路径都有问题，必须同时清两个标志：
                        //
                        // ① city_building=true（继承自 $city_building$ 模板）→ setBuilding 路径②：
                        //   setKingdom(current_tile.zone_city.kingdom)
                        //   读档时 bandit 城市的 kingdom 还是 null → 崩溃（Building.cs:369）
                        //
                        // ② asset.kingdom="nomads_Xia"（本类第 120 行统一设置）→ setBuilding 路径①：
                        //   setKingdom(kingdoms_wild.get("nomads_Xia"))
                        //   若野生王国实例不存在 → null → 崩溃（Building.cs:246）
                        //
                        // 两条路径都清掉：PlaceTowers 已经显式调 building.setKingdom(bandit) 设归属。
                        b.city_building = false;
                        b.kingdom = "";
                        break;
                    case "order_library":     b.fundament = new BuildingFundament(2, 2, 2, 0); break;
                    case "order_docks_0":     b.upgrade_to = "docks_" + archId; b.can_be_upgraded = true; break;
                    case "order_docks_1":
                        b.upgraded_from = "fishing_docks_" + archId;
                        b.upgrade_to = string.Empty;
                        b.can_be_upgraded = false;
                        b.has_sprites_main_disabled = false;
                        break;
                    case "order_windmill_0":
                        b.fundament = new BuildingFundament(2, 2, 2, 0);
                        if (b.shadow) b.setShadow(0.4f, 0.38f, 0.47f);
                        break;
                }

                // 原版 docks_human 从 fishing_docks_human clone 而来,保留 upgrade_to=docks_human,
                // 但用 can_be_upgraded=false 阻止继续升级。不能用 "upgrade_to 非空" 反推升级开关,
                // 否则 docks_Xia 会变成 upgrade_to 自己,CityBehBuild.haveRequiredBuildings 进入死循环。
                if (b.upgrade_to == b.id)
                {
                    b.upgrade_to = string.Empty;
                    b.can_be_upgraded = false;
                }

                // 不在此预读校验贴图:OnModLoad 时机 mod 的 GameResources 尚未注册进 Unity Resources,
                // getSpriteList 会把空数组**永久缓存**进 SpriteTextureLoader(_cached_sprite_list),
                // 导致 Xia 建筑真图之后再也加载不出 → 每帧 getRecoloredBuilding(null) 崩。
                // 让游戏在真正渲染时用 main_path+id 懒加载即可(Cultiway 同样不预读)。
            }
        }

    }
}
