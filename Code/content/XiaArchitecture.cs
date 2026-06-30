using UnityEngine;

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
    ///     建筑外观:贴图放 GameResources/buildings/civ_main/Xia/{building_id}/(继承自 AW2)。
    ///     缺贴图的(library_Xia/market_Xia,AW2 无)由 EnsureSpritesOrFallback 回退 human 外观,不紫不崩。
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

            RekeyStyledOrders(Xia);                          // 复刻 loadAutoBuildingsForAsset 的 key 映射
            if (Xia.shared_building_orders != null)          // shared 也入字典(原版 initBuildingKeys 行为)
            {
                foreach (var pair in Xia.shared_building_orders)
                {
                    Xia.addBuildingOrderKey(pair.Item1, pair.Item2);
                }
            }
            GenerateBuildings(Xia);                          // 复刻 initBuildingsFromArchitectures 的建楼
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
                b.main_path = MAIN_PATH;
                b.can_be_upgraded = false;
                b.has_sprite_construction = true;
                if (arch.spread_biome) { b.spread_biome = true; b.spread_biome_id = arch.spread_biome_id; }
                b.material = arch.material;
                if (b.material == "jelly") b.setAtlasID("buildings_wobbly", "buildings");
                b.shadow = arch.has_shadows;
                b.burnable = arch.burnable_buildings;
                b.affected_by_acid = arch.acid_affected_buildings;

                // —— 按 order 类型分支:fundament / 特例 ——
                switch (order)
                {
                    case "order_tent":        b.fundament = new BuildingFundament(2, 2, 2, 0); break;
                    case "order_hall_0":      b.fundament = new BuildingFundament(3, 3, 4, 0); break;
                    case "order_temple":      b.fundament = new BuildingFundament(2, 2, 3, 0); break;
                    case "order_watch_tower":
                        b.fundament = new BuildingFundament(1, 1, 1, 0);
                        // 投射物从 human 源建筑 clone 已带过来,无需手动设(arch.projectile_id/b.tower_projectile
                        // 在 NML 现场编译用的非 publicized dll 中不可访问,设了会编译失败)。
                        break;
                    case "order_library":     b.fundament = new BuildingFundament(2, 2, 2, 0); break;
                    case "order_docks_0":     b.upgrade_to = "docks_" + archId; b.can_be_upgraded = true; break;
                    case "order_docks_1":     b.upgraded_from = "fishing_docks_" + archId; b.has_sprites_main_disabled = false; break;
                    case "order_windmill_0":
                        b.fundament = new BuildingFundament(2, 2, 2, 0);
                        if (b.shadow) b.setShadow(0.4f, 0.38f, 0.47f);
                        break;
                }

                EnsureSpritesOrFallback(b, src); // 缺专属贴图回退 human 外观
            }
        }

        /// <summary>校验夏朝建筑贴图存在;缺(library_Xia/market_Xia)则把 sprite_path 直指 human 源建筑贴图,回退外观。</summary>
        private static void EnsureSpritesOrFallback(BuildingAsset b, BuildingAsset src)
        {
            Sprite[] list = SpriteTextureLoader.getSpriteList(b.main_path + b.id);
            if (list != null && list.Length > 0) return;

            // loadBuildingSpriteList 优先用 sprite_path(BuildingAsset.cs:686),指向 human 源建筑贴图键。
            b.sprite_path = string.IsNullOrEmpty(src.sprite_path) ? (src.main_path + src.id) : src.sprite_path;
            ModClass.LogWarning("XiaArchitecture: 建筑 '" + b.id + "' 无专属贴图,回退 human 外观(" + b.sprite_path + ")。");
        }
    }
}
