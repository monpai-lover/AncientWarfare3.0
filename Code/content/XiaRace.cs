using UnityEngine;

namespace AncientWarfare3.content
{
    /// <summary>
    ///     夏朝 Xia 文明种族注册。
    ///
    ///     新版 WorldBox(0.51.0+)中"文明种族"由 <see cref="ActorAsset"/> 承载
    ///     (旧版 Race 类已删除),经 <c>AssetManager.actor_library.clone(新id, 模板id)</c> 注册。
    ///     模板 <c>$civ_advanced_unit$</c> 即 human/elf/orc/dwarf 共用的高等文明单位模板。
    ///
    ///     参数取自 AW2 旧 Xia(蓝图 1.1):军事起步弱(army_multiplier 0.5)、基础城市 1、
    ///     产出 bread/pie/tea、地图色 #33724D、贴图复用 actors/races/Xia/。
    ///     旧版被移除的字段(civ_base_zone_range/hateRaces/偏好武器池等)用新版机制替代或暂略。
    /// </summary>
    public static class XiaRace
    {
        public const string ID = "Xia";
        internal static readonly string[] HumanLikeSubspeciesTraits =
        {
            "reproduction_strategy_viviparity",
            "gestation_long",
            "reproduction_sexual",
            "bad_genes",
            "advanced_hippocampus",
            "stomach",
            "amygdala",
            "wernicke_area",
            "diet_omnivore",
            "polyphasic_sleep",
            "nocturnal_dormancy"
        };

        // 贴图根路径已统一到 Cultiway 标准 actors/species/civs/{id}/(原 actors/races/Xia/)。
        // 子目录名仍保留 AW2 命名(unit_male_1 等),由 XiaTextures.BindSkinArrays 扫描对齐。
        public const string TEXTURE_PATH = "actors/species/civs/Xia/";

        public static ActorAsset asset;

