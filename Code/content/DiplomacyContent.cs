using AncientWarfare3.core.lineage;

namespace AncientWarfare3.content
{
    internal static class DiplomacyContent
    {
        public static void Init()
        {
            RitualDiplomacyOpinionService.RegisterAssets();
            AddWarNameTemplate("war_reclaim", "收复战争,收复旧土之战");
            AddWarNameTemplate("war_restoration", "复国战争,恢复旧统之战");
            AddWarNameTemplate("war_tributary", "叩关战争,入贡之战");
            AddWarNameTemplate("war_jingnan", "靖难之役,清君侧之战,宗室靖难");
            AddWarNameTemplate("war_succession_dispute",
                "争位之战,宗统之争,两朝争位");
            AddWarNameTemplate("war_coup_restoration",
                "勤王复统之战,讨逆复统之役,宗室复位之战");
            AddWarNameTemplate("war_zhulu", "逐鹿之战,问鼎之战,天下争衡");

            AddWarType("aw_normal_war", "war_conquest", "war_type_aw_normal_war", "ui/wars/war_conquest", true);
            AddWarType("reclaim", "war_reclaim", "war_type_reclaim", "ui/wars/war_reclaim", true);
            AddWarType("restoration_war", "war_restoration", "war_type_restoration_war", "ui/wars/war_reclaim", true);
            AddWarType("vassal_war", "war_conquest", "war_type_vassal_war", "ui/wars/war_vassal", false);
            AddWarType("tributary_war", "war_tributary", "war_type_tributary_war", "ui/wars/war_vassal", true);
            AddWarType("independence_war", "war_conquest", "war_type_independence_war", "ui/wars/war_independent", false);
            AddWarType("tianming", "war_conquest", "war_type_tianming", "ui/Icons/traits/iconTianming", true);
            AddWarType("tianmingrebel", "war_conquest", "war_type_tianmingrebel", "ui/wars/war_tianmingrebel", false,
                pRebellion: true);
            AddWarType("general_rebellion_war", "war_conquest", "war_type_general_rebellion_war",
                "ui/wars/war_rebellion", false, pRebellion: true);
            AddWarType("fief_independence_war", "war_conquest", "war_type_fief_independence_war",
                "ui/wars/war_independent", false, pRebellion: true);
            AddWarType(FeudatoryJingnanRules.WarTypeId, "war_jingnan",
                "war_type_jingnan_war", "ui/wars/war_rebellion", false,
                pRebellion: true);
            AddWarType(SuccessionDisputeRules.WarTypeId,
                "war_succession_dispute", "war_type_succession_dispute_war",
                "ui/wars/war_rebellion", false, pRebellion: true);
            AddWarType(CoupRestorationRules.WarTypeId,
                "war_coup_restoration", "war_type_coup_restoration_war",
                "ui/wars/war_rebellion", false, pRebellion: true);
            AddWarType(ZhuluWarRules.WarTypeId, "war_zhulu",
                "war_type_zhulu_war", "ui/Icons/traits/iconTianming",
                pAllianceJoin: ZhuluWarRules.ShouldAllowAllianceJoin(),
                pRebellion: false,
                pTotalWar: ZhuluWarRules.ShouldUseVanillaTotalWar());
        }

        private static void AddWarType(string pId, string pNameTemplate,
            string pLocalizedType, string pIcon, bool pAllianceJoin,
            bool pRebellion = false, bool pTotalWar = false)
        {
            WarTypeAsset asset = null;
            try
            {
                asset = AssetManager.war_types_library.get(pId);
            }
            catch { }

            bool add = asset == null;
            if (asset == null)
                asset = new WarTypeAsset { id = pId };

            asset.name_template = WarTypeAssetRules.ResolveWarNameTemplate(pId, pNameTemplate);
            asset.localized_type = pLocalizedType;
            asset.localized_war_name = WarTypeAssetRules.ResolveWarNameLocale(pLocalizedType);
            asset.path_icon = WarIconPathRules.ResolveWarIconPath(pId, pIcon);
            asset.kingdom_for_name_attacker = true;
            asset.forced_war = false;
            asset.total_war = pTotalWar;
            asset.alliance_join = pAllianceJoin;
            asset.rebellion = pRebellion;

            if (add)
            {
                AssetManager.war_types_library.add(asset);
            }
        }

        private static void AddWarNameTemplate(string pId, string pWarNames)
        {
            NameGeneratorAsset asset = null;
            try
            {
                asset = AssetManager.name_generator.get(pId);
            }
            catch { }

            bool add = asset == null;
            if (asset == null)
                asset = new NameGeneratorAsset { id = pId };

            asset.use_dictionary = true;
            asset.replacer_kingdom = NameGeneratorReplacers.replaceKingdom;
            asset.dict_parts = null;
            asset.templates = null;
            asset.addDictPart("kingdom_name", "$kingdom$");
            asset.addDictPart("war", pWarNames);
            asset.addTemplate("kingdom_name,war");

            if (add)
                AssetManager.name_generator.add(asset);
        }
    }
}
