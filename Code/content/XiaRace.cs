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
        public const string TEXTURE_PATH = "actors/races/Xia/";

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
            Xia.banner_id = "Xia";

            // —— 建筑 ——(暂复用 human 建筑集,批D 再做夏朝专属建筑)
            Xia.architecture_id = "human";
            Xia.architecture_asset = AssetManager.architecture_library.get("human");
            Xia.build_order_template_id = "build_order_advanced";

            // —— 文明属性(蓝图 1.1)——
            Xia.civ_base_cities = 1;             // 旧 civ_baseCities
            Xia.civ_base_army_multiplier = 0.5f; // 旧 civ_base_army_mod:军事起步弱
            Xia.production = new[] { "bread", "pie", "tea" };

            // —— 外观 ——
            Xia.color_hex = "#33724D";
            Xia.color = Toolbox.makeColor("#33724D");
            Xia.zombie_color_hex = "#00AD2C";

            // —— 分类学(沿用 human 谱系)——
            Xia.cloneTaxonomyFromForSapiens("human");
            Xia.name_taxonomic_genus = "homo";
            Xia.name_taxonomic_species = "sapiens";

            // —— 单位寿命/繁衍(旧 unit_Xia:max_age 90 / max_children 6)——
            Xia.base_stats["lifespan"] = 90f;
            Xia.base_stats["offspring"] = 6f;

            // —— 贴图接入:逐帧 png 目录(NML ResourcesPatch 自动注入 GameResources)——
            XiaTextures.BindRaceTextures(Xia, TEXTURE_PATH);

            // —— phenotype(肤色着色)——
            AssetManager.actor_library.addPhenotype("skin_light", "default_color");
            AssetManager.actor_library.addPhenotype("skin_dark", "default_color");
            AssetManager.actor_library.addPhenotype("skin_mixed", "default_color");
        }
    }
}