        public static void Init()
        {
            ActorAsset Xia = AssetManager.actor_library.clone(ID, "$civ_advanced_unit$");
            asset = Xia;

            // —— 命名 / 本地化 ——
            Xia.name_locale = ID; // 本地化键 Xia(经 .Underscore() 查 creatures 域)
            Xia.icon = "iconXias";
            Xia.name_template_sets = AssetLibrary<ActorAsset>.a<string>(
                "human_default_set", "human_slavic_set", "human_germanic_set",
                "human_rus_set", "human_posh_set", "human_folk_set");

            // —— 王国关联 ——
            Xia.kingdom_id_wild = "nomads_Xia";
            Xia.kingdom_id_civilization = ID;
            // 旗帜:新版从 banners_kingdoms/{banner_id}/background|icon 扫贴图(KingdomBannerLibrary.loadNewAssetRuntime)。
            // 夏朝旗帜贴图放 GameResources/banners_kingdoms/Xia/(继承自 AW2,F 盘 background/icon 齐全)。
            // **不在此预读 getSpriteList 校验**:OnModLoad 时机 mod 资源未注册进 Unity Resources,预读会把空数组
            // 永久缓存进 SpriteTextureLoader(与建筑崩溃同一毒化机制,见 XiaArchitecture),反使旗帜真图加载不出。
            // banner 加载(loadNewAssetRuntime)走同一 getSpriteList 缓存,故直接信任 "Xia"、由游戏运行时懒加载。
            Xia.banner_id = "Xia";

            // —— 建筑 ——(夏朝专属 architecture,见 XiaArchitecture;在本 Init 之前已注册)
            Xia.architecture_id = "Xia";
            Xia.architecture_asset = AssetManager.architecture_library.get("Xia")
                                     ?? AssetManager.architecture_library.get("human"); // 兜底
            Xia.build_order_template_id = "build_order_advanced"; // 种族建造技术树,与建筑外观无关,保留

            // —— 文明属性(蓝图 1.1)——
            Xia.civ_base_cities = 1;             // 旧 civ_baseCities
            Xia.civ_base_army_multiplier = 1.2f; // 军事加强:原 0.5(军事起步弱)→ 1.2,军队规模略强于原版(默认 0.35)
            Xia.production = new[] { "bread", "pie", "tea" };

            // —— 外观 ——
            Xia.color_hex = "#33724D";
            Xia.color = Toolbox.makeColor("#33724D");
            Xia.zombie_color_hex = "#00AD2C";

            // check_flip 兜底:原版 ActorAssetLibrary.linkAssets(L957)给 check_flip==null 的 asset 补默认委托,
            // 但 mod 在 OnModLoad clone 注册晚于 linkAssets → Xia 漏补 → 移动时 asset.check_flip(this) 抛
            // NullReferenceException(Actor.checkFlip / ActorMovement.cs:91)。手动补与原版同款默认委托。
            if (Xia.check_flip == null)
            {
                Xia.check_flip = (BaseSimObject pObj, WorldTile pTile) => true;
            }

            // —— 分类学(沿用 human 谱系)——
            Xia.cloneTaxonomyFromForSapiens("human");
            Xia.name_taxonomic_genus = "homo";
            Xia.name_taxonomic_species = "sapiens";

            // —— 基因(genome):夏人战斗/文明属性的真正来源 ——
            // 根因(H18 属性弱):原版种族的 health/damage/speed/warfare 等都来自 addGenome(基因),
            //   而非 base_stats。human 段显式 addGenome(health100/damage15/speed15/warfare3...);
            //   夏人 clone 自 $civ_advanced_unit$ 模板(非 clone human),模板本身没 addGenome →
            //   genome 近乎空 → 夏人战斗属性走最低默认,显著弱于 human。这里给夏人一套略强于 human
            //   的基因(华夏先进文明:体质/军略/治理/智力全面高一档),addGenome 对已存在 part 是累加。
            Xia.addGenome(
                ("health", 130f),       // human 100
                ("stamina", 120f),      // human 100
                ("mutation", 1f),
                ("lifespan", 90f),      // human 70(与下方 base_stats lifespan 对齐)
                ("damage", 20f),        // human 15
                ("speed", 16f),         // human 15(略快)
                ("offspring", 6f),      // human 5
                // ⚠ birth_rate 必须显式给(每帧繁殖 BabyMaker.cs:123 用 (int)stats["birth_rate"] 决定额外子女数)。
                //   原版各文明种族 genome 都显式设(human/orc=3~5);夏人 clone 模板未继承 → 漏设则基础≈0,
                //   叠加贵族 guizu 的小数后 (int) 取整成 0 → **贵族/平民几乎不生育**(用户实测生育数=0 的真凶之一)。
                ("birth_rate", 4f),     // human 3,夏人略高(多子)
                ("diplomacy", 5f),      // human 3
                ("warfare", 5f),        // human 3(军略更强)
                ("stewardship", 5f),    // human 3
                ("intelligence", 5f));  // human 3

            // Clone from $civ_advanced_unit$ does not include human's species traits.
            // Without reproduction_sexual/viviparity, Xia units can join cities,
            // raising population, while the normal child-birth path never runs.
            foreach (string traitId in HumanLikeSubspeciesTraits)
            {
                Xia.addSubspeciesTrait(traitId);
            }

            // —— 单位寿命/繁衍(旧 unit_Xia:max_age 90 / max_children 6)——
            // base_stats 与 genome 是两套机制,这里保留直接的 base_stats 寿命/繁衍上限。
            Xia.base_stats["lifespan"] = 90f;
            Xia.base_stats["offspring"] = 6f;

            // —— 军事:不再"起步弱"。原 civ_base_army_multiplier=0.5(AW2 蓝图设的军事起步弱),
            //   现提到 1.2 让夏朝军队规模略强于原版(默认 0.35)。 ——

            // —— 贴图接入:逐帧 png 目录(NML ResourcesPatch 自动注入 GameResources)——
            XiaTextures.BindRaceTextures(Xia, TEXTURE_PATH);

            // —— phenotype(肤色着色)——
            AssetManager.actor_library.addPhenotype("skin_light", "default_color");
            AssetManager.actor_library.addPhenotype("skin_dark", "default_color");
            AssetManager.actor_library.addPhenotype("skin_mixed", "default_color");
        }
    }
}
